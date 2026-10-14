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
    /// Per-channel value at or above which a pixel counts as background. Shared
    /// by content cropping and the blank-frame guard so both agree on what
    /// "empty" means.
    /// </summary>
    internal const int ContentThreshold = 248;

    /// <summary>
    /// Crops whitespace then downscales to <paramref name="targetW"/>×<paramref name="targetH"/>
    /// preserving aspect (letterboxed with white). Used by <c>kind: catalog-thumb</c>
    /// in <c>doc-manifest.yaml</c> for the controls-catalog index thumbnails (spec 041 §6.3 + §12 Q7).
    /// No border / drop shadow — the thumbnail itself is the visual; the catalog page
    /// renders it inside a table cell where additional chrome would be noise.
    /// </summary>
    /// <exception cref="BlankFrameException">
    /// The frame contains no content — see <see cref="Process"/>.
    /// </exception>
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
        var bounds = FindContentBounds(source)
            ?? throw BlankFrameException.ForFrame(source.Width, source.Height);
        if (IsUniformFill(source, new Rectangle(0, 0, source.Width, source.Height)))
            throw BlankFrameException.ForUniformFrame(source.Width, source.Height);
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
    /// Processes a captured frame, adds border + drop shadow, and returns PNG bytes.
    /// </summary>
    /// <exception cref="BlankFrameException">
    /// The frame contains no content — every pixel is at or above
    /// <see cref="ContentThreshold"/>. A doc app whose window never painted (no
    /// interactive desktop, capture polled too early) yields exactly this, and
    /// writing it would silently replace a good committed screenshot with a
    /// solid-white stub. Callers must treat it as a failed capture, not a result.
    /// </exception>
    public static byte[] Process(byte[] frameBytes, ScreenshotCropMode cropMode = ScreenshotCropMode.Content)
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

        // Blank check runs before the crop switch so it covers `crop: none`
        // too — the question is whether the *frame* has content, not whether
        // this particular crop mode would have trimmed it away.
        var contentBounds = FindContentBounds(source)
            ?? throw BlankFrameException.ForFrame(source.Width, source.Height);

        // A frame that is one flat colour has no content regardless of what
        // that colour is; IsContent alone only catches the near-white case.
        if (IsUniformFill(source, new Rectangle(0, 0, source.Width, source.Height)))
            throw BlankFrameException.ForUniformFrame(source.Width, source.Height);

        var bounds = cropMode switch
        {
            ScreenshotCropMode.Content => InflateClamp(
                contentBounds,
                ContentPadding,
                source.Width,
                source.Height),
            ScreenshotCropMode.None => new Rectangle(0, 0, source.Width, source.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(cropMode), cropMode, "Unknown screenshot crop mode.")
        };

        using var cropped = source.Clone(bounds, PixelFormat.Format32bppArgb);

        // 2. Add border + shadow
        using var result = AddBorderAndShadow(cropped);

        // 3. Encode as PNG
        using var output = new MemoryStream();
        result.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// True when <paramref name="frameBytes"/> decodes to an image with at
    /// least one visible content pixel and is not a uniform fill. Cheap probe
    /// used by the capture poller to hold out for a painted frame; returns
    /// <see langword="true"/> for anything it cannot decode so an unexpected
    /// format falls through to the normal validation path instead of being
    /// silently discarded as "blank".
    /// </summary>
    /// <remarks>
    /// The predicate deliberately mirrors what <see cref="Process"/> accepts,
    /// because this decides when the poller stops waiting. If it were the
    /// looser of the two, a frame that satisfies the poller and is then
    /// refused by the processor would fail the capture at the first poll —
    /// even though the deadline it abandoned would have produced a real frame.
    /// The uniform-fill arm is what closes that gap: a themed window paints its
    /// background first, and every pixel of a uniformly *dark* background reads
    /// as content to <see cref="IsContent"/>.
    /// </remarks>
    internal static bool FrameHasContent(byte[] frameBytes)
    {
        if (frameBytes is null || frameBytes.Length == 0) return false;
        if (frameBytes.Length > MaxImageBytes || !HasKnownImageMagic(frameBytes)) return true;
        try
        {
            using var ms = new MemoryStream(frameBytes);
            using var bmp = new Bitmap(ms);
            if (bmp.Width > MaxImageDimension || bmp.Height > MaxImageDimension) return true;
            // Deliberately not FindContentBounds: this runs on every poll of a
            // still-blank window, and the bounds scan is a GetPixel walk over
            // the whole frame (twice, with the full-resolution confirmation).
            // The locked-bits probe below short-circuits on the first content
            // pixel and reads a row at a time.
            var full = new Rectangle(0, 0, bmp.Width, bmp.Height);
            // Order is for cost, not correctness: HasContentPixel short-circuits
            // on the first content pixel, and IsUniformFill short-circuits on the
            // first pixel that differs from the first. A real painted frame
            // therefore leaves both after roughly one row.
            return HasContentPixel(bmp, full) && !IsUniformFill(bmp, full);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException
                                      or global::System.Runtime.InteropServices.ExternalException)
        {
            // GDI+ rejected the bytes — let the caller's normal path report it.
            // All three arms mean the same thing here: GDI+ signals a corrupt or
            // unsupported image as ArgumentException *or* ExternalException
            // depending on the fault, and famously reports a malformed decode as
            // OutOfMemoryException with no memory pressure involved. This runs
            // inside the capture poll loop, so letting any of them escape would
            // abort the whole pass on one bad frame rather than polling again.
            return true;
        }
    }

    /// <summary>
    /// True when <paramref name="b"/>/<paramref name="g"/>/<paramref name="r"/>
    /// at alpha <paramref name="a"/> is visible content once composited over
    /// the white canvas <see cref="AddBorderAndShadow"/> draws.
    /// </summary>
    /// <remarks>
    /// Alpha is not optional here. A composition surface that never rendered
    /// comes back as transparent black — every channel zero, including alpha —
    /// and a naive RGB-only test scores every one of those pixels as content
    /// because 0 &lt; <see cref="ContentThreshold"/>. The frame would sail past
    /// the blank guard, get drawn over white by <c>AddBorderAndShadow</c>, and
    /// be written out as the same solid-white stub the guard exists to stop.
    /// Blending against white first is what makes "content" mean "visible in
    /// the file we are about to write".
    /// </remarks>
    private static bool IsContent(byte b, byte g, byte r, byte a)
    {
        if (a == 0) return false;
        if (a == 255) return b < ContentThreshold || g < ContentThreshold || r < ContentThreshold;

        var packed = CompositeOverWhite(b, g, r, a);
        return (byte)(packed >> 16) < ContentThreshold
            || (byte)(packed >> 8) < ContentThreshold
            || (byte)packed < ContentThreshold;
    }

    /// <summary>
    /// The visible colour of a BGRA pixel once composited source-over opaque
    /// white, packed as 0x00RRGGBB.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="IsContent"/> and <see cref="IsUniformFill"/> on
    /// purpose. They ask different questions — "is this pixel darker than
    /// near-white" and "is every pixel the same colour" — but both are questions
    /// about the <em>visible</em> pixel, and two copies of that arithmetic is
    /// two definitions of "visible" that can drift. When they drifted before,
    /// uniformity compared raw bytes while content composited, so a frame that
    /// renders as one flat sheet through mixed alpha scored as varied and a
    /// blank capture would have been written over a committed screenshot.
    /// </remarks>
    private static uint CompositeOverWhite(byte b, byte g, byte r, byte a)
    {
        if (a == 255) return ((uint)r << 16) | ((uint)g << 8) | b;
        if (a == 0) return 0x00FFFFFFu;

        int inv = 255 - a;
        uint cb = (uint)(((b * a) + (255 * inv) + 127) / 255);
        uint cg = (uint)(((g * a) + (255 * inv) + 127) / 255);
        uint cr = (uint)(((r * a) + (255 * inv) + 127) / 255);
        return (cr << 16) | (cg << 8) | cb;
    }

    /// <summary>
    /// True as soon as any pixel in <paramref name="region"/> is visible
    /// content. Same predicate as <see cref="CountContentPixels"/> but stops at
    /// the first hit — use it whenever the count itself is not needed.
    /// </summary>
    internal static bool HasContentPixel(Bitmap bmp, Rectangle region) =>
        ScanRegion(bmp, region, stopAtFirst: true) > 0;

    internal static ScreenshotCropMode ParseCropMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "content", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenshotCropMode.Content;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenshotCropMode.None;
        }

        throw new ArgumentException(
            $"Unsupported screenshot crop mode '{value}'. Expected 'content' or 'none'.",
            nameof(value));
    }

    /// <summary>
    /// Copies visual row <paramref name="y"/> of a locked region into
    /// <paramref name="row"/>, for either sign of <see cref="BitmapData.Stride"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BitmapData.Scan0"/> points at the image's <em>first scanline</em>,
    /// and a bottom-up DIB expresses "subsequent scanlines live at lower addresses"
    /// as a negative <see cref="BitmapData.Stride"/>. So <c>Scan0 + y * Stride</c>
    /// yields visual row <c>y</c> under both layouts, and no normalisation is
    /// wanted: normalising the base pointer and indexing with <c>|Stride|</c>
    /// mirrors a bottom-up image vertically, while keeping <c>Scan0</c> and
    /// indexing with <c>|Stride|</c> walks off the end of its allocation.
    /// </para>
    /// <para>
    /// This exists as one function because review has proposed that normalisation
    /// three times, against three separate copies of the expression. The risk the
    /// duplication carried was never that the convention was wrong; it was that a
    /// future editor would "fix" one of the three sites and leave the others, so
    /// the three scans would disagree about which row they were reading. One copy
    /// cannot diverge — and it also makes both wrong forms measurable in one edit:
    /// </para>
    /// <list type="table">
    ///   <item><description>
    ///     <c>if (s &lt; 0) { p += s * (h - 1); s = -s; }</c> — the proposed
    ///     normalisation. <strong>1 failure:</strong> the bounds test. It mirrors a
    ///     bottom-up image vertically; the counts survive because counting is
    ///     order-insensitive.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Scan0 + y * |Stride|</c> — keeps the base pointer but drops the sign.
    ///     <strong>2 failures:</strong> bounds and count. It walks off the end of a
    ///     bottom-up allocation, which a count *can* express.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Both were run against the shared helper, so the counts above are for all
    /// three scans at once and do not match the older per-site figures. The oracle
    /// in each case is <see cref="Bitmap.GetPixel(int,int)"/>, which addresses
    /// visual coordinates and knows nothing about stride — so these are not the
    /// implementation agreeing with itself.
    /// </para>
    /// </remarks>
    private static void CopyVisualRow(BitmapData data, int y, byte[] row) =>
        global::System.Runtime.InteropServices.Marshal.Copy(
            data.Scan0 + (y * data.Stride), row, 0, row.Length);

    /// <summary>
    /// Locates the tight bounding box of visible content, or
    /// <see langword="null"/> when the bitmap has none at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One exact pass over the locked bits. The scan this replaced sampled every
    /// other column (<c>x += 2</c>) for speed, which had two consequences: a
    /// frame whose only content sat on odd columns was reported blank, and — the
    /// worse of the two — a frame with content on <em>both</em> odd and even
    /// columns returned a box drawn only around the even ones, silently cropping
    /// real pixels away. The second failure was invisible because the result was
    /// a plausible-looking screenshot, just missing an edge.
    /// </para>
    /// <para>
    /// Sampling is not needed for speed here: the row-buffer read below is far
    /// cheaper per pixel than <see cref="Bitmap.GetPixel"/>, so the exact pass
    /// costs less than the sampled one it replaces.
    /// </para>
    /// </remarks>
    internal static Rectangle? FindContentBounds(Bitmap bmp)
    {
        var full = new Rectangle(0, 0, bmp.Width, bmp.Height);
        if (full.Width <= 0 || full.Height <= 0) return null;

        var data = bmp.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[full.Width * 4];
            int top = -1, bottom = -1, left = int.MaxValue, right = -1;

            for (int y = 0; y < full.Height; y++)
            {
                // Sign-agnostic row addressing — see CopyVisualRow.
                CopyVisualRow(data, y, row);

                int rowLeft = -1, rowRight = -1;
                for (int x = 0, i = 0; x < full.Width; x++, i += 4)
                {
                    // Format32bppArgb is BGRA in memory.
                    if (!IsContent(row[i], row[i + 1], row[i + 2], row[i + 3])) continue;
                    if (rowLeft < 0) rowLeft = x;
                    rowRight = x;
                }

                if (rowLeft < 0) continue;
                if (top < 0) top = y;
                bottom = y;
                if (rowLeft < left) left = rowLeft;
                if (rowRight > right) right = rowRight;
            }

            if (top < 0) return null;
            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Counts visible content pixels inside <paramref name="region"/>. Used by
    /// the committed-corpus gate, which has to scan hundreds of images per
    /// compile, so this reads the locked bits a row at a time rather than
    /// going through <see cref="Bitmap.GetPixel"/>.
    /// </summary>
    internal static int CountContentPixels(Bitmap bmp, Rectangle region) =>
        ScanRegion(bmp, region, stopAtFirst: false);

    /// <summary>
    /// True when every pixel in <paramref name="region"/> is the identical
    /// colour — a single flat fill of any hue, not just white.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IsContent"/> asks "is this pixel darker than near-white", so
    /// a frame that is uniformly *dark* — a themed window whose background
    /// painted but whose content never did — scores every pixel as content and
    /// sails through the blank guard. That is the same failure as the
    /// solid-white stub with the colour inverted, and the same remedy applies:
    /// refuse to write it over a committed screenshot.
    /// </para>
    /// <para>
    /// Deliberately uniformity rather than a minimum content-coverage ratio.
    /// A coverage floor would also catch this, but the committed corpus's
    /// sparsest interior is 0.6084&#160;% content pixels, so any floor able to
    /// catch a stub sits close enough to real assets to start condemning them.
    /// Uniformity has no such tension: every genuine screenshot contains at
    /// least two distinct colours, so this cannot false-fail one. Measured
    /// against all 227 committed images by
    /// <c>DocImageIntegrityTests.Committed_screenshot_corpus_has_no_blank_images</c>.
    /// </para>
    /// </remarks>
    internal static bool IsUniformFill(Bitmap bmp, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (region.Width <= 0 || region.Height <= 0) return false;

        var data = bmp.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[region.Width * 4];
            uint first = 0;
            var haveFirst = false;
            for (int y = 0; y < region.Height; y++)
            {
                // Sign-agnostic row addressing — see CopyVisualRow.
                CopyVisualRow(data, y, row);
                for (int i = 0; i < row.Length; i += 4)
                {
                    // Format32bppArgb is BGRA in memory. Compare what the pixel
                    // LOOKS like, not what it stores: IsContent composites
                    // source-over white for 0 < a < 255, and a uniformity test
                    // that compared raw bytes would disagree with it. Two
                    // pixels that composite to the same visible colour through
                    // different (RGB, A) pairs would read as "varied", the frame
                    // would score as non-uniform, and a contentless frame would
                    // be written over a committed screenshot — the gate failing
                    // open, which is the failure this whole file exists to stop.
                    // Same class as the poller/processor mismatch above: two
                    // predicates that must agree, with nothing making them.
                    var packed = CompositeOverWhite(row[i], row[i + 1], row[i + 2], row[i + 3]);

                    if (!haveFirst)
                    {
                        first = packed;
                        haveFirst = true;
                        continue;
                    }

                    if (packed != first) return false;
                }
            }
            return haveFirst;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static int ScanRegion(Bitmap bmp, Rectangle region, bool stopAtFirst)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (region.Width <= 0 || region.Height <= 0) return 0;

        var data = bmp.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[region.Width * 4];
            int count = 0;
            for (int y = 0; y < region.Height; y++)
            {
                // Row addressing lives in CopyVisualRow. Counting is
                // order-insensitive, so a pure row-order inversion is invisible
                // *at this site*; what a count can express is an address that
                // leaves the buffer. Since the addressing is now one shared
                // function, both mutations are measured against it rather than
                // per-site — see the table on CopyVisualRow.
                CopyVisualRow(data, y, row);
                for (int i = 0; i < row.Length; i += 4)
                {
                    // Format32bppArgb is BGRA in memory.
                    if (!IsContent(row[i], row[i + 1], row[i + 2], row[i + 3])) continue;
                    if (stopAtFirst) return 1;
                    count++;
                }
            }
            return count;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Region of a <em>processed screenshot</em> that excludes the chrome
    /// <see cref="AddBorderAndShadow"/> itself draws — the 1&#160;px border ring and
    /// the <see cref="ShadowOffset"/>&#160;+&#160;<see cref="ShadowBlur"/> strip along the
    /// right and bottom edges. Without this inset a blank capture would still
    /// score its own border as "content" and the gate could never fire.
    /// </summary>
    /// <remarks>
    /// Only meaningful for output of <see cref="Process"/>. Thumbnails
    /// (<see cref="ProcessThumb"/>) and hand-authored assets carry no chrome,
    /// so insetting them would discard real edge content — pass their full
    /// rectangle instead. <see cref="ContentRegionFor"/> makes that choice.
    /// </remarks>
    internal static Rectangle InteriorRegion(int width, int height)
    {
        const int LeadingInset = 2;                                    // 1px border + 1px antialias margin
        const int TrailingInset = ShadowOffset + ShadowBlur + 2;       // shadow strip + border + margin
        var w = width - LeadingInset - TrailingInset;
        var h = height - LeadingInset - TrailingInset;
        if (w <= 0 || h <= 0)
        {
            // Too small to inset meaningfully (thumbnails and hand-authored
            // assets can be tiny). Fall back to the whole image rather than an
            // empty region — an empty region would count zero and false-fire.
            return new Rectangle(0, 0, width, height);
        }
        return new Rectangle(LeadingInset, LeadingInset, w, h);
    }

    /// <summary>
    /// Filename suffix the pipeline reserves for catalog thumbnails.
    /// </summary>
    internal const string ThumbSuffix = "-thumb";

    /// <summary>
    /// True when <paramref name="id"/> — a manifest id or file-name stem, never a
    /// path — ends in the reserved catalog-thumbnail suffix.
    /// </summary>
    /// <remarks>
    /// The single definition of the suffix rule. <see cref="HasThumbSuffix"/> is
    /// this predicate with filename extraction layered on top, for the case where
    /// the subject really is a path; keeping them as one rule plus one adapter is
    /// what stops the two from disagreeing about the same string.
    /// <para>
    /// Passing an <em>id</em> to the path overload is the mistake this split
    /// exists to make hard: <c>Path.GetFileNameWithoutExtension("widget.v2-thumb")</c>
    /// returns <c>"widget"</c>, because it reads <c>.v2-thumb</c> as an extension.
    /// An id is not a path and has no extension to strip, so the transformation
    /// silently changed the subject and the check then answered correctly about a
    /// string nobody asked about.
    /// </para>
    /// </remarks>
    internal static bool IdHasThumbSuffix(string id) =>
        id.EndsWith(ThumbSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="path"/> carries the reserved catalog-thumbnail
    /// suffix, i.e. it was written by <see cref="ProcessThumb"/> and has no chrome.
    /// </summary>
    /// <remarks>
    /// For paths only — see <see cref="IdHasThumbSuffix"/> for manifest ids.
    /// </remarks>
    internal static bool HasThumbSuffix(string path) =>
        IdHasThumbSuffix(Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// The file-name stem a screenshot is written to and linked as, given its
    /// manifest id and whether it is a catalog thumb. Idempotent: an id that
    /// already carries the suffix is not given a second one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One function because there are two callers that must never disagree —
    /// <c>ScreenshotCapture</c> chooses the filename it writes and
    /// <c>DocAssembler</c> chooses the URL that points at it. Two copies of an
    /// append rule is two chances to fix one and not the other, and the symptom
    /// would be a broken image link rather than a compile error.
    /// </para>
    /// <para>
    /// Idempotent because the alternative surprises the author in a way nothing
    /// reports: <c>id: widget-thumb</c> with <c>kind: catalog-thumb</c> yielded
    /// <c>widget-thumb-thumb.png</c>. Both sides agreed, so links resolved and
    /// no gate fired. The reserved-suffix check in <c>CompileCommand</c> does
    /// not cover it either — it exempts <c>catalog-thumb</c> by design, since
    /// for a thumb the suffix is correct rather than a collision. So this shape
    /// had no diagnostic anywhere, which is why it is closed here rather than
    /// reported.
    /// </para>
    /// </remarks>
    internal static string ThumbAwareFileBase(string id, bool isThumb) =>
        !isThumb || IdHasThumbSuffix(id)
            ? id
            : id + ThumbSuffix;

    /// <summary>
    /// Region of a committed image the blank-screenshot gate should score.
    /// </summary>
    /// <remarks>
    /// Full-size captures go through <see cref="AddBorderAndShadow"/>, so their
    /// own chrome has to be excluded or the gate can never fire. Catalog
    /// thumbnails are written by <see cref="ProcessThumb"/>, which draws no
    /// border and no shadow — insetting one would silently ignore up to 10&#160;px
    /// of real content along its right and bottom edges and could report a
    /// perfectly good thumbnail as blank.
    /// <para>
    /// The filename is the only signal a committed file on disk carries, so
    /// <see cref="ThumbSuffix"/> is <em>reserved</em>: <c>docs compile</c> rejects a
    /// non-<c>catalog-thumb</c> manifest entry whose id ends in it
    /// (<c>REACTOR_DOC_SHOT_002</c>). Without that reservation a full-size
    /// screenshot could be named <c>foo-thumb</c>, get scored whole, and hide a
    /// blank capture behind its own border — the exact failure this gate exists
    /// to catch. The inference is sound because the reservation makes the
    /// collision unrepresentable, not because the convention is usually followed.
    /// </para>
    /// <para>
    /// That soundness depends on the reservation testing the id with
    /// <see cref="IdHasThumbSuffix"/>. It previously used the path overload, which
    /// strips from the last dot, so a dotted id such as <c>widget.v2-thumb</c>
    /// passed the reservation and still produced <c>widget.v2-thumb.png</c> — a
    /// file this method then scored as a thumb. The two ends disagreed about the
    /// same screenshot, which is precisely the collision the paragraph above
    /// claims cannot be authored.
    /// </para>
    /// </remarks>
    internal static Rectangle ContentRegionFor(string path, int width, int height) =>
        HasThumbSuffix(path)
            ? new Rectangle(0, 0, width, height)
            : InteriorRegion(width, height);

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

internal enum ScreenshotCropMode
{
    Content,
    None
}
