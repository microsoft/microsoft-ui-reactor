using System.Globalization;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
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
/// <c>RichEditBox</c> template part sets no FlowDirection/TextAlignment/TextReadingOrder, so flow
/// is inherited and alignment/reading order stay at WinUI's DetectFromContent; text placement is
/// then decided by content, and FlowDirection only mirrors chrome (the asymmetric
/// TextControlThemePadding). WinUI's <c>CTextBoxView::IsRightToLeft</c> is hard-coded false, so
/// document rects live in an unmirrored text-view frame in both directions.</para>
/// <para>Deliberately not asserted: empty-document caret side (follows the keyboard input
/// language), ParagraphFormat.RightToLeft/Alignment (recorded only), and document coordinate
/// units when RasterizationScale != 1 (DIP vs physical pixels is underdetermined from source).</para>
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
    // A first-character edge vs. a padded content edge adds sub-DIP glyph positioning.
    private const double PlaceholderTolerance = 2.0;
    // Same paragraph start in the same frame, same font and first character: only rounding.
    private const double EdgeTolerance = 1.0;
    // RichEdit's viewport is floored/ceiled to whole units (TxGetViewportRect) and it may keep a
    // trailing caret reserve; wrong alignment would make the span the text width, not ~300 DIP.
    private const double SpanTolerance = 4.0;

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
            Element Box(FlowDirection direction) =>
                RichSuggestBoxElement.RichSuggestBox(placeholderText: placeholder)
                    .Width(BoxWidth)
                    .HAlign(HorizontalAlignment.Left)
                    .Set(b => b.FlowDirection = direction);
            // The first focusable element absorbs any activation focus, keeping both editors unfocused.
            return VStack(Button("focus sink", () => { }), Box(FlowDirection.LeftToRight), Box(FlowDirection.RightToLeft));
        }
    }

    private sealed record Side(string Label, Wct.RichSuggestBox Box, WinUI.RichEditBox Editor);

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

        // 1-2. Property contract.
        H.Check($"{Name}_RootDirections",
            ltr.Box.FlowDirection == FlowDirection.LeftToRight && rtl.Box.FlowDirection == FlowDirection.RightToLeft);
        foreach (var side in sides)
        {
            var e = side.Editor;
            bool local = e.ReadLocalValue(FrameworkElement.FlowDirectionProperty) != DependencyProperty.UnsetValue;
            Console.WriteLine($"# {Name} {side.Label} editor: flow={e.FlowDirection}, localFlow={local}, " +
                $"textAlignment={e.TextAlignment}, readingOrder={e.TextReadingOrder}, padding={e.Padding}, " +
                $"border={e.BorderThickness}, focus={e.FocusState}, scale={e.XamlRoot?.RasterizationScale}");
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
            foreach (var side in sides) CheckPlaceholder(side, text, latin);
        }

        // 4. Document geometry.
        foreach (var side in sides)
            side.Box.TextDocument!.SetText(TextSetOptions.None, string.Join('\r', Paragraphs));
        bool hidden = await Harness.WaitFor(() => sides.All(s => Placeholder(s)?.Visibility != Visibility.Visible));
        H.Check($"{Name}_Document_PlaceholderHidden", hidden);

        var ltrRects = MeasureDocument(ltr);
        var rtlRects = MeasureDocument(rtl);
        if (ltrRects is null || rtlRects is null) return;
        foreach (var (side, r) in new[] { (ltr, ltrRects), (rtl, rtlRects) })
        {
            string detail = Describe(r);
            H.Check($"{Name}_{side.Label}_Document_LatinStartsShared",
                Math.Abs(r[0].Left - r[3].Left) <= EdgeTolerance, detail);
            H.Check($"{Name}_{side.Label}_Document_HebrewEndsShared",
                Math.Abs(r[1].Right - r[2].Right) <= EdgeTolerance, detail);
            H.Check($"{Name}_{side.Label}_Document_ScriptsOnOppositeSides",
                r[1].Right - r[0].Left > r[1].Width + r[0].Width + 4 * EdgeTolerance, detail);

            var view = TextView(side);
            double scale = side.Editor.XamlRoot?.RasterizationScale ?? 0;
            string spanDetail = Invariant($"span={r[1].Right - r[0].Left:F2}, view={view?.ActualWidth:F2}, scale={scale}");
            Console.WriteLine($"# {Name} {side.Label} span: {spanDetail}");
            if (scale == 1)
                H.Check($"{Name}_{side.Label}_Document_SpansViewport",
                    view is not null && Math.Abs(r[1].Right - r[0].Left - view.ActualWidth) <= SpanTolerance, spanDetail);
            else
                H.Skip($"{Name}_{side.Label}_Document_SpansViewport",
                    $"RasterizationScale={scale}: document coordinate units (DIP vs px) are underdetermined");
        }

        // LTR vs RTL: the document frame is identical; only the text view's physical inset mirrors.
        string cross = $"ltr=[{Describe(ltrRects)}], rtl=[{Describe(rtlRects)}]";
        H.Check($"{Name}_Document_FrameIdenticalAcrossFlow",
            ltrRects.Zip(rtlRects).All(p =>
                Math.Abs(p.First.Left - p.Second.Left) <= EdgeTolerance &&
                Math.Abs(p.First.Right - p.Second.Right) <= EdgeTolerance), cross);

        var ltrInset = ViewInset(ltr);
        var rtlInset = ViewInset(rtl);
        string inset = Invariant($"ltr={ltrInset}, rtl={rtlInset}, padding={padding}, border={border}");
        Console.WriteLine($"# {Name} text-view physical insets (left,right): {inset}");
        H.Check($"{Name}_TextView_PaddingMirrors",
            ltrInset is { } l && rtlInset is { } r2 &&
            Near(l.Left, padding.Left + border.Left) && Near(l.Right, padding.Right + border.Right) &&
            Near(r2.Left, padding.Right + border.Right) && Near(r2.Right, padding.Left + border.Left), inset);
    }

    private void CheckPlaceholder(Side side, string text, bool latin)
    {
        // WinUI-private, template-pinned part (RichEditBox_themeresources.xaml at fb389b88).
        var p = Placeholder(side)!;
        var first = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var firstPhysical = Physical(p, first);
        var block = Physical(p, new Rect(0, 0, p.ActualWidth, p.ActualHeight));
        var boxRect = Physical(side.Box, new Rect(0, 0, side.Box.ActualWidth, side.Box.ActualHeight));
        // Padding mirrors with flow: the physical left inset is Padding.Right under RTL.
        bool rtlFlow = p.FlowDirection == FlowDirection.RightToLeft;
        double contentLeft = block.Left + (rtlFlow ? p.Padding.Right : p.Padding.Left);
        double contentRight = block.Right - (rtlFlow ? p.Padding.Left : p.Padding.Right);
        double edge = latin ? firstPhysical.Left : firstPhysical.Right;
        double target = latin ? contentLeft : contentRight;
        string detail = Invariant(
            $"text={Script(latin)}, flow={p.FlowDirection}, align={p.TextAlignment}, order={p.TextReadingOrder}, " +
            $"firstLocal={first}, firstEdge={edge - boxRect.Left:F2}, content=[{contentLeft - boxRect.Left:F2},{contentRight - boxRect.Left:F2}], " +
            $"block=[{block.Left - boxRect.Left:F2},{block.Right - boxRect.Left:F2}]");
        Console.WriteLine($"# {Name} {side.Label} placeholder: {detail}");
        H.Check($"{Name}_{side.Label}_Placeholder_{Script(latin)}_{(latin ? "AtPhysicalLeft" : "AtPhysicalRight")}",
            first.Height > 0 && Math.Abs(edge - target) <= PlaceholderTolerance, detail);
    }

    private Rect[]? MeasureDocument(Side side)
    {
        var doc = side.Box.TextDocument!;
        var rects = new Rect[Paragraphs.Length];
        var report = new List<string>();
        for (int i = 0; i < Paragraphs.Length; i++)
        {
            var range = doc.GetRange(0, 0);
            range.Move(TextRangeUnit.Paragraph, i);
            range.Expand(TextRangeUnit.Paragraph);
            if (range.Text.EndsWith('\r')) range.MoveEnd(TextRangeUnit.Character, -1);
            range.GetRect(PointOptions.ClientCoordinates, out var client, out int hit);
            range.GetRect(PointOptions.None, out var none, out _);
            var format = range.ParagraphFormat;
            rects[i] = client;
            report.Add(Invariant($"p{i}: text={(range.Text == Paragraphs[i])}, client={client}, none={none}, hit={hit}, " +
                $"rtl={format.RightToLeft}, align={format.Alignment}"));
            H.Check($"{Name}_{side.Label}_Document_Paragraph{i}_Measured",
                range.Text == Paragraphs[i] && client.Width > 0 && client.Height > 0 &&
                double.IsFinite(client.Left) && double.IsFinite(client.Right), report[^1]);
        }
        foreach (var line in report) Console.WriteLine($"# {Name} {side.Label} {line}");
        return rects.All(r => r.Width > 0) ? rects : null;
    }

    private (double Left, double Right)? ViewInset(Side side)
    {
        if (TextView(side) is not { } view) return null;
        var editor = Physical(side.Editor, new Rect(0, 0, side.Editor.ActualWidth, side.Editor.ActualHeight));
        var v = Physical(view, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        return (Math.Round(v.Left - editor.Left, 2), Math.Round(editor.Right - v.Right, 2));
    }

    private Side[] Sides() => H.FindAllControls<Wct.RichSuggestBox>(_ => true)
        .Select(box => (box, editor: Descendants(box).OfType<WinUI.RichEditBox>().FirstOrDefault(e => e.Name == "RichEditBox")))
        .Where(x => x.editor is not null)
        .Select(x => new Side(x.box.FlowDirection == FlowDirection.RightToLeft ? "RTL" : "LTR", x.box, x.editor!))
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

    private static Rect Physical(UIElement element, Rect local) => element.TransformToVisual(null).TransformBounds(local);

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= LayoutTolerance;

    private static string Script(bool latin) => latin ? "Latin" : "Hebrew";

    private static string Describe(Rect[] r) =>
        string.Join("; ", r.Select((x, i) => Invariant($"p{i}=[{x.Left:F2},{x.Right:F2}]")));

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
