using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #916 — <c>NavigationViewElement.IsPaneOpen</c> could be written but never reported
/// back, so a controlled pane state desynced the moment the control opened or closed its own
/// pane (light dismiss, adaptive display-mode changes on resize). The app then wrote back the
/// value the control already held and the pane toggle appeared to need two clicks.
///
/// These fixtures drive the realized <c>NavigationView</c> directly (standing in for the
/// user-driven pane changes Reactor never sees) and assert both halves of the fix: the
/// callback fires with the new state, and a controlled <c>IsPaneOpen</c> fed from that
/// callback reopens the pane on a single toggle.
/// </summary>
internal static class NavigationViewPaneFixtures
{
    private const string ControlName = "navPaneSync";

    internal class PaneOpenChangedFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            bool? last = null;

            var host = H.CreateHost();
            host.Mount(_ =>
                (NavigationView([NavItem("Home", tag: "home")], TextBlock("pane body")) with
                {
                    IsPaneOpen = true,
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsSettingsVisible = false,
                })
                .PaneOpenChanged(open => { count++; last = open; })
                .Set(n => n.Name = ControlName));

            await Harness.Render();

            var nv = H.FindControl<NavigationView>(n => n.Name == ControlName);
            H.Check("NavPane_Mounted", nv is not null);
            H.Check("NavPane_MountedOpen", nv?.IsPaneOpen == true);

            // The control closes its own pane (light dismiss / adaptive collapse).
            count = 0; last = null;
            if (nv is not null) nv.IsPaneOpen = false;
            await Harness.Render();

            H.Check("NavPane_CloseCallbackFired", count >= 1);
            H.Check("NavPane_CloseCallbackPayload", last == false);

            // ...and opens it again.
            count = 0; last = null;
            if (nv is not null) nv.IsPaneOpen = true;
            await Harness.Render();

