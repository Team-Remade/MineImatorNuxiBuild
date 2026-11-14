using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static void FramesTimelineLength(Fixture f)
    {
        var obj = Node("Short animation");
        var timeline = FramesTimeline(obj);
        foreach (int end in new[] { 10, 120, 300, 600 })
        {
            obj.Keyframes.Clear();
            timeline.ImportProjectState(new() { MaxFrames = end });
            timeline.RefreshFromScene();
            Equal(end, timeline.MaxFrames, "Empty scene refresh changed timeline end");
            obj.Keyframes["position.x"] = [new() { Frame = end, Value = 1f }];
            timeline.RefreshFromScene();
            Equal(end, timeline.MaxFrames, "Key at timeline end changed timeline length");
            var saved = timeline.ExportProjectState();
            var restored = FramesTimeline(obj);
            restored.ImportProjectState(saved);
            restored.RefreshFromScene();
            Equal(end, restored.MaxFrames, "Saved timeline end did not survive reload and refresh");
            obj.Keyframes["position.x"].Add(new() { Frame = end + 1, Value = 2f });
            timeline.RefreshFromScene();
            Equal(end + 31, timeline.MaxFrames, "Key past timeline end did not add 30 frames of padding");
        }
    }

    private static unsafe void FramesTimelineMaxInput(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1800, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var timeline = FramesTimeline();
            var drawControls = typeof(Timeline).GetMethod("RenderTransportControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            uint maxId = 0;
            void Draw(float x = -100, float y = -100, bool click = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, click);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 0));
                ImGui.SetNextWindowSize(io.DisplaySize);
                ImGui.Begin("Timeline inputs");
                maxId = ImGui.GetID("##maxf");
                drawControls.Invoke(timeline, null);
                ImGui.End();
                ImGui.Render();
            }
            Draw();
            Draw();
            System.Numerics.Vector2 input = default;
            for (int y = 25; y < 150 && input == default; y += 5)
                for (int x = 10; x < 1750; x += 5)
                {
                    Draw(x, y);
                    if (context.HoveredId != maxId) continue;
                    input = new(x, y);
                    break;
                }
            Check(input != default, "Maximum frames control not reachable");
            io.AddKeyEvent(ImGuiKey.ModCtrl, true);
            Draw(input.X, input.Y, click: true);
            Draw(input.X, input.Y);
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            Draw(input.X, input.Y);
            Check(io.WantTextInput, "Maximum frames control did not enter text editing");
            io.AddInputCharactersUTF8("600");
            Draw(input.X, input.Y);
            io.AddKeyEvent(ImGuiKey.Enter, true);
            Draw(input.X, input.Y);
            io.AddKeyEvent(ImGuiKey.Enter, false);
            Draw();
            Equal(600, timeline.MaxFrames, "Typed maximum frames was lost");
            Equal(30f, timeline.ExportProjectState().FrameRate, "Editing maximum frames changed FPS");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static Timeline FramesTimeline(params SceneObject[] objects)
    {
        var viewport = new Viewport();
        viewport.SceneObjects.AddRange(objects);
        var timeline = new Timeline { Viewport = viewport };
        timeline.LoadKeyframesForAllObjects(objects);
        return timeline;
    }

    private static void SelectFrames(Timeline timeline, params (SceneObject Object, string Path, int Frame)[] addresses)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var selected = (List<TimelineKeyframe>)typeof(Timeline).GetField("_selectedKeyframes", flags)!.GetValue(timeline)!;
        var owners = (Dictionary<TimelineKeyframe, (SceneObject obj, string path)>)typeof(Timeline).GetField("_keyframeOwners", flags)!.GetValue(timeline)!;
        selected.Clear();
        owners.Clear();
        foreach (var address in addresses)
        {
            var keys = (List<TimelineKeyframe>)typeof(Timeline).GetMethod("GetKeyframesForProperty", flags)!
                .Invoke(timeline, [address.Object, address.Path])!;
            var key = keys.Single(key => key.Frame == address.Frame);
            selected.Add(key);
            owners.Add(key, (address.Object, address.Path));
        }
    }

    private static float FrameValue(SceneObject obj, string path, int frame) =>
        Convert.ToSingle(obj.Keyframes[path].Single(key => key.Frame == frame).Value is JsonElement json
            ? json.GetSingle() : obj.Keyframes[path].Single(key => key.Frame == frame).Value);

    private static void FramesRoundtrip(Fixture f)
    {
        var source = Node("Source");
        source.Keyframes["position.x"] =
        [
            new() { Frame = 5, Value = -4f },
            new() { Frame = 20, Value = 2f, InterpolationType = "ease-in-quadratic" },
            new() { Frame = 30, Value = 12f, InterpolationType = "instant" }
        ];
        source.Keyframes["visible"] = [new() { Frame = 25, Value = false }];
        var timeline = FramesTimeline(source);
        SelectFrames(timeline, (source, "position.x", 20), (source, "position.x", 30), (source, "visible", 25));
        string path = f.NewPath(".nxFrames");
        timeline.CopySelectedKeyframes();
        timeline.ExportSelectedKeyframes(path);
        Check(timeline.HasClipboardKeyframes, "Export destroyed clipboard");
        var manifest = NxFramesFile.Read(path);
        Equal(1, manifest.Objects.Count, "Source count");
        Equal(2, manifest.Objects[0].Tracks.Count, "Track count");
        Check(manifest.Objects[0].Tracks[0].Keyframes.Select(key => key.Frame).SequenceEqual([0, 10]), "Selection origin / unselected key leaked");
        Equal("ease-in-quadratic", manifest.Objects[0].Tracks[0].Keyframes[0].InterpolationType, "Interpolation");
        Equal(3, source.Keyframes["position.x"].Count, "Export changed source");
        var first = Node("Different name");
        var second = Node("Other type");
        first.Keyframes["position.x"] = [new() { Frame = 100, Value = 99f }, new() { Frame = 90, Value = -9f }];
        timeline = FramesTimeline(first, second);
        var result = timeline.ImportKeyframes(path, [first, second], 100);
        Equal(6, result.KeyframesImported, "Broadcast key count");
        Equal(2, result.ObjectsChanged, "Broadcast object count");
        Equal(0, result.Skipped.Count, "Compatible properties skipped");
        Equal(3, first.Keyframes["position.x"].Count, "Existing noncolliding key lost");
        Near(-9, FrameValue(first, "position.x", 90), "Preserve earlier key");
        Near(2, FrameValue(first, "position.x", 100), "Replace collision");
        Near(12, FrameValue(second, "position.x", 110), "Relative spacing");
        Check(timeline.HasSelectedKeyframes, "Imported keys not selected");
        typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, 105);
        typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(timeline, [false]);
        Near(4.5f, first.LocalPosition.x, "Imported easing seek");
        Check(!first.ObjectVisible, "Boolean key value");
        var saved = ProjectSceneSerializer.SerializeObjectForLibrary(first);
        Equal(3, saved.Keyframes["position.x"].Count, "Project/object persistence");
        Equal("instant", first.Keyframes["position.x"].Single(key => key.Frame == 110).InterpolationType, "Instant preserved");
    }

    private static void FramesMapping(Fixture f)
    {
        var source = Node("Source rig");
        var arm = Node("Arm");
        var light = new LightSceneObject { Name = "Lamp" };
        light.AssignObjectId();
        source.AddChild(arm);
        source.AddChild(light);
        arm.Modifiers = [new() { Type = SceneModifierType.Array }];
        string sourceModifier = $"modifier.{arm.Modifiers[0].Id}.distance";
        arm.Keyframes[sourceModifier] = [new() { Frame = 10, Value = 4f }];
        arm.CopyConstraints = [new() { Type = CopyConstraintType.Position }];
        arm.Keyframes["copy_constraint.0.offset.x"] = [new() { Frame = 10, Value = 3f }];
        arm.Keyframes["copy_constraint.0.target"] = [new() { Frame = 10, Value = light.ObjectId }];
        light.Keyframes["light.energy"] = [new() { Frame = 15, Value = 6f }];
        light.Keyframes["position.y"] = [new() { Frame = 15, Value = 7f }];
        var timeline = FramesTimeline(source);
        timeline.LoadKeyframesForAllObjects([arm, light]);
        SelectFrames(timeline, (arm, sourceModifier, 10), (arm, "copy_constraint.0.offset.x", 10),
            (arm, "copy_constraint.0.target", 10), (light, "light.energy", 15), (light, "position.y", 15));
        string path = f.NewPath(".nxFrames");
        timeline.ExportSelectedKeyframes(path);
        var destination = Node("Destination rig");
        var mappedArm = Node("arm");
        var mappedLamp = Node("Lamp");
        destination.AddChild(mappedArm);
        destination.AddChild(mappedLamp);
        mappedArm.Modifiers = [new() { Type = SceneModifierType.FollowPath }, new() { Type = SceneModifierType.Array }];
        mappedArm.CopyConstraints = [new() { Type = CopyConstraintType.Scale }, new() { Type = CopyConstraintType.Position }];
        timeline = FramesTimeline(destination);
        var result = timeline.ImportKeyframes(path, [destination, mappedArm], 40);
        Equal(4, result.KeyframesImported, "Hierarchy mapped keys");
        Near(4, FrameValue(mappedArm, $"modifier.{mappedArm.Modifiers[1].Id}.distance", 40), "Modifier ID / type mapping");
        Near(3, FrameValue(mappedArm, "copy_constraint.1.offset.x", 40), "Constraint type mapping");
        Equal(mappedLamp.ObjectId, ((JsonElement)mappedArm.Keyframes["copy_constraint.1.target"][0].Value).GetString(), "Reference remap");
        Near(7, FrameValue(mappedLamp, "position.y", 45), "Common property on different object type");
        Check(result.Skipped.Any(message => message.Contains("light.energy", StringComparison.Ordinal)), "Unsupported light channel not reported");
        Check(!mappedLamp.Keyframes.ContainsKey("light.energy"), "Invalid channel created");
        var duplicate = Node("Lamp");
        destination.AddChild(duplicate);
        result = timeline.ImportKeyframes(path, [destination], 60);
        Check(!mappedLamp.Keyframes["position.y"].Any(key => key.Frame == 65), "Ambiguous name matched arbitrarily");
        Check(result.Skipped.Any(message => message.Contains("unambiguous", StringComparison.Ordinal)), "Ambiguity not reported");
        Check(!mappedArm.Keyframes["copy_constraint.1.target"].Any(key => key.Frame == 60), "Ambiguous reference copied original ID");
    }

    private static void FramesAdvancedMapping(Fixture f)
    {
        var source = Node("Face");
        var sourceMesh = new Mesh(null!);
        sourceMesh.ShapeKeys.Add(new() { Name = "Smile" });
        sourceMesh.ShapeKeys.Add(new() { Name = "Blink" });
        source.Visuals.Add(sourceMesh);
        source.Keyframes["shapekey.0.0"] = [new() { Frame = 0, Value = 0.75f }];
        source.Keyframes["rotation.x"] = [new() { Frame = 0, Value = 45f }];
        var timeline = FramesTimeline(source);
        SelectFrames(timeline, (source, "shapekey.0.0", 0), (source, "rotation.x", 0));
        string path = f.NewPath(".nxFrames");
        timeline.ExportSelectedKeyframes(path);
        var target = Node("New face");
        var targetMesh = new Mesh(null!);
        targetMesh.ShapeKeys.Add(new() { Name = "Blink" });
        targetMesh.ShapeKeys.Add(new() { Name = "Smile" });
        target.Visuals.Add(targetMesh);
        target.Keyframes["mirotation.x"] = [new() { Frame = 0, Value = 10f }];
        timeline = FramesTimeline(target);
        var result = timeline.ImportKeyframes(path, [target], 5);
        Equal(1, result.KeyframesImported, "Rotation convention conflict not skipped");
        Near(0.75f, FrameValue(target, "shapekey.0.1", 5), "Shape name remapping after reorder");
        Check(!target.Keyframes.ContainsKey("shapekey.0.0"), "Mapped by shape index instead of name");
        Check(!target.Keyframes.ContainsKey("rotation.x"), "Mixed Euler conventions");
        Equal(1, result.Skipped.Count, "Rotation conflict summary");
        targetMesh.ShapeKeys.Add(new() { Name = "Smile" });
        result = timeline.ImportKeyframes(path, [target], 6);
        Equal(0, result.KeyframesImported, "Ambiguous shape name selected arbitrarily");

        var rig = Node("Original rig");
        var left = Node("Left");
        var right = Node("Right");
        var leftHand = new BoneSceneObject { Name = "Hand" };
        var rightHand = new BoneSceneObject { Name = "Hand" };
        leftHand.AssignObjectId();
        rightHand.AssignObjectId();
        rig.AddChild(left);
        rig.AddChild(right);
        left.AddChild(leftHand);
        right.AddChild(rightHand);
        leftHand.Constraints.Add(new());
        leftHand.Keyframes["constraint.0.ik.influence"] = [new() { Frame = 0, Value = 0.4f }];
        rightHand.Keyframes["position.z"] = [new() { Frame = 3, Value = 2f }];
        timeline = FramesTimeline(rig);
        timeline.LoadKeyframesForAllObjects([leftHand, rightHand]);
        SelectFrames(timeline, (leftHand, "constraint.0.ik.influence", 0), (rightHand, "position.z", 3));
        timeline.ExportSelectedKeyframes(path);
        var newRig = Node("Different root");
        var newLeft = Node("Left");
        var newRight = Node("Right");
        var newLeftHand = new BoneSceneObject { Name = "Hand" };
        var newRightHand = new BoneSceneObject { Name = "Hand" };
        newLeftHand.AssignObjectId();
        newRightHand.AssignObjectId();
        newLeftHand.Constraints.Add(new());
        newRig.AddChild(newRight);
        newRig.AddChild(newLeft);
        newRight.AddChild(newRightHand);
        newLeft.AddChild(newLeftHand);
        timeline = FramesTimeline(newRig);
        result = timeline.ImportKeyframes(path, [newRig], 10);
        Equal(2, result.KeyframesImported, "Repeated hand names not disambiguated by ancestors");
        Near(0.4f, FrameValue(newLeftHand, "constraint.0.ik.influence", 10), "IK property");
        Near(2, FrameValue(newRightHand, "position.z", 13), "Correct repeated-name hand");
        Check(!newLeftHand.Keyframes.ContainsKey("position.z"), "Wrong hand animated");
    }

    private static void FramesValidationHistory(Fixture f)
    {
        var source = Node("Animated");
        source.Keyframes["position.x"] = [new() { Frame = 2, Value = 8f }];
        var timeline = FramesTimeline(source);
        SelectFrames(timeline, (source, "position.x", 2));
        string path = f.NewPath(".nxFrames");
        timeline.ExportSelectedKeyframes(path);
        var destination = Node("Other");
        timeline = FramesTimeline(destination);
        var before = new Dictionary<SceneObject, ProjectSceneObjectEntry>();
        timeline.KeyframesImporting += objects =>
        {
            foreach (var obj in objects) before.Add(obj, ProjectSceneSerializer.CaptureObjectState(obj));
        };
        var hierarchy = new SceneHistoryAction.HierarchyState(timeline.Viewport);
        var project = SceneHistoryAction.ProjectState.Capture(ProjectManager.Instance.Manifest);
        var result = timeline.ImportKeyframes(path, [destination], 3);
        Equal(1, before.Count, "Pre-import history snapshot");
        var action = SceneHistoryAction.Create(before.Select(pair =>
            (pair.Key, pair.Value, ProjectSceneSerializer.CaptureObjectState(pair.Key))), hierarchy, hierarchy, project, project)!;
        Check(action.Apply(timeline.Viewport, null, undo: true), "Import undo failed");
        Check(!destination.Keyframes.ContainsKey("position.x"), "Undo did not restore keys");
        Check(action.Apply(timeline.Viewport, null, undo: false), "Import redo failed");
        Near(8, FrameValue(destination, "position.x", 3), "Redo key");
        before.Clear();
        var manifest = NxFramesFile.Read(path);
        manifest.Objects[0].Tracks[0].Keyframes.Add(new NxFramesKeyframe
        {
            Frame = int.MaxValue, Value = JsonSerializer.SerializeToElement(1f), InterpolationType = "linear"
        });
        NxFramesFile.Write(path, manifest);
        Reject(() => timeline.ImportKeyframes(path, [destination], 1), "Frame overflow accepted");
        Equal(0, before.Count, "Overflow started mutation");
        Equal(1, destination.Keyframes["position.x"].Count, "Overflow partially imported earlier valid key");
        foreach (string content in new[]
                 {
                     "null", "{}", "{\"Format\":\"nxFrames\",\"Version\":2,\"Objects\":[]}",
                     "{\"Format\":\"nxFrames\",\"Version\":1,\"Objects\":[null]}"
                 })
        {
            File.WriteAllText(path, content);
            Reject(() => timeline.ImportKeyframes(path, [destination], 0), "Malformed file accepted");
            Near(8, FrameValue(destination, "position.x", 3), "Malformed file changed keys");
        }
        manifest.Objects[0].Tracks[0].Keyframes[0].Value = JsonSerializer.SerializeToElement(new[] { 1f, 2f });
        string original = File.ReadAllText(path);
        Reject(() => NxFramesFile.Write(path, manifest), "Vector accepted for scalar track");
        Equal(original, File.ReadAllText(path), "Rejected export replaced existing file");
        Check(!Directory.EnumerateFiles(f.Root, "*.tmp").Any(), "Temporary file leaked");
        var text = Node("Text");
        text.ObjectType = "Text Mesh";
        text.Keyframes["text.font"] = [new() { Frame = 0, Value = "minecraftia" }];
        text.Keyframes["material.texture"] = [new() { Frame = 0, Value = f.NewPath(".png") }];
        timeline = FramesTimeline(text);
        SelectFrames(timeline, (text, "text.font", 0), (text, "material.texture", 0));
        timeline.ExportSelectedKeyframes(path);
        var textTarget = Node("Text target");
        textTarget.ObjectType = "Text Mesh";
        timeline = FramesTimeline(textTarget);
        result = timeline.ImportKeyframes(path, [textTarget], 0);
        Equal(1, result.KeyframesImported, "Built-in font skipped");
        Equal("minecraftia", ((JsonElement)textTarget.Keyframes["text.font"][0].Value).GetString(), "Built-in font changed");
        Equal(1, result.Skipped.Count, "Missing asset not reported");
        var invalid = NxFramesFile.Read(path);
        invalid.Objects[0].Tracks[0].Keyframes[0].Frame = -1;
        Reject(() => NxFramesFile.Write(path, invalid), "Negative frame accepted");
        invalid = NxFramesFile.Read(path);
        invalid.Objects[0].Tracks[0].Keyframes.Add(invalid.Objects[0].Tracks[0].Keyframes[0]);
        Reject(() => NxFramesFile.Write(path, invalid), "Duplicate frame accepted");
        using (var oversized = File.Create(path)) oversized.SetLength(32L * 1024 * 1024 + 1);
        Reject(() => NxFramesFile.Read(path), "Oversized file accepted");

        source.Keyframes["position.y"] = [new() { Frame = 2, Value = 4f }];
        timeline = FramesTimeline(source);
        SelectFrames(timeline, (source, "position.x", 2), (source, "position.y", 2));
        timeline.ExportSelectedKeyframes(path);
        var conflict = NxFramesFile.Read(path);
        var otherSource = new NxFramesObject
        {
            NamePath = ["Elsewhere", destination.Name],
            Tracks = [new NxFramesTrack
            {
                PropertyPath = "position.x", MappingKey = "position.x",
                Keyframes = [new() { Frame = 0, Value = JsonSerializer.SerializeToElement(99f), InterpolationType = "linear" }]
            }]
        };
        conflict.Objects[0].NamePath = [destination.Name];
        conflict.Objects.Add(otherSource);
        NxFramesFile.Write(path, conflict);
        timeline = FramesTimeline(destination);
        Reject(() => timeline.ImportKeyframes(path, [destination], 9), "Conflicting source mappings accepted");
        Check(!destination.Keyframes.ContainsKey("position.y"), "Conflicting file partially changed an earlier valid track");
        Check(!destination.Keyframes["position.x"].Any(key => key.Frame == 9), "Conflicting file partially inserted key");
    }

    private static unsafe void FramesContextMenu(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = Node("Context");
            obj.Keyframes["position.x"] = [new() { Frame = 0, Value = 1f }];
            var timeline = FramesTimeline(obj);
            SelectFrames(timeline, (obj, "position.x", 0));
            SelectionManager.Instance.ClearSelection();
            SelectionManager.Instance.SelectObject(obj);
            uint actionId = 0;
            uint hovered = 0;
            string actionLabel = Localization.Label("text.panel.timeline.export_frames", "Export Selected Keyframes (.nxFrames)...");
            System.Numerics.Vector2 popupPos = default, popupSize = default;
            void Draw(float x = -100, float y = -100, bool click = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, click);
                ImGui.NewFrame();
                ImGui.Begin("Frames context");
                ImGui.OpenPopup("##kf_ctx");
                typeof(Timeline).GetMethod("RenderContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, null);
                if (x < 0) Check(ImGui.IsPopupOpen("##kf_ctx"), "Timeline context menu failed to render");
                if (ImGui.BeginPopup("##kf_ctx"))
                {
                    actionId = ImGui.GetID(actionLabel);
                    popupPos = ImGui.GetWindowPos();
                    popupSize = ImGui.GetWindowSize();
                    ImGui.EndPopup();
                }
                hovered = context.HoveredId;
                ImGui.End();
                ImGui.Render();
            }
            Draw();
            Draw();
            foreach (string key in new[] { "export_frames", "import_frames", "frames_transfer", "frames_exported", "frames_imported", "frames_skipped", "frames_failed" })
                Check(Localization.T("text.panel.timeline." + key) != "text.panel.timeline." + key, "Missing frames localization: " + key);
            Check(!(bool)typeof(Timeline).GetField("_exportFramesPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!,
                "Drawing context menu started native dialog without a click");
            float actionY = -1;
            float actionX = popupPos.X + Math.Min(100, popupSize.X / 2);
            for (float y = popupPos.Y + 5; y < popupPos.Y + popupSize.Y; y += 3)
            {
                Draw(actionX, y);
                if (hovered == actionId) { actionY = y; break; }
            }
            Check(actionY >= 0, "Export keyframe menu item not reachable");
            Draw(actionX, actionY, click: true);
            Draw(actionX, actionY);
            Check((bool)typeof(Timeline).GetField("_exportFramesPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!,
                "Export menu click did not schedule transfer");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
