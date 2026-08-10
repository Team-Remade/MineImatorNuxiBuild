using System.Text;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using StbTrueTypeSharp;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>Rasterizes TrueType/OpenType text and turns its alpha mask into a flat or extruded mesh.</summary>
public static class TextMeshFactory
{
    private const float RasterHeight = 256f;
    private static GL? _gl;

    public static void Rebuild(SceneObject obj, GL? gl = null)
    {
        _gl = gl ?? _gl;
        if (_gl == null) return;

        // A user/camera albedo is distinct from the generated text mask and
        // must survive rebuilds caused by outline/font/property changes.
        uint externalTexture = obj.Visuals
            .FirstOrDefault(mesh => mesh.TextureId != 0 && mesh.TextureId != mesh.AlphaMaskTextureId)
            ?.TextureId ?? 0;

        (byte[] pixels, int width, int height) = Rasterize(obj);
        if (obj.TextMeshExtruded)
            MakeAlphaOpaque(pixels);
        uint texture = UploadTexture(_gl, pixels, width, height);
        float extrusionDepth = Math.Clamp(obj.TextMeshExtrusionDepth, 0.001f, 10f);
        var replacement = new ExtrudedItemMesh(_gl, texture, pixels, obj.TextMeshExtruded,
            (int)RasterHeight, width, height, extrusionDepth, 128)
        {
            BlurTexture = obj.TextMeshAntialiasing,
            AlphaMaskTextureId = texture,
            IsTextAlphaMask = true,
            TextMaskOutlineColor = obj.TextMeshOutlineEnabled
                ? obj.TextMeshOutlineColor
                : new GlmSharp.vec4(1f, 1f, 1f, 1f)
        };
        replacement.TextureId = externalTexture;
        float sizeScale = Math.Clamp(obj.TextMeshFontSize, 1f, 512f) / 64f;
        float shiftX = obj.TextMeshHorizontalAlignment switch
        {
            0 => width / (2f * RasterHeight) * sizeScale, 2 => -width / (2f * RasterHeight) * sizeScale, _ => 0f
        };
        float shiftY = obj.TextMeshVerticalAlignment switch
        {
            0 => -height / (2f * RasterHeight) * sizeScale, 2 => height / (2f * RasterHeight) * sizeScale, _ => 0f
        };
        if (shiftX != 0f || shiftY != 0f || sizeScale != 1f)
        {
            for (int i = 0; i < replacement.Vertices.Count; i++)
            {
                GlmSharp.vec3 vertex = replacement.Vertices[i];
                vertex.x = vertex.x * sizeScale + shiftX;
                vertex.y = vertex.y * sizeScale + shiftY;
                replacement.Vertices[i] = vertex;
            }
            replacement.Upload();
        }
        obj.BlurTexture = obj.TextMeshAntialiasing;
        // A text mask must be sampled from its base level.  At small on-screen
        // sizes the lowest mip levels average glyph coverage across the entire
        // rectangular texture, turning every texel slightly non-transparent;
        // the material shader then renders what looks like a solid plane.
        obj.TextureMipmaps = false;

        foreach (Mesh old in obj.Visuals.ToArray())
        {
            uint oldMaskTexture = old.AlphaMaskTextureId;
            obj.RemoveMesh(old);
            old.Dispose();
            // Only the generated mask is owned here. External and camera-feed
            // textures may be shared and are managed by their respective caches.
            if (oldMaskTexture != 0) _gl.DeleteTexture(oldMaskTexture);
        }
        obj.AddMesh(replacement);
        // Rebuilding text replaces its Mesh instance. Reapply the object's
        // persistent material state so emission and the other material channels
        // do not temporarily reset to StandardMaterial defaults.
        obj.ApplyMaterialSettingsToMeshes();
    }

    /// <summary>
    /// Extruded pixels represent actual solid geometry, so partially transparent
    /// anti-aliasing samples must not survive into the front/back or side faces.
    /// The high-resolution mask still provides a smooth-looking silhouette.
    /// </summary>
    private static void MakeAlphaOpaque(byte[] pixels)
    {
        const byte coverageCutoff = 64;
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = pixels[i] >= coverageCutoff ? (byte)255 : (byte)0;
    }

