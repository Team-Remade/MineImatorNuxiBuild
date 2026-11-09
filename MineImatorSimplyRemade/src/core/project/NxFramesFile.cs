using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineImatorSimplyRemade.core.project;

public sealed class NxFramesManifest
{
    [JsonRequired] public string Format { get; set; } = "nxFrames";
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public List<NxFramesObject> Objects { get; set; } = [];
}

public sealed class NxFramesObject
{
    [JsonRequired] public List<string> NamePath { get; set; } = [];
    [JsonRequired] public List<NxFramesTrack> Tracks { get; set; } = [];
}

public sealed class NxFramesTrack
{
    [JsonRequired] public string PropertyPath { get; set; } = "";
    [JsonRequired] public string MappingKey { get; set; } = "";
    [JsonRequired] public List<NxFramesKeyframe> Keyframes { get; set; } = [];
}

public sealed class NxFramesKeyframe
{
    [JsonRequired] public int Frame { get; set; }
    [JsonRequired] public JsonElement Value { get; set; }
    [JsonRequired] public string InterpolationType { get; set; } = "linear";
    public List<string>? ReferenceNames { get; set; }
}

public sealed record NxFramesImportResult(int KeyframesImported, int ObjectsChanged, IReadOnlyList<string> Skipped);

public static class NxFramesFile
{
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private const int MaxKeyframes = 100_000;

    public static NxFramesManifest Read(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaxFileBytes)
            throw new InvalidDataException("The nxFrames file exceeds 32 MiB.");
        var manifest = JsonSerializer.Deserialize(input, AppJsonContext.Default.NxFramesManifest)
            ?? throw new InvalidDataException("The nxFrames file is empty.");
        Validate(manifest);
        return manifest;
    }

    public static void Write(string path, NxFramesManifest manifest)
    {
        Validate(manifest);
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary))
            {
                JsonSerializer.Serialize(output, manifest, AppJsonContext.Default.NxFramesManifest);
                if (output.Length > MaxFileBytes)
                    throw new InvalidDataException("The nxFrames file exceeds 32 MiB.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void Validate(NxFramesManifest manifest)
    {
        if (manifest.Format != "nxFrames" || manifest.Version != 1)
            throw new InvalidDataException("Unsupported nxFrames format or version.");
        if (manifest.Objects is not { Count: > 0 and <= MaxKeyframes })
            throw new InvalidDataException("The nxFrames file must contain objects with keyframes.");
        int count = 0;
        foreach (var obj in manifest.Objects)
        {
            if (obj == null || !ValidNames(obj.NamePath) || obj.Tracks is not { Count: > 0 and <= MaxKeyframes })
                throw new InvalidDataException("Invalid nxFrames source object.");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in obj.Tracks)
            {
                if (track == null || string.IsNullOrWhiteSpace(track.PropertyPath) ||
                    string.IsNullOrWhiteSpace(track.MappingKey) || !paths.Add(track.PropertyPath) ||
                    track.Keyframes is not { Count: > 0 and <= MaxKeyframes })
                    throw new InvalidDataException("Invalid or duplicate nxFrames track.");
                var frames = new HashSet<int>();
                foreach (var key in track.Keyframes)
                {
                    if (++count > MaxKeyframes || key == null || key.Frame < 0 || !frames.Add(key.Frame) ||
                        string.IsNullOrWhiteSpace(key.InterpolationType) ||
                        (key.ReferenceNames != null && !ValidNames(key.ReferenceNames)) ||
                        !(key.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False ||
                          key.Value.ValueKind == JsonValueKind.Number && key.Value.TryGetSingle(out float value) && float.IsFinite(value)))
                        throw new InvalidDataException("Invalid nxFrames keyframe value, frame or interpolation.");
                }
            }
        }
    }

    private static bool ValidNames(List<string>? names) =>
        names is { Count: > 0 and <= 256 } && names.All(name => name != null);
}
