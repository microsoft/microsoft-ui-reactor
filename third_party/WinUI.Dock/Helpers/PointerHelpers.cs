using System.Runtime.InteropServices;
using Windows.Graphics;

namespace WinUI.Dock;

internal static unsafe partial class PointerHelpers
{
    #region Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct LPPoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;

        public double Y;
    }
    #endregion

    #region Library Imports
    [LibraryImport("USER32.dll")]
    private static partial int GetCursorPos(LPPoint* lpPoint);

    [LibraryImport("libX11.so")]
    private static partial nint XOpenDisplay(nint display);

    [LibraryImport("libX11.so")]
    private static partial int XQueryPointer(nint display,
                                             nint window,
                                             out nint root,
                                             out nint child,
                                             out int rootX,
                                             out int rootY,
                                             out int winX,
                                             out int winY,
                                             out uint mask);

    [LibraryImport("libX11.so")]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial CGPoint CGEventGetLocation(nint eventRef);
    #endregion

    private static readonly Func<PointInt32> getPointerPosition;

    static PointerHelpers()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            getPointerPosition = GetPointerPositionWindows;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            getPointerPosition = GetPointerPositionLinux;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            getPointerPosition = GetPointerPositionMacOS;
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform.");
        }
    }

    public static PointInt32 GetPointerPosition()
    {
        return getPointerPosition();
    }

    private static PointInt32 GetPointerPositionWindows()
    {
        LPPoint point = default;

        _ = GetCursorPos(&point);

        return new()
        {
            X = point.X,
            Y = point.Y
        };
    }

    private static PointInt32 GetPointerPositionLinux()
    {
        nint display = XOpenDisplay((nint)null);

        if (display == nint.Zero)
        {
            throw new InvalidOperationException("Failed to open X display.");
        }

        nint rootWindow = XDefaultRootWindow(display);

        if (rootWindow == nint.Zero)
        {
            throw new InvalidOperationException("Failed to get root window.");
        }

        _ = XQueryPointer(display,
                          rootWindow,
                          out _,
                          out _,
                          out int rootX,
                          out int rootY,
                          out _,
                          out _,
                          out _);

        _ = XCloseDisplay(display);

        return new()
        {
            X = rootX,
            Y = rootY
        };
    }

    private static PointInt32 GetPointerPositionMacOS()
    {
        CGPoint cgPoint = CGEventGetLocation((nint)null);

        return new()
        {
            X = (int)cgPoint.X,
            Y = (int)cgPoint.Y
        };
    }
}