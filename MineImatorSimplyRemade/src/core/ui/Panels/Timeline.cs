using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.audio;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl.meshes;
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

/// <summary>
/// Runtime representation of an audio track on the timeline.  Wraps a
/// <see cref="ProjectAudioTrack"/> so we can hold playback state (OpenAL
/// source, decoded clip) without polluting the serialised manifest.
/// </summary>
public class TimelineAudioTrack
{
    public ProjectAudioTrack ManifestEntry { get; set; } = new();
    public AudioClip?        Clip          { get; set; }
    public AudioSourceHandle Source        { get; set; }
    public bool              IsLoaded      => Clip != null && Source.IsValid;
    public bool              WasPlaying    { get; set; }
}

// ── Timeline panel ────────────────────────────────────────────────────────────

/// <summary>
/// ImGui timeline panel.  Ported from the Godot TimelinePanel.cs in
/// simply-remade-nuxi.
///
/// Features: frame-based playback, collapsible property groups, keyframe
/// add/remove/move, interpolation types, multi-select, apply-to-all-objects.
/// </summary>
public class TimelineMarker
{
    public int Frame { get; set; }
    public string Label { get; set; } = "Marker";
    public Vector4 Color { get; set; } = new(0.9f, 0.2f, 0.2f, 1f);
}

public class Timeline : UiPanel
{
    public static string SanitizeIntegerText(string text)
    {
        return NumericExpressionParser.SanitizeText(text, allowDecimal: false, allowExponent: false);
    }

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
    private const float MinPixelsPerFrame = 1f;
    private const float MaxPixelsPerFrame = 40f;
    private const float ZoomStep = 1.25f;
    private bool   _isPlaying        = false;
    private bool   _autoKeyframe     = false;
    private bool   _loopPlayback     = false;
    private bool   _ghostModeEnabled = false;
    private double _frameAccumulator = 0.0;
    private long   _lastTimestamp    = Stopwatch.GetTimestamp();

    // ── Scroll sync ───────────────────────────────────────────────────────────

    private float _hScrollOffset = 0f;
    private float _vScrollOffset = 0f;
    private float _trackViewportWidth = 0f;
    private float? _pendingHScrollOffset;

    // ── Keyframe data ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<TimelineKeyframe>> _propertyKeyframes = new();
    private List<TimelineProperty>                              _displayRows        = new();
    private readonly Dictionary<string, bool>                   _groupExpanded      = new();
    private readonly Dictionary<TimelineProperty, bool>         _trackRowVisibility = new();
    private readonly HashSet<string>                            _ghostTracks        = new(StringComparer.Ordinal);

    private static readonly (string Label, string Path)[] MaterialScalarChannels =
    {
        ("Metallic", "material.metallic"),
        ("Roughness", "material.roughness"),
        ("PBR shading", "material.pbr_enabled"),
        ("Normal strength", "material.normal_strength"),
        ("Material AO", "material.occlusion_strength"),
        ("Height / parallax", "material.height_scale"),
        ("Emissive", "material.emissive"),
        ("Subsurface", "material.subsurface"),
        ("Mix Amount", "material.mix_percent"),
    };

    private static readonly (string Label, string Path)[] MaterialVectorChannels =
    {
        ("Blend Color", "material.blend"),
        ("Mix Color", "material.mix"),
        ("Glow Color", "material.glow"),
        ("Subsurface Color", "material.subsurface_color"),
        ("Subsurface Radius", "material.subsurface_radius"),
    };

    private static readonly HashSet<string> MaterialPropertyPaths = new(
        MaterialScalarChannels.Select(channel => channel.Path)
            .Concat(MaterialVectorChannels.SelectMany(channel =>
                new[] { $"{channel.Path}.r", $"{channel.Path}.g", $"{channel.Path}.b" }))
            .Append("material.alpha"), StringComparer.Ordinal);

    private static readonly (string Label, string Path)[] TextChannels =
    {
        ("Font", "text.font"),
        ("Horizontal Alignment", "text.horizontal_alignment"),
        ("Vertical Alignment", "text.vertical_alignment"),
        ("Antialiasing", "text.antialiasing"),
        ("Font Size", "text.font_size"),
        ("Outline", "text.outline_enabled"),
        ("Outline Thickness", "text.outline_thickness"),
        ("R", "text.outline.r"), ("G", "text.outline.g"),
        ("B", "text.outline.b"), ("A", "text.outline.a"),
    };

    // ── Selection ─────────────────────────────────────────────────────────────

    private readonly List<TimelineKeyframe>                                        _selectedKeyframes = new();
    private readonly Dictionary<TimelineKeyframe, (SceneObject obj, string path)> _keyframeOwners    = new();

    private readonly List<TimelineMarker> _markers = new();
    private TimelineMarker? _activeMarker;
    private TimelineMarker? _draggedMarker;
    private bool _isDraggingMarker;
    private int _contextFrame;
    private int? _selectedRegionStart;
    private int? _selectedRegionEnd;
    private int? _playbackRegionStart;
    private int? _playbackRegionEnd;
    private readonly List<(SceneObject obj, string path, int offset, object value, string interpolation)> _keyframeClipboard = new();
    private bool _openMarkerEditor;
    private string _markerLabel = "Marker";
    private Vector4 _markerColor = new(0.9f, 0.2f, 0.2f, 1f);
    private float _timelineAreaTop;

    // ── Audio tracks ──────────────────────────────────────────────────────────

    public  IReadOnlyList<TimelineAudioTrack> AudioTracks => _audioTracks;
    private readonly List<TimelineAudioTrack>  _audioTracks = new();

    private TimelineAudioTrack? _selectedAudioTrack;
    private bool                _isDraggingAudioClip;
    private int                 _audioDragStartMouseX;
    private int                 _audioDragStartFrame;

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
    private bool  _isDraggingPlaybackRegion;
    private bool  _playbackRegionDragged;
    private int   _playbackRegionAnchorFrame;

    // ── Drag-select ───────────────────────────────────────────────────────────

    private bool    _isDragSelecting = false;
    private bool    _wasDragging     = false;
    private Vector2 _dragSelectStart;
    private Vector2 _dragSelectEnd;
    private static float DragThreshold => 3f * UiTheme.Scale;

    // ── Context menu ──────────────────────────────────────────────────────────

    private bool              _openContextMenu = false;
    private TimelineKeyframe? _ctxKeyframe;
    private SceneObject?      _ctxObject;
    private string?           _ctxPropPath;
    private TimelineProperty? _ctxTrackRow;
    private bool              _openAddKeyframeMenu = false;
    private SceneObject?      _ctxAddKeyframeObj;
    private bool              _openSpeedScaleMenu;
    private bool              _speedScaleSlowDown = true;
    private float             _speedScaleFactor   = 2f;

    // ── Layout ────────────────────────────────────────────────────────────────

    private static float MarkerRowHeight => 26f * UiTheme.Scale;
    private static float LeftColumnWidth => 200f * UiTheme.Scale;
    private static float RowHeight => 22f * UiTheme.Scale;
    private static float RulerHeight => 24f * UiTheme.Scale;
    private static float KeyframeDiamondSize => 5f * UiTheme.Scale;

    // ── Public API ────────────────────────────────────────────────────────────

    public int   CurrentFrame => _currentFrame;
    public int   MaxFrames    => _maxFrames;
    public float Framerate    => _frameRate;
    public bool  IsPlaying    => _isPlaying;
    public bool  IsWindowHovered { get; private set; }
    public Vector2 WindowPos { get; private set; }
    public Vector2 WindowSize { get; private set; }
    public bool HasEditFocus { get; private set; }
    public bool HasSelectedKeyframes => _selectedKeyframes.Count > 0;
    public bool HasClipboardKeyframes => _keyframeClipboard.Count > 0;

    public void SelectAllKeyframes() => SelectKeyframes(_ => true);
    public void PasteAtCurrentFrame()
    {
        if (HasClipboardKeyframes)
            PasteKeyframes(_currentFrame);
    }

    public void CutSelectedKeyframes()
    {
        if (!HasSelectedKeyframes) return;
        CopySelectedKeyframes();
        DeleteSelectedKeyframes();
    }

    public (int StartFrame, int EndFrame) GetAnimationExportRange()
    {
        if (_playbackRegionStart is int start && _playbackRegionEnd is int end && start >= 0 && end > start)
            return (start, end);

        return (0, _maxFrames);
    }

    public void SetFrameRate(float frameRate)
    {
        float clamped = Math.Clamp(frameRate, 1f, 120f);
        if (MathF.Abs(clamped - _frameRate) < 0.0001f)
            return;

        _frameRate = clamped;
    }

    public void SetCurrentFrame(int frame)
    {
        _currentFrame = Math.Max(0, frame);
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
        SyncAudioWithPlayback();
    }

