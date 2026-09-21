using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Reactor.Tests.Shared;

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
internal static partial class OrphanedHostSweep
{
    /// <summary>How long a run waits for the startup gate before giving up.</summary>
    /// <remarks>
    /// A sweep is sub-second, so any real wait here is another run's startup, not this one's.
    /// Generous enough that ordinary contention resolves, short enough that a gate left held by
    /// something unkillable surfaces as a failure rather than an indefinite hang.
    /// </remarks>
    private static readonly TimeSpan StartupGateTimeout = TimeSpan.FromMinutes(2);

    /// <summary>A process considered for the sweep: its id and its executable path, if known.</summary>
    /// <param name="Pid">Process id, used only for diagnostics.</param>
    /// <param name="ExecutablePath">
    /// Full path to the running image, or <see langword="null"/> when it could not be read.
    /// </param>
    /// <param name="SessionId">
    /// Terminal-services session the process belongs to, or <see langword="null"/> when it
    /// could not be read.
    /// </param>
    /// <param name="LauncherIsLive">
    /// Whether the process that started this one is still running. <see langword="true"/> means
    /// the candidate is demonstrably attended and must not be swept; <see langword="false"/>
    /// means its launcher is gone, which is what "orphan" means. Supplied by the caller rather
    /// than queried here so selection stays pure data.
    /// </param>
    internal readonly record struct Candidate(
        int Pid, string? ExecutablePath, int? SessionId, bool LauncherIsLive);

    /// <summary>Whether a run may sweep, must skip, or could not be admitted at all.</summary>
    /// <remarks>
    /// Three outcomes rather than two because "a sibling is live" and "the lease could not be
    /// written" call for opposite actions, and a boolean forced them to share one. Reporting an
    /// I/O failure as "a sibling is live" reads as the safe direction but is not: it skips the
    /// sweep — which is safe — while also leaving the run with no lease, so it is invisible to
    /// every later run, and the first one to start after the fault clears sees no sibling and
    /// kills this run's live host.
    /// </remarks>
    internal enum SweepAdmission
    {
        /// <summary>Lease held, no other run live. This run may sweep.</summary>
        Admitted,

        /// <summary>Lease held, another run of this image is live. Skip the sweep.</summary>
        Deferred,

        /// <summary>The lease could not be recorded. This run cannot safely continue.</summary>
        Unavailable,
    }

    /// <summary>
    /// Selects the candidates that belong to the build at <paramref name="ourExePath"/> and run
    /// in session <paramref name="ourSessionId"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Fails closed.</b> A candidate whose path could not be read is left alone rather
    /// than killed. A genuine orphan of our own run was started by this user at this integrity
    /// level, so its path reads back fine; the candidates that refuse inspection are the ones
    /// least likely to be ours, and killing on "don't know" is what reintroduces the
    /// cross-checkout kill this exists to prevent. The cost of a miss is one stale process,
    /// which does not affect the run that follows: the session binds to the PID and HWND of
    /// the host it launches itself.</para>
    /// <para><b>Scoped to one session, because the liveness leases are not machine-wide.</b>
    /// <see cref="Process.GetProcessesByName(string)"/> enumerates every session on the
    /// machine, but <see cref="ClaimDirectory"/> lives under the current user's
    /// <c>%LOCALAPPDATA%</c>. A host belonging to a different Windows user therefore has its
    /// lease in a namespace this run cannot see, so it presents exactly as an orphan does —
    /// right image, no live sibling — and an elevated run has the rights to act on that
    /// mistake. Path scoping does not help: the other user may be running the very same build
    /// output. Comparing sessions restores the invariant the lease depends on, because a
    /// session belongs to one user and our own orphans are always in ours: this process
    /// launched them. A candidate whose session cannot be read is left alone for the same
    /// reason an unreadable path is.</para>
    /// <para><b>Requires the launcher to be gone, which is the only staleness signal that does
    /// not depend on the other run cooperating.</b> The lease establishes that no <em>other
    /// participant</em> in this protocol is live, and that is strictly weaker than "nothing is
    /// live": a run started from a revision predating the lease writes no lease at all, so it
    /// is invisible to admission, and this run would be admitted and would then find that run's
    /// perfectly healthy host sharing our image path and kill it mid-test. No handshake can fix
    /// that, because the other side is already running code that does not implement it.
    /// <see cref="Candidate.LauncherIsLive"/> is decidable without its cooperation: a host is
    /// started as a direct child of the run that owns it, so its run being alive is a fact
    /// about the process table rather than about any file this protocol writes. A host whose
    /// launcher is still running is attended by definition, whatever revision started it, and
    /// an orphan's launcher is by definition gone.</para>
    /// <para>The two conditions are kept because neither subsumes the other. The launcher check
    /// alone would admit killing a host whose own run has already exited but which a sibling
    /// run is still driving; the lease alone misses every non-participant. Both must say
    /// "stale" before anything is killed.</para>
    /// </remarks>
    internal static IEnumerable<Candidate> SelectOurs(
        IEnumerable<Candidate> candidates, string ourExePath, int ourSessionId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(ourExePath))
            throw new ArgumentException("Our executable path must be non-empty.", nameof(ourExePath));

