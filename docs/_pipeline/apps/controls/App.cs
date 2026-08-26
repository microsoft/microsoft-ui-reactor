using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;

// Controls-catalog index doc app (spec 041 §2.1, §6.3).
//
// One canvas per control category. Each canvas is intentionally tiny — the
// page is a thumbnail index, not a detail page. Per-category detail pages
// land in Phase 3 (text-and-media, status-and-info, dialogs-and-flyouts)
// and Phase 3 expansions (forms, collections, charting).
//
// `doc-manifest.yaml` declares one `kind: catalog-thumb` capture per
// category; the harness downscales to 320×240. The snippet markers below
// power the lead snippet in `docs/_pipeline/templates/controls.md.dt`.
ReactorApp.Run<ControlsCatalogApp>("Controls Catalog", width: 480, height: 320
);

// <snippet:lead>
class ControlsCatalogApp : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Controls catalog").FontSize(20).Bold(),
        TextBlock("Every Reactor control, grouped by category.").Opacity(0.7),
        Button("Open Forms", () => { })
    ).Padding(16);
}
// </snippet:lead>

// <snippet:forms-group>
class FormsGroup : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("Ada");
        var (agree, setAgree) = UseState(true);
        var (volume, setVolume) = UseState(60.0);

        return VStack(8,
            TextBox(name, setName, placeholderText: "Name", header: "Name").Width(200),
            CheckBox(agree, setAgree, label: "I agree"),
            Slider(volume, 0, 100, setVolume).Width(200),
            Button("Submit", () => { })
        ).Padding(16);
    }
}
// </snippet:forms-group>

// <snippet:collections-group>
class CollectionsGroup : Component
{
    public override Element Render()
    {
        var items = new[] { "Alpha", "Bravo", "Charlie", "Delta" };
        return VStack(4,
            TextBlock("Items").Bold(),
            ForEach(items, item => TextBlock($"  • {item}"))
        ).Padding(16);
    }
}
// </snippet:collections-group>

// <snippet:text-and-media-group>
class TextAndMediaGroup : Component
{
    public override Element Render() => VStack(6,
        TextBlock("Heading").FontSize(20).Bold(),
        TextBlock("Body text with a moderately long paragraph " +
                  "for catalog-thumb composition.").Opacity(0.8)
    ).Padding(16);
}
// </snippet:text-and-media-group>

// <snippet:status-group>
class StatusGroup : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Saving…").Bold(),
        TextBlock("3 of 12 items").Opacity(0.7)
    ).Padding(16);
}
// </snippet:status-group>

// <snippet:dialogs-group>
class DialogsGroup : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Confirm action").Bold(),
        TextBlock("This cannot be undone.").Opacity(0.8),
        HStack(8,
            Button("Cancel", () => { }),
            Button("Delete", () => { })
        )
    ).Padding(16);
}
// </snippet:dialogs-group>

// <snippet:data-system-group>
class DataSystemGroup : Component
{
    public override Element Render()
    {
        var rows = new[] { ("Ada", 36), ("Linus", 55), ("Grace", 85) };
        return VStack(4,
            HStack(16,
                TextBlock("Name").Bold(),
                TextBlock("Age").Bold()
            ),
            ForEach(rows, r => HStack(16,
                TextBlock(r.Item1),
                TextBlock(r.Item2.ToString())
            ))
        ).Padding(16);
    }
}
// </snippet:data-system-group>

// <snippet:charting-group>
class ChartingGroup : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Revenue (Q1–Q4)").Bold(),
        // Placeholder visual — the real charting category uses ReactorCharting.
        TextBlock("▁ ▃ ▅ ▇").FontSize(28)
    ).Padding(16);
}
// </snippet:charting-group>

// <snippet:layout-group>
class LayoutGroup : Component
{
    public override Element Render() => VStack(8,
        Card(
            HStack(8,
                TextBlock("Border / Card").Bold(),
                TextBlock("wraps one child").Opacity(0.7)
            ).Padding(8)
        ),
        Expander("Expander", TextBlock("Collapsible section body.")),
        Viewbox(TextBlock("Viewbox scales its child").FontSize(10))
    ).Padding(16);
}
// </snippet:layout-group>

// <snippet:navigation-group>
class NavigationGroup : Component
{
    public override Element Render()
    {
        var (tab, setTab) = UseState(1);

        return VStack(8,
            BreadcrumbBar([
                new BreadcrumbBarItemData("Home"),
                new BreadcrumbBarItemData("Controls"),
            ]),
            SelectorBar([
                new SelectorBarItemData("All"),
                new SelectorBarItemData("Forms"),
                new SelectorBarItemData("Layout"),
            ], tab, setTab)
        ).Padding(16);
    }
}
// </snippet:navigation-group>

// <snippet:shapes-group>
class ShapesGroup : Component
{
    // Shape fills/strokes take a WinUI Brush (not a color string like the
    // panel-level .Background(string) modifier).
    static SolidColorBrush Swatch(byte r, byte g, byte b) =>
        new(Color.FromArgb(255, r, g, b));

    public override Element Render() => HStack(12,
        Rectangle().Width(48).Height(32).Fill(Swatch(0x4a, 0x7e, 0xbb)),
        Ellipse().Width(40).Height(40).Fill(Swatch(0xbb, 0x4a, 0x7e)),
        Line(0, 0, 48, 32).Stroke(Swatch(0x7e, 0xbb, 0x4a)).StrokeThickness(3)
    ).Padding(16);
}
// </snippet:shapes-group>
