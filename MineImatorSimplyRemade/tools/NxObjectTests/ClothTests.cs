using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static bool BakeClothAndWait(Viewport viewport)
    {
        if (!viewport.BakeCloth()) return false;
        WaitForClothBake(viewport);
        return viewport.ClothError == null;
    }

    private static void WaitForClothBake(Viewport viewport)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        int completed = viewport.ClothBakeCompletedFrames;
        while (viewport.IsClothBaking)
        {
            viewport.AdvanceClothBake();
            Check(viewport.ClothBakeCompletedFrames >= completed &&
                viewport.ClothBakeCompletedFrames <= viewport.ClothBakeTotalFrames,
                "Background bake reports monotonic completed-frame progress");
            completed = viewport.ClothBakeCompletedFrames;
            if (timeout.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Cloth bake did not finish.");
            Thread.Sleep(1);
        }
    }

    private static ClothSurface ClothPatch(string id, ClothSettings? settings = null) => new(id, settings ?? new(),
        [new(-1, 0, 1), new(1, 0, 1), new(-1, 0, -1), new(1, 0, -1), new(-1, 0, 1)],
        [0, 2, 1, 1, 2, 3], Enumerable.Range(0, 5).Select(i => new ClothVertex(0, i)).ToArray());

    private static void ClothPinsAndSeams(Fixture f)
    {
        var group = new ClothVertexGroup { Pinned = true, Vertices = [new(0, 0), new(0, 1)] };
        var a = ClothPatch("a", new() { Groups = [group], SelfCollision = false });
        var edgeA = new ClothVertexGroup { Vertices = [new(0, 2), new(0, 3)] };
        var edgeB = new ClothVertexGroup { Vertices = [new(0, 0), new(0, 1)], SeamObjectId = "a", SeamGroupId = edgeA.Id };
        a = a with { Settings = a.Settings with { Groups = [group, edgeA] } };
        var b = ClothPatch("b", new() { Groups = [edgeB], SelfCollision = false });
        var pose = new Dictionary<string, mat4> { ["a"] = mat4.Identity, ["b"] = mat4.Translate(new vec3(0, 0, -2)) };
        var simulation = new ClothSimulation([a, b], pose);
        for (int i = 0; i < 60; i++) simulation.Step(1 / 60f, pose, []);
        var av = simulation.GetWorldVertices(0); var bv = simulation.GetWorldVertices(1);
        Near(a.Vertices[0], av[0], "Fixed pin remains at source position");
        Near(a.Vertices[1], av[1], "Second fixed pin remains stable");
        Check(av[2].y < -0.01f, "Unpinned cloth responds to gravity");
        Near(av[0], av[4], "Coincident UV duplicates share simulation");
        Near(av[2], bv[0], "First seam pair shares exact position");
        Near(av[3], bv[1], "Second seam pair shares exact position");
        pose["a"] = mat4.Translate(vec3.UnitX);
        simulation.Step(1 / 60f, pose, []);
        Near(a.Vertices[0] + vec3.UnitX, simulation.GetWorldVertices(0)[0], "Pin follows animated object");
        Near(simulation.GetWorldVertices(0)[2], simulation.GetWorldVertices(1)[0], "Seam remains welded during pin movement");
        var invalid = b with { Settings = b.Settings with { Groups = [edgeB with { Vertices = [] }] } };
        bool rejected = false;
        try { _ = new ClothSimulation([a, invalid], pose); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Empty seam groups are rejected explicitly");
        var pinnedA = a with { Settings = a.Settings with { Groups = [group, edgeA with { Pinned = true }] } };
        var pinnedB = b with { Settings = b.Settings with { Groups = [edgeB with { Pinned = true }] } };
        simulation = new ClothSimulation([pinnedA, pinnedB], pose);
        vec3 joined = simulation.GetWorldVertices(0)[2];
        Near(joined, simulation.GetWorldVertices(1)[0], "Separated fixed pins snap to one joined rest position");
        mat4 delta = mat4.Translate(vec3.UnitY) * mat4.RotateZ(0.2f);
        pose["a"] = delta * pose["a"]; pose["b"] = delta * pose["b"];
        simulation.Step(1 / 60f, pose, []);
        Near((delta * new vec4(joined, 1)).xyz, simulation.GetWorldVertices(0)[2],
            "Snap-bound pin follows shared rotation and translation from the joined position");
        Near(simulation.GetWorldVertices(0)[2], simulation.GetWorldVertices(1)[0], "Animated snap-bound pins stay welded");
        pose["b"] = mat4.Translate(vec3.UnitX) * pose["b"];
        rejected = false;
        try { simulation.Step(1 / 60f, pose, []); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Independent animation that separates snap-bound fixed pins is rejected");
    }

    private static void ClothUnequalSeams(Fixture f)
    {
        foreach (bool reverse in new[] { false, true })
        foreach (bool denseSource in new[] { false, true })
        foreach (int sparseCount in new[] { 2, 3, 4 })
        {
            var sparseGroup = new ClothVertexGroup
            { Pinned = true, Vertices = Enumerable.Range(0, sparseCount).Select(i => new ClothVertex(0, i)).ToArray() };
            var denseGroup = new ClothVertexGroup
            { Vertices = Enumerable.Range(0, 5).Select(i => new ClothVertex(0, i)).Append(new(0, 5)).ToArray() };
            if (denseSource)
                denseGroup = denseGroup with { SeamObjectId = "sparse", SeamGroupId = sparseGroup.Id, ReverseSeam = reverse };
            else
                sparseGroup = sparseGroup with { SeamObjectId = "dense", SeamGroupId = denseGroup.Id, ReverseSeam = reverse };
            var sparse = new ClothSurface("sparse", new() { Groups = [sparseGroup] },
                Enumerable.Range(0, sparseCount).Select(i => new vec3(i * 4f / (sparseCount - 1), 0, 0)).ToArray(),
                [], sparseGroup.Vertices);
            var dense = new ClothSurface("dense", new() { Groups = [denseGroup], Gravity = 50 },
                Enumerable.Range(0, 5).Select(i => new vec3(reverse ? 4 - i : i, 0, 0))
                    .Append(new(reverse ? 4 : 0, 0, 0)).ToArray(),
                [0, 1, 2, 2, 3, 4], denseGroup.Vertices);
            var pose = new Dictionary<string, mat4> { ["sparse"] = mat4.Identity, ["dense"] = mat4.Identity };
            var simulation = new ClothSimulation([sparse, dense], pose);
            void Verify()
            {
                var positions = simulation.GetWorldVertices(1);
                var edge = simulation.GetWorldVertices(0);
                for (int i = 0; i < 5; i++)
                {
                    float parameter = (reverse ? 4 - i : i) * (sparseCount - 1) / 4f;
                    int segment = Math.Min((int)parameter, sparseCount - 2);
                    Near(vec3.Lerp(edge[segment], edge[segment + 1], parameter - segment), positions[i],
                        "Unequal seam retains interpolated edge positions in either direction");
                    if (i != 0) Check((positions[i] - positions[i - 1]).Length > 0.9f,
                        "Dense seam vertices do not collapse to sparse particles");
                }
                Near(positions[0], positions[5], "Dense seam UV duplicate follows its particle");
            }
            Verify();
            for (int frame = 0; frame < 30; frame++)
            {
                pose["sparse"] = mat4.Translate(new vec3(0, frame * 0.02f, 0));
                simulation.Step(1 / 60f, pose, []);
                Verify();
            }
            var freeSparse = sparse with { Settings = sparse.Settings with
            { Groups = [sparseGroup with { Pinned = false }], Gravity = 0 } };
            pose["sparse"] = mat4.Identity;
            simulation = new ClothSimulation([freeSparse, dense], pose);
            for (int frame = 0; frame < 30; frame++)
            {
                simulation.Step(1 / 60f, pose, []);
                Verify();
            }
            Check(simulation.GetWorldVertices(0)[0].y < -0.01f,
                "Interpolated seams transfer motion back to the unpinned sparse edge");
            var pinnedDense = dense with { Settings = dense.Settings with
            { Groups = [denseGroup with { Pinned = true }] } };
            pose["sparse"] = mat4.Identity;
            _ = new ClothSimulation([sparse, pinnedDense], pose);
            pinnedDense = pinnedDense with { Vertices = dense.Vertices.ToArray(), Triangles = [] };
            pinnedDense.Vertices[1] += vec3.UnitY;
            simulation = new ClothSimulation([sparse, pinnedDense], pose);
            Verify();
            simulation.Step(1 / 60f, pose, []);
            Verify();
            pose["sparse"] = mat4.Translate(vec3.UnitY);
            pose["dense"] = mat4.Translate(vec3.UnitY);
            simulation.Step(1 / 60f, pose, []);
            Verify();
            pose["sparse"] = mat4.Translate(vec3.UnitY * 2);
            bool rejected = false;
            try { simulation.Step(1 / 60f, pose, []); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Animation cannot silently separate snap-bound interpolated pins");
        }
    }

    private static void ClothRegionSeams(Fixture f)
    {
        ClothSurface Region(string id, float[] coordinates, bool pinned, vec3 offset)
        {
            int n = coordinates.Length, count = n * n;
            var vertices = new vec3[count + 1];
            var permutation = Enumerable.Range(0, count).Select(i => (i * (count - 1) + 3) % count).ToArray();
            for (int row = 0; row < n; row++)
                for (int column = 0; column < n; column++)
                {
                    float x = coordinates[column], z = coordinates[row];
                    vertices[permutation[row * n + column]] = new vec3(x, 0.3f * x + 0.2f * z, z) + offset;
                }
            vertices[count] = vertices[permutation[0]];
            var triangles = new List<int>();
            for (int row = 0; row < n - 1; row++)
                for (int column = 0; column < n - 1; column++)
                {
                    int a = permutation[row * n + column], b = permutation[row * n + column + 1];
                    int c = permutation[(row + 1) * n + column], d = permutation[(row + 1) * n + column + 1];
                    triangles.AddRange([a, c, b, b, c, d]);
                }
            var ids = Enumerable.Range(0, count + 1).Select(i => new ClothVertex(0, i)).ToArray();
            return new(id, new() { Groups = [new() { Pinned = pinned, Vertices = ids }], Gravity = 20 },
                vertices, triangles.ToArray(), ids);
        }
        vec3 normal = new vec3(-0.3f, 1, -0.2f).Normalized;
        var coarse = Region("coarse", [0, 0.5f, 1], true, vec3.Zero);
        var fine = Region("fine", [0, 0.15f, 0.75f, 1], false, normal * 0.01f);
        foreach (bool denseSource in new[] { false, true })
        {
            var a = coarse; var b = fine;
            if (denseSource)
                b = b with { Settings = b.Settings with { Groups = [b.Settings.Groups[0] with
                { SeamObjectId = a.ObjectId, SeamGroupId = a.Settings.Groups[0].Id }] } };
            else
                a = a with { Settings = a.Settings with { Groups = [a.Settings.Groups[0] with
                { SeamObjectId = b.ObjectId, SeamGroupId = b.Settings.Groups[0].Id }] } };
            var pose = new Dictionary<string, mat4> { ["coarse"] = mat4.Identity, ["fine"] = mat4.Identity };
            pose["fine"] = mat4.Translate(new vec3(3, 2, 1)) * mat4.RotateY(0.6f) * mat4.RotateZ(-0.3f);
            var simulation = new ClothSimulation([a, b], pose);
            void Verify(vec3 translation)
            {
                var positions = simulation.GetWorldVertices(1);
                for (int i = 0; i < positions.Length; i++)
                    Near(fine.Vertices[i] - normal * 0.01f + translation, positions[i],
                        "Region weld preserves both rows and columns despite shuffled mesh indices");
                for (int i = 0; i < fine.Triangles.Length; i += 3)
                {
                    int x = fine.Triangles[i], y = fine.Triangles[i + 1], z = fine.Triangles[i + 2];
                    float sourceArea = vec3.Cross(fine.Vertices[y] - fine.Vertices[x], fine.Vertices[z] - fine.Vertices[x]).Length;
                    float area = vec3.Cross(positions[y] - positions[x], positions[z] - positions[x]).Length;
                    Check(MathF.Abs(area / sourceArea - 1) < 0.01f, "Region weld retains triangle area instead of flattening the mesh");
                }
            }
            Verify(vec3.Zero);
            for (int frame = 1; frame <= 30; frame++)
            {
                vec3 translation = vec3.UnitY * (frame * 0.01f);
                pose["coarse"] = mat4.Translate(translation);
                simulation.Step(1 / 60f, pose, []);
                Verify(translation);
            }
            var edge = a with { Triangles = [] };
            bool rejected = false;
            try { _ = new ClothSimulation([edge, b], pose); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Welding a whole region to a triangle-free pool is rejected instead of collapsing it");
        }
    }

    private static void ClothFoldedRegionSeams(Fixture f)
    {
        var targetVertices = new List<vec3>();
        var sourceVertices = new List<vec3>();
        var targetTriangles = new List<int>();
        var sourceTriangles = new List<int>();
        for (int row = 0; row < 3; row++)
        {
            float y = row * 0.5f;
            targetVertices.AddRange([new(0, y, 0), new(0, y, 0.1f)]);
            sourceVertices.AddRange([new(0.2f, y, 0), new(0.2f, y, 0.1f), new(0, y, 0.1f)]);
            if (row == 2) continue;
            int a = row * 2;
            targetTriangles.AddRange([a, a + 2, a + 1, a + 1, a + 2, a + 3]);
            for (int column = 0; column < 2; column++)
            {
                int b = row * 3 + column;
                sourceTriangles.AddRange([b, b + 3, b + 1, b + 1, b + 3, b + 4]);
            }
        }
        var targetIds = Enumerable.Range(0, targetVertices.Count).Select(i => new ClothVertex(0, i)).ToArray();
        var sourceIds = Enumerable.Range(0, sourceVertices.Count).Select(i => new ClothVertex(0, i)).ToArray();
        var targetGroup = new ClothVertexGroup { Pinned = true, Vertices = targetIds };
        var sourceGroup = new ClothVertexGroup { Vertices = sourceIds, SeamObjectId = "target", SeamGroupId = targetGroup.Id };
        var target = new ClothSurface("target", new() { Groups = [targetGroup] },
            targetVertices.ToArray(), targetTriangles.ToArray(), targetIds);
        var source = new ClothSurface("source", new() { Groups = [sourceGroup] },
            sourceVertices.ToArray(), sourceTriangles.ToArray(), sourceIds);
        var pose = new Dictionary<string, mat4>
        { ["target"] = mat4.Identity, ["source"] = mat4.Translate(vec3.UnitX) * mat4.RotateY(0.4f) };
        var simulation = new ClothSimulation([target, source], pose);
        void Verify()
        {
            var vertices = simulation.GetWorldVertices(1);
            for (int i = 0; i < vertices.Length; i++)
            {
                Near(0, vertices[i].x, "Folded region snaps onto the target surface with zero gap");
                for (int j = 0; j < i; j++)
                    Check((vertices[i] - vertices[j]).Length > 0.001f,
                        "Additional folded face rows retain distinct positions instead of being clamped together");
            }
            Check(vertices.Max(v => v.y) - vertices.Min(v => v.y) > 0.8f, "Snapped region retains its length");
            Check(vertices.Max(v => v.z) - vertices.Min(v => v.z) > 0.05f, "Snapped region retains transverse detail");
        }
        Verify();
        for (int frame = 0; frame < 10; frame++) { simulation.Step(1 / 60f, pose, []); Verify(); }
    }

    private static unsafe void ClothProjectWelds(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var manifest = System.Text.Json.JsonSerializer.Deserialize(System.IO.File.ReadAllText(projectPath),
            MineImatorSimplyRemade.AppJsonContext.Default.ProjectManifest)
            ?? throw new InvalidDataException("Missing project manifest.");
        IEnumerable<ProjectSceneObjectEntry> Entries(ProjectSceneObjectEntry root)
        {
            yield return root;
            foreach (var child in root.Children)
                foreach (var entry in Entries(child)) yield return entry;
        }
        var roots = manifest.SceneObjects.Where(root => Entries(root).Any(entry =>
            entry.Modifiers?.Any(m => m.Type == SceneModifierType.Cloth && m.Enabled) == true)).ToArray();
        Check(roots.Length != 0, "Project contains enabled cloth");
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Cloth project regression", null, null);
        Check(window != null, "OpenGL context");
        var meshes = new HashSet<Mesh>();
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            var loader = MineImatorLoader.Instance;
            loader.Initialize(gl);
            MineImatorSimplyRemade.BlockRegistry.Initialize();
            MineImatorSimplyRemade.CharacterRegistry.Initialize();
            MineImatorSimplyRemade.TerrainAtlas.Initialize(gl);
            MineImatorSimplyRemade.ItemsAtlas.Initialize(gl);
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var visibleRoots = manifest.SceneObjects.Where(root => !root.IsHidden || roots.Contains(root)).ToList();
            foreach (var root in visibleRoots)
            {
                foreach (var entry in Entries(root))
                    if (!string.IsNullOrEmpty(entry.SourceAssetPath))
                        entry.SourceAssetPath = entry.SourceAssetPath.StartsWith(DataAssetRemapper.ReferencePrefix, StringComparison.OrdinalIgnoreCase)
                            ? DataAssetRemapper.Current.Resolve(entry.SourceAssetPath) ?? throw new FileNotFoundException($"Cannot resolve {entry.SourceAssetPath}.")
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, entry.SourceAssetPath));
            }
            ProjectSceneSerializer.LoadSceneFromManifest(new() { SceneObjects = visibleRoots }, viewport, spawn);
            var timeline = new Timeline { Viewport = viewport };
            timeline.ImportProjectState(manifest.Timeline ?? new() { MaxFrames = 10 });
            IEnumerable<SceneObject> Objects(SceneObject root)
            {
                yield return root;
                foreach (var child in root.Children)
                    foreach (var obj in Objects(child)) yield return obj;
            }
            var cloth = viewport.SceneObjects.SelectMany(Objects)
                .Where(o => o.Modifiers.Any(m => m.Type == SceneModifierType.Cloth && m.Enabled)).ToArray();
            foreach (var obj in viewport.SceneObjects.SelectMany(Objects)) meshes.UnionWith(obj.Visuals);
            int savedCloth = roots.SelectMany(Entries).Count(entry =>
                entry.Modifiers?.Any(m => m.Type == SceneModifierType.Cloth && m.Enabled) == true);
            Equal(savedCloth, cloth.Length, "Project restoration preserves all saved cloth objects and duplicates");
            Check(cloth.Length != 0, "Imported project contains cloth geometry");
            var surfaceMethod = typeof(Viewport).GetMethod("ClothSurfaceFor", BindingFlags.NonPublic | BindingFlags.Static)!;
            var surfaces = cloth.Select(obj => (ClothSurface)surfaceMethod.Invoke(null, [obj])!).ToArray();
            float Area(vec3[] vertices, int[] triangles)
            {
                float area = 0;
                for (int i = 0; i < triangles.Length; i += 3)
                    area += vec3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                        vertices[triangles[i + 2]] - vertices[triangles[i]]).Length;
                return area;
            }
            Check(BakeClothAndWait(viewport), "Project cloth bake succeeds: " + viewport.ClothError);
            for (int s = 0; s < cloth.Length; s++)
            {
                var obj = cloth[s];
                var bake = obj.Modifiers.Single(m => m.Type == SceneModifierType.Cloth && m.Enabled).ClothBake!;
                bake.Validate();
                meshes.UnionWith(obj.Visuals);
                float originalArea = Area(surfaces[s].Vertices, surfaces[s].Triangles);
                for (int frame = 0; frame < bake.Frames.Length; frame++)
                {
                    var positions = Enumerable.Range(0, bake.MeshVertexCounts.Length).SelectMany(m => bake.GetMeshVertices(frame, m)).ToArray();
                    float ratio = Area(positions, surfaces[s].Triangles) / originalArea;
                    Check(ratio > 0.25f && ratio < 4,
                        $"{obj.Name}, frame {frame}: snapped region must retain mesh area without collapsing or exploding (ratio {ratio:F3})");
                }
            }
            Console.WriteLine($"Project cloth: {cloth.Length} objects, {cloth.Sum(o => o.Visuals.Sum(m => m.Vertices.Count))} vertices, {timeline.MaxFrames + 1} baked frames with {visibleRoots.Count} visible scene roots.");
        }
        finally
        {
            foreach (var mesh in meshes) mesh.Dispose();
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static void ClothContacts(Fixture f)
    {
        var point = new ClothSurface("fall", new() { Thickness = 0.02f, Substeps = 8, Damping = 0.05f },
            [new(0, 1, 0)], [], [new(0, 0)]);
        var pose = new Dictionary<string, mat4> { ["fall"] = mat4.Identity, ["floor"] = mat4.Identity };
        var floor = new ClothTriangle(new(-10, 0, -10), new(0, 0, 10), new(10, 0, -10));
        var simulation = new ClothSimulation([point], pose);
        for (int i = 0; i < 100; i++) simulation.Step(1 / 60f, pose, [floor]);
        float y = simulation.GetWorldVertices(0)[0].y;
        Check(y >= 0.019f && y < 0.03f, "Scene triangle contact maintains collision thickness");
        simulation = new ClothSimulation([point], pose);
        IReadOnlyList<ClothTriangle> staticFloor = [floor];
        simulation.Step(1 / 60f, pose, staticFloor);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) simulation.Step(1 / 60f, pose, staticFloor);
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024,
            "Static scene collisions reuse their broad-phase tree without per-step allocations");
        var pinned = new ClothVertexGroup { Pinned = true, Vertices = [new(0, 0), new(0, 1), new(0, 2)] };
        var clothFloor = new ClothSurface("floor", new() { Groups = [pinned], Gravity = 0 },
            [floor.A, floor.B, floor.C], [0, 1, 2], [new(0, 0), new(0, 1), new(0, 2)]);
        simulation = new ClothSimulation([point, clothFloor], pose);
        for (int i = 0; i < 100; i++) simulation.Step(1 / 60f, pose, []);
        y = simulation.GetWorldVertices(0)[0].y;
        Check(y >= 0.019f && y < 0.03f, "Cloth-to-cloth vertex/triangle self-contact prevents crossing");
        Near(floor.A, simulation.GetWorldVertices(1)[0], "Contact cannot move pinned cloth");
        var fast = point with { Settings = point.Settings with { Gravity = 1000, Substeps = 1 } };
        simulation = new ClothSimulation([fast], pose);
        simulation.Step(0.1f, pose, [floor]);
        Check(simulation.GetWorldVertices(0)[0].y >= 0.019f, "Swept vertex collision catches fast plane crossings");
        var stationary = point with { Settings = point.Settings with { Gravity = 0, Substeps = 4 } };
        simulation = new ClothSimulation([stationary], pose);
        var movedFloor = new ClothTriangle(floor.A + vec3.UnitY * 2, floor.B + vec3.UnitY * 2, floor.C + vec3.UnitY * 2);
        simulation.Step(1 / 30f, pose, [movedFloor], [floor]);
        Check(simulation.GetWorldVertices(0)[0].y > 2, "Animated collider carries cloth rather than passing through it");
        simulation = new ClothSimulation([stationary, clothFloor], pose);
        pose["floor"] = mat4.Translate(vec3.UnitY * 2);
        simulation.Step(1 / 30f, pose, []);
        Check(simulation.GetWorldVertices(0)[0].y > 2, "Cached self-contact detects a moving pinned cloth surface");
        pose["floor"] = mat4.Identity;
        var single = new ClothSurface("fall", new() { Groups = [pinned], Thickness = 0.02f },
            [floor.A, floor.B, floor.C, new(0, 1, 0)], [0, 1, 2], [new(0, 0), new(0, 1), new(0, 2), new(0, 3)]);
        simulation = new ClothSimulation([single], pose);
        for (int i = 0; i < 100; i++) simulation.Step(1 / 60f, pose, []);
        Check(simulation.GetWorldVertices(0)[3].y >= 0.019f, "Nonadjacent vertices collide with triangles on the same cloth mesh");
        var edgePositions = new vec3[] { new(-2, 0, 0), new(2, 0, 0), new(0, 0.005f, -2), new(0, 0.005f, 2) };
        var edgeContact = typeof(ClothSimulation).Assembly.GetType(
            "MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.ClothCollisionTree")!
            .GetMethod("CollideEdges", BindingFlags.Static | BindingFlags.Public)!;
        edgeContact.Invoke(null, [new List<(int, int)> { (0, 1), (2, 3) }, edgePositions,
            new float[] { 0, 0, 1, 1 }, Enumerable.Repeat(0.02f, 4).ToArray(),
            Enumerable.Repeat(true, 4).ToArray(), new HashSet<(int, int)>()]);
        Check(edgePositions[2].y >= 0.019f, "Edge/edge self-contact separates crossing edges with distant endpoints");
        var nearPin = new vec3[] { new(0.001445f, 0.0001386f, 0), new(1, 0, 0), new(0, 0, 1), vec3.Zero };
        var initialNearPin = nearPin.ToArray();
        var pointContact = typeof(ClothSimulation).Assembly.GetType(
            "MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.ClothCollisionTree")!
            .GetMethod("CollideClothPoint", BindingFlags.Static | BindingFlags.Public)!;
        pointContact.Invoke(null, [(0, 1, 2, 3), nearPin, initialNearPin, new float[] { 0, 1, 1, 0 }, 0.02f]);
        Check((nearPin[1] - initialNearPin[1]).Length <= 0.020001f,
            "Contact near a pinned triangle corner cannot amplify penetration into a large vertex displacement");
        Check(nearPin[1].y < initialNearPin[1].y, "Near-pin contact still separates movable geometry");
        Near(initialNearPin[0], nearPin[0], "Near-pin contact preserves the fixed point");
        Near(initialNearPin[3], nearPin[3], "Near-pin contact preserves the fixed triangle corner");
        edgePositions = [vec3.Zero, new(1, 0, 0), new(0.0001f, 0.005f, -1), new(0.0001f, 0.005f, 1)];
        edgeContact.Invoke(null, [new List<(int, int)> { (0, 1), (2, 3) }, edgePositions,
            new float[] { 0, 1, 0, 0 }, Enumerable.Repeat(0.02f, 4).ToArray(),
            Enumerable.Repeat(true, 4).ToArray(), new HashSet<(int, int)>()]);
        Check((edgePositions[1] - new vec3(1, 0, 0)).Length <= 0.020001f && edgePositions[1].y < 0,
            "Edge contact near a pinned endpoint separates without amplifying the correction");
        Near(vec3.Zero, edgePositions[0], "Near-pin edge contact preserves its fixed endpoint");
    }

    private static void ClothSceneBroadPhasePerformance(Fixture f)
    {
        const int count = 32768;
        var triangles = new ClothTriangle[count];
        for (int i = 0; i < count; i++)
        {
            float x = ((i * 7919) % count) * 2f;
            triangles[i] = new(new(x - 0.4f, 0, -0.4f), new(x, 0, 0.4f), new(x + 0.4f, 0, -0.4f));
        }
        var point = new ClothSurface("point", new() { Gravity = 0, SelfCollision = false, Thickness = 0.02f },
            [new(count, 0.02f, 0)], [], [new(0, 0)]);
        var pose = new Dictionary<string, mat4> { ["point"] = mat4.Identity };
        var simulation = new ClothSimulation([point], pose);
        simulation.Step(1 / 30f, pose, triangles);
        var profiler = FrameProfiler.Shared;
        bool enabled = profiler.Enabled;
        double visited;
        try
        {
            profiler.StartCapture(1, "Cloth BVH spatial locality after refit");
            profiler.BeginFrame();
            simulation.Step(1 / 30f, pose, triangles);
            profiler.EndFrame();
            profiler.StopCapture();
            visited = profiler.GetCaptureSummary().Counters.Single(c => c.Counter == ProfileCounter.ClothBvhNodesVisited).MeanValue;
        }
        finally { profiler.Enabled = enabled; }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        const int frames = 10;
        for (int i = 0; i < frames; i++) simulation.Step(1 / 30f, pose, triangles);
        timer.Stop();
        Console.WriteLine($"Cloth scene 32,768 shuffled triangles: {timer.Elapsed.TotalMilliseconds / frames:F2} ms/step, {visited:N0} BVH nodes/step");
        Check(visited < 100 * point.Settings.Substeps * point.Settings.Iterations,
            "Refitting must preserve spatially sorted collider identity, not scramble the BVH leaves");
        Near(point.Vertices[0], simulation.GetWorldVertices(0)[0], "Shuffled scene colliders preserve the supported point");
        simulation.Step(1 / 30f, pose, triangles[..^1], triangles);
        Near(point.Vertices[0], simulation.GetWorldVertices(0)[0],
            "Collider-count changes rebuild the tree without indexing a mismatched previous frame");
    }

    private static void ClothTimelineAndGeometry(Fixture f)
    {
        var instance = typeof(Timeline).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        try
        {
            var patch = ClothPatch("a", new()
            {
                Groups = [new() { Pinned = true, Vertices = [new(0, 0), new(0, 1)] }],
                SelfCollision = false,
                Collisions = false
            });
            var obj = new SceneObject { Name = "Timeline cloth", PivotOffset = vec3.Zero };
            obj.AssignObjectId();
            obj.Modifiers = [new() { Type = SceneModifierType.Cloth, Cloth = patch.Settings }];
            var mesh = new Mesh(null!);
            mesh.Vertices.AddRange(patch.Vertices); mesh.Indices = patch.Triangles.Select(i => (uint)i).ToArray();
            mesh.Normals.AddRange(Enumerable.Repeat(vec3.UnitZ, patch.Vertices.Length));
            obj.Visuals.Add(mesh);
            var parent = new SceneObject { Name = "Animated cloth parent", PivotOffset = vec3.Zero };
            parent.AssignObjectId(); parent.AddChild(obj);
            var viewport = new Viewport(); viewport.SceneObjects.Add(parent);
            var timeline = new Timeline { Viewport = viewport };
            instance.SetValue(null, timeline);
            timeline.AddKeyframeForProperty(obj, "position.x", 0);
            obj.SetLocalPosition(vec3.UnitX * 2);
            timeline.AddKeyframeForProperty(obj, "position.x", 30);
            timeline.AddKeyframeForProperty(parent, "rotation.y", 0);
            parent.SetLocalRotation(new vec3(0, 0.3f, 0));
            timeline.AddKeyframeForProperty(parent, "rotation.y", 30);
            var update = typeof(Viewport).GetMethod("UpdateCloth", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void At(int frame)
            {
                typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, frame);
                typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, null);
                update.Invoke(viewport, null);
                Check(viewport.ClothError == null, "Integrated cloth evaluation succeeds: " + viewport.ClothError);
            }
            At(30);
            Check(ReferenceEquals(mesh.Vertices, mesh.RenderVertices), "Unbaked playback never deforms cloth");
            Check(viewport.BakeCloth(), "Explicit cloth bake succeeds: " + viewport.ClothError);
            Check(viewport.IsClothBaking && obj.Modifiers[0].ClothBake == null,
                "Starting a bake returns before publishing frames");
            Equal(0, viewport.ClothBakeCompletedFrames, "No frames complete before the worker starts");
            Equal(timeline.MaxFrames + 1, viewport.ClothBakeTotalFrames, "Progress includes frame zero and the timeline end");
            viewport.AdvanceClothBake();
            var job = typeof(Viewport).GetField("_clothBakeJob", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewport)!;
            var workProperty = job.GetType().GetProperty("Work")!;
            var worker = (Task)workProperty.GetValue(job)!;
            Check(worker != null, "Frame solving is dispatched as a background task");
            var pending = new TaskCompletionSource();
            workProperty.SetValue(job, pending.Task);
            viewport.AdvanceClothBake();
            Equal(0, viewport.ClothBakeCompletedFrames, "UI polling does not block on or count an unfinished worker");
            Equal(30, timeline.CurrentFrame, "Scene sampling restores the playhead before the worker finishes");
            Near(vec3.UnitX * 2, obj.LocalPosition, "Scene sampling restores authored transforms before worker completion");
            workProperty.SetValue(job, worker);
            WaitForClothBake(viewport);
            Check(viewport.ClothError == null, "Background bake completes successfully: " + viewport.ClothError);
            Equal(viewport.ClothBakeTotalFrames, viewport.ClothBakeCompletedFrames, "Completed bake reaches 100 percent");
            Equal(timeline.MaxFrames + 1, obj.Modifiers[0].ClothBake!.Frames.Length, "Bake includes the timeline end");
            var forward = mesh.RenderVertices.ToArray();
            var world = obj.GetWorldMatrix();
            Near((world * new vec4(patch.Vertices[0], 1)).xyz, (world * new vec4(forward[0], 1)).xyz,
                "Animated pin uses sampled object and parent poses");
            Check(forward[2].y < patch.Vertices[2].y, "Rendered geometry uses cloth deformation");
            Check(mesh.Vertices.SequenceEqual(patch.Vertices), "Simulation never edits authored geometry");
            Check(mesh.RenderNormals.All(n => float.IsFinite(n.x) && MathF.Abs(n.Length - 1) < 0.001f), "Deformation recomputes finite normals");
            At(5); At(30);
            for (int v = 0; v < forward.Length; v++) Near(forward[v], mesh.RenderVertices[v], "Backward seek reproduces exported pose");
            Near(vec3.UnitX * 2, obj.LocalPosition, "Sampling restores current authored transform");
            Near(new vec3(0, 0.3f, 0), parent.LocalRotation, "Sampling restores animated parent transform");
            Equal(30, timeline.CurrentFrame, "Sampling restores timeline playhead");
            obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = false };
            update.Invoke(viewport, null);
            Check(ReferenceEquals(mesh.Vertices, mesh.RenderVertices), "Disabling cloth restores source geometry");
            obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = true };
            At(0);
            Near(patch.Vertices[2], mesh.RenderVertices[2], "Frame zero uploads rebuilt source pose instead of stale geometry");
            var getDeltas = new ModifierEvaluation([obj]).GetDeltas(obj);
            Equal(1, getDeltas.Count, "Cloth never creates unintended Array instances");
            var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int before = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
            At(20);
            int after = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
            Check(before != after, "Cloth movement invalidates cached shadows");
            var invalidCollider = new SceneObject { Name = "Unused collider", PivotOffset = vec3.Zero };
            invalidCollider.AssignObjectId();
            var invalidMesh = new Mesh(null!); invalidMesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitZ]);
            invalidMesh.Indices = [100, 1, 2]; invalidCollider.Visuals.Add(invalidMesh);
            viewport.SceneObjects.Add(invalidCollider);
            At(0);
            Check(viewport.ClothError == null, "Disabled scene collision does not evaluate unrelated collider geometry");
            var saved = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
            var context = MineImatorSimplyRemade.AppJsonContext.Default.ProjectSceneObjectEntry;
            string json = System.Text.Json.JsonSerializer.Serialize(saved, context);
            var restored = System.Text.Json.JsonSerializer.Deserialize(json, context)!;
            typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [obj, restored, null]);
            At(30);
            for (int v = 0; v < forward.Length; v++)
                Near(forward[v], mesh.RenderVertices[v], "Reopened project replays the saved bake without solving");
            var oldBake = obj.Modifiers[0].ClothBake;
            obj.Modifiers[0] = obj.Modifiers[0] with { Cloth = obj.Modifiers[0].Cloth with { Collisions = true } };
            Check(!BakeClothAndWait(viewport), "Invalid collider rejects an explicit rebake");
            Check(ReferenceEquals(oldBake, obj.Modifiers[0].ClothBake), "Failed bake preserves previously saved frames");
            obj.Modifiers[0] = obj.Modifiers[0] with
            {
                Cloth = patch.Settings with
                {
                    Groups = [new()
                    {
                        Vertices = [new(0, 0)],
                        SeamObjectId = Guid.NewGuid().ToString("N"),
                        SeamGroupId = Guid.NewGuid().ToString("N")
                    }]
                }
            };
            Check(viewport.BakeCloth(), "Worker-side validation does not block starting the bake");
            WaitForClothBake(viewport);
            Check(viewport.ClothError?.StartsWith("Cloth bake frame 0:", StringComparison.Ordinal) == true,
                "Worker failures surface with their timeline frame");
            Equal(0, viewport.ClothBakeCompletedFrames, "A failed worker does not report a completed frame");
            Check(ReferenceEquals(oldBake, obj.Modifiers[0].ClothBake), "Worker failure preserves previous saved frames");
            obj.Modifiers[0] = obj.Modifiers[0] with { Cloth = patch.Settings };
            viewport.ClearClothBake();
            Check(obj.Modifiers[0].ClothBake == null && ReferenceEquals(mesh.Vertices, mesh.RenderVertices),
                "Clear bake removes saved frames and restores source geometry");
            At(30);
            Check(ReferenceEquals(mesh.Vertices, mesh.RenderVertices), "Cleared cloth stays unsimulated during playback");
        }
        finally { instance.SetValue(null, previous); }
    }

    private static void ClothPersistence(Fixture f)
    {
        var target = new SceneObject { Name = "Seam target" }; target.AssignObjectId();
        var source = new SceneObject { Name = "Seam source" }; source.AssignObjectId();
        var group = new ClothVertexGroup { Name = "Edge", Vertices = [new(0, 1), new(0, 2)], Pinned = true };
        target.Modifiers = [new() { Type = SceneModifierType.Cloth, Cloth = new() { Groups = [group] } }];
        source.Modifiers = [new()
        {
            Type = SceneModifierType.Cloth,
            Cloth = new() { Groups = [group with { Id = Guid.NewGuid().ToString("N"), Pinned = false,
                SeamObjectId = target.ObjectId, SeamGroupId = group.Id }] }
        }];
        var root = new SceneObject { Name = "Cloth assembly" }; root.AddChild(target); root.AddChild(source);
        var (manifest, _) = f.Roundtrip(root);
        var restored = manifest.Root.Children[1].Modifiers![0].Cloth.Groups[0];
        Equal(target.ObjectId, restored.SeamObjectId, "Archive preserves internal seam reference");
        Check(restored.Vertices.SequenceEqual(group.Vertices), "Archive preserves exact vertex IDs");
        var viewport = new Viewport(); var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        Equal(imported.Children[0].ObjectId, imported.Children[1].Modifiers[0].Cloth.Groups[0].SeamObjectId,
            "Import remaps internal seam target");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copy = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Equal(copy.Children[0].ObjectId, copy.Children[1].Modifiers[0].Cloth.Groups[0].SeamObjectId, "Duplication remaps seams");
        var (standalone, _) = f.Roundtrip(source);
        Equal("", standalone.Root.Modifiers![0].Cloth.Groups[0].SeamObjectId, "Standalone archive clears external seams");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { root }])!;
        ulong before = Hash();
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(source);
        source.Modifiers[0] = source.Modifiers[0] with { Cloth = source.Modifiers[0].Cloth with { Gravity = 2 } };
        Check(Hash() != before, "Cloth settings participate in undo dirty detection");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [source, snapshot, null]);
        Equal(before, Hash(), "Undo snapshot restores cloth settings");
    }

    private static void ClothIdlePerformance(Fixture f)
    {
        var instance = typeof(Timeline).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        try
        {
            var viewport = new Viewport();
            var timeline = new Timeline { Viewport = viewport };
            instance.SetValue(null, timeline);
            var patch = ClothPatch("cloth", new() { Gravity = 0, SelfCollision = false });
            var cloth = new SceneObject { PivotOffset = vec3.Zero };
            cloth.AssignObjectId();
            cloth.Modifiers = [new() { Type = SceneModifierType.Cloth, Cloth = patch.Settings }];
            var mesh = new Mesh(null!);
            mesh.Vertices.AddRange(patch.Vertices);
            mesh.Indices = patch.Triangles.Select(i => (uint)i).ToArray();
            cloth.Visuals.Add(mesh);
            viewport.SceneObjects.Add(cloth);
            for (int i = 0; i < 370; i++)
            {
                var collider = new SceneObject { PivotOffset = vec3.Zero };
                collider.AssignObjectId();
                var colliderMesh = new Mesh(null!)
                {
                    GpuBend = new() { Angle = new(30, 0, 0), SegmentAxis = 1,
                        BendStart = 0, BendEnd = 1, BendSize = 1, Plane = true }
                };
                for (int triangle = 0; triangle < 180; triangle++)
                    colliderMesh.Vertices.AddRange([new(0, 0, 10), new(1, 0, 10), new(0, 1, 10)]);
                collider.Visuals.Add(colliderMesh);
                viewport.SceneObjects.Add(collider);
                timeline.AddKeyframeForProperty(collider, "position.x", 0);
            }
            var update = (Action)typeof(Viewport).GetMethod("UpdateCloth", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action), viewport);
            typeof(Timeline).GetField("_maxFrames", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, 10);
            Check(BakeClothAndWait(viewport), "Dense scene bakes explicitly: " + viewport.ClothError);
            for (int i = 0; i < 3; i++) update();
            Check(viewport.ClothError == null, "Dense idle scene initializes: " + viewport.ClothError);
            var rendered = mesh.RenderVertices;
            var cached = cloth.Modifiers[0].ClothBake;
            var timer = new System.Diagnostics.Stopwatch();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            timer.Start();
            const int frames = 20;
            for (int i = 0; i < frames; i++) update();
            timer.Stop();
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / frames;
            Console.WriteLine($"Cloth idle 371 nodes / 199,805 vertices: {timer.Elapsed.TotalMilliseconds / frames:F2} ms/frame, {bytes:N0} bytes/frame");
            Check(bytes < 128 * 1024, "Paused cloth must not resample animation or deform all collider meshes each redraw");
            Check(ReferenceEquals(rendered, mesh.RenderVertices) && ReferenceEquals(cached, cloth.Modifiers[0].ClothBake),
                "Unchanged paused redraws reuse baked and uploaded geometry");
            var playhead = typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (int frame = 1; frame <= 2; frame++) { playhead.SetValue(timeline, frame); update(); }
            var profiler = FrameProfiler.Shared;
            bool profiling = profiler.Enabled;
            profiler.StartCapture(1, "Cloth dense playback");
            allocated = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            try
            {
                for (int frame = 3; frame <= 7; frame++)
                {
                    playhead.SetValue(timeline, frame);
                    profiler.BeginFrame(); update(); profiler.EndFrame();
                }
            }
            finally { profiler.StopCapture(); profiler.Enabled = profiling; }
            timer.Stop();
            bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 5;
            Console.WriteLine($"Cloth playback 371 nodes / 199,805 vertices: {timer.Elapsed.TotalMilliseconds / 5:F2} ms/frame, {bytes:N0} bytes/frame");
            var stages = profiler.GetCaptureSummary().Stages;
            foreach (var stage in stages.Where(s => s.Stage is ProfileStage.ClothColliderCollection or
                ProfileStage.ClothSimulation or ProfileStage.ClothTimelineSampling or ProfileStage.BoneLookup))
                Console.WriteLine($"  {stage.Stage}: {stage.MeanMs:F2} ms/frame, {stage.MeanAllocatedBytes:N0} bytes/frame");
            Check(stages.Where(s => s.Stage is ProfileStage.ClothColliderCollection or ProfileStage.ClothSimulation or
                ProfileStage.ClothTimelineSampling or ProfileStage.BoneLookup).All(s => s.MeanCalls == 0),
                "Baked playback never runs the solver, timeline sampling or collider collection");
            Check(bytes < 128 * 1024, "Baked playback allocations stay independent of collider geometry");
            playhead.SetValue(timeline, 0);
            update();
            mesh.Vertices[2] += vec3.UnitY;
            update();
            Check(viewport.ClothError == null && ReferenceEquals(cached, cloth.Modifiers[0].ClothBake),
                "Source edits never trigger an automatic simulation");
            cloth.Modifiers[0] = cloth.Modifiers[0] with { Cloth = patch.Settings with { Gravity = 1 } };
            update();
            Check(viewport.ClothError != null && ReferenceEquals(mesh.Vertices, mesh.RenderVertices),
                "Changed settings require an explicit rebake and remove stale deformation");
            cloth.Modifiers[0] = cloth.Modifiers[0] with { Cloth = patch.Settings };
            viewport.SceneObjects[1].Visuals[0].GpuBend!.Angle = new(60, 0, 0);
            update();
            timeline.AddKeyframeForProperty(viewport.SceneObjects[1], "position.x", 10);
            update();
            Check(ReferenceEquals(cached, cloth.Modifiers[0].ClothBake),
                "Collider and timeline edits never rebuild cloth in playback");
            playhead.SetValue(timeline, 11);
            update();
            Check(viewport.ClothError != null && ReferenceEquals(mesh.Vertices, mesh.RenderVertices),
                "Frames outside the saved bake report an error rather than simulating");
            playhead.SetValue(timeline, 0);
            timeline.SetFrameRate(60);
            update();
            Check(viewport.ClothError != null, "Frame rate changes require an explicit rebake");
            timeline.SetFrameRate(30);
            mesh.Vertices.Add(vec3.Zero);
            update();
            Check(viewport.ClothError != null, "Topology count changes reject stale baked frames");
        }
        finally { instance.SetValue(null, previous); }
    }

    private static void ClothBentRestShape(Fixture f)
    {
        var instance = typeof(Timeline).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        try
        {
            var obj = BendBone("Bent cloth", vec3.Zero);
            var mesh = new Mesh(null!)
            {
                GpuBend = new() { SegmentAxis = 1, BendStart = 0, BendEnd = 1, BendSize = 1, Plane = true }
            };
            mesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY, vec3.Ones]);
            mesh.Indices = [0, 1, 2, 1, 3, 2];
            mesh.Normals.AddRange(Enumerable.Repeat(vec3.UnitZ, 4));
            obj.Visuals.Add(mesh);
            obj.Modifiers = [new()
            {
                Type = SceneModifierType.Cloth,
                Cloth = new() { Gravity = 0, Collisions = false, SelfCollision = false,
                    Groups = [new() { Pinned = true, Vertices = [new(0, 2)] }] }
            }];
            var viewport = new Viewport(); viewport.SceneObjects.Add(obj);
            var timeline = new Timeline { Viewport = viewport }; instance.SetValue(null, timeline);
            timeline.AddKeyframeForProperty(obj, "bend.x", 0);
            obj.SetEditableBendAngle(new(60, 0, 0));
            timeline.AddKeyframeForProperty(obj, "bend.x", 10);
            typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, 10);
            var build = typeof(MineImatorSimplyRemade.core.graphics.VeldridSceneRenderer)
                .GetMethod("BuildVertices", BindingFlags.Static | BindingFlags.NonPublic)!;
            vec3[] Evaluated() => ((MineImatorSimplyRemade.core.graphics.VeldridVertex[])build.Invoke(null, [mesh])!)
                .Select(v => v.Position).ToArray();
            var bent = Evaluated();
            Check((bent[2] - mesh.Vertices[2]).Length > 0.5f, "Fixture has a visible current bend");
            Check(Viewport.ClothUnsupportedReason(obj) == null, "Bending is supported by cloth");
            var update = typeof(Viewport).GetMethod("UpdateCloth", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void At(int frame)
            {
                typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, frame);
                update.Invoke(viewport, null);
                Check(viewport.ClothError == null, "Bent cloth evaluation succeeds: " + viewport.ClothError);
            }
            Check(BakeClothAndWait(viewport), "Bent cloth bakes explicitly: " + viewport.ClothError);
            At(10);
            for (int i = 0; i < bent.Length; i++)
            {
                Near(bent[i], mesh.RenderVertices[i], "Cloth starts from the current bend, not frame zero");
                Near(bent[i], Evaluated()[i], "CPU renderer does not apply the bend twice");
            }
            At(0); At(10);
            for (int i = 0; i < bent.Length; i++) Near(bent[i], mesh.RenderVertices[i], "Replay retains the captured bend");
            obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = false };
            At(10);
            for (int i = 0; i < bent.Length; i++) Near(bent[i], Evaluated()[i], "Disabling cloth restores bend evaluation");
            Near(vec3.UnitY, mesh.Vertices[2], "Bent cloth preserves authored source geometry");
        }
        finally { instance.SetValue(null, previous); }
    }

    private static void ClothAnimatedColliderSampling(Fixture f)
    {
        var instance = typeof(Timeline).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        try
        {
            var viewport = new Viewport();
            var timeline = new Timeline { Viewport = viewport };
            instance.SetValue(null, timeline);
            var bent = BendBone("Animated bent collider", vec3.Zero);
            var bentMesh = new Mesh(null!)
            {
                GpuBend = new() { SegmentAxis = 1, BendStart = 0, BendEnd = 1, BendSize = 1, Plane = true }
            };
            bentMesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY]);
            bent.Visuals.Add(bentMesh); viewport.SceneObjects.Add(bent);
            var root = new SceneObject { PivotOffset = vec3.Zero };
            root.AssignObjectId();
            var bone = new BoneSceneObject { BoneName = "skin", PivotOffset = vec3.Zero };
            bone.AssignObjectId(); root.AddChild(bone);
            var skinned = new Mesh(null!);
            skinned.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY]);
            skinned.BoneIndices.AddRange([ivec4.Zero, ivec4.Zero, ivec4.Zero]);
            skinned.BoneWeights.AddRange([new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)]);
            skinned.BoneNames.Add("skin"); skinned.BoneInverseBindMatrices.Add(mat4.Identity);
            root.Visuals.Add(skinned); viewport.SceneObjects.Add(root);
            timeline.AddKeyframeForProperty(bent, "bend.x", 0);
            bent.SetEditableBendAngle(new(60, 0, 0));
            timeline.AddKeyframeForProperty(bent, "bend.x", 10);
            timeline.AddKeyframeForProperty(bone, "position.y", 0);
            bone.SetLocalPosition(vec3.UnitY * 2);
            timeline.AddKeyframeForProperty(bone, "position.y", 10);
            var collect = typeof(Viewport).GetMethod("CollectClothColliders", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var sample = typeof(Timeline).GetMethod("SampleClothFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var build = typeof(MineImatorSimplyRemade.core.graphics.VeldridSceneRenderer)
                .GetMethod("BuildVertices", BindingFlags.Static | BindingFlags.NonPublic)!;
            var objects = new SceneObject[] { bent, root, bone };
            foreach (int frame in new[] { 0, 10, 5, 10 })
            {
                Action check = () =>
                {
                    var triangles = (List<ClothTriangle>)collect.Invoke(viewport, [objects, new HashSet<Mesh>()])!;
                    Equal(2, triangles.Count, "Animated collider collection preserves both meshes");
                    var vertices = (MineImatorSimplyRemade.core.graphics.VeldridVertex[])build.Invoke(null, [bentMesh])!;
                    var expectedBent = vertices.Select(v => (bent.GetWorldMatrix() * new vec4(v.Position, 1)).xyz).ToArray();
                    Near(expectedBent[0], triangles[0].A, "Cached bent collider matches renderer vertex A");
                    Near(expectedBent[1], triangles[0].B, "Cached bent collider matches renderer vertex B");
                    Near(expectedBent[2], triangles[0].C, "Bend changes and backward samples refresh cached collider geometry");
                    var expectedSkin = skinned.Vertices.Select(v => (bone.GetWorldMatrix() * new vec4(v, 1)).xyz).ToArray();
                    Near(expectedSkin[0], triangles[1].A, "Animated skinned collider matches bone pose A");
                    Near(expectedSkin[1], triangles[1].B, "Animated skinned collider matches bone pose B");
                    Near(expectedSkin[2], triangles[1].C, "Animated skinned collider matches bone pose C");
                };
                sample.Invoke(timeline, [frame, check]);
            }
            Near(vec3.UnitY * 2, bone.LocalPosition, "Collider sampling restores the authored bone pose");
            Near(new vec3(60, 0, 0), bent.GetEditableBendAngle(), "Collider sampling restores the authored bend");
        }
        finally { instance.SetValue(null, previous); }
    }

    private static unsafe void ClothSelectionInteraction(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance!.ClearSelection();
        try
        {
            var io = ImGui.GetIO(); io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1000, 900); io.DeltaTime = 1 / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = new SceneObject { PivotOffset = vec3.Zero }; obj.AssignObjectId();
            obj.Modifiers = [new() { Type = SceneModifierType.Cloth }];
            var mesh = new Mesh(null!); mesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY, vec3.Zero]);
            mesh.Indices = [0, 1, 2];
            obj.Visuals.Add(mesh);
            var viewport = new Viewport(); viewport.SceneObjects.Add(obj);
            SelectionManager.Instance.SelectObject(obj);
            viewport.SetClothEditObject(obj);
            var min = new System.Numerics.Vector2(20, 40); var size = new System.Numerics.Vector2(600, 600);
            var method = typeof(Viewport).GetMethod("HandleClothVertexInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var project = typeof(Viewport).GetMethod("ProjectClothVertices", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var points = ((IEnumerable<(ClothVertex Vertex, System.Numerics.Vector2 Screen, float Depth)>)
                project.Invoke(viewport, [viewport.Camera, min, size])!).ToArray();
            Check(points.Length == 4, "Cloth vertices project into the viewport");
            void Frame(System.Numerics.Vector2 mouse, bool down = false, bool shift = false, bool ctrl = false)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y);
                io.AddKeyEvent(ImGuiKey.ModShift, shift); io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new(700, 750), ImGuiCond.Always);
                ImGui.Begin("Cloth selection");
                method.Invoke(viewport, [viewport.Camera, min, size, false]);
                ImGui.End(); ImGui.Render();
            }
            Frame(points[0].Screen); Frame(points[0].Screen);
            Frame(points[0].Screen, true); Frame(points[0].Screen);
            Check(viewport.ClothVertexSelection.SetEquals(new ClothVertex[] { new(0, 0), new(0, 3) }),
                "Viewport click selects coincident UV duplicates together");
            Frame(points[1].Screen, true, shift: true); Frame(points[1].Screen, shift: true);
            Check(viewport.ClothVertexSelection.Contains(new(0, 1)), "Shift click adds another vertex");
            Frame(points[0].Screen, true, ctrl: true); Frame(points[0].Screen, ctrl: true);
            Check(!viewport.ClothVertexSelection.Contains(new(0, 0)), "Ctrl click subtracts duplicates");
            Frame(min + new System.Numerics.Vector2(1), true); Frame(min + size - new System.Numerics.Vector2(1));
            Equal(4, viewport.ClothVertexSelection.Count, "Viewport box selection selects all enclosed vertices");
            io.DisplaySize = new(1200, 1800);
            var panel = new PropertiesPanel { Viewport = viewport };
            var draw = typeof(PropertiesPanel).GetMethod("DrawClothModifier", BindingFlags.Instance | BindingFlags.NonPublic)!;
            uint hovered = 0, target = 0;
            void PanelFrame(float x = -100, float y = -100, bool down = false, bool pin = false, string? control = null)
            {
                io.AddMousePosEvent(x, y); io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new(700, 1600), ImGuiCond.Always);
                ImGui.Begin("Cloth modifier controls");
                draw.Invoke(panel, [obj, 0]);
                if (pin)
                {
                    var group = obj.Modifiers[0].Cloth.Groups.Single();
                    ImGui.PushID(group.Id);
                    ImGui.PushID($"{group.Name} ({group.Vertices.Length})");
                    target = ImGui.GetID(Localization.T("text.cloth.pin"));
                    ImGui.PopID(); ImGui.PopID();
                }
                else target = ImGui.GetID(Localization.T(control ?? "text.cloth.create_group"));
                hovered = context.HoveredId;
                ImGui.End(); ImGui.Render();
            }
            void ClickControl(bool pin, string? control = null)
            {
                PanelFrame(pin: pin, control: control); PanelFrame(pin: pin, control: control);
                float found = -1;
                float x = control == "text.cloth.clear_bake"
                    ? 60 + ImGui.CalcTextSize(Localization.T("text.cloth.bake")).X +
                        ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                    : 60;
                for (int y = 40; y < 1500; y += 3)
                {
                    PanelFrame(x, y, pin: pin, control: control);
                    if (hovered == target) { found = y; break; }
                }
                Check(found >= 0, "Cloth modifier control is reachable");
                PanelFrame(x, found, true, pin, control); PanelFrame(x, found, pin: pin, control: control);
            }
            string originalId = obj.ObjectId;
            ClickControl(false);
            Equal(1, obj.Modifiers[0].Cloth.Groups.Length, "Rendered modifier creates named vertex group");
            Equal(4, obj.Modifiers[0].Cloth.Groups[0].Vertices.Length, "Created group captures viewport vertex selection");
            ClickControl(true);
            Check(obj.Modifiers[0].Cloth.Groups[0].Pinned, "Rendered group pin control updates simulation settings");
            ClickControl(false, "text.cloth.bake");
            WaitForClothBake(viewport);
            Check(obj.Modifiers[0].ClothBake != null, "Rendered Bake button produces saved cloth frames");
            ClickControl(false, "text.cloth.clear_bake");
            Check(obj.Modifiers[0].ClothBake == null && ReferenceEquals(mesh.Vertices, mesh.RenderVertices),
                "Rendered Clear Bake button restores unbaked geometry");
            Equal(originalId, obj.ObjectId, "Vertex group edits preserve scene identity");
            ClickControl(false, "text.cloth.bake");
            void PopupFrame(bool expectGeometry = true)
            {
                io.AddMousePosEvent(-100, -100);
                ImGui.NewFrame();
                viewport.DrawClothBakePopup();
                Check(ImGui.IsPopupOpen(Localization.T("text.cloth.bake") + "##ClothBakeProgress"),
                    "Bake progress popup remains open while running and after completion");
                ImGui.Render();
                if (expectGeometry)
                    Check(ImGui.GetDrawData().TotalVtxCount > 0, "Bake popup renders its progress and status controls");
            }
            Check(viewport.IsClothBaking, "Rendered Bake button starts asynchronous work");
            PopupFrame(false); PopupFrame();
            WaitForClothBake(viewport);
            PopupFrame();
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void ClothGraphics(Fixture f)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Cloth GPU regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            Mesh.ShadowsEnabled = false;
            ubo.Upload(mat4.Identity, vec3.Ones, vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 160) { Distance = 6, AutoRotateSpeed = 0 };
            var patch = ClothPatch("cloth", new()
            {
                Groups = [new() { Pinned = true, Vertices = [new(0, 0), new(0, 1)] }],
                SelfCollision = false,
                Collisions = false
            });
            using var mesh = new Mesh(gl, patch.Vertices, null, patch.Triangles.Select(i => (uint)i).ToArray());
            mesh.DoubleSided = true;
            byte[] Render()
            {
                preview.Render([mesh], "cloth", 0, centerMeshes: false);
                byte[] pixels = new byte[160 * 160 * 4];
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                        Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                return pixels;
            }
            byte[] original = Render();
            var pose = new Dictionary<string, mat4> { ["cloth"] = mat4.Identity };
            var simulation = new ClothSimulation([patch], pose);
            for (int i = 0; i < 30; i++) simulation.Step(1 / 60f, pose, []);
            vec3[] positions = simulation.GetWorldVertices(0);
            mesh.SetClothGeometry(positions);
            var deformed = Render();
            int changed = Enumerable.Range(0, 160 * 160).Count(i =>
                original[i * 4] != deformed[i * 4] || original[i * 4 + 1] != deformed[i * 4 + 1] || original[i * 4 + 2] != deformed[i * 4 + 2]);
            Check(changed > 100, $"Cloth deformation changes visible pixels, got {changed}");
            var build = typeof(MineImatorSimplyRemade.core.graphics.VeldridSceneRenderer)
                .GetMethod("BuildVertices", BindingFlags.Static | BindingFlags.NonPublic)!;
            var veldrid = (MineImatorSimplyRemade.core.graphics.VeldridVertex[])build.Invoke(null, [mesh])!;
            for (int i = 0; i < veldrid.Length; i++)
                Near(positions[i], veldrid[i].Position, "Veldrid receives identical simulated geometry");
            mesh.SetClothGeometry(null);
            Check(original.SequenceEqual(Render()), "Disabling cloth restores exact original GPU pixels");
            mesh.SetClothGeometry(positions);
            Check(deformed.SequenceEqual(Render()), "Reapplying cloth reproduces GPU output");
            mesh.SetClothGeometry(null);
            mesh.GpuBend = new() { Angle = new(45, 0, 0), SegmentAxis = 2,
                AxisStart = -1, BendStart = 0, BendEnd = 2, BendSize = 2, Plane = true };
            byte[] bentPixels = Render();
            var bentVertices = (MineImatorSimplyRemade.core.graphics.VeldridVertex[])build.Invoke(null, [mesh])!;
            mesh.SetClothGeometry(bentVertices.Select(v => v.Position).ToArray());
            Check(bentPixels.SequenceEqual(Render()), "OpenGL does not bend captured cloth geometry twice");
            mesh.SetClothGeometry(null);
            Check(bentPixels.SequenceEqual(Render()), "Disabling bent cloth restores exact original pixels");
            ClothLoaderPlanePerformance(gl);
            Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "Cloth GPU upload has no OpenGL errors");
        }
        finally
        {
            Mesh.SceneUBO = null;
            glfw.DestroyWindow(window); glfw.Terminate();
        }
    }

    private static unsafe void ClothLoaderPlanePerformance(Silk.NET.OpenGL.GL gl)
        {
            const int size = 32;
            uint texture = gl.GenTexture();
            try
            {
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, texture);
                byte[] pixels = Enumerable.Repeat((byte)255, size * size * 4).ToArray();
                fixed (byte* pointer = pixels)
                    gl.TexImage2D(Silk.NET.OpenGL.GLEnum.Texture2D, 0, (int)Silk.NET.OpenGL.GLEnum.Rgba,
                        size, size, 0, Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                var loader = new MineImatorLoader(); loader.Initialize(gl);
                var create = typeof(MineImatorLoader).GetMethod("CreateExtrudedPlaneMesh",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                using var mesh = (Mesh)create.Invoke(loader,
                    [vec3.Zero, new vec3(2, 2, 0), 0f, 0f, (float)size, (float)size,
                        size, size, texture, false, false, 0f, vec3.Zero, vec3.Ones, null])!;
                Check(mesh.Vertices.Count > 8000, "Performance fixture uses the loader's dense per-pixel extruded geometry");
                var settings = new ClothSettings
                {
                    Groups = [new() { Pinned = true, Vertices = mesh.Vertices.Select((p, i) => (p, i))
                        .Where(v => v.p.y == 2).Select(v => new ClothVertex(0, v.i)).ToArray() }]
                };
                var surface = new ClothSurface("plane", settings, mesh.Vertices.ToArray(),
                    mesh.Indices!.Select(i => (int)i).ToArray(),
                    Enumerable.Range(0, mesh.Vertices.Count).Select(i => new ClothVertex(0, i)).ToArray());
                var pose = new Dictionary<string, mat4> { ["plane"] = mat4.Identity };
                var simulation = new ClothSimulation([surface], pose);
                for (int i = 0; i < 30; i++) simulation.Step(1 / 30f, pose, []);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                const int frames = 30;
                for (int i = 0; i < frames; i++) simulation.Step(1 / 30f, pose, []);
                timer.Stop();
                allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / frames;
                Console.WriteLine($"Cloth loader 32x32 plane: {timer.Elapsed.TotalMilliseconds / frames:F2} ms/frame, {allocated:N0} allocated bytes/frame");
                Check(allocated < 512 * 1024, "Dense cloth steps reuse collision trees instead of allocating them every iteration");
                var result = simulation.GetWorldVertices(0);
                foreach (var vertex in settings.Groups[0].Vertices)
                    Near(surface.Vertices[vertex.Vertex], result[vertex.Vertex], "Dense plane pins remain fixed");
                Check(result.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), "Dense loader plane remains finite");
                ClothCpuBentPlane(gl, texture);
            }
            finally { gl.DeleteTexture(texture); }
    }

    private static void ClothCpuBentPlane(Silk.NET.OpenGL.GL gl, uint texture)
                {
                    var instance = typeof(Timeline).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
                    object? previous = instance.GetValue(null);
                    var meshes = new HashSet<Mesh>();
                    try
                    {
                        MineImatorLoader.Instance.Initialize(gl);
                        var shape = new MiShape { Type = "plane", ThreeD = true, Bend = true, From = [0, 0, 0], To = [8, 8, 0] };
                        var bone = BendBone("CPU-bent extruded cloth", new(45, 0, 0));
                        bone.RegisterShapeData(new()
                        {
                            PartName = "plane", ShapeIndex = 0, Shape = shape, Model = new() { TextureSize = [32, 32] },
                            TextureId = texture, AccumulatedScale = vec3.Ones, ModelBendStyle = BendStyle.Realistic
                        });
                        bone.RegenerateMeshes(false); meshes.UnionWith(bone.Visuals);
                        var viewport = new Viewport(); viewport.SceneObjects.Add(bone);
                        var timeline = new Timeline { Viewport = viewport }; instance.SetValue(null, timeline);
                        timeline.AddKeyframeForProperty(bone, "bend.x", 0);
                        bone.SetEditableBendAngle(new(70, 0, 0)); meshes.UnionWith(bone.Visuals);
                        timeline.AddKeyframeForProperty(bone, "bend.x", 10);
                        var mesh = bone.Visuals.Single();
                        Check(mesh.GpuBend == null && mesh.Vertices.Count > 100, "Fixture uses the loader's CPU-bent 3D plane path");
                        var bent = mesh.Vertices.ToArray();
                        bone.Modifiers = [new() { Type = SceneModifierType.Cloth,
                            Cloth = new() { Gravity = 0, Collisions = false, SelfCollision = false } }];
                        var update = typeof(Viewport).GetMethod("UpdateCloth", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        void At(int frame)
                        {
                            typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, frame);
                            update.Invoke(viewport, null);
                            Check(viewport.ClothError == null, "CPU-bent plane evaluation succeeds: " + viewport.ClothError);
                        }
                        Check(BakeClothAndWait(viewport), "CPU-bent cloth bakes explicitly: " + viewport.ClothError);
                        At(10); At(10); At(0); At(10);
                        Check(ReferenceEquals(mesh, bone.Visuals.Single()), "Cloth sampling does not rebuild CPU-bent loader meshes");
                        for (int i = 0; i < bent.Length; i++) Near(bent[i], mesh.RenderVertices[i], "CPU-bent cloth retains its current rest shape");
                        bone.Modifiers[0] = bone.Modifiers[0] with { Enabled = false };
                        At(10); meshes.UnionWith(bone.Visuals);
                        var restored = bone.Visuals.Single();
                        Check(ReferenceEquals(restored.Vertices, restored.RenderVertices), "Disabling CPU-bent cloth restores authored rendering");
                        for (int i = 0; i < bent.Length; i++) Near(bent[i], restored.Vertices[i], "Deferred bend regeneration restores the current bend");
                        bone.Modifiers[0] = bone.Modifiers[0] with { Enabled = true };
                        bone.SetEditableBendAngle(new(90, 0, 0));
                        Check(BakeClothAndWait(viewport), "Rebaking uses the edited CPU bend: " + viewport.ClothError);
                        meshes.UnionWith(bone.Visuals);
                        var changed = bone.Visuals.Single();
                        Check(changed.Vertices.Where((p, i) => (p - bent[i]).Length > 0.001f).Any(),
                            "Explicit rebake refreshes a deferred CPU bend instead of reusing its old rest shape");
                        for (int i = 0; i < changed.Vertices.Count; i++)
                            Near(changed.Vertices[i], changed.RenderVertices[i], "Rebake captures the edited CPU-bent rest shape");
                        viewport.ClearClothBake();
                        bone.SetEditableBendAngle(new(45, 0, 0)); meshes.UnionWith(bone.Visuals);
                        Check(!ReferenceEquals(changed, bone.Visuals.Single()) &&
                            ReferenceEquals(bone.Visuals.Single().Vertices, bone.Visuals.Single().RenderVertices),
                            "Unbaked cloth does not suppress ordinary CPU bend editing");
                    }
                    finally
                    {
                        instance.SetValue(null, previous);
                        foreach (var mesh in meshes) mesh.Dispose();
                    }
                }
}
