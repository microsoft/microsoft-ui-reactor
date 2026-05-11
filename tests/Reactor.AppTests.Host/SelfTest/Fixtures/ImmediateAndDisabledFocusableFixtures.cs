using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Covers the validation-pit-of-success additions:
///  • NumberBox .Immediate() — fires OnValueChanged on every parseable
///    keystroke (Text change), not only on commit-on-blur. Keystroke-
///    level behavior under real input is verified in the Appium tier
///    (Reactor.AppTests); this fixture only validates wiring and marker
///    propagation.
///  • Button .DisabledFocusable() — keeps the button keyboard-focusable
///    while visually dimmed and dropping invokes via the Click trampoline.
///    UIA still reports the button as enabled (a full assistive-tech
///    "unavailable" signal would require a custom AutomationPeer override
///    and is a tracked follow-up).
/// </summary>
internal static class ImmediateAndDisabledFocusableFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  NumberBox.Immediate()
    // ════════════════════════════════════════════════════════════════════════

    internal class NumberBoxImmediateFiresOnTextChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            double lastValue = double.NaN;

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                NumberBox(5, v => { count++; lastValue = v; })
                    .Immediate()
                    .Set(n => n.Name = "nbImm")
            ));
            await Harness.Render();

            var nb = H.FindControl<NumberBox>(n => n.Name == "nbImm");
            H.Check("Immediate_Mounted", nb is not null);

            // The marker is what the reconciler reads to wire the TextProperty
            // callback. Verifying it propagates through Element building is
            // deterministic in-process (no WinUI behavior dependence).
            var el = NumberBox(0, _ => { }).Immediate();
            H.Check("Immediate_MarkerAttached",
                el.GetAttached<ImmediateValueAttached>() is not null);
            H.Check("Immediate_NoMarkerWithoutCall",
                NumberBox(0, _ => { })
                    .GetAttached<ImmediateValueAttached>() is null);

            // Positive smoke: assigning Text drives the TextProperty callback,
            // which fires OnValueChanged when the parsed value differs from
            // the element's value. (Full keystroke-level coverage lives in the
            // Appium E2E tier — programmatic Text assignment also triggers
            // WinUI's Value coerce/commit path, so this fixture asserts only
            // that the user callback was invoked at least once with the right
            // payload, not the exact number of fires.)
            count = 0; lastValue = double.NaN;
            if (nb is not null) nb.Text = "42";
            H.Check("Immediate_FiredOnTextChange", count >= 1);
            H.Check("Immediate_PayloadIsParsedText", Math.Abs(lastValue - 42) < 0.01);
        }
    }


    // ════════════════════════════════════════════════════════════════════════
    //  Button.DisabledFocusable()
    // ════════════════════════════════════════════════════════════════════════

    internal class ButtonDisabledFocusableState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int clicks = 0;

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("Submit", () => clicks++)
                    .DisabledFocusable()
                    .Set(b => b.Name = "btnDF")
            ));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "btnDF");
            H.Check("DF_Mounted", btn is not null);
            if (btn is null) return;

            // Stays keyboard-reachable: IsEnabled must remain true.
            H.Check("DF_IsEnabledTrue", btn.IsEnabled);
            // Visual dim signals 'unavailable' without removing from tab order.
            H.Check("DF_OpacityDimmed", btn.Opacity < 1.0);

            // UIA Invoke routes through the Click trampoline, which sees
            // IsDisabledFocusable=true and drops the user OnClick callback.
            // (The Invoke itself does not throw — full AT 'unavailable'
            // reporting is a TODO that requires a custom AutomationPeer.)
            var peer = new ButtonAutomationPeer(btn);
            var invoker = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invoker.Invoke();
            H.Check("DF_OnClickSuppressed", clicks == 0);
        }
    }

    internal class ButtonDisabledFocusableToggleRestoresState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int clicks = 0;
            bool disabledFocusable = true;

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("Submit", () => clicks++)
                    .DisabledFocusable(disabledFocusable)
                    .Set(b => b.Name = "btnDFT")
            ));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "btnDFT");
            H.Check("DFT_Mounted", btn is not null);
            if (btn is null) return;

            H.Check("DFT_InitialOpacityDim", btn.Opacity < 1.0);

            // Re-mount with disabled-focusable off — state must clear.
            disabledFocusable = false;
            host.Mount(_ => VStack(
                Button("Submit", () => clicks++)
                    .DisabledFocusable(disabledFocusable)
                    .Set(b => b.Name = "btnDFT")
            ));
            await Harness.Render();

            btn = H.FindControl<Button>(b => b.Name == "btnDFT");
            H.Check("DFT_AfterTogglePresent", btn is not null);
            if (btn is null) return;
            H.Check("DFT_OpacityRestored", btn.Opacity == 1.0);

            // Now UIA Invoke fires OnClick because the trampoline gate is open.
            var peer = new ButtonAutomationPeer(btn);
            var invoker = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invoker.Invoke();
            H.Check("DFT_InvokeFiresOnClick", clicks >= 1);
        }
    }
}
