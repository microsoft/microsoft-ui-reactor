using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Collections;

class ItemsRepeaterPage : Component
{
    public override Element Render()
    {
        var items = Enumerable.Range(1, 20)
            .Select(i => $"Inventory item {i}")
            .ToList()
            .AsReadOnly();

        return ScrollView(VStack(16,
            PageHeader("ItemsRepeater", "Displays a repeated set of elements from a data source without adding its own scrolling."),
            SampleCard("Scrollable repeated rows",
                ScrollView(
                    ItemsRepeater(
                        items,
                        s => s,
                        (s, i) => Border(TextBlock($"{i + 1}. {s}"))
                            .Padding(8)
                            .Margin(0, 0, 0, 4)
                            .Background(Theme.SubtleFill)
                            .CornerRadius(4))
                ).Height(260),
                sourceCode: @"ScrollView(
    ItemsRepeater(items, s => s, (s, i) =>
        Border(TextBlock($""{i + 1}. {s}""))
            .Padding(8)
            .Margin(0, 0, 0, 4)
            .Background(Theme.SubtleFill)
            .CornerRadius(4))
).Height(260)"),
            SampleCard("Compact repeated badges",
                ScrollView(
                    ItemsRepeater(
                        items.Take(12).ToList().AsReadOnly(),
                        s => s,
                        (s, i) => HStack(8,
                            Border(TextBlock($"{i + 1}").Center().Foreground("#FFFFFF"))
                                .Background(Theme.Accent)
                                .Size(32, 32)
                                .CornerRadius(16),
                            TextBlock(s).VAlign(VerticalAlignment.Center))
                            .Padding(6))
                ).Height(220),
                sourceCode: @"ScrollView(
    ItemsRepeater(items, s => s, (s, i) =>
        HStack(8,
            Border(TextBlock($""{i + 1}"").Center().Foreground(""#FFFFFF""))
                .Background(Theme.Accent).Size(32, 32).CornerRadius(16),
            TextBlock(s)))
).Height(220)")
        ).Margin(36, 24, 36, 36));
    }
}
