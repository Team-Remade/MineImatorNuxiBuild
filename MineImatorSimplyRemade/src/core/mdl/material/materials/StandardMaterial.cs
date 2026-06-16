using System.Drawing;
using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.material.materials;

public class StandardMaterial : Material
{
    public vec4 AlbedoColor = new vec4(1f, 1f, 1f, 1f);
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
    public bool EmissionEnabled;
    public vec4 Emission;
    public float EmissionEnergyMultiplier;

    /// <summary>
    /// When true, both faces of every triangle are rendered (back-face culling
    /// disabled for this surface).
    /// </summary>
    public bool DoubleSided = false;
}