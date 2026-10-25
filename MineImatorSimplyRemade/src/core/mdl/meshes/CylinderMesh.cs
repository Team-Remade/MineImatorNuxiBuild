using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public sealed class CylinderMesh : RadialMesh
{
    protected override bool IsCone => false;

    public CylinderMesh(GL gl, int segments = 32, bool smoothShading = true, bool closed = true, bool mapped = false)
        : base(gl, segments, smoothShading, closed, mapped) { }

    private CylinderMesh(GL gl, CylinderMesh source) : base(gl, source) { }

    protected override Mesh CreateCloneGeometry(GL gl) => new CylinderMesh(gl, this);
}
