using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Measures whether a <c>UseFocusTrap</c> attached to an in-tree container can
/// contain a <see cref="WinUI.ContentDialog"/>.
///
/// This exists because `focus-and-input-internals.md.dt` presents exactly that
/// shape as "the canonical use of UseFocusTrap and UseAnnounce together". The
/// trap cancels a focus move whose target is not a visual descendant of its
/// container (<c>UseFocusTrap.OnLosingFocus</c> → <c>IsDescendantOf</c>), and
/// an open dialog is side-mounted into a XamlRoot popup, so the question is
/// whether the documented pairing actually works or silently fights the dialog.
/// </summary>
internal class FocusTrapContentDialog_ContainmentProbe(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var host = H.CreateHost();
        FocusTrapHandle? trap = null;

        host.Mount(ctx =>
        {
            var (open, setOpen) = ctx.UseState(false);
            trap = ctx.UseFocusTrap(isActive: open);

            return VStack(8,
                Button("Delete…", () => setOpen(true)),
                Button("ForceClose", () => setOpen(false)),
                ContentDialog(
                    "Confirm delete",
                    TextBlock("Are you sure?"),
                    primaryButtonText: "Delete") with
                {
                    IsOpen = open,
                    CloseButtonText = "Cancel",
                }
            ).FocusTrap(trap);
        });

        await Harness.Render();
        H.Check("FocusTrapDialog_TrapCreated", trap is not null);

        H.ClickButton("Delete…");
        var dialog = await ContentDialogProbe.WaitForOpen(H, "Confirm delete");
        H.Check("FocusTrapDialog_DialogOpened", dialog is not null);
        if (dialog is null || trap is null) return;

        var container = trap.Container;
        H.Check("FocusTrapDialog_ContainerWired", container is not null);
        if (container is null) return;

        // The measurement: is the open dialog inside the trap's container?
        // If it is not, every focus move into the dialog is a move "outside
        // the trap" and OnLosingFocus cancels it.
        bool dialogInsideTrap = IsDescendant(dialog, container);
        Console.WriteLine($"# focustrap diag: dialogInsideTrap={dialogInsideTrap} " +
                          $"container={container.GetType().Name}");

        H.Check("FocusTrapDialog_DialogIsOutsideTrapContainer", !dialogInsideTrap);

        // Positive control for the containment walk itself: the button that
        // opened the dialog IS in the trap, so a walk that always returned
        // false would make the assertion above vacuous.
        var button = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Delete…");
        H.Check("FocusTrapDialog_WalkFindsInTreeChild",
            button is not null && IsDescendant(button, container));

        // Close through the controlled flag, not dialog.Hide(): Hide leaves
        // `open` true, so the trap stays active with a stale container and
        // keeps cancelling focus moves for every fixture that runs after this
        // one. Driving state deactivates the trap and unmounts the dialog.
        H.ClickButton("ForceClose");
        await ContentDialogProbe.WaitForNoneOpen(H);
        H.Check("FocusTrapDialog_CleanedUp", ContentDialogProbe.FindAnyOpen(H) is null);
    }

    private static bool IsDescendant(DependencyObject? node, DependencyObject root)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, root)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}
