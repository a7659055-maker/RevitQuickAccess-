using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitQuickAccess.UI
{
    /// <summary>Draws simple ribbon-button icons at runtime (a glyph on a rounded amber tile),
    /// so the plugin needs no image files.</summary>
    public static class RibbonIcon
    {
        private static readonly Color Amber = Color.FromRgb(0xC6, 0x81, 0x1E);

        /// <summary>Flexible-pipe icon: a wavy line.</summary>
        public static ImageSource MakeFlex(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = size;
                dc.DrawRoundedRectangle(new SolidColorBrush(Amber), null,
                    new Rect(0.5, 0.5, s - 1, s - 1), s * 0.18, s * 0.18);

                var pen = new Pen(System.Windows.Media.Brushes.White, System.Math.Max(1.6, s * 0.10))
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                var fig = new PathFigure { StartPoint = new Point(0.12 * s, 0.58 * s) };
                fig.Segments.Add(new BezierSegment(
                    new Point(0.36 * s, 0.05 * s),
                    new Point(0.60 * s, 1.00 * s),
                    new Point(0.88 * s, 0.44 * s), true));
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                dc.DrawGeometry(null, pen, geo);
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Tee icon: a main pipe run with a branch pointing in the given direction.</summary>
        public static ImageSource MakeTee(int size, string dirKey)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Amber), null,
                    new Rect(0.5, 0.5, size - 1, size - 1), size * 0.18, size * 0.18);

                double s = size;
                var pen = new Pen(System.Windows.Media.Brushes.White, System.Math.Max(1.6, s * 0.10))
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                // main run (slightly sloped = isometric feel)
                var a = new Point(0.12 * s, 0.64 * s);
                var b = new Point(0.88 * s, 0.50 * s);
                dc.DrawLine(pen, a, b);

                var mid = new Point(0.50 * s, 0.57 * s);
                dc.DrawLine(pen, mid, BranchTip(dirKey, s, mid));
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static Point BranchTip(string k, double s, Point c)
        {
            switch (k)
            {
                case "up": return new Point(c.X, 0.10 * s);
                case "down": return new Point(c.X, 0.94 * s);
                case "left": return new Point(0.14 * s, 0.92 * s);
                case "right": return new Point(0.88 * s, 0.94 * s);
                case "up45": return new Point(0.84 * s, 0.12 * s);
                case "down45": return new Point(0.86 * s, 0.96 * s);
                case "left45": return new Point(0.14 * s, 0.18 * s);
                case "right45": return new Point(0.90 * s, 0.86 * s);
                default: return new Point(c.X, 0.10 * s);
            }
        }

        public static ImageSource Make(string glyph, int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var rect = new Rect(0.5, 0.5, size - 1, size - 1);
                dc.DrawRoundedRectangle(new SolidColorBrush(Amber), null, rect, size * 0.18, size * 0.18);
                var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), size * 0.6, Brushes.White, 1.0)
                { TextAlignment = TextAlignment.Center };
                dc.DrawText(ft, new Point(size / 2.0, (size - ft.Height) / 2.0));
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
