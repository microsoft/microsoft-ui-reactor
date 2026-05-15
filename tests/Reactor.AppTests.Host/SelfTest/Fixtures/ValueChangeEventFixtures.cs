using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Verifies that OnChanged / OnValueChanged / OnColorChanged / OnPasswordChanged
/// callbacks wired by the reconciler actually fire through to user code when
/// the underlying WinUI control's native change event is raised.
///
/// DatePicker, TimePicker, and AutoSuggestBox TextChanged are intentionally not
/// covered here — those events only fire for user input, not programmatic
/// property changes, so they require the E2E/UIA tier to exercise.
/// </summary>
internal static class ValueChangeEventFixtures
{
    internal class CheckBoxToggleFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            bool? lastValue = null;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                CheckBox(false, onIsCheckedChanged: v => { count++; lastValue = v; }, label: "chkEvt")
                    .Set(c => c.Name = "chkEvt")
            ));
            await Harness.Render();

            var cb = H.FindControl<CheckBox>(c => c.Name == "chkEvt");
            H.Check("CheckBoxEvt_Mounted", cb is not null);

            count = 0; lastValue = null;
            if (cb is not null) cb.IsChecked = true;
            H.Check("CheckBoxEvt_CheckedFired", count == 1 && lastValue == true);

            if (cb is not null) cb.IsChecked = false;
            H.Check("CheckBoxEvt_UncheckedFired", count == 2 && lastValue == false);
        }
    }

    internal class RadioButtonToggleFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            bool? lastValue = null;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                RadioButton("rbEvt", isChecked: false,
                    onIsCheckedChanged: v => { count++; lastValue = v; })
                    .Set(r => r.Name = "rbEvt")
            ));
            await Harness.Render();

            var rb = H.FindControl<RadioButton>(r => r.Name == "rbEvt");
            H.Check("RadioButtonEvt_Mounted", rb is not null);

            count = 0; lastValue = null;
            if (rb is not null) rb.IsChecked = true;
            H.Check("RadioButtonEvt_CheckedFired", count == 1 && lastValue == true);

            if (rb is not null) rb.IsChecked = false;
            H.Check("RadioButtonEvt_UncheckedFired", count == 2 && lastValue == false);
        }
    }

    internal class ToggleSwitchToggleFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            bool? lastValue = null;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                ToggleSwitch(false, onIsOnChanged: v => { count++; lastValue = v; },
                    header: "tsEvtHdr")
                    .Set(t => t.Name = "tsEvt")
            ));
            await Harness.Render();

            var ts = H.FindControl<ToggleSwitch>(t => t.Name == "tsEvt");
            H.Check("ToggleSwitchEvt_Mounted", ts is not null);

            count = 0; lastValue = null;
            if (ts is not null) ts.IsOn = true;
            H.Check("ToggleSwitchEvt_OnFired", count == 1 && lastValue == true);

            if (ts is not null) ts.IsOn = false;
            H.Check("ToggleSwitchEvt_OffFired", count == 2 && lastValue == false);
        }
    }

    internal class SliderValueFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            double lastValue = double.NaN;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Slider(10, 0, 100, onValueChanged: v => { count++; lastValue = v; })
                    .Set(s => s.Name = "slEvt")
            ));
            await Harness.Render();

            var sl = H.FindControl<Slider>(s => s.Name == "slEvt");
            H.Check("SliderEvt_Mounted", sl is not null);

            count = 0; lastValue = double.NaN;
            if (sl is not null) sl.Value = 75;

            H.Check("SliderEvt_ValueChangedFired", count >= 1);
            H.Check("SliderEvt_PayloadValue", Math.Abs(lastValue - 75) < 0.01);
        }
    }

    internal class NumberBoxValueFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            double lastValue = double.NaN;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                NumberBox(5, onValueChanged: v => { count++; lastValue = v; },
                    header: "nbEvtHdr")
                    .Set(n => n.Name = "nbEvt")
            ));
            await Harness.Render();

            var nb = H.FindControl<NumberBox>(n => n.Name == "nbEvt");
            H.Check("NumberBoxEvt_Mounted", nb is not null);

            count = 0; lastValue = double.NaN;
            if (nb is not null) nb.Value = 42;

            H.Check("NumberBoxEvt_ValueChangedFired", count >= 1);
            H.Check("NumberBoxEvt_PayloadValue", Math.Abs(lastValue - 42) < 0.01);
        }
    }

    // RatingControl.ValueChanged is only raised for user input (tap/keyboard) in
    // WinUI 3 — programmatic Value assignment updates the property but does not
    // fire the event. Verifying the wiring end-to-end therefore requires a UIA
    // interaction and lives in the E2E tier. We still assert the mount + setter
    // round-trip here to catch regressions in the Reconciler mount path.
    internal class RatingControlValueFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                RatingControl(1, onValueChanged: _ => { count++; })
                    .Set(r => r.Name = "rcEvt")
            ));
            await Harness.Render();

            var rc = H.FindControl<RatingControl>(r => r.Name == "rcEvt");
            H.Check("RatingEvt_Mounted", rc is not null);
            H.Check("RatingEvt_InitialValue", rc is not null && Math.Abs(rc.Value - 1) < 0.01);

            if (rc is not null) rc.Value = 4;
            await Harness.Render();

            H.Check("RatingEvt_ProgrammaticAssignReflected",
                rc is not null && Math.Abs(rc.Value - 4) < 0.01);
        }
    }

    internal class PasswordBoxChangeFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            string? lastValue = null;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                PasswordBox("initial", onPasswordChanged: v => { count++; lastValue = v; })
                    .Set(p => p.Name = "pbEvt")
            ));
            await Harness.Render();

            var pb = H.FindControl<PasswordBox>(p => p.Name == "pbEvt");
            H.Check("PasswordBoxEvt_Mounted", pb is not null);

            count = 0; lastValue = null;
            if (pb is not null) pb.Password = "changed";
            await Harness.Render();

            H.Check("PasswordBoxEvt_CallbackFired", count >= 1);
            H.Check("PasswordBoxEvt_PayloadValue", lastValue == "changed");
        }
    }

    internal class ColorPickerChangeFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            global::Windows.UI.Color lastColor = default;

            var red = global::Windows.UI.Color.FromArgb(255, 255, 0, 0);
            var blue = global::Windows.UI.Color.FromArgb(255, 0, 0, 255);

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                ColorPicker(red, onColorChanged: c => { count++; lastColor = c; })
                    .Set(p => p.Name = "cpEvt")
            ));
            await Harness.Render();

            var cp = H.FindControl<ColorPicker>(p => p.Name == "cpEvt");
            H.Check("ColorPickerEvt_Mounted", cp is not null);

            count = 0; lastColor = default;
            if (cp is not null) cp.Color = blue;

            H.Check("ColorPickerEvt_CallbackFired", count >= 1);
            H.Check("ColorPickerEvt_PayloadColor",
                lastColor.R == blue.R && lastColor.G == blue.G && lastColor.B == blue.B);
        }
    }
}
