using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

// The near edge starts first. Adjacent bands share their endpoints, so the
// surface bends continuously while its contents travel into the Dock.
internal sealed class SilkFold : FrameworkElement
{
    private const int Bands = 48;
    private readonly ImageBrush[] strips = new ImageBrush[Bands];
    private readonly Rect start;
    private readonly Point dock;
    private double progress;

    internal SilkFold(FrameworkElement surface, Rect start, Point dock)
    {
        this.start = start;
        this.dock = dock;
        var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        image.Freeze();
        for (int i = 0; i < Bands; i++)
        {
            strips[i] = new ImageBrush(image) { Viewbox = new Rect(0, (double)i / Bands, 1, 1d / Bands) };
            strips[i].Freeze();
        }
        IsHitTestVisible = false;
    }

    internal void Advance(double value) { progress = value; InvalidateVisual(); }

    private (double Left, double Right, double Y) Row(int row)
    {
        double y = (double)row / Bands;
        double distance = dock.Y >= start.Bottom ? 1 - y : y;
        double phase = Math.Clamp((progress - 0.24 * distance) / 0.76, 0, 1);
        double travel = phase * phase * (3 - 2 * phase);
        double pinch = Math.Sin(phase * Math.PI / 2);
        pinch *= pinch;
        double center = start.X + start.Width / 2;
        center += (dock.X - center) * pinch;
        double half = (start.Width * (1 - pinch) + 4 * pinch) / 2;
        double originalY = start.Top + y * start.Height;
        return (center - half, center + half, originalY + (dock.Y - originalY) * travel);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var upper = Row(0);
        for (int i = 0; i < Bands; i++)
        {
            var lower = Row(i + 1);
            if (lower.Y > upper.Y)
            {
                var band = new StreamGeometry();
                using (var path = band.Open())
                {
                    path.BeginFigure(new Point(upper.Left, upper.Y), true, true);
                    path.LineTo(new Point(upper.Right, upper.Y), false, false);
                    path.LineTo(new Point(lower.Right, lower.Y + 0.25), false, false);
                    path.LineTo(new Point(lower.Left, lower.Y + 0.25), false, false);
                }
                band.Freeze();
                dc.DrawGeometry(strips[i], null, band);
            }
            upper = lower;
        }
    }
}
