using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>Unit cube, optionally UV-mapped with a 3x2 cross layout. Ported from
/// the old GL <c>Mesh</c> subclass to <see cref="VeldridMesh"/> - see that
/// class's migration notes for what's changed (Veldrid pipeline/buffers instead
/// of a VAO/VBO, explicit <c>Upload(OutputDescription)</c> call).</summary>
public class CubeMesh : VeldridMesh
{
    public bool Mapped { get; private set; }

    public CubeMesh(bool mapped = false) : base(VeldridContext.Device)
    {
        Mapped = mapped;
        BuildDefaultCube();
        BuildTexCoords();
        Upload(VeldridContext.StandardOutputDescription);
    }

    public void SetMapped(bool mapped)
    {
        if (Mapped == mapped)
            return;

        Mapped = mapped;
        BuildTexCoords();
        Upload(VeldridContext.StandardOutputDescription);
    }

    /// <summary>A unit cube (side 1) centred at origin, with per-face flat normals.
    /// 6 faces x 2 triangles x 3 vertices = 36 vertices (no index buffer).</summary>
    private void BuildDefaultCube()
    {
        var faces = new (Vector3 normal, Vector3[] quad)[]
        {
            (new Vector3(0, 0, 1), new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) }),
            (new Vector3(0, 0, -1), new[] { new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f) }),
            (new Vector3(0, 1, 0), new[] { new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }),
            (new Vector3(0, -1, 0), new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f) }),
            (new Vector3(1, 0, 0), new[] { new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f) }),
            (new Vector3(-1, 0, 0), new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }),
        };

        foreach (var (normal, quad) in faces)
        {
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            {
                Vertices.Add(quad[i]);
                Normals.Add(normal);
            }
        }
    }

    private void BuildTexCoords()
    {
        TexCoords.Clear();

        if (!Mapped)
        {
            for (int i = 0; i < 6; i++)
                AddFaceUv(0f, 0f, 1f, 1f);
            return;
        }

        // 3x2 unwrap layout matching the cubemap example:
        // row 0: Front | Left | Right
        // row 1: Back  | Top  | Bottom
        AddFaceUvForCell(0, 0); // front
        AddFaceUvForCell(0, 1); // back
        AddFaceUvForCell(1, 1); // top
        AddFaceUvForCell(2, 1); // bottom
        AddFaceUvForCell(2, 0); // right
        AddFaceUvForCell(1, 0); // left
    }

    private void AddFaceUvForCell(int column, int rowFromTop)
    {
        const float cellWidth = 1f / 3f;
        const float cellHeight = 1f / 2f;

        float u0 = column * cellWidth;
        float u1 = u0 + cellWidth;

        float vTop = 1f - rowFromTop * cellHeight;
        float vBottom = vTop - cellHeight;

        AddFaceUv(u0, vBottom, u1, vTop);
    }

    private void AddFaceUv(float uMin, float vMin, float uMax, float vMax)
    {
        // Face vertex order in BuildDefaultCube is quad[0..3] expanded as
        // triangle indices [0,1,2, 0,2,3].
        var q0 = new Vector2(uMin, vMin);
        var q1 = new Vector2(uMax, vMin);
        var q2 = new Vector2(uMax, vMax);
        var q3 = new Vector2(uMin, vMax);

        TexCoords.Add(q0);
        TexCoords.Add(q1);
        TexCoords.Add(q2);
        TexCoords.Add(q0);
        TexCoords.Add(q2);
        TexCoords.Add(q3);
    }
}
