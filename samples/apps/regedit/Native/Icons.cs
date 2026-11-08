using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace ReactorRegedit.Native;

/// <summary>
/// 16×16 colored vector icons drawn in code (no image assets). Colors are mid-tone
/// fills with darker outlines so they read on both light and dark Mica.
/// </summary>
internal static class Icons
{
    private static readonly Color FolderBack = Color.FromArgb(0xFF, 0xD9, 0x9A, 0x1E);
    private static readonly Color FolderFront = Color.FromArgb(0xFF, 0xFF, 0xC8, 0x3D);
    private static readonly Color FolderOpenFront = Color.FromArgb(0xFF, 0xFF, 0xD8, 0x6B);
    private static readonly Color FolderEdge = Color.FromArgb(0xFF, 0xA8, 0x74, 0x0C);
    private static readonly Color HiveBody = Color.FromArgb(0xFF, 0x2F, 0x7F, 0xD8);
    private static readonly Color HiveTop = Color.FromArgb(0xFF, 0x7F, 0xB9, 0xF5);
    private static readonly Color HiveEdge = Color.FromArgb(0xFF, 0x1A, 0x55, 0x9E);
    private static readonly Color Screen = Color.FromArgb(0xFF, 0x5B, 0xA8, 0xF0);
    private static readonly Color Bezel = Color.FromArgb(0xFF, 0x4A, 0x55, 0x66);
    private static readonly Color TileFill = Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA);
    private static readonly Color TileEdge = Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);
    private static readonly Color StringRed = Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C);
    private static readonly Color BinaryBlue = Color.FromArgb(0xFF, 0x00, 0x5A, 0xB5);

    public static FrameworkElement Folder(bool open)
    {
        var c = NewCanvas();
        // Back panel with tab.
        c.Children.Add(Poly(FolderBack, FolderEdge, (1, 3), (6, 3), (7.5, 4.5), (15, 4.5), (15, 13.5), (1, 13.5)));
        if (open)
            c.Children.Add(Poly(FolderOpenFront, FolderEdge, (3.5, 7), (16, 7), (13.5, 13.5), (1, 13.5)));
        else
            c.Children.Add(Poly(FolderFront, FolderEdge, (1, 6), (15, 6), (15, 13.5), (1, 13.5)));
        return c;
    }

    /// <summary>A hive root: a blue "database" cylinder, distinct from key folders.</summary>
    public static FrameworkElement Hive()
    {
        var c = NewCanvas();
        c.Children.Add(Poly(HiveBody, HiveEdge, (2, 4), (14, 4), (14, 12.5), (2, 12.5)));
        c.Children.Add(Ellipse(HiveBody, HiveEdge, 2, 10.5, 12, 4));
        c.Children.Add(Ellipse(HiveTop, HiveEdge, 2, 2, 12, 4));
        return c;
    }

    public static FrameworkElement Computer()
    {
        var c = NewCanvas();
        c.Children.Add(Poly(Bezel, Bezel, (1, 2), (15, 2), (15, 11.5), (1, 11.5)));
        c.Children.Add(Poly(Screen, Screen, (2.2, 3.2), (13.8, 3.2), (13.8, 10.3), (2.2, 10.3)));
        c.Children.Add(Poly(Bezel, Bezel, (7, 11.5), (9, 11.5), (9, 13), (7, 13)));
        c.Children.Add(Poly(Bezel, Bezel, (4.5, 13), (11.5, 13), (11.5, 14.2), (4.5, 14.2)));
        return c;
    }

    /// <summary>String-family value ("ab" in red) or binary/number-family ("01" in blue).</summary>
    public static FrameworkElement Value(bool stringFamily)
    {
        var g = new Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        g.Children.Add(new Border
        {
            Margin = new Thickness(0.5, 1.5, 0.5, 1.5),
            Background = new SolidColorBrush(TileFill),
            BorderBrush = new SolidColorBrush(TileEdge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
        });
        g.Children.Add(new TextBlock
        {
            Text = stringFamily ? "ab" : "01",
            FontFamily = new FontFamily(stringFamily ? "Segoe UI" : "Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = stringFamily ? 9.5 : 9,
            Foreground = new SolidColorBrush(stringFamily ? StringRed : BinaryBlue),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
            IsTextScaleFactorEnabled = false,
        });
        return g;
    }

    private static Canvas NewCanvas() => new() { Width = 16, Height = 16, IsHitTestVisible = false };

    private static Path Poly(Color fill, Color stroke, params (double X, double Y)[] pts)
    {
        var fig = new PathFigure { StartPoint = new Windows.Foundation.Point(pts[0].X, pts[0].Y), IsClosed = true, IsFilled = true };
        for (int i = 1; i < pts.Length; i++)
            fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(pts[i].X, pts[i].Y) });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 0.8,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    private static Ellipse Ellipse(Color fill, Color stroke, double x, double y, double w, double h)
    {
        var e = new Ellipse
        {
            Width = w,
            Height = h,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 0.8,
        };
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }
}
