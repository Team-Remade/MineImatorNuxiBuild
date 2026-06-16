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
        CheckShaderCompile(vertexShader, vertShader);

        fragmentShader = _gl.CreateShader(GLEnum.FragmentShader);
        _gl.ShaderSource(fragmentShader, 1, [LoadShader(fragShader)], null);
        _gl.CompileShader(fragmentShader);
        CheckShaderCompile(fragmentShader, fragShader);

        ShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(ShaderProgram, vertexShader);
        _gl.AttachShader(ShaderProgram, fragmentShader);
        _gl.LinkProgram(ShaderProgram);
        CheckProgramLink(ShaderProgram);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
    }

    private void CheckShaderCompile(uint shader, string name)
    {
        _gl.GetShader(shader, GLEnum.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            Console.Error.WriteLine($"[Shader] Compile error in '{name}':\n{log}");
        }
    }

    private void CheckProgramLink(uint program)
    {
        _gl.GetProgram(program, GLEnum.LinkStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            Console.Error.WriteLine($"[Shader] Link error:\n{log}");
        }
    }

    public void Dispose()
    {
        _gl.DeleteProgram(ShaderProgram);
    }
}