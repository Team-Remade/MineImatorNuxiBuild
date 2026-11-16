using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static void FramesRetiming(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        Services.Initialize();
        try
        {
            var obj = Node("Retimed");
            obj.Keyframes["position.x"] =
            [
                new() { Frame = 10, Value = 1f, InterpolationType = "ease_in" },
                new() { Frame = 20, Value = 2f },
                new() { Frame = 30, Value = 3f },
                new() { Frame = 40, Value = 99f }
            ];
            obj.Keyframes["position.y"] = [new() { Frame = 15, Value = 4f }, new() { Frame = 25, Value = 5f }];
            var timeline = FramesTimeline(obj);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            List<TimelineKeyframe> Track(string path) => (List<TimelineKeyframe>)typeof(Timeline)
                .GetMethod("GetKeyframesForProperty", flags)!.Invoke(timeline, [obj, path])!;
            void Expect(string expected, string message) =>
                Equal(expected, string.Join(",", Track("position.x").Select(k => k.Frame)), message);
            void Preview(double scale) => typeof(Timeline).GetMethod("PreviewKeyframeRetiming", flags)!.Invoke(timeline, [scale]);
            void Finish(bool commit) => typeof(Timeline).GetMethod("FinishKeyframeRetiming", flags)!.Invoke(timeline, [commit]);
            void BeginScale(int pivot)
            {
                timeline.SetCurrentFrame(pivot);
                var mode = Enum.Parse(typeof(Timeline).GetNestedType("KeyframeRetimingMode", BindingFlags.NonPublic)!, "Scale");
                Check((bool)typeof(Timeline).GetMethod("BeginKeyframeRetiming", flags)!
                    .Invoke(timeline, [mode, pivot, 0])!, "Scale gesture starts");
            }
            bool BeginStretch(string path, int frame) => (bool)typeof(Timeline).GetMethod("BeginKeyframeStretch", flags)!
                .Invoke(timeline, [Track(path).Single(k => k.Frame == frame)])!;
            void Select() => SelectFrames(timeline, (obj, "position.x", 10), (obj, "position.x", 20),
                (obj, "position.x", 30), (obj, "position.y", 15), (obj, "position.y", 25));
            Select();
            BeginScale(20);
            Preview(100);
            Expect("0,20,40,40", "Playhead pivot limits scaling at frame zero without destroying preview collisions");
            Equal("10,20,30,40", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)),
                "Preview leaves persisted keys untouched");
            Preview(0.5);
            Expect("15,20,25,40", "Every preview scales from original timing, not the previous preview");
            Preview(-1);
            Expect("20,20,20,40", "Negative scale clamps instead of inverting timing");
            Finish(false);
            Expect("10,20,30,40", "Cancel restores all previewed keys and collisions");
            int previousLength = timeline.MaxFrames;
            BeginScale(0);
            Preview(100);
            Equal(previousLength, timeline.MaxFrames, "Preview does not permanently expand timeline length");
            Finish(false);
            Select();
            Check(!BeginStretch("position.x", 20), "Interior selected key does not start stretching");
            Check(BeginStretch("position.x", 10), "Left selection edge starts stretching");
            Preview(2);
            Expect("0,15,30,40", "Left stretch anchors the opposite edge and clamps at zero");
            Finish(false);
            Check(BeginStretch("position.y", 25), "A row-local right edge starts stretching");
            Preview(2);
            Expect("10,30,40,50", "Row-local handle stretches all selected tracks about the global opposite edge");
            Equal("20,40", string.Join(",", Track("position.y").Select(k => k.Frame)), "Cross-track spacing scales together");
            Finish(false);
            SelectFrames(timeline, (obj, "position.x", 10), (obj, "position.x", 30), (obj, "position.y", 25));
            Check(BeginStretch("position.y", 25), "Single selected key in a row uses nearest selection edge");
            Preview(0.5);
            Expect("10,20,20,40", "Single-key row stretches about the opposite edge");
            Finish(false);

            Select();
            var before = new Dictionary<SceneObject, ProjectSceneObjectEntry>();
            timeline.KeyframesRetiming += owners =>
            {
                foreach (var owner in owners) before.Add(owner, ProjectSceneSerializer.CaptureObjectState(owner));
            };
            BeginScale(0);
            Preview(2);
            Equal(0, before.Count, "Preview does not begin a persistent edit");
            var hierarchy = new SceneHistoryAction.HierarchyState(timeline.Viewport);
            var project = SceneHistoryAction.ProjectState.Capture(ProjectManager.Instance.Manifest);
            Finish(true);
            Equal(1, before.Count, "Commit captures each retimed object once for history");
            Equal("20,40,60", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)),
                "Commit replaces an unselected destination key and saves sorted timing");
            Near(2f, FrameValue(obj, "position.x", 40), "Selected key wins a destination collision");
            Equal("ease_in", obj.Keyframes["position.x"][0].InterpolationType, "Retiming preserves interpolation");
            var action = SceneHistoryAction.Create(before.Select(pair =>
                (pair.Key, pair.Value, ProjectSceneSerializer.CaptureObjectState(pair.Key))), hierarchy, hierarchy, project, project)!;
            Check(action.Apply(timeline.Viewport, null, undo: true), "Retiming undo applies");
            Equal("10,20,30,40", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Undo restores overwritten keys");
            Check(action.Apply(timeline.Viewport, null, undo: false), "Retiming redo applies");
            Equal("20,40,60", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Redo restores retimed keys");

            timeline = FramesTimeline(obj);
            SelectFrames(timeline, (obj, "position.x", 20), (obj, "position.x", 40), (obj, "position.x", 60));
            BeginScale(0);
            Preview(0);
            Finish(true);
            Equal(1, obj.Keyframes["position.x"].Count, "Collapsed selected keys merge only on commit");
            Near(1f, FrameValue(obj, "position.x", 0), "Collision winner is deterministic");
            Equal(1, ((List<TimelineKeyframe>)typeof(Timeline).GetField("_selectedKeyframes", flags)!.GetValue(timeline)!).Count,
                "Merged keys are removed from selection");
            timeline = FramesTimeline(obj);
            SelectFrames(timeline, (obj, "position.y", 30), (obj, "position.y", 50));
            BeginScale(0);
            Preview(10);
            Finish(true);
            Equal(530, timeline.MaxFrames, "Confirmed retiming extends the timeline for later keys");
        }
        finally
        {
            Services.Shutdown();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void FramesRetimingInput(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var bindings = Keybinds.Overrides;
        Services.Initialize();
        try
        {
            Keybinds.Reset();
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1800, 900);
            io.DeltaTime = 1f / 60f;
            io.ConfigInputTrickleEventQueue = false;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = Node("Retiming inputs");
            obj.Keyframes["position.x"] =
                [new() { Frame = 10, Value = 1f }, new() { Frame = 20, Value = 2f }, new() { Frame = 30, Value = 3f }];
            var timeline = FramesTimeline(obj);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Timeline).GetField("_displayRows", flags)!.SetValue(timeline,
                new List<TimelineProperty> { new() { Object = obj, PropertyPath = "position.x", Label = "X" } });
            var selected = (List<TimelineKeyframe>)typeof(Timeline).GetField("_selectedKeyframes", flags)!.GetValue(timeline)!;
            SelectFrames(timeline, (obj, "position.x", 10), (obj, "position.x", 20), (obj, "position.x", 30));
            timeline.SetCurrentFrame(10);
            void Draw(float x = 500, float y = 300, bool textInput = false)
            {
                io.AddMousePosEvent(x, y);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(1500, 0));
                ImGui.SetNextWindowSize(new(300, 900));
                ImGui.Begin("Retiming other panel");
                ImGui.SetWindowFocus();
                ImGui.End();
                io.WantTextInput = textInput;
                ImGui.SetNextWindowPos(new(0, 0));
                ImGui.SetNextWindowSize(new(1400, 700));
                timeline.Render();
                ImGui.Render();
            }
            void ScaleShortcut(bool textInput = false)
            {
                io.AddKeyEvent(ImGuiKey.ModAlt, true);
                io.AddKeyEvent(ImGuiKey.S, true);
                Draw(textInput: textInput);
                io.AddKeyEvent(ImGuiKey.S, false);
                io.AddKeyEvent(ImGuiKey.ModAlt, false);
                Draw();
            }
            void Key(ImGuiKey key, float x = 500, float y = 300)
            {
                io.AddKeyEvent(key, true);
                Draw(x, y);
                io.AddKeyEvent(key, false);
                Draw(x, y);
            }
            Draw();
            Draw();
            ScaleShortcut(textInput: true);
            Check(!timeline.IsRetimingKeyframes, "Text editing blocks scale shortcut");
            ScaleShortcut();
            Check(timeline.IsRetimingKeyframes && !timeline.HasEditFocus, "Alt+S starts scaling on hover without focus");
            Draw(600);
            Equal("10,30,50", string.Join(",", selected.Select(k => k.Frame)), "Mouse motion previews playhead-pivot scaling");
            Key(ImGuiKey.Escape, 600);
            Check(!timeline.IsRetimingKeyframes, "Escape cancels scaling");
            Equal("10,20,30", string.Join(",", selected.Select(k => k.Frame)), "Escape restores original timing");
            ScaleShortcut();
            Draw(600);
            Key(ImGuiKey.Enter, 600);
            Equal("10,30,50", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Enter commits scaling");
            Equal(3, selected.Count, "Confirmation does not also clear the selection");

            ScaleShortcut();
            Draw(550);
            io.AddMouseButtonEvent(1, true);
            Draw(550);
            io.AddMouseButtonEvent(1, false);
            Draw(550);
            Check(!timeline.IsRetimingKeyframes, "Right-click cancels without opening a context menu");
            Equal("10,30,50", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Right-click preserves saved keys");
            ScaleShortcut();
            io.AddMouseButtonEvent(0, true);
            Draw(550);
            io.AddMouseButtonEvent(0, false);
            Draw(550);
            Equal("10,35,60", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Left-click commits scaling once");

            Keybinds.LoadOverrides(new() { ["scaleKeyframes"] = "Ctrl+K", ["stretchKeyframes"] = "Shift" });
            ScaleShortcut();
            Check(!timeline.IsRetimingKeyframes, "Rebinding removes the default scale shortcut");
            io.AddKeyEvent(ImGuiKey.ModCtrl, true);
            Key(ImGuiKey.K);
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            Check(timeline.IsRetimingKeyframes, "Custom scale binding starts scaling");
            Key(ImGuiKey.Escape);

            float rulerY = (float)typeof(Timeline).GetField("_timelineAreaTop", flags)!.GetValue(timeline)!;
            float rowY = rulerY + 35f * UiTheme.Scale + ImGui.GetStyle().ItemSpacing.Y;
            float handleX = 8f + 200f * UiTheme.Scale + 60f * 5f;
            Draw(handleX, rowY);
            io.AddKeyEvent(ImGuiKey.ModShift, true);
            io.AddMouseButtonEvent(0, true);
            Draw(handleX, rowY);
            Check(timeline.IsRetimingKeyframes, "Custom stretch modifier plus endpoint drag starts stretching");
            io.AddKeyEvent(ImGuiKey.ModShift, false);
            Draw(handleX + 250, rowY + 80);
            Equal("10,60,110", string.Join(",", selected.Select(k => k.Frame)), "Stretch updates outside the original track row");
            io.AddMouseButtonEvent(0, false);
            Draw(handleX + 250, rowY + 80);
            Draw();
            Check(!timeline.IsRetimingKeyframes, "Releasing outside the original row finishes stretching");
            Equal("10,60,110", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Stretch commits relative spacing");
            Check(!ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel),
                "Retiming confirmation/cancellation does not leak context clicks");

            Keybinds.Reset();
            float leftHandleX = 8f + 200f * UiTheme.Scale + 10f * 5f;
            Draw(leftHandleX, rowY);
            io.AddKeyEvent(ImGuiKey.ModAlt, true);
            io.AddMouseButtonEvent(0, true);
            Draw(leftHandleX, rowY);
            Check(timeline.IsRetimingKeyframes, "Default Alt+drag starts stretching instead of deleting a key");
            Draw(leftHandleX - 25, rowY);
            io.AddMouseButtonEvent(1, true);
            Draw(leftHandleX - 25, rowY);
            io.AddMouseButtonEvent(1, false);
            io.AddMouseButtonEvent(0, false);
            Draw();
            Equal("10,60,110", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Right-click cancels a held stretch");
            Draw(leftHandleX, rowY);
            io.AddMouseButtonEvent(0, true);
            Draw(leftHandleX, rowY);
            Draw(leftHandleX - 50, rowY);
            io.AddMouseButtonEvent(0, false);
            Draw(leftHandleX - 50, rowY);
            io.AddKeyEvent(ImGuiKey.ModAlt, false);
            Draw();
            Equal("0,55,110", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Left-edge stretch commits around the fixed right edge");
            ScaleShortcut();
            Check(timeline.IsRetimingKeyframes, "Scale starts before scene refresh");
            Draw(550);
            timeline.RefreshFromScene();
            Check(!timeline.IsRetimingKeyframes, "Selection/scene refresh cancels active retiming");
            Equal("0,55,110", string.Join(",", obj.Keyframes["position.x"].Select(k => k.Frame)), "Scene refresh preserves original persisted timing");
        }
        finally
        {
            Services.Shutdown();
            Keybinds.LoadOverrides(bindings);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void FramesTimelineShortcuts(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var bindings = Keybinds.Overrides;
        Services.Initialize();
        try
        {
            Keybinds.Reset();
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1800, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.ConfigInputTrickleEventQueue = false;
            var timeline = FramesTimeline();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            float Scale() => (float)typeof(Timeline).GetField("_pixelsPerFrame", flags)!.GetValue(timeline)!;
            float Scroll() => (float)typeof(Timeline).GetField("_hScrollOffset", flags)!.GetValue(timeline)!;
            bool popup = false;
            void Draw(bool hovered = true, bool textInput = false)
            {
                io.AddMousePosEvent(hovered ? 600 : 1600, 300);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(1500, 0));
                ImGui.SetNextWindowSize(new(300, 900));
                ImGui.Begin("Other panel");
                ImGui.SetWindowFocus();
                if (popup) ImGui.OpenPopup("Blocking popup");
                if (ImGui.BeginPopup("Blocking popup"))
                {
                    if (!popup) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.End();
                io.WantTextInput = textInput;
                ImGui.SetNextWindowPos(new(0, 0));
                ImGui.SetNextWindowSize(new(1400, 700));
                timeline.Render();
                ImGui.Render();
            }
            void Press(ImGuiKey key, bool hovered = true, bool textInput = false)
            {
                io.AddKeyEvent(key, true);
                Draw(hovered, textInput);
                io.AddKeyEvent(key, false);
                Draw(hovered);
            }
            Draw();
            Draw();
            Check(timeline.IsWindowHovered && !timeline.HasEditFocus, "Timeline hovered without keyboard focus");
            Press(ImGuiKey.RightArrow);
            Equal(1, timeline.CurrentFrame, "Hover right arrow advances exactly one frame");
            Press(ImGuiKey.LeftArrow);
            Press(ImGuiKey.LeftArrow);
            Equal(0, timeline.CurrentFrame, "Hover left arrow clamps at frame zero");
            timeline.SetCurrentFrame(timeline.MaxFrames);
            Press(ImGuiKey.RightArrow);
            Equal(timeline.MaxFrames, timeline.CurrentFrame, "Hover right arrow clamps at timeline end");
            Press(ImGuiKey.Home);
            Equal(0, timeline.CurrentFrame, "Hover Home jumps to the start");
            Press(ImGuiKey.RightArrow, hovered: false);
            Equal(0, timeline.CurrentFrame, "Unfocused, unhovered timeline ignores navigation");
            Press(ImGuiKey.RightArrow, textInput: true);
            Equal(0, timeline.CurrentFrame, "Text editing blocks hover navigation");
            popup = true;
            Draw();
            Press(ImGuiKey.RightArrow);
            Equal(0, timeline.CurrentFrame, "Open popup blocks navigation");
            popup = false;
            Draw();
            Draw();
            Keybinds.LoadOverrides(new() { ["stepForward"] = "K" });
            Press(ImGuiKey.RightArrow);
            Equal(0, timeline.CurrentFrame, "Hover navigation respects rebound keys");
            Press(ImGuiKey.K);
            Equal(1, timeline.CurrentFrame, "Rebound navigation works on hover");

            Draw();
            float before = Scale();
            float anchor = (Scroll() + 400f) / before;
            io.AddKeyEvent(ImGuiKey.ModShift, true);
            io.AddMouseWheelEvent(0, 1);
            Draw();
            Draw();
            Near(before * 1.25f, Scale(), "Shift wheel zooms in");
            Near(anchor, (Scroll() + 400f) / Scale(), "Zoom preserves frame under mouse");
            io.AddMouseWheelEvent(0, -1);
            Draw();
            Draw();
            Near(before, Scale(), "Shift wheel zooms out");
            io.AddKeyEvent(ImGuiKey.ModShift, false);
            io.AddKeyEvent(ImGuiKey.ModCtrl, true);
            io.AddMouseWheelEvent(0, 1);
            Draw();
            Draw();
            Near(before * 1.25f, Scale(), "Existing Ctrl wheel zoom is preserved");
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            io.AddMouseWheelEvent(0, -1);
            Draw();
            Draw();
            Near(before * 1.25f, Scale(), "Plain wheel does not zoom");
            io.AddKeyEvent(ImGuiKey.ModShift, true);
            io.AddMouseWheelEvent(0, 1);
            Draw(hovered: false);
            Near(before * 1.25f, Scale(), "Wheel outside timeline does not zoom");
            Draw();
            Draw();
            io.AddMouseWheelEvent(0, 100);
            Draw();
            Draw();
            Near(40f, Scale(), "Shift wheel clamps to maximum zoom");
            float scrollAtLimit = Scroll();
            io.AddMouseWheelEvent(0, 1);
            Draw();
            Draw();
            Near(scrollAtLimit, Scroll(), "Shift wheel at maximum zoom does not pan");
            io.AddMouseWheelEvent(0, -100);
            Draw();
            Draw();
            Near(1f, Scale(), "Shift wheel clamps to minimum zoom");
            scrollAtLimit = Scroll();
            io.AddMouseWheelEvent(0, -1);
            Draw();
            Draw();
            Near(scrollAtLimit, Scroll(), "Shift wheel at minimum zoom does not pan");
        }
        finally
        {
            Services.Shutdown();
            Keybinds.LoadOverrides(bindings);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static void FramesBackwardSeekBeforeFirstKey(Fixture f)
    {
        var obj = Node("Animated object");
        obj.SetLocalPosition(new vec3(0, 7, 0));
        obj.Modifiers = [new() { Type = SceneModifierType.Array }];
        string modifier = $"modifier.{obj.Modifiers[0].Id}";
        obj.Keyframes["position.x"] =
        [
            new() { Frame = 30, Value = 12f },
            new() { Frame = 10, Value = 4f }
        ];
        obj.Keyframes["visible"] = [new() { Frame = 10, Value = true }, new() { Frame = 30, Value = false }];
        obj.Keyframes[$"{modifier}.distance"] = [new() { Frame = 10, Value = 2f }, new() { Frame = 30, Value = 6f }];
        obj.Keyframes[$"{modifier}.target"] =
            [new() { Frame = 10, Value = "first-path" }, new() { Frame = 30, Value = "last-path" }];
        var child = Node("Imported rotation");
        child.Keyframes["mirotation.z"] = [new() { Frame = 10, Value = 15f }, new() { Frame = 30, Value = 60f }];
        obj.AddChild(child);
        var light = new LightSceneObject();
        light.AssignObjectId();
        light.Keyframes["light.energy"] = [new() { Frame = 20, Value = 3f }];
        var timeline = FramesTimeline(obj, light);
        timeline.LoadKeyframesForAllObjects([child]);
        void CheckFirstPose()
        {
            Near(4, obj.LocalPosition.x, "Backward seek holds earliest transform key");
            Near(7, obj.LocalPosition.y, "Unkeyed transform component is preserved");
            Check(obj.ObjectVisible, "Backward seek restores first discrete key");
            Near(2, obj.Modifiers[0].Distance, "Backward seek restores first modifier key");
            Equal("first-path", obj.Modifiers[0].PathObjectId, "Backward seek restores first reference key");
            var rotation = MineImatorLoader.ConvertMiRotationMatrix([0f, 0f, 15f]);
            Near(MineImatorLoader.MatrixToEulerRzRyRx(rotation), child.LocalRotation,
                "Backward seek restores first imported rotation key");
            Near(3, light.LightEnergy, "Single-key track holds its value before the key");
        }
        Services.Initialize();
        try
        {
            foreach (int earlier in new[] { 0, 9, 10 })
            {
                timeline.SetCurrentFrame(30);
                Near(12, obj.LocalPosition.x, "Later transform key is applied");
                Check(!obj.ObjectVisible, "Later discrete key is applied");
                timeline.SetCurrentFrame(earlier);
                CheckFirstPose();
                timeline.SetCurrentFrame(30);
                timeline.SetCurrentFrameForRender(earlier);
                CheckFirstPose();
            }
            timeline.SetCurrentFrame(20);
            Near(8, obj.LocalPosition.x, "Interpolation between keys is preserved");
            Near(4, obj.Modifiers[0].Distance, "Modifier interpolation is preserved");
            timeline.SetCurrentFrame(30);
            typeof(Timeline).GetMethod("JumpToStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, null);
            CheckFirstPose();
            timeline.SetCurrentFrame(10);
            typeof(Timeline).GetMethod("StepBackward", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, null);
            CheckFirstPose();
        }
        finally
        {
            Services.Shutdown();
        }
    }

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
            .Invoke(timeline, null);
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

    private static unsafe void FramesInterpolation(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new(1200, 900);
            io.DeltaTime = 0.1f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var first = Node("Easing");
            var second = Node("Other selected object");
            foreach (var obj in new[] { first, second })
                obj.Keyframes["position.x"] = [new() { Frame = 0, Value = 0f }, new() { Frame = 20, Value = 20f }];
            var timeline = FramesTimeline(first, second);
            SelectFrames(timeline, (first, "position.x", 0), (second, "position.x", 0));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            bool openMenu = true;
            uint itemId = 0;
            string itemLabel = "";
            System.Numerics.Vector2 menuPos = default, menuSize = default;
            void Draw(float x = -100, float y = -100, bool down = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.Begin("Interpolation test");
                if (openMenu) { ImGui.OpenPopup("Interpolation options"); openMenu = false; }
                ImGui.SetNextWindowPos(new(20, 20), ImGuiCond.Always);
                if (ImGui.BeginPopup("Interpolation options"))
                {
                    typeof(Timeline).GetMethod("RenderInterpolationMenu", flags)!.Invoke(timeline, null);
                    itemId = ImGui.GetID(Localization.DisplayLabel(itemLabel));
                    menuPos = ImGui.GetWindowPos();
                    menuSize = ImGui.GetWindowSize();
                    ImGui.EndPopup();
                }
                typeof(Timeline).GetMethod("RenderBezierEditor", flags)!.Invoke(timeline, null);
                ImGui.End();
                ImGui.Render();
            }
            System.Numerics.Vector2 FindItem(string label)
            {
                itemLabel = label;
                Draw();
                Draw();
                float x = menuPos.X + 40;
                for (float y = menuPos.Y + 4; y < menuPos.Y + menuSize.Y; y += 2)
                {
                    Draw(x, y);
                    if (context.HoveredId == itemId) return new(x, y);
                }
                throw new InvalidOperationException("Interpolation item not reachable: " + label);
            }
            void Click(System.Numerics.Vector2 point)
            {
                Draw(point.X, point.Y, true);
                Draw(point.X, point.Y);
            }
            (string Label, string Curve, float In, float InOut)[] families =
            [
                ("Quadratic", "quad", 0.0625f, 0.125f),
                ("Cubic", "cubic", 0.015625f, 0.0625f),
                ("Quartic", "quart", 0.00390625f, 0.03125f),
                ("Quintic", "quint", 0.0009765625f, 0.015625f),
                ("Sine", "sine", 0.07612047f, 0.14644661f),
                ("Exponential", "expo", 0.005524272f, 0.015625f),
                ("Circular", "circ", 0.03175416f, 0.06698730f),
                ("Elastic", "elastic", -0.005524272f, 0.01196944f),
                ("Back", "back", -0.06413656f, -0.09968185f),
                ("Bounce", "bounce", 0.02734375f, 0.1171875f)
            ];
            foreach (var family in families)
            for (int direction = 0; direction < 3; direction++)
            {
                openMenu = true;
                var point = FindItem(family.Label);
                for (int i = 0; i < 6; i++) Draw(point.X, point.Y);
                ImGuiWindowPtr submenu = default;
                for (int i = 0; i < context.Windows.Size; i++)
                {
                    var window = context.Windows[i];
                    if (window.Active && (window.Flags & ImGuiWindowFlags.ChildMenu) != 0) submenu = window;
                }
                Check(!submenu.IsNull, family.Label + " submenu did not open");
                float rowHeight = ImGui.GetTextLineHeightWithSpacing();
                Click(submenu.Pos + new System.Numerics.Vector2(35,
                    ImGui.GetStyle().WindowPadding.Y + rowHeight * (direction + 0.5f)));
                string type = new[] { "easein", "easeout", "easeinout" }[direction] + family.Curve;
                foreach (var obj in new[] { first, second })
                {
                    Equal(type, obj.Keyframes["position.x"][0].InterpolationType, "Menu updates every selected object");
                    Equal("linear", obj.Keyframes["position.x"][1].InterpolationType, "Unselected key unchanged");
                }
                float t = direction == 1 ? 0.75f : 0.25f;
                float expected = direction == 0 ? family.In : direction == 1 ? 1f - family.In : family.InOut;
                typeof(Timeline).GetField("_currentFrame", flags)!.SetValue(timeline, direction == 1 ? 15 : 5);
                typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", flags)!.Invoke(timeline, null);
                Check(Math.Abs(first.LocalPosition.x - 20 * expected) < 0.0002f, type + " evaluated curve");
                Near(0, Timeline.ApplyInterpolation(0, type), type + " start");
                Near(1, Timeline.ApplyInterpolation(1, type), type + " end");
                Near(expected, Timeline.ApplyInterpolation(t, type), type + " Mine-imator sample");
                string path = f.NewPath(".nxFrames");
                timeline.ExportSelectedKeyframes(path);
                Equal(type, NxFramesFile.Read(path).Objects[0].Tracks[0].Keyframes[0].InterpolationType,
                    "New interpolation survives frames serialization");
                Equal(type, ProjectSceneSerializer.SerializeObjectForLibrary(first).Keyframes["position.x"][0].InterpolationType,
                    "New interpolation survives object serialization");
            }
            openMenu = true;
            Click(FindItem("Linear"));
            Near(5, first.LocalPosition.x, "Changing interpolation immediately updates the current pose");
            openMenu = true;
            Click(FindItem("Instant"));
            Near(0, first.LocalPosition.x, "Instant holds the previous key");
            Near(Timeline.ApplyInterpolation(0.25f, "easeinquad"),
                Timeline.ApplyInterpolation(0.25f, "ease-in-quadratic"), "Legacy quadratic easing preserved");

            openMenu = true;
            Click(FindItem("Bezier..."));
            Equal("instant", first.Keyframes["position.x"][0].InterpolationType, "Opening Bezier does not mutate keys");
            typeof(Timeline).GetField("_bezierControls", flags)!.SetValue(timeline, new System.Numerics.Vector4(0, 0, 1, 1));
            Draw();
            ImGuiWindowPtr editor = default;
            for (int i = 0; i < context.Windows.Size; i++)
                if (context.Windows[i].Active && (context.Windows[i].Flags & ImGuiWindowFlags.Modal) != 0)
                    editor = context.Windows[i];
            Check(!editor.IsNull, "Bezier editor rendered");
            // Four control rows precede the Apply/Cancel buttons.
            var apply = editor.Pos + new System.Numerics.Vector2(ImGui.GetStyle().WindowPadding.X + 15,
                ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y + ImGui.GetFrameHeightWithSpacing() * 4 +
                ImGui.GetFrameHeight() * 0.5f);
            Click(apply);
            Equal("bezier:0,0,1,1", first.Keyframes["position.x"][0].InterpolationType, "Bezier Apply stores controls");
            Equal("bezier:0,0,1,1", second.Keyframes["position.x"][0].InterpolationType, "Bezier updates selection");
            Check(Math.Abs(first.LocalPosition.x - 5) < 0.025f, "Custom Bezier affects timeline evaluation");
            string bezierPath = f.NewPath(".nxFrames");
            timeline.ExportSelectedKeyframes(bezierPath);
            Equal("bezier:0,0,1,1", NxFramesFile.Read(bezierPath).Objects[0].Tracks[0].Keyframes[0].InterpolationType,
                "Bezier controls survive frames serialization");
            openMenu = true;
            Click(FindItem("Bezier..."));
            Equal(new System.Numerics.Vector4(0, 0, 1, 1),
                (System.Numerics.Vector4)typeof(Timeline).GetField("_bezierControls", flags)!.GetValue(timeline)!,
                "Reopening Bezier restores saved controls");
            typeof(Timeline).GetField("_bezierControls", flags)!.SetValue(timeline, new System.Numerics.Vector4(float.NaN, 0, 1, 1));
            Draw();
            Click(apply + new System.Numerics.Vector2(0, ImGui.GetTextLineHeightWithSpacing()));
            Equal("bezier:0,0,1,1", first.Keyframes["position.x"][0].InterpolationType, "Invalid controls cannot be applied");
            typeof(Timeline).GetField("_bezierControls", flags)!.SetValue(timeline, new System.Numerics.Vector4(0.25f, -2, 0.75f, 3));
            Draw();
            // Cancel is next to Apply; cancelling must not commit edited overshoot controls.
            Click(apply + new System.Numerics.Vector2(ImGui.CalcTextSize(Localization.T("text.panel.timeline.apply")).X +
                ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X, 0));
            Equal("bezier:0,0,1,1", first.Keyframes["position.x"][0].InterpolationType, "Bezier cancellation preserves keys");
            openMenu = true;
            Click(FindItem("Bezier..."));
            Equal(new System.Numerics.Vector4(0, 0, 1, 1),
                (System.Numerics.Vector4)typeof(Timeline).GetField("_bezierControls", flags)!.GetValue(timeline)!,
                "Cancelled controls are discarded");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
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
