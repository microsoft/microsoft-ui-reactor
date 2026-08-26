using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Guards the wiring that makes the <c>Doc snippet analyzer gate</c> CI job meaningful.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect class.</b> Every <c>snippet=</c> block in <c>docs/guide/</c> is extracted
/// verbatim from a doc app under <c>docs/_pipeline/apps/</c>, so a doc app <em>is</em> the code
/// the guides tell readers to write. Two independent gaps let 276 analyzer diagnostics reach the
/// published docset: only <c>win2d-canvas</c> was ever listed in <c>Reactor.slnx</c>, so the other
/// 52 apps were never compiled by CI; and a <c>ProjectReference</c> to <c>src/Reactor</c> does not
/// flow <c>Reactor.Analyzers</c> (it ships as a packed <c>&lt;None Pack="true"&gt;</c> item), so
/// even that one app compiled without the rules its own readers are subject to. The result was
/// snippets that compiled perfectly while warning in a reader's project — and, worse, snippets
/// that silently did nothing the surrounding prose promised.
/// </para>
/// <para>
/// <b>Why a wiring test and not a compile test.</b> The compile itself is the CI job: it builds
/// <c>DocApps.proj</c> and fails on any <c>REACTOR_</c> diagnostic. That is the real gate, and
/// duplicating it here would mean running MSBuild over 53 WinUI apps inside the headless unit
/// suite. What a fast test <em>can</em> do is stop the gate from being quietly bypassed — which is
/// the failure mode that produced this bug in the first place. Every fact below is about
/// reachability and suppression, not about any individual snippet.
/// </para>
/// <para>
/// <b>Non-vacuity.</b> Each fact is differential rather than a bare existence check: the glob is
/// compared against the actual directory listing (not merely asserted non-empty), and the
/// suppression scan is floored by <see cref="Suppression_Scan_Actually_Reads_The_Doc_Apps"/>, which
/// fails if the scanner stops seeing files at all. Without that floor, a scanner that silently read
/// zero files would report zero suppressions and pass forever.
/// </para>
/// </remarks>
public class DocAppGateWiringTests
{
    private const string AppsRelative = "docs/_pipeline/apps";

