using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public sealed class ConeMesh : RadialMesh
{
    protected override bool IsCone => true;

    public ConeMesh(GL gl, int segments = 32, bool smoothShading = true, bool closed = true, bool mapped = false)
        : base(gl, segments, smoothShading, closed, mapped) { }

    private ConeMesh(GL gl, ConeMesh source) : base(gl, source) { }

    protected override Mesh CreateCloneGeometry(GL gl) => new ConeMesh(gl, this);
}
