using System.Diagnostics;
using Silk.NET.OpenAL;

namespace MineImatorSimplyRemade.core.audio;

/// <summary>
/// Lightweight wrapper around OpenAL that decodes audio files to PCM via
/// FFmpeg and exposes per-track playback for the timeline.
///
/// Lifecycle: <see cref="Initialize"/> is called once at startup.  Each
/// <see cref="TimelineAudioTrack"/> owns one <see cref="AudioSource"/>.
/// </summary>
public sealed unsafe class AudioEngine : IDisposable
{
    public static AudioEngine Instance { get; } = new();

    private const int SampleRate     = 44100;
    private const int ChannelCount   = 2;            // stereo output
    private const int BitsPerSample  = 16;

    private AL? _al;
    private ALContext? _alc;
    private unsafe Device* _device;
    private Context* _context;
    private bool _initialized;
    private bool _disposed;

    // Cache of decoded audio data, keyed by full file path.
    private readonly Dictionary<string, AudioClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private AudioEngine() { }

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        if (_initialized) return;

        try
        {
            _al  = AL.GetApi(true);
            _alc = ALContext.GetApi(true);
        }
        catch
        {
            // OpenAL not available – audio will be silently disabled.
            _al = null;
            _alc = null;
            return;
        }

        unsafe
        {
            _device = _alc!.OpenDevice(null);
            if (_device == null) return;

            _context = _alc.CreateContext(_device, null);
            if (_context == null) return;

            _alc.MakeContextCurrent(_context);
            _alc.ProcessContext(_context);
        }

        _initialized = true;
    }

    /// <summary>
    /// Decode (or return cached) PCM data for the given file.  Returns null if
    /// the file can't be decoded or audio is disabled.
    /// </summary>
    public AudioClip? LoadClip(string filePath)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        lock (_lock)
        {
            if (_clipCache.TryGetValue(filePath, out var cached))
                return cached;
        }

        var clip = DecodeWithFfmpeg(filePath);
        if (clip == null) return null;

        lock (_lock) _clipCache[filePath] = clip;
        return clip;
    }

    public bool TryGetCachedClip(string filePath, out AudioClip? clip)
    {
        lock (_lock)
        {
            if (_clipCache.TryGetValue(filePath, out var c))
            {
                clip = c;
                return true;
            }
        }
        clip = null;
        return false;
    }

    /// <summary>
    /// Create (or recycle) a source bound to a clip.  The returned source is
    /// owned by the caller and must be disposed with <see cref="DestroySource"/>.
    /// </summary>
    public AudioSourceHandle CreateSource(AudioClip clip)
    {
        if (!_initialized || _al == null)
            return new AudioSourceHandle(0, clip, valid: false);

        uint buffer = _al.GenBuffer();
        fixed (byte* p = clip.PcmData)
            _al.BufferData(buffer, BufferFormat.Stereo16, p, clip.PcmData.Length, SampleRate);
        uint source = _al.GenSource();
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        return new AudioSourceHandle(source, clip, valid: true);
    }

    public void DestroySource(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SourceStop(handle.SourceId);
        _al.DeleteSource(handle.SourceId);
    }

    public void SetSourceVolume(AudioSourceHandle handle, float volume)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SetSourceProperty(handle.SourceId, SourceFloat.Gain, Math.Clamp(volume, 0f, 1f));
    }

    public void SetSourceLooping(AudioSourceHandle handle, bool loop)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SetSourceProperty(handle.SourceId, SourceBoolean.Looping, loop);
    }

    public void SetSourceOffsetSeconds(AudioSourceHandle handle, float seconds)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SetSourceProperty(handle.SourceId, SourceFloat.SecOffset, Math.Max(0f, seconds));
    }

    public void PlaySource(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SourcePlay(handle.SourceId);
    }

    public void PauseSource(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SourcePause(handle.SourceId);
    }

    public void StopSource(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return;
        _al.SourceStop(handle.SourceId);
    }

    public bool IsSourcePlaying(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return false;
        _al.GetSourceProperty(handle.SourceId, GetSourceInteger.SourceState, out int state);
        return (SourceState)state == SourceState.Playing;
    }

    public bool IsSourcePaused(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return false;
        _al.GetSourceProperty(handle.SourceId, GetSourceInteger.SourceState, out int state);
        return (SourceState)state == SourceState.Paused;
    }

    public float GetSourceOffsetSeconds(AudioSourceHandle handle)
    {
        if (!_initialized || _al == null || !handle.IsValid) return 0f;
        _al.GetSourceProperty(handle.SourceId, SourceFloat.SecOffset, out float offset);
        return offset;
    }

    // ── Decoding via FFmpeg ──────────────────────────────────────────────────

    private static AudioClip? DecodeWithFfmpeg(string filePath)
    {
        string ffmpegPath = FfmpegBootstrap.GetFfmpegExecutablePath();
        if (!File.Exists(ffmpegPath)) return null;

        string args = $"-y -hide_banner -loglevel error -i \"{filePath}\" " +
                      $"-f s16le -ac {ChannelCount} -ar {SampleRate} -";

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = ffmpegPath,
                    Arguments              = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                }
            };
            proc.Start();

            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit();
            if (proc.ExitCode != 0) return null;

            byte[] pcm = ms.ToArray();
            int    bytesPerSample = BitsPerSample / 8;
            int    totalSamples   = pcm.Length / bytesPerSample;
            int    frames         = totalSamples / ChannelCount;
            double duration       = (double)frames / SampleRate;

            return new AudioClip(pcm, SampleRate, ChannelCount, BitsPerSample, duration, filePath);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_initialized) return;

        unsafe
        {
            if (_alc != null && _context != null)
            {
                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
            }
            if (_alc != null && _device != null)
                _alc.CloseDevice(_device);
        }
    }
}

/// <summary>Decoded PCM data ready to upload to OpenAL.</summary>
public sealed class AudioClip
{
    public byte[] PcmData         { get; }
    public int    SampleRate      { get; }
    public int    Channels        { get; }
    public int    BitsPerSample   { get; }
    public double DurationSeconds { get; }
    public string SourcePath      { get; }

    public AudioClip(byte[] pcm, int sampleRate, int channels, int bitsPerSample,
                    double duration, string sourcePath)
    {
        PcmData       = pcm;
        SampleRate    = sampleRate;
        Channels      = channels;
        BitsPerSample = bitsPerSample;
        DurationSeconds = duration;
        SourcePath    = sourcePath;
    }
}

/// <summary>OpenAL source id paired with its clip.</summary>
public readonly struct AudioSourceHandle
{
    public readonly uint       SourceId;
    public readonly AudioClip  Clip;
    public readonly bool       IsValid;

    public AudioSourceHandle(uint sourceId, AudioClip clip, bool valid)
    {
        SourceId = sourceId;
        Clip     = clip;
        IsValid  = valid;
    }
}
