using System.Drawing;
using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.material.materials;

public class StandardMaterial : Material
{
    public vec4 AlbedoColor = new vec4();
    public float Metallic;
    public float Roughness;
    public bool NormalEnabled;
    //public Texture2D NormalTexture;
    public float Transparency;
    public bool EmissionEnabled;
    public vec4 Emission;
    public float EmissionEnergyMultiplier;
}