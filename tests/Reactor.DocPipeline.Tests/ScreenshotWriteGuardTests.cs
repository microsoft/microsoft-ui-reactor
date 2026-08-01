using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #989: the capture phase used to overwrite a committed screenshot with
/// whatever the doc app returned, including the solid-white frame a window that
/// never painted produces. These tests cover
/// <see cref="ScreenshotCapture.ProcessAndWrite"/> — the exact seam where the
/// decision to touch the filesystem is made.
/// </summary>
/// <remarks>
/// This is the strongest headless proof available for the write guard: the full
/// <c>CaptureAsync</c> loop needs a live WinUI desktop and a doc-app subprocess,
/// so exercising it in a unit test is not possible. The end-to-end guarantee is
/// covered instead by the <c>docs-build</c> CI job, which runs a real compile
/// and then <c>git status --porcelain -- docs/guide/images</c> — <c>git status</c>
/// rather than <c>git diff</c>, because <c>git diff</c> reports tracked
/// modifications only and would miss a newly created PNG.
/// </remarks>
public class ScreenshotWriteGuardTests
{
    [Fact]
    public void Blank_frame_does_not_overwrite_an_existing_screenshot()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        Assert.Throws<BlankFrameException>(
            () => ScreenshotCapture.ProcessAndWrite(MakePng(400, 300, painted: false), path, Config()));

