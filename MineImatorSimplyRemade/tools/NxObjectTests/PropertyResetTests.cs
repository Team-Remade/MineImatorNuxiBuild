using System.Reflection;
using System.Numerics;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static unsafe void ProjectResolutionControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var manager = ProjectManager.Instance;
        var originalSettings = manager.Manifest.Settings;
        bool originalDirty = manager.IsDirty;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var draw = typeof(PropertiesPanel).GetMethod("DrawProjectResolutionControls", flags)!;
            var presets = (Array)typeof(PropertiesPanel).GetField("ResolutionPresets",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var manifest = new ProjectManifest();
            manager.Manifest.Settings = manifest.Settings;
            var panel = new PropertiesPanel();
            panel.LoadProjectSettingsFromManifest(manifest);
            Vector2 combo = default, widthInput = default;
            float controlsHeight = 0f, rowHeight = 0f;
            void Frame(Vector2? mouse = null, bool down = false, float wheel = 0f)
            {
                io.AddMousePosEvent(mouse?.X ?? -100f, mouse?.Y ?? -100f);
                io.AddMouseButtonEvent(0, down);
                if (wheel != 0f) io.AddMouseWheelEvent(0f, wheel);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(600, 700), ImGuiCond.Always);
                ImGui.Begin("Project resolution regression");
                var start = ImGui.GetCursorScreenPos();
                combo = start + new Vector2(100f, ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeight() / 2f);
                widthInput = combo + new Vector2(-60f, ImGui.GetFrameHeightWithSpacing());
                draw.Invoke(panel, null);
                controlsHeight = ImGui.GetCursorScreenPos().Y - start.Y;
                rowHeight = ImGui.GetTextLineHeightWithSpacing();
                ImGui.End();
                ImGui.Render();
            }
            void Click(Vector2 point)
            {
                Frame(point);
                Frame(point, down: true);
                Frame(point);
                Frame(point);
            }
            void Select(int index)
            {
                Click(combo);
                Check(context.OpenPopupStack.Size > 0, "Resolution dropdown opens");
                var popup = context.OpenPopupStack[context.OpenPopupStack.Size - 1].Window;
                Check(popup != null, "Resolution dropdown has a popup");
                var center = popup->Pos + popup->Size / 2f;
                Frame(center, wheel: index == presets.Length ? -30f : 30f);
                Frame(center);
                var target = popup->DC.CursorStartPos + new Vector2(40f, index * rowHeight + rowHeight / 2f);
                Click(target);
                Check(context.OpenPopupStack.Size == 0, "Selecting a resolution closes dropdown");
            }
            int PresetIndex(string name)
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    object preset = presets.GetValue(i)!;
                    if ((string)preset.GetType().GetProperty("Name")!.GetValue(preset)! == name) return i;
                }
                throw new InvalidOperationException("Missing resolution preset: " + name);
            }

            Frame();
            Frame();
            float presetHeight = controlsHeight;
            manager.SetDirty(false);
            Select(presets.Length);
            Check(controlsHeight > presetHeight, "Custom reveals dimension editors");
            Equal(1920, panel.GetResolutionWidth(), "Custom preserves width");
            Equal(1080, panel.GetResolutionHeight(), "Custom preserves height");
            Equal("Custom", manager.Manifest.Settings.RenderResolutionPreset, "Custom mode persists even at preset dimensions");
            Check(manager.IsDirty, "Changing resolution mode marks project dirty");
            panel.LoadProjectSettingsFromManifest(manager.Manifest);
            Frame();
            Check(controlsHeight > presetHeight, "Reload keeps explicitly selected Custom mode");

            var input = widthInput;
            Frame(input);
            Frame(input, down: true);
            Frame(input + new Vector2(40f, 0), down: true);
            Frame(input + new Vector2(40f, 0));
            Check(panel.GetResolutionWidth() > 1920, "Custom width editor changes resolution");
            Equal(panel.GetResolutionWidth(), manager.Manifest.Settings.ResolutionWidth, "Custom edits persist");
            Equal("Custom", panel.GetRenderResolutionPreset(), "Custom editing keeps render settings synchronized");

            foreach (var (name, width, height) in new[] { ("720P", 1280, 720), ("1440P", 2560, 1440), ("4K", 3840, 2160) })
            {
                Select(PresetIndex(name));
                Equal(width, panel.GetResolutionWidth(), "Preset applies width");
                Equal(height, panel.GetResolutionHeight(), "Preset applies height");
                Equal(name, manager.Manifest.Settings.RenderResolutionPreset, "Preset synchronizes saved render settings");
                Equal(width, manager.Manifest.Settings.ResolutionWidth, "Preset saves width");
                Equal(height, manager.Manifest.Settings.ResolutionHeight, "Preset saves height");
                Near(presetHeight, controlsHeight, "Preset hides dimension editors");
            }

            manifest.Settings.ResolutionWidth = 1234;
            manifest.Settings.ResolutionHeight = 567;
            panel.LoadProjectSettingsFromManifest(manifest);
            Frame();
            Check(controlsHeight > presetHeight, "Older nonstandard dimensions display as Custom despite stale preset name");
            Equal(1234, panel.GetResolutionWidth(), "Displaying older project preserves its dimensions");
        }
        finally
        {
            manager.Manifest.Settings = originalSettings;
            manager.SetDirty(originalDirty);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void PropertiesTabScrolling(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 800);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var viewport = new Viewport();
            var panel = new PropertiesPanel { Viewport = viewport, Timeline = new Timeline { Viewport = viewport } };

            void Frame(Vector2? mouse = null, bool left = false, float wheel = 0f, float height = 240f)
            {
                io.AddMousePosEvent(mouse?.X ?? -100f, mouse?.Y ?? -100f);
                io.AddMouseButtonEvent(0, left);
                if (wheel != 0f) io.AddMouseWheelEvent(0f, wheel);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10f, 10f), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(650f, height), ImGuiCond.Always);
                panel.Render();
                ImGui.Render();
            }

            ImGuiWindowPtr FindWindow(string name)
            {
                for (int i = 0; i < context.Windows.Size; i++)
                {
                    var window = context.Windows[i];
                    string windowName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)window.Name)!;
                    if (windowName.Contains(name, StringComparison.Ordinal)) return window;
                }
                throw new InvalidOperationException("Properties window not found: " + name);
            }

            Frame();
            Frame();
            var project = FindWindow("##ProjectProperties");
            var parent = project.ParentWindow;
            float contentTop = project.Pos.Y;
            var body = project.Pos + new Vector2(100f, 80f);
            for (int i = 0; i < 5; i++) Frame(body, wheel: -1f);
            Frame(body);
            Check(project.Scroll.Y > 100f, "Project settings did not scroll.");
            float projectScroll = project.Scroll.Y;
            Check(parent.Scroll.Y == 0f && project.Pos.Y == contentTop,
                "Scrolling moved the properties tab header.");

            var style = ImGui.GetStyle();
            float tabY = parent.Pos.Y + ImGui.GetFrameHeight() + style.WindowPadding.Y + ImGui.GetFrameHeight() * 0.5f;
            float projectWidth = ImGui.CalcTextSize(Localization.T("text.panel.properties.project")).X + style.FramePadding.X * 2f;
            var objectTab = new Vector2(parent.Pos.X + style.WindowPadding.X + projectWidth + style.ItemInnerSpacing.X + 15f, tabY);
            void Click(Vector2 point)
            {
                Frame(point);
                Frame(point, left: true);
                Frame(point);
                Frame(point);
            }
            Click(objectTab);
            var objWindow = FindWindow("##ObjectProperties");
            Check(objWindow.Active && objWindow.Scroll.Y == 0f,
                "Object tab was not clickable while Project was scrolled, or inherited its scroll.");
            Check(objWindow.ScrollMax.Y == 0f, "Empty selection should not create overflowing object settings.");

            var obj = new SceneObject { Name = "Scrollable object" };
            typeof(PropertiesPanel).GetField("_currentObject", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, obj);
            Frame();
            Frame();
            body = objWindow.Pos + new Vector2(100f, 80f);
            for (int i = 0; i < 5; i++) Frame(body, wheel: -1f);
            Frame(body);
            Check(objWindow.Scroll.Y > 0f && parent.Scroll.Y == 0f && objWindow.Pos.Y == contentTop,
                "Object settings did not scroll independently below the fixed tabs.");
            float objectScroll = objWindow.Scroll.Y;
            var projectTab = new Vector2(parent.Pos.X + style.WindowPadding.X + 15f, tabY);
            Click(projectTab);
            Check(project.Active && Math.Abs(project.Scroll.Y - projectScroll) < 1f,
                "Switching tabs lost the Project scroll position.");
            Click(objectTab);
            Check(objWindow.Active && Math.Abs(objWindow.Scroll.Y - objectScroll) < 1f,
                "Switching tabs lost the Object scroll position.");
            Frame(height: 160f);
            Frame(height: 160f);
            Check(parent.Scroll.Y == 0f && objWindow.Pos.Y == contentTop && objWindow.Size.Y < 160f,
                "Resizing moved the tabs or failed to resize the scrolling content.");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void PropertiesPanelLayout(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var manager = ProjectManager.Instance;
        var originalSettings = manager.Manifest.Settings;
        bool originalDirty = manager.IsDirty;
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1200f, 3400f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var currentObject = typeof(PropertiesPanel).GetField("_currentObject", flags)!;
            var renderObject = typeof(PropertiesPanel).GetMethod("RenderObjectProperties", flags)!;
            var renderProject = typeof(PropertiesPanel).GetMethod("RenderProjectProperties", flags)!;

            Dictionary<string, float> FindHeaderPositions(string windowName, Action render,
                (string Name, string Key, string Fallback)[] headers)
            {
                var ids = new Dictionary<uint, string>();
                var positions = new Dictionary<string, float>();
                for (float y = 20f; y < io.DisplaySize.Y - 20f && positions.Count < headers.Length; y += 6f)
                {
                    io.AddMousePosEvent(100f, y);
                    ImGui.NewFrame();
                    ImGui.SetNextWindowPos(new Vector2(10f, 10f), ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new Vector2(900f, 3300f), ImGuiCond.Always);
                    ImGui.Begin(windowName);
                    if (ids.Count == 0)
                    {
                        foreach (var header in headers)
                            ids.Add(ImGui.GetID(Localization.Label(header.Key, header.Fallback)), header.Name);
                    }
                    render();
                    if (ids.TryGetValue(context.HoveredId, out string? hovered))
                        positions.TryAdd(hovered, y);
                    ImGui.End();
                    ImGui.Render();
                }
                return positions;
            }

            var objectPanel = new PropertiesPanel();
            currentObject.SetValue(objectPanel, new SceneObject { Name = "Layout test" });
            var objectPositions = FindHeaderPositions("Properties object layout", () => renderObject.Invoke(objectPanel, null),
            [
                ("Position", "text.common.position", "Position"),
                ("Rotation", "text.panel.properties.rotation_degrees", "Rotation (degrees)"),
                ("Scale", "text.common.scale", "Scale"),
                ("Pivot", "text.panel.properties.pivot_offset", "Pivot Offset"),
                ("Material", "text.panel.properties.material", "Material"),
                ("Appearance", "text.common.appearance", "Appearance")
            ]);
            Check(objectPositions["Position"] < objectPositions["Rotation"] &&
                  objectPositions["Rotation"] < objectPositions["Scale"] &&
                  objectPositions["Scale"] < objectPositions["Pivot"] &&
                  objectPositions["Pivot"] < objectPositions["Material"] &&
                  objectPositions["Material"] < objectPositions["Appearance"],
                "Object transforms, pivot, material and appearance are not grouped in the expected order.");

            var cameraPanel = new PropertiesPanel();
            var camera = new CameraSceneObject();
            currentObject.SetValue(cameraPanel, camera);
            var cameraPositions = FindHeaderPositions("Properties camera layout", () => renderObject.Invoke(cameraPanel, null),
            [
                ("Camera", "text.common.camera", "Camera"),
                ("Effects", "text.panel.properties.effects", "Effects")
            ]);
            Check(cameraPositions["Camera"] < cameraPositions["Effects"],
                "Camera setup must appear before camera effects.");

            var projectPanel = new PropertiesPanel();
            var projectPositions = FindHeaderPositions("Properties project layout", () => renderProject.Invoke(projectPanel, null),
            [
                ("Background", "text.panel.properties.background_settings", "Background Settings"),
                ("Render", "text.panel.properties.render_settings", "Render Settings"),
                ("Performance", "text.panel.properties.performance", "Performance"),
                ("Library", "text.panel.properties.library", "Library")
            ]);
            Check(projectPositions["Background"] < projectPositions["Render"] &&
                  projectPositions["Render"] < projectPositions["Performance"] &&
                  projectPositions["Performance"] < projectPositions["Library"],
                "Project environment, render, performance and library sections are not in workflow order.");

            var renderCamera = typeof(PropertiesPanel).GetMethod("RenderCameraProperties", flags)!;
            var cameraLabel = Localization.Label("text.common.camera", "Camera");
            Vector2 activeCameraPosition = default;
            Vector2 activeCameraSize = default;
            void CameraFrame(Vector2? mouse = null, bool left = false)
            {
                io.AddMousePosEvent(mouse?.X ?? -100f, mouse?.Y ?? -100f);
                io.AddMouseButtonEvent(0, left);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10f, 10f), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(700f, 800f), ImGuiCond.Always);
                ImGui.Begin("Camera property controls");
                ImGui.GetStateStorage().SetInt(ImGui.GetID(cameraLabel), 1);
                renderCamera.Invoke(cameraPanel, null);
                activeCameraPosition = ImGui.GetItemRectMin();
                activeCameraSize = ImGui.GetItemRectMax() - activeCameraPosition;
                ImGui.End();
                ImGui.Render();
            }

            bool originalActive = camera.Active;
            CameraFrame();
            Check(activeCameraSize.X > 0f && activeCameraSize.Y > 0f,
                "Active Camera control is missing from the expanded Camera section.");
            var activeCameraCenter = activeCameraPosition + activeCameraSize / 2f;
            CameraFrame(activeCameraCenter, left: true);
            CameraFrame(activeCameraCenter);
            Check(camera.Active != originalActive, "Active Camera control no longer changes the camera state.");
            camera.Active = originalActive;
        }
        finally
        {
            manager.Manifest.Settings = originalSettings;
            manager.SetDirty(originalDirty);
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void BackgroundPropertiesLayout(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var manager = ProjectManager.Instance;
        var originalSettings = manager.Manifest.Settings;
        bool originalDirty = manager.IsDirty;
        try
        {
            manager.Manifest.Settings = new ProjectManifest().Settings;
            var panel = new PropertiesPanel();
            panel.LoadProjectSettingsFromManifest(manager.Manifest);
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1200f, 2600f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var renderBackground = typeof(PropertiesPanel).GetMethod("RenderBackgroundProperties", flags)!;
            string backgroundLabel = Localization.Label("text.panel.properties.background_settings", "Background Settings");
            string backdropLabel = Localization.Label("text.panel.properties.backdrop", "Backdrop");
            string windLabel = Localization.Label("text.panel.properties.wind", "Wind");

            (string Key, string Value)[] sectionTranslations =
            [
                ("text.panel.properties.backdrop", "Backdrop"),
                ("text.panel.properties.sky_settings", "Sky Settings"),
                ("text.panel.properties.ground_biome", "Ground & Biome"),
                ("text.panel.properties.lighting", "Lighting"),
                ("text.panel.properties.sky_colors", "Sky Colors"),
                ("text.panel.properties.stars", "Stars"),
                ("text.panel.properties.sun_moon", "Sun & Moon"),
                ("text.panel.properties.clouds", "Clouds")
            ];
            foreach (var (key, value) in sectionTranslations)
                Check(Localization.T(key) == value, $"Background section translation '{key}' should resolve to '{value}'.");

            (string Name, string Label)[] controls =
            [
                ("Backdrop", Localization.Label("text.panel.properties.backdrop", "Backdrop")),
                ("Sky", Localization.Label("text.panel.properties.sky_settings", "Sky Settings")),
                ("Ground", Localization.Label("text.panel.properties.ground_biome", "Ground & Biome")),
                ("Lighting", Localization.Label("text.panel.properties.lighting", "Lighting")),
                ("Fog", Localization.Label("text.panel.properties.fog", "Fog")),
                ("Wind", Localization.Label("text.panel.properties.wind", "Wind")),
                ("Sky Colors", Localization.Label("text.panel.properties.sky_colors", "Sky Colors")),
                ("Stars", Localization.Label("text.panel.properties.stars", "Stars")),
                ("Sun & Moon", Localization.Label("text.panel.properties.sun_moon", "Sun & Moon")),
                ("Clouds", Localization.Label("text.panel.properties.clouds", "Clouds")),
                ("Wind Enabled", Localization.Label("text.panel.properties.wind_enabled", "Enabled##wind")),
                ("Sky Toggle", Localization.Label("text.panel.properties.sky_enabled", "Sky")),
                ("Sky Time", Localization.Label("text.panel.properties.time", "Time")),
                ("Sky Mode", Localization.Label("text.panel.properties.sky_mode", "Sky Mode")),
                ("Sky Texture", Localization.Label("text.panel.properties.sky_texture", "Sky Texture")),
                ("Skybox Mapped", Localization.Label("text.panel.properties.skybox_mapped", "Mapped##skybox")),
                ("Background Scale", Localization.DisplayLabel("Background Scale"))
            ];

            void Frame(float x = -100f, float y = -100f, bool left = false, bool? backdropOpen = null, bool? windOpen = null,
                string[]? trackedNames = null, Dictionary<uint, string>? trackedIds = null)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, left);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10f, 10f), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(900f, 2500f), ImGuiCond.Always);
                ImGui.Begin("Background properties layout");
                ImGui.GetStateStorage().SetInt(ImGui.GetID(backgroundLabel), 1);
                if (backdropOpen.HasValue)
                    ImGui.GetStateStorage().SetInt(ImGui.GetID(backdropLabel), backdropOpen.Value ? 1 : 0);
                if (windOpen.HasValue)
                    ImGui.GetStateStorage().SetInt(ImGui.GetID(windLabel), windOpen.Value ? 1 : 0);
                if (trackedNames != null && trackedIds != null && trackedIds.Count == 0)
                {
                    foreach (string name in trackedNames)
                    {
                        string label = controls.First(control => control.Name == name).Label;
                        if (name == "Wind Enabled")
                        {
                            ImGui.PushID(windLabel);
                            trackedIds.Add(ImGui.GetID(label), name);
                            ImGui.PopID();
                        }
                        else
                        {
                            trackedIds.Add(ImGui.GetID(label), name);
                        }
                    }
                }
                renderBackground.Invoke(panel, null);
                ImGui.End();
                ImGui.Render();
            }

            Dictionary<string, float> FindPositions(params string[] names)
            {
                var ids = new Dictionary<uint, string>();
                var positions = new Dictionary<string, float>();
                for (float y = 20f; y < io.DisplaySize.Y - 20f && positions.Count < names.Length; y += 6f)
                {
                    Frame(50f, y, trackedNames: names, trackedIds: ids);
                    if (ids.TryGetValue(context.HoveredId, out string? hovered))
                        positions.TryAdd(hovered, y);
                }
                return positions;
            }

            float FindHeaderLeft(string name, float y)
            {
                var ids = new Dictionary<uint, string>();
                Frame(trackedNames: [name], trackedIds: ids);
                uint targetId = ids.Keys.Single();
                for (float x = 0f; x < io.DisplaySize.X; x += 2f)
                {
                    Frame(x, y, trackedNames: [name], trackedIds: ids);
                    if (context.HoveredId == targetId)
                        return x;
                }
                return float.NaN;
            }

            var positions = FindPositions("Backdrop", "Sky", "Ground", "Lighting", "Fog", "Wind",
                "Sky Colors", "Stars", "Sun & Moon", "Clouds", "Sky Toggle", "Sky Time", "Background Scale");
            Check(positions["Backdrop"] < positions["Sky"] && positions["Sky"] < positions["Ground"] &&
                  positions["Ground"] < positions["Lighting"] && positions["Lighting"] < positions["Fog"] &&
                  positions["Fog"] < positions["Wind"],
                "Background groups must follow backdrop, sky, ground, lighting, fog and wind order.");
            Check(Math.Abs(FindHeaderLeft("Backdrop", positions["Backdrop"]) - FindHeaderLeft("Wind", positions["Wind"])) <= 2f,
                "Wind must align with the other top-level Background section headers.");
            Check(positions["Sky Time"] < positions["Sky Colors"] && positions["Sky Colors"] < positions["Stars"] &&
                  positions["Stars"] < positions["Sun & Moon"] && positions["Sun & Moon"] < positions["Clouds"],
                "Minecraft sky settings must keep time first, then group colors, stars, celestial controls and clouds.");

            Frame(windOpen: false);
            Check(!FindPositions("Wind Enabled").ContainsKey("Wind Enabled"),
                "Collapsing Wind must hide its controls.");
            Frame(windOpen: true);
            float windEnabledY = FindPositions("Wind Enabled")["Wind Enabled"];
            bool originalWindEnabled = panel.WindEnabled;
            manager.SetDirty(false);
            Frame(50f, windEnabledY);
            Frame(50f, windEnabledY, left: true);
            Frame(50f, windEnabledY);
            Check(panel.WindEnabled != originalWindEnabled &&
                  manager.Manifest.Settings.WindEnabled == panel.WindEnabled && manager.IsDirty,
                "Wind edits remain interactive and persist to project settings.");

            float backdropY = positions["Backdrop"];
            Frame(100f, backdropY);
            Frame(100f, backdropY, left: true);
            Frame(100f, backdropY);
            Check(!FindPositions("Background Scale").ContainsKey("Background Scale"),
                "Collapsing Backdrop must hide its image and transform controls.");

            Frame(backdropOpen: true);
            float scaleY = FindPositions("Background Scale")["Background Scale"];
            float originalScale = panel.BackgroundScale;
            manager.SetDirty(false);
            Frame(300f, scaleY);
            Frame(300f, scaleY, left: true);
            Frame(340f, scaleY, left: true);
            Frame(340f, scaleY);
            Check(panel.BackgroundScale != originalScale, "Background Scale remains editable in Backdrop.");
            Check(Math.Abs(manager.Manifest.Settings.BackgroundScale - panel.BackgroundScale) < 0.001f,
                "Background image transform edits persist after rendering all backdrop controls.");
            Check(manager.IsDirty, "Changing a background image transform marks the project dirty.");

            manager.SetDirty(false);
            float skyToggleY = positions["Sky Toggle"];
            Frame(50f, skyToggleY);
            Frame(50f, skyToggleY, left: true);
            Frame(50f, skyToggleY);
            Check(!panel.UseSky && !manager.Manifest.Settings.UseSky && manager.IsDirty,
                "The Sky toggle still updates and persists its setting.");

            panel.UseSky = true;
            panel.SkyMode = "sphere";
            var spherePositions = FindPositions("Sky Texture", "Sky Time", "Sky Colors");
            Check(spherePositions.ContainsKey("Sky Texture") && !spherePositions.ContainsKey("Sky Time") &&
                  !spherePositions.ContainsKey("Sky Colors"),
                "Texture sky modes retain their texture control without Minecraft-only details.");

            panel.SkyMode = "box";
            var boxPositions = FindPositions("Skybox Mapped");
            Check(boxPositions.ContainsKey("Skybox Mapped"), "Skybox mode retains its mapped option.");

            panel.SkyMode = "minecraft";
            var minecraftPositions = FindPositions("Sky Time", "Sky Colors", "Stars", "Sun & Moon", "Clouds");
            Check(minecraftPositions.Count == 5,
                "Minecraft mode retains its time and all grouped sky controls.");
        }
        finally
        {
            manager.Manifest.Settings = originalSettings;
            manager.SetDirty(originalDirty);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

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
            var objectTab = typeof(PropertiesPanel).GetMethod("RenderObjectProperties", flags)!;
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
                        if (ImGui.BeginTabItem(Localization.Label("text.common.object", "Object")))
                        {
                            ImGui.BeginChild("##ObjectProperties", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
                            ImGui.GetStateStorage().SetInt(ImGui.GetID(Localization.Label("text.common.position", "Position")), 1);
                            ImGui.GetStateStorage().SetInt(ImGui.GetID(Localization.Label("text.common.appearance", "Appearance")), 1);
                            objectTab.Invoke(panel, null);
                            ImGui.EndChild();
                            ImGui.EndTabItem();
                        }
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

            obj.IgnoreDepth = true;
            var depthSnapshot = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
            Check(depthSnapshot.IgnoreDepth, "Ignore depth is serialized");
            var depthRoundtrip = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(depthSnapshot, AppJsonContext.Default.ProjectSceneObjectEntry),
                AppJsonContext.Default.ProjectSceneObjectEntry)!;
            obj.IgnoreDepth = false;
            typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [obj, depthRoundtrip, null]);
            Check(obj.IgnoreDepth, "Ignore depth survives project serialization");
            Reset(FindControl("object:ignore_depth", 45));
            Check(!obj.IgnoreDepth, "Ignore depth resets to the object default");
            var miTimeline = JsonSerializer.Deserialize(
                """{"depth_ignore":true}""", AppJsonContext.Default.MiTimeline)!;
            Check(miTimeline.DepthIgnore == true, "Mine-imator depth_ignore is imported");

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
