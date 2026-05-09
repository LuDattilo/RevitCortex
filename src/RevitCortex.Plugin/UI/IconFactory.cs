using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Generates ribbon icons programmatically with vector symbols.
/// Teal accent (#00838F) to distinguish from the fork's orange branding.
/// </summary>
public static class IconFactory
{
    private static readonly Color TealPrimary = Color.FromRgb(0, 131, 143);   // #00838F
    private static readonly Color TealDark = Color.FromRgb(0, 96, 100);       // #006064
    private static readonly Color IndigoAccent = Color.FromRgb(92, 107, 192); // #5C6BC0
    private static readonly Color ActiveGreen = Color.FromRgb(46, 125, 50);   // #2E7D32
    private static readonly Color InactiveGray = Color.FromRgb(97, 97, 97);   // #616161

    /// <summary>Connection icon: lightning bolt. Green when active, gray when stopped.</summary>
    public static BitmapSource CreateConnectionIcon(int size, bool isActive = false)
    {
        var bg = isActive ? ActiveGreen : InactiveGray;
        return CreateIconWithDrawing(size, bg, (dc, s) =>
        {
            // Lightning bolt
            double m = s * 0.2; // margin
            var pen = new Pen(Brushes.White, s * 0.08) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var bolt = new StreamGeometry();
            using (var ctx = bolt.Open())
            {
                ctx.BeginFigure(new Point(s * 0.55, m), false, false);
                ctx.LineTo(new Point(s * 0.35, s * 0.48), true, true);
                ctx.LineTo(new Point(s * 0.55, s * 0.48), true, true);
                ctx.LineTo(new Point(s * 0.40, s - m), true, true);
            }
            bolt.Freeze();
            dc.DrawGeometry(null, pen, bolt);

            // Small dot at bottom-right corner (status indicator)
            double dotR = s * 0.1;
            dc.DrawEllipse(Brushes.White, null,
                new Point(s * 0.75, s * 0.75), dotR, dotR);
        });
    }

