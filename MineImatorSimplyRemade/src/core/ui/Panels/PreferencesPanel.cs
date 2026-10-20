using Hexa.NET.ImGui;
using System.Numerics;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Preferences panel that docks with other editor panels.
/// Contains application and interface preferences organized by category.
/// </summary>
public class PreferencesPanel : UiPanel
{
    // ── Program Preferences ────────────────────────────────────────────────────

    public string MinecraftVersion { get; set; } = PreferencesDefaults.GetDefaultMinecraftVersion();
    public int TargetFps { get; set; } = 60;
    public bool AutomaticBackups { get; set; } = true;
    public bool CopyWorkCameraIntoNewCameras { get; set; } = true;

    // ── Interface Preferences ──────────────────────────────────────────────────

    // Appearance
    public enum ThemeMode
    {
        Light,
        Dark,
        Darker,
        Nord,
        PureBlack
    }

    public enum AccentColor
    {
        Red,
        Orange,
        Yellow,
        Lime,
        Green,
        SkyBlue,
        Blue,
        Purple,
        Pink,
        Cyan,
        Indigo,
        Custom
    }

    public ThemeMode Theme { get; set; } = ThemeMode.Darker;
    public AccentColor Accent { get; set; } = AccentColor.Purple;
    public Vector4 CustomAccentColor { get; set; } = new Vector4(0.58f, 0.36f, 0.96f, 1.0f);
    public string Language
    {
        get => Localization.CurrentLanguage;
        set => Localization.SetLanguage(value);
    }

    // Timeline
    public bool AutoScrollWhilePlaying { get; set; } = true;

    // Preview viewport
    public bool PreviewViewportVisible { get; set; } = true;
    public bool PreviewViewportUndocked { get; set; } = false;

    // Tools
    public bool ZIsUp { get; set; } = false;
    public bool HideMineImatorBoneDisplayShapes { get; set; } = true;
    public bool HideRegularAssetBoneDisplayShapes { get; set; } = false;

    /// <summary>
    /// Callback invoked when the theme changes.
    /// </summary>
    public Action<ThemeMode>? ThemeChanged { get; set; }

    /// <summary>
    /// Callback invoked when the accent color changes.
    /// </summary>
    public Action<AccentColor>? AccentColorChanged { get; set; }

    /// <summary>
    /// Tracks whether the preferences panel is open/visible.
    /// Starts hidden; can be toggled via menubar or close button.
    /// </summary>
    private bool _windowOpen = false;

    /// <summary>
    /// Tracks whether the initial theme/accent have been applied.
    /// This is done on first Render() to ensure ImGui is ready.
    /// </summary>
    private bool _defaultsApplied = false;

    /// <summary>
    /// Toggle visibility of the preferences panel.
    /// </summary>
    public void ToggleVisibility()
    {
        _windowOpen = !_windowOpen;
        if (_windowOpen)
            Localization.RefreshLanguages();
    }

