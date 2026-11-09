using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void ItemOptionsPersistence(Fixture fixture)
    {
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(defaults.ItemIs3D && !defaults.ItemBounce && !defaults.ItemSpin && !defaults.ItemFaceCamera,
            "Old projects retain static, extruded items");
        var item = new SceneObject
        {
            SpawnCategory = "Items", ObjectType = "Item", ItemTileKey = "test-item",
            ItemBounce = true, ItemSpin = true, ItemFaceCamera = true
        };
        var (archive, _) = fixture.Roundtrip(item);
        var clone = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [archive.Root])!;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var loaded = new SceneObject { SpawnCategory = "Items" };
        apply.Invoke(null, [loaded, clone, null]);
        var duplicate = (SceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new SceneTree(),
            [item, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { loaded, duplicate })
        {
            Check(copy.ItemBounce && copy.ItemSpin && copy.ItemFaceCamera, "All toggles survive copying");
            Equal(item.ItemTileKey, copy.ItemTileKey, "Tile identity survives copying");
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly
            .GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { item }])!;
        ulong original = Hash();
        foreach (Action change in new Action[]
        {
            () => item.ItemBounce = false, () => item.ItemSpin = false, () => item.ItemFaceCamera = false
        })
        {
            change();
            Check(Hash() != original, "Each setting participates in dirty detection");
            apply.Invoke(null, [item, clone, null]);
            Equal(original, Hash(), "Undo restores the setting");
        }
        var paths = (IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [item])!;
        Check(!paths.Any(path => path is "item.bounce" or "item.spin" or "item.face_camera" or "item.3d"),
            "Item options are not timeline channels");
    }

    private static void ItemOptionsTransforms(Fixture fixture)
    {
        var instance = typeof(Timeline).GetField("<Instance>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = instance.GetValue(null);
        var timeline = new Timeline();
        timeline.SetFrameRate(20f);
        void Seek(int frame) => timeline.ImportProjectState(new ProjectTimelineState { CurrentFrame = frame, FrameRate = 20f });
        instance.SetValue(null, timeline);
        try
        {
            var item = new SceneObject
            {
                SpawnCategory = "Items", Position = new vec3(1f, 2f, 3f),
                Rotation = new vec3(.2f, .3f, .4f), Scale = new vec3(2f, 3f, 4f),
                PivotOffset = new vec3(.2f, .5f, 0f)
            };
            var parent = new SceneObject { Position = new vec3(3f, 1f, -2f), Rotation = new vec3(0f, .5f, 0f) };
            parent.AddChild(item);
            var render = typeof(Viewport).GetMethod("GetRenderableWorldMatrixForInstance",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            vec3 camera = new(8f, 9f, 12f);
            mat4 delta = mat4.Translate(new vec3(4f, 0f, -3f));
            mat4 Matrix() => (mat4)render.Invoke(null, [item, camera, delta])!;
            mat4 authored = item.GetWorldMatrix();
            Equal(delta * authored, Matrix(), "Disabled settings preserve the authored transform");
            item.ItemBounce = true;
            Seek(5);
            var bounced = Matrix();
            Near((delta * authored).m31 + .1f, bounced.m31, "Quarter-second bounce is exactly 0.1 world units");
            item.ItemBounce = false;
            item.ItemSpin = true;
            Seek(20);
            mat4 expected = delta * item.GetWorldMatrixNoPivot() * mat4.RotateY(MathF.PI / 2f)
                * mat4.Translate(item.PivotOffset);
            Near((expected * new vec4(vec3.UnitX, 1f)).xyz, (Matrix() * new vec4(vec3.UnitX, 1f)).xyz,
                "Spin completes a quarter turn in one second");
            Seek(55);
            Seek(20);
            Equal(expected, Matrix(), "Seeking is deterministic");
            item.AddMesh(new Mesh(null!));
            item.ItemFaceCamera = true;
            item.ItemBounce = true;
            Seek(5);
            mat4 facing = Matrix();
            item.ItemSpin = false;
            Equal(facing, Matrix(), "Face camera completely overrides spin");
            var pivot = (facing * new vec4(-item.PivotOffset, 1f)).xyz;
            var normal = new vec3(facing.m20, facing.m21, facing.m22).Normalized;
            Near((camera - pivot).Normalized, normal, "Each parented, translated instance faces its camera");
            Near(1f, new vec3(facing.m10, facing.m11, facing.m12).Length / 3f, "Billboarding preserves scale");
            Equal(authored, item.GetWorldMatrix(), "Render motion never mutates authored transforms");
        }
        finally
        {
            instance.SetValue(null, previous);
        }
    }

    private static unsafe void ItemOptionsControls(Fixture fixture)
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
            var item = new SceneObject { SpawnCategory = "Items" };
            var other = new SceneObject { SpawnCategory = "Items" };
            var unrelated = new SceneObject { SpawnCategory = "Primitives" };
            var timeline = new Timeline();
            timeline.ImportProjectState(new ProjectTimelineState { AutoKeyframe = true });
            var panel = new PropertiesPanel { Timeline = timeline };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, item);
            var draw = typeof(PropertiesPanel).GetMethod("DrawItemControls", flags)!;
            var positions = new System.Numerics.Vector2[3];
            void Frame(int click = -1, bool down = false)
            {
                io.AddMousePosEvent(click >= 0 ? positions[click].X : -100f,
                    click >= 0 ? positions[click].Y : -100f);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Item controls");
                var start = ImGui.GetCursorScreenPos();
                float step = ImGui.GetFrameHeightWithSpacing();
                for (int i = 0; i < 3; i++)
                    positions[i] = start + new System.Numerics.Vector2(8f, step * (i + 1) + 8f);
                draw.Invoke(panel, null);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            SelectionManager.Instance.SelectObject(item);
            SelectionManager.Instance.SelectObject(other);
            SelectionManager.Instance.SelectObject(unrelated);
            for (int i = 0; i < 3; i++)
            {
                Frame(i, true);
                Frame(i);
            }
            Check(item.ItemBounce && item.ItemSpin && item.ItemFaceCamera, "Every checkbox can be enabled");
            Check(other.ItemBounce && other.ItemSpin && other.ItemFaceCamera, "Selected items change together");
            Check(!unrelated.ItemBounce && !unrelated.ItemSpin && !unrelated.ItemFaceCamera, "Non-items are unaffected");
            for (int i = 0; i < 3; i++)
            {
                Frame(i, true);
                Frame(i);
            }
            Check(!item.ItemBounce && !item.ItemSpin && !item.ItemFaceCamera, "Every checkbox can be disabled");
            Check(item.Keyframes.Count == 0 && other.Keyframes.Count == 0, "Edits never create keyframes");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void ItemOptionsGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Item options test", null, null);
        Check(window != null, "OpenGL context");
        const string key = "nx-item-options-test";
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            uint texture = gl.GenTexture();
            byte[] pixels = Enumerable.Repeat((byte)255, TerrainAtlas.TileSize * TerrainAtlas.TileSize * 4).ToArray();
            TerrainAtlas.Textures.Add(key, texture);
            TerrainAtlas.TilePixels.Add(key, pixels);
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Gl = gl, Viewport = viewport };
            var item = spawn.SpawnItemObject(key, ItemAtlasSource.BlockAtlas, true, true, true, true)!;
            try
            {
                Check(item.ItemBounce && item.ItemSpin && item.ItemFaceCamera, "Spawn carries all checkbox choices");
                var mesh = (ExtrudedItemMesh)item.Visuals.Single();
                int extrudedCount = mesh.Vertices.Count;
                var pivot = item.PivotOffset;
                mesh.Set3D(false);
                Check(!mesh.Is3D && mesh.Vertices.All(vertex => vertex.z == 0f), "3D off rebuilds flat geometry");
                var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(item);
                Check(!snapshot.ItemIs3D, "Flat mode is serialized");
                using var cloned = mesh.Clone();
                Check(cloned is ExtrudedItemMesh { Is3D: false }, "Duplicates retain editable item geometry");
                ((ExtrudedItemMesh)cloned).Set3D(true);
                Check(!mesh.Is3D, "Duplicate edits are independent");
                mesh.Set3D(true);
                Equal(extrudedCount, mesh.Vertices.Count, "3D on restores the complete hull");
                Check(mesh.Vertices.Any(vertex => vertex.z != 0f), "3D on has actual depth");
                Equal(texture, mesh.TextureId, "Geometry edits preserve texture");
                Equal(pivot, item.PivotOffset, "Geometry edits preserve custom pivot");
                typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject",
                    BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [item, snapshot, null]);
                Check(!mesh.Is3D, "Undo restores live flat geometry");
            }
            finally
            {
                foreach (var mesh in item.Visuals) mesh.Dispose();
                TerrainAtlas.Textures.Remove(key);
                TerrainAtlas.TilePixels.Remove(key);
                gl.DeleteTexture(texture);
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
