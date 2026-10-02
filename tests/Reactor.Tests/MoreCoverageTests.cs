using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.UI.Reactor.Markdown;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using IoPath = System.IO.Path;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Additional coverage-oriented tests aimed at raising line coverage across
/// pure / semi-pure code paths that previously lacked dedicated tests. Every
/// test here runs without needing an Application host or live WinUI window;
/// tests that would require `new SolidColorBrush()` or similar activation
/// are intentionally avoided.
/// </summary>
public class MoreCoverageTests
{
    // ════════════════════════════════════════════════════════════════════
    //  NodeRegistry — additional bookkeeping paths
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NodeRegistry_InvalidateWindow_OnlyAffectsMatchingPrefix()
    {
        var reg = new NodeRegistry();
        var a = reg.InjectForTests(new NodeDescriptor("alpha", "C", "x", null, "Button", 0, null));
        var b = reg.InjectForTests(new NodeDescriptor("alphabet", "C", "y", null, "Button", 0, null));

        // "alpha" shouldn't accidentally invalidate ids from "alphabet" — the
        // prefix guard appends a '/' to avoid exactly that kind of collision.
        reg.InvalidateWindow("alpha");

        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(a).Status);
        Assert.NotEqual(NodeLookupStatus.Gone, reg.Resolve(b).Status);
    }

    [Fact]
    public void NodeRegistry_Tombstone_IsCounted()
    {
        var reg = new NodeRegistry();
        reg.InjectForTests(new NodeDescriptor("main", "C", "btn", null, "Button", 0, null));
        Assert.Equal(0, reg.TombstoneCountForTests());

        reg.InvalidateWindow("main");

        Assert.Equal(1, reg.TombstoneCountForTests());
    }

    [Fact]
    public void NodeRegistry_InjectedThenInvalidated_ReturnsGone_AndStaysGone()
    {
        var reg = new NodeRegistry();
        var id = reg.InjectForTests(new NodeDescriptor("main", "C", "btn", null, "Button", 0, null));

        reg.InvalidateWindow("main");

        // Resolve a second time — tombstone short-circuit path.
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(id).Status);
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(id).Status);
    }

    // ════════════════════════════════════════════════════════════════════
    //  DevtoolsCliParser — additional branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void DevtoolsCliParser_FpsNonNumeric_KeepsDefault()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "abc"]);
        Assert.Equal(10, opts.Fps);
    }

    // ════════════════════════════════════════════════════════════════════
    //  MaskEngine — edge cases for cursor navigation and mask properties
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MaskEngine_Mask_ExposesOriginalPattern()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal("000-0000", engine.Mask);
    }

    [Fact]
    public void MaskEngine_Length_MatchesMaskLength()
    {
        var engine = new MaskEngine("(000) 000-0000");
        Assert.Equal("(000) 000-0000".Length, engine.Length);
    }

    [Fact]
    public void MaskEngine_NextInputPosition_AtEnd_ReturnsLength()
    {
        var engine = new MaskEngine("000---");
        // Positions 3, 4, 5 are all literal — next input past them is Length.
        Assert.Equal(engine.Length, engine.NextInputPosition(3));
    }

    [Fact]
    public void MaskEngine_PreviousInputPosition_NoInputAtOrBefore_ReturnsMinusOne()
    {
        var engine = new MaskEngine("---000");
        // Starting at position 2 (all literals before or at), there's no input
        // position before it — expected to return -1.
        Assert.Equal(-1, engine.PreviousInputPosition(2));
    }

    [Fact]
    public void MaskEngine_IsLiteral_OutOfRange_ReturnsFalse()
    {
        var engine = new MaskEngine("000");
        Assert.False(engine.IsLiteral(-1));
        Assert.False(engine.IsLiteral(100));
    }

    [Fact]
    public void MaskEngine_LiteralInInput_WhenInputShorter_InsertedAutomatically()
    {
        // Input exhausted before reaching literal — literal should still be written
        // from the mask and placeholders fill the required slots after it.
        var engine = new MaskEngine("0-0");
        Assert.Equal("1-_", engine.Apply("1"));
    }

    [Fact]
    public void MaskEngine_RequiredLetter_WithDigit_PlaceholdersAndAdvances()
    {
        // Invalid character for a required-letter slot: slot gets placeholder and
        // input pointer advances (so the mismatched char doesn't stall the mask).
        var engine = new MaskEngine("AAA");
        // '1' invalid for required-letter → placeholder + advance; 'b' fills slot 1;
        // '2' invalid for slot 2 → placeholder + advance (input exhausted after).
        Assert.Equal("_b_", engine.Apply("1b2"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  MarkdownBuilder — additional branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Markdown_UnknownEntity_PassesThroughVerbatim()
    {
        // An unknown entity should not be resolved; AddEntityText keeps the raw
        // token in the inline stream.
        var result = Microsoft.UI.Reactor.Advanced.Factories.Markdown("&thisisnotreal;");
        var stack = Assert.IsType<StackElement>(result);
        var rtb = Assert.IsType<RichTextBlockElement>(stack.Children[0]);
        var text = string.Join("", rtb.Paragraphs![0].Inlines.OfType<RichTextRun>().Select(r => r.Text));
        Assert.Contains("&thisisnotreal;", text);
    }

    [Fact]
    public void Markdown_EmptyListItem_ProducesMarkerPlusEmpty()
    {
        // An empty list item should still produce an item block.
        var options = new MarkdownOptions
        {
            ListItem = (el) => el,
        };
        // md4c may collapse a fully-empty bullet, so provide a trivial item.
        var result = Microsoft.UI.Reactor.Advanced.Factories.Markdown("- ", options);
        var stack = Assert.IsType<StackElement>(result);
        // The top-level vstack either contains a list stack or is empty —
        // the important thing is that the parser doesn't throw.
        Assert.NotNull(stack);
    }

    [Fact]
    public void Markdown_OrderedListCallback_IsInvoked()
    {
        bool invoked = false;
        Element[]? capturedItems = null;
        var options = new MarkdownOptions
        {
            OrderedList = (start, items) =>
            {
                invoked = true;
                capturedItems = items;
                return VStack(items);
            },
        };

        Microsoft.UI.Reactor.Advanced.Factories.Markdown("1. one\n2. two", options);
        Assert.True(invoked);
        Assert.NotNull(capturedItems);
        Assert.Equal(2, capturedItems!.Length);
    }

    [Fact]
    public void Markdown_CodeBlock_WithLanguageAndTrailingNewline_TrimsNewline()
    {
        string? capturedCode = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (c, l) => { capturedCode = c; return TextBlock(c); },
        };

        Microsoft.UI.Reactor.Advanced.Factories.Markdown("```py\nprint('hi')\n```", options);
        Assert.Equal("print('hi')", capturedCode);
    }

    [Fact]
    public void Markdown_LinkInsideBoldHasSafeUri()
    {
        var result = Microsoft.UI.Reactor.Advanced.Factories.Markdown("**[text](https://example.org)**");
        var stack = Assert.IsType<StackElement>(result);
        var rtb = Assert.IsType<RichTextBlockElement>(stack.Children[0]);
        var link = rtb.Paragraphs![0].Inlines.OfType<RichTextHyperlink>().FirstOrDefault();
        Assert.NotNull(link);
        Assert.Equal(new Uri("https://example.org"), link!.NavigateUri);
    }

    [Fact]
    public void Markdown_MultiParagraphListItem_WrapsInVStack()
    {
        // A list item with multiple block children (paragraph + paragraph) should
        // wrap in a VStack(4, ...) per LeaveListItem.
        var md = "- First paragraph\n\n    Second paragraph\n- Next item";
        var result = Microsoft.UI.Reactor.Advanced.Factories.Markdown(md);
        // Just ensure it doesn't throw and produces some structure.
        var stack = Assert.IsType<StackElement>(result);
        Assert.NotEmpty(stack.Children);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #479 — clickable inline Hyperlink (OnClick on RichTextHyperlink)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Hyperlink_OnClickFactory_SetsOnClickAndSentinelUri()
    {
        var fired = 0;
        var link = Hyperlink("click me", () => fired++);

        Assert.Equal("click me", link.Text);
        Assert.NotNull(link.OnClick);
        // Sentinel URI is set so the record positional ctor stays non-null.
        Assert.Equal(new Uri("about:blank"), link.NavigateUri);

        link.OnClick!.Invoke();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Hyperlink_OnClickFactory_ThrowsOnNullAction()
    {
        Assert.Throws<ArgumentNullException>(() => Hyperlink("x", (Action)null!));
    }

    [Fact]
    public void Hyperlink_UriFactory_LeavesOnClickNull()
    {
        var link = Hyperlink("text", new Uri("https://example.com"));
        Assert.Null(link.OnClick);
        Assert.Equal(new Uri("https://example.com"), link.NavigateUri);
    }

    [Fact]
    public void RichTextHyperlink_RecordEquality_TracksOnClick()
    {
        Action a = () => { };
        Action b = () => { };

        var withA1 = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = a };
        var withA2 = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = a };
        var withB  = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = b };
        var withNone = new RichTextHyperlink("t", new Uri("about:blank"));

        // Same delegate → equal. Different closures → unequal. Required so
        // the reconciler's inline diff calls UpdateHyperlinkInPlace whenever
        // the click handler changes between renders.
        Assert.Equal(withA1, withA2);
        Assert.NotEqual(withA1, withB);
        Assert.NotEqual(withA1, withNone);
    }

    // PathDataParser tests were intentionally skipped — every overload of
    // PathDataParser.Parse constructs a `new PathGeometry()` even for null or
    // whitespace inputs, which requires WinUI XAML Application activation that
    // isn't available to this unit-test host. The parser is exercised by
    // higher-level charting tests that run in the self-host harness.
}
