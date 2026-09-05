using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.audio;
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

/// <summary>A named, coloured marker pinned to a frame on the timeline.</summary>
public class TimelineMarker
{
    public int Frame { get; set; }
    public string Label { get; set; } = "Marker";
    public Vector4 Color { get; set; } = new(0.9f, 0.2f, 0.2f, 1f);
}

/// <summary>
/// Timeline model.  Originally an ImGui <c>UiPanel</c> (ported from the Godot
/// TimelinePanel.cs in simply-remade-nuxi) that both owned the animation data
/// AND drew the panel with ImGui draw lists.
///
/// MIGRATION: in the Avalonia port the panel UI moved to
/// <see cref="core.ui.Dock.TimelineView"/> (a custom-drawn control), so this
/// class is now a plain model. It owns frame-based playback, keyframe storage /
/// interpolation / apply-to-scene, selection, clipboard, markers, ghost tracks,
/// the playback region and audio-track playback. All screen-space concerns
/// (zoom, scrolling, hit-testing, drawing) live in the view.
///
/// The old panel enumerated scene objects through <c>Viewport.SceneObjects</c>.
/// Since the Viewport isn't ported yet, the objects are injected via
/// <see cref="SceneObjectsProvider"/> instead of a hard dependency on the
/// still-broken <c>Viewport</c> type; the item-sheet slot application that went
/// through <c>Viewport.SpawnMenu</c> is likewise exposed as the
/// <see cref="ApplyItemSheetSlot"/> hook.
/// </summary>
public class Timeline
{
    public static string SanitizeIntegerText(string text)
    {
        return NumericExpressionParser.SanitizeText(text, allowDecimal: false, allowExponent: false);
    }

    // ── Singleton ─────────────────────────────────────────────────────────────

    public static Timeline Instance { get; private set; }

    // ── External wiring ───────────────────────────────────────────────────────

    /// <summary>
    /// Supplies the root scene objects (formerly <c>Viewport.SceneObjects</c>).
    /// Assigned by the host once the viewport exists.
    /// </summary>
    public Func<IEnumerable<SceneObject>>? SceneObjectsProvider { get; set; }

    /// <summary>
    /// Applies an item-sheet slot to a spawned object
    /// (formerly <c>Viewport.SpawnMenu.ApplyTemporaryItemSheetSlotToSpawnedObject</c>).
    /// Arguments: object, columnIndex, rowIndex.
    /// </summary>
    public Action<SceneObject, int, int>? ApplyItemSheetSlot { get; set; }

    // ── Playback ──────────────────────────────────────────────────────────────

    private int    _currentFrame     = 0;
    private int    _maxFrames        = 300;
    private float  _frameRate        = 30f;
    private bool   _isPlaying        = false;
    private bool   _autoKeyframe     = false;
    private bool   _loopPlayback     = false;
    private bool   _ghostModeEnabled = false;
    private double _frameAccumulator = 0.0;
    private long   _lastTimestamp    = Stopwatch.GetTimestamp();

    // ── Keyframe data ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<TimelineKeyframe>> _propertyKeyframes = new();
    private readonly List<TimelineProperty>                     _displayRows        = new();
    private readonly Dictionary<string, bool>                   _groupExpanded      = new();
    private readonly HashSet<string>                            _ghostTracks        = new(StringComparer.Ordinal);

    // ── Selection ─────────────────────────────────────────────────────────────

    private readonly List<TimelineKeyframe>                                        _selectedKeyframes = new();
    private readonly Dictionary<TimelineKeyframe, (SceneObject obj, string path)> _keyframeOwners    = new();

    private readonly List<TimelineMarker> _markers = new();
    private int? _selectedRegionStart;
    private int? _selectedRegionEnd;
    private int? _playbackRegionStart;
    private int? _playbackRegionEnd;
    private readonly List<(SceneObject obj, string path, int offset, object value, string interpolation)> _keyframeClipboard = new();

    // ── Audio tracks ──────────────────────────────────────────────────────────

