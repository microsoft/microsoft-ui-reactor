using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

/// <summary>
/// A small "Copy" button used inside <see cref="GalleryControls.SampleCard"/>.
/// Lives as a Component so we can use UseState for the transient "Copied" label.
/// </summary>
internal sealed class CopyToClipboardButton : Component<string>
{
    public override Element Render()
    {
        var text = Props;
        var (copied, setCopied) = UseState(false, threadSafe: true);
        // Per-click generation token: only the latest click is allowed to
        // flip the label back, so rapid clicks can't reset early.
        var generation = UseRef(0);
        // "Mounted" flag flipped in the UseEffect cleanup below — lets the
        // background Task.Delay continuation short-circuit if the component
        // was unmounted before the 1.5s timer fires (avoids touching state
        // on a torn-down RenderContext).
        var isMounted = UseRef(true);
        UseEffect(() =>
        {
            isMounted.Current = true;
            return () => isMounted.Current = false;
        });

        return Button(copied ? "Copied" : "Copy")
            .Click(() =>
            {
                try
                {
                    var dp = new DataPackage();
                    dp.SetText(text);
                    Clipboard.SetContent(dp);
                    // Click handler always runs on the UI thread, so plain ++ is safe.
                    // The timer continuation only READS the int, which is atomic on .NET.
                    var myGen = ++generation.Current;
                    setCopied(true);
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        if (!isMounted.Current) return;
                        if (generation.Current == myGen) setCopied(false);
                    });
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Clipboard.SetContent can throw RPC_E_CALL_REJECTED (HRESULT
                    // 0x80010001) when another app is holding the clipboard
                    // marshaller — swallow this specific COM transient rather
                    // than crash the gallery. Anything else propagates.
                }
            })
            // Overlay chip that adapts to the current theme (matches the WinUI
            // Gallery's copy button, which sits on top of the code surface).
            .FontSize(12)
            .Foreground(Theme.PrimaryText)
            .Background(Theme.Ref("ControlOnImageFillColorDefaultBrush"))
            .CornerRadius(4)
            .Padding(10, 4, 10, 4);
    }
}

/// <summary>
/// The collapsible "Source code" panel body: a rounded, theme-aware editor
/// surface with C# syntax highlighting (matching the WinUI Gallery / ColorCode
/// coloring rules), a line-number gutter, and an overlay copy button. Rendered
/// as a Component so it can react to <see cref="FrameworkElement.ActualThemeChanged"/>
/// and swap between the Light and Dark ColorCode palettes when the app theme toggles.
/// </summary>
internal sealed class SourceCodeView : Component<string>
{
    const double MaxPanelHeight = 420;

    // Resolve the effective theme by walking up the visual tree for the nearest
    // explicit RequestedTheme. This is reliable during reconcile, unlike
    // FrameworkElement.ActualTheme which can lag a synchronous update pass.
    static bool ResolveIsDark(FrameworkElement? start)
    {
        var cur = start;
        while (cur is not null)
        {
            if (cur.RequestedTheme != ElementTheme.Default)
                return cur.RequestedTheme == ElementTheme.Dark;
            cur = VisualTreeHelper.GetParent(cur) as FrameworkElement;
        }
        return Application.Current?.RequestedTheme == ApplicationTheme.Dark;
    }

    public override Element Render()
    {
        var sourceCode = Props;

        // The gallery toggles theme via a per-element RequestedTheme, so resolve
        // the effective theme from the *connected* panel (via OnMount / the
        // ActualThemeChanged sender) and re-render on change. Reading a stored ref
        // or ActualTheme during reconcile is unreliable; walking up RequestedTheme
        // from the live element is what ThemeRef itself does.
        var (isDark, setIsDark) = UseState(
            Application.Current?.RequestedTheme == ApplicationTheme.Dark,
            threadSafe: true);

        var palette = isDark ? CodeHighlighter.Dark : CodeHighlighter.Light;

        var codeParagraphs = CodeHighlighter.Highlight(sourceCode, palette, out int lineCount);
        var gutterParagraphs = CodeHighlighter.Gutter(lineCount, palette);

        var code = (RichTextBlock(codeParagraphs) with
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = CodeHighlighter.CodeFontSize,
                LineHeight = CodeHighlighter.CodeLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            })
            .FontFamily(CodeHighlighter.CodeFontFamily)
            .Padding(16, 12, 20, 12);

