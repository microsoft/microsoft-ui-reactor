using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Hosts the COMPLETE WinUI TableViewSamples gallery (36 pages + NavigationView shell, compiled in the
// TableViewSamples.Embedded library against the projected native Advanced TableView) live inside the
// Reactor gallery's TableView tab via XamlHostElement. The shell injects the satellite control's resource
// closure (AdvancedStyles) scoped to its subtree so every declarative <muxc:TableView> renders; the
// control's localized strings are merged into the app PRI by the post-build step.
class TableViewDemo : Component
{
    public override Element Render() =>
        new XamlHostElement(Factory: () => new TableViewSamples.SampleShell
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 620,
        }) { TypeKey = "TableViewSamples.SampleShell" };
}
