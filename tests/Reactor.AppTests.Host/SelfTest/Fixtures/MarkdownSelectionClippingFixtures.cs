using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal enum MarkdownSelectionClippingCase { Layout, Spacing, TransformsAndScroll }

internal sealed class MarkdownSelectionClippingFixtures(
    Harness harness, MarkdownSelectionClippingCase scenario = MarkdownSelectionClippingCase.Layout)
    : SelfTestFixtureBase(harness)
{
    public override async Task RunAsync()
    {
        var options = new MarkdownTextSelectionOptions();
        string paragraph = string.Join(" ", Enumerable.Repeat(
            "Wrapped text with office affinity ffi, 123-45, and native selection geometry.", 6));
        string text = $"## Heading\n\n{paragraph}\n\n**bold** with _emphasis_ and `code` " +
            "[link](https://example.com)\n\nunicode e\u0301, \U0001F469\u200D\U0001F4BB, \u05d0\u05d1\u05d2\n\n{paragraph}";
        MarkdownSelectionHost? scope = null;
        ScrollViewer? scroll = null;
        var host = H.CreateHost();
        host.Mount(_ => ScrollViewer(MarkdownTextSelection.Scope(options,
            Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, new MarkdownOptions { TextSelection = options }))
            .OnMountAdd(fe => scope = (MarkdownSelectionHost)fe)).Width(640).Height(480)
            .OnMountAdd(fe => scroll = (ScrollViewer)fe));
        await Harness.Render();
        if (!await Harness.WaitFor(() => scope is { IsLoaded: true, Document.Entries.Count: > 0 }))
            throw new InvalidOperationException("Selection scope not loaded");
        var selection = scope ?? throw new InvalidOperationException("Selection scope not mounted");
        var overlay = ((Grid)selection.Content).Children.OfType<Canvas>().Single();
        switch (scenario)
        {
            case MarkdownSelectionClippingCase.Layout:
                await CheckLayouts(selection, overlay, scroll!);
                break;
            case MarkdownSelectionClippingCase.Spacing:
                await CheckSpacing(selection, overlay, scroll!);
                break;
            case MarkdownSelectionClippingCase.TransformsAndScroll:
                await CheckTransformsAndScroll(selection, overlay, scroll!, paragraph);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        host.Dispose();
        await Harness.Render();
    }

    private async Task CheckLayouts(MarkdownSelectionHost selection, Canvas overlay, ScrollViewer scroll)
    {
        bool skipped = false;
        foreach (var (font, rtl, width, scrollY, anchor, caret) in new[]
        {
            ("Segoe UI", false, 320, 0, 0, 350),
            ("Segoe UI", false, 640, 80, 0, 650),
            ("Times New Roman", false, 640, 0, 200, 650),
            ("Times New Roman", false, 320, 80, 650, 200),
            ("Consolas", false, 640, 80, 0, 650),
            ("Consolas", false, 320, 0, 200, 650),
            ("Segoe UI", true, 320, 0, 0, 650),
            ("Consolas", true, 640, 0, 0, 650)
        })
        {
            foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
            {
                rich.FontFamily = new FontFamily(font);
                rich.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }
            scroll.Width = width;
            await Harness.Render();
            scroll.ChangeView(null, scrollY, null, true);
            await Harness.Render();
            var (parity, usedShortcut, visible, _) = Compare(selection, overlay, anchor, caret);
            H.Check($"MdSelection_ClippingParity_{font}_{rtl}_{width}_{scrollY}_{anchor}_{caret}",
                parity && visible > 0);
            if (rtl) H.Check($"MdSelection_ClippingRtlFallback_{font}_{width}_{scrollY}_{anchor}_{caret}",
                !usedShortcut);
            else skipped |= usedShortcut;
        }
        H.Check("MdSelection_ClippingSkippedOffscreenGlyphs", skipped);
    }

    private async Task CheckSpacing(MarkdownSelectionHost selection, Canvas overlay, ScrollViewer scroll)
    {
        foreach (int spacing in new[] { -100, -500, -1000, -2000 })
        {
            foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
            {
                rich.FlowDirection = FlowDirection.LeftToRight;
                rich.FontFamily = new FontFamily("Consolas");
                rich.CharacterSpacing = spacing;
                foreach (var run in rich.Blocks.OfType<Paragraph>().SelectMany(block => block.Inlines).OfType<Run>())
                    run.CharacterSpacing = spacing;
            }
            scroll.Width = 320;
            scroll.ChangeView(null, 0, null, true);
            await Harness.Render();
            var (parity, usedShortcut, visible, queried) = Compare(selection, overlay, 0, 650);
            Console.WriteLine($"# Clipping spacing={spacing}; parity={parity}; shortcut={usedShortcut}; visible={visible}; queried={queried}");
            H.Check($"MdSelection_ClippingNegativeSpacingParity_{spacing}", parity && visible > 0 && queried > 0);
            if (spacing == -100)
                H.Check("MdSelection_ClippingNegativeSpacingExercised", usedShortcut);
            CheckBoundaryGlyphs(selection, overlay, spacing);
        }
    }

    private async Task CheckTransformsAndScroll(
        MarkdownSelectionHost selection, Canvas overlay, ScrollViewer scroll, string paragraph)
    {
        scroll.Width = 320;
        foreach (string transform in new[] { "rotate", "mirror", "perspective", "scale" })
        {
            foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
            {
                rich.FlowDirection = FlowDirection.LeftToRight;
                rich.CharacterSpacing = 0;
                foreach (var run in rich.Blocks.OfType<Paragraph>().SelectMany(block => block.Inlines).OfType<Run>())
                    run.CharacterSpacing = 0;
                rich.RenderTransform = transform switch
                {
                    "mirror" => new ScaleTransform { ScaleX = -1, CenterX = 160 },
                    "rotate" => new RotateTransform { Angle = 5 },
                    "scale" => new ScaleTransform { ScaleX = 1.25, ScaleY = 1.25 },
                    _ => null
                };
                rich.Projection = transform == "perspective"
                    ? new PlaneProjection { RotationY = 15, CenterOfRotationX = 0, CenterOfRotationY = 0 }
                    : null;
            }
            await Harness.Render();
            var (parity, usedShortcut, visible, queried) = Compare(selection, overlay, 0, 650);
            H.Check($"MdSelection_ClippingTransformParity_{transform}", parity);
            H.Check($"MdSelection_ClippingTransformShortcut_{transform}", usedShortcut == (transform == "scale"));
            H.Check($"MdSelection_ClippingTransformExercised_{transform}", visible > 0 && queried > 0);
        }
        foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
        {
            rich.RenderTransform = null;
            rich.FontFamily = new FontFamily("Segoe UI");
        }
        selection.Width = 960;
        scroll.HorizontalScrollMode = ScrollMode.Enabled;
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        scroll.ChangeView(0, 0, null, true);
        await Harness.Render();
        selection.Refresh();
        var plain = selection.Document.Entries.First(entry => entry.Text == paragraph);
        var viewport = selection.Viewport();
        int offscreen = Enumerable.Range(plain.Start, plain.Text.Length - 1).First(offset =>
        {
            var rect = selection.CharacterBounds(offset);
            return rect.Left > viewport.Right + 20 && rect.Left < selection.ActualWidth - 40 &&
                rect.Top >= viewport.Top && rect.Bottom <= viewport.Bottom;
        });
        var (offscreenParity, offscreenSkipped, _, _) = Compare(selection, overlay, plain.Start, offscreen + 1);
        string expectedText = selection.Document.Text[plain.Start..(offscreen + 1)];
        string copied = "";
        selection.CopySelection(value => copied = value);
        var provider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(selection)
            .GetPattern(PatternInterface.Text);
        H.Check("MdSelection_ClippingPreservesOffscreenText",
            offscreenParity && offscreenSkipped && selection.Document.SelectedText == expectedText &&
            copied == expectedText && provider.GetSelection().Single().GetText(-1) == expectedText);
        var hiddenGlyph = selection.GlyphBounds(offscreen, offscreen + 1);
        H.Check("MdSelection_ClippingRetainedGlyphStartsOffscreen",
            MarkdownSelectionHost.Intersect(hiddenGlyph, selection.Viewport()).Width == 0);
        scroll.ChangeView(hiddenGlyph.Left - 160, null, null, true);
        if (!await Harness.WaitFor(() => scroll.HorizontalOffset > 0))
            throw new InvalidOperationException("Selection viewport never scrolled horizontally.");
        selection.Refresh();
        var revealed = MarkdownSelectionHost.Intersect(
            selection.GlyphBounds(offscreen, offscreen + 1), selection.Viewport());
        H.Check("MdSelection_ClippingRevealsSelectedGlyphAfterScroll",
            revealed.Width > 0 && revealed.Height > 0 &&
            selection.Document.SelectedText == expectedText &&
            OverlayRects(overlay).SequenceEqual(ReferenceRects(selection)) &&
            OverlayRects(overlay).Any(rect => rect.Left <= revealed.Left && rect.Right >= revealed.Right &&
                Math.Abs(rect.Y - revealed.Y) < .5));
    }

    private void CheckBoundaryGlyphs(MarkdownSelectionHost selection, Canvas overlay, int spacing)
    {
        var entry = selection.Document.Entries.First(entry => entry.Text.Length > 100);
        var viewport = selection.Viewport();
        var offsets = Enumerable.Range(entry.Start, entry.Text.Length - 1)
            .Where(offset => selection.CharacterBounds(offset).Top >= viewport.Top &&
                selection.CharacterBounds(offset).Bottom <= viewport.Bottom)
            .OrderBy(offset => Math.Abs(selection.CharacterBounds(offset).X - viewport.Right))
            .Take(4).ToArray();
        H.Check($"MdSelection_ClippingBoundaryGlyphsExist_{spacing}", offsets.Length == 4);
        foreach (int offset in offsets)
        {
            // Single-character ranges prevent another selected glyph from masking an omitted cell.
            selection.SelectRefreshed(offset, offset + 1);
            H.Check($"MdSelection_ClippingBoundaryGlyphParity_{spacing}_{offset}",
                OverlayRects(overlay).SequenceEqual(ReferenceRects(selection)));
        }
    }

    private static Rect[] OverlayRects(Canvas overlay) => overlay.Children.Cast<Rectangle>().Select(shape => new Rect(
        Microsoft.UI.Xaml.Controls.Canvas.GetLeft(shape),
        Microsoft.UI.Xaml.Controls.Canvas.GetTop(shape), shape.Width, shape.Height)).ToArray();

    private static (bool Parity, bool UsedShortcut, int Visible, int Queried) Compare(
        MarkdownSelectionHost selection, Canvas overlay, int anchor, int caret)
    {
        selection.SelectRefreshed(0, 0);
        selection.InvalidateDocument();
        selection.Refresh();
        foreach (var entry in selection.Document.Entries)
            for (int offset = entry.Start; offset < entry.End;)
            {
                selection.CharacterBounds(offset);
                offset = Math.Max(offset + 1, selection.Document.MoveGrapheme(offset, 1));
            }
        selection.SelectRefreshed(anchor, Math.Min(caret, selection.Document.Text.Length));
        var actual = OverlayRects(overlay);
        int fastCount = selection.CachedGlyphCount;
        var expected = ReferenceRects(selection);
        return (actual.SequenceEqual(expected), fastCount < selection.CachedGlyphCount,
            expected.Count, selection.CachedGlyphCount);
    }

    private static List<Rect> ReferenceRects(MarkdownSelectionHost selection)
    {
        var result = new List<Rect>();
        var viewport = selection.Viewport();
        foreach (var entry in selection.Document.Entries)
        {
            if (entry.End <= selection.Document.Start || entry.Start >= selection.Document.End) continue;
            var surface = MarkdownSelectionHost.Intersect(
                selection.Bounds((MarkdownSelectionMarker)entry.Identity), viewport);
            if (surface.Width <= 0 || surface.Height <= 0) continue;
            for (int offset = Math.Max(selection.Document.Start, entry.Start);
                offset < Math.Min(selection.Document.End, entry.End);)
            {
                int next = selection.Document.MoveGrapheme(offset, 1);
                var rect = MarkdownSelectionHost.Intersect(selection.GlyphBounds(offset, next), viewport);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    if (result.Count > 0 && Math.Abs(result[^1].Y - rect.Y) < .5 &&
                        rect.Left <= result[^1].Right + 2 && rect.Right >= result[^1].Left - 2)
                    {
                        var previous = result[^1];
                        result[^1] = new Rect(Math.Min(previous.Left, rect.Left), previous.Y,
                            Math.Max(previous.Right, rect.Right) - Math.Min(previous.Left, rect.Left), previous.Height);
                    }
                    else result.Add(rect);
                }
                offset = Math.Max(offset + 1, next);
            }
        }
        return result;
    }
}
