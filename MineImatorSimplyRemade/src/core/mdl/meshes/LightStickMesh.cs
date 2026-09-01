using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Procedural "stick" mesh used as the unselected spot-light aim indicator.
/// A short, thin rectangular cross-section prism that runs from the local
/// origin to z = +1. Ported from the old GL <c>Mesh</c> subclass to
/// <see cref="VeldridMesh"/>.
/// </summary>
public class LightStickMesh : VeldridMesh
{
    public const float HalfThickness = 0.0015f;

    public LightStickMesh() : base(VeldridContext.Device)
    {
        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    private void GenerateVertices()
    {
        const float h = HalfThickness;
        const float backZ = 1f;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var idx = new List<uint>();

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            uint start = (uint)verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            idx.Add(start); idx.Add(start + 1); idx.Add(start + 2);
            idx.Add(start); idx.Add(start + 2); idx.Add(start + 3);
        }

        AddQuad(
            new Vector3(h, -h, 0f), new Vector3(h, h, 0f),
            new Vector3(h, h, backZ), new Vector3(h, -h, backZ),
            new Vector3(1f, 0f, 0f));
        AddQuad(
            new Vector3(-h, h, 0f), new Vector3(-h, -h, 0f),
            new Vector3(-h, -h, backZ), new Vector3(-h, h, backZ),
            new Vector3(-1f, 0f, 0f));
        AddQuad(
            new Vector3(-h, h, 0f), new Vector3(h, h, 0f),
            new Vector3(h, h, backZ), new Vector3(-h, h, backZ),
            new Vector3(0f, 1f, 0f));
        AddQuad(
            new Vector3(h, -h, 0f), new Vector3(-h, -h, 0f),
            new Vector3(-h, -h, backZ), new Vector3(h, -h, backZ),
            new Vector3(0f, -1f, 0f));

        Vertices.AddRange(verts);
        Normals.AddRange(norms);
        Indices = idx.ToArray();

        DoubleSided = true;
    }
}
