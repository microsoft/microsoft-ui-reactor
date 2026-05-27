using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §14 Phase 2 (Q1 spike) — behavior parity fixtures for the
/// descriptor variants of the three Q1 head-to-head controls
/// (<see cref="ToggleSwitchDescriptor"/>, <see cref="BorderDescriptor"/>,
/// <see cref="SliderDescriptor"/>).
///
/// <para><b>What "parity" means here:</b> the descriptor implementations
/// must match the Phase 1 hand-coded handlers' visible behavior on the
/// same element record — same DP values after Mount, same DP values after
/// Update, same callback-fire pattern across mount/update/programmatic-write.
/// Mismatches surface as failing TAP lines and block the Phase 2 perf
/// matrix (no point comparing speed if the descriptor variant is
/// behaviorally wrong).</para>
///
/// <para><b>Setup:</b> each fixture constructs a Reconciler with
/// <c>registerBuiltinHandlers: false</c> (the internal ctor variant) so the
/// auto-registered Phase 1 handler isn't in the way, then registers the
/// descriptor handler for the same element type. The harness mounts and
/// updates elements through <see cref="Reconciler.Mount"/> /
/// <see cref="Reconciler.UpdateChild"/> directly, bypassing the host /
/// component machinery (those aren't on the path the descriptor changes).</para>
/// </summary>
internal static class Spec047V1ProtocolDescriptorFixtures
{
    // ────────────────────────────────────────────────────────────────────
    //  Helper — descriptor-only reconciler with V1 ON.
    // ────────────────────────────────────────────────────────────────────

    private static Reconciler NewDescriptorReconciler()
        => new Reconciler(logger: null, useV1Protocol: true, registerBuiltinHandlers: false);

    private static readonly Action _noOp = static () => { };

    // ────────────────────────────────────────────────────────────────────
    //  ToggleSwitchDescriptor — value-bearing leaf parity.
    // ────────────────────────────────────────────────────────────────────

