using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Custom-drawn ruler + track area of the Timeline panel: the Avalonia
/// replacement for the old ImGui draw-list rendering (RenderRulerRow /
/// RenderLeftLabels / RenderKeyframeTracks / HandleTrackMouse).
///
/// Owns only screen-space state (zoom, scroll, drag bookkeeping); every data
/// operation is delegated to the <see cref="Timeline"/> model.
///
/// Interactions:
///  • ruler click/drag — scrub the playhead,
///  • click keyframe — select (Ctrl toggles), drag to move, Delete to remove,
///  • double-click a property track — insert a keyframe at that frame,
///  • click a group header label — expand/collapse,
///  • right-click — context menu (add/copy/paste/delete/select/ghost),
///  • wheel — vertical scroll, Shift+wheel — horizontal, Ctrl+wheel — zoom,
///  • Space — play/pause, Ctrl+C/V/A — copy/paste/select-all.
/// </summary>
public sealed class TimelineCanvas : Control
{
    public const float ZoomStep = 1.2f;

    private const double LeftColumnWidth   = 210;
    private const double RulerHeight       = 26;
    private const double RowHeight         = 22;
    private const double MinPixelsPerFrame = 1.5;
    private const double MaxPixelsPerFrame = 48;
    private const double KeyframeHitRadius = 6;

    // ── Screen-space state ────────────────────────────────────────────────────

    private double _pixelsPerFrame = 8;
    private double _hScroll;
    private double _vScroll;

    public Timeline? Model { get; set; }
    public double PixelsPerFrame => _pixelsPerFrame;

    // ── Interaction state ─────────────────────────────────────────────────────

    private bool _scrubbing;
    private bool _draggingKeyframes;
    private bool _dragChangedFrames;
    private int  _dragAnchorFrame;

    private TimelineProperty? _ctxRow;
    private int _ctxFrame;
    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _ctxAddKeyframe;
    private readonly MenuItem _ctxPaste;
    private readonly MenuItem _ctxToggleGhost;

    // ── Style ─────────────────────────────────────────────────────────────────

    private static readonly Typeface Font = new("Inter, Segoe UI, sans-serif");

    private static readonly IBrush BgBrush          = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush LabelColBrush    = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush RulerBrush       = new SolidColorBrush(Color.Parse("#202028"));
    private static readonly IBrush RowEvenBrush     = new SolidColorBrush(Color.Parse("#1E1E24"));
    private static readonly IBrush RowOddBrush      = new SolidColorBrush(Color.Parse("#22222A"));
    private static readonly IBrush HeaderRowBrush   = new SolidColorBrush(Color.Parse("#2A2A33"));
    private static readonly IBrush RegionBrush      = new SolidColorBrush(Color.Parse("#2E4A2E"));
    private static readonly IBrush TextBrush        = new SolidColorBrush(Color.Parse("#DDDDDD"));
    private static readonly IBrush DimTextBrush     = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush GhostTextBrush   = new SolidColorBrush(Color.Parse("#5F6B7A"));
    private static readonly IBrush KeyframeBrush    = new SolidColorBrush(Color.Parse("#C9CCD1"));
    private static readonly IBrush KeyframeSelBrush = new SolidColorBrush(Color.Parse("#FFA726"));
    private static readonly IBrush GroupKeyBrush    = new SolidColorBrush(Color.Parse("#8A8F98"));
    private static readonly Pen GridPen      = new(new SolidColorBrush(Color.Parse("#2C2C34")));
    private static readonly Pen GridMajorPen = new(new SolidColorBrush(Color.Parse("#3A3A44")));
    private static readonly Pen SeparatorPen = new(new SolidColorBrush(Color.Parse("#0E0E0E")));
    private static readonly Pen PlayheadPen  = new(new SolidColorBrush(Color.Parse("#E53935")), 2);
    private static readonly Pen KeyframeSelPen = new(KeyframeSelBrush, 1.5);

