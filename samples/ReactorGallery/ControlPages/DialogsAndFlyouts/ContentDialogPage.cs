using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DialogsAndFlyouts;

class ContentDialogPage : Component
{
    public override Element Render()
    {
        var (showBasic, setShowBasic) = UseState(false);
        var (showConfirm, setShowConfirm) = UseState(false);
        var (result, setResult) = UseState("(none)");
        var (showLive, setShowLive) = UseState(false);
        var (count, setCount) = UseState(0);
        var (note, setNote) = UseState("");
        var (dialogMounted, setDialogMounted) = UseState(true);

        return ScrollView(
            VStack(16,
                PageHeader("ContentDialog",
                    "A modal dialog box that displays content and action buttons."),

                SampleCard("Basic Dialog",
                    VStack(8,
                        Button("Show Dialog", () => setShowBasic(true)),
                        ContentDialog("Welcome", TextBlock("Thank you for using this app!"), "OK") with
                        {
                            IsOpen = showBasic,
                            OnClosed = _ => setShowBasic(false),
                        }
                    ),
                    @"Button(""Show Dialog"", () => setShowBasic(true)),
ContentDialog(""Welcome"", TextBlock(""Thank you!""), ""OK"") with {
    IsOpen = showBasic,
    OnClosed = _ => setShowBasic(false),
}"),

                SampleCard("Confirmation Dialog",
                    VStack(8,
                        Button("Delete Item", () => setShowConfirm(true)),
                        TextBlock($"Last result: {result}").Foreground(Theme.SecondaryText),
                        ContentDialog("Confirm Delete",
                            TextBlock("Are you sure you want to delete this item? This action cannot be undone."),
                            "Delete") with
                        {
                            IsOpen = showConfirm,
                            SecondaryButtonText = "Cancel",
                            OnClosed = r =>
                            {
                                setResult(r.ToString());
                                setShowConfirm(false);
                            },
                        }
                    ),
                    @"ContentDialog(""Confirm Delete"",
    TextBlock(""Are you sure?""), ""Delete"") with {
    IsOpen = showConfirm,
    SecondaryButtonText = ""Cancel"",
    OnClosed = r => { setResult(r.ToString()); setShowConfirm(false); },
}"),

                SampleCard("Live content while open",
                    VStack(8,
                        HStack(8,
                            Button("Show Live Dialog", () =>
                            {
                                setCount(0);
                                setNote("");
                                setDialogMounted(true);
                                setShowLive(true);
                            }),
                            Button("Restore dialog element", () => setDialogMounted(true))
                        ),
                        TextBlock($"Page state — count: {count}, note: \"{note}\"")
                            .Foreground(Theme.SecondaryText),
                        dialogMounted
                            ? (Element)(ContentDialog($"Clicked {count} time(s)",
                                VStack(8,
                                    TextBlock($"This line re-renders in place: {count}"),
                                    Button("+1", () => setCount(count + 1)),
                                    TextBox(note, setNote,
                                        placeholderText: "Type here, then press +1 — focus and caret survive"),
                                    Button("Close by setting IsOpen = false", () => setShowLive(false)),
                                    Button("Unmount the dialog element", () => setDialogMounted(false))
                                ),
                                "Save") with
                            {
                                IsOpen = showLive,
                                CloseButtonText = "Dismiss",
                                IsPrimaryButtonEnabled = count > 0,
                                OnClosed = _ => setShowLive(false),
                            })
                            : (Element)TextBlock("Dialog element is unmounted. \"Restore dialog element\" brings it back — and it reopens, because IsOpen is still true.")
                                .Foreground(Theme.SecondaryText)
                    ),
                    @"// State lives in the page, but the dialog content is reconciled
// in place while the dialog is open — no remount, no lost focus.
ContentDialog($""Clicked {count} time(s)"",
    VStack(8,
        TextBlock($""This line re-renders in place: {count}""),
        Button(""+1"", () => setCount(count + 1)),
        TextBox(note, setNote),
        Button(""Close"", () => setShowLive(false))),
    ""Save"") with
{
    IsOpen = showLive,
    IsPrimaryButtonEnabled = count > 0,   // tracks state live
    OnClosed = _ => setShowLive(false),
}")
            ).Margin(36, 24, 36, 36)
        );
    }
}