        var gutter = Border(
                (RichTextBlock(gutterParagraphs) with
                {
                    TextWrapping = TextWrapping.NoWrap,
                    FontSize = CodeHighlighter.CodeFontSize,
                    LineHeight = CodeHighlighter.CodeLineHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                })
                .FontFamily(CodeHighlighter.CodeFontFamily))
            .Background(palette.GutterBackground)
            .Padding(14, 12, 12, 12);

        // Fixed gutter | horizontally scrollable code. A single Auto row keeps
        // both columns the same height so their rows line up.
        var body = Grid(
            columns: [GridSize.Auto, GridSize.Star()], rows: [GridSize.Auto],
            gutter.Grid(row: 0, column: 0),
            (ScrollView(code) with { ContentOrientation = ScrollingContentOrientation.Horizontal })
                .Grid(row: 0, column: 1)
        );

        // Size to content, capped so long snippets scroll instead of pushing the page.
        double panelHeight = global::System.Math.Min(lineCount * CodeHighlighter.CodeLineHeight + 24, MaxPanelHeight);

        var panel = Border(
                (ScrollView(body) with { ContentOrientation = ScrollingContentOrientation.Vertical })
                    .Height(panelHeight))
            .Background(palette.Background)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(6)
            .OnMount(el =>
            {
                setIsDark(ResolveIsDark(el));
                el.ActualThemeChanged += (sender, _) => setIsDark(ResolveIsDark((FrameworkElement)sender));
            });

        return Grid(
            columns: [GridSize.Star()], rows: [GridSize.Star()],
            panel.Grid(row: 0, column: 0),
            Component<CopyToClipboardButton, string>(sourceCode)
                .HAlign(HorizontalAlignment.Right)
                .VAlign(VerticalAlignment.Top)
                .Margin(0, 8, 12, 0)
                .Grid(row: 0, column: 0)
        );
    }
}

/// <summary>
/// Reusable UI building blocks shared across the WinUI Gallery app.
/// Use via: using static WinUIGalleryReactor.GalleryControls;
/// </summary>
public static class GalleryControls
{
    static CornerRadius ControlRadiusCR => ThemeResource.CornerRadius("ControlCornerRadius");
    static CornerRadius OverlayRadiusCR => ThemeResource.CornerRadius("OverlayCornerRadius");
    static double ControlRadius => ControlRadiusCR.TopLeft;
    static double OverlayRadius => OverlayRadiusCR.TopLeft;

