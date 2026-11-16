using MineImatorSimplyRemade.core.mdl;
using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.window.windows;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private WiggleSimulation? _wiggleSimulation;
    private SceneObject[] _wiggleObjects = [];
    private ulong _wiggleConfiguration;
    private int _wiggleFrame = -1;
    private float _wiggleFps;
    private bool _wiggleWasPlaying;
    private int _wigglePoseSignature;
    private readonly Dictionary<BoneSceneObject, GlmSharp.mat4> _wiggleLastWorlds = [];
    private readonly Dictionary<MiBoneSceneObject, GlmSharp.vec3> _wiggleLastBends = [];
    private WiggleBakeJob? _wiggleJob;
    private bool _showWiggleBakePopup;
    private bool _wiggleBakeCancelled;
    private string? _wiggleBakeError;
    public string? WiggleError { get; private set; }
    public bool IsWiggleBaking => (MainViewport ?? this)._wiggleJob != null;
    public int WiggleBakeCompletedFrames { get; private set; }
    public int WiggleBakeTotalFrames { get; private set; }

    private sealed class WiggleBakeJob
    {
        public required SceneObject[] Objects { get; init; }
        public required BoneSceneObject[] Bones { get; init; }
        public required WiggleBake[] Bakes { get; init; }
        public required WiggleSimulation Simulation { get; init; }
        public required WiggleSceneSettings Scene { get; init; }
        public required ulong Configuration { get; init; }
        public int Frame { get; set; }
    }

    private WindSettings WiggleWind(float time)
    {
        var p = PropertiesPanel ?? MainViewport?.PropertiesPanel;
        return p == null ? new(false, 0, 0, 0, 0, 0, time) :
            new(p.WindEnabled, p.WindSpeed, p.WindStrength, p.WindDirection,
                p.WindDirectionalSpeed, p.WindDirectionalStrength, time);
    }

    public void ResetWiggle()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.ResetWiggle(); return; }
        _wiggleSimulation = null;
        _wiggleFrame = -1;
        _wiggleWasPlaying = false;
        _wiggleLastWorlds.Clear();
        _wiggleLastBends.Clear();
        foreach (var obj in WiggleSimulation.Objects(SceneObjects).OfType<BoneSceneObject>())
            obj.SetEvaluatedWiggleWorld(null);
        foreach (var mi in WiggleSimulation.Objects(SceneObjects).OfType<MiBoneSceneObject>())
            mi.RefreshInverseKinematicsVisuals();
        WiggleError = null;
        _wiggleBakeError = null;
    }

    internal void UpdateWiggle(bool requireBake = false)
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.UpdateWiggle(requireBake); return; }
        requireBake |= MainWindow.IsAnimationRenderExportActive;
        try
        {
            var scene = ProjectManager.Instance.Manifest.Settings.Wiggle;
            if (scene == null) throw new InvalidDataException("Wiggle scene settings are missing.");
            if (!scene.Enabled) { _wiggleSimulation = null; _wiggleFrame = -1; return; }
            var objects = WiggleSimulation.Objects(SceneObjects).ToArray();
            var active = objects.OfType<BoneSceneObject>().Where(WiggleSimulation.Active).ToArray();
            if (active.Length == 0) { _wiggleSimulation = null; _wiggleFrame = -1; return; }
            scene.Validate();
            int frame = Timeline.Instance?.CurrentFrame ?? 0;
            float fps = Timeline.Instance?.Framerate ?? 30;
            ulong configuration = WiggleConfiguration.Compute(objects, scene);
            bool allBaked = active.All(b => b.WiggleBake != null);
            if (allBaked)
            {
                foreach (var bone in active)
                {
                    var bake = bone.WiggleBake!;
                    if (bake.Framerate != fps || bake.Configuration != configuration)
                        throw new InvalidDataException(Localization.T("text.wiggle.rebake_required"));
                    if (frame < 0 || frame >= bake.Frames.Length)
                        throw new InvalidDataException(Localization.T("text.wiggle.frame_not_baked"));
                    bool frozen = bone.Wiggle.Freeze || WiggleSimulation.RigFor(bone).WiggleRig.Freeze;
                    var world = frozen && _wiggleLastWorlds.TryGetValue(bone, out var held)
                        ? held : WiggleBake.Matrix(bake.Frames[frame]);
                    _wiggleLastWorlds[bone] = world;
                    bone.SetEvaluatedWiggleWorld(world);
                    if (bone is MiBoneSceneObject mi && bone.Wiggle.Bend.Enabled)
                    {
                        if (bake.BendFrames.Length != bake.Frames.Length)
                            throw new InvalidDataException("Wiggle bake is missing bend frames. Rebake all active bones.");
                        var values = bake.BendFrames[frame];
                        GlmSharp.vec3 bend = frozen && _wiggleLastBends.TryGetValue(mi, out var heldBend)
                            ? heldBend : new(values[0], values[1], values[2]);
                        mi.SetEvaluatedWiggleBend(bend);
                        _wiggleLastBends[mi] = bend;
                    }
                }
                foreach (var mi in objects.OfType<MiBoneSceneObject>()) mi.RefreshInverseKinematicsVisuals();
                WiggleError = _wiggleBakeError;
                return;
            }
            if (active.Any(b => b.WiggleBake != null))
                throw new InvalidDataException(Localization.T("text.wiggle.rebake_required"));
            if (requireBake)
                throw new InvalidDataException(Localization.T("text.wiggle.export_requires_bake"));
            bool playing = Timeline.Instance?.IsPlaying ?? false;
            var poseHash = new HashCode();
            foreach (var bone in active)
            {
                poseHash.Add(bone.GetWorldMatrixNoPivot());
                if (bone is MiBoneSceneObject mi) poseHash.Add(mi.GetEvaluatedEditableBendAngle());
            }
            foreach (var collider in objects.Where(o => o.WiggleCollider.Enabled)) poseHash.Add(collider.GetWorldMatrixNoPivot());
            if (active.Any(b => b.Wiggle.MeshCollisions))
                foreach (var obj in objects)
                {
                    poseHash.Add(obj.GetWorldMatrix());
                    poseHash.Add(obj.GetEffectiveVisibility());
                    foreach (var mesh in obj.Visuals) poseHash.Add(ComputeClothMeshSignature(mesh));
                }
            int poseSignature = poseHash.ToHashCode();
            bool loop = scene.Loop && playing && _wiggleWasPlaying && frame < _wiggleFrame;
            bool reset = _wiggleSimulation == null || configuration != _wiggleConfiguration || fps != _wiggleFps ||
                !objects.SequenceEqual(_wiggleObjects) || (frame == _wiggleFrame && poseSignature != _wigglePoseSignature) ||
                (!loop && (frame < _wiggleFrame || frame - _wiggleFrame > (playing ? fps : 1)));
            if (reset)
            {
                _wiggleSimulation = new(SceneObjects);
                foreach (var bone in active)
                    if ((bone.Wiggle.Freeze || WiggleSimulation.RigFor(bone).WiggleRig.Freeze) &&
                        _wiggleLastWorlds.TryGetValue(bone, out var held))
                    {
                        _wiggleSimulation.HoldWorld(bone, held);
                        if (bone is MiBoneSceneObject mi && _wiggleLastBends.TryGetValue(mi, out var bend))
                            _wiggleSimulation.HoldBend(mi, bend);
                    }
                _wiggleObjects = objects;
                _wiggleConfiguration = configuration;
                _wiggleFps = fps;
                _wiggleSimulation.Step(0, scene, WiggleWind(frame / fps));
                for (int i = 0; i < scene.Preroll; i++)
                    _wiggleSimulation.Step(1 / fps, scene, WiggleWind(frame / fps));
                _wiggleFrame = frame;
            }
            else if (frame != _wiggleFrame)
            {
                _wiggleSimulation!.Step(loop ? 1 / fps : (frame - _wiggleFrame) / fps, scene, WiggleWind(frame / fps));
                _wiggleFrame = frame;
            }
            _wiggleWasPlaying = playing;
            _wigglePoseSignature = poseSignature;
            _wiggleSimulation!.Apply();
            foreach (var bone in active)
            {
                _wiggleLastWorlds[bone] = bone.GetWorldMatrixNoPivot();
                if (bone is MiBoneSceneObject mi && bone.Wiggle.Bend.Enabled)
                    _wiggleLastBends[mi] = mi.GetEvaluatedEditableBendAngle();
            }
            WiggleError = _wiggleBakeError;
        }
        catch (InvalidDataException ex)
        {
            ReportWiggleError(ex);
            foreach (var bone in WiggleSimulation.Objects(SceneObjects).OfType<BoneSceneObject>())
                bone.SetEvaluatedWiggleWorld(null);
            foreach (var mi in WiggleSimulation.Objects(SceneObjects).OfType<MiBoneSceneObject>())
                mi.RefreshInverseKinematicsVisuals();
            _wiggleSimulation = null;
            if (requireBake) throw;
        }
    }

    public bool BakeWiggle()
    {
        var owner = MainViewport ?? this;
        if (owner != this) return owner.BakeWiggle();
        if (IsWiggleBaking || IsClothBaking)
            throw new InvalidOperationException("Finish or cancel the current physics bake first.");
        _showWiggleBakePopup = true;
        _wiggleBakeError = null;
        _wiggleBakeCancelled = false;
        try
        {
            var scene = ProjectManager.Instance.Manifest.Settings.Wiggle;
            scene.Validate();
            if (!scene.Enabled) throw new InvalidDataException("Enable scene wiggle before baking.");
            var objects = WiggleSimulation.Objects(SceneObjects).ToArray();
            var bones = objects.OfType<BoneSceneObject>().Where(WiggleSimulation.Active).ToArray();
            if (bones.Length == 0) throw new InvalidDataException("Enable at least one wiggle bone before baking.");
            if (bones.Any(b => b.Wiggle.Freeze || WiggleSimulation.RigFor(b).WiggleRig.Freeze))
                throw new InvalidDataException("Unfreeze the wiggle bones and armatures before baking.");
            var timeline = Timeline.Instance;
            int end = timeline?.MaxFrames ?? 0;
            float fps = timeline?.Framerate ?? 30;
            WiggleSettings.Range(fps, 1, 1000, "Bake framerate");
            int valuesPerFrame = bones.Sum(b => b.Wiggle.Bend.Enabled ? 19 : 16);
            if (end < 0 || ((long)end + 1) * valuesPerFrame * sizeof(float) > WiggleBake.MaxBytes)
                throw new InvalidDataException("Wiggle bake exceeds 64 MiB. Reduce bone count or timeline length.");
            if (timeline?.IsPlaying == true) timeline.TogglePlayPause();
            ulong configuration = WiggleConfiguration.Compute(objects, scene);
            var bakes = bones.Select(b => new WiggleBake
            {
                Framerate = fps,
                Settings = b.Wiggle.BakeSettings,
                Scene = scene,
                Configuration = configuration,
                EndOffset = [b.GetWiggleEndOffset().x, b.GetWiggleEndOffset().y, b.GetWiggleEndOffset().z],
                Frames = new float[end + 1][],
                BendFrames = b.Wiggle.Bend.Enabled ? new float[end + 1][] : []
            }).ToArray();
            _wiggleJob = new()
            {
                Objects = objects,
                Bones = bones,
                Bakes = bakes,
                Scene = scene,
                Configuration = configuration,
                Simulation = new(SceneObjects)
            };
            WiggleBakeCompletedFrames = 0;
            WiggleBakeTotalFrames = end + 1;
            WiggleError = null;
            return true;
        }
        catch (InvalidDataException ex) { _wiggleBakeError = ex.Message; ReportWiggleError(ex); return false; }
    }

    public void AdvanceWiggleBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.AdvanceWiggleBake(); return; }
        if (_wiggleJob is not { } job) return;
        try
        {
            var objects = WiggleSimulation.Objects(SceneObjects).ToArray();
            if (!objects.SequenceEqual(job.Objects) ||
                WiggleConfiguration.Compute(objects, ProjectManager.Instance.Manifest.Settings.Wiggle) != job.Configuration ||
                job.Bones.Any(b => !WiggleSimulation.Active(b) || b.Wiggle.Freeze || WiggleSimulation.RigFor(b).WiggleRig.Freeze) ||
                (Timeline.Instance?.Framerate ?? 30) != job.Bakes[0].Framerate ||
                (Timeline.Instance?.MaxFrames ?? 0) + 1 != job.Bakes[0].Frames.Length)
                throw new InvalidDataException("Wiggle scene changed while baking. Previous baked frames were kept.");
            void Sample()
            {
                float fps = job.Bakes[0].Framerate;
                WindSettings wind = WiggleWind(job.Frame / fps);
                if (job.Frame == 0)
                {
                    job.Simulation.Step(0, job.Scene, wind);
                    for (int i = 0; i < job.Scene.Preroll; i++) job.Simulation.Step(1 / fps, job.Scene, wind);
                }
                else job.Simulation.Step(1 / fps, job.Scene, wind);
                for (int i = 0; i < job.Bones.Length; i++)
                {
                    job.Bakes[i].Frames[job.Frame] = WiggleBake.Values(job.Simulation.WorldFor(job.Bones[i]));
                    if (job.Bones[i] is MiBoneSceneObject mi && mi.Wiggle.Bend.Enabled)
                    {
                        var bend = job.Simulation.BendFor(mi);
                        job.Bakes[i].BendFrames[job.Frame] = [bend.x, bend.y, bend.z];
                    }
                }
            }
            void SampleWind()
            {
                if (PropertiesPanel is { } properties) properties.SampleWiggleWind(job.Frame, Sample);
                else Sample();
            }
            if (Timeline.Instance is { } timeline) timeline.SamplePhysicsFrame(job.Frame, SampleWind, includeWiggle: false);
            else
            {
                InverseKinematicsSolver.Evaluate(SceneObjects, job.Frame, job.Bakes[0].Framerate);
                SampleWind();
                InverseKinematicsSolver.Evaluate(SceneObjects);
            }
            job.Frame++;
            WiggleBakeCompletedFrames = job.Frame;
            if (job.Frame == job.Bakes[0].Frames.Length)
            {
                foreach (var bake in job.Bakes) bake.Validate();
                for (int i = 0; i < job.Bones.Length; i++) job.Bones[i].WiggleBake = job.Bakes[i];
                _wiggleJob = null;
                ResetWiggle();
                ProjectManager.Instance.SetDirty(true);
            }
        }
        catch (InvalidDataException ex)
        {
            _wiggleJob = null;
            ResetWiggle();
            _wiggleBakeError = ex.Message;
            ReportWiggleError(ex);
        }
    }

    public void CancelWiggleBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.CancelWiggleBake(); return; }
        _wiggleJob = null;
        _wiggleBakeCancelled = true;
        ResetWiggle();
    }

    public void ClearWiggleBake()
    {
        var owner = MainViewport ?? this;
        if (owner != this) { owner.ClearWiggleBake(); return; }
        CancelWiggleBake();
        foreach (var bone in WiggleSimulation.Objects(SceneObjects).OfType<BoneSceneObject>()) bone.WiggleBake = null;
        ProjectManager.Instance.SetDirty(true);
    }

    private void ReportWiggleError(InvalidDataException ex)
    {
        if (WiggleError != ex.Message) Console.Error.WriteLine($"Wiggle: {ex.Message}");
        WiggleError = ex.Message;
    }

    public void DrawWiggleBakePopup()
    {
        if (!_showWiggleBakePopup) return;
        string title = Localization.T("text.wiggle.bake") + "##WiggleBakeProgress";
        ImGui.OpenPopup(title);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f));
        if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.AlwaysAutoResize)) return;
        string status = IsWiggleBaking ? "text.wiggle.baking" : _wiggleBakeError != null ? "text.wiggle.bake_failed" :
            _wiggleBakeCancelled ? "text.wiggle.bake_cancelled" : "text.wiggle.bake_complete";
        ImGui.TextUnformatted(Localization.T(status));
        float progress = WiggleBakeTotalFrames == 0 ? 0 : WiggleBakeCompletedFrames / (float)WiggleBakeTotalFrames;
        ImGui.ProgressBar(progress, new Vector2(360f * UiTheme.Scale, 0), $"{progress * 100:0.0}%");
        ImGui.TextUnformatted(Localization.Format("text.wiggle.bake_progress", WiggleBakeCompletedFrames, WiggleBakeTotalFrames));
        if (_wiggleBakeError != null) UiText.Wrapped(_wiggleBakeError);
        if (IsWiggleBaking)
        {
            if (ImGui.Button(Localization.T("text.wiggle.cancel"))) CancelWiggleBake();
        }
        else if (ImGui.Button(Localization.T("text.window.main.close")))
        {
            _showWiggleBakePopup = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
