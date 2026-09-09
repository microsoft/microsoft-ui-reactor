using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Turns in-memory icon data into a Win32 <c>HICON</c>. Backs the binary arm of
/// <see cref="WindowIcon"/> (<see cref="WindowIcon.FromBytes"/> /
/// <see cref="WindowIcon.FromRgba"/>) for the shell surfaces that need a raw handle:
/// the tray icon, the taskbar overlay, and thumbnail-toolbar buttons. (issue #1185)
/// </summary>
/// <remarks>
/// <para>Everything funnels into a single native call, <c>CreateIconFromResourceEx</c>,
/// which consumes an <c>RT_ICON</c> payload — a <c>BITMAPINFOHEADER</c>-based DIB, or (Vista
/// and later) a whole PNG file. So the two public shapes converge:</para>
/// <list type="bullet">
/// <item><description>A raw RGBA buffer is assembled into the DIB form by
/// <see cref="BuildRgbaIconImage"/>.</description></item>
/// <item><description>An <c>.ico</c> <i>container</i> is not itself an <c>RT_ICON</c> payload —
/// it is a directory of them — so <see cref="TrySelectIcoFrame"/> picks the frame closest to
/// the requested size and hands only that frame's bytes to the loader.</description></item>
/// <item><description>Anything else (a bare PNG, or a DIB blob already in resource form) is
/// passed straight through.</description></item>
/// </list>
/// <para><b>Why not <c>LookupIconIdFromDirectoryEx</c>.</b> That API selects a frame from a
/// PE <c>RESDIR</c>, whose entries end in a <c>WORD</c> resource id. An <c>.ico</c> file's
/// <c>ICONDIRENTRY</c> ends in a <c>DWORD</c> file offset instead, so the two layouts are
/// the same length but mean different things from byte 12 onward. Feeding a file directory to
/// it reads a resource id out of the low half of an offset. Parsing the directory here is
/// both correct and — unlike the native call — headlessly testable.</para>
/// <para>All parsing is bounds-checked against the caller's buffer and reports failure rather
/// than throwing: an icon is cosmetic, and these run on paths (shell re-apply after an
/// Explorer restart, a taskbar overlay update) where an exception would be worse than a
/// missing glyph.</para>
/// </remarks>
internal static partial class BinaryIconImage
{
    /// <summary>Size of an <c>ICONDIR</c> header: reserved, type, count — three <c>WORD</c>s.</summary>
    private const int IcoHeaderSize = 6;

    /// <summary>Size of one <c>ICONDIRENTRY</c>.</summary>
    private const int IcoEntrySize = 16;

    /// <summary><c>idType</c> value that marks the directory as icons rather than cursors.</summary>
    private const int IcoTypeIcon = 1;

    /// <summary>
    /// A <c>bWidth</c>/<c>bHeight</c> of zero means 256 — the dimension byte cannot hold it.
    /// </summary>
    private const int IcoDimensionForZero = 256;

    private const int BitmapInfoHeaderSize = 40;
    private const uint BI_RGB = 0;

    /// <summary>
    /// <c>CreateIconFromResourceEx</c>'s <c>dwVer</c>: the icon-resource format version, which
    /// must be 0x00030000 for every format Windows has shipped.
    /// </summary>
    private const uint IconResourceVersion = 0x00030000;

    private const uint LR_DEFAULTCOLOR = 0x00000000;

    /// <summary>Full-size icon width metric — what a zero <c>cx</c> resolves to.</summary>
    private const int SM_CXICON = 11;

    /// <summary>Full-size icon height metric — what a zero <c>cy</c> resolves to.</summary>
    private const int SM_CYICON = 12;

