using System.Reflection;
using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private static unsafe void PropertyResetInteraction(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 1000);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = new SceneObject { Name = "Reset" };
            var second = new SceneObject { Name = "Second" };
            obj.SetLocalPosition(new vec3(2, 3, 4));
            second.SetLocalPosition(new vec3(5, 6, 7));
            obj.AssignObjectId();
            second.AssignObjectId();
            obj.Modifiers = [new() { Distance = 4 }];
            var viewport = new Viewport();
            viewport.SceneObjects.Add(obj);
            viewport.SceneObjects.Add(second);
            var timeline = new Timeline { Viewport = viewport };
            var panel = new PropertiesPanel { Viewport = viewport, Timeline = timeline };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, obj);
            var path = typeof(PropertiesPanel).GetField("_ctxPropertyPath", flags)!;
            var process = typeof(PropertiesPanel).GetMethod("ProcessPropertyContextRequest", flags)!;
            var objectTab = typeof(PropertiesPanel).GetMethod("RenderObjectTab", flags)!;
            var modifiers = typeof(PropertiesPanel).GetMethod("DrawModifiers", flags)!;
            var register = typeof(PropertiesPanel).GetMethod("RegisterPropertyContext", flags, [typeof(string[])])!;
            var project = typeof(PropertiesPanel).GetMethod("RegisterProjectPropertyContext", flags)!;
            var background = typeof(PropertiesPanel).GetMethod("RegisterBackgroundKeyframeContext", flags)!;
            string mode = "object";
            bool rightShift = false;
            bool popup = false;
            Vector2 target = default;
            void Frame(Vector2? mouse = null, bool right = false, bool shift = false)
            {
                io.AddMousePosEvent(mouse?.X ?? -100, mouse?.Y ?? -100);
                io.AddMouseButtonEvent(1, right);
                io.AddKeyEvent(rightShift ? ImGuiKey.RightShift : ImGuiKey.LeftShift, shift);
                io.AddKeyEvent(ImGuiKey.ModShift, shift);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(650, 900), ImGuiCond.Always);
                ImGui.Begin("Property reset interaction");
                if (mode == "object")
                {
                    if (ImGui.BeginTabBar("Properties"))
                    {
                        ImGui.PushID(Localization.Label("text.common.object", "Object"));
                        ImGui.GetStateStorage().SetInt(ImGui.GetID(Localization.Label("text.common.position", "Position")), 1);
                        ImGui.GetStateStorage().SetInt(ImGui.GetID(Localization.Label("text.common.appearance", "Appearance")), 1);
                        ImGui.PopID();
                        objectTab.Invoke(panel, null);
                        ImGui.EndTabBar();
                    }
                }
                else if (mode == "modifier")
                {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                    modifiers.Invoke(panel, [obj]);
                }
                else
                {
                    float value = 0;
                    ImGui.DragFloat("Reset control", ref value);
                    target = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
                    if (mode == "group") register.Invoke(panel, [new[] { "scale.x", "scale.y", "scale.z" }]);
                    else if (mode == "project") project.Invoke(panel, [nameof(PropertiesPanel.WatermarkOpacity), 0d, 1d]);
                    else background.Invoke(panel, [mode == "background-color" ? nameof(PropertiesPanel.AmbientLightColor)
                        : nameof(PropertiesPanel.AmbientLightStrength)]);
                }
                process.Invoke(panel, null);
                popup = ImGui.IsPopupOpen("##prop_keyframe_ctx");
                if (ImGui.BeginPopup("##prop_keyframe_ctx"))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.End();
                ImGui.Render();
            }
            Vector2 FindControl(string property, float x = 120)
            {
                Frame();
                Frame();
                for (int y = 40; y < 850; y += 3)
                {
                    path.SetValue(panel, null);
                    var point = new Vector2(x, y);
                    Frame(point, right: true);
                    bool found = (string?)path.GetValue(panel) == property;
                    Frame(point);
                    if (found) return point;
                }
                throw new InvalidOperationException("Property control not found: " + property);
            }
            void Reset(Vector2 point)
            {
                Frame(point, shift: true);
                Frame(point, right: true, shift: true);
                Check(!popup, "Shift-right-click does not open the context menu");
                Frame(point);
            }

            Vector2 position = FindControl("position.x");
            Near(2, obj.LocalPosition.x, "Ordinary right-click preserves the property");
            Frame(position, right: true);
            Check(popup, "Ordinary right-click still opens the menu");
            Frame(position);
            SelectionManager.Instance.SelectObject(obj);
            SelectionManager.Instance.SelectObject(second);
            typeof(Timeline).GetField("_autoKeyframe", flags)!.SetValue(timeline, true);
            typeof(Timeline).GetField("_currentFrame", flags)!.SetValue(timeline, 10);
            ProjectManager.Instance.SetDirty(false);
            Reset(position);
            Near(0, obj.LocalPosition.x, "Legacy axis widget resets the clicked axis");
            Near(3, obj.LocalPosition.y, "Axis reset preserves other axes");
            Near(0, second.LocalPosition.x, "Reset applies to all selected objects");
            Near(6, second.LocalPosition.y, "Multi-selection preserves other axes");
            foreach (var selected in new[] { obj, second })
                Equal(0f, selected.Keyframes["position.x"].Single(key => key.Frame == 10).Value,
                    "Reset records an auto-key for each selection");

            obj.SetObjectVisible(false);
            second.SetObjectVisible(false);
            Vector2 visible = FindControl("visible", 45);
            rightShift = true;
            Reset(visible);
            Check(obj.ObjectVisible && second.ObjectVisible, "Right Shift resets checkboxes to their true default");
            foreach (var selected in new[] { obj, second })
                Equal(1f, selected.Keyframes["visible"].Single(key => key.Frame == 10).Value,
                    "Checkbox reset records auto-keys");
            rightShift = false;

            obj.InheritPosition = false;
            second.InheritPosition = false;
            Reset(FindControl("object:inherit_position", 45));
            Check(obj.InheritPosition && second.InheritPosition, "Inheritance reset uses the object default for all selections");
            var child = new SceneObject { IncludeInFog = false };
            obj.AddChild(child);
            obj.IncludeInFog = false;
            second.IncludeInFog = false;
            Reset(FindControl("object:include_fog", 45));
            Check(obj.IncludeInFog && second.IncludeInFog && child.IncludeInFog,
                "Appearance reset preserves the checkbox's subtree-editing behavior");

            mode = "group";
            obj.SetLocalScale(new vec3(2, 3, 4));
            second.SetLocalScale(new vec3(5, 6, 7));
            Frame();
            Frame();
            Reset(target);
            Equal(vec3.Ones, obj.LocalScale, "Combined vector resets all components");
            Equal(vec3.Ones, second.LocalScale, "Combined reset respects multi-selection");

            mode = "modifier";
            Vector2 distance = FindControl($"modifier.{obj.Modifiers[0].Id}.distance");
            Reset(distance);
            Near(1, obj.Modifiers[0].Distance, "Modifier reset uses its default and pixel conversion");
            Equal(1f, obj.Keyframes[$"modifier.{obj.Modifiers[0].Id}.distance"].Single(key => key.Frame == 10).Value,
                "Modifier reset records its stored-unit keyframe");

            mode = "project";
            panel.WatermarkOpacity = 0.25f;
            Frame();
            Frame();
            Reset(target);
            Near(new PropertiesPanel().WatermarkOpacity, panel.WatermarkOpacity, "Project binding uses project defaults");
            Near(panel.WatermarkOpacity, ProjectManager.Instance.Manifest.Settings.WatermarkOpacity,
                "Project reset persists to the manifest");

            mode = "background-color";
            Array.Fill(panel.AmbientLightColor, 0.2f);
            Frame();
            Frame();
            Reset(target);
            Check(panel.AmbientLightColor.SequenceEqual(new PropertiesPanel().AmbientLightColor),
                "Background array resets all color components");
            mode = "background";
            panel.AmbientLightStrength = 0.2f;
            Frame();
            Frame();
            Reset(target);
            Near(new PropertiesPanel().AmbientLightStrength, panel.AmbientLightStrength,
                "Background scalar reset clears stale project bindings");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