    /// <summary>Panel icon: chat bubble on indigo background</summary>
    public static BitmapSource CreatePanelIcon(int size)
    {
        return CreateIconWithDrawing(size, IndigoAccent, (dc, s) =>
        {
            double m = s * 0.18;
            var pen = new Pen(Brushes.White, s * 0.07) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            // Rounded chat bubble
            var bubble = new StreamGeometry();
            using (var ctx = bubble.Open())
            {
                double l = m, t = m, r = s - m, b = s * 0.65;
                double cr = s * 0.08;
                ctx.BeginFigure(new Point(l + cr, t), true, true);
                ctx.LineTo(new Point(r - cr, t), true, true);
                ctx.ArcTo(new Point(r, t + cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(r, b - cr), true, true);
                ctx.ArcTo(new Point(r - cr, b), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                // Tail
                ctx.LineTo(new Point(s * 0.45, b), true, true);
                ctx.LineTo(new Point(s * 0.30, s - m), true, true);
                ctx.LineTo(new Point(s * 0.35, b), true, true);
                ctx.LineTo(new Point(l + cr, b), true, true);
                ctx.ArcTo(new Point(l, b - cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(l, t + cr), true, true);
                ctx.ArcTo(new Point(l + cr, t), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
            }
            bubble.Freeze();
            dc.DrawGeometry(null, pen, bubble);

            // Three dots inside bubble
            double dotY = (m + s * 0.65) / 2;
            double dotR = s * 0.04;
            for (int i = 0; i < 3; i++)
            {
                double dotX = s * 0.35 + i * s * 0.13;
                dc.DrawEllipse(Brushes.White, null, new Point(dotX, dotY), dotR, dotR);
            }
        });
    }

    /// <summary>Settings icon: gear on dark teal background</summary>
    public static BitmapSource CreateSettingsIcon(int size)
    {
        return CreateIconWithDrawing(size, TealDark, (dc, s) =>
        {
            var center = new Point(s / 2.0, s / 2.0);
            double outerR = s * 0.35;
            double innerR = s * 0.18;
            int teeth = 8;
            var pen = new Pen(Brushes.White, s * 0.06);
            pen.Freeze();

            // Gear outline
            var gear = new StreamGeometry();
            using (var ctx = gear.Open())
            {
                bool first = true;
                for (int i = 0; i < teeth; i++)
                {
                    double angle1 = (2 * Math.PI * i / teeth) - Math.PI / 2;
                    double angle2 = angle1 + Math.PI / teeth * 0.5;
                    double angle3 = angle1 + Math.PI / teeth * 0.8;
                    double angle4 = angle1 + Math.PI / teeth;

                    var p1 = PointOnCircle(center, outerR, angle1);
                    var p2 = PointOnCircle(center, outerR, angle2);
                    var p3 = PointOnCircle(center, innerR, angle3);
                    var p4 = PointOnCircle(center, innerR, angle4);

                    if (first) { ctx.BeginFigure(p1, false, true); first = false; }
                    else ctx.LineTo(p1, true, true);
                    ctx.LineTo(p2, true, true);
                    ctx.LineTo(p3, true, true);
                    ctx.LineTo(p4, true, true);
                }
            }
            gear.Freeze();
            dc.DrawGeometry(null, pen, gear);

            // Center circle
            double centerR = s * 0.08;
            dc.DrawEllipse(Brushes.White, null, center, centerR, centerR);
        });
    }

    /// <summary>
    /// Power BI icon: four bars of increasing height (analytics/dashboard motif)
    /// on a yellow-amber background to evoke the Power BI brand without using
    /// the trademarked logo.
    /// </summary>
    public static BitmapSource CreatePowerBiIcon(int size)
    {
        var amber = Color.FromRgb(245, 158, 11); // #F59E0B (warm amber)
        return CreateIconWithDrawing(size, amber, (dc, s) =>
        {
            double margin = s * 0.18;
            double baseY = s - margin;
            double barW = (s - 2 * margin) / 4 * 0.7;
            double gap = (s - 2 * margin) / 4 * 0.3;
            var fill = Brushes.White;

            for (int i = 0; i < 4; i++)
            {
                double h = (i + 1) * (s - 2 * margin) / 5;
                double x = margin + i * (barW + gap);
                double y = baseY - h;
                dc.DrawRectangle(fill, null, new Rect(x, y, barW, h));
            }
        });
    }

    /// <summary>Support icon: stylized envelope on indigo background.</summary>
    public static BitmapSource CreateSupportIcon(int size)
    {
        return CreateIconWithDrawing(size, IndigoAccent, (dc, s) =>
        {
            // Envelope body (rounded rectangle)
            double mx = s * 0.15;
            double my = s * 0.25;
            double w = s - 2 * mx;
            double h = s - 2 * my;
            var rect = new Rect(mx, my, w, h);
            var pen = new Pen(Brushes.White, s * 0.06) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, rect, s * 0.05, s * 0.05);

            // Envelope flap (triangle apex pointing down to center)
            var flap = new StreamGeometry();
            using (var ctx = flap.Open())
            {
                ctx.BeginFigure(new Point(mx, my), false, false);
                ctx.LineTo(new Point(s / 2.0, my + h * 0.55), true, true);
                ctx.LineTo(new Point(mx + w, my), true, true);
            }
            flap.Freeze();
            dc.DrawGeometry(null, pen, flap);
        });
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        return new Point(
            center.X + radius * Math.Cos(angle),
            center.Y + radius * Math.Sin(angle));
    }

    private static BitmapSource CreateIconWithDrawing(int size, Color background,
        Action<DrawingContext, double> drawAction)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new SolidColorBrush(background);
            brush.Freeze();

            double radius = size * 0.2;
            dc.DrawRoundedRectangle(brush, null,
                new Rect(0, 0, size, size), radius, radius);

            drawAction(dc, size);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
