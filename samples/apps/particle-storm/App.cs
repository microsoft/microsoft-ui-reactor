using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace ParticleStorm;

/// <summary>Tracked cursor state: position relative to the canvas + whether the pointer is currently inside.</summary>
public sealed record CursorState(Point Position, bool Inside);

public static class Program
{
    [STAThread]
    public static void Main() => ReactorApp.Run<App>("Particle Storm", width: 1280, height: 800);
}

sealed class App : Component
{
    const int DefaultParticleCount = 5_000;

    public override Element Render()
    {
        var (count, setCount) = UseState(DefaultParticleCount);
        var (gravity, setGravity) = UseState(80.0);
        var (drag, setDrag) = UseState(0.02);
        var (paused, setPaused) = UseState(false);
        var (palette, setPalette) = UseState(Palette.Galaxy);
        var field = UseRef<ParticleField?>(null);
        var fps = UseRef(0);
        var cursor = UseRef(new CursorState(new Point(640, 400), Inside: false));
        var canvasSize = UseRef(new Size(960, 800));

        if (field.Current is null)
            field.Current = new ParticleField(DefaultParticleCount);

        UseEffect(() => () =>
        {
            field.Current?.Dispose();
            field.Current = null;
        }, Array.Empty<object>());

        var fieldRef = field!;
        var paletteColors = Palettes.For(palette);
        var fpsMeter = UseRef<FpsMeter?>(null);
        fpsMeter.Current ??= new FpsMeter();

        return Grid(
            new[] { GridSize.Px(280), GridSize.Star() },
            new[] { GridSize.Star() },
            Component<Sidebar, SidebarProps>(new SidebarProps(
                count,
                setCount,
                gravity,
                setGravity,
                drag,
                setDrag,
                paused,
                setPaused,
                palette,
                setPalette,
                fps,
                cursor,
                fieldRef)).Grid(column: 0),

            Border(
                Win2DAnimatedCanvas(
                    onUpdate: (args, state) =>
                    {
                        var particles = ((Ref<ParticleField?>)state!).Current;
                        if (particles is null)
                            return;

                        var c = cursor.Current;
                        var size = canvasSize.Current;
                        particles.Step(
                            args.Timing.ElapsedTime,
                            count,
                            (float)gravity,
                            (float)drag,
                            (float)c.Position.X,
                            (float)c.Position.Y,
                            c.Inside,
                            (float)size.Width,
                            (float)size.Height);
                    },
                    onDraw: (session, args, state) =>
                    {
                        var particles = ((Ref<ParticleField?>)state!).Current;
                        if (particles is null)
                            return;

                        // Real wall-clock fps measurement — args.Timing.ElapsedTime
                        // reports the *target* tick interval (always 16.67 ms at 60fps),
                        // not the actual frame time, so we sample Stopwatch ourselves.
                        var measured = fpsMeter.Current?.Tick();
                        if (measured is int sample)
                            fps.Current = sample;

                        particles.Render(session, paletteColors);
                    },
                    drawState: fieldRef,
                    isPaused: paused)
                .ClearColor(Colors.Black)
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch))
            .OnPointerMoved((sender, args) =>
            {
                if (sender is UIElement element)
                    cursor.Current = cursor.Current with { Position = args.GetCurrentPoint(element).Position, Inside = true };
            })
            .OnPointerEntered((_, _) => cursor.Current = cursor.Current with { Inside = true })
            .OnPointerExited((_, _) => cursor.Current = cursor.Current with { Inside = false })
            .OnSizeChanged((_, args) => canvasSize.Current = new Size(args.NewSize.Width, args.NewSize.Height))
            .Background("#01000000")
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Grid(column: 1)
        );
    }
}

/// <summary>
/// Wall-clock FPS sampler. Counts frames over a fixed window and reports the rate
/// from the game thread; safe to read from the UI thread via volatile.
/// </summary>
sealed class FpsMeter
{
    readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    int _frames;
    long _windowStartMs;
    int _lastSampleFps;

    public int? Tick()
    {
        _frames++;
        long nowMs = _sw.ElapsedMilliseconds;
        long elapsed = nowMs - _windowStartMs;
        if (elapsed < 500)
            return null;

        int fps = (int)Math.Round(_frames * 1000.0 / elapsed);
        _frames = 0;
        _windowStartMs = nowMs;
        _lastSampleFps = fps;
        return fps;
    }
}
