using System.Globalization;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>RmlUi transport and scrub controller for the animation timeline.</summary>
public sealed class RmlTimelineController
{
    private readonly Element _root;
    private readonly Timeline _timeline;
    private int _displayedFrame = -1;
    private bool _displayedPlaying;
    private string _signature = string.Empty;
    private SceneObject? _selectedKeyObject;
    private string _selectedKeyPath = string.Empty;
    private int _selectedKeyFrame = -1;

    public RmlTimelineController(Element root, Timeline timeline)
    {
        _root = root;
        _timeline = timeline;
        Rebuild(force: true);
    }

    public void Update(bool force = false)
    {
        Rebuild(force);
        if (!force && _displayedFrame == _timeline.CurrentFrame && _displayedPlaying == _timeline.IsPlaying) return;
        _displayedFrame = _timeline.CurrentFrame;
        _displayedPlaying = _timeline.IsPlaying;
        _root.GetElementById("timeline-frame")?.SetInnerRml($"Frame {_timeline.CurrentFrame} / {_timeline.MaxFrames}");
        _root.GetElementById("timeline-time")?.SetInnerRml($"{_timeline.CurrentFrame / _timeline.Framerate:0.00}s");
        _root.GetElementById("timeline-play")?.SetInnerRml(_timeline.IsPlaying ? "Pause" : "Play");
        if (_root.GetElementById("timeline-scrub") is ElementFormControlInput scrub)
            scrub.SetValue(_timeline.CurrentFrame.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Rebuilds the entire panel body (transport bar, scrub bar, settings row and keyframe
    /// track rows) in a single SetInnerRml call. Note: RmlUi's SetInnerRml on a *nested* element
    /// (e.g. previously a separate "track-area" child re-rendered on its own) after the parent
    /// element already had its own children replaced corrupts rendering of the whole ancestor
    /// subtree - the panel would draw nothing at all afterwards. Building the full body content in
    /// one shot on the root element avoids ever calling SetInnerRml on a non-root element here.</summary>
    private void Rebuild(bool force)
    {
        SceneObject? obj = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        string signature = obj == null ? "none" : string.Join('|', obj.Keyframes.OrderBy(pair => pair.Key).Select(pair =>
            $"{obj.ObjectId}:{pair.Key}:{string.Join(',', pair.Value.Select(key => $"{key.Frame}:{key.InterpolationType}"))}"));
        signature += "|audio=" + string.Join(',', _timeline.AudioTracks.Select(track =>
            $"{track.ManifestEntry.AssetDisplayName}:{track.ManifestEntry.StartFrame}:{track.ManifestEntry.Volume}:{track.ManifestEntry.Muted}:{track.ManifestEntry.Loop}"));
        signature += "|markers=" + string.Join(',', _timeline.Markers.Select(marker => $"{marker.Frame}:{marker.Label}"));
        signature += $"|region={_timeline.PlaybackRegionStart}:{_timeline.PlaybackRegionEnd}";
        signature += $"|selected={_selectedKeyPath}:{_selectedKeyFrame}";
        signature += $"|fps={_timeline.Framerate}|len={_timeline.MaxFrames}|auto={_timeline.AutoKeyframe}|loop={_timeline.LoopPlayback}";
        if (!force && signature == _signature) return;
        _signature = signature;

        var html = new System.Text.StringBuilder($$"""
          <div id="transport"><button id="timeline-start">Start</button><button id="timeline-back">-1</button>
            <button id="timeline-play">Play</button><button id="timeline-forward">+1</button><button id="timeline-end">End</button>
            <span id="timeline-frame"></span><span id="timeline-time"></span></div>
          <div id="scrub-row"><input id="timeline-scrub" type="range" min="0" max="{{_timeline.MaxFrames}}" value="{{_timeline.CurrentFrame}}"/></div>
          <div id="timeline-settings">FPS <input id="timeline-fps" value="{{_timeline.Framerate.ToString(CultureInfo.InvariantCulture)}}"/>
            End <input id="timeline-length" value="{{_timeline.MaxFrames}}"/><button id="timeline-auto">Auto: {{(_timeline.AutoKeyframe ? "On" : "Off")}}</button>
            <button id="timeline-loop">Loop: {{(_timeline.LoopPlayback ? "On" : "Off")}}</button><button id="timeline-zoom-out">Zoom -</button>
            <button id="timeline-zoom-in">Zoom +</button><button id="timeline-region">Region 0-now</button><button id="timeline-region-clear">Clear region</button>
            <button id="timeline-marker-add">+ Marker</button></div>
          <div id="track-area">
          """);

        if (obj == null) html.Append("<div class='track-placeholder'>Select an object to edit its keyframes.</div>");
        else
        {
            if (_selectedKeyObject == obj && _selectedKeyFrame >= 0)
                html.Append("<div class='key-editor'>Frame <input id='key-frame-edit' value='").Append(_selectedKeyFrame)
                    .Append("'/> <button id='key-move'>Move</button><button id='key-interp'>Interpolation</button><button id='key-delete'>Delete</button></div>");
            int row = 0;
            foreach ((string path, List<ObjectKeyframe> keys) in obj.Keyframes.OrderBy(pair => pair.Key))
            {
                html.Append("<div class='rml-track'><span class='track-label'>").Append(Escape(path)).Append("</span>");
                int keyIndex = 0;
                foreach (ObjectKeyframe key in keys.OrderBy(item => item.Frame))
                {
                    float position = 20f + Math.Clamp(key.Frame / (float)Math.Max(1, _timeline.MaxFrames), 0, 1) * 74f;
                    bool selected = ReferenceEquals(_selectedKeyObject, obj) && _selectedKeyPath == path && _selectedKeyFrame == key.Frame;
                    html.Append("<button id='key-").Append(row).Append('-').Append(keyIndex).Append("' class='key")
                        .Append(selected ? " selected" : "").Append("' style='left:").Append(position.ToString("0.##", CultureInfo.InvariantCulture)).Append("%'>◆</button>");
                    keyIndex++;
                }
                html.Append("<button id='track-add-").Append(row).Append("' class='track-add'>+ Key</button></div>");
                row++;
            }
            if (obj.Keyframes.Count == 0)
                html.Append("<div class='track-placeholder'>Edit an animatable property, or enable auto-keyframing, to create tracks.</div>");
        }
        foreach (TimelineAudioTrack track in _timeline.AudioTracks)
            html.Append("<div class='audio-row'>Audio · ").Append(Escape(string.IsNullOrWhiteSpace(track.ManifestEntry.DisplayName) ? track.ManifestEntry.AssetDisplayName : track.ManifestEntry.DisplayName)).Append(" · frame ").Append(track.ManifestEntry.StartFrame).Append("</div>");
        html.Append("</div>");

        _root.SetInnerRml(html.ToString());
        Console.WriteLine($"[DEBUG_LOG] Rebuild: len={_root.GetInnerRml().Length} transport={_root.GetElementById("transport") != null} rootW={_root.GetClientWidth()} rootH={_root.GetClientHeight()}");

        Bind("timeline-start", () => _timeline.SetCurrentFrame(0));
        Bind("timeline-back", () => _timeline.SetCurrentFrame(_timeline.CurrentFrame - 1));
        Bind("timeline-play", _timeline.TogglePlayPause);
        Bind("timeline-forward", () => _timeline.SetCurrentFrame(Math.Min(_timeline.MaxFrames, _timeline.CurrentFrame + 1)));
        Bind("timeline-end", () => _timeline.SetCurrentFrame(_timeline.MaxFrames));
        Bind("timeline-auto", () => { _timeline.SetAutoKeyframe(!_timeline.AutoKeyframe); Rebuild(force: true); });
        Bind("timeline-loop", () => { _timeline.SetLoopPlayback(!_timeline.LoopPlayback); Rebuild(force: true); });
        Bind("timeline-zoom-out", () => _timeline.SetTimelineZoom(_timeline.PixelsPerFrame / 1.25f));
        Bind("timeline-zoom-in", () => _timeline.SetTimelineZoom(_timeline.PixelsPerFrame * 1.25f));
        Bind("timeline-region", () => _timeline.SetPlaybackRegion(0, _timeline.CurrentFrame));
        Bind("timeline-region-clear", () => _timeline.SetPlaybackRegion(null, null));
        Bind("timeline-marker-add", () => { _timeline.AddMarker(_timeline.CurrentFrame, "Marker"); Rebuild(force: true); });
        BindNumber("timeline-fps", _timeline.SetFrameRate);
        BindInteger("timeline-length", value => { _timeline.SetMaxFrames(value); Rebuild(force: true); });
        if (_root.GetElementById("timeline-scrub") is ElementFormControlInput scrub)
        {
            void Scrub()
            {
                if (int.TryParse(scrub.GetValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
                    _timeline.SetCurrentFrame(frame);
                Update(force: true);
            }
            scrub.AddEventListener("change", _ => Scrub());
            scrub.AddEventListener("input", _ => Scrub());
        }

        if (obj != null)
        {
            int row = 0;
            foreach ((string path, List<ObjectKeyframe> keys) in obj.Keyframes.OrderBy(pair => pair.Key))
            {
                string capturedPath = path;
                int keyIndex = 0;
                foreach (ObjectKeyframe key in keys.OrderBy(item => item.Frame))
                {
                    int capturedFrame = key.Frame;
                    Bind($"key-{row}-{keyIndex}", () => { _selectedKeyObject = obj; _selectedKeyPath = capturedPath; _selectedKeyFrame = capturedFrame; _timeline.SetCurrentFrame(capturedFrame); Rebuild(force: true); });
                    keyIndex++;
                }
                Bind($"track-add-{row}", () => { _timeline.AddKeyframeForProperty(obj, capturedPath, _timeline.CurrentFrame); Rebuild(force: true); });
                row++;
            }
        }
        Bind("key-delete", DeleteSelectedKey);
        Bind("key-move", MoveSelectedKey);
        Bind("key-interp", CycleInterpolation);
    }

    private void DeleteSelectedKey() { if (_selectedKeyObject == null || _selectedKeyFrame < 0) return; _timeline.RemoveKeyframeForProperty(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame); _selectedKeyFrame = -1; Rebuild(force: true); }
    private void MoveSelectedKey() { if (_selectedKeyObject == null || _root.GetElementById("key-frame-edit") is not ElementFormControlInput input || !int.TryParse(input.GetValue(), out int frame)) return; frame = Math.Clamp(frame, 0, _timeline.MaxFrames); _timeline.MoveKeyframe(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame, frame); _selectedKeyFrame = frame; _timeline.SetCurrentFrame(frame); Rebuild(force: true); }
    private void CycleInterpolation() { if (_selectedKeyObject == null) return; ObjectKeyframe? key = _selectedKeyObject.Keyframes.GetValueOrDefault(_selectedKeyPath)?.FirstOrDefault(item => item.Frame == _selectedKeyFrame); string next = key?.InterpolationType == "linear" ? "cubic" : key?.InterpolationType == "cubic" ? "step" : "linear"; _timeline.SetKeyframeInterpolation(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame, next); Rebuild(force: true); }
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private void BindInteger(string id, Action<int> setter) => BindInput(id, value =>
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) setter(number);
    });
    private void BindNumber(string id, Action<float> setter) => BindInput(id, value =>
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) setter(number);
    });
    private void BindInput(string id, Action<string> setter)
    {
        if (_root.GetElementById(id) is ElementFormControlInput input)
            input.AddEventListener("change", _ => { setter(input.GetValue()); Update(force: true); });
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ =>
    {
        action();
        Update(force: true);
    });
}
