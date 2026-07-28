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
    /// When true, both faces of every triangle are rendered (back-face culling
    /// disabled for this surface).
    /// </summary>
    public bool DoubleSided = false;
}