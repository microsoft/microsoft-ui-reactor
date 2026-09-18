using System.ComponentModel;
using System.Diagnostics;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Kills host processes left behind by a previous run of <em>this</em> build, without touching
/// an identically named host belonging to another checkout.
/// </summary>
/// <remarks>
/// <para>Both session bootstraps used to sweep by process name alone. That is machine-wide:
/// two worktrees build a host with the same file name, so starting a suite in one checkout
/// killed the live host of a suite already running in another. The UI-turn workflow id this
/// suite now stamps arbitrates the <i>desktop</i> between concurrent runs; it does nothing
/// about a run that terminates the other run's process outright, so the sweep has to be
/// scoped too or the two halves of the isolation story disagree.</para>
/// <para>Scoping is by executable path, which is the only thing that reliably distinguishes
/// two builds of the same host. The path is resolved before the sweep runs, so the sweep can
/// be told what "ours" means.</para>
/// </remarks>
internal static class OrphanedHostSweep
{
    /// <summary>A process considered for the sweep: its id and its executable path, if known.</summary>
    /// <param name="Pid">Process id, used only for diagnostics.</param>
    /// <param name="ExecutablePath">
    /// Full path to the running image, or <see langword="null"/> when it could not be read.
    /// </param>
    internal readonly record struct Candidate(int Pid, string? ExecutablePath);

    /// <summary>
    /// Selects the candidates that belong to the build at <paramref name="ourExePath"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Fails closed.</b> A candidate whose path could not be read is left alone rather
    /// than killed. A genuine orphan of our own run was started by this user at this integrity
    /// level, so its path reads back fine; the candidates that refuse inspection are the ones
    /// least likely to be ours, and killing on "don't know" is what reintroduces the
    /// cross-checkout kill this exists to prevent. The cost of a miss is one stale process,
    /// which does not affect the run that follows: the session binds to the PID and HWND of
    /// the host it launches itself.</para>
    /// </remarks>
    internal static IEnumerable<Candidate> SelectOurs(
        IEnumerable<Candidate> candidates, string ourExePath)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(ourExePath))
            throw new ArgumentException("Our executable path must be non-empty.", nameof(ourExePath));

        var ours = NormalizePath(ourExePath);
        return candidates.Where(c => IsSameImage(c.ExecutablePath, ours));
    }

    /// <summary>
    /// Kills every process named <paramref name="processName"/> that runs the image at
    /// <paramref name="ourExePath"/>, and leaves the rest alone.
    /// </summary>
    internal static void KillOrphansOf(string processName, string ourExePath, string label)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            var candidates = processes
                .Select(p => new Candidate(SafePid(p), TryGetExecutablePath(p)))
                .ToList();

            var doomed = SelectOurs(candidates, ourExePath).Select(c => c.Pid).ToHashSet();

            foreach (var proc in processes)
            {
                var pid = SafePid(proc);
                if (!doomed.Contains(pid))
                {
                    Console.WriteLine(
                        $"Leaving {label} (PID {pid}) alone: it is not running this checkout's build.");
                    continue;
                }

                Console.WriteLine($"Killing orphaned {label} (PID {pid}).");
                TryKill(proc, label, pid);
            }
        }
        finally
        {
            foreach (var proc in processes)
            {
                try { proc.Dispose(); }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // Disposing an already-reaped handle is not worth failing a bootstrap over.
                }
            }
        }
    }

    private static void TryKill(Process proc, string label, int pid)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or NotSupportedException
                or Win32Exception or AggregateException)
        {
            // Already exited between enumeration and kill, or not killable by this user.
            // Neither is fatal: the session binds to the host it launches itself.
            Console.WriteLine($"Could not kill {label} (PID {pid}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int SafePid(Process proc)
    {
        try { return proc.Id; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The full path of a process's main image, or <see langword="null"/> when it cannot be read.
    /// </summary>
    /// <remarks>
    /// <c>MainModule</c> throws rather than returning null for a process this one cannot open —
    /// another user's, a higher integrity level, or one that exited mid-enumeration.
    /// </remarks>
    private static string? TryGetExecutablePath(Process proc)
    {
        try { return proc.MainModule?.FileName; }
        catch (Exception ex) when (
            ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static bool IsSameImage(string? candidatePath, string normalizedOurs) =>
        !string.IsNullOrWhiteSpace(candidatePath) &&
        string.Equals(NormalizePath(candidatePath), normalizedOurs, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }
}
