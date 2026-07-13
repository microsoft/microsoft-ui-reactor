using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DialogsAndFlyouts;

class PopupPage : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (persistentOpen, setPersistentOpen) = UseState(false);

        return ScrollView(VStack(16,
            PageHeader("Popup", "Popup displays lightweight content above the current page."),

            SampleCard("Light-dismiss popup",
                VStack(8,
                    Button("Show popup", () => setOpen(true)),
                    Popup(
                        Border(VStack(8,
                            TextBlock("Hello from a Popup").SemiBold(),
                            Button("Close", () => setOpen(false))))
                            .Padding(16)
                            .Background(Theme.CardBackground)
                            .WithBorder(Theme.DividerStroke)
                            .CornerRadius(8),
                        open,
                        () => setOpen(false))
                        .IsLightDismissEnabled(),
                    Caption("Popup anchors to its placement in the tree.")),
                sourceCode: @"Button(""Show popup"", () => setOpen(true))

Popup(
    Border(VStack(8,
        TextBlock(""Hello from a Popup"").SemiBold(),
        Button(""Close"", () => setOpen(false))))
        .Padding(16)
        .Background(Theme.CardBackground)
        .WithBorder(Theme.DividerStroke)
        .CornerRadius(8),
    open,
    () => setOpen(false))
    .IsLightDismissEnabled()"),

            SampleCard("Popup without light dismiss",
                VStack(8,
                    Button("Show persistent popup", () => setPersistentOpen(true)),
                    Popup(
                        Border(VStack(8,
                            TextBlock("Use the button to close this popup.").SemiBold(),
                            Button("Close", () => setPersistentOpen(false))))
                            .Padding(16)
                            .Background(Theme.CardBackground)
                            .WithBorder(Theme.DividerStroke)
                            .CornerRadius(8),
                        persistentOpen,
                        () => setPersistentOpen(false))
                        .IsLightDismissEnabled(false),
                    Caption("Without light dismiss, the popup stays open until its close action runs.")),
                sourceCode: @"Button(""Show persistent popup"", () => setPersistentOpen(true))

Popup(
    Border(VStack(8,
        TextBlock(""Use the button to close this popup."").SemiBold(),
        Button(""Close"", () => setPersistentOpen(false))))
        .Padding(16)
        .Background(Theme.CardBackground)
        .WithBorder(Theme.DividerStroke)
        .CornerRadius(8),
    persistentOpen,
    () => setPersistentOpen(false))
    .IsLightDismissEnabled(false)")
        ).Margin(36, 24, 36, 36));
    }
}
