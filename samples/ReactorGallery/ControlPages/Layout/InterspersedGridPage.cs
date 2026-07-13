using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

class InterspersedGridPage : Component
{
    public override Element Render()
    {
        var horizontalPanels = new Element[]
        {
            Panel("1x", Theme.Accent),
            Panel("2x", "#5B6ABF"),
            Panel("1x", "#107C10"),
        };

        var verticalPanels = new Element[]
        {
            Panel("Top", Theme.Accent),
            Panel("Middle", "#5B6ABF"),
            Panel("Bottom", "#107C10"),
        };

        return ScrollView(VStack(16,
            PageHeader("InterspersedGrid", "Lays out proportional items with generated separators between them."),
            SampleCard("Horizontal proportions",
                InterspersedGrid(
                    Orientation.Horizontal,
                    horizontalPanels,
                    new[] { 1.0, 2.0, 1.0 },
                    8,
                    i => Border(Empty()).Width(2).Background(Theme.DividerStroke))
                    .Height(160),
                sourceCode: @"InterspersedGrid(
    Orientation.Horizontal,
    new Element[] { panelA, panelB, panelC },
    new[] { 1.0, 2.0, 1.0 },
    8,
    i => Border(Empty()).Width(2).Background(Theme.DividerStroke))
    .Height(160)"),
            SampleCard("Vertical proportions",
                InterspersedGrid(
                    Orientation.Vertical,
                    verticalPanels,
                    new[] { 1.0, 1.5, 1.0 },
                    8,
                    i => Border(Empty()).Height(2).Background(Theme.DividerStroke))
                    .Height(240),
                sourceCode: @"InterspersedGrid(
    Orientation.Vertical,
    panels,
    new[] { 1.0, 1.5, 1.0 },
    8,
    i => Border(Empty()).Height(2).Background(Theme.DividerStroke))
    .Height(240)")
        ).Margin(36, 24, 36, 36));
    }

    static Element Panel(string text, ThemeRef background) =>
        Border(TextBlock(text).Center().Foreground("#FFFFFF").SemiBold())
            .Background(background)
            .CornerRadius(4);

    static Element Panel(string text, string background) =>
        Border(TextBlock(text).Center().Foreground("#FFFFFF").SemiBold())
            .Background(background)
            .CornerRadius(4);
}
