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
