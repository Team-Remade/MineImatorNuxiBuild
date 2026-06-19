using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// A scene object that represents a skeletal bone imported from a 3-D model file.
///
/// Bones participate in the normal <see cref="SceneObject"/> hierarchy, support
/// keyframe animation, and are selectable via the colour-pick pass.
///
/// A small octahedron <see cref="IndicatorMesh"/> is generated at construction
/// and rendered by the Viewport as a flat-coloured 3-D indicator so the user
/// can always see and click the bone even when no mesh geometry covers its origin.
/// </summary>
public class BoneSceneObject : SceneObject
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The original bone name as stored in the source asset.
    /// Set by <see cref="MineImatorSimplyRemade.core.mdl.AssimpModelLoader"/>.
    /// </summary>
    public string BoneName = "";

    // ── Visual indicator ──────────────────────────────────────────────────────

    /// <summary>
    /// A small procedural octahedron rendered by the Viewport as a flat-coloured
    /// 3-D indicator.  Built once via <see cref="CreateIndicator"/>.
    /// Null until GL is available.
    /// </summary>
    public Mesh? IndicatorMesh { get; private set; }

    /// <summary>
    /// Builds the octahedron indicator mesh.  Must be called once on the GL
    /// thread (typically from <see cref="AssimpModelLoader"/> after import).
    /// </summary>
    public void CreateIndicator(GL gl)
    {
        // Small octahedron: 6 vertices, 8 triangular faces.
        // Sized to be clearly visible but not distracting (0.08 world units radius).
        const float r  = 0.08f; // equatorial radius
        const float h  = 0.12f; // half-height (top/bottom tips)

        var verts = new vec3[]
        {
            new( 0,  h,  0),  // 0 top
            new( r,  0,  0),  // 1 +X
            new( 0,  0,  r),  // 2 +Z
            new(-r,  0,  0),  // 3 -X
            new( 0,  0, -r),  // 4 -Z
            new( 0, -h,  0),  // 5 bottom
        };

        var idx = new uint[]
        {
            // upper 4 faces
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1,
            // lower 4 faces
            5, 2, 1,
            5, 3, 2,
            5, 4, 3,
            5, 1, 4,
        };

        IndicatorMesh = new Mesh(gl, verts, indices: idx);
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string GetObjectIcon() => "Bone";
}
