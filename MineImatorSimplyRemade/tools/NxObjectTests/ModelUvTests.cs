using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void ModelUvGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Model UV test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var loader = new MineImatorLoader();
            loader.Initialize(gl);
            var create = typeof(MineImatorLoader).GetMethod("CreateBlockMesh",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            int checks = 0;
            foreach (var size in new[] { new vec3(3, 5, 8), new vec3(0, 5, 8),
                new vec3(3, 0, 8), new vec3(3, 5, 0) })
            foreach (var part in Enum.GetValues<BendPart>())
            foreach (bool mirror in new[] { false, true })
            foreach (bool mirrorY in new[] { false, true })
            foreach (bool invert in new[] { false, true })
            {
                int axis = part is BendPart.Right or BendPart.Left ? 0 :
                    part is BendPart.Upper or BendPart.Lower ? 1 : 2;
                if (size[axis] == 0) continue;
                var from = new vec3(0, -size.y, -size.z) / 16f;
                var to = new vec3(size.x, 0, 0) / 16f;
                Mesh Create(BendParams? bend) => (Mesh)create.Invoke(loader,
                    ["uv test", 0, from, to, 24f, 22f, size.x, size.y, size.z,
                        32, 32, mirror, mirrorY, invert, 0f, bend, vec3.Zero,
                        vec3.Zero, vec3.Ones, BendStyle.Realistic, null])!;
                using var ordinary = Create(null);
                using var segmented = Create(new BendParams
                {
                    Part = part, BendSize = 2, ExplicitBendSize = true,
                    BendOffset = (from[axis] + to[axis]) * 8f,
                    AxisY = true
                });
                Check(segmented.Vertices.Count > ordinary.Vertices.Count, "Bend mesh is segmented");
                checks += CheckModelUvs(ordinary, segmented,
                    $"size={size}, part={part}, mirror={mirror}/{mirrorY}, invert={invert}");
            }
            Check(checks > 1000, "All axes, flat blocks, caps and mirror combinations checked");

            string data = Directory.Exists("data") ? "data" : Path.Combine("MineImatorSimplyRemade", "data");
            var allay = loader.LoadModel(Path.GetFullPath(Path.Combine(data, "minecraft", "versions",
                "26.3", "characters", "vanilla", "allay", "allay.mimodel")))
                ?? throw new InvalidOperationException("Bundled Allay model failed to load");
            var wings = allay.Parts[0].Parts.Where(part => part.Name.EndsWith("_wing", StringComparison.Ordinal)).ToArray();
            Equal(2, wings.Length, "Both Allay wings");
            foreach (var wing in wings)
            foreach (var shape in wing.Shapes)
            {
                var bend = BendHelper.ParseBend(wing.Bend, [1f, 1f, 1f], BendStyle.Realistic)
                    ?? throw new InvalidOperationException("Allay wing is missing bend metadata");
                Mesh Create(BendParams? value) => loader.CreateShapeMeshPublic(wing.Name, 0, shape, allay,
                    0, vec3.Ones, value, BendStyle.Realistic, textureSize: allay.TextureSize);
                using var ordinary = Create(null);
                using var segmented = Create(bend);
                checks += CheckModelUvs(ordinary, segmented, wing.Name);
                Check(segmented.GpuBend != null, "Allay retains GPU bending");
                var animatedBend = bend;
                animatedBend.Angle = new vec3(0, 35, 0);
                using var animated = Create(animatedBend);
                Check(segmented.TexCoords.SequenceEqual(animated.TexCoords), "Wing animation preserves UVs");
                Check(animated.GpuBend != null && animated.GpuBend.Angle == animatedBend.Angle,
                    "Wing animation preserves bend angle");
            }
            Equal(GLEnum.NoError, gl.GetError(), "Model UV OpenGL error");
            Console.WriteLine($"PASS Model segmented UVs match ordinary faces ({checks} vertices)");
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static int CheckModelUvs(Mesh ordinary, Mesh segmented, string context)
    {
        int checks = 0;
        for (int i = 0; i < segmented.Vertices.Count; i++)
        {
            var position = segmented.Vertices[i];
            var normal = segmented.Normals[i];
            int face = -1;
            for (int f = 0; f < ordinary.Vertices.Count / 4; f++)
            {
                if ((ordinary.Normals[f * 4] - normal).Length < 1e-5f)
                {
                    face = f * 4;
                    break;
                }
            }
            Check(face >= 0, "Matching ordinary face");
            var origin = ordinary.Vertices[face];
            var horizontal = ordinary.Vertices[face + 1] - origin;
            var vertical = ordinary.Vertices[face + 3] - origin;
            if (horizontal.Length < 1e-6f || vertical.Length < 1e-6f) continue;
            var offset = position - origin;
            float u = vec3.Dot(offset, horizontal) / vec3.Dot(horizontal, horizontal);
            float v = vec3.Dot(offset, vertical) / vec3.Dot(vertical, vertical);
            var expected = ordinary.TexCoords[face] +
                (ordinary.TexCoords[face + 1] - ordinary.TexCoords[face]) * u +
                (ordinary.TexCoords[face + 3] - ordinary.TexCoords[face]) * v;
            Check((segmented.TexCoords[i] - expected).Length < 1e-5f,
                $"UV mismatch: {context}, vertex={i}, expected={expected}, actual={segmented.TexCoords[i]}");
            checks++;
        }
        Check(checks > 0, $"{context} has non-degenerate faces");
        return checks;
    }
}
