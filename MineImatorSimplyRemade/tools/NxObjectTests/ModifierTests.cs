using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static vec3 InstanceOrigin(ModifierEvaluation evaluation, SceneObject obj, int index) =>
        (evaluation.GetDeltas(obj)[index] * obj.GetWorldMatrixNoPivot() * new vec4(0, 0, 0, 1)).xyz;

    private static void ModifierTransforms(Fixture f)
    {
        var obj = new SceneObject { Name = "Array", Position = new(3, 4, 5), PivotOffset = new(0, 0.5f, 0) };
        var child = new SceneObject { Name = "Child", Position = vec3.UnitY };
        obj.AddChild(child);
        var original = obj.GetWorldMatrix();
        foreach (var axis in Enum.GetValues<ArrayAxis>())
        {
            obj.Modifiers = [new() { Count = 4, Axis = axis, Distance = 2f }];
            var evaluation = new ModifierEvaluation([obj]);
            Equal(4, evaluation.GetDeltas(obj).Count, "Count includes original");
            vec3 direction = axis switch { ArrayAxis.X => vec3.UnitX, ArrayAxis.Y => vec3.UnitY, _ => vec3.UnitZ };
            for (int i = 0; i < 4; i++)
            {
                Near(obj.Position + direction * (2f * i), InstanceOrigin(evaluation, obj, i), "Axis spacing");
                Near(obj.Position + vec3.UnitY + direction * (2f * i), InstanceOrigin(evaluation, child, i), "Child copies");
            }
            Equal(original, obj.GetWorldMatrix(), "Non-destructive source");
            Equal(1, obj.Children.Count, "No generated tree objects");
            Equal(0, child.Modifiers.Count, "No child settings mutation");
        }
        obj.Rotation = new(0, 0, MathF.PI / 2f);
        obj.Scale = new(2, 3, 4);
        obj.Modifiers = [new() { Count = 3, Distance = 2, RotationZ = 90, ScaleX = 0.5f, ScaleY = 0.5f, ScaleZ = 0.5f }];
        var rotated = new ModifierEvaluation([obj]);
        Near(obj.Position + vec3.UnitY * 4, InstanceOrigin(rotated, obj, 2), "Local direction independent of source scale");
        mat4 last = rotated.GetDeltas(obj)[2] * obj.GetWorldMatrixNoPivot();
        Near(0.5f, new vec3(last.m00, last.m01, last.m02).Length, "Compounded scale");
        vec3 lastDirection = (last * new vec4(1, 0, 0, 0)).xyz.Normalized;
        Near(-vec3.UnitY, lastDirection, "Incremental rotation in degrees");
        obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = false };
        Equal(1, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Disabled array includes only source");
        obj.Scale = vec3.Zero;
        obj.Modifiers[0] = new() { Count = 2 };
        Check(float.IsFinite(InstanceOrigin(new ModifierEvaluation([obj]), obj, 1).LengthSqr), "Zero-scale source remains finite");
    }

    private static void ModifierPaths(Fixture f)
    {
        var path = StraightPath();
        path.Position = new(8, 2, 3);
        var obj = new SceneObject { Position = new(90, 20, 30), PivotOffset = vec3.Zero };
        var child = new SceneObject { Position = vec3.UnitZ };
        obj.AddChild(child);
        foreach (var axis in Enum.GetValues<ArrayAxis>())
        {
            obj.Modifiers = [new() { Axis = axis, Count = 4, Distance = 2, PathObjectId = path.ObjectId }];
            var evaluation = new ModifierEvaluation([obj, path]);
            for (int i = 0; i < 4; i++)
                Near(path.Position + vec3.UnitY * (2 * i), InstanceOrigin(evaluation, obj, i), "Copies start at path origin");
            vec3 direction = axis switch { ArrayAxis.X => vec3.UnitX, ArrayAxis.Y => vec3.UnitY, _ => vec3.UnitZ };
            mat4 model = evaluation.GetDeltas(obj)[2] * obj.GetWorldMatrixNoPivot();
            Near(vec3.UnitY, (model * new vec4(direction, 0)).xyz.Normalized, "Selected axis aligns to tangent");
            Equal(4, evaluation.GetDeltas(child).Count, "Path repeats subtree");
        }
        obj.Modifiers = [new() { Axis = ArrayAxis.Y, Count = 4, Distance = 7, PathObjectId = path.ObjectId }];
        Near(path.Position + vec3.UnitY * 10, InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 3), "Open path clamps");
        path.Settings = path.Settings with { Closed = true };
        var closed = new ModifierEvaluation([obj, path]);
        Near((path.GetWorldMatrixNoPivot() * new vec4(PathGeometry.AtDistance(path.Samples, 21, true).Position, 1)).xyz,
            InstanceOrigin(closed, obj, 3), "Closed path wraps");
        obj.Modifiers[0] = obj.Modifiers[0] with { Distance = -3 };
        Near((path.GetWorldMatrixNoPivot() * new vec4(PathGeometry.AtDistance(path.Samples, -3, true).Position, 1)).xyz,
            InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 1), "Negative spacing wraps");
        path.Settings = path.Settings with { Closed = false };
        obj.Modifiers[0] = obj.Modifiers[0] with { Count = 2, Distance = 2, ScaleX = 0.5f, ScaleY = 0.5f, ScaleZ = 0.5f };
        foreach (PathPointSceneObject point in path.Children) point.PathPointScale = 2;
        var scaled = new ModifierEvaluation([obj, path]);
        mat4 second = scaled.GetDeltas(obj)[1] * obj.GetWorldMatrixNoPivot();
        Near(1f, new vec3(second.m00, second.m01, second.m02).Length, "Path-point and per-copy scale combine");
        path.Children[1].Position += vec3.UnitX;
        var edited = new ModifierEvaluation([obj, path]);
        Check((InstanceOrigin(edited, obj, 1) - InstanceOrigin(scaled, obj, 1)).Length > 0.1f, "Path edits reevaluate copies");
        path.Children[1].Position -= vec3.UnitX;
        Near(InstanceOrigin(scaled, obj, 1), InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 1), "Path seek is deterministic");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = "missing" };
        Equal(1, new ModifierEvaluation([obj, path]).GetDeltas(obj).Count, "Deleted path inactive");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = path.ObjectId };
        obj.AddChild(path);
        Equal(1, new ModifierEvaluation([obj]).GetDeltas(obj).Count, "Descendant path inactive");
        obj.RemoveChild(path);
        foreach (var point in path.Children) point.Position = vec3.Zero;
        Equal(1, new ModifierEvaluation([obj, path]).GetDeltas(obj).Count, "Degenerate path inactive");
    }

    private static void ModifierStacks(Fixture f)
    {
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        obj.Modifiers = [new() { Count = 3, Distance = 2 }, new() { Count = 2, Axis = ArrayAxis.Y, Distance = 3 }];
        var child = new SceneObject { Position = vec3.UnitZ, PivotOffset = vec3.Zero };
        obj.AddChild(child);
        var evaluation = new ModifierEvaluation([obj]);
        Equal(6, evaluation.GetDeltas(obj).Count, "Stack multiplication");
        Near(new vec3(4, 3, 0), InstanceOrigin(evaluation, obj, 5), "Two-dimensional grid");
        Near(new vec3(4, 3, 1), InstanceOrigin(evaluation, child, 5), "Stack subtree placement");
        child.Modifiers = [new() { Count = 3, Axis = ArrayAxis.Z }];
        Equal(18, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Child arrays compose with parent");
        obj.Modifiers = [new() { Count = 1000 }, new() { Count = 1000 }];
        Equal(1000, new ModifierEvaluation([obj]).GetDeltas(obj).Count, "Stack safety cap");
        Equal(1000, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Inherited safety cap");
        foreach (var invalid in new SceneModifier[]
        {
            new() { Count = 0 }, new() { Count = 1001 }, new() { Axis = (ArrayAxis)9 },
            new() { Type = (SceneModifierType)9 }, new() { Distance = float.NaN },
            new() { RotationX = float.PositiveInfinity }, new() { ScaleY = float.NaN }
        })
        {
            bool rejected = false;
            try { invalid.Validated(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid settings rejected");
        }
        obj.Modifiers = [new() { Count = 1000, ScaleX = 2 }];
        bool overflow = false;
        try { new ModifierEvaluation([obj]).GetDeltas(obj); } catch (InvalidDataException) { overflow = true; }
        Check(overflow, "Transform overflow reported explicitly");
        obj.Modifiers = [new() { Count = 2, RotationZ = 90 }, new() { Count = 2, Distance = 2 }];
        Near(new vec3(1, 2, 0), InstanceOrigin(new ModifierEvaluation([obj]), obj, 3), "Earlier rotations affect later arrays");
        obj.Modifiers.Reverse();
        Near(new vec3(3, 0, 0), InstanceOrigin(new ModifierEvaluation([obj]), obj, 3), "Reordering changes evaluation order");
    }

    private static void ModifierPersistence(Fixture f)
    {
        var root = new SceneObject { Name = "Root" };
        var path = StraightPath();
        var source = new SceneObject { Name = "Source" };
        source.Modifiers = [new() { Count = 4, Axis = ArrayAxis.Z, Distance = -2, RotationX = 15,
            RotationY = 30, RotationZ = 45, ScaleX = 0.8f, ScaleY = 1.1f, ScaleZ = -1, PathObjectId = path.ObjectId },
            new() { Enabled = false, Count = 3 }];
        root.AddChild(path); root.AddChild(source);
        var (manifest, _) = f.Roundtrip(root);
        Equal(source.Modifiers[0], manifest.Root.Children[1].Modifiers![0], "Source-generated archive settings");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        Equal(imported.Children[0].ObjectId, imported.Children[1].Modifiers[0].PathObjectId, "Imported internal path remaps");
        var cloneLibrary = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var library = (ProjectSceneObjectEntry)cloneLibrary.Invoke(null, [manifest.Root])!;
        Check(!ReferenceEquals(library.Children[1].Modifiers, manifest.Root.Children[1].Modifiers), "Independent library stack");
        Equal(source.Modifiers[0], library.Children[1].Modifiers![0], "Library settings");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copied = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Equal(copied.Children[0].ObjectId, copied.Children[1].Modifiers[0].PathObjectId, "Duplicate path remaps");
        copied.Children[1].Modifiers[0] = copied.Children[1].Modifiers[0] with { Count = 9 };
        Equal(4, imported.Children[1].Modifiers[0].Count, "Duplicate changes do not alter original");
        var (standalone, _) = f.Roundtrip(source);
        Equal("", standalone.Root.Modifiers![0].PathObjectId, "Standalone clears external references");
        var capture = ProjectSceneSerializer.SerializeObjectForLibrary(source);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { source }])!;
        ulong initial = Hash();
        source.Modifiers[0] = source.Modifiers[0] with { Count = 8 };
        Check(Hash() != initial, "Modifier changes dirty fingerprint");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [source, capture, null]);
        Equal(initial, Hash(), "Undo snapshot restores modifiers");
        source.Modifiers.Reverse();
        Check(Hash() != initial, "Stack ordering affects dirty fingerprint");
    }

    private static void ModifierRenderCollection(Fixture f)
    {
        var viewport = new Viewport();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        var mesh = new Mesh(null!);
        mesh.Vertices.Add(vec3.Zero);
        mesh.BoundingSphereRadius = 0.1f;
        obj.Visuals.Add(mesh);
        obj.Modifiers = [new() { Count = 4, Axis = ArrayAxis.X, Distance = 2 }];
        viewport.SceneObjects.Add(obj);
        var items = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var noAo = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var overlays = new List<(mat4, Mesh)>();
        var mode = typeof(Viewport).GetNestedType("SceneRenderMode", BindingFlags.NonPublic)!;
        var collect = typeof(Viewport).GetMethod("CollectRenderPairs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        collect.Invoke(viewport, [viewport.SceneObjects, new vec3(0, 0, 10), Enum.Parse(mode, "Rendered"),
            Frustum.FromViewProj(mat4.Ortho(-20, 20, -20, 20, -20, 20)), items, noAo, overlays, null]);
        Equal(4, items.Count + noAo.Count, "Real viewport collects each instance");
        for (int i = 0; i < items.Count; i++)
            Near(new vec3(2 * i, 0, 0), (items[i].Item1 * new vec4(0, 0, 0, 1)).xyz, "Render item placement");
        var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int before = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
        obj.Modifiers[0] = obj.Modifiers[0] with { Distance = 3 };
        typeof(Viewport).GetMethod("ResetModifierEvaluation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, null);
        int after = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
        Check(before != after, "Changed copies invalidate shadow cache");
        var plane = new SceneObject { ObjectType = "Plane", SpawnCategory = "Primitives",
            PrimitivePlaneFaceCamera = true, PivotOffset = vec3.Zero };
        plane.Visuals.Add(mesh);
        plane.Modifiers = [new() { Count = 2, Distance = 10 }];
        mat4 delta = new ModifierEvaluation([plane]).GetDeltas(plane)[1];
        vec3 camera = new(0, 0, 10);
        mat4 billboard = (mat4)typeof(Viewport).GetMethod("GetRenderableWorldMatrixForInstance",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [plane, camera, delta])!;
        Near(new vec3(10, 0, 0), (billboard * new vec4(0, 0, 0, 1)).xyz, "Billboard instance position");
        Near(new vec3(-10, 0, 10).Normalized, (billboard * new vec4(0, 0, 1, 0)).xyz.Normalized,
            "Each billboard faces camera from its own instance position");
    }

    private static unsafe void ModifierGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Array smoke test", null, null);
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
            const uint size = 256;
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var source = spawn.SpawnPrimitiveObject("Cube", "Array")!;
            source.Position = new(-3, 0, 0);
            source.Scale = new(0.5f);
            source.PivotOffset = vec3.Zero;
            source.Modifiers = [new() { Count = 4, Distance = 2 }];
            var mesh = source.Visuals.Single();
            var bone = new BoneSceneObject { BoneName = "arrayBone", Position = new(0, 0.25f, 0), PivotOffset = vec3.Zero };
            source.AddChild(bone);
            mesh.BoneNames.Add(bone.BoneName);
            mesh.BoneInverseBindMatrices.Add(mat4.Identity);
            foreach (var vertex in mesh.Vertices)
            {
                mesh.BoneIndices.Add(new ivec4(0));
                mesh.BoneWeights.Add(new vec4(1, 0, 0, 0));
            }
            mesh.Upload();
            var path = StraightPath();
            path.Position = source.Position;
            path.Rotation = new(0, 0, -MathF.PI / 2f);
            viewport.SceneObjects.Add(path);
            mat4 view = mat4.Translate(new vec3(0, 0, -10));
            mat4 projection = mat4.Ortho(-5, 5, -3, 3, 0.1f, 30f);
            var mode = typeof(Viewport).GetNestedType("SceneRenderMode", BindingFlags.NonPublic)!;
            object quality = Enum.Parse(mode, "Rendered");
            object? Call(string name, params object?[] args) =>
                typeof(Viewport).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, args);
            uint Field(string name) => (uint)typeof(Viewport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewport)!;
            using var pick = new Shader(gl);
            pick.CompileShader("pick.vert", "pick.frag");
            using var silhouette = new Shader(gl);
            silhouette.CompileShader("pick.vert", "silhouette.frag");
            using var shadow = new Shader(gl);
            shadow.CompileShader("shadow_depth.vert", "shadow_depth.frag");
            using var pointShadow = new Shader(gl);
            pointShadow.CompileShader("point_shadow_depth.vert", "point_shadow_depth.frag");
            typeof(Viewport).GetField("_pickShader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, pick);
            typeof(Viewport).GetField("_silhouetteShader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, silhouette);
            Call("InitPickFramebuffer", size, size);
            Call("InitSilhouetteFbo", size, size);
            try
            {
                foreach (bool onPath in new[] { false, true })
                {
                    source.Modifiers[0] = source.Modifiers[0] with { PathObjectId = onPath ? path.ObjectId : "", Axis = onPath ? ArrayAxis.Y : ArrayAxis.X };
                    Call("ResetModifierEvaluation");
                    var items = new List<(mat4, Mesh, float, bool, bool, bool)>();
                    var noAo = new List<(mat4, Mesh, float, bool, bool, bool)>();
                    var overlays = new List<(mat4, Mesh)>();
                    Call("CollectRenderPairs", viewport.SceneObjects, new vec3(0, 0, 10), quality,
                        Frustum.FromViewProj(projection * view), items, noAo, overlays, null);
                    Equal(4, items.Count + noAo.Count, "Four GPU draw instances");
                    BindAndClear();
                    foreach (var item in items.Concat(noAo)) item.Item2.Render(item.Item1, view, projection);
                    AssertCopies(ReadColor(), "Scene pixels");
                    BindAndClear();
                    gl.UseProgram(pick.ShaderProgram);
                    Call("RenderPickObjects", new[] { source }, view, projection, null, false);
                    AssertCopies(ReadColor(), "Pick pixels");
                    for (int i = 0; i < 4; i++)
                    {
                        byte[] rgb = new byte[4];
                        fixed (byte* p = rgb)
                            gl.ReadPixels(PixelX(i), (int)size / 2 + 4, 1, 1, Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, p);
                        Equal((int)MathF.Round(source.PickColor.x * 255), (int)rgb[0], "Copy selects source ID");
                        Equal((int)MathF.Round(source.PickColor.y * 255), (int)rgb[1], "Copy selects source ID");
                        Equal((int)MathF.Round(source.PickColor.z * 255), (int)rgb[2], "Copy selects source ID");
                    }
                    SelectionManager.Instance!.ClearSelection();
                    SelectionManager.Instance.SelectObject(source);
                    Call("RenderSilhouettePassGeneric", Field("_silhouetteFbo"), size, size, view, projection);
                    gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, Field("_silhouetteFbo"));
                    gl.ReadBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    AssertCopies(ReadColor(), "Outline pixels");
                    BindAndClear();
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.None);
                    Call("RenderShadowCasters", new[] { source }, projection * view, shadow, quality, null);
                    AssertDepth("Directional shadow");
                    BindAndClear();
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.None);
                    Call("RenderPointShadowCasters", new[] { source }, projection * view, new vec3(0, 0, 10), 30f, pointShadow, quality, null);
                    AssertDepth("Point shadow");
                    var palette = mesh.BoneMatrices ?? throw new InvalidOperationException("Missing skinning palette.");
                    mat4 sourcePalette = source.GetWorldMatrix().Inverse * bone.GetWorldMatrix();
                    Near((sourcePalette * new vec4(0, 0, 0, 1)).xyz,
                        (palette[0] * new vec4(0, 0, 0, 1)).xyz, "Copies retain source-local skinning palette");
                    Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "OpenGL errors");
                }

                void BindAndClear()
                {
                    gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, Field("_pickFbo"));
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    gl.ReadBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    gl.Viewport(0, 0, size, size);
                    gl.Enable(Silk.NET.OpenGL.GLEnum.DepthTest);
                    gl.DepthMask(true);
                    gl.DepthFunc(Silk.NET.OpenGL.GLEnum.Less);
                    gl.Disable(Silk.NET.OpenGL.GLEnum.CullFace);
                    gl.Disable(Silk.NET.OpenGL.GLEnum.Blend);
                    gl.ClearColor(0, 0, 0, 0);
                    gl.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
                }
                byte[] ReadColor()
                {
                    byte[] pixels = new byte[size * size * 4];
                    fixed (byte* p = pixels)
                        gl.ReadPixels(0, 0, size, size, Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, p);
                    return pixels;
                }
                int PixelX(int i) => (int)(((-3f + 2 * i + 0.125f) / 10f + 0.5f) * size);
                void AssertCopies(byte[] pixels, string message)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int occupied = 0;
                        for (int y = (int)size / 2 - 12; y < (int)size / 2 + 12; y++)
                        for (int x = (int)PixelX(i) - 8; x < (int)PixelX(i) + 8; x++)
                        {
                            int offset = (y * (int)size + x) * 4;
                            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0) occupied++;
                        }
                        Check(occupied > 16, $"{message}: copy {i} occupies {occupied} pixels");
                    }
                }
                void AssertDepth(string message)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float depth = 1;
                        gl.ReadPixels(PixelX(i), (int)size / 2 + 4, 1, 1, Silk.NET.OpenGL.GLEnum.DepthComponent, Silk.NET.OpenGL.GLEnum.Float, &depth);
                        Check(depth < 0.99f, $"{message}: copy {i} writes depth ({depth})");
                    }
                }
            }
            finally
            {
                SelectionManager.Instance?.ClearSelection();
                mesh.Dispose();
                gl.DeleteFramebuffer(Field("_pickFbo"));
                gl.DeleteRenderbuffer(Field("_pickRbo"));
                gl.DeleteTexture(Field("_pickColorTex"));
                gl.DeleteFramebuffer(Field("_silhouetteFbo"));
                gl.DeleteTexture(Field("_silhouetteTex"));
                Mesh.SceneUBO = null;
            }
        }
        finally { glfw.DestroyWindow(window); glfw.Terminate(); }
    }
}
