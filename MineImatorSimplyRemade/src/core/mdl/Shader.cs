using System.Reflection;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

public class Shader : IDisposable
{
    private GL _gl;
    
    uint vertexShader;
    uint fragmentShader;
    
    public uint ShaderProgram;

    public Shader(GL gl)
    {
        _gl = gl;
    }
    
    private string LoadShader(string shaderName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        string resourcePath = $"MineImatorSimplyRemade.assets.shaders.{shaderName}";

        using Stream? stream = assembly.GetManifestResourceStream(resourcePath);
        if(stream == null) throw new FileNotFoundException($"Shader not found: {shaderName}");
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public unsafe void CompileShader(string vertShader, string fragShader)
    {
        vertexShader = _gl.CreateShader(GLEnum.VertexShader);
        _gl.ShaderSource(vertexShader, 1, [LoadShader(vertShader)], null);
        _gl.CompileShader(vertexShader);
        
        fragmentShader = _gl.CreateShader(GLEnum.FragmentShader);
        _gl.ShaderSource(fragmentShader, 1, [LoadShader(fragShader)], null);
        _gl.CompileShader(fragmentShader);
        
        ShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(ShaderProgram, vertexShader);
        _gl.AttachShader(ShaderProgram, fragmentShader);
        _gl.LinkProgram(ShaderProgram);
        
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(ShaderProgram);
    }
}