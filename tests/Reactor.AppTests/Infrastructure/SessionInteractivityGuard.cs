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
/// every winapp click/input call to fail with a generic error, masquerading
/// as test flake. We surface them as Inconclusive (not Failed) and write a
/// marker file so the loop runner can abort the rest of the run.
/// </summary>
public static partial class SessionInteractivityGuard
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
        // Diagnostic opt-out: winapp's UIA verbs (invoke/get-value/get-property/
        // search/wait-for/focus) work cross-desktop — they succeed even when the
        // console session is locked or disconnected (e.g. the operator is on RDP and
        // the agent's tests run in the detached console session). Only *input
        // injection* needs the live input desktop, and that is gated separately by
        // EnsureInputInjectable. Setting E2E_SKIP_LOCK_GUARD=1 lets the UIA-only
        // tests run in that situation (useful for capturing metrics over RDP);
        // the input-injection tests still skip themselves. Off by default so normal
        // runs keep the conservative lock behaviour.
        if (IsTruthy(Environment.GetEnvironmentVariable("E2E_SKIP_LOCK_GUARD")))
            return;

        // A loaded CI runner can momentarily report a non-Active state (a WTS connect-state blip,
        // or the input desktop not yet resolving to "Default") even though it is genuinely
        // interactive. Re-probe until we get a definitive Active read or the window elapses, so a
        // transient blip doesn't wrongly reclassify a real input-injection E2E as Inconclusive
        // (which the CI gate then fails). Track whether ANY probe in the window saw a definite
        // lock/disconnect, so a single transient Unknown reading can't flip a genuinely locked
        // session to a pass — that would reopen the silent-skip hole the gate exists to close.
        var state = GetState();
        var sawLocked = state is SessionInteractivity.Locked or SessionInteractivity.Disconnected;
        for (int attempt = 0; attempt < 4 && state != SessionInteractivity.Active; attempt++)
        {
            Thread.Sleep(500);
            state = GetState();
            sawLocked |= state is SessionInteractivity.Locked or SessionInteractivity.Disconnected;
        }

        if (state == SessionInteractivity.Active)
            return;

        // Unknown means the OS gave us an unexpected error from the desktop probe — don't fabricate a
        // verdict AND let the test run, UNLESS a probe already saw a definite lock/disconnect this
        // window (then the session is genuinely non-interactive and must surface as Inconclusive).
        // Pure-Unknown proceeds; if winapp really can't drive input, the post-failure recheck catches
        // a definite Locked/Disconnected on the second look.
        if (state == SessionInteractivity.Unknown && !sawLocked)
            return;

        WriteMarker(state, operation);
        Assert.Inconclusive(
            $"Cannot perform '{operation}': workstation is {state}. " +
            "UI automation needs an active interactive desktop — locked screen, " +
            "idle/sleep lock, or RDP disconnect makes every winapp click " +
            "fail with a generic error. Treating these as Inconclusive " +
            "(not Failed). Unlock the session and rerun.");
    }

    private static bool IsTruthy(string? v) =>
        v is "1" or "true" or "True" or "yes" or "YES";

    /// <summary>
    /// If <paramref name="operation"/> threw a <see cref="WinAppException"/>, recheck
    /// interactivity and turn the failure into Inconclusive when the screen has locked since
    /// the operation started. Otherwise rethrows the original.
    /// </summary>
    public static void RecheckAfterFailure(string operation)
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
                $"pid={Environment.ProcessId}\n" +
                $"diag={Diagnose()}\n");
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

    /// <summary>
    /// True when this process can inject synthetic input (mouse/keyboard) onto the
    /// active input desktop. A non-uiAccess process running off the interactive input
    /// desktop — a disconnected/headless session, Session 0, or a remote session with
    /// no console — gets ERROR_ACCESS_DENIED from <c>GetCursorPos</c>/<c>SendInput</c>
    /// (the same UIPI boundary that makes winapp's own <c>click</c> verb fail here).
    /// WinAppDriver dodged this because it ships as a signed <c>uiAccess="true"</c>
    /// binary; winapp 0.3.2 and this raw-SendInput fallback have no such privilege.
    /// </summary>
    public static bool CanInjectInput() => GetCursorPos(out _);

    /// <summary>
    /// Gate for the input-injection tests (real keystrokes / drag / tap). When the
    /// process can't reach the input desktop, every <see cref="InputInjector"/> call
    /// is silently swallowed (SendInput returns 0 / ACCESS_DENIED), which would surface
    /// as a misleading assertion timeout. We mark those tests Inconclusive — not Failed —
    /// exactly like the locked-desktop guard, because the failure is environmental.
    /// On a real interactive desktop these tests execute normally.
    /// </summary>
    public static void EnsureInputInjectable(string operation)
    {
        if (CanInjectInput())
            return;

        Assert.Inconclusive(
            $"Cannot perform '{operation}': this process cannot inject synthetic input " +
            "onto the interactive desktop (GetCursorPos/SendInput return ACCESS_DENIED). " +
            "That happens in a disconnected/headless/Session-0 context, or for any " +
            "non-uiAccess process under UIPI — the same boundary that makes winapp's " +
            "click verb fail here. WinAppDriver bypassed it via its signed uiAccess " +
            "binary. Treating input-injection tests as Inconclusive (not Failed); they " +
            "run on a real interactive desktop. Native winapp verbs (winappCli #562 " +
            "send-keys, #498 drag) will remove this fallback entirely.");
    }

    /// <summary>
    /// Snapshot of the calling process's UI context — window station, the input desktop
    /// name (or the access error), and the WTS connect state — for diagnosing why a run
    /// was classified non-interactive.
    /// </summary>
    public static string Diagnose()
    {
        string sta;
        try
        {
            var hWinSta = GetProcessWindowStation();
            sta = ReadObjectName(hWinSta) ?? "<null>";
        }
        catch (Exception ex) { sta = "err:" + ex.Message; }

        string desktop;
        var hDesk = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (hDesk == IntPtr.Zero)
        {
            desktop = $"OpenInputDesktop-failed(err={Marshal.GetLastWin32Error()})";
        }
        else
        {
            try { desktop = ReadObjectName(hDesk) ?? "<null>"; }
            finally { CloseDesktop(hDesk); }
        }

        var wts = TryGetConnectState(out var s) ? s.ToString() : "query-failed";
        return $"winsta={sta};inputDesktop={desktop};wtsConnectState={wts}";
    }

    private static string? ReadObjectName(IntPtr hObj)
    {
        GetUserObjectInformation(hObj, UOI_NAME, IntPtr.Zero, 0, out var needed);
        if (needed == 0) return null;
        var buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            return GetUserObjectInformation(hObj, UOI_NAME, buf, needed, out _)
                ? Marshal.PtrToStringUni(buf)
                : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── P/Invoke ────────────────────────────────────────────────────────────

    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int UOI_NAME = 2;
    private const int ERROR_ACCESS_DENIED = 5;

    // Source-generated interop (LibraryImport) instead of raw DllImport extern: AOT/trim-friendly
    // and the modern recommended form. The user32/wtsapi32 entry points below have no A/W variants
    // except GetUserObjectInformation and WTSQuerySessionInformation, which are pinned to their
    // explicit -W exports because LibraryImport uses ExactSpelling and does not auto-suffix.
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr OpenInputDesktop(
        uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetProcessWindowStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(IntPtr hDesktop);

    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

    private const int WTS_CURRENT_SESSION = -1;
    private const int WTSConnectState_InfoClass = 8;
    private const int WTSActive = 0;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, int infoClass,
        out IntPtr ppBuffer, out int pBytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr pMemory);

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
