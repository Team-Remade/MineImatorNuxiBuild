using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of the old ImGui <c>core.ui.Panels.PropertiesPanel</c> panel
/// (Project tab: project/render/background settings + object library; Object
/// tab: transforms). All state and behaviour live in the injected
/// <see cref="PropertiesPanel"/> model; this view only builds controls and
/// forwards edits. Sections are populated in code because the old panel was
/// fully imperative and the row patterns repeat heavily.
///
/// Not yet ported from the old Object tab: particle, item/text-mesh sections,
/// shape keys, and the right-click
/// "add keyframe" context menus (model APIs for those already exist).
/// </summary>
public partial class PropertiesView : UserControl
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#BBBBBB"));
    private static readonly IBrush ValueBrush = new SolidColorBrush(Color.Parse("#DDDDDD"));
    private static readonly IBrush DisabledBrush = new SolidColorBrush(Color.Parse("#777777"));

    private static readonly int[] ShadowBufferSizes = [256, 512, 1024, 2048, 4096, 8192];

    private readonly PropertiesPanel _model;
    private readonly List<Action> _refreshers = new();
    private bool _syncing;
    private DispatcherTimer? _timer;

    // ── Object tab controls ───────────────────────────────────────────────────
    private CheckBox _inheritPosCheck = null!;
    private CheckBox _inheritRotCheck = null!;
    private CheckBox _inheritScaleCheck = null!;
    private CheckBox _linkScaleCheck = null!;
    private NumericUpDown[] _posNuds = null!;
    private NumericUpDown[] _rotNuds = null!;
    private NumericUpDown[] _scaleNuds = null!;
    private NumericUpDown[] _tileNuds = null!;
    private TextBlock _tileTotalLabel = null!;

    /// <summary>Parameterless constructor for the XAML designer / previewer.</summary>
    public PropertiesView() : this(new PropertiesPanel())
    {
    }

    public PropertiesView(PropertiesPanel model)
    {
        _model = model;
        InitializeComponent();

        BuildProjectSettingsSection();
        BuildRenderSettingsSection();
        BuildBackgroundSettingsSection();
        BuildObjectTabSections();
        WireLibrarySection();

        AttachedToVisualTree += (_, _) =>
        {
            _model.SettingsLoaded += OnModelSettingsLoaded;
            _model.ObjectLibraryChanged += OnObjectLibraryChanged;
            if (SelectionManager.Instance != null)
                SelectionManager.Instance.SelectionChanged += OnSelectionChanged;

            RefreshAll();
            RebuildLibrary();
            RebuildObjectTab();

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OnTimerTick);
            _timer.Start();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _model.SettingsLoaded -= OnModelSettingsLoaded;
            _model.ObjectLibraryChanged -= OnObjectLibraryChanged;
            if (SelectionManager.Instance != null)
                SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;

            _timer?.Stop();
            _timer = null;
        };
    }

    private void OnModelSettingsLoaded()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshAll();
            RebuildLibrary();
        });
    }

    private void OnObjectLibraryChanged() => Dispatcher.UIThread.Post(RebuildLibrary);

    private void OnSelectionChanged() => Dispatcher.UIThread.Post(RebuildObjectTab);

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Replaces the old per-frame Render() call: drives background keyframe
        // animation and keeps transform fields in sync with gizmo drags.
        _model.ApplyBackgroundAnimation(_model.Timeline?.CurrentFrame ?? 0);
        RefreshObjectValues();
    }

    // ── Shared plumbing ───────────────────────────────────────────────────────

    /// <summary>Runs the post-edit hook, then persists settings to the manifest.</summary>
    private void CommitAfter(Action? after)
    {
        after?.Invoke();
        if (ProjectManager.Instance.HasProject)
        {
            _model.WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
            ProjectManager.Instance.SetDirty(true);
        }
    }

    private void RefreshAll()
    {
        _syncing = true;
        try
        {
            foreach (var refresh in _refreshers)
                refresh();
        }
        finally
        {
            _syncing = false;
        }
    }

    private static decimal SafeDecimal(double value, decimal min, decimal max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return min;
        if (value <= (double)min) return min;
        if (value >= (double)max) return max;
        return (decimal)value;
    }

    // ── Row builder helpers ───────────────────────────────────────────────────

    private static TextBlock SectionLabel(Panel parent, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = ValueBrush,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6, 0, 0),
        };
        parent.Children.Add(label);
        return label;
    }

    private static TextBlock MutedLabel(Panel parent, string text)
    {
        var label = new TextBlock { Text = text, Foreground = DisabledBrush, TextWrapping = TextWrapping.Wrap };
        parent.Children.Add(label);
        return label;
    }

    /// <summary>Indented sub-panel whose visibility follows a toggle checkbox.</summary>
    private StackPanel Gated(Panel parent, CheckBox toggle)
    {
        var sub = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(16, 0, 0, 0),
            IsVisible = toggle.IsChecked == true,
        };
        toggle.IsCheckedChanged += (_, _) => sub.IsVisible = toggle.IsChecked == true;
        _refreshers.Add(() => sub.IsVisible = toggle.IsChecked == true);
        parent.Children.Add(sub);
        return sub;
    }

    private CheckBox Check(Panel parent, string label, Func<bool> get, Action<bool> set, Action? after = null)
    {
        var check = new CheckBox { Content = label, Foreground = MutedBrush, IsChecked = get() };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            set(check.IsChecked == true);
            CommitAfter(after);
        };
        _refreshers.Add(() => check.IsChecked = get());
        parent.Children.Add(check);
        return check;
    }

    private void SliderRow(Panel parent, string label, double min, double max, Func<float> get, Action<float> set,
        Action? after = null, string suffix = "", string format = "0.##")
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,56") };
        var text = new TextBlock
        {
            Text = label, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(get(), (float)min, (float)max) };
        var valueLabel = new TextBlock
        {
            Foreground = ValueBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        void UpdateLabel() => valueLabel.Text = get().ToString(format, CultureInfo.InvariantCulture) + suffix;
        UpdateLabel();

        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            set((float)e.NewValue);
            UpdateLabel();
            CommitAfter(after);
        };
        _refreshers.Add(() =>
        {
            slider.Value = Math.Clamp(get(), (float)min, (float)max);
            UpdateLabel();
        });

        Grid.SetColumn(text, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueLabel, 2);
        grid.Children.Add(text);
        grid.Children.Add(slider);
        grid.Children.Add(valueLabel);
        parent.Children.Add(grid);
    }

    private void IntSliderRow(Panel parent, string label, int min, int max, Func<int> get, Action<int> set, Action? after = null)
        => SliderRow(parent, label, min, max, () => get(), v => set((int)Math.Round(v)), after, "", "0");

    /// <summary>Old <c>PercentageSlider</c>: field stores a normalized value, UI shows percent.</summary>
    private void PercentSliderRow(Panel parent, string label, double minPercent, double maxPercent, Func<float> get, Action<float> set, Action? after = null)
        => SliderRow(parent, label, minPercent, maxPercent, () => get() * 100f, v => set(v / 100f), after, "%", "0");

    private NumericUpDown NumberRow(Panel parent, string label, decimal min, decimal max, decimal increment,
        Func<double> get, Action<double> set, Action? after = null, string format = "0.###")
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var text = new TextBlock
        {
            Text = label, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var nud = new NumericUpDown
        {
            Minimum = min, Maximum = max, Increment = increment,
            Value = SafeDecimal(get(), min, max),
            FormatString = format,
            MinWidth = 110,
        };
        nud.ValueChanged += (_, e) =>
        {
            if (_syncing || e.NewValue == null) return;
            set((double)e.NewValue.Value);
            CommitAfter(after);
        };
        _refreshers.Add(() =>
        {
            if (!nud.IsKeyboardFocusWithin)
                nud.Value = SafeDecimal(get(), min, max);
        });

        Grid.SetColumn(text, 0);
        Grid.SetColumn(nud, 1);
        grid.Children.Add(text);
        grid.Children.Add(nud);
        parent.Children.Add(grid);
        return nud;
    }

    private ComboBox ComboRow(Panel parent, string label, IReadOnlyList<string> items, Func<int> get, Action<int> set, Action? after = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var text = new TextBlock
        {
            Text = label, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = Math.Clamp(get(), 0, items.Count - 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0) return;
            set(combo.SelectedIndex);
            CommitAfter(after);
        };
        _refreshers.Add(() => combo.SelectedIndex = Math.Clamp(get(), 0, items.Count - 1));

        Grid.SetColumn(text, 0);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(text);
        grid.Children.Add(combo);
        parent.Children.Add(grid);
        return combo;
    }

    private TextBox TextRow(Panel parent, string label, Func<string> get, Action<string> set, Action? after = null)
    {
        if (!string.IsNullOrEmpty(label))
            SectionLabel(parent, label);

        var box = new TextBox { Text = get() };
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            set(box.Text ?? "");
            CommitAfter(after);
        };
        _refreshers.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = get();
        });
        parent.Children.Add(box);
        return box;
    }

    private void ButtonRow(Panel parent, params (string Label, Action OnClick)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (label, onClick) in buttons)
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 2) };
            button.Click += (_, _) => onClick();
            row.Children.Add(button);
        }
        parent.Children.Add(row);
    }

    /// <summary>Color swatch button backed by a single color picker, editing a model float[] in place.</summary>
    private void ColorRow(Panel parent, string label, float[] channels, bool hasAlpha, Action? after = null)
    {
        ColorRow(parent, label,
            () => Color.FromArgb(
                hasAlpha ? ToByte(channels[3]) : (byte)255,
                ToByte(channels[0]), ToByte(channels[1]), ToByte(channels[2])),
            color =>
            {
                channels[0] = color.R / 255f;
                channels[1] = color.G / 255f;
                channels[2] = color.B / 255f;
                if (hasAlpha)
                    channels[3] = color.A / 255f;
            },
            hasAlpha, after);
    }

    private void ColorRow(Panel parent, string label, Func<Color> get, Action<Color> set, bool hasAlpha, Action? after = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var text = new TextBlock
        {
            Text = label, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var swatch = new Border
        {
            Width = 44, Height = 16,
            CornerRadius = new CornerRadius(2),
            BorderBrush = MutedBrush, BorderThickness = new Thickness(1),
        };
        var button = new Button
        {
            Content = swatch,
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        void UpdateSwatch()
        {
            swatch.Background = new SolidColorBrush(get());
        }
        UpdateSwatch();

        var picker = new ColorPicker
        {
            Color = get(),
            IsAlphaEnabled = hasAlpha,
            Width = 240,
        };
        picker.PropertyChanged += (_, eventArgs) =>
        {
            if (_syncing || eventArgs.Property != ColorPicker.ColorProperty)
                return;
            set(picker.Color);
            UpdateSwatch();
            CommitAfter(after);
        };

        button.Flyout = new Flyout { Content = picker };
        _refreshers.Add(() =>
        {
            picker.Color = get();
            UpdateSwatch();
        });

        Grid.SetColumn(text, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(text);
        grid.Children.Add(button);
        parent.Children.Add(grid);
    }

    private static byte ToByte(float channel) => (byte)(Math.Clamp(channel, 0f, 1f) * 255f);

    // ── Project Settings section ──────────────────────────────────────────────

    private void BuildProjectSettingsSection()
    {
        var panel = ProjectSettingsPanel;

        TextRow(panel, "Project Name:", () => _model.ProjectName, v => _model.ProjectName = v);

        SectionLabel(panel, "Resolution");
        NumberRow(panel, "Width", 1, 16384, 1,
            () => _model.GetResolutionWidth(),
            v => _model.SetRenderDimensionsAndFramerate((int)v, _model.GetResolutionHeight(), _model.GetFramerate()),
            format: "0");
        NumberRow(panel, "Height", 1, 16384, 1,
            () => _model.GetResolutionHeight(),
            v => _model.SetRenderDimensionsAndFramerate(_model.GetResolutionWidth(), (int)v, _model.GetFramerate()),
            format: "0");
        ButtonRow(panel,
            ("720p", () => SetResolutionPreset(1280, 720)),
            ("1080p", () => SetResolutionPreset(1920, 1080)),
            ("1440p", () => SetResolutionPreset(2560, 1440)),
            ("4K", () => SetResolutionPreset(3840, 2160)));

        SectionLabel(panel, "Framerate");
        NumberRow(panel, "FPS", 1, 120, 1,
            () => _model.GetFramerate(),
            v => SetFramerate((int)v),
            format: "0");
        ButtonRow(panel,
            ("24", () => SetFramerate(24)),
            ("30", () => SetFramerate(30)),
            ("60", () => SetFramerate(60)),
            ("120", () => SetFramerate(120)));

        SectionLabel(panel, "Texture Animation Speed");
        NumberRow(panel, "FPS", 1, 240, 1,
            () => _model.TextureAnimationFps,
            v => _model.TextureAnimationFps = (int)v,
            format: "0");
        ButtonRow(panel,
            ("10", () => SetTextureFps(10)),
            ("20", () => SetTextureFps(20)),
            ("30", () => SetTextureFps(30)),
            ("60", () => SetTextureFps(60)));
    }

    private void SetResolutionPreset(int width, int height)
    {
        _model.SetRenderDimensionsAndFramerate(width, height, _model.GetFramerate());
        CommitAfter(null);
        RefreshAll();
    }

    private void SetFramerate(int fps)
    {
        _model.SetRenderDimensionsAndFramerate(_model.GetResolutionWidth(), _model.GetResolutionHeight(), fps);
        _model.Timeline?.SetFrameRate(fps);
        CommitAfter(null);
        RefreshAll();
    }

    private void SetTextureFps(int fps)
    {
        _model.TextureAnimationFps = fps;
        CommitAfter(null);
        RefreshAll();
    }

    // ── Render Settings section ───────────────────────────────────────────────

    private void BuildRenderSettingsSection()
    {
        var panel = RenderSettingsPanel;

        SectionLabel(panel, "Ambient Occlusion");
        var aoCheck = Check(panel, "Enabled", () => _model.AmbientOcclusionEnabled, v => _model.AmbientOcclusionEnabled = v);
        var ao = Gated(panel, aoCheck);
        NumberRow(ao, "Radius (px)", 0, 128, 0.5m, () => _model.AmbientOcclusionRadius, v => _model.AmbientOcclusionRadius = (float)v, format: "0.#");
        PercentSliderRow(ao, "Strength", 0, 200, () => _model.AmbientOcclusionStrength, v => _model.AmbientOcclusionStrength = v);
        IntSliderRow(ao, "Samples", 1, 128, () => _model.AmbientOcclusionSampleCount, v => _model.AmbientOcclusionSampleCount = v);
        ColorRow(ao, "Color", _model.AmbientOcclusionColor, hasAlpha: false);
        SliderRow(ao, "Ratio", 0, 1, () => _model.AmbientOcclusionRatio, v => _model.AmbientOcclusionRatio = v, format: "0.###");
        SliderRow(ao, "Ratio Balance", 0, 1, () => _model.AmbientOcclusionRatioBalance, v => _model.AmbientOcclusionRatioBalance = v, format: "0.###");

        SectionLabel(panel, "Indirect Lighting");
        var giCheck = Check(panel, "Enabled", () => _model.IndirectLightingEnabled, v => _model.IndirectLightingEnabled = v);
        var gi = Gated(panel, giCheck);
        ComboRow(gi, "Global Illumination", ["Screen Space", "World Space (Experimental)"],
            () => string.Equals(_model.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            i => _model.GlobalIlluminationMode = i == 1 ? "world" : "screenspace");
        PercentSliderRow(gi, "Precision", 0, 100, () => _model.IndirectLightingPrecision, v => _model.IndirectLightingPrecision = v);
        PercentSliderRow(gi, "Strength", 0, 400, () => _model.IndirectLightingStrength, v => _model.IndirectLightingStrength = v);
        NumberRow(gi, "Ray Step", 1, 64, 0.5m, () => _model.IndirectLightingRayStep, v => _model.IndirectLightingRayStep = (float)v, format: "0.#");
        NumberRow(gi, "Blur Radius", 0, 8, 0.25m, () => _model.IndirectLightingBlurRadius, v => _model.IndirectLightingBlurRadius = (float)v, format: "0.##");
        var denoiserCheck = Check(gi, "Denoiser", () => _model.IndirectLightingDenoiser, v => _model.IndirectLightingDenoiser = v);
        var denoiser = Gated(gi, denoiserCheck);
        SliderRow(denoiser, "Denoiser Strength", 0, 200, () => _model.IndirectLightingDenoiserStrength, v => _model.IndirectLightingDenoiserStrength = v, suffix: "%", format: "0");

        SectionLabel(panel, "Shadows");
        var shadowsCheck = Check(panel, "Enabled", () => _model.ShadowsEnabled, v => _model.ShadowsEnabled = v);
        var shadows = Gated(panel, shadowsCheck);
        ShadowBufferRow(shadows, "Sun lamps", () => _model.SunShadowBufferSize, v => _model.SunShadowBufferSize = v);
        ShadowBufferRow(shadows, "Spot lights", () => _model.SpotShadowBufferSize, v => _model.SpotShadowBufferSize = v);
        ShadowBufferRow(shadows, "Point lights", () => _model.PointShadowBufferSize, v => _model.PointShadowBufferSize = v);
        PercentSliderRow(shadows, "Blur Strength", 0, 400, () => _model.ShadowBlurStrength, v => _model.ShadowBlurStrength = v);

        SectionLabel(panel, "Glow");
        var glowCheck = Check(panel, "Enabled", () => _model.GlowEnabled, v => _model.GlowEnabled = v);
        var glow = Gated(panel, glowCheck);
        PercentSliderRow(glow, "Strength", 0, 200, () => _model.GlowStrength, v => _model.GlowStrength = v);
        NumberRow(glow, "Size (px)", 0, 20, 0.5m, () => _model.GlowSize, v => _model.GlowSize = (float)v, format: "0.#");

        SectionLabel(panel, "Subsurface Scattering");
        var sssCheck = Check(panel, "Enabled", () => _model.SubsurfaceEnabled, v => _model.SubsurfaceEnabled = v);
        var sss = Gated(panel, sssCheck);
        IntSliderRow(sss, "Blur Samples", 0, 32, () => _model.SubsurfaceBlurSamples, v => _model.SubsurfaceBlurSamples = v);
        PercentSliderRow(sss, "Strength", 0, 400, () => _model.SubsurfaceStrength, v => _model.SubsurfaceStrength = v);
        PercentSliderRow(sss, "Desaturation", 0, 100, () => _model.SubsurfaceDesaturation, v => _model.SubsurfaceDesaturation = v);
        PercentSliderRow(sss, "Color Threshold", 0, 100, () => _model.SubsurfaceColorThreshold, v => _model.SubsurfaceColorThreshold = v);
        NumberRow(sss, "Radius R", 0.0001m, 8, 0.05m, () => _model.SubsurfaceRadiusRgb[0], v => _model.SubsurfaceRadiusRgb[0] = (float)v);
        NumberRow(sss, "Radius G", 0.0001m, 8, 0.05m, () => _model.SubsurfaceRadiusRgb[1], v => _model.SubsurfaceRadiusRgb[1] = (float)v);
        NumberRow(sss, "Radius B", 0.0001m, 8, 0.05m, () => _model.SubsurfaceRadiusRgb[2], v => _model.SubsurfaceRadiusRgb[2] = (float)v);
        NumberRow(sss, "Highlight Size", 0, 8, 0.05m, () => _model.SubsurfaceHighlightSize, v => _model.SubsurfaceHighlightSize = (float)v);
        PercentSliderRow(sss, "Highlight Strength", 0, 800, () => _model.SubsurfaceHighlightStrength, v => _model.SubsurfaceHighlightStrength = v);
        NumberRow(sss, "Highlight Sharpness", 0.01m, 16, 0.1m, () => _model.SubsurfaceHighlightSharpness, v => _model.SubsurfaceHighlightSharpness = (float)v);
        PercentSliderRow(sss, "Highlight Desaturation", 0, 100, () => _model.SubsurfaceHighlightDesaturation, v => _model.SubsurfaceHighlightDesaturation = v);
        PercentSliderRow(sss, "Highlight Color Threshold", 0, 100, () => _model.SubsurfaceHighlightColorThreshold, v => _model.SubsurfaceHighlightColorThreshold = v);
        SliderRow(sss, "Absorption", -0.95, 0.95, () => _model.SubsurfaceAbsorption, v => _model.SubsurfaceAbsorption = v);
    }

    private void ShadowBufferRow(Panel parent, string label, Func<int> get, Action<int> set)
    {
        ComboRow(parent, label,
            ShadowBufferSizes.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray(),
            () => Math.Max(0, Array.IndexOf(ShadowBufferSizes, get())),
            i => set(ShadowBufferSizes[i]));
    }

    // ── Background Settings section ───────────────────────────────────────────

    private void BuildBackgroundSettingsSection()
    {
        var panel = BackgroundPanel;

        // Sky change hook: matches old skyChanged => Viewport.ReloadSkyTextures() + persist.
        void SkyChanged() => _model.ReloadSkyTextures?.Invoke();

        var skyCheck = Check(panel, "Minecraft Sky", () => _model.UseSky, v => _model.UseSky = v, SkyChanged);
        var sky = Gated(panel, skyCheck);
        var solidLabel = MutedLabel(panel, "Solid background color is active.");
        solidLabel.IsVisible = !_model.UseSky;
        skyCheck.IsCheckedChanged += (_, _) => solidLabel.IsVisible = skyCheck.IsChecked != true;
        _refreshers.Add(() => solidLabel.IsVisible = !_model.UseSky);

        ColorRow(sky, "Horizon Day", _model.SkyHorizonDay, false, SkyChanged);
        ColorRow(sky, "Zenith Day", _model.SkyZenithDay, false, SkyChanged);
        ColorRow(sky, "Horizon Sunset", _model.SkyHorizonSunset, false, SkyChanged);
        ColorRow(sky, "Zenith Sunset", _model.SkyZenithSunset, false, SkyChanged);
        ColorRow(sky, "Night Horizon", _model.SkyHorizonNight, false, SkyChanged);
        ColorRow(sky, "Night Zenith", _model.SkyZenithNight, false, SkyChanged);
        Check(sky, "Twilight", () => _model.Twilight, v => _model.Twilight = v, SkyChanged);

        SectionLabel(sky, "Stars");
        var starsCheck = Check(sky, "Show Stars", () => _model.ShowStars, v => _model.ShowStars = v, SkyChanged);
        var stars = Gated(sky, starsCheck);
        SliderRow(stars, "Density", 0, 5, () => _model.StarDensity, v => _model.StarDensity = v, SkyChanged);
        SliderRow(stars, "Brightness", 0, 5, () => _model.StarBrightness, v => _model.StarBrightness = v, SkyChanged);
        SliderRow(stars, "Twinkle Speed", 0, 5, () => _model.StarTwinkleSpeed, v => _model.StarTwinkleSpeed = v, SkyChanged);
        ColorRow(stars, "Star Color", _model.StarColor, false, SkyChanged);

        SectionLabel(sky, "Clouds");
        ColorRow(sky, "Cloud Color", _model.CloudColor, false, SkyChanged);
        ColorRow(sky, "Night Cloud Color", _model.NightCloudColor, false, SkyChanged);
        SkyTextureCombo(sky, "Sun Texture", "sun.png", () => _model.SunTexture, v => _model.SunTexture = v);
        SkyTextureCombo(sky, "Moon Texture", "moon_phases.png", () => _model.MoonTexture, v => _model.MoonTexture = v);
        SkyTextureCombo(sky, "Cloud Texture", "clouds.png", () => _model.CloudTexture, v => _model.CloudTexture = v);
        ComboRow(sky, "Cloud Rendering", ["3D", "Story Mode", "Flat"],
            () => _model.CloudRenderMode switch { "story" => 1, "flat" => 2, _ => 0 },
            i => _model.CloudRenderMode = i switch { 1 => "story", 2 => "flat", _ => "3d" },
            SkyChanged);
        NumberRow(sky, "Cloud Speed", -10000, 10000, 1, () => _model.CloudSpeed, v => _model.CloudSpeed = (float)v, SkyChanged, "0");
        NumberRow(sky, "Cloud Offset X", -100000, 100000, 1, () => _model.CloudOffset[0], v => _model.CloudOffset[0] = (float)v, SkyChanged, "0");
        NumberRow(sky, "Cloud Offset Y", -100000, 100000, 1, () => _model.CloudOffset[1], v => _model.CloudOffset[1] = (float)v, SkyChanged, "0");
        NumberRow(sky, "Cloud Height", 0, 100000, 1, () => _model.CloudHeight, v => _model.CloudHeight = (float)v, SkyChanged, "0");
        NumberRow(sky, "Cloud Block Size", 1, 100000, 1, () => _model.CloudBlockSize, v => _model.CloudBlockSize = (float)v, SkyChanged, "0");
        NumberRow(sky, "Cloud Thickness", 1, 100000, 1, () => _model.CloudThickness, v => _model.CloudThickness = (float)v, SkyChanged, "0");

        SectionLabel(sky, "Sun && Moon");
        IntSliderRow(sky, "Moon Phase", 0, 7, () => _model.MoonPhase, v => _model.MoonPhase = v, SkyChanged);
        SliderRow(sky, "Time", 0, 24, () => _model.SkyTime, v => _model.SkyTime = v, SkyChanged, " h");
        NumberRow(sky, "Sun Size (deg)", 0.1m, 90, 0.5m, () => _model.SunSize, v => _model.SunSize = (float)v, SkyChanged, "0.#");
        NumberRow(sky, "Sun Angle X", -360, 360, 1, () => _model.SunAngle[0], v => _model.SunAngle[0] = (float)v, SkyChanged, "0.##");
        NumberRow(sky, "Sun Angle Y", -360, 360, 1, () => _model.SunAngle[1], v => _model.SunAngle[1] = (float)v, SkyChanged, "0.##");
        NumberRow(sky, "Sun Angle Z", -360, 360, 1, () => _model.SunAngle[2], v => _model.SunAngle[2] = (float)v, SkyChanged, "0.##");
        NumberRow(sky, "Moon Size (deg)", 0.1m, 90, 0.5m, () => _model.MoonSize, v => _model.MoonSize = (float)v, SkyChanged, "0.#");
        NumberRow(sky, "Moon Angle X", -360, 360, 1, () => _model.MoonAngle[0], v => _model.MoonAngle[0] = (float)v, SkyChanged, "0.##");
        NumberRow(sky, "Moon Angle Y", -360, 360, 1, () => _model.MoonAngle[1], v => _model.MoonAngle[1] = (float)v, SkyChanged, "0.##");
        NumberRow(sky, "Moon Angle Z", -360, 360, 1, () => _model.MoonAngle[2], v => _model.MoonAngle[2] = (float)v, SkyChanged, "0.##");
        ColorRow(sky, "Sun Fill Light", _model.SunFillLightColor, false, SkyChanged);
        SliderRow(sky, "Sun Fill Strength", 0, 5, () => _model.SunFillLightStrength, v => _model.SunFillLightStrength = v, SkyChanged);
        Check(sky, "Sun Fill Casts Shadows", () => _model.SunFillLightCastsShadows, v => _model.SunFillLightCastsShadows = v, SkyChanged);
        ColorRow(sky, "Moon Fill Light", _model.MoonFillLightColor, false, SkyChanged);
        SliderRow(sky, "Moon Fill Strength", 0, 5, () => _model.MoonFillLightStrength, v => _model.MoonFillLightStrength = v, SkyChanged);
        Check(sky, "Moon Fill Casts Shadows", () => _model.MoonFillLightCastsShadows, v => _model.MoonFillLightCastsShadows = v, SkyChanged);

        // ── Fog ──
        SectionLabel(panel, "Fog");
        var fogCheck = Check(panel, "Enable Fog", () => _model.FogEnabled, v => _model.FogEnabled = v);
        var fog = Gated(panel, fogCheck);
        Check(fog, "Fog the Sky", () => _model.SkyFog, v => _model.SkyFog = v);
        var customFogCheck = Check(fog, "Custom Fog Color", () => _model.CustomFogColor, v => _model.CustomFogColor = v);
        var customFog = Gated(fog, customFogCheck);
        ColorRow(customFog, "Fog Color", _model.FogColor, false);
        var customObjFogCheck = Check(fog, "Custom Object Fog Color", () => _model.CustomObjectFogColor, v => _model.CustomObjectFogColor = v);
        var customObjFog = Gated(fog, customObjFogCheck);
        ColorRow(customObjFog, "Object Fog Color", _model.ObjectFogColor, false);
        NumberRow(fog, "Distance (px)", 0, 1000000, 10, () => _model.FogDistance, v => _model.FogDistance = (float)v, format: "0");
        NumberRow(fog, "Fade Size (px)", 1, 1000000, 10, () => _model.FogFadeSize, v => _model.FogFadeSize = (float)v, format: "0");
        NumberRow(fog, "Height (px)", -1000000, 1000000, 10, () => _model.FogHeight, v => _model.FogHeight = (float)v, format: "0");
        var heightFogCheck = Check(fog, "Height Fog", () => _model.HeightFog, v => _model.HeightFog = v);
        var heightFog = Gated(fog, heightFogCheck);
        var customHeightFogCheck = Check(heightFog, "Custom Height Fog Color", () => _model.CustomHeightFogColor, v => _model.CustomHeightFogColor = v);
        var customHeightFog = Gated(heightFog, customHeightFogCheck);
        ColorRow(customHeightFog, "Height Fog Color", _model.HeightFogColor, false);
        NumberRow(heightFog, "Height Fog Size (px)", 1, 1000000, 10, () => _model.HeightFogSize, v => _model.HeightFogSize = (float)v, format: "0");
        NumberRow(heightFog, "Height Fog Offset (px)", -1000000, 1000000, 10, () => _model.HeightFogOffset, v => _model.HeightFogOffset = (float)v, format: "0");

        // ── Background color ──
        SectionLabel(panel, "Background Color");
        ColorRow(panel, "Color", _model.BackgroundColor, hasAlpha: true);
        var presets = new (string Name, float R, float G, float B, float A)[]
        {
            ("Dawn", 1f, 0.7f, 0.5f, 1f),
            ("Morning", 0.6f, 0.8f, 1f, 1f),
            ("Day", 0.5764706f, 0.5764706f, 1f, 1f),
            ("Sunset", 1f, 0.5f, 0.3f, 1f),
            ("Dusk", 0.3f, 0.4f, 0.7f, 1f),
            ("Night", 0.05f, 0.05f, 0.15f, 1f),
        };
        ButtonRow(panel, presets.Select(p => (p.Name, (Action)(() =>
        {
            _model.BackgroundColor[0] = p.R;
            _model.BackgroundColor[1] = p.G;
            _model.BackgroundColor[2] = p.B;
            _model.BackgroundColor[3] = p.A;
            CommitAfter(null);
            RefreshAll();
        }))).ToArray());

        // ── Floor ──
        SectionLabel(panel, "Floor");
        void FloorChanged()
        {
            _model.ApplyFloorSettingsToViewport();
        }
        Check(panel, "Show Floor", () => _model.FloorVisible, v => _model.FloorVisible = v, FloorChanged);
        ComboRow(panel, "Floor Atlas", ["Block Atlas", "Item Atlas"],
            () => string.Equals(_model.FloorTextureAtlas, "item", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            i => _model.FloorTextureAtlas = i == 1 ? "item" : "block",
            () => { FloorChanged(); RefreshAll(); });
        FloorTileCombo(panel);

        // ── Background image ──
        SectionLabel(panel, "Background Image");
        void BackgroundChanged() => _model.ApplyBackgroundSettingsToViewport();
        BackgroundImageCombo(panel);
        ButtonRow(panel,
            ("Import", () =>
            {
                if (_model.ImportBackgroundImageFromDialog())
                {
                    CommitAfter(BackgroundChanged);
                    RefreshAll();
                }
            }),
            ("Clear", () =>
            {
                _model.BackgroundImagePath = "No image selected";
                CommitAfter(BackgroundChanged);
                RefreshAll();
            }));
        ComboRow(panel, "Background Mode", ["Stretch", "Fit", "Original"],
            () => _model.BackgroundRenderMode switch { "fit" => 1, "original" => 2, _ => 0 },
            i =>
            {
                _model.BackgroundRenderMode = i switch { 1 => "fit", 2 => "original", _ => "stretch" };
                _model.StretchBackground = i == 0;
            },
            BackgroundChanged);
        NumberRow(panel, "Background Scale", 0.01m, 20, 0.05m, () => _model.BackgroundScale, v => _model.BackgroundScale = (float)v, BackgroundChanged);
        NumberRow(panel, "Background Rotation", -360, 360, 1, () => _model.BackgroundRotationDegrees, v => _model.BackgroundRotationDegrees = (float)v, BackgroundChanged, "0.##");
        NumberRow(panel, "Background Offset X", -3, 3, 0.01m, () => _model.BackgroundOffset[0], v => _model.BackgroundOffset[0] = (float)v, BackgroundChanged);
        NumberRow(panel, "Background Offset Y", -3, 3, 0.01m, () => _model.BackgroundOffset[1], v => _model.BackgroundOffset[1] = (float)v, BackgroundChanged);
        ButtonRow(panel, ("Reset Transform", () =>
        {
            _model.BackgroundScale = 1f;
            _model.BackgroundRotationDegrees = 0f;
            _model.BackgroundOffset[0] = 0f;
            _model.BackgroundOffset[1] = 0f;
            CommitAfter(BackgroundChanged);
            RefreshAll();
        }));

        // ── Ambient / fill lights ──
        void AmbientChanged() => _model.ApplyAmbientSettingsToRenderer();
        SectionLabel(panel, "Ambient Light");
        ColorRow(panel, "Color", _model.AmbientLightColor, false, AmbientChanged);
        SliderRow(panel, "Ambient Strength", 0, 5, () => _model.AmbientLightStrength, v => _model.AmbientLightStrength = v, AmbientChanged);
        SectionLabel(panel, "Night Ambient");
        ColorRow(panel, "Color", _model.NightAmbientLightColor, false, AmbientChanged);
        SliderRow(panel, "Night Ambient Strength", 0, 5, () => _model.NightAmbientLightStrength, v => _model.NightAmbientLightStrength = v, AmbientChanged);
        SectionLabel(panel, "Fill Light");
        ColorRow(panel, "Color", _model.FillLightColor, false, AmbientChanged);
        SliderRow(panel, "Fill Strength", 0, 5, () => _model.FillLightStrength, v => _model.FillLightStrength = v, AmbientChanged);
        Check(panel, "Fill Light Casts Shadows", () => _model.FillLightCastsShadows, v => _model.FillLightCastsShadows = v, AmbientChanged);

        BuildBackgroundAnimationControls(panel);
    }

    private void SkyTextureCombo(Panel parent, string label, string vanillaFile, Func<string> get, Action<string> set)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var text = new TextBlock
        {
            Text = label, Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var optionKeys = new List<string>();

        void Rebuild()
        {
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                optionKeys.Clear();
                var labels = new List<string>();
                string vanilla = $"minecraft:environment/{vanillaFile}";
                optionKeys.Add(vanilla);
                labels.Add($"Vanilla / {vanillaFile}");

                foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png"))
                {
                    string normalized = file.RelativePath.Replace('\\', '/');
                    if (!normalized.Contains("/textures/environment/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    optionKeys.Add(MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, normalized));
                    labels.Add($"{file.PackName} / {Path.GetFileName(normalized)}");
                }

                combo.ItemsSource = labels;
                combo.SelectedIndex = Math.Max(0, optionKeys.IndexOf(get()));
            }
            finally
            {
                _syncing = wasSyncing;
            }
        }

        Rebuild();
        combo.DropDownOpened += (_, _) => Rebuild();
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0 || combo.SelectedIndex >= optionKeys.Count) return;
            set(optionKeys[combo.SelectedIndex]);
            CommitAfter(() => _model.ReloadSkyTextures?.Invoke());
        };
        _refreshers.Add(Rebuild);

        Grid.SetColumn(text, 0);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(text);
        grid.Children.Add(combo);
        parent.Children.Add(grid);
    }

    private void FloorTileCombo(Panel parent)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var text = new TextBlock { Text = "Floor Tile", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center };
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var keys = new List<string>();

        void Rebuild()
        {
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                keys.Clear();
                keys.AddRange(_model.GetFloorAtlasKeys());
                combo.ItemsSource = keys.ToList();
                combo.SelectedIndex = keys.IndexOf(_model.FloorTileKey);
            }
            finally
            {
                _syncing = wasSyncing;
            }
        }

        Rebuild();
        combo.DropDownOpened += (_, _) => Rebuild();
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0 || combo.SelectedIndex >= keys.Count) return;
            _model.FloorTileKey = keys[combo.SelectedIndex];
            CommitAfter(_model.ApplyFloorSettingsToViewport);
        };
        _refreshers.Add(Rebuild);

        Grid.SetColumn(text, 0);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(text);
        grid.Children.Add(combo);
        parent.Children.Add(grid);
    }

    private void BackgroundImageCombo(Panel parent)
    {
        const string noImage = "No image selected";
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var paths = new List<string>();

        void Rebuild()
        {
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                paths.Clear();
                var labels = new List<string>();
                paths.Add(noImage);
                labels.Add(noImage);

                foreach (var asset in _model.GetBackgroundImageAssets())
                {
                    string candidatePath = !string.IsNullOrWhiteSpace(asset.RelativePath) ? asset.RelativePath : asset.SourcePath;
                    if (string.IsNullOrWhiteSpace(candidatePath))
                        continue;

                    paths.Add(candidatePath);
                    labels.Add(string.IsNullOrWhiteSpace(asset.DisplayName) ? Path.GetFileName(candidatePath) : asset.DisplayName);
                }

                combo.ItemsSource = labels;
                int index = paths.FindIndex(p => string.Equals(p, _model.BackgroundImagePath, StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = Math.Max(0, index);
            }
            finally
            {
                _syncing = wasSyncing;
            }
        }

        Rebuild();
        combo.DropDownOpened += (_, _) => Rebuild();
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0 || combo.SelectedIndex >= paths.Count) return;
            _model.BackgroundImagePath = paths[combo.SelectedIndex];
            CommitAfter(_model.ApplyBackgroundSettingsToViewport);
        };
        _refreshers.Add(Rebuild);
        parent.Children.Add(combo);
    }

    private void BuildBackgroundAnimationControls(Panel parent)
    {
        SectionLabel(parent, "Background Animation");

        var propCombo = new ComboBox
        {
            ItemsSource = PropertiesPanel.BackgroundKeyframePropertyPaths,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var valueBox = new TextBox { Watermark = "Value" };

        void LoadValue()
        {
            object? current = _model.GetBackgroundPropertyValue(_model.SelectedBackgroundKeyProperty);
            valueBox.Text = current is IFormattable f
                ? f.ToString(null, CultureInfo.InvariantCulture)
                : current?.ToString() ?? "";
        }

        propCombo.SelectedIndex = Math.Max(0,
            PropertiesPanel.BackgroundKeyframePropertyPaths.ToList().IndexOf(_model.SelectedBackgroundKeyProperty));
        LoadValue();

        propCombo.SelectionChanged += (_, _) =>
        {
            if (propCombo.SelectedItem is not string path) return;
            _model.SelectedBackgroundKeyProperty = path;
            LoadValue();
        };
        _refreshers.Add(LoadValue);

        parent.Children.Add(propCombo);

        var valueRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var setButton = new Button { Content = "Set", Padding = new Thickness(10, 2), Margin = new Thickness(6, 0, 0, 0) };
        setButton.Click += (_, _) =>
        {
            string path = _model.SelectedBackgroundKeyProperty;
            object? current = _model.GetBackgroundPropertyValue(path);
            bool discrete = current is string or bool or int;
            try
            {
                _model.SetBackgroundPropertyValue(path, valueBox.Text ?? "", discrete);
                CommitAfter(() =>
                {
                    _model.ApplyAmbientSettingsToRenderer();
                    _model.ApplyFloorSettingsToViewport();
                    _model.ApplyBackgroundSettingsToViewport();
                });
                RefreshAll();
            }
            catch (FormatException)
            {
                LoadValue();
            }
        };
        Grid.SetColumn(valueBox, 0);
        Grid.SetColumn(setButton, 1);
        valueRow.Children.Add(valueBox);
        valueRow.Children.Add(setButton);
        parent.Children.Add(valueRow);

        ButtonRow(parent,
            ("Add Keyframe", () =>
            {
                _model.AddBackgroundKeyframe(_model.SelectedBackgroundKeyProperty, _model.Timeline?.CurrentFrame ?? 0);
            }),
            ("Remove Keyframe", () =>
            {
                _model.RemoveBackgroundKeyframe(_model.SelectedBackgroundKeyProperty, _model.Timeline?.CurrentFrame ?? 0);
            }));
        ButtonRow(parent, ("Keyframe All Background Settings", () =>
        {
            int frame = _model.Timeline?.CurrentFrame ?? 0;
            foreach (string path in PropertiesPanel.BackgroundKeyframePropertyPaths)
                _model.AddBackgroundKeyframe(path, frame);
        }));
    }

    // ── Object library section ────────────────────────────────────────────────

    private sealed class LibraryNode
    {
        public required ProjectSceneObjectEntry Entry { get; init; }
        public required string Label { get; init; }
        public ObservableCollection<LibraryNode> Children { get; } = new();
    }

    private readonly ObservableCollection<LibraryNode> _libraryRoots = new();

    private void WireLibrarySection()
    {
        LibraryTree.ItemsSource = _libraryRoots;

        LibrarySearchBox.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _model.LibrarySearch = LibrarySearchBox.Text ?? "";
            RebuildLibrary();
        };

        LibraryTree.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _model.SelectedLibraryEntryId = (LibraryTree.SelectedItem as LibraryNode)?.Entry.LibraryEntryId ?? "";
            UpdateLibraryInfo();
        };

        LibraryCreateButton.Click += (_, _) =>
        {
            var selected = _model.GetSelectedLibraryEntry();
            if (selected != null)
                _model.CreateInSceneFromLibraryEntry(selected);
            RebuildLibrary();
        };

        LibraryDeleteButton.Click += (_, _) =>
        {
            var selected = _model.GetSelectedLibraryEntry();
            if (selected != null)
                _model.DeleteLibraryEntry(selected);
            RebuildLibrary();
        };

        LibraryDuplicateButton.Click += (_, _) =>
        {
            var selected = _model.GetSelectedLibraryEntry();
            if (selected != null)
                _model.DuplicateLibraryEntry(selected);
            RebuildLibrary();
        };
    }

    private void RebuildLibrary()
    {
        bool wasSyncing = _syncing;
        _syncing = true;
        try
        {
            _libraryRoots.Clear();

            LibraryNode? selectedNode = null;
            foreach (var root in _model.GetLibraryTreeRoots())
            {
                if (!_model.ShouldShowLibraryEntry(root))
                    continue;
                var node = BuildLibraryNode(root, ref selectedNode);
                _libraryRoots.Add(node);
            }

            LibraryEmptyLabel.IsVisible = _libraryRoots.Count == 0;
            LibraryTree.SelectedItem = selectedNode;
        }
        finally
        {
            _syncing = wasSyncing;
        }

        UpdateLibraryInfo();
    }

    private LibraryNode BuildLibraryNode(ProjectSceneObjectEntry entry, ref LibraryNode? selectedNode)
    {
        var node = new LibraryNode
        {
            Entry = entry,
            Label = $"{PropertiesPanel.GetLibraryDisplayLabel(entry)} ({_model.CountLibraryUsage(entry.LibraryEntryId)} in scene)",
        };

        if (string.Equals(entry.LibraryEntryId, _model.SelectedLibraryEntryId, StringComparison.OrdinalIgnoreCase))
            selectedNode = node;

        foreach (var child in entry.Children)
        {
            if (!_model.ShouldShowLibraryEntry(child))
                continue;
            node.Children.Add(BuildLibraryNode(child, ref selectedNode));
        }

        return node;
    }

    private void UpdateLibraryInfo()
    {
        var selected = _model.GetSelectedLibraryEntry();
        bool hasSelection = selected != null;

        LibraryTypeLabel.IsVisible = hasSelection;
        LibraryUsageLabel.IsVisible = hasSelection;
        if (selected != null)
        {
            string type = string.IsNullOrWhiteSpace(selected.ObjectType) ? "Object" : selected.ObjectType;
            LibraryTypeLabel.Text = $"Type: {type}";
            LibraryUsageLabel.Text = $"Used in scene: {_model.CountLibraryUsage(selected.LibraryEntryId)}";
        }

        LibraryCreateButton.IsEnabled = hasSelection && _model.SpawnFromLibraryEntry != null;
        LibraryDeleteButton.IsEnabled = hasSelection;
        LibraryDuplicateButton.IsEnabled = hasSelection;
    }

    // ── Object tab ────────────────────────────────────────────────────────────

    private void BuildObjectTabSections()
    {
        // Position (stored /16 world units, displayed ×16 pixels — matches old panel)
        _inheritPosCheck = new CheckBox { Content = "Inherit Position", Foreground = MutedBrush };
        _inheritPosCheck.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            bool inherit = _inheritPosCheck.IsChecked == true;
            _model.ApplyToSelectedObjects(o => o.InheritPosition = inherit);
        };
        PositionPanel.Children.Add(_inheritPosCheck);
        _posNuds = BuildAxisNuds(PositionPanel, -1000000, 1000000, 1, _ => OnPositionEdited(), keyframePathPrefix: "position");
        ButtonRow(PositionPanel, ("Reset", () => { _model.ApplyPosition(vec3.Zero); RefreshObjectValues(); }));

        // Rotation (displayed degrees, stored radians)
        _inheritRotCheck = new CheckBox { Content = "Inherit Rotation", Foreground = MutedBrush };
        _inheritRotCheck.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            bool inherit = _inheritRotCheck.IsChecked == true;
            _model.ApplyToSelectedObjects(o => o.InheritRotation = inherit);
        };
        RotationPanel.Children.Add(_inheritRotCheck);
        _rotNuds = BuildAxisNuds(RotationPanel, -1000000, 1000000, 1, _ => OnRotationEdited(), keyframePathPrefix: "rotation");
        ButtonRow(RotationPanel, ("Reset", () => { _model.ApplyRotation(vec3.Zero); RefreshObjectValues(); }));

        // Scale
        _inheritScaleCheck = new CheckBox { Content = "Inherit Scale", Foreground = MutedBrush };
        _inheritScaleCheck.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            bool inherit = _inheritScaleCheck.IsChecked == true;
            _model.ApplyToSelectedObjects(o => o.InheritScale = inherit);
        };
        ScalePanel.Children.Add(_inheritScaleCheck);
        _linkScaleCheck = new CheckBox { Content = "Link Scale", Foreground = MutedBrush, IsChecked = true };
        ScalePanel.Children.Add(_linkScaleCheck);
        _scaleNuds = BuildAxisNuds(ScalePanel, 0.001m, 1000000, 0.05m, OnScaleEdited, keyframePathPrefix: "scale");
        ButtonRow(ScalePanel, ("Reset", () => { _model.ApplyScale(vec3.Ones); RefreshObjectValues(); }));

        // Block tiling
        MutedLabel(TilingPanel, "Repeat the block along each axis. Each axis is limited to 1–1000.");
        _tileNuds = BuildAxisNuds(TilingPanel, 1, SceneObject.MaxTilesPerAxis, 1, _ => OnTilingEdited(), "0");
        _tileTotalLabel = MutedLabel(TilingPanel, "");
        ButtonRow(TilingPanel, ("Reset", () => { _model.ApplyBlockTiling(1, 1, 1); RefreshObjectValues(); }));
    }

    private NumericUpDown[] BuildAxisNuds(Panel parent, decimal min, decimal max, decimal increment,
        Action<int> onAxisEdited, string format = "0.###", string? keyframePathPrefix = null)
    {
        string[] axes = ["X", "Y", "Z"];
        var nuds = new NumericUpDown[3];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*") };
            var label = new TextBlock { Text = axes[i], Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center };
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Increment = increment,
                FormatString = format, Value = 0,
            };
            nud.ValueChanged += (_, e) =>
            {
                if (_syncing || e.NewValue == null) return;
                onAxisEdited(axis);
            };
            if (keyframePathPrefix != null)
            {
                string propertyPath = $"{keyframePathPrefix}.{axes[axis].ToLowerInvariant()}";
                var addKeyframe = new MenuItem { Header = "Add keyframe at playhead" };
                addKeyframe.Click += (_, _) =>
                {
                    if (_model.CurrentObject != null && _model.Timeline != null)
                        _model.Timeline.AddKeyframeForProperty(_model.CurrentObject, propertyPath, _model.Timeline.CurrentFrame);
                };
                nud.ContextMenu = new ContextMenu { ItemsSource = new[] { addKeyframe } };
            }
            nuds[i] = nud;

            Grid.SetColumn(label, 0);
            Grid.SetColumn(nud, 1);
            grid.Children.Add(label);
            grid.Children.Add(nud);
            parent.Children.Add(grid);
        }

        return nuds;
    }

    private static float NudValue(NumericUpDown nud, float fallback = 0f)
        => nud.Value == null ? fallback : (float)nud.Value.Value;

    private void OnPositionEdited()
    {
        if (_model.CurrentObject == null) return;
        _model.ApplyPosition(new vec3(
            NudValue(_posNuds[0]) / 16f,
            NudValue(_posNuds[1]) / 16f,
            NudValue(_posNuds[2]) / 16f));
    }

    private void OnRotationEdited()
    {
        if (_model.CurrentObject == null) return;
        const float degToRad = MathF.PI / 180f;
        _model.ApplyRotation(new vec3(
            NudValue(_rotNuds[0]) * degToRad,
            NudValue(_rotNuds[1]) * degToRad,
            NudValue(_rotNuds[2]) * degToRad));
    }

    private void OnScaleEdited(int axis)
    {
        var obj = _model.CurrentObject;
        if (obj == null) return;

        vec3 current = PropertiesPanel.GetEditableScale(obj);
        float x = MathF.Max(NudValue(_scaleNuds[0], 1f), 0.001f);
        float y = MathF.Max(NudValue(_scaleNuds[1], 1f), 0.001f);
        float z = MathF.Max(NudValue(_scaleNuds[2], 1f), 0.001f);

        if (_linkScaleCheck.IsChecked == true)
        {
            // Linked scale offsets the other axes by the same delta (old panel behaviour).
            float delta = axis switch { 0 => x - current.x, 1 => y - current.y, _ => z - current.z };
            if (axis != 0) x = MathF.Max(current.x + delta, 0.001f);
            if (axis != 1) y = MathF.Max(current.y + delta, 0.001f);
            if (axis != 2) z = MathF.Max(current.z + delta, 0.001f);
        }

        _model.ApplyScale(new vec3(x, y, z));
        RefreshObjectValues();
    }

    private void OnTilingEdited()
    {
        if (_model.CurrentObject == null) return;
        _model.ApplyBlockTiling(
            (int)NudValue(_tileNuds[0], 1f),
            (int)NudValue(_tileNuds[1], 1f),
            (int)NudValue(_tileNuds[2], 1f));
        RefreshObjectValues();
    }

    /// <summary>Rebuilds section visibility and per-object controls after a selection change.</summary>
    private void RebuildObjectTab()
    {
        var obj = _model.CurrentObject;
        NoSelectionLabel.IsVisible = obj == null;
        ObjectPanel.IsVisible = obj != null;
        if (obj == null)
            return;

        ObjectNameLabel.Text = obj.GetDisplayName();

        // Point lights are omni-directional; spot lights aim via rotation.
        RotationExpander.IsVisible = obj is not LightSceneObject light || light.Type == LightType.Spot;
        // Cameras and lights cannot be scaled.
        ScaleExpander.IsVisible = obj is not CameraSceneObject && obj is not LightSceneObject;
        TilingExpander.IsVisible = string.Equals(obj.SpawnCategory, "Blocks", StringComparison.Ordinal);

        BuildBendSection(obj);
        BuildAppearanceSection(obj);
        BuildMaterialSection(obj);
        BuildCameraSection(obj as CameraSceneObject);
        BuildLightSection(obj as LightSceneObject);
        RefreshObjectValues();
    }

    private void BuildAppearanceSection(SceneObject obj)
    {
        AppearancePanel.Children.Clear();
        AddAppearanceCheck("Visible", obj.ObjectVisible, (target, value) => target.SetObjectVisible(value), includeDescendants: false);
        AddAppearanceCheck("Inherit visibility", obj.InheritVisibility, (target, value) => target.InheritVisibility = value);

        if (obj is not CameraSceneObject && obj is not LightSceneObject)
            AddAppearanceCheck("Cast shadows", obj.CastShadow, (target, value) => target.CastShadow = value);

        AddAppearanceCheck("Invert (render backfaces)", obj.InvertFaces, (target, value) => target.InvertFaces = value);
        AddAppearanceCheck("Blur texture (linear filtering)", obj.BlurTexture, (target, value) => target.BlurTexture = value);
        AddAppearanceCheck("Texture filtering (mip maps)", obj.TextureMipmaps, (target, value) => target.TextureMipmaps = value);
        AddAppearanceCheck("Include in ambient occlusion", obj.IncludeInAmbientOcclusion, (target, value) => target.IncludeInAmbientOcclusion = value);
        AddAppearanceCheck("Include in fog", obj.IncludeInFog, (target, value) => target.IncludeInFog = value);
        AddAppearanceCheck("Render in high quality", obj.RenderInHighQuality, (target, value) => target.RenderInHighQuality = value);
        AddAppearanceCheck("Render in low quality", obj.RenderInLowQuality, (target, value) => target.RenderInLowQuality = value);
        AddAppearanceNumber("Render depth", -1000, 1000, 0.01m, obj.RenderDepthOffset, (target, value) => target.RenderDepthOffset = value);
    }

    private void BuildMaterialSection(SceneObject obj)
    {
        MaterialPanel.Children.Clear();
        MaterialExpander.IsVisible = obj is not CameraSceneObject and not LightSceneObject;
        if (!MaterialExpander.IsVisible)
            return;

        MaterialSettings Material() => obj.MaterialSettings ?? new MaterialSettings();

        void Apply(Action<MaterialSettings> edit)
        {
            obj.SetExplicitMaterialSettings();
            edit(obj.MaterialSettings);
            obj.PropagateMaterialSettingsToChildren();
            MarkObjectDirty();
        }

        AddMaterialColor("Albedo", () => Material().AlbedoColor, value => Apply(settings => settings.AlbedoColor = value), true);
        AddMaterialColor("Blend", () => Material().BlendColor, value => Apply(settings => settings.BlendColor = value), false);
        AddMaterialColor("Mix", () => Material().MixColor, value => Apply(settings => settings.MixColor = value), false);
        AddMaterialPercentSlider("Mix amount", () => Material().MixColor.w, value => Apply(settings =>
        {
            var color = settings.MixColor;
            color.w = value;
            settings.MixColor = color;
        }));
        AddObjectNumberRow(MaterialPanel, "Metallic", 0, 1, 0.01m, () => Material().Metallic, value => Apply(settings => settings.Metallic = value));
        AddObjectNumberRow(MaterialPanel, "Roughness", 0, 1, 0.01m, () => Material().Roughness, value => Apply(settings => settings.Roughness = value));

        AddMaterialCheck("Emission", () => Material().EmissionEnabled, value => Apply(settings => settings.EmissionEnabled = value));
        AddMaterialCheck("Auto emission", () => Material().AutoEmission, value => Apply(settings => settings.AutoEmission = value));
        AddMaterialColor("Emission", () => Material().EmissionColor, value => Apply(settings => settings.EmissionColor = value), false);
        AddObjectNumberRow(MaterialPanel, "Emission energy", 0, 10, 0.1m, () => Material().EmissionEnergy, value => Apply(settings => settings.EmissionEnergy = value));
        AddMaterialCheck("Emission indirect only", () => Material().EmissionIndirectOnly, value => Apply(settings => settings.EmissionIndirectOnly = value));

        AddObjectNumberRow(MaterialPanel, "Subsurface", 0, 1, 0.01m, () => Material().Subsurface, value => Apply(settings => settings.Subsurface = value));
        AddMaterialVector3("Subsurface radius", () => Material().SubsurfaceRadius, value => Apply(settings => settings.SubsurfaceRadius = value), 0.0001m, 4, 0.01m);
        AddMaterialColor("Subsurface color", () => Material().SubsurfaceColor, value => Apply(settings => settings.SubsurfaceColor = value), false);
        AddObjectNumberRow(MaterialPanel, "Subsurface highlight", -0.95m, 0.95m, 0.01m, () => Material().SubsurfaceHighlight, value => Apply(settings => settings.SubsurfaceHighlight = value));
        AddObjectNumberRow(MaterialPanel, "Highlight strength", 0, 4, 0.01m, () => Material().SubsurfaceHighlightStrength, value => Apply(settings => settings.SubsurfaceHighlightStrength = value));

        AddMaterialCheck("Double-sided faces", () => Material().DoubleSided, value => Apply(settings => settings.DoubleSided = value));
        AddMaterialVector2("Texture offset", () => Material().TextureOffset, value => Apply(settings => settings.TextureOffset = value), -1000, 1000, 0.01m);
        AddMaterialVector2("Texture repeat", () => Material().TextureRepeat, value => Apply(settings => settings.TextureRepeat = value), 0.0001m, 256, 0.01m);
        AddMaterialCheck("Texture mirror H", () => Material().TextureMirror.x, value => Apply(settings => settings.TextureMirror = new bvec2(value, settings.TextureMirror.y)));
        AddMaterialCheck("Texture mirror V", () => Material().TextureMirror.y, value => Apply(settings => settings.TextureMirror = new bvec2(settings.TextureMirror.x, value)));

        ButtonRow(MaterialPanel, ("Reset material", () => Apply(settings =>
        {
            settings.AlbedoColor = new vec4(1f, 1f, 1f, 1f);
            settings.BlendColor = new vec4(1f, 1f, 1f, 1f);
            settings.MixColor = new vec4(0f, 0f, 0f, 0f);
            settings.Metallic = 0f;
            settings.Roughness = 0.5f;
            settings.EmissionEnabled = false;
            settings.EmissionColor = new vec4(0f, 0f, 0f, 1f);
            settings.EmissionEnergy = 1f;
            settings.EmissionIndirectOnly = false;
            settings.AutoEmission = true;
            settings.Subsurface = 0f;
            settings.SubsurfaceRadius = new vec3(0.42f, 0.24f, 0.14f);
            settings.SubsurfaceColor = new vec4(1f, 1f, 1f, 1f);
            settings.SubsurfaceHighlight = 0.35f;
            settings.SubsurfaceHighlightStrength = 0.6f;
            settings.NormalTexture = 0;
            settings.NormalEnabled = false;
            settings.DoubleSided = false;
            settings.TextureOffset = vec2.Zero;
            settings.TextureRepeat = new vec2(1f, 1f);
            settings.TextureMirror = new bvec2(false, false);
        })));
    }

    private void AddAppearanceCheck(string label, bool initialValue, Action<SceneObject, bool> apply, bool includeDescendants = true)
    {
        var check = new CheckBox { Content = label, Foreground = MutedBrush, IsChecked = initialValue };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            ApplyToSelectedObjects(target =>
            {
                apply(target, check.IsChecked == true);
                target.ApplyAppearanceSettingsToMeshes();
                if (includeDescendants)
                    foreach (var child in target.GetAllDescendants())
                    {
                        apply(child, check.IsChecked == true);
                        child.ApplyAppearanceSettingsToMeshes();
                    }
            });
            MarkObjectDirty();
        };
        AppearancePanel.Children.Add(check);
    }

    private void AddAppearanceNumber(string label, decimal minimum, decimal maximum, decimal increment, float initialValue, Action<SceneObject, float> apply)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var input = new NumericUpDown { Minimum = minimum, Maximum = maximum, Increment = increment, Value = SafeDecimal(initialValue, minimum, maximum), FormatString = "0.##" };
        input.ValueChanged += (_, e) =>
        {
            if (_syncing || e.NewValue == null) return;
            ApplyToSelectedObjects(target =>
            {
                apply(target, (float)e.NewValue.Value);
                target.ApplyAppearanceSettingsToMeshes();
                foreach (var child in target.GetAllDescendants())
                {
                    apply(child, (float)e.NewValue.Value);
                    child.ApplyAppearanceSettingsToMeshes();
                }
            });
            MarkObjectDirty();
        };
        grid.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        AppearancePanel.Children.Add(grid);
    }

    private void ApplyToSelectedObjects(Action<SceneObject> apply)
    {
        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected is { Count: > 0 })
        {
            foreach (var target in selected)
                apply(target);
            return;
        }

        if (_model.CurrentObject != null)
            apply(_model.CurrentObject);
    }

    private void AddMaterialCheck(string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Content = label, Foreground = MutedBrush, IsChecked = get() };
        check.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                set(check.IsChecked == true);
        };
        MaterialPanel.Children.Add(check);
    }

    private void AddMaterialPercentSlider(string label, Func<float> get, Action<float> set)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,48") };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(get() * 100f, 0f, 100f) };
        var valueLabel = new TextBlock { Foreground = ValueBrush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        void RefreshLabel() => valueLabel.Text = $"{get() * 100f:0}%";
        RefreshLabel();
        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            set((float)e.NewValue / 100f);
            RefreshLabel();
        };

        grid.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(valueLabel, 2);
        grid.Children.Add(valueLabel);
        MaterialPanel.Children.Add(grid);
    }

    private void AddMaterialColor(string label, Func<vec4> get, Action<vec4> set, bool includeAlpha)
    {
        ColorRow(MaterialPanel, label,
            () =>
            {
                vec4 color = get();
                return Color.FromArgb(ToByte(color.w), ToByte(color.x), ToByte(color.y), ToByte(color.z));
            },
            color =>
            {
                vec4 current = get();
                set(new vec4(color.R / 255f, color.G / 255f, color.B / 255f,
                    includeAlpha ? color.A / 255f : current.w));
            },
            includeAlpha);
    }

    private void AddMaterialVector2(string label, Func<vec2> get, Action<vec2> set, decimal minimum, decimal maximum, decimal increment)
    {
        AddMaterialVector(label, 2, index => get()[index], values => set(new vec2(values[0], values[1])), minimum, maximum, increment);
    }

    private void AddMaterialVector3(string label, Func<vec3> get, Action<vec3> set, decimal minimum, decimal maximum, decimal increment)
    {
        AddMaterialVector(label, 3, index => get()[index], values => set(new vec3(values[0], values[1], values[2])), minimum, maximum, increment);
    }

    private void AddMaterialVector(string label, int count, Func<int, float> get, Action<float[]> set, decimal minimum, decimal maximum, decimal increment)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"130,{string.Join(',', Enumerable.Repeat("*", count))}") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        for (int index = 0; index < count; index++)
        {
            int channel = index;
            var input = new NumericUpDown { Minimum = minimum, Maximum = maximum, Increment = increment, Value = SafeDecimal(get(channel), minimum, maximum), FormatString = "0.##" };
            input.ValueChanged += (_, e) =>
            {
                if (_syncing || e.NewValue == null) return;
                var values = Enumerable.Range(0, count).Select(get).ToArray();
                values[channel] = (float)e.NewValue.Value;
                set(values);
            };
            Grid.SetColumn(input, channel + 1);
            grid.Children.Add(input);
        }
        MaterialPanel.Children.Add(grid);
    }

    private void BuildCameraSection(CameraSceneObject? camera)
    {
        CameraPanel.Children.Clear();
        CameraExpander.IsVisible = camera != null;
        if (camera == null)
            return;

        var active = new CheckBox { Content = "Active render camera", Foreground = MutedBrush, IsChecked = camera.Active };
        active.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            if (active.IsChecked == true) CameraSceneObject.SetActiveExclusive(camera); else camera.Active = false;
            MarkObjectDirty();
        };
        CameraPanel.Children.Add(active);
        AddObjectNumberRow(CameraPanel, "Field of view", 1, 179, 1, () => camera.Fov, value => camera.Fov = value);
        AddObjectNumberRow(CameraPanel, "Near clip", 0.001m, 1000, 0.01m, () => camera.Near, value => camera.Near = value);
        AddObjectNumberRow(CameraPanel, "Far clip", 0.01m, 1000000, 1, () => camera.Far, value => camera.Far = Math.Max(value, camera.Near + 0.001f));
    }

    private void BuildLightSection(LightSceneObject? light)
    {
        LightPanel.Children.Clear();
        LightExpander.IsVisible = light != null;
        if (light == null)
            return;

        var type = new ComboBox { ItemsSource = new[] { "Point", "Spot" }, SelectedIndex = (int)light.Type };
        type.SelectionChanged += (_, _) =>
        {
            if (_syncing || type.SelectedIndex < 0) return;
            light.Type = (LightType)type.SelectedIndex;
            BuildLightSection(light);
            MarkObjectDirty();
        };
        LightPanel.Children.Add(new TextBlock { Text = "Type", Foreground = MutedBrush });
        LightPanel.Children.Add(type);
        AddLightColorRow(LightPanel, light);
        AddObjectNumberRow(LightPanel, "Energy", 0, 1000, 0.1m, () => light.LightEnergy, value => light.LightEnergy = value);
        AddObjectNumberRow(LightPanel, "Indirect energy", 0, 1000, 0.1m, () => light.LightIndirectEnergy, value => light.LightIndirectEnergy = value);
        AddObjectNumberRow(LightPanel, "Specular", 0, 1, 0.05m, () => light.LightSpecular, value => light.LightSpecular = value);
        AddObjectNumberRow(LightPanel, "Range", 0.01m, 100000, 0.1m, () => light.LightRange, value => light.LightRange = value);
        var shadows = new CheckBox { Content = "Cast shadows", Foreground = MutedBrush, IsChecked = light.LightShadowEnabled };
        shadows.IsCheckedChanged += (_, _) => { if (!_syncing) { light.LightShadowEnabled = shadows.IsChecked == true; MarkObjectDirty(); } };
        LightPanel.Children.Add(shadows);

        if (light.Type == LightType.Spot)
        {
            AddObjectNumberRow(LightPanel, "Cone angle", 0.1m, 179.8m, 1, () => light.LightSpotAngle, value => light.LightSpotAngle = value);
            AddObjectNumberRow(LightPanel, "Edge blend", 0, 89.9m, 0.5m, () => light.LightSpotBlend,
                value => light.LightSpotBlend = Math.Clamp(value, 0f, light.LightSpotAngle * 0.5f));
        }
    }

    private void AddLightColorRow(Panel parent, LightSceneObject light)
    {
        ColorRow(parent, "Color",
            () => Color.FromRgb(ToByte(light.LightColor.x), ToByte(light.LightColor.y), ToByte(light.LightColor.z)),
            color =>
            {
                light.LightColor = new vec4(color.R / 255f, color.G / 255f, color.B / 255f, light.LightColor.w);
                MarkObjectDirty();
            },
            hasAlpha: false);
    }

    private void AddObjectNumberRow(Panel parent, string label, decimal minimum, decimal maximum, decimal increment,
        Func<float> get, Action<float> set)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var value = new NumericUpDown
        {
            Minimum = minimum, Maximum = maximum, Increment = increment,
            Value = SafeDecimal(get(), minimum, maximum), FormatString = "0.###"
        };
        value.ValueChanged += (_, e) =>
        {
            if (_syncing || e.NewValue == null) return;
            set((float)e.NewValue.Value);
            MarkObjectDirty();
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(value);
        parent.Children.Add(grid);
    }

    private static void MarkObjectDirty()
    {
        if (ProjectManager.Instance.HasProject)
            ProjectManager.Instance.SetDirty(true);
    }

    private void BuildBendSection(SceneObject obj)
    {
        BendPanel.Children.Clear();

        if (obj is not MiBoneSceneObject bendBone ||
            bendBone.BendParameters is not BendParams bend ||
            (!bend.AxisX && !bend.AxisY && !bend.AxisZ))
        {
            BendExpander.IsVisible = false;
            return;
        }

        BendExpander.IsVisible = true;

        void AddBendAxis(string axisLabel, int component, float min, float max)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*") };
            var label = new TextBlock { Text = axisLabel, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center };
            var nud = new NumericUpDown
            {
                Minimum = SafeDecimal(min, -100000, 100000),
                Maximum = SafeDecimal(max, -100000, 100000),
                Increment = 1,
                FormatString = "0.##",
                Value = SafeDecimal(bendBone.GetEditableBendAngle()[component], -100000, 100000),
            };
            nud.ValueChanged += (_, e) =>
            {
                if (_syncing || e.NewValue == null) return;
                vec3 angle = bendBone.GetEditableBendAngle();
                angle[component] = (float)e.NewValue.Value;
                _model.ApplyBend(bendBone, angle, $"bend.{axisLabel.ToLowerInvariant()}");
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(nud, 1);
            grid.Children.Add(label);
            grid.Children.Add(nud);
            BendPanel.Children.Add(grid);
        }

        if (bend.AxisX) AddBendAxis("X", 0, bend.DirectionMin.x, bend.DirectionMax.x);
        if (bend.AxisY) AddBendAxis("Y", 1, bend.DirectionMin.y, bend.DirectionMax.y);
        if (bend.AxisZ) AddBendAxis("Z", 2, bend.DirectionMin.z, bend.DirectionMax.z);

        ButtonRow(BendPanel, ("Reset", () => _model.ApplyBend(bendBone, vec3.Zero, "bend.x", "bend.y", "bend.z")));
    }

    /// <summary>Syncs transform fields from the selected object unless the user is typing in them.</summary>
    private void RefreshObjectValues()
    {
        var obj = _model.CurrentObject;
        if (obj == null || !ObjectPanel.IsVisible)
            return;

        bool wasSyncing = _syncing;
        _syncing = true;
        try
        {
            ObjectNameLabel.Text = obj.GetDisplayName();

            _inheritPosCheck.IsChecked = obj.InheritPosition;
            _inheritRotCheck.IsChecked = obj.InheritRotation;
            _inheritScaleCheck.IsChecked = obj.InheritScale;

            vec3 pos = PropertiesPanel.GetEditablePosition(obj);
            SetNudsIfNotFocused(_posNuds, pos.x * 16f, pos.y * 16f, pos.z * 16f);

            const float radToDeg = 180f / MathF.PI;
            vec3 rot = PropertiesPanel.GetEditableRotation(obj);
            SetNudsIfNotFocused(_rotNuds, rot.x * radToDeg, rot.y * radToDeg, rot.z * radToDeg);

            vec3 scale = PropertiesPanel.GetEditableScale(obj);
            SetNudsIfNotFocused(_scaleNuds, scale.x, scale.y, scale.z);

            if (TilingExpander.IsVisible)
            {
                SetNudsIfNotFocused(_tileNuds, obj.TileX, obj.TileY, obj.TileZ);
                long total = (long)obj.GetEffectiveTileX() * obj.GetEffectiveTileY() * obj.GetEffectiveTileZ();
                _tileTotalLabel.Text = $"Total blocks: {total}";
            }
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private static void SetNudsIfNotFocused(NumericUpDown[] nuds, float x, float y, float z)
    {
        if (nuds.Any(n => n.IsKeyboardFocusWithin))
            return;

        nuds[0].Value = SafeDecimal(x, nuds[0].Minimum, nuds[0].Maximum);
        nuds[1].Value = SafeDecimal(y, nuds[1].Minimum, nuds[1].Maximum);
        nuds[2].Value = SafeDecimal(z, nuds[2].Minimum, nuds[2].Maximum);
    }
}
