using Microsoft.VisualStudio.TestTools.UnitTesting;

using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the destructive half of packaged-tier cleanup.
/// </summary>
/// <remarks>
/// <para>These rules decide which existing registrations get removed before this layout
/// registers. A normal packaged run presents only its own clean layout, so it exercises none of
/// the interesting cases: nothing in the tier would fail if these predicates began selecting a
/// live sibling checkout, or stopped reclaiming the legacy and abandoned cases they exist for.
/// The failure mode is silent in both directions, which is why the decision is separated from
/// the <c>PackageManager</c> calls and pinned here.</para>
/// <para>The one that matters most is <see cref="A_Live_Sibling_Checkout_Is_Left_Alone"/>. The
/// original bug was a sweep that matched the shared base name and evicted whatever another
/// checkout had just registered; every other rule exists inside that constraint.</para>
/// </remarks>
[TestClass]
public class RegistrationSelectionTests
{
    private const string Base = "Microsoft.UI.Reactor.PackagedTests.Host";

    private static string Layout => Path.Join(Path.GetTempPath(), "checkout-a", "layout");

    private static string SiblingLayout => Path.Join(Path.GetTempPath(), "checkout-b", "layout");

    /// <summary>Presence probe that answers <c>Present</c> for an explicit set of directories.</summary>
    /// <remarks>Anything not listed is <see cref="LayoutPresence.Absent"/>: provably gone,
    /// which is the only state the reclamation rule may act on.</remarks>
    private static Func<string, LayoutPresence> Live(params string[] dirs) =>
        p => dirs.Contains(p, StringComparer.OrdinalIgnoreCase)
            ? LayoutPresence.Present
            : LayoutPresence.Absent;

    private static RegistrationDisposition Classify(
        RegistrationRecord record,
        Func<string, LayoutPresence>? presence = null,
        Func<string, bool>? isLive = null) =>
        RegistrationSelection.Classify(
            record,
            Layout,
            WorktreeIdentity.DerivePackageName(Base, Layout),
            Base,
            presence ?? Live(Layout, SiblingLayout),
            isLive ?? (_ => false));

