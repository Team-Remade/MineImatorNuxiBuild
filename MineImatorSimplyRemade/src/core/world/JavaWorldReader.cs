using System.Buffers.Binary;
using System.IO.Compression;
using Cyotek.Data.Nbt;

namespace MineImatorSimplyRemade.core.world;

internal sealed class JavaWorldReader(string regionPath, CancellationToken cancellationToken) : IDisposable
{
    private readonly Dictionary<(int, int), FileStream?> _regions = [];

    public IEnumerable<(int X, int Y, int Z, string State)> ReadChunk(int chunkX, int chunkZ)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (chunkX >> 5, chunkZ >> 5);
        if (!_regions.TryGetValue(key, out var stream))
        {
            string path = Path.Combine(regionPath, $"r.{key.Item1}.{key.Item2}.mca");
            stream = File.Exists(path) ? File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            _regions.Add(key, stream);
        }
        if (stream == null)
            yield break;
        if (stream.Length < 8192)
            throw new InvalidDataException("The region header is truncated.");

        byte[] location = new byte[4];
        stream.Position = 4 * ((chunkX & 31) + (chunkZ & 31) * 32);
        stream.ReadExactly(location);
        int sector = (location[0] << 16) | (location[1] << 8) | location[2];
        int sectors = location[3];
        if (sector == 0 && sectors == 0)
            yield break;
        if (sector < 2 || sectors == 0 || (long)(sector + sectors) * 4096 > stream.Length)
            throw new InvalidDataException($"Invalid region location for chunk {chunkX}, {chunkZ}.");

        stream.Position = (long)sector * 4096;
        byte[] header = new byte[5];
        stream.ReadExactly(header);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        int compression = header[4] & 0x7f;
        bool external = (header[4] & 0x80) != 0;
        if (length < 1 || length > sectors * 4096 - 4)
            throw new InvalidDataException("Invalid region chunk length.");

        using Stream payload = external
            ? File.OpenRead(Path.Combine(regionPath, $"c.{chunkX}.{chunkZ}.mcc"))
            : ReadPayload(stream, length - 1);
        using Stream decompressor = compression switch
        {
            1 => new GZipStream(payload, CompressionMode.Decompress),
            2 => new ZLibStream(payload, CompressionMode.Decompress),
            3 => payload,
            _ => throw new NotSupportedException($"Java chunk compression {compression} is not supported.")
        };
        using var buffer = ReadBounded(decompressor, 32 * 1024 * 1024);
        var root = NbtDocument.LoadDocument(buffer).DocumentRoot ??
                   throw new InvalidDataException("The chunk has no NBT root.");
        int version = root.GetTag("DataVersion") is TagInt dataVersion ? dataVersion.Value : 0;
        var level = root.GetCompound("Level") ?? root;
        var sections = level.GetTag("sections") ?? level.GetTag("Sections");
        if (sections == null)
            yield break;
        if (sections is not TagList list || list.ListType != TagType.Compound)
            throw new InvalidDataException("Invalid chunk section list.");
        foreach (Tag sectionTag in list.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sectionTag is not TagCompound section)
                throw new InvalidDataException("Invalid chunk section.");
            int sectionY = section.GetTag("Y") switch
            {
                TagByte tag => unchecked((sbyte)tag.Value),
                TagInt tag => tag.Value,
                _ => throw new InvalidDataException("Missing section Y coordinate.")
            };

            if (section.GetTag("Blocks") is TagByteArray legacy)
            {
                if (legacy.Value.Length != 4096 ||
                    section.GetTag("Data") is not TagByteArray metadata || metadata.Value.Length != 2048)
                    throw new InvalidDataException("Invalid legacy chunk block arrays.");
                byte[]? add = (section.GetTag("Add") as TagByteArray)?.Value;
                if (add != null && add.Length != 2048)
                    throw new InvalidDataException("Invalid legacy extended block IDs.");
                for (int i = 0; i < 4096; i++)
                {
                    int id = legacy.Value[i] | (add == null ? 0 : Nibble(add, i) << 8);
                    if (id != 0)
                        yield return (chunkX * 16 + (i & 15), sectionY * 16 + (i >> 8),
                            chunkZ * 16 + ((i >> 4) & 15), $"legacy:{id}:{Nibble(metadata.Value, i)}");
                }
                continue;
            }

