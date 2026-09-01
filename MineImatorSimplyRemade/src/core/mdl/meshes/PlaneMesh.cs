using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public enum PlaneOrientation
{
    XY,
    XZ
}

/// <summary>Double-sided rectangular plane. Ported from the old GL <c>Mesh</c>
/// subclass to <see cref="VeldridMesh"/>.</summary>
public class PlaneMesh : VeldridMesh
{
    public float Width { get; set; }
    public float Height { get; set; }
    public PlaneOrientation Orientation { get; set; }

    public PlaneMesh(float width, float height, PlaneOrientation orientation) : base(VeldridContext.Device)
    {
        Width = width;
        Height = height;
        Orientation = orientation;

        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    public void SetOrientation(PlaneOrientation orientation)
    {
        if (Orientation == orientation)
            return;

        Orientation = orientation;
        Vertices.Clear();
        Normals.Clear();
        TexCoords.Clear();
        Indices = null;

        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    private void GenerateVertices()
    {
        float halfWidth = Width / 2f;
        float halfHeight = Height / 2f;

        // Vertices 0-3: front face.  Vertices 4-7: back face (same positions, reversed winding).
        // Duplicating vertices lets each face carry its own outward normal so that
        // double-sided lighting is correct from both sides.
        Vector3[] positions;
        Vector3 frontNormal, backNormal;

        if (Orientation == PlaneOrientation.XY)
        {
            // Front face faces +Z, back face faces -Z.
            positions =
            [
                new Vector3(halfWidth, halfHeight, 0),
                new Vector3(halfWidth, -halfHeight, 0),
                new Vector3(-halfWidth, -halfHeight, 0),
                new Vector3(-halfWidth, halfHeight, 0),
                new Vector3(halfWidth, halfHeight, 0),
                new Vector3(halfWidth, -halfHeight, 0),
                new Vector3(-halfWidth, -halfHeight, 0),
                new Vector3(-halfWidth, halfHeight, 0),
            ];
            frontNormal = new Vector3(0, 0, -1);
            backNormal = new Vector3(0, 0, 1);
        }
        else
        {
            // Front face faces +Y, back face faces -Y.
            positions =
            [
                new Vector3(halfWidth, 0, halfHeight),
                new Vector3(halfWidth, 0, -halfHeight),
                new Vector3(-halfWidth, 0, -halfHeight),
                new Vector3(-halfWidth, 0, halfHeight),
                new Vector3(halfWidth, 0, halfHeight),
                new Vector3(halfWidth, 0, -halfHeight),
                new Vector3(-halfWidth, 0, -halfHeight),
                new Vector3(-halfWidth, 0, halfHeight),
            ];
            frontNormal = new Vector3(0, 1, 0);
            backNormal = new Vector3(0, -1, 0);
        }

        Vector2 uv0 = new(Width, Height);
        Vector2 uv1 = new(Width, 0);
        Vector2 uv2 = new(0, 0);
        Vector2 uv3 = new(0, Height);

        Vertices.AddRange(positions);
        TexCoords.AddRange(new[] { uv0, uv1, uv2, uv3, uv0, uv1, uv2, uv3 });

        // Front face: CCW from the front (+Z or +Y side) -> [0,2,1, 0,3,2]
        // Back face:  CCW from the back  (-Z or -Y side) -> [4,5,6, 4,6,7]
        Indices =
        [
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6
        ];

        Normals.Clear();
        for (int i = 0; i < 4; i++) Normals.Add(frontNormal);
        for (int i = 0; i < 4; i++) Normals.Add(backNormal);
    }
}
