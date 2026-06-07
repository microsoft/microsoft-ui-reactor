#nullable enable

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    internal static class PlaceholderClass
    {
        private const string ClassName = "ReactorEmbedPlaceholder";
        private const int ErrorClassAlreadyExists = 1410;
        private static readonly object s_registrationLock = new object();
        private static int s_registered;
        private static int s_registerClassCallCount;
        private static NativeMethods.WndProcDelegate? s_wndProc;

        internal static int RegisterClassCallCount => Volatile.Read(ref s_registerClassCallCount);

        public static string EnsureRegistered()
        {
            lock (s_registrationLock)
            {
                if (Volatile.Read(ref s_registered) == 1)
                {
                    return ClassName;
                }

                if (Interlocked.CompareExchange(ref s_registered, 1, 0) == 0)
                {
                    s_wndProc = WndProc;
                    var wndClass = new NativeMethods.WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX)),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                        hInstance = NativeMethods.GetModuleHandleW(null),
                        // Paint a solid system-color background instead of leaving the
                        // window contents uninitialized. Before the Reactor child app
                        // finishes its first composition pass and SetParent reparents
                        // its HWND inside this placeholder, the child draw region would
                        // otherwise display whatever pixels happened to live in this
                        // screen space last (the "void" effect — leftover VS chrome,
                        // a previous app frame, etc.). The WPF overlay on top of the
                        // HwndHost is the primary user-facing placeholder; this brush
                        // is defense-in-depth for the brief moment between the
                        // overlay being collapsed and the embedded child's first paint.
                        hbrBackground = (IntPtr)(NativeMethods.COLOR_BTNFACE + 1),
                        lpszClassName = ClassName,
                    };

                    var atom = NativeMethods.RegisterClassExW(ref wndClass);
                    if (atom == 0)
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorClassAlreadyExists)
                        {
                            Volatile.Write(ref s_registered, 0);
                            throw new Win32Exception(error, "Failed to register the Reactor embed placeholder window class.");
                        }
                    }

                    Interlocked.Increment(ref s_registerClassCallCount);
                }

                return ClassName;
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // WM_ERASEBKGND is intentionally NOT short-circuited. Falling through to
            // DefWindowProc lets it paint the WNDCLASSEX.hbrBackground brush
            // (COLOR_BTNFACE) so the empty pre-embed window shows a solid background
            // instead of leftover screen pixels. Once the WinUI child is reparented
            // into this HWND and the WPF overlay collapses, the child fully covers
            // this surface so the brush is invisible — no flicker risk.
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }
}
