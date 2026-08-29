using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Packaged-only window-icon coverage. Lives alongside
/// <c>WindowIconApplied</c> so it can reuse that file's window helpers and
/// <c>StubComponent</c>.
/// </summary>
internal static partial class WindowModelFixtures
{
    /// <summary>
    /// Proves that <see cref="WindowIcon.FromResource"/> lands a real icon under MSIX
    /// package identity — the exact defect #1145 shipped and then fixed.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the sibling unpackaged check cannot catch this.</b>
    /// <c>WindowIconApplied</c> asserts <c>WindowIcon_FromResource_Sets_HICON</c> as
    /// <c>HICON != 0</c>, and that check passed throughout the bug's lifetime. Two reasons
    /// it could not have caught it: unpackaged, <c>ms-appx:</c> resolves against the
    /// executable directory and genuinely works; and the broken packaged behaviour was
    /// not a zero handle but a <i>non-zero shared default</i> (measured: 65579, identical
    /// across two separate packaged processes). A liveness check cannot separate "loaded
    /// our icon" from "silently substituted the system default".</para>
    /// <para><b>The oracle.</b> The same <c>.ico</c> is applied two ways in the same
    /// process — through <c>ms-appx:</c> and through a plain filesystem path — and the two
    /// windows' icon <i>pixels</i> are compared. Both arms go through
    /// <c>AppWindow.SetIcon</c>, so any platform-side scaling or format normalisation
    /// applies equally to both and cancels out; the only variable is how the source was
    /// addressed. Broken, the <c>ms-appx:</c> arm is the system default and the pixels
    /// differ. Fixed, they are byte-identical.</para>
    /// <para>Handle <i>identity</i> is deliberately not the oracle. Whether two
    /// <c>SetIcon</c> calls for one file yield one shared <c>HICON</c> or two is a
    /// platform caching detail this fixture has no business depending on — asserting on
    /// it would make the check fragile in a way that has nothing to do with the bug.</para>
    /// <para>The zero control from <c>WindowIconApplied</c> is repeated here rather than
    /// assumed: it is what proves the probe can still observe "no icon" in a packaged
    /// process, where the manifest's own logo assets are in play.</para>
    /// </remarks>
    internal partial class PackagedWindowIconFromResource(Harness h) : SelfTestFixtureBase(h)
    {
        private const uint WM_GETICON = 0x007F;
        private const int ICON_BIG = 1;
        private const int DIB_RGB_COLORS = 0;
        private const int BI_RGB = 0;

        private const string IconAsset = "Assets/SelfTestWindowIcon.ico";
        private const string IconResourceUri = "ms-appx:///Assets/SelfTestWindowIcon.ico";

        [LibraryImport("user32.dll")]
        private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

        [LibraryImport("gdi32.dll")]
        private static partial int GetObjectW(nint h, int c, ref BITMAP pv);

        [LibraryImport("gdi32.dll")]
        private static partial nint CreateCompatibleDC(nint hdc);

        [LibraryImport("gdi32.dll")]
        private static partial int GetDIBits(
            nint hdc, nint hbm, uint start, uint cLines,
            [Out] byte[]? lpvBits, ref BITMAPINFO lpbmi, uint usage);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(nint ho);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(nint hdc);

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
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

        // Header plus room for the (unused at 32bpp) colour table, so GDI never writes
        // past the struct.
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint colour0;
            public uint colour1;
            public uint colour2;
        }

