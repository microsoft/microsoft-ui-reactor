using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #1068 — which <c>Reactor.xml</c> Phase 5.7 reads, and whether it
/// admits to being older than the source it documents.
/// </summary>
/// <remarks>
/// <para>
/// The defect was that <c>FindReactorXml</c> returned the first candidate in a
/// fixed <c>Debug</c>-then-<c>Release</c> sweep, so a leftover Debug build
/// shadowed a fresh Release one and the generator rewrote
/// <c>docs/guide/reference/**</c> from stale input while exiting 0.
/// </para>
/// <para>
/// Direction is the whole design of this file. A newest-wins test whose fixture
/// happens to put the newest file in <c>Debug</c> passes against the *buggy*
/// code — it asserts nothing, because both rules agree there. Every ordering
/// claim below is therefore made twice, once in each direction, so exactly one
/// half is the discriminating case and the other half guards the
/// over-correction (a fixed <c>Release</c>-first order would satisfy the first
/// half alone).
/// </para>
/// <para>
/// Mutation results, measured rather than asserted — each row lists every test
/// that actually reddened, which is not always the set the fix "obviously"
/// protects:
/// </para>
/// <list type="table">
///   <item><term>Restore the config-order sweep (the original bug)</term>
///     <description><see cref="Newest_wins_when_the_newer_build_is_Release"/>,
///     <see cref="Flat_layout_beats_an_older_platform_stamped_build"/>,
///     <see cref="Ordinal_order_does_not_override_a_newer_timestamp"/>,
///     <see cref="The_selection_reports_the_timestamp_and_how_many_it_beat"/>
///     and all three
///     <see cref="Layouts_outside_the_old_hardcoded_shapes_are_discovered"/>
///     cases. Note what stays green: the two fixtures whose newest file sits in
///     <c>Debug</c>, exactly as predicted.</description></item>
///   <item><term>Restore the hardcoded layout shapes</term>
///     <description>the three
///     <see cref="Layouts_outside_the_old_hardcoded_shapes_are_discovered"/>
///     cases.</description></item>
///   <item><term>Drop the ordinal tie-break</term>
///     <description><see cref="Equal_timestamps_break_on_the_ordinal_path"/> —
///     but only in its present form; see that test's own note.</description></item>
///   <item><term>Drop the bin/obj exclusion</term>
///     <description><see cref="Build_output_under_src_Reactor_does_not_count_as_source"/>.</description></item>
///   <item><term>Loosen the staleness comparison to <c>&lt;</c></term>
///     <description><see cref="Source_at_the_same_instant_is_not_stale"/>.</description></item>
///   <item><term>Segment match → prefix match for build output</term>
///     <description><see cref="A_source_directory_whose_name_merely_starts_with_bin_still_counts"/>.</description></item>
///   <item><term>Drop the <c>ReparsePoint</c> skip on the candidate walk</term>
///     <description><see cref="A_junction_nested_in_bin_is_not_followed_out_of_the_tree"/>.
///     Added after that mutation came back green — the hardening had shipped
///     with nothing pinning it.</description></item>
/// </list>
/// <para>
/// These all drive the helpers directly. What they cannot show is that Phase
/// 5.7 still calls them — see <see cref="ReferenceStalenessWiringTests"/>, which
/// runs the real command.
/// </para>
/// </remarks>
public class ReactorXmlSelectionTests
{
    /// <summary>
    /// Fixed clock. Timestamps are asserted against each other, never against
    /// "now", so nothing here depends on how long the suite takes to run.
    /// </summary>
    private static readonly DateTime Base = new(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc);

    // ── Selection ────────────────────────────────────────────────────────────