    internal class DescToggleSwitchMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<ToggleSwitchElement, WinUI.ToggleSwitch>(
                new DescriptorHandler<ToggleSwitchElement, WinUI.ToggleSwitch>(
                    ToggleSwitchDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new ToggleSwitchElement(IsOn: false, OnIsOnChanged: _ => fireCount++)
            {
                OnContent = "Yes",
                OffContent = "No",
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.ToggleSwitch ts1)
            {
                parent.Children.Add(ts1);
                await Harness.Render();

                H.Check("Desc_ToggleSwitch_Mounted", true);
                H.Check("Desc_ToggleSwitch_InitialIsOff", ts1.IsOn == false);
                H.Check("Desc_ToggleSwitch_OnContent", (ts1.OnContent as string) == "Yes");
                H.Check("Desc_ToggleSwitch_OffContent", (ts1.OffContent as string) == "No");
                H.Check("Desc_ToggleSwitch_MountDidNotFire", fireCount == 0);

                // Programmatic update to IsOn=true — the descriptor's Controlled
                // entry wraps the write in WriteSuppressed; the trampoline drains
                // the echo. Callback must NOT fire.
                var el2 = el1 with { IsOn = true };
                rec.UpdateChild(el1, el2, ts1, _noOp);
                await Harness.Render();

                H.Check("Desc_ToggleSwitch_UpdatedIsOn", ts1.IsOn == true);
                H.Check("Desc_ToggleSwitch_NoEchoOnProgrammaticFlip", fireCount == 0);

                // Flip back — verify Update is idempotent.
                rec.UpdateChild(el2, el1, ts1, _noOp);
                await Harness.Render();
                H.Check("Desc_ToggleSwitch_UpdatedIsOff", ts1.IsOn == false);
                H.Check("Desc_ToggleSwitch_NoEchoOnSecondFlip", fireCount == 0);

                rec.UnmountChild(ts1);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_ToggleSwitch_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  SliderDescriptor — coercion-tolerance parity.
    // ────────────────────────────────────────────────────────────────────

    internal class DescSliderCoercionTolerance(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<SliderElement, WinUI.Slider>(
                new DescriptorHandler<SliderElement, WinUI.Slider>(
                    SliderDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new SliderElement(
                Value: 50, Min: 0, Max: 100,
                OnValueChanged: _ => fireCount++);
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.Slider sl1)
            {
                parent.Children.Add(sl1);
                await Harness.Render();

                H.Check("Desc_Slider_Mounted", true);
                H.Check("Desc_Slider_InitialValue", sl1.Value == 50);
                H.Check("Desc_Slider_InitialMin", sl1.Minimum == 0);
                H.Check("Desc_Slider_InitialMax", sl1.Maximum == 100);
                H.Check("Desc_Slider_MountDidNotFire", fireCount == 0);

                // Raise Min to 60 → coerces Value from 50 → 60. The descriptor's
                // CoercingOneWay entry wraps the Minimum write in WriteSuppressed
                // because the predicate (c.Value < newMin) returns true.
                var el2 = el1 with { Min = 60 };
                rec.UpdateChild(el1, el2, sl1, _noOp);
                await Harness.Render();

                H.Check("Desc_Slider_MinRaised", sl1.Minimum == 60);
                H.Check("Desc_Slider_ValueCoerced", sl1.Value == 60);
                H.Check("Desc_Slider_NoEchoOnCoercion", fireCount == 0);

                rec.UnmountChild(sl1);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_Slider_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  TextBoxDescriptor — 2-event proof (Phase 3 prereq 3.0.2).
    //  Exercises HandCodedControlled (Text/TextChanged) +
    //  HandCodedEvent (SelectionChanged) on the same shared payload.
    // ────────────────────────────────────────────────────────────────────

    internal class DescTextBoxMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<TextBoxElement, WinUI.TextBox>(
                new DescriptorHandler<TextBoxElement, WinUI.TextBox>(
                    TextBoxDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int changedCount = 0;
            var el1 = new TextBoxElement(Value: "hello", OnChanged: _ => changedCount++)
            {
                Header = "Name",
                PlaceholderText = "type here",
                IsReadOnly = false,
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.TextBox tb1)
            {
                parent.Children.Add(tb1);
                await Harness.Render();

                H.Check("Desc_TextBox_Mounted", true);
                H.Check("Desc_TextBox_InitialText", tb1.Text == "hello");
                H.Check("Desc_TextBox_PlaceholderText", tb1.PlaceholderText == "type here");
                H.Check("Desc_TextBox_Header", (tb1.Header as string) == "Name");
                H.Check("Desc_TextBox_MountDidNotFire", changedCount == 0);

                // Programmatic Update of Text — HandCodedControlled wraps in
                // WriteSuppressed; the trampoline drains the echo so the
                // OnChanged callback must NOT fire.
                var el2 = el1 with { Value = "world" };
                rec.UpdateChild(el1, el2, tb1, _noOp);
                await Harness.Render();

                H.Check("Desc_TextBox_TextUpdated", tb1.Text == "world");
                H.Check("Desc_TextBox_NoEchoOnProgrammaticWrite", changedCount == 0);

                // Header transition stays on (descriptor's OneWayConditional —
                // matches Phase 2 Border behavior; clearing on null transition
                // is a documented gap vs. the hand-coded handler).
                var el3 = el2 with { Header = "Renamed" };
                rec.UpdateChild(el2, el3, tb1, _noOp);
                await Harness.Render();
                H.Check("Desc_TextBox_HeaderUpdated", (tb1.Header as string) == "Renamed");

                rec.UnmountChild(tb1);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_TextBox_Mounted", false);
            }
        }
    }

    internal class DescTextBoxTwoEventSubscription(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<TextBoxElement, WinUI.TextBox>(
                new DescriptorHandler<TextBoxElement, WinUI.TextBox>(
                    TextBoxDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int changedCount = 0;
            int selectionCount = 0;
            var el = new TextBoxElement(Value: "abc", OnChanged: _ => changedCount++)
            {
                OnSelectionChanged = (_, _, _) => selectionCount++,
            };

            // Both callbacks set → both HandCoded entries must subscribe.
            // Verify by raising the events through reflection-free public
            // surface: TextChanged fires when Text is set programmatically
            // (and is echo-suppressed), SelectionChanged fires when we
            // adjust selection. We measure via the count fields.
            var ui = rec.Mount(el, _noOp);
            if (ui is WinUI.TextBox tb)
            {
                parent.Children.Add(tb);
                await Harness.Render();

                H.Check("Desc_TextBox_TwoEvent_Mounted", true);
                H.Check("Desc_TextBox_TwoEvent_InitialChangedZero", changedCount == 0);
                H.Check("Desc_TextBox_TwoEvent_InitialSelectionZero", selectionCount == 0);

                // Drive SelectionChanged via SelectionStart/Length writes
                // (these synthesize the event on a focused/unfocused box).
                tb.Focus(FocusState.Programmatic);
                tb.SelectionStart = 1;
                tb.SelectionLength = 1;
                await Harness.Render();

                // SelectionChanged may have fired 1+ times from those writes;
                // the proof point is that the subscription is live (count > 0).
                H.Check("Desc_TextBox_TwoEvent_SelectionFired", selectionCount >= 1);

                // Echo-suppression still works for the controlled Text entry
                // even with both subscriptions active.
                int changedBefore = changedCount;
                var elNext = el with { Value = "xyz" };
                rec.UpdateChild(el, elNext, tb, _noOp);
                await Harness.Render();
                H.Check("Desc_TextBox_TwoEvent_TextUpdated", tb.Text == "xyz");
                H.Check("Desc_TextBox_TwoEvent_NoEchoOnControlledWrite", changedCount == changedBefore);

                rec.UnmountChild(tb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_TextBox_TwoEvent_Mounted", false);
            }
        }
    }

    internal class DescTextBoxCallbackGate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<TextBoxElement, WinUI.TextBox>(
                new DescriptorHandler<TextBoxElement, WinUI.TextBox>(
                    TextBoxDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            // No callbacks at all — descriptor should mount without subscribing
            // either event (gate). Update should still apply DP writes.
            var el1 = new TextBoxElement(Value: "first") { Header = "h1" };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.TextBox tb)
            {
                parent.Children.Add(tb);
                await Harness.Render();

                H.Check("Desc_TextBox_Gate_Mounted", true);
                H.Check("Desc_TextBox_Gate_InitialText", tb.Text == "first");
                H.Check("Desc_TextBox_Gate_Header", (tb.Header as string) == "h1");

                var el2 = el1 with { Value = "second" };
                rec.UpdateChild(el1, el2, tb, _noOp);
                await Harness.Render();
                H.Check("Desc_TextBox_Gate_UpdatedText", tb.Text == "second");

                rec.UnmountChild(tb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_TextBox_Gate_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  CheckBoxDescriptor (Phase 3 batch 1) — single-event controlled with
    //  two-event subscribe (Checked + Unchecked → shared trampoline).
    // ────────────────────────────────────────────────────────────────────

    internal class DescCheckBoxMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<CheckBoxElement, WinUI.CheckBox>(
                new DescriptorHandler<CheckBoxElement, WinUI.CheckBox>(
                    CheckBoxDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            bool? lastValue = null;
            var el1 = new CheckBoxElement(
                IsChecked: false,
                OnIsCheckedChanged: v => { fireCount++; lastValue = v; },
                Label: "Accept");
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.CheckBox cb)
            {
                parent.Children.Add(cb);
                await Harness.Render();

                H.Check("Desc_CheckBox_Mounted", true);
                H.Check("Desc_CheckBox_InitialUnchecked", cb.IsChecked is false);
                H.Check("Desc_CheckBox_Label", (cb.Content as string) == "Accept");
                H.Check("Desc_CheckBox_MountDidNotFire", fireCount == 0);

                // Programmatic update — Controlled wraps the IsChecked write in
                // WriteSuppressed; trampoline drains the echo.
                var el2 = el1 with { IsChecked = true };
                rec.UpdateChild(el1, el2, cb, _noOp);
                await Harness.Render();

                H.Check("Desc_CheckBox_UpdatedChecked", cb.IsChecked is true);
                H.Check("Desc_CheckBox_NoEchoOnProgrammaticFlip", fireCount == 0);

                // Label update.
                var el3 = el2 with { Label = "Confirm" };
                rec.UpdateChild(el2, el3, cb, _noOp);
                await Harness.Render();
                H.Check("Desc_CheckBox_LabelUpdated", (cb.Content as string) == "Confirm");

                rec.UnmountChild(cb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_CheckBox_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  RadioButtonDescriptor (Phase 3 batch 1).
    // ────────────────────────────────────────────────────────────────────

    internal class DescRadioButtonMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<RadioButtonElement, WinUI.RadioButton>(
                new DescriptorHandler<RadioButtonElement, WinUI.RadioButton>(
                    RadioButtonDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new RadioButtonElement(
                Label: "Option A",
                IsChecked: false,
                OnIsCheckedChanged: _ => fireCount++,
                GroupName: "g1");
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.RadioButton rb)
            {
                parent.Children.Add(rb);
                await Harness.Render();

                H.Check("Desc_RadioButton_Mounted", true);
                H.Check("Desc_RadioButton_InitialUnchecked", rb.IsChecked is false);
                H.Check("Desc_RadioButton_Label", (rb.Content as string) == "Option A");
                H.Check("Desc_RadioButton_GroupName", rb.GroupName == "g1");
                H.Check("Desc_RadioButton_MountDidNotFire", fireCount == 0);

                var el2 = el1 with { IsChecked = true };
                rec.UpdateChild(el1, el2, rb, _noOp);
                await Harness.Render();

                H.Check("Desc_RadioButton_UpdatedChecked", rb.IsChecked is true);
                H.Check("Desc_RadioButton_NoEchoOnProgrammaticFlip", fireCount == 0);

                rec.UnmountChild(rb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_RadioButton_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  RatingControlDescriptor (Phase 3 batch 1) — TypedEventHandler bridge.
    // ────────────────────────────────────────────────────────────────────

    internal class DescRatingControlMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<RatingControlElement, WinUI.RatingControl>(
                new DescriptorHandler<RatingControlElement, WinUI.RatingControl>(
                    RatingControlDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new RatingControlElement(
                Value: 3,
                OnValueChanged: _ => fireCount++)
            {
                MaxRating = 5,
                Caption = "Stars",
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.RatingControl rc)
            {
                parent.Children.Add(rc);
                await Harness.Render();

                H.Check("Desc_RatingControl_Mounted", true);
                H.Check("Desc_RatingControl_InitialValue", Math.Abs(rc.Value - 3) < 1e-9);
                H.Check("Desc_RatingControl_MaxRating", rc.MaxRating == 5);
                H.Check("Desc_RatingControl_Caption", rc.Caption == "Stars");
                H.Check("Desc_RatingControl_MountDidNotFire", fireCount == 0);

                var el2 = el1 with { Value = 4 };
                rec.UpdateChild(el1, el2, rc, _noOp);
                await Harness.Render();

                H.Check("Desc_RatingControl_UpdatedValue", Math.Abs(rc.Value - 4) < 1e-9);
                H.Check("Desc_RatingControl_NoEchoOnProgrammaticWrite", fireCount == 0);

                rec.UnmountChild(rc);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_RatingControl_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ToggleSplitButtonDescriptor (Phase 3 batch 1) — non-nullable bool.
    // ────────────────────────────────────────────────────────────────────

    internal class DescToggleSplitButtonMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<ToggleSplitButtonElement, WinUI.ToggleSplitButton>(
                new DescriptorHandler<ToggleSplitButtonElement, WinUI.ToggleSplitButton>(
                    ToggleSplitButtonDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new ToggleSplitButtonElement(
                Label: "Run",
                IsChecked: false,
                OnIsCheckedChanged: _ => fireCount++);
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.ToggleSplitButton tsb)
            {
                parent.Children.Add(tsb);
                await Harness.Render();

                H.Check("Desc_ToggleSplitButton_Mounted", true);
                H.Check("Desc_ToggleSplitButton_InitialUnchecked", !tsb.IsChecked);
                H.Check("Desc_ToggleSplitButton_Label", (tsb.Content as string) == "Run");
                H.Check("Desc_ToggleSplitButton_MountDidNotFire", fireCount == 0);

                var el2 = el1 with { IsChecked = true };
                rec.UpdateChild(el1, el2, tsb, _noOp);
                await Harness.Render();

                H.Check("Desc_ToggleSplitButton_UpdatedChecked", tsb.IsChecked);
                H.Check("Desc_ToggleSplitButton_NoEchoOnProgrammaticFlip", fireCount == 0);

                rec.UnmountChild(tsb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_ToggleSplitButton_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ColorPickerDescriptor (Phase 3 batch 2).
    // ────────────────────────────────────────────────────────────────────

    internal class DescColorPickerMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<ColorPickerElement, WinUI.ColorPicker>(
                new DescriptorHandler<ColorPickerElement, WinUI.ColorPicker>(
                    ColorPickerDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var initial = Color.FromArgb(255, 10, 20, 30);
            var el1 = new ColorPickerElement(
                Color: initial,
                OnColorChanged: _ => fireCount++)
            {
                IsAlphaEnabled = true,
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.ColorPicker cp)
            {
                parent.Children.Add(cp);
                await Harness.Render();

                H.Check("Desc_ColorPicker_Mounted", true);
                H.Check("Desc_ColorPicker_InitialColor", cp.Color == initial);
                H.Check("Desc_ColorPicker_IsAlphaEnabled", cp.IsAlphaEnabled);
                H.Check("Desc_ColorPicker_MountDidNotFire", fireCount == 0);

                var next = Color.FromArgb(255, 200, 100, 50);
                var el2 = el1 with { Color = next };
                rec.UpdateChild(el1, el2, cp, _noOp);
                await Harness.Render();

                H.Check("Desc_ColorPicker_UpdatedColor", cp.Color == next);
                H.Check("Desc_ColorPicker_NoEchoOnProgrammaticWrite", fireCount == 0);

                rec.UnmountChild(cp);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_ColorPicker_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  CalendarDatePickerDescriptor (Phase 3 batch 2) — nullable Date.
    // ────────────────────────────────────────────────────────────────────

    internal class DescCalendarDatePickerMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<CalendarDatePickerElement, WinUI.CalendarDatePicker>(
                new DescriptorHandler<CalendarDatePickerElement, WinUI.CalendarDatePicker>(
                    CalendarDatePickerDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var initial = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
            var el1 = new CalendarDatePickerElement(
                Date: initial,
                OnDateChanged: _ => fireCount++)
            {
                PlaceholderText = "Pick a date",
                Header = "Start",
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.CalendarDatePicker cdp)
            {
                parent.Children.Add(cdp);
                await Harness.Render();

                H.Check("Desc_CalendarDatePicker_Mounted", true);
                H.Check("Desc_CalendarDatePicker_InitialDate", cdp.Date == initial);
                H.Check("Desc_CalendarDatePicker_PlaceholderText", cdp.PlaceholderText == "Pick a date");
                H.Check("Desc_CalendarDatePicker_Header", (cdp.Header as string) == "Start");
                H.Check("Desc_CalendarDatePicker_MountDidNotFire", fireCount == 0);

                var next = initial.AddDays(7);
                var el2 = el1 with { Date = next };
                rec.UpdateChild(el1, el2, cdp, _noOp);
                await Harness.Render();

                H.Check("Desc_CalendarDatePicker_UpdatedDate", cdp.Date == next);
                H.Check("Desc_CalendarDatePicker_NoEchoOnProgrammaticWrite", fireCount == 0);

                rec.UnmountChild(cdp);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_CalendarDatePicker_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  DatePickerDescriptor (Phase 3 batch 2) — non-nullable Date.
    // ────────────────────────────────────────────────────────────────────

    internal class DescDatePickerMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<DatePickerElement, WinUI.DatePicker>(
                new DescriptorHandler<DatePickerElement, WinUI.DatePicker>(
                    DatePickerDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var initial = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var el1 = new DatePickerElement(
                Date: initial,
                OnDateChanged: _ => fireCount++)
            {
                Header = "DOB",
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.DatePicker dp)
            {
                parent.Children.Add(dp);
                await Harness.Render();

                H.Check("Desc_DatePicker_Mounted", true);
                H.Check("Desc_DatePicker_InitialDate", dp.Date == initial);
                H.Check("Desc_DatePicker_Header", (dp.Header as string) == "DOB");
                H.Check("Desc_DatePicker_DayVisible", dp.DayVisible);
                H.Check("Desc_DatePicker_MountDidNotFire", fireCount == 0);

                var next = initial.AddMonths(2);
                var el2 = el1 with { Date = next };
                rec.UpdateChild(el1, el2, dp, _noOp);
                await Harness.Render();

                H.Check("Desc_DatePicker_UpdatedDate", dp.Date == next);
                H.Check("Desc_DatePicker_NoEchoOnProgrammaticWrite", fireCount == 0);

                rec.UnmountChild(dp);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_DatePicker_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  TimePickerDescriptor (Phase 3 batch 2).
    // ────────────────────────────────────────────────────────────────────

    internal class DescTimePickerMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<TimePickerElement, WinUI.TimePicker>(
                new DescriptorHandler<TimePickerElement, WinUI.TimePicker>(
                    TimePickerDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var initial = new TimeSpan(9, 30, 0);
            var el1 = new TimePickerElement(
                Time: initial,
                OnTimeChanged: _ => fireCount++)
            {
                Header = "Meeting",
                MinuteIncrement = 15,
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.TimePicker tp)
            {
                parent.Children.Add(tp);
                await Harness.Render();

                H.Check("Desc_TimePicker_Mounted", true);
                H.Check("Desc_TimePicker_InitialTime", tp.Time == initial);
                H.Check("Desc_TimePicker_Header", (tp.Header as string) == "Meeting");
                H.Check("Desc_TimePicker_MinuteIncrement", tp.MinuteIncrement == 15);
                H.Check("Desc_TimePicker_MountDidNotFire", fireCount == 0);

                var next = new TimeSpan(14, 0, 0);
                var el2 = el1 with { Time = next };
                rec.UpdateChild(el1, el2, tp, _noOp);
                await Harness.Render();

                H.Check("Desc_TimePicker_UpdatedTime", tp.Time == next);
                H.Check("Desc_TimePicker_NoEchoOnProgrammaticWrite", fireCount == 0);

                rec.UnmountChild(tp);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_TimePicker_Mounted", false);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  BorderDescriptor — SingleContent child reconcile parity.
    // ────────────────────────────────────────────────────────────────────

    internal class DescBorderSingleContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = NewDescriptorReconciler();
            rec.RegisterHandler<BorderElement, WinUI.Border>(
                new DescriptorHandler<BorderElement, WinUI.Border>(
                    BorderDescriptor.Descriptor));

            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var el1 = new BorderElement(Child: TextBlock("inside"))
            {
                CornerRadius = 10,
                Background = new SolidColorBrush(Colors.LightBlue),
            };
            var ui = rec.Mount(el1, _noOp);
            if (ui is WinUI.Border bdr)
            {
                parent.Children.Add(bdr);
                await Harness.Render();

                H.Check("Desc_Border_Mounted", true);
                H.Check("Desc_Border_HasChild", bdr.Child is TextBlock);
                H.Check("Desc_Border_ChildText", (bdr.Child as TextBlock)?.Text == "inside");
                H.Check("Desc_Border_CornerRadius", bdr.CornerRadius.TopLeft == 10);
                H.Check("Desc_Border_Background", bdr.Background is SolidColorBrush);

                // Swap the child element → SingleContent strategy should reconcile
                // (preserve descendant identity if possible, else remount).
                var el2 = el1 with { Child = TextBlock("swapped") };
                rec.UpdateChild(el1, el2, bdr, _noOp);
                await Harness.Render();
                H.Check("Desc_Border_ChildSwapped", (bdr.Child as TextBlock)?.Text == "swapped");

                rec.UnmountChild(bdr);
                parent.Children.Clear();
            }
            else
            {
                H.Check("Desc_Border_Mounted", false);
            }
        }
    }
}
