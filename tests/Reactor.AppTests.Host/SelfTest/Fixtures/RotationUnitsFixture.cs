using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>.Rotation()</c> and the <c>rotation:</c> parameters of <c>.Keyframes()</c> and
/// <c>.InteractionStates()</c> are degrees on every path that reaches the element (#1615).
/// The oracle is the rendered bounding box, because composition getters return the
/// base value rather than the animated one.
/// </summary>
internal sealed class RotationUnitsFixture(Harness h, string mode) : SelfTestFixtureBase(h)
{
    // A 100x40 red rect at (100,130) on a 300x300 canvas, pivoting on its centre (150,150).
    // At 90 degrees it covers x[130,170] y[100,200]. At 90 radians (~116.6 degrees) it is ~80x107.
    // Keyframes and InteractionStates have no pivot of their own and turn about the top-left
    // corner (100,130), so at 90 degrees they cover x[60,100] y[130,230].
    private const double Left = 100, Top = 130;
    private static readonly Vector3 Pivot = new(50, 20, 0);

    public override async Task RunAsync()
    {
        using var motion = Microsoft.UI.Reactor.Navigation.TransitionEngine.OverrideAnimationsEnabled(true);
        using var reconciler = new Reconciler();
        Element plain = Border(null).Width(100).Height(40).Background("#FFFF0000");
        var curve = Curve.Linear(50);
        // The running variant keeps a long transition in flight to prove Reactor's write triggers it.
        bool running = mode == "CompositorTransitionRunning";
        var transitionDuration = TimeSpan.FromMilliseconds(running ? 600_000 : 50);
        bool compositorTransition = mode.StartsWith("CompositorTransition", StringComparison.Ordinal);
        Element Decorate(Element el) => mode switch
        {
            "Animate" => el.Animate(curve),
            "AnimateRotationOnly" => el.Animate(curve, AnimateProperty.Rotation | AnimateProperty.CenterPoint),
            "TaintedDirect" => el.LayoutAnimation(),
            _ when compositorTransition => el.Transition(Transition.Fade).RotationTransition(transitionDuration),
            _ => el,
        };
        // The transition is added after the enter transition took the visual, the order that
        // reached the compositor fallback before spec 066 layer 2 ran every transition there.
        var initial = compositorTransition ? plain.Rotation(0).Transition(Transition.Fade) : Decorate(plain.Rotation(0));
        var rotated = Decorate(plain.CenterPoint(Pivot).Rotation(90));
        bool topLeftPivot = mode is "Keyframes" or "InteractionStates";
        if (mode == "Keyframes")
        {
            Element Spin(int trigger) => plain.Keyframes("spin", trigger, kf => kf.Duration(50).At(0, rotation: 0).At(1, rotation: 90));
            (initial, rotated) = (Spin(0), Spin(1));
        }
        else if (mode == "InteractionStates")
        {
            initial = rotated = plain.InteractionStates(s => s.PointerOver(rotation: 90), curve);
        }

        var target = (FrameworkElement)reconciler.Mount(initial, () => { })!;
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas
        {
            Width = 300,
            Height = 300,
            // A painted background pins the bitmap to the canvas; otherwise it crops to the content.
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        canvas.Children.Add(target);
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(target, Left);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(target, Top);
        H.SetContent(canvas);
        await Harness.Render();

        string name = "RotationUnits_" + mode;
        try
        {
            if (compositorTransition)
            {
                var armed = Decorate(plain.Rotation(0));
                reconciler.Update(initial, armed, target, () => { });
                await Harness.Render();
                initial = armed;
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check(name + "_CompositorFallback", visual.ImplicitAnimations?.ContainsKey("RotationAngle") == true);
            }
            if (mode == "TaintedDirect" || compositorTransition)
                H.Check(name + "_VisualHandedOut", ElementPool.IsVisualTaken(target));

            void Apply() => reconciler.Update(initial, rotated, target, () => { });
            if (mode == "Ambient") AnimationScope.WithAnimation(curve, Apply);
            else if (mode == "InteractionStates") InteractionStateApplier.SimulatePointerOverForTests(target, isOver: true);
            else Apply();
            await Settle();

            var bbox = await RedBoundsAsync(canvas);
            // Under way, a 10 min transition has barely turned from the unrotated rect.
            var (x0, x1, y0, y1) = running ? (100, 200, 130, 170) : topLeftPivot ? (60, 100, 130, 230) : (130, 170, 100, 200);
            bool placed = bbox is { } b && Near(b.MinX, x0) && Near(b.MaxX, x1) && Near(b.MinY, y0) && Near(b.MaxY, y1);
            H.Check(name + (running ? "_TransitionInFlight" : "_RotatedNinetyDegrees"), placed, bbox is { } r
                ? $"bbox=x[{r.MinX:F0},{r.MaxX:F0}] y[{r.MinY:F0},{r.MaxY:F0}] expected x[{x0},{x1}] y[{y0},{y1}]"
                : "no red pixels rendered");
            if (running)
            {
                // The getter returns the base value, so this proves the write landed, in radians,
                // while the bounding box shows the transition is still playing toward it.
                float angle = ElementCompositionPreview.GetElementVisual(target).RotationAngle;
                H.Check(name + "_TargetIsNinetyDegrees", Math.Abs(angle - MathF.PI / 2) < 1e-3, $"RotationAngle={angle}");
            }
        }
        finally
        {
            // Unmounting drops the static keyframe-trigger and interaction-tracker registrations.
            int trackers = InteractionStateApplier.InteractionTrackerCountForTests;
            reconciler.UnmountChild(target);
            if (mode == "InteractionStates")
                H.Check(name + "_TrackerReleased", InteractionStateApplier.InteractionTrackerCountForTests == trackers - 1,
                    $"trackers {trackers} -> {InteractionStateApplier.InteractionTrackerCountForTests}");
            H.SetContent(null);
        }
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= 3;

    private static async Task Settle()
    {
        await Harness.Render();
        int frames = 0;
        var done = new TaskCompletionSource();
        EventHandler<object> onRendering = (_, _) => { if (++frames >= 4) done.TrySetResult(); };
        CompositionTarget.Rendering += onRendering;
        try { await Task.WhenAny(done.Task, Task.Delay(2000)); }
        finally { CompositionTarget.Rendering -= onRendering; }
        // Well past the 50 ms curves so the compositor has committed the final value.
        await Task.Delay(300);
    }

    private static async Task<(double MinX, double MaxX, double MinY, double MaxY)?> RedBoundsAsync(FrameworkElement root)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var px = (await rtb.GetPixelsAsync()).ToArray();
        int w = rtb.PixelWidth, h = rtb.PixelHeight;
        double sx = w / root.ActualWidth, sy = h / root.ActualHeight;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (px[i + 2] > 200 && px[i + 1] < 90 && px[i] < 90)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
        return maxX < 0 ? null : (minX / sx, (maxX + 1) / sx, minY / sy, (maxY + 1) / sy);
    }
}