    public TimelineCanvas()
    {
        Focusable = true;
        ClipToBounds = true;

        _ctxAddKeyframe = new MenuItem { Header = "Add keyframe here" };
        _ctxAddKeyframe.Click += (_, _) =>
        {
            if (Model != null && _ctxRow is { IsGroupHeader: false } row && row.PropertyPath != "__header__")
                Model.AddKeyframeForProperty(row.Object, row.PropertyPath, _ctxFrame);
        };

        var copy = new MenuItem { Header = "Copy selected", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += (_, _) => Model?.CopySelectedKeyframes();

        _ctxPaste = new MenuItem { Header = "Paste here", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        _ctxPaste.Click += (_, _) =>
        {
            if (Model == null) return;
            Model.PasteKeyframes(_ctxFrame);
            Model.MoveIntoPlaybackRegionIfNeeded();
        };

        var delete = new MenuItem { Header = "Delete selected", InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => Model?.DeleteSelectedKeyframes();

        var selectAll = new MenuItem { Header = "Select all", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
        selectAll.Click += (_, _) => Model?.SelectKeyframes(_ => true);
        var selectFirst = new MenuItem { Header = "Select first frame" };
        selectFirst.Click += (_, _) => Model?.SelectExtreme(first: true);
        var selectLast = new MenuItem { Header = "Select last frame" };
        selectLast.Click += (_, _) => Model?.SelectExtreme(first: false);

        var reverse = new MenuItem { Header = "Reverse selected" };
        reverse.Click += (_, _) => Model?.TransformSelectedFrames(reverse: true);

        _ctxToggleGhost = new MenuItem { Header = "Toggle ghost track" };
        _ctxToggleGhost.Click += (_, _) =>
        {
            if (Model != null && _ctxRow is { } row && row.PropertyPath != "__header__")
                Model.SetTrackRowGhostState(row, !Model.IsTrackRowGhost(row));
        };

        _contextMenu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                _ctxAddKeyframe,
                new Separator(),
                copy, _ctxPaste, delete,
                new Separator(),
                selectAll, selectFirst, selectLast, reverse,
                new Separator(),
                _ctxToggleGhost,
            },
        };
    }

    public void ZoomAtPlayhead(float factor)
    {
        if (Model == null) return;
        double viewportX = Math.Clamp(Model.CurrentFrame * _pixelsPerFrame - _hScroll, 0, Math.Max(0, TrackViewportWidth));
        ZoomAnchored(factor, viewportX);
    }

    private double TrackViewportWidth => Math.Max(0, Bounds.Width - LeftColumnWidth);

    private void ZoomAnchored(double factor, double viewportX)
    {
        double anchorFrame = (_hScroll + viewportX) / _pixelsPerFrame;
        double newScale = Math.Clamp(_pixelsPerFrame * factor, MinPixelsPerFrame, MaxPixelsPerFrame);
        if (Math.Abs(newScale - _pixelsPerFrame) < 0.001) return;
        _pixelsPerFrame = newScale;
        _hScroll = Math.Max(0, anchorFrame * newScale - viewportX);
        InvalidateVisual();
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private int XToFrame(double x) =>
        Math.Max(0, (int)Math.Round((x - LeftColumnWidth + _hScroll) / _pixelsPerFrame));

    private double FrameToX(double frame) => LeftColumnWidth + frame * _pixelsPerFrame - _hScroll;

    private List<TimelineProperty> GetVisibleRows()
    {
        if (Model == null) return new List<TimelineProperty>();
        return Model.DisplayRows.Where(Model.IsTrackRowVisible).ToList();
    }

    private int YToRowIndex(double y) => (int)Math.Floor((y - RulerHeight + _vScroll) / RowHeight);

    private double RowIndexToY(int index) => RulerHeight + index * RowHeight - _vScroll;

    /// <summary>Keyframes drawn on one row: the row's own for property rows, the
    /// union of the group's child tracks for collapsed/expanded group headers.</summary>
    private IEnumerable<(string path, TimelineKeyframe kf)> RowKeyframes(TimelineProperty row)
    {
        if (Model == null || row.PropertyPath == "__header__") yield break;
        if (row.IsGroupHeader && row.GroupPaths is { Length: > 0 })
        {
            foreach (var path in row.GroupPaths)
                foreach (var kf in Model.GetKeyframes(row.Object, path))
                    yield return (path, kf);
        }
        else if (!row.IsGroupHeader)
        {
            foreach (var kf in Model.GetKeyframes(row.Object, row.PropertyPath))
                yield return (row.PropertyPath, kf);
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, bounds);
        if (Model == null) return;

        var rows = GetVisibleRows();
        ClampScroll(rows.Count);

        // Auto-follow the playhead during playback.
        if (Model.IsPlaying && !_scrubbing)
        {
            double px = Model.CurrentFrame * _pixelsPerFrame;
            if (px < _hScroll || px > _hScroll + TrackViewportWidth - 30)
                _hScroll = Math.Max(0, px - TrackViewportWidth * 0.15);
        }

        RenderTracks(ctx, bounds, rows);
        RenderLeftLabels(ctx, bounds, rows);
        RenderRuler(ctx, bounds);
        RenderPlayhead(ctx, bounds);

        ctx.DrawLine(SeparatorPen, new Point(LeftColumnWidth, 0), new Point(LeftColumnWidth, bounds.Height));
        ctx.DrawLine(SeparatorPen, new Point(0, RulerHeight), new Point(bounds.Width, RulerHeight));

        if (rows.Count == 0)
        {
            var hint = Text("Select an object with keyframes to see its tracks.", DimTextBrush);
            ctx.DrawText(hint, new Point(LeftColumnWidth + 12, RulerHeight + 12));
        }
    }

    private void ClampScroll(int rowCount)
    {
        double contentH = rowCount * RowHeight;
        double viewH = Math.Max(0, Bounds.Height - RulerHeight);
        _vScroll = Math.Clamp(_vScroll, 0, Math.Max(0, contentH - viewH));

        double contentW = (Model?.MaxFrames ?? 300) * _pixelsPerFrame * 1.5;
        _hScroll = Math.Clamp(_hScroll, 0, Math.Max(0, contentW - TrackViewportWidth));
    }

    private static FormattedText Text(string text, IBrush brush, double size = 12) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);

    private void RenderLeftLabels(DrawingContext ctx, Rect bounds, List<TimelineProperty> rows)
    {
        ctx.FillRectangle(LabelColBrush, new Rect(0, 0, LeftColumnWidth, bounds.Height));

        using var clip = ctx.PushClip(new Rect(0, RulerHeight, LeftColumnWidth, bounds.Height - RulerHeight));
        for (int i = 0; i < rows.Count; i++)
        {
            double y = RowIndexToY(i);
            if (y + RowHeight < RulerHeight || y > bounds.Height) continue;

            var row = rows[i];
            bool isObjHeader = row.PropertyPath == "__header__";
            if (isObjHeader)
                ctx.FillRectangle(HeaderRowBrush, new Rect(0, y, LeftColumnWidth, RowHeight));

            double x = 8 + row.Indent * 14;
            string label = row.Label ?? string.Empty;
            if (row.IsGroupHeader)
                label = (Model!.IsGroupExpanded(row) ? "\u25BC " : "\u25B6 ") + label;

            var brush = isObjHeader ? TextBrush
                : Model!.ShouldShowGhostIndicator(row) ? GhostTextBrush
                : row.IsGroupHeader ? TextBrush : DimTextBrush;
            var ft = Text(label, brush);
            ft.MaxTextWidth = Math.Max(10, LeftColumnWidth - x - 6);
            ft.MaxLineCount = 1;
            ctx.DrawText(ft, new Point(x, y + (RowHeight - ft.Height) / 2));

            if (Model!.ShouldShowGhostIndicator(row))
            {
                var g = Text("G", GhostTextBrush, 10);
                ctx.DrawText(g, new Point(LeftColumnWidth - 14, y + (RowHeight - g.Height) / 2));
            }
        }
    }

    private void RenderTracks(DrawingContext ctx, Rect bounds, List<TimelineProperty> rows)
    {
        using var clip = ctx.PushClip(new Rect(LeftColumnWidth, RulerHeight,
            bounds.Width - LeftColumnWidth, bounds.Height - RulerHeight));

        // Row bands.
        for (int i = 0; i < rows.Count; i++)
        {
            double y = RowIndexToY(i);
            if (y + RowHeight < RulerHeight || y > bounds.Height) continue;
            var brush = rows[i].PropertyPath == "__header__" ? HeaderRowBrush
                : i % 2 == 0 ? RowEvenBrush : RowOddBrush;
            ctx.FillRectangle(brush, new Rect(LeftColumnWidth, y, bounds.Width - LeftColumnWidth, RowHeight));
        }

        // Playback region tint.
        if (Model!.PlaybackRegionStart is { } rs && Model.PlaybackRegionEnd is { } re && re > rs)
        {
            double x0 = FrameToX(rs), x1 = FrameToX(re);
            ctx.FillRectangle(RegionBrush, new Rect(x0, RulerHeight, x1 - x0, bounds.Height - RulerHeight), 0);
        }

        // Vertical grid lines.
        int step = GridStep();
        int firstFrame = Math.Max(0, (int)(_hScroll / _pixelsPerFrame));
        int lastFrame = (int)((_hScroll + TrackViewportWidth) / _pixelsPerFrame) + 1;
        for (int f = firstFrame / step * step; f <= lastFrame; f += step)
        {
            double x = FrameToX(f);
            ctx.DrawLine(f % (step * 5) == 0 ? GridMajorPen : GridPen,
                new Point(x, RulerHeight), new Point(x, bounds.Height));
        }

        // Keyframes.
        for (int i = 0; i < rows.Count; i++)
        {
            double y = RowIndexToY(i);
            if (y + RowHeight < RulerHeight || y > bounds.Height) continue;
            var row = rows[i];
            double cy = y + RowHeight / 2;
            bool isGroup = row.IsGroupHeader;

            foreach (var (_, kf) in RowKeyframes(row))
            {
                double cx = FrameToX(kf.Frame);
                if (cx < LeftColumnWidth - 8 || cx > bounds.Width + 8) continue;
                bool selected = Model.IsKeyframeSelected(kf);
                DrawDiamond(ctx, cx, cy, selected ? 5.5 : 4.5,
                    selected ? KeyframeSelBrush : isGroup ? GroupKeyBrush : KeyframeBrush,
                    selected ? KeyframeSelPen : null);
            }
        }
    }

    private int GridStep()
    {
        // Pick a frame step so grid lines stay >= ~14 px apart.
        int step = 1;
        while (step * _pixelsPerFrame < 14) step *= step % 3 == 2 ? 2 : 5; // 1,5,10,50,100...
        return step;
    }

    private static void DrawDiamond(DrawingContext ctx, double cx, double cy, double s, IBrush fill, IPen? pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(cx, cy - s), true);
            g.LineTo(new Point(cx + s, cy));
            g.LineTo(new Point(cx, cy + s));
            g.LineTo(new Point(cx - s, cy));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(fill, pen, geometry);
    }

    private void RenderRuler(DrawingContext ctx, Rect bounds)
    {
        ctx.FillRectangle(RulerBrush, new Rect(LeftColumnWidth, 0, bounds.Width - LeftColumnWidth, RulerHeight));
        using var clip = ctx.PushClip(new Rect(LeftColumnWidth, 0, bounds.Width - LeftColumnWidth, RulerHeight));

        int step = GridStep();
        int labelStep = step;
        while (labelStep * _pixelsPerFrame < 50) labelStep *= 2;

        int firstFrame = Math.Max(0, (int)(_hScroll / _pixelsPerFrame));
        int lastFrame = (int)((_hScroll + TrackViewportWidth) / _pixelsPerFrame) + 1;
        for (int f = firstFrame / step * step; f <= lastFrame; f += step)
        {
            double x = FrameToX(f);
            bool labeled = f % labelStep == 0;
            ctx.DrawLine(GridMajorPen,
                new Point(x, labeled ? RulerHeight - 12 : RulerHeight - 6), new Point(x, RulerHeight));
            if (labeled)
                ctx.DrawText(Text(f.ToString(CultureInfo.InvariantCulture), DimTextBrush, 10),
                    new Point(x + 3, 1));
        }

        // Markers: small coloured flags on the ruler.
        foreach (var marker in Model!.Markers)
        {
            double x = FrameToX(marker.Frame);
            if (x < LeftColumnWidth - 8 || x > bounds.Width + 8) continue;
            var c = marker.Color;
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)(c.W * 255), (byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255)));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x, RulerHeight), true);
                g.LineTo(new Point(x - 5, RulerHeight - 8));
                g.LineTo(new Point(x + 5, RulerHeight - 8));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geometry);
        }
    }

    private void RenderPlayhead(DrawingContext ctx, Rect bounds)
    {
        double x = FrameToX(Model!.CurrentFrame);
        if (x < LeftColumnWidth - 1 || x > bounds.Width + 1) return;

        using var clip = ctx.PushClip(new Rect(LeftColumnWidth, 0, bounds.Width - LeftColumnWidth, bounds.Height));
        ctx.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, bounds.Height));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x - 6, 0), true);
            g.LineTo(new Point(x + 6, 0));
            g.LineTo(new Point(x, 9));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(PlayheadPen.Brush, null, geometry);
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    private (TimelineProperty row, List<(string path, TimelineKeyframe kf)> hits)? HitTestKeyframe(Point p)
    {
        if (Model == null || p.X < LeftColumnWidth || p.Y < RulerHeight) return null;
        var rows = GetVisibleRows();
        int index = YToRowIndex(p.Y);
        if (index < 0 || index >= rows.Count) return null;

        var row = rows[index];
        var hits = RowKeyframes(row)
            .Where(x => Math.Abs(FrameToX(x.kf.Frame) - p.X) <= KeyframeHitRadius)
            .ToList();
        return hits.Count > 0 ? (row, hits) : null;
    }

    private TimelineProperty? RowAt(Point p)
    {
        if (Model == null || p.Y < RulerHeight) return null;
        var rows = GetVisibleRows();
        int index = YToRowIndex(p.Y);
        return index >= 0 && index < rows.Count ? rows[index] : null;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Model == null) return;
        Focus();

        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            OpenContextMenu(p);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        // Ruler: scrub the playhead.
        if (p.Y < RulerHeight && p.X >= LeftColumnWidth)
        {
            _scrubbing = true;
            Model.SetCurrentFrame(XToFrame(p.X));
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Left column: expand/collapse group headers.
        if (p.X < LeftColumnWidth)
        {
            if (RowAt(p) is { IsGroupHeader: true } header)
            {
                Model.ToggleGroupExpanded(header);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        bool additive = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;

        if (HitTestKeyframe(p) is { } hit)
        {
            if (e.ClickCount >= 2 && hit.row is { IsGroupHeader: false } dblRow)
            {
                // Double-click a keyframe: jump the playhead to it.
                Model.SetCurrentFrame(hit.hits[0].kf.Frame);
                e.Handled = true;
                return;
            }

            foreach (var (path, kf) in hit.hits)
                Model.SelectKeyframe(hit.row.Object, path, kf, additive);

            if (!additive || Model.SelectedKeyframes.Count > 0)
            {
                _draggingKeyframes = true;
                _dragChangedFrames = false;
                _dragAnchorFrame = XToFrame(p.X);
                Model.BeginKeyframeDrag();
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return;
        }

        // Empty track space.
        if (e.ClickCount >= 2 && RowAt(p) is { IsGroupHeader: false } row && row.PropertyPath != "__header__")
        {
            Model.AddKeyframeForProperty(row.Object, row.PropertyPath, XToFrame(p.X));
        }
        else if (!additive)
        {
            Model.ClearKeyframeSelection();
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Model == null) return;
        var p = e.GetPosition(this);

        if (_scrubbing)
        {
            Model.SetCurrentFrame(XToFrame(p.X));
            e.Handled = true;
        }
        else if (_draggingKeyframes)
        {
            int delta = XToFrame(p.X) - _dragAnchorFrame;
            if (delta != 0) _dragChangedFrames = true;
            Model.DragSelectedKeyframes(delta);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_scrubbing)
        {
            _scrubbing = false;
            e.Pointer.Capture(null);
        }
        if (_draggingKeyframes)
        {
            _draggingKeyframes = false;
            e.Pointer.Capture(null);
            if (_dragChangedFrames) Model?.EndKeyframeDrag();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double viewportX = Math.Clamp(p.X - LeftColumnWidth, 0, Math.Max(0, TrackViewportWidth));
            ZoomAnchored(Math.Pow(ZoomStep, e.Delta.Y), viewportX);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _hScroll = Math.Max(0, _hScroll - e.Delta.Y * 48);
            InvalidateVisual();
        }
        else
        {
            _vScroll = Math.Max(0, _vScroll - e.Delta.Y * 32);
            _hScroll = Math.Max(0, _hScroll - e.Delta.X * 48);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Model == null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Space:
                Model.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Delete or Key.Back:
                Model.DeleteSelectedKeyframes();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                Model.CopySelectedKeyframes();
                e.Handled = true;
                break;
            case Key.V when ctrl:
                Model.PasteKeyframes(Model.CurrentFrame);
                Model.MoveIntoPlaybackRegionIfNeeded();
                e.Handled = true;
                break;
            case Key.A when ctrl:
                Model.SelectKeyframes(_ => true);
                e.Handled = true;
                break;
            case Key.Left:
                Model.StepBackward();
                e.Handled = true;
                break;
            case Key.Right:
                Model.StepForward();
                e.Handled = true;
                break;
        }
    }

    private void OpenContextMenu(Point p)
    {
        if (Model == null) return;
        _ctxRow = RowAt(p);
        _ctxFrame = XToFrame(Math.Max(p.X, LeftColumnWidth));

        _ctxAddKeyframe.IsEnabled = _ctxRow is { IsGroupHeader: false } r && r.PropertyPath != "__header__";
        _ctxAddKeyframe.Header = $"Add keyframe at {_ctxFrame}";
        _ctxPaste.IsEnabled = Model.HasClipboardContent;
        _ctxPaste.Header = $"Paste at {_ctxFrame}";
        _ctxToggleGhost.IsEnabled = _ctxRow != null && _ctxRow.PropertyPath != "__header__";

        _contextMenu.Open(this);
    }
}
