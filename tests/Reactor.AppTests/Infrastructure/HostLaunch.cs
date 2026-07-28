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
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Must be at least 1.");
        if (perAttemptTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(perAttemptTimeoutMs), perAttemptTimeoutMs, "Must be positive.");

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
            catch (Exception ex) when (ex is WinAppTimeoutException or TimeoutException)
            {
                // Retryable: the host window did not appear in time.
                last = ex;
                Console.WriteLine($"Host '{windowTitle}' launch attempt {attempt}/{attempts} failed: {ex.Message}");
                KillAndWait(proc);
                if (attempt < attempts)
                    Thread.Sleep(500);
            }
            catch
            {
                // Non-retryable (config/environment error, or Process.Start returned null): clean up
                // the started host so it can't orphan, then fail fast with the real cause.
                KillAndWait(proc);
                throw;
            }
        }

        throw new WinAppTimeoutException(
            $"Host window '{windowTitle}' did not appear after {attempts} launch attempts " +
            $"(<={perAttemptTimeoutMs}ms each). Last error: {last?.Message ?? "unknown"}.");
    }

    private static void KillAndWait(Process? proc)
    {
        if (proc is null)
            return;
        try
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(2000); // best-effort: don't relaunch on top of a still-exiting host
        }
        catch { /* best effort */ }
        finally
        {
            try { proc.Dispose(); } catch { /* best effort */ }
        }
    }
}