        var ours = NormalizePath(ourExePath);
        return candidates.Where(c =>
            c.SessionId == ourSessionId
            && !c.LauncherIsLive
            && IsSameImage(c.ExecutablePath, ours));
    }

    /// <summary>
    /// Takes the per-image startup gate, or returns <see langword="null"/> when it cannot be
    /// taken within <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>Admission and sweeping have to be one indivisible step, and leases alone do not
    /// make them one. Run A registers, sees no sibling and starts sweeping; run B registers,
    /// sees A, correctly declines to sweep, and launches its host — all before A takes its
    /// snapshot of running processes. A then finds B's freshly launched host running A's own
    /// image and kills it as an orphan. Both runs followed the rules; the interleaving is the
    /// defect.</para>
    /// <para>Holding this gate across <i>register, query and snapshot</i> closes it. Every run
    /// registers under the gate before it may launch a host, so for a host to appear in a
    /// sweeper's snapshot its run must have registered first — and registration is either
    /// before the sweeper's query, in which case the sweeper sees the sibling and stands down,
    /// or after the sweeper released, in which case the host did not yet exist when the
    /// snapshot was taken. There is no third ordering.</para>
    /// <para>Separate from the lease file: a lease is held for the whole run and read by
    /// siblings, whereas this is exclusive and held for milliseconds. One file cannot be both
    /// without a run's own lease blocking every later run's admission.</para>
    /// <para>The two ways this can fail are reported differently on purpose. A
    /// <see langword="null"/> return means one thing only — the gate exists and somebody else
    /// is holding it, which a sharing or lock violation is the only evidence for. Anything
    /// that stops the gate being addressed or opened at all throws
    /// <see cref="GateSetupException"/> instead, because it is not contention, no amount of
    /// waiting resolves it, and reporting it as a timeout sends the reader hunting for a stuck
    /// process that was never there.</para>
    /// </remarks>
    /// <exception cref="GateSetupException">
    /// The claim directory could not be created, or the gate itself could not be opened for
    /// the whole wait for a reason other than another run holding it.
    /// </exception>
    internal static IDisposable? TryEnterStartupGate(string ourExePath, TimeSpan timeout) =>
        TryEnterStartupGate(ourExePath, timeout, ClaimDirectory());

    /// <inheritdoc cref="TryEnterStartupGate(string, TimeSpan)"/>
    /// <param name="claimDirectory">
    /// Directory holding the gate. A parameter only so a test can point this at a path it
    /// controls; the setup-fault path below cannot otherwise be staged, because making the
    /// real <c>%LOCALAPPDATA%</c> unwritable would break the machine rather than the test.
    /// </param>
    internal static IDisposable? TryEnterStartupGate(
        string ourExePath, TimeSpan timeout, string claimDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimDirectory);

        string path;
        try
        {
            var dir = claimDirectory;
            Directory.CreateDirectory(dir);
            path = Path.Join(dir, KeyFor(ourExePath) + ".gate");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GateSetupException(
                $"Could not create or address the claim directory '{claimDirectory}', so this " +
                "run cannot take a startup gate. This is a storage fault, not contention with " +
                "another run.", ex);
        }

        var deadline = DateTime.UtcNow + timeout;
        Exception? lastRefusal = null;
        while (true)
        {
            try
            {
                return new GateHandle(
                    new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
                    path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Polling continues for every failure, because refusal has a genuine transient
                // form: releasing a gate unlinks it, and a third party holding it with delete
                // sharing leaves it delete-pending, which presents as access denied and clears
                // on its own. Only the classification differs — a sharing or lock violation is
                // the one failure that establishes another run holds the gate, so anything
                // else (an ACL denial, a directory occupying the gate's name, a parent that
                // has gone away) is recorded and, if it outlasts the wait, reported as the
                // fault it is rather than as a competitor that was never there.
                //
                // Overwritten rather than kept, so this describes how the wait actually
                // finished: a storage error later replaced by a real contender reads as
                // contention again, and the reverse for the same reason.
                lastRefusal = FileContention.IsHeldByAnother(ex) ? null : ex;

                if (DateTime.UtcNow >= deadline)
                {
                    if (lastRefusal is null) return null;

                    throw new GateSetupException(
                        $"The startup gate '{path}' could not be opened for the whole wait, " +
                        "and not because another run held it. This is a storage fault, not " +
                        "contention: no competing run was ever waited out.", lastRefusal);
                }

                Thread.Sleep(100);
            }
        }
    }

    /// <summary>A held startup gate, which removes its file when released.</summary>
    /// <remarks>
    /// <para>Without the removal a <c>.gate</c> file accumulates per build output and is never
    /// revisited once that worktree is deleted, which for the agent-created worktrees this
    /// change exists to support is unbounded.</para>
    /// <para>Race-safe because the handle shares nothing, <see cref="FileShare.Delete"/>
    /// included: a gate another run has already taken in the window after this one closed its
    /// handle refuses to be unlinked, and the refusal is swallowed. Deleting is safe for the
    /// gate's meaning too — acquisition is <c>OpenOrCreate</c>, so an absent file is simply
    /// created, and Windows will not unlink a name while a handle without delete sharing holds
    /// it, so two runs can never end up gated on different files of the same name.</para>
    /// </remarks>
    private sealed class GateHandle(FileStream stream, string path) : IDisposable
    {
        public void Dispose()
        {
            try { stream.Dispose(); }
            catch (IOException)
            {
                // Process exit closes the handle regardless.
            }

            TryDelete(path);
        }
    }

    /// <summary>
    /// A storage fault that stopped the startup gate being addressed at all.
    /// </summary>
    /// <remarks>
    /// Distinct from the <see langword="null"/> that means contention, because the two need
    /// opposite responses and only one of them is worth waiting out. An unwritable or missing
    /// <c>%LOCALAPPDATA%</c> is not another run holding the gate, so reporting it as a timeout
    /// would name a competing process that does not exist and hide the one fact — the storage
    /// error — that explains the failure and can be acted on.
    /// </remarks>
    internal sealed class GateSetupException(string message, Exception inner)
        : InvalidOperationException(message, inner);

    /// <summary>
    /// Kills every process named <paramref name="processName"/> that runs the image at
    /// <paramref name="ourExePath"/>, and leaves the rest alone.
    /// </summary>
    /// <exception cref="GateSetupException">
    /// The claim directory could not be addressed. Propagated rather than folded into the
    /// timeout below so the message names the storage fault instead of a phantom competitor.
    /// </exception>
    internal static void KillOrphansOf(string processName, string ourExePath, string label)
    {
        // Before anything is keyed off this path. Everything below — the gate, the lease, the
        // sibling probe — is named by KeyFor(ourExePath), and KeyFor is only single-valued for
        // a path the filesystem can resolve. See RequireResolvableImage.
        RequireResolvableImage(ourExePath, label);

        // Held across admission *and* the process snapshot below, which is what makes the two
        // one step. See TryEnterStartupGate for why they cannot be separated.
        using var gate = TryEnterStartupGate(ourExePath, StartupGateTimeout);

        if (gate is null)
        {
            throw new InvalidOperationException(
                $"Could not take the startup gate for this checkout's {label} within " +
                $"{StartupGateTimeout.TotalMinutes:0} minutes, so this run cannot register " +
                "itself as live. Continuing would let a later run mistake this run's host for " +
                "an orphan and kill it mid-test. Check for a stuck test process holding " +
                $"'{KeyFor(ourExePath)}.gate' under %LOCALAPPDATA%\\Microsoft.UI.Reactor\\AppTests.");
        }

        // Under the gate, so exactly one run of this checkout is reclaiming at a time and the
        // work is bounded. Placed before admission because it must happen even on the deferred
        // path: a machine that only ever runs concurrent sessions would otherwise never reclaim.
        PruneAbandonedArtifacts(ClaimDirectory());

        // Path scoping alone separates checkouts, but not two runs of the *same* checkout:
        // their hosts share this exact image path, so without a liveness signal the second
        // run would classify the first run's live host as an orphan and kill it — the very
        // eviction this sweep was narrowed to prevent, reintroduced one scope down.
        //
        // The lease supplies that signal. Being admitted means no other run of this layout is
        // in flight, so anything still running this image is genuinely left over from a run
        // that died. Being deferred means one is, and its hosts are not orphans.
        switch (LayoutRunClaim.TryAcquireFor(ourExePath))
        {
            case SweepAdmission.Unavailable:
                throw new InvalidOperationException(
                    $"Could not record this run's lease for the {label} at '{ourExePath}'. " +
                    "Without it this run is invisible to concurrent runs, and the next one to " +
                    "start would see no live sibling and kill this run's host as an orphan. " +
                    "Check that %LOCALAPPDATA%\\Microsoft.UI.Reactor\\AppTests is writable.");

            case SweepAdmission.Deferred:
                Console.WriteLine(
                    $"Skipping the orphaned-{label} sweep: another run of this checkout is in " +
                    "flight, so its hosts are live rather than orphaned.");
                return;
        }

        var processes = Process.GetProcessesByName(processName);

        var ourSessionId = Process.GetCurrentProcess().SessionId;

        // One snapshot for the whole batch, taken under the gate alongside the process list so
        // both describe the same moment.
        var parents = SnapshotParentPids();

        var candidates = processes
            .Select(p =>
            {
                var pid = SafePid(p);
                return new Candidate(
                    pid, TryGetExecutablePath(p), SafeSessionId(p), LauncherIsLive(pid, parents));
            })
            .ToList();

        var doomed = SelectOurs(candidates, ourExePath, ourSessionId).Select(c => c.Pid).ToHashSet();

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
    internal static bool AnyLiveSiblingOf(string ourExePath) =>
        AnyLiveSiblingOf(ourExePath, ClaimDirectory());

    /// <inheritdoc cref="AnyLiveSiblingOf(string)"/>
    /// <param name="claimDirectory">
    /// Directory holding the leases. A parameter only so a test can point this at a directory
    /// it controls; the fail-closed path below cannot otherwise be staged, because making the
    /// real claim directory unlistable would require editing this account's own ACLs.
    /// </param>
    internal static bool AnyLiveSiblingOf(string ourExePath, string claimDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimDirectory);

        try
        {
            var dir = claimDirectory;

            // Deliberately no Directory.Exists guard. It answers false for an unlistable
            // directory exactly as it does for a missing one, and "missing" here would mean
            // "no siblings", which admits the destructive sweep. By the time this runs the
            // caller has already created its own lease in this directory, so the directory
            // provably exists and any failure to list it is an access or I/O fault. Letting
            // the enumeration throw sends that fault to the catch below, which fails closed.
            var prefix = LeasePrefix(ourExePath);
            var mine = prefix + Environment.ProcessId + ".run";

            foreach (var lease in Directory.EnumerateFiles(dir, prefix + "*.run"))
            {
                if (string.Equals(Path.GetFileName(lease), mine, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Held means its owner's handle is still open, which is a live sibling run.
                // Establishing that is the whole purpose here; the pruning is a side effect.
                if (TryPruneIfStale(lease) == ArtifactState.Held) return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot arbitrate, so cannot safely conclude that anything is an orphan.
            return true;
        }
    }

    /// <summary>What an attempt to reclaim one coordination file established.</summary>
    private enum ArtifactState
    {
        /// <summary>Its owner was gone, and the file has been removed.</summary>
        Pruned,

        /// <summary>Already removed by another run.</summary>
        Vanished,

        /// <summary>Still open, or not reclaimable by this account. Left alone.</summary>
        Held,
    }

    /// <summary>
    /// Removes one coordination file if the run that created it is gone, and reports which.
    /// </summary>
    /// <remarks>
    /// Liveness is an exclusive open, never a recorded pid: the kernel closes the handle when
    /// the owner dies, so a file that still resists opening has a live owner and one that yields
    /// does not. That is also what makes the delete race-safe — every holder opens with
    /// <see cref="FileShare.None"/> or <see cref="FileShare.Read"/>, neither of which includes
    /// <see cref="FileShare.Delete"/>, so a file reacquired between the open and the unlink
    /// refuses to be removed and is reported <see cref="ArtifactState.Held"/> instead.
    /// </remarks>
    private static ArtifactState TryPruneIfStale(string path)
    {
        try
        {
            // Opening denies the holder nothing it still needs, so this is safe to do against
            // a file whose owner died mid-write.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // The open itself is the answer; nothing needs to be read.
            }

            File.Delete(path);
            return ArtifactState.Pruned;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Pruned by another run between enumeration and open, or the whole directory went.
            return ArtifactState.Vanished;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ArtifactState.Held;
        }
    }

    /// <summary>
    /// Removes the leases and gates of runs that are gone, for <em>every</em> build output.
    /// </summary>
    /// <remarks>
    /// <para><see cref="AnyLiveSiblingOf(string, string)"/> only ever prunes files keyed to the
    /// executable it was asked about, so a build output that is never run again — the normal end
    /// of an agent worktree — keeps its files forever. Nothing else revisits them, and the whole
    /// point of per-checkout coordination is that checkouts are numerous and short-lived.</para>
    /// <para>Safe to run against other checkouts' files precisely because staleness is proven by
    /// a handle rather than assumed from a name or a timestamp: a run that still exists keeps
    /// its file, whoever started it. Best-effort throughout — this is housekeeping, and must
    /// never be the reason a test run fails.</para>
    /// </remarks>
    internal static void PruneAbandonedArtifacts(string claimDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimDirectory);

        try
        {
            // Ordered explicitly: Directory.EnumerateFiles guarantees no order, and the
            // regression test for "a held artifact skips rather than stops" depends on the
            // held file being reached before the dead one. Left to the file system, that
            // premise holds only by NTFS index-order accident, so an implementation that
            // stopped at the first refusal could still pass.
            var stale = Directory.EnumerateFiles(claimDirectory, "*.run")
                .Concat(Directory.EnumerateFiles(claimDirectory, "*.gate"))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var path in stale) TryPruneIfStale(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to reclaim if the directory cannot be listed. Unlike the sibling query,
            // no verdict rests on this, so there is nothing to fail closed about.
        }
    }

    /// <summary>Removes a file, ignoring the reasons it might legitimately refuse.</summary>
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Reacquired by another run, or not ours to unlink. Leaving it is harmless: the
            // file carries no state, only the handle does.
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

    /// <summary>Full path of the startup gate for one build output.</summary>
    /// <remarks>
    /// Exposed for the same reason as <see cref="LeasePathFor"/>: the property worth proving
    /// about the gate's cleanup is that the file is gone afterwards, and that is a statement
    /// about a path rather than about the handle a test already holds.
    /// </remarks>
    internal static string GatePathFor(string ourExePath) =>
        Path.Join(ClaimDirectory(), KeyFor(ourExePath) + ".gate");

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

        private static readonly Dictionary<string, (string ExePath, IDisposable Lease)> Held =
            new(StringComparer.Ordinal);

        internal static SweepAdmission TryAcquireFor(string ourExePath)
        {
            // Keyed by the same digest the lease file is named for, so two spellings of one
            // path cannot disagree about whether this run already registered.
            var key = KeyFor(ourExePath);

            lock (Gate)
            {
                if (!Held.ContainsKey(key))
                {
                    var lease = TryClaimRun(ourExePath);

                    // Not "a sibling is live" — nothing was learned about siblings. The run has
                    // no lease, which is a different and worse problem, and it is reported as
                    // itself so the caller can refuse to continue rather than proceed unseen.
                    if (lease is null) return SweepAdmission.Unavailable;

                    Held[key] = (ourExePath, lease);
                }

                // Registered before the question is asked, so a sibling starting concurrently
                // sees this run and declines in turn. Asking first would let both conclude they
                // were alone and both sweep.
                return AnyLiveSiblingOf(ourExePath)
                    ? SweepAdmission.Deferred
                    : SweepAdmission.Admitted;
            }
        }

        /// <summary>
        /// Drops the leases taken for executables under <paramref name="root"/> and removes
        /// their files. Tests only.
        /// </summary>
        /// <remarks>
        /// <para>A real run never calls this: the lease must cover the whole run, and process
        /// exit is exactly that lifetime. Tests are the one caller for which that is wrong.
        /// Each synthetic executable path they exercise takes a real lease under the shared
        /// per-user claim directory, and because the path is unique per run no later run ever
        /// revisits the file — so without this the suite deposits a permanently abandoned
        /// <c>.run</c> file per claim test, in the production directory, every time it runs.</para>
        /// <para>Scoped to a root rather than releasing everything, and that is the whole
        /// design. This assembly's E2E tests hold a lease for the real host in this same
        /// process, and releasing it mid-run would let a concurrent run of another checkout
        /// conclude that nothing is live and sweep this run's host — reintroducing the exact
        /// failure the lease exists to prevent, from the cleanup meant to be harmless.</para>
        /// <para>Removal is part of the contract, not a nicety. Disposing the stream only
        /// closes the handle; the file survives and would still be enumerated by every future
        /// sibling probe, which is the accumulation this exists to prevent.</para>
        /// </remarks>
        internal static void ReleaseForTestsUnder(string root)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);

            lock (Gate)
            {
                var matching = Held
                    .Where(e => e.Value.ExePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var (key, entry) in matching)
                {
                    var path = (entry.Lease as FileStream)?.Name;
                    entry.Lease.Dispose();
                    Held.Remove(key);

                    if (path is null) continue;

                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException)
                    {
                        // Best effort: a file that cannot be removed is left for the stale
                        // pruner, which is the same fate as a crashed run's lease.
                    }
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

    /// <summary>
    /// Throws unless the filesystem can resolve <paramref name="ourExePath"/>, which is the
    /// precondition for this run's coordination key being the same one every other run of this
    /// image derives.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NormalizePath"/> falls back to the caller's own spelling when the
    /// filesystem cannot answer. For <see cref="IsSameImage"/> that is merely weaker, and
    /// weaker in the safe direction: an unmatched candidate is a skipped kill. For
    /// <see cref="KeyFor"/> it is not weaker, it is <em>unsound</em> — the fallback makes the
    /// key a function of how the caller happened to spell the path. Two runs of one build
    /// output, one resolving and one falling back, take different gate and lease files, so each
    /// enumerates no sibling, each is admitted, and each kills the other's live host. That is
    /// the cross-checkout kill this class exists to prevent, arrived at through the mechanism
    /// meant to prevent it.</para>
    /// <para>So the destructive path demands the strong answer rather than accepting the weak
    /// one. This costs nothing in practice: the argument is the host executable this run is
    /// about to launch and keep running, so it exists and resolves. Refusing is also the
    /// conservative half of the trade — the alternative is a run that sweeps while unable to
    /// agree with anyone about what it is sweeping. It matches how an unwritable lease is
    /// already treated a few lines below, and for the same reason: a run that cannot be
    /// coordinated with must not perform an irreversible action.</para>
    /// <para>Deliberately not folded into <see cref="KeyFor"/>. Naming a gate or lease file for
    /// a path that does not exist is perfectly well defined and is what the headless tests do;
    /// it is only <em>sweeping</em> on such a key that is unsafe, so the demand belongs at the
    /// destructive entry point.</para>
    /// </remarks>
    internal static void RequireResolvableImage(string ourExePath, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExePath);

        string? resolved = null;
        try
        {
            resolved = FinalPath.TryResolve(Path.GetFullPath(ourExePath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // Fall through to the throw below with the same message: why the filesystem could
            // not answer does not change what this run is allowed to do.
        }

        if (resolved is not null) return;

        throw new InvalidOperationException(
            $"Could not resolve this checkout's {label} at '{ourExePath}' through the " +
            "filesystem, so the key naming this run's startup gate and liveness lease would " +
            "fall back to this caller's spelling of the path. Another run reaching the same " +
            "build output by a different spelling would derive a different key, see no live " +
            "sibling, and kill this run's host mid-test. Refusing to sweep instead. Check that " +
            "the path exists and is readable by this user.");
    }

    /// <summary>
    /// Whether the process that started <paramref name="pid"/> is still running.
    /// </summary>
    /// <remarks>
    /// <para>Defaults to <see langword="true"/> — "attended" — on every uncertainty, because
    /// this value only ever gates a kill. An unreadable parent is a reason not to act, not a
    /// licence to; the cost of a false "attended" is one stale process that the next run will
    /// reconsider, while the cost of a false "orphan" is terminating a live suite.</para>
    /// <para><b>Guards against pid reuse, which is what makes a parent id alone unusable.</b>
    /// A recorded parent id outlives the parent, and Windows reissues ids freely, so an orphan
    /// whose launcher died can name an id that now belongs to something unrelated and would
    /// read back as live. A real parent necessarily started before its child, so a candidate
    /// parent that started <em>after</em> the process it supposedly launched is a reused id and
    /// is reported as gone. Equal timestamps are treated as the parent being live, keeping the
    /// tie on the conservative side of a clock with coarse resolution.</para>
    /// <para>Reads the parent id from a Toolhelp snapshot rather than per-process
    /// <c>NtQueryInformationProcess</c>: one snapshot answers for every candidate at once and
    /// needs no handle to a process this run may not be able to open.</para>
    /// </remarks>
    internal static bool LauncherIsLive(int pid, IReadOnlyDictionary<int, int> parents)
    {
        if (pid <= 0 || !parents.TryGetValue(pid, out var parentPid) || parentPid <= 0)
            return true;

        try
        {
            using var child = Process.GetProcessById(pid);
            using var parent = Process.GetProcessById(parentPid);

            return parent.StartTime <= child.StartTime;
        }
        catch (ArgumentException)
        {
            // The parent id names nothing running: the launcher is gone, which is exactly the
            // orphan case. A vanished *child* lands here too and is harmless — it is already
            // dead, so reporting it sweepable kills nothing.
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Every running process's parent id, or an empty map when the snapshot cannot be taken.
    /// </summary>
    /// <remarks>
    /// An empty map makes <see cref="LauncherIsLive"/> answer "attended" for everything, so a
    /// snapshot failure disables the sweep rather than broadening it. That is the same
    /// fail-closed direction every other unreadable fact here takes.
    /// </remarks>
    internal static IReadOnlyDictionary<int, int> SnapshotParentPids()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (snapshot == IntPtr.Zero || snapshot == InvalidHandleValue)
            return map;

        try
        {
            var entry = new ProcessEntry32 { dwSize = Marshal.SizeOf<ProcessEntry32>() };

            if (!Process32First(snapshot, ref entry))
                return map;

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry32
    {
        public int dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        // Inline rather than a marshalled string so the struct stays blittable, which is what
        // lets the LibraryImport generator pass it by reference with no marshalling stub. The
        // image name is not read here; only its size contributes to dwSize.
        public fixed char szExeFile[260];
    }

    // ExactSpelling, so the Unicode exports are named explicitly: LibraryImport does not append
    // the W suffix the way the old DllImport CharSet behaviour did.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    private static int SafePid(Process proc)
    {
        try { return proc.Id; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The terminal-services session a process belongs to, or <see langword="null"/> when it
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// Null rather than a sentinel number, so an unreadable session can never accidentally
    /// equal a real one. <see cref="SelectOurs"/> leaves such a candidate alone, which is the
    /// same fail-closed treatment an unreadable path gets and for the same reason: our own
    /// orphans read back fine, because this process launched them.
    /// </remarks>
    private static int? SafeSessionId(Process proc)
    {
        try { return proc.SessionId; }
        catch (Exception ex) when (
            ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
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

    /// <summary>
    /// The path one executable is agreed to have, whichever spelling a run reached it by.
    /// </summary>
    /// <remarks>
    /// <para>Both users of this need the same answer for the same physical file or the whole
    /// scheme inverts. <see cref="KeyFor"/> names the lease and gate files, and
    /// <see cref="IsSameImage"/> decides whether a live process is a sibling; if one build
    /// output has two spellings, two runs take different lease files, each enumerates no
    /// sibling, and each then sweeps the other's running host — the exact cross-checkout kill
    /// this class exists to stop, reintroduced one level down.</para>
    /// <para><c>Path.GetFullPath</c> alone is not enough, because it is pure string handling:
    /// measured on Windows it does expand 8.3 short components of an existing path, but it
    /// leaves a <c>subst</c>'d or mapped drive letter pointing at the mapping and preserves the
    /// extended-length <c>\\?\</c> spelling verbatim. A junction anywhere above the build
    /// output — a linked <c>C:\src</c>, say — is the realistic way one host acquires two
    /// spellings, and it too survives string normalisation. So the filesystem is asked.</para>
    /// <para>Falls back to the textual form when the query cannot be answered, which is the
    /// case that matters for a candidate process whose image has since been deleted. For
    /// <see cref="IsSameImage"/> that is strictly weaker but never wrong in the dangerous
    /// direction: an unresolved path can make this fail to recognise a sibling it should have
    /// matched, and failing to match only ever costs a skipped sweep, never an extra kill.</para>
    /// <para><b>The same fallback is not safe for <see cref="KeyFor"/>,</b> where it would make
    /// the key depend on the caller's spelling rather than on the file, and two runs that
    /// disagree about the key sweep each other. It is kept here rather than split because the
    /// candidate side genuinely needs it, and the destructive path instead refuses to run at
    /// all unless our own image resolves — see <see cref="RequireResolvableImage"/>.</para>
    /// </remarks>
    private static string NormalizePath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return FinalPath.TryResolve(full) ?? full;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }
}
