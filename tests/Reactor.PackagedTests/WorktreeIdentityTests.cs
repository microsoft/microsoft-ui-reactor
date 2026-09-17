using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the per-checkout identity derivation that lets concurrent worktrees run
/// the packaged tier at the same time.
/// </summary>
/// <remarks>
/// <para>The property under test is an absence: two checkouts must not be able to see or evict
/// each other's registration. That cannot be observed directly without registering two real
/// packages, so it is pinned here at the one place it is decided — the derivation — and the
/// registration path is then scoped entirely to derived values.</para>
/// <para>Nothing here touches package state, so it runs at unit speed.</para>
/// </remarks>
[TestClass]
public class WorktreeIdentityTests
{
    private const string Base = AppxLooseLayoutDeployment.PackageName;
    private const string BaseAlias = AppxLooseLayoutDeployment.AliasExeName;

    private static string PathA => Path.Join(Path.GetTempPath(), "reactor-wt-a", "bin", "x64");
    private static string PathB => Path.Join(Path.GetTempPath(), "reactor-wt-b", "bin", "x64");

    // ── The isolation property ─────────────────────────────────────────

    /// <summary>
    /// The whole point. Two checkouts must derive different package names, because every
    /// lookup and every removal in the deployment is scoped to the derived name — so if these
    /// collided, the sweep would be back to evicting a concurrently running checkout.
    /// </summary>
    [TestMethod]
    public void Different_Layouts_Derive_Different_Package_Names()
    {
        Assert.AreNotEqual(
            WorktreeIdentity.DerivePackageName(Base, PathA),
            WorktreeIdentity.DerivePackageName(Base, PathB));
    }

