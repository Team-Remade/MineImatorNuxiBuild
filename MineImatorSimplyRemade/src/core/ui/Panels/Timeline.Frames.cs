using System.Globalization;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Timeline
{
    public event Action<IReadOnlyList<SceneObject>>? KeyframesImporting;

    private bool _exportFramesPending;
    private bool _importFramesPending;
    private string? _framesMessage;

    public void ExportSelectedKeyframes(string path)
    {
        if (_selectedKeyframes.Count == 0)
            throw new InvalidOperationException("Select keyframes to export.");
        int origin = _selectedKeyframes.Min(key => key.Frame);
        var manifest = new NxFramesManifest();
        var objects = new Dictionary<SceneObject, NxFramesObject>();
        foreach (var key in _selectedKeyframes.OrderBy(key => key.Frame))
        {
            if (!_keyframeOwners.TryGetValue(key, out var owner))
                throw new InvalidOperationException("A selected keyframe no longer has an owner.");
            if (!objects.TryGetValue(owner.obj, out var source))
            {
                source = new NxFramesObject { NamePath = FrameObjectNames(owner.obj) };
                objects.Add(owner.obj, source);
                manifest.Objects.Add(source);
            }
            var track = source.Tracks.FirstOrDefault(track => track.PropertyPath == owner.path);
            if (track == null)
            {
                track = new NxFramesTrack { PropertyPath = owner.path, MappingKey = FrameMappingKey(owner.obj, owner.path) };
                source.Tracks.Add(track);
            }
            var entry = new NxFramesKeyframe
            {
                Frame = checked(key.Frame - origin),
                InterpolationType = key.InterpolationType
            };
            if (IsFrameStringPath(owner.path))
            {
                string text = key.Value is JsonElement json && json.ValueKind == JsonValueKind.String
                    ? json.GetString() ?? "" : key.Value as string
                    ?? throw new InvalidDataException($"Invalid string keyframe: {owner.path}.");
                if (IsFrameAssetPath(owner.path, text))
                    text = ProjectManager.Instance.ResolveAssetPath(text);
                entry.Value = JsonSerializer.SerializeToElement(text, AppJsonContext.Default.String);
                if (IsFrameReferencePath(owner.path) && text.Length > 0 && FindObjectById(text) is { } reference)
                    entry.ReferenceNames = FrameObjectNames(reference);
            }
            else
            {
                if (!TryConvertKeyframeValue(key.Value, out float value) || !float.IsFinite(value))
                    throw new InvalidDataException($"Invalid numeric keyframe: {owner.path}.");
                entry.Value = JsonSerializer.SerializeToElement(value, AppJsonContext.Default.Single);
            }
            track.Keyframes.Add(entry);
        }
        NxFramesFile.Write(path, manifest);
    }

    public NxFramesImportResult ImportKeyframes(string path, IEnumerable<SceneObject> destinations, int frame)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        var roots = destinations.Distinct().ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("Select destination objects before importing.");
        var manifest = NxFramesFile.Read(path);
        var skipped = new List<string>();
        var pending = new Dictionary<(SceneObject Object, string Path, int Frame), NxFramesKeyframe>();
        foreach (var source in manifest.Objects)
        {
            foreach (var root in roots)
            {
                var hierarchy = CollectAllObjects([root]).ToArray();
                var target = manifest.Objects.Count == 1 ? root : MatchFrameObject(source.NamePath, hierarchy);
                if (target == null)
                {
                    skipped.Add(Localization.Format("text.panel.timeline.frames_object_unmatched", source.NamePath[^1], root.GetDisplayName()));
                    continue;
                }
                var catalog = GetKeyframePropertyPaths(target).Distinct()
                    .GroupBy(path => FrameMappingKey(target, path), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                foreach (var track in source.Tracks)
                {
                    if (!catalog.TryGetValue(track.MappingKey, out var matches) || matches.Length != 1)
                    {
                        skipped.Add(Localization.Format("text.panel.timeline.frames_property_unmatched", target.GetDisplayName(), track.PropertyPath));
                        continue;
                    }
                    string mappedPath = matches[0];
                    // The two Euler conventions cannot safely be mixed or remapped axis by axis.
                    string? opposite = mappedPath.StartsWith("mirotation.", StringComparison.Ordinal) ? "rotation."
                        : mappedPath.StartsWith("rotation.", StringComparison.Ordinal) ? "mirotation." : null;
                    if (opposite != null && (target.Keyframes.Any(pair => pair.Key.StartsWith(opposite, StringComparison.Ordinal) && pair.Value.Count > 0) ||
                            pending.Keys.Any(key => key.Object == target && key.Path.StartsWith(opposite, StringComparison.Ordinal))))
                    {
                        skipped.Add(Localization.Format("text.panel.timeline.frames_rotation_conflict", target.GetDisplayName(), track.PropertyPath));
                        continue;
                    }
                    foreach (var key in track.Keyframes)
                    {
                        long absoluteFrame = (long)frame + key.Frame;
                        if (absoluteFrame > int.MaxValue / 2 - 30)
                            throw new InvalidDataException("Imported keyframes exceed the supported timeline frame range.");
                        int at = (int)absoluteFrame;
                        JsonElement value = key.Value;
                        if (IsFrameStringPath(mappedPath))
                        {
                            if (value.ValueKind != JsonValueKind.String)
                                throw new InvalidDataException($"Expected a string for {track.PropertyPath}.");
                            string text = value.GetString() ?? "";
                            if (IsFrameReferencePath(mappedPath) && text.Length > 0)
                            {
                                var reference = key.ReferenceNames == null ? null : MatchFrameObject(key.ReferenceNames, hierarchy);
                                if (reference == null)
                                {
                                    skipped.Add(Localization.Format("text.panel.timeline.frames_reference_unmatched", target.GetDisplayName(), track.PropertyPath, at));
                                    continue;
                                }
                                value = JsonSerializer.SerializeToElement(reference.ObjectId, AppJsonContext.Default.String);
                            }
                            else if (IsFrameAssetPath(mappedPath, text) && !File.Exists(text))
                            {
                                skipped.Add(Localization.Format("text.panel.timeline.frames_asset_missing", target.GetDisplayName(), track.PropertyPath, at));
                                continue;
                            }
                        }
                        else if (!TryConvertKeyframeValue(value, out float numeric) || !float.IsFinite(numeric))
                            throw new InvalidDataException($"Expected a finite number for {track.PropertyPath}.");
                        if (TryGetModifier(target, mappedPath, out var modifier, out string channel))
                            _ = WithModifierValue(modifier, channel, value).Validated();
                        var address = (target, mappedPath, at);
                        if (pending.TryGetValue(address, out var previous))
                        {
                            if (previous.Value.GetRawText() != value.GetRawText() || previous.InterpolationType != key.InterpolationType)
                                throw new InvalidDataException($"Multiple sources map conflicting keyframes to {target.GetDisplayName()}: {mappedPath} at {at}.");
                            continue;
                        }
                        pending.Add(address, new NxFramesKeyframe { Frame = at, Value = value, InterpolationType = key.InterpolationType });
                    }
                }
            }
        }
        var changed = pending.Keys.Select(key => key.Object).Distinct().ToArray();
        if (pending.Count == 0)
            return new NxFramesImportResult(0, 0, skipped);
        KeyframesImporting?.Invoke(changed);
        _selectedKeyframes.Clear();
        _keyframeOwners.Clear();
        foreach (var group in pending.GroupBy(pair => (pair.Key.Object, pair.Key.Path)))
        {
            var owner = group.Key;
            _propertyKeyframes.Remove($"{owner.Object.ObjectId}.{owner.Path}");
            LoadKeyframesFromObject(owner.Object, owner.Path);
            var keys = GetKeyframesForProperty(owner.Object, owner.Path);
            foreach (var pair in group)
            {
                var imported = pair.Value;
                keys.RemoveAll(key => key.Frame == imported.Frame);
                var key = new TimelineKeyframe { Frame = imported.Frame, Value = imported.Value, InterpolationType = imported.InterpolationType };
                keys.Add(key);
                _selectedKeyframes.Add(key);
                _keyframeOwners[key] = (owner.Object, owner.Path);
            }
            keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            SaveKeyframesToObject(owner.Object, owner.Path);
        }
        RecalculateTimelineLength();
        RebuildDisplayRows();
        ProjectManager.Instance.SetDirty(true);
        return new NxFramesImportResult(pending.Count, changed.Length, skipped);
    }

    private static bool IsFrameReferencePath(string path) =>
        path is "path_follow.target" or "particle.attractor_target" || IsIkReferencePath(path) || IsModifierReferencePath(path);

    private static bool IsFrameStringPath(string path) =>
        path is "material.texture" or "text.font" || IsFrameReferencePath(path);

    private static bool IsFrameAssetPath(string path, string value) =>
        value.Length > 0 && (path == "material.texture" ||
            path == "text.font" && !value.Equals("minecraftia", StringComparison.OrdinalIgnoreCase));

    private static List<string> FrameObjectNames(SceneObject obj)
    {
        var names = new List<string>();
        for (SceneObject? node = obj; node != null; node = node.Parent)
            names.Insert(0, node.Name ?? "");
        return names;
    }

    private static SceneObject? MatchFrameObject(IReadOnlyList<string> names, IEnumerable<SceneObject> candidates)
    {
        SceneObject? best = null;
        int bestScore = 0;
        bool ambiguous = false;
        foreach (var candidate in candidates.Distinct())
        {
            var candidateNames = FrameObjectNames(candidate);
            int score = 0;
            while (score < names.Count && score < candidateNames.Count &&
                   string.Equals(names[names.Count - score - 1], candidateNames[candidateNames.Count - score - 1], StringComparison.OrdinalIgnoreCase))
                score++;
            if (score > bestScore) { best = candidate; bestScore = score; ambiguous = false; }
            else if (score > 0 && score == bestScore) ambiguous = true;
        }
        return ambiguous ? null : best;
    }

    private static string FrameMappingKey(SceneObject obj, string path)
    {
        if (TryGetModifier(obj, path, out var modifier, out string channel))
            return $"modifier-type.{modifier.Type}.{obj.Modifiers.TakeWhile(item => item.Id != modifier.Id).Count(item => item.Type == modifier.Type)}.{channel}";
        if (TryGetCopyConstraint(obj, path, out var copy, out channel))
            return $"copy-type.{copy.Type}.{obj.CopyConstraints.TakeWhile(item => !ReferenceEquals(item, copy)).Count(item => item.Type == copy.Type)}.{channel}";
        if (TryGetIkSettings(obj, path, out var ik, out channel) && obj is BoneSceneObject bone)
            return $"ik.{bone.Constraints.TakeWhile(item => !ReferenceEquals(item.InverseKinematics, ik)).Count(item => item.Type == BoneConstraintType.InverseKinematics)}.{channel}";
        string[] parts = path.Split('.');
        if (parts.Length == 3 && parts[0] == "shapekey" &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int mesh) &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int shape))
        {
            var meshes = obj.GetMeshInstancesRecursively();
            if (mesh < meshes.Count && shape < meshes[mesh].ShapeKeys.Count)
                return "shapekey-name." + meshes[mesh].ShapeKeys[shape].Name;
        }
        return path;
    }

    private void RenderFramesTransfer()
    {
        string title = Localization.Label("text.panel.timeline.frames_transfer", "Keyframe Transfer");
        if (_exportFramesPending || _importFramesPending)
        {
            bool exporting = _exportFramesPending;
            _exportFramesPending = _importFramesPending = false;
            _framesMessage = null;
            try
            {
                var dialog = exporting
                    ? Dialog.FileSave("nxFrames", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Keyframes.nxFrames"))
                    : Dialog.FileOpen("nxFrames");
                if (dialog.IsError) throw new IOException(dialog.ErrorMessage);
                if (dialog.IsOk && !string.IsNullOrWhiteSpace(dialog.Path))
                {
                    if (exporting)
                    {
                        string path = dialog.Path.EndsWith(".nxFrames", StringComparison.OrdinalIgnoreCase) ? dialog.Path : dialog.Path + ".nxFrames";
                        ExportSelectedKeyframes(path);
                        _framesMessage = Localization.Format("text.panel.timeline.frames_exported", path);
                    }
                    else
                    {
                        var result = ImportKeyframes(dialog.Path, SelectionManager.Instance?.SelectedObjects ?? [], _contextFrame);
                        _framesMessage = Localization.Format("text.panel.timeline.frames_imported", result.KeyframesImported, result.ObjectsChanged);
                        if (result.Skipped.Count > 0)
                            _framesMessage += "\n\n" + Localization.Format("text.panel.timeline.frames_skipped", result.Skipped.Count) +
                                "\n" + string.Join("\n", result.Skipped.Take(20));
                    }
                }
            }
            catch (Exception exception)
            {
                _framesMessage = Localization.Format("text.panel.timeline.frames_failed", exception.Message);
            }
            if (_framesMessage != null) ImGui.OpenPopup(title);
        }
        bool open = true;
        if (ImGui.BeginPopupModal(title, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 520f * UiTheme.Scale);
            ImGui.TextUnformatted(_framesMessage ?? "");
            ImGui.PopTextWrapPos();
            if (ImGui.Button(Localization.Label("text.window.main.close", "Close"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
