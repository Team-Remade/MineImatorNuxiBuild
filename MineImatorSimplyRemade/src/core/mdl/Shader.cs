using System.Reflection;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

public class Shader : IDisposable
{
    private static readonly Dictionary<string, uint> _programCache = new();

    private GL _gl;

    uint vertexShader;
    uint fragmentShader;

    public uint ShaderProgram { get; private set; }

    /// <summary>
    /// Cached uniform locations to avoid repeated glGetUniformLocation calls.
    /// </summary>
    private readonly Dictionary<string, int> _uniformLocations = new();

    public Shader(GL gl)
    {
        _gl = gl;
    }

    private static string LoadShaderSource(string shaderName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourcePath = $"MineImatorSimplyRemade.assets.shaders.{shaderName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null) throw new FileNotFoundException($"Shader not found: {shaderName}");
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Shared shader compilation: looks up the program in a static cache by
    /// vertex+fragment shader name. Only compiles once per unique pair.
    /// </summary>
    public unsafe void CompileShader(string vertShader, string fragShader)
    {
        string key = $"{vertShader}:{fragShader}";
        if (_programCache.TryGetValue(key, out uint existingProgram))
        {
            ShaderProgram = existingProgram;
            _uniformLocations.Clear();
            return;
        }

        string vertSrc = LoadShaderSource(vertShader);
        string fragSrc = LoadShaderSource(fragShader);

        vertexShader = _gl.CreateShader(GLEnum.VertexShader);
        _gl.ShaderSource(vertexShader, 1, [vertSrc], null);
        _gl.CompileShader(vertexShader);
        CheckShaderCompile(vertexShader, vertShader);

        fragmentShader = _gl.CreateShader(GLEnum.FragmentShader);
        _gl.ShaderSource(fragmentShader, 1, [fragSrc], null);
        _gl.CompileShader(fragmentShader);
        CheckShaderCompile(fragmentShader, fragShader);

        ShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(ShaderProgram, vertexShader);
        _gl.AttachShader(ShaderProgram, fragmentShader);
        _gl.LinkProgram(ShaderProgram);
        CheckProgramLink(ShaderProgram);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _programCache[key] = ShaderProgram;
    }

    /// <summary>
    /// Gets the uniform location from cache or queries the GL driver.
    /// </summary>
    public int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int loc))
            return loc;

        loc = _gl.GetUniformLocation(ShaderProgram, name);
        _uniformLocations[name] = loc;
        return loc;
    }

    private void CheckShaderCompile(uint shader, string name)
    {
        _gl.GetShader(shader, GLEnum.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            Console.Error.WriteLine($"Compile error in '{name}':\n{log}");
        }
    }

    private void CheckProgramLink(uint program)
    {
        _gl.GetProgram(program, GLEnum.LinkStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            Console.Error.WriteLine($"Link error:\n{log}");
        }
    }

    public void Dispose()
    {
        // Don't delete shared programs — they are cached and reused.
        // Only delete if this Shader was the one that created the program.
        if (_programCache.ContainsValue(ShaderProgram))
        {
            // Check if this key is the one we registered
            var kvp = _programCache.FirstOrDefault(kvp => kvp.Value == ShaderProgram);
            if (!kvp.Equals(default(KeyValuePair<string, uint>)))
            {
                // Actually keep the program alive since others may still reference it.
                return;
            }
        }
        _gl.DeleteProgram(ShaderProgram);
    }
}
