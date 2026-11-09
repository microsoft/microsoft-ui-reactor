using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static partial class MarkdownSelectionFixtures
{
    private sealed partial class ProviderProbe(Microsoft.UI.Xaml.FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        internal Microsoft.UI.Xaml.UIElement? OwnerOf(IRawElementProviderSimple provider) =>
            (PeerFromProvider(provider) as FrameworkElementAutomationPeer)?.Owner;
    }
    private static Element Md(string text, MarkdownOptions options) =>
        Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, options);
    internal class SemanticMessage(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var markdownOptions = new MarkdownOptions { TextSelection = options };
            var host = H.CreateHost();
            Element Body(bool expanded, bool inner) => VStack(
                TextBlock("outside before"),
                MarkdownTextSelection.Scope(options, VStack(
                    Md("# Title\n\nfirst\n\n- one\n- two\n\n```cs\nvar x = 1;\n```", markdownOptions),
                    MarkdownTextSelection.Surface("Summary", TextBlock("Summary")),
                    Expander("Disclosure", MarkdownTextSelection.Visibility(expanded, VStack(
                        Md("body", markdownOptions),
                        MarkdownTextSelection.Visibility(inner, Md("nested", markdownOptions))))
                    ) with { IsExpanded = expanded },
                    Md("> quote\n\n|a|b|\n|-|-|\n|c|d|", markdownOptions),
                    Button("toolbar outside text"))),
                TextBlock("outside after"));
            host.Mount(_ => Body(false, true));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_SingleMessageHost", H.FindAllControls<MarkdownSelectionHost>(_ => true).Count == 1);
            H.Check("MdSelection_ExactVisibleSemanticText",
                selection.Document.Text == "Title\nfirst\n• one\n• two\nvar x = 1;\nSummary\nquote\na\tb\nc\td");
            host.Mount(_ => Body(true, false));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_ExpandedBody_NotNested",
                selection.Document.Text.Contains("Summary\nbody\nquote", StringComparison.Ordinal)
                && !selection.Document.Text.Contains("nested", StringComparison.Ordinal));
            host.Mount(_ => Body(true, true));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_NestedGatesCompose",
                selection.Document.Text.Contains("Summary\nbody\nnested\nquote", StringComparison.Ordinal));
            host.Mount(_ => Body(false, true));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_CollapseRemovesDescendants",
                !selection.Document.Text.Contains("body", StringComparison.Ordinal)
                && !selection.Document.Text.Contains("nested", StringComparison.Ordinal));
        }
    }

    internal class LifecycleAndStreaming(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var host = H.CreateHost();
            Element Message(string text) => MarkdownTextSelection.Scope(options,
                Md(text, new MarkdownOptions { TextSelection = options }));
            host.Mount(_ => Message("partial"));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            selection.Select(1, 7);
            host.Mount(_ => Message("partial streamed"));
            await Harness.Render();
            var updated = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            updated.Refresh();
            H.Check("MdSelection_ReconciliationKeepsHost", ReferenceEquals(selection, updated));
            H.Check("MdSelection_StreamKeepsSelection", updated.Document.SelectedText == "artial");
            H.Check("MdSelection_NoDuplicateRegistration", updated.Document.Entries.Count == 1);
            var provider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(updated)
                .GetPattern(PatternInterface.Text);
            var oldRange = provider.DocumentRange;
            host.Mount(_ => TextBlock("replacement"));
            await Harness.Render();
            H.Check("MdSelection_UnmountInvalidatesRegistration", updated.Document.Text == "");
            H.Check("MdSelection_OldUiaRangeIsEmpty", oldRange.GetText(-1) == "");
            host.Mount(_ => Message("fresh message"));
            await Harness.Render();
            var fresh = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            fresh.Refresh();
            H.Check("MdSelection_RemountHasNoOldSelection",
                !ReferenceEquals(updated, fresh) && fresh.Document.SelectedText == ""
                && fresh.Document.Text == "fresh message");
        }
    }

    internal class RangeChildren(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var host = H.CreateHost();
            host.Mount(_ => MarkdownTextSelection.Scope(options, VStack(
                Md("first [alpha](https://example.com/a) tail\n\nsecond [beta](https://example.com/b) end",
                    new MarkdownOptions { TextSelection = options, LinkBuilder = link => Button(link.Text) }),
                Button("toolbar"))));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            var peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            var document = peer.DocumentRange;
            H.Check("MdSelection_UiaDocumentText",
                document.GetText(-1) == "first alpha tail\nsecond beta end");
            var alpha = document.FindText("alpha", false, false);
            var beta = document.FindText("beta", false, false);
            H.Check("MdSelection_AlphaHasOneChild", alpha.GetChildren().Length == 1);
            H.Check("MdSelection_BetaHasOneDifferentChild",
                beta.GetChildren().Length == 1 &&
                !Equals(alpha.GetChildren()[0], beta.GetChildren()[0]));
            H.Check("MdSelection_ProseHasNoEmbeddedChild",
                document.FindText("tail", false, false).GetChildren().Length == 0);
            H.Check("MdSelection_ToolbarExcluded", document.GetChildren().Length == 2);
            var fromAlpha = peer.RangeFromChild(alpha.GetChildren()[0]);
            H.Check("MdSelection_RangeFromChildMapsExactLabel", fromAlpha.GetText(-1) == "alpha");
            H.Check("MdSelection_RangeFromChildMatchesPosition", fromAlpha.Compare(alpha));
            H.Check("MdSelection_BetaMapsExactLabel", peer.RangeFromChild(beta.GetChildren()[0]).Compare(beta));
            Element Disclosure(bool expanded) => MarkdownTextSelection.Scope(options, VStack(
                Expander("", MarkdownTextSelection.Visibility(expanded,
                    Md("disclosure body", new MarkdownOptions { TextSelection = options })),
                    isExpanded: expanded).HeaderTemplate(
                        VStack(MarkdownTextSelection.Surface("Disclosure", TextBlock("Disclosure"), ""),
                            MarkdownTextSelection.Surface(" summary",
                                TextBlock(" summary").TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)))),
                Md("neighbor prose", new MarkdownOptions { TextSelection = options })));
            host.Mount(_ => VStack(Disclosure(false), Expander("outside scope", TextBlock("outside"))));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            document = peer.DocumentRange;
            var summary = document.FindText("Disclosure summary", false, false);
            var expander = MarkdownSelectionHost.Descendants(selection).OfType<Expander>().Single();
            var children = summary.GetChildren();
            H.Check("MdSelection_HeaderSurfaceExposesAncestorExpander",
                children.Length == 1 && new ProviderProbe(selection).OwnerOf(children[0]) == expander);
            H.Check("MdSelection_HeaderRangeMapsExactSummary",
                children.Length == 1 && peer.RangeFromChild(children[0]).Compare(summary));
            H.Check("MdSelection_HeaderCollapsedBodyExcluded",
                document.GetText(-1) == "Disclosure summary\nneighbor prose");
            H.Check("MdSelection_HeaderHostContainmentAndDedup", document.GetChildren().Length == 1);
            host.Mount(_ => VStack(Disclosure(true), Expander("outside scope", TextBlock("outside"))));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            document = peer.DocumentRange;
            summary = document.FindText("Disclosure summary", false, false);
            children = summary.GetChildren();
            H.Check("MdSelection_HeaderExpandedRangeUnchanged",
                children.Length == 1 && peer.RangeFromChild(children[0]).Compare(summary));
            H.Check("MdSelection_HeaderExpandedBodyHasNoAncestorChild",
                document.FindText("disclosure body", false, false).GetChildren().Length == 0);
            H.Check("MdSelection_HeaderExpandedDedup", document.GetChildren().Length == 1);
        }
    }

    internal class ItemsViewClippedNavigation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            string longMessage = string.Join("\n\n", Enumerable.Range(0, 35).Select(i => $"paragraph {i:D2}."));
            var host = H.CreateHost();
            host.Mount(_ => ItemsView(new[] { "message", "sibling" }, row => row, (row, _) =>
                ItemContainer(row == "message"
                    ? MarkdownTextSelection.Scope(options, Md(longMessage,
                        new MarkdownOptions { TextSelection = options }))
                    : TextBlock("timeline sibling excluded"))).Height(180).Width(400));
            await Harness.Render();
            var items = H.FindControl<ItemsView>(_ => true)!;
            var modern = MarkdownSelectionHost.Descendants(items).OfType<ScrollView>().First();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_ItemsViewHasRealScrollView", modern.ActualHeight > 0);
            H.Check("MdSelection_ItemsViewViewportClipsMessage",
                selection.Viewport().Height > 0 && selection.Viewport().Height < selection.ActualHeight);
            H.Check("MdSelection_FullyClippedTailStillRegistered",
                selection.Document.Entries.Count == 35 && selection.Document.Text.EndsWith("paragraph 34.", StringComparison.Ordinal)
                && selection.CharacterBounds(selection.Document.Entries[^1].Start).Top > selection.Viewport().Bottom);
            selection.Select(0, 0);
            for (int i = 0; i < 40; i++) selection.MoveVertical(1, false, true);
            H.Check("MdSelection_DownReachesClippedTail",
                selection.Document.Caret >= selection.Document.Entries[^1].Start);
            await Harness.Render();
            H.Check("MdSelection_BringIntoViewUsesInternalScrollView", modern.VerticalOffset > 0);
            modern.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            await Harness.Render();
            selection.Refresh();
            selection.Select(0, 0);
            for (int i = 0; i < 10; i++) selection.MoveVertical(1, true, true);
            H.Check("MdSelection_PageDownReachesClippedTail",
                selection.Document.Caret >= selection.Document.Entries[^1].Start);
            H.Check("MdSelection_MessageScopeExcludesTimelineSibling",
                !selection.Document.Text.Contains("timeline", StringComparison.Ordinal));
            selection.Select(0, selection.Document.Text.Length);
            selection.BringCaretIntoView();
            await Harness.Render();
            selection.Refresh();
            var tail = (MarkdownSelectionMarker)selection.Document.Entries[^1].Identity;
            var text = MarkdownSelectionHost.Descendants(tail).OfType<RichTextBlock>().Single();
            var run = text.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().Single();
            var glyph = run.ContentStart.GetPositionAtOffset(run.Text.Length - 1, LogicalDirection.Forward)
                .GetCharacterRect(LogicalDirection.Forward);
            var endForward = run.ContentEnd.GetCharacterRect(LogicalDirection.Forward);
            var endBackward = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var mappedEnd = selection.CharacterBounds(selection.Document.Text.Length);
            Console.WriteLine($"# Terminal glyph={glyph}; end forward={endForward}; end backward={endBackward}; mapped end={mappedEnd}");
            var target = text.TransformToVisual(selection).TransformPoint(
                new Point(endForward.X - .25, glyph.Top + glyph.Height / 2));
            int offset = selection.HitTest(target);
            Console.WriteLine($"# Target={target}; hit={offset}; document end={selection.Document.Text.Length}");
            selection.Select(0, offset);
            H.Check("MdSelection_TerminalGlyphRightHalfIncludesLastCharacter",
                offset == selection.Document.Text.Length && selection.Document.SelectedText == longMessage.Replace("\n\n", "\n"));
            host.Mount(_ => MarkdownTextSelection.Scope(options, Md(
                "| Fixture selection table | State |\n| --- | --- |\n| FIXTURE SELECTION table cell. | visible |",
                new MarkdownOptions { TextSelection = options })).Width(400));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            var cell = selection.Document.Entries.Single(e => e.Text == "FIXTURE SELECTION table cell.");
            int beforePeriod = cell.End - 1;
            var periodStart = selection.CharacterBounds(beforePeriod);
            var periodEnd = selection.CharacterBounds(cell.End);
            double scale = selection.XamlRoot.RasterizationScale;
            var screenOrigin = FrameworkElementAutomationPeer.CreatePeerForElement(selection).GetBoundingRectangle();
            double rightScreen = screenOrigin.X + periodEnd.X * scale;
            double insideInteger = Math.Ceiling(rightScreen) - 1;
            var inside = new Point((insideInteger - screenOrigin.X) / scale,
                periodStart.Y + periodStart.Height / 2);
            int cellHit = selection.HitTest(inside);
            Console.WriteLine($"# Table period start={periodStart}; end={periodEnd}; scale={scale}; target={inside}; hit={cellHit}; expected={cell.End}");
            H.Check("MdSelection_TableTerminalIntegerPointIncludesPeriod", cellHit == cell.End);
            var cellMarker = (MarkdownSelectionMarker)cell.Identity;
            var cellNative = MarkdownSelectionHost.Descendants(cellMarker).OfType<RichTextBlock>().Single();
            var nativeProvider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(cellNative)
                .GetPattern(PatternInterface.Text);
            var nativeRange = nativeProvider.DocumentRange.Clone();
            nativeRange.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, cell.Text.Length - 1);
            nativeRange.GetBoundingRectangles(out var nativeRects);
            Console.WriteLine($"# Native period range text={nativeRange.GetText(-1)}; rectangles={string.Join(",", nativeRects)}");
            var nativeInside = new Point(
                (Math.Ceiling(nativeRects[0] + nativeRects[2]) - 1 - screenOrigin.X) / scale,
                (nativeRects[1] + nativeRects[3] / 2 - screenOrigin.Y) / scale);
            int nativeHit = selection.HitTest(nativeInside);
            Console.WriteLine($"# Native range target={nativeInside}; hit={nativeHit}; expected={cell.End}");
            H.Check("MdSelection_NativeTextRangeTerminalPointIncludesPeriod", nativeHit == cell.End);
            cellNative.FontSize = 16;
            cellNative.LineHeight = 24;
            await Harness.Render();
            selection.Refresh();
            periodStart = selection.CharacterBounds(beforePeriod);
            periodEnd = selection.CharacterBounds(cell.End);
            screenOrigin = FrameworkElementAutomationPeer.CreatePeerForElement(selection).GetBoundingRectangle();
            rightScreen = screenOrigin.X + periodEnd.X * scale;
            insideInteger = Math.Ceiling(rightScreen) - 1;
            inside = new Point((insideInteger - screenOrigin.X) / scale, periodStart.Y + periodStart.Height / 2);
            cellHit = selection.HitTest(inside);
            Console.WriteLine($"# Font16 period start={periodStart}; end={periodEnd}; target={inside}; hit={cellHit}; expected={cell.End}");
            H.Check("MdSelection_Font16TerminalIntegerPointIncludesPeriod", cellHit == cell.End);
        }
    }
}
