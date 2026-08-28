using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<FocusInputInternalsApp>(
    title: "Focus and Input Internals",
    width: 900,
    height: 600);

class FocusInputInternalsApp : Component
{
    public override Element Render() => Component<DialogAnnouncementDemo>();
}

class DialogAnnouncementDemo : Component
{
    // <snippet:dialog-announcement>
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var announce = UseAnnounce();

        return VStack(
            announce.Region,
            Button("Delete…", () => setOpen(true)),
            ContentDialog(
                "Confirm delete",
                TextBlock("Are you sure?"),
                primaryButtonText: "Delete")
            with
            {
                IsOpen = open,
                CloseButtonText = "Cancel",
                OnOpened = () => announce.Announce("Confirm delete dialog opened", assertive: true),
                OnClosed = result =>
                {
                    if (result == ContentDialogResult.Primary) Delete();
                    setOpen(false);
                },
            }
        );
    }
    // </snippet:dialog-announcement>

    private static void Delete()
    {
    }
}

class InlineFocusTrapDemo : Component
{
    public override Element Render()
    {
        // <snippet:inline-focus-trap>
        var (open, setOpen) = UseState(false);
        var trap = this.UseFocusTrap(isActive: open);

        return VStack(
            Button("Edit…", () => setOpen(true)),
            open
                ? Border(VStack(
                      TextBlock("Inline editor"),
                      Button("Close", () => setOpen(false))))
                    .Background(Theme.CardBackground)
                    .FocusTrap(trap)
                : Empty()
        );
        // </snippet:inline-focus-trap>
    }
}
