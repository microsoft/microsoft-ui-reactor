using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WctControls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal sealed record Scenario(
    Func<int, Element> Build, Action<FrameworkElement, int> Check,
    Func<Element, int, Element>? Wrap = null)
{
    internal Element InContext(Element element, int phase) => Wrap?.Invoke(element, phase) ?? element;
}

internal static class Scenarios
{
    internal static readonly string[] Names =
    [
        "SettingsCard", "SettingsExpander", "Segmented", "RangeSelector", "RadialGauge",
        "ColorPicker", "ImageCropper", "CameraPreview", "TokenizingTextBox", "RichSuggestBox",
        "GridSplitter", "ContentSizer", "PropertySizer", "HeaderedContentControl",
        "HeaderedItemsControl", "HeaderedTreeView", "LayoutTransformControl", "MetadataControl",
        "ConstrainedBox", "UniformGrid", "DockPanel", "WrapPanel", "StaggeredPanel",
        "SwitchPresenter", "TabbedCommandBar"
    ];

    internal static IEnumerable<string> Operations(string name) =>
        new[] { "mount", "update", "remove", "remount" }
            .Concat(PendingOperations(name))
            .Concat(AdditionalOperations(name));

    internal static IEnumerable<string> AdditionalOperations(string name) => name switch
    {
        "SettingsCard" or "SettingsExpander" => ["settings-rich-string-slots-and-naming", "settings-borrowed-header-ownership"],
        "ContentSizer" => ["sample-target-lifecycle"],
        "PropertySizer" => ["sample-native-binding-target"],
        "RichSuggestBox" => ["sample-native-suggestion-filtering", "sample-native-suggestion-choice", "sample-deferred-choice-burst"],
        "SwitchPresenter" => ["sample-native-case-selection"],
        "TokenizingTextBox" => ["sample-filtering-tokens"],
        "RangeSelector" => ["sample-silent-drift-reassertion"],
        "TabbedCommandBar" => ["sample-native-tabs-commands", "inherited-content-visibility"],
        _ => []
    };

    internal static IEnumerable<string> PendingOperations(string name)
    {
        yield return "uia-name-role-update";
        yield return "uia-value-patterns";
        yield return "programmatic-focus";
        yield return "light-dark-render-capture";
        yield return "rtl-render-capture";
        yield return "high-contrast-render-capture";
        yield return "high-contrast-appearance-review";
        yield return "os-text-scaling";
        yield return name switch
        {
            "SettingsCard" => "rich-slots-clear-restore-click",
            "SettingsExpander" => "rich-slots-keyed-reorder-keyboard-expansion",
            "Segmented" => "real-selection-parent-accept-reject-callbacks",
            "RangeSelector" => "both-real-thumbs-parent-accept-reject-callback-routing",
            "RadialGauge" => "real-value-input-and-callback",
            "ColorPicker" => "real-color-input-parent-reject-callback",
            "ImageCropper" => "crop-export-dimensions-pixels",
            "CameraPreview" => "actual-frames-stop-reentry",
            "TokenizingTextBox" => "type-suggest-add-remove-max-tokens",
            "RichSuggestBox" => "type-query-insert-delete-token",
            "GridSplitter" => "real-grid-definition-resize",
            "ContentSizer" => "real-target-resize",
            "PropertySizer" => "real-bound-target-resize-bounds",
            "HeaderedContentControl" => "rich-header-content-clear-restore",
            "HeaderedItemsControl" => "keyed-reorder-identity",
            "HeaderedTreeView" => "hierarchical-expansion-selection",
            "LayoutTransformControl" => "transformed-hit-testing",
            "MetadataControl" => "metadata-command",
            "ConstrainedBox" => "min-max-constraints",
            "UniformGrid" => "first-column-keyed-reorder",
            "DockPanel" => "attached-dock-last-child-fill-geometry",
            "WrapPanel" => "orientation-keyed-reflow",
            "StaggeredPanel" => "keyed-shortest-column-reflow",
            "SwitchPresenter" => "actual-matching-default-case-collection",
            "TabbedCommandBar" => "actual-tabs-command-invocation",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        if (name == "CameraPreview")
            yield return "denied-unavailable-cleanup";
    }

    private static string Label(int phase) => phase == 0 ? "initial-label" : "updated-label";
    private static void Equal<T>(T actual, T expected, string property)
    {
        Program.Results.Note($"{property}: expected={expected}; actual={actual}");
        Program.Check(EqualityComparer<T>.Default.Equals(actual, expected),
            $"{property}: expected {expected}, actual {actual}.");
    }
    private static void Near(double actual, double expected, string property)
    {
        Program.Results.Note($"{property}: expected={expected}; actual={actual}");
        Program.Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1,
            $"{property}: expected {expected}, actual {actual}.");
    }
    private static Element Child(int phase) => TextBlock(Label(phase)).WithKey("child");
    private static string[] Texts(DependencyObject root) => CompatibilityRoot.Descendants(root)
        .OfType<TextBlock>().Select(t => t.Text).ToArray();
    private static void HasText(DependencyObject root, string value) =>
        Program.Check(Texts(root).Contains(value, StringComparer.Ordinal),
            $"Missing realized text '{value}'. Actual: {string.Join(" | ", Texts(root))}");

