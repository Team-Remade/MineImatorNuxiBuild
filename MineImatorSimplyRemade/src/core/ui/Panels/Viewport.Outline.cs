using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private Shader? _outlineShader;
    private uint _outlineNormalTex;
    private uint _previewOutlineNormalTex;

    private static bool NeedsOutlineNormals(CameraSceneObject? camera) =>
        camera?.Effects.Any(effect => effect.Enabled && effect.Type == CameraEffectType.Outline &&
            effect.Outline.NormalPass && effect.Outline.Strength > 0f && effect.Outline.Radius > 0f) == true;

    private void ApplyOutlinePass(uint sceneFbo, uint depthTexture, uint normalTexture,
        uint scratchFbo, uint scratchTexture, uint width, uint height,
        float nearPlane, float farPlane, CameraSceneObject? camera)
    {
        if (Gl == null || camera == null || width == 0 || height == 0 ||
            sceneFbo == 0 || scratchFbo == 0 || scratchTexture == 0)
            return;

        foreach (var effect in camera.Effects)
        {
            if (!effect.Enabled || effect.Type != CameraEffectType.Outline)
                continue;
            var settings = effect.Outline;
            foreach (var control in OutlineSettings.Controls)
            {
                float value = settings.GetValue(control.Key);
                if (!float.IsFinite(value) || value < control.Min || value > control.Max)
                    throw new InvalidDataException($"Outline {control.Key} is outside its supported range.");
            }
            if (settings.Strength == 0f || settings.Radius == 0f)
                continue;
            if (_outlineShader == null)
            {
                _outlineShader = new Shader(Gl);
                _outlineShader.CompileShader("edge.vert", "outline.frag");
                Gl.GetProgram(_outlineShader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException("Outline shader failed to link.");
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
            // Neither sampled attachment may remain attached to the draw framebuffer.
            Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, 0, 0);
            Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment3, GLEnum.Texture2D, 0, 0);
            Gl.Disable(GLEnum.DepthTest);
            Gl.Disable(GLEnum.CullFace);
            Gl.Disable(GLEnum.Blend);
            Gl.Viewport(0, 0, width, height);
            Gl.UseProgram(_outlineShader.ShaderProgram);
            try
            {
                void Texture(string name, uint texture, int unit)
                {
                    Gl.ActiveTexture((GLEnum)((int)GLEnum.Texture0 + unit));
                    Gl.BindTexture(GLEnum.Texture2D, texture);
                    Gl.Uniform1(_outlineShader.GetUniformLocation(name), unit);
                }
                void Float(string name, float value) => Gl.Uniform1(_outlineShader.GetUniformLocation(name), value);
                Texture("uScene", scratchTexture, 0);
                Texture("uDepth", depthTexture, 1);
                Texture("uNormals", normalTexture, 2);
                Gl.Uniform2(_outlineShader.GetUniformLocation("uResolution"), (float)width, (float)height);
                Gl.Uniform3(_outlineShader.GetUniformLocation("uColor"), settings.ColorR, settings.ColorG, settings.ColorB);
                Float("uNear", nearPlane);
                Float("uFar", farPlane);
                Float("uStrength", settings.Strength / 100f);
                Float("uRadius", settings.Radius);
                Float("uDepthThreshold", settings.DepthThreshold);
                Float("uDepthFade", settings.DepthFade);
                Float("uNormalThreshold", settings.NormalThreshold);
                Float("uNormalFade", settings.NormalFade);
                Gl.Uniform1(_outlineShader.GetUniformLocation("uNormalPass"), settings.NormalPass ? 1 : 0);
                Gl.Uniform1(_outlineShader.GetUniformLocation("uMode"), (int)settings.BlendMode);
                Gl.BindVertexArray(_edgeVao);
                Gl.DrawArrays(GLEnum.Triangles, 0, 3);
            }
            finally
            {
                Gl.BindVertexArray(0);
                for (int unit = 2; unit >= 0; unit--)
                {
                    Gl.ActiveTexture((GLEnum)((int)GLEnum.Texture0 + unit));
                    Gl.BindTexture(GLEnum.Texture2D, 0);
                }
                Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, depthTexture, 0);
                Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment3, GLEnum.Texture2D, normalTexture, 0);
                if (depthTest) Gl.Enable(GLEnum.DepthTest);
                if (cullFace) Gl.Enable(GLEnum.CullFace);
                if (blend) Gl.Enable(GLEnum.Blend);
            }
        }
    }
}
