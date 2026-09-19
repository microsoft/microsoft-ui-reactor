using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reactor.Tests.Shared;

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

    /// <summary>
    /// Layouts handed out by <see cref="SyntheticLayout"/>, so teardown can remove the lock
    /// files they caused under the production lock directory.
    /// </summary>
    /// <remarks>
    /// Tests that open a lock directly rather than through <c>TryAcquireAllLocks</c> bypass
    /// <c>Release</c>, which is what normally unlinks these. Left alone they accumulate one
    /// file per test run per version under <c>%LOCALAPPDATA%</c> forever — the same unbounded
    /// growth this PR removes from the E2E suite, reintroduced by its own tests.
    /// </remarks>
    private readonly List<string> _syntheticLayouts = new();

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Join(Path.GetTempPath(), "reactor-lock-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);

        // Direct TryOpenLockFile calls do not create it, so on a profile where the tier has
        // never run the preconditions below would fail for the directory's absence.
        AppxLooseLayoutDeployment.EnsureLockDirectory();
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leaked handle here must not mask the test's own verdict */ }
        catch (UnauthorizedAccessException) { /* likewise: a read-only leftover is not a failure */ }

        var versions = TwoVersions.Concat(WorktreeIdentity.SupportedAlgorithmVersions);
        foreach (var layout in _syntheticLayouts)
        {
            foreach (var version in versions)
            {
                try { File.Delete(AppxLooseLayoutDeployment.LockPathFor(layout, version)); }
                catch (IOException) { /* still held: the owning test's verdict stands regardless */ }
                catch (UnauthorizedAccessException) { /* likewise */ }
            }
        }

        _syntheticLayouts.Clear();
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
    /// <summary>
    /// A released lock set must leave no files behind.
    /// </summary>
    /// <remarks>
    /// Acquisition is <c>OpenOrCreate</c>, so without this every layout directory ever locked —
    /// including every abandoned-layout probe, which locks a directory that no longer exists —
    /// leaves a permanent file under <c>%LOCALAPPDATA%</c>. The audience for per-checkout
    /// identities is agents creating and destroying worktrees, so that set has no bound.
    /// </remarks>
    [TestMethod]
    public void Releasing_A_Lock_Set_Removes_Its_Files()
    {
        var layout = SyntheticLayout();
        var paths = AppxLooseLayoutDeployment.LockPathsFor(layout, TwoVersions);

        var held = AppxLooseLayoutDeployment.TryAcquireAllLocks(
            layout, "pid=1", Instant, Instant, out _, TwoVersions);

        Assert.IsNotNull(held, "Precondition: an unheld layout must be acquirable.");
        foreach (var path in paths)
        {
            Assert.IsTrue(File.Exists(path),
                $"Precondition: acquiring must create '{Path.GetFileName(path)}', or the " +
                "assertion below passes without the cleanup running at all.");
        }

        AppxLooseLayoutDeployment.Release(held);

        foreach (var path in paths)
        {
            Assert.IsFalse(File.Exists(path),
                $"'{Path.GetFileName(path)}' survived its holder, so lock files accumulate " +
                "permanently — one per layout directory this machine has ever locked.");
        }
    }

    /// <summary>
    /// The property that makes the cleanup above race-safe rather than lucky: a lock file
    /// another run currently holds cannot be unlinked.
    /// </summary>
    /// <remarks>
    /// <para>Deleting after releasing opens a window — this run closes its handle, another run
    /// acquires the same file, and only then does the delete land. What closes it is the share
    /// mode: <c>TryOpenLockFile</c> shares <c>Read</c> and <b>not</b> <c>Delete</c>, so Windows
    /// refuses to unlink a held lock and the cleanup simply gives up on it.</para>
    /// <para>That makes this an assertion about <c>TryOpenLockFile</c>, not about the cleanup.
    /// Add <see cref="FileShare.Delete"/> there and this reddens — which is precisely the change
    /// that would let a finishing run unlink the file a live run is holding, handing the next
    /// two contenders one lock file each and no mutual exclusion at all.</para>
    /// </remarks>
    [TestMethod]
    public void A_Held_Lock_File_Cannot_Be_Unlinked()
    {
        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(held, "Precondition: the lock must be acquirable.");

        Assert.ThrowsExactly<IOException>(
            () => File.Delete(LockPath),
            "A lock file was unlinked while its holder still had it open, so the post-release " +
            "cleanup can destroy a file another run has already reacquired.");

        Assert.IsTrue(File.Exists(LockPath), "The refused delete removed the file anyway.");
    }

    private string SyntheticLayout()
    {
        var layout = Path.Join(Path.GetTempPath(), "reactor-layout-" + Guid.NewGuid().ToString("n"));
        _syntheticLayouts.Add(layout);
        return layout;
    }

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

    /// <summary>
    /// A manifest left on a superseded version's derived name must be accepted, not treated as
    /// drift.
    /// </summary>
    /// <remarks>
    /// <para>The generated manifest is rewritten in place, so after an algorithm bump a layout
    /// registered earlier still carries the previous version's name until something rewrites it.
    /// The drift guard runs <i>before</i> that rewrite, so accepting only the current derivation
    /// would abort on exactly the manifest the rewrite was about to migrate — leaving the tier
    /// unrunnable until someone rebuilt, which is not a failure mode a contributor could read
    /// off the message.</para>
    /// <para>Driven through the version-parameterised helper rather than the live supported
    /// list: that list holds one entry today, so "every supported version is accepted" is
    /// trivially true of an implementation that only derives the current one. Two versions make
    /// the difference observable.</para>
    /// </remarks>
    [TestMethod]
    public void A_Superseded_Versions_Name_Is_Accepted_Rather_Than_Called_Drift()
    {
        var layout = Path.Join(Path.GetTempPath(), "reactor-stale-" + Guid.NewGuid().ToString("n"));
        var basePackageName = AppxLooseLayoutDeployment.PackageName;

        var current = WorktreeIdentity.DerivePackageName(
            basePackageName, layout, WorktreeIdentity.AlgorithmVersion);
        var superseded = WorktreeIdentity.DerivePackageName(basePackageName, layout, "0");

        // Control: the two versions genuinely disagree, so accepting both is a real widening
        // and not the same string counted twice.
        Assert.AreNotEqual(current, superseded,
            "Two algorithm versions derived the same name, so this test could not tell a guard " +
            "that accepts every supported version from one that accepts only the current one.");

        var accepted = AppxLooseLayoutDeployment.DeriveSupportedNames(
            basePackageName, layout, new[] { WorktreeIdentity.AlgorithmVersion, "0" });

        CollectionAssert.Contains(accepted.ToList(), superseded,
            "A layout still carrying a superseded version's derived name would be reported as " +
            "drift and abort the run, even though the rewrite immediately after the guard is " +
            "what migrates it.");
        CollectionAssert.Contains(accepted.ToList(), current,
            "The current version's derivation must stay accepted.");
    }

    /// <summary>
    /// Widening to superseded versions must not widen to <i>any</i> derived-looking name.
    /// </summary>
    /// <remarks>
    /// The cheap way to accept a stale name is a suffix-shape test, which also accepts a name
    /// derived for a different directory. Adopting one of those would point cleanup at another
    /// layout's registration — the ownership confusion the sweep exists to refuse — so the
    /// comparison has to stay exact.
    /// </remarks>
    [TestMethod]
    public void Another_Layouts_Derived_Name_Is_Still_Drift()
    {
        var mine = Path.Join(Path.GetTempPath(), "reactor-mine-" + Guid.NewGuid().ToString("n"));
        var theirs = Path.Join(Path.GetTempPath(), "reactor-theirs-" + Guid.NewGuid().ToString("n"));
        var basePackageName = AppxLooseLayoutDeployment.PackageName;

        var foreign = WorktreeIdentity.DerivePackageName(
            basePackageName, theirs, WorktreeIdentity.AlgorithmVersion);

        // Control: the foreign name really is well-formed, so rejecting it is the exactness of
        // the comparison and not the name failing to look derived at all.
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(foreign, basePackageName),
            "The foreign name is not shaped like a derivation, so this proves nothing about a " +
            "guard that tests shape instead of identity.");

        var accepted = AppxLooseLayoutDeployment.DeriveSupportedNames(
            basePackageName, mine, new[] { WorktreeIdentity.AlgorithmVersion, "0" });

        CollectionAssert.DoesNotContain(accepted.ToList(), foreign,
            "A name derived for a different layout was accepted as this layout's own, so " +
            "cleanup would sweep a registration belonging to another checkout.");

        // Through the live predicate as well, not just the pure helper: a guard that tested
        // suffix shape instead of consulting the helper would pass the assertion above while
        // still adopting the foreign name.
        Assert.IsFalse(
            new AppxLooseLayoutDeployment(mine).IsExpectedManifestName(foreign),
            "The drift guard accepted a name derived for a different layout, so a manifest " +
            "belonging to another checkout would be treated as this one's and swept.");
    }

    /// <summary>
    /// The live guard must be wired to the supported list, not to a hardcoded version.
    /// </summary>
    /// <remarks>
    /// The two tests above prove the helper widens correctly; this one proves the instance the
    /// guard actually consults is that helper applied to the real supported set, so adding a
    /// version to <see cref="WorktreeIdentity.SupportedAlgorithmVersions"/> is enough to make
    /// the guard accept it.
    /// </remarks>
    [TestMethod]
    public void The_Guard_Accepts_Exactly_The_Base_Name_And_The_Supported_Derivations()
    {
        var layout = Path.Join(Path.GetTempPath(), "reactor-wired-" + Guid.NewGuid().ToString("n"));
        var deployment = new AppxLooseLayoutDeployment(layout);

        var expected = AppxLooseLayoutDeployment.DeriveSupportedNames(
            AppxLooseLayoutDeployment.PackageName, layout, WorktreeIdentity.SupportedAlgorithmVersions);

        CollectionAssert.AreEquivalent(
            expected.ToList(), deployment.SupportedDerivedNames().ToList(),
            "The guard's accepted set is not the supported versions applied to this layout, so " +
            "adding a version to SupportedAlgorithmVersions would not actually make the guard " +
            "accept a manifest still carrying it.");

        Assert.IsTrue(deployment.IsExpectedManifestName(AppxLooseLayoutDeployment.PackageName),
            "A freshly built layout carries the base name and must not be called drift.");
        Assert.IsTrue(deployment.IsExpectedManifestName(deployment.EffectivePackageName),
            "A layout already rewritten by this version must not be called drift.");
        Assert.IsFalse(deployment.IsExpectedManifestName(AppxLooseLayoutDeployment.PackageName + ".nope"),
            "An unrelated name was accepted, so the guard no longer detects real drift.");
        Assert.IsFalse(deployment.IsExpectedManifestName(null),
            "A manifest with no Identity/@Name was accepted as expected.");
    }

    /// <summary>
    /// The name-only fallback must probe every name this layout could be registered under.
    /// </summary>
    /// <remarks>
    /// When broad enumeration fails, this lookup is all that is left of rules 1 and 2. Probing
    /// only the current derivation would miss this layout's own registration from before an
    /// algorithm bump — a registration the drift guard accepts and cleanup is meant to migrate
    /// — and registration would then proceed on top of it. Driven with two versions, since the
    /// live list holds one and could not distinguish the two implementations.
    /// </remarks>
    [TestMethod]
    public void The_Name_Only_Fallback_Probes_Every_Supported_Derivation()
    {
        var layout = SyntheticLayout();
        var basePackageName = AppxLooseLayoutDeployment.PackageName;

        var names = AppxLooseLayoutDeployment.FallbackLookupNames(
            basePackageName, layout, TwoVersions);

        foreach (var version in TwoVersions)
        {
            var derived = WorktreeIdentity.DerivePackageName(basePackageName, layout, version);
            CollectionAssert.Contains(names.ToList(), derived,
                $"The fallback never looks up the name version '{version}' derives, so a " +
                "registration this layout owns under it survives the sweep and registration " +
                "proceeds on top of it.");
        }

        CollectionAssert.Contains(names.ToList(), basePackageName,
            "The base name is what a pre-derivation run of this same checkout registered " +
            "under; dropping it loses the migration case the fallback exists to reach.");

        Assert.AreEqual(names.Count, names.Distinct(StringComparer.Ordinal).Count(),
            "The fallback probes a name more than once, so the enumeration below it does " +
            "redundant work per duplicate.");
    }

    /// <summary>
    /// A contender's total wait must be one timeout for the whole set, not one per version.
    /// </summary>
    /// <remarks>
    /// <para>Each lock used to be given the full timeout, so the worst case was the timeout times
    /// the number of supported versions. That is invisible today — one version — and silently
    /// breaks the single bounded wait <c>AcquireLayoutLock</c> advertises the moment a second is
    /// added, which is exactly when the multi-version path starts being exercised.</para>
    /// <para><b>The staging is load-bearing.</b> A failed path aborts the whole acquisition, so
    /// only one lock can ever time out and simply blocking the first one costs a single budget
    /// under either implementation — that version of this test passes against the defect. The
    /// per-path restart is only observable when an earlier lock is acquired <i>after waiting</i>,
    /// so the first blocker is released mid-wait while the second is held throughout: the fixed
    /// code spends what is left of one budget on the second lock, the defect spends a whole
    /// fresh one.</para>
    /// </remarks>
    [TestMethod]
    public void The_Whole_Lock_Set_Shares_One_Wait_Budget()
    {
        var layout = SyntheticLayout();
        var paths = AppxLooseLayoutDeployment.LockPathsFor(layout, TwoVersions);

        Assert.AreEqual(2, paths.Count, "Precondition: the staging needs two distinct locks.");

        var budget = TimeSpan.FromSeconds(3);
        var releaseAfter = TimeSpan.FromSeconds(3);

        var first = AppxLooseLayoutDeployment.TryOpenLockFile(paths[0], "pid=1");
        using var second = AppxLooseLayoutDeployment.TryOpenLockFile(paths[1], "pid=1");
        Assert.IsNotNull(first, "Precondition: the first blocker must be acquirable.");
        Assert.IsNotNull(second, "Precondition: the second blocker must be acquirable.");

        // Freed mid-wait, so the contender reaches the second lock with most of its budget
        // already spent. Held to the end, so the second wait is what the budget must bound.
        var release = Task.Run(async () =>
        {
            await Task.Delay(releaseAfter);
            first!.Dispose();
        });

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var acquired = AppxLooseLayoutDeployment.TryAcquireAllLocks(
            layout, "pid=2", budget, TimeSpan.FromMilliseconds(50), out var blockedOn, TwoVersions);
        clock.Stop();

        release.GetAwaiter().GetResult();

        Assert.IsNull(acquired, "Precondition: the acquisition must be refused.");
        Assert.AreEqual(paths[1], blockedOn,
            "Precondition: the wait must have ended on the lock that was held throughout.");

        // Anywhere strictly between one budget (correct) and releaseAfter + one budget (a fresh
        // timeout per path) separates the two; the midpoint leaves equal slack for scheduling.
        var ceiling = budget + (releaseAfter / 2);
        Assert.IsTrue(
            clock.Elapsed < ceiling,
            $"Acquiring {paths.Count} version locks took {clock.Elapsed}, past the {ceiling} " +
            $"that separates one shared {budget} budget from a fresh one per lock. A contender's " +
            "real worst case therefore grows with every supported version instead of staying " +
            "the single bounded wait AcquireLayoutLock documents.");
    }

    /// <summary>
    /// Failing to stamp an already-opened lock is a storage fault and must not be retried as
    /// contention.
    /// </summary>
    /// <remarks>
    /// <para>The open succeeding means this process owns the file, so treating a failed write as
    /// "held" can only burn the whole timeout before blaming a contender that never existed.</para>
    /// <para>Also covers the disposal path: the stamp failure blocks the flush too, so
    /// <c>Dispose</c> throws the same <see cref="IOException"/>. Letting that escape the
    /// handler would pre-empt the wrapper and land back in the retry path, which is what an
    /// unguarded <c>stream.Dispose()</c> there did.</para>
    /// </remarks>
    [TestMethod]
    public void A_Lock_That_Opens_But_Cannot_Be_Stamped_Fails_Loudly()
    {
        var path = Path.Join(_root, "range-locked.lock");
        File.WriteAllText(path, new string('x', 64));

        // A reader sharing ReadWrite, holding a byte-range lock: the open is permitted and only
        // the write fails. The failure arrives as an IOException, which is precisely the family
        // the outer handler converts to "held" — a staging that failed some other way would
        // have escaped the old code too and measured nothing.
        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        holder.Lock(0, long.MaxValue);

        try
        {
            // Control: the open must still succeed, or this measures the open path rather than
            // the stamp.
            using (var probe = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                Assert.IsTrue(probe.CanWrite, "Precondition: the staged file must open writable.");
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AppxLooseLayoutDeployment.WaitForLockFile(
                    path, "pid=1", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50));

                Assert.Fail(
                    "A lock that opened but could not be stamped was reported as ordinary " +
                    "contention, so a storage failure is retried until the full layout timeout " +
                    "and then blamed on a competing owner that does not exist.");
            }
            catch (AppxLooseLayoutDeployment.LockStampException)
            {
                clock.Stop();
                Assert.IsTrue(
                    clock.Elapsed < TimeSpan.FromSeconds(10),
                    $"The storage failure surfaced only after {clock.Elapsed}, so it was being " +
                    "retried rather than escaping the poll loop on the first occurrence.");
            }
        }
        finally
        {
            holder.Unlock(0, long.MaxValue);
        }
    }

    /// <summary>
    /// The wait must cover every pass the owner can legitimately make, not just one.
    /// </summary>
    /// <remarks>
    /// The lock is held across a whole batch, and a batch whose filter excludes the identity
    /// guard runs the host a <i>second</i> time to fetch it. Both passes can approach the full
    /// process budget, so a wait sized for one lets a contender give up and report a collision
    /// against an owner that is simply still working — the exact false positive this lock exists
    /// to avoid. Asserted as a strict inequality against two budgets so the fixed margin is
    /// required to survive too; the constants are read rather than restated, so the test still
    /// means this after <c>REACTOR_PACKAGED_TIMEOUT_SECONDS</c> changes the budget.
    /// </remarks>
    [TestMethod]
    public void The_Wait_Budget_Covers_Both_Host_Passes()
    {
        var oneBudget = TimeSpan.FromMilliseconds(PackagedSelfTestBatch.HostTimeoutMs);
        var twoBudgets = oneBudget + oneBudget;
        var actual = AppxLooseLayoutDeployment.LayoutLockTimeoutForTests;

        Assert.IsTrue(
            actual > twoBudgets,
            $"The layout-lock wait is {actual}, which does not exceed the two host passes an " +
            $"owner can legitimately make ({twoBudgets}; one budget is {oneBudget}). A contender " +
            "would abort mid-way through a healthy owner's identity-guard pass and report a " +
            "collision that is not one.");
    }
}
