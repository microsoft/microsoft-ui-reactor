using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class MarkdownListLayoutFixtures
{
    private const string LongText =
        "This list item must remain completely readable when the available width changes. " +
        "Long plain and formatted text should wrap onto additional lines instead of disappearing " +
        "beyond the right edge of the containing document.";
    private const string Following = "The paragraph after the list must remain visible.";

    internal class Plain(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- {LongText}", [LongText], ["\u2022 "]);
    }

    internal class Bold(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- **{LongText}**", [LongText], ["\u2022 "], bold: true);
    }

    internal class Ordered(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"9. {LongText}\n10. Next item", [LongText, "Next item"], ["9. ", "10. "]);
    }

    internal class Loose(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- {LongText}\n\n  Second paragraph in the same item.\n\n- Last item",
                [LongText, "Second paragraph in the same item.", "Last item"], ["\u2022 ", "\u2022 "]);
    }

    internal class Nested(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- Parent item\n\n  - {LongText}\n\n- Last item",
                ["Parent item", LongText, "Last item"], ["\u2022 ", "\u2022 ", "\u2022 "]);
    }

    internal class Code(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- {LongText}\n\n  ```text\n  {LongText}\n  ```",
                [LongText, LongText], ["\u2022 "]);
    }

    internal class Tasks(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- [x] {LongText}\n- [ ] Pending\n- [X] Done",
                [LongText, "Pending", "Done"], ["\u2611 ", "\u2610 ", "\u2611 "]);
    }

    internal class Unified(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() =>
            CheckWrapping(H, $"- {LongText}", [LongText], ["\u2022 "], unified: true);
    }

    internal class Short(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(_ => Border(
                Microsoft.UI.Reactor.Advanced.Factories.Markdown("- Yes\n- No")
                    .HAlign(HorizontalAlignment.Left))
                .Width(1000).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top));
            await Harness.Render();

            var viewport = H.FindControl<Border>(b => b.Width == 1000)
                ?? throw new InvalidOperationException("Missing Markdown viewport.");
            var document = (FrameworkElement)viewport.Child;
            var text = H.FindAllControls<RichTextBlock>(_ => true);
            H.Check("Short_FullText", text.Select(Text).SequenceEqual(["Yes", "No"]));
            H.Check("Short_IntrinsicWidth", document.ActualWidth > 0 && document.ActualWidth < 100);
            Console.WriteLine($"# Short list: viewport={viewport.ActualWidth:F2}, document={document.ActualWidth:F2}");
        }
    }

    internal class RowMeasureEquivalence(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string suffix = "\n\n  Second paragraph.\n\n  - Nested item\n\n  ```text\n  code\n  ```";
            foreach (bool unified in new[] { false, true })
            {
                var baseline = new List<(Size Desired, Rect Bounds, Rect End)[]>();
                foreach (bool explicitRow in new[] { true, false })
                {
                    using var host = H.CreateHost();
                    host.Mount(_ => Border(
                        Microsoft.UI.Reactor.Advanced.Factories.Markdown(
                            $"- {LongText}{suffix}\n\n{Following}",
                            new MarkdownOptions
                            {
                                UnifiedRichText = unified,
                                ListItem = item =>
                                {
                                    var grid = (GridElement)item;
                                    return grid with
                                    {
                                        Definition = grid.Definition with
                                        {
                                            Rows = explicitRow ? [GridSize.Auto] : [],
                                        },
                                    };
                                },
                            }))
                        .Width(686).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top));
                    await Harness.Render();

                    var viewport = H.FindControl<Border>(b => b.Width == 686)
                        ?? throw new InvalidOperationException("Missing Markdown viewport.");
                    var rows = H.FindAllControls<Microsoft.UI.Xaml.Controls.Grid>(
                        g => g.ColumnDefinitions.Count == 2 && g.ColumnSpacing == 4);
                    H.Check($"RowMode_{unified}_{explicitRow}", rows.Count == 2
                        && rows.All(g => g.RowDefinitions.Count == (explicitRow ? 1 : 0)));
                    var blocks = H.FindAllControls<RichTextBlock>(b => Runs(b).Any());
                    H.Check($"RowModeText_{unified}_{explicitRow}",
                        blocks.SelectMany(Runs).Select(r => r.Text).OrderBy(t => t)
                            .SequenceEqual(new[] { LongText, "Second paragraph.", "Nested item", "code", Following }.OrderBy(t => t)));

                    int step = 0;
                    foreach (double width in new[] { 686.0, 320.0, 686.0 })
                    {
                        viewport.Width = width;
                        viewport.UpdateLayout();
                        await Harness.Render();
                        var elements = new FrameworkElement[] { viewport }
                            .Concat(rows).Concat(blocks).ToArray();
                        var snapshot = elements.Select(element =>
                        {
                            var bounds = element.TransformToVisual(viewport).TransformBounds(
                                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                            var end = element is RichTextBlock block
                                ? block.TransformToVisual(viewport).TransformBounds(
                                    Runs(block).Last().ContentEnd.GetCharacterRect(LogicalDirection.Backward))
                                : new Rect();
                            return (element.DesiredSize, bounds, end);
                        }).ToArray();
                        Console.WriteLine($"# row mode: unified={unified}, explicit={explicitRow}, " +
                            $"width={width}, desired={viewport.DesiredSize}, height={viewport.ActualHeight}");
                        if (explicitRow)
                            baseline.Add(snapshot);
                        else
                            H.Check($"ImplicitRowEquivalent_{unified}_{step}", snapshot.SequenceEqual(baseline[step]));
                        step++;
                    }
                }
            }
        }
    }

    private static async Task CheckWrapping(
        Harness h, string markdown, string[] expectedText, string[] expectedMarkers,
        bool bold = false, bool unified = false)
    {
        using var host = h.CreateHost();
        host.Mount(_ => Border(
            Microsoft.UI.Reactor.Advanced.Factories.Markdown(
                markdown + "\n\n" + Following, new MarkdownOptions { UnifiedRichText = unified }))
            .Width(686).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top));
        await Harness.Render();

        var viewport = h.FindControl<Border>(b => b.Width == 686)
            ?? throw new InvalidOperationException("Missing Markdown viewport.");
        var blocks = h.FindAllControls<RichTextBlock>(b => Runs(b).Any());
        var listBlocks = blocks.Where(b => Text(b) != Following).ToArray();
        h.Check("FullTextAndBlockOrder", listBlocks.Select(Text).SequenceEqual(expectedText));
        var following = blocks.Single(b => Text(b) == Following);
        var markers = h.FindAllControls<TextBlock>(b => expectedMarkers.Contains(b.Text));
        h.Check("Markers", markers.Select(b => b.Text).SequenceEqual(expectedMarkers));
        h.Check("SelectionPreserved", listBlocks.All(b => b.IsTextSelectionEnabled));
        if (bold)
            h.Check("BoldPreserved", Runs(listBlocks.Single()).All(r => r.FontWeight.Weight >= 700));

        double wideHeight = 0;
        foreach (var width in new[] { 686.0, 320.0, 686.0 })
        {
            viewport.Width = width;
            viewport.UpdateLayout();
            await Harness.Render();
            h.Check($"Viewport_{width}", Math.Abs(viewport.ActualWidth - width) < 1);

            foreach (var marker in markers)
            {
                var row = (Panel)VisualTreeHelper.GetParent(marker);
                var content = (FrameworkElement)row.Children[1];
                var contentOrigin = content.TransformToVisual(row).TransformPoint(new Point());
                h.Check($"Marker_{marker.Text}_{width}_TopAligned", marker.VerticalAlignment == VerticalAlignment.Top);
                h.Check($"Marker_{marker.Text}_{width}_Spacing",
                    Math.Abs(contentOrigin.X - marker.ActualWidth - 4) < 1);
            }

            for (int i = 0; i < listBlocks.Length; i++)
            {
                var block = listBlocks[i];
                var runs = Runs(block).ToArray();
                var first = runs[0].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var last = runs[^1].ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                var bounds = block.TransformToVisual(viewport).TransformBounds(
                    new Rect(0, 0, block.ActualWidth, block.ActualHeight));
                var lastInViewport = block.TransformToVisual(viewport).TransformBounds(last);
                Console.WriteLine($"# width={width}, block={i}, textLength={Text(block).Length}, " +
                    $"bounds={bounds}, first={first}, last={last}");
                h.Check($"Block{i}_{width}_FiniteWidth",
                    block.ActualWidth > 0 && double.IsFinite(block.ActualWidth)
                    && bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1);
                h.Check($"Block{i}_{width}_LastCharacterVisible",
                    last.Height > 0 && lastInViewport.Left >= -1
                    && lastInViewport.Right <= viewport.ActualWidth + 1
                    && last.Top >= 0 && last.Bottom <= block.ActualHeight + 1);
                if (Text(block) == LongText)
                    h.Check($"Block{i}_{width}_Wraps", last.Top > first.Top + 1);
            }

            // In unified mode this paragraph shares its RichTextBlock with the list.
            // Measure its text positions, not the bounds of the whole document.
            var followingRuns = Runs(following).ToArray();
            var followingFirst = following.TransformToVisual(viewport).TransformBounds(
                followingRuns[0].ContentStart.GetCharacterRect(LogicalDirection.Forward));
            var followingLast = following.TransformToVisual(viewport).TransformBounds(
                followingRuns[^1].ContentEnd.GetCharacterRect(LogicalDirection.Backward));
            double lastBottom = listBlocks.Max(b => b.TransformToVisual(viewport).TransformBounds(
                new Rect(0, 0, b.ActualWidth, b.ActualHeight)).Bottom);
            h.Check($"FollowingParagraph_{width}",
                followingFirst.Top >= lastBottom - 1 && followingLast.Height > 0
                && followingLast.Right <= viewport.ActualWidth + 1
                && followingLast.Bottom <= viewport.ActualHeight + 1);
            if (wideHeight == 0)
                wideHeight = viewport.ActualHeight;
            else if (width == 320)
                h.Check("Narrow_GrowsHeight", viewport.ActualHeight > wideHeight + 1);
            else
                h.Check("Wide_RestoresHeight", Math.Abs(viewport.ActualHeight - wideHeight) < 1);
        }
    }

    private static IEnumerable<Run> Runs(RichTextBlock block) =>
        block.Blocks.OfType<Paragraph>().SelectMany(p => Runs(p.Inlines));

    private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines) =>
        inlines.SelectMany(inline => inline switch
        {
            Run run => new[] { run },
            Span span => Runs(span.Inlines),
            _ => Enumerable.Empty<Run>(),
        });

    private static string Text(RichTextBlock block) => string.Concat(Runs(block).Select(r => r.Text));
}
