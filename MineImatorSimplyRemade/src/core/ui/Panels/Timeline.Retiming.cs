using Hexa.NET.ImGui;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Timeline
{
    private enum KeyframeRetimingMode { None, Scale, Stretch }

    private KeyframeRetimingMode _keyframeRetimingMode;
    private readonly Dictionary<TimelineKeyframe, int> _retimingStartFrames = new();
    private int _retimingPivot;
    private int _retimingSpan;
    private int _retimingHandle;
    private float _retimingMouseX;
    private float _retimingScrollX;
    private double _retimingScale = 1;
    private int _retimingInputFrame = -1;

    public bool IsRetimingKeyframes => _keyframeRetimingMode != KeyframeRetimingMode.None;
    public event Action<IReadOnlyList<SceneObject>>? KeyframesRetiming;

    private bool RetimingBlocksInput => IsRetimingKeyframes || _retimingInputFrame == ImGui.GetFrameCount();

    private void HandleKeyframeScaleShortcut()
    {
        if ((IsWindowHovered || HasEditFocus) && !RetimingBlocksInput && !ImGui.IsAnyItemActive() &&
            !_isDraggingKeyframe && !_isDragSelecting && !_isDraggingPlayhead &&
            !_isDraggingPlaybackRegion && !_isDraggingMarker && !_isDraggingAudioClip &&
            Keybinds.Pressed("scaleKeyframes"))
            BeginKeyframeRetiming(KeyframeRetimingMode.Scale, _currentFrame, 0);
    }

    private bool BeginKeyframeStretch(TimelineKeyframe handle)
    {
        if (!_selectedKeyframes.Contains(handle) || !_keyframeOwners.TryGetValue(handle, out var owner))
            return false;

        var row = _selectedKeyframes.Where(k => _keyframeOwners.TryGetValue(k, out var candidate) && candidate == owner);
        int first = row.Min(k => k.Frame);
        int last = row.Max(k => k.Frame);
        if (handle.Frame != first && handle.Frame != last)
            return false;

        int min = _selectedKeyframes.Min(k => k.Frame);
        int max = _selectedKeyframes.Max(k => k.Frame);
        bool leftEdge = first == last ? handle.Frame - min < max - handle.Frame : handle.Frame == first;
        int pivot = leftEdge ? max : min;
        if (handle.Frame == pivot)
            return false;

        return BeginKeyframeRetiming(KeyframeRetimingMode.Stretch, pivot, handle.Frame);
    }

    private bool BeginKeyframeRetiming(KeyframeRetimingMode mode, int pivot, int handle)
    {
        if (_selectedKeyframes.Count < 2 || _selectedKeyframes.Any(k => !_keyframeOwners.ContainsKey(k)))
            return false;

        int span = _selectedKeyframes.Max(k => k.Frame) - _selectedKeyframes.Min(k => k.Frame);
        if (span == 0)
            return false;

        _retimingStartFrames.Clear();
        foreach (var keyframe in _selectedKeyframes)
            _retimingStartFrames.Add(keyframe, keyframe.Frame);
        _retimingPivot = pivot;
        _retimingSpan = span;
        _retimingHandle = handle;
        _retimingMouseX = ImGui.GetMousePos().X;
        _retimingScrollX = _hScrollOffset;
        _retimingScale = 1;
        _keyframeRetimingMode = mode;
        _retimingInputFrame = ImGui.GetFrameCount();
        _isPlaying = false;
        PauseAllAudio();
        return true;
    }

    private void UpdateKeyframeRetiming()
    {
        if (!IsRetimingKeyframes)
            return;

        _retimingInputFrame = ImGui.GetFrameCount();
        var io = ImGui.GetIO();
        if (io.AppFocusLost || io.WantTextInput || Keybinds.IsCapturing ||
            ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel) ||
            ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            FinishKeyframeRetiming(commit: false);
            return;
        }

        double delta = (io.MousePos.X - _retimingMouseX + _hScrollOffset - _retimingScrollX) / _pixelsPerFrame;
        double scale = _keyframeRetimingMode == KeyframeRetimingMode.Scale
            ? 1 + delta / _retimingSpan
            : 1 + delta / (_retimingHandle - _retimingPivot);
        PreviewKeyframeRetiming(scale);

        bool confirm = _keyframeRetimingMode == KeyframeRetimingMode.Scale
            ? ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            : !ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (confirm)
            FinishKeyframeRetiming(commit: true);
    }

    private void PreviewKeyframeRetiming(double scale)
    {
        if (!double.IsFinite(scale))
        {
            Console.Error.WriteLine("Keyframe retiming cancelled: non-finite mouse input.");
            FinishKeyframeRetiming(commit: false);
            return;
        }

        double limit = double.MaxValue;
        foreach (int frame in _retimingStartFrames.Values)
        {
            int offset = frame - _retimingPivot;
            if (offset < 0)
                limit = Math.Min(limit, (double)_retimingPivot / -offset);
            else if (offset > 0)
                limit = Math.Min(limit, (double)(int.MaxValue - 30 - _retimingPivot) / offset);
        }
        _retimingScale = Math.Clamp(scale, 0, Math.Max(0, limit));
        foreach (var (keyframe, frame) in _retimingStartFrames)
            keyframe.Frame = (int)Math.Clamp(Math.Round(_retimingPivot + (frame - _retimingPivot) * _retimingScale),
                0, int.MaxValue - 30);
        SortRetimedTracks();
    }

    private void SortRetimedTracks()
    {
        foreach (var owner in _retimingStartFrames.Keys.Select(k => _keyframeOwners[k]).Distinct())
            GetKeyframesForProperty(owner.obj, owner.path).Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    private void FinishKeyframeRetiming(bool commit)
    {
        if (!IsRetimingKeyframes)
            return;

        bool changed = _retimingStartFrames.Any(pair => pair.Key.Frame != pair.Value);
        if (!commit)
        {
            foreach (var (keyframe, frame) in _retimingStartFrames)
                keyframe.Frame = frame;
            SortRetimedTracks();
        }
        else if (changed)
        {
            // Only confirmed timing reaches the scene, so previews cannot create undo entries or overwrite keys.
            KeyframesRetiming?.Invoke(_retimingStartFrames.Keys.Select(k => _keyframeOwners[k].obj).Distinct().ToArray());
            RemoveSelectedCollisionsAndSave();
            ApplyKeyframesAtCurrentFrame();
        }
        _retimingStartFrames.Clear();
        _keyframeRetimingMode = KeyframeRetimingMode.None;
        _retimingInputFrame = ImGui.GetFrameCount();
    }

    private void RenderKeyframeRetimingStatus()
    {
        if (!IsRetimingKeyframes)
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        ImGui.SameLine();
        UiText.Disabled(Localization.Format(_keyframeRetimingMode == KeyframeRetimingMode.Scale
            ? "text.panel.timeline.scaling_keyframes_hint" : "text.panel.timeline.stretching_keyframes_hint", _retimingScale));
    }
}
