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
    private string _trackSignature = string.Empty;
    private SceneObject? _selectedKeyObject;
    private string _selectedKeyPath = string.Empty;
    private int _selectedKeyFrame = -1;

    public RmlTimelineController(Element root, Timeline timeline)
    {
        _root = root;
        _timeline = timeline;
        Build();
        Update(force: true);
    }

    public void Update(bool force = false)
    {
        RefreshTracks();
        if (!force && _displayedFrame == _timeline.CurrentFrame && _displayedPlaying == _timeline.IsPlaying) return;
        _displayedFrame = _timeline.CurrentFrame;
        _displayedPlaying = _timeline.IsPlaying;
        _root.GetElementById("timeline-frame")?.SetInnerRml($"Frame {_timeline.CurrentFrame} / {_timeline.MaxFrames}");
        _root.GetElementById("timeline-time")?.SetInnerRml($"{_timeline.CurrentFrame / _timeline.Framerate:0.00}s");
        _root.GetElementById("timeline-play")?.SetInnerRml(_timeline.IsPlaying ? "Pause" : "Play");
        if (_root.GetElementById("timeline-scrub") is ElementFormControlInput scrub)
            scrub.SetValue(_timeline.CurrentFrame.ToString(CultureInfo.InvariantCulture));
    }

    private void Build()
    {
        _root.SetInnerRml($$"""
          <style>
            #transport{height:38px;display:flex;flex-direction:row;align-items:center;padding:4px;border-bottom:1px #111216;}
            #transport button{background:#30323a;border:1px #4b4d58;margin-right:4px;min-width:48px;}
            #timeline-frame{margin-left:8px;color:#d9b24e;}#timeline-time{margin-left:10px;color:#9296a2;}
            #scrub-row{height:42px;padding:8px;}#timeline-scrub{width:100%;}
            #timeline-settings{height:34px;display:flex;flex-direction:row;align-items:center;padding:3px 7px;border-bottom:1px #111216;}
            #timeline-settings input{width:54px;margin-right:6px;background:#17181d;color:#eee;border:1px #4b4d58;}
            #track-area{position:absolute;top:114px;bottom:0;left:0;right:0;overflow:auto;background:#1b1c21;}
            .track-placeholder{padding:12px;color:#888c97;}
          </style>
          <div id="transport"><button id="timeline-start">Start</button><button id="timeline-back">-1</button>
            <button id="timeline-play">Play</button><button id="timeline-forward">+1</button><button id="timeline-end">End</button>
            <span id="timeline-frame"></span><span id="timeline-time"></span></div>
          <div id="scrub-row"><input id="timeline-scrub" type="range" min="0" max="{{_timeline.MaxFrames}}" value="{{_timeline.CurrentFrame}}"/></div>
          <div id="timeline-settings">FPS <input id="timeline-fps" value="{{_timeline.Framerate.ToString(CultureInfo.InvariantCulture)}}"/>
            End <input id="timeline-length" value="{{_timeline.MaxFrames}}"/><button id="timeline-auto">Auto: {{(_timeline.AutoKeyframe ? "On" : "Off")}}</button>
            <button id="timeline-loop">Loop: {{(_timeline.LoopPlayback ? "On" : "Off")}}</button><button id="timeline-zoom-out">Zoom -</button>
            <button id="timeline-zoom-in">Zoom +</button><button id="timeline-region">Region 0-now</button><button id="timeline-region-clear">Clear region</button>
            <button id="timeline-marker-add">+ Marker</button></div>
          <div id="track-area"></div>
          """);
        Bind("timeline-start", () => _timeline.SetCurrentFrame(0));
        Bind("timeline-back", () => _timeline.SetCurrentFrame(_timeline.CurrentFrame - 1));
        Bind("timeline-play", _timeline.TogglePlayPause);
        Bind("timeline-forward", () => _timeline.SetCurrentFrame(Math.Min(_timeline.MaxFrames, _timeline.CurrentFrame + 1)));
        Bind("timeline-end", () => _timeline.SetCurrentFrame(_timeline.MaxFrames));
        Bind("timeline-auto", () => { _timeline.SetAutoKeyframe(!_timeline.AutoKeyframe); Build(); });
        Bind("timeline-loop", () => { _timeline.SetLoopPlayback(!_timeline.LoopPlayback); Build(); });
        Bind("timeline-zoom-out", () => _timeline.SetTimelineZoom(_timeline.PixelsPerFrame / 1.25f));
        Bind("timeline-zoom-in", () => _timeline.SetTimelineZoom(_timeline.PixelsPerFrame * 1.25f));
        Bind("timeline-region", () => _timeline.SetPlaybackRegion(0, _timeline.CurrentFrame));
        Bind("timeline-region-clear", () => _timeline.SetPlaybackRegion(null, null));
        Bind("timeline-marker-add", () => { _timeline.AddMarker(_timeline.CurrentFrame, "Marker"); _trackSignature = string.Empty; });
        BindNumber("timeline-fps", _timeline.SetFrameRate);
        BindInteger("timeline-length", value => { _timeline.SetMaxFrames(value); Build(); });
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
    }

    private void RefreshTracks()
    {
        SceneObject? obj = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        string signature = obj == null ? "none" : string.Join('|', obj.Keyframes.OrderBy(pair => pair.Key).Select(pair =>
            $"{obj.ObjectId}:{pair.Key}:{string.Join(',', pair.Value.Select(key => $"{key.Frame}:{key.InterpolationType}"))}"));
        signature += "|audio=" + string.Join(',', _timeline.AudioTracks.Select(track =>
            $"{track.ManifestEntry.AssetDisplayName}:{track.ManifestEntry.StartFrame}:{track.ManifestEntry.Volume}:{track.ManifestEntry.Muted}:{track.ManifestEntry.Loop}"));
        signature += "|markers=" + string.Join(',', _timeline.Markers.Select(marker => $"{marker.Frame}:{marker.Label}"));
        signature += $"|region={_timeline.PlaybackRegionStart}:{_timeline.PlaybackRegionEnd}";
        signature += $"|selected={_selectedKeyPath}:{_selectedKeyFrame}";
        if (signature == _trackSignature) return;
        _trackSignature = signature;

        Element? area = _root.GetElementById("track-area");
        if (area == null) return;
        var html = new System.Text.StringBuilder("""
          <style>.rml-track{height:31px;position:relative;border-bottom:1px #30323a;padding-left:145px;}.track-label{position:absolute;left:6px;top:6px;width:132px;overflow:hidden;color:#b8bbc5;}.key{position:absolute;top:5px;width:21px;height:21px;padding:0;background:#c39232;border:1px #f0c65c;}.key.selected{background:#f0e06a}.track-add{position:absolute;right:4px;top:4px;padding:2px 6px;background:#343640;border:1px #555865;}.key-editor{height:34px;padding:4px 7px;background:#24262c;border-bottom:1px #111216;}.key-editor input{width:60px;background:#17181d;color:#eee;border:1px #4b4d58;}.audio-row{height:28px;padding:5px 7px;color:#85b5da;border-bottom:1px #30323a;}</style>
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
        area.SetInnerRml(html.ToString());

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
                    Bind($"key-{row}-{keyIndex}", () => { _selectedKeyObject = obj; _selectedKeyPath = capturedPath; _selectedKeyFrame = capturedFrame; _timeline.SetCurrentFrame(capturedFrame); _trackSignature = string.Empty; RefreshTracks(); });
                    keyIndex++;
                }
                Bind($"track-add-{row}", () => { _timeline.AddKeyframeForProperty(obj, capturedPath, _timeline.CurrentFrame); _trackSignature = string.Empty; RefreshTracks(); });
                row++;
            }
        }
        Bind("key-delete", DeleteSelectedKey);
        Bind("key-move", MoveSelectedKey);
        Bind("key-interp", CycleInterpolation);
    }

    private void DeleteSelectedKey() { if (_selectedKeyObject == null || _selectedKeyFrame < 0) return; _timeline.RemoveKeyframeForProperty(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame); _selectedKeyFrame = -1; _trackSignature = string.Empty; RefreshTracks(); }
    private void MoveSelectedKey() { if (_selectedKeyObject == null || _root.GetElementById("key-frame-edit") is not ElementFormControlInput input || !int.TryParse(input.GetValue(), out int frame)) return; frame = Math.Clamp(frame, 0, _timeline.MaxFrames); _timeline.MoveKeyframe(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame, frame); _selectedKeyFrame = frame; _timeline.SetCurrentFrame(frame); _trackSignature = string.Empty; RefreshTracks(); }
    private void CycleInterpolation() { if (_selectedKeyObject == null) return; ObjectKeyframe? key = _selectedKeyObject.Keyframes.GetValueOrDefault(_selectedKeyPath)?.FirstOrDefault(item => item.Frame == _selectedKeyFrame); string next = key?.InterpolationType == "linear" ? "cubic" : key?.InterpolationType == "cubic" ? "step" : "linear"; _timeline.SetKeyframeInterpolation(_selectedKeyObject, _selectedKeyPath, _selectedKeyFrame, next); _trackSignature = string.Empty; RefreshTracks(); }
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
            input.AddEventListener("change", _ => { setter(input.GetValue()); _trackSignature = string.Empty; Update(force: true); });
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ =>
    {
        action();
        Update(force: true);
    });
}
