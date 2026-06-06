using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

/// <summary>
/// Reusable helpers for building consistent control demonstration pages
/// in the WinUI Gallery Reactor app.
/// Follows WinUI Gallery / Fluent Design spacing and theming conventions.
/// </summary>
public static class SamplePageHost
{
    /// <summary>
    /// Renders a themed card containing a live sample, optional options panel,
    /// and a collapsible source code block.
    /// </summary>
    public static Element SampleCard(string title, Element sample, string sourceCode, Element? options = null) =>
        GalleryControls.SampleCard(title, sample, sourceCode, options);

    /// <summary>
    /// Renders a page header with a title and description.
    /// Follows WinUI Gallery page header pattern.
    /// </summary>
    public static Element PageHeader(string title, string description) =>
        GalleryControls.PageHeader(title, description);

    /// <summary>
    /// Renders source code in a bordered monospace block (for inline use).
    /// </summary>
    public static Element SourceBlock(string code) =>
        Border(
            (TextBlock(code.Trim()) with { IsTextSelectionEnabled = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap })
                .Set(tb =>
                {
                    tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, monospace");
                })
                .Foreground(Theme.PrimaryText)
                .ApplyStyle("CaptionTextBlockStyle")
        )
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
        .Padding(12);

    /// <summary>
    /// Renders a bordered options panel with an "Options" label.
    /// </summary>
    public static Element OptionPanel(params Element[] options) =>
        Border(
                VStack(8,
                    new Element[] { Caption("Options").Foreground(Theme.SecondaryText).SemiBold() }
                        .Concat(options)
                        .ToArray()))
            .Background(Theme.SubtleFill)
            .WithBorder(Theme.SurfaceStroke)
            .CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
            .Padding(12);

    /// <summary>
    /// Wraps page content in a ScrollView with proper WinUI Gallery-style margins.
    /// Use this instead of manually wrapping in ScrollView + VStack + Padding.
    /// </summary>
    public static Element PageContent(string title, string description, params Element[] sampleCards) =>
        ScrollView(
            VStack(16,
                new Element[] { PageHeader(title, description) }
                    .Concat(sampleCards)
                    .ToArray()
            )
            .Margin(36, 24, 36, 36)
            .HAlign(HorizontalAlignment.Stretch)
        );
}
