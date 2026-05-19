using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 Phase 2.0: <see cref="ImageProcessor.ProcessThumb"/> downscales a
/// captured frame to a fixed-size letterboxed thumbnail for the controls-catalog
/// index. These tests exercise size + aspect-ratio behavior; the content-fidelity
/// path is covered by the golden screenshots produced by the doc-app harness.
/// </summary>
public class ImageProcessorTests
{
    [Fact]
    public void ProcessThumb_produces_target_dimensions()
    {
        var png = MakeSolidPng(800, 600, Color.CornflowerBlue);

        var bytes = ImageProcessor.ProcessThumb(png, 320, 240);

        using var ms = new MemoryStream(bytes);
        using var bmp = new Bitmap(ms);
        Assert.Equal(320, bmp.Width);
        Assert.Equal(240, bmp.Height);
    }

    [Fact]
    public void ProcessThumb_letterboxes_non_matching_aspect()
    {
        // 1000×100 source against a 320×240 target — wide aspect should fit
        // horizontally with white letterbox top/bottom.
        var png = MakeSolidPng(1000, 100, Color.Crimson);

        var bytes = ImageProcessor.ProcessThumb(png, 320, 240);

        using var ms = new MemoryStream(bytes);
        using var bmp = new Bitmap(ms);
        Assert.Equal(320, bmp.Width);
        Assert.Equal(240, bmp.Height);
        // Top edge should be the white letterbox, not crimson.
        var topPixel = bmp.GetPixel(160, 4);
        Assert.True(topPixel.R > 240 && topPixel.G > 240 && topPixel.B > 240,
            $"expected letterbox white, got {topPixel}");
    }

    [Fact]
    public void ProcessThumb_rejects_invalid_dimensions()
    {
        var png = MakeSolidPng(100, 100, Color.Black);
        Assert.Throws<ArgumentException>(() => ImageProcessor.ProcessThumb(png, 0, 240));
    }

    [Fact]
    public void ProcessThumb_rejects_non_image_bytes()
    {
        var bogus = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.Throws<ArgumentException>(() => ImageProcessor.ProcessThumb(bogus));
    }

    private static byte[] MakeSolidPng(int w, int h, Color color)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