    /// <summary>
    /// Renders a page header with a title and description.
    /// </summary>
    public static Element PageHeader(string title, string description) =>
        VStack(4,
            TextBlock(title)
                .ApplyStyle("TitleTextBlockStyle")
                .Bold(),
            TextBlock(description)
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Left)
                .Margin(0, 0, 0, 12)
                .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                .MaxWidth(800)
        ).Margin(0, 0, 0, 8);

    /// <summary>
    /// Renders a GridView of control cards matching the WinUI Gallery layout.
    /// Each card is 300×92 with image, title, and description.
    /// </summary>
    public static Element ControlCardGrid(ControlInfo[] controls, Action<string> navigate) =>
        (GridView<ControlInfo>(
            controls,
            c => c.Tag,
            (c, _) => Border(
                Grid(
                    columns: [GridSize.Auto, GridSize.Star()], rows: [GridSize.Auto, GridSize.Star()],

                    Image(c.ImagePath)
                        .Width(32).Height(32)
                        .Margin(4, 0, 16, 0)
                        .VAlign(VerticalAlignment.Top)
                        .Grid(rowSpan: 2),

                    TextBlock(c.Title)
                        .SemiBold()
                        .Foreground(Theme.PrimaryText)
                        .VAlign(VerticalAlignment.Bottom)
                        .Grid(column: 1),

                    TextBlock(c.Description)
                        .ApplyStyle("CaptionTextBlockStyle")
                        .Foreground(Theme.SecondaryText)
                        .Set(tb =>
                        {
                            tb.TextWrapping = TextWrapping.Wrap;
                            tb.TextTrimming = TextTrimming.WordEllipsis;
                        })
                        .Grid(row: 1, column: 1)
                )
            )
            .Background(Theme.ControlFill)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(ControlRadius)
            .Width(300).Height(92)
            .Padding(12)
        ) with
        {
            OnItemClick = c => navigate(c.Tag),
            SelectionMode = ListViewSelectionMode.None,
        })
        .Set(gv =>
        {
            gv.IsItemClickEnabled = true;
            gv.IsSwipeEnabled = false;
            // Disable GridView's internal ScrollViewer so it sizes to content
            // and wraps properly inside an outer ScrollView.
            global::Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(gv, ScrollMode.Disabled);
            global::Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(gv, ScrollBarVisibility.Disabled);
            global::Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(gv, ScrollMode.Disabled);
            global::Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(gv, ScrollBarVisibility.Disabled);
            // Set spacing on the ItemsWrapGrid panel so hover stays on the card, not the margin.
            if (gv.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = 300 + 12;
                wrapGrid.ItemHeight = 92 + 12;
            }
            gv.Loaded += (s, _) =>
            {
                if (((GridView)s!).ItemsPanelRoot is ItemsWrapGrid wg)
                {
                    wg.ItemWidth = 300 + 12;
                    wg.ItemHeight = 92 + 12;
                }
            };
            var itemContainerStyle = new Style(typeof(GridViewItem));
            itemContainerStyle.Setters.Add(new Setter(GridViewItem.PaddingProperty, new Thickness(0)));
            itemContainerStyle.Setters.Add(new Setter(GridViewItem.MarginProperty, new Thickness(0, 0, 12, 12)));
            gv.ItemContainerStyle = itemContainerStyle;
        });

    /// <summary>
    /// Renders a themed card containing a live sample, optional options panel,
    /// and a collapsible source code block.
    /// </summary>
    public static Element SampleCard(string title, Element sample, string sourceCode, Element? options = null)
    {
        var children = new List<Element>();

        var sampleArea = Border(
            VStack(8, sample)
        )
        .Padding(24)
        .Background(Theme.SolidBackground)
        .CornerRadius(OverlayRadius, OverlayRadius, 0, 0);

        children.Add(sampleArea);

        if (options is not null)
        {
            children.Add(
                Border(
                    VStack(8,
                        new Element[]
                        {
                            Caption("Options")
                                .Foreground(Theme.SecondaryText)
                                .SemiBold()
                                .Margin(0, 0, 0, 4)
                        }
                        .Concat(new[] { options })
                        .ToArray()
                    )
                )
                .Padding(16)
                .Background(Theme.SubtleFill)
                .WithBorder(Theme.DividerStroke)
            );
        }

        children.Add(
            Expander("Source code",
                Component<SourceCodeView, string>(sourceCode.Trim()))
            .OnMount(el =>
            {
                var exp = (Expander)el;
                exp.HorizontalAlignment = HorizontalAlignment.Stretch;
                exp.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                exp.Padding = new Thickness(0);
            })
        );

        return VStack(0,
            TextBlock(title)
                .ApplyStyle("BodyStrongTextBlockStyle")
                .Margin(0, 0, 0, 12),
            Border(
                VStack(0, children.ToArray()))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.CardStroke)
                    .CornerRadius(OverlayRadius)
        );
    }
}
