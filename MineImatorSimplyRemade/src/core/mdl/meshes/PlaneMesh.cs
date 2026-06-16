using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public enum PlaneOrientation
{
    XY,
    XZ
}

public class PlaneMesh : Mesh
{
    public float Width { get; set; }
    public float Height { get; set; }
    public PlaneOrientation Orientation { get; set; }
    
    public PlaneMesh(GL gl, float width, float height, PlaneOrientation orientation) : base(gl)
    {
        Width = width;
        Height = height;
        Orientation = orientation;
        
        GenerateVertices();
        Upload();
    }

    public PlaneMesh(float width, float height, PlaneOrientation orientation, GL gl, IEnumerable<vec3> vertices, IEnumerable<vec3>? normals = null, uint[]? indices = null) : base(gl, vertices, normals, indices)
    {
        Width = width;
        Height = height;
        Orientation = orientation;
    }
    
    protected override void GenerateVertices()
    {
        float halfWidth = Width / 2f;
        float halfHeight = Height / 2f;

        // Vertices 0-3: front face.  Vertices 4-7: back face (same positions, reversed winding).
        // Duplicating vertices lets each face carry its own outward normal so that
        // DoubleSided lighting is correct from both sides.
        vec3[] positions;
        vec3   frontNormal, backNormal;

        if (Orientation == PlaneOrientation.XY)
        {
            // Front face faces +Z, back face faces -Z.
            //  3(-x,+y)──0(+x,+y)
            //  │           │
            //  2(-x,-y)──1(+x,-y)
            positions = [
                new vec3( halfWidth,  halfHeight, 0),   // 0 top-right
                new vec3( halfWidth, -halfHeight, 0),   // 1 bottom-right
                new vec3(-halfWidth, -halfHeight, 0),   // 2 bottom-left
                new vec3(-halfWidth,  halfHeight, 0),   // 3 top-left
                // back face duplicates
                new vec3( halfWidth,  halfHeight, 0),   // 4
                new vec3( halfWidth, -halfHeight, 0),   // 5
                new vec3(-halfWidth, -halfHeight, 0),   // 6
                new vec3(-halfWidth,  halfHeight, 0),   // 7
            ];
            frontNormal = new vec3(0, 0, -1);
            backNormal  = new vec3(0, 0, 1);
        }
        else
        {
            // Front face faces +Y, back face faces -Y.
            //  3(-x,+z)──0(+x,+z)
            //  │           │
            //  2(-x,-z)──1(+x,-z)
            positions = [
                new vec3( halfWidth, 0,  halfHeight),   // 0
                new vec3( halfWidth, 0, -halfHeight),   // 1
                new vec3(-halfWidth, 0, -halfHeight),   // 2
                new vec3(-halfWidth, 0,  halfHeight),   // 3
                // back face duplicates
                new vec3( halfWidth, 0,  halfHeight),   // 4
                new vec3( halfWidth, 0, -halfHeight),   // 5
                new vec3(-halfWidth, 0, -halfHeight),   // 6
                new vec3(-halfWidth, 0,  halfHeight),   // 7
            ];
            frontNormal = new vec3(0, 1, 0);
            backNormal  = new vec3(0, -1, 0);
        }

        // UV coordinates match vertex order: top-right, bottom-right, bottom-left, top-left.
        vec2 uv0 = new vec2(Width, Height);
        vec2 uv1 = new vec2(Width, 0);
        vec2 uv2 = new vec2(0, 0);
        vec2 uv3 = new vec2(0, Height);

        Vertices.AddRange(positions);
        TexCoords.AddRange(new[] { uv0, uv1, uv2, uv3, uv0, uv1, uv2, uv3 });

        // Front face: CCW from the front (+Z or +Y side) → [0,2,1, 0,3,2]
        // Back face:  CCW from the back  (-Z or -Y side) → [4,5,6, 4,6,7]
        Indices = [0, 1, 2,  0, 2, 3,
                   4, 6, 5,  4, 7, 6];

        // Assign normals explicitly rather than computing via cross product,
        // since both faces share the same position data.
        Normals.Clear();
        for (int i = 0; i < 4; i++) Normals.Add(frontNormal);
        for (int i = 0; i < 4; i++) Normals.Add(backNormal);
    }
}