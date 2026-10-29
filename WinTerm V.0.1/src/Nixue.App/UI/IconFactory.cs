using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nixue.App.UI
{
    /// <summary>
    /// Builds the application's minimal geometric mark ("&gt;_") entirely in code, so the
    /// app depends on no external image assets. The mark is angular and restrained,
    /// echoing a terminal prompt rather than copying any existing logo.
    /// </summary>
    public static class IconFactory
    {
        public static DrawingImage CreateMark(Color accent, Color background)
        {
            DrawingGroup group = new DrawingGroup();

            // Rounded-free square backing panel.
            GeometryDrawing panel = new GeometryDrawing(
                new SolidColorBrush(background), null,
                new RectangleGeometry(new Rect(0, 0, 64, 64)));
            group.Children.Add(panel);

            SolidColorBrush accentBrush = new SolidColorBrush(accent);
            Pen pen = new Pen(accentBrush, 10);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;

            // Chevron ">" - bold, rounded.
            PathGeometry chevron = new PathGeometry();
            PathFigure f = new PathFigure();
            f.StartPoint = new Point(15, 16);
            f.Segments.Add(new LineSegment(new Point(32, 32), true));
            f.Segments.Add(new LineSegment(new Point(15, 48), true));
            chevron.Figures.Add(f);
            group.Children.Add(new GeometryDrawing(null, pen, chevron));

            // Underscore "_" - thicker, filled.
            GeometryDrawing underscore = new GeometryDrawing(
                accentBrush, null,
                new RectangleGeometry(new Rect(34, 42, 22, 7)));
            group.Children.Add(underscore);

            DrawingImage img = new DrawingImage(group);
            img.Freeze();
            return img;
        }

        public static BitmapSource CreateWindowIcon(Color accent, Color background)
        {
            DrawingImage mark = CreateMark(accent, background);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawImage(mark, new Rect(0, 0, 64, 64));
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
