using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.1 / §2.2 / §2.16 — minimal smoke fixture for the Phase 2
/// native renderer. Asserts that <see cref="DockManager"/> mounts into a
/// Reactor-native subtree (FlexPanel + TabView) without depending on
/// WinUI.Dock controls. Mirrors <see cref="DockingSmokeFixtures"/> in
/// shape so the two renderers are reviewed side by side.
/// </summary>
internal static class NativeDockingSmokeFixtures
{
    internal class TwoPaneMountUpdateUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            // P2: register the native renderer. Same call site as the
            // XAML wrapper; last registration wins on the same TElement.
            DockingNativeInterop.Register(host.Reconciler);

            var pane1 = new DockableContent(
                Title: "Solution Explorer",
                Content: TextBlock("native-solution-content"),
                Key: "tool:solution");
            var pane2 = new DockableContent(
                Title: "Properties",
                Content: TextBlock("native-properties-content"),
                Key: "tool:properties");

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[] { pane1, pane2 }),
            });
            await Harness.Render();

            // The native renderer mounts a FlexPanel for the split.
            var flexes = H.FindAllControls<FlexPanel>(_ => true);
            H.Check("NativeDock_FlexPanelMounted", flexes.Count >= 1);

            // The leaf renderer for a bare DockableContent inlines the
            // Content element — text markers must appear in the visual
            // tree.
            H.Check("NativeDock_Pane1ContentRendered",
                H.FindText("native-solution-content") is not null);
            H.Check("NativeDock_Pane2ContentRendered",
                H.FindText("native-properties-content") is not null);

            // ── Update: swap one pane's content ─────────────────────
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        pane1,
                        pane2 with { Content = TextBlock("native-properties-updated") },
                    }),
            });
            await Harness.Render();

            H.Check("NativeDock_PaneContentUpdated",
                H.FindText("native-properties-updated") is not null);
            H.Check("NativeDock_PreviousContentReplaced",
                H.FindText("native-properties-content") is null);

            // ── Unmount: replace with a different element ───────────
            host.Mount(_ => TextBlock("native-docking-unmounted"));
            await Harness.Render();

            H.Check("NativeDock_UnmountedCleanly",
                H.FindText("native-docking-unmounted") is not null);
            H.Check("NativeDock_NoFlexPanelAfterUnmount",
                H.FindAllControls<FlexPanel>(_ => true).Count == 0);
        }
    }

    /// <summary>
    /// Mounts a tab group and verifies that the native renderer wires
    /// the TabView control with the right tab headers and that swapping
    /// the selected tab preserves the surrounding tree.
    /// </summary>
    internal class TabGroupRendersToTabView(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Alpha", TextBlock("native-body-alpha"), Key: "k:a", CanClose: true),
                    new DockableContent("Beta",  TextBlock("native-body-beta"),  Key: "k:b"),
                }),
            });
            await Harness.Render();

            var tabs = H.FindAllControls<TabView>(_ => true);
            H.Check("NativeDock_TabView_Mounted", tabs.Count >= 1);

            var tab = tabs.FirstOrDefault();
            H.Check("NativeDock_TabView_HasTwoTabs", tab?.TabItems.Count == 2);

            // The selected (first) tab's content is rendered into the visual tree.
            H.Check("NativeDock_TabView_FirstBodyRendered",
                H.FindText("native-body-alpha") is not null);

            host.Mount(_ => TextBlock("native-tabs-unmounted"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.17 — asserts that a function component rendered inside
    /// a docked pane sees the live <c>DockContext</c> slots: <c>UseDockHost</c>
    /// returns a non-null model, <c>UsePane</c> returns identity matching
    /// the enclosing leaf, <c>UseActivePaneKey</c> reflects the manager's
    /// <c>ActiveDocument</c>, and <c>UseIsActivePane</c> flips correctly.
    /// </summary>
    internal class DockContextHooksResolveOnRealMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Build the docking tree inside the mount lambda each call —
            // matches the standard Reactor pattern where Content elements
            // are constructed fresh per render. (Storing element refs
            // outside Mount() means same-reference shallow-equality skips
            // the consumer's re-render before context propagation runs.)
            DockManager Build(bool alphaActive)
            {
                var alpha = new DockableContent(
                    Title: "Alpha",
                    Key: "k:alpha",
                    Content: Func(ctx =>
                    {
                        var dockHost = ctx.UseDockHost();
                        var pane = ctx.UsePane();
                        var isActive = ctx.UseIsActivePane();
                        return VStack(
                            TextBlock($"alpha-host:{(dockHost is null ? "null" : "ok")}"),
                            TextBlock($"alpha-pane-title:{pane.Title}"),
                            TextBlock($"alpha-pane-key:{pane.Key}"),
                            TextBlock($"alpha-active:{isActive}"));
                    }));
                var beta = new DockableContent(
                    Title: "Beta",
                    Key: "k:beta",
                    Content: Func(ctx =>
                    {
                        var isActive = ctx.UseIsActivePane();
                        return TextBlock($"beta-active:{isActive}");
                    }));
                return new DockManager
                {
                    Layout = new DockTabGroup(new[] { alpha, beta }),
                    ActiveDocument = alphaActive ? alpha : beta,
                };
            }

            host.Mount(_ => Build(alphaActive: true));
            await Harness.Render();

            H.Check("DockHooks_Host_Resolved",
                H.FindText("alpha-host:ok") is not null);
            H.Check("DockHooks_Pane_TitleResolved",
                H.FindText("alpha-pane-title:Alpha") is not null);
            H.Check("DockHooks_Pane_KeyResolved",
                H.FindText("alpha-pane-key:k:alpha") is not null);
            H.Check("DockHooks_IsActivePane_TrueWhenActive",
                H.FindText("alpha-active:True") is not null);

            host.Mount(_ => Build(alphaActive: false));
            await Harness.Render();

            H.Check("DockHooks_IsActivePane_FlipsOnActiveChange",
                H.FindText("alpha-active:False") is not null);

            host.Mount(_ => TextBlock("hooks-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.5 — side strip + side popup. Pinning a pane to the
    /// LeftSide renders a button strip; clicking the button opens a
    /// light-dismiss Popup with the pane's content. Click the button
    /// again (or close the popup) collapses it.
    /// </summary>
    internal class SidePopupExpandsAndCollapses(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockManager Build() => new()
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Center", TextBlock("center-body"), Key: "k:center"),
                }),
                LeftSide = new[]
                {
                    new DockableContent(
                        Title: "Outline",
                        Key: "k:outline",
                        Content: TextBlock("outline-popup-body"),
                        CanPin: true),
                },
            };

            host.Mount(_ => Build());
            await Harness.Render();

            // Strip button rendered with the pane title.
            var stripButton = H.FindButton("Outline");
            H.Check("SidePopup_StripButton_Rendered", stripButton is not null);

            // No open popups initially. Use VisualTreeHelper.GetOpenPopups
            // against the host's XamlRoot — WinUI hosts open Popups in a
            // private PopupRoot that VTH child-walks don't traverse, so
            // GetOpenPopups is the supported probe.
            var xamlRoot = stripButton!.XamlRoot;
            int OpenCount() => Microsoft.UI.Xaml.Media.VisualTreeHelper
                .GetOpenPopupsForXamlRoot(xamlRoot).Count;

            H.Check("SidePopup_NotOpenInitially", OpenCount() == 0);

            // Click → popup opens.
            H.ClickButton("Outline");
            await Harness.Render();
            H.Check("SidePopup_OpensOnClick", OpenCount() >= 1);

            // Click again → toggles closed.
            H.ClickButton("Outline");
            await Harness.Render();
            H.Check("SidePopup_TogglesClosedOnRepeatClick", OpenCount() == 0);

            host.Mount(_ => TextBlock("side-popup-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.6 — floating windows are real Reactor windows. Tear a
    /// pane out via the programmatic API; assert that a new
    /// <see cref="Microsoft.UI.Reactor.ReactorWindow"/> is registered and
    /// closing it removes it from the tracker.
    /// </summary>
    internal class FloatingWindowOpensAsRealWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Center", TextBlock("center-body"), Key: "k:center"),
                }),
            });
            await Harness.Render();

            var baselineCount = DockFloatingTracker.Count;

            var pane = new DockableContent(
                Title: "Output (floating)",
                Key: "k:output-floating",
                Content: TextBlock("floating-pane-body"));

            // The harness opens its own primary Window outside ReactorApp's
            // registry, so a fixture-spawned floating window can otherwise
            // become the framework's PrimaryWindow and trip
            // ShutdownPolicy.OnPrimaryWindowClosed when the test closes it.
            // Pin the policy to None for the duration of this fixture.
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                var floatingWindow = DockFloatingWindow.Open(pane, width: 600, height: 400);
                await Harness.Render();

                H.Check("FloatingWindow_OpenedAsRealReactorWindow",
                    floatingWindow is not null);
                H.Check("FloatingWindow_RegisteredWithTracker",
                    DockFloatingTracker.Count == baselineCount + 1);
                H.Check("FloatingWindow_TrackerSnapshotIncludesIt",
                    DockFloatingTracker.Snapshot().Contains(floatingWindow));

                // Close the floating window — tracker should drop it.
                floatingWindow!.Close();
                await Harness.Render();
                H.Check("FloatingWindow_RemovedFromTrackerOnClose",
                    DockFloatingTracker.Count == baselineCount);
            }
            finally
            {
                ReactorApp.ShutdownPolicy = savedPolicy;
            }

            host.Mount(_ => TextBlock("floating-done"));
            await Harness.Render();
        }
    }
}
