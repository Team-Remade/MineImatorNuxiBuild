using System.Drawing;
using GlmSharp;

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
    public float Roughness;
    public bool NormalEnabled;

    /// <summary>
    /// OpenGL texture handle for the normal map (0 = no normal map).
    /// Loaded from an external image file and uploaded to the GPU as a Texture2D.
    /// Currently stored and propagated through the hierarchy; a normal-map shader
    /// stage will consume it once the rendering pipeline supports it.
    /// </summary>
    public uint NormalTexture = 0;

    public float Transparency;

    /// <summary>
    /// When true, adds emissive lighting to the final shaded result.
    /// </summary>
    public bool EmissionEnabled;

    /// <summary>
    /// Emissive colour added in the fragment shader when <see cref="EmissionEnabled"/> is true.
    /// </summary>
    public vec4 Emission;

    /// <summary>
    /// Scalar multiplier for <see cref="Emission"/>.
    /// </summary>
    public float EmissionEnergyMultiplier = 1f;

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
    /// Enables per-material subsurface scattering contribution.
    /// </summary>
    public bool SubsurfaceScatteringEnabled = false;

    /// <summary>
    /// Scalar multiplier for subsurface scattering intensity.
    /// </summary>
    public float SubsurfaceScatteringStrength = 0.45f;

    /// <summary>
    /// Per-channel scattering radius.
    /// </summary>
    public vec3 SubsurfaceScatteringRadius = new vec3(0.65f, 0.35f, 0.2f);

    /// <summary>
    /// Tint for subsurface transmitted light.
    /// </summary>
    public vec3 SubsurfaceScatteringColor = new vec3(1f, 0.78f, 0.72f);

    /// <summary>
    /// Desaturates incoming light color before tinting.
    /// </summary>
    public float SubsurfaceScatteringDesaturation = 0.35f;

    /// <summary>
    /// Henyey-Greenstein-like absorption/phase control.
    /// </summary>
    public float SubsurfaceScatteringAbsorption = 0.35f;

    /// <summary>
    /// Scales depth-derived thickness used by SSS falloff.
    /// </summary>
    public float SubsurfaceScatteringDepthScale = 28f;

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