    /// <summary>
    /// The gate is only as good as its reach: an app the traversal project does not match is an
    /// app CI never compiles, which is exactly how 52 of 53 doc apps escaped for so long.
    /// </summary>
    [Fact]
    public void Every_Doc_App_Is_Reachable_From_The_Traversal_Project()
    {
        var repoRoot = FindRepoRoot();
        var appsDir = AppsDir(repoRoot);

        var onDisk = Directory
            .EnumerateDirectories(appsDir)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.TopDirectoryOnly))
            .Select(p => Path.GetFullPath(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A floor, so a directory walk that silently stops matching cannot pass as "all reachable".
        Assert.True(
            onDisk.Count >= 50,
            $"Expected the doc-app corpus to still be ~53 projects; found {onDisk.Count}. "
            + "If doc apps were intentionally removed, lower this floor deliberately.");

        var proj = XDocument.Load(RepoPath(appsDir, "DocApps.proj"));
        var includes = proj.Descendants()
            .Where(e => e.Name.LocalName == "DocApp")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        Assert.NotEmpty(includes);

        // Resolve the glob the same way MSBuild would, then compare sets. Comparing against the
        // real listing (rather than asserting "the glob is non-empty") is what makes this fail if
        // someone replaces the wildcard with a hand-maintained list that misses a new app.
        var matched = new List<string>();
        var patterns = includes.Select(include =>
            include!.Replace("$(MSBuildThisFileDirectory)", string.Empty)
                    .Replace('/', Path.DirectorySeparatorChar));

        foreach (var pattern in patterns)
        {
            var dirPart = Path.GetDirectoryName(pattern) ?? "*";
            var filePart = Path.GetFileName(pattern);

            foreach (var dir in Directory.EnumerateDirectories(appsDir, dirPart))
            {
                matched.AddRange(Directory.EnumerateFiles(dir, filePart).Select(Path.GetFullPath));
            }
        }

        var missing = onDisk
            .Except(matched.Distinct(), StringComparer.OrdinalIgnoreCase)
            .Select(p => Path.GetRelativePath(repoRoot, p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These doc apps are not matched by DocApps.proj, so CI never builds them and their "
            + "snippets are ungated:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The customer-facing analyzer bundle must be attached to the doc apps. Without it the gate
    /// job builds all 53 apps and reports success while checking nothing.
    /// </summary>
    [Fact]
    public void Doc_Apps_Import_The_Consumer_Analyzer_Bundle()
    {
        var repoRoot = FindRepoRoot();
        var propsPath = RepoPath(repoRoot, AppsRelative, "Directory.Build.props");

        var props = XDocument.Load(propsPath);

        var analyzerRefs = props.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Where(e => ((string?)e.Attribute("Include") ?? string.Empty)
                .Replace('\\', '/')
                .Contains("src/Reactor.Analyzers/Reactor.Analyzers.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            analyzerRefs.Count == 1,
            $"Expected exactly one Reactor.Analyzers ProjectReference in {AppsRelative}/Directory.Build.props, "
            + $"found {analyzerRefs.Count}. Doc apps must compile under the same analyzer bundle their "
            + "readers get from the nupkg.");

        // OutputItemType="Analyzer" is the load-bearing attribute: without it the reference is a
        // plain assembly reference, the rules never run, and the gate silently checks nothing.
        Assert.Equal("Analyzer", (string?)analyzerRefs[0].Attribute("OutputItemType"));
    }

    /// <summary>
    /// The one legitimate reason a doc app may silence a Reactor rule: the page exists to
    /// document the very thing the rule flags. Each entry is an <c>(app, rule)</c> pair with the
    /// justification that earned it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ledger rather than a free-for-all. Project-wide <c>&lt;NoWarn&gt;</c> is invisible at the
    /// call site — the three entries below were already in the tree and were the reason those apps
    /// reported a clean baseline during the sweep that produced this gate. Requiring each pair to
    /// be listed here turns a silent suppression into a reviewable one: a new <c>NoWarn</c> fails
    /// this test until someone writes down why.
    /// </para>
    /// <para>
    /// <c>REACTOR_V1_PREVIEW</c> is not a code-quality rule — it is the provisional-API opt-in
    /// (see <c>PoolPolicy</c>/<c>Reconciler</c>), so a page whose subject *is* the provisional API
    /// must acknowledge it. The <c>rules-of-reactor</c> hook rules are different: that page teaches
    /// the rules of hooks through labelled counterexamples, and "fixing" a deliberate
    /// counterexample would delete the lesson.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string[]> AllowedSuppressions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents provisional control-authoring APIs, which is what the rule announces.
        ["extending-reactor-controls"] = ["REACTOR_V1_PREVIEW"],

        // Documents the provisional V1 reconciler protocol.
        ["v1-protocol"] = ["REACTOR_V1_PREVIEW"],

        // Teaches the rules of hooks by showing violations under "Wrong:" labels, and pairs a
        // keyed list against an unkeyed one. The counterexamples are kept in their natural
        // idiomatic shape on purpose: contorting the "bad" half to dodge the analyzer would make
        // it differ from the "good" half in ways that have nothing to do with the lesson.
        ["rules-of-reactor"] = ["REACTOR_HOOKS_001", "REACTOR_HOOKS_004", "REACTOR_HOOKS_005", "REACTOR_DSL_001"],
    };

    /// <summary>
    /// The suppression patterns, declared once. Every test that reasons about suppressions runs
    /// through <see cref="EnumerateSuppressed"/> so the ledger check, the corpus scan, and the
    /// positive control cannot drift apart — a re-declared copy in one of them was how the
    /// <c>&lt;NoWarn&gt;</c> and <c>.editorconfig</c> arms ended up with no positive control at all.
    /// </summary>
    private static readonly Regex Pragma =
        new(@"#pragma\s+warning\s+disable\s+(?<ids>[^\r\n/]+)", RegexOptions.Compiled);

    private static readonly Regex NoWarnTag =
        new(@"<NoWarn>(?<ids>[^<]*)</NoWarn>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EditorConfigDowngrade = new(
        @"dotnet_diagnostic\.(?<id>REACTOR_[A-Z0-9_]+)\.severity\s*=\s*(none|silent|suggestion)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReactorId =
        new(@"REACTOR_[A-Z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A fix that silences a rule is not a fix. Doc apps are teaching material, so a suppression
    /// there ships the anti-pattern to every reader who copies the snippet.
    /// </summary>
    [Fact]
    public void No_Doc_App_Suppresses_A_Reactor_Rule()
    {
        var repoRoot = FindRepoRoot();
        var appsDir = AppsDir(repoRoot);

        var offenders = new List<string>();

        foreach (var file in SuppressionScannableFiles(appsDir))
        {
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(repoRoot, file);
            var app = OwningApp(appsDir, file);

            foreach (var (kind, ids) in EnumerateSuppressed(text))
            {
                foreach (var id in ids)
                {
                    if (app is not null
                        && AllowedSuppressions.TryGetValue(app, out var allowed)
                        && allowed.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    offenders.Add($"{rel}: {kind} suppresses {id}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Doc-app snippets must not suppress Reactor analyzer rules -- readers copy these verbatim, "
            + "and a suppression hides the anti-pattern rather than removing it. Fix the code, or, if "
            + "the page genuinely documents what the rule flags, add the (app, rule) pair to "
            + "AllowedSuppressions with a justification:\n  "
            + string.Join("\n  ", offenders.OrderBy(o => o, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Every allow-listed suppression must still be load-bearing. An entry whose
    /// <c>NoWarn</c> has since been removed is stale permission that would silently re-authorise a
    /// future suppression nobody reviewed.
    /// </summary>
    [Fact]
    public void Allowed_Suppressions_Are_All_Still_Used()
    {
        var repoRoot = FindRepoRoot();
        var appsDir = AppsDir(repoRoot);

        var stale = new List<string>();
        foreach (var (app, rules) in AllowedSuppressions)
        {
            var appDir = RepoPath(appsDir, app);
            Assert.True(Directory.Exists(appDir), $"AllowedSuppressions names '{app}', which no longer exists.");

            // Parsed, not text-matched. A raw Contains() over the app's sources kept an entry
            // alive on nothing more than a code comment mentioning the rule ID, so a suppression
            // could be deleted while its permission silently survived to bless the next one.
            var suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in SuppressionScannableFiles(appDir))
            {
                foreach (var (_, ids) in EnumerateSuppressed(File.ReadAllText(file)))
                {
                    foreach (var id in ids) suppressed.Add(id);
                }
            }

            stale.AddRange(rules
                .Where(rule => !suppressed.Contains(rule))
                .Select(rule => $"{app} -> {rule}"));
        }

        Assert.True(
            stale.Count == 0,
            "These AllowedSuppressions entries no longer correspond to a real suppression in the doc "
            + "app. Remove them so the ledger keeps matching reality:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The files a suppression can hide in. Shared so the corpus scan and the ledger check cannot
    /// disagree about what was searched.
    /// </summary>
    private static IEnumerable<string> SuppressionScannableFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            // bin/obj carry generated copies; scanning them would double-report.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var isSource = Path.GetExtension(file) is ".cs" or ".csproj" or ".props" or ".proj";
            var isEditorConfig = Path.GetFileName(file).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);
            if (isSource || isEditorConfig) yield return file;
        }
    }

    private static IEnumerable<(string Kind, List<string> Ids)> EnumerateSuppressed(string text)
    {
        foreach (Match m in Pragma.Matches(text))
        {
            var ids = ReactorId.Matches(m.Groups["ids"].Value).Select(x => x.Value).ToList();
            if (ids.Count > 0) yield return ("#pragma warning disable", ids);
        }

        foreach (Match m in NoWarnTag.Matches(text))
        {
            var ids = ReactorId.Matches(m.Groups["ids"].Value).Select(x => x.Value).ToList();
            if (ids.Count > 0) yield return ("<NoWarn>", ids);
        }

        foreach (Match m in EditorConfigDowngrade.Matches(text))
        {
            yield return (".editorconfig severity", [m.Groups["id"].Value]);
        }
    }

    /// <summary>Maps a file back to the doc app directory that owns it, or null if it sits above one.</summary>
    private static string? OwningApp(string appsDir, string file)
    {
        var rel = Path.GetRelativePath(appsDir, file);
        var first = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null || first.Contains('.') ? null : first;
    }


    /// <summary>
    /// Floors <see cref="No_Doc_App_Suppresses_A_Reactor_Rule"/>. That test passes by reporting
    /// nothing, which is indistinguishable from a scanner that reads no files at all — the exact
    /// "a no-match is not a measurement" trap. This proves the corpus it walks is real and that the
    /// patterns it uses can still match.
    /// </summary>
    [Fact]
    public void Suppression_Scan_Actually_Reads_The_Doc_Apps()
    {
        var repoRoot = FindRepoRoot();
        var appsDir = AppsDir(repoRoot);

        var sources = Directory
            .EnumerateFiles(appsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            sources.Count >= 50,
            $"The suppression scan walked only {sources.Count} doc-app sources; it is no longer "
            + "measuring the corpus it claims to cover.");

        // Positive control: planted violations must come back out of the REAL scanner, one per
        // arm. Replaying only the #pragma regex left <NoWarn> and .editorconfig with no control at
        // all, so a break in either would have reported zero offenders and passed forever.
        var planted = EnumerateSuppressed(
                "#pragma warning disable REACTOR_THEME_001 // planted\n"
                + "<NoWarn>$(NoWarn);REACTOR_HOOKS_001;CS0169</NoWarn>\n"
                + "dotnet_diagnostic.REACTOR_DSL_001.severity = none\n")
            .ToList();

        Assert.Equal(3, planted.Count);
        Assert.Equal(
            ["#pragma warning disable", "<NoWarn>", ".editorconfig severity"],
            planted.Select(p => p.Kind));
        Assert.Equal(
            ["REACTOR_THEME_001", "REACTOR_HOOKS_001", "REACTOR_DSL_001"],
            planted.SelectMany(p => p.Ids));

        // ...and must stay silent on non-Reactor suppressions, or the gate would be noise that
        // someone eventually loosens.
        Assert.Empty(EnumerateSuppressed(
            "#pragma warning disable CS0168\n<NoWarn>$(NoWarn);CS0169;IDE0051</NoWarn>\n"));
    }

    /// <summary>
    /// The gate has to actually run, and has to fail closed. A traversal project and wired
    /// analyzers are inert if no workflow builds them — or if the job that builds them is disabled,
    /// or reports diagnostics without failing.
    /// </summary>
    [Fact]
    public void Ci_Runs_The_Doc_Snippet_Gate()
    {
        var repoRoot = FindRepoRoot();
        var ci = File.ReadAllLines(RepoPath(repoRoot, ".github/workflows/ci.yml"));

        var job = JobBlock(ci, "docs-snippet-gate");
        Assert.True(job.Count > 0, "ci.yml no longer defines a `docs-snippet-gate` job.");

        var body = string.Join("\n", job);

        // Substring checks alone were theatre: a job left in place but switched off, or one that
        // printed diagnostics and exited 0, still contained every string this used to assert.
        Assert.DoesNotContain("if: false", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("continue-on-error: true", body, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DocApps.proj", body, StringComparison.Ordinal);
        Assert.Contains("REACTOR_", body, StringComparison.Ordinal);

        // Fails closed: the diagnostic branch must terminate the job non-zero.
        Assert.Contains("exit 1", body, StringComparison.Ordinal);

        // The scan must stay case-sensitive: an insensitive match also hits the restore's
        // "warning NU1900: Error occurred ...", turning the gate into a flaky failure that
        // someone would eventually "fix" by loosening it.
        Assert.Contains("-CaseSensitive", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Slices one job's lines out of a workflow file, from its <c>&lt;name&gt;:</c> key to the next
    /// key at the same indent. Asserting against the whole file would let a match in an unrelated
    /// job satisfy a check about this one.
    /// </summary>
    private static List<string> JobBlock(string[] lines, string jobName)
    {
        var start = Array.FindIndex(lines, l => l.TrimEnd() == $"  {jobName}:");
        if (start < 0) return [];

        var block = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var isNextJob = line.Length > 2
                && line[0] == ' ' && line[1] == ' ' && line[2] != ' '
                && line.TrimEnd().EndsWith(':');
            if (isNextJob) break;
            block.Add(line);
        }

        return block;
    }

    /// <summary>
    /// Combines repo-relative segments onto a trusted base, rejecting any segment that is rooted
    /// or escapes the base.
    /// </summary>
    /// <remarks>
    /// <c>Path.Combine</c> silently discards everything before a rooted segment, so
    /// <c>Path.Combine(repoRoot, x)</c> quietly becomes <c>x</c> if <c>x</c> ever turns absolute.
    /// Every path in this file is assembled from a repo-relative constant or an
    /// <see cref="AllowedSuppressions"/> key, so the guard should never fire — it exists so that a
    /// later edit which makes one of those absolute fails loudly here instead of silently walking
    /// a different tree and reporting a clean result.
    /// </remarks>
    private static string RepoPath(string root, params string[] relativeSegments)
    {
        var combined = root;
        foreach (var segment in relativeSegments)
        {
            var normalized = segment.Replace('/', Path.DirectorySeparatorChar);

            Assert.False(
                Path.IsPathRooted(normalized),
                $"'{segment}' must be repo-relative; a rooted segment would silently discard '{combined}'.");

            combined = Path.Combine(combined, normalized);
        }

        var resolved = Path.GetFullPath(combined);
        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        return resolved;
    }

    /// <summary>The doc-app corpus root, assembled through <see cref="RepoPath"/>.</summary>
    private static string AppsDir(string repoRoot) => RepoPath(repoRoot, AppsRelative);

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
