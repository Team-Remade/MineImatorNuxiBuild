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
        
        DoubleSided = true;
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

        // UV coordinates tile the texture across the plane extent.
        // uv0..uv3 match the vertex order: top-right, bottom-right, bottom-left, top-left.
        vec2 uv0 = new vec2(Width, Height);
        vec2 uv1 = new vec2(Width, 0);
        vec2 uv2 = new vec2(0, 0);
        vec2 uv3 = new vec2(0, Height);

        Vertices.AddRange(positions);
        TexCoords.AddRange(new[] { uv0, uv1, uv2, uv3 });

        // Two CCW triangles forming the quad:
        //  3──0
        //  │ /│
        //  │/ │
        //  2──1
        Indices = [0, 1, 2,  0, 2, 3];

        GenerateNormals();
    }
}