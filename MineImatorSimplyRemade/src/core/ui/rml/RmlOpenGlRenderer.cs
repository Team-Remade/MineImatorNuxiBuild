using System.Runtime.InteropServices;
using RmlUiNet;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>RmlUi render interface backed by the application's Silk.NET OpenGL context.</summary>
public sealed unsafe class RmlOpenGlRenderer : RenderInterface, IDisposable
{
    private sealed class Geometry
    {
        public uint Vao;
        public uint Vbo;
        public uint Ibo;
        public uint IndexCount;
    }

    private readonly GL _gl;
    private readonly Dictionary<nint, Geometry> _geometry = new();
    private readonly HashSet<uint> _textures = new();
    private readonly HashSet<uint> _externalTextures = new();
    private uint _program;
    private int _viewportLocation;
    private int _translationLocation;
    private int _textureLocation;
    private int _useTextureLocation;
    private int _flipTextureLocation;
    private int _viewportHeight;
    private bool _disposed;

    public RmlOpenGlRenderer(GL gl)
    {
        _gl = gl;
        CreateProgram();
    }

    public void BeginFrame(int width, int height)
    {
        _viewportHeight = height;
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(GLEnum.DepthTest);
        _gl.UseProgram(_program);
        _gl.Uniform2(_viewportLocation, (float)width, (float)height);
        _gl.Uniform1(_textureLocation, 0);
    }

    public void EndFrame()
    {
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(GLEnum.ScissorTest);
        _gl.Enable(GLEnum.DepthTest);
    }

