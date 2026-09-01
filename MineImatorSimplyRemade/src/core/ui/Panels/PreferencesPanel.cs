using System.Text.Json;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Preferences settings model.
///
/// MIGRATION: this used to be an ImGui <c>UiPanel</c> that both stored the
/// preference values AND drew the settings UI (theme/accent applied directly to
/// the ImGui style). In the Avalonia port the UI moved to
/// <see cref="core.ui.Dock.PreferencesView"/>, so this class is now a plain
/// model: it holds the preference values, persists them to disk, and raises
/// <see cref="ThemeChanged"/>/<see cref="AccentColorChanged"/> so listeners can
/// react. It is still referenced by the not-yet-ported <c>Viewport</c>/
/// <c>SpawnMenu</c> panels for their enums/properties.
/// </summary>
public class PreferencesPanel
{
    // ── Program Preferences ────────────────────────────────────────────────────

    public string MinecraftVersion { get; set; } = PreferencesDefaults.GetDefaultMinecraftVersion();
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
    /// Updates the selected theme and notifies listeners.
    /// </summary>
    public void ApplyTheme(ThemeMode mode)
    {
        Theme = mode;
        ThemeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Updates the selected accent color and notifies listeners.
    /// </summary>
    public void ApplyAccentColor(AccentColor color)
    {
        Accent = color;
        AccentColorChanged?.Invoke(color);
    }

    /// <summary>
    /// Saves the current preferences to disk.
    /// </summary>
    public void SavePreferences()
    {
        var state = new PreferencesState
        {
            MinecraftVersion = MinecraftVersion,
            AutomaticBackups = AutomaticBackups,
            CopyWorkCameraIntoNewCameras = CopyWorkCameraIntoNewCameras,
            Theme = Theme,
            Accent = Accent,
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
        AutomaticBackups = state.AutomaticBackups;
        CopyWorkCameraIntoNewCameras = state.CopyWorkCameraIntoNewCameras;
        Theme = state.Theme;
        Accent = state.Accent;
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
        AutomaticBackups = defaults.AutomaticBackups;
        CopyWorkCameraIntoNewCameras = defaults.CopyWorkCameraIntoNewCameras;
        Theme = defaults.Theme;
        Accent = defaults.Accent;
        Language = defaults.Language;
        AutoScrollWhilePlaying = defaults.AutoScrollWhilePlaying;
        PreviewViewportVisible = defaults.PreviewViewportVisible;
        PreviewViewportUndocked = defaults.PreviewViewportUndocked;
        ZIsUp = defaults.ZIsUp;
        HideMineImatorBoneDisplayShapes = defaults.HideMineImatorBoneDisplayShapes;
        HideRegularAssetBoneDisplayShapes = defaults.HideRegularAssetBoneDisplayShapes;

        // Notify listeners so dependent UI stays in sync.
        ThemeChanged?.Invoke(Theme);
        AccentColorChanged?.Invoke(Accent);

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
    private PreferencesState? LoadPreferencesState()
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
    public bool AutomaticBackups { get; set; } = true;
    public bool CopyWorkCameraIntoNewCameras { get; set; } = true;
    public PreferencesPanel.ThemeMode Theme { get; set; } = PreferencesPanel.ThemeMode.Darker;
    public PreferencesPanel.AccentColor Accent { get; set; } = PreferencesPanel.AccentColor.Purple;
    public string Language { get; set; } = "English";
    public bool AutoScrollWhilePlaying { get; set; } = true;
    public bool PreviewViewportVisible { get; set; } = true;
    public bool PreviewViewportUndocked { get; set; } = false;
    public bool ZIsUp { get; set; } = false;
    public bool HideMineImatorBoneDisplayShapes { get; set; } = true;
    public bool HideRegularAssetBoneDisplayShapes { get; set; } = false;
}
