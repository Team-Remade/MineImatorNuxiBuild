using System.Text;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.audio;

/// <summary>
/// Renders all audio tracks on a timeline into a single WAV file suitable for
/// muxing into a rendered video by ffmpeg.  Mixes are done in-memory using the
/// already-decoded <see cref="AudioClip"/> PCM data.
/// </summary>
public static class AudioRenderMixer
{
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    /// <summary>
    /// Mixes the audio tracks into a WAV file at <paramref name="outputPath"/>.
    /// Returns true if at least one sample was written; false if there is no
    /// audio to encode or the output is silent.
    /// </summary>
    public static bool RenderTimelineAudioToWav(
        IEnumerable<TimelineAudioTrack> tracks,
        float frameRate,
        int totalFrames,
        string outputPath)
    {
        if (totalFrames <= 0 || frameRate <= 0) return false;
        if (tracks == null) return false;

        var trackList = tracks.Where(t => t != null && !t.ManifestEntry.Muted && t.Clip != null).ToList();
        if (trackList.Count == 0) return false;

        float durationSeconds = totalFrames / frameRate;
        int totalOutputSamples = (int)(durationSeconds * SampleRate);
        if (totalOutputSamples <= 0) return false;

        // Interleaved stereo float buffer.
        var mixBuffer = new float[totalOutputSamples * Channels];
        bool anyAudio = false;

        foreach (var track in trackList)
        {
            var clip = track.Clip!;
            var entry = track.ManifestEntry;

            float startSeconds = entry.StartFrame / frameRate + entry.SourceOffsetSeconds;
            int startOutputSample = (int)(startSeconds * SampleRate);
            if (startOutputSample >= totalOutputSamples) continue;

            int clipTotalSamples = clip.PcmData.Length / (BitsPerSample / 8 * Channels) * Channels;
            if (clipTotalSamples <= 0) continue;

            float volume = Math.Clamp(entry.Volume, 0f, 1f);

            for (int outSample = 0; outSample < totalOutputSamples; outSample++)
            {
                int sourceSampleIndex = (outSample - startOutputSample) * Channels;
                if (sourceSampleIndex < 0) continue;

                if (!entry.Loop && sourceSampleIndex >= clipTotalSamples) continue;

                if (entry.Loop && clipTotalSamples > 0)
                    sourceSampleIndex %= clipTotalSamples;

                // Convert source byte offset to sample frame and clip channel data.
                for (int ch = 0; ch < Channels; ch++)
                {
                    int srcIdx = sourceSampleIndex + ch;
                    if (srcIdx < 0 || srcIdx >= clipTotalSamples) continue;

                    int byteOffset = srcIdx * (BitsPerSample / 8);
                    if (byteOffset + 1 >= clip.PcmData.Length) continue;

                    short sample = (short)(clip.PcmData[byteOffset] | (clip.PcmData[byteOffset + 1] << 8));
                    float sampleF = sample / 32768f * volume;

                    int outIdx = outSample * Channels + ch;
                    mixBuffer[outIdx] += sampleF;
                    anyAudio = true;
                }
            }
        }

        if (!anyAudio) return false;

        // Soft clip / prevent hard clipping by scaling down if peaks exceed 1.0.
        float maxAbs = 0f;
        for (int i = 0; i < mixBuffer.Length; i++)
            maxAbs = Math.Max(maxAbs, Math.Abs(mixBuffer[i]));

        float scale = maxAbs > 1f ? 1f / maxAbs : 1f;

        byte[] pcmBytes = new byte[mixBuffer.Length * 2];
        for (int i = 0; i < mixBuffer.Length; i++)
        {
            float clamped = Math.Clamp(mixBuffer[i] * scale, -1f, 1f);
            short value = (short)(clamped * 32767f);
            pcmBytes[i * 2] = (byte)(value & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        WriteWavFile(outputPath, pcmBytes, SampleRate, Channels, BitsPerSample);
        return File.Exists(outputPath) && new FileInfo(outputPath).Length > 44;
    }

    private static void WriteWavFile(string path, byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcmData.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                        // fmt chunk size
        writer.Write((short)1);                  // audio format PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcmData.Length);
        writer.Write(pcmData);
        writer.Flush();
    }
}
