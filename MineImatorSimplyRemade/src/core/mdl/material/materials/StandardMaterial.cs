using System.Drawing;
using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.mdl.material.materials;

public class StandardMaterial : Material
{
    /// <summary>
    /// Flat base colour used by the fragment shader as <c>uAlbedo</c>/<c>uAlpha</c>
    /// (RGB colour, alpha = opacity). Combined with <see cref="AlbedoTexture"/> when set.
    /// </summary>
    public vec4 AlbedoColor = new vec4(1f, 1f, 1f, 1f);
    public vec4 BlendColor = new vec4(1f, 1f, 1f, 1f);
    public vec4 MixColor = new vec4(0f, 0f, 0f, 0f);

    /// <summary>
    /// OpenGL texture handle bound as the base/diffuse texture when rendering
    /// (0 = no texture, use <see cref="AlbedoColor"/> instead).
    /// </summary>
    public uint AlbedoTexture = 0;

    public float Metallic;
    public float Roughness = 1f;
    public bool PbrEnabled = false;

    /// <summary>Linear packed surface data: R = AO, G = roughness, B = metallic, A = height.</summary>
    public uint SurfaceTexture = 0;
    public float NormalStrength = 1f;
    public float OcclusionStrength = 1f;
    public float HeightScale = 0f;
    public bool NormalEnabled;

    /// <summary>
    /// OpenGL texture handle for the normal map (0 = no normal map).
    /// Loaded from an external image file and uploaded to the GPU as a Texture2D.
    /// </summary>
    public uint NormalTexture = 0;

    public float Transparency;
    public MaterialAlphaMode AlphaMode = MaterialAlphaMode.Blend;
    public MaterialBlendMode BlendMode = MaterialBlendMode.Normal;
    public float AlphaClipThreshold = 0.5f;

    /// <summary>
    /// When true, adds emissive lighting to the final shaded result.
    /// </summary>
    public bool EmissionEnabled;

    /// <summary>Optional RGB emission mask (0 = use the diffuse texture or white).</summary>
    public uint EmissionTexture = 0;

    /// <summary>
    /// Emissive colour added in the fragment shader when <see cref="EmissionEnabled"/> is true.
    /// </summary>
    public vec4 Emission;

    /// <summary>
    /// Scalar multiplier for <see cref="Emission"/>.
    /// </summary>
    public float EmissionEnergyMultiplier = 1f;

    /// <summary>
    /// Subsurface scattering distance in Mine-imator pixels, 16 per block (0 = disabled).
    /// </summary>
    public float Subsurface = 0f;

    /// <summary>
    /// Per-channel subsurface scattering radius.
    /// </summary>
    public vec3 SubsurfaceRadius = vec3.Ones;

    /// <summary>
    /// Subsurface tint colour.
    /// </summary>
    public vec4 SubsurfaceColor = new vec4(1f, 1f, 1f, 1f);

    /// <summary>
    /// Anisotropy-like phase value used by the highlight scatter term.
    /// </summary>
    public float SubsurfaceHighlight = 0.35f;

    /// <summary>
    /// Strength of the highlight scatter term.
    /// </summary>
    public float SubsurfaceHighlightStrength = 0.6f;

    /// <summary>
    /// When true, emissive surfaces only contribute to indirect lighting and
    /// do not emit direct light into nearby geometry.
    /// </summary>
    public bool EmissionIndirectOnly = false;

    /// <summary>
    /// Per-mesh auto-emission level (0..15) inferred from Minecraft block data.
    /// </summary>
    public byte AutoEmissionLevel = 0;

    /// <summary>
    /// When true, both faces of every triangle are rendered (back-face culling
    /// disabled for this surface).
    /// </summary>
    public bool DoubleSided = false;

    /// <summary>
    /// Per-axis UV offset applied after repeat/mirroring.
    /// </summary>
    public vec2 TextureOffset = vec2.Zero;

    /// <summary>
    /// Per-axis UV repeat multiplier.
    /// </summary>
    public vec2 TextureRepeat = new vec2(1f, 1f);

    /// <summary>
    /// Per-axis UV mirroring toggle.
    /// </summary>
    public bvec2 TextureMirror = new bvec2(false, false);
}
