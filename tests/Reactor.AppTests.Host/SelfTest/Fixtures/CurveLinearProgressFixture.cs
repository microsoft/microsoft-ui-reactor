using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>Curve.Linear</c> moves at constant speed on every path that builds a compositor animation
/// from it (#1640, spec 066 §16). Each animation is paused through its controller at fixed time
/// fractions and the rendered value is read back, so the oracle doesn't depend on wall-clock timing.
/// Without an explicit easing function the compositor applies its default ease-out, which is at
/// about 0.7 at a time fraction of 0.3. <c>EaseControl</c> is the positive control: a decelerating
/// curve must read well ahead of the time fraction, proving the oracle can tell the two apart.
/// </summary>
internal sealed class CurveLinearProgressFixture(Harness h, string mode) : SelfTestFixtureBase(h)
{
    // A 100x40 red rect at (50,130) on a 300x300 white canvas. Scale 1 -> 2 grows it to the right
    // from its top-left corner, so progress = (width - 100) / 100. Opacity 1 -> 0 blends it into the
    // white, so progress = green / 255.
    private const double Left = 50, Top = 130;
    private const int DurationMs = 60_000;
    private static readonly float[] Fractions = [0.3f, 0.6f];

    public override async Task RunAsync()
    {
        using var reconciler = new Reconciler();
        Element plain = Border(null).Width(100).Height(40).Background("#FFFF0000");
        var curve = mode == "EaseControl" ? Curve.Ease(DurationMs, Easing.Decelerate) : Curve.Linear(DurationMs);
        bool opacity = mode.EndsWith("Opacity", StringComparison.Ordinal);
        bool implicitWrite = mode.StartsWith("AnimateImplicit", StringComparison.Ordinal);
        string property = opacity ? "Opacity" : "Scale";
        Element Value(Element el, bool end) => opacity ? el.Opacity(end ? 0 : 1) : el.Scale(end ? 2f : 1f);

        Element initial, final;
        switch (mode)
        {
            case "InteractionStatesScale":
                initial = final = plain.InteractionStates(s => s.PointerOver(scale: 2f), curve);
                break;
            case "InteractionStatesOpacity":
                initial = final = plain.InteractionStates(s => s.PointerOver(opacity: 0f), curve);
                break;
            case "AnimateImplicitScale" or "AnimateImplicitOpacity":
                // .Animate() also installs implicit animations on the visual. A write that bypasses
                // Reactor's modifiers triggers them instead of the write-time animation.
                initial = final = plain.Animate(curve);
                break;
            case "AmbientScale" or "AmbientOpacity":
                initial = Value(plain, false);
                final = Value(plain, true);
                break;
            default: // AnimateScale, AnimateOpacity, EaseControl
                initial = Value(plain, false).Animate(curve);
                final = Value(plain, true).Animate(curve);
                break;
        }

        var target = (FrameworkElement)reconciler.Mount(initial, () => { })!;
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas
        {
            Width = 300,
            Height = 300,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        canvas.Children.Add(target);
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(target, Left);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(target, Top);
        H.SetContent(canvas);
        await Harness.Render();

        string name = "CurveLinear_" + mode;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(target);
            void Apply() => reconciler.Update(initial, final, target, () => { });
            if (implicitWrite)
            {
                H.Check(name + "_ImplicitInstalled", visual.ImplicitAnimations?.ContainsKey(property) == true);
                if (opacity) visual.Opacity = 0;
                else visual.Scale = new Vector3(2, 2, 1);
            }
            else if (mode.StartsWith("Ambient", StringComparison.Ordinal)) AnimationScope.WithAnimation(curve, Apply);
            else if (mode.StartsWith("InteractionStates", StringComparison.Ordinal)) InteractionStateApplier.SimulatePointerOverForTests(target, isOver: true);
            else Apply();
            await Harness.Render();

            var controller = visual.TryGetAnimationController(property);
            H.Check(name + "_Animating", controller is not null, $"no animation running on {property}");
            if (controller is null) return;
            controller.Pause();

            var samples = new List<(float Time, double Progress)>();
            foreach (float fraction in Fractions)
            {
                controller.Progress = fraction;
                await Frames(3);
                samples.Add((fraction, opacity ? await OpacityProgressAsync(canvas) : await ScaleProgressAsync(canvas)));
            }

            string trace = string.Join(", ", samples.Select(s => $"t={s.Time:F2} p={s.Progress:F2}"));
            if (mode == "EaseControl")
                H.Check(name + "_ReadsAheadOfTime", samples[0].Progress - samples[0].Time > 0.2, trace);
            else
                H.Check(name + "_ProgressTracksTime", samples.All(s => Math.Abs(s.Progress - s.Time) <= 0.06), trace);
        }
        finally
        {
            reconciler.UnmountChild(target);
            H.SetContent(null);
        }
    }

    private static async Task Frames(int count)
    {
        await Harness.Render();
        int frames = 0;
        var done = new TaskCompletionSource();
        EventHandler<object> onRendering = (_, _) => { if (++frames >= count) done.TrySetResult(); };
        CompositionTarget.Rendering += onRendering;
        try { await Task.WhenAny(done.Task, Task.Delay(2000)); }
        finally { CompositionTarget.Rendering -= onRendering; }
    }

    private static async Task<(byte[] Pixels, int Width, int Height, double Sx, double Sy)> CaptureAsync(FrameworkElement root)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var px = (await rtb.GetPixelsAsync()).ToArray();
        return (px, rtb.PixelWidth, rtb.PixelHeight, rtb.PixelWidth / root.ActualWidth, rtb.PixelHeight / root.ActualHeight);
    }

    private static async Task<double> ScaleProgressAsync(FrameworkElement root)
    {
        var (px, w, h, sx, _) = await CaptureAsync(root);
        int maxX = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (px[i + 2] > 200 && px[i + 1] < 90 && px[i] < 90) maxX = Math.Max(maxX, x);
            }
        return maxX < 0 ? double.NaN : (((maxX + 1) / sx) - Left - 100) / 100;
    }

    private static async Task<double> OpacityProgressAsync(FrameworkElement root)
    {
        var (px, w, _, sx, sy) = await CaptureAsync(root);
        // Average the green channel over a 5x5 patch at the rect's centre.
        int cx = (int)((Left + 50) * sx), cy = (int)((Top + 20) * sy);
        double green = 0;
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
                green += px[((y * w) + x) * 4 + 1];
        return green / 25 / 255;
    }
}
