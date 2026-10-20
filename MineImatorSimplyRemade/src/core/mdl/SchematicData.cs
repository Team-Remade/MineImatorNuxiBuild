using System.IO.Compression;
using Cyotek.Data.Nbt;

namespace MineImatorSimplyRemade.core.mdl;

internal sealed class SchematicData
{
    public int Width { get; private init; }
    public int Height { get; private init; }
    public int Length { get; private init; }
    public int Count { get; private init; }
    public Dictionary<int, string>? Palette { get; private set; }
    public int[] PaletteIndices { get; private set; } = [];
    public byte[] Blocks { get; private set; } = [];
    public byte[] Metadata { get; private set; } = [];
    public byte[] AddBlocks { get; private set; } = [];
    public byte[] EmbeddedModels { get; private set; } = [];

    public static SchematicData Load(string path)
    {
        using var source = File.OpenRead(path);
        bool gzip = source.ReadByte() == 0x1f && source.ReadByte() == 0x8b;
        source.Position = 0;

        if (gzip)
        {
            // Cyotek expects full reads; GZipStream can return partial reads for valid arrays.
            using var decompressor = new GZipStream(source, CompressionMode.Decompress);
            using var buffered = new MemoryStream();
            decompressor.CopyTo(buffered);
            buffered.Position = 0;
            return ReadDocument(buffered);
        }

        return ReadDocument(source);
    }

    private static SchematicData ReadDocument(Stream stream)
    {
        var root = NbtDocument.LoadDocument(stream).DocumentRoot ??
                   throw new InvalidDataException("The file has no NBT root tag.");
        return Read(root);
    }

    public static SchematicData Read(TagCompound root)
    {
        var schematic = root.GetCompound("Schematic") ?? root;
        int width = ReadDimension(schematic, "Width");
        int height = ReadDimension(schematic, "Height");
        int length = ReadDimension(schematic, "Length");
        long count = (long)width * height * length;
        if (count > Array.MaxLength)
            throw new InvalidDataException("The schematic volume is too large.");

        var result = new SchematicData
        {
            Width = width,
            Height = height,
            Length = length,
            Count = (int)count,
            EmbeddedModels = schematic.GetTag("NuxiSpecialBlockModels") is TagByteArray models ? models.Value : []
        };

        bool modern = schematic.Contains("Version") || schematic.Contains("Palette") ||
                      schematic.GetTag("Blocks") is TagCompound;
        if (modern)
        {
            // Older exporters sometimes omit Version; retain support for those palettes.
            int version = schematic.Contains("Version")
                ? schematic.GetTag("Version") is TagInt versionTag
                    ? versionTag.Value
                    : throw new InvalidDataException("The schematic Version must be an integer.")
                : schematic.GetTag("Blocks") is TagCompound ? 3 : 2;
            if (version is < 1 or > 3)
                throw new InvalidDataException($"Unsupported schematic version: {version}.");

            var blockRoot = version == 3
                ? schematic.GetCompound("Blocks") ??
                  throw new InvalidDataException("The v3 schematic has no Blocks compound.")
                : schematic;
            result.ReadPalette(blockRoot, version);
        }
        else
        {
            result.Blocks = ReadLegacyArray(schematic, "Blocks", result.Count);
            // Missing metadata was accepted by the legacy loader as all zeroes.
            result.Metadata = schematic.Contains("Data")
                ? ReadLegacyArray(schematic, "Data", result.Count)
                : [];
            result.AddBlocks = schematic.Contains("AddBlocks")
                ? ReadLegacyArray(schematic, "AddBlocks", (result.Count + 1) / 2)
                : [];
        }

        return result;
    }

    private static int ReadDimension(TagCompound root, string name)
    {
        int value = root.GetTag(name) switch
        {
            // Sponge dimensions are unsigned shorts stored in signed NBT short tags.
            TagShort tag => unchecked((ushort)tag.Value),
            TagInt tag => tag.Value,
            _ => 0
        };
        if (value is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException($"Invalid schematic dimension: {name}.");
        return value;
    }

    private static byte[] ReadLegacyArray(TagCompound root, string name, int count)
    {
        if (root.GetTag(name) is not TagByteArray tag || tag.Value.Length != count)
            throw new InvalidDataException($"The legacy {name} array must contain {count} bytes.");
        return tag.Value;
    }

    private void ReadPalette(TagCompound root, int version)
    {
        var palette = root.GetCompound("Palette");
        if (palette == null || palette.Count == 0)
            throw new InvalidDataException("The schematic palette is missing or empty.");

        var lookup = new Dictionary<int, string>();
        foreach (Tag entry in palette.Value)
        {
            if (entry is not TagInt tag || tag.Value < 0 ||
                !lookup.TryAdd(tag.Value, tag.Name))
                throw new InvalidDataException("The schematic palette has invalid or duplicate IDs.");
        }

        string key = version == 3 ? "Data" : "BlockData";
        switch (root.GetTag(key))
        {
            case TagIntArray ints:
                if (ints.Value.Length != Count)
                    throw new InvalidDataException($"The {key} array must contain {Count} indices.");
                PaletteIndices = ints.Value;
                break;
            case TagByteArray bytes:
                if (bytes.Value.Length < Count)
                    throw new InvalidDataException($"The {key} array is truncated.");
                PaletteIndices = new int[Count];
                if (version == 1 && bytes.Value.Length == Count)
                {
                    for (int i = 0; i < Count; i++)
                        PaletteIndices[i] = bytes.Value[i];
                }
                else
                {
                    DecodeVarInts(bytes.Value, PaletteIndices);
                }
                break;
            default:
                throw new InvalidDataException($"The {key} array is missing or has an invalid type.");
        }

        foreach (int index in PaletteIndices)
        {
            if (!lookup.ContainsKey(index))
                throw new InvalidDataException($"Block data references missing palette ID {index}.");
        }

        Palette = lookup;
    }

    private static void DecodeVarInts(byte[] data, int[] indices)
    {
        int offset = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            int value = 0;
            for (int byteIndex = 0; ; byteIndex++)
            {
                if (offset == data.Length)
                    throw new InvalidDataException("The block data contains a truncated varint.");
                byte next = data[offset++];
                if (byteIndex == 4 && next > 7)
                    throw new InvalidDataException("A block varint exceeds a non-negative 32-bit integer.");
                value |= (next & 0x7f) << (byteIndex * 7);
                if ((next & 0x80) == 0)
                    break;
            }
            indices[i] = value;
        }

        if (offset != data.Length)
            throw new InvalidDataException("The block data contains more entries than the schematic size.");
    }
}
