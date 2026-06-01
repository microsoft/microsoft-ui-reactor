using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §8 — <b>real-input</b> echo-stranding regression fixtures for the
/// descriptor controlled-value paths (the descriptor <c>PropEntry</c>'s
/// <c>ControlledPropEntry</c> and <c>HandCodedControlledPropEntry</c>).
///
/// <para><b>The bug these lock down (code-review Finding 1):</b> the controlled
/// <c>Update</c> entries used to write under echo-suppression whenever the
/// <em>element</em> prop changed (<c>oldEl != newEl</c>), even when the control
/// already held the new value. <c>WriteSuppressed</c> unconditionally increments
/// the suppress counter, which is only consumed when the write actually raises a
/// change event. Writing a value the control already holds raises no event, so
/// the token <b>strands</b> and silently swallows the user's <em>next</em> real
/// interaction — exactly the cross-state echo class §8 exists to prevent.</para>
///
/// <para><b>Why the existing <c>Desc_*_NoEchoOnProgrammaticWrite</c> fixtures
/// miss it:</b> they only drive programmatic updates, where the control has
/// <em>not</em> yet reached the new value at update time, so the suppressed write
/// is real and the token is consumed. The stranding case requires the precise
/// order <c>real user event → state-driven re-render (control already at value) →
/// real user event</c>, which these fixtures reproduce by raising the control's
/// change event directly (the same path a user gesture takes) before and after
/// the matching re-render.</para>
///
/// <para>Each fixture asserts the second real event's callback still fires; under
/// the stranded-token bug it would be swallowed.</para>
/// </summary>
internal static class Spec047EchoStrandingFixtures
{
    private static Reconciler NewDescriptorReconciler()
        => new Reconciler();

    private static readonly Action _noOp = static () => { };

    // ── ControlledPropEntry path ───────────────────────────────────────────

    /// <summary>RadioButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class RadioButtonRealInputEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new RadioButtonElement(
                Label: "Option A", IsChecked: false, OnIsCheckedChanged: _ => fireCount++,
                GroupName: "g1");
            if (rec.Mount(el1, _noOp) is WinUI.RadioButton rb)
            {
                parent.Children.Add(rb);
                await Harness.Render();

                // (1) Real user interaction: control raises Checked.
                rb.IsChecked = true;
                await Harness.Render();
                H.Check("Desc_RadioButton_RealInput_FirstEventFired", fireCount == 1);

                // (2) State-driven re-render to the value the user already set.
                rec.UpdateChild(el1, el1 with { IsChecked = true }, rb, _noOp);
                await Harness.Render();

                // (3) Next real interaction must NOT be swallowed by a stranded token.
                rb.IsChecked = false;
                await Harness.Render();
                H.Check("Desc_RadioButton_RealInput_SecondEventNotSwallowed", fireCount == 2);

                rec.UnmountChild(rb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_RadioButton_RealInput_Mounted", false);
            }
        }
    }

    /// <summary>ToggleSplitButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class ToggleSplitButtonRealInputEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new ToggleSplitButtonElement(
                Label: "Run", IsChecked: false, OnIsCheckedChanged: _ => fireCount++);
            if (rec.Mount(el1, _noOp) is WinUI.ToggleSplitButton tsb)
            {
                parent.Children.Add(tsb);
                await Harness.Render();

                tsb.IsChecked = true;
                await Harness.Render();
                H.Check("Desc_ToggleSplitButton_RealInput_FirstEventFired", fireCount == 1);

                rec.UpdateChild(el1, el1 with { IsChecked = true }, tsb, _noOp);
                await Harness.Render();

                tsb.IsChecked = false;
                await Harness.Render();
                H.Check("Desc_ToggleSplitButton_RealInput_SecondEventNotSwallowed", fireCount == 2);

                rec.UnmountChild(tsb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_ToggleSplitButton_RealInput_Mounted", false);
            }
        }
    }

    // ── HandCodedControlledPropEntry path ──────────────────────────────────

    /// <summary>NumberBox.Value — <c>.HandCodedControlled</c> entry.</summary>
    internal class NumberBoxRealInputEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int valueChanges = 0;
            var el1 = new NumberBoxElement(Value: 5, OnValueChanged: _ => valueChanges++)
            {
                Minimum = 0,
                Maximum = 100,
            };
            if (rec.Mount(el1, _noOp) is WinUI.NumberBox nb)
            {
                parent.Children.Add(nb);
                await Harness.Render();

                nb.Value = 6;
                await Harness.Render();
                H.Check("Desc_NumberBox_RealInput_FirstEventFired", valueChanges == 1);

                rec.UpdateChild(el1, el1 with { Value = 6 }, nb, _noOp);
                await Harness.Render();

                nb.Value = 7;
                await Harness.Render();
                H.Check("Desc_NumberBox_RealInput_SecondEventNotSwallowed", valueChanges == 2);

                rec.UnmountChild(nb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_NumberBox_RealInput_Mounted", false);
            }
        }
    }
}
