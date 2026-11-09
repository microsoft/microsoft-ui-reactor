using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class MarkdownTextSelectionTests
{
    private static Element Md(string text, MarkdownOptions? options = null) =>
        Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, options ?? new MarkdownOptions());
    [Fact]
    public void ExplicitSeparators_UseUtf16Offsets_AndNoTrailingSeparator()
    {
        var document = new MarkdownSelectionDocument();
        document.Rebuild([
            new(new object(), "• ", ""),
            new(new object(), "😀 café", "\n"),
            new(new object(), "a", "\t"),
            new(new object(), "b", "\n")]);
        Assert.Equal("• 😀 café\na\tb", document.Text);
        Assert.Equal(new[] { 0, 2, 10, 12 }, document.Entries.Select(e => e.Start));
        document.Select(2, 9);
        Assert.Equal("😀 café", document.SelectedText);
        Assert.Equal(9, document.End);
    }

    [Theory]
    [InlineData("a😀b", 1, 3)]
    [InlineData("ae\u0301b", 1, 3)]
    [InlineData("a👩‍💻b", 1, 6)]
    [InlineData("a🇺🇸b", 1, 5)]
    public void CaretMovement_SkipsWholeGrapheme(string text, int before, int after)
    {
        var document = new MarkdownSelectionDocument();
        document.Rebuild([new(new object(), text, "")]);
        document.Select(before, before);
        document.Move(1, true);
        Assert.Equal(after, document.Caret);
        Assert.Equal(text[before..after], document.SelectedText);
        document.Move(-1, false);
        Assert.Equal(before, document.Caret);
        Assert.Equal(before, document.Anchor);
    }

    [Fact]
    public void StreamingAppend_PreservesExistingSelection_NotSelectNewText()
    {
        var first = new object();
        var second = new object();
        var document = new MarkdownSelectionDocument();
        document.Rebuild([new(first, "first", "\n"), new(second, "part", "\n")]);
        document.Select(1, 10);
        document.Rebuild([new(first, "first", "\n"), new(second, "partial reply", "\n")]);
        Assert.Equal("irst\npart", document.SelectedText);
        Assert.Equal(10, document.Caret);
    }

    [Fact]
    public void InsertingEarlierSurface_PreservesBothEndpointsByIdentity()
    {
        var selected = new object();
        var document = new MarkdownSelectionDocument();
        document.Rebuild([new(selected, "target", "\n")]);
        document.Select(1, 5);
        document.Rebuild([new(new object(), "new", "\n"), new(selected, "target", "\n")]);
        Assert.Equal("arge", document.SelectedText);
        Assert.Equal(5, document.Anchor);
        Assert.Equal(9, document.Caret);
    }

    [Fact]
    public void TextEdit_PreservesUnchangedSuffix_AndClampsReplacedInterior()
    {
        var id = new object();
        var document = new MarkdownSelectionDocument();
        document.Rebuild([new(id, "before old after", "")]);
        document.Select(8, 16);
        document.Rebuild([new(id, "before replacement after", "")]);
        Assert.Equal(7, document.Anchor);
        Assert.Equal(24, document.Caret);
        Assert.Equal("replacement after", document.SelectedText);
    }

    [Fact]
    public void RemovingAndRecyclingSurfaces_DropsStaleText_AndBoundsSelection()
    {
        var document = new MarkdownSelectionDocument();
        var identity = new object();
        document.Rebuild([new(identity, "secret collapsed content", "")]);
        document.Select(0, document.Text.Length);
        document.Rebuild([]);
        Assert.Equal("", document.SelectedText);
        Assert.Equal(0, document.Caret);
        document.Rebuild([new(new object(), "fresh", "")]);
        Assert.Equal("fresh", document.Text);
        Assert.Equal("", document.SelectedText);
    }

    [Fact]
    public void UnchangedRegistration_DoesNotChangeSelection()
    {
        var id = new object();
        var document = new MarkdownSelectionDocument();
        Assert.True(document.Rebuild([new(id, "abcdef", "\n")]));
        document.Select(5, 1);
        Assert.False(document.Rebuild([new(id, "abcdef", "\n")]));
        Assert.Equal(5, document.Anchor);
        Assert.Equal(1, document.Caret);
        Assert.Equal("bcde", document.SelectedText);
    }

    [Fact]
    public void SameDocumentText_NewSurfaceBoundariesStillRefreshOffsets()
    {
        var first = new object();
        var second = new object();
        var document = new MarkdownSelectionDocument();
        document.Rebuild([new(first, "abc", ""), new(second, "d", "")]);
        Assert.True(document.Rebuild([new(first, "ab", ""), new(second, "cd", "")]));
        Assert.Equal("abcd", document.Text);
        Assert.Equal(2, document.Entries[0].End);
        Assert.Equal(2, document.Entries[1].Start);
        Assert.Equal("cd", document.Entries[1].Text);
    }

    [Fact]
    public void Copy_OneWrite_ThenSuccessfulFeedback()
    {
        var events = new List<string>();
        MarkdownSelectionCopy.Write("selected", text => events.Add("write:" + text),
            (text, success) => events.Add($"result:{text}:{success}"));
        Assert.Equal(new[] { "write:selected", "result:selected:True" }, events);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x800401D0))]
    [InlineData(unchecked((int)0x800401D1))]
    public void Copy_RecognizedFailure_ReportsFalseOnce(int hresult)
    {
        int writes = 0, notifications = 0;
        MarkdownSelectionCopy.Write("selected", _ =>
        {
            writes++;
            throw new global::System.Runtime.InteropServices.COMException("clipboard unavailable", hresult);
        }, (text, success) =>
        {
            notifications++;
            Assert.Equal("selected", text);
            Assert.False(success);
        });
        Assert.Equal(1, writes);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Copy_ProjectedAccessDenied_ReportsFailure()
    {
        bool? result = null;
        MarkdownSelectionCopy.Write("text", _ => throw new UnauthorizedAccessException(),
            (_, success) => result = success);
        Assert.False(result);
    }

    [Fact]
    public void Copy_UnexpectedWriteAndFeedbackErrorsPropagate()
    {
        bool notified = false;
        Assert.Throws<InvalidOperationException>(() => MarkdownSelectionCopy.Write("text",
            _ => throw new InvalidOperationException(), (_, _) => notified = true));
        Assert.False(notified);
        Assert.Throws<InvalidOperationException>(() => MarkdownSelectionCopy.Write("text",
            _ => { }, (_, _) => throw new InvalidOperationException()));
    }

    [Fact]
    public void DefaultMarkdown_HasNoSemanticWrappers()
    {
        var tree = Md("# heading\n\nbody\n\n- item");
        Assert.Empty(Surfaces(tree));
        Assert.IsType<RichTextBlockElement>(Assert.IsType<StackElement>(tree).Children[0]);
    }

    [Fact]
    public void OptIn_EmitsPlainTextAndExplicitListMarkers_NoPerLeafHost()
    {
        var options = new MarkdownTextSelectionOptions();
        var tree = Md("# **Title**\n\nA [link](https://example.com) &amp; `code`.\n\n- one\n- two",
            new MarkdownOptions { TextSelection = options, UnifiedRichText = true });
        var surfaces = Surfaces(tree).ToArray();
        Assert.Equal(new[] { "Title", "A link & code.", "• ", "one", "• ", "two" },
            surfaces.Select(s => s.Text));
        Assert.All(surfaces, s => Assert.Null(s.Options));
        Assert.All(surfaces.Where(s => s.Text == "• "), s => Assert.Equal("", s.SeparatorAfter));
        var scope = Assert.IsType<MarkdownSelectionElement>(MarkdownTextSelection.Scope(options,
            VStack(tree, Md("tail", new MarkdownOptions { TextSelection = options }))));
        Assert.Equal(SelectionRole.Scope, scope.Role);
        Assert.Same(options, scope.Options);
        Assert.Equal(7, Surfaces(scope.Content).Count());
        Assert.Equal("Title\nA link & code.\n• one\n• two\ntail", Join(Surfaces(scope.Content)));
    }

    [Fact]
    public void OptIn_DisablesNativeParagraphSelectionBeforeMount()
    {
        var legacy = Assert.IsType<StackElement>(Md("paragraph"));
        Assert.True(Assert.IsType<RichTextBlockElement>(Assert.Single(legacy.Children)).IsTextSelectionEnabled);

        var selected = Md("# heading\n\nparagraph", new MarkdownOptions { TextSelection = new() });
        Assert.All(Surfaces(selected), surface =>
            Assert.False(Assert.IsType<RichTextBlockElement>(surface.Content).IsTextSelectionEnabled));
    }

    [Fact]
    public void CustomLink_RegistersLabel_NotUrlOrToolbarSource()
    {
        var tree = Md("before [press](https://example.com/path) after",
            new MarkdownOptions
            {
                TextSelection = new(),
                LinkBuilder = link => Button(link.Text)
            });
        Assert.Equal("before press after", Assert.Single(Surfaces(tree)).Text);
    }

    [Fact]
    public void PresentationCallbacks_ReceiveRichText_KeepFormattingBeforeAnnotation()
    {
        bool paragraphCalled = false, headingCalled = false;
        var tree = Md("# **Heading**\n\n*Paragraph*", new MarkdownOptions
        {
            TextSelection = new(),
            Heading = (level, element) =>
            {
                Assert.Equal(1, level);
                var rich = Assert.IsType<RichTextBlockElement>(element);
                Assert.True(Assert.IsType<RichTextRun>(rich.Paragraphs![0].Inlines[0]).IsBold);
                headingCalled = true;
                return rich with { FontSize = 37 };
            },
            Paragraph = element =>
            {
                var rich = Assert.IsType<RichTextBlockElement>(element);
                Assert.True(Assert.IsType<RichTextRun>(rich.Paragraphs![0].Inlines[0]).IsItalic);
                paragraphCalled = true;
                return rich with { FontSize = 19 };
            }
        });
        Assert.True(headingCalled);
        Assert.True(paragraphCalled);
        var surfaces = Surfaces(tree).ToArray();
        Assert.Equal(new[] { "Heading", "Paragraph" }, surfaces.Select(s => s.Text));
        Assert.Equal(37, Assert.IsType<RichTextBlockElement>(surfaces[0].Content).FontSize);
        Assert.Equal(19, Assert.IsType<RichTextBlockElement>(surfaces[1].Content).FontSize);
    }

    [Fact]
    public void CustomTable_ReceivesSemanticRowsAndCells_WithExplicitSeparators()
    {
        int rowCount = 0;
        var tree = Md("| First | Second |\n| --- | --- |\n| a | b |", new MarkdownOptions
        {
            TextSelection = new(),
            TableHeaderBackground = null,
            Table = (rows, alignments) =>
            {
                rowCount = rows.Length;
                Assert.Equal(2, alignments.Length);
                return VStack(rows);
            }
        });
        Assert.Equal(2, rowCount);
        Assert.Equal(new[] { "First", "Second", "a", "b" }, Surfaces(tree).Select(s => s.Text));
        Assert.Equal("First\tSecond\na\tb", Join(Surfaces(tree)));
    }

    [Fact]
    public void CustomCodeBodySurface_ExcludesToolbarAndMarkdownSource()
    {
        var tree = Md("```csharp\nvar x = 1;\n```", new MarkdownOptions
        {
            TextSelection = new(),
            CodeBlock = (code, _) => VStack(
                Button("Copy code"),
                MarkdownTextSelection.Surface(code, TextBlock(code)))
        });
        Assert.Equal("var x = 1;", Join(Surfaces(tree)));
    }

    [Fact]
    public void ExplicitReplacementSurface_IsNotOverwrittenBySourceText()
    {
        var tree = Md("source", new MarkdownOptions
        {
            TextSelection = new(),
            Paragraph = _ => MarkdownTextSelection.Surface("visible replacement", TextBlock("visible replacement"))
        });
        Assert.Equal("visible replacement", Join(Surfaces(tree)));
    }

    [Fact]
    public void SurfaceOwnsOuterLayoutAndPlacement_WithoutDoubleApplying()
    {
        var original = TextBlock("text").Margin(2, 3, 4, 5).Width(250).Padding(9)
            .HAlign(HorizontalAlignment.Right).Grid(row: 2, column: 3, rowSpan: 4, columnSpan: 5)
            with { Key = "stable" };
        var surface = Assert.IsType<MarkdownSelectionElement>(MarkdownTextSelection.Surface("text", original));
        Assert.Equal(original.Margin, surface.Margin);
        Assert.Equal(250, surface.Modifiers!.Width);
        Assert.Equal(HorizontalAlignment.Right, surface.Modifiers.HorizontalAlignment);
        Assert.Equal(new GridAttached(2, 3, 4, 5), surface.GetAttached<GridAttached>());
        Assert.Equal("stable", surface.Key);
        Assert.Null(surface.Content.Key);
        Assert.Null(surface.Content.Margin);
        Assert.Null(surface.Content.Modifiers!.Width);
        Assert.Null(surface.Content.Modifiers.HorizontalAlignment);
        Assert.Null(surface.Content.GetAttached<GridAttached>());
        Assert.Equal(new Thickness(9), surface.Content.Padding);
        Assert.Equal(new Thickness(2, 3, 4, 5), original.Margin);
        var normalized = surface with { Margin = new Thickness(0) };
        Assert.Equal(new Thickness(0), normalized.Margin);
        Assert.Null(normalized.Content.Margin);
    }

    [Fact]
    public void ParserPresentationMargins_AreLiftedAfterCallbacks()
    {
        var tree = Md("# title\n\nbody", new MarkdownOptions
        {
            TextSelection = new(),
            Heading = (_, element) => element.Margin(10).Grid(row: 2),
            Paragraph = element => element.Margin(7).Grid(row: 3),
        });
        var surfaces = Surfaces(tree).ToArray();
        Assert.Equal(new Thickness(10), surfaces[0].Margin);
        Assert.Equal(new Thickness(7), surfaces[1].Margin);
        Assert.Equal(2, surfaces[0].GetAttached<GridAttached>()!.Row);
        Assert.Equal(3, surfaces[1].GetAttached<GridAttached>()!.Row);
        Assert.All(surfaces, surface =>
        {
            Assert.Null(surface.Content.Margin);
            Assert.Null(surface.Content.GetAttached<GridAttached>());
        });
    }

    [Fact]
    public void Visibility_ComposesWithoutChangingVisualContent()
    {
        var child = TextBlock("inner");
        var inner = Assert.IsType<MarkdownSelectionElement>(MarkdownTextSelection.Visibility(true, child));
        var outer = Assert.IsType<MarkdownSelectionElement>(MarkdownTextSelection.Visibility(false, inner));
        Assert.False(outer.IsVisible);
        Assert.True(inner.IsVisible);
        Assert.Equal(child, inner.Content);
        Assert.Same(inner, outer.Content);
    }

    [Fact]
    public void Document_ReportsTextAndSelectionChanges_WithoutDuplicateNotifications()
    {
        var document = new MarkdownSelectionDocument();
        var changes = new List<(bool Text, bool Selection)>();
        document.Changed = (text, selection) => changes.Add((text, selection));
        var identity = new object();
        document.Rebuild([new(identity, "abc", "")]);
        document.Select(0, 2);
        document.Select(0, 2);
        document.Rebuild([new(identity, "abc", "")]);
        Assert.Equal(new[] { (true, false), (false, true) }, changes);
        document.Rebuild([]);
        Assert.Equal((true, true), changes[^1]);
        Assert.Equal(0, document.Caret);
    }

    [Theory]
    [InlineData(0, 0, 1, 2, 2, 1)]
    [InlineData(1, 1, 1, 2, 2, 1)]
    [InlineData(1, 1, -1, 0, 0, -1)]
    [InlineData(6, 6, 1, 6, 6, 0)]
    [InlineData(0, 0, -1, 0, 0, 0)]
    [InlineData(0, 3, 1, 2, 4, 1)]
    [InlineData(0, 3, -1, 0, 3, 0)]
    [InlineData(4, 5, 1, 4, 5, 0)]
    [InlineData(0, 3, 9, 4, 6, 2)]
    [InlineData(0, 3, 0, 0, 3, 0)]
    public void UiaMove_PreservesInsertionPointsAndFailedNonEmptyRanges(
        int start, int end, int count, int expectedStart, int expectedEnd, int expectedMoved)
    {
        Assert.Equal((expectedStart, expectedEnd, expectedMoved),
            MarkdownSelectionRangeMovement.Move(start, end, [0, 2, 4, 6], count));
    }

    [Fact]
    public void UiaMove_EmptyDocumentDoesNotExpand()
    {
        Assert.Equal((0, 0, 0), MarkdownSelectionRangeMovement.Move(0, 0, [0], 1));
        Assert.Equal((0, 0, 0), MarkdownSelectionRangeMovement.Move(0, 0, [0, 0], 1));
    }

    [Theory]
    [InlineData(1, 1, 2, 1)]
    [InlineData(1, -1, 0, -1)]
    [InlineData(6, 1, 6, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(1, int.MaxValue, 6, 3)]
    [InlineData(5, int.MinValue, 0, -3)]
    public void UiaEndpointMove_ClampsLargeCountsAndInteriorOffsets(
        int offset, int count, int expectedOffset, int expectedMoved)
    {
        Assert.Equal((expectedOffset, expectedMoved),
            MarkdownSelectionRangeMovement.MoveEndpoint(offset, [0, 2, 4, 6], count));
    }

    [Fact]
    public void VisibleRanges_QueriesOnlyIntersectingSurfacesAndKeepsDisjointRanges()
    {
        var queried = new List<int>();
        var viewport = new global::Windows.Foundation.Rect(0, 0, 100, 100);
        var outside = new global::Windows.Foundation.Rect(0, 200, 100, 20);
        var spans = new[] { (0, 10000, outside), (10000, 10004, viewport), (10004, 20000, outside) };
        var ranges = MarkdownSelectionHost.VisibleRanges(spans, viewport, offset =>
        {
            queried.Add(offset);
            return offset == 10002 ? outside : viewport;
        });
        Assert.Equal(new[] { 10000, 10001, 10002, 10003 }, queried);
        Assert.Equal(new[] { (10000, 10002), (10003, 10004) }, ranges);
    }

    [Fact]
    public void Document_EntryLookup_UsesPrecedingSurfaceForSeparatorsAndLastEndpoint()
    {
        var document = new MarkdownSelectionDocument();
        var a = new object();
        var b = new object();
        document.Rebuild([new(a, "ab", "\t"), new(b, "cd", "")]);
        Assert.Null(document.EntryAt(-1));
        Assert.Same(a, document.EntryAt(2)!.Identity);
        Assert.Same(b, document.EntryAt(3)!.Identity);
        Assert.Same(b, document.EntryAt(5)!.Identity);
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(10, 20)]
    public void GlyphRectangle_NormalizesBothTextDirections(double start, double end)
    {
        var rect = MarkdownSelectionHost.GlyphRectangle(
            new global::Windows.Foundation.Rect(start, 5, 1, 12), new global::Windows.Foundation.Rect(end, 5, 0, 12));
        Assert.Equal(10, rect.X);
        Assert.Equal(10, rect.Width);
        Assert.Equal(12, rect.Height);
    }

    [Theory]
    [InlineData(unchecked((int)0x800401D0))]
    [InlineData(unchecked((int)0x800401D1))]
    [InlineData(unchecked((int)0x800401D2))]
    [InlineData(unchecked((int)0x800401D3))]
    [InlineData(unchecked((int)0x800401D4))]
    public void Copy_ExpectedClipboardComFailures_ReportFailure(int hresult)
    {
        bool? success = null;
        MarkdownSelectionCopy.Write("copy", _ => throw new global::System.Runtime.InteropServices.COMException("clipboard", hresult),
            (text, result) => { Assert.Equal("copy", text); success = result; });
        Assert.False(success);
    }

    internal static IEnumerable<MarkdownSelectionElement> Surfaces(Element element)
    {
        if (element is MarkdownSelectionElement wrapper)
        {
            if (wrapper.Role == SelectionRole.Surface) { yield return wrapper; yield break; }
            if (wrapper.Role == SelectionRole.Visibility && !wrapper.IsVisible) yield break;
            foreach (var surface in Surfaces(wrapper.Content)) yield return surface;
        }
        else
        {
            IEnumerable<Element> children = element switch
            {
                StackElement stack => stack.Children,
                GridElement grid => grid.Children,
                BorderElement { Child: { } child } => [child],
                _ => []
            };
            foreach (var child in children)
                foreach (var surface in Surfaces(child)) yield return surface;
        }
    }

    private static string Join(IEnumerable<MarkdownSelectionElement> surfaces)
    {
        var document = new MarkdownSelectionDocument();
        document.Rebuild(surfaces.Select(s => new SelectionPart(s, s.Text, s.SeparatorAfter)));
        return document.Text;
    }
}
