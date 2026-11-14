using System.Reflection;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private static unsafe void VisibilityControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1000, 1100);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = new SceneObject { Name = "Visibility" };
            var second = new SceneObject { Name = "Second" };
            obj.AssignObjectId();
            second.AssignObjectId();
            var viewport = new Viewport();
            viewport.SceneObjects.Add(obj);
            viewport.SceneObjects.Add(second);
            var timeline = new Timeline { Viewport = viewport };
            var panel = new PropertiesPanel { Viewport = viewport, Timeline = timeline };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, obj);
            var path = typeof(PropertiesPanel).GetField("_ctxPropertyPath", flags)!;
            var open = typeof(PropertiesPanel).GetField("_openPropContextMenu", flags)!;
            var draw = typeof(PropertiesPanel).GetMethod("RenderObjectTab", flags)!;
            var menu = typeof(PropertiesPanel).GetMethod("RenderPropertyContextMenu", flags)!;
            string action = Localization.Label("text.panel.properties.add_keyframe_at_current_frame",
                "Add Keyframe at Current Frame");
            bool showMenu = false;
            uint hovered = 0, actionId = 0;
            System.Numerics.Vector2 popupPos = default, popupSize = default;
            void Frame(float x = -100, float y = -100, bool left = false, bool right = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, left);
                io.AddMouseButtonEvent(1, right);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(650, 1000), ImGuiCond.Always);
                ImGui.Begin("Visibility controls");
                if (ImGui.BeginTabBar("Properties"))
                {
                    ImGui.PushID(Localization.Label("text.common.object", "Object"));
                    ImGui.GetStateStorage().SetInt(ImGui.GetID(Localization.Label("text.common.appearance", "Appearance")), 1);
                    ImGui.PopID();
                    draw.Invoke(panel, null);
                    ImGui.EndTabBar();
                }
                if ((bool)open.GetValue(panel)!)
                {
                    open.SetValue(panel, false);
                    if (showMenu)
                    {
                        typeof(PropertiesPanel).GetField("_propertyContextObject", flags)!.SetValue(panel, obj);
                        ImGui.OpenPopup("##prop_keyframe_ctx");
                    }
                }
                if (showMenu)
                {
                    menu.Invoke(panel, null);
                    if (ImGui.BeginPopup("##prop_keyframe_ctx"))
                    {
                        popupPos = ImGui.GetWindowPos();
                        popupSize = ImGui.GetWindowSize();
                        actionId = ImGui.GetID(action);
                        ImGui.EndPopup();
                    }
                }
                hovered = context.HoveredId;
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            float visibleY = -1;
            for (int y = 60; y < 950; y += 3)
            {
                path.SetValue(panel, null);
                Frame(45, y, right: true);
                if ((string?)path.GetValue(panel) == "visible") visibleY = y;
                Frame(45, y);
                if (visibleY >= 0) break;
            }
            Check(visibleY >= 0, "Right-clicking the Appearance Visible checkbox registers visibility");
            Check(obj.ObjectVisible && second.ObjectVisible, "Right-click does not toggle visibility");
            showMenu = true;
            Frame(45, visibleY, right: true);
            Frame(45, visibleY);
            Frame();
            float actionY = -1;
            for (int y = (int)popupPos.Y + 4; y < popupPos.Y + popupSize.Y - 4; y++)
            {
                Frame(popupPos.X + 20, y);
                if (hovered == actionId) { actionY = y; break; }
            }
            Check(actionY >= 0, "Visibility menu offers Add Keyframe at Current Frame");
            Frame(popupPos.X + 20, actionY, left: true);
            Frame(popupPos.X + 20, actionY);
            Frame();
            Equal(1f, obj.Keyframes["visible"][0].Value, "Manual key records visible state");
            Check(!second.Keyframes.ContainsKey("visible"), "Manual key targets the inspected object");
            showMenu = false;
            SelectionManager.Instance.SelectObject(obj);
            SelectionManager.Instance.SelectObject(second);
            typeof(Timeline).GetField("_autoKeyframe", flags)!.SetValue(timeline, true);
            typeof(Timeline).GetField("_currentFrame", flags)!.SetValue(timeline, 10);
            Frame(45, visibleY, left: true);
            Frame(45, visibleY);
            Check(!obj.ObjectVisible && !second.ObjectVisible, "Checkbox toggles all selected objects");
            foreach (var selected in new[] { obj, second })
                Equal(0f, selected.Keyframes["visible"].Single(key => key.Frame == 10).Value,
                    "Auto-key records hidden state for each selected object");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static void VisibilityTimeline(Fixture fixture)
    {
        var obj = new SceneObject();
        obj.AssignObjectId();
        var child = new SceneObject();
        var independent = new SceneObject { InheritVisibility = false };
        obj.AddChild(child);
        obj.AddChild(independent);
        var viewport = new Viewport();
        viewport.SceneObjects.Add(obj);
        var timeline = new Timeline { Viewport = viewport };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var frameField = typeof(Timeline).GetField("_currentFrame", flags)!;
        var apply = typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", flags)!;
        void Seek(Timeline target, int frame)
        {
            frameField.SetValue(target, frame);
            apply.Invoke(target, null);
        }
        timeline.AddKeyframeForProperty(obj, "visible", 0);
        obj.SetObjectVisible(false);
        timeline.AddKeyframeForProperty(obj, "visible", 10);
        obj.SetObjectVisible(true);
        timeline.AddKeyframeForProperty(obj, "visible", 20);
        using var scope = (IDisposable)typeof(SceneObject).GetMethod("BeginRenderEvaluation",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        foreach (int frame in new[] { 0, 9, 10, 19, 20, 30, 10, 0, -1 })
        {
            _ = obj.GetEffectiveVisibility();
            _ = child.GetEffectiveVisibility();
            Seek(timeline, frame);
            bool expected = frame < 10 || frame >= 20;
            Equal(expected, obj.ObjectVisible, "Visibility switches exactly at keyed frames");
            Equal(expected, obj.GetEffectiveVisibility(), "Animated visibility invalidates render cache");
            Equal(expected, child.GetEffectiveVisibility(), "Children inherit animated visibility");
            Check(independent.GetEffectiveVisibility(), "Disabled inheritance keeps child visible");
        }
        var (archive, _) = fixture.Roundtrip(obj);
        var loaded = new SceneObject();
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [loaded, archive.Root, null]);
        loaded.AssignObjectId();
        Equal(3, loaded.Keyframes["visible"].Count, "Archive preserves all visibility keys");
        var loadedViewport = new Viewport();
        loadedViewport.SceneObjects.Add(loaded);
        var loadedTimeline = new Timeline { Viewport = loadedViewport };
        loadedTimeline.LoadKeyframesForAllObjects([loaded]);
        Seek(loadedTimeline, 10);
        Check(!loaded.GetEffectiveVisibility(), "Persisted keys hide the loaded object");
        Seek(loadedTimeline, 0);
        Check(loaded.GetEffectiveVisibility(), "Persisted keys support backward seeking");
    }
}
