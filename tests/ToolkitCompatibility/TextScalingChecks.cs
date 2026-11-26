using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitCompatibility;

internal static class TextScalingChecks
{
    internal static void CheckBoundedTargets(FrameworkElement control, string name)
    {
        Program.Check(control.GetType().FullName == $"CommunityToolkit.WinUI.Controls.{name}",
            $"Text-scale subject does not match {name}.");
        var targets = new List<(Native.TextBlock Text, string Role)>();

        void AddText(DependencyObject scope, string role, string expected, params object?[] excluded)
        {
            var excludedText = excluded.OfType<DependencyObject>()
                .SelectMany(CompatibilityRoot.Descendants).OfType<Native.TextBlock>().ToHashSet();
            var text = TextScalingTargets.SelectOne(role, expected,
                CompatibilityRoot.Descendants(scope).OfType<Native.TextBlock>(), t => t.Text, excludedText);
            Program.Results.Note($"Text-scale target={role}; expected='{expected}'; type={text.GetType().Name}");
            targets.Add((text, role));
        }

        void AddHeader(DependencyObject scope, string role, object? header, params object?[] excluded)
        {
            var value = TextScalingTargets.RequireTarget<string>(role, header);
            AddText(scope, role, value, excluded);
        }

        static DependencyObject Content(object? value, string role) =>
            TextScalingTargets.RequireTarget<DependencyObject>(role, value);

        switch (control)
        {
            case Wct.SettingsExpander expander:
                Program.Check(expander.IsExpanded, "Text-scale specimen requires the initial expanded state.");
                TextScalingTargets.RequireCount("SettingsExpander items", 1, expander.Items.Count);
                var childCard = TextScalingTargets.RequireTarget<Wct.SettingsCard>(
                    "SettingsExpander/Item[0]", expander.Items[0]);
                AddHeader(expander, "Header", expander.Header, childCard, expander.Content, expander.HeaderIcon);
                AddHeader(childCard, "Item[0]/Header", childCard.Header,
                    childCard.Description, childCard.Content, childCard.HeaderIcon);
                break;
            case Wct.Segmented segmented:
                string[] choices = ["List", "Grid", "Details"];
                TextScalingTargets.RequireCount("Segmented items", choices.Length, segmented.Items.Count);
                for (int i = 0; i < choices.Length; i++)
                {
                    Program.Check(Equals(segmented.Items[i], choices[i]), "Segmented initial item identity changed.");
                    AddText(Content(segmented.ContainerFromIndex(i), $"Item[{i}]"), $"Item[{i}]", choices[i]);
                }
                break;
            case Wct.HeaderedContentControl headered:
                var content = Content(headered.Content, "Content");
                AddHeader(headered, "Header", headered.Header, content);
                AddText(content, "Content", "initial-label");
                break;
            case Wct.HeaderedItemsControl items:
                TextScalingTargets.RequireCount("HeaderedItemsControl items", 1, items.Items.Count);
                var item = Content(items.Items[0], "Item[0]");
                AddHeader(items, "Header", items.Header, item);
                AddText(item, "Item[0]", "initial-label");
                break;
            case Wct.HeaderedTreeView tree:
                var nodes = CompatibilityRoot.Descendants(tree).OfType<Native.TreeViewItem>().ToArray();
                TextScalingTargets.RequireCount("HeaderedTreeView nodes", 2, nodes.Length);
                AddHeader(tree, "Header", tree.Header, nodes);
                foreach (var label in new[] { "initial-label", "node-0" })
                {
                    var node = TextScalingTargets.SelectOne($"Node[{label}]", label, nodes,
                        n => CompatibilityRoot.Descendants(n).OfType<Native.TextBlock>()
                            .Any(t => t.Text == label) ? label : "");
                    AddText(node, $"Node[{label}]", label);
                }
                break;
            case Wct.LayoutTransformControl transformed:
                Program.Check(transformed.Transform is RotateTransform { Angle: 0 },
                    "Text-scale specimen covers only the initial unrotated child.");
                AddText(Content(transformed.Child, "Child"), "Child", "initial-label");
                break;
            case Wct.ConstrainedBox constrained:
                AddText(Content(constrained.Content, "Content"), "Content", "initial-label");
                break;
            case Wct.UniformGrid or Wct.WrapPanel or Wct.StaggeredPanel:
                var panel = (Native.Panel)control;
                TextScalingTargets.RequireCount($"{name} children", 6, panel.Children.Count);
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    string key = $"tile-{i}";
                    Program.Check(Reconciler.GetElementTag(panel.Children[i])?.Key == key,
                        $"{name}: initial child key/order differs at {i}.");
                    AddText(panel.Children[i], $"Tile[{key}]", i == 0 ? "initial-label" : key);
                }
                break;
            case Wct.DockPanel dock:
                string[] keys = ["edge", "bottom", "last"];
                string[] labels = ["initial-label", "bottom", "last"];
                TextScalingTargets.RequireCount("DockPanel children", keys.Length, dock.Children.Count);
                for (int i = 0; i < keys.Length; i++)
                {
                    Program.Check(Reconciler.GetElementTag(dock.Children[i])?.Key == keys[i],
                        $"DockPanel: initial child key/order differs at {i}.");
                    AddText(dock.Children[i], $"Child[{keys[i]}]", labels[i]);
                }
                break;
            default:
                throw new UnsupportedTextShapeException($"{name}: no bounded plain-text selector is implemented.");
        }