    private static unsafe (byte[] pixels, int width, int height) Rasterize(SceneObject obj)
    {
        string text = obj.GetEffectiveTextMeshString();
        text = string.IsNullOrEmpty(text) ? " " : text;
        string resolvedFont = ResolveFontPath(obj.TextMeshFontPath);
        byte[] fontBytes = File.ReadAllBytes(resolvedFont);
        var info = new StbTrueType.stbtt_fontinfo();
        float scale;
        int ascent, descent, lineGap;
        fixed (byte* fontData = fontBytes)
        {
            if (StbTrueType.stbtt_InitFont(info, fontData, 0) == 0)
                throw new InvalidDataException($"Unable to read font '{resolvedFont}'.");
            scale = StbTrueType.stbtt_ScaleForPixelHeight(info, RasterHeight);
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
        }

        int outlineRadius = obj.TextMeshOutlineEnabled
            ? Math.Clamp((int)MathF.Round(obj.TextMeshOutlineThickness * RasterHeight / 64f), 1,
                (int)RasterHeight) : 0;
        int padding = 8 + outlineRadius;
        int baseline = (int)MathF.Ceiling(ascent * scale) + padding;
        int width = padding * 2;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int advance, bearing;
            StbTrueType.stbtt_GetCodepointHMetrics(info, rune.Value, &advance, &bearing);
            width += (int)MathF.Ceiling(advance * scale);
        }
        width = Math.Clamp(width, 16, 16384);
        int height = Math.Clamp((int)MathF.Ceiling((ascent - descent + lineGap) * scale) + padding * 2, 16, 8192);
        var alpha = new byte[width * height];
        int penX = padding;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int x0, y0, x1, y1, advance, bearing;
            StbTrueType.stbtt_GetCodepointBitmapBox(info, rune.Value, scale, scale, &x0, &y0, &x1, &y1);
            int glyphWidth = x1 - x0, glyphHeight = y1 - y0;
            int targetX = penX + x0, targetY = baseline + y0;
            if (glyphWidth > 0 && glyphHeight > 0 && targetX >= 0 && targetY >= 0 &&
                targetX + glyphWidth <= width && targetY + glyphHeight <= height)
            {
                fixed (byte* dst = &alpha[targetY * width + targetX])
                    StbTrueType.stbtt_MakeCodepointBitmap(info, dst, glyphWidth, glyphHeight, width,
                        scale, scale, rune.Value);
            }
            StbTrueType.stbtt_GetCodepointHMetrics(info, rune.Value, &advance, &bearing);
            penX += (int)MathF.Ceiling(advance * scale);
        }

        if (!obj.TextMeshAntialiasing)
            for (int i = 0; i < alpha.Length; i++) alpha[i] = alpha[i] >= 128 ? (byte)255 : (byte)0;

        byte[] outline = obj.TextMeshOutlineEnabled ? Dilate(alpha, width, height, outlineRadius) : alpha;
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            int d = i * 4;
            float fill = alpha[i] / 255f;
            float border = Math.Max(0f, (outline[i] - alpha[i]) / 255f) *
                           Math.Clamp(obj.TextMeshOutlineColor.w, 0f, 1f);
            float remainingBorder = border * (1f - fill);
            float outputAlpha = fill + remainingBorder;

            // Store straight-alpha colour: the renderer applies texture alpha
            // separately.  Premultiplying here made antialiased fills dark and
            // caused the fill to wash over the chosen outline colour.
            // Red carries the original glyph-fill coverage. The material shader
            // uses it to keep an assigned texture out of the outline pixels.
            rgba[d] = ToByte(fill);
            rgba[d + 1] = 0;
            rgba[d + 2] = 0;
            rgba[d + 3] = ToByte(outputAlpha);
        }
        return (rgba, width, height);
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static byte[] Dilate(byte[] source, int width, int height, int radius)
    {
        var horizontal = new byte[source.Length];
        var result = new byte[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte max = 0;
            for (int dx = -radius; dx <= radius; dx++)
                if (x + dx >= 0 && x + dx < width) max = Math.Max(max, source[y * width + x + dx]);
            horizontal[y * width + x] = max;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte max = 0;
            for (int dy = -radius; dy <= radius; dy++)
                if (y + dy >= 0 && y + dy < height) max = Math.Max(max, horizontal[(y + dy) * width + x]);
            result[y * width + x] = max;
        }
        return result;
    }

    private static string ResolveFontPath(string requested)
    {
        if (string.Equals(requested, "minecraftia", StringComparison.OrdinalIgnoreCase))
            return ResolveMinecraftiaPath();
        if (!string.IsNullOrWhiteSpace(requested) && File.Exists(requested)) return requested;
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")]
            : OperatingSystem.IsMacOS()
                ? ["/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"]
                : ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf"];
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("No default TrueType font was found. Choose a .ttf or .otf font in Text Mesh properties.");
    }

    private static string ResolveMinecraftiaPath()
    {
        string relative = Path.Combine("data", "minecraft", "versions", "26.2", "fonts", "Minecraftia-Regular.ttf");
        string[] directCandidates =
        [
            Path.Combine(AppContext.BaseDirectory, relative),
            Path.Combine(Environment.CurrentDirectory, relative),
            Path.Combine(Environment.CurrentDirectory, "MineImatorSimplyRemade", relative)
        ];
        string? direct = directCandidates.FirstOrDefault(File.Exists);
        if (direct != null) return direct;

        string versions = Path.Combine(AppContext.BaseDirectory, "data", "minecraft", "versions");
        if (Directory.Exists(versions))
        {
            string? discovered = Directory.EnumerateFiles(versions, "Minecraftia-Regular.ttf", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (discovered != null) return discovered;
        }
        throw new FileNotFoundException("The bundled Minecraftia font could not be found.");
    }

    private static unsafe uint UploadTexture(GL gl, byte[] pixels, int width, int height)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, texture);
        fixed (byte* p = pixels)
            gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)width, (uint)height,
                0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.GenerateMipmap(GLEnum.Texture2D);
        gl.BindTexture(GLEnum.Texture2D, 0);
        return texture;
    }
}
