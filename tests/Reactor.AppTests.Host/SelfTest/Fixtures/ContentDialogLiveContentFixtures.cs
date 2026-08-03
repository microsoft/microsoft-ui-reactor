using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Shared probe for reaching an open <see cref="WinUI.ContentDialog"/>. An open
/// dialog is hosted in a popup for the XamlRoot, NOT under
/// <c>Harness.SearchRoot</c>, so <c>H.FindControl</c> cannot see it or anything
/// inside it — every dialog assertion has to start here.
/// </summary>
internal static class ContentDialogProbe
{
    /// <summary>Pumps render passes until a dialog with <paramref name="title"/> is showing.</summary>
    internal static async Task<WinUI.ContentDialog?> WaitForOpen(Harness h, string title, int timeoutMs = 2_000)
    {
        for (var elapsed = 0; elapsed <= timeoutMs; elapsed += 50)
        {
            await Harness.Render(elapsed == 0 ? 0 : 34);
            var dialog = FindOpen(h, title);
            if (dialog is not null) return dialog;
        }

        return null;
    }

    /// <summary>Pumps render passes until no dialog with <paramref name="title"/> is showing.</summary>
    internal static async Task<bool> WaitForClosed(Harness h, string title, int timeoutMs = 2_000)
    {
        for (var elapsed = 0; elapsed <= timeoutMs; elapsed += 50)
        {
            await Harness.Render(elapsed == 0 ? 0 : 34);
            if (FindOpen(h, title) is null) return true;
        }

        return FindOpen(h, title) is null;
    }

    internal static WinUI.ContentDialog? FindOpen(Harness h, string title)
    {
        var xamlRoot = (h.Window.Content as UIElement)?.XamlRoot
                       ?? ReactorApp.PrimaryWindow?.NativeWindow.Content?.XamlRoot;
        if (xamlRoot is null) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            // Popup.Child is not enumerated by VisualTreeHelper.GetChildrenCount,
            // so descend into it explicitly before recursing.
            if (popup.Child is DependencyObject child)
            {
                var found = Walk<WinUI.ContentDialog>(child, cd => cd.Title as string == title);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>Depth-first search inside an already-located dialog's realized visual tree.</summary>
    internal static T? Walk<T>(DependencyObject node, Func<T, bool> predicate) where T : DependencyObject
    {
        if (node is T match && predicate(match)) return match;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var hit = Walk(VisualTreeHelper.GetChild(node, i), predicate);
            if (hit is not null) return hit;
        }
        return null;
    }

    internal static WinUI.TextBlock? FindText(DependencyObject root, string text)
        => Walk<WinUI.TextBlock>(root, tb => tb.Text == text);

    internal static WinUI.Button? FindButton(DependencyObject root, string label)
        => Walk<WinUI.Button>(root, b => b.Content is string s && s == label);

    /// <summary>Invokes a button living inside the dialog subtree via its automation peer.</summary>
    internal static bool ClickButton(DependencyObject root, string label)
    {
        var btn = FindButton(root, label);
        if (btn is null || !btn.IsEnabled) return false;
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
        var invoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
            peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
        invoke.Invoke();
        return true;
    }

    /// <summary>
    /// Pumps render passes until the button is realized and enabled, then invokes it.
    /// A dialog's content presenter realizes over several dispatcher waves after the
    /// dialog object itself appears in the popup tree, so clicking straight after
    /// <see cref="WaitForOpen"/> can silently hit nothing. Returns whether it clicked.
    /// </summary>
    internal static async Task<bool> WaitAndClick(DependencyObject root, string label, int maxPasses = 25)
    {
        for (int i = 0; i < maxPasses; i++)
        {
            if (FindButton(root, label) is { IsEnabled: true }) return ClickButton(root, label);
            await Harness.Render();
        }
        return ClickButton(root, label);
    }
}

/// <summary>
/// Issue #1069 — a <c>ContentDialog</c>'s content used to be side-mounted once at
/// show time and never reconciled again, so state changes inside an open dialog
/// never reached the visual tree. The same missing placeholder→dialog
/// back-reference also meant a declared <c>IsOpen = false</c> could not close the
/// dialog (#948) and an unmounted owner leaked the whole open dialog.
///
/// These fixtures pin the live path: content re-renders while open, scalar props
/// track the element, the falling edge closes, closing unmounts the content, and
/// unmounting the owner tears everything down without raising OnClosed.
///
/// WinUI permits only one ContentDialog per XamlRoot, so every fixture closes its
/// dialog in a finally and waits for the popup to actually go away.
/// </summary>
public static class ContentDialogLiveContentFixtures
{
    private static int s_cleanupCount;
    private static int s_closedCount;

