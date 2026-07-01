using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

// ── Data models ───────────────────────────────────────────────────────────────

/// <summary>
/// A keyframe stored inside the timeline's working state.
/// Mirrors <see cref="ObjectKeyframe"/> on <see cref="SceneObject"/> but kept
/// separate so the timeline can manipulate them before flushing back.
/// </summary>
public class TimelineKeyframe
{
    public int    Frame             { get; set; }
    public object Value             { get; set; }
    public string InterpolationType { get; set; } = "linear";
}

/// <summary>Represents a row (or group-header row) shown in the timeline.</summary>
public class TimelineProperty
{
    public SceneObject Object       { get; set; }
    public string      PropertyPath { get; set; }
    public string      Label        { get; set; }
    public bool        IsGroupHeader{ get; set; }
    public string[]    GroupPaths   { get; set; }
    public int         Indent       { get; set; }
}

// ── Timeline panel ────────────────────────────────────────────────────────────

/// <summary>
/// ImGui timeline panel.  Ported from the Godot TimelinePanel.cs in
/// simply-remade-nuxi.
///
/// Features: frame-based playback, collapsible property groups, keyframe
/// add/remove/move, interpolation types, multi-select, apply-to-all-objects.
/// </summary>
public class Timeline : UiPanel
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static Timeline Instance { get; private set; }

    // ── External wiring ───────────────────────────────────────────────────────

    /// <summary>Main viewport — used to enumerate all scene objects for playback.</summary>
    public Viewport Viewport { get; set; }

    // ── Playback ──────────────────────────────────────────────────────────────

    private int    _currentFrame     = 0;
    private int    _maxFrames        = 300;
    private float  _frameRate        = 30f;
    private float  _pixelsPerFrame   = 5f;
    private bool   _isPlaying        = false;
    private bool   _autoKeyframe     = false;
    private double _frameAccumulator = 0.0;
    private long   _lastTimestamp    = Stopwatch.GetTimestamp();

    // ── Scroll sync ───────────────────────────────────────────────────────────

    private float _hScrollOffset = 0f;
    private float _vScrollOffset = 0f;

    // ── Keyframe data ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<TimelineKeyframe>> _propertyKeyframes = new();
    private readonly List<TimelineProperty>                     _displayRows        = new();
    private readonly Dictionary<string, bool>                   _groupExpanded      = new();

    // ── Selection ─────────────────────────────────────────────────────────────

    private readonly List<TimelineKeyframe>                                        _selectedKeyframes = new();
    private readonly Dictionary<TimelineKeyframe, (SceneObject obj, string path)> _keyframeOwners    = new();

    // ── Drag-keyframe ─────────────────────────────────────────────────────────

    private bool              _isDraggingKeyframe = false;
    private TimelineKeyframe? _draggedKeyframe;
    private int               _dragStartFrame = 0;
    private readonly Dictionary<TimelineKeyframe, int> _selectedStartFrames = new();

    // ── Playhead drag ─────────────────────────────────────────────────────────

    /// <summary>True while the user is dragging the playhead.</summary>
    private bool  _isDraggingPlayhead = false;
    /// <summary>Screen-space X of the ruler's left edge at drag start (accounts for scroll).</summary>
    private float _rulerScreenLeftAtDrag = 0f;
    /// <summary>Horizontal scroll offset at the moment the drag started.</summary>
    private float _rulerScrollAtDrag = 0f;

    // ── Drag-select ───────────────────────────────────────────────────────────

    private bool    _isDragSelecting = false;
    private bool    _wasDragging     = false;
    private Vector2 _dragSelectStart;
    private Vector2 _dragSelectEnd;
    private const float DragThreshold = 3f;

    // ── Context menu ──────────────────────────────────────────────────────────

    private bool              _openContextMenu = false;
    private TimelineKeyframe? _ctxKeyframe;
    private SceneObject?      _ctxObject;
    private string?           _ctxPropPath;

    // ── Layout ────────────────────────────────────────────────────────────────

    private const float LeftColumnWidth    = 200f;
    private const float RowHeight          = 22f;
    private const float RulerHeight        = 24f;
    private const float KeyframeDiamondSize = 5f;

    // ── Public API ────────────────────────────────────────────────────────────

    public int   CurrentFrame => _currentFrame;
    public int   MaxFrames    => _maxFrames;
    public float Framerate    => _frameRate;

    public void SetFrameRate(float frameRate)
    {
        _frameRate = Math.Clamp(frameRate, 1f, 120f);
        _frameAccumulator = 0.0;
    }

    public void SetCurrentFrame(int frame)
    {
        _currentFrame = Math.Max(0, frame);
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    public void SetCurrentFrameForRender(int frame)
    {
        _currentFrame = Math.Max(0, frame);
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: true);
    }

    public ProjectTimelineState ExportProjectState()
    {
        return new ProjectTimelineState
        {
            CurrentFrame = _currentFrame,
            MaxFrames = _maxFrames,
            FrameRate = _frameRate,
            AutoKeyframe = _autoKeyframe
        };
    }

    public void ImportProjectState(ProjectTimelineState? state)
    {
        OnSelectionChanged();

        if (state == null)
        {
            _currentFrame = 0;
            _frameAccumulator = 0.0;
            ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
            return;
        }

        _maxFrames = Math.Max(10, state.MaxFrames);
        _frameRate = Math.Clamp(state.FrameRate, 1f, 120f);
        _autoKeyframe = state.AutoKeyframe;
        _currentFrame = Math.Max(0, state.CurrentFrame);
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    /// <summary>
    /// Subscribe to <see cref="SelectionManager"/> events.
    /// Call once from MainWindow after SelectionManager.Initialize().
    /// </summary>
    public void Initialize()
    {
        Instance = this;
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
        TimelineIcons.Initialize(Gl);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render()
    {
        UpdatePlayback();

        if (!ImGui.Begin("Timeline"))
        {
            ImGui.End();
            return;
        }

        RenderTransportControls();
        ImGui.Separator();

        float availW = ImGui.GetContentRegionAvail().X;
        float availH = ImGui.GetContentRegionAvail().Y;
        float statusH = ImGui.GetFrameHeightWithSpacing();
        float tracksH = Math.Max(availH - RulerHeight - ImGui.GetStyle().ItemSpacing.Y - statusH, 60f);

        RenderRulerRow(availW);
        RenderTrackArea(availW, tracksH);

        // ── Global playhead drag (works even when mouse has left the ruler) ───
        if (_isDraggingPlayhead)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                float mouseX   = ImGui.GetMousePos().X;
                float localX   = mouseX - _rulerScreenLeftAtDrag + _rulerScrollAtDrag;
                int   newFrame = Math.Max(0, (int)MathF.Round(localX / _pixelsPerFrame));
                if (newFrame != _currentFrame)
                {
                    _currentFrame     = newFrame;
                    _frameAccumulator = 0.0;
                    ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
                }
            }
            else
            {
                _isDraggingPlayhead = false;
            }
        }

        ImGui.Text($"Frame: {_currentFrame}  ({_currentFrame / _frameRate:F2}s)");

        // Deferred context menu popup
        if (_openContextMenu)
        {
            _openContextMenu = false;
            ImGui.OpenPopup("##kf_ctx");
        }
        RenderContextMenu();

        ImGui.End();
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private void UpdatePlayback()
    {
        long   now   = Stopwatch.GetTimestamp();
        double delta = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;

        if (!_isPlaying) { _frameAccumulator = 0.0; return; }

        _frameAccumulator += delta * _frameRate;
        int advance = (int)_frameAccumulator;
        _frameAccumulator -= advance;
        if (advance <= 0) return;

        int prev = _currentFrame;
        _currentFrame += advance;

        int furthest = _maxFrames;
        foreach (var kvp in _propertyKeyframes)
            foreach (var kf in kvp.Value)
                if (kf.Frame > furthest) furthest = kf.Frame;

        if (_currentFrame > furthest) { _currentFrame = 0; _frameAccumulator = 0.0; }
        if (_currentFrame != prev) ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    // ── Transport controls ────────────────────────────────────────────────────

    private void RenderTransportControls()
    {
        var  sz  = new System.Numerics.Vector2(20, 20);
        bool ico = TimelineIcons.IsLoaded;

        // Jump to start
        if (ico ? IcoBtn("##js", TimelineIcons.JumpStart,   sz) : ImGui.Button("|<"))
            JumpToStart();
        ImGui.SameLine();

        // Step backward
        if (ico ? IcoBtn("##sb", TimelineIcons.StepBack,    sz) : ImGui.Button("<"))
            StepBackward();
        ImGui.SameLine();

        // Play / Pause
        if (_isPlaying)
        {
            if (ico ? IcoBtn("##pp", TimelineIcons.Pause, sz) : ImGui.Button(" || "))
                _isPlaying = false;
        }
        else
        {
            if (ico ? IcoBtn("##pp", TimelineIcons.Play,  sz) : ImGui.Button(" > "))
            {
                _isPlaying = true;
                _lastTimestamp = Stopwatch.GetTimestamp();
            }
        }
        ImGui.SameLine();

        // Stop
        if (ico ? IcoBtn("##st", TimelineIcons.Stop,        sz) : ImGui.Button("[|]"))
            Stop();
        ImGui.SameLine();

        // Step forward
        if (ico ? IcoBtn("##sf", TimelineIcons.StepForward, sz) : ImGui.Button(">"))
            StepForward();
        ImGui.SameLine();

        // Jump to end
        if (ico ? IcoBtn("##je", TimelineIcons.JumpEnd,     sz) : ImGui.Button(">|"))
            JumpToLastKeyframe();
        ImGui.SameLine();

        // ── Auto-keyframe record button ──────────────────────────────────────
        ImGui.Spacing(); ImGui.SameLine();
        bool autoKeyNow = _autoKeyframe;
        if (!ico)
        {
            if (autoKeyNow)
                ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.80f, 0.10f, 0.10f, 1.00f));
            if (ImGui.Button(" \u25cf "))
                _autoKeyframe = !_autoKeyframe;
            if (autoKeyNow)
                ImGui.PopStyleColor();
        }
        else
        {
            var tint = autoKeyNow
                ? new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f)
                : new System.Numerics.Vector4(1f, 1f,   1f,   1f);
            if (IcoBtnTinted("##ak", TimelineIcons.AutoKey, sz, tint))
                _autoKeyframe = !_autoKeyframe;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_autoKeyframe ? "Auto-keyframing ON" : "Auto-keyframing OFF");

        ImGui.Text("  FPS:");
        ImGui.SameLine();
        int fps = (int)_frameRate;
        ImGui.SetNextItemWidth(55f);
        if (ImGui.InputInt("##fps", ref fps, 0, 0))
            _frameRate = Math.Clamp(fps, 1, 120);

        ImGui.SameLine();
        ImGui.Text("  Frames:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60f);
        if (ImGui.InputInt("##maxf", ref _maxFrames, 0, 0))
            _maxFrames = Math.Max(10, _maxFrames);
    }

    private void JumpToStart()          { _currentFrame = 0; _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    private void Stop()                 { _isPlaying = false; JumpToStart(); }
    private void StepBackward()         { _currentFrame = Math.Max(0, _currentFrame - 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    private void StepForward()          { _currentFrame = Math.Min(_maxFrames, _currentFrame + 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }

    // ── ImageButton helpers (byte* API required by Hexa.NET.ImGui 2.x) ───────

    private static unsafe bool IcoBtn(string id, uint texId, System.Numerics.Vector2 sz)
    {
        var tex = new ImTextureRef(texId: (ulong)texId);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(id + '\0');
        fixed (byte* p = b) return ImGui.ImageButton(p, tex, sz);
    }

    private static unsafe bool IcoBtnTinted(string id, uint texId, System.Numerics.Vector2 sz,
                                             System.Numerics.Vector4 tint)
    {
        var tex  = new ImTextureRef(texId: (ulong)texId);
        var uv0  = new System.Numerics.Vector2(0, 0);
        var uv1  = new System.Numerics.Vector2(1, 1);
        var bg   = new System.Numerics.Vector4(0, 0, 0, 0);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(id + '\0');
        fixed (byte* p = b) return ImGui.ImageButton(p, tex, sz, uv0, uv1, bg, tint);
    }
    private void JumpToLastKeyframe()
    {
        int last = 0;
        foreach (var kvp in _propertyKeyframes)
            foreach (var kf in kvp.Value)
                if (kf.Frame > last) last = kf.Frame;
        _currentFrame = last;
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    // ── Frame ruler ───────────────────────────────────────────────────────────

    private void RenderRulerRow(float availW)
    {
        ImGui.Dummy(new Vector2(LeftColumnWidth, RulerHeight));
        ImGui.SameLine(0, 0);

        var rulerSize = new Vector2(availW - LeftColumnWidth, RulerHeight);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##ruler", rulerSize, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetScrollX(_hScrollOffset);
        ImGui.PopStyleVar();

        var   dl      = ImGui.GetWindowDrawList();
        var   wPos    = ImGui.GetWindowPos();
        var   wSize   = ImGui.GetWindowSize();
        float scrollX = ImGui.GetScrollX();

        dl.AddRectFilled(wPos, wPos + wSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.14f, 0.14f, 0.14f, 1f)));

        int startF = Math.Max(0, (int)(scrollX / _pixelsPerFrame) - 1);
        int endF   = Math.Min((int)(_maxFrames * 1.5f), (int)((scrollX + wSize.X) / _pixelsPerFrame) + 2);

        for (int f = startF; f <= endF; f++)
        {
            float sx = wPos.X + f * _pixelsPerFrame - scrollX;
            if (sx < wPos.X - 2 || sx > wPos.X + wSize.X + 2) continue;

            if (f % 10 == 0)
            {
                dl.AddLine(new Vector2(sx, wPos.Y + RulerHeight * 0.35f),
                           new Vector2(sx, wPos.Y + RulerHeight), 0xFFAAAAAA, 1f);
                if (f % 30 == 0 || _pixelsPerFrame >= 4f)
                    dl.AddText(new Vector2(sx + 2f, wPos.Y + 2f), 0xFFCCCCCC, f.ToString());
            }
            else if (f % 5 == 0)
            {
                dl.AddLine(new Vector2(sx, wPos.Y + RulerHeight * 0.55f),
                           new Vector2(sx, wPos.Y + RulerHeight), 0xFF666666, 1f);
            }
        }

        // Playhead triangle on ruler
        float phX = wPos.X + _currentFrame * _pixelsPerFrame - scrollX;
        dl.AddLine(new Vector2(phX, wPos.Y), new Vector2(phX, wPos.Y + RulerHeight), 0xFF3377FF, 2f);
        dl.AddTriangleFilled(
            new Vector2(phX - 5f, wPos.Y),
            new Vector2(phX + 5f, wPos.Y),
            new Vector2(phX, wPos.Y + 10f), 0xFF3377FF);

        // Start playhead drag on click (global drag handled in Render() outside this child)
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _isDraggingPlayhead      = true;
            _rulerScreenLeftAtDrag   = wPos.X;
            _rulerScrollAtDrag       = scrollX;
            // Seek immediately on the first click
            int newFrame = Math.Max(0, (int)MathF.Round((ImGui.GetMousePos().X - wPos.X + scrollX) / _pixelsPerFrame));
            if (newFrame != _currentFrame) { _currentFrame = newFrame; _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
        }

        ImGui.EndChild();
    }

    // ── Main track area ───────────────────────────────────────────────────────

    private void RenderTrackArea(float availW, float tracksH)
    {
        float rightW = availW - LeftColumnWidth;

        // Left column: property labels (no horizontal scroll)
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 2f));
        ImGui.BeginChild("##left_labels", new Vector2(LeftColumnWidth, tracksH), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetScrollY(_vScrollOffset);
        RenderLeftLabels();
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.SameLine(0, 0);

        // Right column: keyframe tracks.
        // AlwaysHorizontalScrollbar keeps the scrollbar permanently visible so the
        // content-area height never fluctuates and rows stay vertically aligned.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##right_tracks", new Vector2(rightW, tracksH), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar);

        // Right panel is the scroll master; left panel mirrors it each frame.
        _hScrollOffset = ImGui.GetScrollX();
        _vScrollOffset = ImGui.GetScrollY();

        RenderKeyframeTracks();

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    // ── Left labels ───────────────────────────────────────────────────────────

    private void RenderLeftLabels()
    {
        if (_displayRows.Count == 0)
        {
            ImGui.TextDisabled("No object selected");
            return;
        }

        foreach (var row in _displayRows)
        {
            if (row.PropertyPath == "__header__")
            {
                float hY = ImGui.GetCursorPosY();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.5f, 1f));
                // Vertically centre the text within RowHeight
                ImGui.SetCursorPosY(hY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.Text(row.Label);
                ImGui.PopStyleColor();
                ImGui.SetCursorPosY(hY + RowHeight);
                ImGui.Dummy(Vector2.Zero);  // commit boundary
                continue;
            }

            if (row.Indent > 0 && !IsGroupChildVisible(row))
                continue;

            float rowY = ImGui.GetCursorPosY();

            if (row.Indent > 0) ImGui.Indent(12f);

            if (row.IsGroupHeader)
            {
                string gk  = $"{row.Object.ObjectId}.{row.PropertyPath}";
                _groupExpanded.TryAdd(gk, false);
                bool   exp = _groupExpanded[gk];

                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.ArrowButton($"##arr_{gk}", exp ? ImGuiDir.Down : ImGuiDir.Right))
                    _groupExpanded[gk] = !exp;
                ImGui.SameLine();
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.Text(row.Label);
                ImGui.SameLine();
                ImGui.SetCursorPosX(LeftColumnWidth - 26f);
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.Button($"+##grp_{gk}"))
                    foreach (var path in row.GroupPaths ?? Array.Empty<string>())
                        AddKeyframeForProperty(row.Object, path, _currentFrame);
            }
            else
            {
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.Text(row.Label);
                ImGui.SameLine();
                ImGui.SetCursorPosX(LeftColumnWidth - 26f);
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.Button($"+##{row.Object.ObjectId}_{row.PropertyPath}"))
                    AddKeyframeForProperty(row.Object, row.PropertyPath, _currentFrame);
            }

            if (row.Indent > 0) ImGui.Unindent(12f);

            // Force the row to consume exactly RowHeight, matching the right panel.
            ImGui.SetCursorPosY(rowY + RowHeight);
            ImGui.Dummy(Vector2.Zero);  // commit boundary
        }
    }

    private bool IsGroupChildVisible(TimelineProperty childRow)
    {
        foreach (var header in _displayRows)
        {
            if (!header.IsGroupHeader || header.Object != childRow.Object) continue;
            if (header.GroupPaths == null || !header.GroupPaths.Contains(childRow.PropertyPath)) continue;
            string gk = $"{header.Object.ObjectId}.{header.PropertyPath}";
            return _groupExpanded.TryGetValue(gk, out bool exp) && exp;
        }
        return true; // Not inside any group → always visible
    }

    // ── Keyframe tracks ───────────────────────────────────────────────────────

    private void RenderKeyframeTracks()
    {
        float contentW = _maxFrames * _pixelsPerFrame * 1.5f;

        if (_displayRows.Count == 0)
        {
            ImGui.Dummy(new Vector2(contentW, 60f));
            return;
        }

        var   dl      = ImGui.GetWindowDrawList();
        var   wPos    = ImGui.GetWindowPos();
        var   wSize   = ImGui.GetWindowSize();
        float scrollX = ImGui.GetScrollX();
        float scrollY = ImGui.GetScrollY();

        foreach (var row in _displayRows)
        {
            // Skip hidden rows (collapsed groups)
            if (row.Indent > 0 && !IsGroupChildVisible(row)) continue;
            if (row.PropertyPath == "__header__")
            {
                // Object header — draw a separator-like bar
                var hPos = ImGui.GetCursorScreenPos();
                dl.AddRectFilled(hPos, hPos + new Vector2(contentW, RowHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.1f, 1f)));
                ImGui.Dummy(new Vector2(contentW, RowHeight));
                continue;
            }

            var  trackPos = ImGui.GetCursorScreenPos();

            // Track background + grid
            DrawTrackBg(dl, trackPos, contentW, wPos, wSize, scrollX);

            // Keyframe diamonds
            if (!row.IsGroupHeader)
                DrawSingleTrackKeyframes(dl, trackPos, row, scrollX);
            else
                DrawGroupTrackKeyframes(dl, trackPos, row, scrollX);

            // Handle mouse interactions on this track row
            HandleTrackMouse(row, trackPos, contentW, scrollX, wPos, wSize);

            ImGui.Dummy(new Vector2(contentW, RowHeight));
        }

        // Global: playhead vertical line
        float phX = wPos.X + _currentFrame * _pixelsPerFrame - scrollX;
        if (phX >= wPos.X && phX <= wPos.X + wSize.X)
            dl.AddLine(new Vector2(phX, wPos.Y), new Vector2(phX, wPos.Y + wSize.Y), 0xBB3377FF, 2f);

        // Drag-select box
        if (_isDragSelecting && _wasDragging)
        {
            float minX = MathF.Min(_dragSelectStart.X, _dragSelectEnd.X);
            float minY = MathF.Min(_dragSelectStart.Y, _dragSelectEnd.Y);
            float maxX = MathF.Max(_dragSelectStart.X, _dragSelectEnd.X);
            float maxY = MathF.Max(_dragSelectStart.Y, _dragSelectEnd.Y);
            dl.AddRectFilled(new Vector2(minX, minY), new Vector2(maxX, maxY),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.18f)));
            dl.AddRect(new Vector2(minX, minY), new Vector2(maxX, maxY),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.85f)), 0f, ImDrawFlags.None, 1.5f);
        }
    }

    private void DrawTrackBg(ImDrawListPtr dl, Vector2 trackPos, float contentW,
                              Vector2 wPos, Vector2 wSize, float scrollX)
    {
        dl.PushClipRect(
            new Vector2(wPos.X, trackPos.Y),
            new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight), true);

        dl.AddRectFilled(
            new Vector2(wPos.X, trackPos.Y),
            new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.12f, 1f)));

        int startF = Math.Max(0, (int)(scrollX / _pixelsPerFrame) - 1);
        int endF   = Math.Min((int)(_maxFrames * 1.5f), (int)((scrollX + wSize.X) / _pixelsPerFrame) + 1);
        for (int f = startF; f <= endF; f++)
        {
            if (f % 10 != 0) continue;
            float x = trackPos.X + f * _pixelsPerFrame - scrollX;
            dl.AddLine(new Vector2(x, trackPos.Y), new Vector2(x, trackPos.Y + RowHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.22f, 0.22f, 0.6f)), 1f);
        }

        dl.PopClipRect();
    }

    private void DrawSingleTrackKeyframes(ImDrawListPtr dl, Vector2 trackPos,
                                           TimelineProperty row, float scrollX)
    {
        var  wPos  = ImGui.GetWindowPos();
        var  wSize = ImGui.GetWindowSize();
        dl.PushClipRect(new Vector2(wPos.X, trackPos.Y),
                        new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight), true);

        foreach (var kf in GetKeyframesForProperty(row.Object, row.PropertyPath))
        {
            float cx = trackPos.X + kf.Frame * _pixelsPerFrame - scrollX;
            float cy = trackPos.Y + RowHeight / 2f;
            DrawDiamond(dl, cx, cy, KeyframeDiamondSize, _selectedKeyframes.Contains(kf));
        }

        dl.PopClipRect();
    }

    private void DrawGroupTrackKeyframes(ImDrawListPtr dl, Vector2 trackPos,
                                          TimelineProperty row, float scrollX)
    {
        if (row.GroupPaths == null) return;

        var frames = new HashSet<int>();
        foreach (var path in row.GroupPaths)
            foreach (var kf in GetKeyframesForProperty(row.Object, path))
                frames.Add(kf.Frame);

        var  wPos  = ImGui.GetWindowPos();
        var  wSize = ImGui.GetWindowSize();
        dl.PushClipRect(new Vector2(wPos.X, trackPos.Y),
                        new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight), true);

        foreach (int f in frames)
        {
            bool anySelected = row.GroupPaths.Any(p =>
            {
                var kf2 = GetKeyframesForProperty(row.Object, p).Find(k => k.Frame == f);
                return kf2 != null && _selectedKeyframes.Contains(kf2);
            });
            float cx = trackPos.X + f * _pixelsPerFrame - scrollX;
            float cy = trackPos.Y + RowHeight / 2f;
            DrawDiamond(dl, cx, cy, KeyframeDiamondSize - 1f, anySelected);
        }

        dl.PopClipRect();
    }

    private static void DrawDiamond(ImDrawListPtr dl, float cx, float cy, float s, bool selected)
    {
        uint fill    = selected ? 0xFF4499FF : 0xFF33CCFF;
        uint outline = selected ? 0xFF2266CC : 0xFF1188AA;
        dl.AddQuadFilled(new Vector2(cx, cy - s), new Vector2(cx + s, cy),
                         new Vector2(cx, cy + s), new Vector2(cx - s, cy), fill);
        dl.AddQuad(new Vector2(cx, cy - s), new Vector2(cx + s, cy),
                   new Vector2(cx, cy + s), new Vector2(cx - s, cy), outline, 1.5f);
    }

    // ── Track mouse interactions ──────────────────────────────────────────────

    private void HandleTrackMouse(TimelineProperty row, Vector2 trackPos, float contentW,
                                   float scrollX, Vector2 wPos, Vector2 wSize)
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)) return;

        var   mouse   = ImGui.GetMousePos();
        bool  inTrack = mouse.Y >= trackPos.Y && mouse.Y < trackPos.Y + RowHeight;
        if (!inTrack) return;

        float localX     = mouse.X - trackPos.X + scrollX;
        int   frame      = Math.Max(0, (int)MathF.Round(localX / _pixelsPerFrame));
        bool  altHeld    = ImGui.GetIO().KeyAlt;
        bool  shiftHeld  = ImGui.GetIO().KeyShift;
        bool  ctrlHeld   = ImGui.GetIO().KeyCtrl;

        // Skip hit-test on group headers (no individual keyframes to interact with)
        TimelineKeyframe? hoveredKf = null;
        if (!row.IsGroupHeader && row.PropertyPath != "__header__")
        {
            hoveredKf = GetKeyframesForProperty(row.Object, row.PropertyPath)
                .Find(k => Math.Abs(k.Frame - frame) <= 2);
            if (hoveredKf != null && !_isDraggingKeyframe)
                ImGui.SetTooltip($"Frame {hoveredKf.Frame} · {hoveredKf.InterpolationType}");
        }

        // ── Mouse pressed ────────────────────────────────────────────────────
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (row.IsGroupHeader || row.PropertyPath == "__header__")
            {
                // Empty area → start drag-select
                StartDragSelect(mouse, shiftHeld);
            }
            else if (hoveredKf != null && altHeld)
            {
                RemoveKeyframeForProperty(row.Object, row.PropertyPath, hoveredKf.Frame);
            }
            else if (hoveredKf != null)
            {
                // Start keyframe drag / selection
                if (!shiftHeld && !_selectedKeyframes.Contains(hoveredKf))
                {
                    _selectedKeyframes.Clear();
                    _keyframeOwners.Clear();
                }
                if (!_selectedKeyframes.Contains(hoveredKf))
                {
                    _selectedKeyframes.Add(hoveredKf);
                    _keyframeOwners[hoveredKf] = (row.Object, row.PropertyPath);
                }
                _isDraggingKeyframe = true;
                _draggedKeyframe    = hoveredKf;
                _dragStartFrame     = hoveredKf.Frame;
                _selectedStartFrames.Clear();
                foreach (var kf in _selectedKeyframes)
                    _selectedStartFrames[kf] = kf.Frame;
            }
            else
            {
                // Click on empty area → add keyframe + start drag-select
                if (!shiftHeld)
                {
                    _selectedKeyframes.Clear();
                    _keyframeOwners.Clear();
                }
                AddKeyframeForProperty(row.Object, row.PropertyPath, frame);
                StartDragSelect(mouse, shiftHeld);
            }
        }

        // ── Dragging keyframe ────────────────────────────────────────────────
        if (_isDraggingKeyframe && _draggedKeyframe != null && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            int newFrame = Math.Max(0, (int)MathF.Round(localX / _pixelsPerFrame));
            int offset   = newFrame - _dragStartFrame;
            if (offset != 0)
            {
                foreach (var kf in _selectedKeyframes)
                    if (_selectedStartFrames.TryGetValue(kf, out int sf))
                        kf.Frame = Math.Max(0, sf + offset);
            }
        }

        // ── Mouse released ───────────────────────────────────────────────────
        if (_isDraggingKeyframe && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            foreach (var kf in _selectedKeyframes)
            {
                if (_keyframeOwners.TryGetValue(kf, out var owner) &&
                    _selectedStartFrames.TryGetValue(kf, out int sf) && kf.Frame != sf)
                {
                    MoveKeyframe(owner.obj, owner.path, sf, kf.Frame);
                }
            }
            _isDraggingKeyframe = false;
            _draggedKeyframe    = null;
            _selectedStartFrames.Clear();
        }

        // ── Right-click context menu ─────────────────────────────────────────
        if (!row.IsGroupHeader && row.PropertyPath != "__header__" &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Right) && hoveredKf != null)
        {
            _ctxKeyframe = hoveredKf;
            _ctxObject   = row.Object;
            _ctxPropPath = row.PropertyPath;
            if (!_selectedKeyframes.Contains(hoveredKf))
            {
                _selectedKeyframes.Clear();
                _keyframeOwners.Clear();
                _selectedKeyframes.Add(hoveredKf);
                _keyframeOwners[hoveredKf] = (row.Object, row.PropertyPath);
            }
            _openContextMenu = true;
        }

        // ── Drag-select ──────────────────────────────────────────────────────
        if (!_isDraggingKeyframe)
            UpdateDragSelect(mouse, shiftHeld, scrollX, wPos);
    }

    private void StartDragSelect(Vector2 mouse, bool shiftHeld)
    {
        _isDragSelecting = true;
        _wasDragging     = false;
        _dragSelectStart = mouse;
        _dragSelectEnd   = mouse;
        if (!shiftHeld)
        {
            _selectedKeyframes.Clear();
            _keyframeOwners.Clear();
        }
    }

    private void UpdateDragSelect(Vector2 mouse, bool shiftHeld, float scrollX, Vector2 wPos)
    {
        if (!_isDragSelecting)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                StartDragSelect(mouse, shiftHeld);
            return;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float dist = Vector2.Distance(mouse, _dragSelectStart);
            if (dist >= DragThreshold) _wasDragging = true;
            _dragSelectEnd = mouse;

            if (_wasDragging)
                ApplyDragSelection(scrollX, wPos, shiftHeld);
        }
        else
        {
            _isDragSelecting = false;
            _wasDragging     = false;
        }
    }

    private void ApplyDragSelection(float scrollX, Vector2 wPos, bool shiftHeld)
    {
        float minSX = MathF.Min(_dragSelectStart.X, _dragSelectEnd.X);
        float maxSX = MathF.Max(_dragSelectStart.X, _dragSelectEnd.X);
        float minSY = MathF.Min(_dragSelectStart.Y, _dragSelectEnd.Y);
        float maxSY = MathF.Max(_dragSelectStart.Y, _dragSelectEnd.Y);

        if (!shiftHeld)
        {
            _selectedKeyframes.Clear();
            _keyframeOwners.Clear();
        }

        float curY = wPos.Y - _vScrollOffset;
        foreach (var row in _displayRows)
        {
            if (row.PropertyPath == "__header__" || row.IsGroupHeader)
            { curY += RowHeight; continue; }

            if (row.Indent > 0 && !IsGroupChildVisible(row))
                continue;

            float rowMinY = curY;
            float rowMaxY = curY + RowHeight;
            curY += RowHeight;

            if (rowMaxY < minSY || rowMinY > maxSY) continue;

            foreach (var kf in GetKeyframesForProperty(row.Object, row.PropertyPath))
            {
                float kfSX = wPos.X + kf.Frame * _pixelsPerFrame - scrollX;
                if (kfSX < minSX || kfSX > maxSX) continue;
                if (!_selectedKeyframes.Contains(kf))
                {
                    _selectedKeyframes.Add(kf);
                    _keyframeOwners[kf] = (row.Object, row.PropertyPath);
                }
            }
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void RenderContextMenu()
    {
        if (!ImGui.BeginPopup("##kf_ctx")) return;

        ImGui.TextDisabled("Keyframe");
        ImGui.Separator();

        if (ImGui.BeginMenu("Interpolation"))
        {
            foreach (var (lbl, val) in new[]
            {
                ("Linear",               "linear"),
                ("Ease In Quadratic",    "ease-in-quadratic"),
                ("Ease Out Quadratic",   "ease-out-quadratic"),
                ("Ease In-Out Quadratic","ease-in-out-quadratic"),
                ("Instant",              "instant"),
            })
            {
                bool active = _ctxKeyframe?.InterpolationType == val;
                if (ImGui.MenuItem(lbl, "", active))
                {
                    foreach (var kf in _selectedKeyframes) kf.InterpolationType = val;
                    if (_ctxObject != null && _ctxPropPath != null)
                        SaveKeyframesToObject(_ctxObject, _ctxPropPath);
                }
            }
            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Delete Keyframe(s)"))
            DeleteSelectedKeyframes();

        ImGui.EndPopup();
    }

    private void DeleteSelectedKeyframes()
    {
        var toDelete = _selectedKeyframes
            .Where(kf => _keyframeOwners.TryGetValue(kf, out _))
            .ToList();

        foreach (var kf in toDelete)
        {
            if (!_keyframeOwners.TryGetValue(kf, out var owner)) continue;
            string key = $"{owner.obj.ObjectId}.{owner.path}";
            if (_propertyKeyframes.TryGetValue(key, out var list))
                list.Remove(kf);
            SaveKeyframesToObject(owner.obj, owner.path);
        }

        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
    }

    // ── Keyframe operations ───────────────────────────────────────────────────

    private List<TimelineKeyframe> GetKeyframesForProperty(SceneObject obj, string propertyPath)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (!_propertyKeyframes.TryGetValue(key, out var list))
            _propertyKeyframes[key] = list = new List<TimelineKeyframe>();
        return list;
    }

    /// <summary>
    /// Call this whenever a property changes in another panel (e.g. the inspector).
    /// If auto-keyframing is enabled, inserts/updates a keyframe at the current frame.
    /// </summary>
    public void RecordAutoKeyframe(SceneObject obj, string propertyPath)
    {
        if (!_autoKeyframe) return;
        AddKeyframeForProperty(obj, propertyPath, _currentFrame);
    }

    public void AddKeyframeForProperty(SceneObject obj, string propertyPath, int frame)
    {
        string key   = $"{obj.ObjectId}.{propertyPath}";
        object value = GetPropertyValue(obj, propertyPath);
        if (!_propertyKeyframes.ContainsKey(key))
            _propertyKeyframes[key] = new List<TimelineKeyframe>();

        var list     = _propertyKeyframes[key];
        var existing = list.Find(k => k.Frame == frame);
        if (existing != null) { existing.Value = value; }
        else { list.Add(new TimelineKeyframe { Frame = frame, Value = value }); list.Sort((a, b) => a.Frame.CompareTo(b.Frame)); }

        SaveKeyframesToObject(obj, propertyPath);
        RecalculateTimelineLength();
    }

    public void RemoveKeyframeForProperty(SceneObject obj, string propertyPath, int frame)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (_propertyKeyframes.TryGetValue(key, out var list))
        {
            list.RemoveAll(k => k.Frame == frame);
            SaveKeyframesToObject(obj, propertyPath);
        }
    }

    private void MoveKeyframe(SceneObject obj, string propertyPath, int fromFrame, int toFrame)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (!_propertyKeyframes.TryGetValue(key, out var list)) return;
        var dest = list.Find(k => k.Frame == toFrame);
        if (dest != null) list.Remove(dest);
        var src = list.Find(k => k.Frame == fromFrame);
        if (src != null) src.Frame = toFrame;
        list.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        SaveKeyframesToObject(obj, propertyPath);
        RecalculateTimelineLength();
    }

    // ── Load / save ───────────────────────────────────────────────────────────

    private void LoadKeyframesFromObject(SceneObject obj, string propertyPath)
    {
        if (!obj.Keyframes.TryGetValue(propertyPath, out var src) || src.Count == 0) return;
        string key = $"{obj.ObjectId}.{propertyPath}";
        _propertyKeyframes[key] = src
            .Select(ok => new TimelineKeyframe { Frame = ok.Frame, Value = ok.Value, InterpolationType = ok.InterpolationType })
            .OrderBy(kf => kf.Frame).ToList();
        RecalculateTimelineLength();
    }

    private void SaveKeyframesToObject(SceneObject obj, string propertyPath)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (!_propertyKeyframes.TryGetValue(key, out var list) || list.Count == 0)
        { obj.Keyframes.Remove(propertyPath); return; }

        obj.Keyframes[propertyPath] = list
            .Select(kf => new ObjectKeyframe { Frame = kf.Frame, Value = kf.Value, InterpolationType = kf.InterpolationType })
            .ToList();
    }

    public void LoadKeyframesForAllObjects(IEnumerable<SceneObject> objects)
    {
        string[] standardPaths =
        {
            "visible", "material.alpha",
            "position.x", "position.y", "position.z",
            "rotation.x", "rotation.y", "rotation.z",
            "scale.x",    "scale.y",    "scale.z",
        };
        string[] lightPaths =
        {
            "light.energy", "light.range", "light.indirect_energy", "light.specular",
            "light.color.r", "light.color.g", "light.color.b",
        };

        foreach (var obj in objects)
        {
            if (obj == null || obj.Keyframes.Count == 0) continue;
            var paths = obj is LightSceneObject ? standardPaths.Concat(lightPaths) : standardPaths;
            foreach (var path in paths)
                if (obj.Keyframes.ContainsKey(path) && obj.Keyframes[path].Count > 0)
                    LoadKeyframesFromObject(obj, path);
        }

        RecalculateTimelineLength();
    }

    private void RecalculateTimelineLength()
    {
        int max = 300;
        foreach (var kvp in _propertyKeyframes)
            foreach (var kf in kvp.Value)
                if (kf.Frame > max) max = kf.Frame;
        if (max > _maxFrames) _maxFrames = max + 30;
    }

    // ── Apply keyframes ───────────────────────────────────────────────────────

    private void ApplyKeyframesAtCurrentFrame(bool holdFirstKeyframeBeforeStart)
    {
        foreach (var kvp in _propertyKeyframes)
        {
            var keyframes = kvp.Value;
            if (keyframes.Count == 0) continue;

            int dotIdx = kvp.Key.IndexOf('.');
            if (dotIdx < 0) continue;
            string objectId     = kvp.Key[..dotIdx];
            string propertyPath = kvp.Key[(dotIdx + 1)..];

            var target = FindObjectById(objectId);
            if (target == null) continue;

            float? value = InterpolateKeyframes(keyframes, propertyPath, _currentFrame, holdFirstKeyframeBeforeStart);
            if (value.HasValue)
                SetPropertyValue(target, propertyPath, value.Value);
            // null means "before first keyframe" — leave the object at its default state.
        }
    }

    /// <summary>
    /// Returns the interpolated value for the property at <paramref name="frame"/>,
    /// or <c>null</c> if the frame is before the first keyframe (meaning the object
    /// should keep its default/current value rather than being driven by animation).
    /// </summary>
    private float? InterpolateKeyframes(List<TimelineKeyframe> keyframes, string path, int frame, bool holdFirstKeyframeBeforeStart)
    {
        TimelineKeyframe? prev = null, next = null;
        foreach (var kf in keyframes)
        {
            if (kf.Frame <= frame && (prev == null || kf.Frame > prev.Frame)) prev = kf;
            if (kf.Frame >= frame && (next == null || kf.Frame < next.Frame)) next = kf;
        }

        // No keyframe at or before current frame → object is before its first keyframe.
        // Return null so the caller skips applying anything and the object keeps its
        // current/default property value.
        if (prev == null)
        {
            if (holdFirstKeyframeBeforeStart && next != null)
                return Convert.ToSingle(next.Value);
            return null;
        }

        // At or after the last keyframe, or exactly on a keyframe.
        if (next == null || prev.Frame == frame)
            return Convert.ToSingle(prev.Value);

        // Between two keyframes — interpolate.
        // "visible" and "instant" use the previous keyframe's value with no blending.
        if (path == "visible" || prev.InterpolationType == "instant")
            return Convert.ToSingle(prev.Value);

        float t  = (frame - prev.Frame) / (float)(next.Frame - prev.Frame);
        float pv = Convert.ToSingle(prev.Value);
        float nv = Convert.ToSingle(next.Value);
        return pv + (nv - pv) * ApplyInterpolation(t, prev.InterpolationType);
    }

    private static float ApplyInterpolation(float t, string type) => type switch
    {
        "ease-in-quadratic"       => t * t,
        "ease-out-quadratic"      => 1f - (1f - t) * (1f - t),
        "ease-in-out-quadratic"   => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
        "instant"                 => 0f,
        _                         => t,
    };

    // ── Get / set property values ─────────────────────────────────────────────

    private object GetPropertyValue(SceneObject obj, string path)
    {
        if (path == "visible") return obj.ObjectVisible ? 1f : 0f;

        var parts = path.Split('.');
        if (parts.Length == 2)
        {
            string prop = parts[0], comp = parts[1];
            switch (prop)
            {
                case "position":
                {
                    vec3 p = obj is MiBoneSceneObject mb ? mb.OffsetPosition
                           : obj is BoneSceneObject   bo ? bo.TargetPosition
                           : obj.LocalPosition;
                    return comp switch { "x" => (object)p.x, "y" => p.y, "z" => p.z, _ => 0f };
                }
                case "rotation":
                {
                    // Internal: radians → store as degrees
                    vec3 r = obj is MiBoneSceneObject mb ? mb.OffsetRotation
                           : obj is BoneSceneObject   bo ? bo.TargetRotation
                           : obj.LocalRotation;
                    float rad = comp switch { "x" => r.x, "y" => r.y, "z" => r.z, _ => 0f };
                    return rad * (180f / MathF.PI);
                }
                case "scale":
                {
                    vec3 s = obj is MiBoneSceneObject mb ? mb.OffsetScale : obj.LocalScale;
                    return comp switch { "x" => (object)s.x, "y" => s.y, "z" => s.z, _ => 1f };
                }
                case "material":
                    if (comp == "alpha") return obj.MaterialSettings?.AlbedoColor.w ?? 1f;
                    break;
                case "light":
                    if (obj is LightSceneObject lo)
                        return comp switch
                        {
                            "energy"          => (object)lo.LightEnergy,
                            "range"           => lo.LightRange,
                            "indirect_energy" => lo.LightIndirectEnergy,
                            "specular"        => lo.LightSpecular,
                            _                 => 0f,
                        };
                    break;
            }
        }
        else if (parts.Length == 3 && parts[0] == "light" && parts[1] == "color" && obj is LightSceneObject lco)
        {
            return parts[2] switch { "r" => (object)lco.LightColor.x, "g" => lco.LightColor.y, "b" => lco.LightColor.z, _ => 0f };
        }

        return 0f;
    }

    private void SetPropertyValue(SceneObject obj, string path, float value)
    {
        if (path == "visible") { obj.ObjectVisible = value >= 0.5f; return; }

        var parts = path.Split('.');
        if (parts.Length == 2)
        {
            string prop = parts[0], comp = parts[1];
            switch (prop)
            {
                case "position":
                    if (obj is MiBoneSceneObject mbP)
                    {
                        var p = mbP.OffsetPosition;
                        if (comp == "x") p.x = value; else if (comp == "y") p.y = value; else if (comp == "z") p.z = value;
                        mbP.OffsetPosition = p;
                    }
                    else if (obj is BoneSceneObject boP)
                    {
                        var p = boP.TargetPosition;
                        if (comp == "x") p.x = value; else if (comp == "y") p.y = value; else if (comp == "z") p.z = value;
                        boP.TargetPosition = p;
                    }
                    else
                    {
                        var p = obj.LocalPosition;
                        if (comp == "x") p.x = value; else if (comp == "y") p.y = value; else if (comp == "z") p.z = value;
                        obj.SetLocalPosition(p);
                    }
                    break;

                case "rotation":
                {
                    // Keyframes store degrees → convert to radians
                    float rad = value * (MathF.PI / 180f);
                    if (obj is MiBoneSceneObject mbR)
                    {
                        var r = mbR.OffsetRotation;
                        if (comp == "x") r.x = rad; else if (comp == "y") r.y = rad; else if (comp == "z") r.z = rad;
                        mbR.OffsetRotation = r;
                    }
                    else if (obj is BoneSceneObject boR)
                    {
                        var r = boR.TargetRotation;
                        if (comp == "x") r.x = rad; else if (comp == "y") r.y = rad; else if (comp == "z") r.z = rad;
                        boR.TargetRotation = r;
                    }
                    else
                    {
                        var r = obj.LocalRotation;
                        if (comp == "x") r.x = rad; else if (comp == "y") r.y = rad; else if (comp == "z") r.z = rad;
                        obj.SetLocalRotation(r);
                    }
                    break;
                }

                case "scale":
                    if (obj is MiBoneSceneObject mbS)
                    {
                        var s = mbS.OffsetScale;
                        if (comp == "x") s.x = value; else if (comp == "y") s.y = value; else if (comp == "z") s.z = value;
                        mbS.OffsetScale = s;
                    }
                    else
                    {
                        var s = obj.LocalScale;
                        if (comp == "x") s.x = value; else if (comp == "y") s.y = value; else if (comp == "z") s.z = value;
                        obj.SetLocalScale(s);
                    }
                    break;

                case "material":
                    if (comp == "alpha")
                    {
                        if (obj.MaterialSettings == null) obj.MaterialSettings = new MaterialSettings();
                        var c = obj.MaterialSettings.AlbedoColor;
                        c.w = Math.Clamp(value, 0f, 1f);
                        obj.MaterialSettings.AlbedoColor = c;
                        obj.ApplyMaterialSettingsToMeshes();
                    }
                    break;

                case "light":
                    if (obj is LightSceneObject lo)
                    {
                        switch (comp)
                        {
                            case "energy":          lo.LightEnergy         = value; break;
                            case "range":           lo.LightRange          = value; break;
                            case "indirect_energy": lo.LightIndirectEnergy = value; break;
                            case "specular":        lo.LightSpecular       = value; break;
                        }
                    }
                    break;
            }
        }
        else if (parts.Length == 3 && parts[0] == "light" && parts[1] == "color" && obj is LightSceneObject lco)
        {
            var c = lco.LightColor;
            switch (parts[2]) { case "r": c.x = value; break; case "g": c.y = value; break; case "b": c.z = value; break; }
            lco.LightColor = c;
        }
    }

    // ── Selection changed → rebuild display rows ──────────────────────────────

    private void OnSelectionChanged()
    {
        _displayRows.Clear();
        _propertyKeyframes.Clear();

        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected != null)
            foreach (var obj in selected)
                AddObjectRows(obj);

        if (Viewport != null)
            LoadKeyframesForAllObjects(CollectAllObjects(Viewport.SceneObjects));
    }

    private void AddObjectRows(SceneObject obj)
    {
        _displayRows.Add(new TimelineProperty { Object = obj, Label = $"── {obj.Name} ──", PropertyPath = "__header__" });
        _displayRows.Add(MakeSingle(obj, "Visible",         "visible"));
        _displayRows.Add(MakeSingle(obj, "Alpha",           "material.alpha"));
        _displayRows.Add(MakeGroup(obj,  "Position",        new[] { "position.x", "position.y", "position.z" }));
        _displayRows.Add(MakeSingle(obj, "X",               "position.x", 1));
        _displayRows.Add(MakeSingle(obj, "Y",               "position.y", 1));
        _displayRows.Add(MakeSingle(obj, "Z",               "position.z", 1));
        _displayRows.Add(MakeGroup(obj,  "Rotation",        new[] { "rotation.x", "rotation.y", "rotation.z" }));
        _displayRows.Add(MakeSingle(obj, "X",               "rotation.x", 1));
        _displayRows.Add(MakeSingle(obj, "Y",               "rotation.y", 1));
        _displayRows.Add(MakeSingle(obj, "Z",               "rotation.z", 1));
        _displayRows.Add(MakeGroup(obj,  "Scale",           new[] { "scale.x", "scale.y", "scale.z" }));
        _displayRows.Add(MakeSingle(obj, "X",               "scale.x", 1));
        _displayRows.Add(MakeSingle(obj, "Y",               "scale.y", 1));
        _displayRows.Add(MakeSingle(obj, "Z",               "scale.z", 1));

        if (obj is LightSceneObject)
        {
            _displayRows.Add(MakeSingle(obj, "Light Energy",    "light.energy"));
            _displayRows.Add(MakeSingle(obj, "Light Range",     "light.range"));
            _displayRows.Add(MakeSingle(obj, "Indirect Energy", "light.indirect_energy"));
            _displayRows.Add(MakeSingle(obj, "Specular",        "light.specular"));
            _displayRows.Add(MakeGroup(obj,  "Light Color",     new[] { "light.color.r", "light.color.g", "light.color.b" }));
            _displayRows.Add(MakeSingle(obj, "R",               "light.color.r", 1));
            _displayRows.Add(MakeSingle(obj, "G",               "light.color.g", 1));
            _displayRows.Add(MakeSingle(obj, "B",               "light.color.b", 1));
        }

        foreach (var row in _displayRows.Where(r => r.Object == obj && !r.IsGroupHeader && r.PropertyPath != "__header__"))
            LoadKeyframesFromObject(obj, row.PropertyPath);
    }

    private static TimelineProperty MakeSingle(SceneObject obj, string label, string path, int indent = 0) =>
        new() { Object = obj, Label = label, PropertyPath = path, Indent = indent };

    private static TimelineProperty MakeGroup(SceneObject obj, string name, string[] paths) =>
        new() { Object = obj, Label = name, PropertyPath = name.ToLower(), IsGroupHeader = true, GroupPaths = paths };

    // ── Object lookup ─────────────────────────────────────────────────────────

    private SceneObject? FindObjectById(string id)
    {
        foreach (var row in _displayRows)
            if (row.Object?.ObjectId == id) return row.Object;
        if (Viewport != null)
            foreach (var obj in CollectAllObjects(Viewport.SceneObjects))
                if (obj.ObjectId == id) return obj;
        return null;
    }

    private static IEnumerable<SceneObject> CollectAllObjects(IEnumerable<SceneObject> roots)
    {
        foreach (var obj in roots)
        {
            yield return obj;
            foreach (var child in CollectAllObjects(obj.Children))
                yield return child;
        }
    }

    // ── Called after project load ─────────────────────────────────────────────

    public void OnProjectLoaded()
    {
        ImportProjectState(null);
    }
}