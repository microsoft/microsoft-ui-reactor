using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Model = Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix.CompositionModel;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Platform probes that spec 066 hands to layer V, run in the full and record modes. The
/// compositor ones are deterministic (controller-driven) and go into the ledger as <c>@probe</c>:
/// <list type="bullet">
/// <item><c>componentsX</c> (§5.2): a two-keyframe implicit Scale with an X mask animates X and jumps Y.</item>
/// <item><c>translationEase</c> (§5.3, F1f): an explicit Translation animation with the (0.8,0,0.2,1) ease is mid-way at 50%.</item>
/// <item><c>writeThenAttach</c> (§6.3): a compositor write made before the key is attached, before the element enters the tree.</item>
/// </list>
/// XAML transitions are invisible to RenderTargetBitmap in flight, so their side runs only with a
/// screen capture (PrintWindow) and system animations on, and is logged as PR evidence (§12.1 D),
/// never recorded: the ledger can't depend on the desktop the run had.
/// </summary>
internal static class TransformsMatrixProbes
{
    private const double Left = Model.Left, Top = Model.Top, W = Model.Width, Hh = Model.Height;

    public static async Task<LedgerCase> RunAsync(Border host, Harness h)
    {
        var record = new LedgerCase();
        var canvas = new Canvas { Width = Model.StageSize, Height = Model.StageSize };
        host.Child = canvas;
        try
        {
            record.Step("componentsX")["motion"] = await ComponentsX(host, canvas);
            record.Step("translationEase")["motion"] = await TranslationEase(host, canvas);
            record.Step("writeThenAttach")["compositor"] = await WriteThenAttach(canvas, writeFirst: true);
            record.Step("attachThenWrite")["compositor"] = await WriteThenAttach(canvas, writeFirst: false);
            await ScreenEvidence(host, canvas, h);
        }
        finally
        {
            canvas.Children.Clear();
            host.Child = null;
        }

        return record;
    }

    private static Border Rect(Canvas canvas, bool add = true)
    {
        var b = new Border { Width = W, Height = Hh, Background = new SolidColorBrush(Microsoft.UI.Colors.Red) };
        Canvas.SetLeft(b, Left);
        Canvas.SetTop(b, Top);
        canvas.Children.Clear();
        if (add) canvas.Children.Add(b);
        return b;
    }

    private static CompositionEasingFunction Fluent(Compositor c) => c.CreateCubicBezierEasingFunction(new Vector2(0.8f, 0), new Vector2(0.2f, 1));

    private static string Verdict(double mid, double end) =>
        end < 0.1 ? Model.NotApplied : mid >= 0.9 ? Model.Jumped : mid > 0.2 && mid < 0.8 && end > 0.9 ? Model.Animating : $"UNCLEAR({mid:F2},{end:F2})";

    private static async Task<string> ComponentsX(Border host, Canvas canvas)
    {
        var b = Rect(canvas);
        await TransformsMatrixRecorder.Settle();
        var v = ElementCompositionPreview.GetElementVisual(b);
        var anim = v.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertExpressionKeyFrame(0, "Vector3(this.StartingValue.X, this.FinalValue.Y, this.FinalValue.Z)");
        anim.InsertExpressionKeyFrame(1, "this.FinalValue", Fluent(v.Compositor));
        anim.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        anim.Target = "Scale";
        var keys = v.Compositor.CreateImplicitAnimationCollection();
        keys["Scale"] = anim;
        v.ImplicitAnimations = keys;
        v.Scale = new Vector3(2, 2, 1);
        await TransformsMatrixRecorder.Frames(2);
        var ctl = v.TryGetAnimationController("Scale");
        if (ctl is null) return "no-animation";
        ctl.Pause();
        (double X, double Y) Progress(Snap s) => s.RedBox() is { } r ? ((r.X1 - r.X0 - W) / W, (r.Y1 - r.Y0 - Hh) / Hh) : (double.NaN, double.NaN);
        ctl.Progress = 0.5f;
        await TransformsMatrixRecorder.Frames(3);
        var mid = Progress(await Snap.CaptureAsync(host));
        ctl.Progress = 1;
        await TransformsMatrixRecorder.Frames(3);
        var end = Progress(await Snap.CaptureAsync(host));
        v.StopAnimation("Scale");
        v.ImplicitAnimations = null;
        return $"x={Verdict(mid.X, end.X)} y={Verdict(mid.Y, end.Y)}";
    }