    /// <summary>Stateful dialog content — the thing #1069 said could never re-render.</summary>
    private sealed class DialogCounter : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                TextBlock($"dialog-count:{count}"),
                Button("Bump", () => setCount(count + 1))
            );
        }
    }

    private sealed class CleanupChild : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("cleanup-child");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  #1069 — a UseState update inside an OPEN dialog reaches the visuals.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_RerendersOpenContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("anchor"),
                ContentDialog("LiveContent", Component<DialogCounter>(), "OK") with { IsOpen = true }
            ));

            var dialog = await ContentDialogProbe.WaitForOpen(H, "LiveContent");
            H.Check("ContentDialogLive_Rerender_Opened", dialog is not null);
            if (dialog is null) return;

            try
            {
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "dialog-count:0") is not null);
                H.Check("ContentDialogLive_Rerender_InitialText",
                    ContentDialogProbe.FindText(dialog, "dialog-count:0") is not null);

                var clicked = await ContentDialogProbe.WaitAndClick(dialog, "Bump");
                H.Check("ContentDialogLive_Rerender_ClickLanded", clicked);
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "dialog-count:1") is not null);

                // The #1069 regression: before the fix the dialog kept showing
                // "dialog-count:0" forever because UpdateContentDialog dropped
                // the new content element without reconciling it.
                H.Check("ContentDialogLive_Rerender_TextAdvanced_#1069",
                    ContentDialogProbe.FindText(dialog, "dialog-count:1") is not null);
                H.Check("ContentDialogLive_Rerender_StaleTextGone_#1069",
                    ContentDialogProbe.FindText(dialog, "dialog-count:0") is null);

                // A second update proves the path stays live, not just first-flush.
                await ContentDialogProbe.WaitAndClick(dialog, "Bump");
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "dialog-count:2") is not null);
                H.Check("ContentDialogLive_Rerender_SecondUpdate_#1069",
                    ContentDialogProbe.FindText(dialog, "dialog-count:2") is not null);
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForClosed(H, "LiveContent");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scalar props declared on the element track it while the dialog is
    //  open. This is what the shipped `dialog-gated-primary` docs sample
    //  needs: a TextBox inside the dialog gating the dialog's own primary.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_SyncsLivePropsWhileOpen(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (ready, setReady) = ctx.UseState(false);
                return VStack(
                    TextBlock("anchor"),
                    ContentDialog(
                        "LiveProps",
                        VStack(
                            TextBlock(ready ? "ready" : "not-ready"),
                            Button("MakeReady", () => setReady(true))
                        ),
                        ready ? "Save" : "OK") with
                    {
                        IsOpen = true,
                        IsPrimaryButtonEnabled = ready,
                    }
                );
            });

            var dialog = await ContentDialogProbe.WaitForOpen(H, "LiveProps");
            H.Check("ContentDialogLive_Props_Opened", dialog is not null);
            if (dialog is null) return;

            try
            {
                H.Check("ContentDialogLive_Props_InitiallyDisabled", !dialog.IsPrimaryButtonEnabled);
                H.Check("ContentDialogLive_Props_InitialButtonText", dialog.PrimaryButtonText == "OK");
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "not-ready") is not null);
                H.Check("ContentDialogLive_Props_InitialContent",
                    ContentDialogProbe.FindText(dialog, "not-ready") is not null);

                var clicked = await ContentDialogProbe.WaitAndClick(dialog, "MakeReady");
                H.Check("ContentDialogLive_Props_ClickLanded", clicked);
                await Harness.WaitFor(() => dialog.IsPrimaryButtonEnabled);

                // Proves the click landed and the render reached the dialog, so
                // the property checks below are not vacuously passing on a
                // fixture that simply never re-rendered.
                H.Check("ContentDialogLive_Props_ContentReacted",
                    ContentDialogProbe.FindText(dialog, "ready") is not null);
                H.Check("ContentDialogLive_Props_EnabledFlipped_#1069", dialog.IsPrimaryButtonEnabled);
                H.Check("ContentDialogLive_Props_ButtonTextFlipped_#1069", dialog.PrimaryButtonText == "Save");
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForClosed(H, "LiveProps");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  #948 (ContentDialog half) — declaring IsOpen = false closes the dialog.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_ClosesOnIsOpenFalse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(true);
                return VStack(
                    TextBlock("anchor"),
                    ContentDialog("FallingEdge", Button("CloseMe", () => setOpen(false)), "OK") with { IsOpen = open }
                );
            });

            var dialog = await ContentDialogProbe.WaitForOpen(H, "FallingEdge");
            H.Check("ContentDialogLive_FallingEdge_Opened", dialog is not null);
            if (dialog is null) return;

            try
            {
                var clicked = await ContentDialogProbe.WaitAndClick(dialog, "CloseMe");
                H.Check("ContentDialogLive_FallingEdge_ClickLanded", clicked);
                var closed = await ContentDialogProbe.WaitForClosed(H, "FallingEdge");

                // Before the fix IsOpen=false was inert — the dialog stayed up.
                H.Check("ContentDialogLive_FallingEdge_Closed_#948", closed);
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForClosed(H, "FallingEdge");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Closing the dialog unmounts the content subtree that was mounted at
    //  open time — otherwise every open/close cycle leaks its cleanups.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_CloseUnmountsContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("anchor"),
                ContentDialog("CloseUnmount", Component<CleanupChild>(), "OK") with { IsOpen = true }
            ));

            var dialog = await ContentDialogProbe.WaitForOpen(H, "CloseUnmount");
            H.Check("ContentDialogLive_CloseUnmount_Opened", dialog is not null);
            if (dialog is null) return;

            H.Check("ContentDialogLive_CloseUnmount_NoCleanupWhileOpen", s_cleanupCount == 0);

            dialog.Hide();
            await ContentDialogProbe.WaitForClosed(H, "CloseUnmount");
            await Harness.WaitFor(() => s_cleanupCount == 1);

            H.Check("ContentDialogLive_CloseUnmount_CleanupRanExactlyOnce", s_cleanupCount == 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  A content element whose type changes cannot be updated in place: the
    //  stale subtree must be unmounted (not orphaned) and replaced.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_ContentTypeSwapReplacesSubtree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (swapped, setSwapped) = ctx.UseState(false);
                return VStack(
                    TextBlock("anchor"),
                    ContentDialog(
                        "TypeSwap",
                        swapped
                            ? TextBlock("swapped-content")
                            : (Element)VStack(
                                Component<CleanupChild>(),
                                Button("Swap", () => setSwapped(true))),
                        "OK") with { IsOpen = true }
                );
            });

            var dialog = await ContentDialogProbe.WaitForOpen(H, "TypeSwap");
            H.Check("ContentDialogLive_TypeSwap_Opened", dialog is not null);
            if (dialog is null) return;

            try
            {
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "cleanup-child") is not null);
                H.Check("ContentDialogLive_TypeSwap_InitialContent",
                    ContentDialogProbe.FindText(dialog, "cleanup-child") is not null);
                H.Check("ContentDialogLive_TypeSwap_NoCleanupBeforeSwap", s_cleanupCount == 0);

                var clicked = await ContentDialogProbe.WaitAndClick(dialog, "Swap");
                H.Check("ContentDialogLive_TypeSwap_ClickLanded", clicked);
                await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "swapped-content") is not null);

                H.Check("ContentDialogLive_TypeSwap_NewContentShown",
                    ContentDialogProbe.FindText(dialog, "swapped-content") is not null);
                H.Check("ContentDialogLive_TypeSwap_OldContentGone",
                    ContentDialogProbe.FindText(dialog, "cleanup-child") is null);
                H.Check("ContentDialogLive_TypeSwap_StaleSubtreeUnmounted", s_cleanupCount == 1);
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForClosed(H, "TypeSwap");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Unmounting the owner of an OPEN dialog hides it and tears down the
    //  side-mounted content — without raising OnClosed, which the app did
    //  not ask for and which would re-enter a torn-down tree.
    // ────────────────────────────────────────────────────────────────────
    internal class ContentDialog_UnmountTearsDownOpenDialog(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            s_closedCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    TextBlock("anchor"),
                    show
                        ? ContentDialog(
                            "Teardown",
                            VStack(
                                Component<CleanupChild>(),
                                Button("Drop", () => setShow(false))),
                            "OK") with
                        {
                            IsOpen = true,
                            OnClosed = _ => global::System.Threading.Interlocked.Increment(ref s_closedCount),
                        }
                        : (Element)TextBlock("(gone)")
                );
            });

            var dialog = await ContentDialogProbe.WaitForOpen(H, "Teardown");
            H.Check("ContentDialogLive_Teardown_Opened", dialog is not null);
            if (dialog is null) return;

            H.Check("ContentDialogLive_Teardown_NoCleanupWhileMounted", s_cleanupCount == 0);

            var clicked = await ContentDialogProbe.WaitAndClick(dialog, "Drop");
            H.Check("ContentDialogLive_Teardown_ClickLanded", clicked);
            var closed = await ContentDialogProbe.WaitForClosed(H, "Teardown");
            await Harness.WaitFor(() => s_cleanupCount == 1);

            // Proves the owner actually left the tree, so the teardown checks
            // below are not vacuously passing on a fixture that never unmounted.
            H.Check("ContentDialogLive_Teardown_OwnerUnmounted", H.FindText("(gone)") is not null);
            H.Check("ContentDialogLive_Teardown_DialogHidden", closed);
            H.Check("ContentDialogLive_Teardown_ContentCleanupRan", s_cleanupCount == 1);
            // Teardown is not a dismissal: OnClosed belongs to the user, not the
            // unmount. The handler clears the placeholder tag before hiding so
            // the tag-routed callback stays silent.
            H.Check("ContentDialogLive_Teardown_OnClosedNotRaised", s_closedCount == 0);

            // Never leave a dialog up — WinUI allows only one per XamlRoot, so a
            // leak here would fail (or crash) every later dialog fixture.
            ContentDialogProbe.FindOpen(H, "Teardown")?.Hide();
            await ContentDialogProbe.WaitForClosed(H, "Teardown");
        }
    }
}
