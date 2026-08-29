using System.Reflection;
using Veldrid;
using Veldrid.SPIRV;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Compiles and caches Veldrid shader pairs from the embedded GLSL sources under
/// <c>assets/shaders/</c>, replacing <c>core.mdl.Shader</c>'s GL program cache.
///
/// Unlike the old Silk.NET.OpenGL version - which let each shader declare any
/// number of loose top-level <c>uniform</c> variables set by name via
/// <c>glUniform*</c> - Veldrid (through Veldrid.SPIRV's GLSL-&gt;SPIR-V-&gt;backend
/// cross-compilation) requires every uniform to live in an explicit
/// <c>layout(std140) uniform Block { ... }</c> block or be an explicitly bound
/// sampler/texture, with the C# <see cref="ResourceLayout"/> describing those
/// bindings in the same order the shader declares them. See the migration notes
/// at the top of <c>assets/shaders/simple.vert</c>/<c>simple.frag</c> for the
/// concrete block layouts this first reintroduces.
/// </summary>
public static class VeldridShaderCache
{
    private static readonly Dictionary<string, (Shader Vertex, Shader Fragment)> _cache = new();

    /// <summary>
    /// Compiles (or returns the cached) vertex+fragment <see cref="Shader"/> pair
    /// for the given GLSL source file names (e.g. <c>"simple.vert"</c>, <c>"simple.frag"</c>).
    /// </summary>
    public static (Shader Vertex, Shader Fragment) GetOrCompile(GraphicsDevice device, string vertName, string fragName)
    {
        string key = $"{vertName}:{fragName}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        string vertSrc = LoadShaderSource(vertName);
        string fragSrc = LoadShaderSource(fragName);

        // TODO(migration): hardcoded to HLSL because VeldridBitmapRenderSurface
        // defaults to GraphicsBackend.Direct3D11. If a render surface is ever
        // created with the Vulkan backend instead, this needs to branch to
        // CrossCompileTarget.None (SPIR-V passthrough) based on device.BackendType.
        VertexFragmentCompilationResult result = SpirvCompilation.CompileVertexFragment(
            System.Text.Encoding.UTF8.GetBytes(vertSrc),
            System.Text.Encoding.UTF8.GetBytes(fragSrc),
            CrossCompileTarget.HLSL,
            new CrossCompileOptions());

        Shader vertexShader = device.ResourceFactory.CreateShader(new ShaderDescription(
            ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(result.VertexShader), "main"));
        Shader fragmentShader = device.ResourceFactory.CreateShader(new ShaderDescription(
            ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(result.FragmentShader), "main"));

        var pair = (vertexShader, fragmentShader);
        _cache[key] = pair;
        return pair;
    }

    private static string LoadShaderSource(string shaderName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string resourcePath = $"MineImatorSimplyRemade.assets.shaders.{shaderName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
            throw new FileNotFoundException($"Shader not found: {shaderName}");

        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void ClearCache()
    {
        foreach (var (vertex, fragment) in _cache.Values)
        {
            vertex.Dispose();
            fragment.Dispose();
        }
        _cache.Clear();
    }
}
