using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// Identifies the kind of light a <see cref="LightSceneObject"/> represents.
/// Point lights emit in every direction; spot lights emit only inside a cone
/// defined by <see cref="LightSceneObject.SpotAngle"/>.
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
///     billboard icon + coloured ray.
///   • <see cref="LightType.Spot"/>   – emits a glow inside a cone aimed along
///     the light's local forward (-Z) axis.  In the editor the unselected state
///     shows a thin "stick" along the aim; the selected state shows the full
///     cone of coverage.  In unrendered mode the spot light uses the same
///     billboard rendering as a point light.
/// Two camera-facing billboard quads (light icon + coloured ray) are
/// rendered by <c>Viewport</c> as a visual indicator; they are not part of
/// <see cref="SceneObject.Visuals"/> so they bypass the normal pick/outline passes.
/// </summary>
public class LightSceneObject : SceneObject
{
    // ── Light properties ──────────────────────────────────────────────────────

    /// <summary>Kind of light (point or spot).  Defaults to point for backward compatibility.</summary>
    public LightType Type = LightType.Point;

    /// <summary>RGBA colour of the light (alpha is ignored for lighting, used only for the ray tint).</summary>
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
    /// 45° gives a typical flashlight / stage-lamp beam.
    /// </summary>
    public float LightSpotAngle = 45f;

    /// <summary>
    /// Width of the soft falloff band at the edge of the spot cone, in degrees
    /// (0 – <see cref="LightSpotAngle"/>).  Outside the cone + blend band the
    /// light is fully attenuated; inside the inner cone it is at full strength.
    /// </summary>
    public float LightSpotBlend = 5f;

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

    // ── Range indicator (procedural ring drawn for selected lights) ──────────

    /// <summary>
    /// One unit-radius ring mesh shared by every <see cref="LightSceneObject"/>.
    /// The viewport scales the model matrix by <see cref="LightRange"/> so the
    /// same geometry is reused regardless of the light's actual range.
    /// </summary>
    public static Mesh? SharedRangeRingMesh { get; private set; }

    /// <summary>
    /// Unit cone mesh (apex at origin, base at +Z = 1) shared by every
    /// <see cref="LightSceneObject"/>.  The viewport scales it to match the
    /// spot light's outer-cone radius / range.
    /// </summary>
    public static Mesh? SharedSpotConeMesh { get; private set; }

    /// <summary>
    /// Thin unit-length stick (square cross-section cylinder) mesh shared by
    /// every <see cref="LightSceneObject"/>.  Rendered along the spot light's
    /// forward axis in the unselected state.  The default zero rotation aims
    /// the stick along local +Z.
    /// </summary>
    public static Mesh? SharedSpotStickMesh { get; private set; }

    /// <summary>
    /// Uploads the shared unit-radius ring mesh.  Must be called once on the GL
    /// thread (typically from <c>Viewport.InitLightBillboards</c> after context
    /// creation).  Subsequent calls are no-ops.
    /// </summary>
    public static void EnsureRangeRingMesh(GL gl)
    {
        if (SharedRangeRingMesh == null)
            SharedRangeRingMesh = new MineImatorSimplyRemade.core.mdl.meshes.LightRangeRingMesh(gl);
        if (SharedSpotConeMesh == null)
            SharedSpotConeMesh = new MineImatorSimplyRemade.core.mdl.meshes.LightConeMesh(gl);
        if (SharedSpotStickMesh == null)
            SharedSpotStickMesh = new MineImatorSimplyRemade.core.mdl.meshes.LightStickMesh(gl);
    }
}
