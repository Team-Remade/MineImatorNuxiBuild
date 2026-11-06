using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static PathSceneObject StraightPath()
    {
        var path = new PathSceneObject { Name = "Path", Settings = new() { Closed = false, Smooth = false } };
        path.AssignObjectId();
        path.AddChild(new PathPointSceneObject { Name = "Start", Position = vec3.Zero });
        path.AddChild(new PathPointSceneObject { Name = "Middle", Position = vec3.UnitY });
        path.AddChild(new PathPointSceneObject { Name = "End", Position = vec3.UnitY * 10f });
        return path;
    }

    private static void PathSampling(Fixture f)
    {
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var path = (PathSceneObject)spawn.SpawnPrimitiveObject("Path", "Path")!;
        Equal(1, viewport.SceneObjects.Count, "Only the path is a root");
        Equal(4, path.Children.Count, "Four default control points");
        Check(path.Children.All(p => p is PathPointSceneObject && p.Parent == path && !string.IsNullOrEmpty(p.ObjectId)), "Typed, identified, parented points");
        var samples = path.Samples;
        Near(samples[0].Position, samples[^1].Position, "Closed seam");
        Near(samples[0].Normal, samples[^1].Normal, "Closed frame seam");
        Check(path.Length > 8f, "Default loop has length");
        var straight = StraightPath();
        Near(10f, straight.Length, "Uneven control-point arc length");
        for (int i = 0; i <= 10; i++)
            Near(vec3.UnitY * i, PathGeometry.AtDistance(straight.Samples, i, false).Position, "Constant distance sampling");
        var first = straight.Children[0];
        straight.MoveChild(first, 2);
        Check(straight.Children[^1] == first && first.Parent == straight, "Reordering preserves parent");
        Check(straight.Length > 10, "Reordering changes path");
        path.Settings = path.Settings with { Smooth = false };
        Check(path.Length < samples[^1].Distance, "Linear polygon is shorter than smooth loop");
    }

    private static void PathMeshes(Fixture f)
    {
        var path = StraightPath();
        var mesh = new Mesh(null!);
        foreach (var shape in new[] { PathShape.Flat, PathShape.Tube })
        foreach (bool smoothSegments in new[] { false, true })
        foreach (bool smoothRing in new[] { false, true })
        foreach (bool invert in new[] { false, true })
        {
            var settings = path.Settings with { Shape = shape, Radius = 0.5f, Sides = 8,
                SmoothSegments = smoothSegments, SmoothRing = smoothRing, Invert = invert, MappedTexture = true };
            PathGeometry.Populate(mesh, path.Samples, settings);
            Equal((path.Samples.Count - 1) * (shape == PathShape.Flat ? 1 : 8) * 6, mesh.Vertices.Count, "Triangle count");
            Equal(mesh.Vertices.Count, mesh.Normals.Count, "Normal count");
            Equal(mesh.Vertices.Count, mesh.TexCoords.Count, "UV count");
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Near(1f, mesh.Normals[i].Length, "Normalized shading");
                Check(float.IsFinite(mesh.Vertices[i].LengthSqr), "Finite geometry");
                Check(mesh.TexCoords[i].x is >= 0 and <= 1 && mesh.TexCoords[i].y is >= 0 and <= 1, "Mapped UV range");
                Near(0.5f, new vec3(mesh.Vertices[i].x, 0, mesh.Vertices[i].z).Length, "Radius");
            }
            for (int i = 0; i < mesh.Vertices.Count; i += 3)
                Check(vec3.Dot(vec3.Cross(mesh.Vertices[i + 1] - mesh.Vertices[i], mesh.Vertices[i + 2] - mesh.Vertices[i]),
                    mesh.Normals[i]) > 0f, "Winding agrees with shading normals");
        }
        PathGeometry.Populate(mesh, path.Samples, path.Settings);
        Equal(0, mesh.Vertices.Count, "None has no rendered geometry");
        PathGeometry.Populate(mesh, path.Samples, path.Settings, pickOnly: true);
        Check(mesh.PickOnly && mesh.Vertices.Count > 0, "None retains a pick proxy");
        var point = (PathPointSceneObject)path.Children[0];
        point.PathPointScale = 2f; point.PathPointAngle = 90f;
        var sample = path.Samples[0];
        Near(2f, sample.Scale, "Control-point scale");
        Check(MathF.Abs(vec3.Dot(sample.Normal, vec3.UnitX)) < 0.001f, "Point angle rotates frame");
        PathGeometry.Populate(mesh, path.Samples, path.Settings with { Shape = PathShape.Tube, TextureLength = 2f });
        Near(5f, mesh.TexCoords.Max(uv => uv.y), "Texture repetition in distance units");
    }

    private static void PathLimits(Fixture f)
    {
        var path = StraightPath();
        foreach (var point in path.Children) point.Position = vec3.Zero;
        Equal(0, path.Samples.Count, "Coincident points have no length");
        path.RemoveChild(path.Children[^1]); path.RemoveChild(path.Children[^1]);
        Equal(0, path.Samples.Count, "Single point");
        var points = Enumerable.Range(0, 512)
            .Select(i => new PathControlPoint(new vec3(i, i % 3, 0), 0, 1)).ToArray();
        var settings = new PathSettings { Detail = int.MaxValue, Sides = int.MaxValue, Shape = PathShape.Tube };
        var samples = PathGeometry.Sample(points, settings);
        Check(samples.Length <= PathGeometry.MaxSegments + 1, "Bounded samples");
        var mesh = new Mesh(null!);
        PathGeometry.Populate(mesh, samples, settings);
        Check(mesh.Vertices.Count <= PathGeometry.MaxSegments * 32 * 6, "Bounded geometry");
        Check(PathGeometry.Sample(points.Append(points[0]).ToArray(), settings).Length <= PathGeometry.MaxSegments + 1,
            "Adding or duplicating more control points retains the geometry budget");
        Reject(() => (settings with { Radius = float.NaN }).Validated(), "Invalid radius");
        Reject(() => (settings with { TextureLength = 0 }).Validated(), "Invalid texture length");
        Reject(() => (settings with { Shape = (PathShape)100 }).Validated(), "Invalid shape");
        points[0] = points[0] with { Angle = float.PositiveInfinity };
        Reject(() => PathGeometry.Sample(points, settings), "Invalid point angle");
        Reject(() => PathGeometry.AtDistance(samples, float.NaN, false), "Invalid offset");
    }

    private static void PathFollowing(Fixture f)
    {
        var path = StraightPath();
        path.Position = new(3, 4, 5);
        path.Scale = new(2);
        var follower = new SceneObject { PathFollowObjectId = path.ObjectId, PathFollowOffset = 3 };
        var roots = new List<SceneObject> { follower, path };
        InverseKinematicsSolver.Evaluate(roots);
        Near(new vec3(3, 10, 5), follower.GetWorldPosition(), "Transformed path position");
        Near(vec3.Zero, (follower.GetParentWorldTransform().Inverse * new vec4(follower.GetWorldPosition(), 1f)).xyz,
            "Gizmo movement removes the path edit frame even on root objects");
        follower.Position = new(0, 1, 0);
        InverseKinematicsSolver.Evaluate(roots);
        Near(new vec3(3, 12, 5), follower.GetWorldPosition(), "Authored local offset follows path frame");
        follower.Position = vec3.Zero;
        InverseKinematicsSolver.Evaluate(roots);
        mat4 matrix = follower.GetWorldMatrixNoPivot();
        Near(vec3.UnitY, new vec3(matrix.m10, matrix.m11, matrix.m12).Normalized, "Local Y follows tangent");
        for (int i = 0; i < 10; i++) InverseKinematicsSolver.Evaluate(roots);
        Near(new vec3(3, 10, 5), follower.GetWorldPosition(), "No accumulated transform");
        follower.PathFollowOffset = 100;
        InverseKinematicsSolver.Evaluate(roots);
        Near(new vec3(3, 24, 5), follower.GetWorldPosition(), "Open endpoint clamp");
        follower.PathFollowOffset = -1;
        InverseKinematicsSolver.Evaluate(roots);
        Near(path.Position, follower.GetWorldPosition(), "Open start clamp");
        path.Settings = path.Settings with { Closed = true };
        follower.PathFollowOffset = -1;
        InverseKinematicsSolver.Evaluate(roots);
        var negative = follower.GetWorldPosition();
        follower.PathFollowOffset = path.Length - 1;
        InverseKinematicsSolver.Evaluate(roots);
        Near(negative, follower.GetWorldPosition(), "Negative closed offsets wrap");
        follower.PathFollowObjectId = "missing";
        InverseKinematicsSolver.Evaluate(roots);
        Near(follower.Position, follower.GetWorldPosition(), "Missing target clears evaluated pose");
        follower.PathFollowObjectId = path.ObjectId;
        foreach (var point in path.Children) point.Position = vec3.Zero;
        InverseKinematicsSolver.Evaluate(roots);
        Near(follower.Position, follower.GetWorldPosition(), "Degenerate target clears pose");
    }

    private static void PathDependencies(Fixture f)
    {
        var path = StraightPath();
        var anchor = new SceneObject { Position = new(5, 0, 0) };
        anchor.AssignObjectId();
        path.CopyConstraints.Add(new() { Type = CopyConstraintType.Position, TargetObjectId = anchor.ObjectId });
        var follower = new SceneObject { PathFollowObjectId = path.ObjectId, PathFollowOffset = 2f };
        var child = new SceneObject { Position = vec3.UnitY };
        follower.AddChild(child);
        var roots = new List<SceneObject> { follower, path, anchor };
        InverseKinematicsSolver.Evaluate(roots);
        Near(new vec3(5, 2, 0), follower.GetWorldPosition(), "Path constraints evaluate before following");
        Near(new vec3(5, 3, 0), child.GetWorldPosition(), "Children inherit following");
        follower.CopyConstraints.Add(new() { Type = CopyConstraintType.Position, TargetObjectId = anchor.ObjectId });
        InverseKinematicsSolver.Evaluate(roots);
        Near(anchor.Position, follower.GetWorldPosition(), "Copy constraint follows the path stage");
        follower.CopyConstraints.Clear();
        anchor.PathFollowObjectId = path.ObjectId;
        InverseKinematicsSolver.Evaluate(roots);
        Near(vec3.Zero, follower.GetWorldPosition(), "Constraint/follower cycle is inactive");
        anchor.PathFollowObjectId = "";
        path.CopyConstraints.Clear();
        follower.AddChild(path); roots.Remove(path);
        InverseKinematicsSolver.Evaluate(roots);
        Near(follower.Position, follower.GetWorldPosition(), "Following a descendant is inactive");
    }

    private static void PathPersistence(Fixture f)
    {
        var root = new SceneObject { Name = "Root" };
        var path = StraightPath();
        path.Settings = path.Settings with { Shape = PathShape.Flat, Radius = 0.3f, Invert = true, MappedTexture = true };
        var point = (PathPointSceneObject)path.Children[1];
        point.PathPointAngle = 30; point.PathPointScale = 2;
        point.Keyframes["path_point.angle"] = [new() { Frame = 5, Value = 40f }];
        var follower = new SceneObject { Name = "Follower", PathFollowObjectId = path.ObjectId, PathFollowOffset = 4f };
        follower.Keyframes["path_follow.target"] = [new() { Frame = 0, Value = path.ObjectId }];
        follower.Keyframes["path_follow.offset"] = [new() { Frame = 0, Value = 4f }];
        root.AddChild(path); root.AddChild(follower);
        var (manifest, _) = f.Roundtrip(root);
        Equal(path.Settings, manifest.Root.Children[0].Path, "Source-generated path settings");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        var importedPath = (PathSceneObject)imported.Children[0];
        var importedFollower = imported.Children[1];
        Equal(3, importedPath.Children.Count, "Restore does not append default points");
        Equal(importedPath.ObjectId, importedFollower.PathFollowObjectId, "Internal follow target remaps");
        Equal(importedPath.ObjectId, importedFollower.Keyframes["path_follow.target"][0].Value, "Animated follow target remaps");
        Near(30f, ((PathPointSceneObject)importedPath.Children[1]).PathPointAngle, "Point roll restore");
        Near(2f, ((PathPointSceneObject)importedPath.Children[1]).PathPointScale, "Point scale restore");
        var cloneLibrary = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var library = (ProjectSceneObjectEntry)cloneLibrary.Invoke(null, [manifest.Root])!;
        Equal(path.Settings, library.Children[0].Path, "Library clone settings");
        Equal(path.ObjectId, library.Children[1].PathFollowObjectId, "Library clone references");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copied = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Equal(copied.Children[0].ObjectId, copied.Children[1].PathFollowObjectId, "Duplicate reference remaps");
        Check(copied.Children[0] is PathSceneObject && copied.Children[0].Children[0] is PathPointSceneObject, "Duplicate runtime types");
        var (standalone, _) = f.Roundtrip(follower);
        Equal("", standalone.Root.PathFollowObjectId, "External target cleared from standalone archive");
        Equal("", standalone.Root.Keyframes["path_follow.target"][0].Value.ToString(), "External animated target cleared");
    }

    private static void PathSnapshots(Fixture f)
    {
        var path = StraightPath();
        var capture = ProjectSceneSerializer.SerializeObjectForLibrary(path);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { path }])!;
        ulong initial = Hash();
        path.Settings = path.Settings with { Shape = PathShape.Tube, SmoothRing = false };
        Check(Hash() != initial, "Settings affect dirty detection");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [path, capture, null]);
        Equal(initial, Hash(), "Snapshot restores path settings");
        ((PathPointSceneObject)path.Children[0]).PathPointAngle = 10f;
        Check(Hash() != initial, "Point roll affects dirty detection");
        ((PathPointSceneObject)path.Children[0]).PathPointAngle = 0f;
        path.MoveChild(path.Children[0], 2);
        Check(Hash() != initial, "Order affects dirty detection");
    }

    private static void PathTimeline(Fixture f)
    {
        var path = StraightPath();
        var follower = new SceneObject { PathFollowObjectId = path.ObjectId };
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var discover = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (obj, channel, value) in new (SceneObject, string, object)[]
            { (follower, "path_follow.target", path.ObjectId), (follower, "path_follow.offset", 5f),
              (path.Children[0], "path_point.angle", 30f), (path.Children[0], "path_point.scale", 2f) })
        {
            Check(((IEnumerable<string>)discover.Invoke(null, [obj])!).Contains(channel), "Discover path channel");
            set.Invoke(timeline, [obj, channel, value]);
            Equal(value, get.Invoke(timeline, [obj, channel]), "Read/write path channel");
        }
        var keys = new List<TimelineKeyframe> { new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = 10f } };
        Near(5f, Convert.ToSingle(interpolate.Invoke(timeline, [keys, "path_follow.offset", 5, false])), "Offset interpolation");
        var targets = new List<TimelineKeyframe> { new() { Frame = 0, Value = path.ObjectId }, new() { Frame = 10, Value = "other" } };
        Equal(path.ObjectId, interpolate.Invoke(timeline, [targets, "path_follow.target", 5, false]), "Target is stepped");
        var roots = new List<SceneObject> { follower, path };
        set.Invoke(timeline, [follower, "path_follow.offset", 3f]); InverseKinematicsSolver.Evaluate(roots);
        mat4 original = follower.GetWorldMatrixNoPivot();
        set.Invoke(timeline, [follower, "path_follow.offset", 8f]); InverseKinematicsSolver.Evaluate(roots);
        set.Invoke(timeline, [follower, "path_follow.offset", 3f]); InverseKinematicsSolver.Evaluate(roots);
        Equal(original, follower.GetWorldMatrixNoPivot(), "Backward seek is reproducible");
    }

    private static unsafe void PathGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Path smoke test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            ubo.Upload(mat4.Identity, new vec3(0.1f), vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 160) { Distance = 6f, AutoRotateSpeed = 0f };
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var path = spawn.SpawnPathObject("Path")!;
            try
            {
                byte[] Render()
                {
                    path.EvaluateGeometry();
                    preview.Render([], "path", 0, sceneRoot: path);
                    return ReadPixels();
                }
                byte[] ReadPixels()
                {
                    byte[] pixels = new byte[160 * 160 * 4];
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, preview.ColorTexture);
                    fixed (byte* p = pixels)
                        gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                            Silk.NET.OpenGL.GLEnum.UnsignedByte, p);
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, 0);
                    return pixels;
                }
                preview.Render([], "path", 0);
                byte[] baseline = ReadPixels();
                var loader = new MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader();
                loader.Initialize(gl);
                var imported = loader.CreateSceneFromMiObject(MiDocument(MiPathDocument));
                var importedPath = imported.Children.OfType<PathSceneObject>().Single();
                try
                {
                    Check(importedPath.Visuals.Single().Vertices.Count > 0, "Imported path uploads generated geometry");
                    Check(importedPath.Children.All(p => p.Visuals.Single().PickOnly), "Imported point proxies are editor-only");
                    preview.Render([], "imported-path", 0, sceneRoot: importedPath);
                    byte[] pixels = ReadPixels();
                    int changed = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                        if (pixels[i] != baseline[i] || pixels[i + 1] != baseline[i + 1] || pixels[i + 2] != baseline[i + 2]) changed++;
                    Check(changed > 32, $"Imported path draws visible pixels ({changed})");
                }
                finally
                {
                    foreach (var mesh in imported.GetMeshInstancesRecursively()) mesh.Dispose();
                }
                Check(Render().SequenceEqual(baseline), "None and point proxies are not rendered");
                foreach (var shape in new[] { PathShape.Flat, PathShape.Tube })
                {
                    path.Settings = path.Settings with { Shape = shape };
                    byte[] first = Render();
                    int changed = 0;
                    for (int i = 0; i < first.Length; i += 4)
                        if (first[i] != baseline[i] || first[i + 1] != baseline[i + 1] || first[i + 2] != baseline[i + 2]) changed++;
                    Check(changed > 100, $"Visible {shape} pixels ({changed})");
                    Check(path.Visuals[0].BoundingSphereRadius > 0f, "Uploaded bounds");
                    Check(Render().SequenceEqual(first), "Unchanged geometry/pixels");
                    var point = (PathPointSceneObject)path.Children[0];
                    point.Position += new vec3(0, 2, 0);
                    Check(!Render().SequenceEqual(first), "Point edits rebuild uploaded geometry");
                    point.Position -= new vec3(0, 2, 0);
                    Check(Render().SequenceEqual(first), "Point seek restores pixels");
                }
                path.Settings = path.Settings with { Shape = PathShape.None };
                Check(Render().SequenceEqual(baseline), "Shape-to-None removes rendered geometry");
                Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "OpenGL errors");
            }
            finally
            {
                foreach (var mesh in path.GetMeshInstancesRecursively()) mesh.Dispose();
                Mesh.SceneUBO = null;
            }
        }
        finally { glfw.DestroyWindow(window); glfw.Terminate(); }
    }
}