            var blockStates = section.GetCompound("block_states");
            var paletteTag = (blockStates ?? section).GetTag(blockStates != null ? "palette" : "Palette");
            if (paletteTag == null)
            {
                if (blockStates != null || section.Contains("BlockStates") || section.Contains("Blocks"))
                    throw new InvalidDataException("A chunk section has block storage but no valid palette.");
                continue; // Light-only sections contain no block storage.
            }
            if (paletteTag is not TagList palette || palette.Count is < 1 or > 4096)
                throw new InvalidDataException("Invalid chunk block palette.");
            string[] states = palette.Value.Cast<Tag>().Select(ParseState).ToArray();
            long[]? packed = ((blockStates ?? section).GetTag(blockStates != null ? "data" : "BlockStates")
                as TagLongArray)?.Value;
            int[] indices = DecodeIndices(states.Length, packed, version >= 2529);
            for (int i = 0; i < indices.Length; i++)
            {
                string state = states[indices[i]];
                if (!WorldVolume.IsAir(state))
                    yield return (chunkX * 16 + (i & 15), sectionY * 16 + (i >> 8),
                        chunkZ * 16 + ((i >> 4) & 15), state);
            }
        }
    }

    private static int Nibble(byte[] values, int index) => (values[index >> 1] >> ((index & 1) * 4)) & 15;

    internal static string ParseState(Tag tag)
    {
        if (tag is TagString simple && !string.IsNullOrWhiteSpace(simple.Value))
            return simple.Value;
        if (tag is TagCompound { Count: 1 } wrapper && wrapper.GetTag("") is TagString wrapped)
            return ParseState(wrapped);
        if (tag is not TagCompound entry ||
            (entry.GetTag("id") ?? entry.GetTag("Name")) is not TagString name ||
            string.IsNullOrWhiteSpace(name.Value))
            throw new InvalidDataException("A block palette entry has no valid block ID.");
        var properties = entry.GetCompound(entry.Contains("id") ? "properties" : "Properties");
        if (properties == null || properties.Count == 0)
            return name.Value;
        var pairs = new List<string>();
        foreach (Tag property in properties.Value)
        {
            if (property is not TagString text)
                throw new InvalidDataException("A Java block property is not a string.");
            pairs.Add($"{text.Name}={text.Value}");
        }
        pairs.Sort(StringComparer.Ordinal);
        return $"{name.Value}[{string.Join(',', pairs)}]";
    }

    internal static int[] DecodeIndices(int paletteCount, long[]? words, bool padded)
    {
        if (paletteCount is < 1 or > 4096)
            throw new InvalidDataException("Invalid block palette size.");
        int[] result = new int[4096];
        if (words == null || words.Length == 0)
        {
            if (paletteCount != 1)
                throw new InvalidDataException("A multi-entry block palette has no block data.");
            return result;
        }
        int bits = Math.Max(4, 32 - int.LeadingZeroCount(paletteCount - 1));
        int perWord = 64 / bits;
        int expected = padded ? (4096 + perWord - 1) / perWord : (4096 * bits + 63) / 64;
        if (words.Length != expected)
            throw new InvalidDataException("Invalid packed block-state array length.");
        ulong mask = (1UL << bits) - 1;
        for (int i = 0; i < result.Length; i++)
        {
            int word = padded ? i / perWord : i * bits / 64;
            int shift = padded ? (i % perWord) * bits : i * bits % 64;
            ulong value = unchecked((ulong)words[word]) >> shift;
            if (!padded && shift + bits > 64)
                value |= unchecked((ulong)words[word + 1]) << (64 - shift);
            result[i] = (int)(value & mask);
            if (result[i] >= paletteCount)
                throw new InvalidDataException("Block data references a missing palette entry.");
        }
        return result;
    }

    private static MemoryStream ReadPayload(Stream stream, int length)
    {
        byte[] bytes = new byte[length];
        stream.ReadExactly(bytes);
        return new MemoryStream(bytes, false);
    }

    internal static MemoryStream ReadBounded(Stream input, int limit)
    {
        var output = new MemoryStream();
        try
        {
            byte[] bytes = new byte[8192];
            int read;
            while ((read = input.Read(bytes)) != 0)
            {
                if (output.Length + read > limit)
                    throw new InvalidDataException("Decompressed NBT exceeds the supported size.");
                output.Write(bytes, 0, read);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var stream in _regions.Values)
            stream?.Dispose();
    }
}
