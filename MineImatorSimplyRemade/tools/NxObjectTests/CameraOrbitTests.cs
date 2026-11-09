using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static unsafe void CameraOrbitControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var camera = new CameraSceneObject();
            var secondCamera = new CameraSceneObject();
            var panel = new PropertiesPanel();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, camera);
            var draw = typeof(PropertiesPanel).GetMethod("DrawCameraOrbitControls", flags)!;
            System.Numerics.Vector2 checkbox = default;
            float Frame(bool down = false, bool hover = false)
            {
                io.AddMousePosEvent(hover ? checkbox.X : -100f, hover ? checkbox.Y : -100f);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Camera orbit controls");
                float start = ImGui.GetCursorPosY();
                draw.Invoke(panel, [camera]);
                float height = ImGui.GetCursorPosY() - start;
                if (!camera.OrbitEnabled)
                    checkbox = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
                ImGui.End();
                ImGui.Render();
                return height;
            }
            Frame();
            float hidden = Frame();
            SelectionManager.Instance.SelectObject(camera);
            SelectionManager.Instance.SelectObject(secondCamera);
            Frame(true, true);
            float visible = Frame(false, true);
            Check(camera.OrbitEnabled && secondCamera.OrbitEnabled, "Enabled checkbox edits selected cameras");
            Check(visible > hidden + ImGui.GetFontSize() * 3f, "Enabled reveals all four orbit settings");
            camera.OrbitDistance = 237f;
            Frame(true, true);
            float collapsed = Frame(false, true);
            Check(!camera.OrbitEnabled && !secondCamera.OrbitEnabled, "Enabled checkbox disables selected cameras");
            Near(hidden, collapsed, "Disabled hides the remaining settings");
            Near(237f, camera.OrbitDistance, "Hiding settings retains their values");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static void CameraOrbitTransforms(Fixture fixture)
    {
        var camera = new CameraSceneObject { PivotOffset = vec3.Zero };
        Check(!camera.OrbitEnabled && camera.OrbitLookAtPoint, "Orbit defaults");
        Near(100f, camera.OrbitDistance, "Default distance");
        Near(0f, camera.OrbitRotationXY, "Default horizontal angle");
        Near(0f, camera.OrbitRotationZ, "Default vertical angle");
        camera.SetLocalPosition(new vec3(10f, 20f, 30f));
        camera.SetLocalRotation(new vec3(.2f, .3f, .4f));
        mat4 original = camera.GetWorldMatrixNoPivot();
        camera.SyncCameraToTransform();
        Near(camera.Position, camera.ViewCamera.Position, "Disabled eye is unchanged");
        camera.OrbitEnabled = true;
        camera.UpdateVisualPosition();
        Near(camera.Position + new vec3(0f, 0f, 6.25f), camera.GetWorldPosition(), "Default distance 100 maps to 6.25 world units");
        foreach (float distance in new[] { 0f, 16f, 32f, 237f })
        {
            camera.OrbitDistance = distance;
            camera.UpdateVisualPosition();
            camera.SyncCameraToTransform();
            Near(distance / 16f, (camera.ViewCamera.Position - camera.Position).Length,
                "Editor distance uses the 16-to-1 world scale");
        }
        camera.OrbitDistance = 100f;

        foreach (float xy in new[] { 0f, 90f, 180f, 270f, 360f, -90f })
        foreach (float z in new[] { 0f, 45f, 90f, 135f, -90f })
        {
            camera.OrbitRotationXY = xy;
            camera.OrbitRotationZ = z;
            camera.UpdateVisualPosition();
            camera.SyncCameraToTransform();
            vec3 offset = camera.ViewCamera.Position - camera.Position;
            Near(6.25f, offset.Length, "Orbit maintains scaled distance");
            mat4 view = camera.ViewCamera.GetViewMatrix();
            var backward = new vec3(view.m02, view.m12, view.m22);
            Near(offset.Normalized, backward, "Camera points at the rotation point, including poles");
            Near(camera.GetWorldPosition(), camera.ViewCamera.Position, "Visual and render camera agree");
        }
        camera.OrbitRotationXY = 90f;
        camera.OrbitRotationZ = 0f;
        camera.UpdateVisualPosition();
        Near(camera.Position + vec3.UnitX * 6.25f, camera.GetWorldPosition(), "XY rotates horizontally");
        camera.OrbitRotationXY = 0f;
        camera.OrbitRotationZ = 90f;
        camera.UpdateVisualPosition();
        Near(camera.Position + vec3.UnitY * 6.25f, camera.GetWorldPosition(), "Z tilts vertically");

        camera.OrbitLookAtPoint = false;
        camera.UpdateVisualPosition();
        var freeRotation = camera.GetWorldMatrixNoPivot();
        Near(new vec3(original.m20, original.m21, original.m22),
            new vec3(freeRotation.m20, freeRotation.m21, freeRotation.m22), "Look-at off retains authored rotation");

        var parent = new SceneObject { PivotOffset = vec3.Zero };
        parent.SetLocalPosition(new vec3(3f, 4f, 5f));
        parent.SetLocalRotation(new vec3(.1f, .6f, -.2f));
        parent.AddChild(camera);
        foreach (bool lookAt in new[] { false, true })
        {
            camera.OrbitLookAtPoint = lookAt;
            camera.OrbitRotationXY = 30f;
            camera.OrbitRotationZ = 25f;
            camera.UpdateVisualPosition();
            camera.SyncCameraToTransform();
            vec3 eye = camera.ViewCamera.Position;
            vec3 point = camera.Position;
            camera.SyncTransformFromCamera();
            camera.SyncCameraToTransform();
            Near(eye, camera.ViewCamera.Position, "Parented navigation roundtrip preserves eye");
            Near(point, camera.Position, "Parented navigation roundtrip preserves rotation point");
            camera.ViewCamera.Target += new vec3(2f, -1f, 3f);
            eye = camera.ViewCamera.Position;
            camera.SyncTransformFromCamera();
            camera.SyncCameraToTransform();
            Near(eye, camera.ViewCamera.Position, "Viewport movement persists without orbit drift");
        }
        camera.OrbitEnabled = false;
        camera.UpdateVisualPosition();
        Near(camera.Position, (parent.GetWorldMatrixNoPivot().Inverse *
            new vec4(camera.GetWorldPosition(), 1f)).xyz, "Disabled orbit restores ordinary local position");
    }

    private static void CameraOrbitPersistence(Fixture fixture)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(!legacy.CameraOrbitEnabled && legacy.CameraOrbitLookAtPoint, "Old projects keep orbit disabled");
        Near(100f, legacy.CameraOrbitDistance, "Old project distance default");
        var camera = new CameraSceneObject
        {
            OrbitEnabled = true, OrbitDistance = 237f, OrbitRotationXY = -71f,
            OrbitRotationZ = 32f, OrbitLookAtPoint = false
        };
        var (archive, _) = fixture.Roundtrip(camera);
        var clone = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [archive.Root])!;
        var loaded = new CameraSceneObject();
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!;
        apply.Invoke(null, [loaded, clone, null]);
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { loaded, duplicate })
        {
            Check(copy.OrbitEnabled && !copy.OrbitLookAtPoint, "Archive/library/duplicate preserve toggles");
            Near(camera.OrbitDistance, copy.OrbitDistance, "Distance persists");
            Near(camera.OrbitRotationXY, copy.OrbitRotationXY, "XY persists");
            Near(camera.OrbitRotationZ, copy.OrbitRotationZ, "Z persists");
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (Action change in new Action[]
        {
            () => camera.OrbitEnabled = false, () => camera.OrbitDistance++,
            () => camera.OrbitRotationXY++, () => camera.OrbitRotationZ++, () => camera.OrbitLookAtPoint = true
        })
        {
            change();
            Check(Hash() != original, "Each orbit setting participates in dirty detection");
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), "Undo restores orbit settings");
        }
    }

    private static void CameraOrbitTimeline(Fixture fixture)
    {
        var camera = new CameraSceneObject();
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var discover = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var paths = ((IEnumerable<string>)discover.Invoke(null, [camera])!).ToArray();
        foreach (var (channel, value) in new (string, float)[]
        {
            ("orbit_enabled", 1f), ("orbit_distance", 125f), ("orbit_rotation_xy", 45f),
            ("orbit_rotation_z", -30f), ("orbit_look_at", 0f)
        })
        {
            string path = "camera." + channel;
            Check(paths.Contains(path), "Timeline discovers " + path);
            set.Invoke(timeline, [camera, path, value]);
            Near(value, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Timeline read/write " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = value }
            };
            float expected = channel is "orbit_enabled" or "orbit_look_at" ? 0f : value / 2f;
            Near(expected, Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, 5, false])),
                "Numeric interpolation and stepped toggles");
        }
        set.Invoke(timeline, [camera, "camera.orbit_rotation_xy", 20f]);
        mat4 first = camera.GetWorldMatrixNoPivot();
        set.Invoke(timeline, [camera, "camera.orbit_rotation_xy", 150f]);
        set.Invoke(timeline, [camera, "camera.orbit_rotation_xy", 20f]);
        Equal(first, camera.GetWorldMatrixNoPivot(), "Seeking is deterministic");
        set.Invoke(timeline, [camera, "camera.orbit_distance", -10f]);
        Near(0f, camera.OrbitDistance, "Distance cannot be negative");
    }
}
