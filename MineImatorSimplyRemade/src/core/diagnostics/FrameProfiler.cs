using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MineImatorSimplyRemade.core.diagnostics;

public enum ProfileStage
{
    Frame, BackendBegin, Events, MainWindow, UiSetup, MainViewportPanel, TimelinePanel,
    SceneTreePanel, PropertiesPanel, ContentBrowserPanel, PreferencesPanel, OtherPanels,
    DirtyTracking, HistoryTracking, SnapshotCheck, SnapshotSerialize, PreviewWindow,
    ImGuiDraw, PlatformWindows, SwapBuffers, BackendEnd, FrameLimiter, MainScene,
    PreviewScene, ScenePreparation, ParticleUpdate, ShadowPass, SceneCollection,
    SceneSort, SceneDraw, Overlays, PreviewPreparation, TransformEvaluation,
    VisibilityEvaluation, BoneLookup, MeshFlattening, ProfilerUi, CameraFeeds,
    AmbientOcclusion, IndirectLighting, Glow, DepthOfField, FilmGrain, Picking,
    ShadowSignature, ShadowBounds, BoneMatrices, FrameWork, SceneFingerprint,
    SnapshotMetadata, LightSelection, Count
}

public enum ProfileCounter
{
    SceneNodesVisited, MeshesSubmitted, WorldMatrixQueries, VisibilityQueries,
    MeshFlattenCalls, SnapshotsCreated, SnapshotCacheHits, MainRenderInvocations,
    PreviewRenderInvocations, CameraFeedRenderInvocations, WorldMatrixCacheHits,
    VisibilityCacheHits, ShadowCacheHits, ShadowBoundsRebuilds, LightCandidatesExamined,
    PointLightsUploaded, EmissiveProxyLights, LightSelectionCacheHits, Count
}

public readonly record struct ProfileStageSummary(ProfileStage Stage, double MeanMs, double MaxMs, double MeanCalls)
{
    public double MeanAllocatedBytes { get; init; }
}
public readonly record struct ProfileCounterSummary(ProfileCounter Counter, double MeanValue, long MaxValue);

public sealed class ProfileSummary
{
    public int FrameCount { get; init; }
    public double MeanFrameMs { get; init; }
    public double P50FrameMs { get; init; }
    public double P95FrameMs { get; init; }
    public double P99FrameMs { get; init; }
    public double MaxFrameMs { get; init; }
    public double MeanAllocatedBytes { get; init; }
    public int[] GcCollections { get; init; } = new int[3];
    public ProfileStageSummary[] Stages { get; init; } = Array.Empty<ProfileStageSummary>();
    public ProfileCounterSummary[] Counters { get; init; } = Array.Empty<ProfileCounterSummary>();
    public string Label { get; init; } = "";
}

/// <summary>Main-thread CPU wall-time sampling. Summaries and exports allocate; frame sampling does not.</summary>
public sealed class FrameProfiler
{
    private const int LiveCapacity = 240;
    private const int CaptureCapacity = 20000;
    private const int StageCount = (int)ProfileStage.Count;
    private const int CounterCount = (int)ProfileCounter.Count;
    private static readonly double TickMilliseconds = 1000.0 / Stopwatch.Frequency;
    public static FrameProfiler Shared { get; } = new();

