using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace MineImatorSimplyRemade.core.graphics;

public class VeldridShader : IDisposable
{
    private static readonly Dictionary<string, VeldridShader> _cache = new();

    public Shader[] Shaders { get; }
    public ResourceLayout[] ResourceLayouts { get; }
    public string Name { get; }

    public VeldridShader(string name, Shader[] shaders, ResourceLayout[] resourceLayouts)
    {
        Name = name;
        Shaders = shaders;
        ResourceLayouts = resourceLayouts;
    }

    public static string LoadShaderSource(string shaderName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourcePath = $"MineImatorSimplyRemade.assets.shaders.{shaderName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null) throw new FileNotFoundException($"Shader resource not found: {shaderName}");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static Shader[] CompileFromGlsl(GraphicsDevice gd, string vertSrc, string fragSrc)
    {
        var vertBytes = Encoding.UTF8.GetBytes(vertSrc);
        var fragBytes = Encoding.UTF8.GetBytes(fragSrc);

        var vertDesc = new ShaderDescription(ShaderStages.Vertex, vertBytes, "main");
        var fragDesc = new ShaderDescription(ShaderStages.Fragment, fragBytes, "main");

        if (gd.BackendType == GraphicsBackend.Vulkan ||
            gd.BackendType == GraphicsBackend.Direct3D11 ||
            gd.BackendType == GraphicsBackend.Metal)
        {
            var crossCompileOptions = new CrossCompileOptions();
            return gd.ResourceFactory.CreateFromSpirv(vertDesc, fragDesc, crossCompileOptions);
        }
        else
        {
            return
            [
                gd.ResourceFactory.CreateShader(vertDesc),
                gd.ResourceFactory.CreateShader(fragDesc)
            ];
        }
    }

    public static VeldridShader GetOrCreate(
        GraphicsDevice gd,
        string vertName,
        string fragName,
        ResourceLayoutDescription[]? layoutDescriptions = null)
    {
        string key = $"{vertName}:{fragName}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string vertSrc = LoadShaderSource(vertName);
        string fragSrc = LoadShaderSource(fragName);

        var vertBytes = Encoding.UTF8.GetBytes(vertSrc);
        var fragBytes = Encoding.UTF8.GetBytes(fragSrc);

        var vertDesc = new ShaderDescription(ShaderStages.Vertex, vertBytes, "main");
        var fragDesc = new ShaderDescription(ShaderStages.Fragment, fragBytes, "main");

        Shader[] shaders;
        if (gd.BackendType == GraphicsBackend.Vulkan ||
            gd.BackendType == GraphicsBackend.Direct3D11 ||
            gd.BackendType == GraphicsBackend.Metal)
        {
            // Use SPIRV cross-compilation for modern APIs
            var crossCompileOptions = new CrossCompileOptions();
            shaders = gd.ResourceFactory.CreateFromSpirv(vertDesc, fragDesc, crossCompileOptions);
        }
        else
        {
            shaders = new[]
            {
                gd.ResourceFactory.CreateShader(vertDesc),
                gd.ResourceFactory.CreateShader(fragDesc)
            };
        }

        var layouts = new List<ResourceLayout>();
        if (layoutDescriptions != null)
        {
            foreach (var desc in layoutDescriptions)
            {
                layouts.Add(gd.ResourceFactory.CreateResourceLayout(desc));
            }
        }

        var veldridShader = new VeldridShader(key, shaders, layouts.ToArray());
        _cache[key] = veldridShader;
        return veldridShader;
    }

    public void Dispose()
    {
        foreach (var s in Shaders)
        {
            s.Dispose();
        }
        foreach (var l in ResourceLayouts)
        {
            l.Dispose();
        }
    }

    public static void DisposeAll()
    {
        foreach (var shader in _cache.Values)
        {
            shader.Dispose();
        }
        _cache.Clear();
    }
}
