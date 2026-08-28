using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the packaged tier's own plumbing (issue #1148).
/// </summary>
/// <remarks>
/// The harness is the thing deciding whether the tier reports green, so leaving it untested
/// would mean the tier's verdict rests on unverified code. This mirrors how the unpackaged
/// tier covers the same class of logic — <c>Reactor.SelfTests/SkipReportingTests.cs</c> for
/// the skip-vs-pass distinction (issue #1061) and
/// <c>Reactor.SelfTests/SuiteBudgetDiagnosticsTests.cs</c> for the timeout-override parsing.
/// <para>Nothing here launches a process or touches package state, so these run at unit
/// speed alongside the packaged run.</para>
/// </remarks>
[TestClass]
public class PackagedHarnessTests
{
    private static Dictionary<string, PackagedSelfTestBatch.FixtureOutcome> Parse(
        string tap, params string[] known)
    {
        var map = new Dictionary<string, PackagedSelfTestBatch.FixtureOutcome>(StringComparer.Ordinal);
        PackagedSelfTestBatch.ParseTap(tap, map, known.Length == 0 ? null : known);
        return map;
    }

    // ── ParseTap: the three-state verdict ──────────────────────────────

    [TestMethod]
    public void Fixture_With_A_Real_Check_Passes()
    {
        var map = Parse("# Running: F\nok F_Check\n# Total failures: 0\n");
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["F"].Status);
    }

    /// <summary>
    /// The regression that issue #1061 documents for the unpackaged parser: a skip is
    /// emitted as a line starting <c>ok </c>, so a parser that counts it as an assertion
    /// reports a fixture that measured nothing as green.
    /// </summary>
    [TestMethod]
    public void Fixture_Whose_Only_Output_Is_A_Skip_Is_Not_Passed()
    {
        var map = Parse("# Running: F\nok F_Check # SKIP needs identity\n# Total failures: 0\n");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Skipped, map["F"].Status);
        StringAssert.Contains(map["F"].Detail, "needs identity");
    }

    /// <summary>Differential partner to the test above — same fixture, one real check added.</summary>
    [TestMethod]
    public void Fixture_With_A_Skip_And_A_Real_Check_Passes()
    {
        var map = Parse("# Running: F\nok F_Skipped # SKIP nope\nok F_Real\n# Total failures: 0\n");
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["F"].Status);
    }

    [TestMethod]
    public void Failure_Beats_Passing_Checks_In_The_Same_Fixture()
    {
        var map = Parse("# Running: F\nok F_A\nnot ok F_B - assertion failed\nok F_C\n");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["F"].Status);
        StringAssert.Contains(map["F"].Detail, "F_B");
    }

    [TestMethod]
    public void Fixture_That_Emitted_Nothing_Is_Skipped_Not_Passed()
    {
        var map = Parse("# Running: F\n# Fixture time: F 3\n");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Skipped, map["F"].Status);
        Assert.AreEqual("no checks ran", map["F"].Detail);
    }

    [TestMethod]
    public void Checks_Are_Attributed_To_The_Fixture_That_Emitted_Them()
    {
        var map = Parse(
            "# Running: A\nok A_Check\n# Running: B\nnot ok B_Check - assertion failed\n");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["A"].Status);
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["B"].Status);
    }

    [TestMethod]
    public void Crlf_Line_Endings_Are_Handled()
    {
        var map = Parse("# Running: F\r\nok F_Check\r\n");
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["F"].Status);
    }

    // ── ParseTap: runner-level attribution ─────────────────────────────

    /// <summary>
    /// The runner can report on a fixture it never started, with no <c># Running:</c> marker
    /// of its own. Without the known-fixture set that line lands in whichever bucket happens
    /// to be open, reddening an innocent neighbour while the real fixture looks untouched.
    /// </summary>
    [TestMethod]
    public void Runner_Level_Failure_Is_Attributed_To_The_Fixture_It_Names()
    {
        var map = Parse(
            "# Running: A\nok A_Check\nnot ok 2 B_CRASH - InvalidOperationException: boom\n",
            "A", "B");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["A"].Status,
            "A emitted a passing check and must not inherit B's crash.");
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["B"].Status);
        StringAssert.Contains(map["B"].Detail, "boom");
    }

    [TestMethod]
    public void Runner_Level_Timeout_Is_Attributed_To_The_Fixture_It_Names()
    {
        var map = Parse("# Running: A\nok A_Check\nnot ok 7 B_TIMEOUT\n", "A", "B");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["A"].Status);
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["B"].Status);
    }

    [TestMethod]
    public void Runner_Level_Skip_Is_Attributed_To_The_Fixture_It_Names()
    {
        var map = Parse("# Running: A\nok A_Check\nok 2 B # SKIP declined to run\n", "A", "B");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Passed, map["A"].Status);
        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Skipped, map["B"].Status);
    }

    /// <summary>
    /// Positive control for the attribution logic: with no known-fixture set the same stream
    /// mis-attributes, which is what proves the tests above are measuring the fix rather than
    /// a property the parser had anyway.
    /// </summary>
    [TestMethod]
    public void Without_Known_Fixtures_The_Same_Stream_Misattributes()
    {
        var map = Parse("# Running: A\nok A_Check\nnot ok 2 B_CRASH - boom\n");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["A"].Status);
        Assert.IsFalse(map.ContainsKey("B"));
    }

    [TestMethod]
    public void A_Fixtures_Own_Checks_Are_Not_Treated_As_Runner_Level()
    {
        // "A_Check" is not a known fixture name, so it must stay a check of A.
        var map = Parse("# Running: A\nnot ok A_Check - assertion failed\n", "A", "B");

        Assert.AreEqual(PackagedSelfTestBatch.FixtureStatus.Failed, map["A"].Status);
        Assert.IsFalse(map.ContainsKey("B"));
    }

    // ── Filter resolution ──────────────────────────────────────────────

    /// <summary>
    /// The whole corpus by default: a fixture only establishes anything about package
    /// identity if it actually runs under it.
    /// </summary>
    [TestMethod]
    public void Filter_Defaults_To_The_Whole_Corpus() =>
        Assert.IsNull(PackagedSelfTestBatch.ResolveFilter(null));

    [TestMethod]
    public void Explicit_Filter_Is_Trimmed_And_Honoured() =>
        Assert.AreEqual("Flex", PackagedSelfTestBatch.ResolveFilter("  Flex  "));

    [TestMethod]
    public void Blank_Explicit_Filter_Falls_Back_To_The_Whole_Corpus() =>
        Assert.IsNull(PackagedSelfTestBatch.ResolveFilter("   "));

    // ── Identity-guard inclusion ───────────────────────────────────────

    [TestMethod]
    public void Default_Filter_Selects_The_Identity_Guard() =>
        Assert.IsTrue(PackagedSelfTestBatch.FilterSelects(
            PackagedSelfTestBatch.ResolveFilter(null), "Packaged_IdentityGuard"));

    /// <summary>
    /// A perfectly reasonable custom filter can exclude the guard. That must be detectable,
    /// because the guard is the only thing establishing that the run had package identity —
    /// the harness answers it by fetching the guard in a second pass rather than either
    /// running the subset unguarded or failing for the guard's absence.
    /// </summary>
    [TestMethod]
    public void A_Narrow_Custom_Filter_Excludes_The_Identity_Guard() =>
        Assert.IsFalse(PackagedSelfTestBatch.FilterSelects(
            PackagedSelfTestBatch.ResolveFilter("SettingsStore"), "Packaged_IdentityGuard"));

    [TestMethod]
    public void Filter_Selection_Is_Case_Insensitive() =>
        Assert.IsTrue(PackagedSelfTestBatch.FilterSelects("packaged_identityguard", "Packaged_IdentityGuard"));

    // ── Timeout resolution ─────────────────────────────────────────────

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("nonsense")]
    [DataRow("9.5")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("99999999999999")]
    public void Malformed_Timeout_Overrides_Fall_Back_To_The_Default(string? value)
    {
        // A 0 or negative budget would kill the host instantly, and an overflowing one lands
        // as a negative millisecond delay — both worse than the default they replace.
        var fallback = PackagedSelfTestBatch.ResolveTimeoutSeconds(null);
        Assert.AreEqual(fallback, PackagedSelfTestBatch.ResolveTimeoutSeconds(value));
        Assert.IsTrue(fallback > 0);
    }

    [TestMethod]
    public void Valid_Timeout_Override_Is_Honoured() =>
        Assert.AreEqual(1500, PackagedSelfTestBatch.ResolveTimeoutSeconds("1500"));

    [TestMethod]
    public void Timeout_Override_Is_Trimmed() =>
        Assert.AreEqual(120, PackagedSelfTestBatch.ResolveTimeoutSeconds(" 120 "));

    // ── Fixture-list parsing ───────────────────────────────────────────

    [TestMethod]
    public void Fixture_List_Drops_Comments_And_Blanks()
    {
        var names = PackagedSelfTestBatch.ParseFixtureList("# note\nAlpha\n\n  Beta  \n", null);
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, names);
    }

    [TestMethod]
    public void Fixture_List_Filter_Matches_Case_Insensitive_Substring()
    {
        // Mirrors SelfTestRunner's own Contains(filter, OrdinalIgnoreCase) semantics, so the
        // set enumerated here cannot drift from the set the host actually runs.
        var names = PackagedSelfTestBatch.ParseFixtureList(
            "Packaged_One\nFlex_Two\npackaged_three\n", "Packaged_");

        CollectionAssert.AreEqual(new[] { "Packaged_One", "packaged_three" }, names);
    }

    // ── Host-directory override ────────────────────────────────────────

    /// <summary>
    /// A relative override has to come back absolute: the value reaches
    /// <c>RegisterPackageAsync</c> as <c>new Uri(manifestPath)</c>, which rejects a relative
    /// path, so a valid-but-relative layout directory would fail for a reason with nothing
    /// to do with the layout.
    /// </summary>
    [TestMethod]
    public void Relative_Host_Dir_Override_Is_Absolutised()
    {
        var resolved = AppxLooseLayoutDeployment.ResolveOverrideDirectory(".");

        Assert.IsTrue(Path.IsPathRooted(resolved), $"Expected an absolute path, got '{resolved}'.");
        Assert.IsTrue(
            Uri.TryCreate(Path.Join(resolved, "AppxManifest.xml"), UriKind.Absolute, out _),
            "The resolved directory must produce an absolute manifest URI.");
    }

    [TestMethod]
    public void Absolute_Host_Dir_Override_Is_Preserved()
    {
        var absolute = Path.GetFullPath(AppContext.BaseDirectory);
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(absolute),
            Path.TrimEndingDirectorySeparator(
                AppxLooseLayoutDeployment.ResolveOverrideDirectory(absolute)));
    }

    [TestMethod]
    public void Missing_Host_Dir_Override_Is_Rejected() =>
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            AppxLooseLayoutDeployment.ResolveOverrideDirectory(
                Path.Join(AppContext.BaseDirectory, "definitely-not-a-real-layout-dir")));

    /// <summary>
    /// The documented shape of this override is a repo-relative path, but the test host's
    /// working directory is its own binary folder — so without the repo-root fallback the
    /// intuitive value never resolves.
    /// </summary>
    [TestMethod]
    public void Repo_Relative_Host_Dir_Override_Resolves_Against_The_Repo_Root()
    {
        var repoRoot = AppxLooseLayoutDeployment.TryFindRepoRoot();
        Assert.IsNotNull(repoRoot, "Could not find repo root (Reactor.slnx).");

        var resolved = AppxLooseLayoutDeployment.ResolveOverrideDirectory(
            Path.Join("tests", "Reactor.PackagedTests.Host"), repoRoot);

        Assert.IsTrue(Path.IsPathRooted(resolved));
        Assert.IsTrue(File.Exists(Path.Join(resolved, "Package.appxmanifest")),
            $"Expected the packaged host project directory, got '{resolved}'.");
    }

    /// <summary>Positive control: the same value does not resolve without the repo root.</summary>
    [TestMethod]
    public void Repo_Relative_Override_Fails_Without_The_Repo_Root() =>
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            AppxLooseLayoutDeployment.ResolveOverrideDirectory(
                Path.Join("tests", "Reactor.PackagedTests.Host")));

    // ── Manifest / constant parity ─────────────────────────────────────

    /// <summary>
    /// The package identity, publisher and execution alias are spelled once in the manifest
    /// and again as C# constants, with nothing linking them at compile time.
    /// </summary>
    /// <remarks>
    /// Drift is silent in the direction that matters: registration still succeeds, but
    /// lookups by name find nothing, so cleanup no-ops and a stale layout keeps owning the
    /// alias for the next run. <c>Register()</c> now fails fast on that, and this test
    /// catches it without needing Developer Mode or a registration at all.
    /// </remarks>
    [TestMethod]
    public void Deployment_Constants_Match_The_Manifest()
    {
        var manifestPath = Path.Join(
            RepoRoot(), "tests", "Reactor.PackagedTests.Host", "Package.appxmanifest");
        Assert.IsTrue(File.Exists(manifestPath), $"Manifest not found: {manifestPath}");

        var doc = XDocument.Load(manifestPath);
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";

        var identity = doc.Root!.Element(foundation + "Identity");
        Assert.IsNotNull(identity, "Manifest has no Identity element.");

        Assert.AreEqual(
            AppxLooseLayoutDeployment.PackageName,
            identity!.Attribute("Name")!.Value,
            "AppxLooseLayoutDeployment.PackageName drifted from Identity/@Name.");

        Assert.AreEqual(
            AppxLooseLayoutDeployment.PackagePublisher,
            identity.Attribute("Publisher")!.Value,
            "AppxLooseLayoutDeployment.PackagePublisher drifted from Identity/@Publisher.");

        var alias = doc.Descendants(uap5 + "ExecutionAlias").SingleOrDefault();
        Assert.IsNotNull(alias,
            "Manifest declares no uap5:ExecutionAlias. The tier launches the host through that " +
            "alias precisely because it is the only route that keeps package identity while " +
            "still inheriting stdout.");

        Assert.AreEqual(
            AppxLooseLayoutDeployment.AliasExeName,
            alias!.Attribute("Alias")!.Value,
            "AppxLooseLayoutDeployment.AliasExeName drifted from the manifest's ExecutionAlias.");
    }

    /// <summary>
    /// The host-side copies of the same two identifiers — the package name the identity guard
    /// compares <c>Package.Current.Id.Name</c> against, and the assembly name the tier gate
    /// keys off — live in a different assembly this project deliberately does not reference,
    /// so they are checked against their source of truth by reading the files.
    /// </summary>
    /// <remarks>
    /// Reading the source rather than the compiled constant is the point: the tier gate
    /// compares <c>Assembly.GetEntryAssembly()</c> to a string literal, so if the host's
    /// <c>AssemblyName</c> is ever renamed the gate silently stops matching, every
    /// identity-dependent fixture skips, and only the shim's skip guard would catch it — at
    /// runtime, on a machine with Developer Mode. This catches it at unit speed.
    /// </remarks>
    [TestMethod]
    public void Host_Side_Identity_Constants_Match_Their_Sources()
    {
        var root = RepoRoot();

        var fixtureSource = File.ReadAllText(Path.Join(
            root, "tests", "Reactor.AppTests.Host", "SelfTest", "Fixtures",
            "PackagedIdentityFixtures.cs"));

        var packageIdentityName = ConstValue(fixtureSource, "PackageIdentityName");
        Assert.AreEqual(
            AppxLooseLayoutDeployment.PackageName,
            packageIdentityName,
            "PackagedIdentityFixtures.PackageIdentityName drifted from " +
            "AppxLooseLayoutDeployment.PackageName; Packaged_IdentityGuard would fail against a " +
            "correctly registered package.");

        var hostAssemblyName = ConstValue(fixtureSource, "PackagedHostAssemblyName");
        var hostProject = File.ReadAllText(Path.Join(
            root, "tests", "Reactor.PackagedTests.Host", "Reactor.PackagedTests.Host.csproj"));
        var declaredAssemblyName = XDocument.Parse(hostProject)
            .Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault();

        Assert.AreEqual(
            declaredAssemblyName,
            hostAssemblyName,
            "PackagedIdentityFixtures.PackagedHostAssemblyName drifted from the packaged host's " +
            "<AssemblyName>. The tier gate keys off that name, so every identity-dependent " +
            "fixture would silently skip inside the packaged tier.");
    }

    /// <summary>Extracts the value of a <c>const string NAME = "value";</c> declaration.</summary>
    private static string ConstValue(string source, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            source, $@"const\s+string\s+{System.Text.RegularExpressions.Regex.Escape(name)}\s*=\s*""([^""]*)""");

        Assert.IsTrue(match.Success, $"Could not find `const string {name}` in the host source.");
        return match.Groups[1].Value;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);

        Assert.IsNotNull(dir, "Could not find repo root (Reactor.slnx).");
        return dir!;
    }
}
