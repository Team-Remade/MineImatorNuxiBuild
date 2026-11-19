using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private RigidBodyBakeJob? _rigidBodyBakeJob;
    private string? _rigidBodyBakeError;
    private bool _showRigidBodyBakePopup;
    public bool IsRigidBodyBaking => (MainViewport ?? this)._rigidBodyBakeJob != null;
    public bool IsPhysicsBaking => IsRigidBodyBaking || IsClothBaking || IsWiggleBaking;
    public int RigidBodyBakeCompletedFrames { get; private set; }
    public int RigidBodyBakeTotalFrames { get; private set; }
    public string? RigidBodyError { get; private set; }

    private sealed record RigidBodyFrameSample(int Frame, mat4[] Worlds, bool[] Animated, RigidBodySource[]? Sources,
        mat4[] FluidWorlds, IReadOnlyList<ClothTriangle>?[] FluidColliders);

    private sealed class RigidBodyBakeJob : IDisposable
    {
        public required SceneObject[] Objects { get; init; }
        public required SceneObject[] Bodies { get; init; }
        public required string[] ModifierIds { get; init; }
        public required RigidBodySettings[] Settings { get; init; }
        public required FluidSceneObject[] Fluids { get; init; }
        public required RigidBodyFluid[] FluidStates { get; init; }
        public required RigidBodyWorldSettings World { get; init; }
        public required HashSet<Mesh> BodyMeshes { get; init; }
        public required float Framerate { get; init; }
        public required int FirstFrame { get; init; }
        public required int StartFrame { get; init; }
        public required int EndFrame { get; init; }
        public required float[]?[][] BodyFrames { get; init; }
        public required float[][][] FluidFrames { get; init; }
        public Timeline? Timeline { get; init; }
        public int Frame { get; set; }
        public bool Cancelled { get; set; }
        public Task? Work { get; set; }
        private RigidBodySimulation? _simulation;

        public void Solve(RigidBodyFrameSample sample)
        {
            if (_simulation == null)
                _simulation = new RigidBodySimulation(sample.Sources ??
                    throw new InvalidOperationException("The first rigid body frame must capture collision sources."), World);
            else
            {
                var targets = new RigidBodyTarget[Bodies.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    // Frame `StartFrame` is the initial state; dynamics run on the frames after it.
                    bool dynamic = Settings[i].Type == RigidBodyType.Active && !sample.Animated[i] && sample.Frame > StartFrame;
                    targets[i] = new RigidBodyTarget(sample.Worlds[i], dynamic);
                }
                for (int f = 0; f < FluidStates.Length; f++)
                {
                    FluidStates[f].World = sample.FluidWorlds[f];
                    FluidStates[f].SceneColliders = sample.FluidColliders[f];
                }
                _simulation.Step(1f / Framerate, targets, FluidStates, (sample.Frame - 1) / Framerate);
            }
            if (sample.Frame >= StartFrame)
                for (int i = 0; i < Bodies.Length; i++)
                    if (BodyFrames[i] is { } frames)
                        frames[sample.Frame - StartFrame] = WiggleBake.Values(_simulation.GetWorld(i));
            for (int f = 0; f < FluidStates.Length; f++)
            {
                var particles = FluidStates[f].Simulation.Particles;
                var data = new float[particles.Count * 3];
                for (int p = 0; p < particles.Count; p++)
                {
                    vec3 position = particles[p].Position;
                    if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                        throw new InvalidDataException($"{Fluids[f].Name}: fluid simulation became nonfinite.");
                    data[p * 3] = position.x; data[p * 3 + 1] = position.y; data[p * 3 + 2] = position.z;
                }
                FluidFrames[f][sample.Frame] = data;
            }
        }

        public void Dispose() => _simulation?.Dispose();
    }

    public static string? RigidBodyUnsupportedReason(SceneObject obj) =>
        obj is CameraSceneObject or LightSceneObject or PathSceneObject or PathPointSceneObject or
            ParticleSpawnerSceneObject or ProceduralEffectSceneObject or FluidSceneObject
            ? Localization.T("text.rigid_body.unsupported_object") : null;

    /// <summary>Meshes forming a body's collision shape: its own and, optionally, children without their own body.</summary>
    private IEnumerable<(SceneObject Owner, Mesh Mesh)> RigidBodyMeshes(SceneObject body, RigidBodySettings settings,
        HashSet<SceneObject> bodies)
    {
        var pending = new Stack<SceneObject>();
        pending.Push(body);
        while (pending.TryPop(out var node))
        {
            if (node != body && bodies.Contains(node)) continue;
            if (IsObjectVisible(node) && RigidBodyUnsupportedReason(node) == null)
                foreach (var mesh in node.Visuals)
                    if (!mesh.PickOnly && !mesh.DepthTestDisabled && mesh.Vertices.Count > 0)
                        yield return (node, mesh);
            if (!settings.IncludeChildren) yield break;
            for (int i = node.Children.Count - 1; i >= 0; i--) pending.Push(node.Children[i]);
        }
    }

    private RigidBodySource CaptureRigidBodySource(SceneObject body, RigidBodySettings settings, mat4 world,
        HashSet<SceneObject> bodies, Dictionary<SceneObject, Dictionary<string, BoneSceneObject>> rootBones)
    {
        var points = new List<vec3>();
        var triangles = new List<int>();
        foreach (var (owner, mesh) in RigidBodyMeshes(body, settings, bodies))
        {
            mat4 source = owner.GetWorldMatrix();
            var vertices = ColliderVertices(owner, mesh, source, rootBones);
            int offset = points.Count;
            foreach (var vertex in vertices) points.Add((source * new vec4(vertex, 1)).xyz);
            int count = mesh.Indices is { Length: > 0 } ? mesh.Indices.Length - mesh.Indices.Length % 3 : vertices.Count - vertices.Count % 3;
            for (int i = 0; i < count; i++)
            {
                int index = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i]) : i;
                if ((uint)index >= vertices.Count) throw new InvalidDataException($"{owner.Name}: invalid collision mesh index.");
                triangles.Add(offset + index);
            }
            if (points.Count > RigidBodySimulation.MaxSourcePoints)
                throw new InvalidDataException($"{body.Name}: rigid body collision geometry exceeds {RigidBodySimulation.MaxSourcePoints} vertices.");
        }
        if (points.Count == 0) throw new InvalidDataException($"{body.Name}: {Localization.T("text.rigid_body.no_mesh")}");
        return new RigidBodySource(body.ObjectId, settings, world, points.ToArray(), triangles.ToArray());
    }

    public bool BakeRigidBodies()
    {
        var owner = MainViewport ?? this;
        if (owner != this) return owner.BakeRigidBodies();
        if (IsPhysicsBaking) throw new InvalidOperationException("Finish or cancel the current physics bake first.");
        _showRigidBodyBakePopup = true;
        RigidBodyBakeCompletedFrames = RigidBodyBakeTotalFrames = 0;
        var timeline = Timeline.Instance;
        if (timeline?.IsPlaying == true) timeline.TogglePlayPause();
        try
        {
            var world = RigidBodyEvaluation.World;
            world.Validate();
            var objects = EnumerateClothObjects(SceneObjects).ToArray();
            var bodies = objects.Where(o => RigidBodyEvaluation.ActiveModifier(o) != null).ToArray();
            if (bodies.Length == 0) throw new InvalidDataException(Localization.T("text.rigid_body.none"));
            if (bodies.Length > RigidBodySimulation.MaxBodies)
                throw new InvalidDataException($"Rigid body worlds are limited to {RigidBodySimulation.MaxBodies} bodies.");
            foreach (var body in bodies)
            {
                if (RigidBodyUnsupportedReason(body) is { } reason) throw new InvalidDataException($"{body.Name}: {reason}");
                if (body.Modifiers.Count(m => m.Type == SceneModifierType.RigidBody && m.Enabled) != 1)
                    throw new InvalidDataException($"{body.Name}: only one enabled rigid body modifier is supported per object.");
                if (string.IsNullOrEmpty(body.ObjectId)) body.AssignObjectId();
            }
            if (bodies.Select(o => o.ObjectId).Distinct(StringComparer.Ordinal).Count() != bodies.Length)
                throw new InvalidDataException("Rigid body objects require unique scene object IDs.");
            var modifiers = bodies.Select(o => RigidBodyEvaluation.ActiveModifier(o)!.Validated()).ToArray();
            var fluids = objects.OfType<FluidSceneObject>()
                .Where(f => f.Settings.RigidBodyInteraction && IsObjectVisible(f)).ToArray();
            float fps = timeline?.Framerate ?? 30f;
            int end = timeline?.MaxFrames ?? 0;
            int start = Math.Min(world.StartFrame, end);
            // Fluids always run from frame zero, so coupled scenes start sampling there.
            int first = fluids.Length > 0 ? 0 : start;
            long bodyBytes = (long)(end - start + 1) * 16 * sizeof(float);
            if (bodyBytes > RigidBodyBake.MaxBytes)
                throw new InvalidDataException("Rigid body bake exceeds 128 MiB per object. Shorten the timeline.");
            foreach (var fluid in fluids)
                if ((long)fluid.Settings.ParticleCount * 3 * sizeof(float) * (end + 1) > FluidBake.MaxBytes)
                    throw new InvalidDataException($"{fluid.Name}: fluid bake exceeds 256 MiB. Reduce particles or timeline length.");
            var bodySet = bodies.ToHashSet();
            var bodyMeshes = bodies.SelectMany((b, i) => RigidBodyMeshes(b, modifiers[i].RigidBody, bodySet))
                .Select(entry => entry.Mesh).ToHashSet();
            _rigidBodyBakeJob = new RigidBodyBakeJob
            {
                Objects = objects, Bodies = bodies, ModifierIds = modifiers.Select(m => m.Id).ToArray(),
                Settings = modifiers.Select(m => m.RigidBody).ToArray(), Fluids = fluids,
                FluidStates = fluids.Select(f => new RigidBodyFluid
                    { ObjectId = f.ObjectId, Simulation = new FluidSimulation(f.Settings), Settings = f.Settings }).ToArray(),
                World = world, BodyMeshes = bodyMeshes, Framerate = fps, FirstFrame = first, StartFrame = start, EndFrame = end,
                BodyFrames = modifiers.Select(m => m.RigidBody.Type == RigidBodyType.Active ? new float[end - start + 1][] : null).ToArray(),
                FluidFrames = fluids.Select(_ => new float[end + 1][]).ToArray(),
                Timeline = timeline, Frame = first
            };
            RigidBodyBakeTotalFrames = end - first + 1;
            _rigidBodyBakeError = null;
            RigidBodyError = null;
            return true;
        }
        catch (InvalidDataException ex)
        {
            _rigidBodyBakeError = ex.Message;
            ReportRigidBodyError(ex);
            return false;
        }
    }

    public void AdvanceRigidBodyBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.AdvanceRigidBodyBake(); return; }
        if (_rigidBodyBakeJob is not { } job || job.Work is { IsCompleted: false }) return;
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
                job.Frame++;
                RigidBodyBakeCompletedFrames = job.Frame - job.FirstFrame;
                job.Work = null;
            }
            if (job.Cancelled)
            {
                _rigidBodyBakeError = Localization.T("text.rigid_body.bake_cancelled");
                RigidBodyError = _rigidBodyBakeError;
                FinishRigidBodyBake();
                return;
            }
            if (job.Frame > job.EndFrame)
            {
                PublishRigidBodyBake(job);
                return;
            }
            RigidBodyFrameSample? sample = null;
            void Sample() => sample = SampleRigidBodyFrame(job);
            // Suspend saved bakes so animated and initial poses come from authored animation.
            using (RigidBodyEvaluation.Suspend())
            {
                if (job.Timeline != null) job.Timeline.SamplePhysicsFrame(job.Frame, Sample, includeWiggle: true);
                else Sample();
            }
            var sampled = sample ?? throw new InvalidOperationException("Rigid body frame sampling did not produce a pose.");
            job.Work = Task.Run(() => job.Solve(sampled));
        }
        catch (InvalidDataException ex)
        {
            var error = new InvalidDataException($"Rigid body bake frame {job.Frame}: {ex.Message}", ex);
            _rigidBodyBakeError = error.Message;
            ReportRigidBodyError(error);
            FinishRigidBodyBake();
        }
    }

    private RigidBodyFrameSample SampleRigidBodyFrame(RigidBodyBakeJob job)
    {
        var worlds = new mat4[job.Bodies.Length];
        var animated = new bool[job.Bodies.Length];
        for (int i = 0; i < job.Bodies.Length; i++)
        {
            var modifier = job.Bodies[i].Modifiers.FirstOrDefault(m => m.Id == job.ModifierIds[i]) ??
                throw new InvalidDataException("Scene changed during rigid body baking. Rebake the scene.");
            worlds[i] = job.Bodies[i].GetWorldMatrixNoPivot();
            animated[i] = modifier.RigidBody.Animated;
        }
        RigidBodySource[]? sources = null;
        if (job.Frame == job.FirstFrame)
        {
            var bodySet = job.Bodies.ToHashSet();
            var rootBones = new Dictionary<SceneObject, Dictionary<string, BoneSceneObject>>();
            using var evaluation = SceneObject.BeginRenderEvaluation();
            sources = job.Bodies.Select((body, i) => CaptureRigidBodySource(body,
                job.Settings[i] with { Animated = animated[i] }, worlds[i], bodySet, rootBones)).ToArray();
        }
        var fluidWorlds = job.Fluids.Select(f => f.GetWorldMatrix()).ToArray();
        var fluidColliders = new IReadOnlyList<ClothTriangle>?[job.Fluids.Length];
        if (job.Fluids.Any(f => f.Settings.MeshCollisions))
        {
            // Simulated bodies collide with water through their Jolt shapes, not their authored-pose meshes.
            var sceneTriangles = CollectClothColliders(job.Objects, job.BodyMeshes).ToArray();
            for (int f = 0; f < job.Fluids.Length; f++)
            {
                if (!job.Fluids[f].Settings.MeshCollisions || sceneTriangles.Length == 0) continue;
                mat4 inverse = fluidWorlds[f].Inverse;
                fluidColliders[f] = sceneTriangles.Select(t => new ClothTriangle((inverse * new vec4(t.A, 1)).xyz,
                    (inverse * new vec4(t.B, 1)).xyz, (inverse * new vec4(t.C, 1)).xyz)).ToArray();
            }
        }
        return new RigidBodyFrameSample(job.Frame, worlds, animated, sources, fluidWorlds, fluidColliders);
    }

    private void PublishRigidBodyBake(RigidBodyBakeJob job)
    {
        if (job.Timeline != Timeline.Instance || (job.Timeline?.Framerate ?? 30f) != job.Framerate ||
            (job.Timeline?.MaxFrames ?? 0) != job.EndFrame)
            throw new InvalidDataException("Timeline changed during rigid body baking. Rebake the scene.");
        var scene = EnumerateClothObjects(SceneObjects).ToHashSet();
        var indices = job.Bodies.Select((body, i) => body.Modifiers.FindIndex(m => m.Id == job.ModifierIds[i])).ToArray();
        if (indices.Any(i => i < 0) || job.Bodies.Any(b => !scene.Contains(b)) || job.Fluids.Any(f => !scene.Contains(f)))
            throw new InvalidDataException("Scene changed during rigid body baking. Rebake the scene.");
        for (int i = 0; i < job.Bodies.Length; i++)
        {
            var modifier = job.Bodies[i].Modifiers[indices[i]];
            RigidBodyBake? bake = job.BodyFrames[i] is { } frames ? new RigidBodyBake
            {
                Framerate = job.Framerate, StartFrame = job.StartFrame, Settings = job.Settings[i],
                World = job.World, Frames = frames
            } : null;
            bake?.Validate();
            job.Bodies[i].Modifiers[indices[i]] = modifier with { RigidBodyBake = bake };
        }
        for (int f = 0; f < job.Fluids.Length; f++)
            job.Fluids[f].Bake = new FluidBake
                { Framerate = job.Framerate, Settings = job.FluidStates[f].Settings, Frames = job.FluidFrames[f] };
        FinishRigidBodyBake();
        ProjectManager.Instance.SetDirty(true);
    }

    public void CancelRigidBodyBake()
    {
        var owner = MainViewport ?? this;
        if (owner._rigidBodyBakeJob is { } job) job.Cancelled = true;
    }

    private void FinishRigidBodyBake()
    {
        if (_rigidBodyBakeJob is { } job)
        {
            // A cancelled or failed frame may still be running; release native state once it finishes.
            if (job.Work is { IsCompleted: false } work) work.ContinueWith(_ => job.Dispose(), TaskScheduler.Default);
            else job.Dispose();
        }
        _rigidBodyBakeJob = null;
    }

    public void ClearRigidBodyBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.ClearRigidBodyBake(); return; }
        if (IsRigidBodyBaking) throw new InvalidOperationException("Cannot clear rigid bodies while a bake is running.");
        foreach (var obj in EnumerateClothObjects(SceneObjects))
        {
            for (int i = 0; i < obj.Modifiers.Count; i++)
                if (obj.Modifiers[i].RigidBodyBake != null)
                    obj.Modifiers[i] = obj.Modifiers[i] with { RigidBodyBake = null };
            obj.SetEvaluatedRigidBodyWorld(null);
            if (obj is FluidSceneObject { Bake: not null } fluid) fluid.Bake = null;
        }
        _rigidBodyBakeError = null;
        RigidBodyError = null;
        ProjectManager.Instance.SetDirty(true);
    }

    public void DrawRigidBodyBakePopup()
    {
        if (!_showRigidBodyBakePopup) return;
        string title = Localization.T("text.rigid_body.bake") + "##RigidBodyBakeProgress";
        ImGui.OpenPopup(title);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f));
        if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.TextUnformatted(Localization.T(IsRigidBodyBaking ? "text.rigid_body.baking" :
            _rigidBodyBakeError == null ? "text.rigid_body.bake_complete" : "text.rigid_body.bake_failed"));
        float progress = RigidBodyBakeTotalFrames == 0 ? 0 : (float)RigidBodyBakeCompletedFrames / RigidBodyBakeTotalFrames;
        ImGui.ProgressBar(progress, new Vector2(360f * UiTheme.Scale, 0), $"{progress * 100:0.0}%");
        ImGui.TextUnformatted(Localization.Format("text.cloth.bake_progress", RigidBodyBakeCompletedFrames, RigidBodyBakeTotalFrames));
        if (_rigidBodyBakeError != null) UiText.Wrapped(_rigidBodyBakeError);
        if (IsRigidBodyBaking)
        {
            if (ImGui.Button(Localization.T("text.wiggle.cancel"), new Vector2(120f * UiTheme.Scale, 0))) CancelRigidBodyBake();
        }
        else if (ImGui.Button(Localization.T("text.window.main.close"), new Vector2(120f * UiTheme.Scale, 0)))
        {
            _showRigidBodyBakePopup = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void ReportRigidBodyError(InvalidDataException ex)
    {
        if (RigidBodyError != ex.Message) Console.Error.WriteLine($"Rigid body: {ex.Message}");
        RigidBodyError = ex.Message;
    }
}
