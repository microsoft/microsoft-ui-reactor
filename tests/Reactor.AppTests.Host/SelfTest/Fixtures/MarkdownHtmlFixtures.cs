using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Tests the Markdown → HTML generation path (MarkdownHtml) and displays the result in a WebView2.
/// This covers the HTML renderer that the native-element fixtures do not exercise.
/// </summary>
internal static class MarkdownHtmlFixtures
{
    private const string SampleMarkdown = """
        # Project Status Report

        ## Overview

        The **Reactor framework** is a _declarative UI toolkit_ for WinUI 3.
        It supports ~~imperative~~ **reactive** component rendering.

        ## Features

        - Virtual element tree with keyed reconciliation
        - Hooks: `UseState`, `UseEffect`, `UseRef`, `UseMemo`
        - CSS Flexbox layout via [Yoga](https://yogalayout.dev/)
        - Native Rust differ for O(n) tree diffing

        ### Code Example

        ```csharp
        var app = ReactorApp.Run<MyApp>();
        ```

        ## Roadmap

        | Quarter | Milestone         | Status      |
        |---------|-------------------|-------------|
        | Q1      | Core reconciler   | ✅ Complete |
        | Q2      | Flex layout       | ✅ Complete |
        | Q3      | Animation system  | 🚧 Active  |
        | Q4      | Plugin ecosystem  | 📋 Planned  |

        1. Finish animation API
        2. Ship v1.0
        3. Open-source release

        > **Note:** This is a living document.
        > Updated weekly by the core team.

        ---

        For questions, contact [the team](mailto:duct@example.com).
        """;

    internal class HtmlGeneration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Generate HTML from markdown using the md4c HTML renderer
            var html = MarkdownHtml.ToHtml(SampleMarkdown, MarkdownParserFlags.DialectGitHub);

            // Verify the HTML contains expected structural elements
            H.Check("MdHtml_HasH1", html.Contains("<h1>"));
            H.Check("MdHtml_HasH2", html.Contains("<h2>"));
            H.Check("MdHtml_HasH3", html.Contains("<h3>"));
            H.Check("MdHtml_HasStrong", html.Contains("<strong>"));
            H.Check("MdHtml_HasEm", html.Contains("<em>"));
            H.Check("MdHtml_HasDel", html.Contains("<del>"));
            H.Check("MdHtml_HasCode", html.Contains("<code>"));
            H.Check("MdHtml_HasPre", html.Contains("<pre>"));
            H.Check("MdHtml_HasLink", html.Contains("<a href="));
            H.Check("MdHtml_HasTable", html.Contains("<table>"));
            H.Check("MdHtml_HasTh", html.Contains("<th"));
            H.Check("MdHtml_HasTd", html.Contains("<td"));
            H.Check("MdHtml_HasUl", html.Contains("<ul>"));
            H.Check("MdHtml_HasOl", html.Contains("<ol>"));
            H.Check("MdHtml_HasBlockquote", html.Contains("<blockquote>"));
            H.Check("MdHtml_HasHr", html.Contains("<hr"));

            await Task.CompletedTask;
        }
    }

    internal class HtmlInWebView2(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Generate a full HTML document from the markdown
            var bodyHtml = MarkdownHtml.ToHtml(SampleMarkdown, MarkdownParserFlags.DialectGitHub);
            var fullHtml = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <style>
                        body { font-family: 'Segoe UI', sans-serif; padding: 24px; line-height: 1.6; }
                        code { background: #f0f0f0; padding: 2px 6px; border-radius: 3px; font-family: Consolas, monospace; }
                        pre { background: #f0f0f0; padding: 16px; border-radius: 6px; overflow-x: auto; }
                        blockquote { border-left: 4px solid #0078d4; margin-left: 0; padding-left: 16px; color: #555; }
                        table { border-collapse: collapse; width: 100%; }
                        th, td { border: 1px solid #ddd; padding: 8px 12px; text-align: left; }
                        th { background: #f5f5f5; font-weight: 600; }
                        hr { border: none; border-top: 1px solid #ddd; margin: 24px 0; }
                    </style>
                </head>
                <body>{{bodyHtml}}</body>
                </html>
                """;

            // Mount a WebView2 and load the HTML into it
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(
                    TextBlock("Markdown \u2192 HTML \u2192 WebView2"),
                    (WebView2() with { OnNavigationCompleted = _ => { } })
                        .Width(800).Height(600)
                        .Set(wv =>
                        {
                            _ = InitWebView(wv, fullHtml);
                        })
                )
            );

            // WebView2 initialization is async — give it time
            await Harness.Render(2000);

            // Verify the WebView2 was mounted
            var webView = H.FindControl<WebView2>(_ => true);
            H.Check("MdHtml_WebView2Mounted", webView is not null);

            // Verify the title text rendered
            H.Check("MdHtml_TitleVisible",
                H.FindText("Markdown \u2192 HTML \u2192 WebView2") is not null);

            // Check HTML content is valid and substantial
            H.Check("MdHtml_HtmlNotEmpty", fullHtml.Length > 500);
            H.Check("MdHtml_HtmlHasDoctype", fullHtml.Contains("<!DOCTYPE html>"));
        }

        private static async Task InitWebView(WebView2 wv, string html)
        {
            await wv.EnsureCoreWebView2Async();
            wv.NavigateToString(html);
        }
    }
}
