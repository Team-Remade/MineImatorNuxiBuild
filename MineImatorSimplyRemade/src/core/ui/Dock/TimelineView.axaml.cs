using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of the old ImGui <c>core.ui.Panels.Timeline</c> panel UI.
///
/// The transport bar (play/pause/step/jump, loop, ghost mode, auto-keyframe
/// record, FPS / frame-count inputs, zoom) lives in the XAML; the ruler,
/// track labels, keyframe tracks and playhead are custom-drawn by
/// <see cref="TimelineCanvas"/>. All animation state and behaviour lives in
/// the injected <see cref="Timeline"/> model.
///
/// A UI-thread timer ticks <see cref="Timeline.UpdatePlayback"/> and repaints
/// the canvas, replacing the old per-frame immediate-mode Render() call.
///
/// Not yet ported: audio track lanes and rectangle drag-selection (model
/// support for both already exists).
/// </summary>
public partial class TimelineView : UserControl
{
    private readonly Timeline _model;
    private readonly DispatcherTimer _timer;

    /// <summary>Parameterless constructor for the XAML designer / previewer.</summary>
    public TimelineView() : this(new Timeline())
    {
    }

    public TimelineView(Timeline model)
    {
        _model = model;
        TimelineIcons.Initialize();
        InitializeComponent();

        Canvas.Model = _model;
        JumpStartIcon.Source = TimelineIcons.JumpStart;
        StepBackIcon.Source = TimelineIcons.StepBack;
        PlayPauseIcon.Source = TimelineIcons.Play;
        StopIcon.Source = TimelineIcons.Stop;
        StepForwardIcon.Source = TimelineIcons.StepForward;
        JumpEndIcon.Source = TimelineIcons.JumpEnd;
        LoopIcon.Source = TimelineIcons.Loop;
        GhostIcon.Source = TimelineIcons.Ghost;
        AutoKeyIcon.Source = TimelineIcons.AutoKey;

        JumpStartButton.Click   += (_, _) => _model.JumpToStart();
        StepBackButton.Click    += (_, _) => _model.StepBackward();
        PlayPauseButton.Click   += (_, _) => _model.TogglePlayPause();
        StopButton.Click        += (_, _) => _model.Stop();
        StepForwardButton.Click += (_, _) => _model.StepForward();
        JumpEndButton.Click     += (_, _) => _model.JumpToLastKeyframe();

        LoopToggle.IsChecked   = _model.LoopPlayback;
        GhostToggle.IsChecked  = _model.GhostModeEnabled;
        RecordToggle.IsChecked = _model.AutoKeyframe;
        LoopToggle.IsCheckedChanged   += (_, _) => _model.LoopPlayback     = LoopToggle.IsChecked == true;
        GhostToggle.IsCheckedChanged  += (_, _) => _model.GhostModeEnabled = GhostToggle.IsChecked == true;
        RecordToggle.IsCheckedChanged += (_, _) => _model.AutoKeyframe     = RecordToggle.IsChecked == true;

        FpsBox.Value    = (decimal)_model.Framerate;
        FramesBox.Value = _model.MaxFrames;
        FpsBox.ValueChanged    += (_, e) => { if (e.NewValue is { } v) _model.SetFrameRate((float)v); };
        FramesBox.ValueChanged += (_, e) => { if (e.NewValue is { } v) _model.SetMaxFrames((int)v); };
        MarkersButton.Click += (_, _) => OpenMarkerEditor();

        ZoomOutButton.Click += (_, _) => Canvas.ZoomAtPlayhead(1f / TimelineCanvas.ZoomStep);
        ZoomInButton.Click  += (_, _) => Canvas.ZoomAtPlayhead(TimelineCanvas.ZoomStep);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        AttachedToVisualTree   += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _model.UpdatePlayback();

        PlayPauseIcon.Source = _model.IsPlaying ? TimelineIcons.Pause : TimelineIcons.Play;
        FrameLabel.Text = $"Frame: {_model.CurrentFrame}  ({_model.CurrentFrame / _model.Framerate:F2}s)";
        ZoomLabel.Text  = $"{Canvas.PixelsPerFrame:0.#} px/f";

        // External changes (project load, menu actions) must reach the inputs
        // without fighting in-progress typing: only push when unfocused.
        if (!FpsBox.IsKeyboardFocusWithin && FpsBox.Value != (decimal)_model.Framerate)
            FpsBox.Value = (decimal)_model.Framerate;
        if (!FramesBox.IsKeyboardFocusWithin && FramesBox.Value != _model.MaxFrames)
            FramesBox.Value = _model.MaxFrames;
        if (LoopToggle.IsChecked != _model.LoopPlayback)     LoopToggle.IsChecked   = _model.LoopPlayback;
        if (GhostToggle.IsChecked != _model.GhostModeEnabled) GhostToggle.IsChecked = _model.GhostModeEnabled;
        if (RecordToggle.IsChecked != _model.AutoKeyframe)   RecordToggle.IsChecked = _model.AutoKeyframe;

        Canvas.InvalidateVisual();
    }

