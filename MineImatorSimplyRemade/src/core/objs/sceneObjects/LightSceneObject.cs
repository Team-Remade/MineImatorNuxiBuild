using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// A point light placed in the scene.  Emits a glow within <see cref="LightRange"/>
/// metres.  Two camera-facing billboard quads (light icon + coloured ray) are
/// rendered by <c>Viewport</c> as a visual indicator; they are not part of
/// <see cref="SceneObject.Visuals"/> so they bypass the normal pick/outline passes.
/// </summary>
public class LightSceneObject : SceneObject
{
    // ── Light properties ──────────────────────────────────────────────────────

    /// <summary>RGBA colour of the light (alpha is ignored for lighting, used only for the ray tint).</summary>
    public vec4 LightColor = new vec4(1f, 1f, 1f, 1f);

    /// <summary>Overall brightness multiplier.</summary>
    public float LightEnergy = 1f;

    /// <summary>Radius of influence in world units (default 5 metres).</summary>
    public float LightRange = 5f;

    public float LightIndirectEnergy = 1f;
    public float LightSpecular = 0.5f;
    public bool  LightShadowEnabled = true;

    // ── Billboard GPU handles (set by Viewport.InitLightTextures) ─────────────

    /// <summary>
    /// GL texture handle for the <c>light.png</c> icon billboard.
    /// 0 until <c>Viewport</c> assigns it after loading embedded textures.
    /// </summary>
    public uint IconTextureId = 0;

    /// <summary>
    /// GL texture handle for the <c>lightRay.png</c> billboard drawn behind the icon.
    /// 0 until <c>Viewport</c> assigns it.
    /// </summary>
    public uint RayTextureId = 0;

    // ── Billboard geometry (shared VAO/VBO created once by Viewport) ──────────

    /// <summary>
    /// VAO for a screen-aligned unit quad used by the billboard shader.
    /// Shared across all <see cref="LightSceneObject"/> instances; assigned by <c>Viewport</c>.
    /// </summary>
    public static uint BillboardVao = 0;
    public static uint BillboardVbo = 0;
    public static uint BillboardEbo = 0;
}
