using System.Diagnostics;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Launches a test host process and binds to its top-level window, retrying the whole launch
/// (kill + relaunch) when the window does not appear in time. A loaded CI runner is occasionally
/// slow to first-paint the host window within a single window-wait; because the launch happens in
/// a test class's <c>[ClassInitialize]</c>, the per-method <see cref="E2eRetryAttribute"/> cannot
/// cover that failure (it wraps a test method, not class init) — so the resilience lives here.
/// Throws only after every attempt is exhausted.
/// </summary>
internal static class HostLaunch
{
    internal static (Process proc, long hwnd) LaunchAndBind(
        string exePath, string windowTitle, int attempts = 3, int perAttemptTimeoutMs = 15000)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            Process? proc = null;
            try
            {
                proc = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })
                       ?? throw new WinAppException($"Process.Start returned null for '{exePath}'.");
                Console.WriteLine($"Host '{windowTitle}' launch attempt {attempt}/{attempts} (PID {proc.Id}).");
                var hwnd = WinAppUi.FindWindowHwnd(proc.Id, windowTitle, timeoutMs: perAttemptTimeoutMs);
                return (proc, hwnd);
            }
            catch (Exception ex) when (ex is WinAppException or TimeoutException)
            {
                last = ex;
                Console.WriteLine($"Host '{windowTitle}' launch attempt {attempt}/{attempts} failed: {ex.Message}");
                TryKill(proc);
                if (attempt < attempts)
                    Thread.Sleep(500);
            }
        }

        throw last ?? new WinAppTimeoutException(
            $"Host window '{windowTitle}' did not appear after {attempts} launch attempts.");
    }

    private static void TryKill(Process? proc)
    {
        if (proc is null)
            return;
        try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
        try { proc.Dispose(); } catch { /* best effort */ }
    }
}
