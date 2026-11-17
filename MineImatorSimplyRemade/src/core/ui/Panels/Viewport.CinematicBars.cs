using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private Shader? _cinematicBarsShader;

    private void ApplyCinematicBarsPass(uint sceneFbo, uint width, uint height, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 || sceneFbo == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (!effect.Enabled || effect.Type != CameraEffectType.CinematicBars)
                continue;
            var settings = effect.CinematicBars;
            foreach (var control in CinematicBarsSettings.Controls)
                if (!float.IsFinite(settings.GetValue(control.Key)))
                    throw new InvalidDataException($"Cinematic bars {control.Key} must be finite.");
            if (settings.Size <= 0f)
                continue;
            if (_cinematicBarsShader == null)
            {
                var shader = new Shader(Gl);
                shader.CompileShader("edge.vert", "cinematic_bars.frag");
                Gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                {
                    shader.Dispose();
                    throw new InvalidOperationException("Cinematic bars shader failed to link.");
                }
                _cinematicBarsShader = shader;
            }
            bool depthTest = Gl.IsEnabled(GLEnum.DepthTest);
            bool cullFace = Gl.IsEnabled(GLEnum.CullFace);
            bool blend = Gl.IsEnabled(GLEnum.Blend);
            Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.Disable(GLEnum.DepthTest);
            Gl.Disable(GLEnum.CullFace);
            Gl.Disable(GLEnum.Blend);
            Gl.Viewport(0, 0, width, height);
            Gl.UseProgram(_cinematicBarsShader.ShaderProgram);
            Gl.Uniform2(_cinematicBarsShader.GetUniformLocation("uResolution"), (float)width, (float)height);
            Gl.Uniform1(_cinematicBarsShader.GetUniformLocation("uSize"), Math.Clamp(settings.Size, 0f, 50f) / 100f);
            Gl.Uniform1(_cinematicBarsShader.GetUniformLocation("uRotation"), Math.Clamp(settings.Rotation, -180f, 180f) * MathF.PI / 180f);
            Gl.Uniform1(_cinematicBarsShader.GetUniformLocation("uOffset"), Math.Clamp(settings.Offset, -100f, 100f) / 100f);
            Gl.Uniform3(_cinematicBarsShader.GetUniformLocation("uColor"),
                Math.Clamp(settings.ColorR, 0f, 1f), Math.Clamp(settings.ColorG, 0f, 1f), Math.Clamp(settings.ColorB, 0f, 1f));
            Gl.BindVertexArray(_edgeVao);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
            Gl.BindVertexArray(0);
            if (depthTest) Gl.Enable(GLEnum.DepthTest);
            if (cullFace) Gl.Enable(GLEnum.CullFace);
            if (blend) Gl.Enable(GLEnum.Blend);
        }
    }
}
