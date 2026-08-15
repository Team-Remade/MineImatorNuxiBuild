using System.Net;
using System.Text;
using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

public sealed class RmlPreferencesController
{
    private readonly Element _overlay;
    private readonly Element _root;
    private readonly PreferencesPanel _preferences;
    public bool Visible { get; private set; }

    public RmlPreferencesController(Element overlay, Element root, PreferencesPanel preferences)
    {
        _overlay = overlay;
        _root = root;
        _preferences = preferences;
        Refresh();
    }

    public void Toggle()
    {
        Visible = !Visible;
        _overlay.SetProperty("display", Visible ? "block" : "none");
        if (Visible) Refresh();
    }

    private void Refresh()
    {
        var html = new StringBuilder("""
            <div id="pref-scroll">
            """);
        Section(html, "Program");
        Row(html, "Minecraft version", _preferences.MinecraftVersion, "pref-version");
        ToggleRow(html, "Automatic backups", _preferences.AutomaticBackups, "pref-backups");
        ToggleRow(html, "Copy work camera into new cameras", _preferences.CopyWorkCameraIntoNewCameras, "pref-copy-camera");
        EndSection(html);
        Section(html, "Appearance");
        Row(html, "Theme", _preferences.Theme.ToString(), "pref-theme");
        Row(html, "Accent color", _preferences.Accent.ToString(), "pref-accent");
        Row(html, "Language", _preferences.Language, "pref-language");
        EndSection(html);
        Section(html, "Timeline and preview");
        ToggleRow(html, "Auto-scroll while playing", _preferences.AutoScrollWhilePlaying, "pref-autoscroll");
        ToggleRow(html, "Show preview viewport", _preferences.PreviewViewportVisible, "pref-preview");
        ToggleRow(html, "Undock preview viewport", _preferences.PreviewViewportUndocked, "pref-undock");
        EndSection(html);
        Section(html, "Tools");
        ToggleRow(html, "Z is up", _preferences.ZIsUp, "pref-z-up");
        ToggleRow(html, "Hide Mine-imator bone shapes", _preferences.HideMineImatorBoneDisplayShapes, "pref-hide-mi-bones");
        ToggleRow(html, "Hide regular asset bone shapes", _preferences.HideRegularAssetBoneDisplayShapes, "pref-hide-bones");
        EndSection(html);
        html.Append("</div><div id='pref-footer'><button id='pref-defaults'>Restore Defaults</button><button id='pref-close'>Close</button></div>");
        _root.SetInnerRml(html.ToString());

        Bind("pref-backups", () => _preferences.AutomaticBackups = !_preferences.AutomaticBackups);
        Bind("pref-copy-camera", () => _preferences.CopyWorkCameraIntoNewCameras = !_preferences.CopyWorkCameraIntoNewCameras);
        Bind("pref-autoscroll", () => _preferences.AutoScrollWhilePlaying = !_preferences.AutoScrollWhilePlaying);
        Bind("pref-preview", () => _preferences.PreviewViewportVisible = !_preferences.PreviewViewportVisible);
        Bind("pref-undock", () => _preferences.PreviewViewportUndocked = !_preferences.PreviewViewportUndocked);
        Bind("pref-z-up", () => _preferences.ZIsUp = !_preferences.ZIsUp);
        Bind("pref-hide-mi-bones", () => _preferences.HideMineImatorBoneDisplayShapes = !_preferences.HideMineImatorBoneDisplayShapes);
        Bind("pref-hide-bones", () => _preferences.HideRegularAssetBoneDisplayShapes = !_preferences.HideRegularAssetBoneDisplayShapes);
        Bind("pref-theme", CycleTheme);
        Bind("pref-accent", CycleAccent);
        Bind("pref-version", CycleVersion);
        Bind("pref-defaults", _preferences.RestoreDefaults);
        Bind("pref-close", Toggle);
    }

    private void CycleTheme()
    {
        PreferencesPanel.ThemeMode[] values = Enum.GetValues<PreferencesPanel.ThemeMode>();
        _preferences.Theme = values[(Array.IndexOf(values, _preferences.Theme) + 1) % values.Length];
        _preferences.ThemeChanged?.Invoke(_preferences.Theme);
    }

    private void CycleAccent()
    {
        PreferencesPanel.AccentColor[] values = Enum.GetValues<PreferencesPanel.AccentColor>();
        _preferences.Accent = values[(Array.IndexOf(values, _preferences.Accent) + 1) % values.Length];
        _preferences.AccentColorChanged?.Invoke(_preferences.Accent);
    }

    private void CycleVersion()
    {
        IReadOnlyList<string> values = PreferencesDefaults.GetAvailableMinecraftVersions();
        int index = values.IndexOf(_preferences.MinecraftVersion);
        _preferences.MinecraftVersion = values[(index + 1 + values.Count) % values.Count];
    }

    private void Bind(string id, Action change) => _root.GetElementById(id)?.AddEventListener("click", _ =>
    {
        change();
        _preferences.SavePreferences();
        Refresh();
    });
    private static void Section(StringBuilder html, string title) => html.Append("<div class='pref-section'><h3>").Append(WebUtility.HtmlEncode(title)).Append("</h3>");
    private static void EndSection(StringBuilder html) => html.Append("</div>");
    private static void Row(StringBuilder html, string label, string value, string id) => html.Append("<div class='pref-row'><span class='pref-label'>")
        .Append(WebUtility.HtmlEncode(label)).Append("</span><span class='pref-value'>").Append(WebUtility.HtmlEncode(value))
        .Append("</span><button id='").Append(id).Append("'>Change</button></div>");
    private static void ToggleRow(StringBuilder html, string label, bool value, string id) => Row(html, label, value ? "On" : "Off", id);
}

internal static class PreferenceListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (int i = 0; i < values.Count; i++) if (EqualityComparer<T>.Default.Equals(values[i], value)) return i;
        return -1;
    }
}