    private async void OpenMarkerEditor()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dialog = new Window
        {
            Title = "Timeline Markers",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var markerRows = new ObservableCollection<string>();
        var markerList = new ListBox { ItemsSource = markerRows, MinHeight = 180 };
        var labelBox = new TextBox { IsEnabled = false };
        var frameBox = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100000,
            Increment = 1,
            FormatString = "0",
            IsEnabled = false,
        };
        var addButton = new Button { Content = "Add at Playhead", MinWidth = 110 };
        var updateButton = new Button { Content = "Update", MinWidth = 80, IsEnabled = false };
        var removeButton = new Button { Content = "Remove", MinWidth = 80, IsEnabled = false };
        var closeButton = new Button { Content = "Close", MinWidth = 80 };
        TimelineMarker? selected = null;

        void RefreshList()
        {
            markerRows.Clear();
            foreach (var marker in _model.Markers)
                markerRows.Add($"{marker.Frame}: {marker.Label}");
        }

        void SetSelectedMarker(TimelineMarker? marker)
        {
            selected = marker;
            labelBox.IsEnabled = marker != null;
            frameBox.IsEnabled = marker != null;
            updateButton.IsEnabled = marker != null;
            removeButton.IsEnabled = marker != null;
            labelBox.Text = marker?.Label ?? "";
            frameBox.Value = marker?.Frame ?? 0;
        }

        markerList.SelectionChanged += (_, _) =>
        {
            int index = markerList.SelectedIndex;
            SetSelectedMarker(index >= 0 && index < _model.Markers.Count ? _model.Markers[index] : null);
        };
        addButton.Click += (_, _) =>
        {
            var marker = new TimelineMarker { Frame = _model.CurrentFrame };
            _model.AddMarker(marker);
            RefreshList();
            markerList.SelectedIndex = _model.Markers.ToList().IndexOf(marker);
        };
        updateButton.Click += (_, _) =>
        {
            if (selected == null)
                return;

            selected.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? "Marker" : labelBox.Text.Trim();
            selected.Frame = Math.Max(0, (int)(frameBox.Value ?? 0));
            _model.SortMarkers();
            RefreshList();
            markerList.SelectedIndex = _model.Markers.ToList().IndexOf(selected);
            Canvas.InvalidateVisual();
        };
        removeButton.Click += (_, _) =>
        {
            if (selected == null)
                return;

            _model.RemoveMarker(selected);
            RefreshList();
            SetSelectedMarker(null);
            Canvas.InvalidateVisual();
        };
        closeButton.Click += (_, _) => dialog.Close();

        RefreshList();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                markerList,
                new TextBlock { Text = "Label" },
                labelBox,
                new TextBlock { Text = "Frame" },
                frameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { addButton, updateButton, removeButton, closeButton },
                },
            },
        };

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
    }
}