        Assert.Equal(committed, global::System.IO.File.ReadAllBytes(path));
    }

    [Fact]
    public void Blank_frame_does_not_create_a_new_screenshot()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");

        Assert.Throws<BlankFrameException>(
            () => ScreenshotCapture.ProcessAndWrite(MakePng(400, 300, painted: false), path, Config()));

        Assert.False(global::System.IO.File.Exists(path),
            "a blank capture must not leave a stub behind for the next reader to trust");
    }

    /// <summary>
    /// Control. Without it the two tests above would pass against a
    /// <c>ProcessAndWrite</c> that never wrote anything at all, which would be a
    /// worse bug than the one being fixed. A real frame must still replace the
    /// committed bytes.
    /// </summary>
    [Fact]
    public void Painted_frame_overwrites_the_existing_screenshot()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        ScreenshotCapture.ProcessAndWrite(MakePng(400, 300, painted: true), path, Config());

        Assert.NotEqual(committed, global::System.IO.File.ReadAllBytes(path));
    }

    [Fact]
    public void Blank_frame_is_refused_for_catalog_thumbs_too()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget-thumb.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        var thumb = Config();
        thumb.Kind = "catalog-thumb";

        Assert.Throws<BlankFrameException>(
            () => ScreenshotCapture.ProcessAndWrite(MakePng(400, 300, painted: false), path, thumb));

        Assert.Equal(committed, global::System.IO.File.ReadAllBytes(path));
    }

    /// <summary>
    /// The unpainted-composition-surface case, at the write seam. Before the
    /// guard blended against white this frame was the one that got through:
    /// every channel is zero, so an RGB-only threshold read it as content, and
    /// it was written out as the solid-white stub the guard exists to stop.
    /// </summary>
    [Fact]
    public void Transparent_frame_does_not_overwrite_an_existing_screenshot()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        Assert.Throws<BlankFrameException>(
            () => ScreenshotCapture.ProcessAndWrite(MakeTransparentPng(400, 300), path, Config()));

        Assert.Equal(committed, global::System.IO.File.ReadAllBytes(path));
    }

    private static ScreenshotConfig Config() =>
        new() { Id = "widget", Format = "png", Crop = "content" };

    /// <summary>
    /// A write that cannot complete fails loudly and leaves the committed
    /// screenshot intact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What this test does not prove.</strong> It does not discriminate
    /// the atomic temp-file-and-move write from the destructive
    /// <c>File.WriteAllBytes(outputPath, …)</c> it replaced. Measured, not
    /// assumed: reverting <c>ProcessAndWrite</c> to the single destructive
    /// write leaves this test — and the whole suite — green, because a
    /// destination held open without write sharing makes <c>WriteAllBytes</c>
    /// fail at <em>open</em>, before it truncates anything. Both formulations
    /// therefore leave the file intact under this fixture.
    /// </para>
    /// <para>
    /// The property that actually differs — that a fault <em>partway through</em>
    /// the write cannot leave the destination truncated — needs the write to
    /// begin and then fail, which no portable fixture can arrange:
    /// <c>WriteAllBytes</c> is a single opaque call with no seam to interrupt.
    /// The change is kept because the mechanism is real (an interrupted
    /// destructive write shreds the committed file while the caller reports a
    /// failed capture that "left it untouched"), but this comment is here so
    /// nobody reads the test name as evidence for the untested half.
    /// </para>
    /// <para>
    /// Nor does it pin the exception <em>type</em>: the assertion admits either
    /// IO fault deliberately, because which one Windows raises for a blocked
    /// replace depends on whether the sharing mode denies write or denies
    /// delete. The observed type here is
    /// <see cref="global::System.UnauthorizedAccessException"/>, which is why
    /// <c>CaptureAsync</c>'s catch list names it — that catch list addition is
    /// itself uncovered, since reaching it needs a live capture server.
    /// </para>
    /// <para>
    /// What it does verify is real and would fail if removed: the fault
    /// surfaces as an exception rather than a silent no-op, the committed bytes
    /// survive, and no <c>.tmp</c> debris is left in the output directory.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_failed_write_fails_loudly_and_leaves_the_committed_screenshot_intact()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        // Hold the destination open with no sharing so the final move fails.
        using (var hold = new global::System.IO.FileStream(
                   path,
                   global::System.IO.FileMode.Open,
                   global::System.IO.FileAccess.Read,
                   global::System.IO.FileShare.Read))
        {
            // File.Move surfaces a blocked replace as UnauthorizedAccessException,
            // not IOException — which is why CaptureAsync's catch list names it
            // explicitly. Both are accepted here so this test pins "the write
            // failed loudly", not one platform's choice of exception type.
            var ex = Assert.ThrowsAny<global::System.Exception>(
                () => ScreenshotCapture.ProcessAndWrite(MakePng(400, 300, painted: true), path, Config()));
            Assert.True(
                ex is global::System.IO.IOException or global::System.UnauthorizedAccessException,
                $"expected an IO fault, got {ex.GetType().Name}: {ex.Message}");
        }

        // Premise guard: if the write had somehow succeeded, "unchanged" below
        // would be asserting about a file the code never tried to replace.
        Assert.Equal(committed, global::System.IO.File.ReadAllBytes(path));

        // And no temp debris is left behind next to it.
        Assert.Empty(global::System.IO.Directory.GetFiles(
            global::System.IO.Path.GetDirectoryName(path)!, "*.tmp"));
    }

    /// <summary>
    /// A uniformly *dark* frame — a themed window whose background painted but
    /// whose content never did — is refused, and the committed screenshot
    /// survives.
    /// </summary>
    /// <remarks>
    /// Before <c>IsUniformFill</c> this frame was accepted: every pixel is
    /// below <c>ContentThreshold</c>, so the near-white content test scored the
    /// entire frame as content and the guard passed it straight through to the
    /// write. The blank-white stub and this are the same failure with the
    /// colour inverted.
    /// </remarks>
    [Fact]
    public void Uniformly_dark_frame_does_not_overwrite_an_existing_screenshot()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        Assert.Throws<BlankFrameException>(
            () => ScreenshotCapture.ProcessAndWrite(MakeSolidPng(400, 300, Color.FromArgb(32, 32, 32)), path, Config()));

        Assert.Equal(committed, global::System.IO.File.ReadAllBytes(path));
    }

    /// <summary>
    /// Non-vacuity pair for the test above: the same solid dark fill with a
    /// single lighter pixel added is written. One pixel is the whole
    /// difference, so this proves the refusal keys on uniformity rather than on
    /// darkness — and that the guard has not simply become unconditional.
    /// </summary>
    [Fact]
    public void Dark_frame_with_one_differing_pixel_is_written()
    {
        using var dir = new TempDir();
        var path = dir.Path("widget.png");
        var committed = MakePng(120, 90, painted: true);
        global::System.IO.File.WriteAllBytes(path, committed);

        var frame = MakeSolidPng(400, 300, Color.FromArgb(32, 32, 32), speckle: Color.FromArgb(200, 200, 200));
        ScreenshotCapture.ProcessAndWrite(frame, path, Config());

        Assert.NotEqual(committed, global::System.IO.File.ReadAllBytes(path));
    }

    private static byte[] MakeSolidPng(int w, int h, Color fill, Color? speckle = null)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = global::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.Clear(fill);
        }

        if (speckle is { } s) bmp.SetPixel(w / 2, h / 2, s);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] MakeTransparentPng(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = global::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.Clear(Color.FromArgb(0, 0, 0, 0));
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] MakePng(int w, int h, bool painted)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            if (painted)
            {
                using var ink = new SolidBrush(Color.FromArgb(24, 24, 24));
                g.FillRectangle(ink, w / 4, h / 4, w / 2, h / 2);
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private sealed class TempDir : global::System.IDisposable
    {
        private readonly string _root = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            "reactor-shot-guard-" + global::System.Guid.NewGuid().ToString("N"));

        public TempDir() => global::System.IO.Directory.CreateDirectory(_root);

        public string Path(string name) => global::System.IO.Path.Join(_root, name);

        public void Dispose() => FixtureCleanup.DeleteTree(_root);
    }
}
