using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MineImatorSimplyRemade.gizmo;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Screen-space overlay drawn on top of the rendered viewport image. Renders the
/// gizmo's 2D draw data (rotation ring / arc / cursor line produced by
/// <see cref="Gizmo3D.RenderOverlay"/>) that cannot be expressed as depth-disabled
/// 3D geometry. Replaces the old ImGui draw-list calls. Hit-testing is disabled so
/// pointer events fall through to the viewport.
/// </summary>
internal sealed class GizmoOverlayControl : Control
{
    private readonly Func<Gizmo3D?> _gizmoProvider;

    public GizmoOverlayControl(Func<Gizmo3D?> gizmoProvider)
    {
        _gizmoProvider = gizmoProvider;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        Gizmo3D? gizmo = _gizmoProvider();
        if (gizmo == null)
            return;

        foreach (Gizmo3D.OverlayTriangle tri in gizmo.OverlayTriangles)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(ToPoint(tri.A), isFilled: true);
                ctx.LineTo(ToPoint(tri.B));
                ctx.LineTo(ToPoint(tri.C));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(ToBrush(tri.Color), null, geometry);
        }

        foreach (Gizmo3D.OverlayLine line in gizmo.OverlayLines)
        {
            var pen = new Pen(ToBrush(line.Color), line.Thickness);
            context.DrawLine(pen, ToPoint(line.A), ToPoint(line.B));
        }
    }

    private static Point ToPoint(Vector2 v) => new(v.X, v.Y);

    private static IImmutableBrush ToBrush(Vector4 c) =>
        new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)(c.W * 255), (byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255)));
}
