using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Headless tests for the startup sweep that removes hosts left behind by a previous run.
/// </summary>
/// <remarks>
/// <para>The regression these pin is the one that makes every other part of the concurrency
/// work moot: the sweep used to match on process name alone, so a suite starting in one
/// checkout killed the live host of a suite already running in another. Stamping a winapp UI
/// workflow id arbitrates the desktop between two runs, but arbitration is worthless if one
/// run terminates the other run's process before the first command is even issued.</para>
/// <para>Deliberately not derived from <c>AppTestBase</c>: nothing here launches a host or
/// touches the desktop, so it must not take a UI turn.</para>
/// </remarks>
[TestClass]
public class OrphanedHostSweepTests
{
    private static string Ours => Path.Join(Path.GetTempPath(), "checkout-a", "Reactor.AppTests.Host.exe");
    private static string Theirs => Path.Join(Path.GetTempPath(), "checkout-b", "Reactor.AppTests.Host.exe");

    /// <summary>
    /// Root for synthetic executable paths used by the claim tests, unique to this run.
    /// </summary>
    /// <remarks>
    /// Claims are backed by per-user files keyed from the executable path, so a fixed path
    /// would make two concurrent copies of this suite contend for one claim and fail each
    /// other — the exact cross-checkout interference this class exists to prevent. Tests that
    /// want contention create it deliberately by reusing one path within the test.
    /// </remarks>
    private static readonly string ClaimRoot =
        Path.Join(Path.GetTempPath(), "reactor-claim-" + Guid.NewGuid().ToString("n"));

    private static string Probe(string name) =>
        Path.Join(ClaimRoot, name, "Reactor.AppTests.Host.exe");

    private static OrphanedHostSweep.Candidate At(int pid, string? path) => new(pid, path);

