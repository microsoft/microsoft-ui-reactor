using System.Text;
using Microsoft.UI.Reactor.Markdown;
using SharpFuzz;

namespace Microsoft.UI.Reactor.Fuzz;

/// <summary>
/// libFuzzer-driven harness over <see cref="MarkdownHtml.Render"/>, which is
/// the public entry that drives <c>Md4cParser.Parse</c>. Inputs are treated
/// as UTF-8 markdown text (md4c is byte-oriented in its C origin and tolerant
/// of malformed UTF-8; the .NET <see cref="Encoding.UTF8"/> default replaces
/// invalid sequences with U+FFFD, preserving full byte-space exploration).
/// </summary>
internal static class MarkdownFuzzHarness
{
    public static void Run()
    {
        // DialectGitHub enables tables, strikethrough, task lists, and the
        // three permissive autolink modes — maximizes the parser surface area
        // visible to the fuzzer in a single target.
        const MarkdownParserFlags parserFlags = MarkdownParserFlags.DialectGitHub;
        const MarkdownHtml.HtmlFlags renderFlags = MarkdownHtml.HtmlFlags.None;

        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            string text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

            var sb = new StringBuilder();
            MarkdownHtml.Render(text, parserFlags, renderFlags, sb);
        });
    }
}