    /// <summary>
    /// The discriminating half: a fresh Release build must win over a stale
    /// Debug one. This is the reported scenario — build Release only, compile
    /// docs, get pages generated from an older Debug tree.
    /// </summary>
    [Fact]
    public void Newest_wins_when_the_newer_build_is_Release()
    {
        using var fx = new RepoFixture();
        var debug = fx.Xml("Debug/net10.0-windows10.0.22621.0", Base);
        var release = fx.Xml("Release/net10.0-windows10.0.22621.0", Base.AddMinutes(16));

        Assert.Equal(release, CompileCommand.FindReactorXml(fx.Root)?.Path);
        Assert.NotEqual(debug, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// The other half. Debug is not disfavoured — it wins when it is newer, so
    /// the rule is freshness and not a reversed configuration preference.
    /// </summary>
    [Fact]
    public void Newest_wins_when_the_newer_build_is_Debug()
    {
        using var fx = new RepoFixture();
        var debug = fx.Xml("Debug/net10.0-windows10.0.22621.0", Base.AddMinutes(16));
        fx.Xml("Release/net10.0-windows10.0.22621.0", Base);

        Assert.Equal(debug, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// The layout the issue's repro actually selected on a real tree:
    /// <c>bin/&lt;arch&gt;/&lt;config&gt;/&lt;tfm&gt;/</c>. Reproduced with the
    /// arch-stamped tree as the *newer* one so a correct answer here requires
    /// both reaching that shape and comparing timestamps.
    /// </summary>
    [Fact]
    public void Platform_stamped_layout_beats_a_flat_Release_build()
    {
        using var fx = new RepoFixture();
        fx.Xml("Release/net10.0-windows10.0.22621.0", Base.AddMinutes(12));
        var stamped = fx.Xml("x64/Debug/net10.0-windows10.0.22621.0", Base.AddMinutes(20));

        Assert.Equal(stamped, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// And the inverse, so the arch-stamped path is not merely preferred: a
    /// newer flat build beats an older arch-stamped one.
    /// </summary>
    [Fact]
    public void Flat_layout_beats_an_older_platform_stamped_build()
    {
        using var fx = new RepoFixture();
        var flat = fx.Xml("Release/net10.0-windows10.0.22621.0", Base.AddMinutes(20));
        fx.Xml("x64/Debug/net10.0-windows10.0.22621.0", Base.AddMinutes(12));

        Assert.Equal(flat, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// The enumeration used to hard-code two directory shapes and an arch
    /// allow-list of <c>x64</c>/<c>ARM64</c>. Anything else — a future
    /// architecture, a RID-nested publish output — was invisible, which is the
    /// same "incomplete enumeration, confident answer" failure as the ordering
    /// bug itself.
    /// </summary>
    /// <remarks>
    /// The stale x64 build present alongside is the positive control: without
    /// it, a hard-coded enumerator would return <c>null</c> and the test would
    /// have to assert on a no-match, which proves nothing. With it, the broken
    /// implementation returns a *specific wrong file* instead.
    /// </remarks>
    [Theory]
    [InlineData("x86/Release/net10.0-windows10.0.22621.0")]
    [InlineData("x64/Release/net10.0-windows10.0.22621.0/win-x64")]
    [InlineData("x64/Release/net10.0-windows10.0.22621.0/publish")]
    public void Layouts_outside_the_old_hardcoded_shapes_are_discovered(string layout)
    {
        using var fx = new RepoFixture();
        var stale = fx.Xml("x64/Debug/net10.0-windows10.0.22621.0", Base);
        var newest = fx.Xml(layout, Base.AddMinutes(30));

        var selected = CompileCommand.FindReactorXml(fx.Root)?.Path;

        Assert.Equal(newest, selected);
        Assert.NotEqual(stale, selected);
    }

    [Fact]
    public void No_bin_directory_selects_nothing()
    {
        using var fx = new RepoFixture();
        fx.Source("Core/Widget.cs", Base);

        Assert.Null(CompileCommand.FindReactorXml(fx.Root));
        Assert.Empty(CompileCommand.EnumerateReactorXmlCandidates(fx.Root));
    }

    /// <summary>
    /// A populated <c>bin</c> that happens to contain no <c>Reactor.xml</c> is
    /// still "nothing to read" — distinct from the case above, because it
    /// exercises the enumeration rather than the existence guard.
    /// </summary>
    [Fact]
    public void Bin_without_a_Reactor_xml_selects_nothing()
    {
        using var fx = new RepoFixture();
        fx.BinFile("Debug/net10.0-windows10.0.22621.0", "Reactor.Cli.xml", Base);
        fx.BinFile("Debug/net10.0-windows10.0.22621.0", "Reactor.dll", Base);

        Assert.Null(CompileCommand.FindReactorXml(fx.Root));
    }

    /// <summary>
    /// The timestamp and candidate count are the printed diagnostic, so they
    /// are load-bearing rather than incidental: an operator reading
    /// <c>newest of 1 candidate(s)</c> on a tree they know has three builds is
    /// being told the enumeration is broken.
    /// </summary>
    [Fact]
    public void The_selection_reports_the_timestamp_and_how_many_it_beat()
    {
        using var fx = new RepoFixture();
        fx.Xml("Debug/net10.0-windows10.0.22621.0", Base);
        fx.Xml("x64/Debug/net10.0-windows10.0.22621.0", Base.AddMinutes(4));
        var newest = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(9));

        var choice = CompileCommand.FindReactorXml(fx.Root);

        Assert.NotNull(choice);
        Assert.Equal(newest, choice!.Value.Path);
        Assert.Equal(Base.AddMinutes(9), choice.Value.WriteUtc);
        Assert.Equal(3, choice.Value.CandidateCount);
    }

    /// <summary>
    /// A junction nested inside <c>bin</c> must not be descended into: a file
    /// reached through one is not this build's output, and a junction that
    /// points back at an ancestor has no cycle detection in the runtime's
    /// walker.
    /// </summary>
    /// <remarks>
    /// The first two assertions are the positive control and they are what make
    /// the third a measurement. A junction that silently failed to be created
    /// would leave the "not a candidate" assertion passing for the wrong reason,
    /// so the test first proves the target really is reachable through the link
    /// — i.e. that the default walk *would* have found it.
    /// </remarks>
    [Fact]
    public void A_junction_nested_in_bin_is_not_followed_out_of_the_tree()
    {
        using var fx = new RepoFixture();
        var real = fx.Xml("x64/Debug/net10.0-windows10.0.22621.0", Base);

        // Newer than the real candidate, so following the link would change the
        // answer rather than merely add a tie.
        var outside = fx.OutsideXml("elsewhere", Base.AddMinutes(45));
        var linked = fx.JunctionInsideBin("linked", Path.GetDirectoryName(outside)!);

        Assert.True(File.Exists(Path.Combine(linked, "Reactor.xml")),
            "fixture: the junction must resolve, or the exclusion below proves nothing");
        Assert.Equal(Base.AddMinutes(45), File.GetLastWriteTimeUtc(Path.Combine(linked, "Reactor.xml")));

        var candidates = CompileCommand.EnumerateReactorXmlCandidates(fx.Root).ToList();

        Assert.Equal(new[] { real }, candidates);
        Assert.Equal(real, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// Skipping reparse points must not disqualify the enumeration root itself
    /// — redirecting <c>bin</c> to another volume is a normal thing to do, and
    /// it would be a poor trade to break it while hardening against nested
    /// links.
    /// </summary>
    [Fact]
    public void A_bin_directory_that_is_itself_a_junction_still_enumerates()
    {
        using var fx = new RepoFixture();
        var expected = fx.XmlBehindJunctionedBin("x64/Release/net10.0-windows10.0.22621.0", Base);

        var candidates = CompileCommand.EnumerateReactorXmlCandidates(fx.Root).ToList();

        Assert.Single(candidates);
        Assert.Equal(expected, CompileCommand.FindReactorXml(fx.Root)?.Path);
    }

    /// <summary>
    /// Equal timestamps must not leave the choice to enumeration order, which
    /// the filesystem does not promise to keep stable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven through <see cref="CompileCommand.SelectNewest"/> with the same
    /// two files in both input orders, because that is the only form of this
    /// test that can fail. The first attempt went through
    /// <c>FindReactorXml</c> and asserted the ordinal-least path: it passed —
    /// and it passed just as happily with the <c>ThenBy</c> deleted, because
    /// the directory walk handed the sorter its candidates in an order that
    /// already agreed. Feeding both orders is what turns "the answer looked
    /// right once" into "order of arrival does not decide it".
    /// </para>
    /// <para>
    /// The expectation is computed rather than memorised so the assertion
    /// states the rule and not a path that happens to sort that way today.
    /// </para>
    /// </remarks>
    [Fact]
    public void Equal_timestamps_break_on_the_ordinal_path()
    {
        using var fx = new RepoFixture();
        var debug = fx.Xml("Debug/net10.0-windows10.0.22621.0", Base);
        var release = fx.Xml("Release/net10.0-windows10.0.22621.0", Base);

        // Positive control on the fixture: the tie-break can only be exercised
        // if the filesystem actually recorded a tie. Without this, a rig that
        // failed to make them equal would look like a defect in SelectNewest.
        Assert.Equal(File.GetLastWriteTimeUtc(debug), File.GetLastWriteTimeUtc(release));

        var expected = string.CompareOrdinal(debug, release) <= 0 ? debug : release;

        Assert.Equal(expected, CompileCommand.SelectNewest(new[] { debug, release }));
        Assert.Equal(expected, CompileCommand.SelectNewest(new[] { release, debug }));
    }

    /// <summary>
    /// The tie-break is a tie-break, not the primary key: an older file that
    /// sorts earlier must still lose. Without this, "always return the
    /// ordinal-least path" would satisfy the test above.
    /// </summary>
    [Fact]
    public void Ordinal_order_does_not_override_a_newer_timestamp()
    {
        using var fx = new RepoFixture();
        var debug = fx.Xml("Debug/net10.0-windows10.0.22621.0", Base);
        var release = fx.Xml("Release/net10.0-windows10.0.22621.0", Base.AddMinutes(1));

        Assert.True(string.CompareOrdinal(debug, release) < 0, "fixture assumes Debug sorts first");
        Assert.Equal(release, CompileCommand.SelectNewest(new[] { debug, release }));
    }

    [Fact]
    public void Selecting_from_nothing_yields_nothing()
    {
        Assert.Null(CompileCommand.SelectNewest(Array.Empty<string>()));
    }

    // ── Staleness (REACTOR_DOC_REFGEN_W002) ──────────────────────────────────

    /// <summary>
    /// The residue newest-wins cannot reach: every build is stale because
    /// nothing was rebuilt after the edit. Selection is correct and the output
    /// is still wrong, so the only signal available is the comparison against
    /// source.
    /// </summary>
    [Fact]
    public void Source_newer_than_the_xml_is_reported_as_stale()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base);
        fx.Source("Core/Widget.cs", Base.AddMinutes(5));

        var finding = CompileCommand.BuildStaleXmlFinding(fx.Root, xml);

        Assert.NotNull(finding);
        Assert.Equal("REACTOR_DOC_REFGEN_W002", finding!.Code);
        Assert.Equal(TierLintSeverity.Warning, finding.Severity);
        // Both operands named, so the reader can act without re-deriving them.
        Assert.Contains("src/Reactor/Core/Widget.cs", finding.Message, StringComparison.Ordinal);
        Assert.Contains("2026-08-01T19:00:00Z", finding.Message, StringComparison.Ordinal);
        Assert.Contains("2026-08-01T19:05:00Z", finding.Message, StringComparison.Ordinal);
        Assert.Contains("src/Reactor/bin/x64/Release", finding.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_newer_than_every_source_is_not_stale()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(5));
        fx.Source("Core/Widget.cs", Base);
        fx.Source("Elements/Dsl.cs", Base.AddMinutes(4));

        Assert.Null(CompileCommand.BuildStaleXmlFinding(fx.Root, xml));
    }

    /// <summary>
    /// Boundary. The comparison is strict, so a source file stamped at exactly
    /// the XML's instant does not fire — it did not postdate the build.
    /// </summary>
    [Fact]
    public void Source_at_the_same_instant_is_not_stale()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base);
        fx.Source("Core/Widget.cs", Base);

        Assert.Null(CompileCommand.BuildStaleXmlFinding(fx.Root, xml));
    }

    /// <summary>
    /// Generated code under <c>obj</c> and copied sources under <c>bin</c> are
    /// written *by* the build whose age is in question, so counting them would
    /// fire the warning after every successful build.
    /// </summary>
    /// <remarks>
    /// The second half is the positive control, and it is what makes the first
    /// half a measurement: it plants a real source file at the *same* newer
    /// timestamp and shows the probe does fire there. Without it, a null return
    /// could equally mean the scan found nothing at all.
    /// </remarks>
    [Fact]
    public void Build_output_under_src_Reactor_does_not_count_as_source()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base.AddMinutes(5));
        fx.Source("Core/Widget.cs", Base);
        fx.Source("obj/x64/Release/Reactor.GlobalUsings.g.cs", Base.AddMinutes(30));
        fx.Source("bin/x64/Release/net10.0-windows10.0.22621.0/Embedded.cs", Base.AddMinutes(30));

        Assert.Null(CompileCommand.BuildStaleXmlFinding(fx.Root, xml));

        fx.Source("Core/Later.cs", Base.AddMinutes(30));
        var finding = CompileCommand.BuildStaleXmlFinding(fx.Root, xml);
        Assert.NotNull(finding);
        Assert.Contains("src/Reactor/Core/Later.cs", finding!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory named <c>bin</c>-something is not build output. Segment
    /// matching rather than a substring test is what separates them.
    /// </summary>
    [Fact]
    public void A_source_directory_whose_name_merely_starts_with_bin_still_counts()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base);
        fx.Source("binding/Converter.cs", Base.AddMinutes(5));

        var finding = CompileCommand.BuildStaleXmlFinding(fx.Root, xml);

        Assert.NotNull(finding);
        Assert.Contains("src/Reactor/binding/Converter.cs", finding!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_sources_at_all_yields_no_finding()
    {
        using var fx = new RepoFixture();
        var xml = fx.Xml("x64/Release/net10.0-windows10.0.22621.0", Base);

        Assert.Null(CompileCommand.BuildStaleXmlFinding(fx.Root, xml));
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A throwaway repo root holding only the two directories the selection
    /// code looks at: <c>src/Reactor/bin</c> and <c>src/Reactor</c>.
    /// </summary>
    private sealed class RepoFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "reactor-xml-selection-" + Guid.NewGuid().ToString("N"));

        public RepoFixture() => Directory.CreateDirectory(Root);

        /// <summary>Writes <c>src/Reactor/bin/&lt;layout&gt;/Reactor.xml</c>.</summary>
        public string Xml(string layout, DateTime writeUtc) =>
            BinFile(layout, "Reactor.xml", writeUtc);

        public string BinFile(string layout, string fileName, DateTime writeUtc) =>
            Write(Path.Combine("src", "Reactor", "bin", Native(layout)), fileName, writeUtc);

        /// <summary>Writes a C# file at <c>src/Reactor/&lt;relativePath&gt;</c>.</summary>
        public string Source(string relativePath, DateTime writeUtc)
        {
            var native = Native(relativePath);
            var dir = Path.GetDirectoryName(native);
            return Write(
                Path.Combine("src", "Reactor", dir ?? string.Empty),
                Path.GetFileName(native),
                writeUtc);
        }

        private string Write(string relativeDir, string fileName, DateTime writeUtc)
        {
            var dir = Path.Combine(Root, relativeDir);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            File.WriteAllText(path, "// fixture\n");
            // Set after the write: writing stamps the file with "now".
            File.SetLastWriteTimeUtc(path, writeUtc);
            return path;
        }

        /// <summary>
        /// Writes a <c>Reactor.xml</c> outside the repo root entirely — the
        /// thing a junction inside <c>bin</c> could otherwise drag in.
        /// </summary>
        public string OutsideXml(string dirName, DateTime writeUtc)
        {
            var dir = Path.Combine(Root + "-outside", dirName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Reactor.xml");
            File.WriteAllText(path, "// fixture\n");
            File.SetLastWriteTimeUtc(path, writeUtc);
            return path;
        }

        private readonly List<string> _junctions = new();

        /// <summary>
        /// Creates <c>src/Reactor/bin/&lt;name&gt;</c> as a junction to
        /// <paramref name="target"/> and returns the link path.
        /// </summary>
        public string JunctionInsideBin(string name, string target)
        {
            var link = Path.Combine(Root, "src", "Reactor", "bin", name);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            CreateJunction(link, target);
            return link;
        }

        /// <summary>
        /// Makes <c>src/Reactor/bin</c> itself a junction to a real directory
        /// holding the given layout, and returns the candidate's path as seen
        /// through the link.
        /// </summary>
        public string XmlBehindJunctionedBin(string layout, DateTime writeUtc)
        {
            var realBin = Path.Combine(Root + "-realbin");
            var dir = Path.Combine(realBin, Native(layout));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Reactor.xml");
            File.WriteAllText(path, "// fixture\n");
            File.SetLastWriteTimeUtc(path, writeUtc);

            var link = Path.Combine(Root, "src", "Reactor", "bin");
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            CreateJunction(link, realBin);
            return Path.Combine(link, Native(layout), "Reactor.xml");
        }

        /// <summary>
        /// Junctions rather than symlinks: <c>mklink /J</c> needs no elevation
        /// or Developer Mode, so this works for an ordinary user and on the CI
        /// runner. Throws rather than skipping — a test that quietly opts out
        /// when its fixture fails is a test that cannot fail.
        /// </summary>
        private void CreateJunction(string link, string target)
        {
            using var p = global::System.Diagnostics.Process.Start(
                new global::System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    ArgumentList = { "/c", "mklink", "/J", link, target },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;
            p.WaitForExit();
            if (p.ExitCode != 0 || !Directory.Exists(link))
            {
                throw new InvalidOperationException(
                    $"could not create junction {link} -> {target}: " +
                    $"exit {p.ExitCode}, {p.StandardError.ReadToEnd().Trim()}");
            }
            _junctions.Add(link);
        }

        private static string Native(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar);

        /// <summary>
        /// The junction fixtures put their targets in sibling directories so a
        /// link can point genuinely outside the repo root; tear those down too,
        /// or each run leaks two trees into TEMP.
        /// </summary>
        /// <remarks>
        /// Junctions are removed first, as links. Measured, not assumed:
        /// <c>Directory.Delete(root, recursive: true)</c> over a tree
        /// containing one throws <c>UnauthorizedAccessException</c> ("Access to
        /// the path 'linked' is denied") — which
        /// <see cref="FixtureCleanup.DeleteTree"/> swallows by design, so the
        /// whole tree leaks silently. It did, twelve directories' worth, before
        /// this loop existed. Deleting the junction non-recursively removes the
        /// link and leaves its target intact.
        /// </remarks>
        public void Dispose()
        {
            foreach (var link in _junctions)
            {
                try { Directory.Delete(link, recursive: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            FixtureCleanup.DeleteTree(Root);
            FixtureCleanup.DeleteTree(Root + "-outside");
            FixtureCleanup.DeleteTree(Root + "-realbin");
        }
    }
}
