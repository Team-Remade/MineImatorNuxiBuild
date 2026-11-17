using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private void ApplyPixelatePass(uint sceneFbo, uint scratchFbo, uint scratchTexture,
        uint width, uint height, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 ||
            sceneFbo == 0 || scratchFbo == 0 || scratchTexture == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (!effect.Enabled || effect.Type != CameraEffectType.Pixelate)
                continue;
            float size = effect.Pixelate.Size;
            if (!float.IsFinite(size))
                throw new InvalidDataException("Pixelate size must be finite.");
            size = MathF.Floor(Math.Clamp(size, 1f, 256f) + 0.5f);
            if (size <= 1f)
                continue;
            if (_pixelateShader == null)
            {
                _pixelateShader = new Shader(Gl);
                _pixelateShader.CompileShader("edge.vert", "pixelate.frag");
                Gl.GetProgram(_pixelateShader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException("Pixelate shader failed to link.");
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
            Gl.UseProgram(_pixelateShader.ShaderProgram);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
            Gl.Uniform1(_pixelateShader.GetUniformLocation("uScene"), 0);
            Gl.Uniform1(_pixelateShader.GetUniformLocation("uSize"), size);
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
