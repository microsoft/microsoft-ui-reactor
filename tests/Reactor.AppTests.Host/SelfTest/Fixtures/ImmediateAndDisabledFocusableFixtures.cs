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
///    keystroke (Text change), not only on commit-on-blur.
///  • Button .DisabledFocusable() — keeps the button keyboard-focusable
///    while presenting as disabled (dim opacity, AT IsEnabled=false,
///    Click suppressed). Mirrors Fluent UI React `disabledFocusable`.
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

            // Programmatically setting Text simulates typing — Value stays at 5
            // until commit, but the Immediate hook should fire OnValueChanged
            // off the TextProperty change.
            count = 0; lastValue = double.NaN;
            if (nb is not null) nb.Text = "42";
            H.Check("Immediate_FiredOnTextChange", count >= 1);
            H.Check("Immediate_PayloadIsParsedText", Math.Abs(lastValue - 42) < 0.01);

            // Non-parseable text should NOT fire (no payload to commit).
            count = 0;
            if (nb is not null) nb.Text = "abc";
            H.Check("Immediate_NoFireForUnparseable", count == 0);

            // Out-of-range parsed values should NOT fire.
            count = 0;
            if (nb is not null) { nb.Maximum = 100; nb.Text = "5000"; }
            H.Check("Immediate_NoFireWhenOutOfRange", count == 0);
        }
    }

    internal class NumberBoxWithoutImmediateIgnoresTextChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                NumberBox(5, v => { count++; })
                    .Set(n => n.Name = "nbNoImm")
            ));
            await Harness.Render();

            var nb = H.FindControl<NumberBox>(n => n.Name == "nbNoImm");
            H.Check("NoImmediate_Mounted", nb is not null);

            // Without Immediate, Text changes should not fire OnValueChanged —
            // only the WinUI commit path (Value setter) does.
            count = 0;
            if (nb is not null) nb.Text = "42";
            H.Check("NoImmediate_TextChangeDoesNotFire", count == 0);

            // Regression: the commit path still fires.
            count = 0;
            if (nb is not null) nb.Value = 99;
            H.Check("NoImmediate_ValueSetFires", count >= 1);
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
