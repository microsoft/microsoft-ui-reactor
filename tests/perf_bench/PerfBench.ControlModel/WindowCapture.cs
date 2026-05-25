// PrintWindow-based screenshot capture for the bench host window.
// RenderTargetBitmap.RenderAsync fails on the top-level Window.Content in
// WinUI 3 because the content tree is rooted by a SwapChainPanel that the
// API can't traverse; PrintWindow is a win32 GDI path that captures whatever
// the compositor last presented for the HWND.
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PerfBench.ControlModel;

internal static partial class WindowCapture
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint nFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // PW_CLIENTONLY = 1, PW_RENDERFULLCONTENT = 2 — needed for WinUI/DWM apps
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    public static bool CaptureWindow(IntPtr hwnd, string outPath)
    {
        if (!GetWindowRect(hwnd, out var rect)) return false;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                    return false;
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        bmp.Save(outPath, ImageFormat.Png);
        return true;
    }
}
