using System.Text.Json;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public static partial class MiObjectFeatureMapper
{
    public static bool IsAudio(MiTimeline timeline) =>
        string.Equals(timeline.Type, "audio", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Path, ProjectAudioTrack Track)> AudioTracks(MiObject source,
        Action<string>? warning = null)
    {
        var result = new List<(string, ProjectAudioTrack)>();
        var resources = (source.Resources ?? []).Where(r => !string.IsNullOrEmpty(r.Id))
            .ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var timeline in (source.Timelines ?? []).Where(IsAudio))
        {
            var frames = timeline.Animated == false
                ? new List<(int Frame, MiKeyframe Key)> { (0, timeline.CurrentValues ?? new MiKeyframe()) }
                : Frames(timeline);
            foreach (var (frame, key) in frames)
            {
                string reference = Text(Value(timeline, key, "SOUND_OBJ"), "");
                if (reference is "" or "null" or "none" or "-1") continue;
                string filename = resources.TryGetValue(reference, out var resource) ? resource.Filename : reference;
                if (string.IsNullOrWhiteSpace(filename))
                {
                    Report($"MIObject audio '{timeline.Name}' at frame {frame}: sound '{reference}' has no filename.", warning);
                    continue;
                }
                string path = Path.GetFullPath(Path.Combine(source.DirectoryPath ?? "", filename));
                if (!File.Exists(path))
                {
                    Report($"MIObject audio '{timeline.Name}' at frame {frame}: missing sound '{reference}' ({path}).", warning);
                    continue;
                }
                try
                {
                    float volume = Number(Value(timeline, key, "SOUND_VOLUME"), 1);
                    float pitch = Number(Value(timeline, key, "SOUND_PITCH"), 1);
                    float start = Number(Value(timeline, key, "SOUND_START"), 0);
                    float end = Number(Value(timeline, key, "SOUND_END"), 0);
                    if (volume is < 0 or > 1 || pitch <= 0 || start < 0 || !float.IsFinite(start * pitch))
                        throw new InvalidDataException("Invalid volume, pitch or source trim.");
                    result.Add((path, new ProjectAudioTrack
                    {
                        DisplayName = string.IsNullOrWhiteSpace(timeline.Name) ? Path.GetFileNameWithoutExtension(path) : timeline.Name,
                        StartFrame = frame,
                        Volume = volume,
                        Pitch = pitch,
                        SourceOffsetSeconds = start * pitch,
                        EndOffsetSeconds = end,
                        Loop = end > 0,
                        Muted = timeline.Hide == true
                    }));
                }
                catch (InvalidDataException ex)
                {
                    Report($"MIObject audio '{timeline.Name}' at frame {frame}: {ex.Message}", warning);
                }
            }
            foreach (string channel in (timeline.DefaultValues?.Values.Keys ?? Enumerable.Empty<string>())
                         .Concat(frames.SelectMany(f => f.Key.ExtraValues.Keys)).Distinct())
                if (channel is not ("SOUND_OBJ" or "SOUND_VOLUME" or "SOUND_PITCH" or "SOUND_START" or "SOUND_END" or
                    "TRANSITION" or "EASE_IN_X" or "EASE_IN_Y" or "EASE_OUT_X" or "EASE_OUT_Y"))
                    Report($"MIObject audio '{timeline.Name}': channel '{channel}' has no native mapping.", warning);
        }
        return result;
    }
}
