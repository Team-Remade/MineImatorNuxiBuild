using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;
using ZstdSharp;

namespace MineImatorSimplyRemade.core.mdl;

internal sealed class MeshCacheData
{
    private const int MaxBytes = 512 * 1024 * 1024;
    private const int MaxVertices = 8_000_000;
    public int Width { get; private init; }
    public int Height { get; private init; }
    public int Length { get; private init; }
    public List<Batch> Batches { get; } = [];

    internal readonly record struct Material(string Texture, int Depth, BiomeTintKind Biome,
        uint Color, int Emission, int Wave);

    internal sealed class Batch(Material material)
    {
        public Material Material { get; } = material;
        public List<vec3> Vertices { get; } = [];
        public List<vec3> Normals { get; } = [];
        public List<vec2> TexCoords { get; } = [];
    }

    private readonly record struct Buffer(int Slot, byte[] Vertices, uint[] Indices);

    public static bool IsCache(string path) =>
        string.Equals(Path.GetExtension(path), ".meshcache", StringComparison.OrdinalIgnoreCase);

    public static bool IsSchematic(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".schem" or ".schematic";

    public static string? FindSidecar(string path)
    {
        if (!IsSchematic(path) || !File.Exists(path + ".meshcache")) return null;
        string cache = path + ".meshcache";
        if (File.GetLastWriteTimeUtc(cache) >= File.GetLastWriteTimeUtc(path)) return cache;
        Console.Error.WriteLine($"Ignoring stale meshcache '{cache}'; rebuilding the schematic.");
        return null;
    }

    public static MeshCacheData Load(string path)
    {
        using var source = File.OpenRead(path);
        if (source.Length < 4) throw new InvalidDataException("The meshcache header is truncated.");
        Span<byte> magic = stackalloc byte[4];
        source.ReadExactly(magic);
        source.Position = 0;
        bool zstd = magic.SequenceEqual(new byte[] { 0x28, 0xb5, 0x2f, 0xfd });
        bool gzip = magic[0] == 0x1f && magic[1] == 0x8b;
        if (!zstd && !gzip && magic[0] != 2)
            throw new InvalidDataException("Expected a gzip, Zstandard or uncompressed format-2 meshcache.");
        using Stream decompressor = zstd
            ? new DecompressionStream(source)
            : gzip ? new GZipStream(source, CompressionMode.Decompress) : source;
        using var output = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = decompressor.Read(chunk)) != 0)
        {
            if (output.Length + read > MaxBytes)
                throw new InvalidDataException("The decompressed meshcache exceeds 512 MiB.");
            output.Write(chunk, 0, read);
        }
        return Read(output.ToArray(), zstd);
    }

    private static MeshCacheData Read(byte[] data, bool zstd)
    {
        var reader = new CacheReader(data);
        int format = reader.Byte();
        if (zstd ? format != 7 : format is not (2 or 4 or 6))
            throw new InvalidDataException($"Unsupported meshcache format {format}.");
        string? version = null;
        if (format != 2)
        {
            int count = reader.UInt16();
            if (count is < 1 or > 64) throw new InvalidDataException("Invalid meshcache asset version.");
            version = new UTF8Encoding(false, true).GetString(reader.Bytes(count));
            if (version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
                throw new InvalidDataException("Invalid meshcache asset version.");
        }
        int length = reader.Dimension(), width = reader.Dimension(), height = reader.Dimension();
        var result = new MeshCacheData { Width = width, Height = height, Length = length };
        var buffers = new List<Buffer>();
        if (format is 6 or 7)
        {
            int dimX = reader.Int32(), dimY = reader.Int32(), dimZ = reader.Int32();
            if (dimX != (width + 63L) / 64 || dimY != (length + 63L) / 64 || dimZ != (height + 63L) / 64 ||
                (long)dimX * dimY > int.MaxValue / dimZ)
                throw new InvalidDataException("Invalid meshcache chunk dimensions.");
            int count = dimX * dimY * dimZ;
            reader.Require(count);
            for (int mesh = 0; mesh < count; mesh++)
            {
                int present = reader.Byte();
                if (present > 1) throw new InvalidDataException("Invalid meshcache chunk presence flag.");
                if (present == 0) continue;
                ulong mask = reader.UInt64();
                if (mask == 0 || (mask >> 15) != 0)
                    throw new InvalidDataException("Invalid meshcache buffer mask.");
                for (int slot = 0; slot < 15; slot++)
                {
                    if ((mask & (1UL << slot)) == 0) continue;
                    buffers.Add(ReadBuffer(reader, slot, 32, format == 7));
                    int positions = reader.Byte();
                    if (positions > 1) throw new InvalidDataException("Invalid meshcache block-position flag.");
                    if (positions != 0)
                    {
                        reader.Bytes(12);
                        reader.Bytes(checked(buffers[^1].Indices.Length / 3 * 4));
                    }
                }
            }
        }
        else
        {
            int slot = 0;
            while (reader.Remaining > 0)
            {
                if (slot == 45) throw new InvalidDataException("Too many meshcache buffers.");
                long count = reader.Int64();
                if (count < 0 || count > reader.Remaining / 16)
                    throw new InvalidDataException("Invalid meshcache submesh count.");
                for (long mesh = 0; mesh < count; mesh++)
                    buffers.Add(ReadBuffer(reader, slot, 36, false));
                slot++;
            }
            // Custom model caches use one buffer and a dummy 1x1x1 size, without a block atlas.
            bool customMesh = format == 2 && slot == 1 && width == 1 && length == 1 && height == 1;
            if (!customMesh && (format == 4 ? slot != 45 : slot is not (36 or 39)))
                throw new InvalidDataException("Incomplete meshcache buffer table.");
            var atlas = customMesh ? null : new Atlas(version);
            int buffersPerDepth = customMesh ? 12 : slot / 3;
            int sheetHeight = customMesh ? 1 : format == 4 ? atlas!.Sheets[0].Height :
                buffersPerDepth == 12 ? 32 : DetectLegacySheetHeight(buffers);
            result.BuildBatches(buffers, atlas, format, buffersPerDepth, sheetHeight);
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Trailing meshcache data.");
        if (format is 6 or 7)
        {
            var atlas = new Atlas(version);
            result.BuildBatches(buffers, atlas, format, 5, atlas.Sheets[0].Height);
        }
        if (result.Batches.Count == 0) throw new InvalidDataException("The meshcache contains no renderable triangles.");
        return result;
    }

    private static Buffer ReadBuffer(CacheReader reader, int slot, int stride, bool shuffled)
    {
        long vertices = reader.Int64(), indices = reader.Int64();
        if (vertices is <= 0 or > MaxVertices || indices is <= 0 or > MaxVertices || indices % 3 != 0 ||
            vertices * stride + indices * 4 > reader.Remaining)
            throw new InvalidDataException("Invalid or oversized meshcache vertex/index counts.");
        byte[] vertexData = reader.Array((int)vertices, stride, shuffled);
        byte[] indexData = reader.Array((int)indices, 4, shuffled);
        var indexValues = new uint[(int)indices];
        for (int i = 0; i < indexValues.Length; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(indexData.AsSpan(i * 4));
            if (value >= vertices) throw new InvalidDataException("A meshcache index is outside its vertex array.");
            indexValues[i] = value;
        }
        ReadOnlySpan<int> components = stride == 36 ? [0, 4, 8, 20, 24] : [0, 4, 8, 12, 16];
        for (int i = 0; i < vertices; i++)
        {
            int offset = i * stride;
            foreach (int component in components)
                if (!float.IsFinite(Float(vertexData, offset + component)))
                    throw new InvalidDataException("A meshcache vertex contains a non-finite position or UV.");
        }
        return new Buffer(slot, vertexData, indexValues);
    }

    private static int DetectLegacySheetHeight(List<Buffer> buffers)
    {
        int square = 0, tall = 0;
        foreach (var buffer in buffers)
        {
            if (buffer.Slot % 13 == 1) continue;
            for (int v = 0; v + 3 < Math.Min(buffer.Vertices.Length / 36, 1024); v += 4)
            {
                vec3 lo = new(float.MaxValue), hi = new(float.MinValue);
                vec2 uvLo = new(float.MaxValue), uvHi = new(float.MinValue);
                for (int c = 0; c < 4; c++)
                {
                    int offset = (v + c) * 36;
                    var p = new vec3(Float(buffer.Vertices, offset), Float(buffer.Vertices, offset + 4),
                        Float(buffer.Vertices, offset + 8));
                    var uv = new vec2(Float(buffer.Vertices, offset + 20), Float(buffer.Vertices, offset + 24));
                    lo = vec3.Min(lo, p); hi = vec3.Max(hi, p);
                    uvLo = vec2.Min(uvLo, uv); uvHi = vec2.Max(uvHi, uv);
                }
                vec3 span = hi - lo;
                int full = 0, flat = 0;
                for (int a = 0; a < 3; a++)
                {
                    if (MathF.Abs(span[a] - 16) < .001f) full++;
                    if (MathF.Abs(span[a]) < .001f) flat++;
                }
                if (full != 2 || flat != 1 || MathF.Abs(uvHi.x - uvLo.x - 1f / 32) > .00003f) continue;
                if (MathF.Abs(uvHi.y - uvLo.y - 1f / 32) < .00003f) square++;
                if (MathF.Abs(uvHi.y - uvLo.y - 1f / 48) < .00003f) tall++;
            }
        }
        if ((square == 0) == (tall == 0))
            throw new InvalidDataException("Cannot determine the legacy meshcache's 32x32 or 32x48 texture layout.");
        return tall > 0 ? 48 : 32;
    }

    private void BuildBatches(List<Buffer> buffers, Atlas? atlas, int format, int buffersPerDepth, int sheetHeight)
    {
        bool customMesh = atlas == null;
        var batches = new Dictionary<Material, Batch>();
        int total = 0;
        foreach (var buffer in buffers)
        {
            bool oldVertex = format is 2 or 4;
            int stride = oldVertex ? 36 : 32;
            int type = buffer.Slot % buffersPerDepth;
            int sheet;
            int tintIndex = 0;
            if (buffersPerDepth == 5) sheet = type >= 3 ? 3 : type;
            else if (buffersPerDepth == 15)
            {
                sheet = type < 4 ? type : type == 14 ? 3 : 0;
                tintIndex = type is >= 4 and <= 13 ? type - 3 : 0;
                if (tintIndex >= 4) tintIndex++;
            }
            else
            {
                sheet = type == 1 || type == buffersPerDepth - 1 ? 3 : 0;
                tintIndex = type >= 2 && type < buffersPerDepth - 1 ? type - 1 : 0;
                if (buffersPerDepth == 12 && tintIndex >= 3) tintIndex += 2;
                else if (buffersPerDepth == 13 && tintIndex >= 4) tintIndex++;
            }
            if (type == buffersPerDepth - 1) tintIndex = 4;
            var layout = atlas == null ? new Atlas.Sheet(1, 1, [""]) : atlas.Sheets[sheet];
            int columns = !customMesh && format == 2 && sheet == 0 ? 32 : layout.Width;
            int rows = sheet == 0 ? sheetHeight : layout.Height;
            for (int i = 0; i < buffer.Indices.Length; i += 3)
            {
                if ((total += 3) > MaxVertices)
                    throw new InvalidDataException("The meshcache exceeds eight million rendered vertices.");
                int a = (int)buffer.Indices[i] * stride;
                int b = (int)buffer.Indices[i + 1] * stride;
                int c = (int)buffer.Indices[i + 2] * stride;
                int uvOffset = oldVertex ? 20 : 12;
                vec2 UV(int offset) => new(Float(buffer.Vertices, offset + uvOffset),
                    Float(buffer.Vertices, offset + uvOffset + 4));
                vec2 uvA = UV(a), uvB = UV(b), uvC = UV(c);
                vec2 uvLo = vec2.Min(uvA, vec2.Min(uvB, uvC));
                vec2 uvHi = vec2.Max(uvA, vec2.Max(uvB, uvC));
                if (!customMesh && (uvLo.x < -.001f || uvLo.y < -.001f || uvHi.x > 1.001f || uvHi.y > 1.001f))
                    throw new InvalidDataException("A meshcache UV is outside its texture sheet.");
                vec2 center = (uvA + uvB + uvC) / 3;
                int tx = customMesh ? 0 : Math.Clamp((int)MathF.Floor(center.x * columns), 0, columns - 1);
                int ty = customMesh ? 0 : Math.Clamp((int)MathF.Floor(center.y * rows), 0, rows - 1);
                int tile = ty * columns + tx;
                if (!customMesh && (tile >= layout.Textures.Length || string.IsNullOrEmpty(layout.Textures[tile])))
                    throw new InvalidDataException($"The meshcache references an unknown texture slot {tile} on sheet {sheet}.");
                uint color = oldVertex ? UInt(buffer.Vertices, a + 16) : 0xffffffff;
                int palette = oldVertex ? tintIndex : (int)(UInt(buffer.Vertices, a + 20) >> 24);
                (BiomeTintKind biome, uint fixedColor) = Palette(palette);
                color = MultiplyColor(color, fixedColor);
                uint flags = UInt(buffer.Vertices, a + (oldVertex ? 28 : 24));
                var material = new Material(layout.Textures[tile], buffer.Slot / buffersPerDepth, biome,
                    color, (int)((flags >> 8) & 255), (int)(flags & 3));
                if (!batches.TryGetValue(material, out var batch))
                {
                    batch = new Batch(material);
                    batches.Add(material, batch);
                    Batches.Add(batch);
                }
                // Both Z-up conversions reflect handedness, so reverse triangle winding.
                Add(a, uvA); Add(c, uvC); Add(b, uvB);

                void Add(int offset, vec2 uv)
                {
                    var p = new vec3(Float(buffer.Vertices, offset), Float(buffer.Vertices, offset + 4),
                        Float(buffer.Vertices, offset + 8)) / 16;
                    uint normal = UInt(buffer.Vertices, offset + (oldVertex ? 12 : 20));
                    var n = new vec3(normal & 255, (normal >> 8) & 255, (normal >> 16) & 255) / 127 - vec3.Ones;
                    if (n.LengthSqr < 1e-8f)
                    {
                        // Older caches include unlit faces with no authored normal.
                        vec3 Position(int index) => new(Float(buffer.Vertices, index),
                            Float(buffer.Vertices, index + 4), Float(buffer.Vertices, index + 8));
                        n = vec3.Cross(Position(b) - Position(a), Position(c) - Position(a));
                    }
                    if (format == 2)
                    {
                        p = new vec3(p.x - (customMesh ? 0 : Width * .5f), p.z,
                            p.y - (customMesh ? 0 : Length * .5f));
                        n = new vec3(n.x, n.z, n.y);
                        if (customMesh)
                        {
                            p = new vec3(-p.x, p.y, -p.z);
                            n = new vec3(-n.x, n.y, -n.z);
                        }
                    }
                    else
                    {
                        p = new vec3(Width * .5f - p.y, p.z, p.x - Length * .5f);
                        n = new vec3(-n.y, n.z, n.x);
                    }
                    batch.Vertices.Add(p);
                    batch.Normals.Add(n.LengthSqr > 1e-8f ? n.Normalized : vec3.Zero);
                    batch.TexCoords.Add(new vec2(uv.x * columns - tx, uv.y * rows - ty));
                }
            }
        }
    }

    private static (BiomeTintKind, uint) Palette(int index) => index switch
    {
        0 => (BiomeTintKind.None, 0xffffffff),
        1 => (BiomeTintKind.Grass, 0xffffffff),
        2 => (BiomeTintKind.Foliage, 0xffffffff),
        3 => (BiomeTintKind.None, 0xff4675a3),
        4 => (BiomeTintKind.Water, 0xffffffff),
        >= 5 and <= 11 => ((BiomeTintKind)((int)BiomeTintKind.Oak + index - 5), 0xffffffff),
        12 or 13 => (BiomeTintKind.None, 0xff10df80),
        14 => (BiomeTintKind.None, 0xff308020),
        >= 15 and <= 30 => (BiomeTintKind.None, 0xff000000u | (uint)MathF.Round(
            ((index - 15) == 0 ? .3f : (index - 15) / 15f * .6f + .4f) * 255)),
        _ => throw new InvalidDataException($"Unsupported meshcache color palette index {index}.")
    };

    private static uint MultiplyColor(uint a, uint b)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
            result |= (((a >> shift) & 255) * ((b >> shift) & 255) / 255) << shift;
        return result;
    }

    private static float Float(byte[] data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
    private static uint UInt(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private sealed class Atlas
    {
        internal sealed record Sheet(int Width, int Height, string[] Textures);
        public Sheet[] Sheets { get; }

        public Atlas(string? version)
        {
            string root = MinecraftDataLoader.GetVersionRoot();
            if (version != null && !string.Equals(Path.GetFileName(root), version, StringComparison.Ordinal))
                root = Path.Combine(Path.GetDirectoryName(root)!, version);
            string path = Path.Combine(root, "meshcache-atlas.json");
            var document = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
                throw new InvalidDataException("Invalid meshcache atlas metadata.");
            if (document["version"]?.GetValue<string>() != (version ?? Path.GetFileName(root)))
                throw new InvalidDataException("The meshcache atlas version does not match the cache.");
            Sheets = new Sheet[4];
            string[] sizes = ["block_sheet_size", "block_32_sheet_size", "block_64_sheet_size", "block_animated_sheet_size"];
            string[] textures = ["block_textures", "block_32_textures", "block_64_textures", "block_textures_animated"];
            for (int i = 0; i < Sheets.Length; i++)
            {
                var size = document[sizes[i]]!.AsArray();
                int width = size[0]!.GetValue<int>(), height = size[1]!.GetValue<int>();
                if (width <= 0 || height <= 0 || (long)width * height > 65536)
                    throw new InvalidDataException("Invalid meshcache atlas dimensions.");
                Sheets[i] = new Sheet(width, height,
                    document[textures[i]]!.AsArray().Select(t => t!.GetValue<string>()).ToArray());
            }
        }
    }

    // QDataStream scalars are big-endian; raw C++ arrays contain little-endian fields.
    private sealed class CacheReader(byte[] data)
    {
        private int _offset;
        public int Remaining => data.Length - _offset;
        public void Require(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated meshcache data.");
        }
        public ReadOnlySpan<byte> Bytes(int count)
        {
            Require(count);
            var bytes = data.AsSpan(_offset, count);
            _offset += count;
            return bytes;
        }
        public byte Byte() => Bytes(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));
        public int Int32() => BinaryPrimitives.ReadInt32BigEndian(Bytes(4));
        public long Int64() => BinaryPrimitives.ReadInt64BigEndian(Bytes(8));
        public ulong UInt64() => BinaryPrimitives.ReadUInt64BigEndian(Bytes(8));
        public int Dimension()
        {
            long value = Int64();
            if (value is <= 0 or > int.MaxValue) throw new InvalidDataException("Invalid meshcache scenery dimensions.");
            return (int)value;
        }
        public byte[] Array(int count, int stride, bool shuffled)
        {
            var input = Bytes(checked(count * stride));
            if (!shuffled) return input.ToArray();
            var result = new byte[input.Length];
            for (int b = 0; b < stride; b++)
            for (int i = 0; i < count; i++)
                result[i * stride + b] = input[b * count + i];
            return result;
        }
    }
}
