using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A tidy tree where each node is a live WinUI control matching its type —
/// Button, ToggleSwitch, Slider, TextBox, CheckBox — laid out by D3's
/// Reingold-Tilford algorithm. All controls are Reactor elements positioned
/// inside a D3Canvas using .Canvas(x, y) attached properties.
/// </summary>
public sealed class ComponentHierarchySample : GallerySample
{
    public override string Title => "Component Hierarchy";
    public override string Description =>
        "A tidy tree layout where each node is a real WinUI control (Button, ToggleSwitch, Slider, etc.) " +
        "positioned by D3's Reingold-Tilford algorithm. Demonstrates hosting interactive controls inside a D3 graph.";
    public override string Category => "Controls";

    public override string SourceCode => """
        var layout = TreeLayout.Create<CtrlNode>().Size(660, 380);
        var root = layout.Hierarchy(data, n => n.Children);
        layout.Layout(root);

        return D3Canvas(W, H,
            [.. nodes.SelectMany(n => n.Children.Select(c =>
                D3Link(n.X, n.Y, c.X, c.Y, stroke: linkBrush))),
             .. nodes.Select(n =>
                ControlForNode(n.Data).Canvas(n.X - offset, n.Y - 14)),
            ]);

        Element ControlForNode(node) => node.Kind switch {
            "Button"       => Button(node.Name, null),
            "ToggleSwitch" => ToggleSwitch(false, null, "On", "Off"),
            "Slider"       => Slider(50, 0, 100).Width(90),
            "TextBox"      => TextBox("", placeholderText: node.Name).Width(90),
            "CheckBox"     => CheckBox(false, label: node.Name),
            _              => HeaderBadge(node.Name),
        };
        .AutomationName("Component Hierarchy")
        .FullDescription("Tree layout of 11 WinUI controls organized in 3 groups, positioned by D3 Reingold-Tilford algorithm.");
        """;

    record CtrlNode(string Name, string Kind, CtrlNode[]? Children = null);

    public override Element Render()
    {
        const double W = 750, H = 460;
        const double PadX = 40, PadY = 40;

        var data = new CtrlNode("App", "Header", [
            new("Navigation", "Header", [
                new("Home", "Button"),
                new("Settings", "Button"),
                new("Dark Mode", "ToggleSwitch"),
            ]),
            new("Content", "Header", [
                new("Search", "TextBox"),
                new("Volume", "Slider"),
                new("Accept Terms", "CheckBox"),
            ]),
            new("Actions", "Header", [
                new("Submit", "Button"),
                new("Reset", "Button"),
            ]),
        ]);

        var layout = TreeLayout.Create<CtrlNode>().Size(W - PadX * 2, H - PadY * 2);
        var root = layout.Hierarchy(data, n => n.Children);
        layout.Layout(root);

        var nodes = root.Descendants().ToList();
        var linkBrush = ChartAxis;

        return D3Canvas(W, H,
        [
            // Bezier links between parent and child nodes
            .. nodes.SelectMany(n =>
                n.Children.Select(c =>
                    (Element)D3Link(PadX + n.X, PadY + n.Y, PadX + c.X, PadY + c.Y,
                        stroke: linkBrush, strokeWidth: 1.5))),

            // WinUI controls at each node position
            .. nodes.Select(n =>
            {
                double nx = PadX + n.X, ny = PadY + n.Y;
                double offsetX = n.Children.Count == 0 ? 45 : 30;
                return ControlForNode(n.Data).Canvas(nx - offsetX, ny - 14);
            }),
        ])
            .AutomationName("Component Hierarchy")
            .FullDescription("Tree layout of 11 WinUI controls organized in 3 groups, positioned by D3 Reingold-Tilford algorithm.");
    }

    static Element ControlForNode(CtrlNode node) => node.Kind switch
    {
        "Button" => Button(node.Name, null).Set(b => { b.FontSize = 11; b.Padding = new Thickness(10, 4, 10, 4); }),
        "ToggleSwitch" => ToggleSwitch(false, null, "On", "Off").Set(ts => { ts.MinWidth = 0; ts.FontSize = 10; }),
        "Slider" => Slider(50, 0, 100).Width(90).Height(32),
        "TextBox" => TextBox("", placeholderText: node.Name).Width(90).Set(tb => { tb.FontSize = 11; tb.Padding = new Thickness(6, 4, 6, 4); }),
        "CheckBox" => CheckBox(false, label: node.Name).Set(cb => { cb.FontSize = 10; cb.MinWidth = 0; }),
        _ => HeaderBadge(node.Name),
    };

    static Element HeaderBadge(string text) =>
        Border(
            (TextBlock(text) with { FontSize = 11, Weight = Microsoft.UI.Text.FontWeights.SemiBold })
                .Foreground("#ffffff")
        ) with
        {
            Background = Brush(Palette[3 % Palette.Count]),
            CornerRadius = 4,
            Padding = new Thickness(10, 4, 10, 4),
        };
}
