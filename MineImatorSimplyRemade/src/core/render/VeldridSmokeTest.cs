using System.Numerics;
using MineImatorSimplyRemade.core.window;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Standalone verification that the renderer subsystem pass 1 pieces
/// (<see cref="VeldridShaderCache"/>, <see cref="VeldridMesh"/>,
/// <see cref="VeldridBitmapRenderSurface"/>) actually work together end to end:
/// builds a small headless render surface, uploads a unit cube, draws it, and
/// writes the resulting frame to a PNG so the output can be inspected visually
/// instead of just trusting that "it compiled". Invoked via the
/// <c>--veldrid-smoke-test &lt;output.png&gt;</c> command-line switch in <c>main.cs</c>
/// so it can run without needing any Avalonia window (or the not-yet-ported
/// Viewport/panels) at all.
/// </summary>
public static class VeldridSmokeTest
{
    public static int Run(string outputPngPath)
    {
        try
        {
            using var surface = VeldridBitmapRenderSurface.CreateStandalone(320, 240);
            using var mesh = new VeldridMesh(surface.GraphicsDevice);

            using var shadowMap = new VeldridShadowMap(surface.GraphicsDevice, 1024);
            using var pointShadowMap = new VeldridPointShadowMap(surface.GraphicsDevice, 256) { FarPlane = 8f };

            BuildUnitCube(mesh);
            mesh.Upload(surface.OutputDescription);
            mesh.Albedo = new Vector3(0.85f, 0.35f, 0.25f);
            mesh.Unlit = false;

            Vector3 lightDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));
            Matrix4x4 lightSpaceMatrix = VeldridShadowMap.ComputeLightSpaceMatrix(lightDir, Vector3.Zero, extent: 6f, near: 0.1f, far: 20f);

            var sceneData = SceneDataUniforms.Default;
            sceneData.LightSpaceMatrix = lightSpaceMatrix;
            sceneData.LightDir = -lightDir;
            sceneData.MainLightCastsShadows = 1;
            sceneData.UseShadowMap = 1;
            surface.UpdateSceneData(sceneData);

            Vector3 pointLightPos = new Vector3(1.5f, 1.5f, 1.5f);
            var pointLights = new PointLightUniforms();
            pointLights.Set(new[]
            {
                new PointLightEntry(pointLightPos, Range: 8f, Color: new Vector3(0.3f, 0.6f, 1f), Energy: 1.5f,
                    FarPlane: pointShadowMap.FarPlane, ShadowIndex: 0),
            });
            surface.UpdatePointLights(pointLights);

            Matrix4x4 model = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6f)
                             * Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 8f);

            // Directional shadow pass: render the same cube depth-only from the light's POV.
            shadowMap.RenderShadowPass(commandList =>
            {
                mesh.RenderDepthOnly(commandList, model * lightSpaceMatrix, shadowMap.Framebuffer.OutputDescription);
            });

            // Point-light shadow pass: render all 6 cube faces from the point light's POV.
            for (int face = 0; face < 6; face++)
            {
                Matrix4x4 faceViewProj = pointShadowMap.GetFaceViewProjection(face, pointLightPos);
                pointShadowMap.RenderFace(face, commandList =>
                {
                    mesh.RenderPointShadowDepthOnly(commandList, model, faceViewProj, pointLightPos,
                        pointShadowMap.FarPlane, pointShadowMap.FaceFramebuffers[face].OutputDescription);
                });
            }

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 1.2f, 3f), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 4f, surface.Width / (float)surface.Height, 0.1f, 100f);

            var environment = SceneEnvironmentUniforms.Default;
            environment.CameraPosition = new Vector3(0, 1.2f, 3f);
            surface.UpdateEnvironment(environment);

            using var aoPass = new VeldridAmbientOcclusionPass(surface.GraphicsDevice) { Radius = 4f, Strength = 0.8f };
            using var indirectPass = new VeldridIndirectLightingPass(surface.GraphicsDevice) { SampleCount = 12 };
            indirectPass.Resize(surface.Width, surface.Height);

            const float nearPlane = 0.1f, farPlane = 100f;

            var bitmap = surface.RenderFrame(new RgbaFloat(0.08f, 0.08f, 0.1f, 1f), commandList =>
            {
                mesh.Render(commandList, model, view, proj, surface.SceneDataBuffer, surface.PointLightBuffer,
                    surface.EnvironmentBuffer, shadowMap, new VeldridPointShadowMap?[] { pointShadowMap });

                // Screen-space passes 4b/N: AO darkens the just-rendered scene in
                // place; indirect lighting's raw pass writes to its own scratch
                // texture, then its denoise step composites back additively -
                // both need the framebuffer re-bound since RenderRaw switches away.
                aoPass.Render(commandList, surface.DepthTargetView, surface.Width, surface.Height, nearPlane, farPlane, surface.OutputDescription);

                indirectPass.RenderRaw(commandList, surface.ColorTargetView, surface.DepthTargetView, nearPlane, farPlane);
                commandList.SetFramebuffer(surface.Framebuffer);
                indirectPass.CompositeDenoised(commandList, surface.DepthTargetView, nearPlane, farPlane, surface.OutputDescription);
            });

            string? dir = Path.GetDirectoryName(outputPngPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            bitmap.Save(outputPngPath);

            Console.WriteLine($"[VeldridSmokeTest] OK - wrote {outputPngPath} ({surface.Width}x{surface.Height}) using backend {surface.GraphicsDevice.BackendType}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[VeldridSmokeTest] FAILED: " + ex);
            return 1;
        }
    }

    private static void BuildUnitCube(VeldridMesh mesh)
    {
        var faces = new (Vector3 normal, Vector3[] quad)[]
        {
            (new Vector3(0, 0, 1),  new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) }),
            (new Vector3(0, 0, -1), new[] { new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f) }),
            (new Vector3(0, 1, 0),  new[] { new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }),
            (new Vector3(0, -1, 0), new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f) }),
            (new Vector3(1, 0, 0),  new[] { new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f) }),
            (new Vector3(-1, 0, 0), new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }),
        };

        foreach (var (normal, quad) in faces)
        {
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            {
                mesh.Vertices.Add(quad[i]);
                mesh.Normals.Add(normal);
                mesh.TexCoords.Add(Vector2.Zero);
            }
        }
    }
}
