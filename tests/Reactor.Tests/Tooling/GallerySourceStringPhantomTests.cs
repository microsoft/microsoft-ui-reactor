using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;
using Regex = global::System.Text.RegularExpressions.Regex;
using Match = global::System.Text.RegularExpressions.Match;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Repository gate for <see cref="PhantomSymbolLint.Surface.ExampleText"/>.
///
/// That surface is documented as covering "a gallery <c>SourceCode</c> string",
/// but nothing in production scanned one: <c>CompileCommand</c> lints assembled
/// guide templates, and the budget test lints <c>src/**</c> doc comments. The
/// gallery's displayed source is neither, so a phantom introduced there — the
/// exact defect fixed in TeachingTipPage on this branch — would regress
/// silently past every other gate.
///
/// The displayed snippets are verbatim string literals handed to SampleCard.
/// They are never compiled, which is precisely why they rot.
/// </summary>
public sealed class GallerySourceStringPhantomTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    const string GalleryRoot = "samples/ReactorGallery";

    /// <summary>
    /// Shipped agent-kit Markdown. These files are packed into the NuGet under
    /// <c>agentkit/</c> and are what an AI assistant reads when writing Reactor
    /// code, so a phantom here propagates straight into generated apps — but
    /// nothing compiled or linted them: <c>docs compile</c> only scans guide
    /// templates. Several of this PR's headline phantoms were still live here.
    /// </summary>
    static readonly string[] AgentKitRoots = ["plugins", "skills"];

    /// <summary>
    /// Verbatim literal, honouring the doubled-quote escape: @"...""..." .
    /// Gallery snippets are multi-line, so this must not stop at a newline.
    /// </summary>
    static readonly Regex VerbatimString = new(@"@""((?:[^""]|"""")*)""",
        global::System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>
    /// C# raw string literal: three-or-more quotes, same count to close.
    /// Newer gallery pages (ScrollViewPage, GridPage, …) use these instead of
    /// verbatim strings, so a verbatim-only sweep silently skipped them.
    /// </summary>
    static readonly Regex RawString = new("(?<q>\"{3,})(?<text>.*?)\\k<q>",
        global::System.Text.RegularExpressions.RegexOptions.Singleline);

    [Fact]
    public void GallerySourceStrings_NameNoPhantomApis()
    {
        var findings = new List<string>();
        int scannedFiles = 0, scannedLiterals = 0;

        foreach (var (path, literal) in EnumerateVerbatimLiterals(ref scannedFiles))
        {
            scannedLiterals++;
            foreach (var f in PhantomSymbolLint.Lint(path, literal, PhantomSymbolLint.Surface.ExampleText))
                findings.Add($"  {f.Format()}");
        }

        // Log the inputs, not just the verdict: a zero-finding result is only
        // meaningful if the sweep actually reached files and literals.
        _output.WriteLine($"scanned {scannedFiles} gallery file(s), {scannedLiterals} verbatim literal(s)");

        Assert.True(scannedFiles > 0, $"No .cs files found under {GalleryRoot} — the sweep is broken, not clean.");
        Assert.True(scannedLiterals > 0, "No verbatim string literals extracted — the extractor is broken, not clean.");

        Assert.True(findings.Count == 0,
            "REACTOR_DOC_PHANTOM_001: a phantom API is named in a gallery source string.\n" +
            "These strings are displayed to users but never compiled, so nothing else\n" +
            "validates them.\n" + string.Join("\n", findings));
    }

    [Fact]
    public void Extractor_AndMatcher_CanBothFire()
    {
        // Positive control for the gate above. Without this, a regex that
        // matched nothing and a genuinely clean gallery are the same result.
        const string sample = """
            SampleCard("Demo", Button("Go"),
                @"var target = this.UseElementRef<FrameworkElement>();
            Text(""Centered"")")
            """;

        var literals = VerbatimString.Matches(sample)
            .Select(m => m.Groups[1].Value.Replace("\"\"", "\""))
            .ToList();

        Assert.NotEmpty(literals);
        Assert.Contains(literals, l => l.Contains("UseElementRef", global::System.StringComparison.Ordinal));

        // The same matcher, same surface, must flag a phantom in that literal.
        var hits = literals.SelectMany(l =>
            PhantomSymbolLint.Lint("probe.cs", l, PhantomSymbolLint.Surface.ExampleText)).ToList();
        Assert.Contains(hits, f => f.Message.Contains("'Text'", global::System.StringComparison.Ordinal));
    }

    [Fact]
    public void Extractor_HandlesRawStringLiterals()
    {
        // Newer gallery pages use raw strings rather than verbatim literals
        // (ScrollViewPage, GridPage, …). A verbatim-only extractor skipped them
        // entirely, so this is the positive control for that half of the sweep.
        var q = new string('"', 3);
        var sample = "SampleCard(\"Demo\", ScrollView(),\n" + q + "\nScrollView(\n    Text(\"Item\")\n)\n" + q + ")";

        var literals = RawString.Matches(sample).Select(m => m.Groups["text"].Value).ToList();
        Assert.NotEmpty(literals);
        Assert.Contains(literals, l => l.Contains("ScrollView(", global::System.StringComparison.Ordinal));

        var hits = literals.SelectMany(l =>
            PhantomSymbolLint.Lint("probe.cs", l, PhantomSymbolLint.Surface.ExampleText)).ToList();
        Assert.Contains(hits, f => f.Message.Contains("'Text'", global::System.StringComparison.Ordinal));
    }

    [Fact]
    public void AgentKitMarkdown_NamesNoPhantomApis()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.False(string.IsNullOrEmpty(root), "repo root not found — the sweep is broken, not clean.");

        var findings = new List<string>();
        int files = 0;

        foreach (var relRoot in AgentKitRoots)
        {
            var dir = global::System.IO.Path.Combine(root!, relRoot);
            if (!global::System.IO.Directory.Exists(dir)) continue;

            foreach (var file in global::System.IO.Directory.EnumerateFiles(dir, "*.md", global::System.IO.SearchOption.AllDirectories))
            {
                files++;
                var rel = global::System.IO.Path.GetRelativePath(root!, file).Replace('\\', '/');
                foreach (var f in PhantomSymbolLint.Lint(rel, global::System.IO.File.ReadAllText(file), PhantomSymbolLint.Surface.Markdown))
                    findings.Add("  " + f.Format());
            }
        }

        _output.WriteLine($"scanned {files} agent-kit markdown file(s)");
        Assert.True(files > 0, "No agent-kit markdown found — the sweep is broken, not clean.");

        Assert.True(findings.Count == 0,
            "REACTOR_DOC_PHANTOM_001: a phantom API is named in shipped agent-kit markdown.\n" +
            "These files are packed into the NuGet under agentkit/ and are what an AI\n" +
            "assistant reads when writing Reactor code, so a phantom here propagates\n" +
            "into generated apps.\n" + string.Join("\n", findings));
    }

    static IEnumerable<(string Path, string Literal)> EnumerateVerbatimLiterals(ref int fileCount)
    {
        var root = RepoRootFinder.FindRepoRoot();
        if (string.IsNullOrEmpty(root)) return [];
        var dir = global::System.IO.Path.Combine(root, GalleryRoot.Replace('/', global::System.IO.Path.DirectorySeparatorChar));
        if (!global::System.IO.Directory.Exists(dir)) return [];

        var results = new List<(string, string)>();
        var files = global::System.IO.Directory.EnumerateFiles(dir, "*.cs", global::System.IO.SearchOption.AllDirectories);
        int n = 0;
        foreach (var file in files)
        {
            n++;
            var text = global::System.IO.File.ReadAllText(file);
            var rel = global::System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');

            // Raw strings first: a raw literal can contain @" sequences, and
            // matching verbatim inside one would split it into nonsense.
            var consumed = new List<(int Start, int End)>();
            foreach (Match m in RawString.Matches(text))
            {
                consumed.Add((m.Index, m.Index + m.Length));
                results.Add((rel, m.Groups["text"].Value));
            }

            foreach (Match m in VerbatimString.Matches(text))
            {
                if (consumed.Any(c => m.Index >= c.Start && m.Index < c.End)) continue;
                results.Add((rel, m.Groups[1].Value.Replace("\"\"", "\"")));
            }
        }
        fileCount = n;
        return results;
    }
}
