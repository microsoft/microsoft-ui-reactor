using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

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
                ScrollView(
                    TextBlock(sourceCode.Trim())
                        .FontFamily("Consolas, 'Cascadia Code', monospace")
                        .FontSize(13)
                        .Padding(12)
                )
                .Height(200)
                .Background(Theme.SubtleFill),
                isExpanded: false,
                onIsExpandedChanged: null
            )
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