    /// <summary>
    /// The cleanup seam has to actually remove the file, and has to leave every claim outside
    /// its root alone.
    /// </summary>
    /// <remarks>
    /// Both halves matter, and the second is the dangerous one. A lease is deliberately never
    /// released by a real run, so a cleanup that released everything would drop the lease this
    /// same process holds for the real host while the E2E tests are still running — and a
    /// concurrent run in another checkout would then see nothing live and sweep this run's
    /// host, which is precisely the failure the lease exists to prevent. Deleting the file is
    /// the other half: closing the handle leaves the <c>.run</c> behind, and every future
    /// sibling probe would still enumerate it.
    /// </remarks>
    [TestMethod]
    public void Releasing_Staged_Claims_Removes_Only_That_Roots_Files()
    {
        var mineRoot = Path.Join(ClaimRoot, "release-" + Guid.NewGuid().ToString("n"));
        var mine = Path.Join(mineRoot, "Reactor.AppTests.Host.exe");
        var theirs = Probe("release-bystander-" + Guid.NewGuid().ToString("n"));

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(mine),
            "Precondition: the claim under test has to be taken before it can be released.");
        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(theirs),
            "Precondition: the bystander claim has to be taken too. Admitted rather than " +
            "Deferred because siblings are scoped to one build output, and this is a " +
            "different path — the claim above is not a sibling of it.");

        var minePath = OrphanedHostSweep.LeasePathFor(mine, Environment.ProcessId);
        var theirsPath = OrphanedHostSweep.LeasePathFor(theirs, Environment.ProcessId);

        Assert.IsTrue(File.Exists(minePath), "Precondition: taking a claim writes its lease file.");
        Assert.IsTrue(File.Exists(theirsPath), "Precondition: the bystander's lease file exists.");

        OrphanedHostSweep.LayoutRunClaim.ReleaseForTestsUnder(mineRoot);

        Assert.IsFalse(File.Exists(minePath),
            "The released claim's lease file must be removed, not merely closed — a file left " +
            "behind is still enumerated as a sibling by every later run.");
        Assert.IsTrue(File.Exists(theirsPath),
            "A claim outside the released root must survive. Releasing everything would drop " +
            "the live host's own lease mid-run and expose it to a concurrent checkout's sweep.");
    }

    /// <summary>
    /// A staged lease has to be recorded so class cleanup can remove it, because nothing else
    /// will.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing and neither is visible from a <c>[ClassCleanup]</c>, so
    /// they are pinned here. A staged lease is not a claim this process took, so the release
    /// seam cannot see it; disposing deliberately leaves the file behind, because that is what
    /// a process exit does and the prune test depends on it. Together that means an unrecorded
    /// staged lease is never deleted by anything, and every run of this suite would add another
    /// permanent file to the real per-user claim directory.
    /// </remarks>
    [TestMethod]
    public void A_Staged_Lease_Is_Recorded_And_Removed_By_Cleanup()
    {
        var exe = Probe("staged-cleanup-" + Guid.NewGuid().ToString("n"));
        var path = OrphanedHostSweep.LeasePathFor(exe, ForeignPid);

        StageForeignLease(exe).Dispose();

        Assert.IsTrue(File.Exists(path),
            "Precondition: disposing a staged lease closes the handle and leaves the file, the " +
            "way a process exit does.");
        CollectionAssert.Contains(StagedLeases, path,
            "An unrecorded staged lease is unreachable by every cleanup path there is, so it " +
            "stays in the real claim directory forever.");

        DeleteStagedLeases();

        Assert.IsFalse(File.Exists(path),
            "Cleanup left a staged lease behind, so each run of this suite leaks another file " +
            "into the per-user claim directory that later runs then pay to probe.");
    }

    /// <summary>
    /// Releases the leases these tests took and removes everything they staged.
    /// </summary>
    /// <remarks>
    /// <para>Claims are recorded as real files under the shared per-user claim directory, and a
    /// run deliberately never releases its own — the lease is meant to last the whole process.
    /// That is right for a run and wrong for a suite: every path here is unique to this
    /// execution, so nothing ever revisits those files and each run of this class would
    /// otherwise leave a permanent <c>.run</c> file behind in a production directory. The
    /// release is scoped to this class's synthetic root so the lease the E2E tests hold for the
    /// real host, in this same process, is left untouched.</para>
    /// <para>Staged foreign leases need the second step. They are not claims this process took,
    /// so they are absent from <c>LayoutRunClaim.Held</c> and the release seam cannot reach
    /// them; only the tests that deliberately provoke a prune clean up after themselves.</para>
    /// </remarks>
    [ClassCleanup]
    public static void ReleaseStagedClaims()
    {
        OrphanedHostSweep.LayoutRunClaim.ReleaseForTestsUnder(ClaimRoot);
        DeleteStagedLeases();

        try
        {
            if (Directory.Exists(ClaimRoot)) Directory.Delete(ClaimRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Staging lives under the temp directory; a file still held here is the OS's to
            // collect, and failing cleanup would turn a passing suite red for nothing.
        }
    }

    /// <summary>
    /// The regression itself. Two checkouts build a host with the same file name, so process
    /// name cannot distinguish them; only the image path can.
    /// </summary>
    [TestMethod]
    public void A_Host_From_Another_Checkout_Is_Not_Swept()
    {
        var selected = OrphanedHostSweep
            .SelectOurs([At(100, Ours), At(200, Theirs)], Ours)
            .Select(c => c.Pid)
            .ToList();

        CollectionAssert.AreEqual(new[] { 100 }, selected,
            "Only the host running this checkout's build may be killed.");
    }

    /// <summary>
    /// The sweep still has to do its job: a leftover host from a previous run of this same
    /// build holds the window title the next run binds to, so leaving it would trade one
    /// flake for another.
    /// </summary>
    [TestMethod]
    public void Our_Own_Orphan_Is_Still_Swept()
    {
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, Ours)], Ours).Count());
    }

    /// <summary>
    /// Path comparison must survive the spellings the two sides actually produce: the launch
    /// path is composed by the test host, while the candidate path is whatever Windows reports
    /// for a running image.
    /// </summary>
    [TestMethod]
    public void Spelling_Differences_In_One_Path_Still_Match()
    {
        var unnormalized = Path.Join(Path.GetTempPath(), "checkout-a", ".", "Reactor.AppTests.Host.exe");
        var cased = Ours.ToUpperInvariant();

        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, unnormalized)], Ours).Count(),
            "A redundant path segment names the same image.");
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, cased)], Ours).Count(),
            "Windows paths are case-insensitive.");
    }

    /// <summary>
    /// Fails closed. A candidate whose path could not be read is left alone, because killing
    /// on "don't know" is exactly the machine-wide behaviour this replaced.
    /// </summary>
    [TestMethod]
    public void A_Candidate_With_An_Unreadable_Path_Is_Left_Alone()
    {
        Assert.AreEqual(0, OrphanedHostSweep.SelectOurs([At(100, null), At(200, "  ")], Ours).Count());
    }

    /// <summary>
    /// Guards the argument that would silently turn the sweep back into a match-everything
    /// sweep if it were ever allowed through.
    /// </summary>
    [TestMethod]
    public void An_Empty_Executable_Path_Is_Rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OrphanedHostSweep.SelectOurs([At(100, Ours)], "  ").ToList());
    }
    /// <summary>
    /// Path scoping alone does not separate two runs of the <em>same</em> checkout: their hosts
    /// share the image path exactly. Leases supply liveness, and this is the differential that
    /// proves it — while another run's lease is held, this run must decline to sweep, or it
    /// would classify that run's live host as an orphan.
    /// </summary>
    /// <remarks>
    /// This is the sequence a single-owner claim got wrong. Run A claimed and launched a host;
    /// run B was refused the claim, skipped the sweep, and launched a host anyway; when A exited
    /// its claim was freed, so run C acquired it, swept, and killed B's live host. Every run
    /// being individually visible is what removes that window.
    /// </remarks>
    [TestMethod]
    public void A_Live_Sibling_Run_Blocks_The_Sweep()
    {
        var exe = Probe("one");

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
            "Control: with no sibling live, this run must be allowed to sweep.");

        using (StageForeignLease(exe))
        {
            Assert.AreEqual(Deferred, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
                "A run swept while a sibling run of the same build output was still live, " +
                "which kills that run's host mid-suite.");
        }

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
            "Once the sibling is gone the sweep must resume, or one crashed run would wedge " +
            "every later run out of cleaning up.");
    }

    /// <summary>
    /// The refusal must be scoped to the build output, not global: a run in another checkout
    /// has to keep sweeping its own leftovers.
    /// </summary>
    [TestMethod]
    public void A_Run_Of_A_Different_Build_Output_Sweeps_Independently()
    {
        var mine = Probe("a");
        var theirs = Probe("b");

        using var foreign = StageForeignLease(mine);

        Assert.AreEqual(Deferred, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(mine),
            "Precondition: the staged lease must block its own build output.");

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(theirs),
            "A different build output is a different lease; blocking it would stop other " +
            "checkouts from ever cleaning up after themselves.");
    }

    /// <summary>
    /// A lease whose owner died must stop counting, or a single crashed run would wedge every
    /// later run of that build output out of sweeping forever.
    /// </summary>
    [TestMethod]
    public void A_Stale_Lease_Is_Pruned_And_Does_Not_Block()
    {
        var exe = Probe("release");
        var stale = OrphanedHostSweep.LeasePathFor(exe, ForeignPid);

        var lease = StageForeignLease(exe);
        Assert.AreEqual(Deferred, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
            "Precondition: a held lease must block.");

        // A process exit closes the handle exactly this way, leaving the file behind.
        lease.Dispose();

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
            "A lease left behind by a dead run kept blocking the sweep.");

        Assert.IsFalse(File.Exists(stale),
            "The stale lease must be pruned, otherwise every later run pays to re-test it and " +
            "the directory grows without bound.");
    }

    /// <summary>An unnamed build output cannot be arbitrated, so it must not be guessed at.</summary>
    [TestMethod]
    public void Claiming_Without_An_Executable_Path_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrphanedHostSweep.TryClaimRun("  "));
        Assert.ThrowsExactly<ArgumentException>(() => OrphanedHostSweep.AnyLiveSiblingOf("  "));
    }

    /// <summary>
    /// A claim directory that cannot be listed must read as "a sibling may be live", never as
    /// "no siblings".
    /// </summary>
    /// <remarks>
    /// <para>The answer this returns is what licenses killing processes. Every unlistable
    /// directory — an ACL this account is excluded from, a disconnected redirected
    /// <c>%LOCALAPPDATA%</c>, a handle limit — has to fail closed, because the alternative is a
    /// sweep that kills another agent's running host precisely when it could not check whether
    /// one existed.</para>
    /// <para>Staged by pointing the claim directory at a path occupied by a <em>file</em>.
    /// Windows rejects listing it with an I/O error, which is the same channel a permission or
    /// device fault arrives through, and unlike an ACL change it needs no privilege and leaves
    /// nothing behind. That shape is also exactly what a <c>Directory.Exists</c> pre-check
    /// mistakes for "missing": the guard this replaced returned "no siblings" here.</para>
    /// </remarks>
    [TestMethod]
    public void An_Unlistable_Claim_Directory_Reports_A_Possible_Live_Sibling()
    {
        var notADirectory = Path.Join(ClaimRoot, "occupied-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(ClaimRoot);
        File.WriteAllText(notADirectory, "not a directory");

        try
        {
            Assert.IsTrue(
                OrphanedHostSweep.AnyLiveSiblingOf(Probe("unlistable"), notADirectory),
                "A claim directory that could not be listed was reported as holding no live " +
                "siblings, which licenses the sweep to kill hosts it never managed to check " +
                "for.");
        }
        finally
        {
            File.Delete(notADirectory);
        }
    }

    /// <summary>
    /// Discriminator for the test above: a listable, genuinely empty claim directory must still
    /// report no siblings.
    /// </summary>
    /// <remarks>
    /// Without this, "fail closed" could be satisfied by returning <see langword="true"/>
    /// unconditionally — which would disable the sweep entirely and reintroduce the stale-host
    /// flake it exists to fix. The pair is what pins the behaviour to the fault, not to the
    /// verdict.
    /// </remarks>
    [TestMethod]
    public void An_Empty_Claim_Directory_Reports_No_Live_Sibling()
    {
        var empty = Path.Join(ClaimRoot, "empty-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(empty);

        Assert.IsFalse(OrphanedHostSweep.AnyLiveSiblingOf(Probe("listable"), empty));
    }

    /// <summary>
    /// This assembly sweeps two different hosts, so leases must be tracked per executable. A
    /// single "already registered something" flag makes the first host vouch for the second
    /// without ever consulting its lease, leaving that host sweepable while a sibling run is
    /// still using it.
    /// </summary>
    [TestMethod]
    public void Claiming_One_Host_Does_Not_Vouch_For_Another()
    {
        var appHost = Probe("multi-app");
        var winFormsHost = Path.Join(ClaimRoot, "multi-winforms", "Reactor.WinFormsTests.Host.exe");

        using var foreign = StageForeignLease(winFormsHost);

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(appHost),
            "Precondition: the first host has no live sibling and must be sweepable.");

        Assert.AreEqual(Deferred, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(winFormsHost),
            "The second host was reported sweepable on the strength of the first host's " +
            "lease, so a concurrent run's WinForms host stays exposed.");
    }

    /// <summary>
    /// Re-acquiring a path this process already holds must succeed. Both sweeps run per
    /// session, and a self-refusal would silently disable the second one.
    /// </summary>
    [TestMethod]
    public void Re_Acquiring_Our_Own_Claim_Succeeds()
    {
        var exe = Probe("reentrant");

        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe));
        Assert.AreEqual(Admitted, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe),
            "A process must not lock itself out of its own lease.");
    }

    /// <summary>
    /// The lease key must agree with the kill predicate about what counts as one image, or two
    /// spellings of one path become two leases and each run sweeps the other's live host.
    /// </summary>
    [TestMethod]
    public void Two_Spellings_Of_One_Path_Claim_The_Same_Build_Output()
    {
        var plain = Probe("spelling");
        var viaDot = Path.Join(ClaimRoot, "spelling", ".", "Reactor.AppTests.Host.exe");

        // Precondition: the kill predicate already treats these as the same image, which is
        // what makes disagreeing about them a defect rather than a preference.
        Assert.AreEqual(
            1,
            OrphanedHostSweep.SelectOurs([new OrphanedHostSweep.Candidate(1, viaDot)], plain).Count(),
            "Precondition: the sweep must already consider these one image.");

        using var foreign = StageForeignLease(plain);

        Assert.AreEqual(Deferred, OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(viaDot),
            "The two spellings took different lease files, so both runs would conclude no " +
            "sibling was live and each would sweep the other's host.");
    }

    /// <summary>
    /// Admission and sweeping have to be indivisible. Without a gate, run A can be admitted,
    /// run B can register, see A, stand down and launch its host, and A can then snapshot
    /// processes and kill that brand-new host as an orphan — every step individually correct.
    /// </summary>
    /// <remarks>
    /// Tested on the gate itself rather than through <c>KillOrphansOf</c>, which would have to
    /// launch real host processes to observe the race. Windows applies share modes per handle
    /// even within one process, so a second open here is refused exactly as another run's would
    /// be, and mutual exclusion is the whole property being asserted.
    /// </remarks>
    [TestMethod]
    public void The_Startup_Gate_Admits_One_Run_At_A_Time()
    {
        var exe = Probe("gate");
        var instant = TimeSpan.Zero;

        using (var first = OrphanedHostSweep.TryEnterStartupGate(exe, instant))
        {
            Assert.IsNotNull(first, "Control: an uncontended gate must be enterable.");

            Assert.IsNull(
                OrphanedHostSweep.TryEnterStartupGate(exe, instant),
                "A second run entered the gate while the first still held it, so its lease " +
                "registration and sweep can interleave with the first run's.");
        }

        using var reentered = OrphanedHostSweep.TryEnterStartupGate(exe, instant);
        Assert.IsNotNull(reentered,
            "The gate stayed held after release, which would wedge every later run.");
    }

    /// <summary>
    /// The gate is per build output. A machine-wide gate would serialize unrelated checkouts,
    /// and worse, a run that crashed holding it would stall every other checkout's startup.
    /// </summary>
    [TestMethod]
    public void The_Startup_Gate_Is_Scoped_To_One_Build_Output()
    {
        var mine = Probe("gate-a");
        var theirs = Probe("gate-b");
        var instant = TimeSpan.Zero;

        using var held = OrphanedHostSweep.TryEnterStartupGate(mine, instant);
        Assert.IsNotNull(held, "Precondition: the first gate must be enterable.");

        using var other = OrphanedHostSweep.TryEnterStartupGate(theirs, instant);
        Assert.IsNotNull(other,
            "A different build output was blocked by this one's gate, which serializes " +
            "checkouts that share nothing.");
    }

    /// <summary>
    /// Releasing the gate removes its file. Every run of every checkout takes one, and a
    /// worktree that is deleted is never run again, so a gate left behind is permanent litter
    /// in a directory whose whole purpose is to be shared by short-lived checkouts.
    /// </summary>
    [TestMethod]
    public void Releasing_The_Startup_Gate_Removes_Its_File()
    {
        var exe = Probe("gate-cleanup");
        var path = OrphanedHostSweep.GatePathFor(exe);

        using (var gate = OrphanedHostSweep.TryEnterStartupGate(exe, TimeSpan.Zero))
        {
            Assert.IsNotNull(gate, "Precondition: the gate must be enterable.");
            Assert.IsTrue(File.Exists(path),
                "Control: the gate must exist while held, or its absence afterwards proves " +
                "nothing about cleanup.");
        }

        Assert.IsFalse(File.Exists(path),
            $"'{path}' survived its gate being released, so every run of every checkout that " +
            "ever takes a gate leaves one behind for good.");
    }

    /// <summary>
    /// Reclamation covers other build outputs' leftovers, not just this one's. The sibling
    /// query only ever looks at files keyed to the executable it was asked about, so a
    /// checkout that is deleted after its last run keeps its files forever.
    /// </summary>
    /// <remarks>
    /// Staged against a synthetic directory rather than the real one: this asserts that files
    /// disappear, and the real directory is shared with any concurrent run of this same suite.
    /// </remarks>
    [TestMethod]
    public void Reclamation_Removes_Another_Build_Outputs_Leftovers()
    {
        var dir = Path.Join(ClaimRoot, "prune-foreign");
        Directory.CreateDirectory(dir);

        var lease = Path.Join(dir, "0123456789abcdef.4242.run");
        var gate = Path.Join(dir, "fedcba9876543210.gate");
        File.WriteAllText(lease, "pid=4242");
        File.WriteAllText(gate, string.Empty);

        OrphanedHostSweep.PruneAbandonedArtifacts(dir);

        Assert.IsFalse(File.Exists(lease),
            "A dead run's lease from another build output was left behind, so the claim " +
            "directory grows without bound as worktrees come and go.");
        Assert.IsFalse(File.Exists(gate),
            "A dead run's gate from another build output was left behind.");
    }

    /// <summary>
    /// A live run's files stop reclamation from touching them, but not from continuing. If one
    /// held file ended the pass, a single long-running suite would suppress reclamation for the
    /// whole machine and the unbounded growth would come straight back.
    /// </summary>
    /// <remarks>
    /// The load-bearing assertion is the second one. That the held file itself survives is
    /// guaranteed by Windows — no handle opened without <c>FileShare.Delete</c> can be unlinked
    /// — so it is a precondition here rather than a property of this code. What this code has
    /// to get right is that it treats a refusal as "skip this one", not "stop".
    /// </remarks>
    [TestMethod]
    public void A_Live_Runs_Files_Do_Not_Stop_Reclamation()
    {
        var dir = Path.Join(ClaimRoot, "prune-live");
        Directory.CreateDirectory(dir);

        // Enumerated in name order by the sweep itself, so the held file is reached first and a
        // pass that stops at the first refusal never reaches the dead one. That ordering is a
        // property of the production code, not of the file system — Directory.EnumerateFiles
        // promises no order, and without the explicit sort this staging would be an accident.
        var held = Path.Join(dir, "0123456789abcdef.4242.run");
        var dead = Path.Join(dir, "0123456789abcdef.4243.run");
        File.WriteAllText(dead, "pid=4243");

        // The same share mode a real lease is taken with, so the open test sees exactly what it
        // would against another process's handle.
        using (new FileStream(held, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
        {
            OrphanedHostSweep.PruneAbandonedArtifacts(dir);

            Assert.IsTrue(File.Exists(held),
                "Precondition: a held lease must survive, or the assertion below is measuring " +
                "a pass that had nothing to skip.");
            Assert.IsFalse(File.Exists(dead),
                "A dead run's lease was left behind because a live run's lease came before it " +
                "in the directory, so one long-running suite suppresses reclamation entirely.");
        }
    }

    /// <summary>
    /// Reclamation is housekeeping and must never fail a run. A claim directory that cannot be
    /// listed is a reason to reclaim nothing, not a reason to throw on the way into the sweep.
    /// </summary>
    [TestMethod]
    public void Reclamation_Tolerates_An_Unusable_Directory()
    {
        var notADirectory = Path.Join(ClaimRoot, "prune-blocked");
        Directory.CreateDirectory(ClaimRoot);
        File.WriteAllText(notADirectory, "occupied");

        OrphanedHostSweep.PruneAbandonedArtifacts(notADirectory);
        OrphanedHostSweep.PruneAbandonedArtifacts(Path.Join(ClaimRoot, "never-created"));
    }

    /// <summary>
    /// A blank directory is a caller error, not something to silently skip.
    /// </summary>
    [TestMethod]
    public void Reclamation_Rejects_A_Blank_Directory()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OrphanedHostSweep.PruneAbandonedArtifacts("  "));
    }

    /// <summary>
    /// A run that cannot record its lease must be told so, not told that a sibling is live.
    /// The two call for opposite actions and a boolean forced them to share one answer.
    /// </summary>
    /// <remarks>
    /// <para>Reporting an I/O failure as "a sibling is live" looks like the safe direction: it
    /// skips the sweep, and skipping never kills anything. But it also leaves the run with no
    /// lease, so it is invisible to every later run — and the first one to start once the fault
    /// clears sees no sibling, is admitted, and kills this run's live host.</para>
    /// <para>Staged by putting a <em>directory</em> where the lease file goes, which is what
    /// <c>FileStream</c> refuses with <c>UnauthorizedAccessException</c>. The mechanism does not
    /// matter to the caller; being able to distinguish the outcome does.</para>
    /// </remarks>
    [TestMethod]
    public void A_Lease_That_Cannot_Be_Recorded_Is_Not_Reported_As_A_Live_Sibling()
    {
        var exe = Probe("unwritable");
        var lease = OrphanedHostSweep.LeasePathFor(exe, Environment.ProcessId);

        Directory.CreateDirectory(Path.GetDirectoryName(lease)!);
        Directory.CreateDirectory(lease);

        try
        {
            var admission = OrphanedHostSweep.LayoutRunClaim.TryAcquireFor(exe);

            Assert.AreNotEqual(Deferred, admission,
                "An unrecordable lease was reported as a live sibling. The run then continues " +
                "with no lease of its own, and the next run to start sees nothing and kills " +
                "this run's host as an orphan.");

            Assert.AreEqual(OrphanedHostSweep.SweepAdmission.Unavailable, admission,
                "A run that could not record its lease must be able to refuse to continue.");
        }
        finally
        {
            Directory.Delete(lease, recursive: true);
        }
    }

    /// <summary>Admitted, for readability at the assertion sites.</summary>
    private const OrphanedHostSweep.SweepAdmission Admitted =
        OrphanedHostSweep.SweepAdmission.Admitted;

    /// <summary>Deferred, for readability at the assertion sites.</summary>
    private const OrphanedHostSweep.SweepAdmission Deferred =
        OrphanedHostSweep.SweepAdmission.Deferred;

    /// <summary>Process id used for staged leases; outside the range Windows assigns.</summary>
    private const int ForeignPid = 999999;

    /// <summary>
    /// Stages the lease another live run of <paramref name="exePath"/> would hold.
    /// </summary>
    /// <remarks>
    /// <para>Opened with the same share mode the production path uses, because that share mode
    /// is the whole signal: liveness is "this handle still refuses an exclusive open", so a
    /// stand-in that opened it any other way would answer a different question than the sweep
    /// asks.</para>
    /// <para>The path is recorded for class cleanup rather than deleted on dispose. Disposing
    /// has to leave the file behind — that is exactly what a process exit does, and
    /// <c>A_Stale_Lease_Is_Pruned_And_Does_Not_Block</c> asserts the sweep prunes what is left
    /// — but a staged lease is not a claim this process took, so it is absent from
    /// <c>LayoutRunClaim.Held</c> and the release seam cannot see it. Without this record the
    /// tests that never trigger a prune would each leave a permanent <c>.run</c> file in the
    /// real per-user claim directory.</para>
    /// </remarks>
    private static FileStream StageForeignLease(string exePath)
    {
        var path = OrphanedHostSweep.LeasePathFor(exePath, ForeignPid);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (StagedLeases) StagedLeases.Add(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
    }

    /// <summary>Every lease file <see cref="StageForeignLease"/> created, for class cleanup.</summary>
    private static readonly List<string> StagedLeases = [];

    /// <summary>Removes the staged lease files, ignoring the ones a test already pruned.</summary>
    private static void DeleteStagedLeases()
    {
        lock (StagedLeases)
        {
            foreach (var path in StagedLeases)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A handle a test left open outlives this sweep; the file is then the OS's
                    // to collect. Failing cleanup would turn a passing suite red for nothing.
                }
            }

            StagedLeases.Clear();
        }
    }
}