    /// <summary>
    /// Create an <c>HICON</c> from in-memory icon data.
    /// </summary>
    /// <param name="data">
    /// An <c>.ico</c> container, a PNG file, or a bare <c>RT_ICON</c> DIB blob.
    /// </param>
    /// <param name="cx">
    /// Desired width in pixels, or <c>0</c> to let the platform pick. Zero is resolved
    /// here to <c>SM_CXICON</c> — the same metric <c>CreateIconFromResourceEx</c> and
    /// <c>LoadImageW</c>'s <c>LR_DEFAULTSIZE</c> both resolve it to — so that frame
    /// selection and the native load agree on one size rather than each deriving its own.
    /// </param>
    /// <param name="cy">Desired height in pixels, or <c>0</c>. See <paramref name="cx"/>.</param>
    /// <returns>The handle, or <c>0</c> when the data could not be loaded.</returns>
    /// <remarks>
    /// Resolving zero <em>before</em> selection is load-bearing for a multi-frame
    /// <c>.ico</c>. Left unresolved, "no preference" would take the largest frame — so a
    /// file carrying both 32px and 256px artwork would render the 256px frame downscaled,
    /// where the file-backed <c>LoadImageW</c> arm on the same surface picks the frame
    /// authored for the default size. The two arms of the same call site would then
    /// disagree about which artwork a multi-size icon shows.
    /// </remarks>
    internal static nint TryCreateHIcon(ReadOnlySpan<byte> data, int cx, int cy)
    {
        if (data.Length == 0) return 0;

        if (cx <= 0) cx = DefaultIconSize(SM_CXICON);
        if (cy <= 0) cy = DefaultIconSize(SM_CYICON);
        var payload = data;
        if (LooksLikeIcoContainer(data))
        {
            if (!TrySelectIcoFrame(data, cx, cy, out var offset, out var length))
            {
                Debug.WriteLine(
                    "[Reactor] BinaryIconImage: no usable frame in the supplied .ico data.");
                return 0;
            }
            payload = data.Slice(offset, length);
        }

        try
        {
            var hIcon = CreateIconFromResourceEx(
                payload, (uint)payload.Length, fIcon: true, IconResourceVersion,
                cx, cy, LR_DEFAULTCOLOR);
            if (hIcon == 0)
            {
                Debug.WriteLine(
                    "[Reactor] BinaryIconImage: CreateIconFromResourceEx failed " +
                    $"(0x{Marshal.GetLastWin32Error():X8}) for {payload.Length} bytes.");
            }
            return hIcon;
        }
        catch (Exception ex)
        {
            // The generated marshalling stub is blittable and should not throw, but a
            // DllNotFoundException / EntryPointNotFoundException on a stripped host must
            // not take down a shell re-apply over a cosmetic icon.
            Debug.WriteLine($"[Reactor] BinaryIconImage: CreateIconFromResourceEx threw: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// The system metric a caller means by "no preference", falling back to the 96-DPI
    /// value if the metric is unavailable. Never returns zero, because zero is what this
    /// resolution exists to eliminate.
    /// </summary>
    private static int DefaultIconSize(int metric)
    {
        try
        {
            var value = TrayIconComInterop.GetSystemMetrics(metric);
            if (value > 0) return value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] BinaryIconImage: GetSystemMetrics({metric}) failed: {ex.Message}");
        }
        return 32;
    }

    /// <summary>
    /// True when <paramref name="data"/> starts with an <c>ICONDIR</c> header: two zero bytes
    /// (reserved) followed by type 1.
    /// </summary>
    /// <remarks>
    /// Distinguishing this from the pass-through payloads needs no heuristics. A PNG starts
    /// with 0x89 'P' 'N' 'G', and a DIB starts with a <c>biSize</c> of 40 — neither can begin
    /// with two zero bytes, so the three shapes are mutually exclusive on their first word.
    /// </remarks>
    internal static bool LooksLikeIcoContainer(ReadOnlySpan<byte> data)
        => data.Length >= IcoHeaderSize
        && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0
        && BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2)) == IcoTypeIcon;

    /// <summary>True when <paramref name="data"/> carries the eight-byte PNG signature.</summary>
    internal static bool LooksLikePng(ReadOnlySpan<byte> data)
        => data.Length >= 8
        && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
        && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>
    /// True when <paramref name="data"/> opens with a <c>BITMAPINFOHEADER</c>, which is the
    /// shape of an <c>RT_ICON</c> resource blob.
    /// </summary>
    internal static bool LooksLikeDibIconImage(ReadOnlySpan<byte> data)
        => data.Length > BitmapInfoHeaderSize
        && BinaryPrimitives.ReadUInt32LittleEndian(data) == BitmapInfoHeaderSize;

