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
        string baseDir = AppContext.BaseDirectory;
        string corpusDir = Path.Combine(baseDir, "corpus");
        if (!Directory.Exists(corpusDir))
        {
            Console.Error.WriteLine($"Smoke: corpus directory not found at {corpusDir}");
            return 1;
        }

        int failures = 0;
        failures += RunCorpus(
            "markdown",
            Path.Combine(corpusDir, "markdown"),
            text =>
            {
                var sb = new StringBuilder();
                MarkdownHtml.Render(text, MarkdownParserFlags.DialectGitHub, MarkdownHtml.HtmlFlags.None, sb);
            });

        failures += RunCorpus(
            "pathdata",
            Path.Combine(corpusDir, "pathdata"),
            PathDataParser.ParseTokens);

        if (failures > 0)
        {
            Console.Error.WriteLine($"Smoke: FAILED — {failures} seed input(s) threw uncaught exceptions.");
            return 1;
        }

        Console.Out.WriteLine("Smoke: OK");
        return 0;
    }

    private static int RunCorpus(string label, string dir, Action<string> action)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Smoke[{label}]: corpus directory not found at {dir}");
            return 1;
        }

        var files = Directory.GetFiles(dir);
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
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"  THROW [{label}] {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return failures;
    }
}
