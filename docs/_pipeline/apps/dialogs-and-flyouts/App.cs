using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<DialogsAndFlyoutsApp>("Dialogs and Flyouts", width: 640, height: 960
);

// <snippet:basic-dialog>
class BasicDialogDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);

        return VStack(8,
            SubHeading("Basic ContentDialog"),
            Button("Show dialog", () => setOpen(true)),
            // Dialog lives in the tree at all times. IsOpen controls
            // visibility; OnClosed flips it back when the user dismisses.
            ContentDialog(
                "Welcome",
                TextBlock("Thank you for trying Reactor."),
                primaryButtonText: "OK") with
            {
                IsOpen = open,
                OnClosed = _ => setOpen(false),
            }
        ).Padding(24);
    }
}
// </snippet:basic-dialog>

// <snippet:confirm-dialog>
class ConfirmDialogDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (result, setResult) = UseState("(none)");

        return VStack(8,
            SubHeading("Confirmation with three buttons"),
            Button("Delete item…", () => setOpen(true)),
            TextBlock($"Last result: {result}").Opacity(0.6),
            ContentDialog(
                "Delete this item?",
                TextBlock("This action cannot be undone."),
                primaryButtonText: "Delete") with
            {
                IsOpen = open,
                SecondaryButtonText = "Cancel",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                OnClosed = r =>
                {
                    setResult(r.ToString());
                    setOpen(false);
                },
            }
        ).Padding(24);
    }
}
// </snippet:confirm-dialog>

// <snippet:dialog-gated-primary>
class DialogGatedPrimaryDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (name, setName) = UseState("");

        return VStack(8,
            SubHeading("Primary disabled until input is valid"),
            Button("Rename file…", () => setOpen(true)),
            ContentDialog(
                "Rename file",
                VStack(8,
                    TextBox(name, setName, placeholderText: "untitled.txt", header: "New filename")
                        .Width(280)),
                primaryButtonText: "Rename") with
            {
                IsOpen = open,
                SecondaryButtonText = "Cancel",
                // .IsPrimaryButtonEnabled drives the inline primary
                // disabled state without taking it out of tab order.
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name),
                OnClosed = _ => setOpen(false),
            }
        ).Padding(24);
    }
}
// </snippet:dialog-gated-primary>

