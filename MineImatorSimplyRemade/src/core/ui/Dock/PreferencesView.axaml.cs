using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of the old ImGui <c>PreferencesPanel.Render()</c>. Presents the
/// same settings (Program / Appearance / Timeline / Tools) as native Avalonia
/// controls bound to a <see cref="PreferencesPanel"/> model instance. Changes are
/// written back to the model and persisted via <see cref="PreferencesPanel.SavePreferences"/>;
/// theme/accent changes route through the model's <see cref="PreferencesPanel.ApplyTheme"/>/
/// <see cref="PreferencesPanel.ApplyAccentColor"/> so listeners stay notified.
/// </summary>
public partial class PreferencesView : UserControl
{
    private static readonly PreferencesPanel.ThemeMode[] ThemeModes =
    {
        PreferencesPanel.ThemeMode.Light,
        PreferencesPanel.ThemeMode.Dark,
        PreferencesPanel.ThemeMode.Darker,
    };

    private static readonly PreferencesPanel.AccentColor[] AccentColors =
    {
        PreferencesPanel.AccentColor.Red,
        PreferencesPanel.AccentColor.Orange,
        PreferencesPanel.AccentColor.Yellow,
        PreferencesPanel.AccentColor.Lime,
        PreferencesPanel.AccentColor.Green,
        PreferencesPanel.AccentColor.SkyBlue,
        PreferencesPanel.AccentColor.Blue,
        PreferencesPanel.AccentColor.Purple,
        PreferencesPanel.AccentColor.Pink,
        PreferencesPanel.AccentColor.Custom,
    };

    private PreferencesPanel _model = new();

    // Guards against change events firing while we programmatically populate the
    // controls in Bind()/reload, which would otherwise persist spurious values.
    private bool _suppressEvents;

    public PreferencesView()
    {
        InitializeComponent();

        ThemeCombo.ItemsSource = ThemeModes.Select(m => m.ToString()).ToList();
        AccentCombo.ItemsSource = AccentColors.Select(c => c.ToString()).ToList();
        MinecraftVersionCombo.ItemsSource = PreferencesDefaults.GetAvailableMinecraftVersions().ToList();

        MinecraftVersionCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            if (MinecraftVersionCombo.SelectedItem is string version)
            {
                _model.MinecraftVersion = version;
                _model.SavePreferences();
            }
        };
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            int idx = ThemeCombo.SelectedIndex;
            if (idx >= 0 && idx < ThemeModes.Length)
            {
                _model.ApplyTheme(ThemeModes[idx]);
                _model.SavePreferences();
            }
        };
        AccentCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            int idx = AccentCombo.SelectedIndex;
            if (idx >= 0 && idx < AccentColors.Length)
            {
                _model.ApplyAccentColor(AccentColors[idx]);
                _model.SavePreferences();
            }
        };
        LanguageCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            if (LanguageCombo.SelectedItem is ComboBoxItem { Content: string language })
            {
                _model.Language = language;
                _model.SavePreferences();
            }
        };

        WireCheckbox(AutomaticBackupsCheck, v => _model.AutomaticBackups = v);
        WireCheckbox(CopyWorkCameraCheck, v => _model.CopyWorkCameraIntoNewCameras = v);
        WireCheckbox(AutoScrollCheck, v => _model.AutoScrollWhilePlaying = v);
        WireCheckbox(ZIsUpCheck, v => _model.ZIsUp = v);
        WireCheckbox(HideMiBonesCheck, v => _model.HideMineImatorBoneDisplayShapes = v);
        WireCheckbox(HideRegularBonesCheck, v => _model.HideRegularAssetBoneDisplayShapes = v);

        RestoreDefaultsButton.Click += (_, _) =>
        {
            _model.RestoreDefaults();
            Bind(_model);
        };

        Bind(_model);
    }

    /// <summary>
    /// Points this view at the given preferences model, loading its current values
    /// into the controls. Pass the app-wide instance so edits persist and notify.
    /// </summary>
    public void Bind(PreferencesPanel model)
    {
        _model = model ?? new PreferencesPanel();

        _suppressEvents = true;
        try
        {
            MinecraftVersionCombo.SelectedItem = _model.MinecraftVersion;
            ThemeCombo.SelectedIndex = Array.IndexOf(ThemeModes, _model.Theme);
            AccentCombo.SelectedIndex = Array.IndexOf(AccentColors, _model.Accent);
            SelectLanguage(_model.Language);

            AutomaticBackupsCheck.IsChecked = _model.AutomaticBackups;
            CopyWorkCameraCheck.IsChecked = _model.CopyWorkCameraIntoNewCameras;
            AutoScrollCheck.IsChecked = _model.AutoScrollWhilePlaying;
            ZIsUpCheck.IsChecked = _model.ZIsUp;
            HideMiBonesCheck.IsChecked = _model.HideMineImatorBoneDisplayShapes;
            HideRegularBonesCheck.IsChecked = _model.HideRegularAssetBoneDisplayShapes;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SelectLanguage(string language)
    {
        foreach (var item in LanguageCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is string content &&
                string.Equals(content, language, StringComparison.OrdinalIgnoreCase))
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }

        LanguageCombo.SelectedIndex = 0;
    }

    private void WireCheckbox(CheckBox checkbox, Action<bool> setter)
    {
        checkbox.IsCheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            setter(checkbox.IsChecked ?? false);
            _model.SavePreferences();
        };
    }
}