            H.Check("NavPane_OpenCallbackFired", count >= 1);
            H.Check("NavPane_OpenCallbackPayload", last == true);
        }
    }

    /// <summary>
    /// The issue's repro, minus the title bar: state drives <c>IsPaneOpen</c>, the control
    /// closes the pane on its own, and ONE toggle must reopen it. Before the fix the state
    /// still read <c>true</c> at that point, so the toggle wrote <c>false</c> — a value the
    /// control already held — and the pane stayed shut.
    /// </summary>
    internal class ControlledPaneResyncsAfterControlDrivenClose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => Component<PaneSyncComponent>());

            await Harness.Render();

            var nv = H.FindControl<NavigationView>(n => n.Name == ControlName);
            H.Check("NavPaneSync_Mounted", nv is not null);
            H.Check("NavPaneSync_StartsOpen", nv?.IsPaneOpen == true);

            // Control-driven close — the app never asked for this.
            if (nv is not null) nv.IsPaneOpen = false;
            await Harness.Render();
            H.Check("NavPaneSync_ClosedByControl", nv?.IsPaneOpen == false);

            // A single toggle click must reopen it.
            H.ClickButton("TogglePane");
            await Harness.Render();
            H.Check("NavPaneSync_OneClickReopens", nv?.IsPaneOpen == true);

            // ...and the next click closes it again (state didn't run away in the other
            // direction — a callback that reported the wrong bool would fail here).
            H.ClickButton("TogglePane");
            await Harness.Render();
            H.Check("NavPaneSync_NextClickCloses", nv?.IsPaneOpen == false);
        }
    }

    /// <summary>
    /// <c>PaneClosing</c> is cancellable, and WinUI leaves <c>IsPaneOpen</c> at the requested
    /// value when a close is cancelled. Reporting off the event would hand the app a bool the
    /// control's own property disagrees with; observing the DP guarantees element state and
    /// control state stay identical — which is exactly what the reconciler diffs against.
    /// </summary>
    internal class CancelledCloseKeepsCallbackInSyncWithControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            bool? last = null;

            var host = H.CreateHost();
            host.Mount(_ =>
                (NavigationView([NavItem("Home", tag: "home")], TextBlock("pane body")) with
                {
                    IsPaneOpen = true,
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsSettingsVisible = false,
                })
                .PaneOpenChanged(open => last = open)
                .Set(n => n.Name = ControlName));

            await Harness.Render();

            var nv = H.FindControl<NavigationView>(n => n.Name == ControlName);
            H.Check("NavPaneCancel_Mounted", nv is not null);

            bool closingSeen = false;
            if (nv is not null) nv.PaneClosing += (_, args) => { closingSeen = true; args.Cancel = true; };

            last = null;
            if (nv is not null) nv.IsPaneOpen = false;
            await Harness.Render();

            // The cancellation path really ran (without this the fixture would pass
            // identically with the Cancel handler removed)...
            H.Check("NavPaneCancel_ClosingCancelled", closingSeen);
            // ...and whatever the control settled on, the app was told the same thing — so
            // the next render's diff (element vs control) can never be a no-op write.
            H.Check("NavPaneCancel_CallbackFired", last is not null);
            H.Check("NavPaneCancel_AgreesWithControl", nv is not null && last == nv.IsPaneOpen);
        }
    }

    /// <summary>
    /// The handler can be wired and cleared across renders. Covers the live
    /// <c>null → callback</c> and <c>callback → null</c> transitions: the observer must
    /// start reporting when a handler appears, and a cleared handler must never be
    /// invoked afterwards (the element pointer the trampoline resolves has to keep up).
    /// </summary>
    internal class HandlerTransitionsAcrossRenders(Harness h) : SelfTestFixtureBase(h)
    {
        internal static int Count;
        internal static bool? Last;
        private const string PhaseButton = "NextPhase";

        public override async Task RunAsync()
        {
            Count = 0; Last = null;

            var host = H.CreateHost();
            host.Mount(_ => Component<HandlerPhaseComponent>());
            await Harness.Render();

            var nv = H.FindControl<NavigationView>(n => n.Name == ControlName);
            H.Check("NavPaneXition_Mounted", nv is not null);

            // Phase 0 — no handler wired.
            if (nv is not null) nv.IsPaneOpen = false;
            await Harness.Render();
            H.Check("NavPaneXition_NoHandlerDpMoved", nv?.IsPaneOpen == false);
            H.Check("NavPaneXition_NoHandlerNoCallback", Count == 0);

            // Phase 1 — handler wired on a later render, not at mount.
            H.ClickButton(PhaseButton);
            await Harness.Render();
            if (nv is not null) nv.IsPaneOpen = true;
            await Harness.Render();
            H.Check("NavPaneXition_LateHandlerFires", Count == 1);
            H.Check("NavPaneXition_LateHandlerPayload", Last == true);

            // Phase 2 — handler cleared; the stale delegate must not be invoked.
            H.ClickButton(PhaseButton);
            await Harness.Render();
            if (nv is not null) nv.IsPaneOpen = false;
            await Harness.Render();
            // The DP really moved, so "no callback" means silence, not a missing change.
            H.Check("NavPaneXition_ClearedPhaseDpMoved", nv?.IsPaneOpen == false);
            H.Check("NavPaneXition_ClearedHandlerSilent", Count == 1);
            H.Check("NavPaneXition_ClearedHandlerPayloadUnchanged", Last == true);
        }
    }

    private sealed class HandlerPhaseComponent : Component
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);

            Action<bool>? handler = phase == 1
                ? open =>
                {
                    HandlerTransitionsAcrossRenders.Count++;
                    HandlerTransitionsAcrossRenders.Last = open;
                }
                : null;

            return VStack(
                Button("NextPhase", () => setPhase(phase + 1)),
                (NavigationView([NavItem("Home", tag: "home")], TextBlock("pane body")) with
                {
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsSettingsVisible = false,
                })
                .PaneOpenChanged(handler)
                .Set(n => n.Name = ControlName)
            );
        }
    }

    private sealed class PaneSyncComponent : Component
    {
        public override Element Render()
        {
            var (isPaneOpen, setIsPaneOpen) = UseState(true);

            return VStack(
                Button("TogglePane", () => setIsPaneOpen(!isPaneOpen)),
                (NavigationView([NavItem("Home", tag: "home")], TextBlock("pane body")) with
                {
                    IsPaneOpen = isPaneOpen,
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsSettingsVisible = false,
                })
                .PaneOpenChanged(setIsPaneOpen)
                .Set(n => n.Name = ControlName)
            );
        }
    }
}
