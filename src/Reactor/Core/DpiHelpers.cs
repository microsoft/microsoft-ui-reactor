using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Internal helpers for DPI-related fallbacks invoked from hooks that may
/// run outside a <see cref="Microsoft.UI.Reactor.ReactorWindow"/> — e.g. tray
/// flyout content, where there is no owning window to query. (spec 036 §5.2)
/// </summary>
internal static class DpiHelpers
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>System DPI fallback. Returns 96 on any failure.</summary>
    public static uint GetSystemDpiSafe()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi == 0 ? 96 : dpi;
        }
        catch { return 96; }
    }
}
