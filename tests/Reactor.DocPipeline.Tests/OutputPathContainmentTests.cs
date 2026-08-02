using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// The compiled output path is built from a topic id that comes out of
/// <c>Path.GetRelativePath</c>, which yields a rooted path across volumes and a
/// ../-prefixed one when the template resolves outside the templates root. The
/// original <c>Path.Combine</c> discarded the output directory outright for the
/// rooted case.
/// </summary>
/// <remarks>
/// <para>
/// Both directions matter and they fail differently: <c>Path.Join</c> alone
/// fixes the rooted case, and a <c>Path.IsPathRooted</c> guard alone fixes it
/// too — but neither catches traversal, which needs the containment check. The
/// theory below would pass against either half-fix if it only covered one, so
/// it covers both.
/// </para>
/// <para>
/// Scope, stated precisely because the gap was measured rather than assumed:
/// these pin <see cref="DocPaths.IsUnder"/> and the Combine-vs-Join
/// difference, and mutating <c>IsUnder</c> (dropping the trailing-separator
/// normalisation) does turn the prefix-sibling case red. They do <em>not</em>
/// cover the call site in <c>CompileCommand.Run</c> that builds the compiled
/// page path — replacing that call with a bare <c>Path.Combine</c> and no guard
/// leaves the whole suite green. Re-measured after that site was moved onto
/// <see cref="DocPaths.ResolveContained"/>; still 258 passed, 0 failed.
/// Reaching that guard needs a topic id that is rooted or traversing, which
/// <c>Path.GetRelativePath</c> only produces for a templates root on another
/// volume or behind a link, and that is not constructible portably in a unit
/// test. The guard is therefore defence-in-depth against a future refactor,
/// not a currently reachable bug, and is deliberately shipped without
/// call-site coverage rather than with a test that would pass either way.
/// </para>
/// <para>
/// The previous wording named a line number. That is the same drift generator
/// this file exists to catch — the line had moved by the time anyone read it,
/// and a stale pointer invites the reader to conclude the claim is stale too.
/// Named by symbol instead.
/// </para>
/// </remarks>
public class OutputPathContainmentTests
{
    private static readonly string Root =
        global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), "guide-out"));

    [Theory]
    [InlineData("hooks")]
    [InlineData("recipes/login")]
    public void Ordinary_topic_ids_stay_inside_the_output_directory(string topicId)
    {
        var full = global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(Root, $"{topicId}.md"));

        Assert.True(DocPaths.IsUnder(full, Root),
            $"'{topicId}' should compile inside the output dir but resolved to {full}");
    }

    [Theory]
    [InlineData("../escaped")]              // traversal: Join keeps the base, containment rejects
    [InlineData("../../etc/passwd")]
    [InlineData("recipes/../../escaped")]   // traversal that only appears mid-path
    public void Traversing_topic_ids_are_rejected(string topicId)
    {
        var full = global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(Root, $"{topicId}.md"));

        Assert.False(DocPaths.IsUnder(full, Root),
            $"'{topicId}' escapes the output dir ({full}) and must be rejected");
    }

    /// <summary>
    /// The case that motivated the change. <c>Path.Combine</c> returns the
    /// rooted segment alone, so the output directory is silently dropped;
    /// <c>Path.Join</c> keeps it and the result stays contained. Asserting on
    /// both calls in one test is what makes this a measurement of the
    /// difference rather than a restatement of the fix.
    /// </summary>
    [Fact]
    public void Rooted_topic_id_drops_the_base_under_Combine_but_not_under_Join()
    {
        var rooted = global::System.IO.Path.IsPathRooted("/etc/passwd")
            ? "/etc/passwd"
            : global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), "elsewhere");

        var combined = global::System.IO.Path.Combine(Root, $"{rooted}.md");
        var joined = global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(Root, $"{rooted}.md"));

        Assert.False(DocPaths.IsUnder(global::System.IO.Path.GetFullPath(combined), Root),
            "Path.Combine should drop the base for a rooted segment — if this fails the premise changed");
        Assert.True(DocPaths.IsUnder(joined, Root),
            "Path.Join must preserve the base so the rooted segment stays contained");
    }

    /// <summary>
    /// The drive-rooted variant, which the test above does not reach: its
    /// rooted value is either a POSIX path or a temp path, neither of which
    /// carries a colon into a non-leading position after <c>Join</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because a review argued that <c>GetFullPath</c> throws on
    /// the embedded colon, crashing the compile before the containment check
    /// runs. It does not, on .NET 10 — but the measurement is the load-bearing
    /// part of that answer, and an assumption about BCL behaviour that only
    /// lives in a comment is one framework update away from being false while
    /// still reading as true.
    /// </para>
    /// <para>
    /// Both halves matter and they point in opposite directions. Containment
    /// says <em>yes</em>, so it is not the thing that would catch a
    /// drive-rooted id; and the result is not a valid Windows file path, so
    /// "contained" must not be read as "writable". If a future runtime starts
    /// rejecting the colon, the first assertion fails here rather than the
    /// guarantee silently widening in prose.
    /// </para>
    /// </remarks>
    [Fact]
    public void Drive_rooted_topic_id_stays_contained_and_is_not_rejected_by_GetFullPath()
    {
        if (!OperatingSystem.IsWindows()) return;

        const string driveRooted = "D:/other/topic";
        Assert.True(global::System.IO.Path.IsPathRooted(driveRooted),
            "premise: the id must be rooted, else this measures nothing");

        var joined = global::System.IO.Path.Join(Root, $"{driveRooted}.md");
        var full = global::System.IO.Path.GetFullPath(joined);

        Assert.True(DocPaths.IsUnder(full, Root),
            $"containment reports '{full}' is outside '{Root}' — if this fails, a rooted id " +
            "now escapes and the guard in CompileCommand must reject it rather than contain it");

        Assert.Contains(":", full[Root.Length..], global::System.StringComparison.Ordinal);
    }


    /// <remarks>
    /// <para>
    /// This is not a restatement of the test above. There the escaped value was
    /// the thing being checked; here it is the thing being checked
    /// <em>against</em> — the guard runs, returns a correct answer, and answers
    /// the wrong question. That distinction is why
    /// <see cref="DocPaths.ResolveContained"/> exists rather than a convention
    /// of calling Join before IsUnder at each site.
    /// </para>
    /// <para>
    /// Note what the helper does <em>not</em> do here: it does not reject the
    /// rooted segment, it neutralises it. <c>Path.Join</c> keeps the base, so
    /// the result is already inside and there is nothing to reject. The first
    /// draft of this test asserted a throw and failed — worth recording,
    /// because "rooted input is refused" and "rooted input cannot escape" are
    /// different guarantees and only the second one is true. Traversal is the
    /// case that genuinely needs the throw, covered separately below.
    /// </para>
    /// <para>
    /// This does not re-derive the escape with <c>Path.Combine</c>.
    /// <see cref="Rooted_topic_id_drops_the_base_under_Combine_but_not_under_Join"/>
    /// is the one place that measures the framework behaviour, and duplicating
    /// the call here bought nothing: what this test adds is what happens
    /// <em>downstream</em> of an escaped base, not the escape itself. The
    /// escaped base is therefore constructed directly and the first assertion
    /// is a premise guard on it, so a framework change fails the
    /// characterization test loudly rather than quietly changing what this one
    /// is about.
    /// </para>
    /// <para>
    /// Non-vacuity: the first two assertions reconstruct the old two-step form
    /// and require it to <em>pass</em> on input that escapes, so they fail if
    /// the premise stops holding; the third requires the helper to contain the
    /// same input. A helper that threw unconditionally would fail this test and
    /// <see cref="Contained_segments_resolve_and_are_returned"/>; one that never
    /// threw would fail
    /// <see cref="Traversing_segments_are_rejected_by_the_helper"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_rooted_directory_segment_escapes_the_base_a_file_guard_measures_against()
    {
        var rootedDir = global::System.IO.Path.IsPathRooted("/evil")
            ? "/evil"
            : global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), "evil");

        // The pre-fix shape: the topic directory came out of Path.Combine, which
        // returns a rooted segment verbatim, so it is the escaped base itself.
        // Then Join + IsUnder for the file — genuinely executed, genuinely passes.
        var escapedBase = global::System.IO.Path.GetFullPath(rootedDir);
        var file = global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(escapedBase, "shot.png"));

        Assert.False(DocPaths.IsUnder(escapedBase, Root),
            "premise: a rooted directory segment must escape the output root");
        Assert.True(DocPaths.IsUnder(file, escapedBase),
            "premise: the per-file guard passes, because it is measuring against the escaped base");

        var resolved = DocPaths.ResolveContained(Root, rootedDir, "Topic id");
        Assert.True(DocPaths.IsUnder(resolved, Root),
            $"the helper must keep a rooted segment inside the root, but resolved to {resolved}");
    }

    [Theory]
    [InlineData("hooks")]
    [InlineData("recipes/login")]
    public void Contained_segments_resolve_and_are_returned(string segment)
    {
        var resolved = DocPaths.ResolveContained(Root, segment, "Topic id");

        Assert.True(DocPaths.IsUnder(resolved, Root));
        Assert.Equal(
            global::System.IO.Path.GetFullPath(global::System.IO.Path.Join(Root, segment)),
            resolved);
    }

    /// <summary>
    /// Join alone does not make the helper safe — a traversal segment keeps the
    /// base and still walks out of it — so the containment half must remain.
    /// </summary>
    [Theory]
    [InlineData("../escaped")]
    [InlineData("recipes/../../escaped")]
    public void Traversing_segments_are_rejected_by_the_helper(string segment)
    {
        Assert.Throws<global::System.InvalidOperationException>(
            () => DocPaths.ResolveContained(Root, segment, "Topic id"));
    }

    /// <summary>
    /// A <c>:</c> in a content-derived segment is not an ordinary filename
    /// character on Windows, and containment does not notice.
    /// </summary>
    [Theory]
    [InlineData("topic:hidden")]
    [InlineData("D:foo")]
    [InlineData("recipes/login:stream")]
    public void Colon_segments_are_rejected_by_the_helper(string segment)
    {
        Assert.Throws<global::System.InvalidOperationException>(
            () => DocPaths.ResolveContained(Root, segment, "Topic id"));
    }

    /// <summary>
    /// The hazard the rule above exists for, demonstrated rather than asserted:
    /// the containment check passes on a colon segment and the write still does
    /// not go where the path says.
    /// </summary>
    /// <remarks>
    /// Non-vacuity: the first assertion requires the old behaviour to
    /// <em>pass</em> containment, so if a framework change ever made
    /// <c>GetFullPath</c> reject or rewrite the colon, this test fails loudly
    /// instead of quietly becoming a tautology. The last two then show the
    /// bytes landing somewhere a directory listing cannot see — which is what
    /// makes this a containment bypass rather than a naming preference.
    /// </remarks>
    [Fact]
    public void A_colon_segment_passes_containment_but_writes_to_an_alternate_stream()
    {
        Assert.SkipUnless(global::System.OperatingSystem.IsWindows(),
            "alternate data streams are an NTFS concept");

        var dir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            "ads-probe-" + global::System.Guid.NewGuid().ToString("N")[..8]);
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            var root = global::System.IO.Path.GetFullPath(dir);

            // Exactly what ResolveContained did before the colon rule: Join,
            // GetFullPath, then containment.
            var resolved = global::System.IO.Path.GetFullPath(
                global::System.IO.Path.Join(root, "topic:hidden"));

            Assert.True(DocPaths.IsUnder(resolved, root),
                "premise: the containment check passes on a colon segment — that is why it is not sufficient");

            global::System.IO.File.WriteAllBytes(resolved, [1, 2, 3, 4]);

            var listed = global::System.IO.Directory.GetFiles(root)
                .Select(p => global::System.IO.Path.GetFileName(p)!)
                .ToArray();

            Assert.Equal(["topic"], listed);
            Assert.Equal(0, new global::System.IO.FileInfo(
                global::System.IO.Path.Join(root, "topic")).Length);
        }
        finally
        {
            global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The boundary that made the old comment at the `CompileCommand` call site
    /// look safe: whether a colon path is loud or silent depends on whether the
    /// tail contains a separator, and only one of the two is loud.
    /// </summary>
    /// <remarks>
    /// This is why the rule rejects any colon rather than the shapes known to
    /// be dangerous. <c>GetRelativePath</c> across volumes usually produces the
    /// loud shape, so the quiet one is the one that would have gone unnoticed,
    /// and "safe because the tail happens to contain a separator" is not a
    /// property anyone would think to preserve through a refactor.
    /// </remarks>
    [Fact]
    public void Colon_paths_are_loud_or_silent_depending_on_a_separator()
    {
        Assert.SkipUnless(global::System.OperatingSystem.IsWindows(),
            "alternate data streams are an NTFS concept");

        var dir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            "ads-edge-" + global::System.Guid.NewGuid().ToString("N")[..8]);
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            var root = global::System.IO.Path.GetFullPath(dir);

            var withSeparator = global::System.IO.Path.GetFullPath(
                global::System.IO.Path.Join(root, "D:/other/topic.md"));
            var bare = global::System.IO.Path.GetFullPath(
                global::System.IO.Path.Join(root, "D:foo"));

            Assert.True(DocPaths.IsUnder(withSeparator, root));
            Assert.True(DocPaths.IsUnder(bare, root),
                "premise: containment passes for both, so it cannot be what separates them");

            // Loud: a stream name cannot contain a separator.
            Assert.ThrowsAny<global::System.IO.IOException>(
                () => global::System.IO.File.WriteAllBytes(withSeparator, [1, 2, 3, 4]));

            // Silent: same containment verdict, and it writes.
            global::System.IO.File.WriteAllBytes(bare, [1, 2, 3, 4]);
            Assert.Equal(0, new global::System.IO.FileInfo(
                global::System.IO.Path.Join(root, "D")).Length);
        }
        finally
        {
            global::System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The case-sensitive arm, which ships on Linux and which a Windows-only
    /// test run reaches only through the explicit-comparison overload.
    /// </summary>
    /// <remarks>
    /// The pair is the point: the same input is inside under the Windows
    /// comparison and outside under the POSIX one, so this fails if
    /// <c>IsUnder</c> ever hard-codes either.
    /// </remarks>
    [Fact]
    public void Case_differing_paths_are_inside_only_under_the_windows_comparison()
    {
        var root = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(), "guide");
        var candidate = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(), "GUIDE", "x.md");

        Assert.True(
            DocPaths.IsUnder(candidate, root, global::System.StringComparison.OrdinalIgnoreCase));
        Assert.False(
            DocPaths.IsUnder(candidate, root, global::System.StringComparison.Ordinal),
            "on a case-sensitive filesystem /tmp/GUIDE is a different directory from /tmp/guide");
    }

    [Fact]
    public void Default_path_comparison_follows_the_platform()
    {
        Assert.Equal(
            global::System.OperatingSystem.IsWindows()
                ? global::System.StringComparison.OrdinalIgnoreCase
                : global::System.StringComparison.Ordinal,
            DocPaths.PathComparison);
    }

    /// <summary>
    /// The comparer and the comparison must give the same answer, on whatever
    /// platform the test happens to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately an *agreement* test rather than a second copy of the
    /// platform assertion above. That one's expected value is derived from
    /// <c>OperatingSystem.IsWindows()</c> — the same input the code under test
    /// reads — so on Windows it cannot separate a correct platform-selected
    /// comparer from one hard-coded to <c>OrdinalIgnoreCase</c>. Both sides
    /// would say "insensitive" and the test would pass either way. That is the
    /// environment-derived-oracle shape from <c>AGENTS.md</c>: a check whose
    /// inputs come from the environment rather than from the code cannot convict
    /// the code.
    /// </para>
    /// <para>
    /// Agreement has no such blind spot. A <c>PathComparer</c> written as an
    /// independent literal disagrees with <c>PathComparison</c> on exactly one
    /// of the two platforms, and this test fails there — whereas a comparer
    /// *derived* from the comparison agrees on both by construction. So the
    /// assertion is about the derivation, which is the property being claimed.
    /// </para>
    /// <para>
    /// Measured, because the asymmetry is worth stating rather than implying.
    /// Substituting <c>StringComparer.Ordinal</c> fails this test on Windows;
    /// substituting <c>StringComparer.OrdinalIgnoreCase</c> — the literal this
    /// change replaced — does <em>not</em>, and cannot, because on Windows that
    /// is the right answer. The defect it caused was always Linux-only, so no
    /// Windows run can convict it and this test does not claim to. What it
    /// pins is that the two never diverge, which is checkable everywhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void Path_comparer_and_path_comparison_agree()
    {
        foreach (var (a, b) in new[]
        {
            ("guide/x.md", "GUIDE/X.MD"),
            ("guide/x.md", "guide/x.md"),
            ("guide/x.md", "guide/y.md"),
        })
        {
            Assert.Equal(
                a.Equals(b, DocPaths.PathComparison),
                DocPaths.PathComparer.Equals(a, b));
        }
    }

    /// <summary>
    /// A sibling directory sharing a prefix is not "inside". Without the
    /// trailing separator in <see cref="DocPaths.IsUnder"/> this passes
    /// containment and writes outside the tree.
    /// </summary>
    [Fact]
    public void Prefix_sibling_directory_is_not_treated_as_inside()
    {
        Assert.False(DocPaths.IsUnder(Root + "-old" + global::System.IO.Path.DirectorySeparatorChar + "x.md", Root));
    }

    /// <summary>
    /// A root that already ends in a separator must not gain a second one.
    /// This is the case that made consolidating the third copy in
    /// <c>ScreenshotCapture</c> more than cosmetic: that copy appended
    /// <see cref="global::System.IO.Path.DirectorySeparatorChar"/>
    /// unconditionally, so a root like <c>C:\</c> became <c>C:\\</c> and
    /// <em>every</em> path under it was rejected as an escape.
    /// <see cref="DocPaths.IsUnder"/> guards the append with an
    /// <c>EndsWith</c> check, so it does not have that behaviour.
    /// </summary>
    [Fact]
    public void Root_that_already_ends_in_a_separator_is_handled()
    {
        var driveRoot = global::System.IO.Path.GetPathRoot(
            global::System.IO.Path.GetFullPath(Root))!;

        Assert.EndsWith(
            global::System.IO.Path.DirectorySeparatorChar.ToString(), driveRoot);

        var child = driveRoot + "some-file.md";

        Assert.True(DocPaths.IsUnder(child, driveRoot),
            "a file at the drive root must count as inside the drive root");

        // The formulation that was inlined in ScreenshotCapture, shown failing
        // on the same input — so this test documents a real difference rather
        // than asserting that two spellings of the same thing agree.
        Assert.False(
            child.StartsWith(
                driveRoot + global::System.IO.Path.DirectorySeparatorChar,
                global::System.StringComparison.OrdinalIgnoreCase),
            "unconditional separator append double-separates an already-rooted path");
    }

    /// <summary>
    /// <see cref="DocPaths"/>'s own remark says the containment rule "lives in
    /// exactly one place". Until this test, nothing checked that — and a
    /// comment whose guarantee is wider than its enforcement is precisely the
    /// shape this pipeline keeps producing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is worth enforcing rather than trusting because the drift already
    /// happened once: three copies existed (<c>CompileCommand</c>,
    /// <c>DiagramProcessor</c>, <c>ScreenshotCapture</c>), and they had already
    /// diverged before anyone noticed — the <c>ScreenshotCapture</c> one
    /// appended a separator unconditionally, which
    /// <see cref="Root_that_already_ends_in_a_separator_is_handled"/> shows
    /// rejects every path under a drive root. Divergence here is silent by
    /// construction: each copy keeps compiling and keeps returning an answer.
    /// </para>
    /// <para>
    /// The scope is <c>src/Reactor.Cli/Docs</c> deliberately.
    /// <c>Microsoft.UI.Reactor.Cli.Check.CompilationLoader</c> has its own
    /// <c>IsUnder</c>; it filters a file enumeration rather than deciding a
    /// write target, so it is out of scope for this PR. It is named here
    /// instead of silently excluded because its parameters are
    /// <em>reversed</em> (root first), which is a worse hazard than the
    /// duplication this test guards: a later "consolidation" that points one
    /// call at the other inverts the containment decision with no compile
    /// error and no failing test.
    /// </para>
    /// <para>
    /// This scans source rather than reflecting over the assembly because the
    /// test project treats trim warnings as errors, and because the source
    /// form is the broader check anyway — it also catches a copy added as a
    /// local function or under a different namespace in the same folder.
    /// </para>
    /// </remarks>
    [Fact]
    public void Containment_rule_has_exactly_one_implementation_in_the_doc_pipeline()
    {
        var docsDir = global::System.IO.Path.Join(
            FindRepoRoot(), "src", "Reactor.Cli", "Docs");

        var sources = global::System.IO.Directory.GetFiles(
            docsDir, "*.cs", global::System.IO.SearchOption.AllDirectories);

        // A mis-resolved path would find nothing and read as "no duplicates",
        // which is the same answer a healthy tree gives. Pin the corpus first.
        Assert.True(sources.Length >= 10,
            $"expected the doc-pipeline sources, found only {sources.Length} .cs files under {docsDir}");

        var declarations = sources
            .Where(f => global::System.Text.RegularExpressions.Regex.IsMatch(
                global::System.IO.File.ReadAllText(f),
                @"\bbool\s+IsUnder\s*\("))
            .Select(f => global::System.IO.Path.GetFileName(f))
            .OrderBy(f => f)
            .ToArray();

        Assert.Equal(new[] { "DocPaths.cs" }, declarations);
    }

    private static string FindRepoRoot()
    {
        var dir = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new global::System.InvalidOperationException(
            "Could not locate repo root (Reactor.slnx) from test base dir.");
    }
}
