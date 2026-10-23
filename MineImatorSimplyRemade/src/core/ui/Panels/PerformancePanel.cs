using System.Diagnostics;
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.diagnostics;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public sealed class PerformancePanel : UiPanel
{
    public bool IsOpen;
    public Func<string>? CaptureContext { get; set; }

    private string _label = "Model visible";
    private int _durationSeconds = 10;
    private bool _showCapture;
    private bool _wasCapturing;
    private double _nextRefresh;
    private ProfileSummary? _summary;
    private ProfileStageSummary[] _stages = [];
    private string _message = "";
    private string _renderContext = "";

    public void ToggleVisibility()
    {
        IsOpen = !IsOpen;
        if (!IsOpen) return;

        FrameProfiler.Shared.Enabled = true;
        _showCapture = false;
        _nextRefresh = 0;
    }

    public override void Render()
    {
        if (!IsOpen) return;
        using var timing = FrameProfiler.Shared.Measure(ProfileStage.ProfilerUi);
        ImGui.SetNextWindowSize(new Vector2(760, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Rendering Debug / Benchmark###Performance / Benchmark", ref IsOpen))
        {
            ImGui.End();
            return;
        }

        var profiler = FrameProfiler.Shared;
        ImGui.TextUnformatted("F11: toggle rendering debug view");
        bool enabled = profiler.Enabled;
        if (ImGui.Checkbox("Enable CPU profiling", ref enabled))
            profiler.Enabled = enabled;
        ImGui.TextWrapped("CPU wall-clock timings include driver blocking, not asynchronous GPU execution. Stages are inclusive: child rows overlap their parents and must not be added together.");
        ImGui.TextWrapped("Closing this view leaves profiling and active captures running. Uncheck CPU profiling to disable sampling.");
        ImGui.Separator();
        ImGui.SetNextItemWidth(280);
        ImGui.InputText("Capture label", ref _label, 160);
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Duration (seconds, 1-60)", ref _durationSeconds);
        _durationSeconds = Math.Clamp(_durationSeconds, 1, 60);

        ImGui.BeginDisabled(profiler.IsCapturing);
        if (ImGui.Button("Start capture"))
        {
            try
            {
                profiler.Context = CaptureContext?.Invoke() ?? "";
                profiler.StartCapture(_durationSeconds, _label);
                _showCapture = false;
                _message = "Capture starts on the next frame. You can close this panel while recording to reduce UI overhead; reopen it for results. Avoid imports/dialogs during capture.";
                _nextRefresh = 0;
            }
            catch (Exception ex)
            {
                _message = $"Could not start capture: {ex.Message}";
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!profiler.IsCapturing);
        if (ImGui.Button("Stop capture")) profiler.StopCapture();
        ImGui.EndDisabled();
        ImGui.TextUnformatted($"{profiler.CaptureStatus} | {profiler.CapturedFrames:N0} captured frames");

        if (_wasCapturing && !profiler.IsCapturing)
        {
            _showCapture = profiler.CapturedFrames > 0;
            _nextRefresh = 0;
        }
        _wasCapturing = profiler.IsCapturing;
        ImGui.BeginDisabled(profiler.IsCapturing || profiler.CapturedFrames == 0);
        if (ImGui.Button("Copy capture report"))
        {
            ImGui.SetClipboardText(profiler.BuildCaptureReport());
            _message = "Capture report copied to clipboard.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Export capture CSV")) ExportCsv(profiler);
        ImGui.EndDisabled();
        ImGui.TextWrapped("Compare equal-length captures with the model visible, hidden, then removed. Keep camera, preview visibility, selection, render mode and FPS cap consistent. Capture labels are included in exports; scene names and asset paths are not collected.");
        if (_message.Length > 0) ImGui.TextWrapped(_message);

        ImGui.Separator();
        ImGui.BeginDisabled(profiler.CapturedFrames == 0);
        if (ImGui.Checkbox("Show captured results instead of live window", ref _showCapture))
            _nextRefresh = 0;
        ImGui.EndDisabled();
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (now >= _nextRefresh || _summary == null)
        {
            _summary = _showCapture ? profiler.GetCaptureSummary() : profiler.GetLiveSummary();
            _stages = _summary.Stages.Where(row => row.MeanCalls > 0 || row.MeanMs > 0)
                .OrderByDescending(row => row.MeanMs).ToArray();
            _renderContext = CaptureContext?.Invoke() ?? "";
            _nextRefresh = now + 0.5;
        }

        var summary = _summary;
        ImGui.TextUnformatted(_showCapture ? $"Capture: {summary.Label}" : "Live: last 240 profiled frames (updates twice per second)");
        if (summary.FrameCount == 0)
        {
            ImGui.TextUnformatted("No complete samples yet. Enable profiling or start a capture.");
        }
        else
        {
            double fps = summary.MeanFrameMs > 0 ? 1000 / summary.MeanFrameMs : 0;
            ImGui.TextUnformatted($"{summary.FrameCount:N0} frames | Mean {summary.MeanFrameMs:F3} ms ({fps:F1} FPS) | Max {summary.MaxFrameMs:F3} ms");
            ImGui.TextUnformatted($"P50 {summary.P50FrameMs:F3} ms | P95 {summary.P95FrameMs:F3} ms | P99 {summary.P99FrameMs:F3} ms");
            ImGui.TextUnformatted($"Main-thread allocation {summary.MeanAllocatedBytes / 1024:F1} KiB/frame | GC collections {string.Join(" / ", summary.GcCollections)} (Gen 0 / 1 / 2)");
            RenderStages();
            if (ImGui.CollapsingHeader("Work counters", ImGuiTreeNodeFlags.DefaultOpen))
                foreach (var row in summary.Counters)
                    ImGui.TextUnformatted($"{row.Counter}: mean {row.MeanValue:N1}/frame, max {row.MaxValue:N0}");
            ImGui.TextWrapped("Counters count repeated work across views, not unique objects. MeshesSubmitted counts scene/ground submissions, excluding shadow/pick/direct-overlay draws; it is not a GL draw-call count. SceneDraw includes sorting. Frame includes limiter/swap waits. Disable profiling for normal use.");
        }
        if (ImGui.CollapsingHeader("Current renderer / scene details"))
            ImGui.TextWrapped(_renderContext);
        ImGui.End();
    }

    private void RenderStages()
    {
        if (!ImGui.BeginTable("CpuStages", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Stage (inclusive)");
        ImGui.TableSetupColumn("Mean ms");
        ImGui.TableSetupColumn("Max ms");
        ImGui.TableSetupColumn("Calls/frame");
        ImGui.TableHeadersRow();
        foreach (var row in _stages)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Stage.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.MeanMs:F3}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.MaxMs:F3}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.MeanCalls:N1}");
        }
        ImGui.EndTable();
    }

    private void ExportCsv(FrameProfiler profiler)
    {
        try
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"MineImator-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var result = Dialog.FileSave("csv", defaultPath);
            if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path)) return;
            string path = Path.HasExtension(result.Path) ? result.Path : result.Path + ".csv";
            File.WriteAllText(path, profiler.ExportCaptureCsv(), new UTF8Encoding(false));
            _message = $"Saved benchmark: {path}";
        }
        catch (Exception ex)
        {
            _message = $"Could not export benchmark: {ex.Message}";
        }
    }
}
