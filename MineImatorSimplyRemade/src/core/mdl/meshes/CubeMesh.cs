using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public class CubeMesh : Mesh
{
    public CubeMesh(GL gl) : base(gl)
    {
        BuildDefaultCube();
        Upload();
    }

    public CubeMesh(GL gl, IEnumerable<vec3> vertices, IEnumerable<vec3>? normals = null, uint[]? indices = null) : base(gl, vertices, normals, indices)
    {
    }
}