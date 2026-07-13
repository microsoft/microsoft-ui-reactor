using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Collections;

class SwipeControlPage : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("No swipe action invoked yet.");

        return ScrollView(VStack(16,
            PageHeader("SwipeControl", "Adds contextual actions that appear when an item is swiped."),
            SampleCard("Left and right actions",
                VStack(8,
                    SwipeControl(
                        Border(TextBlock("Swipe me left or right"))
                            .Padding(16)
                            .Background(Theme.CardBackground)
                            .WithBorder(Theme.DividerStroke),
                        leftItems: new[] { new SwipeItemData("Archive", () => { }) },
                        rightItems: new[] { new SwipeItemData("Delete", () => { }) }),
                    Caption("Swiping requires touch or pointer drag input.")
                ),
                sourceCode: @"SwipeControl(
    Border(TextBlock(""Swipe me left or right""))
        .Padding(16)
        .Background(Theme.CardBackground),
    leftItems: new[] { new SwipeItemData(""Archive"", () => { }) },
    rightItems: new[] { new SwipeItemData(""Delete"", () => { }) })"),
            SampleCard("Action status",
                VStack(8,
                    SwipeControl(
                        Border(TextBlock("Swipe to invoke an action"))
                            .Padding(16)
                            .Background(Theme.SubtleFill)
                            .CornerRadius(4),
                        leftItems: new[] { new SwipeItemData("Flag", () => setStatus("Flag action invoked.")) },
                        rightItems: new[] { new SwipeItemData("Remove", () => setStatus("Remove action invoked.")) }),
                    TextBlock(status).Foreground(Theme.SecondaryText)
                ),
                sourceCode: @"var (status, setStatus) = UseState(""No swipe action invoked yet."");

SwipeControl(
    Border(TextBlock(""Swipe to invoke an action"")).Padding(16),
    leftItems: new[] { new SwipeItemData(""Flag"", () => setStatus(""Flag action invoked."")) },
    rightItems: new[] { new SwipeItemData(""Remove"", () => setStatus(""Remove action invoked."")) })")
        ).Margin(36, 24, 36, 36));
    }
}
