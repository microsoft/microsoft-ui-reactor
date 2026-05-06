using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared error-fallback rendering used by host shells (full-app render failure)
/// and by the reconciler's in-tree placeholder for component <c>Render()</c> throws.
/// Both surfaces include the full <see cref="Exception.ToString"/> output (type +
/// message + stack + inner exception chain) so users can copy a usable repro
/// without rerunning under a debugger.
/// </summary>
internal static class ErrorFallback
{
    private const string MonoFontStack = "Consolas, Cascadia Mono, Courier New";
    private static readonly global::Windows.UI.Color HeaderRed = global::Windows.UI.Color.FromArgb(255, 196, 43, 28);
    private static readonly global::Windows.UI.Color BorderRed = global::Windows.UI.Color.FromArgb(255, 255, 0, 0);

    /// <summary>
    /// Builds the host-level fallback shown when a top-level render throws.
    /// Returns a scrollable panel: header (type + message, red, bold) above the
    /// full <c>ex.ToString()</c> in a monospace, selectable, wrapping TextBlock.
    /// </summary>
    public static UIElement BuildPanel(Exception ex)
    {
        var header = new TextBlock
        {
            Text = $"Render error: {ex.GetType().Name}: {ex.Message}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(HeaderRed),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var details = new TextBlock
        {
            Text = ex.ToString(),
            FontFamily = new FontFamily(MonoFontStack),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(header);
        stack.Children.Add(details);

        var scroller = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        };

        return new WinUI.Border
        {
            BorderBrush = new SolidColorBrush(BorderRed),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(16),
            Child = scroller,
        };
    }

    /// <summary>
    /// Builds the in-tree placeholder used when a component <c>Render()</c> throws.
    /// Returns a Reactor element tree (ScrollView + monospace, selectable TextBlock)
    /// so the placeholder fits the slot the failed component would have filled and
    /// the stack trace remains visible/copyable.
    /// </summary>
    public static Element BuildElement(Exception ex) =>
        new ScrollViewElement(
            new TextBlockElement($"⚠ Render error: {ex.GetType().Name}: {ex.Message}\n\n{ex}")
            {
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontFamily = new FontFamily(MonoFontStack),
            });
}
