using System.Numerics;
using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui;

public sealed class PerformanceMetricsOverlay
{
    public bool IsVisible { get; private set; }

    private int _frames;
    private double _elapsedMs;
    private double _workMs;
    private double _peakMs;
    private long _allocatedBytes;
    private readonly int[] _initialGc = new int[3];
    private string _text = "Performance (F12)\nCollecting metrics...";

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
        if (!IsVisible) return;

        _frames = 0;
        _elapsedMs = _workMs = _peakMs = 0;
        _allocatedBytes = GC.GetTotalAllocatedBytes(false);
        for (int generation = 0; generation < _initialGc.Length; generation++)
            _initialGc[generation] = GC.CollectionCount(generation);
        _text = "Performance (F12)\nCollecting metrics...";
    }

    // Sample the whole app loop, including both windows and the FPS limiter,
    // rather than a window's ImGui frame rate or profiler data.
    public void RecordFrame(double frameMs, double workMs, int targetFps)
    {
        if (!IsVisible) return;

        _frames++;
        _elapsedMs += frameMs;
        _workMs += workMs;
        _peakMs = Math.Max(_peakMs, frameMs);
        if (_elapsedMs < 500) return;

        const double mib = 1024 * 1024;
        long allocated = GC.GetTotalAllocatedBytes(false);
        double allocationRate = Math.Max(0, allocated - _allocatedBytes) / mib / (_elapsedMs / 1000);
        string cap = targetFps > 0 ? $"{targetFps} FPS" : "Unlimited";
        _text = $"Performance (F12)\n" +
                $"FPS: {_frames * 1000 / _elapsedMs:F1}\n" +
                $"Frame: {_elapsedMs / _frames:F2} ms | Peak: {_peakMs:F2} ms\n" +
                $"Work + swap: {_workMs / _frames:F2} ms\n" +
                $"RAM: {Environment.WorkingSet / mib:F0} MiB | Managed: {GC.GetTotalMemory(false) / mib:F0} MiB\n" +
                $"Alloc (app): {allocationRate:F2} MiB/s\n" +
                $"GC since shown (0/1/2): {GC.CollectionCount(0) - _initialGc[0]}/" +
                $"{GC.CollectionCount(1) - _initialGc[1]}/{GC.CollectionCount(2) - _initialGc[2]}\n" +
                $"FPS cap: {cap}";
        _allocatedBytes = allocated;
        _frames = 0;
        _elapsedMs = _workMs = _peakMs = 0;
    }

    public void Render()
    {
        if (!IsVisible) return;

        var viewport = ImGui.GetMainViewport();
        var padding = ImGui.GetStyle().WindowPadding;
        var size = ImGui.CalcTextSize(_text) + padding * 2;
        var position = viewport.WorkPos + new Vector2(Math.Max(0, viewport.WorkSize.X - size.X - padding.X), padding.Y);
        var drawList = ImGui.GetForegroundDrawList();
        drawList.AddRectFilled(position, position + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 0.86f)), 6f);
        drawList.AddRect(position, position + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.75f, 0.95f, 0.95f)), 6f);
        drawList.AddText(position + padding, ImGui.ColorConvertFloat4ToU32(Vector4.One), _text);
    }
}
