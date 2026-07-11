using Hexa.NET.ImGui;

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
    /// Tracks whether the preferences panel is open/visible.
    /// Starts hidden; can be toggled via menubar or close button.
    /// </summary>
    private bool _windowOpen = false;

    /// <summary>
    /// Toggle visibility of the preferences panel.
    /// </summary>
    public void ToggleVisibility()
    {
        _windowOpen = !_windowOpen;
    }

    /// <summary>
    /// Renders the preferences panel as a standard docking window.
    /// </summary>
    public override void Render()
    {
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
                    // TODO: Implement camera copying behavior
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
                                Theme = mode;
                                // TODO: Apply theme
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
                                Accent = color;
                                // TODO: Apply accent color
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
