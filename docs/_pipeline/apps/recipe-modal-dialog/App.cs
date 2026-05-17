using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<ModalDialogRecipeApp>("Modal Dialog Recipe", width: 420, height: 320
#if DEBUG
    , preview: true
#endif
);

class ModalDialogRecipeApp : Component
{
    public override Element Render() => Component<DeleteConfirmation>();
}

class DeleteConfirmation : Component
{
    public override Element Render()
    {
        // <snippet:state>
        var (open, setOpen) = UseState(false);
        var (deleted, setDeleted) = UseState(false);
        // </snippet:state>

        // <snippet:trigger>
        // The page renders normally; the modal is just another element
        // returned conditionally based on `open`.
        var page = VStack(12,
            TextBlock(deleted ? "Item deleted." : "1 item selected."),
            Button("Delete…", () => setOpen(true))
        ).Padding(20);
        // </snippet:trigger>

        // <snippet:modal>
        // Pair the dialog with a scrim (SmokeFill) so clicks outside the
        // dialog don't reach the page underneath. The focus trap lives on
        // the modal Border.
        Element modal = Border(
            VStack(16,
                Heading("Delete this item?"),
                TextBlock("This action cannot be undone.").Opacity(0.8),
                HStack(8,
                    Button("Cancel", () => setOpen(false)),
                    Button("Delete", () => { setDeleted(true); setOpen(false); })
                ).HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Right)
            ).Padding(20).Background("#FFFFFF").CornerRadius(8)
        ).Background("#80000000").Padding(40);
        // </snippet:modal>

        return open ? Group(page, modal) : page;
    }
}
