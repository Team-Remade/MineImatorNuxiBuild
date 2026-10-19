using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// Identifies the kind of light a <see cref="LightSceneObject"/> represents.
/// Point lights emit in every direction; spot lights emit only inside a cone
/// defined by <see cref="LightSceneObject.LightSpotAngle"/>.
/// </summary>
public enum LightType
{
    Point,
    Spot
}

/// <summary>
/// A light placed in the scene.  Two variants are supported:
///   • <see cref="LightType.Point"/>  – emits a glow within <see cref="LightRange"/>
///     metres in every direction.  Rendered in the editor with a camera-facing
///     billboard icon + coloured ray glow, plus Mine-imator–style wireframe
///     gizmos (small bulb circle + coloured range circle).
///   • <see cref="LightType.Spot"/>   – emits a glow inside a cone aimed along
///     the light's local forward (+Z) axis.  Rendered with the same billboard
///     plus wireframe gizmos: bulb circle, 8-point cone-box outline, axis/edge
///     lines, and a world-space range circle at the cone base.
/// The billboard quads (icon + ray) are rendered by <c>Viewport</c> and bypass
/// the normal pick/outline passes.  The wireframe gizmos are rendered by
/// <see cref="MineImatorSimplyRemade.core.ui.LightGizmoRenderer"/>.
/// </summary>
public class LightSceneObject : SceneObject
{
    // ── Light properties ──────────────────────────────────────────────────────

    /// <summary>Kind of light (point or spot).  Defaults to point.</summary>
    public LightType Type = LightType.Point;

    /// <summary>RGBA colour of the light (alpha used only for the ray billboard tint).</summary>
    public vec4 LightColor = new vec4(1f, 1f, 1f, 1f);

    /// <summary>Overall brightness multiplier.</summary>
    public float LightEnergy = 1f;

    /// <summary>Radius of influence in world units (default 5 metres).</summary>
    public float LightRange = 5f;

    public float LightIndirectEnergy = 1f;
    public float LightSpecular = 0.5f;
    public bool  LightShadowEnabled = true;

    // ── Spot-light properties (only used when Type == LightType.Spot) ─────────

    /// <summary>
    /// Full cone angle in degrees for spot lights (0 – 180).  The default
    /// 45° gives a typical flashlight beam.
    /// </summary>
    public float LightSpotAngle = 45f;

    /// <summary>
    /// Width of the soft falloff band at the edge of the spot cone, in degrees
    /// (0 – <see cref="LightSpotAngle"/>).
    /// </summary>
    public float LightSpotBlend = 5f;

    // ── Billboard GPU handles (set by Viewport.InitLightBillboards) ───────────

    /// <summary>GL texture handle for the <c>light.png</c> icon billboard.</summary>
    public uint IconTextureId = 0;

    /// <summary>GL texture handle for the <c>lightRay.png</c> glow billboard drawn behind the icon.</summary>
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
