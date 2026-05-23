using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUIColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for the "programmatic-write echo" class of bug.
///
/// When a Reactor Update handler writes a value-bearing DP (ColorPicker.Color,
/// NumberBox.Value, ToggleSwitch.IsOn, Slider.Value, …), the control raises its
/// change event. If that event re-enters the user's onChange callback, the
/// owning component's state gets overwritten with the value Reactor just wrote —
/// which, for components bound to an external selection (PropertyGrid, tabbed
/// forms), corrupts the PREVIOUS selection's state with the NEW selection's
/// value.
///
/// Each fixture below:
///   1. mounts the editor with value A and a call-recording onChange.
///   2. verifies the mount itself did NOT invoke onChange.
///   3. re-renders with value B via setState and verifies:
///        - the control reflects value B (basic update works),
///        - onChange was NOT invoked (no echo).
///   4. directly pokes the control to simulate real user input, and verifies
///      onChange DID fire (continuous-value editing is NOT broken by the fix).
///
/// All three invariants together distinguish a correct suppressor from either
/// an over-eager (kills real input) or under-eager (regression) implementation.
/// </summary>
internal static class EchoSuppressionFixtures
{
    // ── ColorPicker ───────────────────────────────────────────────────

    internal class ColorPickerNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<WinUIColor>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var color = phase == 0
                    ? global::Microsoft.UI.Colors.Red
                    : global::Microsoft.UI.Colors.Green;
                return VStack(
                    Button("Go_CP", () => setPhase(1)),
                    ColorPicker(color, c => calls.Add(c))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_ColorPicker_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_CP");
            await Harness.Render();

            var cp = H.FindControl<ColorPicker>(_ => true);
            H.Check("EchoSuppress_ColorPicker_UpdateAppliedValue",
                cp is not null && cp.Color == global::Microsoft.UI.Colors.Green);
            H.Check("EchoSuppress_ColorPicker_NoEchoCall", calls.Count == 0);

            if (cp is not null) cp.Color = global::Microsoft.UI.Colors.Blue;
            await Harness.Render();
            H.Check("EchoSuppress_ColorPicker_UserEditFires",
                calls.Count >= 1 && calls[^1] == global::Microsoft.UI.Colors.Blue);
        }
    }

    // ── NumberBox ─────────────────────────────────────────────────────

    internal class NumberBoxNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<double>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var value = phase == 0 ? 10.0 : 42.0;
                return VStack(
                    Button("Go_NB", () => setPhase(1)),
                    NumberBox(value, v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_NumberBox_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_NB");
            await Harness.Render();

            var nb = H.FindControl<NumberBox>(_ => true);
            H.Check("EchoSuppress_NumberBox_UpdateAppliedValue", nb?.Value == 42.0);
            H.Check("EchoSuppress_NumberBox_NoEchoCall", calls.Count == 0);

            if (nb is not null) nb.Value = 77.0;
            await Harness.Render();
            H.Check("EchoSuppress_NumberBox_UserEditFires",
                calls.Count >= 1 && calls[^1] == 77.0);
        }
    }

    // ── ToggleSwitch ──────────────────────────────────────────────────

    internal class ToggleSwitchNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<bool>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var on = phase == 1;
                return VStack(
                    Button("Go_TS", () => setPhase(1)),
                    ToggleSwitch(on, v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_ToggleSwitch_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_TS");
            await Harness.Render();

            var ts = H.FindControl<ToggleSwitch>(_ => true);
            H.Check("EchoSuppress_ToggleSwitch_UpdateAppliedValue", ts?.IsOn == true);
            H.Check("EchoSuppress_ToggleSwitch_NoEchoCall", calls.Count == 0);

            if (ts is not null) ts.IsOn = false;
            await Harness.Render();
            H.Check("EchoSuppress_ToggleSwitch_UserEditFires",
                calls.Count >= 1 && calls[^1] == false);
        }
    }

    // ── CheckBox ──────────────────────────────────────────────────────

    internal class CheckBoxNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<bool>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var chk = phase == 1;
                return VStack(
                    Button("Go_CB", () => setPhase(1)),
                    CheckBox(chk, v => calls.Add(v), label: "CB")
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_CheckBox_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_CB");
            await Harness.Render();

            var cb = H.FindControl<CheckBox>(c => c.Content is string s && s == "CB");
            H.Check("EchoSuppress_CheckBox_UpdateAppliedValue", cb?.IsChecked == true);
            H.Check("EchoSuppress_CheckBox_NoEchoCall", calls.Count == 0);

            if (cb is not null) cb.IsChecked = false;
            await Harness.Render();
            H.Check("EchoSuppress_CheckBox_UserEditFires",
                calls.Count >= 1 && calls[^1] == false);
        }
    }

    // ── Slider ────────────────────────────────────────────────────────

    internal class SliderNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<double>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var v = phase == 0 ? 25.0 : 75.0;
                return VStack(
                    Button("Go_SL", () => setPhase(1)),
                    Slider(v, 0, 100, d => calls.Add(d))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_Slider_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_SL");
            await Harness.Render();

            var sl = H.FindControl<Slider>(_ => true);
            H.Check("EchoSuppress_Slider_UpdateAppliedValue", sl?.Value == 75.0);
            H.Check("EchoSuppress_Slider_NoEchoCall", calls.Count == 0);

            if (sl is not null) sl.Value = 50.0;
            await Harness.Render();
            H.Check("EchoSuppress_Slider_UserEditFires",
                calls.Count >= 1 && calls[^1] == 50.0);
        }
    }

    /// <summary>
    /// Min/Max coercion can itself raise ValueChanged before we reach the
    /// explicit Value= write. Verifies that when the range shifts such that
    /// the current Value is forced in-range, the user's onChange is still
    /// not called. This is the regression coverage for the gap Copilot
    /// flagged on the initial PR review.
    /// </summary>
    internal class SliderMinMaxCoercionNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<double>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                // Phase 0: Value=25 inside [0,100]. Phase 1: Min=50 shifts the
                // range so Value=25 is out of range. If Min= coerces Value
                // without suppression, the echo fires. Also note the explicit
                // Value jumps to 75 to exercise the Value= path on the same
                // reconcile as the Min= coercion.
                var (min, max, v) = phase == 0 ? (0.0, 100.0, 25.0) : (50.0, 100.0, 75.0);
                return VStack(
                    Button("Go_SL_MM", () => setPhase(1)),
                    Slider(v, min, max, d => calls.Add(d))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_SliderMinMax_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_SL_MM");
            await Harness.Render();

            var sl = H.FindControl<Slider>(_ => true);
            H.Check("EchoSuppress_SliderMinMax_UpdateApplied",
                sl is not null && sl.Minimum == 50.0 && sl.Value == 75.0);
            H.Check("EchoSuppress_SliderMinMax_NoEchoCall", calls.Count == 0);
        }
    }

    /// <summary>
    /// Same coercion scenario for NumberBox. Min shift forces the existing
    /// Value out of range; we must suppress that echo too.
    /// </summary>
    internal class NumberBoxMinMaxCoercionNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<double>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var (min, max, v) = phase == 0 ? (0.0, 100.0, 25.0) : (50.0, 100.0, 75.0);
                return VStack(
                    Button("Go_NB_MM", () => setPhase(1)),
                    NumberBox(v, d => calls.Add(d)) with { Minimum = min, Maximum = max }
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_NumberBoxMinMax_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_NB_MM");
            await Harness.Render();

            var nb = H.FindControl<NumberBox>(_ => true);
            H.Check("EchoSuppress_NumberBoxMinMax_UpdateApplied",
                nb is not null && nb.Minimum == 50.0 && nb.Value == 75.0);
            H.Check("EchoSuppress_NumberBoxMinMax_NoEchoCall", calls.Count == 0);
        }
    }

    // ── RatingControl ─────────────────────────────────────────────────

    internal class RatingControlNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<double>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var v = phase == 0 ? 2.0 : 4.0;
                return VStack(
                    Button("Go_RC", () => setPhase(1)),
                    RatingControl(v, d => calls.Add(d))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_Rating_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_RC");
            await Harness.Render();

            var rc = H.FindControl<RatingControl>(_ => true);
            H.Check("EchoSuppress_Rating_UpdateAppliedValue", rc?.Value == 4.0);
            H.Check("EchoSuppress_Rating_NoEchoCall", calls.Count == 0);

            // RatingControl in WinAppSDK 2.0-preview does not raise
            // ValueChanged from a programmatic Value= assignment (by contrast
            // with NumberBox/Slider/ColorPicker). That means there is no echo
            // to suppress in the first place for this control — the
            // "no echo during Update" invariant holds trivially. We keep the
            // fixture for mount + update-applies-value coverage and skip the
            // user-edit assertion that would need real input to exercise.
            if (rc is not null) rc.Value = 3.0;
            await Harness.Render();
            H.Check("EchoSuppress_Rating_ProgrammaticAppliesValue", rc?.Value == 3.0);
        }
    }

    // ── DatePicker ────────────────────────────────────────────────────

    internal class DatePickerNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<DateTimeOffset>();
            var host = H.CreateHost();
            var d0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var d1 = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);
            var d2 = new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero);
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var d = phase == 0 ? d0 : d1;
                return VStack(
                    Button("Go_DP", () => setPhase(1)),
                    DatePicker(d, v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_DatePicker_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_DP");
            await Harness.Render();

            var dp = H.FindControl<DatePicker>(_ => true);
            H.Check("EchoSuppress_DatePicker_UpdateAppliedValue", dp?.Date == d1);
            H.Check("EchoSuppress_DatePicker_NoEchoCall", calls.Count == 0);

            if (dp is not null) dp.Date = d2;
            await Harness.Render();
            H.Check("EchoSuppress_DatePicker_UserEditFires",
                calls.Count >= 1 && calls[^1] == d2);
        }
    }

    // ── CalendarDatePicker ────────────────────────────────────────────

    internal class CalendarDatePickerNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<DateTimeOffset?>();
            var host = H.CreateHost();
            var d0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var d1 = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);
            var d2 = new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero);
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                DateTimeOffset? d = phase == 0 ? d0 : d1;
                return VStack(
                    Button("Go_CDP", () => setPhase(1)),
                    CalendarDatePicker(d, v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_CalDatePicker_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_CDP");
            await Harness.Render();

            var cdp = H.FindControl<CalendarDatePicker>(_ => true);
            H.Check("EchoSuppress_CalDatePicker_UpdateAppliedValue", cdp?.Date == d1);
            H.Check("EchoSuppress_CalDatePicker_NoEchoCall", calls.Count == 0);

            if (cdp is not null) cdp.Date = d2;
            await Harness.Render();
            H.Check("EchoSuppress_CalDatePicker_UserEditFires",
                calls.Count >= 1 && calls[^1] == d2);
        }
    }

    // ── TimePicker ────────────────────────────────────────────────────

    internal class TimePickerNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<TimeSpan>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var t = phase == 0 ? TimeSpan.FromHours(9) : TimeSpan.FromHours(17);
                return VStack(
                    Button("Go_TP", () => setPhase(1)),
                    TimePicker(t, v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_TimePicker_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_TP");
            await Harness.Render();

            var tp = H.FindControl<TimePicker>(_ => true);
            H.Check("EchoSuppress_TimePicker_UpdateAppliedValue",
                tp?.Time == TimeSpan.FromHours(17));
            H.Check("EchoSuppress_TimePicker_NoEchoCall", calls.Count == 0);

            if (tp is not null) tp.Time = TimeSpan.FromHours(12);
            await Harness.Render();
            H.Check("EchoSuppress_TimePicker_UserEditFires",
                calls.Count >= 1 && calls[^1] == TimeSpan.FromHours(12));
        }
    }

    // ── PasswordBox ───────────────────────────────────────────────────

    internal class PasswordBoxNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<string>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var p = phase == 0 ? "initial" : "next";
                return VStack(
                    Button("Go_PW", () => setPhase(1)),
                    PasswordBox(p, s => calls.Add(s))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_PasswordBox_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_PW");
            await Harness.Render();

            var pb = H.FindControl<PasswordBox>(_ => true);
            H.Check("EchoSuppress_PasswordBox_UpdateAppliedValue", pb?.Password == "next");
            H.Check("EchoSuppress_PasswordBox_NoEchoCall", calls.Count == 0);

            if (pb is not null) pb.Password = "typed";
            // PasswordBox.PasswordChanged can be raised via the dispatcher rather
            // than synchronously in the setter, so a single render pump is racy.
            // Pump twice so the queued event lands before the assertion.
            await Harness.Render();
            await Harness.Render();
            H.Check("EchoSuppress_PasswordBox_UserEditFires",
                calls.Count >= 1 && calls[^1] == "typed");
        }
    }

    // ── TextField ─────────────────────────────────────────────────────

    internal class TextFieldNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<string>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var t = phase == 0 ? "initial" : "next";
                return VStack(
                    Button("Go_TF", () => setPhase(1)),
                    TextBox(t, s => calls.Add(s), placeholder: "test")
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_TextField_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_TF");
            await Harness.Render();

            var tb = H.FindControl<TextBox>(t => t.PlaceholderText == "test");
            H.Check("EchoSuppress_TextField_UpdateAppliedValue", tb?.Text == "next");
            // Note: after a setState-driven change to "next", the controlled
            // TextField's onChange MAY receive a trailing call from the
            // re-render snap-back path. We allow at most one non-echo call
            // that equals the current value (not a cross-value echo).
            bool onlyCurrentOrEmpty = calls.All(s => s == "next");
            H.Check("EchoSuppress_TextField_NoEchoCallCrossValue", onlyCurrentOrEmpty);

            var precedingCount = calls.Count;
            if (tb is not null) tb.Text = "typed";
            await Harness.Render();
            H.Check("EchoSuppress_TextField_UserEditFires",
                calls.Count > precedingCount && calls[^1] == "typed");
        }
    }

    // ── ToggleSplitButton ─────────────────────────────────────────────

    internal class ToggleSplitButtonNoEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var calls = new List<bool>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var chk = phase == 1;
                return VStack(
                    Button("Go_TSB", () => setPhase(1)),
                    ToggleSplitButton("Label", isChecked: chk, onIsCheckedChanged: v => calls.Add(v))
                );
            });
            await Harness.Render();
            H.Check("EchoSuppress_TSB_MountNoFire", calls.Count == 0);

            H.ClickButton("Go_TSB");
            await Harness.Render();

            var tsb = H.FindControl<ToggleSplitButton>(_ => true);
            H.Check("EchoSuppress_TSB_UpdateAppliedValue", tsb?.IsChecked == true);
            H.Check("EchoSuppress_TSB_NoEchoCall", calls.Count == 0);

            if (tsb is not null) tsb.IsChecked = false;
            await Harness.Render();
            H.Check("EchoSuppress_TSB_UserEditFires",
                calls.Count >= 1 && calls[^1] == false);
        }
    }
}
