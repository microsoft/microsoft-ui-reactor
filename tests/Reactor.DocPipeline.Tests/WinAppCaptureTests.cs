using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.Windows.SDK.BuildTools.WinApp.UIAutomation;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Covers <see cref="WinAppCapture"/>, the in-process winapp capture path for doc
/// screenshots. Everything here is headless: Windows Graphics Capture against a live window
/// is exercised by a real doc-pipeline compile run, not by unit tests.
/// </summary>
public class WinAppCaptureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    // ── client-area crop ───────────────────────────────────────────────────

    /// <summary>
    /// Geometry measured from a real doc app window on a 150% monitor: DWM frame bounds
    /// (2609,0)-(3551,831), client origin (2611,45), client 938x784, and the 942x831 frame
    /// Windows Graphics Capture returned. The crop drops the 2px visible border and the 45px
    /// title bar.
    /// </summary>
    [Fact]
    public void Client_crop_drops_the_title_bar_and_border()
    {
        var crop = WinAppCapture.ComputeClientCrop(
            Rectangle.FromLTRB(2609, 0, 3551, 831), new Point(2611, 45), new Size(938, 784), new Size(942, 831));

        Assert.Equal(new Rectangle(2, 45, 938, 784), crop);
    }

    [Fact]
    public void A_capture_that_does_not_match_the_frame_is_rejected()
    {
        // The window was resized (or changed DPI) between the capture and the measurement.
        Assert.Throws<InvalidOperationException>(() => WinAppCapture.ComputeClientCrop(
            Rectangle.FromLTRB(2609, 0, 3551, 831), new Point(2611, 45), new Size(938, 784), new Size(882, 591)));
    }

    [Fact]
    public void A_client_area_outside_the_capture_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => WinAppCapture.ComputeClientCrop(
            new Rectangle(0, 0, 100, 100), new Point(10, 10), new Size(100, 100), new Size(100, 100)));
    }

    [Fact]
    public void Crop_returns_exactly_the_client_pixels()
    {
        using var frame = new Bitmap(10, 8, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame)) g.Clear(Color.Gray);
        frame.SetPixel(2, 3, Color.Red);
        frame.SetPixel(7, 6, Color.Blue);

        var png = WinAppCapture.CropBgra(ToBgra(frame), 10, 8, new Rectangle(2, 3, 6, 4));

        using var cropped = Decode(png);
        Assert.Equal(new Size(6, 4), cropped.Size);
        Assert.Equal(Color.Red.ToArgb(), cropped.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), cropped.GetPixel(5, 3).ToArgb());
    }

    [Fact]
    public void Crop_rejects_a_buffer_that_is_not_the_reported_size()
    {
        using var frame = new Bitmap(10, 8, PixelFormat.Format32bppArgb);

        var ex = Assert.Throws<InvalidOperationException>(
            () => WinAppCapture.CropBgra(ToBgra(frame), 12, 8, new Rectangle(0, 0, 4, 4)));
        Assert.Contains("12x8", ex.Message);
    }

    // ── Windows Graphics Capture only ──────────────────────────────────────

    /// <summary>
    /// The library's one-shot screenshot falls back to PrintWindow and can foreground the
    /// window; capture must not. Without Graphics Capture the screenshot fails instead, and no
    /// other capture method is touched.
    /// </summary>
    [Fact]
    public async Task Without_Graphics_Capture_the_screenshot_fails_and_nothing_else_is_tried()
    {
        var capture = new FakeWindowCapture { Supported = false };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WinAppCapture.CaptureClientAreaAsync(capture, new IntPtr(1), TestContext.Current.CancellationToken));

        Assert.Equal(WinAppCapture.GraphicsCaptureUnavailable, ex.Message);
        Assert.Equal(0, capture.GrabbersStarted);
        Assert.Equal(0, capture.FallbackCalls);
    }

    [Fact]
    public async Task A_grabber_that_cannot_start_fails_the_screenshot_not_the_compile()
    {
        var capture = new FakeWindowCapture { StartFailure = new PlatformNotSupportedException("no WGC") };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WinAppCapture.CaptureClientAreaAsync(capture, new IntPtr(1), TestContext.Current.CancellationToken));

        Assert.Contains("no WGC", ex.Message);
        Assert.IsType<PlatformNotSupportedException>(ex.InnerException);
        Assert.Equal(0, capture.FallbackCalls);
    }

    [Fact]
    public async Task A_grabber_with_no_frame_fails_the_screenshot_and_is_disposed()
    {
        var capture = new FakeWindowCapture();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WinAppCapture.CaptureClientAreaAsync(capture, new IntPtr(1), TestContext.Current.CancellationToken));

        Assert.Equal(1, capture.GrabbersStarted);
        Assert.True(capture.LastGrabber!.Disposed);
        Assert.Equal(0, capture.FallbackCalls);
    }
    // ── rounded corners ────────────────────────────────────────────────────

    [Fact]
    public void Rounded_corner_pixels_take_the_nearest_inner_colour()
    {
        using var bmp = WindowWithRoundedBottomCorners(40, 30, out var cornerPixels);

        var replaced = WinAppCapture.SquareRoundedCorners(bmp);

        Assert.Equal(cornerPixels, replaced);
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                Assert.Equal(255, bmp.GetPixel(x, y).A);
        Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(0, 29).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(39, 29).ToArgb());
        // Content is untouched.
        Assert.Equal(Color.Black.ToArgb(), bmp.GetPixel(20, 10).ToArgb());
    }

    [Fact]
    public void An_opaque_capture_is_left_alone()
    {
        using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);

        Assert.Equal(0, WinAppCapture.SquareRoundedCorners(bmp));
    }

    [Fact]
    public void Corner_squaring_rejects_a_bitmap_that_is_not_32bpp_argb()
    {
        using var bmp = new Bitmap(4, 4, PixelFormat.Format24bppRgb);

        Assert.Throws<ArgumentException>(() => WinAppCapture.SquareRoundedCorners(bmp));
    }

    [Fact]
    public void A_fully_transparent_capture_is_left_alone()
    {
        // A headless or not-yet-composed window can come back fully transparent; every row has
        // no opaque pixel, so nothing is replaced (and the pass stays linear per row).
        using var bmp = new Bitmap(1366, 1092, PixelFormat.Format32bppArgb);

        Assert.Equal(0, WinAppCapture.SquareRoundedCorners(bmp));
        Assert.Equal(0, bmp.GetPixel(683, 546).A);
    }

    /// <summary>
    /// The linear pass must give exactly what the nearest-opaque scan it replaced gave: the
    /// left half takes the nearest opaque pixel to its right, the right half the nearest
    /// already-opaque pixel to its left.
    /// </summary>
    [Fact]
    public void Corner_squaring_matches_the_nearest_opaque_scan()
    {
        var rng = new Random(1320);
        for (var trial = 0; trial < 200; trial++)
        {
            var width = rng.Next(1, 24);
            var row = new int[width];
            for (var x = 0; x < width; x++)
            {
                var alpha = rng.Next(3) switch { 0 => 0, 1 => 128, _ => 255 };
                row[x] = (alpha << 24) | rng.Next(0x1000000);
            }

            var expected = (int[])row.Clone();
            var expectedReplaced = ReferenceSquare(expected);

            using var bmp = new Bitmap(width, 1, PixelFormat.Format32bppArgb);
            for (var x = 0; x < width; x++) bmp.SetPixel(x, 0, Color.FromArgb(row[x]));

            Assert.Equal(expectedReplaced, WinAppCapture.SquareRoundedCorners(bmp));
            for (var x = 0; x < width; x++)
                Assert.Equal(expected[x], bmp.GetPixel(x, 0).ToArgb());
        }

        static int ReferenceSquare(int[] row)
        {
            var replaced = 0;
            var mid = row.Length / 2;
            for (var x = 0; x < row.Length; x++)
            {
                if ((uint)row[x] >> 24 == 0xFF) continue;
                var step = x < mid ? 1 : -1;
                for (var s = x + step; s >= 0 && s < row.Length; s += step)
                {
                    if ((uint)row[s] >> 24 != 0xFF) continue;
                    row[x] = row[s];
                    replaced++;
                    break;
                }
            }
            return replaced;
        }
    }

    /// <summary>
    /// Why the squaring exists. Without it the corner arcs read as content, so
    /// content-crop keeps the whole frame. After it, the crop lands on the real content.
    /// </summary>
    [Fact]
    public void Content_crop_finds_the_content_only_after_the_corners_are_squared()
    {
        using var rounded = WindowWithRoundedBottomCorners(40, 30, out _);
        // The arcs stretch the box to both bottom corners.
        Assert.Equal(new Rectangle(0, 8, 40, 22), ImageProcessor.FindContentBounds(rounded));

        WinAppCapture.SquareRoundedCorners(rounded);

        Assert.Equal(new Rectangle(15, 8, 10, 6), ImageProcessor.FindContentBounds(rounded));
    }

    // ── hold-out for a painted frame (issue #989) ──────────────────────────

    [Fact]
    public async Task Blank_frames_are_skipped_until_a_painted_one_arrives()
    {
        var blank = SolidPng(60, 40, Color.White);
        var painted = PaintedPng(60, 40);
        var calls = 0;

        var got = await WinAppCapture.CaptureUntilContent(
            _ => Task.FromResult(++calls < 3 ? blank : painted), Deadline, interval: TimeSpan.Zero);

        Assert.Equal(painted, got);
        Assert.Equal(3, calls);
    }

    /// <summary>Differential control for the test above: same frames, no hold-out, opposite answer.</summary>
    [Fact]
    public async Task Without_the_hold_out_the_first_frame_wins_even_when_blank()
    {
        var blank = SolidPng(60, 40, Color.White);
        var painted = PaintedPng(60, 40);
        var calls = 0;

        var got = await WinAppCapture.CaptureUntilContent(
            _ => Task.FromResult(++calls < 3 ? blank : painted), Deadline, requireContent: false, interval: TimeSpan.Zero);

        Assert.Equal(blank, got);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_deadline_of_only_blank_frames_returns_the_last_blank_frame()
    {
        var blank = SolidPng(60, 40, Color.White);

        var got = await WinAppCapture.CaptureUntilContent(
            _ => Task.FromResult(blank), TimeSpan.FromMilliseconds(400));

        Assert.Equal(blank, got);
        Assert.Throws<BlankFrameException>(
            () => ImageProcessor.Process(got, ImageProcessor.ParseCropMode("content")));
    }

    [Fact]
    public async Task A_deadline_with_no_frame_at_all_returns_empty()
    {
        var calls = 0;

        var got = await WinAppCapture.CaptureUntilContent(
            _ => { calls++; return Task.FromResult(Array.Empty<byte>()); }, TimeSpan.FromMilliseconds(400));

        Assert.Empty(got);
        Assert.True(calls > 0, "the capture was never attempted — this asserts nothing");
    }

    /// <summary>
    /// The deadline bounds each attempt: a capture that hangs (no frame ever arrives) is
    /// cancelled when the time is up instead of holding the pass forever.
    /// </summary>
    [Fact]
    public async Task A_hung_capture_does_not_outlive_the_deadline()
    {
        var started = 0;
        var sw = global::System.Diagnostics.Stopwatch.StartNew();

        var got = await WinAppCapture.CaptureUntilContent(
            async ct => { started++; await Task.Delay(Timeout.Infinite, ct); return []; },
            TimeSpan.FromMilliseconds(500));
        sw.Stop();

        Assert.Equal(1, started);
        Assert.Empty(got);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task A_failing_capture_is_not_retried_as_if_it_were_blank()
    {
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => WinAppCapture.CaptureUntilContent(
            _ => { calls++; throw new InvalidOperationException("Windows Graphics Capture delivered no frame"); }, Deadline));

        Assert.Equal(1, calls);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// White window with a black block, whose two bottom corners are DWM-style rounded:
    /// a 4px arc of semi-transparent grey and fully transparent pixels.
    /// </summary>
    private static Bitmap WindowWithRoundedBottomCorners(int w, int h, out int cornerPixels)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 15, 8, 10, 6);
        }
        cornerPixels = 0;
        for (var dy = 0; dy < 4; dy++)
        {
            for (var dx = 0; dx < 4 - dy; dx++)
            {
                var c = dx == 3 - dy ? Color.FromArgb(120, 96, 96, 96) : Color.FromArgb(0, 0, 0, 0);
                bmp.SetPixel(dx, h - 1 - dy, c);
                bmp.SetPixel(w - 1 - dx, h - 1 - dy, c);
                cornerPixels += 2;
            }
        }
        return bmp;
    }

    /// <summary>Tightly packed BGRA, the layout <see cref="IFrameGrabber.TryGetLatest"/> returns.</summary>
    private static byte[] ToBgra(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bmp.Width * bmp.Height * 4];
            for (var y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + (y * data.Stride), bytes, y * bmp.Width * 4, bmp.Width * 4);
            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Records what capture asked of the library. <see cref="FallbackCalls"/> counts the
    /// PrintWindow / screen paths, which may foreground a window and must never be used.
    /// </summary>
    private sealed class FakeWindowCapture : IWindowCapture
    {
        public bool Supported { get; init; } = true;
        public Exception? StartFailure { get; init; }
        public int GrabbersStarted { get; private set; }
        public int FallbackCalls { get; private set; }
        public FakeGrabber? LastGrabber { get; private set; }

        public bool IsFrameCaptureSupported => Supported;

        public IFrameGrabber StartFrameGrabber(nint hwnd, int fps = 0)
        {
            if (StartFailure is not null) throw StartFailure;
            GrabbersStarted++;
            return LastGrabber = new FakeGrabber();
        }

        public byte[] CaptureWindowPixels(nint hwnd, int width, int height)
        {
            FallbackCalls++;
            return [];
        }

        public byte[] CaptureScreenPixels(int x, int y, int cropWidth, int cropHeight, int encoderWidth, int encoderHeight, int displayWidth, int displayHeight)
        {
            FallbackCalls++;
            return [];
        }
    }

    /// <summary>A grabber whose window never delivers a frame.</summary>
    private sealed class FakeGrabber : IFrameGrabber
    {
        public bool Disposed { get; private set; }
        public bool IsClosed => false;
        public (byte[] Pixels, int Width, int Height, long Version)? TryGetLatest() => null;
        public Task<bool> WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken ct) => Task.FromResult(false);
        public void Dispose() => Disposed = true;
    }
    private static byte[] Encode(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static Bitmap Decode(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var loaded = new Bitmap(ms);
        return new Bitmap(loaded);
    }

    private static byte[] SolidPng(int w, int h, Color color)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(color);
        return Encode(bmp);
    }

    private static byte[] PaintedPng(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var ink = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(ink, w / 4, h / 4, w / 2, h / 2);
        }
        return Encode(bmp);
    }
}
