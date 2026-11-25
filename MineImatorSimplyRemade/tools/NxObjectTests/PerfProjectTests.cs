using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void ReportInstancingDiagnostics(HashSet<Mesh> meshes)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var eligible = typeof(Mesh).GetProperty("IsInstanceable", flags)!;
        int instanceable = meshes.Count(m => (bool)eligible.GetValue(m)!);
        Console.WriteLine($"Instancing diagnostics: {instanceable}/{meshes.Count} meshes instanceable");
        Console.WriteLine("  alpha modes: " + string.Join(", ", meshes.GroupBy(m => $"{m.AlphaMode}/alpha{(m.Alpha * m.BaseAlpha >= 1f ? "=1" : "<1")}/translucent={m.IsTranslucent}/blend={m.BlendMode}")
            .Select(g => $"{g.Key}: {g.Count()}")));
        Console.WriteLine("  ineligible reasons: " + string.Join(", ", new (string, Func<Mesh, bool>)[]
        {
            ("skinned", m => m.IsSkinned), ("shapeKeys", m => m.HasShapeKeys), ("gpuBend", m => m.GpuBend != null),
            ("wind", m => m.WindActive), ("anim", m => !string.IsNullOrEmpty(m.AnimationKey)),
            ("shader", m => m.CustomShader is { Enabled: true }), ("depthTestOff", m => m.DepthTestDisabled),
            ("pickOnly", m => m.PickOnly), ("portal", m => m.EndPortalSurface), ("textMask", m => m.IsTextAlphaMask),
            ("glint", m => m.GlintMode != MineImatorSimplyRemadeNuxi.core.objs.MaterialGlintMode.None),
        }.Select(r => $"{r.Item1}={meshes.Count(r.Item2)}")));
        {
            var list = meshes.Where(m => (bool)eligible.GetValue(m)!).Take(200).ToList();
            var isInstanceable = (Func<Mesh, bool>)Delegate.CreateDelegate(typeof(Func<Mesh, bool>), eligible.GetMethod!);
            var canInstance = (Func<Mesh, Mesh, bool>)Delegate.CreateDelegate(typeof(Func<Mesh, Mesh, bool>),
                typeof(Mesh).GetMethod("CanInstanceWith", flags)!);
            var geometryKey = (Func<Mesh, object?>)Delegate.CreateDelegate(typeof(Func<Mesh, object?>),
                typeof(Mesh).GetProperty("InstanceGeometryKey", flags)!.GetMethod!);
            void Measure(string name, Action<Mesh, Mesh> action)
            {
                for (int i = 0; i < list.Count; i++) action(list[i], list[(i + 1) % list.Count]);
                long before = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                const int rounds = 50;
                for (int r = 0; r < rounds; r++)
                    for (int i = 0; i < list.Count; i++) action(list[i], list[(i + 1) % list.Count]);
                double calls = rounds * list.Count;
                Console.WriteLine($"  {name}: {sw.Elapsed.TotalMilliseconds * 1000 / calls:F3} us/call, {(GC.GetAllocatedBytesForCurrentThread() - before) / calls:F1} B/call");
            }
            Measure("IsInstanceable", (a, _) => isInstanceable(a));
            Measure("InstanceGeometryKey", (a, _) => geometryKey(a));
            Measure("CanInstanceWith", (a, b) => canInstance(a, b));
            Measure("CanInstanceWith(same)", (a, _) => canInstance(a, a));
        }
        var byGeometry = meshes.GroupBy(m =>
        {
            var hash = new HashCode();
            hash.Add(m.Vertices.Count);
            foreach (var v in m.Vertices) { hash.Add(v.x); hash.Add(v.y); hash.Add(v.z); }
            foreach (var v in m.TexCoords) { hash.Add(v.x); hash.Add(v.y); }
            return hash.ToHashCode();
        }).OrderByDescending(g => g.Count()).Take(4).ToList();
        foreach (var group in byGeometry)
        {
            var list = group.ToList();
            var first = list[0];
            var canInstance = typeof(Mesh).GetMethod("CanInstanceWith", flags)!;
            int same = list.Skip(1).Count(m => (bool)canInstance.Invoke(first, [m])!);
            var differing = new Dictionary<string, int>();
            foreach (var other in list.Skip(1))
            {
                foreach (var property in typeof(Mesh).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0 || property.SetMethod is not { IsPublic: true }) continue;
                    object? a = property.GetValue(first), b = property.GetValue(other);
                    if (a is System.Collections.IEnumerable and not string) continue;
                    if (!Equals(a, b)) differing[property.Name] = differing.GetValueOrDefault(property.Name) + 1;
                }
                foreach (var field in typeof(Mesh).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.IsInitOnly) continue;
                    object? a = field.GetValue(first), b = field.GetValue(other);
                    if (a is System.Collections.IEnumerable and not string) continue;
                    if (!Equals(a, b)) differing[field.Name] = differing.GetValueOrDefault(field.Name) + 1;
                }
            }
            Console.WriteLine($"  group of {list.Count}: instanceable-with-first {same}; instanceable {list.Count(m => (bool)eligible.GetValue(m)!)}; differing members: {string.Join(", ", differing.Select(p => $"{p.Key}={p.Value}"))}");
        }
    }

    private static void ReportGeometryDuplication(HashSet<Mesh> meshes)
    {
        var groups = new Dictionary<int, int>();
        int skinned = 0, shapeKeys = 0, indexed = 0;
        foreach (var mesh in meshes)
        {
            if (mesh.BoneNames.Count > 0) skinned++;
            if (mesh.HasShapeKeys) shapeKeys++;
            if (mesh.Indices is { Length: > 0 }) indexed++;
            var hash = new HashCode();
            hash.Add(mesh.Vertices.Count);
            foreach (var v in mesh.Vertices) { hash.Add(v.x); hash.Add(v.y); hash.Add(v.z); }
            foreach (var v in mesh.Normals) { hash.Add(v.x); hash.Add(v.y); hash.Add(v.z); }
            foreach (var v in mesh.TexCoords) { hash.Add(v.x); hash.Add(v.y); }
            if (mesh.Indices != null) foreach (var i in mesh.Indices) hash.Add(i);
            int key = hash.ToHashCode();
            groups[key] = groups.GetValueOrDefault(key) + 1;
        }
        var sorted = groups.Values.OrderByDescending(v => v).ToList();
        Console.WriteLine($"Geometry: {meshes.Count} meshes, {groups.Count} unique, skinned {skinned}, shape-keyed {shapeKeys}, indexed {indexed}; largest groups: {string.Join(", ", sorted.Take(10))}");
        Console.WriteLine($"Geometry: meshes in groups of 2+: {sorted.Where(v => v > 1).Sum()}");
    }

    /// <summary>
    /// Restores a saved project read-only and renders timeline frames through the preview viewport,
    /// printing wall-time per frame and the FrameProfiler stage breakdown.
    /// Usage: --perf-project "path\to\project.nxProj" [frames] [width] [height] [startFrame]
    /// </summary>
    private static unsafe void PerfProject(string projectPath, int frames, uint width, uint height, int startFrame)
    {
        if (Directory.Exists(projectPath))
            projectPath = Path.Combine(projectPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath)) + ".nxProj");
        projectPath = Path.GetFullPath(projectPath);
        var sw = Stopwatch.StartNew();
        if (!ProjectManager.Instance.LoadProject(projectPath))
            throw new InvalidDataException("ProjectManager could not open the project.");
        var manifest = ProjectManager.Instance.Manifest;
        Console.WriteLine($"Project opened in {sw.Elapsed.TotalSeconds:F2}s");


        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        if (!glfw.Init()) throw new InvalidOperationException("GLFW initialization failed.");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Perf project", null, null);
        if (window == null) throw new InvalidOperationException("OpenGL context creation failed.");
        glfw.SwapInterval(0);
        var meshes = new HashSet<Mesh>();
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var timelineProperty = typeof(Timeline).GetProperty(nameof(Timeline.Instance))!;
        var previousTimeline = Timeline.Instance;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            BlockRegistry.Initialize();
            CharacterRegistry.Initialize();
            TerrainAtlas.Initialize(gl);
            ItemsAtlas.Initialize(gl);

            var properties = new PropertiesPanel();
            properties.LoadProjectSettingsFromManifest(manifest);
            var viewport = new Viewport { Gl = gl, PropertiesPanel = properties, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            sw.Restart();
            var timeline = new Timeline { Viewport = viewport };
            // Restore roots one by one so a single broken asset does not abort the benchmark scene.
            var restoreNode = typeof(ProjectSceneSerializer).GetMethod("RestoreNode", BindingFlags.Static | BindingFlags.NonPublic)!;
            int skipped = 0;
            foreach (var root in manifest.SceneObjects)
            {
                try { restoreNode.Invoke(null, [root, viewport, spawn, null, default(GlmSharp.vec3), null]); }
                catch (TargetInvocationException ex)
                {
                    skipped++;
                    Console.WriteLine($"SKIPPED root '{root.Name}': {ex.InnerException?.Message}");
                }
            }
            timeline.ImportProjectState(manifest.Timeline);
            Console.WriteLine($"Restored {manifest.SceneObjects.Count - skipped}/{manifest.SceneObjects.Count} roots");
            Console.WriteLine($"Scene restored in {sw.Elapsed.TotalSeconds:F2}s");

            IEnumerable<SceneObject> Objects(SceneObject root)
            {
                yield return root;
                foreach (var child in root.Children)
                    foreach (var obj in Objects(child)) yield return obj;
            }
            var all = viewport.SceneObjects.SelectMany(Objects).ToList();
            foreach (var obj in all) meshes.UnionWith(obj.Visuals);
            Console.WriteLine($"Objects: {all.Count}, meshes: {meshes.Count}, vertices: {meshes.Sum(m => (long)m.Vertices.Count):N0}, max frame: {timeline.MaxFrames}");

            ReportGeometryDuplication(meshes);
            ReportInstancingDiagnostics(meshes);
            Mesh.InstancingEnabled = Environment.GetEnvironmentVariable("NX_NO_INSTANCING") == null;
            Mesh.InstancingReorder = Environment.GetEnvironmentVariable("NX_REORDER") != null;
            Mesh.BatchStateReuse = Environment.GetEnvironmentVariable("NX_NO_STATE_REUSE") == null;
            Console.WriteLine($"Instancing: {(Mesh.InstancingEnabled ? "on" : "off")}, reorder {(Mesh.InstancingReorder ? "on" : "off")}, batch state reuse {(Mesh.BatchStateReuse ? "on" : "off")}");
            var sceneCamera = all.OfType<CameraSceneObject>().FirstOrDefault() ?? new CameraSceneObject();
            var camera = all.OfType<CameraSceneObject>().Any() ? sceneCamera.ViewCamera : viewport.Camera;
            var playhead = typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int maxFrame = Math.Max(1, timeline.MaxFrames);
            // NX_RENDERED=1 renders the Rendered view (shadows, lighting passes) instead of the plain preview.
            bool rendered = Environment.GetEnvironmentVariable("NX_RENDERED") != null;
            // The timeline is registered and marked as playing, like during playback in the editor, so shadow
            // maps are not reused and animated textures advance. NX_PAUSED=1 measures the paused editor instead.
            bool playing = Environment.GetEnvironmentVariable("NX_PAUSED") == null;
            timelineProperty.SetValue(null, timeline);
            typeof(Timeline).GetField("_isPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, playing);
            Console.WriteLine($"Render quality: {(rendered ? "rendered" : "preview")}, timeline {(playing ? "playing" : "paused")}");
            long keyframeTicks = 0;
            void Render(int frame)
            {
                playhead.SetValue(timeline, Math.Clamp(frame, 0, maxFrame));
                long started = Stopwatch.GetTimestamp();
                timeline.ApplyKeyframesAtCurrentFrame();
                keyframeTicks += Stopwatch.GetTimestamp() - started;
                preview.RenderScenePublic(camera, sceneCamera, width, height, highQuality: rendered);
            }

            sw.Restart();
            Render(startFrame);
            gl.Finish();
            Console.WriteLine($"First frame (cold): {sw.Elapsed.TotalMilliseconds:F1} ms");
            for (int i = 1; i <= 3; i++) { Render(startFrame + i); gl.Finish(); }

            var profiler = FrameProfiler.Shared;
            bool wasEnabled = profiler.Enabled;
            profiler.StartCapture(1, "Perf project");
            var times = new List<double>();
            keyframeTicks = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int gc0 = GC.CollectionCount(0);
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    var frameTimer = Stopwatch.StartNew();
                    profiler.BeginFrame();
                    Render(startFrame + 4 + i);
                    gl.Finish();
                    profiler.EndFrame();
                    times.Add(frameTimer.Elapsed.TotalMilliseconds);
                }
            }
            finally { profiler.StopCapture(); profiler.Enabled = wasEnabled; }
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / Math.Max(1, frames);
            times.Sort();
            double Pct(double p) => times[Math.Min(times.Count - 1, (int)(p * times.Count))];
            Console.WriteLine($"PERF {width}x{height}, {frames} frames from {startFrame}: mean {times.Average():F2} ms, p50 {Pct(.5):F2}, p95 {Pct(.95):F2}, max {times[^1]:F2}; {bytes:N0} bytes/frame, {GC.CollectionCount(0) - gc0} gen0 GCs");
            Console.WriteLine($"  Keyframe application      {keyframeTicks * 1000.0 / Stopwatch.Frequency / frames,8:F2} ms/frame (included in the frame time above)");
            var summary = profiler.GetCaptureSummary();
            foreach (var stage in summary.Stages.OrderByDescending(s => s.MeanMs).Take(20))
                Console.WriteLine($"  {stage.Stage,-24} {stage.MeanMs,8:F2} ms  calls {stage.MeanCalls,8:F1}  {stage.MeanAllocatedBytes,12:N0} B");
            foreach (var counter in summary.Counters.Where(c => c.MeanValue > 0))
                Console.WriteLine($"  counter {counter.Counter,-26} mean {counter.MeanValue,10:F1} max {counter.MaxValue}");
        }
        finally
        {
            foreach (var mesh in meshes) mesh.Dispose();
            timelineProperty.SetValue(null, previousTimeline);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
