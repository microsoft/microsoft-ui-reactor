using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class WinUiRefFrontMatterTests
{
    [Fact]
    public void Emits_callout_when_winui_ref_present()
    {
        var content = """
            ---
            title: "Buttons"
            winui-ref: "https://learn.microsoft.com/en-us/windows/apps/design/controls/buttons"
            ---

            The Reactor `Button` factory wraps WinUI's `Button`.
            """;

        var body = TemplateParser.ParseContent(content).Body.Replace("\r\n", "\n");

        Assert.StartsWith("The Reactor `Button` factory", body.TrimStart());
        Assert.EndsWith("\n\n> **WinUI reference:** For the full property surface and design guidance, see [Buttons](https://learn.microsoft.com/en-us/windows/apps/design/controls/buttons).\n", body);
    }

    [Fact]
    public void Places_callout_before_first_section_outside_code()
    {
        var content = """
            ---
            title: "Buttons"
            winui-ref: "https://learn.microsoft.com/en-us/windows/apps/design/controls/buttons"
            ---

            # Buttons

            Intro.

            ```csharp
            ## not a heading
            ```

            ## Usage

            Body.
            """;

        var body = TemplateParser.ParseContent(content).Body.Replace("\r\n", "\n");

        var callout = body.IndexOf("> **WinUI reference:**", System.StringComparison.Ordinal);
        Assert.True(callout > body.IndexOf("## not a heading", System.StringComparison.Ordinal));
        Assert.Contains("```\n\n> **WinUI reference:**", body);
        Assert.Contains("(https://learn.microsoft.com/en-us/windows/apps/design/controls/buttons).\n\n## Usage", body);
        Assert.StartsWith("# Buttons\n\nIntro.", body.TrimStart());
    }

    [Fact]
    public void A_longer_fence_is_not_closed_by_a_shorter_inner_fence()
    {
        var body = "# Page\n\nIntro.\n\n````markdown\n```csharp\nvar x = 1;\n```\n## inside the example\n````\n\n## Usage\n";

        var result = TemplateParser.InsertBeforeFirstSection(body, "CALLOUT").Replace("\r\n", "\n");

        Assert.Contains("````\n\nCALLOUT\n\n## Usage", result);
        Assert.DoesNotContain("CALLOUT\n\n## inside the example", result);
    }

    [Fact]
    public void Title_cases_hyphenated_path_segments()
    {
        var content = """
            ---
            title: "Auto suggest"
            winui-ref: "https://learn.microsoft.com/en-us/windows/apps/design/controls/auto-suggest-box"
            ---

            Body.
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.Contains("[Auto Suggest Box]", template.Body);
    }

    [Fact]
    public void Omits_callout_when_winui_ref_missing()
    {
        var content = """
            ---
            title: "Reactor-original concept"
            ---

            Body.
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.Empty(template.WinUiRef);
        Assert.DoesNotContain("WinUI reference", template.Body);
    }

    [Fact]
    public void Falls_back_to_raw_url_when_unparseable()
    {
        var content = """
            ---
            title: "Edge case"
            winui-ref: "not-a-real-url"
            ---

            Body.
            """;

        var template = TemplateParser.ParseContent(content);
        Assert.Contains("](not-a-real-url)", template.Body);
    }
}