        [global::System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static nint IconOf(ReactorWindow win) => SendMessageW(win.Hwnd, WM_GETICON, ICON_BIG, 0);

        /// <summary>
        /// Reads an <c>HICON</c>'s colour plane as top-down 32bpp BGRA.
        /// </summary>
        /// <returns>
        /// <c>null</c> when the handle carries no colour bitmap or GDI refuses the read —
        /// the caller reports that as a failed check rather than treating it as a match,
        /// so a broken probe can never be mistaken for agreement.
        /// </returns>
        [global::System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static byte[]? IconPixels(nint hIcon, out int width, out int height)
        {
            width = height = 0;
            if (hIcon == 0) return null;
            if (!GetIconInfo(hIcon, out var info)) return null;

            var dc = nint.Zero;
            try
            {
                if (info.hbmColor == nint.Zero) return null;

                var bm = default(BITMAP);
                if (GetObjectW(info.hbmColor, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
                if (bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;

                width = bm.bmWidth;
                height = bm.bmHeight;

                var bmi = default(BITMAPINFO);
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = bm.bmWidth;
                // Negative height requests a top-down DIB, so the byte order is stable
                // rather than depending on GDI's default bottom-up convention.
                bmi.bmiHeader.biHeight = -bm.bmHeight;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = BI_RGB;

                dc = CreateCompatibleDC(nint.Zero);
                if (dc == nint.Zero) return null;

                var buffer = new byte[checked(bm.bmWidth * bm.bmHeight * 4)];
                var scanLines = GetDIBits(dc, info.hbmColor, 0, (uint)bm.bmHeight, buffer, ref bmi, DIB_RGB_COLORS);
                return scanLines == bm.bmHeight ? buffer : null;
            }
            finally
            {
                // GetIconInfo hands back two bitmaps the caller owns.
                if (info.hbmColor != nint.Zero) DeleteObject(info.hbmColor);
                if (info.hbmMask != nint.Zero) DeleteObject(info.hbmMask);
                if (dc != nint.Zero) DeleteDC(dc);
            }
        }

        public override async Task RunAsync()
        {
            if (!PackagedIdentityFixtures.RequirePackagedTier(H, this))
                return;

            EnsureUIDispatcher();

            // Control. This host ships no <ApplicationIcon> and no Assets\AppIcon.ico, so
            // a window with no declared icon has nothing to fall back to. Re-asserted here
            // because a packaged process is exactly where that could stop being true: if
            // the manifest's logo assets ever did reach the HWND, every reading below
            // would be comparing against a non-empty default and the oracle would rot.
            var bare = await OpenAndSettle(
                new WindowSpec { Title = "Packaged Icon Control", Width = 200, Height = 160 },
                () => new StubComponent());
            nint bareIcon;
            try { bareIcon = IconOf(bare); }
            finally { await CloseAndSettle(bare); }

            H.Check("PackagedWindowIcon_Control_NoIcon_IsZero", bareIcon == 0);

            // The two arms. Same file, addressed two ways.
            var byResource = await OpenAndSettle(
                new WindowSpec
                {
                    Title = "Packaged Icon FromResource",
                    Width = 200,
                    Height = 160,
                    Icon = WindowIcon.FromResource(IconResourceUri),
                },
                () => new StubComponent());

            byte[]? resourcePixels;
            int resourceW, resourceH;
            nint resourceIcon;
            try
            {
                resourceIcon = IconOf(byResource);
                resourcePixels = IconPixels(resourceIcon, out resourceW, out resourceH);
            }
            finally { await CloseAndSettle(byResource); }

            var byPath = await OpenAndSettle(
                new WindowSpec
                {
                    Title = "Packaged Icon FromPath",
                    Width = 200,
                    Height = 160,
                    Icon = WindowIcon.FromPath(IconAsset),
                },
                () => new StubComponent());

            byte[]? pathPixels;
            int pathW, pathH;
            try
            {
                pathPixels = IconPixels(IconOf(byPath), out pathW, out pathH);
            }
            finally { await CloseAndSettle(byPath); }

            // Liveness. Weak on its own — this is the check that stayed green through the
            // whole bug — but it separates "no icon at all" from "wrong icon" in the
            // failure report.
            H.Check("PackagedWindowIcon_FromResource_Sets_HICON", resourceIcon != 0);

            // Both probes have to have worked, or the comparison below would be an
            // agreement between two nulls.
            H.Check("PackagedWindowIcon_Probe_Read_Both_Icons",
                resourcePixels is not null && pathPixels is not null);

            H.Check("PackagedWindowIcon_Resource_And_Path_Same_Size",
                resourcePixels is not null && pathPixels is not null &&
                resourceW == pathW && resourceH == pathH);

            // The oracle. Broken, the ms-appx arm is the shared system default and these
            // differ; fixed, both are the same asset loaded twice and they are identical.
            H.Check("PackagedWindowIcon_FromResource_Matches_FromPath_Pixels",
                resourcePixels is not null && pathPixels is not null &&
                resourcePixels.AsSpan().SequenceEqual(pathPixels));

            if (resourcePixels is null || pathPixels is null)
            {
                Console.WriteLine(
                    $"# icon probe: resource={(resourcePixels is null ? "null" : $"{resourceW}x{resourceH}")} " +
                    $"path={(pathPixels is null ? "null" : $"{pathW}x{pathH}")} hicon=0x{resourceIcon:X}");
            }
        }
    }
}