    /// <summary>
    /// Applies the selected theme (Light/Dark/Darker) to ImGui.
    /// After applying the theme, re-applies the current accent color on top.
    /// Only affects whichever ImGui context is current when called - see
    /// <see cref="ApplyThemeAndAccentToCurrentContext"/> for syncing other windows.
    /// </summary>
    public void ApplyTheme(ThemeMode mode)
    {
        Theme = mode;
        ApplyThemeInternal(mode);

        ThemeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Re-applies the currently selected theme and accent color (<see cref="Theme"/>/
    /// <see cref="Accent"/>) to whichever ImGui context is current, without changing
    /// the stored preference or firing <see cref="ThemeChanged"/>/<see cref="AccentColorChanged"/>.
    ///
    /// Each secondary window (e.g. the popped-out camera window) owns its own independent
    /// ImGui context/style (see <see cref="core.window.Window.SetupImgui"/>), so the
    /// Darker theme + accent overrides applied here only ever land on whichever context
    /// was current at the time. Callers that own a secondary window should call this via
    /// <see cref="core.window.Window.WithContextCurrent"/> both once at startup (after that
    /// window's context is created) and whenever <see cref="ThemeChanged"/> or
    /// <see cref="AccentColorChanged"/> fires, so its style stays in sync with the main
    /// window instead of falling back to ImGui's default blue theme.
    /// </summary>
    public void ApplyThemeAndAccentToCurrentContext() => ApplyThemeInternal(Theme);

    /// <summary>
    /// Applies <paramref name="mode"/>'s base colors plus the current accent color to
    /// whichever ImGui context is current. Does not update <see cref="Theme"/> or fire
    /// any change events - callers decide whether/when to do that.
    /// </summary>
    private void ApplyThemeInternal(ThemeMode mode)
    {
        UiTheme.ThemePreset themePreset = mode switch
        {
            ThemeMode.Light => UiTheme.ThemePreset.Light,
            ThemeMode.Dark => UiTheme.ThemePreset.Dark,
            ThemeMode.Nord => UiTheme.ThemePreset.Nord,
            ThemeMode.PureBlack => UiTheme.ThemePreset.PureBlack,
            _ => UiTheme.ThemePreset.Darker
        };

        UiTheme.AccentPreset accentPreset = Accent switch
        {
            AccentColor.Red => UiTheme.AccentPreset.Red,
            AccentColor.Orange => UiTheme.AccentPreset.Orange,
            AccentColor.Yellow => UiTheme.AccentPreset.Yellow,
            AccentColor.Lime => UiTheme.AccentPreset.Lime,
            AccentColor.Green => UiTheme.AccentPreset.Green,
            AccentColor.SkyBlue => UiTheme.AccentPreset.SkyBlue,
            AccentColor.Blue => UiTheme.AccentPreset.Blue,
            AccentColor.Purple => UiTheme.AccentPreset.Purple,
            AccentColor.Pink => UiTheme.AccentPreset.Pink,
            AccentColor.Cyan => UiTheme.AccentPreset.Cyan,
            AccentColor.Indigo => UiTheme.AccentPreset.Indigo,
            AccentColor.Custom => UiTheme.AccentPreset.Custom,
            _ => UiTheme.AccentPreset.Purple
        };

        UiTheme.ApplyTheme(themePreset, accentPreset, CustomAccentColor);
    }

    /// <summary>
    /// Applies the selected accent color throughout the UI.
    /// </summary>
    public void ApplyAccentColor(AccentColor color)
    {
        Accent = color;
        ApplyThemeInternal(Theme);
        AccentColorChanged?.Invoke(color);
    }

    /// <summary>
    /// Converts an AccentColor enum value to an RGBA vector.
    /// </summary>
    public Vector4 GetAccentColorVector(AccentColor color)
    {
        UiTheme.AccentPreset preset = color switch
        {
            AccentColor.Red => UiTheme.AccentPreset.Red,
            AccentColor.Orange => UiTheme.AccentPreset.Orange,
            AccentColor.Yellow => UiTheme.AccentPreset.Yellow,
            AccentColor.Lime => UiTheme.AccentPreset.Lime,
            AccentColor.Green => UiTheme.AccentPreset.Green,
            AccentColor.SkyBlue => UiTheme.AccentPreset.SkyBlue,
            AccentColor.Blue => UiTheme.AccentPreset.Blue,
            AccentColor.Purple => UiTheme.AccentPreset.Purple,
            AccentColor.Pink => UiTheme.AccentPreset.Pink,
            AccentColor.Cyan => UiTheme.AccentPreset.Cyan,
            AccentColor.Indigo => UiTheme.AccentPreset.Indigo,
            AccentColor.Custom => UiTheme.AccentPreset.Custom,
            _ => UiTheme.AccentPreset.Purple
        };
        return UiTheme.GetAccentColorVector(preset, CustomAccentColor);
    }

    /// <summary>
    /// Renders the preferences panel as a standard docking window.
    /// </summary>
    public override void Render()
    {
        // Apply default theme and accent on first render (after ImGui is fully initialized)
        if (!_defaultsApplied)
        {
            _defaultsApplied = true;
            ApplyTheme(Theme);
            ApplyAccentColor(Accent);
        }

        if (!_windowOpen)
            return;

        if (!ImGui.Begin(Localization.Label("text.common.preferences", "Preferences"), ref _windowOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(Localization.T("text.common.preferences"));
        ImGui.Separator();

        bool preferencesChanged = false;
        string? selectedLanguage = null;

        // ── Program Section ───────────────────────────────────────────────────
        if (ImGui.CollapsingHeader(Localization.Label("text.panel.preferences.program", "Program##PrefProgram"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            IReadOnlyList<string> availableMinecraftVersions = PreferencesDefaults.GetAvailableMinecraftVersions();

            {
                ImGui.TextUnformatted(Localization.T("text.panel.preferences.minecraft_version"));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150f * UiTheme.Scale);
                if (ImGui.BeginCombo("##minecraftVersion", MinecraftVersion))
                {
                    foreach (string version in availableMinecraftVersions)
                    {
                        bool selected = MinecraftVersion == version;
                        if (ImGui.Selectable(version, selected))
                        {
                            MinecraftVersion = version;
                            preferencesChanged = true;
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.Spacing();

            // Target FPS
            {
                int targetFps = TargetFps;
                string format = targetFps == 0 ? Localization.T("text.panel.preferences.unlimited").Replace("%", "%%") : "%d";
                if (ImGui.SliderInt(Localization.Label("text.panel.preferences.target_framerate", "Target framerate##targetFps"), ref targetFps, 0, 120, format))
                {
                    TargetFps = targetFps;
                    preferencesChanged = true;
                }
            }

            ImGui.Spacing();

            // Automatic Backups
            {
                bool backups = AutomaticBackups;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.automatic_backups", "Automatic Backups##backups"), ref backups))
                {
                    AutomaticBackups = backups;
                    preferencesChanged = true;
                    // TODO: Implement automatic backups
                }
            }

            ImGui.Spacing();

            // Copy work camera into new cameras
            {
                bool copyCamera = CopyWorkCameraIntoNewCameras;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.copy_work_camera_into_new_cameras", "Copy work camera into new cameras##copyCamera"), ref copyCamera))
                {
                    CopyWorkCameraIntoNewCameras = copyCamera;
                    preferencesChanged = true;
                }
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ── Interface Section ──────────────────────────────────────────────────
        if (ImGui.CollapsingHeader(Localization.Label("text.panel.preferences.interface", "Interface##PrefInterface"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // ── Appearance ──
            if (ImGui.TreeNodeEx(Localization.Label("text.common.appearance", "Appearance"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                // Theme
                {
                    ImGui.TextUnformatted(Localization.T("text.panel.preferences.theme"));
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(180f * UiTheme.Scale);
                    string themeName = Theme switch
                    {
                        ThemeMode.Darker => "Midnight Darker (Default)",
                        ThemeMode.Dark => "Studio Dark",
                        ThemeMode.Nord => "Nord Arctic Slate",
                        ThemeMode.PureBlack => "OLED Pure Black",
                        ThemeMode.Light => "Clean Light",
                        _ => Theme.ToString()
                    };

                    if (ImGui.BeginCombo("##theme", Localization.DisplayText(themeName)))
                    {
                        var modes = new[]
                        {
                            (ThemeMode.Darker, "Midnight Darker (Default)"),
                            (ThemeMode.Dark, "Studio Dark"),
                            (ThemeMode.Nord, "Nord Arctic Slate"),
                            (ThemeMode.PureBlack, "OLED Pure Black"),
                            (ThemeMode.Light, "Clean Light")
                        };

                        foreach (var (mode, label) in modes)
                        {
                            bool selected = Theme == mode;
                            if (ImGui.Selectable(Localization.DisplayLabel(label), selected))
                            {
                                ApplyTheme(mode);
                                preferencesChanged = true;
                            }
                        }
                        ImGui.EndCombo();
                    }
                }

                ImGui.Spacing();

                // Accent Color
                {
                    ImGui.TextUnformatted(Localization.T("text.panel.preferences.accent_color"));
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(180f * UiTheme.Scale);
                    string accentName = Accent.ToString();
                    if (ImGui.BeginCombo("##accent", Localization.DisplayText(accentName)))
                    {
                        var colors = new[]
                        {
                            AccentColor.Purple,
                            AccentColor.Blue,
                            AccentColor.SkyBlue,
                            AccentColor.Cyan,
                            AccentColor.Green,
                            AccentColor.Lime,
                            AccentColor.Yellow,
                            AccentColor.Orange,
                            AccentColor.Red,
                            AccentColor.Pink,
                            AccentColor.Indigo,
                            AccentColor.Custom
                        };

                        foreach (var color in colors)
                        {
                            bool selected = Accent == color;
                            Vector4 colorVec = GetAccentColorVector(color);
                            
                            // Draw small color swatch
                            ImGui.ColorButton($"##swatch_{color}", colorVec, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(16, 16) * UiTheme.Scale);
                            ImGui.SameLine();
                            if (ImGui.Selectable(Localization.DisplayLabel(color.ToString()), selected))
                            {
                                ApplyAccentColor(color);
                                preferencesChanged = true;
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (Accent == AccentColor.Custom)
                    {
                        ImGui.Spacing();
                        ImGui.TextUnformatted(Localization.T("text.panel.preferences.custom_color"));
                        ImGui.SameLine();
                        Vector4 custom = CustomAccentColor;
                        if (ImGui.ColorEdit4("##customAccentPicker", ref custom, ImGuiColorEditFlags.AlphaBar))
                        {
                            CustomAccentColor = custom;
                            ApplyAccentColor(AccentColor.Custom);
                            preferencesChanged = true;
                        }
                    }
                }

                ImGui.Spacing();

                // Language
                {
                    ImGui.TextUnformatted(Localization.T("text.panel.preferences.language"));
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(180f * UiTheme.Scale);
                    if (ImGui.BeginCombo("##language", Localization.CurrentLanguageName))
                    {
                        foreach (var language in Localization.AvailableLanguages)
                        {
                            bool selected = Language == language.Code;
                            ImGui.PushID(language.Code);
                            if (ImGui.Selectable(language.Name + "###languageOption", selected))
                            {
                                selectedLanguage = language.Code;
                                preferencesChanged = true;
                            }
                            if (selected)
                                ImGui.SetItemDefaultFocus();
                            ImGui.PopID();
                        }
                        ImGui.EndCombo();
                    }
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Spacing();

            // ── Timeline ──
            if (ImGui.TreeNodeEx(Localization.Label("text.common.timeline", "Timeline"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                bool autoScroll = AutoScrollWhilePlaying;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.auto_scroll_while_playing_animation", "Auto scroll while playing animation##autoScroll"), ref autoScroll))
                {
                    AutoScrollWhilePlaying = autoScroll;
                    preferencesChanged = true;
                    // TODO: Implement timeline auto-scroll
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Spacing();

            // ── Tools ──
            if (ImGui.TreeNodeEx(Localization.Label("text.panel.preferences.tools", "Tools"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                bool zUp = ZIsUp;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.z_is_up", "Z is up##zUp"), ref zUp))
                {
                    ZIsUp = zUp;
                    preferencesChanged = true;
                    // TODO: Implement Z-up coordinate system
                }

                ImGui.Spacing();

                bool hideBoneDisplayShapes = HideMineImatorBoneDisplayShapes;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.hide_bone_display_shapes_on_mine_imator_assets", "Hide bone display shapes on Mine-imator assets##hideMiBoneDisplayShapes"), ref hideBoneDisplayShapes))
                {
                    HideMineImatorBoneDisplayShapes = hideBoneDisplayShapes;
                    preferencesChanged = true;
                }

                ImGui.Spacing();

                bool hideRegularBoneDisplayShapes = HideRegularAssetBoneDisplayShapes;
                if (ImGui.Checkbox(Localization.Label("text.panel.preferences.hide_bone_display_shapes_on_regular_assets", "Hide bone display shapes on regular assets##hideRegularBoneDisplayShapes"), ref hideRegularBoneDisplayShapes))
                {
                    HideRegularAssetBoneDisplayShapes = hideRegularBoneDisplayShapes;
                    preferencesChanged = true;
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(Localization.Label("text.panel.preferences.restore_defaults", "Restore Defaults")))
        {
            RestoreDefaults();
            preferencesChanged = false;
        }

        // Save preferences if any changed
        if (preferencesChanged)
        {
            if (selectedLanguage != null)
                Language = selectedLanguage;
            SavePreferences();
        }

        ImGui.End();
    }

    /// <summary>
    /// Saves the current preferences to disk.
    /// </summary>
    public void SavePreferences()
    {
        var state = new PreferencesState
        {
            MinecraftVersion = MinecraftVersion,
            TargetFps = TargetFps,
            AutomaticBackups = AutomaticBackups,
            CopyWorkCameraIntoNewCameras = CopyWorkCameraIntoNewCameras,
            Theme = Theme,
            Accent = Accent,
            CustomAccentColor = CustomAccentColor,
            Language = Language,
            AutoScrollWhilePlaying = AutoScrollWhilePlaying,
            PreviewViewportVisible = PreviewViewportVisible,
            PreviewViewportUndocked = PreviewViewportUndocked,
            ZIsUp = ZIsUp,
            HideMineImatorBoneDisplayShapes = HideMineImatorBoneDisplayShapes,
            HideRegularAssetBoneDisplayShapes = HideRegularAssetBoneDisplayShapes
        };

        SavePreferencesState(state);
    }

    /// <summary>
    /// Applies the language before startup UI exists, without applying ImGui preferences.
    /// </summary>
    public static void LoadLanguagePreference() => Localization.SetLanguage(LoadPreferencesState()?.Language);

    /// <summary>
    /// Loads preferences from disk. If no saved preferences exist, returns false
    /// and the PreferencesPanel retains its default values.
    /// </summary>
    public bool LoadPreferences()
    {
        var state = LoadPreferencesState();
        if (state == null)
            return false;

        MinecraftVersion = string.IsNullOrWhiteSpace(state.MinecraftVersion)
            ? PreferencesDefaults.GetDefaultMinecraftVersion()
            : state.MinecraftVersion;
        TargetFps = Math.Clamp(state.TargetFps, 0, 120);
        AutomaticBackups = state.AutomaticBackups;
        CopyWorkCameraIntoNewCameras = state.CopyWorkCameraIntoNewCameras;
        Theme = state.Theme;
        Accent = state.Accent;
        if (state.CustomAccentColor != Vector4.Zero)
            CustomAccentColor = state.CustomAccentColor;
        Language = state.Language;
        AutoScrollWhilePlaying = state.AutoScrollWhilePlaying;
        PreviewViewportVisible = state.PreviewViewportVisible;
        PreviewViewportUndocked = state.PreviewViewportUndocked;
        ZIsUp = state.ZIsUp;
        HideMineImatorBoneDisplayShapes = state.HideMineImatorBoneDisplayShapes;
        HideRegularAssetBoneDisplayShapes = state.HideRegularAssetBoneDisplayShapes;

        return true;
    }

    /// <summary>
    /// Restores all preferences to their default values and persists them.
    /// </summary>
    public void RestoreDefaults()
    {
        var defaults = new PreferencesState();

        MinecraftVersion = defaults.MinecraftVersion;
        TargetFps = defaults.TargetFps;
        AutomaticBackups = defaults.AutomaticBackups;
        CopyWorkCameraIntoNewCameras = defaults.CopyWorkCameraIntoNewCameras;
        Theme = defaults.Theme;
        Accent = defaults.Accent;
        CustomAccentColor = defaults.CustomAccentColor;
        Language = defaults.Language;
        AutoScrollWhilePlaying = defaults.AutoScrollWhilePlaying;
        PreviewViewportVisible = defaults.PreviewViewportVisible;
        PreviewViewportUndocked = defaults.PreviewViewportUndocked;
        ZIsUp = defaults.ZIsUp;
        HideMineImatorBoneDisplayShapes = defaults.HideMineImatorBoneDisplayShapes;
        HideRegularAssetBoneDisplayShapes = defaults.HideRegularAssetBoneDisplayShapes;

        // Re-apply styling and notify listeners so secondary windows stay in sync.
        ApplyTheme(Theme);

        SavePreferences();
    }

    /// <summary>
    /// Gets the path where preferences are stored on disk.
    /// </summary>
    private static string PreferencesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MineImatorSimplyRemade",
        "preferences.json");

    /// <summary>
    /// Loads preferences from disk, returning null if the file doesn't exist or cannot be read.
    /// </summary>
    private static PreferencesState? LoadPreferencesState()
    {
        if (!File.Exists(PreferencesFilePath))
            return null;

        try
        {
            string json = File.ReadAllText(PreferencesFilePath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.PreferencesState)
                   ?? null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves preferences to disk.
    /// </summary>
    private void SavePreferencesState(PreferencesState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesFilePath) ?? 
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            var writerOptions = new JsonWriterOptions { Indented = true };
            using var stream = File.Create(PreferencesFilePath);
            using var writer = new Utf8JsonWriter(stream, writerOptions);
            JsonSerializer.Serialize(writer, state, AppJsonContext.Default.PreferencesState);
            writer.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save preferences: {ex.Message}");
        }
    }
}

internal static class PreferencesDefaults
{
    private const string LegacyFallbackMinecraftVersion = "1.3.2";

    public static string GetDefaultMinecraftVersion()
    {
        if (!string.IsNullOrWhiteSpace(BlockRegistry.LoadedVersion))
            return BlockRegistry.LoadedVersion;

        string versionRoot = MinecraftDataLoader.GetVersionRoot(LegacyFallbackMinecraftVersion);
        string? version = Path.GetFileName(versionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return string.IsNullOrWhiteSpace(version) ? LegacyFallbackMinecraftVersion : version;
    }

    public static IReadOnlyList<string> GetAvailableMinecraftVersions()
    {
        string currentVersion = GetDefaultMinecraftVersion();
        string versionsDir = Path.Combine(MinecraftDataLoader.GetBasePath(), "data", "minecraft", "versions");

        if (!Directory.Exists(versionsDir))
            return new[] { currentVersion };

        List<string> versions = Directory
            .GetDirectories(versionsDir)
            .Select(Path.GetFileName)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!versions.Contains(currentVersion, StringComparer.OrdinalIgnoreCase))
            versions.Insert(0, currentVersion);

        return versions;
    }
}

/// <summary>
/// Serializable representation of user preferences.
/// This class is used for JSON serialization/deserialization of preference state.
/// </summary>
public class PreferencesState
{
    public string MinecraftVersion { get; set; } = PreferencesDefaults.GetDefaultMinecraftVersion();
    public int TargetFps { get; set; } = 60;
    public bool AutomaticBackups { get; set; } = true;
    public bool CopyWorkCameraIntoNewCameras { get; set; } = true;
    public PreferencesPanel.ThemeMode Theme { get; set; } = PreferencesPanel.ThemeMode.Darker;
    public PreferencesPanel.AccentColor Accent { get; set; } = PreferencesPanel.AccentColor.Purple;
    public Vector4 CustomAccentColor { get; set; } = new Vector4(0.58f, 0.36f, 0.96f, 1.0f);
    public string Language { get; set; } = Localization.DefaultLanguage;
    public bool AutoScrollWhilePlaying { get; set; } = true;
    public bool PreviewViewportVisible { get; set; } = true;
    public bool PreviewViewportUndocked { get; set; } = false;
    public bool ZIsUp { get; set; } = false;
    public bool HideMineImatorBoneDisplayShapes { get; set; } = true;
    public bool HideRegularAssetBoneDisplayShapes { get; set; } = false;
}
