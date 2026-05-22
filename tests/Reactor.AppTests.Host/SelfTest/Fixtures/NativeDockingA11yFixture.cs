using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.22 — accessibility selftests that need a realized
/// WinUI tree. Unit tests cover the pure functions
/// (<see cref="DockHostNativeComponent.AutomationIdForPane"/>); these
/// fixtures verify the values reach the actual visual-tree elements.
/// </summary>
internal static class NativeDockingA11yFixtures
{
    /// <summary>
    /// Mounts a two-pane DockHost and walks the realized tree to find
    /// (a) the host Border carrying the <see cref="AutomationLandmarkType.Custom"/>
    /// landmark type + localized name, and (b) per-pane Border wrappers
    /// carrying <c>AutomationProperties.AutomationId = "pane:&lt;key&gt;"</c>.
    /// </summary>
    internal class A11y_HostLandmarkAndPaneAutomationIds(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var docA = new Document
            {
                Title = "Editor",
                Key = "a11y:editor",
                Content = TextBlock("body-editor"),
            };
            var docB = new Document
            {
                Title = "Output",
                Key = "a11y:output",
                Content = TextBlock("body-output"),
            };
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { docA, docB }),
            });
            await Harness.Render();

            // Locate the docking host Border by its landmark name.
            var allBorders = H.FindAllControls<Border>(_ => true);
            Border? hostBorder = null;
            foreach (var b in allBorders)
            {
                if (AutomationProperties.GetLandmarkType(b) == AutomationLandmarkType.Custom &&
                    AutomationProperties.GetName(b) == DockingStrings.Get(DockingStringKeys.DockHostLandmark))
                {
                    hostBorder = b;
                    break;
                }
            }
            H.Check("A11y_DockHostLandmark_FoundOnRealizedBorder", hostBorder is not null);
            if (hostBorder is not null)
            {
                H.Check("A11y_DockHostLandmark_NameLocalized",
                    AutomationProperties.GetName(hostBorder) == "Docking area");
                H.Check("A11y_DockHostLandmark_TypeIsCustom",
                    AutomationProperties.GetLandmarkType(hostBorder) == AutomationLandmarkType.Custom);
            }

            // Per-pane AutomationId on the *active* tab. WinUI TabView
            // lazy-realizes inactive tab bodies, so we assert that the
            // selected pane's wrapper carries `pane:a11y:editor`. The
            // tab-switch case is exercised by the keyboard-chord fixtures
            // which select the next tab via Ctrl+PageDown and observe
            // active-pane key transitions.
            bool foundActive = false;
            foreach (var b in allBorders)
            {
                if (AutomationProperties.GetAutomationId(b) == "pane:a11y:editor")
                {
                    foundActive = true;
                    H.Check("A11y_PaneAutomationName_MatchesTitle",
                        AutomationProperties.GetName(b) == "Editor");
                    break;
                }
            }
            H.Check("A11y_PaneAutomationId_ActiveTabFound", foundActive);

            host.Mount(_ => TextBlock("a11y-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.22 — focus invariant: after the last pane in a host
    /// closes, focus lands on the host element so chord targets stay
    /// reachable. The model-mutator close path (CloseOp drain) is the
    /// chord-equivalent code path; we use it here so the assertion is
    /// independent of the keyboard chord wiring (covered by
    /// `DockHostKeyboardTests`).
    /// </summary>
    internal class A11y_FocusFallback_OnLastPaneClose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var docA = new Document
            {
                Title = "Editor",
                Key = "focusfx:editor",
                Content = TextBlock("body-editor"),
                CanClose = true,
            };
            // Stable manager ref so the bridges resolve consistently across
            // the close-then-re-render cycle (matches the
            // `Reliability_Effect_*` fixture pattern).
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { docA }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            // Find the host Border before the close so we can compare
            // identity against the post-close registered host.
            var allBorders = H.FindAllControls<Border>(_ => true);
            Border? hostBorder = null;
            foreach (var b in allBorders)
            {
                if (AutomationProperties.GetLandmarkType(b) == AutomationLandmarkType.Custom
                    && AutomationProperties.GetName(b) == DockingStrings.Get(DockingStringKeys.DockHostLandmark))
                {
                    hostBorder = b;
                    break;
                }
            }
            H.Check("A11y_FocusFallback_HostBorderFound", hostBorder is not null);

            // The live-region bridge registers the same host element. If the
            // pre-close walk found one, the bridge must point at it too.
            var registered = DockHostLiveAnnouncer.GetHost(managerEl);
            H.Check("A11y_FocusFallback_AnnouncerRegistered", registered is not null);
            if (hostBorder is not null && registered is not null)
            {
                H.Check("A11y_FocusFallback_AnnouncerHostMatchesBorder",
                    ReferenceEquals(hostBorder, registered));
            }

            // Drive the close through the model-mutator path so the drain
            // runs synchronously inside Render (no chord plumbing needed).
            // Bridging via the registered host model — the bridge entry
            // is set in DockHostNativeComponent on every render.
            var model = DockHostModelBridge.Get(managerEl);
            H.Check("A11y_FocusFallback_ModelBridgeResolved", model is not null);
            if (model is null) return;

            model.Close(docA);
            // Force a re-render via a fresh element ref so the drain runs
            // even without a parent state mutation.
            host.Mount(_ => managerEl! with { });
            await Harness.Render();

            // The last-pane close drain calls FocusHostFallback. We can't
            // synchronously assert FocusManager.GetFocusedElement under
            // the headless harness (no XamlRoot dispatcher tick after the
            // best-effort focus call), so we verify the contract proxy:
            // the host element is still alive AND still registered with
            // the live announcer, AND the post-close layout has no group.
            var postRegistered = DockHostLiveAnnouncer.GetHost(managerEl);
            H.Check("A11y_FocusFallback_HostStillRegisteredAfterClose",
                postRegistered is not null);
            H.Check("A11y_FocusFallback_NoPanesLeft",
                model.Root is null
                || DockHostKeyboard.FindFirstGroup(model.Root).Group is null
                || DockHostKeyboard.FindFirstGroup(model.Root).Group!.Documents.Count == 0);

            host.Mount(_ => TextBlock("focusfx-done"));
            await Harness.Render();
        }
    }
}
