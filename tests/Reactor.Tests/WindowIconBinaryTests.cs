using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting.Shell;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1185 — <c>WindowIcon.FromBytes</c> / <c>WindowIcon.FromRgba</c>, and the
/// <see cref="BinaryIconImage"/> parsing and assembly behind them.
/// </summary>
/// <remarks>
/// <para>These run at the headless tier even though they end in a real <c>HICON</c>, because
/// nothing here touches <c>Microsoft.UI.Xaml</c> — <c>CreateIconFromResourceEx</c>,
/// <c>GetIconInfo</c> and <c>GetDIBits</c> are plain <c>user32</c>/<c>gdi32</c>, the same way
/// <c>PackageRuntimeTests</c> calls <c>kernel32</c>.</para>
/// <para>That matters for what the assertions are worth. A test that only compared our
/// assembled bytes against our own expectation of them would pass just as happily if both
/// sides were wrong about the icon-resource layout — the layout is a fact about Windows, not
/// about this repo. So the byte-level tests are backed by
/// <see cref="Rgba_Icon_Round_Trips_Through_The_Platform_Loader"/>, which hands the buffer to
/// the loader and reads the pixels back out, and by
/// <see cref="Doubling_biHeight_Is_Load_Bearing"/>, which perturbs the one header field most
/// easily got wrong and shows the round-trip notices.</para>
/// </remarks>
public partial class WindowIconBinaryTests
{
    // ══════════════════════════════════════════════════════════════
    //  Factory validation and kind reporting
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void FromBytes_Rejects_Empty_Data()
    {
        Assert.Throws<ArgumentException>(() => WindowIcon.FromBytes(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void FromRgba_Rejects_A_Buffer_That_Does_Not_Match_The_Dimensions()
    {
        // One pixel short of 4x4. Silently padding or truncating would produce a garbled
        // icon rather than a diagnosable failure.
        var pixels = new byte[(4 * 4 * 4) - 4];
        Assert.Throws<ArgumentException>(() => WindowIcon.FromRgba(pixels, 4, 4));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    [InlineData(16, -1)]
    [InlineData(BinaryIconImage.MaxRgbaDimension + 1, 16)]
    [InlineData(16, BinaryIconImage.MaxRgbaDimension + 1)]
    public void FromRgba_Rejects_Dimensions_Outside_The_Supported_Range(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowIcon.FromRgba(new byte[4], width, height));
    }

    [Fact]
    public void Binary_Icons_Report_The_Binary_Kind_And_An_Empty_Source()
    {
        // Source is empty on purpose: TaskbarOverlay and ThumbnailToolbar open with an
        // IsNullOrEmpty(Source) guard, so any consumer not taught about the new kind skips
        // it rather than handing an empty string to LoadImageW.
        var fromBytes = WindowIcon.FromBytes(BuildIco((16, 32)));
        var fromRgba = WindowIcon.FromRgba(SolidRgba(8, 8, 1, 2, 3, 255), 8, 8);

        foreach (var icon in new[] { fromBytes, fromRgba })
        {
            Assert.Equal(WindowIconKind.Binary, icon.Kind);
            Assert.True(icon.IsBinary);
            Assert.False(icon.IsResource);
            Assert.Equal(string.Empty, icon.Source);
        }
    }

    [Fact]
    public void Path_And_Resource_Icons_Keep_Their_Kinds()
    {
        // Pins the two pre-existing kinds through the bool-to-enum refactor: IsResource is
        // now derived from Kind, so a wrong mapping would silently reroute every consumer
        // that branches on it.
        var path = WindowIcon.FromPath("Assets/App.ico");
        Assert.Equal(WindowIconKind.Path, path.Kind);
        Assert.False(path.IsResource);
        Assert.False(path.IsBinary);
        Assert.Equal("Assets/App.ico", path.Source);

        var resource = WindowIcon.FromResource("ms-appx:///Assets/App.ico");
        Assert.Equal(WindowIconKind.Resource, resource.Kind);
        Assert.True(resource.IsResource);
        Assert.False(resource.IsBinary);
        Assert.Equal("ms-appx:///Assets/App.ico", resource.Source);
    }

    [Fact]
    public void Binary_Icons_Resolve_To_No_Filesystem_Path()
    {
        // What makes WindowSpec.Icon fall through to the convention/PE fallback, and what
        // keeps the TitleBar icon default from projecting an icon it cannot build a Uri for.
        var icon = WindowIcon.FromRgba(SolidRgba(8, 8, 0, 0, 0, 255), 8, 8);
        Assert.False(icon.TryResolvePath(out var resolved));
        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void FromBytes_Copies_The_Callers_Buffer()
    {
        // The caller owns its array and may reuse it. Overwriting it after construction
        // must not change what the icon renders — and since the wipe destroys the .ico
        // signature, an aliased buffer could not produce a handle at all.
        var caller = BuildRealIco((16, 255, 0, 0));
        var icon = WindowIcon.FromBytes(caller);
        Array.Clear(caller);

        AssertIconColour(icon.CreateBinaryHIcon(16, 16), redish: true);
    }

    // ══════════════════════════════════════════════════════════════
    //  .ico directory parsing and frame selection
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Frame_Selection_Prefers_An_Exact_Size_Match()
    {
        var ico = BuildIco((16, 32), (32, 32), (48, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Frame_Selection_Prefers_The_Smallest_Frame_At_Least_As_Large_As_Requested()
    {
        // Downscaling 48 to 32 keeps detail that upscaling 16 has already thrown away.
        var ico = BuildIco((16, 32), (48, 32), (64, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Frame_Selection_Falls_Back_To_The_Largest_Smaller_Frame()
    {
        var ico = BuildIco((8, 32), (16, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Frame_Selection_With_No_Preference_Takes_The_Largest_Frame()
    {
        // Helper-level fallback only: TryCreateHIcon resolves a zero size to SM_CXICON
        // before selecting, so no shell surface takes this arm. See
        // Default_Size_Selection_Matches_The_System_Icon_Metric for the behaviour callers
        // actually get.
        var ico = BuildIco((16, 32), (48, 32), (32, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 0, 0, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Frame_Selection_Actually_Varies_With_The_Requested_Size()
    {
        // The anti-vacuity pin for every test above: a selector that collapsed to "always
        // take the first entry" would satisfy several of them individually (the exact-match
        // case puts the answer at index 1, but a one-frame-per-assertion reading is easy to
        // lose). Three distinct answers from one file cannot be produced by any constant.
        var ico = BuildIco((16, 32), (32, 32), (48, 32));

        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 16, 16, out var small, out _));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var medium, out _));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 48, 48, out var large, out _));

        Assert.Equal(3, new[] { small, medium, large }.Distinct().Count());
        Assert.True(small < medium && medium < large);
    }

    [Fact]
    public void Frame_Selection_Breaks_Size_Ties_On_Colour_Depth()
    {
        var ico = BuildIco((32, 8), (32, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Frame_Selection_Reads_A_Zero_Dimension_Byte_As_256()
    {
        // The .ico format cannot store 256 in a byte, so it stores 0. Read literally, a
        // 256px frame would rank as the smallest in the file rather than the largest.
        var ico = BuildIco((256, 32), (48, 32));
        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 0, 0, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 0), offset);
    }

    [Fact]
    public void Frame_Selection_Rejects_An_Offset_Pointing_Into_The_Directory()
    {
        // An entry whose payload offset lands inside the ICONDIR is in-bounds for the
        // buffer but cannot be image data. Left un-rejected it out-ranks — and so
        // displaces — the real frame beside it, and the load then fails on a file that
        // had a perfectly good 32px frame available.
        var ico = BuildRealIco((32, 255, 0, 0), (32, 0, 0, 255));

        // Point the first (otherwise winning, since ties go to the earlier equal rank)
        // entry back at the directory itself.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12), 8);

        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);

        // And the whole path still yields a real icon rather than failing the load.
        AssertIconColour(WindowIcon.FromBytes(ico).CreateBinaryHIcon(32, 32), redish: false);
    }

    [Fact]
    public void Frame_Selection_Ranks_Both_Dimensions()
    {
        // Scored on width alone, a 32x16 frame reads as an exact match for a 32x32
        // request and beats the genuinely square one. LoadImageW, given both cx and cy,
        // would not make that mistake.
        var ico = BuildIcoWithSizes((32, 16, 32), (32, 32, 32));

        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 1), offset);
    }

    [Fact]
    public void Malformed_Directories_Are_Rejected()
    {
        var valid = BuildIco((16, 32), (32, 32));

        // Truncated below the six-byte ICONDIR.
        Assert.False(BinaryIconImage.TrySelectIcoFrame(valid.AsSpan(0, 4), 32, 32, out _, out _));

        // idType 2 is a cursor, not an icon.
        var cursor = (byte[])valid.Clone();
        cursor[2] = 2;
        Assert.False(BinaryIconImage.TrySelectIcoFrame(cursor, 32, 32, out _, out _));

        // Zero entries.
        var empty = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(empty.AsSpan(4), 0);
        Assert.False(BinaryIconImage.TrySelectIcoFrame(empty, 32, 32, out _, out _));

        // A count whose directory alone runs past the end of the buffer.
        var overrun = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(overrun.AsSpan(4), 4096);
        Assert.False(BinaryIconImage.TrySelectIcoFrame(overrun, 32, 32, out _, out _));

        // Every entry's payload extends past the end.
        var runaway = (byte[])valid.Clone();
        for (int i = 0; i < 2; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(runaway.AsSpan(6 + (i * 16) + 8), uint.MaxValue);
        Assert.False(BinaryIconImage.TrySelectIcoFrame(runaway, 32, 32, out _, out _));

        // Every entry declares a zero-length payload.
        var hollow = (byte[])valid.Clone();
        for (int i = 0; i < 2; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(hollow.AsSpan(6 + (i * 16) + 8), 0);
        Assert.False(BinaryIconImage.TrySelectIcoFrame(hollow, 32, 32, out _, out _));
    }

    [Fact]
    public void One_Unusable_Entry_Does_Not_Discard_The_Frames_Around_It()
    {
        var ico = BuildIco((16, 32), (32, 32), (48, 32));
        // Point the 32px entry past the end of the buffer.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 16 + 12), uint.MaxValue - 1);

        Assert.True(BinaryIconImage.TrySelectIcoFrame(ico, 32, 32, out var offset, out _));
        Assert.Equal(FrameOffset(ico, index: 2), offset);
    }

    // ══════════════════════════════════════════════════════════════
    //  RGBA to RT_ICON assembly
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Rgba_Assembly_Writes_An_Icon_Style_BitmapInfoHeader()
    {
        var image = BinaryIconImage.BuildRgbaIconImage(SolidRgba(4, 6, 1, 2, 3, 255), 4, 6);

        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(image));          // biSize
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4)));   // biWidth
        // biHeight is doubled: an icon resource stacks the colour plane and the AND mask
        // into one bitmap and the header describes the pair, not the visible icon.
        Assert.Equal(12, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8)));  // biHeight
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(12))); // biPlanes
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(14)));// biBitCount
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(16)));// BI_RGB
    }

    [Fact]
    public void Rgba_Assembly_Emits_Bottom_Up_Bgra_Rows()
    {
        // Two rows, two pixels: every byte of the colour plane is pinned, so a wrong row
        // order and a wrong channel order are separately visible.
        byte[] rgba =
        [
            10, 11, 12, 13,   20, 21, 22, 23,   // top row
            30, 31, 32, 33,   40, 41, 42, 43,   // bottom row
        ];

        var image = BinaryIconImage.BuildRgbaIconImage(rgba, 2, 2);
        var colour = image.AsSpan(40, 2 * 2 * 4).ToArray();

        Assert.Equal(
            new byte[]
            {
                32, 31, 30, 33,   42, 41, 40, 43,   // bottom row first, as B G R A
                12, 11, 10, 13,   22, 21, 20, 23,   // then the top row
            },
            colour);
    }

    [Theory]
    // width, height, expected AND-mask stride: 1 bpp rows padded to a DWORD boundary,
    // which is a different stride from the 32-bpp colour plane's.
    [InlineData(1, 1, 4)]
    [InlineData(16, 16, 4)]
    [InlineData(32, 32, 4)]
    [InlineData(33, 2, 8)]
    [InlineData(64, 3, 8)]
    [InlineData(65, 1, 12)]
    public void Rgba_Assembly_Appends_A_Dword_Aligned_Zero_Mask(int width, int height, int expectedStride)
    {
        var image = BinaryIconImage.BuildRgbaIconImage(
            SolidRgba(width, height, 9, 9, 9, 255), width, height);

        int expectedLength = 40 + (width * height * 4) + (expectedStride * height);
        Assert.Equal(expectedLength, image.Length);

        // All-zero means "take the colour pixel everywhere", deferring transparency to the
        // 32-bpp alpha channel. A non-zero byte here would punch holes in the icon.
        var mask = image.AsSpan(40 + (width * height * 4));
        Assert.All(mask.ToArray(), b => Assert.Equal(0, b));
    }

    // ══════════════════════════════════════════════════════════════
    //  End-to-end through the real platform loader
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Rgba_Icon_Round_Trips_Through_The_Platform_Loader()
    {
        // The oracle for every byte-level assertion above: Windows, not this test, decides
        // whether the layout is right. A banded source also makes the row order observable
        // — a flipped image would report blue at the top.
        var icon = WindowIcon.FromRgba(BandedRgba(32, 32), 32, 32);
        var hIcon = icon.CreateBinaryHIcon(32, 32);
        Assert.NotEqual(0, hIcon);

        try
        {
            var pixels = ReadIconPixels(hIcon, out var width, out var height);
            Assert.Equal(32, width);
            Assert.Equal(32, height);
            AssertRedish(pixels, width, x: 16, y: 4);
            AssertBluish(pixels, width, x: 16, y: 27);
        }
        finally { DestroyIcon(hIcon); }
    }

    [Fact]
    public void Doubling_biHeight_Is_Load_Bearing()
    {
        // Mutation check on the previous test's oracle. With biHeight halved, the loader
        // reads only the first half of the colour plane — which, bottom-up, is the blue
        // band — so a correct-looking icon becomes uniformly blue. If the round-trip above
        // could not tell these apart it would not be proving anything about the header.
        var image = BinaryIconImage.BuildRgbaIconImage(BandedRgba(32, 32), 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(8), 32); // was 64

        var hIcon = BinaryIconImage.TryCreateHIcon(image, 32, 32);
        Assert.NotEqual(0, hIcon);

        try
        {
            var pixels = ReadIconPixels(hIcon, out var width, out _);
            AssertBluish(pixels, width, x: 16, y: 4);
        }
        finally { DestroyIcon(hIcon); }
    }

    [Fact]
    public void Ico_Container_Loading_Selects_The_Frame_The_Surface_Asked_For()
    {
        // Ties parsing, selection, slicing and the native load into one differential: the
        // frames differ only in colour, and the loader rescales whichever it is given to
        // the requested size — so the resulting handle's dimensions cannot distinguish
        // them, and only the pixels can.
        var ico = BuildRealIco(
            (16, 255, 0, 0),   // red
            (48, 0, 0, 255));  // blue
        var icon = WindowIcon.FromBytes(ico);

        AssertIconColour(icon.CreateBinaryHIcon(16, 16), redish: true);
        AssertIconColour(icon.CreateBinaryHIcon(48, 48), redish: false);
    }

    [Fact]
    public void Rgba_Alpha_Survives_The_Round_Trip()
    {
        // A badge is the motivating case for FromRgba, and a badge is mostly transparent.
        // The AND mask this assembles is all-zero, which means "take the colour pixel
        // everywhere" and defers transparency entirely to the 32-bpp alpha channel — so if
        // alpha did not survive, every binary icon would render as an opaque square.
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;
            pixels[i + 1] = 200;
            pixels[i + 2] = 0;
            // Opaque along the top row, fully transparent everywhere else.
            pixels[i + 3] = (i / 4) < 4 ? (byte)255 : (byte)0;
        }

        var hIcon = WindowIcon.FromRgba(pixels, 4, 4).CreateBinaryHIcon(4, 4);
        Assert.NotEqual(0, hIcon);

        try
        {
            var bgra = ReadIconPixels(hIcon, out var width, out _);
            Assert.Equal(255, bgra[(((0 * width) + 1) * 4) + 3]);
            Assert.Equal(0, bgra[(((3 * width) + 1) * 4) + 3]);
        }
        finally { DestroyIcon(hIcon); }
    }

    [Fact]
    public void Default_Size_Selection_Matches_The_System_Icon_Metric()
    {
        // A caller passing 0 means "whatever LR_DEFAULTSIZE would have meant", which is
        // SM_CXICON — not "the biggest frame you have". This differential proves the
        // resolution happens: a .ico carrying default-size artwork and an oversized frame
        // must render the default-size one, which is only observable by colour because
        // CreateIconFromResourceEx rescales whichever frame it is handed.
        // Deliberately an independent binding to the same export rather than the product's
        // own, so a resolution that stopped consulting the OS could not satisfy this.
        int defaultSize = GetSystemMetrics(SM_CXICON);
        Assert.True(defaultSize > 0, "SM_CXICON should be positive on any real desktop.");

        var ico = BuildRealIco(
            (defaultSize, 255, 0, 0),      // the frame authored for the default size
            (defaultSize * 4, 0, 0, 255)); // a larger frame that must not win

        AssertIconColour(WindowIcon.FromBytes(ico).CreateBinaryHIcon(0, 0), redish: true);
    }

    [Fact]
    public void TaskbarOverlay_Loads_A_Binary_Icon()
    {
        // The overlay's binary arm is otherwise unreachable from any test: Apply() needs a
        // live ITaskbarList3, which no headless context has. Reaching the loader directly
        // is how the existing overlay tests pin the null / resource arms too.
        var icon = WindowIcon.FromRgba(SolidRgba(16, 16, 0, 0, 255, 255), 16, 16);
        var hIcon = InvokeLoadIconFor(typeof(TaskbarOverlay), icon);

        Assert.NotEqual(0, hIcon);
        DestroyIcon(hIcon);
    }

    [Fact]
    public void ThumbnailToolbar_Loads_A_Binary_Icon()
    {
        var icon = WindowIcon.FromRgba(SolidRgba(16, 16, 255, 0, 0, 255), 16, 16);
        var hIcon = InvokeLoadIconFor(typeof(ThumbnailToolbarState), icon);

        Assert.NotEqual(0, hIcon);
        DestroyIcon(hIcon);
    }

    /// <summary>
    /// Calls a shell surface's <c>private static nint LoadIconFor(WindowIcon)</c>. Both
    /// overlay and toolbar keep that helper private, and <c>TaskbarOverlayTests</c> already
    /// reaches it this way — matching that rather than widening product visibility for a
    /// test. The <c>DynamicallyAccessedMembers</c> annotation is what keeps this honest
    /// under the project's trim analysis.
    /// </summary>
    private static nint InvokeLoadIconFor(
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type owner,
        WindowIcon icon)
    {
        var method = owner.GetMethod(
            "LoadIconFor",
            global::System.Reflection.BindingFlags.Static
                | global::System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"LoadIconFor not found on {owner.FullName}");
        return (nint)method.Invoke(null, [icon])!;
    }

    [Fact]
    public void FromBytes_Loads_A_Png_Payload()
    {
        // FromBytes documents PNG support, which rests entirely on
        // CreateIconFromResourceEx accepting a whole PNG file as icon-resource bits
        // (Vista and later). That is a claim about Windows, not about this repo, so it
        // has to be measured rather than asserted in a doc comment.
        var png = BuildSolidPng(8, 8, 0, 200, 0);
        var hIcon = WindowIcon.FromBytes(png).CreateBinaryHIcon(8, 8);
        Assert.NotEqual(0, hIcon);

        try
        {
            var bgra = ReadIconPixels(hIcon, out var width, out var height);
            Assert.Equal(8, width);
            Assert.Equal(8, height);
            var (b, g, r) = PixelAt(bgra, width, 4, 4);
            Assert.True(g > 150 && r < 60 && b < 60, $"expected green, got B={b} G={g} R={r}");
        }
        finally { DestroyIcon(hIcon); }
    }

    [Fact]
    public void Data_The_Loader_Cannot_Read_Yields_No_Handle()
    {
        // Failure has to be a zero handle rather than an exception: these run on shell
        // re-apply paths where a throw would cost more than a missing glyph.
        var icon = WindowIcon.FromBytes(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11 });
        Assert.Equal(0, icon.CreateBinaryHIcon(16, 16));
    }

    [Fact]
    public void Non_Binary_Icons_Produce_No_Handle_From_The_Binary_Path()
    {
        Assert.Equal(0, WindowIcon.FromPath("Assets/App.ico").CreateBinaryHIcon(16, 16));
        Assert.Equal(0, WindowIcon.FromResource("ms-appx:///Assets/App.ico").CreateBinaryHIcon(16, 16));
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>Offset of the <paramref name="index"/>th frame's payload within the file.</summary>
    private static int FrameOffset(byte[] ico, int index)
        => (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + (index * 16) + 12));

    /// <summary>
    /// Assemble an <c>.ico</c> whose frames carry filler rather than real images. Enough for
    /// the directory-level tests, which only ever compare offsets.
    /// </summary>
    private static byte[] BuildIco(params (int Size, int BitCount)[] frames)
    {
        // A distinct, non-trivial payload length per frame so an off-by-one in the entry
        // walk shows up as a wrong offset rather than coincidentally matching.
        var payloads = frames.Select((f, i) => new byte[64 + (i * 16)]).ToArray();
        return Assemble(frames.Select(f => (f.Size, f.Size, f.BitCount)).ToArray(), payloads);
    }

    /// <summary>
    /// Same as <see cref="BuildIco"/>, but each frame declares its width and height
    /// separately so a non-square entry can be described.
    /// </summary>
    private static byte[] BuildIcoWithSizes(params (int Width, int Height, int BitCount)[] frames)
    {
        var payloads = frames.Select((f, i) => new byte[64 + (i * 16)]).ToArray();
        return Assemble(frames, payloads);
    }

    /// <summary>
    /// Assemble an <c>.ico</c> whose frames are real 32-bpp icon images, each a solid colour,
    /// so the loader can decode whichever one selection picks.
    /// </summary>
    private static byte[] BuildRealIco(params (int Size, byte R, byte G, byte B)[] frames)
    {
        var payloads = frames
            .Select(f => BinaryIconImage.BuildRgbaIconImage(
                SolidRgba(f.Size, f.Size, f.R, f.G, f.B, 255), f.Size, f.Size))
            .ToArray();
        return Assemble(frames.Select(f => (f.Size, f.Size, 32)).ToArray(), payloads);
    }

    private static byte[] Assemble((int Width, int Height, int BitCount)[] frames, byte[][] payloads)
    {
        int directory = 6 + (frames.Length * 16);
        var file = new byte[directory + payloads.Sum(p => p.Length)];

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), 0);                    // idReserved
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);                    // idType = icon
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), (ushort)frames.Length);

        int cursor = directory;
        for (int i = 0; i < frames.Length; i++)
        {
            var entry = file.AsSpan(6 + (i * 16), 16);
            // 256 is stored as 0 — the format's own encoding of "does not fit in a byte".
            entry[0] = (byte)(frames[i].Width == 256 ? 0 : frames[i].Width);
            entry[1] = (byte)(frames[i].Height == 256 ? 0 : frames[i].Height);
            entry[2] = 0;                                                               // bColorCount
            entry[3] = 0;                                                               // bReserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(4), 1);                // wPlanes
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(6), (ushort)frames[i].BitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8), (uint)payloads[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12), (uint)cursor);

            payloads[i].CopyTo(file.AsSpan(cursor));
            cursor += payloads[i].Length;
        }

        return file;
    }

    private static byte[] SolidRgba(int width, int height, byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        return pixels;
    }

    /// <summary>Opaque red over the top half, opaque blue over the bottom half.</summary>
    private static byte[] BandedRgba(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            bool top = y < height / 2;
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i] = top ? (byte)255 : (byte)0;
                pixels[i + 1] = 0;
                pixels[i + 2] = top ? (byte)0 : (byte)255;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>
    /// Build a minimal single-colour PNG file: signature, IHDR, one zlib-deflated IDAT,
    /// IEND. Assembled here rather than checked in as a base64 blob so the payload is
    /// readable and its dimensions and colour can be varied by a future test.
    /// </summary>
    private static byte[] BuildSolidPng(int width, int height, byte r, byte g, byte b)
    {
        // Raw scanlines: one filter byte (0 = None) followed by RGB triples.
        var raw = new byte[height * (1 + (width * 3))];
        int at = 0;
        for (int y = 0; y < height; y++)
        {
            raw[at++] = 0;
            for (int x = 0; x < width; x++)
            {
                raw[at++] = r;
                raw[at++] = g;
                raw[at++] = b;
            }
        }

        byte[] deflated;
        using (var buffer = new global::System.IO.MemoryStream())
        {
            using (var zlib = new global::System.IO.Compression.ZLibStream(
                buffer, global::System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }
            deflated = buffer.ToArray();
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type 2 = truecolour RGB
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // adaptive filtering
        ihdr[12] = 0; // no interlace

        using var png = new global::System.IO.MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WritePngChunk(png, "IHDR", ihdr);
        WritePngChunk(png, "IDAT", deflated);
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(global::System.IO.Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typed = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) typed[i] = (byte)type[i];
        data.CopyTo(typed.AsSpan(4));
        stream.Write(typed);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static void AssertIconColour(nint hIcon, bool redish)
    {
        Assert.NotEqual(0, hIcon);
        try
        {
            var pixels = ReadIconPixels(hIcon, out var width, out var height);
            int x = width / 2;
            int y = height / 2;
            if (redish) AssertRedish(pixels, width, x, y);
            else AssertBluish(pixels, width, x, y);
        }
        finally { DestroyIcon(hIcon); }
    }

    private static void AssertRedish(byte[] bgra, int width, int x, int y)
    {
        var (b, g, r) = PixelAt(bgra, width, x, y);
        Assert.True(r > 200 && b < 60 && g < 60, $"expected red at ({x},{y}), got B={b} G={g} R={r}");
    }

    private static void AssertBluish(byte[] bgra, int width, int x, int y)
    {
        var (b, g, r) = PixelAt(bgra, width, x, y);
        Assert.True(b > 200 && r < 60 && g < 60, $"expected blue at ({x},{y}), got B={b} G={g} R={r}");
    }

    private static (byte B, byte G, byte R) PixelAt(byte[] bgra, int width, int x, int y)
    {
        int i = ((y * width) + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2]);
    }

    /// <summary>
    /// Read an <c>HICON</c>'s colour plane back as top-down BGRA. This is what makes the
    /// round-trip tests an oracle rather than a restatement: the bytes come back out of the
    /// platform's own copy of the icon.
    /// </summary>
    private static byte[] ReadIconPixels(nint hIcon, out int width, out int height)
    {
        Assert.True(GetIconInfo(hIcon, out var info), "GetIconInfo failed");
        try
        {
            var bitmap = default(BITMAP);
            Assert.NotEqual(0, GetObjectW(info.hbmColor, Marshal.SizeOf<BITMAP>(), ref bitmap));

            width = bitmap.bmWidth;
            height = bitmap.bmHeight;

            var header = default(BITMAPINFOHEADER);
            header.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            header.biWidth = width;
            header.biHeight = -height; // negative: hand the rows back top-down
            header.biPlanes = 1;
            header.biBitCount = 32;
            header.biCompression = 0; // BI_RGB

            var pixels = new byte[width * height * 4];
            nint hdc = GetDC(0);
            try
            {
                int scanned = GetDIBits(hdc, info.hbmColor, 0, (uint)height, pixels, ref header, 0);
                Assert.Equal(height, scanned);
            }
            finally { ReleaseDC(0, hdc); }

            return pixels;
        }
        finally
        {
            if (info.hbmColor != 0) DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) DeleteObject(info.hbmMask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint hIcon, out ICONINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int c, ref BITMAP pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(
        nint hdc, nint hbm, uint start, uint lines,
        [Out] byte[] bits, ref BITMAPINFOHEADER header, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    /// <summary><c>SM_CXICON</c> — the full-size icon width metric.</summary>
    private const int SM_CXICON = 11;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);
}