        Program.Check(targets.Count > 0 && targets.Select(t => t.Text).Distinct().Count() == targets.Count,
            "Text-scale target list is empty or reuses the same TextBlock for multiple roles.");
        // Text assigned through TextBlock.Text may be exposed as one implicit Run.
        foreach (var (text, role) in targets)
        {
            TextScalingTargets.RequirePlainText(role, text.Inlines,
                inline => inline is Microsoft.UI.Xaml.Documents.Run run &&
                run.Text == text.Text && run.FontFamily.Source == text.FontFamily.Source &&
                run.FontSize == text.FontSize && run.FontWeight.Weight == text.FontWeight.Weight &&
                run.FontStyle == text.FontStyle && run.FontStretch == text.FontStretch &&
                run.CharacterSpacing == text.CharacterSpacing && run.Language == text.Language);
        }
        control.UpdateLayout();
        foreach (var (text, role) in targets) CheckText(text, role);
        Program.Results.Note($"Bounded plain-text targets checked={targets.Count}; initial specimen only; appearance-review=pending.");
    }

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
        int rejected = CheckUndersized(card, headers[0], "Header") ? 1 : 0;
        rejected += CheckUndersized(card, description, "Description") ? 1 : 0;
        rejected += CheckAncestorClip(card, headers[0]) ? 1 : 0;
        Program.Check(rejected == 3, $"Expected three native negative-control rejections, observed {rejected}.");
        CheckText(headers[0], "Header");
        CheckText(description, "Description");
        Program.Results.Note("Text-scale native negative controls=3; final positive Header/Description baseline restored.");
    }

    private static bool CheckUndersized(Wct.SettingsCard card, Native.TextBlock text, string role)
    {
        var original = text.ReadLocalValue(FrameworkElement.HeightProperty);
        var before = new Size(text.ActualWidth, text.ActualHeight);
        var cardBefore = new Size(card.ActualWidth, card.ActualHeight);
        Program.Check(before.Height > 4, $"{role}: baseline is too small to establish a nonzero undersizing control.");
        bool rejected = false;
        try
        {
            text.Height = before.Height / 2;
            card.UpdateLayout();
            var slot = Native.Primitives.LayoutInformation.GetLayoutSlot(text);
            double constrainedHeight = Math.Min(text.ActualHeight, slot.Height - text.Margin.Top - text.Margin.Bottom);
            Program.Results.Note($"Text-scale negative {role}-undersized: baseline-height={before.Height}; " +
                $"requested-height={text.Height}; actual-height={text.ActualHeight}; slot={slot}; constrained-height={constrainedHeight}");
            Program.Check(double.IsFinite(constrainedHeight) && constrainedHeight > 0 && constrainedHeight < before.Height - 2,
                $"{role}: native undersizing was not established; no rejection may be claimed.");
            try { CheckText(text, role); }
            catch (TextScalingFailureException ex) when (
                (ex.Role == role || ex.Role.StartsWith(role + "/", StringComparison.Ordinal)) &&
                ex.Kind is TextScalingFailureKind.Trimmed or TextScalingFailureKind.TextOverflow or TextScalingFailureKind.AncestorOverflow)
            {
                rejected = true;
                Program.Results.Note($"Text-scale negative {role}-undersized: expected text/ancestor overflow or trimming; observed={ex.Kind}; {ex.Message}");
            }
        }
        finally
        {
            RestoreLocalValue(text, FrameworkElement.HeightProperty, original);
            card.UpdateLayout();
            Program.Check(Equals(text.ReadLocalValue(FrameworkElement.HeightProperty), original),
                $"{role}: original Height value source was not restored.");
            CheckRestored(card, text, role, before, cardBefore);
        }
        return rejected;
    }

    private static bool CheckAncestorClip(Wct.SettingsCard card, Native.TextBlock header)
    {
        var original = card.ReadLocalValue(UIElement.ClipProperty);
        var before = new Size(header.ActualWidth, header.ActualHeight);
        var cardBefore = new Size(card.ActualWidth, card.ActualHeight);
        var bounds = header.TransformToVisual(card).TransformBounds(new Rect(0, 0, before.Width, before.Height));
        var clip = new RectangleGeometry { Rect = new Rect(0, 0, card.ActualWidth, bounds.Y + bounds.Height / 2) };
        Program.Check(clip.Rect.Height > 0 && clip.Rect.Bottom < bounds.Bottom - 1,
            "Header: ancestor clip does not cut the measured header bounds.");
        bool rejected = false;
        try
        {
            card.Clip = clip;
            card.UpdateLayout();
            Program.Check(ReferenceEquals(card.Clip, clip), "Header: ancestor clip was not applied.");
            Program.Results.Note($"Text-scale negative Header-ancestor-clip: header-bounds={bounds}; clip={clip.Rect}");
            try { CheckText(header, "Header"); }
            catch (ScenarioBlockedException ex) when (ex.ReasonKind == "not-implemented" &&
                ex.Message == "Header: explicit clip geometry on SettingsCard is not covered by the rectangular layout oracle.")
            {
                rejected = true;
                Program.Results.Note($"Text-scale negative Header-ancestor-clip: expected=not-implemented/explicit-clip; observed={ex.ReasonKind}; {ex.Message}");
            }
        }
        finally
        {
            RestoreLocalValue(card, UIElement.ClipProperty, original);
            card.UpdateLayout();
            Program.Check(Equals(card.ReadLocalValue(UIElement.ClipProperty), original),
                "Header: original ancestor Clip value source was not restored.");
            CheckRestored(card, header, "Header", before, cardBefore);
        }
        return rejected;
    }

    private static void RestoreLocalValue(DependencyObject target, DependencyProperty property, object original)
    {
        if (ReferenceEquals(original, DependencyProperty.UnsetValue)) target.ClearValue(property);
        else target.SetValue(property, original);
    }

    private static void CheckRestored(Wct.SettingsCard card, Native.TextBlock text, string role, Size before, Size cardBefore)
    {
        Program.Check(Math.Abs(text.ActualWidth - before.Width) < 0.01 && Math.Abs(text.ActualHeight - before.Height) < 0.01 &&
            Math.Abs(card.ActualWidth - cardBefore.Width) < 0.01 && Math.Abs(card.ActualHeight - cardBefore.Height) < 0.01,
            $"{role}: positive baseline dimensions were not restored after the negative control.");
        CheckText(text, role);
        Program.Results.Note($"Text-scale restored {role}: actual={text.ActualWidth}x{text.ActualHeight}; card={card.ActualWidth}x{card.ActualHeight}");
    }

    private static void CheckText(Native.TextBlock text, string role)
    {
        Program.Check(CompatibilityRoot.IsAttached(text), $"{role}: TextBlock is not attached.");
        var slot = Native.Primitives.LayoutInformation.GetLayoutSlot(text);
        Program.Results.Note($"Text-scale {role}: text='{text.Text}'; enabled={text.IsTextScaleFactorEnabled}; trimmed={text.IsTextTrimmed}; " +
            $"allocated={text.ActualWidth}x{text.ActualHeight}; font={text.FontFamily.Source}/{text.FontSize}; " +
            $"wrapping={text.TextWrapping}; maxLines={text.MaxLines}; theme={text.ActualTheme}; rasterizationScale={text.XamlRoot.RasterizationScale}");
        Program.Results.Note($"Text-scale {role}: desired={text.DesiredSize}; " +
            $"layout-slot={slot}; margin={text.Margin}; layout-rounding={text.UseLayoutRounding}");
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
            UseLayoutRounding = text.UseLayoutRounding,
            IsTextScaleFactorEnabled = true,
            TextTrimming = TextTrimming.None,
            MaxLines = 0
        };
        // ActualWidth can be a fractional text extent below the rounded layout width;
        // using that smaller extent as a fresh constraint can introduce an extra line.
        double probeWidth = text.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity :
            TextScalingMeasurements.ProbeWidth(role, text.ActualWidth, text.DesiredSize.Width, slot.Width,
                text.Margin.Left, text.Margin.Right);
        probe.Measure(new Size(probeWidth, double.PositiveInfinity));
        Program.Results.Note($"Text-scale {role}: probe-width={probeWidth}; " +
            $"full-text required={probe.DesiredSize.Width}x{probe.DesiredSize.Height}; probe-layout-rounding={probe.UseLayoutRounding}");
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
