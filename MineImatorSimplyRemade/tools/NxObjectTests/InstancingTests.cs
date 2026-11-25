using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void InstancingGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Instancing regression", null, null);
        Check(window != null, "OpenGL context");
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        bool instancing = Mesh.InstancingEnabled, reorder = Mesh.InstancingReorder, stateReuse = Mesh.BatchStateReuse;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            const uint size = 256;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(size, size);
            var preview = new Viewport
            {
                Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false
            };
            preview.InitPreviewViewport(size, size);
            viewport.PropertiesPanel = new PropertiesPanel
            {
                TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                ColorManagementEnabled = false, AntiAliasingEnabled = false,
                AmbientOcclusionEnabled = false, IndirectLightingEnabled = false
            };
            viewport.Camera.Distance = 14f;
            var camera = new CameraSceneObject();
            var meshes = new List<CubeMesh>();

            vec3 red = new(0.9f, 0.2f, 0.2f), blue = new(0.2f, 0.3f, 0.9f);
            void Build(Func<int, vec3> albedo, int count, Action<CubeMesh, int>? configure = null)
            {
                foreach (var obj in viewport.SceneObjects.ToArray()) viewport.SceneObjects.Remove(obj);
                foreach (var mesh in meshes) mesh.Dispose();
                meshes.Clear();
                for (int i = 0; i < count; i++)
                {
                    var cube = new CubeMesh(gl) { Albedo = albedo(i) };
                    configure?.Invoke(cube, i);
                    meshes.Add(cube);
                    var obj = new SceneObject { Position = new vec3((i - count / 2f) * 1.15f, 0f, 0f) };
                    obj.Visuals.Add(cube);
                    viewport.SceneObjects.Add(obj);
                }
            }
            byte[] Read(uint texture)
            {
                byte[] pixels = new byte[size * size * 4];
                gl.BindTexture(GLEnum.Texture2D, texture);
                fixed (byte* data = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                return pixels;
            }
            (byte[] Pixels, double Draws, double Meshes) Render(bool enabled, bool allowReorder)
            {
                Mesh.InstancingEnabled = enabled;
                Mesh.InstancingReorder = allowReorder;
                var profiler = FrameProfiler.Shared;
                bool wasEnabled = profiler.Enabled;
                profiler.StartCapture(1, "Instancing regression");
                profiler.BeginFrame();
                preview.RenderScenePublic(viewport.Camera, camera, size, size);
                profiler.EndFrame();
                profiler.StopCapture();
                profiler.Enabled = wasEnabled;
                var counters = profiler.GetCaptureSummary().Counters;
                double Counter(ProfileCounter counter) => counters.Where(c => c.Counter == counter).Select(c => c.MeanValue).FirstOrDefault();
                return (Read(preview.ColorTexture), Counter(ProfileCounter.InstancedDrawCalls),
                    Counter(ProfileCounter.InstancedMeshes));
            }
            int Covered(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] == 255);

            // Neighbouring identical meshes merge: [0-3] red, [4] blue, [5-8] red -> two instanced draws.
            Build(i => i == 4 ? blue : red, 9);
            var plain = Render(false, false);
            var batched = Render(true, false);
            Console.WriteLine($"Covered pixels: {Covered(plain.Pixels)}");
            Check(Covered(plain.Pixels) > 800, "The row of cubes is visible");
            Check(plain.Pixels.SequenceEqual(batched.Pixels), "Instanced pixels match per-mesh pixels");
            Equal(0.0, plain.Draws, "Disabled instancing issues no instanced draws");
            Equal(2.0, batched.Draws, "Runs on both sides of a different mesh become two instanced draws");
            Equal(8.0, batched.Meshes, "Eight meshes join instanced draws");

            // Interleaved meshes stay in scene order unless reordering is explicitly enabled.
            Build(i => i % 2 == 0 ? red : blue, 8);
            plain = Render(false, false);
            batched = Render(true, false);
            Check(plain.Pixels.SequenceEqual(batched.Pixels), "Interleaved meshes render identically");
            Equal(0.0, batched.Draws, "Interleaved meshes are not merged without reordering");
            var reordered = Render(true, true);
            Check(plain.Pixels.SequenceEqual(reordered.Pixels), "Reordered opaque instances match per-mesh pixels");
            Equal(2.0, reordered.Draws, "Reordering gathers each color into one instanced draw");

            // A batch reuses the program, material uniforms, culling and vertex array the previous mesh left in
            // place. Pairs of identical neighbours between very different materials, including settings that are
            // reset after every draw (PBR), must produce exactly the pixels of sending all state for every mesh.
            Build(i => i < 6 ? red : blue, 13, (cube, i) =>
            {
                switch (i)
                {
                    case 2 or 3: cube.PbrEnabled = true; cube.Roughness = 0.2f; break;
                    case 4 or 5: cube.Unlit = true; break;
                    case 8 or 9:
                        cube.EmissionEnabled = true;
                        cube.EmissionColor = new vec3(0.9f, 0.7f, 0.1f);
                        cube.EmissionEnergy = 1.5f;
                        break;
                    case 10 or 11: cube.DoubleSided = true; break;
                }
            });
            Mesh.BatchStateReuse = false;
            var fullState = Render(false, false);
            Mesh.BatchStateReuse = true;
            var reusedState = Render(false, false);
            var reusedAgain = Render(false, false);
            Check(Covered(fullState.Pixels) > 800, "The mixed-material row is visible");
            Check(fullState.Pixels.SequenceEqual(reusedState.Pixels), "Reused batch state renders the same pixels");
            Check(fullState.Pixels.SequenceEqual(reusedAgain.Pixels), "Reused batch state is repeatable across batches");

            // Changing a material between frames must not leave the previous frame's uniforms behind.
            meshes[1].Albedo = new vec3(0.1f, 0.8f, 0.2f);
            meshes[7].Roughness = 0.9f;
            var changedReused = Render(false, false);
            Mesh.BatchStateReuse = false;
            var changedFull = Render(false, false);
            Mesh.BatchStateReuse = true;
            Check(!fullState.Pixels.SequenceEqual(changedFull.Pixels), "The edited materials change the image");
            Check(changedFull.Pixels.SequenceEqual(changedReused.Pixels), "Edited materials render the same pixels with reuse");

            // Every setting that reaches the GPU must stop two meshes from sharing a draw.
            Build(_ => red, 2);
            Mesh.InstancingEnabled = true;
            var canInstance = typeof(Mesh).GetMethod("CanInstanceWith", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Can(Mesh a, Mesh b) => (bool)canInstance.Invoke(a, [b])!;
            var (first, second) = (meshes[0], meshes[1]);
            Check(Can(first, second), "Identical meshes can share an instanced draw");
            var ignored = new HashSet<string>
            {
                nameof(Mesh.Indices), nameof(Mesh.BoundingSphereCenter), nameof(Mesh.BoundingSphereRadius),
                nameof(Mesh.BoundsMin), nameof(Mesh.BoundsMax), nameof(Mesh.SortDepth), nameof(Mesh.GpuBend),
                nameof(Mesh.BoneMatrices), nameof(Mesh.CustomShader)
            };
            var untested = new List<string>();
            int tested = 0;
            void Probe(string name, object target, Func<object?> get, Action<object?> set, Type type, bool detectedByMaterial)
            {
                object? original = get();
                var changed = Alternative(original, type, down: false);
                if (changed == null) { untested.Add(name); return; }
                set(changed);
                if (Equals(get(), original))
                {
                    // The setter clamped the change away (e.g. a value already at its maximum); go the other way.
                    set(Alternative(original, type, down: true));
                    if (Equals(get(), original)) { set(original); untested.Add(name + " (setter clamps)"); return; }
                }
                Check(!Can(first, second), $"Changing {name} stops meshes from sharing a draw");
                set(original);
                Check(Can(first, second), $"Restoring {name} allows sharing again");
                tested++;
            }
            foreach (var property in typeof(Mesh).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod is not { IsPublic: true } ||
                    property.SetMethod is not { IsPublic: true } || ignored.Contains(property.Name)) continue;
                Probe(property.Name, second, () => property.GetValue(second), value => property.SetValue(second, value),
                    property.PropertyType, false);
            }
            foreach (var field in typeof(Mesh).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || ignored.Contains(field.Name)) continue;
                Probe(field.Name, second, () => field.GetValue(second), value => field.SetValue(second, value),
                    field.FieldType, false);
            }
            var material = (StandardMaterial)second.SurfaceGetMaterial(0);
            foreach (var field in typeof(StandardMaterial).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Probe("StandardMaterial." + field.Name, material, () => field.GetValue(material),
                    value => field.SetValue(material, value), field.FieldType, true);
            Console.WriteLine($"Instancing state members detected: {tested}; not mutated: {string.Join(", ", untested)}");
            Check(tested > 80, "The mutation check covered the mesh and material settings");
            Check(untested.All(name => name.EndsWith("(setter clamps)")),
                "Every untested setting is only clamped by its setter: " + string.Join(", ", untested));

            // Shape-keyed, skinned or actively bent meshes keep their own draws.
            second.GpuBend = new MineImatorSimplyRemade.core.mdl.mineImator.GpuBendState { Angle = new vec3(10f, 0f, 0f) };
            Check(!Can(first, second), "An actively bent mesh is not instanced");
            second.GpuBend = new MineImatorSimplyRemade.core.mdl.mineImator.GpuBendState();
            Check(Can(first, second), "A bend with a zero angle does not prevent instancing");
            second.GpuBend = null;

            // Geometry that differs in a single vertex is never merged.
            using var different = new CubeMesh(gl);
            different.Vertices[0] = different.Vertices[0] + new vec3(0.01f, 0f, 0f);
            different.Upload();
            Check(!Can(first, different), "Different geometry is not instanced");
        }
        finally
        {
            Mesh.InstancingEnabled = instancing;
            Mesh.InstancingReorder = reorder;
            Mesh.BatchStateReuse = stateReuse;
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    /// <summary>Returns a value of the same type that differs from <paramref name="original"/>, or null.</summary>
    private static object? Alternative(object? original, Type type, bool down)
    {
        if (type == typeof(bool)) return !(bool)original!;
        if (type == typeof(string)) return (string?)original + "x";
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue((Array.IndexOf(values, original) + 1) % values.Length);
        }
        if (type == typeof(float)) return (float)original! + (down ? -0.37f : 0.37f);
        if (type == typeof(int)) return (int)original! + 1;
        if (type == typeof(uint)) return (uint)original! + 1;
        if (type == typeof(byte)) return (byte)((byte)original! ^ 1);
        if (type == typeof(bvec2)) { var flag = (bvec2)original!; return new bvec2(!flag.x, flag.y); }
        if (type == typeof(vec2)) { var v = (vec2)original!; return new vec2(v.x + 0.37f, v.y); }
        if (type == typeof(vec3)) { var v = (vec3)original!; return new vec3(v.x + 0.37f, v.y, v.z); }
        if (type == typeof(vec4)) { var v = (vec4)original!; return new vec4(v.x + 0.37f, v.y, v.z, v.w); }
        if (type == typeof(MaterialColorAdjustments))
        {
            var value = (MaterialColorAdjustments)original!;
            value.MultiplyHsv += new vec3(0.1f);
            return value;
        }
        return null;
    }
}