    public void TogglePlayPause()
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            MoveIntoPlaybackRegionIfNeeded();
            _lastTimestamp = Stopwatch.GetTimestamp();
            // Force every track to restart on the next sync so the audio
            // re-aligns with the current playhead after a pause.
            foreach (var t in _audioTracks) t.WasPlaying = false;
        }
        else
        {
            PauseAllAudio();
        }
    }

    public void SetCurrentFrameForRender(int frame)
    {
        _currentFrame = Math.Max(0, frame);
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: true);
        // Re-sync audio offsets after a render-driven seek.
        SyncAudioWithPlayback();
    }

    public ProjectTimelineState ExportProjectState()
    {
        return new ProjectTimelineState
        {
            CurrentFrame = _currentFrame,
            MaxFrames = _maxFrames,
            FrameRate = _frameRate,
            AutoKeyframe = _autoKeyframe,
            LoopPlayback = _loopPlayback,
            GhostModeEnabled = _ghostModeEnabled,
            GhostTracks = _ghostTracks.ToList(),
            PlaybackRegionStart = _playbackRegionStart,
            PlaybackRegionEnd = _playbackRegionEnd,
            Markers = _markers.Select(m => new ProjectTimelineMarker
                { Frame = m.Frame, Label = m.Label, Red = m.Color.X, Green = m.Color.Y,
                    Blue = m.Color.Z, Alpha = m.Color.W }).ToList()
        };
    }

    public void ImportProjectState(ProjectTimelineState? state)
    {
        // The scene snapshot already contains the saved pose, including unkeyed
        // edits. Restore tracks without evaluating them until the user seeks or plays.
        _isPlaying = false;
        PauseAllAudio();
        _groupExpanded.Clear();
        RefreshFromScene();

        if (state == null)
        {
            _markers.Clear();
            _loopPlayback = false;
            _ghostModeEnabled = false;
            _ghostTracks.Clear();
            _selectedRegionStart = null;
            _selectedRegionEnd = null;
            _playbackRegionStart = null;
            _playbackRegionEnd = null;
            _currentFrame = 0;
            _frameAccumulator = 0.0;
            return;
        }

        _maxFrames = Math.Max(10, state.MaxFrames);
        _frameRate = Math.Clamp(state.FrameRate, 1f, 120f);
        _autoKeyframe = state.AutoKeyframe;
        _loopPlayback = state.LoopPlayback;
        _ghostModeEnabled = state.GhostModeEnabled;
        _ghostTracks.Clear();
        foreach (var track in state.GhostTracks ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(track))
                _ghostTracks.Add(track);
        }
        PruneHiddenSelection();
        _playbackRegionStart = state.PlaybackRegionStart;
        _playbackRegionEnd = state.PlaybackRegionEnd;
        _markers.Clear();
        foreach (var marker in state.Markers ?? new List<ProjectTimelineMarker>())
            _markers.Add(new TimelineMarker
                { Frame = Math.Max(0, marker.Frame), Label = marker.Label,
                    Color = new Vector4(marker.Red, marker.Green, marker.Blue, marker.Alpha) });
        _currentFrame = Math.Max(0, state.CurrentFrame);
        _frameAccumulator = 0.0;
    }

    /// <summary>
    /// Subscribe to <see cref="SelectionManager"/> events.
    /// Call once from MainWindow after SelectionManager.Initialize().
    /// </summary>
    public void Initialize()
    {
        Instance = this;
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += RefreshFromScene;
        TimelineIcons.Initialize(Gl);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render()
    {
        UpdatePlayback();
        SyncAudioWithPlayback();
        _trackRowVisibility.Clear();

        if (!ImGui.Begin(Localization.Label("text.common.timeline", "Timeline")))
        {
            IsWindowHovered = false;
            HasEditFocus = false;
            WindowPos = Vector2.Zero;
            WindowSize = Vector2.Zero;
            ImGui.End();
            return;
        }

        IsWindowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        WindowPos = ImGui.GetWindowPos();
        WindowSize = ImGui.GetWindowSize();

        // Keep the editing target while a menu is open so menu commands use the same scope.
        if (!ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            HasEditFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (HasEditFocus && !Input.IsAnyFreeFlyActive && !ImGui.IsAnyItemActive())
        {
            if (Keybinds.Pressed("cut")) CutSelectedKeyframes();
            else if (Keybinds.Pressed("copy") && HasSelectedKeyframes) CopySelectedKeyframes();
            else if (Keybinds.Pressed("paste")) PasteAtCurrentFrame();
            else if (Keybinds.Pressed("selectAll")) SelectAllKeyframes();
            else if (Keybinds.Pressed("stepBackward")) StepBackward();
            else if (Keybinds.Pressed("stepForward")) StepForward();
            else if (Keybinds.Pressed("jumpToStart")) JumpToStart();
            else if (Keybinds.Pressed("jumpToEnd")) JumpToLastKeyframe();
        }

        HandleTimelineZoomShortcut();

        RenderTransportControls();
        ImGui.Separator();

        float availW = ImGui.GetContentRegionAvail().X;
        float availH = ImGui.GetContentRegionAvail().Y;
        float statusH = ImGui.GetFrameHeightWithSpacing();
        float audioH  = RenderAudioTracksHeight();
        float tracksH = Math.Max(availH - RulerHeight - MarkerRowHeight - ImGui.GetStyle().ItemSpacing.Y - statusH - audioH, 60f * UiTheme.Scale);

        RenderRulerRow(availW);
        RenderTrackArea(availW, tracksH);
        RenderMarkers(availW);
        RenderAudioTracks(availW, audioH);

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

        ImGui.TextUnformatted(Localization.Format("text.panel.timeline.frame_s", _currentFrame, _currentFrame / _frameRate));

        // Deferred context menu popup
        if (_openContextMenu)
        {
            _openContextMenu = false;
            ImGui.OpenPopup("##kf_ctx");
        }
        RenderContextMenu();

        // Deferred add-keyframe menu popup
        if (_openAddKeyframeMenu)
        {
            _openAddKeyframeMenu = false;
            ImGui.OpenPopup("##kf_add");
        }
        RenderAddKeyframeMenu();
        RenderSpeedScaleMenu();
        RenderMarkerEditor();

        ImGui.End();
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private void UpdatePlayback()
    {
        long now = Stopwatch.GetTimestamp();
        double stopwatchDelta = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;

        if (!_isPlaying)
        {
            _frameAccumulator = 0.0;
            return;
        }

        double imguiDelta = ImGui.GetIO().DeltaTime;
        bool imguiValid = imguiDelta > 0.0 && imguiDelta < 0.5;
        bool stopwatchValid = stopwatchDelta > 0.0 && stopwatchDelta < 0.5;

        double delta = 0.0;
        if (imguiValid && stopwatchValid)
            delta = Math.Max(imguiDelta, stopwatchDelta);
        else if (imguiValid)
            delta = imguiDelta;
        else if (stopwatchValid)
            delta = stopwatchDelta;

        if (delta <= 0.0)
            return;

        float playbackFps = float.IsFinite(_frameRate)
            ? Math.Clamp(_frameRate, 1f, 120f)
            : 30f;

        _frameAccumulator += delta * playbackFps;
        int advance = (int)_frameAccumulator;
        _frameAccumulator -= advance;
        if (advance <= 0)
            return;

        int prev = _currentFrame;
        _currentFrame += advance;

        int furthest = _maxFrames;
        foreach (var kvp in _propertyKeyframes)
            foreach (var kf in kvp.Value)
                if (kf.Frame > furthest) furthest = kf.Frame;

        int loopStart = 0;
        int playbackEnd = furthest;
        if (_playbackRegionStart.HasValue && _playbackRegionEnd.HasValue &&
            _playbackRegionEnd.Value > _playbackRegionStart.Value)
        {
            loopStart = _playbackRegionStart.Value;
            playbackEnd = _playbackRegionEnd.Value;
        }

        if (_currentFrame > playbackEnd)
        {
            _currentFrame = _loopPlayback ? loopStart : playbackEnd;
            _frameAccumulator = 0.0;
            StopAllAudio();
            if (!_loopPlayback) _isPlaying = false;
        }
        if (_currentFrame != prev) ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    // ── Transport controls ────────────────────────────────────────────────────

    private void RenderTransportControls()
    {
        var  sz  = new Vector2(20, 20) * UiTheme.Scale;
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
                if (_loopPlayback && _playbackRegionStart.HasValue && _playbackRegionEnd.HasValue &&
                    (_currentFrame < _playbackRegionStart.Value || _currentFrame > _playbackRegionEnd.Value))
                    _currentFrame = _playbackRegionStart.Value;
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

        var loopTint = _loopPlayback
            ? new Vector4(0.35f, 0.75f, 1f, 1f)
            : Vector4.One;
        if (ico ? IcoBtnTinted("##loop", TimelineIcons.Loop, sz, loopTint) : ImGui.Button(_loopPlayback ? Localization.Label("text.panel.timeline.loop_2", "Loop*") : Localization.Label("text.panel.timeline.loop", "Loop")))
            _loopPlayback = !_loopPlayback;
        if (ImGui.IsItemHovered())
            UiText.Tooltip(_loopPlayback
                ? Localization.T("text.panel.timeline.looping_enabled_selected_playback_region_takes_priority")
                : Localization.T("text.panel.timeline.looping_disabled"));
        ImGui.SameLine();

        var ghostTint = _ghostModeEnabled
            ? new Vector4(0.75f, 0.95f, 1f, 1f)
            : Vector4.One;
        bool canUseGhostIcon = ico && TimelineIcons.Ghost != 0;
        if (canUseGhostIcon
                ? IcoBtnTinted("##ghost_mode", TimelineIcons.Ghost, sz, ghostTint)
                : ImGui.Button(_ghostModeEnabled ? Localization.Label("text.panel.timeline.ghost_3", "Ghost*") : Localization.Label("text.panel.timeline.ghost_2", "Ghost")))
        {
            _ghostModeEnabled = !_ghostModeEnabled;
            PruneHiddenSelection();
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip(_ghostModeEnabled
                ? Localization.T("text.panel.timeline.ghost_mode_on_ghost_tracks_hidden")
                : Localization.T("text.panel.timeline.ghost_mode_off_show_all_tracks"));
        ImGui.SameLine();

        // ── Auto-keyframe record button ──────────────────────────────────────
        ImGui.Spacing(); ImGui.SameLine();
        bool autoKeyNow = _autoKeyframe;
        if (!ico)
        {
            if (autoKeyNow)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.80f, 0.10f, 0.10f, 1.00f));
            if (ImGui.Button(" \u25cf "))
                _autoKeyframe = !_autoKeyframe;
            if (autoKeyNow)
                ImGui.PopStyleColor();
        }
        else
        {
            var tint = autoKeyNow
                ? new Vector4(1f, 0.3f, 0.3f, 1f)
                : new Vector4(1f, 1f,   1f,   1f);
            if (IcoBtnTinted("##ak", TimelineIcons.AutoKey, sz, tint))
                _autoKeyframe = !_autoKeyframe;
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip(_autoKeyframe ? Localization.T("text.panel.timeline.auto_keyframing_on") : Localization.T("text.panel.timeline.auto_keyframing_off"));

        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.panel.timeline.key_changed_properties", "Key Changed Properties")))
            KeyChangedPropertiesAtCurrentFrame();
        if (ImGui.IsItemHovered())
            UiText.Tooltip(Localization.T("text.panel.timeline.key_changed_properties_hint"));

        ImGui.TextUnformatted(Localization.T("text.panel.timeline.fps"));
        ImGui.SameLine();
        int fps = (int)Math.Clamp(_frameRate, 1, 120);
        ImGui.SetNextItemWidth(55f * UiTheme.Scale);
        if (ImGui.GetIO().WantTextInput)
        {
            string fpsBuffer = fps.ToString();
            if (ImGui.InputText("##fps", ref fpsBuffer, 16, ImGuiInputTextFlags.CharsDecimal))
            {
                fpsBuffer = SanitizeIntegerText(fpsBuffer);
                if (int.TryParse(fpsBuffer, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedFps))
                    _frameRate = Math.Clamp(parsedFps, 1, 120);
            }
        }
        else if (ImGui.DragInt("##fps", ref fps, 1f, 1, 120, "%d", ImGuiSliderFlags.AlwaysClamp))
            _frameRate = Math.Clamp(fps, 1, 120);

        ImGui.SameLine();
        ImGui.TextUnformatted(Localization.T("text.panel.timeline.frames"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60f * UiTheme.Scale);
        int maxFrames = Math.Max(10, _maxFrames);
        if (ImGui.GetIO().WantTextInput)
        {
            string maxFramesBuffer = maxFrames.ToString();
            if (ImGui.InputText("##maxf", ref maxFramesBuffer, 16, ImGuiInputTextFlags.CharsDecimal))
            {
                maxFramesBuffer = SanitizeIntegerText(maxFramesBuffer);
                if (int.TryParse(maxFramesBuffer, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedMaxFrames))
                    _maxFrames = Math.Max(10, parsedMaxFrames);
            }
        }
        else if (ImGui.DragInt("##maxf", ref maxFrames, 1f, 10, 100000, "%d", ImGuiSliderFlags.AlwaysClamp))
            _maxFrames = Math.Max(10, maxFrames);

        ImGui.SameLine();
        ImGui.TextUnformatted(Localization.T("text.panel.timeline.zoom"));
        ImGui.SameLine();
        if (ImGui.SmallButton("-##timeline_zoom_out"))
            ZoomTimeline(1f / ZoomStep, GetPlayheadZoomAnchor());
        if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.T("text.panel.timeline.zoom_out_ctrl_mouse_wheel"));
        ImGui.SameLine();
        UiText.Disabled(Localization.Format("text.panel.timeline.px_f", _pixelsPerFrame));
        ImGui.SameLine();
        if (ImGui.SmallButton("+##timeline_zoom_in"))
            ZoomTimeline(ZoomStep, GetPlayheadZoomAnchor());
        if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.T("text.panel.timeline.zoom_in_ctrl_mouse_wheel"));
    }

    private void HandleTimelineZoomShortcut()
    {
        var io = ImGui.GetIO();
        if (!IsWindowHovered || !io.KeyCtrl || MathF.Abs(io.MouseWheel) < float.Epsilon)
            return;

        float timelineLeft = WindowPos.X + LeftColumnWidth;
        float mouseViewportX = Math.Clamp(io.MousePos.X - timelineLeft, 0f, Math.Max(0f, _trackViewportWidth));
        float anchorFrame = (_hScrollOffset + mouseViewportX) / _pixelsPerFrame;
        ZoomTimeline(MathF.Pow(ZoomStep, io.MouseWheel), (anchorFrame, mouseViewportX));
    }

    private (float frame, float viewportX) GetPlayheadZoomAnchor()
    {
        float viewportX = _currentFrame * _pixelsPerFrame - _hScrollOffset;
        viewportX = Math.Clamp(viewportX, 0f, Math.Max(0f, _trackViewportWidth));
        float frame = (_hScrollOffset + viewportX) / _pixelsPerFrame;
        return (frame, viewportX);
    }

    private void ZoomTimeline(float factor, (float frame, float viewportX) anchor)
    {
        float newScale = Math.Clamp(_pixelsPerFrame * factor, MinPixelsPerFrame, MaxPixelsPerFrame);
        if (MathF.Abs(newScale - _pixelsPerFrame) < 0.001f) return;

        _pixelsPerFrame = newScale;
        _pendingHScrollOffset = Math.Max(0f, anchor.frame * newScale - anchor.viewportX);
    }

    private string GetStableObjectTrackId(SceneObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.LibrarySourceId)
            ? obj.LibrarySourceId
            : obj.ObjectId;
    }

    private string BuildGhostTrackKey(SceneObject obj, string propertyPath)
    {
        return $"{GetStableObjectTrackId(obj)}.{propertyPath}";
    }

    private bool IsGhostTrack(SceneObject obj, string propertyPath)
    {
        return _ghostTracks.Contains(BuildGhostTrackKey(obj, propertyPath));
    }

    private bool IsTrackRowGhost(TimelineProperty row)
    {
        if (row.PropertyPath == "__header__")
            return false;

        if (!row.IsGroupHeader)
            return IsGhostTrack(row.Object, row.PropertyPath);

        if (row.GroupPaths == null || row.GroupPaths.Length == 0)
            return IsGhostTrack(row.Object, row.PropertyPath);

        return row.GroupPaths.All(path => IsGhostTrack(row.Object, path));
    }

    private void SetTrackGhostState(SceneObject obj, string propertyPath, bool ghost)
    {
        string key = BuildGhostTrackKey(obj, propertyPath);
        if (ghost)
            _ghostTracks.Add(key);
        else
            _ghostTracks.Remove(key);
    }

    private void SetTrackRowGhostState(TimelineProperty row, bool ghost)
    {
        if (!row.IsGroupHeader)
        {
            SetTrackGhostState(row.Object, row.PropertyPath, ghost);
            return;
        }

        if (row.GroupPaths == null || row.GroupPaths.Length == 0)
        {
            SetTrackGhostState(row.Object, row.PropertyPath, ghost);
            return;
        }

        foreach (var path in row.GroupPaths)
            SetTrackGhostState(row.Object, path, ghost);
    }

    private bool IsTrackRowVisible(TimelineProperty row)
    {
        if (_trackRowVisibility.TryGetValue(row, out bool visible))
            return visible;

        visible = row.PropertyPath == "__header__" ||
                  (!_ghostModeEnabled || !IsTrackRowGhost(row)) &&
                  (row.Indent == 0 || IsGroupChildVisible(row));

        _trackRowVisibility[row] = visible;
        return visible;
    }

    private bool ShouldShowGhostIndicator(TimelineProperty row)
    {
        return !_ghostModeEnabled
               && row.PropertyPath != "__header__"
               && IsTrackRowGhost(row);
    }

    private void PruneHiddenSelection()
    {
        if (!_ghostModeEnabled || _selectedKeyframes.Count == 0)
            return;

        var hidden = _selectedKeyframes.Where(kf =>
        {
            if (!_keyframeOwners.TryGetValue(kf, out var owner))
                return false;
            return IsGhostTrack(owner.obj, owner.path);
        }).ToList();

        if (hidden.Count == 0)
            return;

        foreach (var keyframe in hidden)
        {
            _selectedKeyframes.Remove(keyframe);
            _keyframeOwners.Remove(keyframe);
        }
    }

    // ── Audio tracks ──────────────────────────────────────────────────────────

    private static float AudioRowHeight => 28f * UiTheme.Scale;
    private static float AudioFooterHeight => 24f * UiTheme.Scale;
    private static float AudioControlWidth => 176f * UiTheme.Scale;  // vol slider + M + L + X

    private static string TruncateWithEllipsis(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        const string ellipsis = "...";
        float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        float budget = Math.Max(0f, maxWidth - ellipsisWidth);
        if (budget <= 0f) return ellipsis;

        int lo = 0, hi = text.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= budget)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return text[..best] + ellipsis;
    }

    private float RenderAudioTracksHeight()
    {
        // One row per clip + a footer row holding the "+ Add Audio Track" button.
        return Math.Max(48f * UiTheme.Scale, _audioTracks.Count * AudioRowHeight + AudioFooterHeight + 4f * UiTheme.Scale);
    }

    private void RenderAudioTracks(float availW, float audioH)
    {
        ImGui.Separator();

        // ── Scrollable track list (no visible scrollbar, synced to keyframe tracks above) ──
        float footerH = AudioFooterHeight + 2f * UiTheme.Scale;
        float tracksH = Math.Max(0f, audioH - footerH);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##audio_tracks", new Vector2(availW, tracksH), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetScrollX(_hScrollOffset);
        ImGui.PopStyleVar();

        if (!Services.AudioEngine.IsInitialized)
        {
            var warnDl = ImGui.GetWindowDrawList();
            var p      = ImGui.GetCursorScreenPos();
            warnDl.AddText(p, 0xFFFFAA66, Localization.T("text.panel.timeline.audio_no_openal_device_available_audio_disabled"));
            ImGui.Dummy(new Vector2(0, RowHeight));
        }

        float leftW  = LeftColumnWidth;
        float rightW = Math.Max(0f, availW - leftW);

        var   dl       = ImGui.GetWindowDrawList();
        var   wPos     = ImGui.GetWindowPos();
        float scrollX  = ImGui.GetScrollX();
        float contentW = _maxFrames * _pixelsPerFrame * 1.5f;

        for (int i = 0; i < _audioTracks.Count; i++)
        {
            var track = _audioTracks[i];
            bool selected = _selectedAudioTrack == track;
            float rowTopY = ImGui.GetCursorPosY();

            ImGui.PushID($"aud_{i}");
            ImGui.BeginGroup();

            // Compact label — truncate to keep controls on the left column.
            var labelColor = selected
                ? new Vector4(0.55f, 0.85f, 1f, 1f)
                : new Vector4(0.85f, 0.85f, 0.85f, 1f);
            float labelMaxW = Math.Max(60f * UiTheme.Scale, LeftColumnWidth - AudioControlWidth - 12f * UiTheme.Scale);
            string labelText = TruncateWithEllipsis(track.ManifestEntry.DisplayName, labelMaxW);
            UiText.Colored(labelColor, labelText);
            ImGui.SameLine();

            // Compact volume slider
            float vol = track.ManifestEntry.Volume;
            ImGui.SetNextItemWidth(70f * UiTheme.Scale);
            if (ImGui.SliderFloat($"##vol{i}", ref vol, 0f, 1f, "%.2f"))
            {
                track.ManifestEntry.Volume = vol;
                if (track.IsLoaded)
                    Services.AudioEngine.SetSourceVolume(track.Source, vol);
            }
            ImGui.SameLine();

            bool muted = track.ManifestEntry.Muted;
            if (ImGui.Checkbox(Localization.T("text.panel.timeline.m") + $"###M##mute{i}", ref muted))
            {
                track.ManifestEntry.Muted = muted;
                if (track.IsLoaded)
                    Services.AudioEngine.SetSourceVolume(track.Source, muted ? 0f : vol);
            }
            ImGui.SameLine();

            bool loop = track.ManifestEntry.Loop;
            if (ImGui.Checkbox(Localization.T("text.panel.timeline.l") + $"###L##loop{i}", ref loop))
            {
                track.ManifestEntry.Loop = loop;
                if (track.IsLoaded)
                    Services.AudioEngine.SetSourceLooping(track.Source, loop);
            }
            ImGui.SameLine();

            if (ImGui.SmallButton($"X##rm{i}"))
            {
                RemoveAudioTrack(track);
                ImGui.EndGroup();
                ImGui.PopID();
                break;
            }

            ImGui.EndGroup();
            ImGui.PopID();

            // Right side: clip rectangle, full row height.
            Vector2 rightMin  = new Vector2(wPos.X + leftW, wPos.Y + rowTopY);
            Vector2 rightSize = new Vector2(rightW, AudioRowHeight);

            dl.AddRectFilled(rightMin, rightMin + rightSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.14f, 1f)));

            int durFrames = (int)Math.Ceiling(track.ManifestEntry.CachedDurationSeconds * _frameRate);
            if (durFrames <= 0 && track.Clip != null)
            {
                durFrames = (int)Math.Ceiling(track.Clip.DurationSeconds * _frameRate);
                if (durFrames > 0)
                    track.ManifestEntry.CachedDurationSeconds = (float)track.Clip.DurationSeconds;
            }
            if (durFrames < 1) durFrames = 1;

            float clipX0 = rightMin.X + track.ManifestEntry.StartFrame * _pixelsPerFrame - scrollX;
            float clipX1 = clipX0 + durFrames * _pixelsPerFrame;
            if (clipX1 >= rightMin.X && clipX0 <= rightMin.X + rightSize.X)
            {
                var clipMin = new Vector2(Math.Max(clipX0, rightMin.X), rightMin.Y + 2f * UiTheme.Scale);
                var clipMax = new Vector2(Math.Min(clipX1, rightMin.X + rightSize.X), rightMin.Y + rightSize.Y - 2f * UiTheme.Scale);
                uint clipFill = track.IsLoaded
                    ? (selected ? 0xFF3377CC : 0xFF2266AA)
                    : (selected ? 0xFF666677 : 0xFF444455);
                uint clipEdge = selected ? 0xFF66BBFF : 0xFF4488CC;
                dl.AddRectFilled(clipMin, clipMax, clipFill, 3f * UiTheme.Scale);
                dl.AddRect(clipMin, clipMax, clipEdge, 3f * UiTheme.Scale, ImDrawFlags.RoundCornersAll, 1.5f * UiTheme.Scale);

                // Always show the start frame at the left edge of the clip.
                string label = Localization.Format("text.panel.timeline.f", track.ManifestEntry.StartFrame);
                if (!track.IsLoaded) label += Localization.T("text.panel.timeline.decoding");
                dl.AddText(clipMin + new Vector2(4f, 4f) * UiTheme.Scale, 0xFFFFFFFF, label);

                // If very thin, still draw a visible left-edge handle.
                if (clipMax.X - clipMin.X < 6f * UiTheme.Scale)
                {
                    dl.AddLine(new Vector2(clipMin.X + UiTheme.Scale, clipMin.Y), new Vector2(clipMin.X + UiTheme.Scale, clipMax.Y), 0xFF66BBFF, 2f * UiTheme.Scale);
                }

                Vector2 mouse = ImGui.GetMousePos();
                bool mouseInClipArea = mouse.X >= rightMin.X && mouse.X < rightMin.X + rightSize.X
                                    && mouse.Y >= rightMin.Y && mouse.Y < rightMin.Y + rightSize.Y;
                if (mouseInClipArea && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    if (_isDraggingAudioClip)
                        UiText.Tooltip(Localization.Format("text.panel.timeline.dragging_audio_clip_to_frame", track.ManifestEntry.StartFrame));
                    else
                        UiText.Tooltip(Localization.T("text.panel.timeline.drag_to_reposition_on_timeline"));

                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _selectedAudioTrack   = track;
                        _isDraggingAudioClip  = true;
                        _audioDragStartMouseX = (int)mouse.X;
                        _audioDragStartFrame  = track.ManifestEntry.StartFrame;
                    }
                    else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    {
                        _selectedAudioTrack = track;
                        ImGui.OpenPopup($"##audio_ctx_{i}");
                    }
                }

                if (ImGui.BeginPopup($"##audio_ctx_{i}"))
                {
                    if (ImGui.MenuItem(Localization.Label("text.panel.timeline.remove_audio_track", "Remove Audio Track")))
                    {
                        RemoveAudioTrack(track);
                        ImGui.EndPopup();
                        break;
                    }
                    ImGui.EndPopup();
                }

                if (_isDraggingAudioClip && _selectedAudioTrack == track && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    float localX = mouse.X - rightMin.X + scrollX;
                    int   newFrame = Math.Max(0, (int)MathF.Round(localX / _pixelsPerFrame));
                    if (newFrame != track.ManifestEntry.StartFrame)
                    {
                        track.ManifestEntry.StartFrame = newFrame;
                        if (track.IsLoaded && _isPlaying)
                        {
                            float playOffset = Math.Max(0f, (_currentFrame - track.ManifestEntry.StartFrame) / _frameRate)
                                             + track.ManifestEntry.SourceOffsetSeconds;
                            Services.AudioEngine.SetSourceOffsetSeconds(track.Source, playOffset);
                        }
                    }
                }
            }

            ImGui.Dummy(new Vector2(contentW, AudioRowHeight));
        }

        if (_isDraggingAudioClip && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _isDraggingAudioClip = false;

        ImGui.EndChild();

        // ── Fixed footer (outside the scrollable area) ──────────────────────
        if (_audioTracks.Count == 0)
        {
            UiText.Disabled(Localization.T("text.panel.timeline.no_audio_tracks_yet"));
            ImGui.SameLine();
        }
        ImGui.SetCursorPosX(availW - 160f * UiTheme.Scale);
        if (ImGui.SmallButton(Localization.Label("text.panel.timeline.add_audio_track", "+ Add Audio Track")))
            ImGui.OpenPopup("##addAudioPopup");

        if (ImGui.BeginPopup("##addAudioPopup"))
        {
            var assets = ProjectManager.Instance.GetProjectAssets();
            var soundAssets = assets.Where(a => a.AssetType == ProjectAssetType.Sound).ToList();
            if (soundAssets.Count == 0)
            {
                UiText.Disabled(Localization.T("text.panel.timeline.no_sound_assets_in_this_project"));
                UiText.Disabled(Localization.T("text.panel.timeline.import_a_wav_mp3_ogg_flac_m4a_first"));
            }
            else
            {
                foreach (var a in soundAssets.Where(a => ImGui.MenuItem(a.DisplayName)))
                {
                    AddAudioTrackFromAsset(a);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>Add a new audio track backed by the given sound asset.</summary>
    public TimelineAudioTrack AddAudioTrackFromAsset(ProjectAssetEntry asset)
    {
        string fullPath = ProjectManager.Instance.GetAssetFullPath(asset);
        var manifest = new ProjectAudioTrack
        {
            AssetDisplayName    = asset.DisplayName,
            DisplayName         = Path.GetFileNameWithoutExtension(asset.DisplayName),
            StartFrame          = _currentFrame,
            SourceOffsetSeconds = 0f,
            Volume              = 1f,
            Muted               = false,
            Loop                = false,
        };

        var track = new TimelineAudioTrack { ManifestEntry = manifest };
        _audioTracks.Add(track);

        Task.Run(() =>
        {
            var clip = Services.AudioEngine.LoadClip(fullPath);
            if (clip == null) return;
            track.Clip = clip;
            var src   = Services.AudioEngine.CreateSource(clip);
            track.Source = src;
            Services.AudioEngine.SetSourceVolume(src, manifest.Muted ? 0f : manifest.Volume);
            Services.AudioEngine.SetSourceLooping(src, manifest.Loop);
            if (manifest.CachedDurationSeconds <= 0f)
                manifest.CachedDurationSeconds = (float)clip.DurationSeconds;
        });

        RecalculateTimelineLength();
        return track;
    }

    public void RemoveAudioTrack(TimelineAudioTrack track)
    {
        if (track.IsLoaded)
        {
            Services.AudioEngine.StopSource(track.Source);
            Services.AudioEngine.DestroySource(track.Source);
        }
        _audioTracks.Remove(track);
        if (_selectedAudioTrack == track) _selectedAudioTrack = null;
    }

    /// <summary>
    /// Load audio tracks from the project manifest (call once after project load).
    /// </summary>
    public void LoadAudioTracksFromManifest(IEnumerable<ProjectAudioTrack> entries)
    {
        foreach (var entry in entries)
        {
            var track = new TimelineAudioTrack { ManifestEntry = entry };
            _audioTracks.Add(track);

            var asset = ProjectManager.Instance.GetProjectAssets()
                .FirstOrDefault(a => string.Equals(a.DisplayName, entry.AssetDisplayName, StringComparison.OrdinalIgnoreCase));
            if (asset == null) continue;
            string fullPath = ProjectManager.Instance.GetAssetFullPath(asset);

            Task.Run(() =>
            {
                var clip = Services.AudioEngine.LoadClip(fullPath);
                if (clip == null) return;
                track.Clip = clip;
                var src   = Services.AudioEngine.CreateSource(clip);
                track.Source = src;
                Services.AudioEngine.SetSourceVolume(src, entry.Muted ? 0f : entry.Volume);
                Services.AudioEngine.SetSourceLooping(src, entry.Loop);
                if (entry.CachedDurationSeconds <= 0f)
                    entry.CachedDurationSeconds = (float)clip.DurationSeconds;
            });
        }
        RecalculateTimelineLength();
    }

    /// <summary>Pause / stop every active audio source.</summary>
    public void PauseAllAudio()
    {
        foreach (var t in _audioTracks)
        {
            if (!t.IsLoaded) continue;
            Services.AudioEngine.PauseSource(t.Source);
            // Keep WasPlaying=true so resume re-syncs without restarting from 0.
        }
    }

    public void StopAllAudio()
    {
        foreach (var t in _audioTracks)
        {
            if (!t.IsLoaded) continue;
            Services.AudioEngine.StopSource(t.Source);
            t.WasPlaying = false;
        }
    }

    /// <summary>
    /// Called every frame while the timeline is rendering.  Starts / pauses
    /// sources so the audio stays in sync with the current frame and play state.
    /// Only re-seeks when the user scrubs the playhead (avoids per-frame
    /// stutter caused by constantly rewinding the source).
    /// </summary>
    public void SyncAudioWithPlayback()
    {
        if (!Services.AudioEngine.IsInitialized) return;
        bool wantPlaying = _isPlaying;

        foreach (var t in _audioTracks)
        {
            if (!t.IsLoaded) continue;

            int durFrames = (int)Math.Ceiling(t.Clip!.DurationSeconds * _frameRate);
            if (durFrames < 1) durFrames = 1;
            int endFrame = t.ManifestEntry.StartFrame + durFrames;
            bool inRange = _currentFrame >= t.ManifestEntry.StartFrame && _currentFrame < endFrame;

            if (wantPlaying && inRange)
            {
                float offset = (_currentFrame - t.ManifestEntry.StartFrame) / _frameRate
                             + t.ManifestEntry.SourceOffsetSeconds;
                if (offset < 0f) offset = 0f;

                if (!t.WasPlaying)
                {
                    // First time entering the play range — start the source
                    // from the correct offset and let it run naturally.
                    Services.AudioEngine.SetSourceOffsetSeconds(t.Source, offset);
                    Services.AudioEngine.SetSourceLooping(t.Source, t.ManifestEntry.Loop);
                    Services.AudioEngine.SetSourceVolume(t.Source,
                        t.ManifestEntry.Muted ? 0f : t.ManifestEntry.Volume);
                    Services.AudioEngine.PlaySource(t.Source);
                    t.WasPlaying = true;
                }
                else
                {
                    // Already playing — only re-seek if the user scrubbed the
                    // playhead by more than a small threshold.
                    float currentOffset = Services.AudioEngine.GetSourceOffsetSeconds(t.Source);
                    if (MathF.Abs(currentOffset - offset) > 0.1f)
                        Services.AudioEngine.SetSourceOffsetSeconds(t.Source, offset);
                }
            }
            else
            {
                if (t.WasPlaying)
                {
                    Services.AudioEngine.StopSource(t.Source);
                    t.WasPlaying = false;
                }
            }
        }
    }

    private void JumpToStart()          { _currentFrame = 0; _frameAccumulator = 0.0; StopAllAudio(); ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    private void Stop()                 { _isPlaying = false; StopAllAudio(); JumpToStart(); }
    private void StepBackward()         { _currentFrame = Math.Max(0, _currentFrame - 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    private void StepForward()          { _currentFrame = Math.Min(_maxFrames, _currentFrame + 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }

    // ── ImageButton helpers (byte* API required by Hexa.NET.ImGui 2.x) ───────

    private static unsafe bool IcoBtn(string id, uint texId, Vector2 sz)
    {
        var tex = new ImTextureRef(texId: (ulong)texId);
        byte[] b = Encoding.UTF8.GetBytes(id + '\0');
        fixed (byte* p = b) return ImGui.ImageButton(p, tex, sz);
    }

    private static unsafe bool IcoBtnTinted(string id, uint texId, Vector2 sz,
                                             Vector4 tint)
    {
        var tex  = new ImTextureRef(texId: (ulong)texId);
        var uv0  = new Vector2(0, 0);
        var uv1  = new Vector2(1, 1);
        var bg   = new Vector4(0, 0, 0, 0);
        byte[] b = Encoding.UTF8.GetBytes(id + '\0');
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
        _timelineAreaTop = wPos.Y;

        dl.AddRectFilled(wPos, wPos + wSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.14f, 0.14f, 0.14f, 1f)));

        if (_playbackRegionStart.HasValue && _playbackRegionEnd.HasValue)
        {
            float regionX0 = wPos.X + _playbackRegionStart.Value * _pixelsPerFrame - scrollX;
            float regionX1 = wPos.X + _playbackRegionEnd.Value * _pixelsPerFrame - scrollX;
            dl.AddRectFilled(new Vector2(Math.Max(wPos.X, regionX0), wPos.Y),
                new Vector2(Math.Min(wPos.X + wSize.X, regionX1), wPos.Y + RulerHeight), 0x443399FF);
            dl.AddLine(new Vector2(regionX0, wPos.Y), new Vector2(regionX0, wPos.Y + RulerHeight), 0xFF66AAFF, 2f * UiTheme.Scale);
            dl.AddLine(new Vector2(regionX1, wPos.Y), new Vector2(regionX1, wPos.Y + RulerHeight), 0xFF66AAFF, 2f * UiTheme.Scale);
        }

        int startF = Math.Max(0, (int)(scrollX / _pixelsPerFrame) - 1);
        int endF   = Math.Min((int)(_maxFrames * 1.5f), (int)((scrollX + wSize.X) / _pixelsPerFrame) + 2);

        for (int f = startF; f <= endF; f++)
        {
            float sx = wPos.X + f * _pixelsPerFrame - scrollX;
            if (sx < wPos.X - 2f * UiTheme.Scale || sx > wPos.X + wSize.X + 2f * UiTheme.Scale) continue;

            if (f % 10 == 0)
            {
                dl.AddLine(new Vector2(sx, wPos.Y + RulerHeight * 0.35f),
                           new Vector2(sx, wPos.Y + RulerHeight), 0xFFAAAAAA, UiTheme.Scale);
                if (f % 30 == 0 || _pixelsPerFrame >= 4f)
                    dl.AddText(new Vector2(sx + 2f * UiTheme.Scale, wPos.Y + 2f * UiTheme.Scale), 0xFFCCCCCC, f.ToString());
            }
            else if (f % 5 == 0)
            {
                dl.AddLine(new Vector2(sx, wPos.Y + RulerHeight * 0.55f),
                           new Vector2(sx, wPos.Y + RulerHeight), 0xFF666666, UiTheme.Scale);
            }
        }

        // Playhead triangle on ruler
        float phX = wPos.X + _currentFrame * _pixelsPerFrame - scrollX;
        dl.AddLine(wPos with { X = phX }, new Vector2(phX, wPos.Y + RulerHeight), 0xFF3377FF, 2f * UiTheme.Scale);
        dl.AddTriangleFilled(
            wPos with { X = phX - 5f * UiTheme.Scale },
            wPos with { X = phX + 5f * UiTheme.Scale },
            new Vector2(phX, wPos.Y + 10f * UiTheme.Scale), 0xFF3377FF);

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

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _contextFrame = Math.Max(0, (int)MathF.Round((ImGui.GetMousePos().X - wPos.X + scrollX) / _pixelsPerFrame));
            _playbackRegionAnchorFrame = _contextFrame;
            _playbackRegionStart = _contextFrame;
            _playbackRegionEnd = _contextFrame;
            _isDraggingPlaybackRegion = true;
            _playbackRegionDragged = false;
        }

        if (_isDraggingPlaybackRegion && ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            int frame = Math.Max(0, (int)MathF.Round((ImGui.GetMousePos().X - wPos.X + scrollX) / _pixelsPerFrame));
            _playbackRegionStart = Math.Min(_playbackRegionAnchorFrame, frame);
            _playbackRegionEnd = Math.Max(_playbackRegionAnchorFrame, frame);
            _playbackRegionDragged |= Math.Abs(frame - _playbackRegionAnchorFrame) > 0;
        }

        if (_isDraggingPlaybackRegion && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            _isDraggingPlaybackRegion = false;
            if (!_playbackRegionDragged)
            {
                _playbackRegionStart = null;
                _playbackRegionEnd = null;
                _ctxKeyframe = null;
                _ctxTrackRow = null;
                _activeMarker = null;
                _openContextMenu = true;
            }
        }

        ImGui.EndChild();
    }

    // ── Main track area ───────────────────────────────────────────────────────

    private void RenderMarkers(float availW)
    {
        // Docked windows can report a zero-width content region for one frame
        // while ImGui restores the layout. InvisibleButton asserts on either
        // dimension being exactly zero, so keep its hit target valid.
        float markerWidth = Math.Max(UiTheme.Scale, availW);
        Vector2 rowPos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(rowPos, rowPos + new Vector2(markerWidth, MarkerRowHeight), 0xFF202020);
        dl.AddText(rowPos + new Vector2(5f, 5f) * UiTheme.Scale, 0xFFAAAAAA, Localization.T("text.panel.timeline.markers"));

        float rightX = rowPos.X + LeftColumnWidth;
        Vector2 mouse = ImGui.GetMousePos();
        TimelineMarker? hovered = null;
        foreach (var marker in _markers.OrderBy(m => m.Frame))
        {
            float x = rightX + marker.Frame * _pixelsPerFrame - _hScrollOffset;
            if (x < rightX || x > rowPos.X + markerWidth) continue;
            uint color = ImGui.ColorConvertFloat4ToU32(marker.Color);
            dl.AddLine(new Vector2(x, _timelineAreaTop), new Vector2(x, rowPos.Y + MarkerRowHeight), color, 1.5f * UiTheme.Scale);
            dl.AddTriangleFilled(new Vector2(x - 6f * UiTheme.Scale, rowPos.Y + 2f * UiTheme.Scale), new Vector2(x + 6f * UiTheme.Scale, rowPos.Y + 2f * UiTheme.Scale),
                new Vector2(x, rowPos.Y + 10f * UiTheme.Scale), color);
            string text = string.IsNullOrWhiteSpace(marker.Label) ? Localization.Format("text.panel.timeline.frame", marker.Frame) : marker.Label;
            dl.AddText(new Vector2(x + 7f * UiTheme.Scale, rowPos.Y + 7f * UiTheme.Scale), color, text);
            if (MathF.Abs(mouse.X - x) <= 7f * UiTheme.Scale && mouse.Y >= rowPos.Y && mouse.Y < rowPos.Y + MarkerRowHeight)
                hovered = marker;
        }

        ImGui.InvisibleButton("##marker_row", new Vector2(markerWidth, MarkerRowHeight));
        if (hovered != null && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _draggedMarker = hovered;
            _activeMarker = hovered;
            _isDraggingMarker = true;
        }

        if (_isDraggingMarker && _draggedMarker != null)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _draggedMarker.Frame = Math.Max(0,
                    (int)MathF.Round((mouse.X - rightX + _hScrollOffset) / _pixelsPerFrame));
                UiText.Tooltip(Localization.Format("text.panel.timeline.frame_3", _draggedMarker.Label, _draggedMarker.Frame));
            }
            else
            {
                _isDraggingMarker = false;
                _draggedMarker = null;
                _markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _contextFrame = Math.Max(0, (int)MathF.Round((mouse.X - rightX + _hScrollOffset) / _pixelsPerFrame));
            _activeMarker = hovered;
            _ctxKeyframe = null;
            _ctxTrackRow = null;
            _openContextMenu = true;
        }
        if (hovered != null && ImGui.IsItemHovered())
            UiText.Tooltip(Localization.Format("text.panel.timeline.frame_right_click_for_options", hovered.Label, hovered.Frame));
    }

    private void RenderMarkerEditor()
    {
        if (_openMarkerEditor)
        {
            _openMarkerEditor = false;
            ImGui.OpenPopup(Localization.Label("text.panel.timeline.add_marker", "Add Marker"));
        }
        bool open = true;
        if (!ImGui.BeginPopupModal(Localization.Label("text.panel.timeline.add_marker", "Add Marker"), ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.InputText(Localization.Label("text.panel.timeline.label", "Label"), ref _markerLabel, 128);
        if (ImGui.BeginCombo(Localization.Label("text.panel.timeline.preset", "Preset"), Localization.T("text.panel.timeline.choose_color")))
        {
            foreach (var preset in new (string name, Vector4 color)[]
            {
                ("Red", new(0.90f, 0.18f, 0.18f, 1f)), ("Orange", new(1f, 0.48f, 0.10f, 1f)),
                ("Yellow", new(0.95f, 0.82f, 0.12f, 1f)), ("Green", new(0.30f, 0.80f, 0.25f, 1f)),
                ("Forest Green", new(0.10f, 0.42f, 0.18f, 1f)), ("Teal", new(0.08f, 0.68f, 0.65f, 1f)),
                ("Blue", new(0.18f, 0.45f, 0.95f, 1f)), ("Purple", new(0.56f, 0.28f, 0.85f, 1f)),
                ("Pink", new(0.95f, 0.35f, 0.65f, 1f))
            })
                if (ImGui.Selectable(Localization.DisplayLabel(preset.name))) _markerColor = preset.color;
            ImGui.EndCombo();
        }
        ImGui.ColorEdit4(Localization.Label("text.common.color", "Color"), ref _markerColor, ImGuiColorEditFlags.NoInputs);
        UiText.Disabled(Localization.Format("text.panel.timeline.frame", _contextFrame));
        if (ImGui.Button(Localization.Label("text.common.add", "Add")))
        {
            _markers.Add(new TimelineMarker { Frame = _contextFrame,
                Label = string.IsNullOrWhiteSpace(_markerLabel) ? "Marker" : _markerLabel.Trim(), Color = _markerColor });
            _markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel"))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void RenderTrackArea(float availW, float tracksH)
    {
        float rightW = availW - LeftColumnWidth;
        _trackViewportWidth = Math.Max(0f, rightW);

        // Left column: property labels (no horizontal scroll)
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 2f) * UiTheme.Scale);
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

        if (_pendingHScrollOffset.HasValue)
        {
            ImGui.SetScrollX(_pendingHScrollOffset.Value);
            _pendingHScrollOffset = null;
        }

        // Right panel is the scroll master; left panel mirrors it each frame.
        _hScrollOffset = ImGui.GetScrollX();
        _vScrollOffset = ImGui.GetScrollY();

        RenderKeyframeTracks();

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    // ── Left labels ───────────────────────────────────────────────────────────

    private static string GetRowDisplayLabel(TimelineProperty row)
    {
        if (row.PropertyPath.StartsWith("shapekey.", StringComparison.Ordinal))
            return row.Label;

        string[] parts = row.PropertyPath.Split('.');
        if (parts.Length == 2 && parts[0] == "copy_constraint" && int.TryParse(parts[1], out int copyIndex) &&
            copyIndex >= 0 && copyIndex < row.Object.CopyConstraints.Count)
            return Localization.Format("text.panel.timeline.copy_constraint", copyIndex + 1,
                Localization.DisplayText(GetCopyConstraintLabel(row.Object.CopyConstraints[copyIndex].Type)));
        if (row.Object is BoneSceneObject bone && parts[0] == "constraint")
        {
            if (parts.Length == 2 && int.TryParse(parts[1], out int constraintIndex))
                return Localization.Format("text.panel.timeline.constraint_inverse_kinematics", constraintIndex + 1);
            if (parts.Length == 6 && GetIkParticipationBone(bone, string.Join('.', parts.Skip(3))) is { } chainBone)
                return Localization.Format("text.panel.timeline.ik_bone_participation", chainBone.Name,
                    Localization.DisplayText(parts[5] == "rotate" ? "Rotate" : "Bend"));
        }
        if (row.Object is CameraSceneObject camera && parts.Length == 3 && parts[0] == "camera" && parts[1] == "effect" &&
            int.TryParse(parts[2], out int index) && index >= 0 && index < camera.Effects.Count)
            return camera.Effects[index].Type switch
            {
                CameraEffectType.DepthOfField => Localization.Format("text.panel.timeline.effect_depth_of_field", index + 1),
                CameraEffectType.Bloom => Localization.Format("text.panel.timeline.effect_bloom", index + 1),
                CameraEffectType.FilmGrain => Localization.Format("text.panel.timeline.effect_film_grain", index + 1),
                _ => Localization.Format("text.panel.timeline.effect_camera_shake", index + 1)
            };

        return Localization.DisplayText(row.Label);
    }

    private void RenderLeftLabels()
    {
        if (_displayRows.Count == 0)
        {
            UiText.Disabled(Localization.T("text.common.no_object_selected"));
            return;
        }

        var rows = _displayRows.ToArray();
        foreach (var row in rows)
        {
            if (!IsTrackRowVisible(row))
                continue;

            if (row.PropertyPath == "__header__")
            {
                float hY = ImGui.GetCursorPosY();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.5f, 1f));
                // Vertically centre the text within RowHeight
                ImGui.SetCursorPosY(hY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.TextUnformatted(row.Label);
                ImGui.PopStyleColor();
                ImGui.SetCursorPosY(hY + RowHeight);
                ImGui.Dummy(Vector2.Zero);  // commit boundary
                continue;
            }

            float rowY = ImGui.GetCursorPosY();

            if (row.Indent > 0) ImGui.Indent(12f * UiTheme.Scale);
            bool showGhostIndicator = ShouldShowGhostIndicator(row);

            if (row.IsGroupHeader)
            {
                string gk  = $"{row.Object.ObjectId}.{row.PropertyPath}";
                _groupExpanded.TryAdd(gk, false);
                bool   exp = _groupExpanded[gk];

                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.ArrowButton($"##arr_{gk}", exp ? ImGuiDir.Down : ImGuiDir.Right))
                {
                    _groupExpanded[gk] = !exp;
                    _trackRowVisibility.Clear();
                }
                ImGui.SameLine();
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.TextUnformatted(GetRowDisplayLabel(row));
                if (showGhostIndicator)
                {
                    ImGui.SameLine();
                    UiText.Colored(new Vector4(0.70f, 0.86f, 0.98f, 1f), Localization.T("text.panel.timeline.ghost"));
                }
                ImGui.SameLine();
                ImGui.SetCursorPosX(LeftColumnWidth - 26f * UiTheme.Scale);
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.Button($"+##grp_{gk}"))
                    foreach (var path in row.GroupPaths ?? Array.Empty<string>())
                        AddKeyframeForProperty(row.Object, path, _currentFrame);
            }
            else
            {
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
                ImGui.TextUnformatted(GetRowDisplayLabel(row));
                if (showGhostIndicator)
                {
                    ImGui.SameLine();
                    UiText.Colored(new Vector4(0.70f, 0.86f, 0.98f, 1f), Localization.T("text.panel.timeline.ghost"));
                }
                ImGui.SameLine();
                ImGui.SetCursorPosX(LeftColumnWidth - 26f * UiTheme.Scale);
                ImGui.SetCursorPosY(rowY + (RowHeight - ImGui.GetFrameHeight()) * 0.5f);
                if (ImGui.Button($"+##{row.Object.ObjectId}_{row.PropertyPath}"))
                    AddKeyframeForProperty(row.Object, row.PropertyPath, _currentFrame);
            }

            if (row.Indent > 0) ImGui.Unindent(12f * UiTheme.Scale);

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
            if (!_groupExpanded.TryGetValue(gk, out bool exp) || !exp)
                return false;
        }

        // Not inside any group -> always visible.
        return true;
    }

    // ── Keyframe tracks ───────────────────────────────────────────────────────

    private void RenderKeyframeTracks()
    {
        float contentW = _maxFrames * _pixelsPerFrame * 1.5f;

        if (_displayRows.Count == 0)
        {
            ImGui.Dummy(new Vector2(contentW, 60f * UiTheme.Scale));
            return;
        }

        var   dl      = ImGui.GetWindowDrawList();
        var   wPos    = ImGui.GetWindowPos();
        var   wSize   = ImGui.GetWindowSize();
        float scrollX = ImGui.GetScrollX();

        var rows = _displayRows.ToArray();
        foreach (var row in rows)
        {
            if (!IsTrackRowVisible(row)) continue;
            if (row.PropertyPath == "__header__")
            {
                // Object header — draw a separator-like bar
                var hPos = ImGui.GetCursorScreenPos();
                dl.AddRectFilled(hPos, hPos + new Vector2(contentW, RowHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.1f, 1f)));
                ImGui.Dummy(new Vector2(contentW, RowHeight));
                
                // Handle mouse interactions on header row
                HandleTrackMouse(row, hPos, contentW, scrollX, wPos, wSize);
                continue;
            }

            var  trackPos = ImGui.GetCursorScreenPos();
            bool showGhostIndicator = ShouldShowGhostIndicator(row);

            // Track background + grid
            DrawTrackBg(dl, trackPos, wPos, wSize, scrollX, showGhostIndicator);

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
            dl.AddLine(wPos with { X = phX }, new Vector2(phX, wPos.Y + wSize.Y), 0xBB3377FF, 2f * UiTheme.Scale);

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
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.85f)), 0f, ImDrawFlags.None, 1.5f * UiTheme.Scale);
        }
    }

    private void DrawTrackBg(ImDrawListPtr dl, Vector2 trackPos,
                              Vector2 wPos, Vector2 wSize, float scrollX, bool ghostTint)
    {
        dl.PushClipRect(
            new Vector2(wPos.X, trackPos.Y),
            new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight), true);

        var bgColor = ghostTint
            ? new Vector4(0.10f, 0.15f, 0.19f, 1f)
            : new Vector4(0.12f, 0.12f, 0.12f, 1f);

        dl.AddRectFilled(
            new Vector2(wPos.X, trackPos.Y),
            new Vector2(wPos.X + wSize.X, trackPos.Y + RowHeight),
            ImGui.ColorConvertFloat4ToU32(bgColor));

        int startF = Math.Max(0, (int)(scrollX / _pixelsPerFrame) - 1);
        int endF   = Math.Min((int)(_maxFrames * 1.5f), (int)((scrollX + wSize.X) / _pixelsPerFrame) + 1);
        for (int f = startF; f <= endF; f++)
        {
            if (f % 10 != 0) continue;
            float x = trackPos.X + f * _pixelsPerFrame - scrollX;
            dl.AddLine(new Vector2(x, trackPos.Y), new Vector2(x, trackPos.Y + RowHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.22f, 0.22f, 0.6f)), UiTheme.Scale);
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
            DrawDiamond(dl, cx, cy, KeyframeDiamondSize - UiTheme.Scale, anySelected);
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
                   new Vector2(cx, cy + s), new Vector2(cx - s, cy), outline, 1.5f * UiTheme.Scale);
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
                .Find(k => MathF.Abs(k.Frame * _pixelsPerFrame - localX) <= Math.Max(KeyframeDiamondSize, 2f * _pixelsPerFrame * UiTheme.Scale));
            if (hoveredKf != null && !_isDraggingKeyframe)
                UiText.Tooltip(Localization.Format("text.panel.timeline.frame_2", hoveredKf.Frame, hoveredKf.InterpolationType));
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
                // Click on empty area → only start drag-select, do NOT create keyframe
                if (!shiftHeld)
                {
                    _selectedKeyframes.Clear();
                    _keyframeOwners.Clear();
                }
                StartDragSelect(mouse, shiftHeld);
            }
        }

        // ── Dragging keyframe ────────────────────────────────────────────────
        // Update keyframe frames in real-time during drag, keeping lists sorted for rendering
        if (_isDraggingKeyframe && _draggedKeyframe != null && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            int newFrame = Math.Max(0, (int)MathF.Round(localX / _pixelsPerFrame));
            int offset   = newFrame - _dragStartFrame;
            
            if (offset != 0)
            {
                var sortedLists = new HashSet<string>();
                foreach (var kf in _selectedKeyframes)
                {
                    if (_selectedStartFrames.TryGetValue(kf, out int sf))
                    {
                        kf.Frame = Math.Max(0, sf + offset);
                        
                        // Mark the keyframe's list for sorting
                        if (_keyframeOwners.TryGetValue(kf, out var owner))
                        {
                            string key = $"{owner.obj.ObjectId}.{owner.path}";
                            sortedLists.Add(key);
                        }
                    }
                }
                
                // Sort each affected list to keep keyframes in frame order for rendering
                foreach (var key in sortedLists)
                {
                    if (_propertyKeyframes.TryGetValue(key, out var list))
                        list.Sort((a, b) => a.Frame.CompareTo(b.Frame));
                }
            }
        }

        // ── Mouse released ───────────────────────────────────────────────────
        if (_isDraggingKeyframe && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            // Collect all owners that had keyframes moved, then handle collisions and save
            var changedOwners = new Dictionary<(SceneObject, string), List<TimelineKeyframe>>();
            
            foreach (var kf in _selectedKeyframes)
            {
                if (!_keyframeOwners.TryGetValue(kf, out var owner)) continue;
                if (!_selectedStartFrames.TryGetValue(kf, out int sf)) continue;
                
                if (kf.Frame != sf)
                {
                    var key = (owner.obj, owner.path);
                    if (!changedOwners.ContainsKey(key))
                        changedOwners[key] = new();
                    changedOwners[key].Add(kf);
                }
            }
            
            // For each changed property, remove collisions and save
            foreach (var ((obj, path), movedKfs) in changedOwners)
            {
                string key = $"{obj.ObjectId}.{path}";
                if (_propertyKeyframes.TryGetValue(key, out var list))
                {
                    // Remove collisions: if multiple keyframes now occupy the same frame,
                    // keep only the first one and remove duplicates
                    var frameGroups = movedKfs.GroupBy(k => k.Frame);
                    foreach (var group in frameGroups.Where(g => g.Count() > 1))
                    {
                        var toKeep = group.First();
                        foreach (var dup in group.Skip(1))
                            list.Remove(dup);
                    }
                    
                    SaveKeyframesToObject(obj, path);
                }
            }
            
            _isDraggingKeyframe = false;
            _draggedKeyframe    = null;
            _selectedStartFrames.Clear();
            RecalculateTimelineLength();
        }

        // ── Right-click context menu ─────────────────────────────────────────
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            if (!row.IsGroupHeader && row.PropertyPath != "__header__" && hoveredKf != null)
            {
                // Right-click on a keyframe
                _ctxKeyframe = hoveredKf;
                _ctxObject   = row.Object;
                _ctxPropPath = row.PropertyPath;
                _ctxTrackRow = row;
                if (!_selectedKeyframes.Contains(hoveredKf))
                {
                    _selectedKeyframes.Clear();
                    _keyframeOwners.Clear();
                    _selectedKeyframes.Add(hoveredKf);
                    _keyframeOwners[hoveredKf] = (row.Object, row.PropertyPath);
                }
                _openContextMenu = true;
                _contextFrame = hoveredKf.Frame;
                _activeMarker = null;
            }
            else if (row.PropertyPath == "__header__")
            {
                // Right-click on object header row
                _ctxAddKeyframeObj = row.Object;
                _ctxTrackRow = null;
                _openAddKeyframeMenu = true;
            }
            else
            {
                _contextFrame = frame;
                _ctxKeyframe = null;
                _ctxObject = row.Object;
                _ctxPropPath = row.PropertyPath;
                _ctxTrackRow = row;
                _activeMarker = null;
                _openContextMenu = true;
            }
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
        _selectedRegionStart = Math.Max(0, (int)MathF.Round((minSX - wPos.X + scrollX) / _pixelsPerFrame));
        _selectedRegionEnd = Math.Max(0, (int)MathF.Round((maxSX - wPos.X + scrollX) / _pixelsPerFrame));

        if (!shiftHeld)
        {
            _selectedKeyframes.Clear();
            _keyframeOwners.Clear();
        }

        float curY = wPos.Y - _vScrollOffset;
        var rows = _displayRows.ToArray();
        foreach (var row in rows)
        {
            if (row.PropertyPath == "__header__" || row.IsGroupHeader)
            { curY += RowHeight; continue; }

            if (!IsTrackRowVisible(row))
                continue;

            float rowMinY = curY;
            float rowMaxY = curY + RowHeight;
            curY += RowHeight;

            if (rowMaxY < minSY || rowMinY > maxSY) continue;

            foreach (var kf in from kf in GetKeyframesForProperty(row.Object, row.PropertyPath) let kfSX = wPos.X + kf.Frame * _pixelsPerFrame - scrollX where !(kfSX < minSX) && !(kfSX > maxSX) where !_selectedKeyframes.Contains(kf) select kf)
            {
                _selectedKeyframes.Add(kf);
                _keyframeOwners[kf] = (row.Object, row.PropertyPath);
            }
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void RenderContextMenu()
    {
        if (!ImGui.BeginPopup("##kf_ctx")) return;

        UiText.Disabled(_activeMarker != null ? Localization.Format("text.panel.timeline.marker", _activeMarker.Label) : Localization.T("text.common.timeline"));
        ImGui.Separator();

        bool hasSelection = _selectedKeyframes.Count > 0;
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.cut_keyframes", "Cut Keyframes"), Keybinds.Label("cut"), false, hasSelection)) CutSelectedKeyframes();
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.copy_keyframes", "Copy Keyframes"), Keybinds.Label("copy"), false, hasSelection)) CopySelectedKeyframes();
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.paste_keyframes", "Paste Keyframes"), Keybinds.Label("paste"), false, _keyframeClipboard.Count > 0)) PasteKeyframes(_contextFrame);
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.scale_selected_keyframe_speed_2", "Scale Selected Keyframe Speed..."), "", false, _selectedKeyframes.Count > 1))
        {
            _speedScaleSlowDown = true;
            _speedScaleFactor = 2f;
            _openSpeedScaleMenu = true;
        }
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.reverse_selected_keyframes", "Reverse Selected Keyframes"), "", false, _selectedKeyframes.Count > 1)) TransformSelectedFrames(reverse: true);
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.randomize_selected_keyframes", "Randomize Selected Keyframes"), "", false, _selectedKeyframes.Count > 1)) TransformSelectedFrames(reverse: false);

        if (ImGui.BeginMenu(Localization.Label("text.panel.timeline.select_keyframes", "Select Keyframes")))
        {
            int markerFrame = _activeMarker?.Frame ?? _contextFrame;
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.before_marker", "Before Marker"))) SelectKeyframes(k => k.Frame < markerFrame);
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.after_marker", "After Marker"))) SelectKeyframes(k => k.Frame > markerFrame);
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.first", "First"))) SelectExtreme(first: true);
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.last", "Last"))) SelectExtreme(first: false);
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.at_current_frame", "At Current Frame"))) SelectKeyframes(k => k.Frame == _currentFrame);
            bool hasRegion = _selectedRegionStart.HasValue && _selectedRegionEnd.HasValue;
            if (ImGui.MenuItem(Localization.Label("text.panel.timeline.in_selected_timeline_region", "In Selected Timeline Region"), "", false, hasRegion))
                SelectKeyframes(k => k.Frame >= _selectedRegionStart && k.Frame <= _selectedRegionEnd);
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.add_marker", "Add Marker")))
        {
            _markerLabel = "Marker";
            _markerColor = new Vector4(0.9f, 0.2f, 0.2f, 1f);
            _openMarkerEditor = true;
        }
        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.clear_playback_region", "Clear Playback Region"), "", false,
                _playbackRegionStart.HasValue && _playbackRegionEnd.HasValue))
        {
            _playbackRegionStart = null;
            _playbackRegionEnd = null;
        }
        if (_activeMarker != null && ImGui.MenuItem(Localization.Label("text.panel.timeline.delete_marker", "Delete Marker")))
        {
            _markers.Remove(_activeMarker);
            _activeMarker = null;
        }

        ImGui.Separator();

        if (ImGui.BeginMenu(Localization.Label("text.panel.timeline.interpolation", "Interpolation"), hasSelection))
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
                if (ImGui.MenuItem(Localization.DisplayLabel(lbl), "", active))
                {
                    foreach (var kf in _selectedKeyframes) kf.InterpolationType = val;
                    SaveSelectedOwners();
                }
            }
            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (_ctxTrackRow is { PropertyPath: not "__header__" } row)
        {
            bool rowGhost = IsTrackRowGhost(row);
            if (ImGui.MenuItem(rowGhost ? Localization.Label("text.panel.timeline.remove_track_from_ghost_timeline", "Remove Track from Ghost Timeline") : Localization.Label("text.panel.timeline.make_track_ghost_timeline", "Make Track Ghost Timeline")))
            {
                SetTrackRowGhostState(row, !rowGhost);
                PruneHiddenSelection();
            }
            ImGui.Separator();
        }

        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.delete_keyframe_s", "Delete Keyframe(s)"), Keybinds.Label("delete"), false, hasSelection))
            DeleteSelectedKeyframes();

        ImGui.EndPopup();
    }

    private IEnumerable<(TimelineKeyframe keyframe, SceneObject obj, string path)> EnumerateKeyframes()
    {
        var rows = _displayRows.ToArray();
        foreach (var row in rows.Where(r => !r.IsGroupHeader && r.PropertyPath != "__header__"))
        {
            if (!IsTrackRowVisible(row))
                continue;
            foreach (var keyframe in GetKeyframesForProperty(row.Object, row.PropertyPath))
                yield return (keyframe, row.Object, row.PropertyPath);
        }
    }

    private void SelectKeyframes(Func<TimelineKeyframe, bool> predicate)
    {
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
        foreach (var (keyframe, obj, path) in EnumerateKeyframes().Where(x => predicate(x.keyframe)))
        {
            _selectedKeyframes.Add(keyframe);
            _keyframeOwners[keyframe] = (obj, path);
        }
    }

    private void MoveIntoPlaybackRegionIfNeeded()
    {
        if (_loopPlayback && _playbackRegionStart.HasValue && _playbackRegionEnd.HasValue &&
            (_currentFrame < _playbackRegionStart.Value || _currentFrame > _playbackRegionEnd.Value))
        {
            _currentFrame = _playbackRegionStart.Value;
            _frameAccumulator = 0.0;
            ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
        }
    }

    private void SelectExtreme(bool first)
    {
        var all = EnumerateKeyframes().ToList();
        if (all.Count == 0) { SelectKeyframes(_ => false); return; }
        int frame = first ? all.Min(x => x.keyframe.Frame) : all.Max(x => x.keyframe.Frame);
        SelectKeyframes(k => k.Frame == frame);
    }

    public void CopySelectedKeyframes()
    {
        _keyframeClipboard.Clear();
        if (_selectedKeyframes.Count == 0) return;
        int origin = _selectedKeyframes.Min(k => k.Frame);
        foreach (var keyframe in _selectedKeyframes)
            if (_keyframeOwners.TryGetValue(keyframe, out var owner))
                _keyframeClipboard.Add((owner.obj, owner.path, keyframe.Frame - origin, keyframe.Value, keyframe.InterpolationType));
    }

    private void PasteKeyframes(int frame)
    {
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
        foreach (var item in _keyframeClipboard)
        {
            int target = Math.Max(0, frame + item.offset);
            var list = GetKeyframesForProperty(item.obj, item.path);
            list.RemoveAll(k => k.Frame == target);
            var pasted = new TimelineKeyframe { Frame = target, Value = item.value, InterpolationType = item.interpolation };
            list.Add(pasted);
            list.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            _selectedKeyframes.Add(pasted);
            _keyframeOwners[pasted] = (item.obj, item.path);
            SaveKeyframesToObject(item.obj, item.path);
        }
        RecalculateTimelineLength();
    }

    private void TransformSelectedFrames(bool reverse)
    {
        int min = _selectedKeyframes.Min(k => k.Frame);
        int max = _selectedKeyframes.Max(k => k.Frame);
        var random = Random.Shared;
        foreach (var keyframe in _selectedKeyframes)
            keyframe.Frame = reverse ? min + max - keyframe.Frame : random.Next(min, max + 1);
        RemoveSelectedCollisionsAndSave();
    }

    private void ScaleSelectedKeyframeSpeed(float factor, bool slowDown)
    {
        if (_selectedKeyframes.Count < 2)
            return;

        float clampedFactor = Math.Clamp(factor, 1.01f, 20f);
        float scale = slowDown ? clampedFactor : 1f / clampedFactor;
        int anchorFrame = _selectedKeyframes.Min(k => k.Frame);

        foreach (var keyframe in _selectedKeyframes)
        {
            int offset = keyframe.Frame - anchorFrame;
            int scaledOffset = (int)MathF.Round(offset * scale);
            keyframe.Frame = Math.Max(0, anchorFrame + scaledOffset);
        }

        RemoveSelectedCollisionsAndSave();
    }

    private void RemoveSelectedCollisionsAndSave()
    {
        foreach (var group in _selectedKeyframes.Where(_keyframeOwners.ContainsKey)
                     .GroupBy(k => _keyframeOwners[k]))
        {
            string key = $"{group.Key.obj.ObjectId}.{group.Key.path}";
            if (!_propertyKeyframes.TryGetValue(key, out var list)) continue;
            var selected = group.ToHashSet();
            foreach (int frame in selected.Select(k => k.Frame).Distinct())
                list.RemoveAll(k => k.Frame == frame && !selected.Contains(k));
            foreach (var duplicate in group.GroupBy(k => k.Frame).SelectMany(g => g.Skip(1)).ToList())
            {
                list.Remove(duplicate);
                _selectedKeyframes.Remove(duplicate);
                _keyframeOwners.Remove(duplicate);
            }
            list.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            SaveKeyframesToObject(group.Key.obj, group.Key.path);
        }
        RecalculateTimelineLength();
    }

    private void SaveSelectedOwners()
    {
        foreach (var owner in _selectedKeyframes.Where(_keyframeOwners.ContainsKey).Select(k => _keyframeOwners[k]).Distinct())
            SaveKeyframesToObject(owner.obj, owner.path);
    }

    private void RenderAddKeyframeMenu()
    {
        if (!ImGui.BeginPopup("##kf_add")) return;
        if (_ctxAddKeyframeObj == null) { ImGui.EndPopup(); return; }

        UiText.Disabled(Localization.T("text.panel.timeline.add_keyframe"));
        ImGui.Separator();

        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.key_changed_properties", "Key Changed Properties")))
            KeyChangedPropertiesAtCurrentFrame(CollectAllObjects(new[] { _ctxAddKeyframeObj }));
        if (ImGui.IsItemHovered())
            UiText.Tooltip(Localization.T("text.panel.timeline.key_changed_properties_hint"));
        ImGui.Separator();

        bool canRotate = !(_ctxAddKeyframeObj is LightSceneObject);
        bool canScale = !(_ctxAddKeyframeObj is CameraSceneObject) && !(_ctxAddKeyframeObj is LightSceneObject);

        if (ImGui.MenuItem(Localization.Label("text.panel.timeline.all_transforms", "All Transforms")))
        {
            // Always add position keyframes
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.x", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.y", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.z", _currentFrame);
            
            // Add rotation only if object supports it
            if (canRotate)
            {
                AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.x", _currentFrame);
                AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.y", _currentFrame);
                AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.z", _currentFrame);
            }
            
            // Add scale only if object supports it
            if (canScale)
            {
                AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.x", _currentFrame);
                AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.y", _currentFrame);
                AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.z", _currentFrame);
            }
        }

        if (ImGui.MenuItem(Localization.Label("text.common.position", "Position")))
        {
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.x", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.y", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "position.z", _currentFrame);
        }

        if (canRotate && ImGui.MenuItem(Localization.Label("text.common.rotation", "Rotation")))
        {
            AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.x", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.y", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "rotation.z", _currentFrame);
        }

        if (canScale && ImGui.MenuItem(Localization.Label("text.common.scale", "Scale")))
        {
            AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.x", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.y", _currentFrame);
            AddKeyframeForProperty(_ctxAddKeyframeObj, "scale.z", _currentFrame);
        }

        ImGui.EndPopup();
    }

    private void RenderSpeedScaleMenu()
    {
        if (_openSpeedScaleMenu)
        {
            _openSpeedScaleMenu = false;
            ImGui.OpenPopup(Localization.Label("text.panel.timeline.scale_selected_keyframe_speed", "Scale Selected Keyframe Speed"));
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(Localization.Label("text.panel.timeline.scale_selected_keyframe_speed", "Scale Selected Keyframe Speed"), ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        UiText.Disabled(Localization.T("text.panel.timeline.scale_the_selected_keyframe_timing_around_the_first_selected_frame"));
        ImGui.Separator();

        if (ImGui.RadioButton(Localization.Label("text.panel.timeline.slow_down", "Slow Down"), _speedScaleSlowDown))
            _speedScaleSlowDown = true;
        ImGui.SameLine();
        if (ImGui.RadioButton(Localization.Label("text.panel.timeline.speed_up", "Speed Up"), !_speedScaleSlowDown))
            _speedScaleSlowDown = false;

        float factor = _speedScaleFactor;
        if (ImGui.DragFloat(Localization.Label("text.panel.timeline.scale_factor", "Scale Factor"), ref factor, 0.05f, 1.01f, 20f, "%.2fx", ImGuiSliderFlags.AlwaysClamp))
            _speedScaleFactor = Math.Clamp(factor, 1.01f, 20f);

        int beforeMin = _selectedKeyframes.Count > 0 ? _selectedKeyframes.Min(k => k.Frame) : 0;
        int beforeMax = _selectedKeyframes.Count > 0 ? _selectedKeyframes.Max(k => k.Frame) : 0;
        int beforeSpan = Math.Max(0, beforeMax - beforeMin);
        float previewScale = _speedScaleSlowDown ? _speedScaleFactor : 1f / Math.Max(1.01f, _speedScaleFactor);
        int afterSpan = (int)MathF.Round(beforeSpan * previewScale);
        UiText.Disabled(Localization.Format("text.panel.timeline.span_preview_f_f", beforeSpan, afterSpan));

        bool canApply = _selectedKeyframes.Count > 1;
        if (ImGui.Button(Localization.Label("text.panel.timeline.apply", "Apply")) && canApply)
        {
            ScaleSelectedKeyframeSpeed(_speedScaleFactor, _speedScaleSlowDown);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel")))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    public void DeleteSelectedKeyframes()
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
        if (propertyPath.StartsWith("rotation.", StringComparison.Ordinal) &&
            obj.Keyframes.Keys.Any(p => p.StartsWith("mirotation.", StringComparison.Ordinal)))
            propertyPath = "mirotation." + propertyPath["rotation.".Length..];
        StoreKeyframeValue(obj, propertyPath, frame, GetRecordedKeyframeValue(obj, propertyPath, frame));
        // A nullable endpoint loses its coordinates when disabled, so key its enabled pose as well.
        if (propertyPath == "bone.end_enabled" && obj is BoneSceneObject { EndOffset: not null })
            foreach (string axis in new[] { "x", "y", "z" })
                StoreKeyframeValue(obj, $"bone.end.{axis}", frame, GetPropertyValue(obj, $"bone.end.{axis}"));
        RecalculateTimelineLength();
        RebuildDisplayRows();
    }

    private object GetRecordedKeyframeValue(SceneObject obj, string propertyPath, int frame)
    {
        object value = GetPropertyValue(obj, propertyPath);
        if (propertyPath.StartsWith("mirotation.", StringComparison.Ordinal))
        {
            vec3 reference = GetMiRotationAngles(obj);
            for (int axis = 0; axis < 3; axis++)
            {
                string path = "mirotation." + (axis == 0 ? "x" : axis == 1 ? "y" : "z");
                if (!_propertyKeyframes.ContainsKey($"{obj.ObjectId}.{path}")) LoadKeyframesFromObject(obj, path);
                if (_propertyKeyframes.TryGetValue($"{obj.ObjectId}.{path}", out var track) &&
                    InterpolateKeyframes(track, path, frame, true) is { } sampled &&
                    TryConvertKeyframeValue(sampled, out float angle)) reference[axis] = angle;
            }
            vec3 recorded = GetMiRotationAngles(obj, reference);
            value = propertyPath.EndsWith(".x", StringComparison.Ordinal) ? recorded.x
                : propertyPath.EndsWith(".y", StringComparison.Ordinal) ? recorded.y : recorded.z;
        }
        return value;
    }

    private void StoreKeyframeValue(SceneObject obj, string propertyPath, int frame, object value)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (!_propertyKeyframes.ContainsKey(key))
            LoadKeyframesFromObject(obj, propertyPath);
        if (!_propertyKeyframes.ContainsKey(key))
            _propertyKeyframes[key] = new List<TimelineKeyframe>();

        var list     = _propertyKeyframes[key];
        var existing = list.Find(k => k.Frame == frame);
        if (existing != null) { existing.Value = value; }
        else { list.Add(new TimelineKeyframe { Frame = frame, Value = value }); list.Sort((a, b) => a.Frame.CompareTo(b.Frame)); }

        SaveKeyframesToObject(obj, propertyPath);
    }

    public int KeyChangedPropertiesAtCurrentFrame() =>
        KeyChangedPropertiesAtCurrentFrame(Viewport == null
            ? Enumerable.Empty<SceneObject>() : CollectAllObjects(Viewport.SceneObjects));

    private int KeyChangedPropertiesAtCurrentFrame(IEnumerable<SceneObject> objects)
    {
        if (_isPlaying)
            TogglePlayPause();

        // Sample everything before writing keys, especially coupled MI rotation axes.
        var changed = new List<(SceneObject obj, string path, object value)>();
        foreach (var obj in objects.Distinct())
        {
            SceneObject defaults = obj is CameraSceneObject ? new CameraSceneObject()
                : obj is LightSceneObject ? new LightSceneObject()
                : obj is ParticleSpawnerSceneObject ? new ParticleSpawnerSceneObject()
                : obj is BoneSceneObject ? new BoneSceneObject() : new SceneObject();
            defaults.UsePropertyInheritance = obj.UsePropertyInheritance;
            foreach (var constraint in obj.CopyConstraints)
                defaults.CopyConstraints.Add(new CopyConstraint
                {
                    Type = constraint.Type,
                    Offset = constraint.Type == CopyConstraintType.Scale ? vec3.Ones : vec3.Zero
                });
            if (defaults is CameraSceneObject defaultCamera && obj is CameraSceneObject camera)
                foreach (var effect in camera.Effects)
                    defaultCamera.Effects.Add(new CameraEffect { Type = effect.Type });
            if (defaults is BoneSceneObject defaultBone && obj is BoneSceneObject sourceBone)
                foreach (var constraint in sourceBone.Constraints)
                    defaultBone.Constraints.Add(new BoneConstraint { Type = constraint.Type });

            bool usesMiRotation = obj.Keyframes.Keys.Any(p => p.StartsWith("mirotation.", StringComparison.Ordinal));
            foreach (string path in GetKeyframePropertyPaths(obj).Distinct())
            {
                if (path.StartsWith(usesMiRotation ? "rotation." : "mirotation.", StringComparison.Ordinal))
                    continue;
                if (!_propertyKeyframes.ContainsKey($"{obj.ObjectId}.{path}"))
                    LoadKeyframesFromObject(obj, path);
                // Include edits back to defaults when an existing track would overwrite them.
                object baseline = _propertyKeyframes.TryGetValue($"{obj.ObjectId}.{path}", out var track) && track.Count > 0
                    ? InterpolateKeyframes(track, path, _currentFrame, true) ?? GetPropertyValue(defaults, path)
                    : GetPropertyValue(defaults, path);
                object value = GetRecordedKeyframeValue(obj, path, _currentFrame);
                bool equal;
                if (TryConvertKeyframeValue(value, out float current) && TryConvertKeyframeValue(baseline, out float initial))
                {
                    // Degree/radian round trips lose precision on multi-turn rotations.
                    float tolerance = path.StartsWith("rotation.", StringComparison.Ordinal)
                        ? MathF.Max(0.00001f, MathF.Max(MathF.Abs(current), MathF.Abs(initial)) * 0.0000005f)
                        : 0.00001f;
                    equal = MathF.Abs(current - initial) <= tolerance;
                }
                else
                    equal = Equals(value, baseline is JsonElement json && json.ValueKind == JsonValueKind.String ? json.GetString() : baseline);
                if (!equal)
                    changed.Add((obj, path, value));
            }
        }

        foreach (var (obj, path, value) in changed)
            StoreKeyframeValue(obj, path, _currentFrame, value);
        if (changed.Count > 0)
        {
            RecalculateTimelineLength();
            RebuildDisplayRows();
            ProjectManager.Instance.SetDirty(true);
        }
        return changed.Count;
    }

    public void RemoveKeyframeForProperty(SceneObject obj, string propertyPath, int frame)
    {
        string key = $"{obj.ObjectId}.{propertyPath}";
        if (_propertyKeyframes.TryGetValue(key, out var list))
        {
            list.RemoveAll(k => k.Frame == frame);
            SaveKeyframesToObject(obj, propertyPath);
            RebuildDisplayRows();
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
        foreach (var obj in objects)
        {
            if (obj == null || obj.Keyframes.Count == 0) continue;
            foreach (var path in GetKeyframePropertyPaths(obj))
                if (obj.Keyframes.TryGetValue(path, out var track) && track.Count > 0)
                    LoadKeyframesFromObject(obj, path);

            if (obj is CameraSceneObject)
                foreach (var path in obj.Keyframes.Keys.Where(k => k.StartsWith("camera.effect.", StringComparison.Ordinal)))
                    if (obj.Keyframes[path].Count > 0)
                        LoadKeyframesFromObject(obj, path);
        }
        RecalculateTimelineLength();
    }

    private static IEnumerable<string> GetKeyframePropertyPaths(SceneObject obj)
    {
        string[] standardPaths =
        {
            "visible", "material.alpha", "material.texture",
            "position.x", "position.y", "position.z",
            "rotation.x", "rotation.y", "rotation.z",
            "mirotation.x", "mirotation.y", "mirotation.z",
            "scale.x",    "scale.y",    "scale.z",
        };
        string[] itemPaths =
        {
            "item.slot", "item.custom_slot",
        };
        string[] lightPaths =
        {
            "light.energy", "light.range", "light.indirect_energy", "light.specular",
            "light.color.r", "light.color.g", "light.color.b",
            "light.spot_angle", "light.spot_blend",
        };
        string[] cameraPaths =
        {
            "camera.active", "camera.fov",
        };
        string[] particleScalarPaths =
        {
            "particle.emitting", "particle.one_shot", "particle.amount", "particle.spawn_rate",
            "particle.lifetime_min", "particle.lifetime_max", "particle.simulation_speed",
            "particle.linear_damping", "particle.angular_damping", "particle.emission_shape",
            "particle.directional_emission", "particle.top_level_particles", "particle.spread", "particle.speed_min", "particle.speed_max",
            "particle.start_scale_min", "particle.start_scale_max", "particle.end_scale_min", "particle.end_scale_max",
        };
        string[] particleVectorPaths =
        {
            "particle.spawn_extents.x", "particle.spawn_extents.y", "particle.spawn_extents.z",
            "particle.velocity_min.x", "particle.velocity_min.y", "particle.velocity_min.z",
            "particle.velocity_max.x", "particle.velocity_max.y", "particle.velocity_max.z",
            "particle.gravity.x", "particle.gravity.y", "particle.gravity.z",
            "particle.direction.x", "particle.direction.y", "particle.direction.z",
            "particle.rotation_min.x", "particle.rotation_min.y", "particle.rotation_min.z",
            "particle.rotation_max.x", "particle.rotation_max.y", "particle.rotation_max.z",
            "particle.angular_velocity_min.x", "particle.angular_velocity_min.y", "particle.angular_velocity_min.z",
            "particle.angular_velocity_max.x", "particle.angular_velocity_max.y", "particle.angular_velocity_max.z",
        };

        IEnumerable<string> paths = obj is LightSceneObject   ? standardPaths.Concat(lightPaths)
                                  : obj is CameraSceneObject  ? standardPaths.Concat(cameraPaths)
                                  : standardPaths;
        paths = paths.Concat(MaterialPropertyPaths);
        paths = paths.Concat(obj.CopyConstraints.SelectMany((_, index) => GetCopyConstraintPropertyPaths(index)));
        if (obj is MiBoneSceneObject)
            paths = paths.Concat(new[] { "bend.x", "bend.y", "bend.z" });
        if (obj is BoneSceneObject bone)
            paths = paths.Concat(new[] { "bone.end_enabled", "bone.end.x", "bone.end.y", "bone.end.z" })
                .Concat(bone.Constraints.SelectMany((constraint, index) =>
                    constraint.Type == BoneConstraintType.InverseKinematics
                        ? GetIkPropertyPaths(bone, index) : Array.Empty<string>()));
        if (obj is CameraSceneObject cameraObj)
            paths = paths.Concat(GetCameraEffectPropertyPaths(cameraObj));
        if (obj is ParticleSpawnerSceneObject)
            paths = paths.Concat(particleScalarPaths).Concat(particleVectorPaths);
        if (obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            paths = paths.Concat(itemPaths);
        if (string.Equals(obj.ObjectType, "Text Mesh", StringComparison.OrdinalIgnoreCase))
            paths = paths.Concat(TextChannels.Select(channel => channel.Path));
        foreach (var path in paths)
            yield return path;

        // Shape keys depend on the object's mesh/morph-target layout.
        var shapeKeyMeshes = obj.GetMeshInstancesRecursively();
        for (int m = 0; m < shapeKeyMeshes.Count; m++)
        {
            if (!shapeKeyMeshes[m].HasShapeKeys) continue;
            for (int k = 0; k < shapeKeyMeshes[m].ShapeKeys.Count; k++)
            {
                yield return $"shapekey.{m}.{k}";
            }
        }
    }

    private static IEnumerable<string> GetCameraEffectPropertyPaths(CameraSceneObject cam)
    {
        for (int i = 0; i < cam.Effects.Count; i++)
        {
            if (cam.Effects[i].Type == CameraEffectType.DepthOfField)
            {
                foreach (var control in DepthOfFieldSettings.Controls)
                    yield return $"camera.effect.{i}.depth_of_field.{control.Key}";
                continue;
            }
            if (cam.Effects[i].Type == CameraEffectType.Bloom)
            {
                foreach (var control in BloomSettings.Controls)
                    yield return $"camera.effect.{i}.bloom.{control.Key}";
                continue;
            }
            if (cam.Effects[i].Type == CameraEffectType.FilmGrain)
            {
                string grainPath = $"camera.effect.{i}.film_grain";
                yield return $"{grainPath}.strength";
                yield return $"{grainPath}.saturation";
                yield return $"{grainPath}.size";
                continue;
            }
            string basePath = $"camera.effect.{i}.shake";
            yield return $"{basePath}.mode";
            yield return $"{basePath}.trauma";
            yield return $"{basePath}.strength.x";
            yield return $"{basePath}.strength.y";
            yield return $"{basePath}.strength.z";
            yield return $"{basePath}.speed.x";
            yield return $"{basePath}.speed.y";
            yield return $"{basePath}.speed.z";
            yield return $"{basePath}.offset.x";
            yield return $"{basePath}.offset.y";
            yield return $"{basePath}.offset.z";
        }
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
        var rotatedObjects = new HashSet<string>();
        // Enable the optional endpoint before applying its animated components.
        foreach (var kvp in _propertyKeyframes.OrderBy(pair => pair.Key.EndsWith(".bone.end_enabled", StringComparison.Ordinal) ? 0 : 1))
        {
            var keyframes = kvp.Value;
            if (keyframes.Count == 0) continue;

            int dotIdx = kvp.Key.IndexOf('.');
            if (dotIdx < 0) continue;
            string objectId     = kvp.Key[..dotIdx];
            string propertyPath = kvp.Key[(dotIdx + 1)..];

            var target = FindObjectById(objectId);
            if (target == null) continue;

            if (propertyPath.StartsWith("mirotation.", StringComparison.Ordinal))
            {
                if (!rotatedObjects.Add(objectId)) continue;
                vec3 angles = GetMiRotationAngles(target);
                for (int axis = 0; axis < 3; axis++)
                {
                    string path = "mirotation." + (axis == 0 ? "x" : axis == 1 ? "y" : "z");
                    if (_propertyKeyframes.TryGetValue($"{objectId}.{path}", out var track) &&
                        InterpolateKeyframes(track, path, _currentFrame, holdFirstKeyframeBeforeStart) is { } raw &&
                        TryConvertKeyframeValue(raw, out float angle))
                        angles[axis] = angle;
                }
                mat4 rotation = MineImatorLoader.ConvertMiRotationMatrix([angles.x, angles.y, angles.z]);
                if (target is MiBoneSceneObject bone)
                    bone.SetOffsetRotationMatrix(rotation);
                else
                    target.SetLocalRotationMatrix(rotation, MineImatorLoader.MatrixToEulerRzRyRx(rotation));
                continue;
            }

            object? value = InterpolateKeyframes(keyframes, propertyPath, _currentFrame, holdFirstKeyframeBeforeStart);
            if (value != null)
                SetPropertyValue(target, propertyPath, value);
            // null means "before first keyframe" — leave the object at its default state.
        }
    }

    /// <summary>
    /// Returns the interpolated value for the property at <paramref name="frame"/>,
    /// or <c>null</c> if the frame is before the first keyframe (meaning the object
    /// should keep its default/current value rather than being driven by animation).
    /// </summary>
    private object? InterpolateKeyframes(List<TimelineKeyframe> keyframes, string path, int frame, bool holdFirstKeyframeBeforeStart)
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
                return path is "text.font" or "material.texture" || IsIkReferencePath(path) ? next.Value : TryConvertKeyframeValue(next.Value, out float nextBeforeStart) ? nextBeforeStart : null;
            return null;
        }

        // At or after the last keyframe, or exactly on a keyframe.
        if (path is "text.font" or "material.texture" || IsIkReferencePath(path)) return prev.Value;
        if (next == null || prev.Frame == frame)
            return TryConvertKeyframeValue(prev.Value, out float prevDirect) ? prevDirect : null;

        // Between two keyframes — interpolate.
        // Discrete state-like properties and "instant" interpolation use the
        // previous keyframe's value with no blending.
        if (path == "visible" || path == "camera.active" || path == "material.pbr_enabled" || path == "item.slot" || path == "item.custom_slot" ||
            path == "text.horizontal_alignment" || path == "text.vertical_alignment" ||
            path == "text.antialiasing" || path == "text.outline_enabled" ||
            path == "particle.emitting" || path == "particle.one_shot" || path == "particle.amount" ||
            path == "particle.emission_shape" || path == "particle.directional_emission" || path == "particle.top_level_particles" ||
            path.EndsWith(".mode", StringComparison.Ordinal) || path == "bone.end_enabled" ||
            (path.StartsWith("constraint.", StringComparison.Ordinal) &&
                !(path.EndsWith(".pole_angle", StringComparison.Ordinal) || path.EndsWith(".influence", StringComparison.Ordinal))) ||
            (path.StartsWith("copy_constraint.", StringComparison.Ordinal) &&
                !path.Contains(".offset.", StringComparison.Ordinal) && !path.EndsWith(".blend", StringComparison.Ordinal)) ||
            prev.InterpolationType == "instant")
            return TryConvertKeyframeValue(prev.Value, out float prevInstant) ? prevInstant : null;

        if (!TryConvertKeyframeValue(prev.Value, out float pv) || !TryConvertKeyframeValue(next.Value, out float nv))
            return null;

        float t  = (frame - prev.Frame) / (float)(next.Frame - prev.Frame);
        float progress = ApplyInterpolation(t, prev.InterpolationType);
        if (path.StartsWith("material.blend.", StringComparison.Ordinal) ||
            path.StartsWith("material.mix.", StringComparison.Ordinal) ||
            path.StartsWith("material.glow.", StringComparison.Ordinal) ||
            path.StartsWith("material.subsurface_color.", StringComparison.Ordinal))
            progress = Math.Clamp(progress, 0f, 1f);
        return pv + (nv - pv) * progress;
    }

    private static bool TryConvertKeyframeValue(object? rawValue, out float value)
    {
        switch (rawValue)
        {
            case null:
                value = 0f;
                return true;
            case float f:
                value = f;
                return true;
            case double d:
                value = (float)d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case bool b:
                value = b ? 1f : 0f;
                return true;
            case JsonElement json:
                return TryConvertFromJsonElement(json, out value);
            default:
                if (rawValue is IConvertible conv)
                {
                    try
                    {
                        value = Convert.ToSingle(conv, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch
                    {
                        // Fall through and report unsupported value.
                    }
                }

                value = 0f;
                return false;
        }
    }

    private static bool TryConvertFromJsonElement(JsonElement json, out float value)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Number:
                if (json.TryGetSingle(out float n))
                {
                    value = n;
                    return true;
                }
                break;
            case JsonValueKind.True:
                value = 1f;
                return true;
            case JsonValueKind.False:
                value = 0f;
                return true;
            case JsonValueKind.String:
                if (float.TryParse(json.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                {
                    value = s;
                    return true;
                }
                break;
        }

        value = 0f;
        return false;
    }

    private static float ApplyInterpolation(float t, string type)
    {
        if (t <= 0f || t >= 1f) return Math.Clamp(t, 0f, 1f);
        type = type switch
        {
            "ease-in-quadratic" => "easeinquad",
            "ease-out-quadratic" => "easeoutquad",
            "ease-in-out-quadratic" => "easeinoutquad",
            _ => type
        };
        if (type == "instant") return 0f;
        if (type.StartsWith("bezier:", StringComparison.Ordinal))
        {
            string[] parts = type[7..].Split(',');
            float[] controls = new float[4];
            if (parts.Length != 4) return t;
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out controls[i]) ||
                    !float.IsFinite(controls[i])) return t;
            float lower = 0f, upper = 1f, u = 0.5f;
            // Match Mine-imator's X search, then evaluate the corresponding Y.
            for (int i = 0; i <= 10; i++)
            {
                u = (lower + upper) * 0.5f;
                float x = Bezier(u, controls[0], controls[2]);
                if (MathF.Abs(t - x) < 0.001f) break;
                if (t > x) lower = u; else upper = u;
            }
            return Bezier(u, controls[1], controls[3]);
        }

        bool inOut = type.StartsWith("easeinout", StringComparison.Ordinal);
        bool easeOut = type.StartsWith("easeout", StringComparison.Ordinal);
        if (!inOut && !easeOut && !type.StartsWith("easein", StringComparison.Ordinal)) return t;
        string curve = type[(inOut ? 9 : easeOut ? 7 : 6)..];
        if (inOut)
            return t < 0.5f ? 0.5f * EaseIn(t * 2f, curve, true)
                : 1f - 0.5f * EaseIn(2f - t * 2f, curve, true);
        return easeOut ? 1f - EaseIn(1f - t, curve, false) : EaseIn(t, curve, false);

        static float Bezier(float u, float a, float b) =>
            3f * (1f - u) * (1f - u) * u * a + 3f * (1f - u) * u * u * b + u * u * u;

        static float EaseIn(float v, string curve, bool inOut)
        {
            if (v <= 0f || v >= 1f) return v;
            float s = 1.70158f * (inOut ? 1.525f : 1f);
            float period = inOut ? 0.45f : 0.3f;
            return curve switch
            {
                "quad" => v * v,
                "cubic" => v * v * v,
                "quart" => MathF.Pow(v, 4f),
                "quint" => MathF.Pow(v, 5f),
                "sine" => 1f - MathF.Cos(v * MathF.PI * 0.5f),
                "expo" => MathF.Pow(2f, 10f * (v - 1f)),
                "circ" => 1f - MathF.Sqrt(Math.Max(0f, 1f - v * v)),
                "elastic" => -MathF.Pow(2f, 10f * (v - 1f)) *
                    MathF.Sin((v - 1f - period * 0.25f) * (2f * MathF.PI) / period),
                "back" => v * v * ((s + 1f) * v - s),
                "bounce" => 1f - BounceOut(1f - v),
                _ => v
            };
        }

        static float BounceOut(float v)
        {
            if (v < 1f / 2.75f) return 7.5625f * v * v;
            if (v < 2f / 2.75f) { v -= 1.5f / 2.75f; return 7.5625f * v * v + 0.75f; }
            if (v < 2.5f / 2.75f) { v -= 2.25f / 2.75f; return 7.5625f * v * v + 0.9375f; }
            v -= 2.625f / 2.75f;
            return 7.5625f * v * v + 0.984375f;
        }
    }

    // ── Get / set property values ─────────────────────────────────────────────

    private static bool IsIkReferencePath(string path) =>
        (path.StartsWith("constraint.", StringComparison.Ordinal) &&
            (path.EndsWith(".ik.target", StringComparison.Ordinal) || path.EndsWith(".ik.pole", StringComparison.Ordinal))) ||
        (path.StartsWith("copy_constraint.", StringComparison.Ordinal) && path.EndsWith(".target", StringComparison.Ordinal));

    private static readonly (string Channel, string Label)[] CopyConstraintChannels =
    [
        ("enabled", "Enabled"), ("target", "Target Object"),
        ("offset.x", "Offset X"), ("offset.y", "Offset Y"), ("offset.z", "Offset Z"),
        ("axis.x", "Axis X"), ("axis.y", "Axis Y"), ("axis.z", "Axis Z"),
        ("blend", "Blend"), ("bent_half", "Bent Half"), ("rotate", "Rotate")
    ];

    internal static string GetCopyConstraintLabel(CopyConstraintType type) => type switch
    {
        CopyConstraintType.Position => "Copy Position",
        CopyConstraintType.Rotation => "Copy Rotation",
        _ => "Copy Scale"
    };

    private static IEnumerable<string> GetCopyConstraintPropertyPaths(int index) =>
        CopyConstraintChannels.Select(channel => $"copy_constraint.{index}.{channel.Channel}");

    internal static bool TryGetCopyConstraint(SceneObject obj, string path, out CopyConstraint constraint, out string channel)
    {
        constraint = null!;
        channel = "";
        if (!path.StartsWith("copy_constraint.", StringComparison.Ordinal)) return false;
        string[] parts = path.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
            index < 0 || index >= obj.CopyConstraints.Count) return false;
        string parsedChannel = string.Join('.', parts.Skip(2));
        if (!CopyConstraintChannels.Any(entry => entry.Channel == parsedChannel)) return false;
        channel = parsedChannel;
        constraint = obj.CopyConstraints[index];
        return true;
    }

    private static IEnumerable<string> GetIkPropertyPaths(BoneSceneObject bone, int index)
    {
        string prefix = $"constraint.{index}.ik";
        foreach (string channel in new[] { "enabled", "target", "pole", "lock_orientation", "chain_length", "influence", "pole_angle", "iterations" })
            yield return $"{prefix}.{channel}";
        // Ancestor depth keeps tracks stable when participation entries are created or the chain length changes.
        int depth = 0;
        for (SceneObject? current = bone; current is BoneSceneObject chainBone && depth < 256; current = current.Parent, depth++)
        {
            yield return $"{prefix}.bone.{depth}.rotate";
            if (chainBone is MiBoneSceneObject miBone && miBone.BendParameters.HasValue)
                yield return $"{prefix}.bone.{depth}.bend";
        }
    }

    private static BoneSceneObject? GetIkParticipationBone(SceneObject obj, string channel)
    {
        string[] parts = channel.Split('.');
        if (parts.Length != 3 || parts[0] != "bone" || parts[2] is not ("rotate" or "bend") ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int depth) || depth < 0 || depth >= 256)
            return null;
        SceneObject? current = obj;
        while (depth-- > 0 && current is BoneSceneObject) current = current.Parent;
        return current as BoneSceneObject;
    }

    private static bool TryGetIkSettings(SceneObject obj, string path,
        out InverseKinematicsSettings settings, out string channel)
    {
        settings = null;
        channel = "";
        if (obj is not BoneSceneObject bone || !path.StartsWith("constraint.", StringComparison.Ordinal)) return false;
        string[] parts = path.Split('.');
        if (parts.Length < 4 || parts[2] != "ik" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
            index < 0 || index >= bone.Constraints.Count ||
            bone.Constraints[index].Type != BoneConstraintType.InverseKinematics)
            return false;
        settings = bone.Constraints[index].InverseKinematics;
        channel = string.Join('.', parts.Skip(3));
        return settings != null;
    }

    internal object GetPropertyValue(SceneObject obj, string path)
    {
        if (TryGetCopyConstraint(obj, path, out var copy, out string copyChannel))
            return copyChannel switch
            {
                "enabled" => copy.Enabled ? 1f : 0f,
                "target" => copy.TargetObjectId,
                "offset.x" => copy.Offset.x, "offset.y" => copy.Offset.y, "offset.z" => copy.Offset.z,
                "axis.x" => copy.AxisX ? 1f : 0f, "axis.y" => copy.AxisY ? 1f : 0f, "axis.z" => copy.AxisZ ? 1f : 0f,
                "blend" => copy.Blend, "bent_half" => copy.BentHalf ? 1f : 0f, "rotate" => copy.Rotate ? 1f : 0f,
                _ => 0f
            };
        if (path == "visible") return obj.ObjectVisible ? 1f : 0f;
        if (path == "material.texture") return obj.AlbedoTexturePath;
        if (path == "text.font") return obj.TextMeshFontPath;
        if (obj is BoneSceneObject endpointBone)
        {
            if (path == "bone.end_enabled") return endpointBone.EndOffset.HasValue ? 1f : 0f;
            if (path.StartsWith("bone.end.", StringComparison.Ordinal))
            {
                vec3 end = endpointBone.EndOffset ?? new vec3(0f, 1f, 0f);
                return path switch { "bone.end.x" => end.x, "bone.end.y" => end.y, "bone.end.z" => end.z, _ => 0f };
            }
        }
        if (TryGetIkSettings(obj, path, out var ik, out string ikChannel))
        {
            if (ikChannel.StartsWith("bone.", StringComparison.Ordinal))
            {
                var chainBone = GetIkParticipationBone(obj, ikChannel);
                var participation = chainBone == null ? null : ik.GetBoneSettings(chainBone.ObjectId);
                return (ikChannel.EndsWith(".rotate", StringComparison.Ordinal)
                    ? participation?.AllowRotation ?? true : participation?.AllowBend ?? true) ? 1f : 0f;
            }
            return ikChannel switch
            {
                "enabled" => ((BoneSceneObject)obj).Constraints[int.Parse(path.Split('.')[1], CultureInfo.InvariantCulture)].Enabled ? 1f : 0f,
                "target" => ik.TargetObjectId,
                "pole" => ik.PoleObjectId,
                "lock_orientation" => ik.LockOrientation ? 1f : 0f,
                "chain_length" => (float)ik.ChainLength,
                "iterations" => (float)ik.Iterations,
                "pole_angle" => ik.PoleAngle,
                "influence" => ik.Influence,
                _ => 0f,
            };
        }

        var parts = path.Split('.');
        if (parts.Length == 2)
        {
            string prop = parts[0], comp = parts[1];
            switch (prop)
            {
                case "mirotation":
                {
                    vec3 angles = GetMiRotationAngles(obj);
                    return comp switch { "x" => angles.x, "y" => angles.y, "z" => angles.z, _ => 0f };
                }
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
                case "bend":
                {
                    if (obj is MiBoneSceneObject mb)
                    {
                        vec3 angle = mb.GetEditableBendAngle();
                        return comp switch { "x" => (object)angle.x, "y" => angle.y, "z" => angle.z, _ => 0f };
                    }
                    break;
                }
                case "material":
                    return comp switch
                    {
                        "alpha" => obj.MaterialSettings?.AlbedoColor.w ?? 1f,
                        "metallic" => obj.MaterialSettings?.Metallic ?? 0f,
                        "roughness" => obj.MaterialSettings?.Roughness ?? (obj.UsePropertyInheritance ? 1f : 0.5f),
                        "pbr_enabled" => (object)(obj.MaterialSettings?.PbrEnabled ?? false),
                        "normal_strength" => obj.MaterialSettings?.NormalStrength ?? 1f,
                        "occlusion_strength" => obj.MaterialSettings?.OcclusionStrength ?? 1f,
                        "height_scale" => obj.MaterialSettings?.HeightScale ?? 0f,
                        "emissive" => obj.MaterialSettings?.EmissionEnabled == true ? obj.MaterialSettings.EmissionEnergy : 0f,
                        "subsurface" => obj.MaterialSettings?.Subsurface ?? 0f,
                        "mix_percent" => obj.MaterialSettings?.MixColor.w ?? 0f,
                        _ => 0f,
                    };
                case "text":
                    return comp switch
                    {
                        "horizontal_alignment" => obj.TextMeshHorizontalAlignment,
                        "vertical_alignment" => obj.TextMeshVerticalAlignment,
                        "antialiasing" => obj.TextMeshAntialiasing ? 1f : 0f,
                        "font_size" => obj.TextMeshFontSize,
                        "outline_enabled" => obj.TextMeshOutlineEnabled ? 1f : 0f,
                        "outline_thickness" => obj.TextMeshOutlineThickness,
                        _ => 0f
                    };
                case "item":
                    return comp switch
                    {
                        "slot" => obj.TemporaryItemSheetColumnIndex,
                        "custom_slot" => obj.TemporaryItemSheetRowIndex,
                        _ => 0f,
                    };
                case "light":
                    if (obj is LightSceneObject lo)
                        return comp switch
                        {
                            "energy"          => (object)lo.LightEnergy,
                            "range"           => lo.LightRange,
                            "indirect_energy" => lo.LightIndirectEnergy,
                            "specular"        => lo.LightSpecular,
                            "spot_angle"      => lo.LightSpotAngle,
                            "spot_blend"      => lo.LightSpotBlend,
                            _                 => 0f,
                        };
                    break;
                case "camera":
                    if (obj is CameraSceneObject cam)
                        return comp switch
                        {
                            "active" => cam.Active ? 1f : 0f,
                            "fov" => cam.Fov,
                            _ => 0f,
                        };
                    break;
                case "particle":
                    if (obj is ParticleSpawnerSceneObject particle)
                    {
                        return comp switch
                        {
                            "emitting" => particle.Emitting ? 1f : 0f,
                            "one_shot" => particle.OneShot ? 1f : 0f,
                            "amount" => particle.Amount,
                            "spawn_rate" => particle.SpawnRate,
                            "lifetime_min" => particle.LifetimeMin,
                            "lifetime_max" => particle.LifetimeMax,
                            "simulation_speed" => particle.SimulationSpeed,
                            "linear_damping" => particle.LinearDamping,
                            "angular_damping" => particle.AngularDamping,
                            "emission_shape" => (int)particle.EmissionShape,
                            "directional_emission" => particle.UseDirectionalEmission ? 1f : 0f,
                            "top_level_particles" => particle.TopLevelParticles ? 1f : 0f,
                            "spread" => particle.SpreadDegrees,
                            "speed_min" => particle.InitialSpeedMin,
                            "speed_max" => particle.InitialSpeedMax,
                            "start_scale_min" => particle.StartScaleMin,
                            "start_scale_max" => particle.StartScaleMax,
                            "end_scale_min" => particle.EndScaleMin,
                            "end_scale_max" => particle.EndScaleMax,
                            _ => 0f,
                        };
                    }
                    break;
            }
        }
        else if (parts.Length == 3 && parts[0] == "material")
        {
            var material = obj.MaterialSettings;
            vec4 color = parts[1] switch
            {
                "blend" => material?.BlendColor ?? vec4.Ones,
                "mix" => material?.MixColor ?? vec4.Zero,
                "glow" => material?.EmissionColor ?? (obj.UsePropertyInheritance ? vec4.Ones : new vec4(0f, 0f, 0f, 1f)),
                "subsurface_color" => material?.SubsurfaceColor ?? vec4.Ones,
                "subsurface_radius" => material == null ? vec4.Ones : new vec4(
                    material.SubsurfaceRadius.x, material.SubsurfaceRadius.y, material.SubsurfaceRadius.z, 1f),
                _ => vec4.Zero,
            };
            return parts[2] switch { "r" => color.x, "g" => color.y, "b" => color.z, _ => 0f };
        }
        else if (parts.Length == 3 && parts[0] == "text" && parts[1] == "outline")
        {
            return parts[2] switch { "r" => obj.TextMeshOutlineColor.x, "g" => obj.TextMeshOutlineColor.y,
                "b" => obj.TextMeshOutlineColor.z, "a" => obj.TextMeshOutlineColor.w, _ => 0f };
        }
        else if (parts.Length == 3 && parts[0] == "light" && parts[1] == "color" && obj is LightSceneObject lco)
        {
            return parts[2] switch { "r" => (object)lco.LightColor.x, "g" => lco.LightColor.y, "b" => lco.LightColor.z, _ => 0f };
        }
        else if (parts.Length == 3 && parts[0] == "particle" && obj is ParticleSpawnerSceneObject particle)
        {
            vec3 vec = parts[1] switch
            {
                "spawn_extents" => particle.SpawnBoxExtents,
                "velocity_min" => particle.InitialVelocityMin,
                "velocity_max" => particle.InitialVelocityMax,
                "gravity" => particle.Gravity,
                "direction" => particle.Direction,
                "rotation_min" => particle.InitialRotationMinDegrees,
                "rotation_max" => particle.InitialRotationMaxDegrees,
                "angular_velocity_min" => particle.AngularVelocityMinDegrees,
                "angular_velocity_max" => particle.AngularVelocityMaxDegrees,
                _ => vec3.Zero,
            };

            return parts[2] switch
            {
                "x" => vec.x,
                "y" => vec.y,
                "z" => vec.z,
                _ => 0f,
            };
        }
        else if (parts.Length == 3 && parts[0] == "shapekey")
        {
            return GetShapeKeyWeight(obj, parts[1], parts[2]) ?? 0f;
        }
        else if ((parts.Length == 5 || parts.Length == 6) &&
                 parts[0] == "camera" && parts[1] == "effect" &&
                 obj is CameraSceneObject camFx &&
                 int.TryParse(parts[2], out int effectIndex) &&
                 effectIndex >= 0 && effectIndex < camFx.Effects.Count)
        {
            var effect = camFx.Effects[effectIndex];
            if (effect.Type == CameraEffectType.DepthOfField && parts.Length == 5 && parts[3] == "depth_of_field")
                return effect.DepthOfField.GetValue(parts[4]);
            if (effect.Type == CameraEffectType.Bloom && parts[3] == "bloom")
                return effect.Bloom.GetValue(string.Join('.', parts.Skip(4)));
            if (effect.Type == CameraEffectType.CameraShake && parts[3] == "shake")
            {
                if (parts.Length == 5 && parts[4] == "mode")
                    return (float)effect.Shake.Mode;

                if (parts.Length == 5 && parts[4] == "trauma")
                    return effect.Shake.Trauma;

                if (parts.Length == 6)
                {
                    vec3 valueVec = parts[4] switch
                    {
                        "strength" => effect.Shake.Strength,
                        "speed" => effect.Shake.Speed,
                        "offset" => effect.Shake.Offset,
                        _ => vec3.Zero
                    };

                    return parts[5] switch
                    {
                        "x" => valueVec.x,
                        "y" => valueVec.y,
                        "z" => valueVec.z,
                        _ => 0f
                    };
                }
            }
            else if (effect.Type == CameraEffectType.FilmGrain && parts.Length == 5 && parts[3] == "film_grain")
            {
                return parts[4] switch
                {
                    "strength" => effect.FilmGrain.Strength,
                    "saturation" => effect.FilmGrain.Saturation,
                    "size" => effect.FilmGrain.Size,
                    _ => 0f
                };
            }
        }

        return 0f;
    }

    /// <summary>
    /// Resolves a "shapekey.&lt;meshIndex&gt;.&lt;keyIndex&gt;" property path to the
    /// current weight of that shape key, or <c>null</c> if either index is out of
    /// range (e.g. the model was swapped since the keyframe was recorded).
    /// </summary>
    private static float? GetShapeKeyWeight(SceneObject obj, string meshIndexStr, string keyIndexStr)
    {
        if (!int.TryParse(meshIndexStr, out int meshIndex) || !int.TryParse(keyIndexStr, out int keyIndex))
            return null;

        var meshes = obj.GetMeshInstancesRecursively();
        if (meshIndex < 0 || meshIndex >= meshes.Count) return null;

        var shapeKeys = meshes[meshIndex].ShapeKeys;
        if (keyIndex < 0 || keyIndex >= shapeKeys.Count) return null;

        return shapeKeys[keyIndex].Weight;
    }

    private static vec3 GetMiRotationAngles(SceneObject obj, vec3? reference = null)
    {
        vec3 euler = obj is MiBoneSceneObject bone ? bone.OffsetRotation : obj.LocalRotation;
        mat4 rotation = mat4.RotateZ(euler.z) * mat4.RotateY(euler.y) * mat4.RotateX(euler.x);
        float x = MathF.Asin(Math.Clamp(-rotation.m21, -1f, 1f));
        bool singular = MathF.Abs(rotation.m21) >= 1f - 1e-6f;
        vec3 angles = new vec3(x, singular ? MathF.Atan2(-rotation.m02, rotation.m00) : MathF.Atan2(rotation.m20, rotation.m22),
            singular ? 0f : MathF.Atan2(rotation.m01, rotation.m11)) * (180f / MathF.PI);
        if (reference is not { } previous) return angles;

        mat4 expected = MineImatorLoader.ConvertMiRotationMatrix([previous.x, previous.y, previous.z]);
        bool unchanged = true;
        for (int column = 0; column < 3; column++)
            for (int row = 0; row < 3; row++)
                unchanged &= MathF.Abs(expected[column, row] - rotation[column, row]) < 1e-5f;
        if (unchanged) return previous;

        vec3 alternative = new vec3(180f - angles.x, angles.y + 180f, angles.z + 180f);
        for (int axis = 0; axis < 3; axis++)
        {
            angles[axis] += 360f * MathF.Round((previous[axis] - angles[axis]) / 360f);
            alternative[axis] += 360f * MathF.Round((previous[axis] - alternative[axis]) / 360f);
        }
        return (angles - previous).LengthSqr <= (alternative - previous).LengthSqr ? angles : alternative;
    }

    internal void SetPropertyValue(SceneObject obj, string path, object rawValue)
    {
        if (TryGetCopyConstraint(obj, path, out var copy, out string copyChannel))
        {
            if (copyChannel == "target")
                copy.TargetObjectId = rawValue is JsonElement reference && reference.ValueKind == JsonValueKind.String
                    ? reference.GetString() ?? "" : Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
            else if (TryConvertKeyframeValue(rawValue, out float copyValue) && float.IsFinite(copyValue))
            {
                switch (copyChannel)
                {
                    case "enabled": copy.Enabled = copyValue >= 0.5f; break;
                    case "axis.x": copy.AxisX = copyValue >= 0.5f; break;
                    case "axis.y": copy.AxisY = copyValue >= 0.5f; break;
                    case "axis.z": copy.AxisZ = copyValue >= 0.5f; break;
                    case "blend": copy.Blend = Math.Clamp(copyValue, 0f, 1f); break;
                    case "bent_half": copy.BentHalf = copyValue >= 0.5f; break;
                    case "rotate": copy.Rotate = copyValue >= 0.5f; break;
                    case "offset.x": case "offset.y": case "offset.z":
                        var offset = copy.Offset;
                        offset[copyChannel[^1] == 'x' ? 0 : copyChannel[^1] == 'y' ? 1 : 2] = copyValue;
                        copy.Offset = offset;
                        break;
                }
            }
            return;
        }
        if (TryGetIkSettings(obj, path, out var ik, out string ikChannel))
        {
            if (ikChannel is "target" or "pole")
            {
                string reference = rawValue is JsonElement jsonReference && jsonReference.ValueKind == JsonValueKind.String
                    ? jsonReference.GetString() ?? "" : Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
                if (ikChannel == "target") ik.TargetObjectId = reference;
                else ik.PoleObjectId = reference;
            }
            else if (TryConvertKeyframeValue(rawValue, out float ikValue) && float.IsFinite(ikValue))
            {
                if (ikChannel == "pole_angle") ik.PoleAngle = ikValue;
                else if (ikChannel == "influence") ik.Influence = Math.Clamp(ikValue, 0f, 1f);
                else if (ikChannel == "enabled") ((BoneSceneObject)obj).Constraints[int.Parse(path.Split('.')[1], CultureInfo.InvariantCulture)].Enabled = ikValue >= 0.5f;
                else if (ikChannel == "lock_orientation") ik.LockOrientation = ikValue >= 0.5f;
                else if (ikChannel == "chain_length") ik.ChainLength = (int)Math.Clamp(MathF.Round(ikValue), 1f, 256f);
                else if (ikChannel == "iterations") ik.Iterations = (int)Math.Clamp(MathF.Round(ikValue), 1f, 256f);
                else if (GetIkParticipationBone(obj, ikChannel) is { } chainBone)
                {
                    var participation = ik.GetBoneSettings(chainBone.ObjectId);
                    if (participation == null)
                    {
                        participation = new IkBoneSettings { BoneObjectId = chainBone.ObjectId };
                        ik.Bones.Add(participation);
                    }
                    if (ikChannel.EndsWith(".rotate", StringComparison.Ordinal)) participation.AllowRotation = ikValue >= 0.5f;
                    else participation.AllowBend = ikValue >= 0.5f;
                }
            }
            return;
        }
        if (path == "material.texture")
        {
            string texture = rawValue is JsonElement jsonTexture && jsonTexture.ValueKind == JsonValueKind.String
                ? jsonTexture.GetString() ?? "" : Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
            texture = ProjectManager.Instance.ResolveAssetPath(texture);
            if (obj.AlbedoTexturePath != texture)
            {
                obj.AlbedoTexturePath = texture;
                obj.RefreshInheritedMaterials();
            }
            return;
        }
        if (path == "text.font")
        {
            string font = rawValue is JsonElement json && json.ValueKind == JsonValueKind.String
                ? json.GetString() ?? "" : Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
            if (obj.TextMeshFontPath != font) { obj.TextMeshFontPath = font; TextMeshFactory.Rebuild(obj); }
            return;
        }
        if (!TryConvertKeyframeValue(rawValue, out float value)) return;
        if (obj is BoneSceneObject endpointBone && float.IsFinite(value))
        {
            if (path == "bone.end_enabled")
            {
                endpointBone.EndOffset = value >= 0.5f ? endpointBone.EndOffset ?? new vec3(0f, 1f, 0f) : null;
                return;
            }
            if (path.StartsWith("bone.end.", StringComparison.Ordinal))
            {
                bool animatedToggle = endpointBone.Keyframes.ContainsKey("bone.end_enabled");
                if (!animatedToggle || endpointBone.EndOffset.HasValue)
                {
                    vec3 end = endpointBone.EndOffset ?? new vec3(0f, 1f, 0f);
                    if (path == "bone.end.x") end.x = value;
                    else if (path == "bone.end.y") end.y = value;
                    else if (path == "bone.end.z") end.z = value;
                    endpointBone.EndOffset = end;
                }
                return;
            }
        }
        if (path == "visible") { obj.ObjectVisible = value >= 0.5f; return; }

        var parts = path.Split('.');
        if (MaterialPropertyPaths.Contains(path))
        {
            // Detach inherited settings before changing a local animation channel.
            if (obj.MaterialSettings == null && obj.UsePropertyInheritance)
                obj.MaterialSettings = new MaterialSettings
                {
                    Roughness = 1f,
                    EmissionColor = vec4.Ones,
                    EmissionEnergy = 0f,
                };
            obj.SetExplicitMaterialSettings();
            var material = obj.MaterialSettings!;
            if (parts.Length == 2)
            {
                switch (parts[1])
                {
                    case "alpha": material.AlbedoColor.w = Math.Clamp(value, 0f, 1f); break;
                    case "metallic": material.Metallic = Math.Clamp(value, 0f, 1f); break;
                    case "roughness": material.Roughness = Math.Clamp(value, 0f, 1f); break;
                    case "pbr_enabled": material.PbrEnabled = value >= 0.5f; break;
                    case "normal_strength": material.NormalStrength = Math.Clamp(value, 0f, 2f); break;
                    case "occlusion_strength": material.OcclusionStrength = Math.Clamp(value, 0f, 1f); break;
                    case "height_scale": material.HeightScale = Math.Clamp(value, 0f, 0.1f); break;
                    case "emissive":
                        material.EmissionEnergy = Math.Max(value, 0f);
                        material.EmissionEnabled = material.EmissionEnergy != 0f;
                        break;
                    case "subsurface": material.Subsurface = Math.Max(value, 0f); break;
                    case "mix_percent": material.MixColor.w = Math.Clamp(value, 0f, 1f); break;
                }
            }
            else
            {
                int component = parts[2] == "r" ? 0 : parts[2] == "g" ? 1 : 2;
                if (parts[1] == "subsurface_radius")
                {
                    var radius = material.SubsurfaceRadius;
                    radius[component] = Math.Max(value, 0f);
                    material.SubsurfaceRadius = radius;
                }
                else
                {
                    var color = parts[1] switch
                    {
                        "blend" => material.BlendColor,
                        "mix" => material.MixColor,
                        "glow" => material.EmissionColor,
                        _ => material.SubsurfaceColor,
                    };
                    color[component] = Math.Clamp(value, 0f, 1f);
                    switch (parts[1])
                    {
                        case "blend": material.BlendColor = color; break;
                        case "mix": material.MixColor = color; break;
                        case "glow": material.EmissionColor = color; break;
                        case "subsurface_color": material.SubsurfaceColor = color; break;
                    }
                }
            }
            obj.PropagateMaterialSettingsToChildren();
            return;
        }
        switch (parts.Length)
        {
            case 2:
            {
                string prop = parts[0], comp = parts[1];
                switch (prop)
                {
                    case "text":
                    {
                        bool changed = false;
                        switch (comp)
                        {
                            case "horizontal_alignment": changed = obj.TextMeshHorizontalAlignment != (int)value; obj.TextMeshHorizontalAlignment = Math.Clamp((int)value, 0, 2); break;
                            case "vertical_alignment": changed = obj.TextMeshVerticalAlignment != (int)value; obj.TextMeshVerticalAlignment = Math.Clamp((int)value, 0, 2); break;
                            case "antialiasing": changed = obj.TextMeshAntialiasing != (value >= .5f); obj.TextMeshAntialiasing = value >= .5f; break;
                            case "font_size": changed = MathF.Abs(obj.TextMeshFontSize - value) > .001f; obj.TextMeshFontSize = Math.Clamp(value, 1f, 512f); break;
                            case "outline_enabled": changed = obj.TextMeshOutlineEnabled != (value >= .5f); obj.TextMeshOutlineEnabled = value >= .5f; break;
                            case "outline_thickness": changed = MathF.Abs(obj.TextMeshOutlineThickness - value) > .001f; obj.TextMeshOutlineThickness = Math.Clamp(value, 0f, 64f); break;
                        }
                        if (changed) TextMeshFactory.Rebuild(obj);
                        break;
                    }
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

                    case "bend":
                        if (obj is MiBoneSceneObject mbB)
                        {
                            var angle = mbB.GetEditableBendAngle();
                            if (comp == "x") angle.x = value; else if (comp == "y") angle.y = value; else if (comp == "z") angle.z = value;
                            mbB.SetEditableBendAngle(angle);
                        }
                        break;

                    case "item":
                        if (Viewport?.SpawnMenu == null || obj.TemporaryItemSheetColumns <= 0 || obj.TemporaryItemSheetRows <= 0)
                            break;

                        int columnIndex = obj.TemporaryItemSheetColumnIndex;
                        int rowIndex = obj.TemporaryItemSheetRowIndex;
                        if (comp == "slot")
                            columnIndex = Math.Clamp((int)MathF.Round(value), 0, obj.TemporaryItemSheetColumns - 1);
                        else if (comp == "custom_slot")
                            rowIndex = Math.Clamp((int)MathF.Round(value), 0, obj.TemporaryItemSheetRows - 1);
                        else
                            break;

                        Viewport.SpawnMenu.ApplyTemporaryItemSheetSlotToSpawnedObject(obj, columnIndex, rowIndex);
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
                                case "spot_angle":      lo.LightSpotAngle      = value; break;
                                case "spot_blend":      lo.LightSpotBlend      = value; break;
                            }
                        }
                        break;

                    case "camera":
                        if (obj is CameraSceneObject camSet)
                        {
                            if (comp == "active")
                            {
                                bool shouldBeActive = value >= 0.5f;
                                if (shouldBeActive)
                                    CameraSceneObject.SetActiveExclusive(camSet);
                                else
                                    camSet.Active = false;
                            }
                            else if (comp == "fov" && float.IsFinite(value))
                                camSet.Fov = Math.Clamp(value, 1f, 179f);
                        }
                        break;

                    case "particle":
                        if (obj is ParticleSpawnerSceneObject particle)
                        {
                            switch (comp)
                            {
                                case "emitting": particle.Emitting = value >= 0.5f; break;
                                case "one_shot": particle.OneShot = value >= 0.5f; break;
                                case "amount": particle.Amount = Math.Clamp((int)MathF.Round(value), 1, 10000); break;
                                case "spawn_rate": particle.SpawnRate = MathF.Max(0f, value); break;
                                case "lifetime_min": particle.LifetimeMin = MathF.Max(0.01f, value); break;
                                case "lifetime_max": particle.LifetimeMax = MathF.Max(0.01f, value); break;
                                case "simulation_speed": particle.SimulationSpeed = MathF.Max(0f, value); break;
                                case "linear_damping": particle.LinearDamping = MathF.Max(0f, value); break;
                                case "angular_damping": particle.AngularDamping = MathF.Max(0f, value); break;
                                case "emission_shape":
                                    particle.EmissionShape = (int)MathF.Round(value) == 1
                                        ? ParticleEmissionShape.Sphere
                                        : ParticleEmissionShape.Box;
                                    break;
                                case "directional_emission": particle.UseDirectionalEmission = value >= 0.5f; break;
                                case "top_level_particles": particle.TopLevelParticles = value >= 0.5f; break;
                                case "spread": particle.SpreadDegrees = Math.Clamp(value, 0f, 180f); break;
                                case "speed_min": particle.InitialSpeedMin = MathF.Max(0f, value); break;
                                case "speed_max": particle.InitialSpeedMax = MathF.Max(0f, value); break;
                                case "start_scale_min": particle.StartScaleMin = MathF.Max(0.001f, value); break;
                                case "start_scale_max": particle.StartScaleMax = MathF.Max(0.001f, value); break;
                                case "end_scale_min": particle.EndScaleMin = MathF.Max(0.001f, value); break;
                                case "end_scale_max": particle.EndScaleMax = MathF.Max(0.001f, value); break;
                            }
                        }
                        break;
                }

                break;
            }
            case 3 when parts[0] == "light" && parts[1] == "color" && obj is LightSceneObject lco:
            {
                var c = lco.LightColor;
                switch (parts[2]) { case "r": c.x = value; break; case "g": c.y = value; break; case "b": c.z = value; break; }
                lco.LightColor = c;
                break;
            }
            case 3 when parts[0] == "particle" && obj is ParticleSpawnerSceneObject particle:
            {
                vec3 v = parts[1] switch
                {
                    "spawn_extents" => particle.SpawnBoxExtents,
                    "velocity_min" => particle.InitialVelocityMin,
                    "velocity_max" => particle.InitialVelocityMax,
                    "gravity" => particle.Gravity,
                    "direction" => particle.Direction,
                    "rotation_min" => particle.InitialRotationMinDegrees,
                    "rotation_max" => particle.InitialRotationMaxDegrees,
                    "angular_velocity_min" => particle.AngularVelocityMinDegrees,
                    "angular_velocity_max" => particle.AngularVelocityMaxDegrees,
                    _ => vec3.Zero,
                };

                if (parts[2] == "x") v.x = value;
                else if (parts[2] == "y") v.y = value;
                else if (parts[2] == "z") v.z = value;

                switch (parts[1])
                {
                    case "spawn_extents": particle.SpawnBoxExtents = new vec3(MathF.Max(0f, v.x), MathF.Max(0f, v.y), MathF.Max(0f, v.z)); break;
                    case "velocity_min": particle.InitialVelocityMin = v; break;
                    case "velocity_max": particle.InitialVelocityMax = v; break;
                    case "gravity": particle.Gravity = v; break;
                    case "direction": particle.Direction = v; break;
                    case "rotation_min": particle.InitialRotationMinDegrees = v; break;
                    case "rotation_max": particle.InitialRotationMaxDegrees = v; break;
                    case "angular_velocity_min": particle.AngularVelocityMinDegrees = v; break;
                    case "angular_velocity_max": particle.AngularVelocityMaxDegrees = v; break;
                }
                break;
            }
            case 3 when parts[0] == "shapekey":
                SetShapeKeyWeight(obj, parts[1], parts[2], value);
                break;
            case 5 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "shake" && parts[4] == "mode" && obj is CameraSceneObject camMode:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camMode.Effects.Count)
                    break;

                var effect = camMode.Effects[effectIndex];
                if (effect.Type != CameraEffectType.CameraShake)
                    break;

                int modeValue = Math.Clamp((int)MathF.Round(value), 0, CameraShakeModeOptionsCount - 1);
                effect.Shake.Mode = (CameraShakeMode)modeValue;
                break;
            }
            case 5 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "shake" && parts[4] == "trauma" && obj is CameraSceneObject camTrauma:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camTrauma.Effects.Count)
                    break;

                var effect = camTrauma.Effects[effectIndex];
                if (effect.Type != CameraEffectType.CameraShake)
                    break;

                effect.Shake.Trauma = value;
                break;
            }
            case 6 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "shake" && obj is CameraSceneObject camVec:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camVec.Effects.Count)
                    break;

                var effect = camVec.Effects[effectIndex];
                if (effect.Type != CameraEffectType.CameraShake)
                    break;

                vec3 targetVec = vec3.Zero;
                switch (parts[4])
                {
                    case "strength":
                        targetVec = effect.Shake.Strength;
                        break;
                    case "speed":
                        targetVec = effect.Shake.Speed;
                        break;
                    case "offset":
                        targetVec = effect.Shake.Offset;
                        break;
                    default:
                        break;
                }

                if (parts[4] != "strength" && parts[4] != "speed" && parts[4] != "offset")
                    break;

                switch (parts[5])
                {
                    case "x": targetVec.x = value; break;
                    case "y": targetVec.y = value; break;
                    case "z": targetVec.z = value; break;
                    default:
                        break;
                }

                switch (parts[4])
                {
                    case "strength":
                        effect.Shake.Strength = targetVec;
                        break;
                    case "speed":
                        effect.Shake.Speed = targetVec;
                        break;
                    case "offset":
                        effect.Shake.Offset = targetVec;
                        break;
                }
                break;
            }
            case 3 when parts[0] == "text" && parts[1] == "outline":
            {
                vec4 c = obj.TextMeshOutlineColor;
                switch (parts[2]) { case "r": c.x = value; break; case "g": c.y = value; break;
                    case "b": c.z = value; break; case "a": c.w = value; break; }
                if (c != obj.TextMeshOutlineColor) { obj.TextMeshOutlineColor = c; TextMeshFactory.Rebuild(obj); }
                break;
            }
            case 5 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "depth_of_field" && obj is CameraSceneObject camDof:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camDof.Effects.Count)
                    break;
                var effect = camDof.Effects[effectIndex];
                if (effect.Type == CameraEffectType.DepthOfField)
                    effect.DepthOfField.SetValue(parts[4], value);
                break;
            }
            case 5 or 6 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "bloom" && obj is CameraSceneObject camBloom:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camBloom.Effects.Count)
                    break;
                var effect = camBloom.Effects[effectIndex];
                if (effect.Type == CameraEffectType.Bloom)
                    effect.Bloom.SetValue(string.Join('.', parts.Skip(4)), value);
                break;
            }
            case 5 when parts[0] == "camera" && parts[1] == "effect" && parts[3] == "film_grain" && obj is CameraSceneObject camGrain:
            {
                if (!int.TryParse(parts[2], out int effectIndex) || effectIndex < 0 || effectIndex >= camGrain.Effects.Count)
                    break;
                var effect = camGrain.Effects[effectIndex];
                if (effect.Type != CameraEffectType.FilmGrain) break;
                switch (parts[4])
                {
                    case "strength": effect.FilmGrain.Strength = value; break;
                    case "saturation": effect.FilmGrain.Saturation = value; break;
                    case "size": effect.FilmGrain.Size = value; break;
                }
                break;
            }
        }
    }

    private const int CameraShakeModeOptionsCount = 3;

    /// <summary>
    /// Applies <paramref name="value"/> to the shape key identified by a
    /// "shapekey.&lt;meshIndex&gt;.&lt;keyIndex&gt;" property path. No-op if either
    /// index is out of range.
    /// </summary>
    private static void SetShapeKeyWeight(SceneObject obj, string meshIndexStr, string keyIndexStr, float value)
    {
        if (!int.TryParse(meshIndexStr, out int meshIndex) || !int.TryParse(keyIndexStr, out int keyIndex))
            return;

        var meshes = obj.GetMeshInstancesRecursively();
        if (meshIndex < 0 || meshIndex >= meshes.Count) return;

        meshes[meshIndex].SetShapeKeyWeight(keyIndex, value);
    }

    // ── Selection changed → rebuild display rows ──────────────────────────────

    public void RefreshFromScene()
    {
        _displayRows.Clear();
        _propertyKeyframes.Clear();
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();

        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected != null)
            foreach (var obj in selected)
                AddObjectRows(obj);

        if (Viewport != null)
            LoadKeyframesForAllObjects(CollectAllObjects(Viewport.SceneObjects));
        
        // Rebuild display rows to only show properties with keyframes
        RebuildDisplayRows();
    }

    /// <summary>
    /// Rebuilds _displayRows to only include properties that have keyframes.
    /// Called whenever selection changes or keyframes are added/removed.
    /// </summary>
    private void RebuildDisplayRows()
    {
        var selectedObjects = SelectionManager.Instance?.SelectedObjects ?? new List<SceneObject>();
        var newRows = new List<TimelineProperty>();

        foreach (var obj in selectedObjects)
        {
            // Add header
            newRows.Add(new TimelineProperty { Object = obj, Label = $"── {obj.Name} ──", PropertyPath = "__header__" });

            // Collect properties that have keyframes
            var propsWithKeyframes = new HashSet<string>();
            foreach (var kvp in _propertyKeyframes)
            {
                if (kvp.Value.Count == 0) continue;
                int dotIdx = kvp.Key.IndexOf('.');
                if (dotIdx < 0) continue;
                string objectId = kvp.Key[..dotIdx];
                if (objectId != obj.ObjectId) continue;
                string propPath = kvp.Key[(dotIdx + 1)..];
                propsWithKeyframes.Add(propPath);
            }

            // Define all possible properties in order
            var standardProps = new[]
            {
                (label: "Visible",         path: "visible", parent: "", indent: 0),
                (label: "Alpha",           path: "material.alpha", parent: "", indent: 0),
                (label: "Texture",         path: "material.texture", parent: "", indent: 0),
                (label: "Position",        path: "position", parent: "", indent: 0),
                (label: "X",               path: "position.x", parent: "position", indent: 1),
                (label: "Y",               path: "position.y", parent: "position", indent: 1),
                (label: "Z",               path: "position.z", parent: "position", indent: 1),
                (label: "Rotation",        path: "rotation", parent: "", indent: 0),
                (label: "X",               path: "rotation.x", parent: "rotation", indent: 1),
                (label: "Y",               path: "rotation.y", parent: "rotation", indent: 1),
                (label: "Z",               path: "rotation.z", parent: "rotation", indent: 1),
                (label: "Rotation",        path: "mirotation", parent: "", indent: 0),
                (label: "X",               path: "mirotation.x", parent: "mirotation", indent: 1),
                (label: "Y",               path: "mirotation.y", parent: "mirotation", indent: 1),
                (label: "Z",               path: "mirotation.z", parent: "mirotation", indent: 1),
                (label: "Scale",           path: "scale", parent: "", indent: 0),
                (label: "X",               path: "scale.x", parent: "scale", indent: 1),
                (label: "Y",               path: "scale.y", parent: "scale", indent: 1),
                (label: "Z",               path: "scale.z", parent: "scale", indent: 1),
            };
            var bendProps = obj is MiBoneSceneObject miBone && miBone.BendParameters is { } bend && (bend.AxisX || bend.AxisY || bend.AxisZ)
                ? new[]
                {
                    (label: "Bend", path: "bend", parent: "", indent: 0),
                    (label: "X", path: "bend.x", parent: "bend", indent: 1),
                    (label: "Y", path: "bend.y", parent: "bend", indent: 1),
                    (label: "Z", path: "bend.z", parent: "bend", indent: 1),
                }
                : Array.Empty<(string label, string path, string parent, int indent)>();
            var itemProps = new[]
            {
                (label: "Item Slot",       path: "item.slot", parent: "", indent: 0),
                (label: "Custom Item Slot", path: "item.custom_slot", parent: "", indent: 0),
            };

            // Add rows for properties with keyframes
            foreach (var (label, path, parent, indent) in standardProps)
            {
                if (path == "position" || path == "rotation" || path == "mirotation" || path == "scale")
                {
                    // This is a group header
                    string[] groupPaths = path switch
                    {
                        "position" => new[] { "position.x", "position.y", "position.z" },
                        "rotation" => new[] { "rotation.x", "rotation.y", "rotation.z" },
                        "mirotation" => new[] { "mirotation.x", "mirotation.y", "mirotation.z" },
                        "scale" => new[] { "scale.x", "scale.y", "scale.z" },
                        _ => Array.Empty<string>()
                    };

                    // Group paths are organizational rows and never receive
                    // keyframes themselves. Show the header whenever at least
                    // one of its component tracks has keyframes.
                    if (groupPaths.Any(propsWithKeyframes.Contains))
                        newRows.Add(MakeGroup(obj, label, groupPaths));
                }
                else if (propsWithKeyframes.Contains(path))
                {
                    newRows.Add(MakeSingle(obj, label, path, indent));
                }
            }

            foreach (var (label, path) in MaterialScalarChannels)
                if (propsWithKeyframes.Contains(path))
                    newRows.Add(MakeSingle(obj, label, path));

            if (string.Equals(obj.ObjectType, "Text Mesh", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (label, path) in TextChannels.Where(c => !c.Path.StartsWith("text.outline.", StringComparison.Ordinal)))
                    if (propsWithKeyframes.Contains(path))
                        newRows.Add(MakeSingle(obj, label, path));
                string[] outlinePaths = ["text.outline.r", "text.outline.g", "text.outline.b", "text.outline.a"];
                if (outlinePaths.Any(propsWithKeyframes.Contains))
                {
                    newRows.Add(MakeGroup(obj, "Outline Color", outlinePaths, "text.outline"));
                    foreach (var (label, path) in TextChannels.Where(c => outlinePaths.Contains(c.Path)))
                        if (propsWithKeyframes.Contains(path))
                            newRows.Add(MakeSingle(obj, label, path, 1));
                }
            }

            foreach (var (label, basePath) in MaterialVectorChannels)
            {
                string[] paths = { $"{basePath}.r", $"{basePath}.g", $"{basePath}.b" };
                if (!paths.Any(propsWithKeyframes.Contains)) continue;
                newRows.Add(MakeGroup(obj, label, paths, basePath));
                for (int i = 0; i < paths.Length; i++)
                    if (propsWithKeyframes.Contains(paths[i]))
                        newRows.Add(MakeSingle(obj, i == 0 ? "R" : i == 1 ? "G" : "B", paths[i], 1));
            }

            foreach (var (label, path, parent, indent) in bendProps)
            {
                if (path == "bend")
                {
                    string[] groupPaths = ["bend.x", "bend.y", "bend.z"];
                    if (groupPaths.Any(propsWithKeyframes.Contains))
                        newRows.Add(MakeGroup(obj, label, groupPaths, path));
                }
                else if (propsWithKeyframes.Contains(path))
                {
                    newRows.Add(MakeSingle(obj, label, path, indent));
                }
            }

            if (obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            {
                foreach (var (label, path, parent, indent) in itemProps)
                {
                    if (propsWithKeyframes.Contains(path))
                        newRows.Add(MakeSingle(obj, label, path, indent));
                }
            }

            for (int i = 0; i < obj.CopyConstraints.Count; i++)
            {
                string[] paths = GetCopyConstraintPropertyPaths(i).ToArray();
                if (!paths.Any(propsWithKeyframes.Contains)) continue;
                newRows.Add(MakeGroup(obj, GetCopyConstraintLabel(obj.CopyConstraints[i].Type), paths, $"copy_constraint.{i}"));
                foreach (var (channel, label) in CopyConstraintChannels)
                {
                    string path = $"copy_constraint.{i}.{channel}";
                    if (propsWithKeyframes.Contains(path)) newRows.Add(MakeSingle(obj, label, path, 1));
                }
            }

            if (obj is BoneSceneObject constraintBone)
            {
                for (int i = 0; i < constraintBone.Constraints.Count; i++)
                {
                    if (constraintBone.Constraints[i].Type != BoneConstraintType.InverseKinematics) continue;
                    string prefix = $"constraint.{i}.ik";
                    string[] paths = GetIkPropertyPaths(constraintBone, i).ToArray();
                    if (!paths.Any(propsWithKeyframes.Contains)) continue;
                    newRows.Add(MakeGroup(obj, "Inverse Kinematics", paths, $"constraint.{i}"));
                    for (int channel = 0; channel < paths.Length; channel++)
                        if (propsWithKeyframes.Contains(paths[channel]))
                        {
                            string label = paths[channel][(prefix.Length + 1)..] switch
                            {
                                "enabled" => "Enabled", "target" => "Target Object", "pole" => "Pole Object",
                                "lock_orientation" => "Lock Orientation", "chain_length" => "Chain Length",
                                "pole_angle" => "Pole Angle", "influence" => "Influence", "iterations" => "Iterations",
                                _ => "Chain Participation"
                            };
                            newRows.Add(MakeSingle(obj, label, paths[channel], 1));
                        }
                }
                if (propsWithKeyframes.Contains("bone.end_enabled"))
                    newRows.Add(MakeSingle(obj, "Custom Bone End", "bone.end_enabled", 0));
                string[] endPaths = ["bone.end.x", "bone.end.y", "bone.end.z"];
                if (endPaths.Any(propsWithKeyframes.Contains))
                {
                    newRows.Add(MakeGroup(obj, "End Offset", endPaths, "bone.end"));
                    for (int axis = 0; axis < endPaths.Length; axis++)
                        if (propsWithKeyframes.Contains(endPaths[axis]))
                            newRows.Add(MakeSingle(obj, axis == 0 ? "X" : axis == 1 ? "Y" : "Z", endPaths[axis], 1));
                }
            }

            // Light properties
            if (obj is LightSceneObject)
            {
                var lightProps = new[]
                {
                    (label: "Light Energy",    path: "light.energy", parent: "", indent: 0),
                    (label: "Light Range",     path: "light.range", parent: "", indent: 0),
                    (label: "Indirect Energy", path: "light.indirect_energy", parent: "", indent: 0),
                    (label: "Specular",        path: "light.specular", parent: "", indent: 0),
                    (label: "Spot Angle",      path: "light.spot_angle", parent: "", indent: 0),
                    (label: "Spot Blend",      path: "light.spot_blend", parent: "", indent: 0),
                    (label: "Light Color",     path: "light.color", parent: "", indent: 0),
                    (label: "R",               path: "light.color.r", parent: "light.color", indent: 1),
                    (label: "G",               path: "light.color.g", parent: "light.color", indent: 1),
                    (label: "B",               path: "light.color.b", parent: "light.color", indent: 1),
                };

                foreach (var (label, path, parent, indent) in lightProps)
                {
                    if (path == "light.color")
                    {
                        string[] groupPaths = ["light.color.r", "light.color.g", "light.color.b"];
                        if (groupPaths.Any(propsWithKeyframes.Contains))
                            newRows.Add(MakeGroup(obj, label, groupPaths));
                    }
                    else if (propsWithKeyframes.Contains(path))
                    {
                        newRows.Add(MakeSingle(obj, label, path, indent));
                    }
                }
            }

            // Camera properties
            if (obj is CameraSceneObject)
            {
                var cameraProps = new[]
                {
                    (label: "Active", path: "camera.active", parent: "", indent: 0),
                    (label: "Field of View", path: "camera.fov", parent: "", indent: 0),
                };

                foreach (var (label, path, parent, indent) in cameraProps)
                {
                    bool hasKeyframes = propsWithKeyframes.Contains(path);
                    if (!hasKeyframes) continue;

                    newRows.Add(MakeSingle(obj, label, path, indent));
                }

                if (obj is CameraSceneObject cameraObj)
                {
                    for (int i = 0; i < cameraObj.Effects.Count; i++)
                    {
                        var effect = cameraObj.Effects[i];
                        if (effect.Type == CameraEffectType.DepthOfField)
                        {
                            string baseDofPath = $"camera.effect.{i}.depth_of_field";
                            string[] paths = DepthOfFieldSettings.Controls.Select(c => $"{baseDofPath}.{c.Key}").ToArray();
                            if (!paths.Any(propsWithKeyframes.Contains)) continue;
                            newRows.Add(MakeGroup(obj, $"Effect {i + 1}: Depth of Field", paths, $"camera.effect.{i}"));
                            foreach (var control in DepthOfFieldSettings.Controls)
                                if (propsWithKeyframes.Contains($"{baseDofPath}.{control.Key}"))
                                    newRows.Add(MakeSingle(obj, control.Label, $"{baseDofPath}.{control.Key}", 1));
                            continue;
                        }
                        if (effect.Type == CameraEffectType.Bloom)
                        {
                            string bloomPath = $"camera.effect.{i}.bloom";
                            string[] paths = BloomSettings.Controls.Select(c => $"{bloomPath}.{c.Key}").ToArray();
                            if (!paths.Any(propsWithKeyframes.Contains)) continue;
                            newRows.Add(MakeGroup(obj, $"Effect {i + 1}: Bloom", paths, $"camera.effect.{i}"));
                            foreach (var control in BloomSettings.Controls)
                                if (propsWithKeyframes.Contains($"{bloomPath}.{control.Key}"))
                                    newRows.Add(MakeSingle(obj, control.Label, $"{bloomPath}.{control.Key}", 1));
                            continue;
                        }
                        if (effect.Type == CameraEffectType.FilmGrain)
                        {
                            string grainPath = $"camera.effect.{i}.film_grain";
                            string[] paths = { $"{grainPath}.strength", $"{grainPath}.saturation", $"{grainPath}.size" };
                            if (!paths.Any(propsWithKeyframes.Contains)) continue;
                            newRows.Add(MakeGroup(obj, $"Effect {i + 1}: Film Grain", paths, $"camera.effect.{i}"));
                            if (propsWithKeyframes.Contains(paths[0])) newRows.Add(MakeSingle(obj, "Strength", paths[0], 1));
                            if (propsWithKeyframes.Contains(paths[1])) newRows.Add(MakeSingle(obj, "Saturation", paths[1], 1));
                            if (propsWithKeyframes.Contains(paths[2])) newRows.Add(MakeSingle(obj, "Size", paths[2], 1));
                            continue;
                        }
                        if (effect.Type != CameraEffectType.CameraShake)
                            continue;

                        string basePath = $"camera.effect.{i}.shake";
                        string modePath = $"{basePath}.mode";
                        string traumaPath = $"{basePath}.trauma";
                        string strengthGroupPath = $"camera.effect.{i}.strength";
                        string speedGroupPath = $"camera.effect.{i}.speed";
                        string offsetGroupPath = $"camera.effect.{i}.offset";
                        string[] strengthPaths = { $"{basePath}.strength.x", $"{basePath}.strength.y", $"{basePath}.strength.z" };
                        string[] speedPaths = { $"{basePath}.speed.x", $"{basePath}.speed.y", $"{basePath}.speed.z" };
                        string[] offsetPaths = { $"{basePath}.offset.x", $"{basePath}.offset.y", $"{basePath}.offset.z" };
                        string[] allPaths = [modePath, traumaPath, strengthGroupPath, ..strengthPaths, speedGroupPath, ..speedPaths, offsetGroupPath, ..offsetPaths];

                        if (!allPaths.Any(propsWithKeyframes.Contains))
                            continue;

                        newRows.Add(MakeGroup(obj,
                            $"Effect {i + 1}: Camera Shake",
                            allPaths,
                            $"camera.effect.{i}"));

                        if (propsWithKeyframes.Contains(modePath))
                            newRows.Add(MakeSingle(obj, "Mode", modePath, 1));

                        if (propsWithKeyframes.Contains(traumaPath))
                            newRows.Add(MakeSingle(obj, "Trauma", traumaPath, 1));

                        if (strengthPaths.Any(propsWithKeyframes.Contains))
                        {
                            newRows.Add(MakeGroup(obj, "Strength", strengthPaths, strengthGroupPath, 1));
                            newRows.Add(MakeSingle(obj, "X", strengthPaths[0], 2));
                            newRows.Add(MakeSingle(obj, "Y", strengthPaths[1], 2));
                            newRows.Add(MakeSingle(obj, "Z", strengthPaths[2], 2));
                        }

                        if (speedPaths.Any(propsWithKeyframes.Contains))
                        {
                            newRows.Add(MakeGroup(obj, "Speed", speedPaths, speedGroupPath, 1));
                            newRows.Add(MakeSingle(obj, "X", speedPaths[0], 2));
                            newRows.Add(MakeSingle(obj, "Y", speedPaths[1], 2));
                            newRows.Add(MakeSingle(obj, "Z", speedPaths[2], 2));
                        }

                        if (offsetPaths.Any(propsWithKeyframes.Contains))
                        {
                            newRows.Add(MakeGroup(obj, "Offset", offsetPaths, offsetGroupPath, 1));
                            newRows.Add(MakeSingle(obj, "X", offsetPaths[0], 2));
                            newRows.Add(MakeSingle(obj, "Y", offsetPaths[1], 2));
                            newRows.Add(MakeSingle(obj, "Z", offsetPaths[2], 2));
                        }
                    }
                }
            }

            if (obj is ParticleSpawnerSceneObject)
            {
                var particleProps = new[]
                {
                    (label: "Emitting", path: "particle.emitting", parent: "", indent: 0),
                    (label: "One Shot", path: "particle.one_shot", parent: "", indent: 0),
                    (label: "Amount", path: "particle.amount", parent: "", indent: 0),
                    (label: "Spawn Rate", path: "particle.spawn_rate", parent: "", indent: 0),
                    (label: "Lifetime Min", path: "particle.lifetime_min", parent: "", indent: 0),
                    (label: "Lifetime Max", path: "particle.lifetime_max", parent: "", indent: 0),
                    (label: "Simulation Speed", path: "particle.simulation_speed", parent: "", indent: 0),
                    (label: "Linear Damping", path: "particle.linear_damping", parent: "", indent: 0),
                    (label: "Angular Damping", path: "particle.angular_damping", parent: "", indent: 0),
                    (label: "Emission Shape", path: "particle.emission_shape", parent: "", indent: 0),
                    (label: "Directional Emission", path: "particle.directional_emission", parent: "", indent: 0),
                    (label: "Top Level Particles", path: "particle.top_level_particles", parent: "", indent: 0),
                    (label: "Spread", path: "particle.spread", parent: "", indent: 0),
                    (label: "Speed Min", path: "particle.speed_min", parent: "", indent: 0),
                    (label: "Speed Max", path: "particle.speed_max", parent: "", indent: 0),
                    (label: "Start Scale Min", path: "particle.start_scale_min", parent: "", indent: 0),
                    (label: "Start Scale Max", path: "particle.start_scale_max", parent: "", indent: 0),
                    (label: "End Scale Min", path: "particle.end_scale_min", parent: "", indent: 0),
                    (label: "End Scale Max", path: "particle.end_scale_max", parent: "", indent: 0),
                };

                foreach (var (label, path, _, indent) in particleProps)
                {
                    if (propsWithKeyframes.Contains(path))
                        newRows.Add(MakeSingle(obj, label, path, indent));
                }

                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Spawn Extents", "particle.spawn_extents", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Velocity Min", "particle.velocity_min", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Velocity Max", "particle.velocity_max", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Gravity", "particle.gravity", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Direction", "particle.direction", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Initial Rotation Min", "particle.rotation_min", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Initial Rotation Max", "particle.rotation_max", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Angular Velocity Min", "particle.angular_velocity_min", newRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Angular Velocity Max", "particle.angular_velocity_max", newRows);
            }

            // Shape key properties — dynamic per-mesh/per-key paths.
            {
                var meshes = obj.GetMeshInstancesRecursively();
                for (int m = 0; m < meshes.Count; m++)
                {
                    if (!meshes[m].HasShapeKeys) continue;
                    for (int k = 0; k < meshes[m].ShapeKeys.Count; k++)
                    {
                        string path = $"shapekey.{m}.{k}";
                        if (!propsWithKeyframes.Contains(path)) continue;

                        string keyName = meshes[m].ShapeKeys[k].Name;
                        string label   = meshes.Count > 1 ? $"{keyName} (Mesh {m})" : keyName;
                        newRows.Add(MakeSingle(obj, label, path));
                    }
                }
            }
        }

        // Groups can temporarily disappear during selection changes or undo/redo.
        // Keep their expansion state until another project is loaded.
        _displayRows = newRows;
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

        if (obj is MiBoneSceneObject miBone && miBone.BendParameters is { } bend && (bend.AxisX || bend.AxisY || bend.AxisZ))
        {
            _displayRows.Add(MakeGroup(obj, "Bend", ["bend.x", "bend.y", "bend.z"], "bend"));
            if (bend.AxisX) _displayRows.Add(MakeSingle(obj, "X", "bend.x", 1));
            if (bend.AxisY) _displayRows.Add(MakeSingle(obj, "Y", "bend.y", 1));
            if (bend.AxisZ) _displayRows.Add(MakeSingle(obj, "Z", "bend.z", 1));
        }

        if (obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
        {
            _displayRows.Add(MakeSingle(obj, "Item Slot",        "item.slot"));
            _displayRows.Add(MakeSingle(obj, "Custom Item Slot", "item.custom_slot"));
        }

        if (obj is LightSceneObject)
        {
            _displayRows.Add(MakeSingle(obj, "Light Energy",    "light.energy"));
            _displayRows.Add(MakeSingle(obj, "Light Range",     "light.range"));
            _displayRows.Add(MakeSingle(obj, "Indirect Energy", "light.indirect_energy"));
            _displayRows.Add(MakeSingle(obj, "Specular",        "light.specular"));
            _displayRows.Add(MakeSingle(obj, "Spot Angle",      "light.spot_angle"));
            _displayRows.Add(MakeSingle(obj, "Spot Blend",      "light.spot_blend"));
            _displayRows.Add(MakeGroup(obj,  "Light Color",     new[] { "light.color.r", "light.color.g", "light.color.b" }));
            _displayRows.Add(MakeSingle(obj, "R",               "light.color.r", 1));
            _displayRows.Add(MakeSingle(obj, "G",               "light.color.g", 1));
            _displayRows.Add(MakeSingle(obj, "B",               "light.color.b", 1));
        }

        if (obj is CameraSceneObject cameraObj)
        {
            _displayRows.Add(MakeSingle(obj, "Active", "camera.active"));
            _displayRows.Add(MakeSingle(obj, "Field of View", "camera.fov"));

            for (int i = 0; i < cameraObj.Effects.Count; i++)
            {
                var effect = cameraObj.Effects[i];
                if (effect.Type == CameraEffectType.DepthOfField)
                {
                    string baseDofPath = $"camera.effect.{i}.depth_of_field";
                    string[] paths = DepthOfFieldSettings.Controls.Select(c => $"{baseDofPath}.{c.Key}").ToArray();
                    _displayRows.Add(MakeGroup(obj, $"Effect {i + 1}: Depth of Field", paths, $"camera.effect.{i}"));
                    foreach (var control in DepthOfFieldSettings.Controls)
                        _displayRows.Add(MakeSingle(obj, control.Label, $"{baseDofPath}.{control.Key}", 1));
                    continue;
                }
                if (effect.Type == CameraEffectType.Bloom)
                {
                    string bloomPath = $"camera.effect.{i}.bloom";
                    string[] paths = BloomSettings.Controls.Select(c => $"{bloomPath}.{c.Key}").ToArray();
                    _displayRows.Add(MakeGroup(obj, $"Effect {i + 1}: Bloom", paths, $"camera.effect.{i}"));
                    foreach (var control in BloomSettings.Controls)
                        _displayRows.Add(MakeSingle(obj, control.Label, $"{bloomPath}.{control.Key}", 1));
                    continue;
                }
                if (effect.Type == CameraEffectType.FilmGrain)
                {
                    string grainPath = $"camera.effect.{i}.film_grain";
                    string[] paths = { $"{grainPath}.strength", $"{grainPath}.saturation", $"{grainPath}.size" };
                    _displayRows.Add(MakeGroup(obj, $"Effect {i + 1}: Film Grain", paths, $"camera.effect.{i}"));
                    _displayRows.Add(MakeSingle(obj, "Strength", paths[0], 1));
                    _displayRows.Add(MakeSingle(obj, "Saturation", paths[1], 1));
                    _displayRows.Add(MakeSingle(obj, "Size", paths[2], 1));
                    continue;
                }
                if (effect.Type != CameraEffectType.CameraShake)
                    continue;

                string basePath = $"camera.effect.{i}.shake";
                string modePath = $"{basePath}.mode";
                string traumaPath = $"{basePath}.trauma";
                string strengthGroupPath = $"camera.effect.{i}.strength";
                string speedGroupPath = $"camera.effect.{i}.speed";
                string offsetGroupPath = $"camera.effect.{i}.offset";
                string[] strengthPaths = { $"{basePath}.strength.x", $"{basePath}.strength.y", $"{basePath}.strength.z" };
                string[] speedPaths = { $"{basePath}.speed.x", $"{basePath}.speed.y", $"{basePath}.speed.z" };
                string[] offsetPaths = { $"{basePath}.offset.x", $"{basePath}.offset.y", $"{basePath}.offset.z" };

                _displayRows.Add(MakeGroup(obj, $"Effect {i + 1}: Camera Shake", [modePath, traumaPath, strengthGroupPath, ..strengthPaths, speedGroupPath, ..speedPaths, offsetGroupPath, ..offsetPaths], $"camera.effect.{i}"));
                _displayRows.Add(MakeSingle(obj, "Mode", modePath, 1));
                _displayRows.Add(MakeSingle(obj, "Trauma", traumaPath, 1));
                _displayRows.Add(MakeGroup(obj, "Strength", strengthPaths, strengthGroupPath, 1));
                _displayRows.Add(MakeSingle(obj, "X", strengthPaths[0], 2));
                _displayRows.Add(MakeSingle(obj, "Y", strengthPaths[1], 2));
                _displayRows.Add(MakeSingle(obj, "Z", strengthPaths[2], 2));
                _displayRows.Add(MakeGroup(obj, "Speed", speedPaths, speedGroupPath, 1));
                _displayRows.Add(MakeSingle(obj, "X", speedPaths[0], 2));
                _displayRows.Add(MakeSingle(obj, "Y", speedPaths[1], 2));
                _displayRows.Add(MakeSingle(obj, "Z", speedPaths[2], 2));
                _displayRows.Add(MakeGroup(obj, "Offset", offsetPaths, offsetGroupPath, 1));
                _displayRows.Add(MakeSingle(obj, "X", offsetPaths[0], 2));
                _displayRows.Add(MakeSingle(obj, "Y", offsetPaths[1], 2));
                _displayRows.Add(MakeSingle(obj, "Z", offsetPaths[2], 2));
            }
        }

        if (obj is ParticleSpawnerSceneObject)
        {
            _displayRows.Add(MakeSingle(obj, "Emitting", "particle.emitting"));
            _displayRows.Add(MakeSingle(obj, "One Shot", "particle.one_shot"));
            _displayRows.Add(MakeSingle(obj, "Amount", "particle.amount"));
            _displayRows.Add(MakeSingle(obj, "Spawn Rate", "particle.spawn_rate"));
            _displayRows.Add(MakeSingle(obj, "Lifetime Min", "particle.lifetime_min"));
            _displayRows.Add(MakeSingle(obj, "Lifetime Max", "particle.lifetime_max"));
            _displayRows.Add(MakeSingle(obj, "Simulation Speed", "particle.simulation_speed"));
            _displayRows.Add(MakeSingle(obj, "Linear Damping", "particle.linear_damping"));
            _displayRows.Add(MakeSingle(obj, "Angular Damping", "particle.angular_damping"));
            _displayRows.Add(MakeSingle(obj, "Emission Shape", "particle.emission_shape"));
            _displayRows.Add(MakeSingle(obj, "Directional Emission", "particle.directional_emission"));
            _displayRows.Add(MakeSingle(obj, "Top Level Particles", "particle.top_level_particles"));
            _displayRows.Add(MakeSingle(obj, "Spread", "particle.spread"));
            _displayRows.Add(MakeSingle(obj, "Speed Min", "particle.speed_min"));
            _displayRows.Add(MakeSingle(obj, "Speed Max", "particle.speed_max"));
            _displayRows.Add(MakeSingle(obj, "Start Scale Min", "particle.start_scale_min"));
            _displayRows.Add(MakeSingle(obj, "Start Scale Max", "particle.start_scale_max"));
            _displayRows.Add(MakeSingle(obj, "End Scale Min", "particle.end_scale_min"));
            _displayRows.Add(MakeSingle(obj, "End Scale Max", "particle.end_scale_max"));

            AddParticleVectorGroup(obj, "Spawn Extents", "particle.spawn_extents");
            AddParticleVectorGroup(obj, "Velocity Min", "particle.velocity_min");
            AddParticleVectorGroup(obj, "Velocity Max", "particle.velocity_max");
            AddParticleVectorGroup(obj, "Gravity", "particle.gravity");
            AddParticleVectorGroup(obj, "Direction", "particle.direction");
            AddParticleVectorGroup(obj, "Initial Rotation Min", "particle.rotation_min");
            AddParticleVectorGroup(obj, "Initial Rotation Max", "particle.rotation_max");
            AddParticleVectorGroup(obj, "Angular Velocity Min", "particle.angular_velocity_min");
            AddParticleVectorGroup(obj, "Angular Velocity Max", "particle.angular_velocity_max");
        }

        foreach (var row in _displayRows.Where(r => r.Object == obj && !r.IsGroupHeader && r.PropertyPath != "__header__").ToArray())
            LoadKeyframesFromObject(obj, row.PropertyPath);
        foreach (var path in MaterialPropertyPaths)
            LoadKeyframesFromObject(obj, path);
    }

    private static TimelineProperty MakeSingle(SceneObject obj, string label, string path, int indent = 0) =>
        new() { Object = obj, Label = label, PropertyPath = path, Indent = indent };

    private static TimelineProperty MakeGroup(SceneObject obj, string name, string[] paths, string? keyPath = null, int indent = 0) =>
        new() { Object = obj, Label = name, PropertyPath = keyPath ?? name.ToLower(), IsGroupHeader = true, GroupPaths = paths, Indent = indent };

    private void AddParticleVectorGroup(SceneObject obj, string label, string basePath)
    {
        string[] paths = { $"{basePath}.x", $"{basePath}.y", $"{basePath}.z" };
        _displayRows.Add(MakeGroup(obj, label, paths, basePath));
        _displayRows.Add(MakeSingle(obj, "X", paths[0], 1));
        _displayRows.Add(MakeSingle(obj, "Y", paths[1], 1));
        _displayRows.Add(MakeSingle(obj, "Z", paths[2], 1));
    }

    private static void AddParticleVectorGroupIfKeyed(
        SceneObject obj,
        HashSet<string> propsWithKeyframes,
        string label,
        string basePath,
        List<TimelineProperty> rows)
    {
        string[] paths = { $"{basePath}.x", $"{basePath}.y", $"{basePath}.z" };
        if (!paths.Any(propsWithKeyframes.Contains))
            return;

        rows.Add(MakeGroup(obj, label, paths, basePath));
        rows.Add(MakeSingle(obj, "X", paths[0], 1));
        rows.Add(MakeSingle(obj, "Y", paths[1], 1));
        rows.Add(MakeSingle(obj, "Z", paths[2], 1));
    }

    // ── Object lookup ─────────────────────────────────────────────────────────

    private SceneObject? FindObjectById(string id)
    {
        var rows = _displayRows.ToArray();
        foreach (var row in rows)
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
