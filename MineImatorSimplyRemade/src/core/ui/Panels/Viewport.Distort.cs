using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private void ApplyDistortPass(uint sceneFbo, uint scratchFbo, uint scratchTexture,
        uint width, uint height, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 ||
            sceneFbo == 0 || scratchFbo == 0 || scratchTexture == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (effect.Type != CameraEffectType.Distort)
                continue;
            var settings = effect.Distort;
            foreach (var control in DistortSettings.Controls)
                if (!float.IsFinite(settings.GetValue(control.Key)))
                    throw new InvalidDataException($"Distort {control.Key} must be finite.");
            if (settings.ZoomAmount == 100f && settings.DistortAmount == 0f)
                continue;
            if (_distortShader == null)
            {
                _distortShader = new Shader(Gl);
                _distortShader.CompileShader("edge.vert", "distort.frag");
                Gl.GetProgram(_distortShader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException("Distort shader failed to link.");
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
            Gl.UseProgram(_distortShader.ShaderProgram);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
            Gl.Uniform1(_distortShader.GetUniformLocation("uScene"), 0);
            Gl.Uniform2(_distortShader.GetUniformLocation("uResolution"), (float)width, (float)height);
            Gl.Uniform1(_distortShader.GetUniformLocation("uRepeatImage"), settings.RepeatImage ? 1 : 0);
            Gl.Uniform1(_distortShader.GetUniformLocation("uZoomAmount"), settings.ZoomAmount);
            Gl.Uniform1(_distortShader.GetUniformLocation("uDistortAmount"), settings.DistortAmount);
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
