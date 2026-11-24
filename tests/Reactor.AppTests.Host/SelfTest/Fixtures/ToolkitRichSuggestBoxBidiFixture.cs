using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WctControls;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using Wct = CommunityToolkit.WinUI.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Native bidi geometry of the generated Toolkit RichSuggestBox's inner editor, in LTR and RTL.
/// </summary>
/// <remarks>
/// <para>Source predictions (Toolkit 39c45efe, WinUI release/2.2.0 fb389b88): the Toolkit
/// <c>RichEditBox</c> template part sets no FlowDirection/TextAlignment/TextReadingOrder, so flow is
/// inherited and alignment/reading order stay at WinUI's runtime defaults (recorded, not asserted).
/// Text placement is then decided by content (Latin at the physical left, Hebrew at the physical
/// right) in both flows, and FlowDirection only mirrors chrome (the asymmetric
/// TextControlThemePadding). WinUI's <c>CTextBoxView::IsRightToLeft</c> is hard-coded false, so the
/// text view itself is never mirrored.</para>
/// <para>Instruments. Positions are physical: each box sits alone in a tight LTR <c>Border</c> with an
/// opaque neutral fill, everything is transformed into that frame, and it is also what is rendered to a
/// bitmap, so pixel 0 is the box's left edge. The placeholder is measured by
/// <c>TextPointer.GetCharacterRect</c> and, independently, by rendered ink. The document is measured by
/// rendered ink only: native <c>ITextRange.GetRect</c> returns a zero-width rect at x=0 for an
/// all-Hebrew paragraph and reports a Latin-first mixed paragraph narrower (47) than its Hebrew-first
/// twin (83), all at x=0, which is also its value for a left-aligned paragraph, so its edges cannot
/// tell the cases apart (CI evidence, head 63b9961). GetRect/GetPoint and
/// ParagraphFormat are recorded; GetRect supplies only each paragraph's line band.</para>
/// <para>Deliberately not asserted: empty-document caret side (follows the keyboard input language)
/// and ParagraphFormat.RightToLeft/Alignment (DetectFromContent is applied through bidi options).</para>
/// </remarks>
internal sealed class ToolkitRichSuggestBoxBidiFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "Toolkit_RichSuggestBox_BidiGeometry";
    private const string Latin = "Search";
    private const string Hebrew = "חיפוש";
    private const double BoxWidth = 320;

    // Layout rounding at scale 1 snaps each element edge by at most 0.5 DIP; two snapped
    // quantities are compared, so 1 DIP. A wrong-side placement is off by >200 DIP.
    private const double LayoutTolerance = 1.0;
    // A character's advance edge vs. a padded content edge adds sub-DIP glyph positioning.
    private const double CaretTolerance = 2.0;
    // Ink starts inside the advance box by the glyph's side bearing (about 1 DIP at the 14 DIP
    // theme font) plus one antialiased pixel.
    private const double InkTolerance = 3.0;
    // The same first glyph at the same line edge renders identically: one antialiased pixel.
    private const double SameGlyphTolerance = 1.0;
    // Two side bearings, RichEdit's whole-unit viewport (TxGetViewportRect floors/ceils) and a
    // possible trailing caret reserve. Wrong alignment would make the span ~40 DIP, not ~300.
    private const double SpanTolerance = 6.0;
    // Luminance distance from the region's median (its background) that counts as ink. Over the
    // #808080 frame, primary and placeholder text differ from the text-control fill by >90 in both
    // light and dark themes.
    private const int InkContrast = 64;

    private static readonly string[] Paragraphs =
        [Latin, Hebrew, $"{Hebrew} {Latin}", $"{Latin} {Hebrew}"];

    private sealed class Probe
    {
        public Action<string> SetPlaceholder = null!;
    }

    private sealed class Content : Component<Probe>
    {
        public override Element Render()
        {
            var (placeholder, setPlaceholder) = UseState(Latin);
            Props.SetPlaceholder = setPlaceholder;
            // A tight, opaque LTR frame per box: its local space is physical and it is what gets rendered.
            Element Box(FlowDirection direction) =>
                Border(RichSuggestBoxElement.RichSuggestBox(placeholderText: placeholder)
                        .Width(BoxWidth)
                        .Set(b => b.FlowDirection = direction))
                    .Background("#FF808080")
                    .HAlign(HorizontalAlignment.Left);
            // The first focusable element absorbs any activation focus, keeping both editors unfocused.
            return VStack(Button("focus sink", () => { }), Box(FlowDirection.LeftToRight), Box(FlowDirection.RightToLeft));
        }
    }

    private sealed record Side(string Label, FrameworkElement Box, WinUI.RichEditBox Editor, FrameworkElement? Frame);

    private sealed record Snapshot(byte[] Pixels, int Width, int Height, double ScaleX, double ScaleY);

    private readonly record struct Ink(double Left, double Right, int Count, int Background);

    public override async Task RunAsync()
    {
        var probe = new Probe();
        using var host = H.CreateHost();
        host.Mount(_ => Component<Content, Probe>(probe));
        await Harness.Render();

        bool realized = await Harness.WaitFor(() => Sides() is { Length: 2 } s && s.All(x => x.Editor.IsLoaded));
        var sides = Sides();
        H.Check($"{Name}_Realized", realized && sides.Length == 2, $"sides={sides.Length}");
        if (sides.Length != 2) return;
        var (ltr, rtl) = (sides[0], sides[1]);

        // Pixels and positions are read in each box's own frame, which must be LTR and box-sized.
        foreach (var side in sides)
        {
            var f = side.Frame;
            H.Check($"{Name}_{side.Label}_TightLeftToRightFrame",
                f is not null && f.FlowDirection == FlowDirection.LeftToRight &&
                Near(f.ActualWidth, side.Box.ActualWidth) && Near(In(side, side.Box).Left, 0),
                Invariant($"frame={f?.GetType().Name}, flow={f?.FlowDirection}, width={f?.ActualWidth:F2}, box={side.Box.ActualWidth:F2}"));
        }
        if (sides.Any(s => s.Frame is null)) return;

        // 1-2. Property contract.
        H.Check($"{Name}_RootDirections",
            ltr.Box.FlowDirection == FlowDirection.LeftToRight && rtl.Box.FlowDirection == FlowDirection.RightToLeft);
        foreach (var side in sides)
        {
            var e = side.Editor;
            bool local = e.ReadLocalValue(FrameworkElement.FlowDirectionProperty) != DependencyProperty.UnsetValue;
            Console.WriteLine(Invariant($"# {Name} {side.Label} editor: flow={e.FlowDirection}, localFlow={local}, ") +
                Invariant($"textAlignment={e.TextAlignment}, readingOrder={e.TextReadingOrder}, padding={e.Padding}, ") +
                Invariant($"border={e.BorderThickness}, focus={e.FocusState}, scale={e.XamlRoot?.RasterizationScale}"));
            H.Check($"{Name}_{side.Label}_EditorInheritsFlow", e.FlowDirection == side.Box.FlowDirection,
                $"root={side.Box.FlowDirection}, editor={e.FlowDirection}");
            H.Check($"{Name}_{side.Label}_Unfocused", e.FocusState == FocusState.Unfocused, $"focus={e.FocusState}");
        }
        var padding = ltr.Editor.Padding;
        var border = ltr.Editor.BorderThickness;
        // Positive control: a symmetric inset would make every mirror comparison below a tautology.
        H.Check($"{Name}_ChromeInsetAsymmetric",
            Math.Abs((padding.Left + border.Left) - (padding.Right + border.Right)) > 2 * LayoutTolerance &&
            rtl.Editor.Padding == padding && rtl.Editor.BorderThickness == border,
            $"padding={padding}, border={border}, rtlPadding={rtl.Editor.Padding}");

        // 3. Placeholder geometry, for each script, in both directions.
        foreach (var (text, latin) in new[] { (Latin, true), (Hebrew, false) })
        {
            probe.SetPlaceholder(text);
            bool shown = await Harness.WaitFor(() => sides.All(s =>
                Placeholder(s) is { } p && p.Text == text && p.Visibility == Visibility.Visible && p.ActualWidth > 0));
            H.Check($"{Name}_Placeholder_{Script(latin)}_Shown", shown);
            var current = Sides();
            H.Check($"{Name}_Placeholder_{Script(latin)}_RetainedNative",
                current.Length == 2 && ReferenceEquals(ltr.Box, current[0].Box) && ReferenceEquals(rtl.Box, current[1].Box));
            if (!shown) continue;
            await Harness.Render(100);
            foreach (var side in sides) CheckPlaceholder(side, latin, await SnapAsync(side, $"placeholder {Script(latin)}"));
        }

        // 4. Document geometry.
        foreach (var side in sides)
            side.Editor.TextDocument.SetText(TextSetOptions.None, string.Join('\r', Paragraphs));
        // WaitFor checks before it pumps, and view invalidation only schedules a measure.
        await Harness.Render();
        bool hidden = await Harness.WaitFor(() => sides.All(s => Placeholder(s)?.Visibility != Visibility.Visible));
        H.Check($"{Name}_Document_PlaceholderHidden", hidden);
        await Harness.Render(100);

        var ltrInk = MeasureDocument(ltr, await SnapAsync(ltr, "document"));
        var rtlInk = MeasureDocument(rtl, await SnapAsync(rtl, "document"));
        await RecordPlainEditorAsync(ltr);
        if (ltrInk is null || rtlInk is null) return;
        foreach (var (side, ink) in new[] { (ltr, ltrInk), (rtl, rtlInk) })
        {
            var view = TextView(side)!;
            string detail = Invariant($"{Describe(ink)}; view={ViewRect(side)}");
            H.Check($"{Name}_{side.Label}_Document_LatinStartsShared",
                Math.Abs(ink[0].Left - ink[3].Left) <= SameGlyphTolerance, detail);
            H.Check($"{Name}_{side.Label}_Document_HebrewEndsShared",
                Math.Abs(ink[1].Right - ink[2].Right) <= SameGlyphTolerance, detail);
            H.Check($"{Name}_{side.Label}_Document_SpansViewport",
                Math.Abs(ink[1].Right - ink[0].Left - view.ActualWidth) <= SpanTolerance,
                Invariant($"span={ink[1].Right - ink[0].Left:F2}, viewWidth={view.ActualWidth:F2}; {detail}"));
        }

        // LTR vs RTL: identical relative to the text view; only the view's physical inset mirrors.
        var (ltrView, rtlView) = (ViewRect(ltr), ViewRect(rtl));
        H.Check($"{Name}_Document_TextViewRelativeInkIdentical",
            ltrInk.Zip(rtlInk).All(p =>
                Math.Abs((p.First.Left - ltrView.Left) - (p.Second.Left - rtlView.Left)) <= SameGlyphTolerance &&
                Math.Abs((p.First.Right - ltrView.Left) - (p.Second.Right - rtlView.Left)) <= SameGlyphTolerance),
            Invariant($"ltr=[{Describe(ltrInk)}] view={ltrView}; rtl=[{Describe(rtlInk)}] view={rtlView}"));

        var ltrInset = ViewInset(ltr);
        var rtlInset = ViewInset(rtl);
        string inset = Invariant($"ltr={ltrInset}, rtl={rtlInset}, padding={padding}, border={border}");
        Console.WriteLine($"# {Name} text-view physical insets (left,right): {inset}");
        H.Check($"{Name}_TextView_PaddingMirrors",
            ltrInset is { } l && rtlInset is { } r &&
            Near(l.Left, padding.Left + border.Left) && Near(l.Right, padding.Right + border.Right) &&
            Near(r.Left, padding.Right + border.Right) && Near(r.Right, padding.Left + border.Left), inset);

        // Normalized bounds cannot show a mirrored text view, so transform its two local edges directly.
        foreach (var side in sides)
        {
            var view = TextView(side);
            var t = view?.TransformToVisual(side.Frame);
            double start = t?.TransformPoint(new Point(0, 0)).X ?? double.NaN;
            double end = t?.TransformPoint(new Point(view!.ActualWidth, 0)).X ?? double.NaN;
            string orientation = Invariant($"type={view?.GetType().Name}, localStart->{start:F2}, localEnd->{end:F2}, width={view?.ActualWidth:F2}");
            Console.WriteLine($"# {Name} {side.Label} text-view orientation: {orientation}");
            H.Check($"{Name}_{side.Label}_TextView_PhysicallyLeftToRight",
                view is not null && Near(end - start, view.ActualWidth) && view.ActualWidth > 2 * SpanTolerance, orientation);
        }
    }

    // Recorded only, to classify the document outcome: the same SetText into a plain WinUI RichEditBox
    // created outside Reactor and Toolkit, in RTL, in the same kind of frame. Added after the last
    // Reactor render and removed before teardown, so the reconciler never sees it.
    private async Task RecordPlainEditorAsync(Side sibling)
    {
        if (VisualTreeHelper.GetParent(sibling.Frame!) is not WinUI.Panel panel) return;
        var editor = new WinUI.RichEditBox { Width = BoxWidth, FlowDirection = FlowDirection.RightToLeft };
        var frame = new WinUI.Border
        {
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 128, 128, 128)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = editor,
        };
        panel.Children.Add(frame);
        try
        {
            var plain = new Side("PlainRTL", editor, editor, frame);
            bool ready = await Harness.WaitFor(() => editor.IsLoaded && TextView(plain) is not null);
            Console.WriteLine(Invariant($"# {Name} PlainRTL editor: ready={ready}, flow={editor.FlowDirection}, ") +
                Invariant($"textAlignment={editor.TextAlignment}, readingOrder={editor.TextReadingOrder}, padding={editor.Padding}, focus={editor.FocusState}"));
            if (!ready) return;
            editor.TextDocument.SetText(TextSetOptions.None, string.Join('\r', Paragraphs));
            await Harness.Render();
            await Harness.Render(100);
            var ink = MeasureDocument(plain, await SnapAsync(plain, "document"));
            Console.WriteLine($"# {Name} PlainRTL document ink (box-relative): {(ink is null ? "incomplete" : Describe(ink))}; view={ViewRect(plain)}");
        }
        finally
        {
            panel.Children.Remove(frame);
            await Harness.Render();
        }
    }

    private void CheckPlaceholder(Side side, bool latin, Snapshot snapshot)
    {
        // WinUI-private, template-pinned part (RichEditBox_themeresources.xaml at fb389b88).
        var p = Placeholder(side)!;
        H.Check($"{Name}_{side.Label}_Placeholder_{Script(latin)}_InheritsFlow", p.FlowDirection == side.Box.FlowDirection,
            $"root={side.Box.FlowDirection}, placeholder={p.FlowDirection}");

        double boxLeft = In(side, side.Box).Left;
        var (contentLeft, contentRight) = ContentEdges(side);
        double target = latin ? contentLeft : contentRight;
        var first = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var firstInFrame = p.TransformToVisual(side.Frame).TransformBounds(first);
        double edge = latin ? firstInFrame.Left : firstInFrame.Right;

        var block = In(side, p);
        var interior = Interior(side);
        var line = new Rect(interior.Left, block.Top + p.Padding.Top, interior.Width,
            Math.Max(0, block.Height - p.Padding.Top - p.Padding.Bottom));
        var ink = FindInk(snapshot, line);
        double inkEdge = ink is { } i ? (latin ? i.Left : i.Right) : double.NaN;

        // The placeholder TextBlock is content-sized at the start side; its glyphs follow the
        // editor's content box, which is therefore the reference, not the TextBlock's own bounds.
        string detail = Invariant($"flow={p.FlowDirection}, align={p.TextAlignment}, order={p.TextReadingOrder}, ") +
            Invariant($"firstLocal={first}, caretEdge={edge - boxLeft:F2}, inkEdge={inkEdge - boxLeft:F2}, ink={Rel(ink, boxLeft)}, ") +
            Invariant($"content=[{contentLeft - boxLeft:F2},{contentRight - boxLeft:F2}], block=[{block.Left - boxLeft:F2},{block.Right - boxLeft:F2}]");
        Console.WriteLine($"# {Name} {side.Label} placeholder {Script(latin)}: {detail}");
        string sideName = latin ? "AtPhysicalLeft" : "AtPhysicalRight";
        H.Check($"{Name}_{side.Label}_Placeholder_{Script(latin)}_Caret{sideName}",
            first.Height > 0 && Math.Abs(edge - target) <= CaretTolerance, detail);
        H.Check($"{Name}_{side.Label}_Placeholder_{Script(latin)}_Ink{sideName}",
            ink is { Count: > 0 } && Math.Abs(inkEdge - target) <= InkTolerance, detail);
    }

    private Ink[]? MeasureDocument(Side side, Snapshot snapshot)
    {
        var doc = side.Editor.TextDocument;
        var view = TextView(side);
        H.Check($"{Name}_{side.Label}_Document_TextViewFound", view is not null);
        if (view is null) return null;
        double boxLeft = In(side, side.Box).Left;
        var interior = Interior(side);
        var result = new Ink[Paragraphs.Length];
        bool complete = true;
        for (int i = 0; i < Paragraphs.Length; i++)
        {
            var range = doc.GetRange(0, 0);
            range.Move(TextRangeUnit.Paragraph, i);
            range.Expand(TextRangeUnit.Paragraph);
            if (range.Text.EndsWith('\r')) range.MoveEnd(TextRangeUnit.Character, -1);
            range.GetRect(PointOptions.ClientCoordinates, out var client, out int hit);
            range.GetRect(PointOptions.None, out var none, out _);
            var head = doc.GetRange(range.StartPosition, range.StartPosition + 1);
            head.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Baseline,
                PointOptions.Start | PointOptions.ClientCoordinates, out var headLeft);
            head.GetPoint(HorizontalCharacterAlignment.Right, VerticalCharacterAlignment.Baseline,
                PointOptions.Start | PointOptions.ClientCoordinates, out var headRight);
            var format = range.ParagraphFormat;

            // Only the line band comes from GetRect: the middle half of the line, mapped through the
            // (unmirrored, asserted below) text view, across the whole editor interior.
            var band = view.TransformToVisual(side.Frame).TransformBounds(
                new Rect(0, client.Y + client.Height / 4, 1, Math.Max(0, client.Height / 2)));
            var ink = FindInk(snapshot, new Rect(interior.Left, band.Top, interior.Width, band.Height));
            Console.WriteLine(Invariant($"# {Name} {side.Label} p{i}: ink={Rel(ink, boxLeft)}, getRect client={client} none={none} hit={hit}, ") +
                Invariant($"getPoint head left={headLeft} right={headRight}, paragraphFormat rtl={format.RightToLeft} align={format.Alignment}"));
            bool measured = range.Text == Paragraphs[i] && client.Height > 0 && double.IsFinite(client.Y) && ink is { Count: > 0 };
            H.Check($"{Name}_{side.Label}_Document_Paragraph{i}_Measured", measured,
                Invariant($"text={range.Text == Paragraphs[i]}, band={client.Y:F2}+{client.Height:F2}, ink={Rel(ink, boxLeft)}"));
            complete &= measured;
            if (ink is { } found) result[i] = found with { Left = found.Left - boxLeft, Right = found.Right - boxLeft };
        }
        return complete ? result : null;
    }

    // The box-relative text view rect.
    private static Rect ViewRect(Side side)
    {
        var view = In(side, TextView(side)!);
        double boxLeft = In(side, side.Box).Left;
        return new Rect(view.Left - boxLeft, view.Top, view.Width, view.Height);
    }

    private static (double Left, double Right)? ViewInset(Side side)
    {
        if (TextView(side) is not { } view) return null;
        var editor = In(side, side.Editor);
        var v = In(side, view);
        return (Math.Round(v.Left - editor.Left, 2), Math.Round(editor.Right - v.Right, 2));
    }

    // Editor content box in frame coordinates: border plus padding, mirrored with flow.
    private static (double Left, double Right) ContentEdges(Side side)
    {
        var editor = In(side, side.Editor);
        var (b, p) = (side.Editor.BorderThickness, side.Editor.Padding);
        double start = b.Left + p.Left, end = b.Right + p.Right;
        return side.Box.FlowDirection == FlowDirection.RightToLeft
            ? (editor.Left + end, editor.Right - start)
            : (editor.Left + start, editor.Right - end);
    }

    // Editor interior (inside the border) in frame coordinates; the scan never sees border strokes.
    private static Rect Interior(Side side)
    {
        var editor = In(side, side.Editor);
        var b = side.Editor.BorderThickness;
        double inset = Math.Max(b.Left, b.Right) + 1;
        return new Rect(editor.Left + inset, editor.Top + b.Top, Math.Max(0, editor.Width - 2 * inset),
            Math.Max(0, editor.Height - b.Top - b.Bottom));
    }

    private static Rect In(Side side, FrameworkElement element) =>
        element.TransformToVisual(side.Frame).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private async Task<Snapshot> SnapAsync(Side side, string phase)
    {
        var root = side.Frame!;
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        var snapshot = new Snapshot(pixels, rtb.PixelWidth, rtb.PixelHeight,
            rtb.PixelWidth / root.ActualWidth, rtb.PixelHeight / root.ActualHeight);
        double scale = root.XamlRoot?.RasterizationScale ?? 1;
        string detail = Invariant($"bitmap={rtb.PixelWidth}x{rtb.PixelHeight}, frame={root.ActualWidth:F2}x{root.ActualHeight:F2}, ") +
            Invariant($"scale={snapshot.ScaleX:F3}x{snapshot.ScaleY:F3}, rasterization={scale}, bytes={pixels.Length}");
        Console.WriteLine($"# {Name} {side.Label} {phase} snapshot: {detail}");
        // Precondition for every ink check: the bitmap covers exactly the frame at the device scale.
        H.Check($"{Name}_{side.Label}_{phase.Replace(' ', '_')}_BitmapCoversFrame",
            Math.Abs(snapshot.ScaleX - scale) <= 0.02 && Math.Abs(snapshot.ScaleY - scale) <= 0.02 &&
            pixels.Length >= rtb.PixelWidth * rtb.PixelHeight * 4, detail);
        return snapshot;
    }

    // Horizontal ink extent (frame DIPs) of pixels contrasting with the region's median luminance.
    private static Ink? FindInk(Snapshot s, Rect region)
    {
        int x0 = Math.Clamp((int)Math.Floor(region.Left * s.ScaleX), 0, s.Width);
        int x1 = Math.Clamp((int)Math.Ceiling(region.Right * s.ScaleX), 0, s.Width);
        int y0 = Math.Clamp((int)Math.Floor(region.Top * s.ScaleY), 0, s.Height);
        int y1 = Math.Clamp((int)Math.Ceiling(region.Bottom * s.ScaleY), 0, s.Height);
        if (x1 <= x0 || y1 <= y0 || s.Pixels.Length < s.Width * s.Height * 4) return null;
        var luminance = new int[(x1 - x0) * (y1 - y0)];
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                luminance[n++] = Luminance(s, x, y);
        var sorted = (int[])luminance.Clone();
        Array.Sort(sorted);
        int background = sorted[sorted.Length / 2];
        int min = int.MaxValue, max = -1, count = 0;
        n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (Math.Abs(luminance[n++] - background) > InkContrast)
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                    count++;
                }
        return max < 0
            ? new Ink(double.NaN, double.NaN, 0, background)
            : new Ink(min / s.ScaleX, (max + 1) / s.ScaleX, count, background);
    }

    private static int Luminance(Snapshot s, int x, int y)
    {
        int i = (y * s.Width + x) * 4; // BGRA8, premultiplied
        return (s.Pixels[i + 2] * 299 + s.Pixels[i + 1] * 587 + s.Pixels[i] * 114) / 1000;
    }

    private Side[] Sides() => H.FindAllControls<Wct.RichSuggestBox>(_ => true)
        .Select(box => (box, editor: Descendants(box).OfType<WinUI.RichEditBox>().FirstOrDefault(e => e.Name == "RichEditBox")))
        .Where(x => x.editor is not null)
        .Select(x => new Side(x.box.FlowDirection == FlowDirection.RightToLeft ? "RTL" : "LTR", x.box, x.editor!,
            VisualTreeHelper.GetParent(x.box) as WinUI.Border))
        .OrderBy(s => s.Label == "RTL")
        .ToArray();

    private static WinUI.TextBlock? Placeholder(Side side) =>
        Descendants(side.Editor).OfType<WinUI.TextBlock>().FirstOrDefault(t => t.Name == "PlaceholderTextContentPresenter");

    private static FrameworkElement? TextView(Side side) =>
        Descendants(side.Editor).OfType<WinUI.ScrollViewer>().FirstOrDefault(s => s.Name == "ContentElement")?.Content as FrameworkElement;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= LayoutTolerance;

    private static string Script(bool latin) => latin ? "Latin" : "Hebrew";

    private static string Rel(Ink? ink, double origin) => ink is { } i
        ? Invariant($"[{i.Left - origin:F2},{i.Right - origin:F2}] px={i.Count} bg={i.Background}")
        : "none";

    private static string Describe(Ink[] ink) =>
        string.Join("; ", ink.Select((x, i) => Invariant($"p{i}=[{x.Left:F2},{x.Right:F2}]")));

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
