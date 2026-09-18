using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the file lock that serialises packaged runs sharing one layout.
/// </summary>
/// <remarks>
/// <para>The lock is what stops a second run of this checkout from unregistering and
/// re-registering the package underneath a run already in progress. A normal packaged run is
/// the only run on its layout, so it never contends and would pass just as happily if the
/// "lock" granted everyone access — the failure is invisible from inside the tier, and only
/// appears as an unexplained mid-run identity change when two runs do overlap.</para>
/// <para>These tests therefore assert the two properties the lock is actually bought for:
/// a second acquisition is <b>refused</b> while the first is held, and it becomes available
/// again once released. The <c>FileShare.Read</c> choice is pinned too — a contender must be
/// able to read the owner record, because that record is the whole diagnostic when a run
/// fails to acquire.</para>
/// </remarks>
[TestClass]
public class PackagedLayoutLockTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Join(Path.GetTempPath(), "reactor-lock-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leaked handle here must not mask the test's own verdict */ }
        catch (UnauthorizedAccessException) { /* likewise: a read-only leftover is not a failure */ }
    }

    private string LockPath => Path.Join(_root, "layout.lock");

    [TestMethod]
    public void A_Second_Acquisition_Is_Refused_While_The_First_Is_Held()
    {
        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");

        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");
        Assert.IsNull(
            AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2"),
            "A second run acquired the same layout lock while the first still held it, so " +
            "nothing prevents it from re-registering the package under a live run.");
    }

    [TestMethod]
    public void Releasing_The_Lock_Lets_The_Next_Run_Acquire_It()
    {
        var first = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(first, "Precondition: the first acquisition must succeed.");
        first.Dispose();

        using var second = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2");

        Assert.IsNotNull(second,
            "The lock stayed unavailable after its holder released it, so one finished run " +
            "would block every later run on this layout until the file was deleted by hand.");
    }

    /// <summary>
    /// The owner record must stay readable under contention: it is the only diagnostic a
    /// blocked run can report. Exercised through the production reader, because the share
    /// mode is the subtle part — a reader sharing only <c>Read</c> is refused outright.
    /// </summary>
    [TestMethod]
    public void A_Blocked_Contender_Can_Still_Read_The_Owner_Record()
    {
        const string Owner = "pid=4242 layout=C:\\checkout\\layout";

        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, Owner);
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        Assert.AreEqual(Owner, AppxLooseLayoutDeployment.ReadOwner(LockPath),
            "The lock file did not carry a readable owner record, so a run that fails to " +
            "acquire cannot say who is holding it.");
    }

    /// <summary>
    /// Re-stamping must not append. A lock file reused across runs would otherwise accumulate
    /// records and report a long-dead owner first.
    /// </summary>
    [TestMethod]
    public void Re_Acquiring_A_Released_Lock_Replaces_The_Owner_Record()
    {
        var first = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1111");
        Assert.IsNotNull(first, "Precondition: the first acquisition must succeed.");
        first.Dispose();

        using var second = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2222");
        Assert.IsNotNull(second, "Precondition: the second acquisition must succeed.");

        Assert.AreEqual("pid=2222", AppxLooseLayoutDeployment.ReadOwner(LockPath),
            "Re-acquiring appended instead of replacing, so the lock file names a dead owner " +
            "first and every later diagnostic is misleading.");
    }

    [TestMethod]
    public void Waiting_Gives_Up_And_Reports_Failure_When_The_Lock_Is_Never_Released()
    {
        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        var started = DateTime.UtcNow;
        var waited = AppxLooseLayoutDeployment.WaitForLockFile(
            LockPath, "pid=2", TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));
        var elapsed = DateTime.UtcNow - started;

        Assert.IsNull(waited,
            "The wait reported success against a lock that was never released.");
        Assert.IsTrue(elapsed >= TimeSpan.FromMilliseconds(250),
            $"The wait returned after {elapsed.TotalMilliseconds:F0}ms without honouring its " +
            "300ms deadline, so it never actually retried.");
    }

    [TestMethod]
    public void Waiting_Succeeds_Once_The_Holder_Releases()
    {
        var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        // Released on another thread while the wait is already polling, which is the
        // handover this loop exists for.
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            held.Dispose();
        });

        using var acquired = AppxLooseLayoutDeployment.WaitForLockFile(
            LockPath, "pid=2", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(25));

        release.GetAwaiter().GetResult();

        Assert.IsNotNull(acquired,
            "The wait never picked up a lock that was released well inside its deadline.");
    }

    /// <summary>A synthetic layout path, unique per test, for the whole-set acquisitions.</summary>
    private static string SyntheticLayout() =>
        Path.Join(Path.GetTempPath(), "reactor-layout-" + Guid.NewGuid().ToString("n"));

    /// <summary>
    /// Two algorithm versions that are both non-live, so the multi-version property is measured
    /// rather than coinciding with the single version that exists today.
    /// </summary>
    private static readonly string[] TwoVersions = ["8", "9"];

    private static readonly TimeSpan Instant = TimeSpan.Zero;

    /// <summary>
    /// The lock has to cover every supported algorithm version, not just the current one.
    /// Cleanup already recognizes locks and registrations from all of them, so a run holding
    /// only its own version's file does not exclude a run of an earlier revision over the same
    /// directory: both proceed, and rule 2 unregisters the other's live package.
    /// </summary>
    /// <remarks>
    /// The held lock is the <em>second</em> version deliberately. An implementation that locked
    /// only the current version — the first entry — would take a different file, see it free,
    /// and pass. Only acquiring the whole set fails here.
    /// </remarks>
    [TestMethod]
    public void A_Lock_Held_Under_Any_Supported_Version_Refuses_The_Whole_Set()
    {
        var layout = SyntheticLayout();
        var paths = AppxLooseLayoutDeployment.LockPathsFor(layout, TwoVersions);

        Assert.AreEqual(2, paths.Count,
            "Precondition: the two versions must map to two distinct lock files, or this test " +
            "cannot tell the whole set apart from a single file.");

        foreach (var other in paths)
        {
            using var held = AppxLooseLayoutDeployment.TryOpenLockFile(other, "pid=1");
            Assert.IsNotNull(held, $"Precondition: {other} must be free before the test.");

            var acquired = AppxLooseLayoutDeployment.TryAcquireAllLocks(
                layout, "pid=2", Instant, Instant, out var blockedOn, TwoVersions);

            Assert.IsNull(acquired,
                $"A run acquired this layout while version lock '{Path.GetFileName(other)}' was " +
                "still held, so two revisions of the tier can run on one directory and each " +
                "will unregister the other's live package.");

            Assert.AreEqual(other, blockedOn,
                "The diagnostic names the wrong lock, so a blocked run cannot report who holds it.");
        }
    }

    /// <summary>
    /// A refused acquisition must leave nothing behind. Keeping a partial set would wedge the
    /// very run this one just deferred to out of taking its own locks.
    /// </summary>
    [TestMethod]
    public void A_Refused_Acquisition_Releases_The_Locks_It_Already_Took()
    {
        var layout = SyntheticLayout();
        var paths = AppxLooseLayoutDeployment.LockPathsFor(layout, TwoVersions);

        // Hold the last one, so the attempt below has certainly taken the earlier one first.
        using (var blocker = AppxLooseLayoutDeployment.TryOpenLockFile(paths[^1], "pid=1"))
        {
            Assert.IsNotNull(blocker, "Precondition: the blocking lock must be acquirable.");

            Assert.IsNull(
                AppxLooseLayoutDeployment.TryAcquireAllLocks(
                    layout, "pid=2", Instant, Instant, out _, TwoVersions),
                "Precondition: the acquisition must be refused.");
        }

        using var first = AppxLooseLayoutDeployment.TryOpenLockFile(paths[0], "pid=3");
        Assert.IsNotNull(first,
            "The refused run kept the lock it had already taken, so the run it deferred to can " +
            "never acquire the full set and both are stuck.");
    }

    /// <summary>
    /// Reclaiming an abandoned registration must hold the layout's locks across the removal,
    /// not merely sample them beforehand.
    /// </summary>
    /// <remarks>
    /// Sampling answers a question about the past: between "nobody holds this" and the
    /// <c>RemovePackage</c> call, a run can recreate the worktree, take the lock and register,
    /// and the removal then evicts a live package. Holding the handles through the removal
    /// makes that interleaving unrepresentable.
    /// </remarks>
    [TestMethod]
    public void A_Held_Reclamation_Lease_Blocks_A_Concurrent_Layout_Acquisition()
    {
        var layout = SyntheticLayout();

        using var lease = AppxLooseLayoutDeployment.TryAcquireReclamationLease(layout);
        Assert.IsNotNull(lease,
            "Precondition: an unclaimed layout must be leasable, or nothing could ever be " +
            "reclaimed.");

        foreach (var path in AppxLooseLayoutDeployment.LockPathsFor(layout))
        {
            Assert.IsNull(AppxLooseLayoutDeployment.TryOpenLockFile(path, "pid=2"),
                $"A run took '{Path.GetFileName(path)}' while a reclamation was in flight, so " +
                "it can register this layout before the reclaiming run removes the package it " +
                "already captured.");
        }
    }

    /// <summary>
    /// The mirror: a layout some run still holds must not be reclaimable at all. This is the
    /// deleted-worktree case — the directory is gone but the run that registered it is alive.
    /// </summary>
    [TestMethod]
    public void A_Locked_Layout_Cannot_Be_Leased_For_Reclamation()
    {
        var layout = SyntheticLayout();
        var paths = AppxLooseLayoutDeployment.LockPathsFor(layout);

        Assert.IsFalse(AppxLooseLayoutDeployment.IsLayoutLocked(layout),
            "Control: an unheld layout must read as unlocked, or the assertion below passes " +
            "for the wrong reason.");

        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(paths[0], "pid=1");
        Assert.IsNotNull(held, "Precondition: the lock must be acquirable.");

        Assert.IsNull(AppxLooseLayoutDeployment.TryAcquireReclamationLease(layout),
            "A live run's layout was leased for reclamation, so its registration would be " +
            "removed underneath it.");

        Assert.IsTrue(AppxLooseLayoutDeployment.IsLayoutLocked(layout),
            "The liveness check disagreed with the lease it is derived from.");
    }

    /// <summary>
    /// Releasing the lease must free the locks, or one reclamation would block every later run
    /// on that layout for the lifetime of the process.
    /// </summary>
    [TestMethod]
    public void Releasing_A_Reclamation_Lease_Frees_The_Locks()
    {
        var layout = SyntheticLayout();

        var lease = AppxLooseLayoutDeployment.TryAcquireReclamationLease(layout);
        Assert.IsNotNull(lease, "Precondition: the lease must be acquirable.");
        lease.Dispose();

        using var after = AppxLooseLayoutDeployment.TryAcquireReclamationLease(layout);
        Assert.IsNotNull(after,
            "The locks stayed held after the lease was released, so a single reclamation " +
            "permanently wedges that layout.");
    }

    /// <summary>
    /// Absence must be established by naming the component that failed to resolve, not by
    /// enumerating whatever ancestor happens to be readable.
    /// </summary>
    /// <remarks>
    /// <para>Staged with a <em>file</em> where a directory was recorded, which reproduces the
    /// defect's shape without needing an ACL: <c>Directory.Exists</c> answers
    /// <see langword="false"/> for it exactly as it does for an unreadable directory, and the
    /// parent is perfectly readable. A probe that enumerates the parent and stops reports
    /// <c>Absent</c> — and rule 3 then unregisters a package whose path was never gone.</para>
    /// <para>The <c>Absent</c> and <c>Present</c> controls sit in the same test so a staging
    /// mistake cannot leave the interesting assertion passing vacuously.</para>
    /// </remarks>
    [TestMethod]
    public void A_Path_That_Resolves_To_Something_Unreadable_Is_Not_Reported_Absent()
    {
        var missing = Path.Join(_root, "never-created");
        Assert.AreEqual(LayoutPresence.Absent, AppxLooseLayoutDeployment.ProbeLayout(missing),
            "Control: a genuinely missing directory under a readable parent must be Absent, " +
            "or rule 3 stops reclaiming deleted worktrees entirely.");

        var present = Path.Join(_root, "real");
        Directory.CreateDirectory(present);
        Assert.AreEqual(LayoutPresence.Present, AppxLooseLayoutDeployment.ProbeLayout(present),
            "Control: an existing directory must be Present.");

        var occupied = Path.Join(_root, "occupied");
        File.WriteAllText(occupied, "not a directory");

        Assert.AreEqual(LayoutPresence.Unknown, AppxLooseLayoutDeployment.ProbeLayout(occupied),
            "A path whose final component exists but does not resolve as a readable directory " +
            "was reported gone. Anything Directory.Exists hides — an ACL, an offline share — " +
            "reads the same way, and the action taken on 'gone' is to unregister the package.");
    }

    /// <summary>
    /// The same defect one level up: an unresolvable intermediate component must not be walked
    /// past to a readable grandparent.
    /// </summary>
    [TestMethod]
    public void An_Unresolvable_Intermediate_Component_Is_Not_Walked_Past()
    {
        var intermediate = Path.Join(_root, "middle");
        File.WriteAllText(intermediate, "not a directory");

        var target = Path.Join(intermediate, "layout");

        Assert.AreEqual(LayoutPresence.Unknown, AppxLooseLayoutDeployment.ProbeLayout(target),
            "The probe climbed past a component it could not resolve, enumerated a readable " +
            "ancestor, and called the target gone — so a live layout under an unreadable " +
            "parent directory would have its registration reclaimed.");
    }
}
