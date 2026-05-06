using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

public enum SessionInteractivity
{
    Active,
    Locked,
    Disconnected,
    Unknown,
}

/// <summary>
/// Detects when the test process can no longer drive the desktop — workstation
/// locked, idle/timeout lock, or RDP/console disconnect. These conditions cause
/// every Click/SendKeys to fail with a generic WebDriverException, masquerading
/// as test flake. We surface them as Inconclusive (not Failed) and write a
/// marker file so the loop runner can abort the rest of the run.
/// </summary>
public static class SessionInteractivityGuard
{
    public const string MarkerEnvVar = "E2E_LOCK_MARKER_PATH";

    public static SessionInteractivity GetState()
    {
        if (TryGetConnectState(out var wtsState) && wtsState != WTSActive)
            return SessionInteractivity.Disconnected;

        var hDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (hDesktop == IntPtr.Zero)
        {
            // Read GetLastError before any other call can clobber it.
            // ERROR_ACCESS_DENIED is the documented signal that the calling
            // thread can't access the active input desktop — what happens when
            // Winlogon's secure desktop is up. Other failures (invalid handle,
            // out of memory, transient) are genuinely Unknown — don't tag them
            // Locked, or real test failures get masked as Inconclusive.
            var err = Marshal.GetLastWin32Error();
            return err == ERROR_ACCESS_DENIED
                ? SessionInteractivity.Locked
                : SessionInteractivity.Unknown;
        }

        try
        {
            GetUserObjectInformation(hDesktop, UOI_NAME, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
                return SessionInteractivity.Unknown;

            var buf = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetUserObjectInformation(hDesktop, UOI_NAME, buf, needed, out _))
                    return SessionInteractivity.Unknown;
                var name = Marshal.PtrToStringUni(buf) ?? string.Empty;
                return string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                    ? SessionInteractivity.Active
                    : SessionInteractivity.Locked;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        finally
        {
            CloseDesktop(hDesktop);
        }
    }

    /// <summary>
    /// Throws <see cref="AssertInconclusiveException"/> with a clear message and
    /// writes a marker file if the session is not Active. The test framework
    /// records the outcome as Inconclusive (not Failed), and the loop runner sees
    /// the marker and stops scheduling further iterations.
    /// </summary>
    public static void EnsureInteractive(string operation)
    {
        var state = GetState();
        // Unknown means the OS gave us an unexpected error from the desktop
        // probe — don't fabricate a verdict. Let the test run; if WinAppDriver
        // really can't drive input, the WebDriverException recheck will catch
        // a definite Locked/Disconnected on the second look.
        if (state == SessionInteractivity.Active || state == SessionInteractivity.Unknown)
            return;

        WriteMarker(state, operation);
        Assert.Inconclusive(
            $"Cannot perform '{operation}': workstation is {state}. " +
            "UI automation needs an active interactive desktop — locked screen, " +
            "idle/sleep lock, or RDP disconnect makes every WinAppDriver Click() " +
            "fail with a generic WebDriverException. Treating these as Inconclusive " +
            "(not Failed). Unlock the session and rerun.");
    }

    /// <summary>
    /// If <paramref name="operation"/> threw a <see cref="OpenQA.Selenium.WebDriverException"/>,
    /// recheck interactivity and turn the failure into Inconclusive when the screen
    /// has locked since the operation started. Otherwise rethrows the original.
    /// </summary>
    public static void RecheckAfterWebDriverFailure(string operation)
    {
        var state = GetState();
        // Only reclassify when we have positive evidence the desktop is
        // unreachable. Active and Unknown both fall through and the original
        // WebDriverException is rethrown — masking a real failure as
        // Inconclusive on Unknown would lose signal in the diagnostic loop
        // we built this for.
        if (state == SessionInteractivity.Active || state == SessionInteractivity.Unknown)
            return; // Real test failure — caller should rethrow.

        WriteMarker(state, operation);
        Assert.Inconclusive(
            $"'{operation}' failed and the workstation is now {state}. " +
            "The failure is environmental (locked desktop / disconnected session), " +
            "not a test bug. Marker written; remaining tests will short-circuit.");
    }

    private static void WriteMarker(SessionInteractivity state, string operation)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable(MarkerEnvVar);
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), "reactor_e2e_session_locked.flag");

            // FileMode.CreateNew is atomic — first writer wins under parallel
            // contention, and a stale marker from a previous loop won't get
            // silently overwritten with a misleading new timestamp. The runner
            // is responsible for clearing the path between iterations (it
            // points at a fresh per-run directory each time).
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                $"timestamp={DateTimeOffset.Now:O}\n" +
                $"state={state}\n" +
                $"operation={operation}\n" +
                $"pid={Environment.ProcessId}\n");
            using var fs = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            fs.Write(bytes, 0, bytes.Length);
        }
        catch (IOException)
        {
            // Marker already exists — first writer won. Their state/operation
            // is what we want to preserve, so don't overwrite.
        }
        catch
        {
            // Best-effort — never let marker writing mask the real signal.
        }
    }

    // ─── P/Invoke ────────────────────────────────────────────────────────────

    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int UOI_NAME = 2;
    private const int ERROR_ACCESS_DENIED = 5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

    private const int WTS_CURRENT_SESSION = -1;
    private const int WTSConnectState_InfoClass = 8;
    private const int WTSActive = 0;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, int infoClass,
        out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    private static bool TryGetConnectState(out int state)
    {
        state = WTSActive;
        if (!WTSQuerySessionInformation(
                WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION,
                WTSConnectState_InfoClass, out var buf, out _))
        {
            return false;
        }
        try
        {
            state = Marshal.ReadInt32(buf);
            return true;
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }
}