    /// <summary>
    /// The regression. Another checkout is running, its package is derived and alive, and its
    /// name shares the base with ours. It must survive.
    /// </summary>
    [TestMethod]
    public void A_Live_Sibling_Checkout_Is_Left_Alone()
    {
        var sibling = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, SiblingLayout), SiblingLayout);

        Assert.AreEqual(
            RegistrationDisposition.Leave,
            Classify(sibling),
            "A live sibling checkout's registration was selected for removal. That is the " +
            "eviction this whole change exists to prevent.");
    }

    /// <summary>Rule 1: an earlier run of this same checkout left its registration behind.</summary>
    [TestMethod]
    public void Our_Own_Previous_Registration_Is_Contending()
    {
        var mine = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, Layout), Layout);

        Assert.AreEqual(RegistrationDisposition.RemoveContending, Classify(mine));
    }

    /// <summary>
    /// Rule 2, the migration case: a registration made under the undecorated base name, from
    /// this layout, before identities were derived. Registering a second package over a
    /// directory another package already claims was observed to kill the host mid-run.
    /// </summary>
    [TestMethod]
    public void A_Legacy_Base_Name_Registration_From_This_Layout_Is_Contending()
    {
        var legacy = new RegistrationRecord(Base, Layout);

        Assert.AreEqual(
            RegistrationDisposition.RemoveContending,
            Classify(legacy),
            "A pre-derivation registration over this exact layout must be reclaimed, not left " +
            "to contend with the registration about to be made.");
    }

    /// <summary>Rule 2 is about the directory, so any name installed there contends.</summary>
    [TestMethod]
    public void An_Unrelated_Name_Installed_From_This_Layout_Is_Contending()
    {
        var squatter = new RegistrationRecord("Some.Other.Package", Layout);

        Assert.AreEqual(RegistrationDisposition.RemoveContending, Classify(squatter));
    }

    /// <summary>Rule 3: a deleted worktree, whose name this algorithm would have produced.</summary>
    [TestMethod]
    public void A_Derived_Package_Whose_Worktree_Is_Gone_Is_Reclaimed()
    {
        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");
        var abandoned = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, gone), gone);

        Assert.AreEqual(
            RegistrationDisposition.ReclaimAbandoned,
            Classify(abandoned),
            "Per-checkout identities trade one shared registration for one per worktree, so " +
            "dead ones must be reclaimed or they accumulate without bound.");
    }

    /// <summary>
    /// The near miss rule 3 must refuse: the name has the derived shape and the directory is
    /// gone, but the name is not the one this algorithm would derive for that path. Matching
    /// the shape alone would remove packages this derivation never produced.
    /// </summary>
    [TestMethod]
    public void A_Derived_Shaped_Name_That_Does_Not_Match_Its_Own_Path_Is_Left_Alone()
    {
        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");
        var elsewhere = Path.Join(Path.GetTempPath(), "some-other-worktree", "layout");

        // Derived shape, plausible suffix, wrong path.
        var impostor = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, elsewhere), gone);

        Assert.AreEqual(
            RegistrationDisposition.Leave,
            Classify(impostor),
            "Only a name this algorithm would have generated for that exact recorded path " +
            "qualifies; matching the <base>.w<suffix> shape alone is not ownership.");
    }

    /// <summary>
    /// A derived package whose directory still exists is live, not abandoned, even though its
    /// name matches its own path exactly. Existence is the whole of rule 3's liveness test.
    /// </summary>
    [TestMethod]
    public void A_Derived_Package_Whose_Worktree_Still_Exists_Is_Left_Alone()
    {
        var sibling = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, SiblingLayout), SiblingLayout);

        Assert.AreEqual(
            RegistrationDisposition.Leave,
            Classify(sibling, Live(Layout, SiblingLayout)));
    }

    /// <summary>
    /// A package whose install location could not be read is not one to reason about. It is
    /// neither this layout's nor provably abandoned, so it must be left alone.
    /// </summary>
    [TestMethod]
    public void A_Package_With_No_Readable_Path_Is_Left_Alone()
    {
        var opaque = new RegistrationRecord($"{Base}.wdeadbeef", InstalledPath: null);

        Assert.AreEqual(RegistrationDisposition.Leave, Classify(opaque));
    }

    /// <summary>
    /// Rule 1's guard: our derived name over a <em>different</em> path is a conflict, not a
    /// removal.
    /// </summary>
    /// <remarks>
    /// The suffix is 40 bits of hashed path, so two checkouts deriving one name is possible,
    /// and a registration left pointing at a path it no longer occupies reaches the same state
    /// with no collision at all. Either way the package under our name is not demonstrably
    /// ours, and it may be backing another agent's live run — removing it would be this file's
    /// original eviction bug reached through our own name. Aborting strands a registration,
    /// which a developer can undo; the alternative cannot be undone.
    /// </remarks>
    [TestMethod]
    public void Our_Derived_Name_From_Another_Path_Aborts_The_Run()
    {
        var confused = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, Layout), SiblingLayout);

        Assert.AreEqual(
            RegistrationDisposition.FailConflicting,
            Classify(confused),
            "A package holding this layout's derived name but installed elsewhere was selected " +
            "for removal. Name equality is not an ownership proof.");
    }

    /// <summary>
    /// The same guard with an unreadable path. Unreadable is not matching: it is the one state
    /// in which nothing at all is known about what would be destroyed.
    /// </summary>
    [TestMethod]
    public void Our_Derived_Name_With_An_Unreadable_Path_Aborts_The_Run()
    {
        var opaque = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, Layout), InstalledPath: null);

        Assert.AreEqual(RegistrationDisposition.FailConflicting, Classify(opaque));
    }

    /// <summary>
    /// A registration made by a <i>previous</i> algorithm version must still be reclaimed once
    /// its worktree is gone. Only version "1" exists today, so this drives the seam with an
    /// explicit two-version list: delete the loop and this goes red, where a test written
    /// against the live list could not.
    /// </summary>
    /// <remarks>
    /// Both versions are deliberately <b>not</b> the live <c>AlgorithmVersion</c>. Using "1" as
    /// the superseded version made the test pass against a loop collapsed to the current version
    /// only — the two branches coincided, and the oracle measured nothing.
    /// </remarks>
    [TestMethod]
    public void A_Previous_Algorithm_Versions_Registration_Is_Reclaimed()
    {
        // Non-const locals on purpose. As `const string` both sides fold at compile time and
        // MSTEST0032 rightly rejects the guard as always-true — which would also mean it could
        // never catch an AlgorithmVersion bump onto one of these values, the one job it has.
        var current = "3";
        var superseded = "2";

        Assert.IsFalse(
            current == WorktreeIdentity.AlgorithmVersion ||
            superseded == WorktreeIdentity.AlgorithmVersion,
            "Precondition: neither probe version may be the live one, or a loop collapsed to " +
            "the current version would satisfy this test by coincidence.");

        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");
        string[] versions = [current, superseded];

        var legacy = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, gone, superseded), gone);

        Assert.AreNotEqual(
            WorktreeIdentity.DerivePackageName(Base, gone, superseded),
            WorktreeIdentity.DerivePackageName(Base, gone, current),
            "Precondition: the version must actually change the derived name, otherwise this " +
            "test would pass without any migration handling at all.");

        Assert.AreEqual(
            RegistrationDisposition.ReclaimAbandoned,
            RegistrationSelection.Classify(
                legacy,
                Layout,
                WorktreeIdentity.DerivePackageName(Base, Layout, current),
                Base,
                Live(Layout, SiblingLayout),
                _ => false,
                versions),
            "A registration from a superseded algorithm version was left behind after its " +
            "worktree disappeared, so a version bump strands every identity the old one made.");
    }

    /// <summary>
    /// Every supported algorithm version must be reclaimable once its worktree is gone.
    /// <c>WorktreeIdentity</c> promises exactly that, and re-deriving with only the current
    /// version would strand everything an earlier version registered.
    /// </summary>
    [TestMethod]
    public void Every_Supported_Algorithm_Version_Is_Reclaimed()
    {
        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");

        Assert.IsTrue(WorktreeIdentity.SupportedAlgorithmVersions.Contains(
            WorktreeIdentity.AlgorithmVersion, StringComparer.Ordinal),
            "The current algorithm version must be listed as supported.");

        foreach (var version in WorktreeIdentity.SupportedAlgorithmVersions)
        {
            var abandoned = new RegistrationRecord(
                WorktreeIdentity.DerivePackageName(Base, gone, version), gone);

            Assert.AreEqual(
                RegistrationDisposition.ReclaimAbandoned,
                Classify(abandoned),
                $"A registration derived by supported algorithm version '{version}' was left " +
                "behind after its worktree disappeared, so bumping the version strands every " +
                "identity the old one produced.");
        }
    }

    /// <summary>
    /// A version deliberately not in the supported list is not ours to reclaim. Fail-closed:
    /// dropping a version means abandoning its leftovers, not guessing at them.
    /// </summary>
    [TestMethod]
    public void An_Unsupported_Algorithm_Version_Is_Left_Alone()
    {
        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");
        const string unsupported = "this-version-does-not-exist";

        Assert.IsFalse(WorktreeIdentity.SupportedAlgorithmVersions.Contains(
            unsupported, StringComparer.Ordinal),
            "Precondition: the probe version must be outside the supported set.");

        var foreign = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, gone, unsupported), gone);

        Assert.AreEqual(RegistrationDisposition.Leave, Classify(foreign));
    }

    /// <summary>
    /// A path that cannot be read is not a path that is gone. An offline share, a dismounted
    /// volume, or a directory this account cannot traverse all make <c>Directory.Exists</c>
    /// answer <see langword="false"/>, and acting on that answer unregisters a package that may
    /// be perfectly alive on a machine that briefly lost sight of it.
    /// </summary>
    [TestMethod]
    public void An_Unreadable_Path_Is_Not_Treated_As_Abandoned()
    {
        var unreachable = Path.Join(Path.GetTempPath(), "offline-share", "layout");

        var abandoned = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, unreachable), unreachable);

        Assert.AreEqual(
            RegistrationDisposition.ReclaimAbandoned,
            Classify(abandoned, _ => LayoutPresence.Absent),
            "Control: with the path provably gone this registration is reclaimable, so the " +
            "Unknown case below differs only in what the probe could establish.");

        Assert.AreEqual(
            RegistrationDisposition.Leave,
            Classify(abandoned, _ => LayoutPresence.Unknown),
            "A path whose state could not be established was reclaimed anyway, so a transient " +
            "outage is enough to unregister a live package.");
    }

    /// <summary>
    /// Deleting a worktree does not stop the run using it. The layout lock lives under
    /// <c>%LOCALAPPDATA%</c> rather than in the worktree precisely so it survives, and a held
    /// lock means the registration is still in use no matter what became of the directory.
    /// </summary>
    [TestMethod]
    public void A_Live_Run_Whose_Worktree_Was_Deleted_Is_Left_Alone()
    {
        var gone = Path.Join(Path.GetTempPath(), "deleted-worktree", "layout");

        var abandoned = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, gone), gone);

        Assert.AreEqual(
            RegistrationDisposition.ReclaimAbandoned,
            Classify(abandoned, isLive: _ => false),
            "Control: with no live owner this registration is reclaimable, so the locked case " +
            "below differs only in the liveness answer.");

        Assert.AreEqual(
            RegistrationDisposition.Leave,
            Classify(abandoned, isLive: _ => true),
            "A registration still locked by a live run was reclaimed, which evicts a running " +
            "packaged host mid-suite.");
    }
}

