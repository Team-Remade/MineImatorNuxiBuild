using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.audio;

internal static class AudioTrackTiming
{
    internal static double DurationSeconds(ProjectAudioTrack track, double clipDuration)
    {
        Validate(track);
        return Math.Max(0, (clipDuration - track.SourceOffsetSeconds) / track.Pitch + track.EndOffsetSeconds);
    }

    internal static double SourceSeconds(ProjectAudioTrack track, double elapsedSeconds, double clipDuration)
    {
        Validate(track);
        double position = track.SourceOffsetSeconds + elapsedSeconds * track.Pitch;
        return track.Loop && clipDuration > 0 ? position % clipDuration : position;
    }

    private static void Validate(ProjectAudioTrack track)
    {
        if (!float.IsFinite(track.Pitch) || track.Pitch <= 0 ||
            !float.IsFinite(track.SourceOffsetSeconds) || track.SourceOffsetSeconds < 0 ||
            !float.IsFinite(track.EndOffsetSeconds))
            throw new InvalidDataException($"Invalid audio timing for '{track.DisplayName}'.");
    }
}