    /// <summary>
    /// Pick the <c>ICONDIRENTRY</c> whose image best matches the requested pixel size and
    /// return the slice bounds of its <c>RT_ICON</c> payload.
    /// </summary>
    /// <param name="ico">The whole <c>.ico</c> file.</param>
    /// <param name="desiredWidth">
    /// Target width in pixels. <c>0</c> or negative means "no preference", in which case the
    /// largest frame wins. Note <see cref="TryCreateHIcon"/> resolves zero to
    /// <c>SM_CXICON</c> before calling this, so no shell surface reaches that arm — it is
    /// a helper-level fallback, not the behaviour any caller relies on.
    /// </param>
    /// <param name="desiredHeight">Target height in pixels. See <paramref name="desiredWidth"/>.</param>
    /// <param name="offset">Byte offset of the chosen frame within <paramref name="ico"/>.</param>
    /// <param name="length">Byte length of the chosen frame.</param>
    /// <returns>
    /// <c>false</c> when the directory is malformed or every entry is unusable. Entries whose
    /// declared extent runs past the end of the buffer are skipped individually rather than
    /// failing the whole file, so one bad record does not discard the frames around it.
    /// </returns>
    internal static bool TrySelectIcoFrame(
        ReadOnlySpan<byte> ico, int desiredWidth, int desiredHeight, out int offset, out int length)
    {
        offset = 0;
        length = 0;

        if (!LooksLikeIcoContainer(ico)) return false;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.Slice(4));
        if (count == 0) return false;

        long directoryEnd = (long)IcoHeaderSize + ((long)count * IcoEntrySize);
        if (directoryEnd > ico.Length) return false;

        var best = default(FrameRank);
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            var entry = ico.Slice(IcoHeaderSize + (i * IcoEntrySize), IcoEntrySize);