    public  IReadOnlyList<TimelineAudioTrack> AudioTracks => _audioTracks;
    private readonly List<TimelineAudioTrack>  _audioTracks = new();

    // ── Drag-keyframe (frames at drag start, keyed by keyframe) ───────────────

    private readonly Dictionary<TimelineKeyframe, int> _selectedStartFrames = new();

    // ── Public API ────────────────────────────────────────────────────────────

    public int   CurrentFrame => _currentFrame;
    public int   MaxFrames    => _maxFrames;
    public float Framerate    => _frameRate;
    public bool  IsPlaying    => _isPlaying;

    public bool AutoKeyframe { get => _autoKeyframe; set => _autoKeyframe = value; }
    public bool LoopPlayback { get => _loopPlayback; set => _loopPlayback = value; }

    public bool GhostModeEnabled
    {
        get => _ghostModeEnabled;
        set { _ghostModeEnabled = value; PruneHiddenSelection(); }
    }

    public int? PlaybackRegionStart => _playbackRegionStart;
    public int? PlaybackRegionEnd   => _playbackRegionEnd;
    public int? SelectedRegionStart { get => _selectedRegionStart; set => _selectedRegionStart = value; }
    public int? SelectedRegionEnd   { get => _selectedRegionEnd;   set => _selectedRegionEnd = value; }

    public IReadOnlyList<TimelineMarker>   Markers           => _markers;
    public IReadOnlyList<TimelineProperty> DisplayRows       => _displayRows;
    public IReadOnlyList<TimelineKeyframe> SelectedKeyframes => _selectedKeyframes;
    public bool HasClipboardContent => _keyframeClipboard.Count > 0;

    public void SetMaxFrames(int frames) => _maxFrames = Math.Max(10, frames);

    public void SetPlaybackRegion(int start, int end)
    {
        _playbackRegionStart = Math.Min(start, end);
        _playbackRegionEnd   = Math.Max(start, end);
    }

    public void ClearPlaybackRegion()
    {
        _playbackRegionStart = null;
        _playbackRegionEnd   = null;
    }

    public void AddMarker(TimelineMarker marker)
    {
        _markers.Add(marker);
        SortMarkers();
    }

    public void RemoveMarker(TimelineMarker marker) => _markers.Remove(marker);

