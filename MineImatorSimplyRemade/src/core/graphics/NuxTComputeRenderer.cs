using System.Numerics;
using MineImatorSimplyRemade.core.mdl;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.graphics;

public sealed class NuxTComputeRenderer : IDisposable
{
    private const int MaxRenderPixels = 8_388_608;
    private const int MaxUploadChunkBytes = 8 * 1024 * 1024;
    private const int MaxUploadChunkRows = 64;
    private const int NodeBoundsTexture = 0;
    private const int NodeDataTexture = 1;
    private const int TriangleIndexTexture = 2;
    private const int GeometryTexture = 3;
    private const int TriangleMaterialTexture = 4;
    private const int MaterialTexture = 5;
    private const int MaterialTextureReferences = 6;
    private const int PointLightTexture = 7;
    private const int DirectionalLightTexture = 8;
    private const int TexturePixelsTexture = 9;
    private const int TlasBoundsTexture = 10;
    private const int TlasDataTexture = 11;
    private const int TlasInstanceIndexTexture = 12;
    private const int InstanceTransformTexture = 13;
    private const int InstanceInfoTexture = 14;
    private readonly uint[] _sceneTextures = new uint[15];
    private readonly int[] _textureWidths = new int[15];
    private readonly int _maxTextureSize;
    private readonly GL _gl;
    private uint _program;
    private uint _accumulationTexture;
    private uint _outputTexture;
    private int _width;
    private int _height;
    private int _completedSamples;
    private NuxTScene? _scene;
    private NuxTScene? _failedScene;
    private NuxTCamera _camera;
    private NuxTSettings _settings;
    private bool _transparentBackground;
    private bool _hasRequest;
    private bool _disposed;
    private string _status = "Idle";
    private int _sceneGeometryUploadCount;
    private int _lightingTextureUploadCount;

    public static bool IsSupported(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        int major = gl.GetInteger(GLEnum.MajorVersion);
        int minor = gl.GetInteger(GLEnum.MinorVersion);
        return major > 4 || major == 4 && minor >= 3;
    }

    public int CompletedSamples => _completedSamples;
    public string Status => _status;
    public uint OutputTexture => _outputTexture;
    internal int SceneGeometryUploadCount => _sceneGeometryUploadCount;
    internal int LightingTextureUploadCount => _lightingTextureUploadCount;

    public NuxTComputeRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        if (!IsSupported(gl))
            throw new NotSupportedException("NuxT GPU tracing requires an OpenGL 4.3 compute-capable context.");