            int width = entry[0] == 0 ? IcoDimensionForZero : entry[0];
            int height = entry[1] == 0 ? IcoDimensionForZero : entry[1];
            int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6));
            uint bytesInRes = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8));
            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12));

            // Bounds are checked in 64-bit so a hostile pair of DWORDs cannot wrap into a
            // range that looks in-bounds. The offset must also clear the directory itself:
            // an entry pointing back into the ICONDIR is in-bounds but cannot be image
            // data, and left un-rejected it could out-rank — and so displace — a real
            // frame later in the file.
            if (bytesInRes == 0) continue;
            if (imageOffset < directoryEnd) continue;
            if ((long)imageOffset + bytesInRes > ico.Length) continue;

            var rank = FrameRank.For(width, height, bitCount, desiredWidth, desiredHeight);
            if (!found || rank.CompareTo(best) > 0)
            {
                found = true;
                best = rank;
                offset = (int)imageOffset;
                length = (int)bytesInRes;
            }
        }

        return found;
    }

    /// <summary>
    /// How good one <c>.ico</c> frame is for a requested size. Larger compares better.
    /// </summary>
    /// <remarks>
    /// Three ordered fields rather than a single score, so the tiers cannot collide: an exact
    /// match must beat every inexact frame no matter how the sizes happen to be spaced, and
    /// "smallest frame at least as large as the target" must beat every smaller frame for the
    /// same reason. Downscaling a larger frame keeps detail that upscaling a smaller one has
    /// already lost, which is why the ≥ tier outranks the &lt; tier.
    /// <para>Both dimensions are ranked, not just width. An <c>.ico</c> may hold a
    /// deliberately non-square frame, and scoring on width alone would call a 32×16 frame an
    /// exact match for a 32×32 request — diverging from what <c>LoadImageW</c> picks when
    /// both <c>cx</c> and <c>cy</c> are given.</para>
    /// </remarks>
    private readonly struct FrameRank(int tier, int primary, int bitCount)
        : IComparable<FrameRank>
    {
        private readonly int _tier = tier;
        private readonly int _primary = primary;
        private readonly int _bitCount = bitCount;

        internal static FrameRank For(
            int width, int height, int bitCount, int desiredWidth, int desiredHeight)
        {
            if (desiredWidth <= 0 || desiredHeight <= 0)
            {
                // No preference: prefer the most pixels. Area, not width, so a
                // deliberately non-square frame is not mistaken for a better one.
                return new FrameRank(0, width * height, bitCount);
            }

            if (width == desiredWidth && height == desiredHeight)
                return new FrameRank(2, 0, bitCount);

            // Within the "at least as large on both axes" tier, closer to the target is
            // better, so negate the total overshoot. Everything else has to be upscaled on
            // at least one axis; there, more pixels is the best available proxy.
            return width >= desiredWidth && height >= desiredHeight
                ? new FrameRank(1, (desiredWidth - width) + (desiredHeight - height), bitCount)
                : new FrameRank(0, width * height, bitCount);
        }

        public int CompareTo(FrameRank other)
        {
            if (_tier != other._tier) return _tier.CompareTo(other._tier);
            if (_primary != other._primary) return _primary.CompareTo(other._primary);
            return _bitCount.CompareTo(other._bitCount);
        }
    }

    /// <summary>
    /// The largest icon edge <see cref="BuildRgbaIconImage"/> will assemble. Well beyond the
    /// 256px ceiling the <c>.ico</c> format itself can describe; it exists so a bad
    /// width/height pair fails as an argument error instead of as an allocation.
    /// </summary>
    internal const int MaxRgbaDimension = 4096;

    /// <summary>
    /// Assemble a 32-bpp <c>RT_ICON</c> payload from a straight-alpha RGBA8 buffer.
    /// </summary>
    /// <param name="rgba">
    /// <c>width * height * 4</c> bytes, top-down, one pixel as R, G, B, A.
    /// </param>
    /// <param name="width">Icon width in pixels.</param>
    /// <param name="height">Icon height in pixels.</param>
    /// <remarks>
    /// <para>Three things differ between the caller's buffer and what the loader wants, and
    /// all three are easy to get subtly wrong:</para>
    /// <list type="number">
    /// <item><description><c>biHeight</c> is <b>twice</b> the icon height. An icon resource
    /// stacks a colour plane and an AND mask into one bitmap, and the header describes the
    /// pair.</description></item>
    /// <item><description>DIB rows run bottom-up, so the rows are emitted in reverse.</description></item>
    /// <item><description>DIB pixels are BGRA, not RGBA.</description></item>
    /// </list>
    /// <para>The AND mask is left all-zero: on a 32-bpp icon the alpha channel carries
    /// transparency, and a zero mask bit means "take the colour pixel", so an all-zero mask
    /// defers to alpha for every pixel. It still has to be present and correctly sized —
    /// its rows are DWORD-aligned at 1 bpp, which is a different stride from the colour
    /// plane's.</para>
    /// </remarks>
    internal static byte[] BuildRgbaIconImage(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || width > MaxRgbaDimension)
            throw new ArgumentOutOfRangeException(nameof(width), width,
                $"Icon width must be between 1 and {MaxRgbaDimension}.");
        if (height <= 0 || height > MaxRgbaDimension)
            throw new ArgumentOutOfRangeException(nameof(height), height,
                $"Icon height must be between 1 and {MaxRgbaDimension}.");

        int expected = width * height * 4;
        if (rgba.Length != expected)
            throw new ArgumentException(
                $"RGBA buffer must be exactly {expected} bytes for a {width}x{height} icon " +
                $"(got {rgba.Length}).", nameof(rgba));

        int maskStride = ((width + 31) / 32) * 4;
        int maskSize = maskStride * height;
        var buffer = new byte[BitmapInfoHeaderSize + expected + maskSize];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, BitmapInfoHeaderSize);          // biSize
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), width);                 // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), height * 2);            // biHeight
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12), 1);                   // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14), 32);                  // biBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), BI_RGB);              // biCompression
        // biSizeImage stays 0, which BITMAPINFOHEADER explicitly permits for BI_RGB; the
        // loader derives the extents from the dimensions above. The remaining fields
        // (biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant) stay 0 too.

        var colour = span.Slice(BitmapInfoHeaderSize, expected);
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++)
        {
            var source = rgba.Slice(y * rowBytes, rowBytes);
            var destination = colour.Slice((height - 1 - y) * rowBytes, rowBytes);
            for (int x = 0; x < rowBytes; x += 4)
            {
                destination[x + 0] = source[x + 2]; // B
                destination[x + 1] = source[x + 1]; // G
                destination[x + 2] = source[x + 0]; // R
                destination[x + 3] = source[x + 3]; // A
            }
        }

        return buffer;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateIconFromResourceEx(
        ReadOnlySpan<byte> presbits,
        uint dwResSize,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVer,
        int cxDesired,
        int cyDesired,
        uint flags);
}
