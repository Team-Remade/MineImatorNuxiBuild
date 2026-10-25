using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public class CubeMesh : Mesh
{
    public bool Mapped { get; private set; }
    private bvec2 _mappedMirrors;

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

    private CubeMesh(GL gl, CubeMesh source) : base(gl)
    {
        Mapped = source.Mapped;
        _mappedMirrors = source._mappedMirrors;
    }

    protected override Mesh CreateCloneGeometry(GL gl) => new CubeMesh(gl, this);

    public void SetMapped(bool mapped)
    {
        if (Mapped == mapped)
            return;

        Mapped = mapped;
        BuildTexCoords();
        Upload();
    }

    public void SetMappedMirrors(bvec2 mirrors)
    {
        if (_mappedMirrors.x == mirrors.x && _mappedMirrors.y == mirrors.y)
            return;

        _mappedMirrors = mirrors;
        if (!Mapped) return;
        BuildTexCoords();
        Upload();
    }

    private void BuildTexCoords()
    {
        TexCoords.Clear();

        if (!Mapped)
        {
            for (int face = 0; face < 6; face++)
            {
                if (face == 2 || face == 3)
                    AddFaceUv(0f, 1f, 1f, 0f);
                else
                    AddFaceUv(1f, 0f, 0f, 1f);
            }
            return;
        }

        // Reference atlas after (x,z,-y) conversion; rows are texture top to bottom:
        // -Z | -X | +X
        // +Z | +Y | -Y
        AddFaceUvForCell(0, 1); // front
        AddFaceUvForCell(0, 0); // back
        AddFaceUvForCell(1, 1, true); // top
        AddFaceUvForCell(2, 1, true); // bottom
        AddFaceUvForCell(2, 0); // right
        AddFaceUvForCell(1, 0); // left
    }

    private void AddFaceUvForCell(int column, int rowFromTop, bool flipVertical = false)
    {
        const float cellWidth = 1f / 3f;
        const float cellHeight = 1f / 2f;

        float u0 = column * cellWidth;
        float u1 = u0 + cellWidth;

        float vTop = 1f - rowFromTop * cellHeight;
        float vBottom = vTop - cellHeight;

        if (_mappedMirrors.x) (u0, u1) = (u1, u0);
        if (_mappedMirrors.y) (vTop, vBottom) = (vBottom, vTop);

        if (flipVertical)
            AddFaceUv(u0, vTop, u1, vBottom);
        else
            AddFaceUv(u1, vBottom, u0, vTop);
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
