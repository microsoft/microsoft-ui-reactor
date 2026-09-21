using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>
    /// A candidate with the default disposition every existing test assumes: our session, and
    /// a launcher that is already gone.
    /// </summary>
    /// <remarks>
    /// Sweepable by default because these tests are about the image-path and session rules, and
    /// a candidate whose launcher is live is excluded before those are reached. Tests that care
    /// about the launcher say so with <see cref="Attended"/>.
    /// </remarks>
    private static OrphanedHostSweep.Candidate At(int pid, string? path) =>
        new(pid, path, OurSession, LauncherIsLive: false);

    /// <summary>The same candidate, but still owned by a running launcher.</summary>
    private static OrphanedHostSweep.Candidate Attended(int pid, string? path) =>
        new(pid, path, OurSession, LauncherIsLive: true);

    /// <summary>
    /// The session the synthetic candidates are stamped with, and the one passed as "ours".
    /// </summary>
    /// <remarks>
    /// A literal rather than this process's real session id. The sweep only ever compares the
    /// two for equality, so a fixed pair exercises the comparison exactly while keeping the
    /// tests independent of how the suite was launched — an interactive session, a service, or
    /// a CI agent all number differently.
    /// </remarks>
    private const int OurSession = 7;

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

        // Before the recursive delete below, which cannot remove a directory holding an open
        // handle. Deliberately held for the class's lifetime by the contention control, so
        // this is the only place they can be closed.
        foreach (var gate in HeldGates)
        {
            try
            {
                gate.Dispose();
            }
            catch (IOException ex)
            {
                // Non-fatal by design — teardown must not redden a passing suite — but not
                // silent: a gate that will not close is why the recursive delete below may
                // then fail, so recording it is what makes that second failure explicable.
                Console.WriteLine(
                    $"[Reactor.AppTests] Could not release a staged gate handle: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        HeldGates.Clear();

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
            .SelectOurs([At(100, Ours), At(200, Theirs)], Ours, OurSession)
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
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, Ours)], Ours, OurSession).Count());
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

        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, unnormalized)], Ours, OurSession).Count(),
            "A redundant path segment names the same image.");
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, cased)], Ours, OurSession).Count(),
            "Windows paths are case-insensitive.");
    }

    /// <summary>
    /// Two spellings that the filesystem resolves to one file must agree on both derived
    /// answers: the key that names this run's lease and gate, and whether a live process is a
    /// sibling.
    /// </summary>
    /// <remarks>
    /// <para>Disagreement here inverts the whole scheme rather than weakening it. If one build
    /// output has two keys, two runs of it take different lease files, each enumerates no
    /// sibling, each is admitted to sweep, and each then kills the other's running host — the
    /// cross-checkout kill this class exists to prevent, reintroduced one level down where no
    /// other test looks.</para>
    /// <para>The extended-length <c>\\?\</c> spelling is the alias used because it is exact and
    /// needs no privilege: a junction or a <c>subst</c> drive is the realistic way a host
    /// acquires two spellings in the field, but creating either requires rights a test run may
    /// not have, and a test that quietly skips is indistinguishable from one that passes. This
    /// one is refused by textual normalisation on every machine, so it discriminates
    /// everywhere. Note the file is deliberately created: an absent path cannot be resolved by
    /// the filesystem, so the sibling tests above exercise only the textual fallback.</para>
    /// </remarks>
    [TestMethod]
    public void An_Alias_Spelling_Of_One_Image_Is_One_Build_Output()
    {
        var dir = Path.Join(ClaimRoot, "alias-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var exe = Path.Join(dir, "Reactor.AppTests.Host.exe");
        File.WriteAllText(exe, string.Empty);

        var aliased = @"\\?\" + exe;
        Assert.AreNotEqual(exe, aliased, StringComparer.OrdinalIgnoreCase,
            "Control: the two spellings must differ textually, or matching them proves nothing.");

        Assert.AreEqual(
            OrphanedHostSweep.GatePathFor(exe),
            OrphanedHostSweep.GatePathFor(aliased),
            "Two spellings of one image took different startup gates, so both runs enter at " +
            "once and their lease registration and sweep interleave freely.");

        Assert.AreEqual(
            OrphanedHostSweep.LeasePathFor(exe, 4242),
            OrphanedHostSweep.LeasePathFor(aliased, 4242),
            "Two spellings of one image took different lease files, so neither run can see the " +
            "other as a live sibling and each is admitted to sweep the other's host.");

        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, aliased)], exe, OurSession).Count(),
            "A live host reached by an alias was not recognised as our own image, so it " +
            "survives a sweep that should have reclaimed it.");
    }

    /// <summary>
    /// Fails closed. A candidate whose path could not be read is left alone, because killing
    /// on "don't know" is exactly the machine-wide behaviour this replaced.
    /// </summary>
    [TestMethod]
    public void A_Candidate_With_An_Unreadable_Path_Is_Left_Alone()
    {
        Assert.AreEqual(0, OrphanedHostSweep.SelectOurs([At(100, null), At(200, "  ")], Ours, OurSession).Count());
    }

    /// <summary>
    /// A host belonging to another Windows user is not an orphan, however exactly its image
    /// path matches.
    /// </summary>
    /// <remarks>
    /// <para>Process enumeration is machine-wide, but the liveness leases are per-user files
    /// under <c>%LOCALAPPDATA%</c>. Another user running this same build output therefore
    /// registers a lease this run cannot see, and so presents exactly as an orphan does: right
    /// image, no live sibling. Path scoping cannot separate them — it is the same path — so
    /// without the session comparison an elevated run has both the mistaken verdict and the
    /// rights to act on it.</para>
    /// <para>The image path is deliberately identical to ours here. Using a different one
    /// would let the existing path check carry the assertion and the test would still pass
    /// with session scoping removed.</para>
    /// </remarks>
    [TestMethod]
    public void A_Host_In_Another_Session_Is_Not_Swept()
    {
        var otherUser = new OrphanedHostSweep.Candidate(300, Ours, OurSession + 1, LauncherIsLive: false);

        Assert.AreEqual(0, OrphanedHostSweep.SelectOurs([otherUser], Ours, OurSession).Count(),
            "A process running our image in a different session belongs to a different user, " +
            "whose lease is in a namespace this run cannot read.");

        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(300, Ours)], Ours, OurSession).Count(),
            "Control: the same candidate in our own session is still swept, so the assertion " +
            "above is about the session and not about the path.");
    }

    /// <summary>
    /// A candidate whose session could not be read is left alone, for the same reason an
    /// unreadable path is.
    /// </summary>
    /// <remarks>
    /// Null is not a session number, so it can never coincide with a real one. Our own orphans
    /// always read back — this process launched them — so the only candidates this excludes are
    /// ones there was never positive evidence for.
    /// </remarks>
    [TestMethod]
    public void A_Candidate_With_An_Unreadable_Session_Is_Left_Alone()
    {
        var unknown = new OrphanedHostSweep.Candidate(400, Ours, null, LauncherIsLive: false);

        Assert.AreEqual(0, OrphanedHostSweep.SelectOurs([unknown], Ours, OurSession).Count());
    }

    /// <summary>
    /// A host whose launcher is still running is never swept, however this run was admitted.
    /// </summary>
    /// <remarks>
    /// <para>This is the case the lease cannot cover, and it is not hypothetical. Admission
    /// proves only that no other <em>participant</em> is live: a run started from a revision
    /// predating the lease protocol writes no lease, so it is invisible to admission, this run
    /// is admitted, and its snapshot then finds that run's healthy host sharing our image path.
    /// Every other rule here agrees it should die — same image, same session, no sibling — so
    /// without the launcher check the sweep kills a live suite.</para>
    /// <para>The image path is deliberately ours and the session deliberately ours, so the only
    /// thing standing between this candidate and the doomed set is the launcher. Dropping that
    /// condition reddens this test and nothing else.</para>
    /// </remarks>
    [TestMethod]
    public void A_Host_Whose_Launcher_Is_Still_Running_Is_Not_Swept()
    {
        Assert.AreEqual(
            0,
            OrphanedHostSweep.SelectOurs([Attended(500, Ours)], Ours, OurSession).Count(),
            "A host whose launching run is still alive is attended, not orphaned. Sweeping it " +
            "kills a live suite that simply does not write a lease this run can see.");

        Assert.AreEqual(
            1,
            OrphanedHostSweep.SelectOurs([At(500, Ours)], Ours, OurSession).Count(),
            "Control: the identical candidate with a dead launcher is still swept, so the " +
            "assertion above is about the launcher and not about the path or the session.");
    }

    /// <summary>
    /// The real probe agrees with the process table: a live child reads as attended, and the
    /// same pid reads as orphaned once its launcher is gone.
    /// </summary>
    /// <remarks>
    /// <para>The selection tests above take <c>LauncherIsLive</c> as data, so they pin the rule
    /// but say nothing about whether the value handed to it is ever right. This drives the
    /// actual Toolhelp snapshot against a process this test really launched, which is the only
    /// way the interop, the parent lookup and the start-time guard are exercised at all.</para>
    /// <para>Both directions are asserted against the <em>same</em> pid. A probe hard-wired to
    /// either answer passes one half, so checking only one would leave the constant
    /// indistinguishable from a working query.</para>
    /// </remarks>
    [TestMethod]
    public void The_Launcher_Probe_Tracks_A_Real_Process()
    {
        // A child of this test process, so its launcher is known-alive: us.
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c pause",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start the probe child.");

        try
        {
            var parents = OrphanedHostSweep.SnapshotParentPids();

            Assert.IsTrue(parents.TryGetValue(child.Id, out var parentPid),
                "Precondition: the snapshot must contain the child, or the probe below is " +
                "answering from a missing entry rather than from the process table.");

            Assert.AreEqual(
                Environment.ProcessId,
                parentPid,
                "Precondition: this test process must really be the child's parent.");

            Assert.IsTrue(
                OrphanedHostSweep.LauncherIsLive(child.Id, parents),
                "A process whose launcher is this very test was reported as orphaned.");

            // Now the orphan case, without killing anything real: the same child, described by
            // a snapshot whose recorded parent no longer exists. Pid 0 is never a live process,
            // and the map is what the probe reads, so this is the state a dead launcher leaves.
            var orphaned = new Dictionary<int, int>(parents) { [child.Id] = int.MaxValue };

            Assert.IsFalse(
                OrphanedHostSweep.LauncherIsLive(child.Id, orphaned),
                "A process whose recorded launcher is not running must read as orphaned.");
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                Console.WriteLine($"Probe child already gone: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The destructive path refuses to run at all when our own image cannot be resolved through
    /// the filesystem.
    /// </summary>
    /// <remarks>
    /// <para>Everything the sweep coordinates on — the startup gate, the liveness lease, the
    /// sibling probe — is named by a digest of this path, and that digest is only a function of
    /// the <em>file</em> while the filesystem can be asked. When it cannot, the key silently
    /// becomes a function of the caller's spelling instead. Two runs of one build output that
    /// disagree about the key take different gate and lease files, so each sees no sibling,
    /// each is admitted, and each kills the other's live host.</para>
    /// <para>Unlike the sibling predicate, there is no safe weaker answer available here, so
    /// the sweep declines rather than proceeding on a key nobody else will derive.</para>
    /// </remarks>
    [TestMethod]
    public void A_Sweep_Is_Refused_When_Our_Own_Image_Cannot_Be_Resolved()
    {
        var absent = Path.Join(ClaimRoot, "never-created", "Reactor.AppTests.Host.exe");
        Assert.IsFalse(File.Exists(absent), "Control: the path must not exist.");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => OrphanedHostSweep.RequireResolvableImage(absent, "Host app"));

        StringAssert.Contains(ex.Message, absent,
            "The refusal has to name the path, or nobody can act on it.");

        // The positive control. A real file resolves, so the guard is discriminating between
        // resolvable and unresolvable rather than refusing everything.
        var dir = Path.Join(ClaimRoot, "resolvable-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var present = Path.Join(dir, "Reactor.AppTests.Host.exe");
        File.WriteAllText(present, string.Empty);

        OrphanedHostSweep.RequireResolvableImage(present, "Host app");
    }

    /// <summary>
    /// Guards the argument that would silently turn the sweep back into a match-everything
    /// sweep if it were ever allowed through.
    /// </summary>
    [TestMethod]
    public void An_Empty_Executable_Path_Is_Rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OrphanedHostSweep.SelectOurs([At(100, Ours)], "  ", OurSession).ToList());
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
            OrphanedHostSweep.SelectOurs([At(1, viaDot)], plain, OurSession).Count(),
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
    /// A storage fault that stops the gate being addressed is reported as itself, not as a
    /// timeout waiting for a competing run.
    /// </summary>
    /// <remarks>
    /// The two outcomes need opposite responses, and only one of them is worth waiting out.
    /// Folded together, an unwritable claim directory spent the full startup-gate timeout and
    /// then told the reader another run was stuck holding the gate — naming a competitor that
    /// never existed, claiming a wait that resolved nothing, and discarding the storage error
    /// that was the only actionable fact. The fault is staged by naming a directory under an
    /// existing <i>file</i>, which Windows refuses to create deterministically and without any
    /// privilege.
    /// </remarks>
    [TestMethod]
    public void A_Claim_Directory_That_Cannot_Be_Created_Is_Not_Reported_As_Contention()
    {
        var blocker = Path.Join(ClaimRoot, "gate-fault-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, string.Empty);

        var unusable = Path.Join(blocker, "claims");
        var exe = Probe("gate-fault");

        var thrown = Assert.Throws<OrphanedHostSweep.GateSetupException>(
            () => OrphanedHostSweep.TryEnterStartupGate(exe, TimeSpan.Zero, unusable),
            "An unusable claim directory was reported as a held gate, so the caller waits out " +
            "a timeout and then blames a process that does not exist.");

        Assert.IsNotNull(thrown.InnerException,
            "The storage error is the only fact that explains the failure, so it must survive.");

        using var control = OrphanedHostSweep.TryEnterStartupGate(
            exe, TimeSpan.Zero, Path.Join(ClaimRoot, "gate-fault-ok-" + Guid.NewGuid().ToString("n")));
        Assert.IsNotNull(control,
            "Control: a usable directory must still admit the run, or the throw above says " +
            "nothing about the fault and everything about the overload.");
    }

    /// <summary>
    /// A gate path this run cannot open is likewise not contention, even though the claim
    /// directory around it is perfectly healthy.
    /// </summary>
    /// <remarks>
    /// <para>The directory and the gate fail at different points, and only the first was
    /// distinguished. The open loop retried every <c>IOException</c> and
    /// <c>UnauthorizedAccessException</c> alike, so a permanent fault at the gate path itself
    /// — an ACL denial, or a directory occupying <c>&lt;key&gt;.gate</c> — still spent the full
    /// two-minute startup timeout and still ended by blaming a run that was never there.</para>
    /// <para>Retrying is nevertheless kept for every failure, because refusal has a real
    /// transient form: releasing a gate unlinks it, and a third party holding it with delete
    /// sharing leaves it delete-pending, which also presents as access denied and clears on its
    /// own. Only a sharing or lock violation proves another run holds the gate, so that is the
    /// single failure allowed to end the wait as a timeout.</para>
    /// <para>Staged with a directory occupying the gate's exact name, which Windows refuses to
    /// open as a file deterministically and without privilege.</para>
    /// </remarks>
    [TestMethod]
    public void A_Gate_Path_That_Cannot_Be_Opened_Is_Not_Reported_As_Contention()
    {
        var exe = Probe("gate-path-fault-" + Guid.NewGuid().ToString("n"));
        var claims = Path.Join(ClaimRoot, "gate-path-claims-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(claims);

        var gate = Path.Join(claims, Path.GetFileName(OrphanedHostSweep.GatePathFor(exe)));
        Directory.CreateDirectory(gate);

        var thrown = Assert.Throws<OrphanedHostSweep.GateSetupException>(
            () => OrphanedHostSweep.TryEnterStartupGate(exe, TimeSpan.Zero, claims),
            "A gate this run cannot open at all was reported as a gate somebody else holds, " +
            "so the caller waits out the whole timeout and then names a phantom competitor.");

        Assert.IsInstanceOfType<UnauthorizedAccessException>(thrown.InnerException,
            "The refusal itself has to travel with the report, or it is no more actionable " +
            "than the false contention it replaced.");

        Assert.IsNull(
            OrphanedHostSweep.TryEnterStartupGate(exe, TimeSpan.Zero, ContendedClaims(exe)),
            "Control: a gate genuinely held by another handle must still read as contention. " +
            "Without this the throw above is satisfied by a gate that never admits anyone.");
    }

    /// <summary>
    /// A claim directory whose gate for <paramref name="exe"/> is already held, so an
    /// acquisition against it meets a real sharing violation rather than a storage fault.
    /// </summary>
    /// <remarks>
    /// Held for the remainder of the class rather than released, because the handle is the
    /// whole point: closing it would turn the control assertion into a second uncontended
    /// acquisition, which passes whatever the classification does.
    /// </remarks>
    private static string ContendedClaims(string exe)
    {
        var claims = Path.Join(ClaimRoot, "gate-contended-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(claims);

        var gate = Path.Join(claims, Path.GetFileName(OrphanedHostSweep.GatePathFor(exe)));
        HeldGates.Add(new FileStream(
            gate, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));

        return claims;
    }

    private static readonly List<FileStream> HeldGates = [];

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

    /// <summary>
    /// A host belonging to a checkout whose directory differs from ours only in case is not our
    /// image, and must survive the sweep.
    /// </summary>
    /// <remarks>
    /// <para>The kill predicate used to compare resolved paths case-insensitively, which reads
    /// as harmless on Windows and is not. Under a case-sensitive parent <c>Repo</c> and
    /// <c>repo</c> are two checkouts holding two different binaries, and folding case makes one
    /// of them answer "mine" for the other's live host. Every other condition then agrees by
    /// construction — same session, and an orphan's launcher is gone — so nothing downstream
    /// stops the kill.</para>
    /// <para>The candidate is stamped with our own session and a dead launcher deliberately, so
    /// the image path is the only thing that can exclude it. Both binaries are really created,
    /// because the defect lives in the branch where the filesystem <em>does</em> answer: two
    /// absent paths fall back to their spellings and are still compared case-insensitively, by
    /// design.</para>
    /// </remarks>
    [TestMethod]
    public void A_Host_From_A_Checkout_Differing_Only_In_Case_Is_Not_Ours()
    {
        var root = Path.Join(ClaimRoot, "case-image-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        if (!TryEnableCaseSensitivity(root))
        {
            Assert.Inconclusive(
                "This filesystem cannot be made case-sensitive, so two checkouts differing " +
                "only in case cannot exist and the premise is unreachable.");
        }

        var ours = StageExe(Path.Join(root, "repo"));
        var theirs = StageExe(Path.Join(root, "Repo"));

        // Positive control. On a filesystem that merged the two spellings these would be one
        // file, and the assertion below would pass for a reason that has nothing to do with
        // the predicate under test.
        Assert.AreEqual(2, Directory.GetDirectories(root).Length,
            "Precondition: the two spellings must really be two checkouts.");

        Assert.AreEqual(
            1,
            OrphanedHostSweep.SelectOurs([At(100, ours)], ours, OurSession).Count(),
            "Control: our own host must still be selected, or the test below proves only that " +
            "the predicate stopped matching anything.");

        Assert.AreEqual(
            0,
            OrphanedHostSweep.SelectOurs([At(200, theirs)], ours, OurSession).Count(),
            "A host from a different checkout was judged to be our own image, so this run " +
            "would terminate another checkout's process.");
    }

    /// <summary>
    /// Those same two checkouts must also not share a startup gate or a lease prefix.
    /// </summary>
    /// <remarks>
    /// <para>The key and the kill predicate are required to agree about what one build output
    /// is, and the agreement has to hold in both directions. Making the predicate case-exact
    /// without doing the same to the key leaves two distinct checkouts sharing one claim
    /// namespace: each reads the other's lease as its own live sibling and stands down from a
    /// sweep it was entitled to run, and each queues behind the other at a gate that was never
    /// meant to span them.</para>
    /// <para>The alias control is the other half. A key that simply hashed the caller's
    /// spelling would satisfy the first assertion trivially while breaking the property the
    /// whole claim scheme rests on, so one image reached two ways is asserted to keep one key.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Checkouts_Differing_Only_In_Case_Do_Not_Share_A_Claim_Key()
    {
        var root = Path.Join(ClaimRoot, "case-key-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        if (!TryEnableCaseSensitivity(root))
        {
            Assert.Inconclusive(
                "This filesystem cannot be made case-sensitive, so two checkouts differing " +
                "only in case cannot exist and the premise is unreachable.");
        }

        var ours = StageExe(Path.Join(root, "repo"));
        var theirs = StageExe(Path.Join(root, "Repo"));

        Assert.AreEqual(2, Directory.GetDirectories(root).Length,
            "Precondition: the two spellings must really be two checkouts.");

        Assert.AreNotEqual(
            OrphanedHostSweep.GatePathFor(ours),
            OrphanedHostSweep.GatePathFor(theirs),
            "Two different checkouts took one startup gate, so each stands down from its own " +
            "sweep on the strength of the other's lease and each blocks the other's admission.");

        Assert.AreNotEqual(
            OrphanedHostSweep.LeasePathFor(ours, 4242),
            OrphanedHostSweep.LeasePathFor(theirs, 4242),
            "Two different checkouts wrote into one lease namespace, so each reads the other " +
            "as its own live sibling.");

        Assert.AreEqual(
            OrphanedHostSweep.GatePathFor(ours),
            OrphanedHostSweep.GatePathFor(@"\\?\" + ours),
            "Control: one image reached by two spellings must still take one gate, which is " +
            "the property the whole claim scheme rests on.");
    }

    /// <summary>Creates an empty host binary under <paramref name="directory"/>.</summary>
    /// <remarks>
    /// Real files, because the behaviour under test only exists once the filesystem can answer
    /// for the path. An empty file is enough: nothing here launches it, and the final-path
    /// query needs only a handle.
    /// </remarks>
    private static string StageExe(string directory)
    {
        Directory.CreateDirectory(directory);
        var exe = Path.Join(directory, "Reactor.AppTests.Host.exe");
        File.WriteAllText(exe, string.Empty);
        return exe;
    }

    /// <summary>
    /// Marks a directory case-sensitive, reporting whether the filesystem accepted it.
    /// </summary>
    /// <remarks>
    /// Measured to succeed unelevated on a developer machine. Reported rather than asserted so
    /// the caller can skip on a volume that refuses, since the premise is then unreachable.
    /// </remarks>
    private static bool TryEnableCaseSensitivity(string directory)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "fsutil.exe",
                Arguments = $"file setCaseSensitiveInfo \"{directory}\" enable",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (proc is null) return false;

            proc.WaitForExit(30_000);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}