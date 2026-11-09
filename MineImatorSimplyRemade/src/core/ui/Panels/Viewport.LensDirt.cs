using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    private Shader? _lensDirtShader;
    private uint _lensDirtGlowFbo, _lensDirtGlowTex;
    private uint _lensDirtBloomFbo, _lensDirtBloomTex;
    private uint _lensDirtWidth, _lensDirtHeight;
    private bool _captureLensDirt;
    private float _lensDirtGlowStrength;

    private void PrepareLensDirt(CameraSceneObject? camera, uint width, uint height)
    {
        _captureLensDirt = camera?.Effects.Any(e => e.Type == CameraEffectType.LensDirt &&
            !string.IsNullOrEmpty(e.LensDirt.TexturePath) && e.LensDirt.Intensity > 0f &&
            (e.LensDirt.AffectedByBloom || e.LensDirt.AffectedByGlow)) == true;
        _lensDirtGlowStrength = 0f;
        if (!_captureLensDirt || Gl == null || width == 0 || height == 0)
            return;
        if (_lensDirtShader == null)
        {
            _lensDirtShader = new Shader(Gl);
            _lensDirtShader.CompileShader("edge.vert", "lens_dirt.frag");
        }
        // Shared by sequential main/preview/feed renders; never retain light from the previous camera.
        if (_lensDirtGlowFbo == 0)
        {
            InitGlowResources(ref _lensDirtGlowFbo, ref _lensDirtGlowTex, width, height, "Lens dirt glow", hdr: true);
            InitGlowResources(ref _lensDirtBloomFbo, ref _lensDirtBloomTex, width, height, "Lens dirt bloom", hdr: true);
        }
        else if (_lensDirtWidth != width || _lensDirtHeight != height)
        {
            ResizeGlowResources(_lensDirtGlowTex, width, height, hdr: true);
            ResizeGlowResources(_lensDirtBloomTex, width, height, hdr: true);
        }
        _lensDirtWidth = width;
        _lensDirtHeight = height;
        Gl.ClearColor(0f, 0f, 0f, 0f);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _lensDirtGlowFbo);
        Gl.Clear(ClearBufferMask.ColorBufferBit);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _lensDirtBloomFbo);
        Gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    private void ApplyLensDirtPass(uint sceneFbo, uint sceneTexture, uint scratchFbo, uint scratchTexture,
        uint pingFbo, uint pingTexture, uint width, uint height, CameraSceneObject? camera)
    {
        if (!_captureLensDirt || Gl == null || _lensDirtShader == null || camera == null)
            return;
        bool depthTest = Gl.IsEnabled(GLEnum.DepthTest);
        bool cullFace = Gl.IsEnabled(GLEnum.CullFace);
        bool blend = Gl.IsEnabled(GLEnum.Blend);
        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);
        Gl.Viewport(0, 0, width, height);
        Gl.BindVertexArray(_edgeVao);
        foreach (var effect in camera.Effects)
        {
            var settings = effect.LensDirt;
            if (effect.Type != CameraEffectType.LensDirt || string.IsNullOrEmpty(settings.TexturePath) ||
                settings.Intensity <= 0f || (!settings.AffectedByBloom && !settings.AffectedByGlow))
                continue;
            uint dirt = camera.LoadTexture(settings.TexturePath);
            if (dirt == 0)
            {
                Console.Error.WriteLine($"Could not load lens dirt texture '{settings.TexturePath}'.");
                continue;
            }
            Gl.UseProgram(_lensDirtShader.ShaderProgram);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uScene"), 0);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uGlow"), 1);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uBloom"), 2);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uDirt"), 3);
            Gl.Uniform2(_lensDirtShader.GetUniformLocation("uTexelSize"), 1f / width, 1f / height);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uGlowStrength"), _lensDirtGlowStrength);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uAffectedByBloom"), settings.AffectedByBloom ? 1 : 0);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uAffectedByGlow"), settings.AffectedByGlow ? 1 : 0);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uIntensity"), Math.Clamp(settings.Intensity, 0f, 1000f) / 100f);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uPower"), Math.Clamp(settings.Power, 1f, 1000f) / 100f);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uTransparentOutput"), TransparentBackground ? 1 : 0);
            Bind(1, _lensDirtGlowTex);
            Bind(2, _lensDirtBloomTex);
            Bind(3, dirt);
            Draw(scratchFbo, 0);

            float radius = Math.Clamp(settings.Radius, 0f, 100f) * 0.0004f * Math.Min(width, height);
            int stages = Math.Clamp((int)MathF.Ceiling(MathF.Log2(MathF.Max(1f, radius))) + 1, 1, 10);
            for (int stage = 0; stage < stages && radius > 0f; stage++)
            {
                float step = radius * 0.5f / MathF.Pow(2f, stages - stage - 1);
                Bind(0, scratchTexture);
                Gl.Uniform2(_lensDirtShader.GetUniformLocation("uStep"), step / width, 0f);
                Draw(pingFbo, 1);
                Bind(0, pingTexture);
                Gl.Uniform2(_lensDirtShader.GetUniformLocation("uStep"), 0f, step / height);
                Draw(scratchFbo, 1);
            }
            Bind(0, 0);
            Gl.BindFramebuffer(GLEnum.ReadFramebuffer, sceneFbo);
            Gl.ReadBuffer(GLEnum.ColorAttachment0);
            Gl.BindFramebuffer(GLEnum.DrawFramebuffer, pingFbo);
            Gl.BlitFramebuffer(0, 0, (int)width, (int)height, 0, 0, (int)width, (int)height,
                ClearBufferMask.ColorBufferBit, GLEnum.Nearest);
            Bind(0, pingTexture);
            Bind(2, scratchTexture);
            Draw(sceneFbo, 2);
        }
        for (int unit = 3; unit >= 0; unit--) Bind(unit, 0);
        Gl.BindVertexArray(0);
        Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
        if (depthTest) Gl.Enable(GLEnum.DepthTest);
        if (cullFace) Gl.Enable(GLEnum.CullFace);
        if (blend) Gl.Enable(GLEnum.Blend);
        _captureLensDirt = false;

        void Bind(int unit, uint texture)
        {
            Gl.ActiveTexture((GLEnum)((uint)GLEnum.Texture0 + unit));
            Gl.BindTexture(GLEnum.Texture2D, texture);
        }
        void Draw(uint target, int pass)
        {
            Gl.BindFramebuffer(GLEnum.Framebuffer, target);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.Uniform1(_lensDirtShader.GetUniformLocation("uPass"), pass);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        }
    }

    private void DisposeLensDirtResources()
    {
        if (Gl == null) return;
        if (_lensDirtGlowFbo != 0) Gl.DeleteFramebuffer(_lensDirtGlowFbo);
        if (_lensDirtBloomFbo != 0) Gl.DeleteFramebuffer(_lensDirtBloomFbo);
        if (_lensDirtGlowTex != 0) Gl.DeleteTexture(_lensDirtGlowTex);
        if (_lensDirtBloomTex != 0) Gl.DeleteTexture(_lensDirtBloomTex);
        if (_lensDirtShader != null) Gl.DeleteProgram(_lensDirtShader.ShaderProgram);
        _lensDirtGlowFbo = _lensDirtBloomFbo = _lensDirtGlowTex = _lensDirtBloomTex = 0;
        _lensDirtShader = null;
        _captureLensDirt = false;
    }
}
