using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

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
        // Path scoping alone separates checkouts, but not two runs of the *same* checkout:
        // their hosts share this exact image path, so without a liveness signal the second
        // run would classify the first run's live host as an orphan and kill it — the very
        // eviction this sweep was narrowed to prevent, reintroduced one scope down.
        //
        // The claim supplies that signal. Acquiring it means no other run of this layout is
        // in flight, so anything still running this image is genuinely left over from a run
        // that died. Failing to acquire means one is, and its hosts are not orphans.
        if (!LayoutRunClaim.TryAcquireFor(ourExePath))
        {
            Console.WriteLine(
                $"Skipping the orphaned-{label} sweep: another run of this checkout is in " +
                "flight, so its hosts are live rather than orphaned.");
            return;
        }

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

    /// <summary>
    /// Claims this build output for the current run, or returns <see langword="null"/> when a
    /// live sibling run already holds it.
    /// </summary>
    /// <remarks>
    /// <para>Exposed rather than folded into <see cref="LayoutRunClaim"/> so the mechanism can
    /// be exercised directly: whether a second run is refused is the whole of the guarantee,
    /// and a static holder would make that unobservable.</para>
    /// <para>Dispose to release. In the suite nothing does — see
    /// <see cref="LayoutRunClaim"/>.</para>
    /// </remarks>
    internal static IDisposable? TryClaimRun(string ourExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);

        try
        {
            var dir = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft.UI.Reactor",
                "AppTests");
            Directory.CreateDirectory(dir);

            var path = Path.Join(dir, KeyFor(ourExePath) + ".run");

            // FileShare.Read lets a refused contender read the owner record while still
            // denying a second writer, which is what makes this the claim.
            var stream = new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            var owner = $"pid={Environment.ProcessId} exe={ourExePath} started={DateTime.UtcNow:O}";
            var bytes = Encoding.UTF8.GetBytes(owner);
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
            return stream;
        }
        catch (IOException)
        {
            // Held by a live sibling run of this same build output.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Cannot arbitrate, so cannot safely conclude that anything is an orphan.
            return null;
        }
    }

    /// <summary>Stable, filename-safe key for one build output.</summary>
    private static string KeyFor(string ourExePath)
    {
        var canonical = ourExePath.Trim().ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// A held file marking one E2E run of one build output as in flight.
    /// </summary>
    /// <remarks>
    /// <para>Held for the lifetime of the test process and released by the kernel closing the
    /// handle, so a run that crashes leaves no stale claim and the next run correctly sees its
    /// leftovers as orphans. That self-healing is the whole reason this is a file handle rather
    /// than a marker file whose contents have to be believed.</para>
    /// <para>Kept under <c>%LOCALAPPDATA%</c> because the processes it arbitrates between are
    /// per-user, and keyed by a hash of the image path so two build outputs never share one
    /// claim. Acquisition is non-blocking: a sibling run is a reason to skip the sweep, not a
    /// reason to wait — the two runs are then arbitrated by winapp UI turns, which is a
    /// different layer and already handles them.</para>
    /// <para>Claims are tracked per executable, not as a single flag. This assembly sweeps two
    /// different hosts (<c>Reactor.AppTests.Host</c> and <c>Reactor.WinFormsTests.Host</c>), so
    /// a single flag would let the first claim vouch for the second host's path without ever
    /// opening its file, leaving that host sweepable by a concurrent run of this same
    /// checkout.</para>
    /// <para>Deliberately never released explicitly. The claim must outlive the sweep and cover
    /// the whole run, and the process exiting is exactly that lifetime.</para>
    /// </remarks>
    internal static class LayoutRunClaim
    {
        private static readonly object Gate = new();

        private static readonly Dictionary<string, IDisposable> Held = new(StringComparer.Ordinal);

        internal static bool TryAcquireFor(string ourExePath)
        {
            // Keyed by the same digest the claim file is named for, so two spellings of one
            // path cannot disagree about whether it is already held.
            var key = KeyFor(ourExePath);

            lock (Gate)
            {
                if (Held.ContainsKey(key)) return true;

                var claim = TryClaimRun(ourExePath);
                if (claim is null) return false;

                Held[key] = claim;
                return true;
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
