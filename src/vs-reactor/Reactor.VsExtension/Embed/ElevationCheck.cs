#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    public static class ElevationCheck
    {
        public static bool IsCurrentProcessElevated()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!EmbedNativeMethods.OpenProcessToken(EmbedNativeMethods.GetCurrentProcess(), EmbedNativeMethods.TOKEN_QUERY, out token))
                {
                    return false;
                }

                var elevation = new EmbedNativeMethods.TOKEN_ELEVATION();
                var size = Marshal.SizeOf(typeof(EmbedNativeMethods.TOKEN_ELEVATION));
                if (!EmbedNativeMethods.GetTokenInformation(token, EmbedNativeMethods.TokenElevation, out elevation, size, out _))
                {
                    return false;
                }

                return elevation.TokenIsElevated != 0;
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    EmbedNativeMethods.CloseHandle(token);
                }
            }
        }
    }
}
