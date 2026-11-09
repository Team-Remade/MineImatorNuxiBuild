using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private void ApplyChromaticAberrationPass(uint sceneFbo, uint scratchFbo, uint scratchTexture,
        uint width, uint height, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 ||
            sceneFbo == 0 || scratchFbo == 0 || scratchTexture == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (effect.Type != CameraEffectType.ChromaticAberration)
                continue;
            var settings = effect.ChromaticAberration;
            foreach (var control in ChromaticAberrationSettings.Controls)
                if (!float.IsFinite(settings.GetValue(control.Key)))
                    throw new InvalidDataException($"Chromatic aberration {control.Key} must be finite.");
            if (settings.BlurAmount == 0f && settings.RedOffset == 0f &&
                settings.GreenOffset == 0f && settings.BlueOffset == 0f)
                continue;
            if (_chromaticAberrationShader == null)
            {
                _chromaticAberrationShader = new Shader(Gl);
                _chromaticAberrationShader.CompileShader("edge.vert", "chromatic_aberration.frag");
                Gl.GetProgram(_chromaticAberrationShader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException("Chromatic aberration shader failed to link.");
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
            Gl.UseProgram(_chromaticAberrationShader.ShaderProgram);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
            Gl.Uniform1(_chromaticAberrationShader.GetUniformLocation("uScene"), 0);
            Gl.Uniform2(_chromaticAberrationShader.GetUniformLocation("uResolution"), (float)width, (float)height);
            Gl.Uniform1(_chromaticAberrationShader.GetUniformLocation("uBlurAmount"), settings.BlurAmount);
            Gl.Uniform1(_chromaticAberrationShader.GetUniformLocation("uDistortRgb"), settings.DistortRgb ? 1 : 0);
            Gl.Uniform3(_chromaticAberrationShader.GetUniformLocation("uOffsets"),
                settings.RedOffset, settings.GreenOffset, settings.BlueOffset);
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
