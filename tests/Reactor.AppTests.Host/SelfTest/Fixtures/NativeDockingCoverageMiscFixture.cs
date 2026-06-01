using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 — coverage fixtures for the smaller / mostly-pure pieces of
/// the native renderer: <see cref="DockHostLiveAnnouncer"/>,
/// <see cref="DockSideStripRenderer"/>, <see cref="DockTabGroupRenderer"/>,
/// and <see cref="DockingNativeInterop"/>.
/// </summary>
internal static class NativeDockingCoverageMiscFixtures
{
    // ── DockHostLiveAnnouncer ───────────────────────────────────────────

    /// <summary>
    /// Register a host, announce on-thread and from a background thread,
    /// then call FocusHostFallback against both a Control host (focusable
    /// directly) and a Panel host (focused via FocusManager async walk).
    /// </summary>
    internal class LiveAnnouncer_RegisterAnnounceAndFocus(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hostBorder = new Border { Width = 200, Height = 100 };
            H.SetContent(hostBorder);
            await Harness.Render();

            var fakeManager = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[]
                {
                    new Document { Title = "X", Key = "x", Content = TextBlock("body-x") },
                }),
            };

            // Pre-registration: GetHost returns null, Announce no-ops.
            H.Check("LiveAnn_PreRegister_GetHostNull",
                DockHostLiveAnnouncer.GetHost(fakeManager) is null);
            DockHostLiveAnnouncer.Announce(fakeManager, "ignored-no-host");
            H.Check("LiveAnn_PreRegister_AnnounceNoOps", true);

            DockHostLiveAnnouncer.Register(fakeManager, hostBorder);
            H.Check("LiveAnn_AfterRegister_GetHostMatches",
                ReferenceEquals(DockHostLiveAnnouncer.GetHost(fakeManager), hostBorder));

            // Re-register to exercise the Remove-before-Add idempotency branch.
            DockHostLiveAnnouncer.Register(fakeManager, hostBorder);
            H.Check("LiveAnn_Reregister_DoesNotThrow", true);

            // On-thread announce.
            DockHostLiveAnnouncer.Announce(fakeManager, "on-thread");
            H.Check("LiveAnn_OnThread_Announce_DoesNotThrow", true);

            // Null + empty inputs — early return.
            DockHostLiveAnnouncer.Announce(null, "ignored");
            DockHostLiveAnnouncer.Announce(fakeManager, string.Empty);
            H.Check("LiveAnn_NullAndEmpty_Inputs_Skipped", true);

            // Off-thread announce — dispatched onto host's queue.
            await Task.Run(() => DockHostLiveAnnouncer.Announce(fakeManager, "off-thread"));
            await Harness.Render();
            H.Check("LiveAnn_OffThread_Announce_DoesNotThrow", true);

            // FocusHostFallback against null / unregistered manager — no-ops.
            // (Exercise the null-guard arms before swapping the host.)
            DockHostLiveAnnouncer.FocusHostFallback(null);
            DockHostLiveAnnouncer.FocusHostFallback(new DockManager());
            H.Check("LiveAnn_FocusFallback_Null_NoOps", true);

            // Register a Control host so the Control.Focus branch runs.
            // (The Border/Panel branch in TryFocus uses
            // FocusManager.TryMoveFocusAsync(Next, FindNextElementOptions),
            // which is unsupported in this WinUI version — calling it
            // throws an ArgumentException. The Control branch is safe.)
            var controlHost = new Button { Content = "host" };
            H.SetContent(controlHost);
            await Harness.Render();
            DockHostLiveAnnouncer.Register(fakeManager, controlHost);
            DockHostLiveAnnouncer.FocusHostFallback(fakeManager);
            await Harness.Render();
            H.Check("LiveAnn_FocusFallback_ControlHost_DoesNotThrow", true);

            // Off-thread focus fallback queues onto dispatcher.
            await Task.Run(() => DockHostLiveAnnouncer.FocusHostFallback(fakeManager));
            await Harness.Render();
            H.Check("LiveAnn_FocusFallback_OffThread_DoesNotThrow", true);

            DockHostLiveAnnouncer.Clear(fakeManager);
            H.Check("LiveAnn_AfterClear_GetHostNull",
                DockHostLiveAnnouncer.GetHost(fakeManager) is null);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    // ── DockSideStripRenderer ────────────────────────────────────────────

    /// <summary>
    /// Drives <see cref="DockSideStripRenderer.Compose"/> through three
    /// shapes: all sides empty, one side populated, all four sides
    /// populated. Asserts the rendered element tree mounts without
    /// throwing — exercises the BuildVerticalStrip / BuildHorizontalStrip
    /// / BuildSidePopup branches.
    /// </summary>
    internal class SideStrip_ComposeVariants_RendersWithoutThrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var t1 = new ToolWindow { Title = "Left1", Key = "l1", Content = TextBlock("body-l1") };
            var t2 = new ToolWindow { Title = "Top1", Key = "t1", Content = TextBlock("body-t1") };
            var t3 = new ToolWindow { Title = "Right1", Key = "r1", Content = TextBlock("body-r1") };
            var t4 = new ToolWindow { Title = "Bottom1", Key = "b1", Content = TextBlock("body-b1") };

            var manager = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[]
                {
                    new Document { Title = "Center", Key = "c", Content = TextBlock("body-c") },
                }),
            };

            // 1) All sides empty — outerStack returned directly (no Grid wrap, no popup).
            var compose1 = DockSideStripRenderer.Compose(
                manager,
                center: TextBlock("center-1"),
                effectiveLeftSide: null,
                effectiveTopSide: null,
                effectiveRightSide: null,
                effectiveBottomSide: null,
                expandedPaneKey: null,
                setExpandedPaneKey: _ => { });
            host.Mount(_ => compose1);
            await Harness.Render();
            H.Check("SideStrip_NoSides_CenterRendered",
                H.FindText("center-1") is not null);

            // 2) One pane on every side, none expanded — BuildVerticalStrip /
            //    BuildHorizontalStrip arms fire; popup branch skipped.
            var compose2 = DockSideStripRenderer.Compose(
                manager,
                center: TextBlock("center-2"),
                effectiveLeftSide: new[] { (DockableContent)t1 },
                effectiveTopSide: new[] { (DockableContent)t2 },
                effectiveRightSide: new[] { (DockableContent)t3 },
                effectiveBottomSide: new[] { (DockableContent)t4 },
                expandedPaneKey: null,
                setExpandedPaneKey: _ => { });
            host.Mount(_ => compose2);
            await Harness.Render();
            H.Check("SideStrip_AllSidesPopulated_CenterRendered",
                H.FindText("center-2") is not null);

            // 3) Same shape but with the Left pane expanded — Grid + popup
            //    branch executes.
            var compose3 = DockSideStripRenderer.Compose(
                manager,
                center: TextBlock("center-3"),
                effectiveLeftSide: new[] { (DockableContent)t1 },
                effectiveTopSide: new[] { (DockableContent)t2 },
                effectiveRightSide: new[] { (DockableContent)t3 },
                effectiveBottomSide: new[] { (DockableContent)t4 },
                expandedPaneKey: "l1",
                setExpandedPaneKey: _ => { });
            host.Mount(_ => compose3);
            await Harness.Render();
            H.Check("SideStrip_LeftExpanded_PopupAndCenterRendered",
                H.FindText("center-3") is not null);

            // 4) Top expanded — exercises the Top/Bottom side popup orientation.
            var compose4 = DockSideStripRenderer.Compose(
                manager,
                center: TextBlock("center-4"),
                effectiveLeftSide: new[] { (DockableContent)t1 },
                effectiveTopSide: new[] { (DockableContent)t2 },
                effectiveRightSide: new[] { (DockableContent)t3 },
                effectiveBottomSide: new[] { (DockableContent)t4 },
                expandedPaneKey: "t1",
                setExpandedPaneKey: _ => { });
            host.Mount(_ => compose4);
            await Harness.Render();
            H.Check("SideStrip_TopExpanded_PopupAndCenterRendered",
                H.FindText("center-4") is not null);

            host.Mount(_ => TextBlock("sidestrip-done"));
            await Harness.Render();
        }
    }

    // ── DockTabGroupRenderer ─────────────────────────────────────────────

    /// <summary>
    /// Exercises <see cref="DockTabGroupRenderer.Render"/> with: empty group
    /// (placeholder branch), all-ToolWindow group (CompactTabs auto-flip),
    /// mixed Document + ToolWindow (no auto-flip), and explicit TabChrome
    /// preset variants (Flat / TitleBar / Win11) via BuildSetters.
    /// </summary>
    internal class TabRenderer_EdgeCases_RenderWithoutThrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Empty group renders a placeholder (BorderElement).
            var empty = new DockTabGroup(Array.Empty<DockableContent>());
            var placeholder = DockTabGroupRenderer.Render(
                empty,
                renderLeafContent: _ => null,
                onSelectedIndexChanged: null,
                onTabClosing: null);
            H.Check("TabRenderer_EmptyGroup_ReturnsBorderPlaceholder",
                placeholder is BorderElement);

            // All-ToolWindow group at defaults → CompactTabs auto-true.
            var allTw = new DockTabGroup(new DockableContent[]
            {
                new ToolWindow { Title = "TW1", Key = "tw1", Content = TextBlock("body-tw1") },
                new ToolWindow { Title = "TW2", Key = "tw2", Content = TextBlock("body-tw2") },
            });
            var twRendered = DockTabGroupRenderer.Render(
                allTw,
                renderLeafContent: d => d.Content,
                onSelectedIndexChanged: _ => { },
                onTabClosing: _ => { },
                onTabDragStarting: (_, _) => { },
                onTabDragCompleted: (_, _, _) => { },
                onPinRequested: _ => { });
            H.Check("TabRenderer_AllToolWindow_ReturnsTabViewElement",
                twRendered is TabViewElement);
            if (twRendered is TabViewElement tve)
            {
                H.Check("TabRenderer_AllToolWindow_CompactTabsAutoFlipped",
                    tve.TabWidthMode == TabViewWidthMode.Compact);
            }

            // Mixed group — flip is suppressed.
            var mixed = new DockTabGroup(new DockableContent[]
            {
                new Document { Title = "Doc", Key = "d1", Content = TextBlock("body-d1") },
                new ToolWindow { Title = "TW", Key = "tw3", Content = TextBlock("body-tw3") },
            });
            var mixedRendered = DockTabGroupRenderer.Render(
                mixed,
                renderLeafContent: d => d.Content,
                onSelectedIndexChanged: null,
                onTabClosing: null);
            if (mixedRendered is TabViewElement tve2)
            {
                H.Check("TabRenderer_MixedGroup_NoCompactFlip",
                    tve2.TabWidthMode == TabViewWidthMode.Equal);
            }

            // BuildSetters for each TabChrome preset.
            var flat = new DockTabGroup(allTw.Documents, TabChrome: TabChrome.Flat);
            var tb = new DockTabGroup(allTw.Documents, TabChrome: TabChrome.TitleBar);
            var win11 = new DockTabGroup(allTw.Documents, TabChrome: TabChrome.Win11);
            H.Check("TabRenderer_BuildSetters_Flat",
                DockTabGroupRenderer.BuildSetters(flat).Length >= 2);
            H.Check("TabRenderer_BuildSetters_TitleBar",
                DockTabGroupRenderer.BuildSetters(tb).Length >= 2);
            H.Check("TabRenderer_BuildSetters_Win11",
                DockTabGroupRenderer.BuildSetters(win11).Length >= 1);

            // Render the TitleBar variant into the live tree so ClearManagedKeys + ApplyTitleBarChrome execute.
            var tbRendered = DockTabGroupRenderer.Render(
                tb,
                renderLeafContent: d => d.Content,
                onSelectedIndexChanged: null,
                onTabClosing: null);
            host.Mount(_ => tbRendered);
            await Harness.Render();
            // TabView lazy-realizes the selected pane's body on Normal-priority
            // dispatcher messages scheduled by layout; on contended CI a single
            // Render() pump can race the realization (rare, ~timing-dependent —
            // surfaced once under the V1-ON selftest gate). A second pump drains
            // the straggler so FindText reliably sees the realized body.
            await Harness.Render();
            H.Check("TabRenderer_TitleBarChrome_BodyRendered",
                H.FindText("body-tw1") is not null);

            // Render the Flat variant — different setter array.
            var flatRendered = DockTabGroupRenderer.Render(
                flat,
                renderLeafContent: d => d.Content,
                onSelectedIndexChanged: null,
                onTabClosing: null);
            host.Mount(_ => flatRendered);
            await Harness.Render();
            await Harness.Render();
            H.Check("TabRenderer_FlatChrome_BodyRendered",
                H.FindText("body-tw1") is not null);

            // Group with explicit SelectedIndex out-of-range — falls back to 0.
            var explicitlySelected = new DockTabGroup(allTw.Documents, SelectedIndex: 99);
            var selRendered = DockTabGroupRenderer.Render(
                explicitlySelected,
                renderLeafContent: d => d.Content,
                onSelectedIndexChanged: _ => { },
                onTabClosing: null);
            if (selRendered is TabViewElement tve3)
            {
                H.Check("TabRenderer_OutOfRangeSelectedIndex_ClampsToZero",
                    tve3.SelectedIndex == 0);
            }

            host.Mount(_ => TextBlock("tabrenderer-done"));
            await Harness.Render();
        }
    }
}
