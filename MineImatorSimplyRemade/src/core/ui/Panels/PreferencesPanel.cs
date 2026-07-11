using Hexa.NET.ImGui;
using System.Numerics;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Preferences panel that docks with other editor panels.
/// Contains application and interface preferences organized by category.
/// </summary>
public class PreferencesPanel : UiPanel
{
    // ── Program Preferences ────────────────────────────────────────────────────

    public string MinecraftVersion { get; set; } = "1.3.2";
    public bool AutomaticBackups { get; set; } = true;
    public bool CopyWorkCameraIntoNewCameras { get; set; } = true;

    // ── Interface Preferences ──────────────────────────────────────────────────

    // Appearance
    public enum ThemeMode
    {
        Light,
        Dark,
        Darker
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
        Custom
    }

    public ThemeMode Theme { get; set; } = ThemeMode.Darker;
    public AccentColor Accent { get; set; } = AccentColor.Purple;
    public string Language { get; set; } = "English";

    // Timeline
    public bool AutoScrollWhilePlaying { get; set; } = true;

    // Tools
    public bool ZIsUp { get; set; } = false;

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
    }

    /// <summary>
    /// Applies the selected theme (Light/Dark/Darker) to ImGui.
    /// After applying the theme, re-applies the current accent color on top.
    /// </summary>
    public void ApplyTheme(ThemeMode mode)
    {
        var style = ImGui.GetStyle();

        switch (mode)
        {
            case ThemeMode.Light:
                ImGui.StyleColorsLight();
                break;

            case ThemeMode.Dark:
                ImGui.StyleColorsDark();
                break;

            case ThemeMode.Darker:
                ImGui.StyleColorsDark();
                // Make the dark theme even darker
                style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.09f, 0.09f, 0.09f, 1.0f);
                style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.11f, 0.11f, 0.11f, 1.0f);
                style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.15f, 0.15f, 0.15f, 1.0f);
                style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.20f, 0.20f, 0.20f, 1.0f);
                style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.08f, 0.08f, 0.08f, 1.0f);
                style.Colors[(int)ImGuiCol.Header] = new Vector4(0.15f, 0.15f, 0.15f, 0.8f);
                style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.20f, 0.20f, 0.20f, 0.8f);
                style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.25f, 0.25f, 0.25f, 0.8f);
                break;
        }

        Theme = mode;

        // Re-apply accent color on top of the new theme (since StyleColors* resets all colors)
        ApplyAccentColorInternal(Accent);

        ThemeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Applies the selected accent color throughout the UI.
    /// </summary>
    public void ApplyAccentColor(AccentColor color)
    {
        Accent = color;
        ApplyAccentColorInternal(color);
        AccentColorChanged?.Invoke(color);
    }

    /// <summary>
    /// Internal helper to apply accent color without triggering callbacks.
    /// Used by ApplyTheme to re-apply accent after theme changes.
    /// </summary>
    private void ApplyAccentColorInternal(AccentColor color)
    {
        var style = ImGui.GetStyle();
        Vector4 accentVec = GetAccentColorVector(color);

        // Apply accent to various UI elements
        style.Colors[(int)ImGuiCol.Button] = accentVec with { W = 0.6f };
        style.Colors[(int)ImGuiCol.ButtonHovered] = accentVec with { W = 0.8f };
        style.Colors[(int)ImGuiCol.ButtonActive] = accentVec;
        style.Colors[(int)ImGuiCol.CheckMark] = accentVec;
        style.Colors[(int)ImGuiCol.SliderGrab] = accentVec with { W = 0.7f };
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accentVec;

        // Apply accent to window title bars
        style.Colors[(int)ImGuiCol.TitleBg] = accentVec with { W = 0.5f };
        style.Colors[(int)ImGuiCol.TitleBgActive] = accentVec with { W = 0.8f };
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = accentVec with { W = 0.3f };

        // Apply accent to tabs using reflection to find available tab color enums
        ApplyAccentToTabColors(accentVec);
    }

    /// <summary>
    /// Applies accent color to tab-related ImGui colors.
    /// </summary>
    private void ApplyAccentToTabColors(Vector4 accentVec)
    {
        var style = ImGui.GetStyle();

        // Apply accent to all tab-related colors
        style.Colors[(int)ImGuiCol.Tab] = accentVec with { W = 0.4f };
        style.Colors[(int)ImGuiCol.TabHovered] = accentVec with { W = 0.7f };
        style.Colors[(int)ImGuiCol.TabSelected] = accentVec;
        style.Colors[(int)ImGuiCol.TabDimmed] = accentVec with { W = 0.2f };
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = accentVec with { W = 0.5f };
    }

    /// <summary>
    /// Converts an AccentColor enum value to an RGBA vector.
    /// </summary>
    private Vector4 GetAccentColorVector(AccentColor color)
    {
        return color switch
        {
            AccentColor.Red => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
            AccentColor.Orange => new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
            AccentColor.Yellow => new Vector4(1.0f, 1.0f, 0.2f, 1.0f),
            AccentColor.Lime => new Vector4(0.7f, 1.0f, 0.2f, 1.0f),
            AccentColor.Green => new Vector4(0.2f, 1.0f, 0.5f, 1.0f),
            AccentColor.SkyBlue => new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
            AccentColor.Blue => new Vector4(0.3f, 0.5f, 1.0f, 1.0f),
            AccentColor.Purple => new Vector4(0.8f, 0.3f, 1.0f, 1.0f),
            AccentColor.Pink => new Vector4(1.0f, 0.4f, 0.7f, 1.0f),
            AccentColor.Custom => new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            _ => new Vector4(0.8f, 0.3f, 1.0f, 1.0f) // Default purple
        };
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

        if (!ImGui.Begin("Preferences", ref _windowOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Preferences");
        ImGui.Separator();

        // ── Program Section ───────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Program##PrefProgram", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Minecraft version (stub)
            {
                ImGui.TextUnformatted("Minecraft version:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                if (ImGui.BeginCombo("##minecraftVersion", MinecraftVersion))
                {
                    if (ImGui.Selectable("1.3.2", MinecraftVersion == "1.3.2"))
                        MinecraftVersion = "1.3.2";
                    ImGui.EndCombo();
                }
            }

            ImGui.Spacing();

            // Automatic Backups
            {
                bool backups = AutomaticBackups;
                if (ImGui.Checkbox("Automatic Backups##backups", ref backups))
                {
                    AutomaticBackups = backups;
                    // TODO: Implement automatic backups
                }
            }

            ImGui.Spacing();

            // Copy work camera into new cameras
            {
                bool copyCamera = CopyWorkCameraIntoNewCameras;
                if (ImGui.Checkbox("Copy work camera into new cameras##copyCamera", ref copyCamera))
                {
                    CopyWorkCameraIntoNewCameras = copyCamera;
                }
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ── Interface Section ──────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Interface##PrefInterface", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // ── Appearance ──
            if (ImGui.TreeNodeEx("Appearance", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                // Theme
                {
                    ImGui.TextUnformatted("Theme:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    string themeName = Theme.ToString();
                    if (ImGui.BeginCombo("##theme", themeName))
                    {
                        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.Darker })
                        {
                            bool selected = Theme == mode;
                            if (ImGui.Selectable(mode.ToString(), selected))
                            {
                                ApplyTheme(mode);
                            }
                        }
                        ImGui.EndCombo();
                    }
                }

                ImGui.Spacing();

                // Accent Color
                {
                    ImGui.TextUnformatted("Accent color:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    string accentName = Accent.ToString();
                    if (ImGui.BeginCombo("##accent", accentName))
                    {
                        foreach (var color in new[] { AccentColor.Red, AccentColor.Orange, AccentColor.Yellow, AccentColor.Lime, AccentColor.Green, AccentColor.SkyBlue, AccentColor.Blue, AccentColor.Purple, AccentColor.Pink, AccentColor.Custom })
                        {
                            bool selected = Accent == color;
                            if (ImGui.Selectable(color.ToString(), selected))
                            {
                                ApplyAccentColor(color);
                            }
                        }
                        ImGui.EndCombo();
                    }
                }

                ImGui.Spacing();

                // Language
                {
                    ImGui.TextUnformatted("Language:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.BeginCombo("##language", Language))
                    {
                        if (ImGui.Selectable("English", Language == "English"))
                            Language = "English";
                        ImGui.EndCombo();
                    }
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Spacing();

            // ── Timeline ──
            if (ImGui.TreeNodeEx("Timeline", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                bool autoScroll = AutoScrollWhilePlaying;
                if (ImGui.Checkbox("Auto scroll while playing animation##autoScroll", ref autoScroll))
                {
                    AutoScrollWhilePlaying = autoScroll;
                    // TODO: Implement timeline auto-scroll
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Spacing();

            // ── Tools ──
            if (ImGui.TreeNodeEx("Tools", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                bool zUp = ZIsUp;
                if (ImGui.Checkbox("Z is up##zUp", ref zUp))
                {
                    ZIsUp = zUp;
                    // TODO: Implement Z-up coordinate system
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Unindent();
        }

        ImGui.End();
    }
}
