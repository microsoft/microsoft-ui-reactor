using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

class UniformGridPage : Component
{
    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("UniformGrid", "Arranges children into evenly sized cells."),
            SampleCard("Horizontal fill",
                UniformGrid(
                    Orientation.Horizontal,
                    Enumerable.Range(1, 9).Select(i => Cell(i, Theme.Accent)).ToArray()),
                sourceCode: @"UniformGrid(
    Orientation.Horizontal,
    Enumerable.Range(1, 9).Select(i =>
        Border(TextBlock($""{i}"").Center().Foreground(""#FFFFFF""))
            .Background(Theme.Accent)
            .Size(60, 60)
            .Margin(4)
            .CornerRadius(4)).ToArray())"),
            SampleCard("Vertical fill",
                UniformGrid(
                    Orientation.Vertical,
                    Enumerable.Range(1, 9).Select(i => Cell(i, "#5B6ABF")).ToArray()),
                sourceCode: @"UniformGrid(
    Orientation.Vertical,
    Enumerable.Range(1, 9).Select(i =>
        Border(TextBlock($""{i}"").Center().Foreground(""#FFFFFF""))
            .Background(""#5B6ABF"")
            .Size(60, 60)
            .Margin(4)
            .CornerRadius(4)).ToArray())")
        ).Margin(36, 24, 36, 36));
    }

    static Element Cell(int number, ThemeRef background) =>
        Border(TextBlock($"{number}").Center().Foreground("#FFFFFF"))
            .Background(background)
            .Size(60, 60)
            .Margin(4)
            .CornerRadius(4);

    static Element Cell(int number, string background) =>
        Border(TextBlock($"{number}").Center().Foreground("#FFFFFF"))
            .Background(background)
            .Size(60, 60)
            .Margin(4)
            .CornerRadius(4);
}