    private static async Task<string> TranslationEase(Border host, Canvas canvas)
    {
        var b = Rect(canvas);
        await TransformsMatrixRecorder.Settle();
        ElementCompositionPreview.SetIsTranslationEnabled(b, true);
        var v = ElementCompositionPreview.GetElementVisual(b);
        var anim = v.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1, new Vector3(100, 0, 0), Fluent(v.Compositor));
        anim.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        v.StartAnimation("Translation", anim);
        var ctl = v.TryGetAnimationController("Translation");
        if (ctl is null) return "no-animation";
        ctl.Pause();
        double X(Snap s) => s.RedBox() is { } r ? (r.X0 - Left) / 100 : double.NaN;
        ctl.Progress = 0.5f;
        await TransformsMatrixRecorder.Frames(3);
        double mid = X(await Snap.CaptureAsync(host));
        ctl.Progress = 1;
        await TransformsMatrixRecorder.Frames(3);
        double end = X(await Snap.CaptureAsync(host));
        v.StopAnimation("Translation");
        return $"x={Verdict(mid, end)}";
    }

    private static async Task<string> WriteThenAttach(Canvas canvas, bool writeFirst)
    {
        var b = Rect(canvas, add: false);
        var v = ElementCompositionPreview.GetElementVisual(b);
        var anim = v.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertExpressionKeyFrame(1, "this.FinalValue");
        anim.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        anim.Target = "Scale";
        var keys = v.Compositor.CreateImplicitAnimationCollection();
        keys["Scale"] = anim;
        if (writeFirst) { v.Scale = new Vector3(2, 2, 1); v.ImplicitAnimations = keys; }
        else { v.ImplicitAnimations = keys; v.Scale = new Vector3(2, 2, 1); }
        canvas.Children.Add(b);
        await TransformsMatrixRecorder.Frames(3);
        bool animating = v.TryGetAnimationController("Scale") is not null;
        v.StopAnimation("Scale");
        v.ImplicitAnimations = null;
        return animating ? "animates" : "jumps";
    }

    // ── Screen oracle (PrintWindow) ─────────────────────────────────────────

    private static async Task ScreenEvidence(Border host, Canvas canvas, Harness h)
    {
        void Log(string s) => Console.WriteLine("# TransformsMatrix screen: " + s);
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            Log("skipped: system animations are off, so XAML transitions jump");
            return;
        }

        Rect(canvas);
        await TransformsMatrixRecorder.Settle();
        if (Screen(host, h).RedBox() is not { } seen || !seen.Near(Model.Bounds(Pose.Identity, false), 3))
        {
            Log("skipped: PrintWindow can't see the window (no interactive desktop)");
            return;
        }

        // Positive control, Q1f: after SetIsTranslationEnabled and a render, a facade Translation
        // write is ignored on screen, while RenderTargetBitmap still reports it applied.
        var q = Rect(canvas);
        await TransformsMatrixRecorder.Settle();
        ElementCompositionPreview.SetIsTranslationEnabled(q, true);
        await TransformsMatrixRecorder.Settle();
        q.Translation = new Vector3(100, 0, 0);
        await TransformsMatrixRecorder.Settle(300);
        double rtb = (await Snap.CaptureAsync(host)).RedBox() is { } a ? (a.X0 - Left) / 100 : double.NaN;
        double screen = Screen(host, h).RedBox() is { } s ? (s.X0 - Left) / 100 : double.NaN;
        Log($"Q1f control: rtb={rtb:F2} screen={screen:F2} ({(screen < 0.1 && rtb > 0.9 ? "reproduced" : "NOT reproduced")})");

        // §5.1 parity evidence: XAML transition progress at 25/50/75% of a 3 s duration, next to the
        // (0.8,0,0.2,1) curve layer 2 adopts. §6.3: the same with the write made before attaching.
        foreach (var (label, writeFirst) in new[] { ("ScaleTransition attach-then-write", false), ("ScaleTransition write-then-attach", true) })
        {
            var b = Rect(canvas, add: !writeFirst);
            if (!writeFirst) await TransformsMatrixRecorder.Settle();
            var transition = new Vector3Transition { Duration = TimeSpan.FromSeconds(3) };
            if (writeFirst) { b.Scale = new Vector3(2, 2, 1); b.ScaleTransition = transition; canvas.Children.Add(b); }
            else { b.ScaleTransition = transition; b.Scale = new Vector3(2, 2, 1); }
            var sw = Stopwatch.StartNew();
            var samples = new List<string>();
            foreach (double t in new[] { 0.25, 0.5, 0.75 })
            {
                while (sw.Elapsed.TotalSeconds < t * 3) await Task.Delay(10);
                double at = sw.Elapsed.TotalSeconds / 3;
                double p = Screen(host, h).RedBox() is { } r ? (r.X1 - r.X0 - W) / W : double.NaN;
                samples.Add($"t={at:F2} p={p:F2} bezier={Bezier(at):F2}");
            }

            Log($"{label}: {string.Join(", ", samples)}");
            await Task.Delay(2600);
        }
    }

    /// <summary>Progress of cubic-bezier (0.8,0,0.2,1) at time fraction <paramref name="x"/>.</summary>
    internal static double Bezier(double x)
    {
        static double B(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (B(mid, 0.8, 0.2) < x) lo = mid; else hi = mid; }
        return B((lo + hi) / 2, 0, 1);
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr h, uint off);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int bmiColors;
    }

    /// <summary>PrintWindow(PW_RENDERFULLCONTENT) of the host's window region, which sees compositor-side XAML transitions.</summary>
    private static Snap Screen(FrameworkElement target, Harness h)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(h.Window);
        GetWindowRect(hwnd, out var wr);
        var org = new POINT();
        ClientToScreen(hwnd, ref org);
        int offX = org.X - wr.Left, offY = org.Y - wr.Top, w = wr.Right - wr.Left, hgt = wr.Bottom - wr.Top;
        double scale = target.XamlRoot.RasterizationScale;
        var origin = target.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -hgt, biPlanes = 1, biBitCount = 32 };
        var dib = CreateDIBSection(memDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(memDc, dib);
        PrintWindow(hwnd, memDc, 2);
        var all = new byte[w * hgt * 4];
        Marshal.Copy(bits, all, 0, all.Length);
        SelectObject(memDc, old); DeleteObject(dib); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        int pw = (int)Math.Round(target.ActualWidth * scale), ph = (int)Math.Round(target.ActualHeight * scale);
        int x0 = offX + (int)Math.Round(origin.X * scale), y0 = offY + (int)Math.Round(origin.Y * scale);
        var px = new byte[pw * ph * 4];
        for (int y = 0; y < ph; y++)
        {
            int sy = y0 + y;
            if (sy < 0 || sy >= hgt) continue;
            for (int x = 0; x < pw; x++)
            {
                int sx = x0 + x;
                if (sx >= 0 && sx < w) Array.Copy(all, (sy * w + sx) * 4, px, (y * pw + x) * 4, 4);
            }
        }

        return new Snap(px, pw, ph, scale, scale);
    }
}
