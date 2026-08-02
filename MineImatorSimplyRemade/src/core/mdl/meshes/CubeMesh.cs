using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public class CubeMesh : Mesh
{
    public bool Mapped { get; private set; }

    public CubeMesh(GL gl, bool mapped = false) : base(gl)
    {
        Mapped = mapped;
        BuildDefaultCube();
        BuildTexCoords();
        Upload();
    }

    public CubeMesh(GL gl, IEnumerable<vec3> vertices, IEnumerable<vec3>? normals = null, uint[]? indices = null) : base(gl, vertices, normals, indices)
    {
    }

    public void SetMapped(bool mapped)
    {
        if (Mapped == mapped)
            return;

        Mapped = mapped;
        BuildTexCoords();
        Upload();
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
        var q0 = new vec2(uMin, vMin);
        var q1 = new vec2(uMax, vMin);
        var q2 = new vec2(uMax, vMax);
        var q3 = new vec2(uMin, vMax);

        TexCoords.Add(q0);
        TexCoords.Add(q1);
        TexCoords.Add(q2);
        TexCoords.Add(q0);
        TexCoords.Add(q2);
        TexCoords.Add(q3);
    }
}