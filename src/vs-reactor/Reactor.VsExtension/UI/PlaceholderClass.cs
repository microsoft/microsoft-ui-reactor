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
            if (msg == NativeMethods.WM_ERASEBKGND)
            {
                return (IntPtr)1;
            }

            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }
}