    public void SortMarkers() => _markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));

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
        OnSelectionChanged();

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
            ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
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
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    /// <summary>Advances playback based on wall-clock time. The view calls this
    /// every UI tick (the old ImGui panel called it once per rendered frame).</summary>
    public void UpdatePlayback()
    {
        long now = Stopwatch.GetTimestamp();
        double stopwatchDelta = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;

        if (!_isPlaying)
        {
            _frameAccumulator = 0.0;
            return;
        }

        double delta = stopwatchDelta > 0.0 && stopwatchDelta < 0.5 ? stopwatchDelta : 0.0;
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

    public bool IsTrackRowGhost(TimelineProperty row)
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

    public void SetTrackRowGhostState(TimelineProperty row, bool ghost)
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

    public bool IsTrackRowVisible(TimelineProperty row)
    {
        if (row.PropertyPath == "__header__")
            return true;

        if (_ghostModeEnabled && IsTrackRowGhost(row))
            return false;

        if (row.Indent > 0 && !IsGroupChildVisible(row))
            return false;

        return true;
    }

    public bool ShouldShowGhostIndicator(TimelineProperty row)
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

    private const float AudioRowHeight    = 28f;
    private const float AudioFooterHeight = 24f;
    private const float AudioControlWidth = 176f;  // vol slider + M + L + X

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

    public void JumpToStart()          { _currentFrame = 0; _frameAccumulator = 0.0; StopAllAudio(); ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    public void Stop()                 { _isPlaying = false; StopAllAudio(); JumpToStart(); }
    public void StepBackward()         { _currentFrame = Math.Max(0, _currentFrame - 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }
    public void StepForward()          { _currentFrame = Math.Min(_maxFrames, _currentFrame + 1); _frameAccumulator = 0.0; ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false); }

    public void JumpToLastKeyframe()
    {
        int last = 0;
        foreach (var kvp in _propertyKeyframes)
            foreach (var kf in kvp.Value)
                if (kf.Frame > last) last = kf.Frame;
        _currentFrame = last;
        _frameAccumulator = 0.0;
        ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
    }

    // ── Main track area ───────────────────────────────────────────────────────

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

    private IEnumerable<(TimelineKeyframe keyframe, SceneObject obj, string path)> EnumerateKeyframes()
    {
        foreach (var row in _displayRows.Where(r => !r.IsGroupHeader && r.PropertyPath != "__header__"))
        {
            if (!IsTrackRowVisible(row))
                continue;
            foreach (var keyframe in GetKeyframesForProperty(row.Object, row.PropertyPath))
                yield return (keyframe, row.Object, row.PropertyPath);
        }
    }

    public void SelectKeyframes(Func<TimelineKeyframe, bool> predicate)
    {
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
        foreach (var (keyframe, obj, path) in EnumerateKeyframes().Where(x => predicate(x.keyframe)))
        {
            _selectedKeyframes.Add(keyframe);
            _keyframeOwners[keyframe] = (obj, path);
        }
    }

    public void MoveIntoPlaybackRegionIfNeeded()
    {
        if (_loopPlayback && _playbackRegionStart.HasValue && _playbackRegionEnd.HasValue &&
            (_currentFrame < _playbackRegionStart.Value || _currentFrame > _playbackRegionEnd.Value))
        {
            _currentFrame = _playbackRegionStart.Value;
            _frameAccumulator = 0.0;
            ApplyKeyframesAtCurrentFrame(holdFirstKeyframeBeforeStart: false);
        }
    }

    public void SelectExtreme(bool first)
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

    public void PasteKeyframes(int frame)
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

    public void TransformSelectedFrames(bool reverse)
    {
        int min = _selectedKeyframes.Min(k => k.Frame);
        int max = _selectedKeyframes.Max(k => k.Frame);
        var random = Random.Shared;
        foreach (var keyframe in _selectedKeyframes)
            keyframe.Frame = reverse ? min + max - keyframe.Frame : random.Next(min, max + 1);
        RemoveSelectedCollisionsAndSave();
    }

    public void ScaleSelectedKeyframeSpeed(float factor, bool slowDown)
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

    /// <summary>Read-only view of the keyframes on one property track.</summary>
    public IReadOnlyList<TimelineKeyframe> GetKeyframes(SceneObject obj, string propertyPath) =>
        GetKeyframesForProperty(obj, propertyPath);

    /// <summary>Whether a group header row is currently expanded in the view.</summary>
    public bool IsGroupExpanded(TimelineProperty header)
    {
        string key = $"{header.Object.ObjectId}.{header.PropertyPath}";
        return _groupExpanded.TryGetValue(key, out bool expanded) && expanded;
    }

    public void ToggleGroupExpanded(TimelineProperty header)
    {
        string key = $"{header.Object.ObjectId}.{header.PropertyPath}";
        _groupExpanded[key] = !(_groupExpanded.TryGetValue(key, out bool expanded) && expanded);
    }

    // ── Keyframe selection (view-driven) ──────────────────────────────────────

    public bool IsKeyframeSelected(TimelineKeyframe keyframe) => _selectedKeyframes.Contains(keyframe);

    public void ClearKeyframeSelection()
    {
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
    }

    /// <summary>Click-select a keyframe. With <paramref name="additive"/> the
    /// keyframe is toggled in/out of the existing selection (ctrl/shift-click).</summary>
    public void SelectKeyframe(SceneObject obj, string propertyPath, TimelineKeyframe keyframe, bool additive)
    {
        if (!additive)
        {
            if (_selectedKeyframes.Contains(keyframe)) return; // keep multi-selection for drags
            ClearKeyframeSelection();
        }
        else if (_selectedKeyframes.Remove(keyframe))
        {
            _keyframeOwners.Remove(keyframe);
            return;
        }
        _selectedKeyframes.Add(keyframe);
        _keyframeOwners[keyframe] = (obj, propertyPath);
    }

    // ── Keyframe dragging (view-driven) ───────────────────────────────────────

    /// <summary>Snapshot selected keyframe frames before a drag starts.</summary>
    public void BeginKeyframeDrag()
    {
        _selectedStartFrames.Clear();
        foreach (var keyframe in _selectedKeyframes)
            _selectedStartFrames[keyframe] = keyframe.Frame;
    }

    /// <summary>Offset all selected keyframes from their drag-start frames.</summary>
    public void DragSelectedKeyframes(int frameDelta)
    {
        if (_selectedStartFrames.Count == 0) return;
        int minStart = _selectedStartFrames.Values.Min();
        int clampedDelta = Math.Max(frameDelta, -minStart); // keep everything >= frame 0
        foreach (var (keyframe, start) in _selectedStartFrames)
            keyframe.Frame = start + clampedDelta;
    }

    /// <summary>Commit a drag: resolve frame collisions and flush to the objects.</summary>
    public void EndKeyframeDrag()
    {
        if (_selectedStartFrames.Count == 0) return;
        _selectedStartFrames.Clear();
        RemoveSelectedCollisionsAndSave();
    }

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
        RebuildDisplayRows();
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
        string[] standardPaths =
        {
            "visible", "material.alpha",
            "position.x", "position.y", "position.z",
            "rotation.x", "rotation.y", "rotation.z",
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
            "camera.active",
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

        foreach (var obj in objects)
        {
            if (obj == null || obj.Keyframes.Count == 0) continue;
            IEnumerable<string> paths = obj is LightSceneObject   ? standardPaths.Concat(lightPaths)
                                      : obj is CameraSceneObject  ? standardPaths.Concat(cameraPaths)
                                      : standardPaths;
            if (obj is MiBoneSceneObject)
                paths = paths.Concat(new[] { "bend.x", "bend.y", "bend.z" });
            if (obj is CameraSceneObject cameraObj)
                paths = paths.Concat(GetCameraEffectPropertyPaths(cameraObj));
            if (obj is ParticleSpawnerSceneObject)
                paths = paths.Concat(particleScalarPaths).Concat(particleVectorPaths);
            if (obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
                paths = paths.Concat(itemPaths);
            foreach (var path in paths)
                if (obj.Keyframes.ContainsKey(path) && obj.Keyframes[path].Count > 0)
                    LoadKeyframesFromObject(obj, path);

            if (obj is CameraSceneObject)
            {
                foreach (var path in obj.Keyframes.Keys
                             .Where(k => k.StartsWith("camera.effect.", StringComparison.Ordinal)))
                {
                    if (obj.Keyframes[path].Count > 0)
                        LoadKeyframesFromObject(obj, path);
                }
            }

            // Shape keys use dynamic "shapekey.<meshIndex>.<keyIndex>" paths that
            // depend on the object's mesh/morph-target layout, so they can't be
            // enumerated from a static list like the properties above.
            var shapeKeyMeshes = obj.GetMeshInstancesRecursively();
            for (int m = 0; m < shapeKeyMeshes.Count; m++)
            {
                if (!shapeKeyMeshes[m].HasShapeKeys) continue;
                for (int k = 0; k < shapeKeyMeshes[m].ShapeKeys.Count; k++)
                {
                    string skPath = $"shapekey.{m}.{k}";
                    if (obj.Keyframes.ContainsKey(skPath) && obj.Keyframes[skPath].Count > 0)
                        LoadKeyframesFromObject(obj, skPath);
                }
            }
        }

        RecalculateTimelineLength();
    }

    private static IEnumerable<string> GetCameraEffectPropertyPaths(CameraSceneObject cam)
    {
        for (int i = 0; i < cam.Effects.Count; i++)
        {
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
                return path == "text.font" ? next.Value : TryConvertKeyframeValue(next.Value, out float nextBeforeStart) ? nextBeforeStart : null;
            return null;
        }

        // At or after the last keyframe, or exactly on a keyframe.
        if (path == "text.font") return prev.Value;
        if (next == null || prev.Frame == frame)
            return TryConvertKeyframeValue(prev.Value, out float prevDirect) ? prevDirect : null;

        // Between two keyframes — interpolate.
        // Discrete state-like properties and "instant" interpolation use the
        // previous keyframe's value with no blending.
        if (path == "visible" || path == "camera.active" || path == "item.slot" || path == "item.custom_slot" ||
            path == "text.horizontal_alignment" || path == "text.vertical_alignment" ||
            path == "text.antialiasing" || path == "text.outline_enabled" ||
            path == "particle.emitting" || path == "particle.one_shot" || path == "particle.amount" ||
            path == "particle.emission_shape" || path == "particle.directional_emission" || path == "particle.top_level_particles" ||
            path.EndsWith(".mode", StringComparison.Ordinal) || prev.InterpolationType == "instant")
            return TryConvertKeyframeValue(prev.Value, out float prevInstant) ? prevInstant : null;

        if (!TryConvertKeyframeValue(prev.Value, out float pv) || !TryConvertKeyframeValue(next.Value, out float nv))
            return null;

        float t  = (frame - prev.Frame) / (float)(next.Frame - prev.Frame);
        return pv + (nv - pv) * ApplyInterpolation(t, prev.InterpolationType);
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
        if (path == "text.font") return obj.TextMeshFontPath;

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
                    if (comp == "alpha") return obj.MaterialSettings?.AlbedoColor.w ?? 1f;
                    break;
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
                    if (comp == "active" && obj is CameraSceneObject camAct)
                        return camAct.Active ? 1f : 0f;
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

    private void SetPropertyValue(SceneObject obj, string path, object rawValue)
    {
        if (path == "text.font")
        {
            string font = rawValue is JsonElement json && json.ValueKind == JsonValueKind.String
                ? json.GetString() ?? "" : Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
            if (obj.TextMeshFontPath != font) { obj.TextMeshFontPath = font; TextMeshFactory.Rebuild(obj); }
            return;
        }
        if (!TryConvertKeyframeValue(rawValue, out float value)) return;
        if (path == "visible") { obj.ObjectVisible = value >= 0.5f; return; }

        var parts = path.Split('.');
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

                    case "item":
                        if (ApplyItemSheetSlot == null || obj.TemporaryItemSheetColumns <= 0 || obj.TemporaryItemSheetRows <= 0)
                            break;

                        int columnIndex = obj.TemporaryItemSheetColumnIndex;
                        int rowIndex = obj.TemporaryItemSheetRowIndex;
                        if (comp == "slot")
                            columnIndex = Math.Clamp((int)MathF.Round(value), 0, obj.TemporaryItemSheetColumns - 1);
                        else if (comp == "custom_slot")
                            rowIndex = Math.Clamp((int)MathF.Round(value), 0, obj.TemporaryItemSheetRows - 1);
                        else
                            break;

                        ApplyItemSheetSlot(obj, columnIndex, rowIndex);
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
                        if (comp == "active" && obj is CameraSceneObject camSet)
                        {
                            bool shouldBeActive = value >= 0.5f;
                            if (shouldBeActive)
                                CameraSceneObject.SetActiveExclusive(camSet);
                            else
                                camSet.Active = false;
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

    private void OnSelectionChanged()
    {
        _displayRows.Clear();
        _propertyKeyframes.Clear();

        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected != null)
            foreach (var obj in selected)
                AddObjectRows(obj);

        var roots = SceneObjectsProvider?.Invoke();
        if (roots != null)
            LoadKeyframesForAllObjects(CollectAllObjects(roots));

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
        _displayRows.Clear();

        foreach (var obj in selectedObjects)
        {
            // Add header
            _displayRows.Add(new TimelineProperty { Object = obj, Label = $"── {obj.Name} ──", PropertyPath = "__header__" });

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
                (label: "Position",        path: "position", parent: "", indent: 0),
                (label: "X",               path: "position.x", parent: "position", indent: 1),
                (label: "Y",               path: "position.y", parent: "position", indent: 1),
                (label: "Z",               path: "position.z", parent: "position", indent: 1),
                (label: "Rotation",        path: "rotation", parent: "", indent: 0),
                (label: "X",               path: "rotation.x", parent: "rotation", indent: 1),
                (label: "Y",               path: "rotation.y", parent: "rotation", indent: 1),
                (label: "Z",               path: "rotation.z", parent: "rotation", indent: 1),
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
                if (path == "position" || path == "rotation" || path == "scale")
                {
                    // This is a group header
                    string[] groupPaths = path switch
                    {
                        "position" => new[] { "position.x", "position.y", "position.z" },
                        "rotation" => new[] { "rotation.x", "rotation.y", "rotation.z" },
                        "scale" => new[] { "scale.x", "scale.y", "scale.z" },
                        _ => Array.Empty<string>()
                    };

                    // Group paths are organizational rows and never receive
                    // keyframes themselves. Show the header whenever at least
                    // one of its component tracks has keyframes.
                    if (groupPaths.Any(propsWithKeyframes.Contains))
                        _displayRows.Add(MakeGroup(obj, label, groupPaths));
                }
                else if (propsWithKeyframes.Contains(path))
                {
                    _displayRows.Add(MakeSingle(obj, label, path, indent));
                }
            }

            foreach (var (label, path, parent, indent) in bendProps)
            {
                if (path == "bend")
                {
                    string[] groupPaths = ["bend.x", "bend.y", "bend.z"];
                    if (groupPaths.Any(propsWithKeyframes.Contains))
                        _displayRows.Add(MakeGroup(obj, label, groupPaths, path));
                }
                else if (propsWithKeyframes.Contains(path))
                {
                    _displayRows.Add(MakeSingle(obj, label, path, indent));
                }
            }

            if (obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            {
                foreach (var (label, path, parent, indent) in itemProps)
                {
                    if (propsWithKeyframes.Contains(path))
                        _displayRows.Add(MakeSingle(obj, label, path, indent));
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
                            _displayRows.Add(MakeGroup(obj, label, groupPaths));
                    }
                    else if (propsWithKeyframes.Contains(path))
                    {
                        _displayRows.Add(MakeSingle(obj, label, path, indent));
                    }
                }
            }

            // Camera properties
            if (obj is CameraSceneObject)
            {
                var cameraProps = new[]
                {
                    (label: "Active", path: "camera.active", parent: "", indent: 0),
                };

                foreach (var (label, path, parent, indent) in cameraProps)
                {
                    bool hasKeyframes = propsWithKeyframes.Contains(path);
                    if (!hasKeyframes) continue;

                    _displayRows.Add(MakeSingle(obj, label, path, indent));
                }

                if (obj is CameraSceneObject cameraObj)
                {
                    for (int i = 0; i < cameraObj.Effects.Count; i++)
                    {
                        var effect = cameraObj.Effects[i];
                        if (effect.Type == CameraEffectType.FilmGrain)
                        {
                            string grainPath = $"camera.effect.{i}.film_grain";
                            string[] paths = { $"{grainPath}.strength", $"{grainPath}.saturation", $"{grainPath}.size" };
                            if (!paths.Any(propsWithKeyframes.Contains)) continue;
                            _displayRows.Add(MakeGroup(obj, $"Effect {i + 1}: Film Grain", paths, $"camera.effect.{i}"));
                            if (propsWithKeyframes.Contains(paths[0])) _displayRows.Add(MakeSingle(obj, "Strength", paths[0], 1));
                            if (propsWithKeyframes.Contains(paths[1])) _displayRows.Add(MakeSingle(obj, "Saturation", paths[1], 1));
                            if (propsWithKeyframes.Contains(paths[2])) _displayRows.Add(MakeSingle(obj, "Size", paths[2], 1));
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

                        _displayRows.Add(MakeGroup(obj,
                            $"Effect {i + 1}: Camera Shake",
                            allPaths,
                            $"camera.effect.{i}"));

                        if (propsWithKeyframes.Contains(modePath))
                            _displayRows.Add(MakeSingle(obj, "Mode", modePath, 1));

                        if (propsWithKeyframes.Contains(traumaPath))
                            _displayRows.Add(MakeSingle(obj, "Trauma", traumaPath, 1));

                        if (strengthPaths.Any(propsWithKeyframes.Contains))
                        {
                            _displayRows.Add(MakeGroup(obj, "Strength", strengthPaths, strengthGroupPath, 1));
                            _displayRows.Add(MakeSingle(obj, "X", strengthPaths[0], 2));
                            _displayRows.Add(MakeSingle(obj, "Y", strengthPaths[1], 2));
                            _displayRows.Add(MakeSingle(obj, "Z", strengthPaths[2], 2));
                        }

                        if (speedPaths.Any(propsWithKeyframes.Contains))
                        {
                            _displayRows.Add(MakeGroup(obj, "Speed", speedPaths, speedGroupPath, 1));
                            _displayRows.Add(MakeSingle(obj, "X", speedPaths[0], 2));
                            _displayRows.Add(MakeSingle(obj, "Y", speedPaths[1], 2));
                            _displayRows.Add(MakeSingle(obj, "Z", speedPaths[2], 2));
                        }

                        if (offsetPaths.Any(propsWithKeyframes.Contains))
                        {
                            _displayRows.Add(MakeGroup(obj, "Offset", offsetPaths, offsetGroupPath, 1));
                            _displayRows.Add(MakeSingle(obj, "X", offsetPaths[0], 2));
                            _displayRows.Add(MakeSingle(obj, "Y", offsetPaths[1], 2));
                            _displayRows.Add(MakeSingle(obj, "Z", offsetPaths[2], 2));
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
                        _displayRows.Add(MakeSingle(obj, label, path, indent));
                }

                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Spawn Extents", "particle.spawn_extents", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Velocity Min", "particle.velocity_min", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Velocity Max", "particle.velocity_max", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Gravity", "particle.gravity", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Direction", "particle.direction", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Initial Rotation Min", "particle.rotation_min", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Initial Rotation Max", "particle.rotation_max", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Angular Velocity Min", "particle.angular_velocity_min", _displayRows);
                AddParticleVectorGroupIfKeyed(obj, propsWithKeyframes, "Angular Velocity Max", "particle.angular_velocity_max", _displayRows);
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
                        _displayRows.Add(MakeSingle(obj, label, path));
                    }
                }
            }
        }

        // Keep expanded/collapsed state for groups that still exist after the
        // rebuild, while discarding stale entries from removed rows/objects.
        var validGroupKeys = _displayRows
            .Where(r => r.IsGroupHeader)
            .Select(r => $"{r.Object.ObjectId}.{r.PropertyPath}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var staleKey in _groupExpanded.Keys.Where(k => !validGroupKeys.Contains(k)).ToList())
            _groupExpanded.Remove(staleKey);
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

            for (int i = 0; i < cameraObj.Effects.Count; i++)
            {
                var effect = cameraObj.Effects[i];
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

        foreach (var row in _displayRows.Where(r => r.Object == obj && !r.IsGroupHeader && r.PropertyPath != "__header__"))
            LoadKeyframesFromObject(obj, row.PropertyPath);
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
        foreach (var row in _displayRows)
            if (row.Object?.ObjectId == id) return row.Object;
        var roots = SceneObjectsProvider?.Invoke();
        if (roots != null)
            foreach (var obj in CollectAllObjects(roots))
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
