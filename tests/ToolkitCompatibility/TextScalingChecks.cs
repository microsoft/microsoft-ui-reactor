using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitCompatibility;

internal static class TextScalingChecks
{
    internal static void Check(FrameworkElement control)
    {
        Program.Check(control is Wct.SettingsCard, "Text-scaling subject is not the expected Toolkit SettingsCard.");
        var card = (Wct.SettingsCard)control;
        if (card.Header is not string || card.Description is not Native.TextBlock)
            throw new ScenarioBlockedException("Text-scaling coverage requires a string Header and a TextBlock Description; other slot shapes are not implemented.");

        string header = (string)card.Header;
        var description = (Native.TextBlock)card.Description;
        // Header and Content may contain the same string in the core scenario. Do not
        // let the content's TextBlock substitute for a missing native header.
        var excluded = new HashSet<DependencyObject>(CompatibilityRoot.Descendants(description));
        if (card.Content is DependencyObject content) excluded.UnionWith(CompatibilityRoot.Descendants(content));
        if (card.HeaderIcon is DependencyObject icon) excluded.UnionWith(CompatibilityRoot.Descendants(icon));
        var headers = CompatibilityRoot.Descendants(card).OfType<Native.TextBlock>()
            .Where(t => t.Text == header && !excluded.Contains(t)).ToArray();
        Program.Results.Note($"Text-scale header='{header}'; matching header TextBlocks={headers.Length}; description='{description.Text}'");
        Program.Check(headers.Length == 1, $"Expected exactly one realized SettingsCard header, found {headers.Length}.");

        control.UpdateLayout();
        CheckText(headers[0], "Header");
        CheckText(description, "Description");
    }

    private static void CheckText(Native.TextBlock text, string role)
    {
        Program.Check(CompatibilityRoot.IsAttached(text), $"{role}: TextBlock is not attached.");
        Program.Results.Note($"Text-scale {role}: text='{text.Text}'; enabled={text.IsTextScaleFactorEnabled}; trimmed={text.IsTextTrimmed}; " +
            $"allocated={text.ActualWidth}x{text.ActualHeight}; font={text.FontFamily.Source}/{text.FontSize}; " +
            $"wrapping={text.TextWrapping}; maxLines={text.MaxLines}; theme={text.ActualTheme}; rasterizationScale={text.XamlRoot.RasterizationScale}");
        TextScalingMeasurements.RequireText(role, text.Text, text.IsTextScaleFactorEnabled, text.IsTextTrimmed);

        // Measure the full text at the real OS scale with the same typography, but no
        // trimming, line cap or fixed height. Never mutate the subject or OS setting.
        var probe = new Native.TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            FontStretch = text.FontStretch,
            CharacterSpacing = text.CharacterSpacing,
            LineHeight = text.LineHeight,
            LineStackingStrategy = text.LineStackingStrategy,
            TextLineBounds = text.TextLineBounds,
            OpticalMarginAlignment = text.OpticalMarginAlignment,
            TextWrapping = text.TextWrapping,
            TextAlignment = text.TextAlignment,
            FlowDirection = text.FlowDirection,
            Language = text.Language,
            Padding = text.Padding,
            IsTextScaleFactorEnabled = true,
            TextTrimming = TextTrimming.None,
            MaxLines = 0
        };
        probe.Measure(new Size(text.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : text.ActualWidth,
            double.PositiveInfinity));
        Program.Results.Note($"Text-scale {role}: full-text required={probe.DesiredSize.Width}x{probe.DesiredSize.Height}");
        TextScalingMeasurements.RequireFits(role, text.ActualWidth, text.ActualHeight,
            probe.DesiredSize.Width, probe.DesiredSize.Height);

        var root = ReactorApp.PrimaryWindow!.NativeWindow.Content;
        DependencyObject? current = text;
        while (current is not null)
        {
            if (current is UIElement element)
            {
                Program.Check(element.Visibility == Visibility.Visible && element.Opacity > 0,
                    $"{role}: hidden or transparent ancestor {current.GetType().Name}.");
                if (element.Clip is not null)
                    throw new ScenarioBlockedException($"{role}: explicit clip geometry on {current.GetType().Name} is not covered by the rectangular layout oracle.");
            }
            if (current is FrameworkElement ancestor)
            {
                var bounds = text.TransformToVisual(ancestor).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                Program.Results.Note($"Text-scale {role}: ancestor={ancestor.GetType().Name}/{ancestor.Name}; " +
                    $"text-bounds={bounds}; ancestor-size={ancestor.ActualWidth}x{ancestor.ActualHeight}");
                TextScalingMeasurements.RequireContained($"{role}/{ancestor.GetType().Name}", bounds.X, bounds.Y,
                    bounds.Width, bounds.Height, ancestor.ActualWidth, ancestor.ActualHeight);
            }
            if (ReferenceEquals(current, root)) return;
            current = VisualTreeHelper.GetParent(current);
        }
        throw new InvalidOperationException($"{role}: ancestor walk did not reach the window content.");
    }
}