        _maxTextureSize = gl.GetInteger(GLEnum.MaxTextureSize);
        CompileProgram();
    }

    public bool Render(NuxTScene scene, NuxTCamera camera, int width, int height, NuxTSettings settings,
        bool transparentBackground = false, bool completeImmediately = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (width <= 0 || height <= 0 || width > _maxTextureSize || height > _maxTextureSize ||
            (long)width * height > MaxRenderPixels)
        {
            _status = "CPU fallback: render size exceeds GPU limit";
            return false;
        }

        camera = camera.Normalized();
        settings = settings.Normalized();
        bool sceneChanged = !ReferenceEquals(scene, _scene);
        bool geometryChanged = _scene is null ||
                               !ReferenceEquals(scene.GpuGeometryIdentity, _scene.GpuGeometryIdentity);
        if (ReferenceEquals(scene, _failedScene))
            return false;
        if (sceneChanged)
        {
            try
            {
                if (geometryChanged)
                    UploadScene(scene);
                else
                    UploadLighting(scene);
                _scene = scene;
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or OutOfMemoryException)
            {
                ReleaseSceneTextures();
                _scene = null;
                _hasRequest = false;
                _failedScene = scene;
                _status = $"CPU fallback: {exception.Message}";
                return false;
            }
        }

        bool requestChanged = sceneChanged || !_hasRequest || _width != width || _height != height ||
                              _camera != camera || _settings != settings ||
                              _transparentBackground != transparentBackground;
        if (requestChanged)
        {
            EnsureOutputTextures(width, height);
            _width = width;
            _height = height;
            _camera = camera;
            _settings = settings;
            _transparentBackground = transparentBackground;
            _completedSamples = 0;
            _hasRequest = true;
        }

        if (_completedSamples >= settings.Samples)
        {
            _status = "Complete · GPU";
            return true;
        }

        do
        {
            DispatchSample(scene, camera, width, height, settings, _completedSamples, transparentBackground);
            _completedSamples++;
        } while (completeImmediately && _completedSamples < settings.Samples);
        _status = _completedSamples >= settings.Samples ? "Complete · GPU" : "Tracing · GPU";
        return true;
    }

    private unsafe void CompileProgram()
    {
        uint shader = _gl.CreateShader(GLEnum.ComputeShader);
        try
        {
            string source = MineImatorSimplyRemade.core.mdl.Shader.LoadShaderSource("nuxt.comp");
            _gl.ShaderSource(shader, 1, [source], null);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
            if (compiled == 0)
                throw new InvalidOperationException($"NuxT compute shader compilation failed: {_gl.GetShaderInfoLog(shader)}");

            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, shader);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, GLEnum.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException($"NuxT compute program link failed: {_gl.GetProgramInfoLog(_program)}");
        }
        catch
        {
            if (_program != 0)
            {
                _gl.DeleteProgram(_program);
                _program = 0;
            }
            throw;
        }
        finally
        {
            _gl.DeleteShader(shader);
        }
    }

    private void UploadScene(NuxTScene scene)
    {
        int previousActiveTexture = _gl.GetInteger(GLEnum.ActiveTexture);
        int previousTexture = _gl.GetInteger(GLEnum.TextureBinding2D);
        bool previousBindingWasSceneTexture = Array.IndexOf(_sceneTextures, (uint)previousTexture) >= 0;
        int previousUnpackBuffer = _gl.GetInteger(GLEnum.PixelUnpackBufferBinding);
        int previousUnpackAlignment = _gl.GetInteger(GLEnum.UnpackAlignment);
        int previousUnpackRowLength = _gl.GetInteger(GLEnum.UnpackRowLength);
        int previousUnpackSkipPixels = _gl.GetInteger(GLEnum.UnpackSkipPixels);
        int previousUnpackSkipRows = _gl.GetInteger(GLEnum.UnpackSkipRows);
        try
        {
            _gl.BindBuffer(GLEnum.PixelUnpackBuffer, 0);
            _gl.PixelStore(GLEnum.UnpackAlignment, 1);
            _gl.PixelStore(GLEnum.UnpackRowLength, 0);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
            _gl.PixelStore(GLEnum.UnpackSkipRows, 0);
            UploadSceneData(scene);
            UploadLighting(scene);
            GLEnum error = _gl.GetError();
            if (error != GLEnum.NoError)
                throw new InvalidOperationException($"NuxT GPU data texture upload failed: {error}.");
            _sceneGeometryUploadCount++;
        }
        finally
        {
            _gl.PixelStore(GLEnum.UnpackAlignment, previousUnpackAlignment);
            _gl.PixelStore(GLEnum.UnpackRowLength, previousUnpackRowLength);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, previousUnpackSkipPixels);
            _gl.PixelStore(GLEnum.UnpackSkipRows, previousUnpackSkipRows);
            _gl.BindBuffer(GLEnum.PixelUnpackBuffer, (uint)previousUnpackBuffer);
            _gl.BindTexture(GLEnum.Texture2D, previousBindingWasSceneTexture ? 0u : (uint)previousTexture);
            _gl.ActiveTexture((GLEnum)previousActiveTexture);
        }
    }

    private void UploadSceneData(NuxTScene scene)
    {
        ReleaseSceneTextures();
        NuxTTriangle[] triangles = scene.GpuTriangles;
        var materialIds = new Dictionary<NuxTMaterial, int>();
        var materials = new List<NuxTMaterial>();
        var triangleMaterials = new int[triangles.Length];
        var textureOffsets = new Dictionary<NuxTTexture, int>();
        var textures = new List<NuxTTexture>();
        long texturePixelCount = 0;

        for (int i = 0; i < triangles.Length; i++)
            triangleMaterials[i] = GetMaterialId(triangles[i].Material);

        int[] instanceMaterialIds = new int[scene.GpuInstanceCount];
        Array.Fill(instanceMaterialIds, -1);
        for (int i = 0; i < instanceMaterialIds.Length; i++)
            if (scene.GetGpuMaterialOverride(i) is { } material)
                instanceMaterialIds[i] = GetMaterialId(material);

        if (texturePixelCount > int.MaxValue)
            throw new InvalidOperationException("NuxT texture data exceeds the addressable GPU image range.");

        int[] materialTextureReferences = new int[materials.Count * 12];
        Array.Fill(materialTextureReferences, -1);
        for (int i = 0; i < materials.Count; i++)
        {
            NuxTMaterial material = materials[i];
            WriteTextureReference(materialTextureReferences, i * 12, material.AlbedoTexture, textureOffsets);
            WriteTextureReference(materialTextureReferences, i * 12 + 4, material.EmissionTexture, textureOffsets);
            WriteTextureReference(materialTextureReferences, i * 12 + 8, material.AlphaTexture, textureOffsets);
        }

        int[] triangleIndices = scene.GpuTriangleIndices;
        int[] tlasInstanceIndices = scene.GpuInstanceIndices;
        UploadFloatTexture(NodeBoundsTexture, checked(scene.GpuNodeCount * 2), (index, row, offset) =>
        {
            NuxTBvhNode node = scene.GetGpuNode(index / 2);
            SetVector(row, offset, index % 2 == 0 ? node.Min : node.Max, 0f);
        });
        CheckUploadError("BVH bounds");
        UploadIntTexture(NodeDataTexture, scene.GpuNodeCount, 4, (index, row, offset) =>
        {
            NuxTBvhNode node = scene.GetGpuNode(index);
            row[offset] = node.Start;
            row[offset + 1] = node.Count;
            row[offset + 2] = node.Left;
            row[offset + 3] = node.Right;
        });
        CheckUploadError("BVH nodes");
        UploadIntTexture(TriangleIndexTexture, triangleIndices.Length, 1,
            (index, row, offset) => row[offset] = triangleIndices[index]);
        CheckUploadError("triangle indices");
        UploadFloatTexture(GeometryTexture, checked(triangles.Length * 8), (index, row, offset) =>
            SetVector(row, offset, GetTriangleGeometry(triangles[index / 8], index % 8)));
        CheckUploadError("triangle geometry");
        UploadIntTexture(TriangleMaterialTexture, triangleMaterials.Length, 1,
            (index, row, offset) => row[offset] = triangleMaterials[index]);
        CheckUploadError("triangle materials");
        UploadFloatTexture(MaterialTexture, checked(materials.Count * 11), (index, row, offset) =>
            SetVector(row, offset, GetMaterialValue(materials[index / 11], index % 11)));
        CheckUploadError("materials");
        UploadIntTexture(MaterialTextureReferences, checked(materials.Count * 3), 4, (index, row, offset) =>
            Array.Copy(materialTextureReferences, index * 4, row, offset, 4));
        CheckUploadError("material texture references");
        UploadTexturePixels(textures, checked((int)texturePixelCount));
        CheckUploadError("texture pixels");

        UploadFloatTexture(TlasBoundsTexture, checked(scene.GpuTlasNodeCount * 2), (index, row, offset) =>
        {
            NuxTBvhNode node = scene.GetGpuTlasNode(index / 2);
            SetVector(row, offset, index % 2 == 0 ? node.Min : node.Max, 0f);
        });
        CheckUploadError("TLAS bounds");
        UploadIntTexture(TlasDataTexture, scene.GpuTlasNodeCount, 4, (index, row, offset) =>
        {
            NuxTBvhNode node = scene.GetGpuTlasNode(index);
            row[offset] = node.Start;
            row[offset + 1] = node.Count;
            row[offset + 2] = node.Left;
            row[offset + 3] = node.Right;
        });
        CheckUploadError("TLAS nodes");
        UploadIntTexture(TlasInstanceIndexTexture, tlasInstanceIndices.Length, 1,
            (index, row, offset) => row[offset] = tlasInstanceIndices[index]);
        CheckUploadError("TLAS instance indices");
        UploadFloatTexture(InstanceTransformTexture, checked(scene.GpuInstanceCount * 6), (index, row, offset) =>
        {
            NuxTGpuInstance instance = scene.GetGpuInstance(index / 6, instanceMaterialIds[index / 6]);
            Matrix4x4 matrix = index % 6 < 3 ? instance.Transform : instance.InverseTransform;
            SetVector(row, offset, GetInstanceTransformValue(matrix, index % 3));
        });
        CheckUploadError("instance transforms");
        UploadIntTexture(InstanceInfoTexture, scene.GpuInstanceCount, 4, (index, row, offset) =>
        {
            NuxTGpuInstance instance = scene.GetGpuInstance(index, instanceMaterialIds[index]);
            row[offset] = instance.RootNode;
            row[offset + 1] = instance.MaterialIndex;
            row[offset + 2] = 0;
            row[offset + 3] = 0;
        });
        CheckUploadError("instance info");

        _nodeCount = scene.GpuNodeCount;
        _triangleCount = triangles.Length;
        _tlasNodeCount = scene.GpuTlasNodeCount;
        _instanceCount = scene.GpuInstanceCount;
        void RegisterTexture(NuxTTexture? texture)
        {
            if (texture is null || textureOffsets.ContainsKey(texture))
                return;
            textureOffsets.Add(texture, checked((int)texturePixelCount));
            textures.Add(texture);
            texturePixelCount = checked(texturePixelCount + texture.Width * (long)texture.Height);
        }

        int GetMaterialId(NuxTMaterial material)
        {
            if (materialIds.TryGetValue(material, out int materialId))
                return materialId;
            materialId = materials.Count;
            materialIds.Add(material, materialId);
            materials.Add(material);
            RegisterTexture(material.AlbedoTexture);
            RegisterTexture(material.EmissionTexture);
            RegisterTexture(material.AlphaTexture);
            return materialId;
        }
    }

    private void UploadLighting(NuxTScene scene)
    {
        int previousActiveTexture = _gl.GetInteger(GLEnum.ActiveTexture);
        int previousTexture = _gl.GetInteger(GLEnum.TextureBinding2D);
        bool previousBindingWasSceneTexture = Array.IndexOf(_sceneTextures, (uint)previousTexture) >= 0;
        int previousUnpackBuffer = _gl.GetInteger(GLEnum.PixelUnpackBufferBinding);
        int previousUnpackAlignment = _gl.GetInteger(GLEnum.UnpackAlignment);
        int previousUnpackRowLength = _gl.GetInteger(GLEnum.UnpackRowLength);
        int previousUnpackSkipPixels = _gl.GetInteger(GLEnum.UnpackSkipPixels);
        int previousUnpackSkipRows = _gl.GetInteger(GLEnum.UnpackSkipRows);
        try
        {
            _gl.BindBuffer(GLEnum.PixelUnpackBuffer, 0);
            _gl.PixelStore(GLEnum.UnpackAlignment, 1);
            _gl.PixelStore(GLEnum.UnpackRowLength, 0);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
            _gl.PixelStore(GLEnum.UnpackSkipRows, 0);
            ReleaseSceneTexture(PointLightTexture);
            ReleaseSceneTexture(DirectionalLightTexture);
            NuxTLight[] pointLights = scene.GpuLights;
            NuxTDirectionalLight[] directionalLights = scene.GpuDirectionalLights;
            UploadFloatTexture(PointLightTexture, checked(pointLights.Length * 4), (index, row, offset) =>
                SetVector(row, offset, GetPointLightValue(pointLights[index / 4], index % 4)));
            CheckUploadError("point lights");
            UploadFloatTexture(DirectionalLightTexture, checked(directionalLights.Length * 3), (index, row, offset) =>
                SetVector(row, offset, GetDirectionalLightValue(directionalLights[index / 3], index % 3)));
            CheckUploadError("directional lights");
            _pointLightCount = pointLights.Length;
            _directionalLightCount = directionalLights.Length;
            _lightingTextureUploadCount++;
        }
        finally
        {
            _gl.PixelStore(GLEnum.UnpackAlignment, previousUnpackAlignment);
            _gl.PixelStore(GLEnum.UnpackRowLength, previousUnpackRowLength);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, previousUnpackSkipPixels);
            _gl.PixelStore(GLEnum.UnpackSkipRows, previousUnpackSkipRows);
            _gl.BindBuffer(GLEnum.PixelUnpackBuffer, (uint)previousUnpackBuffer);
            _gl.BindTexture(GLEnum.Texture2D, previousBindingWasSceneTexture ? 0u : (uint)previousTexture);
            _gl.ActiveTexture((GLEnum)previousActiveTexture);
        }
    }

    private int _nodeCount;
    private int _triangleCount;
    private int _tlasNodeCount;
    private int _instanceCount;
    private int _pointLightCount;
    private int _directionalLightCount;

    private static Vector4 GetTriangleGeometry(NuxTTriangle triangle, int field) => field switch
    {
        0 => new Vector4(triangle.A, 0f),
        1 => new Vector4(triangle.B, 0f),
        2 => new Vector4(triangle.C, 0f),
        3 => new Vector4(triangle.NormalA, 0f),
        4 => new Vector4(triangle.NormalB, 0f),
        5 => new Vector4(triangle.NormalC, 0f),
        6 => new Vector4(triangle.UvA.X, triangle.UvA.Y, triangle.UvB.X, triangle.UvB.Y),
        _ => new Vector4(triangle.UvC.X, triangle.UvC.Y, 0f, 0f)
    };

    private static Vector4 GetMaterialValue(NuxTMaterial material, int field)
    {
        Vector2 repeat = material.TextureRepeat == default ? Vector2.One : material.TextureRepeat;
        return field switch
        {
            0 => new Vector4(material.Albedo, material.Alpha),
            1 => new Vector4(material.Emission, 0f),
            2 => new Vector4(material.Metallic, material.Roughness, material.AlphaClipThreshold, 0f),
            3 => new Vector4(material.TextureOffset.X, material.TextureOffset.Y, repeat.X, repeat.Y),
            4 => new Vector4(material.TextureFrameScale, material.TextureFrameOffset,
                material.MirrorU ? 1f : 0f, material.MirrorV ? 1f : 0f),
            5 => new Vector4(material.AlphaClip ? 1f : 0f, material.Unlit ? 1f : 0f,
                material.EmissionIndirectOnly ? 1f : 0f, material.HasColorAdjustments ? 1f : 0f),
            6 => new Vector4(material.MultiplyHsv, 0f),
            7 => new Vector4(material.AddRgb, 0f),
            8 => new Vector4(material.AddHsv, 0f),
            9 => new Vector4(material.SubtractRgb, 0f),
            _ => new Vector4(material.SubtractHsv, 0f)
        };
    }

    private static Vector4 GetPointLightValue(NuxTLight light, int field) => field switch
    {
        0 => new Vector4(light.Position, 0f),
        1 => new Vector4(light.Direction, 0f),
        2 => new Vector4(light.Color, light.Energy),
        _ => new Vector4(light.Range, light.SpotInnerCosine, light.SpotOuterCosine, light.CastShadows ? 1f : 0f)
    };

    private static Vector4 GetDirectionalLightValue(NuxTDirectionalLight light, int field) => field switch
    {
        0 => new Vector4(light.DirectionToLight, 0f),
        1 => new Vector4(light.Radiance, 0f),
        _ => new Vector4(light.CastShadows ? 1f : 0f, 0f, 0f, 0f)
    };

    private static Vector4 GetInstanceTransformValue(Matrix4x4 matrix, int field) => field switch
    {
        0 => new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41),
        1 => new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42),
        _ => new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43)
    };

    private static void WriteTextureReference(int[] references, int offset, NuxTTexture? texture,
        Dictionary<NuxTTexture, int> offsets)
    {
        if (texture is null)
            return;
        references[offset] = offsets[texture];
        references[offset + 1] = texture.Width;
        references[offset + 2] = texture.Height;
        references[offset + 3] = 0;
    }

    private unsafe void UploadFloatTexture(int slot, int texelCount, Action<int, float[], int> writeTexel)
    {
        int width = CreateDataTexture(slot, texelCount, InternalFormat.Rgba32f, GLEnum.Rgba, GLEnum.Float);
        int height = GetTextureHeight(texelCount, width);
        int rowsPerBatch = GetUploadRowsPerBatch(width, sizeof(float) * 4);
        for (int y = 0; y < height && y * width < texelCount; y += rowsPerBatch)
        {
            int rows = Math.Min(rowsPerBatch, height - y);
            float[] batch = new float[checked(width * rows * 4)];
            int texelStart = checked(y * width);
            int texels = Math.Min(checked(width * rows), texelCount - texelStart);
            for (int i = 0; i < texels; i++)
                writeTexel(texelStart + i, batch, i * 4);
            _gl.BindTexture(GLEnum.Texture2D, _sceneTextures[slot]);
            fixed (float* pointer = batch)
                _gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, y, (uint)width, (uint)rows,
                    GLEnum.Rgba, GLEnum.Float, pointer);
        }
    }

    private unsafe void UploadIntTexture(int slot, int texelCount, int components, Action<int, int[], int> writeTexel)
    {
        int width = CreateDataTexture(slot, texelCount,
            components == 1 ? InternalFormat.R32i : InternalFormat.Rgba32i,
            components == 1 ? GLEnum.RedInteger : GLEnum.RgbaInteger, GLEnum.Int);
        int height = GetTextureHeight(texelCount, width);
        int rowsPerBatch = GetUploadRowsPerBatch(width, sizeof(int) * components);
        for (int y = 0; y < height && y * width < texelCount; y += rowsPerBatch)
        {
            int rows = Math.Min(rowsPerBatch, height - y);
            int[] batch = new int[checked(width * rows * components)];
            int texelStart = checked(y * width);
            int texels = Math.Min(checked(width * rows), texelCount - texelStart);
            for (int i = 0; i < texels; i++)
                writeTexel(texelStart + i, batch, i * components);
            _gl.BindTexture(GLEnum.Texture2D, _sceneTextures[slot]);
            fixed (int* pointer = batch)
                _gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, y, (uint)width, (uint)rows,
                    components == 1 ? GLEnum.RedInteger : GLEnum.RgbaInteger, GLEnum.Int, pointer);
        }
    }

    private unsafe void UploadTexturePixels(List<NuxTTexture> textures, int pixelCount)
    {
        int width = CreateDataTexture(TexturePixelsTexture, pixelCount, InternalFormat.Rgba8, GLEnum.Rgba,
            GLEnum.UnsignedByte);
        int height = GetTextureHeight(pixelCount, width);
        int rowsPerBatch = GetUploadRowsPerBatch(width, 4);
        int textureIndex = 0;
        int sourcePixel = 0;
        for (int y = 0; y < height && y * width < pixelCount; y += rowsPerBatch)
        {
            int rows = Math.Min(rowsPerBatch, height - y);
            byte[] batch = new byte[checked(width * rows * 4)];
            int batchPixelCount = Math.Min(checked(width * rows), pixelCount - y * width);
            int batchPixel = 0;
            while (batchPixel < batchPixelCount)
            {
                NuxTTexture texture = textures[textureIndex];
                int texturePixelCount = texture.Pixels.Length / 4;
                int copyPixels = Math.Min(batchPixelCount - batchPixel, texturePixelCount - sourcePixel);
                System.Buffer.BlockCopy(texture.Pixels, sourcePixel * 4, batch, batchPixel * 4, copyPixels * 4);
                batchPixel += copyPixels;
                sourcePixel += copyPixels;
                if (sourcePixel == texturePixelCount)
                {
                    textureIndex++;
                    sourcePixel = 0;
                }
            }
            _gl.BindTexture(GLEnum.Texture2D, _sceneTextures[TexturePixelsTexture]);
            fixed (byte* pointer = batch)
                _gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, y, (uint)width, (uint)rows,
                    GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
        }
    }

    private static int GetUploadRowsPerBatch(int width, int bytesPerTexel) =>
        Math.Max(1, Math.Min(MaxUploadChunkRows, MaxUploadChunkBytes / checked(width * bytesPerTexel)));

    private void CheckUploadError(string stage)
    {
        GLEnum error = _gl.GetError();
        if (error != GLEnum.NoError)
            throw new InvalidOperationException($"NuxT {stage} texture upload failed: {error}.");
    }

    private unsafe int CreateDataTexture(int slot, int texelCount, InternalFormat internalFormat, GLEnum format,
        GLEnum type)
    {
        (int width, int height) = GetTextureDimensions(texelCount);
        _textureWidths[slot] = width;
        _gl.GenTextures(1, out _sceneTextures[slot]);
        _gl.BindTexture(GLEnum.Texture2D, _sceneTextures[slot]);
        _gl.TexImage2D(GLEnum.Texture2D, 0, internalFormat, (uint)width, (uint)height, 0, format, type, null);
        GLEnum error = _gl.GetError();
        if (error != GLEnum.NoError)
            throw new InvalidOperationException($"NuxT texture {slot} allocation ({width}x{height}) failed: {error}.");
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return width;
    }

    private (int Width, int Height) GetTextureDimensions(int texelCount)
    {
        long count = Math.Max(1, texelCount);
        if (_maxTextureSize <= 0 || count > (long)_maxTextureSize * _maxTextureSize)
            throw new InvalidOperationException("NuxT scene exceeds the GPU 2D texture dimensions.");
        int width = Math.Min(_maxTextureSize, (int)Math.Ceiling(Math.Sqrt(count)));
        return (width, checked((int)((count + width - 1) / width)));
    }

    private static int GetTextureHeight(int texelCount, int width) =>
        checked((int)((Math.Max(1L, texelCount) + width - 1) / width));

    private void ReleaseSceneTextures()
    {
        for (int i = 0; i < _sceneTextures.Length; i++)
            ReleaseSceneTexture(i);
    }

    private void ReleaseSceneTexture(int slot)
    {
        if (_sceneTextures[slot] != 0)
            _gl.DeleteTexture(_sceneTextures[slot]);
        _sceneTextures[slot] = 0;
        _textureWidths[slot] = 1;
    }

    private static void SetVector(float[] values, int offset, Vector3 value, float w)
    {
        values[offset] = value.X;
        values[offset + 1] = value.Y;
        values[offset + 2] = value.Z;
        values[offset + 3] = w;
    }

    private static void SetVector(float[] values, int offset, Vector4 value)
    {
        values[offset] = value.X;
        values[offset + 1] = value.Y;
        values[offset + 2] = value.Z;
        values[offset + 3] = value.W;
    }

    private unsafe void EnsureOutputTextures(int width, int height)
    {
        if (_accumulationTexture != 0 && _width == width && _height == height)
            return;

        if (_accumulationTexture != 0)
            _gl.DeleteTexture(_accumulationTexture);
        if (_outputTexture != 0)
            _gl.DeleteTexture(_outputTexture);
        _gl.GenTextures(1, out _accumulationTexture);
        _gl.GenTextures(1, out _outputTexture);
        int previousTexture = _gl.GetInteger(GLEnum.TextureBinding2D);
        try
        {
            _gl.BindTexture(GLEnum.Texture2D, _accumulationTexture);
            _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba32f, (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.Float, null);
            _gl.BindTexture(GLEnum.Texture2D, _outputTexture);
            _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte, null);
            foreach (uint texture in new[] { _accumulationTexture, _outputTexture })
            {
                _gl.BindTexture(GLEnum.Texture2D, texture);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }
        }
        finally
        {
            _gl.BindTexture(GLEnum.Texture2D, (uint)previousTexture);
        }
    }

    private void DispatchSample(NuxTScene scene, NuxTCamera camera, int width, int height, NuxTSettings settings,
        int sampleIndex, bool transparentBackground)
    {
        _gl.UseProgram(_program);
        for (int i = 0; i < _sceneTextures.Length; i++)
        {
            _gl.ActiveTexture((GLEnum)((int)GLEnum.Texture0 + i));
            _gl.BindTexture(GLEnum.Texture2D, _sceneTextures[i]);
        }
        _gl.BindImageTexture(0, _accumulationTexture, 0, false, 0, GLEnum.ReadWrite, InternalFormat.Rgba32f);
        _gl.BindImageTexture(1, _outputTexture, 0, false, 0, GLEnum.WriteOnly, InternalFormat.Rgba8);
        SetUniform("uNodeBounds", NodeBoundsTexture);
        SetUniform("uNodeData", NodeDataTexture);
        SetUniform("uTriangleIndices", TriangleIndexTexture);
        SetUniform("uGeometry", GeometryTexture);
        SetUniform("uTriangleMaterials", TriangleMaterialTexture);
        SetUniform("uMaterials", MaterialTexture);
        SetUniform("uMaterialTextureRefs", MaterialTextureReferences);
        SetUniform("uPointLights", PointLightTexture);
        SetUniform("uDirectionalLights", DirectionalLightTexture);
        SetUniform("uTexturePixels", TexturePixelsTexture);
        SetUniform("uTlasBounds", TlasBoundsTexture);
        SetUniform("uTlasData", TlasDataTexture);
        SetUniform("uTlasInstanceIndices", TlasInstanceIndexTexture);
        SetUniform("uInstanceTransforms", InstanceTransformTexture);
        SetUniform("uInstanceInfo", InstanceInfoTexture);
        SetUniform("uNodeBoundsWidth", _textureWidths[NodeBoundsTexture]);
        SetUniform("uNodeDataWidth", _textureWidths[NodeDataTexture]);
        SetUniform("uTriangleIndicesWidth", _textureWidths[TriangleIndexTexture]);
        SetUniform("uGeometryWidth", _textureWidths[GeometryTexture]);
        SetUniform("uTriangleMaterialsWidth", _textureWidths[TriangleMaterialTexture]);
        SetUniform("uMaterialsWidth", _textureWidths[MaterialTexture]);
        SetUniform("uMaterialTextureRefsWidth", _textureWidths[MaterialTextureReferences]);
        SetUniform("uPointLightsWidth", _textureWidths[PointLightTexture]);
        SetUniform("uDirectionalLightsWidth", _textureWidths[DirectionalLightTexture]);
        SetUniform("uTexturePixelsWidth", _textureWidths[TexturePixelsTexture]);
        SetUniform("uTlasBoundsWidth", _textureWidths[TlasBoundsTexture]);
        SetUniform("uTlasDataWidth", _textureWidths[TlasDataTexture]);
        SetUniform("uTlasInstanceIndicesWidth", _textureWidths[TlasInstanceIndexTexture]);
        SetUniform("uInstanceTransformsWidth", _textureWidths[InstanceTransformTexture]);
        SetUniform("uInstanceInfoWidth", _textureWidths[InstanceInfoTexture]);
        SetUniform("uWidth", width);
        SetUniform("uHeight", height);
        SetUniform("uSampleIndex", sampleIndex);
        SetUniform("uTransparentBackground", transparentBackground ? 1 : 0);
        SetUniform("uSeed", settings.Seed);
        SetUniform("uMaxBounces", settings.MaxBounces);
        SetUniform("uNodeCount", _nodeCount);
        SetUniform("uTriangleCount", _triangleCount);
        SetUniform("uTlasNodeCount", _tlasNodeCount);
        SetUniform("uInstanceCount", _instanceCount);
        SetUniform("uPointLightCount", _pointLightCount);
        SetUniform("uDirectionalLightCount", _directionalLightCount);
        SetUniform("uExposure", settings.Exposure);
        SetUniform("uFov", camera.VerticalFovRadians);
        SetUniform("uCameraPosition", camera.Position);
        SetUniform("uCameraForward", camera.Forward);
        SetUniform("uCameraRight", camera.Right);
        SetUniform("uCameraUp", camera.Up);
        SetUniform("uEnvironment", scene.EnvironmentColor);
        SetUniform("uAmbient", scene.AmbientLight);
        _gl.DispatchCompute((uint)((width + 7) / 8), (uint)((height + 7) / 8), 1);
        _gl.MemoryBarrier((uint)(GLEnum.ShaderImageAccessBarrierBit | GLEnum.TextureFetchBarrierBit));
    }

    private void SetUniform(string name, int value) => _gl.Uniform1(_gl.GetUniformLocation(_program, name), value);
    private void SetUniform(string name, float value) => _gl.Uniform1(_gl.GetUniformLocation(_program, name), value);
    private void SetUniform(string name, Vector3 value) => _gl.Uniform3(_gl.GetUniformLocation(_program, name), value.X, value.Y, value.Z);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_program != 0)
            _gl.DeleteProgram(_program);
        ReleaseSceneTextures();
        if (_accumulationTexture != 0)
            _gl.DeleteTexture(_accumulationTexture);
        if (_outputTexture != 0)
            _gl.DeleteTexture(_outputTexture);
        _status = "Disposed";
    }
}