    public override nint CompileGeometry(Vertex* vertices, int vertexCount, int* indices, int indexCount)
    {
        var item = new Geometry { IndexCount = (uint)indexCount };
        _gl.GenVertexArrays(1, out item.Vao);
        _gl.GenBuffers(1, out item.Vbo);
        _gl.GenBuffers(1, out item.Ibo);

        _gl.BindVertexArray(item.Vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, item.Vbo);
        _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertexCount * sizeof(Vertex)), vertices, GLEnum.StaticDraw);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, item.Ibo);
        _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indexCount * sizeof(int)), indices, GLEnum.StaticDraw);

        uint stride = (uint)sizeof(Vertex);
        _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 4, GLEnum.UnsignedByte, true, stride, 2 * sizeof(float));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, 2 * sizeof(float) + 4);
        _gl.EnableVertexAttribArray(2);
        _gl.BindVertexArray(0);

        nint handle = GCHandle.ToIntPtr(GCHandle.Alloc(item));
        _geometry.Add(handle, item);
        return handle;
    }

    public override void RenderGeometry(nint geometry, Vector2f translation, nint texture)
    {
        if (!_geometry.TryGetValue(geometry, out Geometry? item)) return;
        _gl.UseProgram(_program);
        _gl.Uniform2(_translationLocation, translation.X, translation.Y);
        uint textureId = unchecked((uint)texture);
        _gl.Uniform1(_useTextureLocation, textureId == 0 ? 0 : 1);
        _gl.Uniform1(_flipTextureLocation, _externalTextures.Contains(textureId) ? 1 : 0);
        _gl.ActiveTexture(GLEnum.Texture0);
        _gl.BindTexture(GLEnum.Texture2D, textureId);
        _gl.BindVertexArray(item.Vao);
        _gl.DrawElements(GLEnum.Triangles, item.IndexCount, GLEnum.UnsignedInt, null);
    }

    public override void ReleaseGeometry(nint geometry)
    {
        if (!_geometry.Remove(geometry, out Geometry? item)) return;
        _gl.DeleteVertexArray(item.Vao);
        _gl.DeleteBuffer(item.Vbo);
        _gl.DeleteBuffer(item.Ibo);
        GCHandle.FromIntPtr(geometry).Free();
    }

    public override nint LoadTexture(ref Vector2i textureDimensions, string source)
    {
        try
        {
            if (source.StartsWith("gl-texture://", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = source[13..].Split('/');
                if (parts.Length < 3 || !uint.TryParse(parts[0], out uint external) ||
                    !int.TryParse(parts[1], out int width) || !int.TryParse(parts[2], out int height))
                    return 0;
                textureDimensions = new Vector2i(width, height);
                _externalTextures.Add(external);
                return (nint)external;
            }
            using FileStream stream = File.OpenRead(source);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            textureDimensions = new Vector2i(image.Width, image.Height);
            fixed (byte* pixels = image.Data)
                return CreateTexture(pixels, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RmlUi texture load failed for '{source}': {ex.Message}");
            return 0;
        }
    }

    public override nint GenerateTexture(byte* source, int numBytes, Vector2i dimensions) =>
        CreateTexture(source, dimensions.X, dimensions.Y);

    private nint CreateTexture(byte* pixels, int width, int height)
    {
        uint texture = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, texture);
        _gl.PixelStore(GLEnum.UnpackAlignment, 1);
        _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _textures.Add(texture);
        return (nint)texture;
    }

    public override void ReleaseTexture(nint textureHandle)
    {
        uint texture = unchecked((uint)textureHandle);
        if (_externalTextures.Remove(texture)) return;
        if (_textures.Remove(texture)) _gl.DeleteTexture(texture);
    }

    public override void EnableScissorRegion(bool enable)
    {
        if (enable) _gl.Enable(GLEnum.ScissorTest);
        else _gl.Disable(GLEnum.ScissorTest);
    }

    public override void SetScissorRegion(int x, int y, int width, int height) =>
        _gl.Scissor(x, _viewportHeight - y - height, (uint)Math.Max(0, width), (uint)Math.Max(0, height));

    private void CreateProgram()
    {
        const string vertex = """
            #version 330 core
            layout(location=0) in vec2 position;
            layout(location=1) in vec4 colour;
            layout(location=2) in vec2 texCoord;
            uniform vec2 viewport;
            uniform vec2 translation;
            out vec4 vertexColour;
            out vec2 vertexTexCoord;
            void main() {
                vec2 p = position + translation;
                gl_Position = vec4(p.x * 2.0 / viewport.x - 1.0, 1.0 - p.y * 2.0 / viewport.y, 0.0, 1.0);
                vertexColour = colour;
                vertexTexCoord = texCoord;
            }
            """;
        const string fragment = """
            #version 330 core
            in vec4 vertexColour;
            in vec2 vertexTexCoord;
            uniform sampler2D tex;
            uniform int useTexture;
            uniform int flipTexture;
            out vec4 outputColour;
            void main() {
                vec2 uv = flipTexture != 0 ? vec2(vertexTexCoord.x, 1.0 - vertexTexCoord.y) : vertexTexCoord;
                vec4 sampled = useTexture != 0 ? texture(tex, uv) : vec4(1.0);
                outputColour = vertexColour * sampled;
            }
            """;
        uint vs = CompileShader(GLEnum.VertexShader, vertex);
        uint fs = CompileShader(GLEnum.FragmentShader, fragment);
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vs);
        _gl.AttachShader(_program, fs);
        _gl.LinkProgram(_program);
        _gl.GetProgram(_program, GLEnum.LinkStatus, out int linked);
        if (linked == 0) throw new InvalidOperationException(_gl.GetProgramInfoLog(_program));
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        _viewportLocation = _gl.GetUniformLocation(_program, "viewport");
        _translationLocation = _gl.GetUniformLocation(_program, "translation");
        _textureLocation = _gl.GetUniformLocation(_program, "tex");
        _useTextureLocation = _gl.GetUniformLocation(_program, "useTexture");
        _flipTextureLocation = _gl.GetUniformLocation(_program, "flipTexture");
    }

    private uint CompileShader(GLEnum type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
        if (compiled == 0) throw new InvalidOperationException(_gl.GetShaderInfoLog(shader));
        return shader;
    }

    public new void Dispose()
    {
        if (_disposed) return;
        foreach (nint handle in _geometry.Keys.ToArray()) ReleaseGeometry(handle);
        foreach (uint texture in _textures.ToArray()) ReleaseTexture((nint)texture);
        if (_program != 0) _gl.DeleteProgram(_program);
        _disposed = true;
        base.Dispose();
    }
}
