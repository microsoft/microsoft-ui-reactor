using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DialogsAndFlyouts;

class TeachingTipPage : Component
{
    public override Element Render()
    {
        var (showTip, setShowTip) = UseState(false);
        var (showTargetedTip, setShowTargetedTip) = UseState(false);
        var crossContainerTarget = this.UseElementRef<FrameworkElement>();

        return ScrollView(
            VStack(16,
                PageHeader("TeachingTip",
                    "A notification flyout for guiding users through features."),

                SampleCard("Basic TeachingTip",
                    VStack(8,
                        Button("Show Teaching Tip", () => setShowTip(true)),
                        (TeachingTip("Welcome!", "This is a helpful tip to guide you through this feature.") with
                        {
                            IsOpen = showTip,
                            OnClosed = () => setShowTip(false),
                        })
                    ),
                    @"Button(""Show Tip"", () => setShow(true)),
TeachingTip(""Welcome!"", ""Helpful guidance text."") with {
    IsOpen = show,
    OnClosed = () => setShow(false),
}"),

                SampleCard("TeachingTip (Title Only)",
                    TeachingTip("Did you know?", "You can customize the title bar!"),
                    @"TeachingTip(""Did you know?"", ""You can customize the title bar!"")"),

                SampleCard("TeachingTip targeting another subtree",
                    HStack(16,
                        Border(
                            VStack(8,
                                TextBlock("Target container"),
                                Button("Show anchored tip", () => setShowTargetedTip(true))
                                    .Ref(crossContainerTarget))
                        ).Padding(16),
                        Border(
                            VStack(8,
                                TextBlock("Tip container"),
                                TeachingTip(
                                    "Cross-container target",
                                    "This TeachingTip is declared in a different subtree from its target.",
                                    target: crossContainerTarget) with
                                {
                                    IsOpen = showTargetedTip,
                                    OnClosed = () => setShowTargetedTip(false),
                                })
                        ).Padding(16)),
                    @"var target = UseElementRef<FrameworkElement>();

HStack(
    Border(Button(""Show anchored tip"", () => setShow(true)).Ref(target)),
    Border(TeachingTip(""Cross-container target"", target: target) with
    {
        IsOpen = show,
        OnClosed = () => setShow(false),
    }));")
            ).Margin(36, 24, 36, 36)
        );
    }
}
