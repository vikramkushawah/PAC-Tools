// PAC Tools - ribbon button icons, drawn in code.
//
// Drawing them in code means no .png resources to keep in step, and they stay sharp at 16 and 32 px.
// There is deliberately no Revit API in this file, so there is no clash between System.Windows.Point /
// Color and Autodesk.Revit.DB.Point / Color.
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>Ribbon icons. Each method returns a frozen bitmap, safe to hand to Revit.</summary>
    public static class Icons
    {
        /// <summary>The PAC Tools accent colour.</summary>
        public static Color Accent => (Color)ColorConverter.ConvertFromString(Brand.AccentHex);

        /// <summary>The darker shade of the accent colour.</summary>
        public static Color AccentDark => (Color)ColorConverter.ConvertFromString(Brand.AccentDarkHex);

        /// <summary>Three bars on a baseline - the Element Count icon.</summary>
        public static BitmapSource Bars(int size) => Render(size, (dc, s) =>
        {
            var fill = new SolidColorBrush(Accent);
            var dark = new SolidColorBrush(AccentDark);

            // Baseline.
            dc.DrawRectangle(dark, null, new Rect(s * 0.10, s * 0.82, s * 0.80, Math.Max(1.0, s / 16.0)));

            // Bars, shortest on the left.
            double w = s * 0.18;
            dc.DrawRectangle(fill, null, new Rect(s * 0.16, s * 0.58, w, s * 0.24));
            dc.DrawRectangle(fill, null, new Rect(s * 0.41, s * 0.34, w, s * 0.48));
            dc.DrawRectangle(fill, null, new Rect(s * 0.66, s * 0.16, w, s * 0.66));
        });

        /// <summary>A filled circle with an "i" - the About icon.</summary>
        public static BitmapSource Info(int size) => Render(size, (dc, s) =>
        {
            var fill = new SolidColorBrush(Accent);
            var white = Brushes.White;

            dc.DrawEllipse(fill, null, new Point(s * 0.5, s * 0.5), s * 0.46, s * 0.46);
            double w = Math.Max(1.5, s * 0.11);
            dc.DrawEllipse(white, null, new Point(s * 0.5, s * 0.29), w * 0.62, w * 0.62);
            dc.DrawRectangle(white, null, new Rect(s * 0.5 - w / 2.0, s * 0.42, w, s * 0.33));
        });

        /// <summary>An open book - the Guide icon.</summary>
        public static BitmapSource Book(int size) => Render(size, (dc, s) =>
        {
            var fill = new SolidColorBrush(Accent);
            var line = new Pen(Brushes.White, Math.Max(1.0, s / 16.0));

            // Two pages, either side of a gap.
            dc.DrawRectangle(fill, null, new Rect(s * 0.06, s * 0.18, s * 0.41, s * 0.66));
            dc.DrawRectangle(fill, null, new Rect(s * 0.53, s * 0.18, s * 0.41, s * 0.66));

            // Lines of text.
            foreach (double y in new[] { 0.34, 0.50, 0.66 })
            {
                dc.DrawLine(line, new Point(s * 0.14, s * y), new Point(s * 0.39, s * y));
                dc.DrawLine(line, new Point(s * 0.61, s * y), new Point(s * 0.86, s * y));
            }
        });

        private static BitmapSource Render(int size, Action<DrawingContext, double> draw)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
                draw(dc, size);

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
