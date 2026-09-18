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

        var candidates = processes
            .Select(p => new Candidate(SafePid(p), TryGetExecutablePath(p)))
            .ToList();

        var doomed = SelectOurs(candidates, ourExePath).Select(c => c.Pid).ToHashSet();

        foreach (var proc in processes)
        {
            using (proc)
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
    }

    /// <summary>
    /// Registers this run as a live participant in <paramref name="ourExePath"/>, or returns
    /// <see langword="null"/> when the lease cannot be taken.
    /// </summary>
    /// <remarks>
    /// <para>Every live run holds its own lease, named for its process id. A single-owner claim
    /// would be unsound here: it only records the process that took it, not every run that is
    /// live. Run A claims and launches its host, run B is refused and skips the sweep but still
    /// launches a host of its own, and once A exits and its handle is released, run C acquires
    /// the freed claim and sweeps — killing B's host, which is live and not an orphan. Leases
    /// make each run individually visible so that sequence cannot arise.</para>
    /// <para>Held open for the lifetime of the run so the kernel releases it on exit. A crashed
    /// run therefore leaves a file that no longer resists an exclusive open, which is exactly
    /// what marks it as stale to <see cref="AnyLiveSiblingOf"/> — the leftovers of a crash must
    /// stay sweepable.</para>
    /// <para>Exposed rather than folded into <see cref="LayoutRunClaim"/> so the mechanism can
    /// be exercised directly; a static holder would make it unobservable.</para>
    /// </remarks>
    internal static IDisposable? TryClaimRun(string ourExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);

        try
        {
            var dir = ClaimDirectory();
            Directory.CreateDirectory(dir);

            var path = Path.Join(dir, LeasePrefix(ourExePath) + Environment.ProcessId + ".run");

            // FileShare.Read lets a sibling read the owner record, and prove liveness by being
            // refused write access, while still denying a second writer.
            var stream = new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            // Ownership transfers to the caller only on the success path. If stamping the owner
            // record throws, this process would otherwise hold the handle without ever returning
            // a lease, and every sibling would read that as a live run for the rest of the run.
            try
            {
                var owner = $"pid={Environment.ProcessId} exe={ourExePath} started={DateTime.UtcNow:O}";
                var bytes = Encoding.UTF8.GetBytes(owner);
                stream.SetLength(0);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            return stream;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Cannot arbitrate, so cannot safely conclude that anything is an orphan.
            return null;
        }
    }

    /// <summary>
    /// Reports whether any run of <paramref name="ourExePath"/> other than this process is live,
    /// pruning the leases of runs that are not.
    /// </summary>
    /// <remarks>
    /// Liveness is tested by attempting an exclusive open rather than by believing the recorded
    /// pid: the holder's handle is released by the kernel, so a lease that still resists opening
    /// has a live owner and one that yields does not. Reading a pid back and probing it would
    /// reintroduce the reuse hazard the file handle exists to avoid.
    /// </remarks>
    internal static bool AnyLiveSiblingOf(string ourExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);

        try
        {
            var dir = ClaimDirectory();
            if (!Directory.Exists(dir)) return false;

            var prefix = LeasePrefix(ourExePath);
            var mine = prefix + Environment.ProcessId + ".run";

            foreach (var lease in Directory.EnumerateFiles(dir, prefix + "*.run"))
            {
                if (string.Equals(Path.GetFileName(lease), mine, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    // Opening denies the holder nothing it still needs, so this is safe to do
                    // against a lease whose owner died mid-write.
                    using (new FileStream(lease, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }

                    File.Delete(lease);
                }
                catch (FileNotFoundException)
                {
                    // Pruned by another run between enumeration and open.
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (IOException)
                {
                    // Still held: a live sibling run.
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // Cannot establish that it is stale, so it must be treated as live.
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot arbitrate, so cannot safely conclude that anything is an orphan.
            return true;
        }
    }

    private static string ClaimDirectory() => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft.UI.Reactor",
        "AppTests");

    /// <summary>Filename prefix shared by every lease on one build output.</summary>
    private static string LeasePrefix(string ourExePath) => KeyFor(ourExePath) + ".";

    /// <summary>Full path of the lease one process holds on one build output.</summary>
    /// <remarks>
    /// Exposed so a test can stage another run's lease. Liveness here is a held handle, which
    /// no in-process API can fake, and the sequence worth proving — a later run declining to
    /// sweep because an earlier one is still going — needs a second participant to exist.
    /// </remarks>
    internal static string LeasePathFor(string ourExePath, int pid) =>
        Path.Join(ClaimDirectory(), LeasePrefix(ourExePath) + pid + ".run");

    /// <summary>Stable, filename-safe key for one build output.</summary>
    /// <remarks>
    /// Normalized through the same <see cref="NormalizePath"/> the kill predicate uses. The two
    /// must agree: <see cref="SelectOurs"/> treats <c>bin\.\Host.exe</c> and <c>bin\Host.exe</c>
    /// as one image, so if the claim keyed off the raw spelling those two runs would take
    /// different claim files, each conclude no sibling was live, and sweep the other's host.
    /// </remarks>
    private static string KeyFor(string ourExePath)
    {
        var canonical = NormalizePath(ourExePath.Trim()).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// A held file marking one E2E run of one build output as in flight.
    /// </summary>
    /// <remarks>
    /// <para>Held for the lifetime of the test process and released by the kernel closing the
    /// handle, so a run that crashes leaves no live lease and the next run correctly sees its
    /// leftovers as orphans. That self-healing is the whole reason this is a file handle rather
    /// than a marker file whose contents have to be believed.</para>
    /// <para>Kept under <c>%LOCALAPPDATA%</c> because the processes it arbitrates between are
    /// per-user, and keyed by a hash of the image path so two build outputs never share one
    /// lease. Taking the lease never blocks: a sibling run is a reason to skip the sweep, not a
    /// reason to wait — the two runs are then arbitrated by winapp UI turns, which is a
    /// different layer and already handles them.</para>
    /// <para>Leases are tracked per executable, not as a single flag. This assembly sweeps two
    /// different hosts (<c>Reactor.AppTests.Host</c> and <c>Reactor.WinFormsTests.Host</c>), so
    /// a single flag would let the first lease vouch for the second host's path without ever
    /// opening its file, leaving that host sweepable by a concurrent run of this same
    /// checkout.</para>
    /// <para>Two runs starting at once can both observe the other and both decline to sweep.
    /// That is the intended direction to fail: neither has launched a host yet, so nothing is
    /// leaked by waiting, and the next solo run collects whatever they left.</para>
    /// <para>Deliberately never released explicitly. The lease must outlive the sweep and cover
    /// the whole run, and the process exiting is exactly that lifetime.</para>
    /// </remarks>
    internal static class LayoutRunClaim
    {
        private static readonly object Gate = new();

        private static readonly Dictionary<string, IDisposable> Held = new(StringComparer.Ordinal);

        internal static bool TryAcquireFor(string ourExePath)
        {
            // Keyed by the same digest the lease file is named for, so two spellings of one
            // path cannot disagree about whether this run already registered.
            var key = KeyFor(ourExePath);

            lock (Gate)
            {
                if (!Held.ContainsKey(key))
                {
                    var lease = TryClaimRun(ourExePath);
                    if (lease is null) return false;

                    Held[key] = lease;
                }

                // Registered before the question is asked, so a sibling starting concurrently
                // sees this run and declines in turn. Asking first would let both conclude they
                // were alone and both sweep.
                return !AnyLiveSiblingOf(ourExePath);
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
