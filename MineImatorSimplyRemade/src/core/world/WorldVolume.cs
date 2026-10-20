using System.IO.Compression;
using Cyotek.Data.Nbt;

namespace MineImatorSimplyRemade.core.world;

internal readonly record struct WorldBounds(int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)
{
    public const int MaxBlocks = 2 * 1024 * 1024;
    public int Width => checked(MaxX - MinX + 1);
    public int Height => checked(MaxY - MinY + 1);
    public int Length => checked(MaxZ - MinZ + 1);
    public int Count => checked(Width * Height * Length);

    public void Validate()
    {
        if (MinX > MaxX || MinY > MaxY || MinZ > MaxZ)
            throw new InvalidDataException("Minimum coordinates must not exceed maximum coordinates.");
        if (MinX < -30_000_000 || MaxX > 30_000_000 || MinZ < -30_000_000 || MaxZ > 30_000_000 ||
            MinY < -2048 || MaxY > 2047)
            throw new InvalidDataException("The selected coordinates are outside the supported world bounds.");
        long width = (long)MaxX - MinX + 1, height = (long)MaxY - MinY + 1, length = (long)MaxZ - MinZ + 1;
        if (width > 512 || length > 512 || width * height * length > MaxBlocks)
            throw new InvalidDataException($"Select at most {MaxBlocks:N0} blocks, with width and length at most 512.");
    }
}

internal sealed class WorldVolume
{
    public WorldBounds Bounds { get; }
    public List<string> Palette { get; } = ["minecraft:air"];
    public int[] Blocks { get; }
    public int NonAirCount { get; private set; }
    private readonly Dictionary<string, int> _paletteIds = new(StringComparer.Ordinal) { ["minecraft:air"] = 0 };

    public WorldVolume(WorldBounds bounds)
    {
        bounds.Validate();
        Bounds = bounds;
        Blocks = new int[bounds.Count];
    }

    public static bool IsAir(string state) =>
        state is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air" or "air";

    public void Set(int x, int y, int z, string state)
    {
        if (x < Bounds.MinX || x > Bounds.MaxX || y < Bounds.MinY || y > Bounds.MaxY ||
            z < Bounds.MinZ || z > Bounds.MaxZ || IsAir(state))
            return;
        if (!_paletteIds.TryGetValue(state, out int id))
        {
            id = Palette.Count;
            Palette.Add(state);
            _paletteIds.Add(state, id);
        }
        int index = ((y - Bounds.MinY) * Bounds.Length + z - Bounds.MinZ) * Bounds.Width + x - Bounds.MinX;
        if (Blocks[index] == 0)
            NonAirCount++;
        Blocks[index] = id;
    }

    public string Get(int x, int y, int z) =>
        Palette[Blocks[(y * Bounds.Length + z) * Bounds.Width + x]];

    public WorldVolume WithoutBlocks(IEnumerable<int> indices)
    {
        var copy = new WorldVolume(Bounds);
        copy.Palette.Clear();
        copy.Palette.AddRange(Palette);
        copy._paletteIds.Clear();
        foreach (var pair in _paletteIds) copy._paletteIds.Add(pair.Key, pair.Value);
        Blocks.CopyTo(copy.Blocks, 0);
        copy.NonAirCount = NonAirCount;
        foreach (int index in indices)
        {
            if ((uint)index >= (uint)copy.Blocks.Length)
                throw new ArgumentOutOfRangeException(nameof(indices));
            if (copy.Blocks[index] == 0) continue;
            copy.Blocks[index] = 0;
            copy.NonAirCount--;
        }
        return copy;
    }

    public WorldVolume ApplyFilter(WorldBlockFilter? filter)
    {
        if (filter == null || !filter.IsActive)
            return this;

        var copy = new WorldVolume(Bounds);
        int[] paletteMap = new int[Palette.Count];
        paletteMap[0] = 0;
        bool waterFiltered = filter.IsWaterFilteredOut();

        for (int i = 1; i < Palette.Count; i++)
        {
            string state = Palette[i];
            if (IsAir(state))
            {
                paletteMap[i] = 0;
                continue;
            }

            string blockName = WorldBlockFilter.GetBlockNameFromState(state);
            if (filter.ShouldRemoveBlock(blockName))
            {
                paletteMap[i] = 0;
                continue;
            }

            string effectiveState = waterFiltered ? WorldBlockFilter.RemoveWaterloggedProperty(state) : state;
            if (!copy._paletteIds.TryGetValue(effectiveState, out int newId))
            {
                newId = copy.Palette.Count;
                copy.Palette.Add(effectiveState);
                copy._paletteIds.Add(effectiveState, newId);
            }
            paletteMap[i] = newId;
        }

        int nonAir = 0;
        for (int i = 0; i < Blocks.Length; i++)
        {
            int oldId = Blocks[i];
            int newId = paletteMap[oldId];
            copy.Blocks[i] = newId;
            if (newId != 0) nonAir++;
        }
        copy.NonAirCount = nonAir;
        return copy;
    }

    public void SaveSchematic(string path, byte[]? embeddedModels = null)
    {
        var root = new TagCompound("Schematic");
        root.Value.Add(new TagShort("Width", (short)Bounds.Width));
        root.Value.Add(new TagShort("Height", (short)Bounds.Height));
        root.Value.Add(new TagShort("Length", (short)Bounds.Length));
        // Keep entity-rendered block models and their textures portable with the scenery asset.
        if (embeddedModels != null)
            root.Value.Add(new TagByteArray("NuxiSpecialBlockModels", embeddedModels));
        bool legacy = Palette.Skip(1).Any(s => s.StartsWith("legacy:", StringComparison.Ordinal));
        if (legacy)
        {
            byte[] blocks = new byte[Blocks.Length], metadata = new byte[Blocks.Length];
            byte[] add = new byte[(Blocks.Length + 1) / 2];
            var lookup = Palette.Select(state =>
            {
                if (IsAir(state)) return (Id: 0, Data: 0);
                string[] parts = state.Split(':');
                if (parts.Length != 3 || parts[0] != "legacy" || !int.TryParse(parts[1], out int id) ||
                    !int.TryParse(parts[2], out int data) || id is < 0 or > 4095 || data is < 0 or > 15)
                    throw new InvalidDataException("A world selection mixes legacy and modern block formats.");
                return (Id: id, Data: data);
            }).ToArray();
            for (int i = 0; i < Blocks.Length; i++)
            {
                var entry = lookup[Blocks[i]];
                blocks[i] = (byte)entry.Id;
                metadata[i] = (byte)entry.Data;
                add[i >> 1] |= (byte)((entry.Id >> 8) << ((i & 1) * 4));
            }
            root.Value.Add(new TagString("Materials", "Alpha"));
            root.Value.Add(new TagByteArray("Blocks", blocks));
            root.Value.Add(new TagByteArray("Data", metadata));
            root.Value.Add(new TagByteArray("AddBlocks", add));
        }
        else
        {
            root.Value.Add(new TagInt("Version", 2));
            var palette = new TagCompound("Palette");
            for (int i = 0; i < Palette.Count; i++)
                palette.Value.Add(new TagInt(Palette[i], i));
            root.Value.Add(palette);
            using var data = new MemoryStream();
            foreach (int index in Blocks)
            {
                uint value = (uint)index;
                while (value >= 128)
                {
                    data.WriteByte((byte)(value | 128));
                    value >>= 7;
                }
                data.WriteByte((byte)value);
            }
            root.Value.Add(new TagByteArray("BlockData", data.ToArray()));
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        new NbtDocument(root).Save(gzip);
    }
}