    internal static Scenario Create(string name)
    {
        var fixtureText = name is "DockPanel" or "WrapPanel" or "StaggeredPanel"
            ? MeasureFixtureLabels() : default;
        double tileWidth = fixtureText.WidthAtLeast(90);
        double columnWidth = fixtureText.WidthAtLeast(100);
        double edgeWidth = fixtureText.WidthAtLeast(80);
        double edgeHeight = fixtureText.HeightAtLeast(40);
        double bottomHeight = fixtureText.HeightAtLeast(60);
        double lastWidth = fixtureText.WidthAtLeast(50);
        double rasterizationScale = name == "WrapPanel"
            ? ReactorApp.PrimaryWindow!.NativeWindow.Content.XamlRoot.RasterizationScale : 1;
        Scenario scenario = name switch
        {
        "SettingsCard" => new(
            p => SettingsCardElement.SettingsCard(header: Label(p), description: $"description-{p}",
                content: Child(p), headerIcon: Icon(FontIcon("\uE701"))).Width(500),
            (c, p) =>
            {
                var card = (Wct.SettingsCard)c;
                Equal(card.Header?.ToString(), Label(p), "Header");
                Equal((card.Description as TextBlock)?.Text, $"description-{p}", "Description slot");
                HasText(card, Label(p));
                Program.Check(card.HeaderIcon is FontIcon, "HeaderIcon slot was not realized.");
            }),
        "SettingsExpander" => new(
            p => SettingsExpanderElement.SettingsExpander(header: Label(p), isExpanded: true,
                items: p == 0
                    ? new object[] { SettingsCardElement.SettingsCard(header: Label(p)).WithKey("child") }
                    : new object[] { SettingsCardElement.SettingsCard(header: Label(p)).WithKey("child"),
                        SettingsCardElement.SettingsCard(header: "added").WithKey("added") })
                .Width(500),
            (c, p) =>
            {
                var expander = (Wct.SettingsExpander)c;
                Equal(expander.Header?.ToString(), Label(p), "Header");
                Equal(expander.Items.Count, p + 1, "Items.Count");
                HasText(expander, Label(p));
                if (p == 1) HasText(expander, "added");
            }),
        "Segmented" => new(
            p => SegmentedElement.Segmented(selectedIndex: p,
                items: new object[] { "List", "Grid", "Details" }).Width(400),
            (c, p) =>
            {
                var segmented = (Wct.Segmented)c;
                Equal(segmented.SelectedIndex, p, "SelectedIndex");
                Equal(segmented.SelectedItem?.ToString(), p == 0 ? "List" : "Grid", "SelectedItem");
                Equal(segmented.Items.Count, 3, "Items.Count");
                HasText(segmented, "List");
                HasText(segmented, "Grid");
            }),
        "RangeSelector" => new(
            p => RangeSelectorElement.RangeSelector(minimum: 0, maximum: 100,
                rangeStart: p == 0 ? 20 : 30, rangeEnd: p == 0 ? 80 : 70).Width(400),
            (c, p) =>
            {
                var range = (Wct.RangeSelector)c;
                Near(range.RangeStart, p == 0 ? 20 : 30, "RangeStart");
                Near(range.RangeEnd, p == 0 ? 80 : 70, "RangeEnd");
                Equal(CompatibilityRoot.Descendants(range)
                    .OfType<Microsoft.UI.Xaml.Controls.Primitives.Thumb>().Count(), 2, "realized range thumbs");
            }),
        "RadialGauge" => new(
            p => RadialGaugeElement.RadialGauge(value: p == 0 ? 25 : 65,
                minimum: 0, maximum: 100, unit: p == 0 ? "%" : "rpm").Size(180, 180),
            (c, p) =>
            {
                var gauge = (Wct.RadialGauge)c;
                Near(gauge.Value, p == 0 ? 25 : 65, "Value");
                Equal(gauge.Unit, p == 0 ? "%" : "rpm", "Unit");
            }),
        "ColorPicker" => new(
            p => WctControls.ColorPickerElement.ColorPicker(color: p == 0 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Blue,
                isAlphaEnabled: p == 1).Width(400),
            (c, p) =>
            {
                var picker = (Wct.ColorPicker)c;
                Equal(picker.Color, p == 0 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Blue, "Color");
                Equal(picker.IsAlphaEnabled, p == 1, "IsAlphaEnabled");
                Program.Check(CompatibilityRoot.Descendants(picker).OfType<Microsoft.UI.Xaml.Controls.Primitives.ColorSpectrum>()
                    .Any(s => s.IsLoaded && s.ActualWidth > 0 && s.ActualHeight > 0),
                    "ColorPicker did not realize a visible native ColorSpectrum.");
            }),
        "ImageCropper" => ImageCropper(),
        "CameraPreview" => new(
            p => CameraPreviewElement.CameraPreview(isFrameSourceGroupButtonVisible: p == 1,
                onPreviewFailed: error => Program.Results.Note($"CameraPreview.PreviewFailed: {error}"))
                .Size(400, 240),
            (c, p) => Equal(((Wct.CameraPreview)c).IsFrameSourceGroupButtonVisible, p == 1,
                "IsFrameSourceGroupButtonVisible")),
        "TokenizingTextBox" => new(
            p => TokenizingTextBoxElement.TokenizingTextBox(placeholderText: Label(p),
                maximumTokens: p == 0 ? 3 : 5, suggestedItemsSource: new[] { "alpha", "beta" }).Width(400),
            (c, p) =>
            {
                var tokens = (Wct.TokenizingTextBox)c;
                Equal(tokens.PlaceholderText, Label(p), "PlaceholderText");
                Equal(tokens.MaximumTokens, p == 0 ? 3 : 5, "MaximumTokens");
            }),
        "RichSuggestBox" => new(
            p => RichSuggestBoxElement.RichSuggestBox(placeholderText: Label(p),
                prefixes: p == 0 ? "@" : "#", itemsSource: new[] { "alpha", "beta" }).Size(400, 180),
            (c, p) =>
            {
                var suggestions = (Wct.RichSuggestBox)c;
                Equal(suggestions.PlaceholderText, Label(p), "PlaceholderText");
                Equal(suggestions.Prefixes, p == 0 ? "@" : "#", "Prefixes");
            }),
        "GridSplitter" => new(
            p => GridSplitterElement.GridSplitter().Width(p == 0 ? 12 : 20).Height(150),
            (c, p) => Near(c.ActualWidth, p == 0 ? 12 : 20, "ActualWidth")),
        "ContentSizer" => new(
            p => ContentSizerElement.ContentSizer(
                orientation: p == 0 ? Orientation.Horizontal : Orientation.Vertical).Size(100, 100),
            (c, p) => Equal(((Wct.ContentSizer)c).Orientation,
                p == 0 ? Orientation.Horizontal : Orientation.Vertical, "Orientation")),
        "PropertySizer" => new(
            p => PropertySizerElement.PropertySizer(binding: p == 0 ? 180 : 260,
                minimum: 120, maximum: 360).Size(16, 150),
            (c, p) => Near(((Wct.PropertySizer)c).Binding, p == 0 ? 180 : 260, "Binding")),
        "HeaderedContentControl" => new(
            p => HeaderedContentControlElement.HeaderedContentControl(header: $"header-{p}",
                content: Child(p)).Width(400),
            (c, p) =>
            {
                Equal(((Wct.HeaderedContentControl)c).Header?.ToString(), $"header-{p}", "Header");
                HasText(c, $"header-{p}");
                HasText(c, Label(p));
            }),
        "HeaderedItemsControl" => new(
            p => HeaderedItemsControlElement.HeaderedItemsControl(header: $"header-{p}",
                items: p == 0 ? new object[] { Child(p) } : new object[] { Child(p), TextBlock("added") }).Width(400),
            (c, p) =>
            {
                Equal(((Wct.HeaderedItemsControl)c).Items.Count, p + 1, "Items.Count");
                HasText(c, $"header-{p}");
                HasText(c, Label(p));
                if (p == 1) HasText(c, "added");
            }),
        "HeaderedTreeView" => new(
            p => HeaderedTreeViewElement.HeaderedTreeView(header: $"header-{p}",
                itemsSource: new[] { Label(p), $"node-{p}" }).Size(400, 250),
            (c, p) =>
            {
                Equal(((Wct.HeaderedTreeView)c).Header?.ToString(), $"header-{p}", "Header");
                HasText(c, Label(p));
                HasText(c, $"node-{p}");
            }),
        "LayoutTransformControl" => new(
            p => LayoutTransformControlElement.LayoutTransformControl(
                transform: new RotateTransform { Angle = p == 0 ? 0 : 90 },
                content: Child(p).Size(180, 60)).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top),
            (c, p) =>
            {
                HasText(c, Label(p));
                c.Measure(new Windows.Foundation.Size(500, 500));
                Near(c.DesiredSize.Width, p == 0 ? 180 : 60, "transformed desired width");
                Near(c.DesiredSize.Height, p == 0 ? 60 : 180, "transformed desired height");
                Near(c.ActualWidth, p == 0 ? 180 : 60, "transformed arranged width");
                Near(c.ActualHeight, p == 0 ? 60 : 180, "transformed arranged height");
            }),
        "MetadataControl" => new(
            p => MetadataControlElement.MetadataControl(separator: p == 0 ? " / " : " | ",
                items: new Wct.MetadataItem[] { new() { Label = Label(p) }, new() { Label = $"metadata-{p}" } }),
            (c, p) =>
            {
                Equal(((Wct.MetadataControl)c).Separator, p == 0 ? " / " : " | ", "Separator");
                HasText(c, $"{Label(p)}{(p == 0 ? " / " : " | ")}metadata-{p}");
            }),
        "ConstrainedBox" => new(
            p => ConstrainedBoxElement.ConstrainedBox(
                aspectRatio: new Wct.AspectRatio(p == 0 ? 2 : 1, 1),
                content: Border(Child(p))).Width(300).VAlign(VerticalAlignment.Top),
            (c, p) =>
            {
                HasText(c, Label(p));
                Near(c.ActualWidth, 300, "ActualWidth");
                Near(c.ActualHeight, p == 0 ? 150 : 300, "constrained ActualHeight");
            }),
        "UniformGrid" => new(
            p => UniformGridElement.UniformGrid(columns: p == 0 ? 3 : 2, rows: p == 0 ? 3 : 4, firstColumn: 1,
                children: Tiles(p)).Size(300, 240),
            (c, p) =>
            {
                var grid = (Wct.UniformGrid)c;
                Equal(grid.Children.Count, 6, "Children.Count");
                Near(((FrameworkElement)grid.Children[0]).ActualWidth, p == 0 ? 100 : 150, "cell width");
                Near(((FrameworkElement)grid.Children[0]).ActualHeight, p == 0 ? 80 : 60, "cell height");
                Near(((FrameworkElement)grid.Children[0]).TransformToVisual(grid)
                    .TransformPoint(new Windows.Foundation.Point()).X, p == 0 ? 100 : 150, "FirstColumn offset");
                HasText(c, Label(p));
            }),
        "DockPanel" => new(
            p => DockPanelElement.DockPanel(lastChildFill: p == 0, children:
                [
                    (p == 0 ? Border(Child(p)).Width(edgeWidth) : Border(Child(p)).Height(edgeHeight))
                        .WithKey("edge").Set(b => Wct.DockPanel.SetDock(b, p == 0 ? Wct.Dock.Left : Wct.Dock.Top)),
                    Border(TextBlock("bottom")).Height(bottomHeight).WithKey("bottom")
                        .Set(b => Wct.DockPanel.SetDock(b, Wct.Dock.Bottom)),
                    (p == 0 ? Border(TextBlock("last")) : Border(TextBlock("last")).Width(lastWidth)).WithKey("last")
                ]).Size(400, 240),
            (c, p) =>
            {
                var dock = (Wct.DockPanel)c;
                Equal(dock.LastChildFill, p == 0, "LastChildFill");
                Equal(dock.Children.Count, 3, "Children.Count");
                var edge = (FrameworkElement)dock.Children[0];
                var last = (FrameworkElement)dock.Children[2];
                Equal(Wct.DockPanel.GetDock(edge), p == 0 ? Wct.Dock.Left : Wct.Dock.Top, "attached Dock");
                Near(edge.ActualWidth, p == 0 ? edgeWidth : 400, "docked edge width");
                Near(edge.ActualHeight, p == 0 ? 240 : edgeHeight, "docked edge height");
                Near(last.ActualWidth, p == 0 ? 400 - edgeWidth : lastWidth, "last-child width");
                Near(last.ActualHeight, p == 0 ? 240 - bottomHeight : 240 - bottomHeight - edgeHeight, "last-child height");
                Near(Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(last).Width,
                    p == 0 ? 400 - edgeWidth : lastWidth, "last-child allocated width");
                HasText(c, Label(p));
            }),
        "WrapPanel" => new(
            p => WrapPanelElement.WrapPanel(horizontalSpacing: 0, verticalSpacing: 0,
                children: Tiles(p, sized: true, textSize: fixtureText))
                .Width(FixtureTextSize.WrapRowWidth(tileWidth, p == 0 ? 4 : 2, rasterizationScale)),
            (c, p) =>
            {
                var panel = (Wct.WrapPanel)c;
                Equal(panel.Children.Count, 6, "Children.Count");
                var first = (FrameworkElement)panel.Children[0];
                Near(first.ActualWidth, tileWidth, "rounded tile width");
                Near(first.ActualHeight, fixtureText.HeightAtLeast(40), "rounded tile height");
                int columns = p == 0 ? 4 : 2;
                Program.Results.Note($"Wrap fixture columns={columns}; panel-width={panel.ActualWidth}; " +
                    $"tile={first.ActualWidth}x{first.ActualHeight}; rasterization-scale={rasterizationScale}");
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    var tile = (FrameworkElement)panel.Children[i];
                    Near(tile.ActualWidth, first.ActualWidth, $"tile {i} width");
                    Near(tile.ActualHeight, first.ActualHeight, $"tile {i} height");
                    var point = tile.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point());
                    Near(point.X, i % columns * first.ActualWidth, $"tile {i} reflow X");
                    Near(point.Y, i / columns * first.ActualHeight, $"tile {i} reflow Y");
                }
                HasText(c, Label(p));
            }),
        "StaggeredPanel" => new(
            p => StaggeredPanelElement.StaggeredPanel(desiredColumnWidth: (p == 0 ? 1 : 2) * columnWidth,
                columnSpacing: 0, rowSpacing: 0, children: Tiles(p, sized: true, staggered: true, textSize: fixtureText))
                .Size(4 * columnWidth, 300),
            (c, p) =>
            {
                var panel = (Wct.StaggeredPanel)c;
                Equal(panel.Children.Count, 6, "Children.Count");
                Equal(panel.DesiredColumnWidth, (p == 0 ? 1 : 2) * columnWidth, "DesiredColumnWidth");
                var point = ((FrameworkElement)panel.Children[2]).TransformToVisual(panel)
                    .TransformPoint(new Windows.Foundation.Point());
                Near(point.Y, p == 0 ? 0 : fixtureText.HeightAtLeast(60), "third child column reflow Y");
                HasText(c, Label(p));
            }),
        "SwitchPresenter" => new(
            p => SwitchPresenterElement.SwitchPresenter(value: Label(p), content: Child(p)).Width(400),
            (c, p) =>
            {
                Equal(((Wct.SwitchPresenter)c).Value?.ToString(), Label(p), "Value");
                HasText(c, Label(p));
            }),
        "TabbedCommandBar" => new(
            p => TabbedCommandBarElement.TabbedCommandBar().Set(bar => TabbedCommandBarChecks.Configure(bar, p)),
            TabbedCommandBarChecks.Check),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown control.")
        };
        if (name is "UniformGrid" or "WrapPanel" or "StaggeredPanel")
            return WithKeyedChildren(scenario);
        return scenario with
        {
            Wrap = name switch
            {
                "GridSplitter" => (element, phase) => Grid(
                    columns: [GridSize.Star(), GridSize.Px(phase == 0 ? 12 : 20), GridSize.Star()],
                    rows: [GridSize.Star()],
                    Border(TextBlock("Left pane")).Grid(0, 0), element.Grid(0, 1),
                    Border(TextBlock("Right pane")).Grid(0, 2)).Size(420, 150),
                "ContentSizer" => (element, _) => Border(element).Size(240, 180),
                "PropertySizer" => (element, phase) => HStack(
                    Border(TextBlock("Bound target")).Width(phase == 0 ? 180 : 260), element).Height(150),
                "RangeSelector" => (element, _) => VStack(element),
                _ => null
            }
        };
    }

    private static Scenario WithKeyedChildren(Scenario scenario)
    {
        UIElement[]? mounted = null;
        return new(scenario.Build, (control, phase) =>
        {
            scenario.Check(control, phase);
            var children = ((Panel)control).Children;
            if (phase == 0)
                mounted = children.ToArray();
            else
            {
                Program.Check(mounted is { Length: 6 }, "No initial keyed children snapshot.");
                Program.Check(ReferenceEquals(children[0], mounted![2]) && ReferenceEquals(children[1], mounted[0]) &&
                    ReferenceEquals(children[3], mounted[1]) && ReferenceEquals(children[4], mounted[3]) &&
                    ReferenceEquals(children[5], mounted[4]), "Keyed panel reorder lost native identities.");
                Program.Check(Reconciler.GetElementTag(children[2])?.Key == "tile-6" &&
                    !CompatibilityRoot.Descendants(control).OfType<UIElement>()
                        .Any(child => Reconciler.GetElementTag(child)?.Key == "tile-5"),
                    "Panel did not replace the removed logical key with the inserted key.");
                HasText(control, "tile-6");
                Program.Check(!Texts(control).Contains("tile-5"), "Removed panel content remains visible.");
            }
        });
    }

    private static FixtureTextSize MeasureFixtureLabels()
    {
        string[] labels = [Label(0), Label(1), "bottom", "last", .. Enumerable.Range(1, 6).Select(i => $"tile-{i}")];
        return FixtureTextSize.FromMeasurements(labels.Select(label =>
        {
            // Same default typography as the fixture's TextBlock, measured at the real OS scale.
            var text = new TextBlock { Text = label, IsTextScaleFactorEnabled = true };
            text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Program.Results.Note($"fixture label='{label}'; natural-size={text.DesiredSize}");
            return new FixtureTextSize(text.DesiredSize.Width, text.DesiredSize.Height);
        }));
    }

    private static Element[] Tiles(int phase, bool sized = false, bool staggered = false, FixtureTextSize textSize = default) =>
        (phase == 0 ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 2, 0, 6, 1, 3, 4 })
        .Select(i =>
        {
            Element tile = Border(TextBlock(i == 0 ? Label(phase) : $"tile-{i}")).WithKey($"tile-{i}");
            double height = staggered ? new[] { 60, 120, 90, 70, 110, 80, 100 }[i] : 40;
            return sized ? tile.Size(textSize.WidthAtLeast(90), textSize.HeightAtLeast(height)) : tile;
        }).ToArray();

    private static Scenario ImageCropper()
    {
        WriteableBitmap? bitmap = null;
        return new(
            p =>
            {
                bitmap = new WriteableBitmap(64, 48);
                var pixels = new byte[64 * 48 * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = (byte)(p == 0 ? 30 : 200);
                    pixels[i + 1] = (byte)((i / 4) % 64);
                    pixels[i + 2] = (byte)((i / 4) / 64);
                    pixels[i + 3] = 255;
                }
                using (var stream = bitmap.PixelBuffer.AsStream())
                    stream.Write(pixels);
                return ImageCropperElement.ImageCropper(source: bitmap,
                    cropShape: p == 0 ? Wct.CropShape.Rectangular : Wct.CropShape.Circular).Size(400, 300);
            },
            (c, p) =>
            {
                var cropper = (Wct.ImageCropper)c;
                Program.Check(ReferenceEquals(cropper.Source, bitmap), "Cropper did not retain the supplied bitmap.");
                Equal(cropper.CropShape, p == 0 ? Wct.CropShape.Rectangular : Wct.CropShape.Circular, "CropShape");
                var source = cropper.Source ?? throw new InvalidOperationException("Cropper Source is null.");
                Equal(source.PixelWidth, 64, "Source.PixelWidth");
                Equal(source.PixelHeight, 48, "Source.PixelHeight");
            });
    }
}
