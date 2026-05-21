using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Markdown;

namespace Microsoft.UI.Reactor.Fuzz;

/// <summary>
/// CI-friendly fuzz-free pass: feed every file in <c>corpus/markdown</c> and
/// <c>corpus/pathdata</c> through the matching harness body once. Surfaces
/// harness rot (renamed APIs, broken seed inputs) without requiring the
/// libfuzzer-dotnet driver or SharpFuzz instrumentation tool in the CI image.
/// </summary>
internal static class SmokeRunner
{
    public static int Run()
    {
        // Path.Join (not Path.Combine) — Combine would silently discard
        // baseDir if a later segment ever became rooted; Join always
        // concatenates with a separator.
        string baseDir = AppContext.BaseDirectory;
        string corpusDir = Path.Join(baseDir, "corpus");
        if (!Directory.Exists(corpusDir))
        {
            Console.Error.WriteLine($"Smoke: corpus directory not found at {corpusDir}");
            return 1;
        }

        int failures = 0;
        failures += RunCorpus(
            "markdown",
            Path.Join(corpusDir, "markdown"),
            text =>
            {
                var sb = new StringBuilder();
                MarkdownHtml.Render(text, MarkdownParserFlags.DialectGitHub, MarkdownHtml.HtmlFlags.None, sb);
            });

        failures += RunCorpus(
            "pathdata",
            Path.Join(corpusDir, "pathdata"),
            PathDataParser.ParseTokens);

        if (failures > 0)
        {
            Console.Error.WriteLine($"Smoke: FAILED — {failures} seed input(s) threw uncaught exceptions.");
            return 1;
        }

        Console.Out.WriteLine("Smoke: OK");
        return 0;
    }

    // Broad catch is the intended behavior here: the smoke runner's job is to
    // surface *any* parser exception per seed (FormatException from numeric
    // tokens, NullReferenceException from a regression, OOM from runaway
    // allocation — all of them point at a bug we want CI to fail on). Catching
    // and reporting per seed means one bad seed doesn't mask others.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Smoke runner must surface any exception thrown by a parser seed; broad catch is the intended design.")]
    private static int RunCorpus(string label, string dir, Action<string> action)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Smoke[{label}]: corpus directory not found at {dir}");
            return 1;
        }

        // Sort by file name so CI logs are deterministic across machines and
        // filesystems — Directory.GetFiles makes no ordering guarantee.
        var files = Directory.GetFiles(dir);
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"Smoke[{label}]: no seed files in {dir}");
            return 1;
        }

        int failures = 0;
        foreach (var file in files)
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
            try
            {
                action(text);
                Console.Out.WriteLine($"  ok    [{label}] {Path.GetFileName(file)}");
            }
            catch (Exception ex) // CodeQL[cs/catch-of-all-exceptions]: intentional — see method-level suppression.
            {
                failures++;
                Console.Error.WriteLine($"  THROW [{label}] {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return failures;
    }
}
