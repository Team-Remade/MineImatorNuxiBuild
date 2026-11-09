using System.Numerics;
using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private static readonly Vector2 SnapImageMin = new(100, 50);
    private static readonly Vector2 SnapImageSize = new(800, 600);
    private const BindingFlags SnapPrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static Camera SnapCamera() => new() { Yaw = 0, Pitch = 0, Distance = 10 };
    private static Vector2 SnapScreen(Camera camera, vec3 point) =>
        camera.UnprojectPosition(point, SnapImageMin, SnapImageSize);

    private static void SnapPreferences(Fixture fixture)
    {
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.PreferencesState)!.Snapping;
        Check(!defaults.Enabled && defaults.Move && defaults.Rotate && defaults.Scale, "Legacy preference defaults");
        var settings = new SnapSettings
        {
            Enabled = true, Mode = SnapMode.Geometry,
            Elements = SnapElements.Vertex | SnapElements.EdgeMidpoint | SnapElements.Face | SnapElements.Origin,
            TranslationStep = 0.125f, RotationStep = 22.5f, ScaleStep = 0.2f,
            PixelRadius = 24f, AlignToNormal = true, XRay = true, Rotate = false
        };
        var state = new PreferencesState { Snapping = settings };
        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(state, AppJsonContext.Default.PreferencesState),
            AppJsonContext.Default.PreferencesState)!.Snapping;
        Check(restored.Enabled && restored.XRay && restored.AlignToNormal && !restored.Rotate, "Flags persist");
        Equal(settings.Mode, restored.Mode, "Mode persists");
        Equal(settings.Elements, restored.Elements, "Combined geometry elements persist");
        Near(.125f, restored.TranslationStep, "Translation persists");
        Near(22.5f, restored.RotationStep, "Rotation persists");
        Near(.2f, restored.ScaleStep, "Scale persists");
        Near(24f, restored.PixelRadius, "Radius persists");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Reject(() => settings.TranslationStep = invalid);
            Reject(() => settings.RotationStep = invalid);
            Reject(() => settings.ScaleStep = invalid);
            Reject(() => settings.PixelRadius = invalid);
        }
        Reject(() => settings.PixelRadius = 257f);
        Reject(() => settings.Elements = 0);
        Reject(() => settings.Mode = (SnapMode)99);
        var gizmo = new Gizmo3D(null!) { SnapSettings = settings };
        Check(gizmo.Snapping, "Magnet enables snapping");
        gizmo.InvertSnapping = true;
        Check(!gizmo.Snapping, "Ctrl temporarily disables magnet");
        settings.Enabled = false;
        Check(gizmo.Snapping, "Ctrl temporarily enables snapping");
        gizmo.ShiftSnap = true;
        Near(settings.TranslationStep / 10f, gizmo.GetTranslateSnap(), "Fine translation");
        Near(settings.RotationStep / 3f, gizmo.GetRotationSnap(), "Fine rotation");
        Near(settings.ScaleStep / 2f, gizmo.GetScaleSnap(), "Fine scaling");
        Equal("Shift+Tab", Keybinds.Label("toggleSnapping"), "Blender-style default shortcut");

        static void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { return; }
            throw new InvalidOperationException("Invalid snapping setting was accepted.");
        }
    }

    private static void SnapConstraints(Fixture fixture)
    {
        Near(1f, SnapMath.Round(.5f, 1f), "Positive half step rounds away from zero");
        Near(-1f, SnapMath.Round(-.5f, 1f), "Negative half step rounds symmetrically");
        vec3 center = new(.25f, -.3f, .4f), delta = new(.6f, -.6f, 1.6f);
        foreach (var plane in Enum.GetValues<Gizmo3D.TransformPlane>())
        {
            mat4 basis = mat4.RotateZ(.6f) * mat4.RotateY(.3f);
            vec3 relative = SnapMath.Translation(center, delta, basis, plane, .25f, false);
            vec3 absolute = SnapMath.Translation(center, delta, basis, plane, .25f, true);
            Near(relative, SnapMath.Constrain(relative, basis, plane), $"Relative preserves {plane}");
            Near(absolute, SnapMath.Constrain(absolute, basis, plane), $"Absolute preserves {plane}");
            for (int axis = 0; axis < 3; axis++)
            {
                vec3 direction = new(basis[axis, 0], basis[axis, 1], basis[axis, 2]);
                if (SnapMath.Constrain(direction, basis, plane).LengthSqr < .5f) continue;
                float rel = vec3.Dot(relative, direction);
                float abs = vec3.Dot(center + absolute, direction);
                Near(SnapMath.Round(rel, .25f), rel, "Relative lattice");
                Near(SnapMath.Round(abs, .25f), abs, "Absolute lattice");
            }
        }
        Near(new vec3(1f, -1f, 2f), SnapMath.Translation(center, delta, mat4.Identity,
            Gizmo3D.TransformPlane.View, 1f, false), "Relative snap retains initial offset");
        Near(new vec3(.75f, -.7f, 1.6f), SnapMath.Translation(center, delta, mat4.Identity,
            Gizmo3D.TransformPlane.View, 1f, true), "Absolute snap removes initial offset");
        foreach (vec3 normal in new[] { vec3.UnitY, -vec3.UnitY, vec3.UnitZ, new vec3(.3f, .5f, -.7f).Normalized })
        {
            vec3 up = new vec3(.2f, .8f, .3f).Normalized;
            Near(normal, (SnapMath.Align(up, normal) * new vec4(up, 0f)).xyz, "Alignment follows normal");
        }
        Near(-vec3.UnitY, (SnapMath.Align(vec3.UnitY, -vec3.UnitY) * new vec4(vec3.UnitY, 0f)).xyz,
            "Antiparallel alignment is stable");
    }

    private static SnapGeometry SnapSquare(Camera camera, float z = 0f)
    {
        var scene = new SnapGeometry(camera, SnapImageMin, SnapImageSize);
        scene.AddMesh([new(-1, -1, z), new(1, -1, z), new(1, 1, z), new(-1, 1, z)],
            [0, 1, 2, 0, 2, 3]);
        return scene;
    }

    private static void SnapTargets(Fixture fixture)
    {
        var camera = SnapCamera();
        var scene = SnapSquare(camera);
        scene.AddOrigin(new vec3(.3f, .4f, 1f));
        scene.AddOrigin(new vec3(0, 0, 12f));
        var vertex = scene.Find(SnapScreen(camera, new vec3(1, 1, 0)), SnapElements.Vertex, 16, false);
        Near(new vec3(1, 1, 0), vertex!.Value.Position, "Vertex target");
        var edge = scene.Find(SnapScreen(camera, new vec3(1, 0, 0)), SnapElements.EdgeMidpoint, 16, false);
        Near(new vec3(1, 0, 0), edge!.Value.Position, "Real edge midpoint");
        Check(!scene.Find(SnapScreen(camera, vec3.Zero), SnapElements.EdgeMidpoint, 8, true).HasValue,
            "Triangulation diagonal is not an edge midpoint");
        var face = scene.Find(SnapScreen(camera, new vec3(.25f, -.25f, 0)), SnapElements.Face, 16, false);
        Near(new vec3(.25f, -.25f, 0), face!.Value.Position, "Face ray intersection, not triangle center");
        Near(vec3.UnitZ, face.Value.Normal, "Winding supplies surface normal");
        var origin = scene.Find(SnapScreen(camera, new vec3(.3f, .4f, 1f)), SnapElements.Origin, 16, false);
        Near(new vec3(.3f, .4f, 1f), origin!.Value.Position, "Logical object origin");
        Check(!scene.Find(new Vector2(102, 52), SnapElements.Vertex, 16, true).HasValue, "No target outside radius");
        var combined = scene.Find(SnapScreen(camera, new vec3(1, 1, 0)),
            SnapElements.Vertex | SnapElements.Face, 16, false);
        Equal(SnapElements.Vertex, combined!.Value.Element, "Discrete targets take precedence over faces");
        var behind = new SnapGeometry(camera, SnapImageMin, SnapImageSize);
        behind.AddOrigin(new vec3(0, 0, 12f));
        Check(!behind.Find(SnapScreen(camera, vec3.Zero), SnapElements.Origin, 16, true).HasValue,
            "Behind-camera targets rejected even with X-Ray");
    }

    private static void SnapOcclusion(Fixture fixture)
    {
        var camera = SnapCamera();
        var scene = SnapSquare(camera, 1f);
        scene.AddOrigin(vec3.Zero);
        var mouse = SnapScreen(camera, vec3.Zero);
        Check(!scene.Find(mouse, SnapElements.Origin, 16, false).HasValue, "Opaque surface occludes origin");
        Near(vec3.Zero, scene.Find(mouse, SnapElements.Origin, 16, true)!.Value.Position, "X-Ray bypasses occlusion");
        var nearFace = scene.Find(mouse, SnapElements.Face, 16, false);
        Near(new vec3(0, 0, 1), nearFace!.Value.Position, "Nearest surface wins");
        var unindexed = new SnapGeometry(camera, SnapImageMin, SnapImageSize);
        unindexed.AddMesh([new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0),
            new(-1, -1, 0), new(1, 1, 0), new(-1, 1, 0)], null);
        Check(!unindexed.Find(mouse, SnapElements.EdgeMidpoint, 8, false).HasValue,
            "Non-indexed triangle seams are excluded too");
        Check(unindexed.Find(SnapScreen(camera, new vec3(1, 1, 0)), SnapElements.Vertex, 16, false).HasValue,
            "Surface vertex must not occlude itself");
        var clippedCamera = SnapCamera();
        clippedCamera.Near = 2f;
        clippedCamera.Far = 12f;
        var clipped = SnapSquare(clippedCamera, 9f);
        clipped.AddMesh([new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0)], null);
        Near(vec3.Zero, clipped.Find(mouse, SnapElements.Face, 16, false)!.Value.Position,
            "Near-clipped triangles do not intercept face snapping");
        var beyondFar = SnapSquare(clippedCamera, -3f);
        Check(!beyondFar.Find(mouse, SnapElements.Face, 16, false).HasValue, "Far-clipped faces rejected");
        camera.Target = new vec3(5, 5, 5);
        Near(new vec3(0, 0, 1), scene.Find(mouse, SnapElements.Face, 16, false)!.Value.Position,
            "Drag snapshot retains its original camera projection");
    }

    private static Gizmo3D SnapDrag(Camera camera, SceneObject[] objects, SnapSettings settings,
        Gizmo3D.TransformPlane plane = Gizmo3D.TransformPlane.View,
        Gizmo3D.TransformMode mode = Gizmo3D.TransformMode.Translate, bool local = false,
        vec3? clickWorld = null)
    {
        var gizmo = new Gizmo3D(null!) { Mode = Gizmo3D.ToolMode.Move, SnapSettings = settings, UseLocalSpace = local };
        foreach (var obj in objects) gizmo.Select(obj);
        vec3 center = objects.Select(obj => obj.GetWorldPosition()).Aggregate(vec3.Zero, (a, b) => a + b) / objects.Length;
        var click = SnapScreen(camera, clickWorld ?? center);
        gizmo.UpdateHover(click, camera, SnapImageMin, SnapImageSize);
        typeof(Gizmo3D).GetMethod("ComputeEdit", SnapPrivate)!.Invoke(gizmo, [click]);
        var field = typeof(Gizmo3D).GetField("_edit", SnapPrivate)!;
        object edit = field.GetValue(gizmo)!;
        void Set(string name, object value) => edit.GetType().GetField(name)!.SetValue(edit, value);
        Set("Mode", mode);
        Set("Plane", plane);
        Set("MousePos", click);
        Set("MousePosAtStart", click);
        Set("RotationAxis", vec3.UnitZ);
        Set("GizmoInitiated", true);
        field.SetValue(gizmo, edit);
        typeof(Gizmo3D).GetProperty(nameof(Gizmo3D.Editing))!.SetValue(gizmo, true);
        return gizmo;
    }

    private static void SnapTransforms(Fixture fixture)
    {
        var camera = SnapCamera();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        obj.SetLocalPosition(new vec3(.25f, .25f, 0));
        var settings = new SnapSettings { Enabled = true };
        var gizmo = SnapDrag(camera, [obj], settings);
        var mouse = SnapScreen(camera, new vec3(.85f, .85f, 0));
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(1.25f, 1.25f, 0), obj.GetWorldPosition(), "Relative snapping in actual drag");
        settings.Mode = SnapMode.Grid;
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(1, 1, 0), obj.GetWorldPosition(), "Switching mode recomputes from captured start");
        gizmo.InvertSnapping = true;
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(.85f, .85f, 0), obj.GetWorldPosition(), "Ctrl removes snap without accumulating drift");
        gizmo.InvertSnapping = false;
        settings.Mode = SnapMode.Increment;
        gizmo.ShiftSnap = true;
        gizmo.ContinueEdit(SnapScreen(camera, new vec3(.88f, .88f, 0)), camera, SnapImageMin, SnapImageSize);
        Near(new vec3(.85f, .85f, 0), obj.GetWorldPosition(), "Fine relative increment");
        settings.Move = false;
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(.85f, .85f, 0), obj.GetWorldPosition(), "Move affect toggle disables snap");
        gizmo.EndEdit();
        Check(!gizmo.SnapTarget.HasValue && !gizmo.Editing, "End clears edit state");

        var parent = new SceneObject { PivotOffset = vec3.Zero };
        parent.SetLocalRotation(new vec3(0, 0, .6f));
        parent.SetLocalScale(new vec3(2, 3, 1));
        var child = new SceneObject { PivotOffset = vec3.Zero };
        parent.AddChild(child);
        var second = new SceneObject { PivotOffset = vec3.Zero };
        second.SetLocalPosition(new vec3(2, 0, 0));
        settings.Move = true;
        settings.Mode = SnapMode.Geometry;
        settings.Elements = SnapElements.Origin;
        gizmo = SnapDrag(camera, [child, second], settings);
        gizmo.CreateSnapGeometry = (cam, min, size) =>
        {
            var scene = new SnapGeometry(cam, min, size);
            scene.AddOrigin(new vec3(3, 2, 0));
            return scene;
        };
        var targetMouse = SnapScreen(camera, new vec3(3, 2, 0));
        gizmo.ContinueEdit(targetMouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(2, 2, 0), child.GetWorldPosition(), "Parent inverse converts snapped position");
        Near(new vec3(4, 2, 0), second.GetWorldPosition(), "Group uses common selection-center delta");
        Near(new vec3(2, 0, 0), second.GetWorldPosition() - child.GetWorldPosition(), "Group spacing unchanged");
        gizmo.EndEdit();
    }

    private static void SnapAlignment(Fixture fixture)
    {
        var camera = SnapCamera();
        var parent = new SceneObject { PivotOffset = vec3.Zero };
        parent.SetLocalRotation(new vec3(.1f, .2f, .3f));
        var child = new SceneObject { PivotOffset = vec3.Zero };
        parent.AddChild(child);
        child.SetLocalRotation(new vec3(.25f, -.1f, .3f));
        vec3 initial = child.Rotation;
        var settings = new SnapSettings { Enabled = true, Mode = SnapMode.Geometry,
            Elements = SnapElements.Face, AlignToNormal = true };
        var gizmo = SnapDrag(camera, [child], settings);
        int builds = 0;
        gizmo.CreateSnapGeometry = (cam, min, size) => { builds++; return SnapSquare(cam, -1); };
        var target = new vec3(.5f, .5f, -1f);
        var mouse = SnapScreen(camera, target);
        for (int frame = 0; frame < 3; frame++)
        {
            gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
            Near(target, child.GetWorldPosition(), "Center reaches face in unconstrained move");
            mat4 world = child.GetWorldMatrixNoPivot();
            Near(vec3.UnitZ, new vec3(world.m10, world.m11, world.m12).Normalized,
                "Local Y aligns to normal through parent rotation");
        }
        Equal(1, builds, "Scene snapshot reused throughout drag");
        Check(gizmo.NormalAlignmentApplied, "Alignment exposed to auto-key handler");
        gizmo.InvertSnapping = true;
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(initial, child.Rotation, "Ctrl restores original rotation, not stale surface alignment");
        Check(!gizmo.NormalAlignmentApplied, "Releasing snapping removes alignment flag");
        gizmo.EndEdit();
        Check(!gizmo.SnapTarget.HasValue, "Target marker cleared on release");

        var constrained = new SceneObject { PivotOffset = vec3.Zero };
        var axisGizmo = SnapDrag(camera, [constrained], settings, Gizmo3D.TransformPlane.X);
        axisGizmo.CreateSnapGeometry = (cam, min, size) => SnapSquare(cam, -1);
        axisGizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(new vec3(.5f, 0, 0), constrained.GetWorldPosition(), "Face snapping preserves axis constraint");
    }

    private static void SnapScaleRotation(Fixture fixture)
    {
        var camera = SnapCamera();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        var settings = new SnapSettings { Enabled = true, ScaleStep = .25f, RotationStep = 15f };
        var gizmo = SnapDrag(camera, [obj], settings, mode: Gizmo3D.TransformMode.Scale);
        Vector2 start = SnapScreen(camera, vec3.Zero);
        gizmo.ContinueEdit(start + new Vector2(27f, 0), camera, SnapImageMin, SnapImageSize);
        Near(new vec3(1.25f), obj.Scale, "Uniform scale snaps factor delta");
        gizmo.ShiftSnap = true;
        gizmo.ContinueEdit(start + new Vector2(27f, 0), camera, SnapImageMin, SnapImageSize);
        Near(new vec3(1.375f), obj.Scale, "Fine scale uses half step");
        settings.Scale = false;
        gizmo.ContinueEdit(start + new Vector2(27f, 0), camera, SnapImageMin, SnapImageSize);
        Near(new vec3(1f + 27f / 80f), obj.Scale, "Scale affect toggle");
        gizmo.EndEdit();
        obj.SetLocalScale(vec3.Ones);
        gizmo = SnapDrag(camera, [obj], settings, Gizmo3D.TransformPlane.Z,
            Gizmo3D.TransformMode.Rotate, clickWorld: vec3.UnitX);
        gizmo.ContinueEdit(SnapScreen(camera, vec3.UnitX), camera, SnapImageMin, SnapImageSize);
        float angle = glm.Radians(22f);
        var mouse = SnapScreen(camera, new vec3(MathF.Cos(angle), MathF.Sin(angle), 0));
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(glm.Radians(15f), obj.Rotation.z, "Actual rotation drag quantized in degrees");
        settings.Rotate = false;
        gizmo.ContinueEdit(mouse, camera, SnapImageMin, SnapImageSize);
        Near(angle, obj.Rotation.z, "Rotation affect toggle removes quantization");
    }

    private static void SnapSceneCollection(Fixture fixture)
    {
        var camera = SnapCamera();
        var viewport = new Viewport();
        var selected = new SceneObject { PivotOffset = vec3.Zero };
        var target = new SceneObject { PivotOffset = vec3.Zero };
        target.SetLocalPosition(new vec3(2, 1, 0));
        target.PivotOffset = new vec3(0, 0, .5f);
        var mesh = new Mesh(null!);
        mesh.Vertices.AddRange([new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0)]);
        target.AddMesh(mesh);
        var hidden = new SceneObject { PivotOffset = vec3.Zero, IsHidden = true };
        hidden.SetLocalPosition(new vec3(-2, 0, 0));
        viewport.SceneObjects.AddRange([selected, target, hidden]);
        var gizmo = SnapDrag(camera, [selected], new SnapSettings());
        typeof(Viewport).GetProperty(nameof(Viewport.Gizmo))!.SetValue(viewport, gizmo);
        var build = typeof(Viewport).GetMethod("BuildSnapGeometry", SnapPrivate)!;
        var scene = (SnapGeometry)build.Invoke(viewport, [camera, SnapImageMin, SnapImageSize])!;
        Near(target.GetWorldPosition(), scene.Find(SnapScreen(camera, target.GetWorldPosition()),
            SnapElements.Origin, 10, true)!.Value.Position, "Origin uses logical pivot, not mesh offset");
        Check(!scene.Find(SnapScreen(camera, vec3.Zero), SnapElements.Origin, 10, true).HasValue, "Selected object excluded");
        Check(!scene.Find(SnapScreen(camera, hidden.GetWorldPosition()), SnapElements.Origin, 10, true).HasValue, "Hidden object excluded");
        vec3 vertex = (target.GetWorldMatrix() * new vec4(mesh.Vertices[2], 1f)).xyz;
        Near(vertex, scene.Find(SnapScreen(camera, vertex), SnapElements.Vertex, 10, true)!.Value.Position,
            "Pivot and world transform applied to mesh vertices");
    }

    private static unsafe void SnapInput(Fixture fixture)
    {
        var camera = SnapCamera();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        var settings = new SnapSettings { Enabled = true };
        var gizmo = new Gizmo3D(null!) { Mode = Gizmo3D.ToolMode.Move, SnapSettings = settings };
        gizmo.Select(obj);
        var input = new Input();
        Vector2 start = SnapScreen(camera, vec3.Zero);
        void Frame(Vector2 mouse, bool down, bool clicked = false, bool released = false,
            bool ctrl = false, bool fine = false, bool overlays = true)
        {
            input.ProcessInput(camera, gizmo, SnapImageMin, SnapImageSize, null, null, mouse,
                down, false, false, clicked, false, released, false, 0,
                false, false, false, false, false, false, false, false,
                false, false, 1f / 60f, true, ctrl, overlays, snapFine: fine);
        }
        gizmo.UpdateHover(start, camera, SnapImageMin, SnapImageSize);
        Frame(start, true, clicked: true);
        Check(input.IsGizmoDragging, "Center handle starts actual input drag");
        var mouse = SnapScreen(camera, new vec3(.64f, .64f, 0));
        Frame(mouse, true);
        Near(new vec3(1, 1, 0), obj.GetWorldPosition(), "Input pipeline enables snapping");
        Frame(mouse, true, ctrl: true);
        Near(new vec3(.64f, .64f, 0), obj.GetWorldPosition(), "Ctrl state reaches shared gizmo");
        Frame(mouse, true, fine: true);
        Near(new vec3(.6f, .6f, 0), obj.GetWorldPosition(), "Dedicated Shift is independent of flight bindings");
        Frame(mouse, false, released: true);
        Check(!input.IsGizmoDragging && !gizmo.Editing, "Input release commits transform");

        obj.SetLocalPosition(vec3.Zero);
        gizmo.UpdateHover(start, camera, SnapImageMin, SnapImageSize);
        Frame(start, true, clicked: true);
        Frame(mouse, true, overlays: false);
        Check(!input.IsGizmoDragging && !gizmo.Editing, "Hiding overlays ends active snapping drag");
        Frame(mouse, false, released: true, overlays: false);
    }

    private static void SnapKeyframesHistory(Fixture fixture)
    {
        var selection = SelectionManager.Instance;
        selection.Gizmo = null;
        selection.ClearSelection();
        var viewport = new Viewport();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        viewport.SceneObjects.Add(obj);
        selection.SelectObject(obj);
        var timeline = new Timeline { Viewport = viewport };
        typeof(Timeline).GetField("_autoKeyframe", SnapPrivate)!.SetValue(timeline, true);
        selection.Timeline = timeline;
        var camera = SnapCamera();
        var settings = new SnapSettings { Enabled = true, Mode = SnapMode.Geometry,
            Elements = SnapElements.Face, AlignToNormal = true };
        var gizmo = SnapDrag(camera, [obj], settings);
        selection.Gizmo = gizmo;
        gizmo.CreateSnapGeometry = (cam, min, size) => SnapSquare(cam, -1f);
        var before = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
        var hierarchy = new SceneHistoryAction.HierarchyState(viewport);
        var project = SceneHistoryAction.ProjectState.Capture(ProjectManager.Instance.Manifest);
        try
        {
            gizmo.ContinueEdit(SnapScreen(camera, new vec3(.5f, .5f, -1f)), camera, SnapImageMin, SnapImageSize);
            gizmo.EndEdit();
            foreach (string channel in new[] { "position.x", "position.y", "position.z",
                "rotation.x", "rotation.y", "rotation.z" })
                Check(obj.Keyframes.ContainsKey(channel), $"Auto-key records {channel} including alignment");
            vec3 position = obj.Position, rotation = obj.Rotation;
            var after = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
            var history = SceneHistoryAction.Create([(obj, before, after)], hierarchy, hierarchy, project, project)!;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Check(history.Apply(viewport, null, undo: true), "Snapped transform undo");
                Near(vec3.Zero, obj.Position, "Undo restores position");
                Near(vec3.Zero, obj.Rotation, "Undo restores rotation");
                Check(history.Apply(viewport, null, undo: false), "Snapped transform redo");
                Near(position, obj.Position, "Redo restores snapped position");
                Near(rotation, obj.Rotation, "Redo restores surface alignment");
            }
        }
        finally
        {
            selection.Timeline = null;
            selection.Gizmo = null;
            selection.ClearSelection();
        }
    }

    private static void SnapLargeScene(Fixture fixture)
    {
        var camera = SnapCamera();
        var scene = new SnapGeometry(camera, SnapImageMin, SnapImageSize);
        var vertices = new List<vec3>();
        var indices = new List<uint>();
        const int side = 100;
        for (int y = 0; y <= side; y++)
        for (int x = 0; x <= side; x++)
            vertices.Add(new vec3((x - side / 2) * .1f, (y - side / 2) * .1f, 0));
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            uint a = (uint)(y * (side + 1) + x), b = a + 1, c = a + side + 1, d = c + 1;
            indices.AddRange([a, b, d, a, d, c]);
        }
        scene.AddMesh(vertices, indices.ToArray());
        scene.Build();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            vec3 target = new((i % 80 - 40) * .1f, (i % 60 - 30) * .1f, 0);
            var hit = scene.Find(SnapScreen(camera, target), SnapElements.Vertex | SnapElements.Face, 12, false);
            Near(target, hit!.Value.Position, "Accelerated query returns exact vertex");
        }
        watch.Stop();
        Check(watch.Elapsed < TimeSpan.FromSeconds(2),
            $"200 queries on 20,000 triangles exceeded 2s: {watch.Elapsed.TotalMilliseconds:0}ms");
        Console.WriteLine($"  Snap BVH: 200 queries / 20,000 triangles in {watch.Elapsed.TotalMilliseconds:0}ms");
    }

    private static unsafe void SnapControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            Localization.SetLanguage("en_us");
            var viewport = new Viewport();
            var gizmo = new Gizmo3D(null!);
            typeof(Viewport).GetProperty(nameof(Viewport.Gizmo))!.SetValue(viewport, gizmo);
            var render = typeof(Viewport).GetMethod("RenderSnapSettings", SnapPrivate)!;
            void Frame()
            {
                ImGui.NewFrame();
                ImGui.Begin("Snapping controls");
                ImGui.OpenPopup("vpSnapSettingsPopup");
                render.Invoke(viewport, null);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            gizmo.SnapSettings.Mode = SnapMode.Geometry;
            Frame();
            Check(Localization.T("text.snap.element.edgemidpoint") == "Edge Midpoints", "Geometry labels localized");
            Check(!gizmo.SnapSettings.Enabled, "Opening settings does not enable snapping");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
