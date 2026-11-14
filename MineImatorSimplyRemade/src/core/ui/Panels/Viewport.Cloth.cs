using System.Runtime.CompilerServices;
using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private readonly Dictionary<Mesh, (ClothBake Bake, int Frame)> _clothUploadedFrames = [];
    private string? _clothBakeError;
    private IReadOnlyList<ClothTriangle> _clothColliders = [];
    private readonly List<ClothTriangle> _clothColliderBufferA = [], _clothColliderBufferB = [];
    private readonly Dictionary<Mesh, (int Signature, vec3[] Vertices)> _clothColliderVertices = [];
    private readonly HashSet<Mesh> _clothMeshes = [];
    private ClothBakeJob? _clothBakeJob;
    private bool _showClothBakePopup;
    public bool IsClothBaking => (MainViewport ?? this)._clothBakeJob != null;
    public int ClothBakeCompletedFrames { get; private set; }
    public int ClothBakeTotalFrames { get; private set; }
    public string? ClothError { get; private set; }

    private sealed class ClothBakeJob
    {
        public required SceneObject[] Objects { get; init; }
        public required SceneObject[] ClothObjects { get; init; }
        public required SceneModifier[] Modifiers { get; init; }
        public required ClothSurface[] Surfaces { get; init; }
        public required ClothBake[] Bakes { get; init; }
        public required HashSet<Mesh> ActiveMeshes { get; init; }
        public Timeline? Timeline { get; init; }
        public bool Collisions { get; init; }
        public int Frame { get; set; }
        public Task? Work { get; set; }
        private ClothSimulation? _simulation;

        public void SolveFrame(Dictionary<string, mat4> pose, IReadOnlyList<ClothTriangle> colliders,
            IReadOnlyList<ClothTriangle> previousColliders)
        {
            if (Frame == 0) _simulation = new ClothSimulation(Surfaces, pose);
            else _simulation!.Step(1 / Bakes[0].Framerate, pose, colliders, previousColliders);
            for (int s = 0; s < Surfaces.Length; s++)
            {
                mat4 world = pose[Surfaces[s].ObjectId];
                if (!float.IsFinite(world.Determinant) || MathF.Abs(world.Determinant) < 1e-10f)
                    throw new InvalidDataException($"Cloth object {Surfaces[s].ObjectId}: cloth requires a nonsingular object transform.");
                mat4 inverse = world.Inverse;
                var positions = _simulation!.GetWorldVertices(s);
                var local = new float[positions.Length * 3];
                for (int v = 0; v < positions.Length; v++)
                {
                    vec3 p = (inverse * new vec4(positions[v], 1)).xyz;
                    if (!ClothSimulation.Finite(p)) throw new InvalidDataException("Cloth bake contains nonfinite geometry.");
                    local[v * 3] = p.x; local[v * 3 + 1] = p.y; local[v * 3 + 2] = p.z;
                }
                Bakes[s].Frames[Frame] = local;
            }
        }
    }

    public static string? ClothUnsupportedReason(SceneObject obj)
    {
        if (obj is CameraSceneObject or LightSceneObject or PathSceneObject or PathPointSceneObject or
            ParticleSpawnerSceneObject or ProceduralEffectSceneObject)
            return Localization.T("text.cloth.unsupported_object");
        if (obj.Visuals.Count == 0 || obj.Visuals.All(m => m.PickOnly || m.Vertices.Count == 0))
            return Localization.T("text.cloth.no_mesh");
        if (obj.Visuals.Any(m => m.IsSkinned || m.HasShapeKeys || m.WindActive))
            return Localization.T("text.cloth.unsupported_deformation");
        if (obj.ItemBounce || obj.ItemSpin || obj.ItemFaceCamera || obj.PrimitivePlaneFaceCamera || obj.TextMeshFaceCamera)
            return Localization.T("text.cloth.unsupported_billboard");
        return null;
    }

    private void UpdateCloth()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.UpdateCloth(); return; }
        using var profile = FrameProfiler.Shared.Measure(ProfileStage.ClothUpdate);
        try
        {
            int frame = Timeline.Instance?.CurrentFrame ?? 0;
            float fps = Timeline.Instance?.Framerate ?? 30;
            var active = new HashSet<Mesh>();
            foreach (var obj in EnumerateClothObjects(SceneObjects))
            {
                if (obj is MiBoneSceneObject bone) bone.RestoreDeferredClothBend();
                var modifier = obj.Modifiers.FirstOrDefault(m => m.Type == SceneModifierType.Cloth && m.Enabled);
                if (modifier?.ClothBake is not { } bake) continue;
                if (bake.Settings != modifier.Cloth || bake.Framerate != fps ||
                    bake.MeshVertexCounts.Length != obj.Visuals.Count ||
                    obj.Visuals.Where((m, i) => m.Vertices.Count != bake.MeshVertexCounts[i]).Any())
                    throw new InvalidDataException(Localization.T("text.cloth.rebake_required"));
                if (frame < 0 || frame >= bake.Frames.Length)
                    throw new InvalidDataException(Localization.T("text.cloth.frame_not_baked"));
                if (ClothUnsupportedReason(obj) is { } reason)
                    throw new InvalidDataException($"{obj.Name}: {reason}");
                for (int i = 0; i < obj.Visuals.Count; i++)
                {
                    var mesh = obj.Visuals[i];
                    if (!active.Add(mesh))
                        throw new InvalidDataException("Cloth objects require independent mesh instances; duplicate the geometry first.");
                    if (!_clothUploadedFrames.TryGetValue(mesh, out var uploaded) ||
                        !ReferenceEquals(uploaded.Bake, bake) || uploaded.Frame != frame || !mesh.HasClothGeometry)
                    {
                        mesh.SetClothGeometry(bake.GetMeshVertices(frame, i));
                        _clothUploadedFrames[mesh] = (bake, frame);
                    }
                    _clothMeshes.Add(mesh);
                }
            }
            foreach (var mesh in _clothMeshes.Where(m => !active.Contains(m)).ToArray())
            {
                mesh.SetClothGeometry(null);
                _clothMeshes.Remove(mesh);
                _clothUploadedFrames.Remove(mesh);
            }
            ClothError = _clothBakeError;
        }
        catch (InvalidDataException ex)
        {
            ReportClothError(ex);
            ResetClothGeometry();
            if (MineImatorSimplyRemade.core.window.windows.MainWindow.IsAnimationRenderExportActive) throw;
        }
    }

    public bool BakeCloth()
    {
        var owner = MainViewport ?? this;
        if (owner != this) return owner.BakeCloth();
        if (IsClothBaking) throw new InvalidOperationException("A cloth bake is already running.");
        _showClothBakePopup = true;
        ClothBakeCompletedFrames = ClothBakeTotalFrames = 0;
        var timeline = Timeline.Instance;
        if (timeline?.IsPlaying == true) timeline.TogglePlayPause();
        try
        {
            var objects = EnumerateClothObjects(SceneObjects).ToArray();
            var clothObjects = objects.Where(o => o.Modifiers.Any(m => m.Type == SceneModifierType.Cloth && m.Enabled)).ToArray();
            if (clothObjects.Length == 0) throw new InvalidDataException("Enable a cloth modifier before baking.");
            ResetClothGeometry();
            foreach (var bone in clothObjects.OfType<MiBoneSceneObject>()) bone.RestoreDeferredClothBend(force: true);
            int end = timeline?.MaxFrames ?? 0;
            float fps = timeline?.Framerate ?? 30;
            long vertices = clothObjects.SelectMany(o => o.Visuals).Sum(m => (long)m.Vertices.Count);
            if (vertices > ClothSimulation.MaxVertices)
                throw new InvalidDataException($"Cloth is limited to {ClothSimulation.MaxVertices} source vertices per scene.");
            if (end < 0 || vertices * 3 * sizeof(float) * ((long)end + 1) > ClothBake.MaxBytes)
                throw new InvalidDataException("Cloth bake exceeds 256 MiB. Reduce the timeline length or cloth vertex count.");
            var activeMeshes = clothObjects.SelectMany(o => o.Visuals).ToHashSet();
            if (activeMeshes.Count != clothObjects.Sum(o => o.Visuals.Count))
                throw new InvalidDataException("Cloth objects require independent mesh instances; duplicate the geometry first.");
            foreach (var obj in clothObjects)
            {
                if (ClothUnsupportedReason(obj) is { } reason) throw new InvalidDataException($"{obj.Name}: {reason}");
                if (obj.Modifiers.Count(m => m.Type == SceneModifierType.Cloth && m.Enabled) != 1)
                    throw new InvalidDataException($"{obj.Name}: only one enabled cloth modifier is supported per object.");
            }
            if (clothObjects.Any(o => string.IsNullOrEmpty(o.ObjectId)) ||
                clothObjects.Select(o => o.ObjectId).Distinct(StringComparer.Ordinal).Count() != clothObjects.Length)
                throw new InvalidDataException("Cloth objects require unique scene object IDs.");
            // Capture the current bend before temporarily sampling frame zero.
            var surfaces = clothObjects.Select(ClothSurfaceFor).ToArray();
            var bakes = clothObjects.Select((o, i) => new ClothBake
            {
                Framerate = fps, Settings = surfaces[i].Settings,
                MeshVertexCounts = o.Visuals.Select(m => m.Vertices.Count).ToArray(),
                Frames = new float[end + 1][]
            }).ToArray();
            _clothColliderVertices.Clear();
            _clothColliders = [];
            _clothBakeJob = new ClothBakeJob
            {
                Objects = objects, ClothObjects = clothObjects, Surfaces = surfaces, Bakes = bakes,
                Modifiers = clothObjects.Select(o => o.Modifiers.Single(m => m.Type == SceneModifierType.Cloth && m.Enabled)).ToArray(),
                ActiveMeshes = activeMeshes, Timeline = timeline, Collisions = surfaces.Any(s => s.Settings.Collisions)
            };
            foreach (var bone in clothObjects.OfType<MiBoneSceneObject>()) bone.IsClothBakeSampling = true;
            ClothBakeTotalFrames = end + 1;
            _clothBakeError = null;
            ClothError = null;
            return true;
        }
        catch (InvalidDataException ex)
        {
            _clothBakeError = ex.Message;
            ReportClothError(ex);
            return false;
        }
    }

    public void AdvanceClothBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.AdvanceClothBake(); return; }
        if (_clothBakeJob is not { } job || job.Work is { IsCompleted: false }) return;
        try
        {
            if (job.Work is { } work)
            {
                if (work.IsFaulted)
                {
                    var failure = work.Exception!.GetBaseException();
                    Console.Error.WriteLine(failure);
                    throw new InvalidDataException(failure.Message, failure);
                }
                ClothBakeCompletedFrames = ++job.Frame;
                job.Work = null;
            }
            if (job.Frame == ClothBakeTotalFrames)
            {
                if (job.Timeline != Timeline.Instance || (job.Timeline?.Framerate ?? 30) != job.Bakes[0].Framerate ||
                    (job.Timeline?.MaxFrames ?? 0) + 1 != ClothBakeTotalFrames)
                    throw new InvalidDataException("Timeline changed during cloth baking. Rebake the scene.");
                var indices = job.ClothObjects.Select((obj, s) => obj.Modifiers.FindIndex(m => m == job.Modifiers[s])).ToArray();
                if (indices.Any(i => i < 0) || job.ClothObjects.Any(obj => !EnumerateClothObjects(SceneObjects).Contains(obj)))
                    throw new InvalidDataException("Scene changed during cloth baking. Rebake the scene.");
                for (int s = 0; s < job.ClothObjects.Length; s++)
                    job.ClothObjects[s].Modifiers[indices[s]] = job.Modifiers[s] with { ClothBake = job.Bakes[s] };
                FinishClothBake();
                ProjectManager.Instance.SetDirty(true);
                UpdateCloth();
                return;
            }

            Dictionary<string, mat4>? pose = null;
            IReadOnlyList<ClothTriangle> colliders = [];
            void Sample()
            {
                pose = job.ClothObjects.ToDictionary(o => o.ObjectId, o => o.GetWorldMatrix(), StringComparer.Ordinal);
                colliders = job.Collisions ? CollectClothColliders(job.Objects, job.ActiveMeshes) : [];
            }
            if (job.Timeline != null) job.Timeline.SampleClothFrame(job.Frame, Sample);
            else Sample();
            var sampledPose = pose ?? throw new InvalidOperationException("Cloth frame sampling did not produce a pose.");
            var previous = _clothColliders;
            // Start only after sampling restores the scene. Collider buffers are not
            // reused until the worker finishes, so both sampled frames stay stable.
            job.Work = Task.Run(() => job.SolveFrame(sampledPose, colliders, previous));
            _clothColliders = colliders;
        }
        catch (InvalidDataException ex)
        {
            var error = new InvalidDataException($"Cloth bake frame {job.Frame}: {ex.Message}", ex);
            _clothBakeError = error.Message;
            ReportClothError(error);
            FinishClothBake();
        }
    }

    private void FinishClothBake()
    {
        if (_clothBakeJob is { } job)
            foreach (var bone in job.ClothObjects.OfType<MiBoneSceneObject>())
            {
                bone.IsClothBakeSampling = false;
                bone.RestoreDeferredClothBend();
            }
        _clothBakeJob = null;
        _clothColliders = [];
        _clothColliderVertices.Clear();
        _clothColliderBufferA.Clear();
        _clothColliderBufferB.Clear();
    }

    public void DrawClothBakePopup()
    {
        if (!_showClothBakePopup) return;
        string title = Localization.T("text.cloth.bake") + "##ClothBakeProgress";
        ImGui.OpenPopup(title);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f));
        if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.TextUnformatted(Localization.T(IsClothBaking ? "text.cloth.baking" :
            _clothBakeError == null ? "text.cloth.bake_complete" : "text.cloth.bake_failed"));
        float progress = ClothBakeTotalFrames == 0 ? 0 : (float)ClothBakeCompletedFrames / ClothBakeTotalFrames;
        ImGui.ProgressBar(progress, new Vector2(360f * UiTheme.Scale, 0), $"{progress * 100:0.0}%");
        ImGui.TextUnformatted(Localization.Format("text.cloth.bake_progress", ClothBakeCompletedFrames, ClothBakeTotalFrames));
        if (_clothBakeError != null) UiText.Wrapped(_clothBakeError);
        if (!IsClothBaking && ImGui.Button(Localization.T("text.window.main.close"), new Vector2(120f * UiTheme.Scale, 0)))
        {
            _showClothBakePopup = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    public void ClearClothBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.ClearClothBake(); return; }
        if (IsClothBaking) throw new InvalidOperationException("Cannot clear cloth while a bake is running.");
        foreach (var obj in EnumerateClothObjects(SceneObjects))
            for (int i = 0; i < obj.Modifiers.Count; i++)
                if (obj.Modifiers[i].ClothBake != null)
                    obj.Modifiers[i] = obj.Modifiers[i] with { ClothBake = null };
        ResetClothGeometry();
        foreach (var bone in EnumerateClothObjects(SceneObjects).OfType<MiBoneSceneObject>())
            bone.RestoreDeferredClothBend();
        _clothBakeError = null;
        ClothError = null;
        ProjectManager.Instance.SetDirty(true);
    }

    private void ResetClothGeometry()
    {
        foreach (var mesh in _clothMeshes) mesh.SetClothGeometry(null);
        _clothMeshes.Clear();
        _clothUploadedFrames.Clear();
    }

    private void ReportClothError(InvalidDataException ex)
    {
        if (ClothError != ex.Message) Console.Error.WriteLine($"Cloth: {ex.Message}");
        ClothError = ex.Message;
    }

    private static int ComputeClothMeshSignature(Mesh mesh)
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(mesh));
        hash.Add(mesh.Vertices.Count);
        foreach (vec3 p in mesh.Vertices) hash.Add(p);
        hash.Add(mesh.Indices?.Length ?? -1);
        if (mesh.Indices != null) foreach (uint index in mesh.Indices) hash.Add(index);
        hash.Add(mesh.PickOnly);
        hash.Add(mesh.DepthTestDisabled);
        hash.Add(mesh.GpuBend != null);
        if (mesh.GpuBend is { } bend)
        {
            // Hash deformation inputs, not a freshly allocated/deformed copy of every mesh.
            hash.Add(bend.Angle); hash.Add(bend.Pivot); hash.Add(bend.ShapePosition);
            hash.Add(bend.InverseShapeTransform); hash.Add(bend.SegmentAxis); hash.Add(bend.ScaleCorrectionAxis);
            hash.Add(bend.AxisStart); hash.Add(bend.BendStart); hash.Add(bend.BendEnd); hash.Add(bend.BendSize);
            hash.Add(bend.InvertWeight); hash.Add(bend.Sharp); hash.Add(bend.Plane); hash.Add(bend.ExtrudedPlane);
            hash.Add(mesh.BendSourceSegmentPositions.Count);
            foreach (float segment in mesh.BendSourceSegmentPositions) hash.Add(segment);
        }
        return hash.ToHashCode();
    }

    private static ClothSurface ClothSurfaceFor(SceneObject obj)
    {
        var vertices = new List<vec3>();
        var ids = new List<ClothVertex>();
        var triangles = new List<int>();
        for (int m = 0; m < obj.Visuals.Count; m++)
        {
            var mesh = obj.Visuals[m];
            int offset = vertices.Count;
            if (mesh.GpuBend != null)
                vertices.AddRange(VeldridSceneRenderer.BuildClothRestVertices(mesh).Select(v => v.Position));
            else vertices.AddRange(mesh.Vertices);
            for (int v = 0; v < mesh.Vertices.Count; v++) ids.Add(new(m, v));
            int count = mesh.Indices is { Length: > 0 } ? mesh.Indices.Length : mesh.Vertices.Count;
            if (count % 3 != 0) throw new InvalidDataException($"{obj.Name}: cloth requires triangle geometry.");
            for (int i = 0; i < count; i++)
            {
                int index = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i]) : i;
                if ((uint)index >= mesh.Vertices.Count) throw new InvalidDataException($"{obj.Name}: invalid mesh index.");
                triangles.Add(offset + index);
            }
        }
        return new(obj.ObjectId, obj.Modifiers.Single(m => m.Type == SceneModifierType.Cloth && m.Enabled).Cloth,
            vertices.ToArray(), triangles.ToArray(), ids.ToArray());
    }

    private List<ClothTriangle> CollectClothColliders(SceneObject[] objects, HashSet<Mesh> clothMeshes)
    {
        using var profile = FrameProfiler.Shared.Measure(ProfileStage.ClothColliderCollection);
        using var evaluation = SceneObject.BeginRenderEvaluation();
        const int maxTriangles = 200000;
        var triangles = ReferenceEquals(_clothColliders, _clothColliderBufferA) ? _clothColliderBufferB : _clothColliderBufferA;
        triangles.Clear();
        var modifiers = new ModifierEvaluation(SceneObjects);
        var rootBones = new Dictionary<SceneObject, Dictionary<string, BoneSceneObject>>();
        foreach (var obj in objects)
        {
            if (!IsObjectVisible(obj) || obj is CameraSceneObject or LightSceneObject or PathSceneObject or
                PathPointSceneObject or ParticleSpawnerSceneObject or ProceduralEffectSceneObject) continue;
            mat4 source = obj.GetWorldMatrix();
            foreach (var mesh in obj.Visuals)
            {
                if (clothMeshes.Contains(mesh) || mesh.PickOnly || mesh.DepthTestDisabled) continue;
                if (mesh.IsSkinned)
                {
                    var root = obj;
                    while (root.Parent != null) root = root.Parent;
                    if (!rootBones.TryGetValue(root, out var bones))
                        rootBones.Add(root, bones = BuildBoneDictionary(root));
                    UpdateBoneMatrices(mesh, source, bones);
                }
                IReadOnlyList<vec3> vertices;
                if (mesh.IsSkinned || mesh.HasShapeKeys)
                    vertices = VeldridSceneRenderer.BuildVertices(mesh).Select(v => v.Position).ToArray();
                else if (mesh.GpuBend == null)
                    vertices = mesh.Vertices;
                else
                {
                    int signature = ComputeClothMeshSignature(mesh);
                    if (!_clothColliderVertices.TryGetValue(mesh, out var cached) || cached.Signature != signature)
                    {
                        cached = (signature, VeldridSceneRenderer.BuildClothRestVertices(mesh).Select(v => v.Position).ToArray());
                        _clothColliderVertices[mesh] = cached;
                    }
                    vertices = cached.Vertices;
                }
                int count = mesh.Indices is { Length: > 0 } ? mesh.Indices.Length : vertices.Count;
                foreach (var delta in modifiers.GetDeltas(obj))
                {
                    mat4 model = delta * source;
                    for (int i = 0; i + 2 < count; i += 3)
                    {
                        int a = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i]) : i;
                        int b = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 1]) : i + 1;
                        int c = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 2]) : i + 2;
                        if ((uint)a >= vertices.Count || (uint)b >= vertices.Count || (uint)c >= vertices.Count)
                            throw new InvalidDataException($"{obj.Name}: invalid collision mesh index.");
                        var triangle = new ClothTriangle((model * new vec4(vertices[a], 1)).xyz,
                            (model * new vec4(vertices[b], 1)).xyz, (model * new vec4(vertices[c], 1)).xyz);
                        if (!ClothSimulation.Finite(triangle.A) || !ClothSimulation.Finite(triangle.B) || !ClothSimulation.Finite(triangle.C))
                            throw new InvalidDataException($"{obj.Name}: nonfinite collision geometry.");
                        triangles.Add(triangle);
                        if (triangles.Count > maxTriangles)
                            throw new InvalidDataException($"Cloth collision geometry exceeds {maxTriangles} triangles. Hide unused collision objects.");
                    }
                }
            }
        }
        return triangles;
    }

    internal static IEnumerable<SceneObject> EnumerateClothObjects(IEnumerable<SceneObject> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in EnumerateClothObjects(root.Children)) yield return child;
        }
    }
}
