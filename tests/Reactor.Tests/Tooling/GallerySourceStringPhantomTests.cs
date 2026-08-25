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
    /// Verbatim literal, honouring the doubled-quote escape: @"...""..." .
    /// Gallery snippets are multi-line, so this must not stop at a newline.
    /// </summary>
    static readonly Regex VerbatimString = new(@"@""((?:[^""]|"""")*)""",
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
            foreach (Match m in VerbatimString.Matches(text))
            {
                var literal = m.Groups[1].Value.Replace("\"\"", "\"");
                results.Add((global::System.IO.Path.GetRelativePath(root, file).Replace('\\', '/'), literal));
            }
        }
        fileCount = n;
        return results;
    }
}