    /// <summary>
    /// Unique package names alone would not be enough. Execution aliases are created at a
    /// single fixed path under <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, so two checkouts
    /// declaring one alias would still fight over one stub, with Windows deciding which binary
    /// the tier actually launches.
    /// </summary>
    [TestMethod]
    public void Different_Layouts_Derive_Different_Alias_Names()
    {
        Assert.AreNotEqual(
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA),
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathB));
    }

    /// <summary>
    /// The derivation has to be a function of the directory and nothing else — no clock, no
    /// counter, no randomness. A rerun that derived a fresh name would leak the previous
    /// registration and leave its alias owned by a layout nothing could remove.
    /// </summary>
    [TestMethod]
    public void Derivation_Is_Stable_Across_Calls()
    {
        Assert.AreEqual(
            WorktreeIdentity.DerivePackageName(Base, PathA),
            WorktreeIdentity.DerivePackageName(Base, PathA));
        Assert.AreEqual(
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA),
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA));
    }

    /// <summary>
    /// The deployment derives from the layout directory it registers; the in-host guard derives
    /// from <c>AppContext.BaseDirectory</c>, which carries a trailing separator and whatever
    /// casing the OS reports. Those are the same directory and must derive the same identity,
    /// or the guard fails against a package that is in fact correct.
    /// </summary>
    [TestMethod]
    public void Casing_And_Trailing_Separator_Do_Not_Change_The_Identity()
    {
        var expected = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, PathA.ToUpperInvariant()));
        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, PathA + Path.DirectorySeparatorChar));
        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, Path.Join(PathA, "..", "x64")));
    }

    // ── MSIX validity ──────────────────────────────────────────────────

    /// <summary>
    /// A derived name that violates the MSIX schema does not fail the derivation, it fails
    /// registration — at which point the tier reports a deployment error rather than the
    /// naming bug that caused it.
    /// </summary>
    [TestMethod]
    public void Derived_Name_Is_A_Valid_Msix_Identity_Name()
    {
        var name = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.IsTrue(
            name.Length is >= WorktreeIdentity.MinPackageNameLength
                       and <= WorktreeIdentity.MaxPackageNameLength,
            $"'{name}' is {name.Length} characters, outside the MSIX 3-50 limit.");

        foreach (var c in name)
        {
            Assert.IsTrue(
                char.IsAsciiLetterOrDigit(c) || c is '.' or '-',
                $"'{name}' contains '{c}', which MSIX Identity/@Name does not allow.");
        }

        Assert.IsFalse(name.EndsWith('.'), $"'{name}' ends with a dot.");
        Assert.IsFalse(name.Contains("..", StringComparison.Ordinal), $"'{name}' has an empty segment.");
    }

    /// <summary>
    /// The base name is kept so a human reading <c>Get-AppxPackage</c> output can still tell
    /// what the package is. This is also what makes the repo-wide cleanup in CI — and the
    /// abandoned-registration rule — able to recognise our packages at all.
    /// </summary>
    [TestMethod]
    public void Derived_Name_Keeps_The_Base_Name_Recognisable()
    {
        var name = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.AreNotEqual(Base, name, "Derivation returned the base name unchanged.");
        Assert.IsTrue(name.StartsWith(Base + ".", StringComparison.Ordinal),
            $"'{name}' does not extend '{Base}'.");
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(name, Base));
    }

    /// <summary>
    /// The real base name fits without truncation today, but it is 39 of the 50 characters
    /// available. A longer one must still produce a legal name rather than an over-length one
    /// that only fails at registration time.
    /// </summary>
    [TestMethod]
    public void An_Over_Long_Base_Name_Is_Truncated_To_Fit()
    {
        var longBase = new string('a', 80);
        var name = WorktreeIdentity.DerivePackageName(longBase, PathA);

        Assert.AreEqual(WorktreeIdentity.MaxPackageNameLength, name.Length);
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(name, longBase));
    }

    /// <summary>Truncation must not leave a trailing dot, which is not a legal segment.</summary>
    [TestMethod]
    public void Truncation_Does_Not_Leave_A_Trailing_Dot()
    {
        // Contrived so that the truncation boundary lands exactly on a '.'.
        var budget = WorktreeIdentity.MaxPackageNameLength - (WorktreeIdentity.SuffixHashLength + 2);
        var awkward = new string('a', budget - 1) + "." + new string('b', 20);

        var name = WorktreeIdentity.DerivePackageName(awkward, PathA);

        Assert.IsFalse(name.Contains("..", StringComparison.Ordinal), $"'{name}' has an empty segment.");
    }

    // ── Alias shape ────────────────────────────────────────────────────

    /// <summary>
    /// The alias is a file name, so the suffix goes on the stem. Appending it after the
    /// extension would produce a stub Windows does not treat as an executable.
    /// </summary>
    [TestMethod]
    public void Derived_Alias_Keeps_A_Single_Exe_Extension()
    {
        var alias = WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA);

        Assert.AreEqual(".exe", Path.GetExtension(alias));
        Assert.AreEqual(1, alias.Split('.').Length - 1, $"'{alias}' has more than one extension.");
        Assert.AreNotEqual(BaseAlias, alias, "Derivation returned the base alias unchanged.");
        Assert.IsTrue(alias.StartsWith("reactor-packaged-test-host-", StringComparison.Ordinal));
    }

    // ── IsDerivedFrom: used to decide what may be removed ──────────────

    /// <summary>
    /// This predicate gates the abandoned-registration rule, so a false positive would remove
    /// a package that is not ours at all.
    /// </summary>
    [TestMethod]
    public void IsDerivedFrom_Rejects_Unrelated_And_Near_Miss_Names()
    {
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(Base, Base), "The base name itself is ours.");

        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom("Contoso.Something", Base));
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".Extra", Base),
            "A real sibling package is not a derived identity.");
        // Each of the three below isolates one rule, using characters that are otherwise
        // legal, so a passing assertion means that rule fired and not an unrelated one.
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".xabcdefgh", Base),
            "A suffix with the wrong marker is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".wabcdefg", Base),
            "A short hash is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".wabcdefg!", Base),
            "A hash outside the encoding alphabet is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".w", Base),
            "A suffix without a hash is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(string.Empty, Base));
    }

    // ── The deployment actually uses it ────────────────────────────────

    /// <summary>
    /// Pins the derivation to the type that registers packages, not just to the helper. A
    /// deployment that computed its identity some other way would leave every test above true
    /// and the tier still unable to run concurrently.
    /// </summary>
    [TestMethod]
    public void Deployment_Scopes_Its_Identity_To_Its_Layout_Directory()
    {
        var a = new AppxLooseLayoutDeployment(PathA);
        var b = new AppxLooseLayoutDeployment(PathB);

        Assert.AreEqual(WorktreeIdentity.DerivePackageName(Base, PathA), a.EffectivePackageName);
        Assert.AreEqual(WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA), a.EffectiveAliasExeName);

        Assert.AreNotEqual(a.EffectivePackageName, b.EffectivePackageName);
        Assert.AreNotEqual(a.EffectiveAliasExeName, b.EffectiveAliasExeName);
    }
}