    private readonly Samples _live = new(LiveCapacity);
    private readonly int[] _depth = new int[StageCount];
    private readonly long[] _stageStarts = new long[StageCount];
    private readonly long[] _stageAllocationStarts = new long[StageCount];
    private Samples? _capture;
    private bool _enabled;
    private volatile bool _inFrame;
    private int _frameThread;
    private long _generation;
    private int _liveSlot;
    private int _captureSlot = -1;
    private long _frameStart, _allocationStart;
    private int _gc0, _gc1, _gc2;
    private long _captureStart;
    private long _captureDuration;
    private string _label = "";
    private string _captureContext = "";
    public string Context { get; set; } = "";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _inFrame = false;
            _generation++;
            _captureSlot = -1;
            if (!value && IsCapturing) FinishCapture("Stopped: profiling disabled");
        }
    }

    public bool IsCapturing { get; private set; }
    public int CapturedFrames => _capture?.Count ?? 0;
    public string CaptureStatus { get; private set; } = "No capture";

    public void BeginFrame()
    {
        if (!_enabled) return;
        BeginFrame(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    internal void BeginFrame(long timestamp, long allocatedBytes, int gc0, int gc1, int gc2)
    {
        if (!_enabled) return;
        int thread = Environment.CurrentManagedThreadId;
        if (_inFrame && _frameThread != thread) return;
        _frameThread = thread;
        _generation++;
        _inFrame = true;
        Array.Clear(_depth);
        _frameStart = timestamp;
        _allocationStart = allocatedBytes;
        _gc0 = gc0; _gc1 = gc1; _gc2 = gc2;
        _liveSlot = _live.Next;
        _live.Clear(_liveSlot);
        _captureSlot = -1;
        if (IsCapturing && _capture != null)
        {
            if (_capture.Count == 0) _captureStart = timestamp;
            CaptureStatus = "Capturing";
            _captureSlot = _capture.Count;
            _capture.Clear(_captureSlot);
        }
    }

    public void EndFrame()
    {
        if (!_inFrame || _frameThread != Environment.CurrentManagedThreadId) return;
        EndFrame(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    internal void EndFrame(long timestamp, long allocatedBytes, int gc0, int gc1, int gc2)
    {
        if (!_inFrame || _frameThread != Environment.CurrentManagedThreadId) return;
        _inFrame = false;
        long ticks = Math.Max(0, timestamp - _frameStart);
        long bytes = Math.Max(0, allocatedBytes - _allocationStart);
        int g0 = Math.Max(0, gc0 - _gc0), g1 = Math.Max(0, gc1 - _gc1), g2 = Math.Max(0, gc2 - _gc2);
        _live.Finish(_liveSlot, ticks, bytes, g0, g1, g2);
        if (_captureSlot >= 0 && IsCapturing && _capture != null)
        {
            _capture.Finish(_captureSlot, ticks, bytes, g0, g1, g2);
            if (_capture.Count >= CaptureCapacity) FinishCapture("Completed: frame limit (20000)");
            else if (timestamp - _captureStart >= _captureDuration) FinishCapture("Completed: duration reached");
        }
        _captureSlot = -1;
    }

    public Scope Measure(ProfileStage stage)
    {
        if (!_inFrame || _frameThread != Environment.CurrentManagedThreadId || (uint)stage >= StageCount || stage == ProfileStage.Frame) return default;
        return BeginScope(stage, Stopwatch.GetTimestamp());
    }

    internal Scope Measure(ProfileStage stage, long timestamp) =>
        _inFrame && _frameThread == Environment.CurrentManagedThreadId && (uint)stage < StageCount && stage != ProfileStage.Frame
            ? BeginScope(stage, timestamp) : default;

    private Scope BeginScope(ProfileStage stage, long timestamp)
    {
        if (_depth[(int)stage]++ == 0)
        {
            _stageStarts[(int)stage] = timestamp;
            _stageAllocationStarts[(int)stage] = GC.GetAllocatedBytesForCurrentThread();
        }
        return new Scope(this, stage, _generation);
    }

    public void Count(ProfileCounter counter, long amount = 1)
    {
        if (!_inFrame || _frameThread != Environment.CurrentManagedThreadId || (uint)counter >= CounterCount) return;
        _live.Counters[_liveSlot * CounterCount + (int)counter] += amount;
        if (_captureSlot >= 0) _capture!.Counters[_captureSlot * CounterCount + (int)counter] += amount;
    }

    private void Record(ProfileStage stage, long generation, long end)
    {
        if (!_inFrame || _frameThread != Environment.CurrentManagedThreadId || generation != _generation) return;
        if (_depth[(int)stage] <= 0) return;
        bool outermost = --_depth[(int)stage] == 0;
        long ticks = outermost ? Math.Max(0, end - _stageStarts[(int)stage]) : 0;
        long bytes = outermost ? Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _stageAllocationStarts[(int)stage]) : 0;
        int index = _liveSlot * StageCount + (int)stage;
        _live.Ticks[index] += ticks;
        _live.StageBytes[index] += bytes;
        _live.Calls[index]++;
        if (_captureSlot < 0) return;
        index = _captureSlot * StageCount + (int)stage;
        _capture!.Ticks[index] += ticks;
        _capture.StageBytes[index] += bytes;
        _capture.Calls[index]++;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly FrameProfiler? _owner;
        private readonly ProfileStage _stage;
        private readonly long _generation;
        internal Scope(FrameProfiler owner, ProfileStage stage, long generation)
        { _owner = owner; _stage = stage; _generation = generation; }
        public void Dispose()
        {
            if (_owner != null && _owner._inFrame && _owner._frameThread == Environment.CurrentManagedThreadId && _owner._generation == _generation)
                _owner.Record(_stage, _generation, Stopwatch.GetTimestamp());
        }
        internal void DisposeAt(long timestamp) => _owner?.Record(_stage, _generation, timestamp);
    }

    public void StartCapture(double durationSeconds, string label)
    {
        if (!double.IsFinite(durationSeconds)) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        var samples = new Samples(CaptureCapacity);
        Enabled = true;
        _capture = samples;
        _captureSlot = -1;
        _captureDuration = (long)(Math.Clamp(durationSeconds, 1, 60) * Stopwatch.Frequency);
        _label = label ?? "";
        _captureContext = Context ?? "";
        IsCapturing = true;
        CaptureStatus = "Armed: starts next frame";
    }

    public void StopCapture()
    {
        if (IsCapturing) FinishCapture("Stopped: requested");
    }

    private void FinishCapture(string reason)
    {
        IsCapturing = false;
        _captureSlot = -1;
        CaptureStatus = reason;
    }

    public ProfileSummary GetLiveSummary() => Summarize(_live, "Live (last 240 frames)");
    public ProfileSummary GetCaptureSummary() => Summarize(_capture, _label);

    private static ProfileSummary Summarize(Samples? samples, string label)
    {
        int count = samples?.Count ?? 0;
        var stages = new ProfileStageSummary[StageCount];
        var counters = new ProfileCounterSummary[CounterCount];
        var gc = new int[3];
        var frames = new double[count];
        double frameTotal = 0, bytes = 0;
        for (int s = 0; s < StageCount; s++)
        {
            double total = 0, maximum = 0, calls = 0, stageBytes = 0;
            for (int i = 0; i < count; i++)
            {
                int slot = samples!.Slot(i);
                double ms = samples.Ticks[slot * StageCount + s] * TickMilliseconds;
                total += ms;
                maximum = Math.Max(maximum, ms);
                calls += samples.Calls[slot * StageCount + s];
                stageBytes += samples.StageBytes[slot * StageCount + s];
                if (s == 0)
                {
                    frames[i] = ms;
                    frameTotal += ms;
                    bytes += samples.Bytes[slot];
                    for (int g = 0; g < 3; g++) gc[g] += samples.Gc[slot * 3 + g];
                }
            }
            stages[s] = new((ProfileStage)s, count == 0 ? 0 : total / count, maximum, count == 0 ? 0 : calls / count)
            { MeanAllocatedBytes = count == 0 ? 0 : stageBytes / count };
        }
        for (int c = 0; c < CounterCount; c++)
        {
            double total = 0;
            long maximum = long.MinValue;
            for (int i = 0; i < count; i++)
            {
                long value = samples!.Counters[samples.Slot(i) * CounterCount + c];
                total += value;
                maximum = Math.Max(maximum, value);
            }
            counters[c] = new((ProfileCounter)c, count == 0 ? 0 : total / count, count == 0 ? 0 : maximum);
        }
        Array.Sort(frames);
        return new ProfileSummary
        {
            FrameCount = count, Label = label, MeanFrameMs = count == 0 ? 0 : frameTotal / count,
            P50FrameMs = Percentile(frames, .50), P95FrameMs = Percentile(frames, .95),
            P99FrameMs = Percentile(frames, .99), MaxFrameMs = count == 0 ? 0 : frames[^1],
            MeanAllocatedBytes = count == 0 ? 0 : bytes / count, GcCollections = gc, Stages = stages, Counters = counters
        };
    }

    // Nearest-rank percentiles: an observed frame, including for short captures.
    private static double Percentile(double[] sorted, double fraction) => sorted.Length == 0
        ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];

    public string ExportCaptureCsv()
    {
        var text = new StringBuilder();
        text.Append("# Label,").Append(Quote(_label)).AppendLine();
        text.Append("# Status,").Append(Quote(CaptureStatus)).AppendLine();
        text.Append("# Context,").Append(Quote(_captureContext)).AppendLine();
        text.Append("Frame,FrameMs,AllocatedBytes,GC0,GC1,GC2");
        for (int s = 0; s < StageCount; s++)
            text.Append(',').Append((ProfileStage)s).Append("Ms,").Append((ProfileStage)s).Append("Calls,")
                .Append((ProfileStage)s).Append("AllocatedBytes");
        for (int c = 0; c < CounterCount; c++) text.Append(',').Append((ProfileCounter)c);
        text.AppendLine();
        for (int i = 0; i < CapturedFrames; i++)
        {
            int slot = _capture!.Slot(i);
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Number(_capture.Ticks[slot * StageCount] * TickMilliseconds));
            AppendInteger(text, _capture.Bytes[slot]);
            for (int g = 0; g < 3; g++) AppendInteger(text, _capture.Gc[slot * 3 + g]);
            for (int s = 0; s < StageCount; s++)
            {
                text.Append(',').Append(Number(_capture.Ticks[slot * StageCount + s] * TickMilliseconds));
                AppendInteger(text, _capture.Calls[slot * StageCount + s]);
                AppendInteger(text, _capture.StageBytes[slot * StageCount + s]);
            }
            for (int c = 0; c < CounterCount; c++) AppendInteger(text, _capture.Counters[slot * CounterCount + c]);
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static void AppendInteger(StringBuilder text, long value) => text.Append(',').Append(value.ToString(CultureInfo.InvariantCulture));

    public string BuildCaptureReport()
    {
        var summary = GetCaptureSummary();
        var text = new StringBuilder();
        text.Append("Frame profiler: ").AppendLine(summary.Label);
        text.Append("Status: ").AppendLine(CaptureStatus);
        text.Append("Context: ").AppendLine(_captureContext);
        text.Append("Runtime: ").AppendLine(RuntimeInformation.FrameworkDescription);
#if DEBUG
        text.AppendLine("Build: Debug (use Release for representative performance)");
#else
        text.AppendLine("Build: Release");
#endif
        text.Append("OS: ").AppendLine(RuntimeInformation.OSDescription);
        text.Append("CPU count: ").AppendLine(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        text.Append("Frames: ").AppendLine(summary.FrameCount.ToString(CultureInfo.InvariantCulture));
        text.Append("Frame ms: mean ").Append(Number(summary.MeanFrameMs)).Append(", p50 ").Append(Number(summary.P50FrameMs))
            .Append(", p95 ").Append(Number(summary.P95FrameMs)).Append(", p99 ").Append(Number(summary.P99FrameMs))
            .Append(", max ").AppendLine(Number(summary.MaxFrameMs));
        text.Append("Main-thread allocated bytes/frame: ").AppendLine(Number(summary.MeanAllocatedBytes));
        text.Append("Process GC collections (gen 0/1/2): ").Append(summary.GcCollections[0]).Append('/')
            .Append(summary.GcCollections[1]).Append('/').AppendLine(summary.GcCollections[2].ToString(CultureInfo.InvariantCulture));
        text.AppendLine("Slowest inclusive stages (mean ms, max ms, calls/frame, allocated bytes/frame):");
        Array.Sort(summary.Stages, static (a, b) => b.MeanMs.CompareTo(a.MeanMs));
        foreach (var stage in summary.Stages)
        {
            if (stage.Stage == ProfileStage.Frame || stage.MeanCalls == 0) continue;
            text.Append(stage.Stage).Append(": ").Append(Number(stage.MeanMs)).Append(", ")
                .Append(Number(stage.MaxMs)).Append(", ").Append(Number(stage.MeanCalls)).Append(", ")
                .AppendLine(Number(stage.MeanAllocatedBytes));
        }
        text.AppendLine("Counters (mean/frame, max/frame):");
        foreach (var counter in summary.Counters)
            text.Append(counter.Counter).Append(": ").Append(Number(counter.MeanValue)).Append(", ")
                .AppendLine(counter.MaxValue.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("CPU wall time, not CPU utilization. Frame includes waits/frame limiter; inclusive stages overlap and must not be summed. Same-stage recursion times only the outermost span; all calls are counted.");
        text.AppendLine("Shared render substages aggregate main/preview/camera-feed views. SceneDraw includes sorting. SceneNodesVisited counts color-collection visits, including repeated/hidden nodes.");
        text.AppendLine("World/visibility queries include cache hits; evaluation timings measure misses. Caches exist only inside renderer read phases, not across editor updates or separate views.");
        text.AppendLine("ShadowCacheHits covers main/preview directional-map reuse; ShadowBoundsRebuilds includes main/preview bounds recomputation.");
        text.AppendLine("MeshesSubmitted counts scene/ground submissions, not GL draw calls; excludes shadow, picking and direct-overlay draws. Close the profiler panel during recording to reduce UI overhead.");
        text.AppendLine("LightCandidatesExamined sums candidate-list sizes across uncached per-mesh light selections; LightSelectionCacheHits reuses exact-origin selections within one draw pass. PointLightsUploaded sums selected lights across color draws. EmissiveProxyLights counts generated fallback lights (suppressed by screen-space GI).");
        text.AppendLine("GPU work is asynchronous: CPU submission/swap time is not GPU execution time. Allocations are main-thread only and stage totals are inclusive (do not sum overlapping stages); GC counts are process-wide.");
        return text.ToString();
    }

    private sealed class Samples
    {
        internal readonly int Capacity, Limit;
        internal readonly long[] Ticks, Calls, Counters, Bytes, StageBytes;
        internal readonly int[] Gc;
        internal int Count, Next;
        internal Samples(int capacity)
        {
            // One spare slot keeps an unfinished frame out of completed live samples.
            Limit = capacity;
            Capacity = capacity + 1;
            Ticks = new long[Capacity * StageCount];
            Calls = new long[Capacity * StageCount];
            StageBytes = new long[Capacity * StageCount];
            Counters = new long[Capacity * CounterCount];
            Bytes = new long[Capacity];
            Gc = new int[Capacity * 3];
        }
        internal int Slot(int chronologicalIndex) => (Next - Count + chronologicalIndex + Capacity) % Capacity;
        internal void Clear(int slot)
        {
            Array.Clear(Ticks, slot * StageCount, StageCount);
            Array.Clear(Calls, slot * StageCount, StageCount);
            Array.Clear(StageBytes, slot * StageCount, StageCount);
            Array.Clear(Counters, slot * CounterCount, CounterCount);
        }
        internal void Finish(int slot, long ticks, long bytes, int gc0, int gc1, int gc2)
        {
            Ticks[slot * StageCount] = ticks;
            Calls[slot * StageCount] = 1;
            Bytes[slot] = bytes;
            StageBytes[slot * StageCount] = bytes;
            Gc[slot * 3] = gc0; Gc[slot * 3 + 1] = gc1; Gc[slot * 3 + 2] = gc2;
            Count = Math.Min(Count + 1, Limit);
            Next = (slot + 1) % Capacity;
        }
    }
}
