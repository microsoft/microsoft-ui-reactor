using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §1.7: the minimal smoke fixture called out in §10.1 item 10.
/// Mount a <see cref="DockManager"/> with a 2-pane layout, assert basic tree
/// shape, swap one pane, unmount.
///
/// Goal: verify the wrapper assembly mounts + updates + unmounts inside a
/// real ReactorHost without throwing — full functional coverage (drag,
/// drop, persistence) lives in the showcase and §1.9 human review.
/// </summary>
internal static class DockingSmokeFixtures
{
    internal class TwoPaneMountUpdateUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            // Register the docking element type with this host's reconciler.
            // (Same convention as XamlInterop.Register — call once at host init.)
            DockingXamlInterop.Register(host.Reconciler);

            // ── Phase 1: Mount with two panes side-by-side ─────────────
            var pane1 = new DockableContent(
                Title: "Solution Explorer",
                Content: TextBlock("solution-content"),
                Key: "tool:solution");
            var pane2 = new DockableContent(
                Title: "Properties",
                Content: TextBlock("properties-content"),
                Key: "tool:properties");

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[] { pane1, pane2 }),
            });
            await Harness.Render();

            // The vendored DockManager is reachable in the visual tree.
            var managers = H.FindAllControls<WinUIDock.DockManager>(_ => true);
            H.Check("DockSmoke_DockManagerMounted", managers.Count >= 1);

            // Its Panel should have 2 children (the two panes wrapped in groups).
            var manager = managers.FirstOrDefault();
            H.Check("DockSmoke_PanelHasChildren",
                manager?.Panel is { Children.Count: > 0 });
            H.Check("DockSmoke_PanelOrientation_Horizontal",
                manager?.Panel?.Orientation == Orientation.Horizontal);

            // Pane content rendered into the host ContentControls — assert the
            // text markers appear via the harness search.
            H.Check("DockSmoke_Pane1ContentRendered",
                H.FindText("solution-content") is not null);
            H.Check("DockSmoke_Pane2ContentRendered",
                H.FindText("properties-content") is not null);

            // ── Phase 2: Update — swap the second pane's content ──────
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        pane1,
                        pane2 with { Content = TextBlock("properties-updated") },
                    }),
            });
            await Harness.Render();

            H.Check("DockSmoke_PaneContentUpdated",
                H.FindText("properties-updated") is not null);
            H.Check("DockSmoke_PreviousContentReplaced",
                H.FindText("properties-content") is null);

            // ── Phase 3: Update — change orientation to vertical ──────
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Vertical,
                    new DockNode[]
                    {
                        pane1,
                        pane2 with { Content = TextBlock("properties-updated") },
                    }),
            });
            await Harness.Render();

            var manager2 = H.FindAllControls<WinUIDock.DockManager>(_ => true).FirstOrDefault();
            H.Check("DockSmoke_OrientationFlipped",
                manager2?.Panel?.Orientation == Orientation.Vertical);

            // ── Phase 4: Unmount — replace with a different element ───
            host.Mount(_ => TextBlock("docking-unmounted"));
            await Harness.Render();

            H.Check("DockSmoke_UnmountedCleanly",
                H.FindText("docking-unmounted") is not null);
            H.Check("DockSmoke_DockManagerGone",
                H.FindAllControls<WinUIDock.DockManager>(_ => true).Count == 0);
        }
    }

    /// <summary>
    /// Verifies that DockableContent.Key reconciliation preserves the pane's
    /// vendored Document instance across a structural update. Spec 045 §4.4
    /// "Keyed reconciliation contract: DockableContent.Key survives tab
    /// reorderings per spec 042."
    /// </summary>
    internal class KeyedPanePreservation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingXamlInterop.Register(host.Reconciler);

            // Mount two panes in a tab group.
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("A", TextBlock("body-a"), Key: "k:a"),
                    new DockableContent("B", TextBlock("body-b"), Key: "k:b"),
                }),
            });
            await Harness.Render();

            var managerBefore = H.FindAllControls<WinUIDock.DockManager>(_ => true).FirstOrDefault();
            var docBeforeA = FindDocumentByTitle(managerBefore, "A");
            var docBeforeB = FindDocumentByTitle(managerBefore, "B");
            H.Check("DockSmoke_Keyed_BothDocumentsCreated", docBeforeA is not null && docBeforeB is not null);

            // Reorder: B then A.
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("B", TextBlock("body-b"), Key: "k:b"),
                    new DockableContent("A", TextBlock("body-a"), Key: "k:a"),
                }),
            });
            await Harness.Render();

            var managerAfter = H.FindAllControls<WinUIDock.DockManager>(_ => true).FirstOrDefault();
            var docAfterA = FindDocumentByTitle(managerAfter, "A");
            var docAfterB = FindDocumentByTitle(managerAfter, "B");

            // The instances should be the SAME — the pane state map keyed by
            // DockableContent.Key preserves the vendored Document instances
            // across the rebuild (so Reactor element subtrees mounted into
            // their content hosts survive reorderings).
            H.Check("DockSmoke_Keyed_DocumentAInstance_Survived",
                ReferenceEquals(docBeforeA, docAfterA));
            H.Check("DockSmoke_Keyed_DocumentBInstance_Survived",
                ReferenceEquals(docBeforeB, docAfterB));

            host.Mount(_ => TextBlock("done"));
            await Harness.Render();
        }

        private static WinUIDock.Document? FindDocumentByTitle(WinUIDock.DockManager? manager, string title)
        {
            if (manager?.Panel is null) return null;
            return Walk(manager.Panel, title);

            static WinUIDock.Document? Walk(WinUIDock.DockContainer container, string title)
            {
                foreach (var child in container.Children)
                {
                    switch (child)
                    {
                        case WinUIDock.Document d when d.Title == title:
                            return d;
                        case WinUIDock.DockContainer inner:
                            var result = Walk(inner, title);
                            if (result is not null) return result;
                            break;
                    }
                }
                return null;
            }
        }
    }
}
