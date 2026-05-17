using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Post-processes captured screenshots: auto-crops whitespace,
/// adds a subtle border and drop shadow so images don't blend into the page.
/// </summary>
internal static class ImageProcessor
{
    private const int ContentPadding = 8;   // breathing room inside the border
    private const int ShadowOffset = 2;     // shadow offset (right + down)
    private const int ShadowBlur = 6;       // number of graduated shadow layers
    private const float ShadowMaxAlpha = 0.12f;

    /// <summary>Hard cap on input image size in bytes. TASK-044.</summary>
    public const int MaxImageBytes = 64 * 1024 * 1024; // 64 MiB

    /// <summary>Hard cap on decoded dimensions. TASK-044.</summary>
    public const int MaxImageDimension = 16384;

    /// <summary>
    /// Crops whitespace then downscales to <paramref name="targetW"/>×<paramref name="targetH"/>
    /// preserving aspect (letterboxed with white). Used by <c>kind: catalog-thumb</c>
    /// in <c>doc-manifest.yaml</c> for the controls-catalog index thumbnails (spec 041 §6.3 + §12 Q7).
    /// No border / drop shadow — the thumbnail itself is the visual; the catalog page
    /// renders it inside a table cell where additional chrome would be noise.
    /// </summary>
    public static byte[] ProcessThumb(byte[] frameBytes, int targetW = 320, int targetH = 240)
    {
        if (frameBytes is null || frameBytes.Length == 0)
            throw new ArgumentException("Empty image bytes.", nameof(frameBytes));
        if (frameBytes.Length > MaxImageBytes)
            throw new ArgumentException($"Image exceeds {MaxImageBytes / (1024 * 1024)} MiB cap.", nameof(frameBytes));
        if (!HasKnownImageMagic(frameBytes))
            throw new ArgumentException("Image bytes are neither PNG nor JPEG.", nameof(frameBytes));
        if (targetW <= 0 || targetH <= 0)
            throw new ArgumentException("Target dimensions must be positive.", nameof(targetW));

        using var ms = new MemoryStream(frameBytes);
        using var source = new Bitmap(ms);
        if (source.Width > MaxImageDimension || source.Height > MaxImageDimension)
            throw new ArgumentException($"Image dimensions exceed {MaxImageDimension}px cap.", nameof(frameBytes));

        // Trim whitespace to focus the thumb on real content.
        var bounds = FindContentBounds(source, threshold: 248);
        bounds = InflateClamp(bounds, ContentPadding, source.Width, source.Height);
        using var cropped = source.Clone(bounds, PixelFormat.Format32bppArgb);

        // Compute letterbox to preserve aspect.
        double scale = Math.Min((double)targetW / cropped.Width, (double)targetH / cropped.Height);
        int drawW = Math.Max(1, (int)Math.Round(cropped.Width * scale));
        int drawH = Math.Max(1, (int)Math.Round(cropped.Height * scale));
        int offX = (targetW - drawW) / 2;
        int offY = (targetH - drawH) / 2;

        using var result = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.White);
            g.DrawImage(cropped, new Rectangle(offX, offY, drawW, drawH));
        }

        using var output = new MemoryStream();
        result.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// Auto-crops whitespace from a captured frame, adds border + drop shadow,
    /// and returns PNG bytes.
    /// </summary>
    public static byte[] Process(byte[] frameBytes)
    {
        // SECURITY (TASK-044): validate magic bytes and size before handing
        // attacker-controllable data to GDI+. GDI+ has a long history of
        // decode-time vulnerabilities; pre-filter to known formats and bound
        // the input size.
        if (frameBytes is null || frameBytes.Length == 0)
            throw new ArgumentException("Empty image bytes.", nameof(frameBytes));
        if (frameBytes.Length > MaxImageBytes)
            throw new ArgumentException($"Image exceeds {MaxImageBytes / (1024 * 1024)} MiB cap.", nameof(frameBytes));
        if (!HasKnownImageMagic(frameBytes))
            throw new ArgumentException("Image bytes are neither PNG nor JPEG.", nameof(frameBytes));

        using var ms = new MemoryStream(frameBytes);
        using var source = new Bitmap(ms);
        if (source.Width > MaxImageDimension || source.Height > MaxImageDimension)
            throw new ArgumentException($"Image dimensions exceed {MaxImageDimension}px cap.", nameof(frameBytes));

        // 1. Find content bounds (trim white edges)
        var bounds = FindContentBounds(source, threshold: 248);

        // Add small padding around content so the border isn't flush
        bounds = InflateClamp(bounds, ContentPadding, source.Width, source.Height);

        using var cropped = source.Clone(bounds, PixelFormat.Format32bppArgb);

        // 2. Add border + shadow
        using var result = AddBorderAndShadow(cropped);

        // 3. Encode as PNG
        using var output = new MemoryStream();
        result.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static Rectangle FindContentBounds(Bitmap bmp, int threshold)
    {
        int top = 0, bottom = bmp.Height - 1;
        int left = 0, right = bmp.Width - 1;

        // Scan from top
        for (int y = 0; y < bmp.Height; y++)
        {
            if (RowHasContent(bmp, y, threshold)) { top = y; break; }
        }

        // Scan from bottom
        for (int y = bmp.Height - 1; y >= top; y--)
        {
            if (RowHasContent(bmp, y, threshold)) { bottom = y; break; }
        }

        // Scan from left
        for (int x = 0; x < bmp.Width; x++)
        {
            if (ColumnHasContent(bmp, x, top, bottom, threshold)) { left = x; break; }
        }

        // Scan from right
        for (int x = bmp.Width - 1; x >= left; x--)
        {
            if (ColumnHasContent(bmp, x, top, bottom, threshold)) { right = x; break; }
        }

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private static bool RowHasContent(Bitmap bmp, int y, int threshold)
    {
        for (int x = 0; x < bmp.Width; x += 2) // sample every other pixel for speed
        {
            var p = bmp.GetPixel(x, y);
            if (p.R < threshold || p.G < threshold || p.B < threshold) return true;
        }
        return false;
    }

    private static bool ColumnHasContent(Bitmap bmp, int x, int yStart, int yEnd, int threshold)
    {
        for (int y = yStart; y <= yEnd; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.R < threshold || p.G < threshold || p.B < threshold) return true;
        }
        return false;
    }

    private static Bitmap AddBorderAndShadow(Bitmap source)
    {
        int w = source.Width;
        int h = source.Height;

        // Canvas: image + space for shadow on right/bottom edges
        int canvasW = w + ShadowOffset + ShadowBlur;
        int canvasH = h + ShadowOffset + ShadowBlur;

        var result = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);

        // Draw drop shadow: graduated semi-transparent rectangles offset behind the image
        for (int i = ShadowBlur; i >= 1; i--)
        {
            float t = (float)i / ShadowBlur;               // 1.0 → 0.0 as we get closer
            int alpha = (int)(ShadowMaxAlpha * (1f - t) * 255);
            if (alpha <= 0) continue;

            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillRectangle(brush,
                ShadowOffset + i,
                ShadowOffset + i,
                w - 1,
                h - 1);
        }

        // Draw the image
        g.DrawImage(source, 0, 0, w, h);

        // Draw 1px border
        using var borderPen = new Pen(Color.FromArgb(209, 213, 219), 1); // gray-300
        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

        return result;
    }

    private static Rectangle InflateClamp(Rectangle r, int padding, int maxW, int maxH)
    {
        int x = Math.Max(0, r.X - padding);
        int y = Math.Max(0, r.Y - padding);
        int right = Math.Min(maxW, r.Right + padding);
        int bottom = Math.Min(maxH, r.Bottom + padding);
        return new Rectangle(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// Returns true iff <paramref name="bytes"/> starts with PNG or JPEG
    /// magic bytes. PNG: 89 50 4E 47 0D 0A 1A 0A. JPEG: FF D8 FF (any ext).
    /// TASK-044.
    /// </summary>
    internal static bool HasKnownImageMagic(byte[] bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return true;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;
        return false;
    }
}
