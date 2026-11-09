using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private Shader? _watermarkShader;

    private void ApplyWatermarkPass(uint sceneFbo, uint scratchFbo, uint scratchTexture,
        uint width, uint height, PropertiesPanel? settings)
    {
        if (Gl == null || settings?.WatermarkEnabled != true ||
            string.IsNullOrWhiteSpace(settings.WatermarkImagePath) || settings.WatermarkOpacity <= 0f)
            return;

        string path = ProjectManager.Instance.ResolveAssetPath(settings.WatermarkImagePath);
        uint texture = MineImatorLoader.Instance.LoadTextureFromFile(path, flipVertically: true);
        if (texture == 0)
        {
            Console.Error.WriteLine($"Could not load watermark image '{path}'.");
            return;
        }
        Gl.ActiveTexture(GLEnum.Texture1);
        Gl.BindTexture(GLEnum.Texture2D, texture);
        Gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int imageWidth);
        Gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int imageHeight);
        float scale = Math.Clamp(settings.WatermarkScale, 0.01f, 20f);
        float markWidth = imageWidth * scale, markHeight = imageHeight * scale;
        float padding = Math.Clamp(settings.WatermarkPadding, 0f, 100000f);
        float x = Align(width, markWidth, settings.WatermarkHorizontalAlignment);
        float y = Align(height, markHeight, settings.WatermarkVerticalAlignment);

        _watermarkShader ??= new Shader(Gl);
        if (_watermarkShader.ShaderProgram == 0)
            _watermarkShader.CompileShader("edge.vert", "watermark.frag");
        bool depth = Gl.IsEnabled(GLEnum.DepthTest);
        bool cull = Gl.IsEnabled(GLEnum.CullFace);
        bool blend = Gl.IsEnabled(GLEnum.Blend);
        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindFramebuffer(GLEnum.ReadFramebuffer, sceneFbo);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.BindFramebuffer(GLEnum.DrawFramebuffer, scratchFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.BlitFramebuffer(0, 0, (int)width, (int)height, 0, 0, (int)width, (int)height,
            ClearBufferMask.ColorBufferBit, GLEnum.Nearest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        Gl.UseProgram(_watermarkShader.ShaderProgram);
        Gl.Uniform1(_watermarkShader.GetUniformLocation("uScene"), 0);
        Gl.Uniform1(_watermarkShader.GetUniformLocation("uWatermark"), 1);
        Gl.Uniform2(_watermarkShader.GetUniformLocation("uFrameSize"), (float)width, (float)height);
        Gl.Uniform4(_watermarkShader.GetUniformLocation("uRect"), x, y, markWidth, markHeight);
        Gl.Uniform1(_watermarkShader.GetUniformLocation("uOpacity"), Math.Clamp(settings.WatermarkOpacity, 0f, 1f));
        Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.ActiveTexture(GLEnum.Texture1);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.ActiveTexture(GLEnum.Texture0);
        if (depth) Gl.Enable(GLEnum.DepthTest);
        if (cull) Gl.Enable(GLEnum.CullFace);
        if (blend) Gl.Enable(GLEnum.Blend);

        float Align(float frameSize, float markSize, int alignment) => alignment switch
        {
            0 => padding,
            1 => (frameSize - markSize) * 0.5f,
            _ => frameSize - markSize - padding
        };
    }
}
