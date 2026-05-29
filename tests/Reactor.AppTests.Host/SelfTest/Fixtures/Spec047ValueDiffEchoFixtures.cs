using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §8 — <b>value-diff</b> echo-suppression proof-of-concept fixtures
/// for the descriptor controlled fast path (<c>ControlledPropEntry</c>).
///
/// <para><b>What changed:</b> on this path a programmatic <c>Update</c> write no
/// longer bumps the causal <c>ChangeEchoSuppressor</c> counter. Instead it arms a
/// per-control <c>ExpectedEcho</c> on the <c>DescriptorControlledPayload</c>; the
/// change-event trampoline drops the single event whose readback equals that
/// expected value (then clears it). The counter is retained for every other path
/// (hand-coded / coercing / collection entries, the Slider/TextBox/ToggleSwitch
/// handlers, the setter scope, and the public <c>WriteSuppressed</c> primitive).</para>
///
/// <para><b>What these lock down:</b> the <em>drift</em> case the existing
/// <c>Echo_*_RealInput</c> stranding fixtures don't cover — those update the
/// control to the value the user already produced (no drift, no write), whereas
/// these drive a real programmatic change (control at X, element now Y, control
/// not yet at Y) so the suppressed write is genuine and the value-diff path is
/// exercised end to end. Each fixture asserts: (1) the programmatic update lands
/// on the control, (2) it does NOT echo into the user callback, and (3) a
/// subsequent real interaction still fires — distinguishing a correct value-diff
/// suppressor from one that is over-eager (kills real input) or under-eager
/// (regression).</para>
/// </summary>
internal static class Spec047ValueDiffEchoFixtures
{
    private static readonly Action _noOp = static () => { };

    /// <summary>RadioButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class RadioButtonProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new RadioButtonElement(
                Label: "Option A", IsChecked: false, OnIsCheckedChanged: _ => fireCount++,
                GroupName: "vd-g1");

            if (rec.Mount(el1, _noOp) is WinUI.RadioButton rb)
            {
                parent.Children.Add(rb);
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_MountNoFire", fireCount == 0);

                // Programmatic drift: control is false, element re-renders to true.
                // The synthesized Checked event must be recognized as the echo of
                // our own write (readback == ExpectedEcho) and dropped.
                rec.UpdateChild(el1, el1 with { IsChecked = true }, rb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_UpdateAppliedValue", rb.IsChecked == true);
                H.Check("ValueDiff_RadioButton_NoEchoCall", fireCount == 0);

                // Real user interaction must still fire (one-shot arm was consumed).
                rb.IsChecked = false;
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_RealInputFires", fireCount == 1);

                rec.UnmountChild(rb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_RadioButton_Mounted", false);
            }
        }
    }

    /// <summary>ToggleSplitButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class ToggleSplitButtonProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new ToggleSplitButtonElement(
                Label: "Run", IsChecked: false, OnIsCheckedChanged: _ => fireCount++);

            if (rec.Mount(el1, _noOp) is WinUI.ToggleSplitButton tsb)
            {
                parent.Children.Add(tsb);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_MountNoFire", fireCount == 0);

                rec.UpdateChild(el1, el1 with { IsChecked = true }, tsb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_UpdateAppliedValue", tsb.IsChecked == true);
                H.Check("ValueDiff_ToggleSplitButton_NoEchoCall", fireCount == 0);

                tsb.IsChecked = false;
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_RealInputFires", fireCount == 1);

                rec.UnmountChild(tsb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_ToggleSplitButton_Mounted", false);
            }
        }
    }
}
