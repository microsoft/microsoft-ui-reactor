using System.Drawing;
using System.Drawing.Imaging;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Builders for the exact PNG artifacts the doc pipeline produces, shared by the
/// image-gate tests and the end-to-end compile tests.
/// </summary>
internal static class TestImages
{
    /// <summary>
    /// Builds the exact artifact the pre-fix pipeline produced: an unpainted
    /// (or painted) source frame composited onto a canvas with the drop shadow
    /// and 1&#160;px gray-300 border that <c>ImageProcessor.AddBorderAndShadow</c>
    /// draws.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than routed through <c>ImageProcessor.Process</c>
    /// because <c>Process</c> now refuses blank frames outright — these tests
    /// have to be able to produce the artifact that fix prevents. The geometry
    /// is therefore a duplicate of the product's, and
    /// <c>DocImageIntegrityTests</c> pins it: the <c>blank: false</c> output
    /// must be accepted and the <c>blank: true</c> output must be rejected, so a
    /// drift that made this stub unfaithful (e.g. omitting the surface fill and
    /// leaving shadow in the interior) fails rather than silently weakening the
    /// suite.
    /// </remarks>
    public static byte[] CapturedStub(int w, int h, bool blank)
    {
        const int shadowOffset = 2;
        const int shadowBlur = 6;

        using var bmp = new Bitmap(w + shadowOffset + shadowBlur, h + shadowOffset + shadowBlur,
            PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = global::System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            for (int i = shadowBlur; i >= 1; i--)
            {
                var alpha = (int)(0.12f * (1f - (float)i / shadowBlur) * 255);
                if (alpha <= 0) continue;
                using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillRectangle(shadow, shadowOffset + i, shadowOffset + i, w - 1, h - 1);
            }

            // The captured frame itself, drawn over the shadow.
            using (var surface = new SolidBrush(Color.White))
            {
                g.FillRectangle(surface, 0, 0, w, h);
            }

            if (!blank)
            {
                using var ink = new SolidBrush(Color.FromArgb(32, 32, 32));
                g.FillRectangle(ink, w / 2, h / 2, 8, 8);
            }

            using var border = new Pen(Color.FromArgb(209, 213, 219), 1);
            g.DrawRectangle(border, 0, 0, w - 1, h - 1);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
