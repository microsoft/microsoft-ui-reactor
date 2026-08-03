using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Issue #983 — an <c>AnimatedIcon</c> whose <c>State</c> is written on hover must actually play
/// the <c>NormalToPointerOver</c> segment, not jump to its end frame.
/// <para>
/// This is the only tier that can answer it. Every cheaper check passed while the page was still
/// visibly broken: the marker segments are non-zero, the control is updated in place rather than
/// remounted, the mount write lands, the fluent chain carries the pointer handlers, and the
/// pointer enter/exit counts stay balanced. None of those observe the *rendering*, and the defect
/// is that the rendering does not change over time. So this test moves a real pointer and samples
/// real pixels.
/// </para>
/// </summary>
/// <remarks>
/// The oracle is a transition, not a state: an animation produces a <em>run</em> of differing
/// consecutive frames, a hard cut produces exactly one. A single-point assertion ("the icon
/// exists", "the state is PointerOver") is satisfied whether or not anything animates, which is
/// how this bug survived several rounds of green tests.
/// </remarks>
[TestClass]
public class AnimatedIconHoverTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context)
    {
        // Before anything else. UIA reports bounding rectangles in physical pixels, but a
        // DPI-unaware process has its SetCursorPos/GetCursorPos coordinates virtualised, so the
        // pointer lands somewhere else entirely while GetCursorPos still echoes back what was
        // asked for -- the move looks successful and the hover never happens.
        SetProcessDPIAware();
        TestSession.AssemblyInit(context);
    }

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEINPUT mi; }

    /// <summary>
    /// Moves the pointer with real injected input, the same primitive the winapp verbs use.
    /// <c>SetCursorPos</c> is not sufficient: it warps the cursor without delivering mouse input,
    /// so the pointer sits over the control while WinUI never raises PointerEntered — the state
    /// stays Normal and a hover test reads as "no animation" for entirely the wrong reason.
    /// </summary>
    private static void MovePointerTo(int screenX, int screenY)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Absolute virtual-desktop coordinates are 0..65535 across the whole virtual screen,
        // which is what makes this correct on a multi-monitor layout with negative origins.
        var ax = (int)Math.Round((screenX - vx) * 65535.0 / (vw - 1));
        var ay = (int)Math.Round((screenY - vy) * 65535.0 / (vh - 1));

        var input = new INPUT
        {
            type = 0, // INPUT_MOUSE
            mi = new MOUSEINPUT
            {
                dx = ax,
                dy = ay,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            },
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        Assert.AreEqual(1u, sent, $"SendInput did not inject the pointer move (err {Marshal.GetLastWin32Error()}) — this test needs an interactive desktop");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // Screen capture through raw GDI rather than System.Drawing, which this project does not
    // reference and which is not worth adding as a dependency for one test.
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int dx, int dy, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    private static byte[] CaptureRegion(int x, int y, int w, int h)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY);

        var bi = new BITMAPINFO();
        bi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bi.bmiHeader.biWidth = w;
        bi.bmiHeader.biHeight = -h; // top-down
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        var bytes = new byte[w * h * 4];
        GetDIBits(mem, bmp, 0, (uint)h, bytes, ref bi, 0);

        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        return bytes;
    }

    /// <summary>Pixels differing by more than this between two frames count as changed.</summary>
    private const int ChannelDelta = 8;

    /// <summary>
    /// Samples a screen rectangle as fast as it can for <paramref name="durationMs"/> and returns,
    /// for each consecutive pair, how many pixels changed.
    /// </summary>
    private static List<int> SampleFrames(int x, int y, int w, int h, int durationMs)
    {
        var frames = new List<byte[]>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            frames.Add(CaptureRegion(x, y, w, h));
        }

        var changes = new List<int>();
        for (var i = 1; i < frames.Count; i++)
        {
            var a = frames[i - 1];
            var b = frames[i];
            var changed = 0;
            for (var p = 0; p + 2 < a.Length; p += 4)
            {
                var d = Math.Abs(a[p] - b[p]) + Math.Abs(a[p + 1] - b[p + 1]) + Math.Abs(a[p + 2] - b[p + 2]);
                if (d > ChannelDelta) changed++;
            }

            changes.Add(changed);
        }

        return changes;
    }

    private static int FramesWithChange(List<int> changes) => changes.FindAll(c => c > 0).Count;

    /// <summary>
    /// Distinct byte values in a frame. A screen grab that misses the window — or that catches
    /// WinUI 3 composition content GDI cannot see — comes back flat, and a flat region can never
    /// register a change. Without this the whole test degrades into "0 changed frames", which
    /// reads as a product defect when it is a blind instrument.
    /// </summary>
    private static int DistinctValues(byte[] frame)
    {
        var seen = new HashSet<byte>();
        for (var i = 0; i < frame.Length; i += 4) seen.Add(frame[i]);
        return seen.Count;
    }

    private static void AssertRegionIsVisible(int x, int y, int w, int h, string what)
    {
        var frame = CaptureRegion(x, y, w, h);
        var distinct = DistinctValues(frame);
        if (distinct <= 2)
        {
            // Inconclusive, not failed. A blank capture says nothing about the icon, and a test
            // that reports "the animation is broken" when it cannot see the animation is worse
            // than no test: it would fail identically on a correct build.
            Assert.Inconclusive(
                $"the captured {what} region ({x},{y},{w}x{h}) has {distinct} distinct value(s) — it is blank, "
                + "so the sampler is not looking at the rendered icon (window occluded or off-screen, or "
                + "WinUI 3 composition content that GDI BitBlt cannot read). No frame-difference reading "
                + "here would mean anything, so this run is making no claim about the animation.");
        }
    }

    [E2eRetry(3)]
    [TestMethod]
    public void AnimatedIcon_HoverPlaysTransition_NotJustAStateWrite()
    {
        NavigateToFixtureFresh("AnimatedIcon_HoverTransition");

        var target = FindById("HoverIconTarget").Rect;

        // Park the pointer inside the window but well clear of the target, and let it settle. A
        // previous test may have left the pointer on the target, so this is not optional.
        var awayX = target.X + target.Width + 160;
        var awayY = target.Y + target.Height + 160;
        MovePointerTo(awayX, awayY);
        Thread.Sleep(600);

        // The pointer must be genuinely clear of the target, or the "away" baseline is already a
        // hover. Asserted as "outside the rect" rather than pixel-exact: absolute injected
        // coordinates are quantised to 1/65535 of the virtual desktop and land within a pixel or
        // two of the request, which is irrelevant here and would otherwise fail spuriously.
        Assert.IsTrue(GetCursorPos(out var parked), "GetCursorPos failed");
        Assert.IsFalse(
            parked.X >= target.X && parked.X <= target.X + target.Width
            && parked.Y >= target.Y && parked.Y <= target.Y + target.Height,
            $"parked pointer ({parked.X},{parked.Y}) is still inside the target rect "
            + $"({target.X},{target.Y},{target.Width}x{target.Height}) — the baseline would already be hovering");
        WaitForText("HoverState", "state=Normal");

        AssertRegionIsVisible(target.X, target.Y, target.Width, target.Height, "icon");

        // NEGATIVE CONTROL — an idle icon must produce no changed frames at all. Without this a
        // positive reading below could be screen noise, a caret, or the cursor itself.
        var idle = SampleFrames(target.X, target.Y, target.Width, target.Height, 900);
        Assert.IsTrue(idle.Count > 20, $"sampler collected too few frames ({idle.Count}) to mean anything");
        Assert.AreEqual(0, FramesWithChange(idle),
            $"the icon changed while nothing was happening — the sampler is picking up noise, so no reading here is trustworthy (changes: {string.Join(",", idle)})");

        // Move onto the icon and sample across the transition.
        var cx = target.X + target.Width / 2;
        var cy = target.Y + target.Height / 2;
        MovePointerTo(cx, cy);
        Thread.Sleep(120);

        // Prove the pointer is physically inside the target's own rectangle. GetCursorPos echoing
        // back what was requested is not evidence of that: under a DPI mismatch both the set and
        // the read are virtualised together and agree with each other while pointing elsewhere.
        Assert.IsTrue(GetCursorPos(out var onIcon), "GetCursorPos failed");
        Assert.IsTrue(
            onIcon.X >= target.X && onIcon.X <= target.X + target.Width
            && onIcon.Y >= target.Y && onIcon.Y <= target.Y + target.Height,
            $"pointer at ({onIcon.X},{onIcon.Y}) is outside the target rect "
            + $"({target.X},{target.Y},{target.Width}x{target.Height}) — the hover never reached the icon");

        var hover = SampleFrames(target.X, target.Y, target.Width, target.Height, 900);

        // The input must have arrived, or "no animation" is a statement about the pointer and not
        // about the icon. Asserted after sampling so the move and the capture are not serialised.
        WaitForText("HoverState", "state=PointerOver");
        Assert.AreNotEqual("enters=0", FindById("HoverEnters").Text, "the pointer never entered the target");

        var changed = FramesWithChange(hover);

        // The real assertion. One changed frame is a hard cut to the segment's end; a played
        // transition spans several. Zero means the write did not even repaint.
        Assert.IsTrue(changed > 1,
            $"hovering wrote State=PointerOver but the icon did not animate into it: {changed} changed frame(s) "
            + $"across {hover.Count} samples (per-frame: {string.Join(",", hover)}). "
            + "One changed frame is a hard cut to the end of the segment; a real transition spans several.");
    }

    /// <summary>
    /// The differential that gives the test above its meaning. A press on the same icon, through
    /// the same state pipeline, is reported to animate — so if this passes while the hover case
    /// fails, the defect is specific to that transition rather than to the fixture, the sampler,
    /// or this machine's ability to render an AnimatedIcon at all.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void AnimatedIcon_PressPlaysTransition_Control()
    {
        NavigateToFixtureFresh("AnimatedIcon_HoverTransition");

        var target = FindById("HoverIconTarget").Rect;

        // Clear the pointer off the target first. A previous test may have left it there, and
        // starting from an unknown hover state would make the press transition ambiguous.
        MovePointerTo(target.X + target.Width + 160, target.Y + target.Height + 160);
        WaitForText("HoverState", "state=Normal");

        MovePointerTo(target.X + target.Width / 2, target.Y + target.Height / 2);
        WaitForText("HoverState", "state=PointerOver");
        Thread.Sleep(500);

        AssertRegionIsVisible(target.X, target.Y, target.Width, target.Height, "icon");
        var press = SampleFrames(target.X, target.Y, target.Width, target.Height, 900);
        App.Click("HoverIconTarget");
        var during = SampleFrames(target.X, target.Y, target.Width, target.Height, 900);

        Assert.IsTrue(press.Count > 20, "sampler collected too few frames to mean anything");
        if (FramesWithChange(during) <= 1)
        {
            Assert.Inconclusive(
                $"pressing did not register a visible change either ({FramesWithChange(during)} changed frame(s), "
                + $"per-frame: {string.Join(",", during)}). The press transition is reported to animate, so this run "
                + "cannot see an AnimatedIcon animate at all and makes no claim about the hover case.");
        }
    }
}
