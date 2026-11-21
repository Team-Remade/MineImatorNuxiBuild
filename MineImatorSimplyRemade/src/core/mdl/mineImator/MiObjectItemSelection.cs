using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public sealed record MiObjectItemSelectionResult(
    MiResource Resource, string TexturePath, int Columns, int Rows, int ColumnIndex, int RowIndex);

public static class MiObjectItemSelection
{
    public static string? ResolveItemName(MiTemplate template, MiTimeline timeline, MiKeyframe? frame)
    {
        bool customSlot = frame?.CustomItemSlot is { } custom
            ? custom != 0
            : timeline.DefaultValues != null &&
              timeline.DefaultValues.TryGetBool("CUSTOM_ITEM_SLOT", out bool enabled) && enabled;
        string? name = customSlot ? ReadItemName(frame?.ExtraValues, "ITEM_NAME") : null;
        if (customSlot && string.IsNullOrWhiteSpace(name) && timeline.DefaultValues != null &&
            timeline.DefaultValues.TryGetString("ITEM_NAME", out string defaultName))
            name = defaultName;
        if (string.IsNullOrWhiteSpace(name))
            name = ReadItemName(template.Item?.ExtraValues, "name");
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public static string? ResolveBuiltinItemKey(MiTemplate template, MiTimeline timeline, MiKeyframe? frame)
    {
        string? name = ResolveItemName(template, timeline, frame);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string key = name.Trim().Replace('\\', '/');
        int separator = key.IndexOf(':');
        if (separator >= 0)
        {
            if (!key[..separator].Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                return null;
            key = key[(separator + 1)..];
        }
        if (key.StartsWith("textures/item/", StringComparison.OrdinalIgnoreCase))
            key = key["textures/item/".Length..];
        if (key.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
            key = key["item/".Length..];
        if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            key = key[..^4];
        key = key.Trim('/').ToLowerInvariant();
        return ItemsAtlas.Textures.ContainsKey(key) ? key : null;
    }

    public static MiObjectItemSelectionResult? Resolve(MiTemplate template, MiTimeline timeline,
        MiKeyframe? frame, IReadOnlyDictionary<string, MiResource> resources, string directory)
    {
        if (template.Item == null)
            return null;

        string? textureReference = frame?.TextureObject;
        if (textureReference == null && timeline.DefaultValues != null &&
            timeline.DefaultValues.TryGetString("TEXTURE_OBJ", out string defaultTexture))
            textureReference = defaultTexture;

        MiResource? resource = ResolveResource(textureReference) ?? ResolveResource(template.Item.Tex);
        if (resource == null)
            return null;

        int columns = Math.Max(1, resource.ItemSheetSize is { Length: >= 1 } ? resource.ItemSheetSize[0] : 1);
        int rows = Math.Max(1, resource.ItemSheetSize is { Length: >= 2 } ? resource.ItemSheetSize[1] : 1);
        bool customSlot = frame?.CustomItemSlot is { } custom
            ? custom != 0
            : timeline.DefaultValues != null &&
              timeline.DefaultValues.TryGetBool("CUSTOM_ITEM_SLOT", out bool enabled) && enabled;
        int slot = template.Item.Slot ?? 0;
        if (customSlot)
        {
            slot = frame?.ItemSlot ?? 0;
            if (frame?.ItemSlot == null && timeline.DefaultValues != null &&
                timeline.DefaultValues.TryGetValue("ITEM_SLOT", out float defaultSlot) && float.IsFinite(defaultSlot))
                slot = (int)Math.Clamp((double)defaultSlot, 0, int.MaxValue);
        }

        // Clamp the absolute slot before converting to row/column, never the column alone.
        long index = Math.Clamp((long)slot, 0, (long)columns * rows - 1);
        string path = Path.IsPathRooted(resource.Filename)
            ? resource.Filename
            : Path.Combine(directory, resource.Filename);
        return new(resource, path, columns, rows, (int)(index % columns), (int)(index / columns));

        MiResource? ResolveResource(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                string.Equals(reference, "null", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reference, "default", StringComparison.OrdinalIgnoreCase) ||
                !resources.TryGetValue(reference, out MiResource? candidate) ||
                string.IsNullOrWhiteSpace(candidate?.Filename) ||
                string.Equals(candidate.Filename, "null", StringComparison.OrdinalIgnoreCase))
                return null;
            return candidate;
        }
    }

    private static string? ReadItemName(IReadOnlyDictionary<string, JsonElement>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out JsonElement value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
