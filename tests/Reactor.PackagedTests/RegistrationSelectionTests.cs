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

    /// <summary>Existence probe that answers for an explicit set of live directories.</summary>
    private static Func<string, bool> Live(params string[] dirs) =>
        p => dirs.Contains(p, StringComparer.OrdinalIgnoreCase);

    private static RegistrationDisposition Classify(
        RegistrationRecord record, Func<string, bool>? exists = null) =>
        RegistrationSelection.Classify(
            record,
            Layout,
            WorktreeIdentity.DerivePackageName(Base, Layout),
            Base,
            exists ?? Live(Layout, SiblingLayout));

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
    /// A package sharing our derived name is contending regardless of where it claims to live;
    /// the name is this layout's by construction.
    /// </summary>
    [TestMethod]
    public void Our_Derived_Name_Is_Contending_Even_From_Another_Path()
    {
        var confused = new RegistrationRecord(
            WorktreeIdentity.DerivePackageName(Base, Layout), SiblingLayout);

        Assert.AreEqual(RegistrationDisposition.RemoveContending, Classify(confused));
    }
}