// <snippet:menu-flyout>
class MenuFlyoutDemo : Component
{
    public override Element Render()
    {
        var (action, setAction) = UseState("(none)");

        return VStack(8,
            SubHeading("MenuFlyout — right-click or button-click"),
            MenuFlyout(
                Button("Edit ▾"),
                MenuItem("Cut",   () => setAction("Cut"),   icon: "Cut"),
                MenuItem("Copy",  () => setAction("Copy"),  icon: "Copy"),
                MenuItem("Paste", () => setAction("Paste"), icon: "Paste"),
                MenuSeparator(),
                MenuSubItem("Format",
                    ToggleMenuItem("Bold", isChecked: false),
                    ToggleMenuItem("Italic", isChecked: true)),
                MenuSeparator(),
                MenuItem("Delete…", () => setAction("Delete"))
            ),
            TextBlock($"Last action: {action}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:menu-flyout>

// <snippet:command-bar-flyout>
class CommandBarFlyoutDemo : Component
{
    public override Element Render()
    {
        var (action, setAction) = UseState("(none)");

        return VStack(8,
            SubHeading("CommandBarFlyout"),
            CommandBarFlyout(
                Button("Selection ▾"),
                primaryCommands: new AppBarItemBase[]
                {
                    AppBarButton("Cut",   () => setAction("Cut"),   icon: "Cut"),
                    AppBarButton("Copy",  () => setAction("Copy"),  icon: "Copy"),
                    AppBarButton("Paste", () => setAction("Paste"), icon: "Paste"),
                },
                secondaryCommands: new AppBarItemBase[]
                {
                    AppBarButton("Select All", () => setAction("Select All")),
                    AppBarButton("Find",       () => setAction("Find")),
                }),
            TextBlock($"Last action: {action}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:command-bar-flyout>

// <snippet:popup>
class PopupDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);

        // Popup is a free-form positioned surface. Use it for overlays
        // that aren't dialogs or flyouts — color pickers, in-place
        // editors, custom tooltips.
        var popupContent = Border(
            VStack(8,
                TextBlock("This is a Popup.").Bold(),
                TextBlock("Click outside to dismiss.")
            ).Padding(12)
        ).Background(Theme.SolidBackground).WithBorder(Theme.ControlStroke).CornerRadius(6);

        return VStack(8,
            SubHeading("Popup"),
            Button(open ? "Hide popup" : "Show popup",
                () => setOpen(!open))
                .AutomationName(open ? "Hide popup" : "Show popup"),
            Popup(popupContent, isOpen: open,
                onClosed: () => setOpen(false))
                .IsLightDismissEnabled()
                .Offset(120, 0)
        ).Padding(24);
    }
}
// </snippet:popup>

// <snippet:commanding-integration>
class CommandingIntegrationDemo : Component
{
    public override Element Render()
    {
        // One Command drives the button, the menu item, and (via
        // .Accelerator) Ctrl+S. The same Command can light up an
        // AppBarButton in a CommandBarFlyout too — same declaration,
        // three surfaces.
        var (saved, setSaved) = UseState(false);

        var save = new Command
        {
            Label = "Save",
            Execute = () => setSaved(true),
            CanExecute = !saved,
            Icon = SymbolIcon("Save"),
        };

        return VStack(8,
            SubHeading("One Command, two surfaces"),
            Button(save),                          // primary CTA
            MenuFlyout(
                Button("File ▾"),
                MenuItem(save),                    // menu duplicate
                MenuSeparator(),
                MenuItem("Reset", () => setSaved(false))),
            TextBlock(saved ? "Saved." : "Unsaved changes.")
                .Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:commanding-integration>

// <snippet:teaching-tip-target>
class TeachingTipTargetDemo : Component
{
    public override Element Render()
    {
        var (show, setShow) = UseState(false);

        // ElementRef<T> converts implicitly to the non-generic ElementRef the
        // TeachingTip target: parameter takes. UseElementRef is an extension on
        // Component, so call it through `this.`.
        var target = this.UseElementRef<FrameworkElement>();

        return HStack(16,
            Border(
                Button("Show anchored tip", () => setShow(true))
                    .Ref(target)),
            Border(
                TeachingTip(
                    "Cross-container target",
                    "This TeachingTip is declared in a different subtree.",
                    target: target) with
                {
                    IsOpen = show,
                    OnClosed = () => setShow(false),
                })
        ).Padding(24);
    }
}
// </snippet:teaching-tip-target>

// <snippet:popup-focus-trap>
class ModalPopupDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);

        // UseFocusTrap is an extension on Component (hence `this.`) and takes the
        // active flag; attach the handle with .FocusTrap(...).
        var trap = this.UseFocusTrap(open);

        return VStack(8,
            SubHeading("Popup that behaves modally"),
            Button("Open modal popup", () => setOpen(true)),
            Popup(
                Border(
                    VStack(8,
                        TextBlock("Focus stays inside this popup."),
                        Button("Close", () => setOpen(false))
                    ).Padding(16)
                ).FocusTrap(trap)
                 .Semantics(role: "dialog"),
                isOpen: open,
                onClosed: () => setOpen(false))
        ).Padding(24);
    }
}
// </snippet:popup-focus-trap>

// <snippet:dialog-async-command>
class DialogAsyncCommandDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (deleted, setDeleted) = UseState(false);

        // UseCommand wraps ExecuteAsync into Execute and tracks IsExecuting,
        // so IsEnabled goes false for the duration of the async action.
        var delete = UseCommand(new Command
        {
            Label = "Delete",
            ExecuteAsync = async () =>
            {
                await Task.Delay(400);
                setDeleted(true);
                setOpen(false);
            },
            CanExecute = !deleted,
        });

        return VStack(8,
            SubHeading("Dialog-driven async command"),
            Button("Delete item…", () => setOpen(true)),
            TextBlock(deleted ? "Deleted." : "Not deleted.").Opacity(0.6),
            ContentDialog(
                "Delete this item?",
                TextBlock("This action cannot be undone."),
                primaryButtonText: "Delete") with
            {
                IsOpen = open,
                SecondaryButtonText = "Cancel",
                IsPrimaryButtonEnabled = delete.IsEnabled,
                OnClosed = r =>
                {
                    if (r == ContentDialogResult.Primary && delete.IsEnabled)
                        delete.Execute?.Invoke();
                    else
                        setOpen(false);
                },
            }
        ).Padding(24);
    }
}
// </snippet:dialog-async-command>

record ContextMenuRow(int Id, string Name);

class RowContextMenuDemo : Component
{
    private static readonly ContextMenuRow[] Items =
    [
        new(1, "Quarterly plan"),
        new(2, "Launch checklist"),
        new(3, "Budget review")
    ];

    public override Element Render()
    {
        var items = Items;
        var deleteCommand = new Command<ContextMenuRow>
        {
            Label = "Delete",
            Execute = _ => { },
        };
        var renameCommand = new Command<ContextMenuRow>
        {
            Label = "Rename",
            Execute = _ => { },
        };
        var propertiesCommand = new Command<ContextMenuRow>
        {
            Label = "Properties",
            Execute = _ => { },
        };

        Element RowContent(ContextMenuRow item) =>
            HStack(8, TextBlock(item.Name));

        return
            // <snippet:right-click-list-row>
            ListView(items, item => item.Id.ToString(), (item, _) =>
                MenuFlyout(
                    RowContent(item),
                    MenuItem(deleteCommand, item),
                    MenuItem(renameCommand, item),
                    MenuSeparator(),
                    MenuItem(propertiesCommand, item)))
            // </snippet:right-click-list-row>
            ;
    }
}

class DialogsAndFlyoutsApp : Component
{
    public override Element Render() => ScrollView(
        VStack(24,
            Heading("Dialogs and Flyouts"),
            Component<BasicDialogDemo>(),
            Component<ConfirmDialogDemo>(),
            Component<DialogGatedPrimaryDemo>(),
            Component<MenuFlyoutDemo>(),
            Component<CommandBarFlyoutDemo>(),
            Component<PopupDemo>(),
            Component<CommandingIntegrationDemo>(),
            Component<TeachingTipTargetDemo>(),
            Component<ModalPopupDemo>(),
            Component<DialogAsyncCommandDemo>(),
            Component<RowContextMenuDemo>()
        ).Padding(24)
    );
}
