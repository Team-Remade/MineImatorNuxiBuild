using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using GlmSharp;
using ImageMagick;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using ZstdSharp;

internal static partial class Program
{
    private static byte[] CachePayload(int format, int buffersPerDepth = 12, int sheetHeight = 0,
        int slot = 0, int palette = 0, uint color = 0xffffffff, int tile = 0)
    {
        int type = slot % (format is 6 or 7 ? 5 : buffersPerDepth);
        int columns = format != 2 && type == 1 ? 16 : format != 2 && type == 2 ? 8 : 32;
        if (sheetHeight == 0) sheetHeight = format == 2 ? 32 : type == 1 ? 3 : type == 2 ? 5 : 48;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        void Long(long value) => writer.Write(BinaryPrimitives.ReverseEndianness(value));
        void Int(int value) => writer.Write(BinaryPrimitives.ReverseEndianness(value));
        writer.Write((byte)format);
        if (format != 2)
        {
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)4));
            writer.Write(Encoding.UTF8.GetBytes("26.3"));
        }
        Long(3); Long(2); Long(1);
        bool modern = format is 6 or 7;
        if (modern)
        {
            Int(1); Int(1); Int(1);
            writer.Write((byte)1);
            writer.Write(BinaryPrimitives.ReverseEndianness(1UL << slot));
        }
        int count = modern ? 1 : buffersPerDepth * 3;
        for (int buffer = 0; buffer < count; buffer++)
        {
            if (!modern)
            {
                Long(buffer == slot ? 1 : 0);
                if (buffer != slot) continue;
            }
            Long(4); Long(6);
            using var raw = new MemoryStream();
            using (var vertices = new BinaryWriter(raw, Encoding.UTF8, true))
            {
                (float X, float Y, float U, float V)[] corners =
                    [(0, 0, 0, 1), (0, 16, 0, 0), (16, 16, 1, 0), (16, 0, 1, 1)];
                foreach (var corner in corners)
                {
                    if (format == 2) { vertices.Write(corner.X); vertices.Write(0f); vertices.Write(corner.Y); }
                    else { vertices.Write(0f); vertices.Write(32 - corner.X); vertices.Write(corner.Y); }
                    uint normal = format == 2 ? 0x007f007fu : 0x007f7f00u;
                    float u = (tile % columns + corner.U) / columns;
                    float v = (tile / columns + corner.V) / sheetHeight;
                    if (modern)
                    {
                        vertices.Write(u); vertices.Write(v);
                        vertices.Write(normal | ((uint)palette << 24));
                    }
                    else
                    {
                        vertices.Write(normal); vertices.Write(color);
                        vertices.Write(u); vertices.Write(v);
                    }
                    vertices.Write(0x00008003u);
                    vertices.Write(0x007f7ffeu);
                }
            }
            int stride = modern ? 32 : 36;
            byte[] vertexBytes = raw.ToArray();
            WriteArray(vertexBytes, 4, stride);
            byte[] indices = new byte[24];
            uint[] values = [0, 2, 1, 0, 3, 2];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(indices.AsSpan(i * 4), values[i]);
            WriteArray(indices, 6, 4);
            if (modern)
            {
                writer.Write((byte)1);
                Int(17); Int(-20); Int(30);
                WriteArray(new byte[8], 2, 4);
            }
        }
        return output.ToArray();

        void WriteArray(byte[] bytes, int elements, int stride)
        {
            if (format != 7) { writer.Write(bytes); return; }
            for (int b = 0; b < stride; b++)
            for (int i = 0; i < elements; i++)
                writer.Write(bytes[i * stride + b]);
        }
    }

    private static byte[] CustomMeshCachePayload()
    {
        byte[] bytes = CachePayload(2)[..(49 + 4 * 36 + 6 * 4)];
        for (int axis = 0; axis < 3; axis++)
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1 + axis * 8), 1);
        for (int vertex = 0; vertex < 4; vertex++)
        {
            int offset = 49 + vertex * 36;
            float x = BitConverter.ToSingle(bytes, offset);
            float u = BitConverter.ToSingle(bytes, offset + 20);
            float v = BitConverter.ToSingle(bytes, offset + 24);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), x - 16);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4), -24);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 20), u * 32);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 24), v * 32 - 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 28), 0);
        }
        return bytes;
    }

    private static string CacheFile(Fixture f, byte[] payload, bool raw = false)
    {
        string path = f.NewPath(".schematic.meshcache");
        if (raw)
            File.WriteAllBytes(path, payload);
        else if (payload[0] == 7)
        {
            using var compressor = new Compressor();
            File.WriteAllBytes(path, compressor.Wrap(payload).ToArray());
        }
        else
        {
            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionLevel.Fastest);
            gzip.Write(payload);
        }
        return path;
    }

    private static void CheckCacheQuad(MeshCacheData data, string texture = "block/stone")
    {
        Equal(2, data.Width, "Cache width");
        Equal(1, data.Height, "Cache height");
        Equal(3, data.Length, "Cache length");
        var batch = data.Batches.Single();
        Equal(texture, batch.Material.Texture, "Atlas tile name");
        Equal(6, batch.Vertices.Count, "Both indexed triangles retained");
        Near(new vec3(-1, 0, -1.5f), batch.Vertices[0], "Centered cache origin and block scale");
        Near(new vec3(-1, 1, -1.5f), batch.Vertices[1], "Source Z-up becomes Y-up");
        Near(new vec3(0, 1, -1.5f), batch.Vertices[2], "Source horizontal axes");
        Near(-vec3.UnitZ, batch.Normals[0], "Packed normal and coordinate conversion");
        vec3 normal = vec3.Cross(batch.Vertices[1] - batch.Vertices[0], batch.Vertices[2] - batch.Vertices[0]);
        Check(vec3.Dot(normal, batch.Normals[0]) > .99f, "Converted triangle winding faces outwards");
        Near(0, batch.TexCoords[0].x, "Local tile UV left");
        Near(1, batch.TexCoords[0].y, "Local tile UV bottom");
        Near(0, batch.TexCoords[1].y, "Local tile UV top");
        Near(1, batch.TexCoords[2].x, "Local tile UV right");
        Equal(128, batch.Material.Emission, "Packed emissive byte");
        Equal(3, batch.Material.Wave, "Packed wind flags");
    }

    private static void MeshCacheCurrent(Fixture f)
    {
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(7))));
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(6))));
        Equal("block/oak_sign", MeshCacheData.Load(CacheFile(f, CachePayload(7, slot: 1)))
            .Batches.Single().Material.Texture, "32-pixel sheet layout");
        Equal("block/straw_bed", MeshCacheData.Load(CacheFile(f, CachePayload(7, slot: 2)))
            .Batches.Single().Material.Texture, "64-pixel sheet layout");
        var tinted = MeshCacheData.Load(CacheFile(f, CachePayload(7, palette: 7))).Batches.Single();
        Equal(BiomeTintKind.Birch, tinted.Material.Biome, "Current palette tint");
        var wet = MeshCacheData.Load(CacheFile(f, CachePayload(7, sheetHeight: 2, slot: 14))).Batches.Single();
        Equal("block/water_still", wet.Material.Texture, "Water uses animated sheet");
        Equal(2, wet.Material.Depth, "Translucent depth retained");
        var animated = MeshCacheData.Load(CacheFile(f, CachePayload(7, sheetHeight: 2, slot: 3, tile: 2))).Batches.Single();
        Equal("block/lava_still", animated.Material.Texture, "Animated tile lookup");
        Near(1, animated.TexCoords[0].y, "Animated UVs normalized to a single frame");
        byte[] valid = CachePayload(7);
        byte[] noNormal = (byte[])valid.Clone();
        noNormal[68 + 20 * 4] = 127;
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, noNormal)));
        byte[] sparse = [.. valid, 0, 0];
        BinaryPrimitives.WriteInt64BigEndian(sparse.AsSpan(15), 130);
        BinaryPrimitives.WriteInt32BigEndian(sparse.AsSpan(31), 3);
        var chunked = MeshCacheData.Load(CacheFile(f, sparse));
        Equal(130, chunked.Width, "Non-square chunk grid");
        Equal(6, chunked.Batches.Single().Vertices.Count, "Sparse chunks retain only active geometry");
        foreach (int length in new[] { 1, 6, 30, 45, valid.Length - 1 })
            Reject(() => MeshCacheData.Load(CacheFile(f, valid[..length])), "Truncated current cache accepted");
        void Mutation(int offset, byte value)
        {
            byte[] bytes = (byte[])valid.Clone();
            bytes[offset] = value;
            Reject(() => MeshCacheData.Load(CacheFile(f, bytes)), "Invalid current cache accepted");
        }
        Mutation(0, 99);
        Mutation(3, (byte)'/');
        Mutation(30, 0);
        Mutation(34, 2);
        Mutation(43, 2);
        Mutation(44, 0x80);
        Mutation(51, 0);
        Mutation(59, 0);
        byte[] badIndex = (byte[])valid.Clone();
        badIndex[196] = 4;
        Reject(() => MeshCacheData.Load(CacheFile(f, badIndex)), "Out-of-range shuffled index accepted");
        byte[] nonFinite = (byte[])valid.Clone();
        // X is the first float in each 32-byte vertex; byte planes 2/3 carry its exponent.
        nonFinite[68 + 2 * 4] = 0x80;
        nonFinite[68 + 3 * 4] = 0x7f;
        Reject(() => MeshCacheData.Load(CacheFile(f, nonFinite)), "NaN cache position accepted");
        Reject(() => MeshCacheData.Load(CacheFile(f, [.. valid, 0])), "Trailing cache data accepted");
    }

    private static void MeshCacheLegacy(Fixture f)
    {
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(2))));
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(2), raw: true)));
        byte[] custom = CustomMeshCachePayload();
        foreach (bool raw in new[] { true, false })
        {
            var batch = MeshCacheData.Load(CacheFile(f, custom, raw)).Batches.Single();
            Equal("", batch.Material.Texture, "Custom caches do not use Minecraft atlas textures");
            Equal(6, batch.Vertices.Count, "Custom indexed triangles retained");
            Near(new vec3(-2, 0, 1.5f), batch.Vertices[0], "Legacy scenery placement includes its default rotation point");
            Near(new vec3(-2, 1, 1.5f), batch.Vertices[1], "Source-space rotation preserves custom mesh height");
            Near(new vec3(-2, 1, .5f), batch.Vertices[2], "Custom cache preserves authored offsets relative to the default pivot");
            Near(-vec3.UnitX, batch.Normals[0], "Custom normals follow the converted source-space rotation");
            Near(-1, batch.TexCoords[1].y, "Negative custom V retained");
            Near(1, batch.TexCoords[2].x, "Custom UVs not expanded from a block tile");
            Check(vec3.Dot(vec3.Cross(batch.Vertices[1] - batch.Vertices[0],
                batch.Vertices[2] - batch.Vertices[0]), batch.Normals[0]) > .99f, "Custom outward winding");
        }
        var attachmentParent = new SceneObject();
        attachmentParent.SetLocalPosition(new vec3(4, 2, -3));
        attachmentParent.SetLocalRotation(new vec3(0, 0, MathF.PI / 2));
        attachmentParent.SetLocalScale(new vec3(2));
        var attachment = new SceneObject();
        attachment.SetLocalPosition(new vec3(0.5f, -1, 0.25f));
        attachment.SetLocalRotation(new vec3(0, MathF.PI / 2, 0));
        attachment.SetLocalScale(new vec3(1.5f, 0.75f, 2));
        attachmentParent.AddChild(attachment);
        Near(new vec3(6, 3, -2.5f), attachment.GetWorldPosition(),
            "Attached custom cache object follows its parent's translation, combined rotation and scale");
        string attachmentCache = CacheFile(f, custom, raw: true);
        var attachmentVertices = MeshCacheData.Load(attachmentCache).Batches.Single().Vertices;
        var referenceVertices = ReferenceCustomCacheVertices(attachmentCache);
        for (int i = 0; i < attachmentVertices.Count; i++)
            Near((attachment.GetWorldMatrix() * new vec4(referenceVertices[i], 1)).xyz,
                (attachment.GetWorldMatrix() * new vec4(attachmentVertices[i], 1)).xyz,
                "Attached vertex matches reference draw matrix and automatic scenery rotation point");
        foreach (int length in new[] { 1, 25, 48, custom.Length - 1 })
            Reject(() => MeshCacheData.Load(CacheFile(f, custom[..length], raw: true)), "Truncated raw cache accepted");
        byte[] badUv = CachePayload(2);
        BinaryPrimitives.WriteSingleLittleEndian(badUv.AsSpan(49 + 24), -.5f);
        Reject(() => MeshCacheData.Load(CacheFile(f, badUv, raw: true)), "Out-of-sheet block UV accepted");
        Reject(() => MeshCacheData.Load(CacheFile(f, CachePayload(2)[..custom.Length], raw: true)),
            "Incomplete block table mistaken for a custom mesh");
        foreach (int rows in new[] { 32, 48 })
            CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(2, 13, rows))));
        CheckCacheQuad(MeshCacheData.Load(CacheFile(f, CachePayload(4, 15, 48))));
        var grass = MeshCacheData.Load(CacheFile(f, CachePayload(2, slot: 2, color: 0x80402010))).Batches.Single();
        Equal(BiomeTintKind.Grass, grass.Material.Biome, "Legacy buffer biome category");
        Equal(0x80402010u, grass.Material.Color, "Legacy baked RGB and alpha retained");
        var birch = MeshCacheData.Load(CacheFile(f, CachePayload(4, 15, 48, slot: 24))).Batches.Single();
        Equal(BiomeTintKind.Birch, birch.Material.Biome, "Format-four depth and leaf buffer mapping");
        Equal(1, birch.Material.Depth, "Format-four cutout depth");
        Reject(() => MeshCacheData.Load(CacheFile(f, CachePayload(2, 11))), "Incomplete legacy buffer table accepted");
        byte[] valid = CachePayload(2);
        Reject(() => MeshCacheData.Load(CacheFile(f, valid[..^1])), "Truncated legacy table accepted");
        BinaryPrimitives.WriteInt64BigEndian(valid.AsSpan(33), long.MaxValue);
        Reject(() => MeshCacheData.Load(CacheFile(f, valid)), "Oversized legacy vertex array accepted");
    }

    private static void MeshCachePersistence(Fixture f)
    {
        string schematic = f.NewPath(".schematic");
        new MineImatorSimplyRemade.core.world.WorldVolume(
            new MineImatorSimplyRemade.core.world.WorldBounds(0, 0, 0, 1, 0, 2)).SaveSchematic(schematic);
        string cache = schematic + ".meshcache";
        File.Copy(CacheFile(f, CachePayload(7)), cache);
        File.SetLastWriteTimeUtc(schematic, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddMinutes(-1));
        Equal(cache, MeshCacheData.FindSidecar(schematic), "Fresh sidecar selected");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddMinutes(-3));
        Check(MeshCacheData.FindSidecar(schematic) == null, "Stale cache rejected");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddMinutes(-1));
        WithTextureSheetProject(f, manager =>
        {
            var asset = manager.AddAsset(schematic, ProjectAssetType.Other);
            string stored = manager.GetAssetFullPath(asset);
            Check(File.ReadAllBytes(stored + ".meshcache").SequenceEqual(File.ReadAllBytes(cache)),
                "Project asset copying retains the sidecar");
            Check(MeshCacheData.FindSidecar(stored) != null, "Project copy retains freshness");
            var root = new SceneObject { Name = "Cache scenery", SpawnCategory = "Scenery", SourceAssetPath = stored };
            var (manifest, directory) = f.Roundtrip(root);
            string extracted = Resource(directory, manifest.Root.SourceAssetPath);
            Check(MeshCacheData.FindSidecar(extracted) != null, "Archive sidecar survives with its timestamp");
            CheckCacheQuad(MeshCacheData.Load(extracted + ".meshcache"));
            Check(manager.RemoveAsset(asset), "Project asset removal");
            Check(!File.Exists(stored + ".meshcache"), "Removing schematic also removes its owned cache");

            string modelFolder = Path.Combine(f.Root, "cache-only-model");
            Directory.CreateDirectory(modelFolder);
            string miObject = Path.Combine(modelFolder, "model.miobject");
            string cacheOnly = Path.Combine(modelFolder, "part.schematic.meshcache");
            string modelTexture = Path.Combine(modelFolder, "skin.png");
            File.Copy(cache, cacheOnly);
            File.WriteAllText(modelTexture, "texture bytes");
            File.WriteAllText(miObject, """
                {"resources":[{"id":"terrain","type":"scenery","filename":"part.schematic"},
                               {"id":"skin","type":"texture","filename":"skin.png"}],
                 "templates":[{"id":"scenery","type":"scenery","scenery":"terrain","block":{"tex":"skin"}}],
                 "timelines":[{"id":"scene","type":"scenery","temp":"scenery"}]}
                """);
            var modelAsset = manager.AddAsset(miObject, ProjectAssetType.Model);
            string storedModel = manager.GetAssetFullPath(modelAsset);
            string storedCache = Path.Combine(Path.GetDirectoryName(storedModel)!, Path.GetFileName(cacheOnly));
            Check(File.Exists(storedCache), "Model import copies a cache when its referenced schematic is absent");
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(storedModel)!, Path.GetFileName(modelTexture))),
                "Model import keeps the scenery texture dependency");

            var modelRoot = new SceneObject { SourceAssetPath = miObject };
            var (modelManifest, modelDirectory) = f.Roundtrip(modelRoot);
            string archivedModel = Resource(modelDirectory, modelManifest.Root.SourceAssetPath);
            NxObjectResources.ValidateModelReferences(archivedModel, modelDirectory);
            var archivedDocument = JsonNode.Parse(File.ReadAllText(archivedModel))!.AsObject();
            string archivedScenery = archivedDocument["resources"]!.AsArray().OfType<JsonObject>()
                .Single(resource => resource["id"]!.GetValue<string>() == "terrain")["filename"]!.GetValue<string>();
            string archivedCache = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivedModel)!, archivedScenery));
            Check(File.Exists(archivedCache) && MeshCacheData.IsCache(archivedCache),
                "Portable model archive rewrites cache-only scenery to its surviving meshcache");
        });
        var standalone = new SceneObject { Name = "Standalone", SpawnCategory = "Scenery", SourceAssetPath = cache };
        var (standaloneManifest, standaloneDirectory) = f.Roundtrip(standalone);
        CheckCacheQuad(MeshCacheData.Load(Resource(standaloneDirectory, standaloneManifest.Root.SourceAssetPath)));
    }

    private static unsafe void MeshCacheGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Meshcache regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            using var f = new Fixture();
            uint texture = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, texture);
            byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255];
            fixed (byte* pointer = pixels)
                gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, 2, 2, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            TerrainAtlas.Textures["stone"] = texture;
            TerrainAtlas.TilePixels["stone"] = pixels;
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Gl = gl, Viewport = viewport };
            var roots = new List<SceneObject>();
            try
            {
                byte[]? referenceImage = null;
                foreach (int format in new[] { 2, 4, 6, 7 })
                {
                    string path = CacheFile(f, CachePayload(format, format == 4 ? 15 : 12, format == 2 ? 32 : 48));
                    var root = spawn.SpawnSchematicFromPath(path) ?? throw new InvalidOperationException("Standalone cache failed");
                    roots.Add(root);
                    Equal(path, root.SourceAssetPath, "Standalone source reference retained");
                    var mesh = root.Visuals.Single();
                    Equal(texture, mesh.TextureId, "Real atlas handle");
                    Equal((byte)8, mesh.AutoEmissionLevel, "Emission byte converted to app light level");
                    mesh.BaseDoubleSided = true;
                    root.ApplyMaterialSettingsToMeshes();
                    using (var copy = mesh.Clone())
                        Check(mesh.DoubleSided && copy.BaseDoubleSided, "Intrinsic two-sided geometry survives materials/cloning");
                    mesh.BaseDoubleSided = false;
                    root.ApplyMaterialSettingsToMeshes();
                    byte[] image = Draw(mesh);
                    referenceImage ??= image;
                    Check(image.Where((_, i) => i % 4 != 3).Any(v => v > 200), "Cache draws actual colored pixels");
                    // Camera looks along +Z, so screen X runs opposite model X.
                    Pixel(48, 48, 255, 0, 0);
                    Pixel(16, 48, 0, 255, 0);
                    Pixel(48, 16, 0, 0, 255);
                    Pixel(16, 16, 255, 255, 0);
                    void Pixel(int x, int y, byte r, byte g, byte b)
                    {
                        int p = (y * 64 + x) * 4;
                        Check(Math.Abs(image[p] - r) < 3 && Math.Abs(image[p + 1] - g) < 3 &&
                              Math.Abs(image[p + 2] - b) < 3, $"Format {format} UV pixel ({x},{y})");
                    }
                    string archive = f.NewPath(".nxObj");
                    NxObjectArchive.Export(archive, root);
                    SceneObject? restored = null;
                    WithTextureSheetProject(f, _ => restored = NxObjectArchive.Import(archive, viewport, spawn));
                    roots.Add(restored!);
                    Check(Draw(restored!.Visuals.Single()).SequenceEqual(image), "Portable cache reload renders identical pixels");
                }
                string missingSchematic = f.NewPath(".schematic");
                string adjacentCache = missingSchematic + ".meshcache";
                File.Copy(CacheFile(f, CachePayload(7)), adjacentCache);
                var source = MiDocument("""
                    {"resources":[{"id":"terrain","type":"scenery","filename":""}],
                     "templates":[{"id":"scenery","type":"scenery","scenery":"terrain"}],
                     "timelines":[{"id":"terrain","type":"scenery","temp":"scenery"}]}
                    """);
                source.Resources[0].Filename = Path.GetFileName(missingSchematic);
                source.DirectoryPath = Path.GetDirectoryName(missingSchematic)!;
                int sceneCount = viewport.SceneObjects.Count;
                var imported = MineImatorLoader.Instance.CreateSceneFromMiObject(source,
                    sceneryFactory: path => spawn.CreateSchematicPreview(path, "")).Children.Single();
                roots.Add(imported);
                Equal(sceneCount, viewport.SceneObjects.Count, "MI scenery loader does not register an extra scene root");
                Equal(adjacentCache, imported.SourceAssetPath, "Missing schematic uses surviving cache as portable source");
                Check(Draw(imported.Visuals.Single()).SequenceEqual(referenceImage!), "MI cache-only scenery renders exact source pixels");
                string importedArchive = f.NewPath(".nxObj");
                NxObjectArchive.Export(importedArchive, imported);
                SceneObject? importedRestored = null;
                WithTextureSheetProject(f, _ => importedRestored = NxObjectArchive.Import(importedArchive, viewport, spawn));
                roots.Add(importedRestored!);
                Check(Draw(importedRestored!.Visuals.Single()).SequenceEqual(referenceImage!),
                    "MI cache-only scenery archive reload renders identical pixels");

                string customPath = CacheFile(f, CustomMeshCachePayload(), raw: true);
                var customRoot = spawn.SpawnSchematicFromPath(customPath) ??
                    throw new InvalidOperationException("Raw custom cache import failed");
                roots.Add(customRoot);
                var customMesh = customRoot.Visuals.Single();
                Equal(0u, customMesh.TextureId, "Custom mesh starts without an unrelated atlas texture");
                byte[] untextured = Draw(customMesh, custom: true, customRoot.GetWorldMatrix());
                Check(untextured.Where((_, i) => i % 4 != 3).All(v => v > 250), "Custom mesh renders untextured");
                string customArchive = f.NewPath(".nxObj");
                NxObjectArchive.Export(customArchive, customRoot);
                SceneObject? restoredCustom = null;
                WithTextureSheetProject(f, _ => restoredCustom = NxObjectArchive.Import(customArchive, viewport, spawn));
                roots.Add(restoredCustom!);
                Check(Draw(restoredCustom!.Visuals.Single(), custom: true, restoredCustom.GetWorldMatrix()).SequenceEqual(untextured),
                    "Raw custom archive reload preserves orientation");
                string customTexture = f.NewPath(".png");
                using (var image = new MagickImage(pixels, new MagickReadSettings
                {
                    Width = 2, Height = 2, Format = MagickFormat.Rgba, Depth = 8
                }))
                    image.Write(customTexture, MagickFormat.Png32);
                customRoot.AlbedoTexturePath = customTexture;
                new PropertiesPanel { Gl = gl, Viewport = viewport }.LoadPendingAlbedoTextures([customRoot]);
                byte[] customTextured = Draw(customMesh, custom: true, customRoot.GetWorldMatrix());
                Check(customTextured.SequenceEqual(referenceImage!),
                    "Custom negative UVs preserve texture pixels through the converted legacy transform");
                string schematic = f.NewPath(".schem");
                File.WriteAllText(schematic, "Not NBT: a valid cache must avoid meshing the source.");
                File.SetLastWriteTimeUtc(schematic, DateTime.UtcNow.AddMinutes(-2));
                File.Copy(CacheFile(f, CachePayload(7)), schematic + ".meshcache");
                var sidecar = spawn.SpawnSchematicFromPath(schematic);
                Check(sidecar != null && sidecar.SourceAssetPath == schematic, "Sidecar is used without parsing schematic");
                roots.Add(sidecar!);
                int before = viewport.SceneObjects.Count;
                File.WriteAllText(schematic + ".meshcache", "Broken cache");
                Check(spawn.SpawnSchematicFromPath(schematic) == null, "Broken source and cache explicitly fail");
                Equal(before, viewport.SceneObjects.Count, "Failed imports leave the scene unchanged");
                string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
                typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, version);
                var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks",
                    BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                typeof(BlockRegistry).GetMethod("ParseBlockstate", BindingFlags.Static | BindingFlags.NonPublic,
                    [typeof(string), typeof(JsonObject), typeof(string)])!.Invoke(null,
                    ["stone", JsonNode.Parse(File.ReadAllText(Path.Combine(version, "blockstates", "stone.json")))!.AsObject(), ""]);
                blocks.Add("stone");
                var volume = new MineImatorSimplyRemade.core.world.WorldVolume(
                    new MineImatorSimplyRemade.core.world.WorldBounds(0, 0, 0, 0, 0, 0));
                volume.Set(0, 0, 0, "minecraft:stone");
                File.Delete(schematic);
                volume.SaveSchematic(schematic);
                File.SetLastWriteTimeUtc(schematic, DateTime.UtcNow.AddMinutes(-2));
                var fallback = spawn.SpawnSchematicFromPath(schematic);
                Check(fallback != null && fallback.Visuals.Sum(m => m.Vertices.Count) == 36,
                    "Corrupt cache falls back to actual schematic meshing");
                roots.Add(fallback!);
                File.Copy(CacheFile(f, CachePayload(7)), schematic + ".meshcache", true);
                File.SetLastWriteTimeUtc(schematic + ".meshcache", DateTime.UtcNow.AddMinutes(-3));
                var stale = spawn.SpawnSchematicFromPath(schematic);
                Check(stale != null && stale.Visuals.Sum(m => m.Vertices.Count) == 36,
                    "Stale cache uses schematic geometry, not the cached quad");
                roots.Add(stale!);

                byte[] Draw(Mesh mesh, bool custom = false, mat4? model = null)
                {
                    gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                    gl.Viewport(0, 0, 64, 64);
                    gl.Disable(GLEnum.Blend);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.DepthTest);
                    gl.ClearColor(0, 0, 0, 0);
                    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    mesh.Unlit = true;
                    var eye = custom ? new vec3(-4, .5f, 1) : new vec3(-.5f, .5f, -3);
                    var target = custom ? new vec3(-2, .5f, 1) : new vec3(-.5f, .5f, -1.5f);
                    var view = mat4.LookAt(eye, target, vec3.UnitY);
                    mesh.Render(model ?? mat4.Identity, view, mat4.Ortho(-.5f, .5f, -.5f, .5f, .1f, 10), useSceneLighting: false);
                    var image = new byte[64 * 64 * 4];
                    fixed (byte* pointer = image)
                        gl.ReadPixels(0, 0, 64, 64, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                    return image;
                }
            }
            finally
            {
                foreach (var root in roots)
                    foreach (var mesh in root.Visuals) mesh.Dispose();
                TerrainAtlas.Textures.Remove("stone");
                TerrainAtlas.TilePixels.Remove("stone");
                gl.DeleteTexture(texture);
            }
        }
        finally { glfw.DestroyWindow(window); glfw.Terminate(); }
    }

    private static unsafe void MeshCacheModelGraphics(string miObjectPath, string? imagePath)
    {
        Check(File.Exists(miObjectPath), $"MIObject exists: {miObjectPath}");
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(640, 480, "Meshcache model placement regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var loader = MineImatorLoader.Instance;
            loader.Initialize(gl);
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Gl = gl, Viewport = viewport };
            MiObject source = loader.LoadMiObject(miObjectPath) ??
                throw new InvalidDataException($"Could not load MIObject '{miObjectPath}'.");
            var warnings = new List<string>();
            SceneObject scene = loader.CreateSceneFromMiObject(source, warning: warnings.Add,
                sceneryFactory: path => spawn.CreateSchematicPreview(path, ""));
            var objects = new[] { scene }.Concat(scene.GetAllDescendants()).ToArray();
            var caches = objects.Where(obj => MeshCacheData.IsCache(obj.SourceAssetPath ?? "")).ToArray();
            Check(caches.Length >= 3, $"All supplied meshcaches load into the scene ({caches.Length})");
            Console.WriteLine($"MIObject import warnings: {warnings.Count}");

            new PropertiesPanel { Gl = gl, Viewport = viewport }.LoadPendingAlbedoTextures([scene]);
            // Frame only non-cache geometry: a displaced cache must not change its own test camera.
            var worldVertices = objects.Except(caches).Where(obj => obj.GetEffectiveVisibility()).SelectMany(obj =>
            {
                mat4 world = obj.GetWorldMatrix();
                return obj.Visuals.SelectMany(mesh => mesh.Vertices.Select(vertex =>
                    (world * new vec4(vertex, 1)).xyz));
            }).ToArray();
            Check(worldVertices.Length > 0, "Imported model has renderable geometry");
            var min = new vec3(worldVertices.Min(v => v.x), worldVertices.Min(v => v.y), worldVertices.Min(v => v.z));
            var max = new vec3(worldVertices.Max(v => v.x), worldVertices.Max(v => v.y), worldVertices.Max(v => v.z));
            vec3 center = (min + max) * .5f;
            float radius = Math.Max((max - min).Length * .55f, .5f);
            vec3 direction = new vec3(3.5f, 2.2f, 7).Normalized;
            var view = mat4.LookAt(center + direction * radius * 3, center, vec3.UnitY);
            float aspect = 640f / 480f;
            var projection = mat4.Ortho(-radius * aspect, radius * aspect, -radius, radius,
                .1f, radius * 10);
            gl.Viewport(0, 0, 640, 480);
            gl.Disable(GLEnum.Blend);
            gl.Enable(GLEnum.CullFace);
            gl.Enable(GLEnum.DepthTest);

            byte[] Render(IEnumerable<SceneObject> drawObjects)
            {
                gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                gl.ClearColor(0, 0, 0, 0);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                foreach (var obj in drawObjects)
                {
                    if (!obj.GetEffectiveVisibility()) continue;
                    foreach (var mesh in obj.Visuals)
                    {
                        mesh.Unlit = true;
                        mesh.IncludeInFog = false;
                        mesh.Render(obj.GetWorldMatrix(), view, projection, useSceneLighting: false);
                    }
                }
                var pixels = new byte[640 * 480 * 4];
                fixed (byte* pointer = pixels)
                    gl.ReadPixels(0, 0, 640, 480, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                return pixels;
            }

            byte[] image = Render(objects);
            var fullBounds = PixelBounds(image);
            Check(fullBounds.Count > 1000, $"Full outfit renders actual pixels ({fullBounds.Count})");
            var nonCaches = objects.Except(caches).ToArray();
            var bodyBounds = PixelBounds(Render(nonCaches));
            Check(bodyBounds.Count > 1000, $"Outfit body renders actual pixels ({bodyBounds.Count})");
            foreach (var cache in caches)
            {
                byte[] actual = Render([cache]);
                var bounds = PixelBounds(actual);
                Check(bounds.Count > 10, $"Cache '{cache.Name}' produces actual pixels");
                var mesh = cache.Visuals.Single();
                var original = mesh.Vertices.ToArray();
                try
                {
                    mesh.Vertices.Clear();
                    mesh.Vertices.AddRange(ReferenceCustomCacheVertices(cache.SourceAssetPath!));
                    mesh.Upload();
                    byte[] reference = Render([cache]);
                    Check(actual.SequenceEqual(reference),
                        $"Cache '{cache.Name}' attachment pixels match the independent reference draw transform");
                }
                finally
                {
                    mesh.Vertices.Clear();
                    mesh.Vertices.AddRange(original);
                    mesh.Upload();
                }
                Console.WriteLine($"Cache pixels: {cache.Name}: ({bounds.MinX},{bounds.MinY})-" +
                    $"({bounds.MaxX},{bounds.MaxY}), {bounds.Count} pixels; world origin {cache.GetWorldPosition()}, " +
                    $"parent {cache.Parent?.Name ?? "<root>"}, local position {cache.LocalPosition}, " +
                    $"rotation {cache.LocalRotation}, scale {cache.LocalScale}");
            }
            Console.WriteLine($"Outfit pixels: ({fullBounds.MinX},{fullBounds.MinY})-" +
                $"({fullBounds.MaxX},{fullBounds.MaxY}), body pixels: {bodyBounds.Count}");
            if (imagePath != null)
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using var rendered = new MagickImage(image, new MagickReadSettings
                {
                    Width = 640, Height = 480, Format = MagickFormat.Rgba, Depth = 8
                });
                rendered.Flip();
                rendered.Write(imagePath, MagickFormat.Png32);
                Console.WriteLine($"Rendered image: {Path.GetFullPath(imagePath)}");
            }

            static (int MinX, int MinY, int MaxX, int MaxY, int Count) PixelBounds(byte[] pixels)
            {
                int minX = 640, minY = 480, maxX = -1, maxY = -1, count = 0;
                for (int y = 0; y < 480; y++)
                for (int x = 0; x < 640; x++)
                {
                    if (pixels[(y * 640 + x) * 4 + 3] < 16) continue;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    count++;
                }
                return (minX, minY, maxX, maxY, count);
            }
        }
        finally { glfw.DestroyWindow(window); glfw.Terminate(); }
    }

    private static List<vec3> ReferenceCustomCacheVertices(string path)
    {
        using var file = File.OpenRead(path);
        using var payload = new MemoryStream();
        if (file.ReadByte() == 0x1f)
        {
            file.Position = 0;
            using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
            gzip.CopyTo(payload);
        }
        else
        {
            file.Position = 0;
            file.CopyTo(payload);
        }
        byte[] bytes = payload.ToArray();
        Equal((byte)2, bytes[0], "Reference fixture is a format-2 custom cache");
        long length = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(1));
        long width = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(9));
        long height = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(17));
        Equal(1L, BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(25)), "Reference fixture has one buffer");
        int vertices = checked((int)BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(33)));
        int indices = checked((int)BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(41)));
        // render_world_block's matrix_build(0, height, 0, 0, 0, 90, 1, 1, 1)
        // uses the transposed rotation. temp_update_rot_point supplies (width/2, length/2, 0).
        var legacy = new mat4(new vec4(0, -1, 0, 0), new vec4(1, 0, 0, 0),
            new vec4(0, 0, 1, 0), new vec4(0, height, 0, 1));
        var pivot = mat4.Translate(new vec3(-width * .5f, -length * .5f, 0));
        var axes = new mat4(new vec4(1, 0, 0, 0), new vec4(0, 0, 1, 0),
            new vec4(0, 1, 0, 0), new vec4(0, 0, 0, 1));
        var result = new List<vec3>(indices);
        for (int triangle = 0; triangle < indices; triangle += 3)
        foreach (int corner in new[] { 0, 2, 1 })
        {
            int index = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(49 + vertices * 36 + (triangle + corner) * 4)));
            int offset = 49 + index * 36;
            var position = new vec4(BitConverter.ToSingle(bytes, offset) / 16,
                BitConverter.ToSingle(bytes, offset + 4) / 16, BitConverter.ToSingle(bytes, offset + 8) / 16, 1);
            result.Add((axes * pivot * legacy * position).xyz);
        }
        return result;
    }
}
