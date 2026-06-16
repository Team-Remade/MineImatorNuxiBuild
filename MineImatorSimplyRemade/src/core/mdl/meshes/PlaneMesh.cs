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

        vec3[] positions;
        if (Orientation == PlaneOrientation.XY)
        {
            positions =
            [
                new vec3(halfWidth, halfHeight, 0),
                new vec3(halfWidth, -halfHeight, 0),
                new vec3(-halfWidth, -halfHeight, 0),
                new vec3(-halfWidth, halfHeight, 0)
            ];
        }
        else
        {
            positions =
            [
                new vec3(halfWidth, 0, halfHeight),
                new vec3(halfWidth, 0, -halfHeight),
                new vec3(-halfWidth, 0, -halfHeight),
                new vec3(-halfWidth, 0, halfHeight)
            ];
        }

        vec2 uv0, uv1, uv2, uv3;
        if (Orientation == PlaneOrientation.XY)
        {
            uv0 = new vec2(Width, Height);
            uv1 = new vec2(Width, 0);
            uv2 = new vec2(0, 0);
            uv3 = new vec2(0, Height);
        }
        else
        {
            uv0 = new vec2(Width, Height);
            uv1 = new vec2(Width, 0);
            uv2 = new vec2(0, 0);
            uv3 = new vec2(0, Height);
        }

        // Front face: vertices 0-3 (CCW when viewed from the positive normal side)
        // Back face:  vertices 4-7 (same positions, reversed winding for correct back normals)
        Vertices.AddRange(positions);
        Vertices.AddRange(positions);

        // Front face triangles (CCW):
        //  0──3
        //  │ /│
        //  │/ │
        //  1──2
        // Back face triangles (CW of front = CCW from behind): indices offset by 4
        Indices =
        [
            // front
            0, 1, 2,  0, 2, 3,
            // back (reversed winding)
            4, 6, 5,  4, 7, 6,
        ];

        GenerateNormals();
    }
}