using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private void ApplyHeatDistortPass(uint sceneFbo, uint scratchFbo, uint scratchTexture,
        uint width, uint height, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 ||
            sceneFbo == 0 || scratchFbo == 0 || scratchTexture == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (!effect.Enabled || effect.Type != CameraEffectType.HeatDistort)
                continue;
            var settings = effect.HeatDistort;
            foreach (var control in HeatDistortSettings.Controls)
                if (!float.IsFinite(settings.GetValue(control.Key)))
                    throw new InvalidDataException($"Heat distort {control.Key} must be finite.");
            if (settings.Strength <= 0f)
                continue;
            if (_heatDistortShader == null)
            {
                var shader = new Shader(Gl);
                shader.CompileShader("edge.vert", "heat_distort.frag");
                Gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                {
                    shader.Dispose();
                    throw new InvalidOperationException("Heat distort shader failed to link.");
                }
                _heatDistortShader = shader;
            }
            bool depthTest = Gl.IsEnabled(GLEnum.DepthTest);
            bool cullFace = Gl.IsEnabled(GLEnum.CullFace);
            bool blend = Gl.IsEnabled(GLEnum.Blend);
            Gl.BindFramebuffer(GLEnum.ReadFramebuffer, sceneFbo);
            Gl.ReadBuffer(GLEnum.ColorAttachment0);
            Gl.BindFramebuffer(GLEnum.DrawFramebuffer, scratchFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.BlitFramebuffer(0, 0, (int)width, (int)height, 0, 0, (int)width, (int)height,
                ClearBufferMask.ColorBufferBit, GLEnum.Nearest);
            Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.Disable(GLEnum.DepthTest);
            Gl.Disable(GLEnum.CullFace);
            Gl.Disable(GLEnum.Blend);
            Gl.Viewport(0, 0, width, height);
            Gl.UseProgram(_heatDistortShader.ShaderProgram);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
            Gl.Uniform1(_heatDistortShader.GetUniformLocation("uScene"), 0);
            Gl.Uniform2(_heatDistortShader.GetUniformLocation("uResolution"), (float)width, (float)height);
            Gl.Uniform1(_heatDistortShader.GetUniformLocation("uTime"), GetTimelineEffectTimeSeconds());
            Gl.Uniform1(_heatDistortShader.GetUniformLocation("uStrength"), Math.Clamp(settings.Strength, 0f, 100f) / 100f);
            Gl.Uniform1(_heatDistortShader.GetUniformLocation("uSpeed"), Math.Clamp(settings.Speed, 0f, 1000f) / 100f);
            Gl.Uniform1(_heatDistortShader.GetUniformLocation("uScale"), Math.Clamp(settings.Scale, 1f, 1000f) / 100f);
            Gl.BindVertexArray(_edgeVao);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
            Gl.BindVertexArray(0);
            Gl.BindTexture(GLEnum.Texture2D, 0);
            if (depthTest) Gl.Enable(GLEnum.DepthTest);
            if (cullFace) Gl.Enable(GLEnum.CullFace);
            if (blend) Gl.Enable(GLEnum.Blend);
        }
    }
}
