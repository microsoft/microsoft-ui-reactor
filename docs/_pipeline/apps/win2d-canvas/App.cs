using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using Colors = Microsoft.UI.Colors;

ReactorApp.Run<Win2DCanvasApp>("Win2D canvas", width: 920, height: 760);

// <snippet:manual-canvas>
class ManualCanvasDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            SubHeading("Manual canvas"),
            Button($"Redraw with count {count}", () => setCount(count + 1)),
            Win2DCanvas((session, _) =>
            {
                session.Clear(Colors.White);
                session.DrawText($"Count = {count}", 24, 24, Colors.DarkSlateBlue);
                session.DrawCircle(90, 96, 20 + count * 3, Colors.DeepSkyBlue, 4);
            }, redrawKey: count)
                .ClearColor(Colors.White)
                .Width(360)
                .Height(150)
        ).Padding(20);
    }
}
// </snippet:manual-canvas>

// <snippet:animated-canvas>
class AnimatedCanvasDemo : Component
{
    public override Element Render()
    {
        return Memo(ctx =>
        {
            var dots = ctx.UseDrawState(() => DotField.Create(count: 180, width: 420, height: 220));
// <snippet:use-canvas-resources>
            var sprite = ctx.UseCanvasResources<CanvasBitmap>(device =>
            {
                byte[] pixels =
                [
                    0x00, 0x78, 0xD4, 0xFF,
                    0x50, 0xC8, 0x78, 0xFF,
                    0xFF, 0xB9, 0x00, 0xFF,
                    0xD8, 0x3B, 0x01, 0xFF,
                ];

                var bitmap = CanvasBitmap.CreateFromBytes(
                    device,
                    pixels,
                    widthInPixels: 2,
                    heightInPixels: 2,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
                return ValueTask.FromResult(bitmap);
            });
// </snippet:use-canvas-resources>

            return VStack(12,
                SubHeading("Animated canvas"),
                Win2DAnimatedCanvas(
                    onUpdate: (args, state) => ((DotField)state!).Step(args.Timing.ElapsedTime),
                    onDraw: (session, _, state) =>
                    {
                        var field = (DotField)state!;
                        session.Clear(Color.FromArgb(255, 12, 16, 28));
                        field.Draw(session);

                        if (sprite.Current is { } bitmap)
                            session.DrawImage(bitmap, 16, 16, new Rect(0, 0, 2, 2), 0.85f);
                    },
                    drawState: dots.Current)
                    .ClearColor(Color.FromArgb(255, 12, 16, 28))
                    .TargetFps(60)
                    .Width(420)
                    .Height(220)
            ).Padding(20);
        });
    }
}
// </snippet:animated-canvas>

class AnimatedCanvasScreenshot : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Animated canvas"),
            Win2DAnimatedCanvas(
                onUpdate: (_, _) => { },
                onDraw: (session, _, _) =>
                {
                    session.Clear(Color.FromArgb(255, 12, 16, 28));
                    for (var i = 0; i < 48; i++)
                    {
                        var x = 24 + (i % 12) * 32;
                        var y = 28 + (i / 12) * 42;
                        session.FillCircle(x, y, 4, Color.FromArgb(220, (byte)(80 + i * 3), 190, 255));
                    }
                },
                isPaused: true)
                .ClearColor(Color.FromArgb(255, 12, 16, 28))
                .Width(420)
                .Height(220)
        ).Padding(20);
    }
}

// <snippet:virtual-canvas>
class VirtualCanvasDemo : Component
{
    public override Element Render()
    {
        var (stamp, setStamp) = UseState(0);
        var highlightedTile = new Rect(1024, 512, 360, 360);

        var canvas = Win2DVirtualCanvas((session, region) =>
        {
            const double tile = 512;
            session.Clear(Colors.WhiteSmoke);

            for (double y = Math.Floor(region.Y / tile) * tile; y < region.Y + region.Height; y += tile)
            {
                for (double x = Math.Floor(region.X / tile) * tile; x < region.X + region.Width; x += tile)
                {
                    var rect = new Rect(x, y, tile, tile);
                    var color = ((int)(x / tile + y / tile) % 2) == 0
                        ? Color.FromArgb(255, 232, 244, 255)
                        : Color.FromArgb(255, 245, 235, 255);
                    session.FillRectangle(rect, color);
                    session.DrawRectangle(rect, Colors.SlateGray, 2);
                    session.DrawText($"tile {x / tile:0},{y / tile:0}", (float)x + 24, (float)y + 28, Colors.DarkSlateGray);
                }
            }

            session.FillRectangle(new Rect(0, 0, 420, 260), Color.FromArgb(255, 0, 120, 212));
            session.DrawText("origin tile", 32, 32, Colors.White);
            session.FillRectangle(highlightedTile, Color.FromArgb(255, 255, 185, 0));
            session.DrawText($"invalidated {stamp}", 1052, 560, Colors.Black);
        }, new Size(4000, 4000)) with
        {
            InvalidateRegions = stamp == 0 ? null : [highlightedTile]
        };

        return VStack(12,
            SubHeading("Virtual canvas"),
            Button("Invalidate highlighted tile", () => setStamp(stamp + 1)),
            ScrollView(canvas)
                .Width(620)
                .Height(320)
        ).Padding(20);
    }
}
// </snippet:virtual-canvas>

class Win2DCanvasApp : Component
{
    public override Element Render()
    {
        if (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--vscode", StringComparison.OrdinalIgnoreCase)))
        {
            return VStack(8,
                Heading("Win2D canvas"),
                TextBlock("The documentation capture host switches to the individual canvas demos listed in doc-manifest.yaml.")
            ).Padding(24);
        }

        return ScrollView(
            VStack(24,
                Heading("Win2D canvas"),
                Component<ManualCanvasDemo>(),
                Component<AnimatedCanvasDemo>(),
                Component<VirtualCanvasDemo>()
            ).Padding(24)
        );
    }
}

sealed class DotField
{
    private readonly Dot[] _dots;
    private readonly double _width;
    private readonly double _height;

    private DotField(Dot[] dots, double width, double height)
    {
        _dots = dots;
        _width = width;
        _height = height;
    }

    public static DotField Create(int count, double width, double height)
    {
        var random = new Random(053);
        var dots = Enumerable.Range(0, count)
            .Select(_ => new Dot(
                random.NextDouble() * width,
                random.NextDouble() * height,
                random.NextDouble() * 90 - 45,
                random.NextDouble() * 90 - 45,
                Color.FromArgb(190, (byte)random.Next(90, 255), (byte)random.Next(120, 255), 255)))
            .ToArray();
        return new DotField(dots, width, height);
    }

    public void Step(TimeSpan elapsed)
    {
        var dt = Math.Min(0.033, elapsed.TotalSeconds);
        for (int i = 0; i < _dots.Length; i++)
        {
            var dot = _dots[i];
            dot.X += dot.Vx * dt;
            dot.Y += dot.Vy * dt;

            if (dot.X < 0 || dot.X > _width) dot.Vx = -dot.Vx;
            if (dot.Y < 0 || dot.Y > _height) dot.Vy = -dot.Vy;
            dot.X = Math.Clamp(dot.X, 0, _width);
            dot.Y = Math.Clamp(dot.Y, 0, _height);
            _dots[i] = dot;
        }
    }

    public void Draw(CanvasDrawingSession session)
    {
        foreach (var dot in _dots)
            session.FillCircle((float)dot.X, (float)dot.Y, 2.2f, dot.Color);
    }

    private record struct Dot(double X, double Y, double Vx, double Vy, Color Color);
}
