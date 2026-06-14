using System.Drawing;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

public class Mesh : IDisposable
{
    private GL _gl;
    
    private uint VBO, VAO;
    private Shader shader;
    
    protected Color Color { get; set; } = Color.White;
    
    private float[] vertices = {
        -0.5f, -0.5f * glm.Sqrt(3) / 3, 0.0f,
        0.5f, -0.5f * glm.Sqrt(3) / 3, 0.0f,
        0.0f, 0.5f * glm.Sqrt(3) * 2 / 3, 0.0f
    };

    public Mesh(GL gl)
    {
        _gl = gl;

        shader = new Shader(gl);
        shader.CompileShader("simple.vert", "simple.frag");
        
        _gl.GenVertexArrays(1, out VAO);
        _gl.GenBuffers(1, out VBO);
        
        _gl.BindVertexArray(VAO);
        
        _gl.BindBuffer(GLEnum.ArrayBuffer, VBO);
        _gl.BufferData(GLEnum.ArrayBuffer, 9 * sizeof(float), vertices, GLEnum.StaticDraw);
        
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 3 * sizeof(float), 0);
        _gl.EnableVertexAttribArray(0);
        
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void Render()
    {
        _gl.UseProgram(shader.ShaderProgram);
        
        _gl.BindVertexArray(VAO);
        _gl.DrawArrays(GLEnum.Triangles, 0, 3);
    }
    
    public int GetSurfaceCount()
    {
        return 1;
    }

    public Material SurfaceGetMaterial(int surfaceIndex)
    {
        return new Material();
    }

    public void Dispose()
    {
        _gl.DeleteVertexArrays(1, VAO);
        _gl.DeleteBuffers(1, VBO);
        shader.Dispose();
    }
}