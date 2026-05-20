using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class RichMessageTests
{
    private static IntlAccessor CreateAccessor(string locale, InMemoryResourceProvider provider)
    {
        return new IntlAccessor(locale, provider, new MessageCache(), "en-US");
    }

    // ── Simple tag mapping ─────────────────────────────────────────

    [Fact]
    public void RichMessage_SimpleTag_MapsToElement()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Help", "ClickHere", "Click <bold>here</bold> to continue.");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Help", "ClickHere"), tags: new()
        {
            ["bold"] = text => new TextBlockElement(text) { FontSize = 20 },
        });

        // Should be a GroupElement with 3 children: "Click ", bold element, " to continue."
        var group = Assert.IsType<GroupElement>(result);
        Assert.Equal(3, group.Children.Length);
        Assert.Equal("Click ", Assert.IsType<TextBlockElement>(group.Children[0]).Content);
        Assert.Equal("here", Assert.IsType<TextBlockElement>(group.Children[1]).Content);
        Assert.Equal(20.0, Assert.IsType<TextBlockElement>(group.Children[1]).FontSize);
        Assert.Equal(" to continue.", Assert.IsType<TextBlockElement>(group.Children[2]).Content);
    }

    // ── Multiple tags ──────────────────────────────────────────────

    [Fact]
    public void RichMessage_MultipleTags_CorrectOrdering()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Help", "Instructions", "Press <key>Enter</key> or click <btn>Submit</btn>.");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Help", "Instructions"), tags: new()
        {
            ["key"] = text => new TextBlockElement($"[{text}]"),
            ["btn"] = text => new TextBlockElement(text) { FontSize = 14 },
        });

        var group = Assert.IsType<GroupElement>(result);
        Assert.Equal(5, group.Children.Length);
        Assert.Equal("Press ", Assert.IsType<TextBlockElement>(group.Children[0]).Content);
        Assert.Equal("[Enter]", Assert.IsType<TextBlockElement>(group.Children[1]).Content);
        Assert.Equal(" or click ", Assert.IsType<TextBlockElement>(group.Children[2]).Content);
        Assert.Equal("Submit", Assert.IsType<TextBlockElement>(group.Children[3]).Content);
        Assert.Equal(".", Assert.IsType<TextBlockElement>(group.Children[4]).Content);
    }

    // ── Tags with ICU arguments ────────────────────────────────────

    [Fact]
    public void RichMessage_TagsWithIcuArgs_ResolvedAndMapped()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Profile", "Welcome", "Hello <bold>{name}</bold>, welcome!");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Profile", "Welcome"),
            args: new Dictionary<string, object> { ["name"] = "Alice" },
            tags: new()
            {
                ["bold"] = text => new TextBlockElement(text) { FontSize = 20 },
            });

        var group = Assert.IsType<GroupElement>(result);
        Assert.Equal(3, group.Children.Length);
        Assert.Equal("Hello ", Assert.IsType<TextBlockElement>(group.Children[0]).Content);
        Assert.Equal("Alice", Assert.IsType<TextBlockElement>(group.Children[1]).Content);
        Assert.Equal(", welcome!", Assert.IsType<TextBlockElement>(group.Children[2]).Content);
    }

    // ── No tags -> plain text element ──────────────────────────────

    [Fact]
    public void RichMessage_NoTags_FallsToPlainTextBlockElement()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Greeting", "Hello, world!");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Common", "Greeting"));

        var text = Assert.IsType<TextBlockElement>(result);
        Assert.Equal("Hello, world!", text.Content);
    }

    [Fact]
    public void RichMessage_EmptyTagsDictionary_FallsToPlainTextBlockElement()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Greeting", "Hello, world!");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Common", "Greeting"), tags: new());

        var text = Assert.IsType<TextBlockElement>(result);
        Assert.Equal("Hello, world!", text.Content);
    }

    // ── Missing key ────────────────────────────────────────────────

    [Fact]
    public void RichMessage_MissingKey_ReturnsMissingMarkerText()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.RichMessage(new MessageKey("Common", "Missing"), tags: new()
        {
            ["bold"] = text => new TextBlockElement(text),
        });

        var text = Assert.IsType<TextBlockElement>(result);
        Assert.Equal("[?? Common.Missing ??]", text.Content);
    }

    // ── Unknown tag stripped to plain text ──────────────────────────

    [Fact]
    public void RichMessage_UnknownTag_RendersContentAsPlainText()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Help", "Info", "See <unknown>this</unknown> for details.");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Help", "Info"), tags: new()
        {
            ["bold"] = text => new TextBlockElement(text),
        });

        var group = Assert.IsType<GroupElement>(result);
        Assert.Equal(3, group.Children.Length);
        Assert.Equal("See ", Assert.IsType<TextBlockElement>(group.Children[0]).Content);
        Assert.Equal("this", Assert.IsType<TextBlockElement>(group.Children[1]).Content);
        Assert.Equal(" for details.", Assert.IsType<TextBlockElement>(group.Children[2]).Content);
    }

    // ── Single tag wrapping entire string -> single element, not group ──

    [Fact]
    public void RichMessage_SingleTagWrappingAll_ReturnsSingleElement()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "All", "<bold>Everything is bold</bold>");

        var t = CreateAccessor("en-US", provider);
        var result = t.RichMessage(new MessageKey("Common", "All"), tags: new()
        {
            ["bold"] = text => new TextBlockElement(text) { FontSize = 20 },
        });

        // Single element, not wrapped in GroupElement
        var text = Assert.IsType<TextBlockElement>(result);
        Assert.Equal("Everything is bold", text.Content);
        Assert.Equal(20.0, text.FontSize);
    }
}
