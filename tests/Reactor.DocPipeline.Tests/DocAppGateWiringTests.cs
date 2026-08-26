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
        var appsDir = Path.Combine(repoRoot, AppsRelative.Replace('/', Path.DirectorySeparatorChar));

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

        var proj = XDocument.Load(Path.Combine(appsDir, "DocApps.proj"));
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
        foreach (var include in includes)
        {
            var pattern = include!.Replace("$(MSBuildThisFileDirectory)", string.Empty)
                                  .Replace('/', Path.DirectorySeparatorChar);
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
        var propsPath = Path.Combine(
            repoRoot, AppsRelative.Replace('/', Path.DirectorySeparatorChar), "Directory.Build.props");

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
    /// A fix that silences a rule is not a fix. Doc apps are teaching material, so a suppression
    /// there ships the anti-pattern to every reader who copies the snippet.
    /// </summary>
    [Fact]
    public void No_Doc_App_Suppresses_A_Reactor_Rule()
    {
        var repoRoot = FindRepoRoot();
        var appsDir = Path.Combine(repoRoot, AppsRelative.Replace('/', Path.DirectorySeparatorChar));

        var offenders = new List<string>();
        var pragma = new Regex(@"#pragma\s+warning\s+disable\s+(?<ids>[^\r\n/]+)", RegexOptions.Compiled);
        var noWarn = new Regex(@"<NoWarn>(?<ids>[^<]*)</NoWarn>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var editorConfigNone = new Regex(
            @"dotnet_diagnostic\.(?<id>REACTOR_[A-Z0-9_]+)\.severity\s*=\s*(none|silent|suggestion)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var reactorId = new Regex(@"REACTOR_[A-Z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var file in Directory.EnumerateFiles(appsDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);

            // bin/obj carry generated copies; scanning them would double-report.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var isSource = ext is ".cs" or ".csproj" or ".props" or ".proj";
            var isEditorConfig = name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);
            if (!isSource && !isEditorConfig) continue;

            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(repoRoot, file);
            var app = OwningApp(appsDir, file);

            foreach (var (kind, ids) in EnumerateSuppressed(text, pragma, noWarn, editorConfigNone, reactorId))
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
        var appsDir = Path.Combine(repoRoot, AppsRelative.Replace('/', Path.DirectorySeparatorChar));

        var stale = new List<string>();
        foreach (var (app, rules) in AllowedSuppressions)
        {
            var appDir = Path.Combine(appsDir, app);
            Assert.True(Directory.Exists(appDir), $"AllowedSuppressions names '{app}', which no longer exists.");

            var text = string.Concat(Directory
                .EnumerateFiles(appDir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f) is ".cs" or ".csproj" or ".editorconfig")
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

            foreach (var rule in rules)
            {
                if (!text.Contains(rule, StringComparison.OrdinalIgnoreCase))
                    stale.Add($"{app} -> {rule}");
            }
        }

        Assert.True(
            stale.Count == 0,
            "These AllowedSuppressions entries are no longer present in the doc app. Remove them so "
            + "the ledger keeps matching reality:\n  " + string.Join("\n  ", stale));
    }

    private static IEnumerable<(string Kind, List<string> Ids)> EnumerateSuppressed(
        string text, Regex pragma, Regex noWarn, Regex editorConfigNone, Regex reactorId)
    {
        foreach (Match m in pragma.Matches(text))
        {
            var ids = reactorId.Matches(m.Groups["ids"].Value).Select(x => x.Value).ToList();
            if (ids.Count > 0) yield return ("#pragma warning disable", ids);
        }

        foreach (Match m in noWarn.Matches(text))
        {
            var ids = reactorId.Matches(m.Groups["ids"].Value).Select(x => x.Value).ToList();
            if (ids.Count > 0) yield return ("<NoWarn>", ids);
        }

        foreach (Match m in editorConfigNone.Matches(text))
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
        var appsDir = Path.Combine(repoRoot, AppsRelative.Replace('/', Path.DirectorySeparatorChar));

        var sources = Directory
            .EnumerateFiles(appsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            sources.Count >= 50,
            $"The suppression scan walked only {sources.Count} doc-app sources; it is no longer "
            + "measuring the corpus it claims to cover.");

        // Positive control: the same patterns, applied to a planted violation, must fire. Without
        // this, a regex broken by a future edit would report zero offenders and pass forever.
        var pragma = new Regex(@"#pragma\s+warning\s+disable\s+(?<ids>[^\r\n/]+)", RegexOptions.Compiled);
        var reactorId = new Regex(@"REACTOR_[A-Z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var planted = pragma.Match("#pragma warning disable REACTOR_THEME_001 // planted");
        Assert.True(planted.Success);
        Assert.Equal("REACTOR_THEME_001", reactorId.Match(planted.Groups["ids"].Value).Value);

        // ...and must not fire on a non-Reactor suppression, or the gate would be noise.
        var unrelated = pragma.Match("#pragma warning disable CS0168");
        Assert.True(unrelated.Success);
        Assert.DoesNotMatch(reactorId, unrelated.Groups["ids"].Value);
    }

    /// <summary>
    /// The gate has to actually run. A traversal project and wired analyzers are inert if no
    /// workflow builds them.
    /// </summary>
    [Fact]
    public void Ci_Runs_The_Doc_Snippet_Gate()
    {
        var repoRoot = FindRepoRoot();
        var ci = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("docs-snippet-gate:", ci, StringComparison.Ordinal);
        Assert.Contains("DocApps.proj", ci, StringComparison.Ordinal);

        // The scan must stay case-sensitive: an insensitive match also hits the restore's
        // "warning NU1900: Error occurred ...", turning the gate into a flaky failure that
        // someone would eventually "fix" by loosening it.
        Assert.Contains("-CaseSensitive", ci, StringComparison.Ordinal);
    }

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
