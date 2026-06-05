using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 Phase 7 coverage for pool reuse after controlled values move to
/// Optional&lt;T&gt;.Unset skip-write semantics.
/// </summary>
internal static class ElementPoolOptionalResetFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await TextBox_UnsetRemount_UsesDefaultAndFreshCallback();
            await TextBox_HasValueRemount_WritesValueAndFreshCallback();
            await ToggleSwitch_UnsetRemount_UsesDefaultAndFreshCallback();
            await ToggleSwitch_HasValueRemount_WritesValueAndFreshCallback();
        }

        private async Task TextBox_UnsetRemount_UsesDefaultAndFreshCallback()
        {
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("TB_Unset_Next", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(next, TextBox(Optional<string>.Of("hello"), s => calls.Add("old:" + s))),
                    1 => VStack(next),
                    _ => VStack(next, TextBox(Optional<string>.Unset, s => calls.Add("new:" + s))),
                };
            });

            await Harness.Render();
            var first = H.FindControl<WinUI.TextBox>(_ => true);
            H.Check("ElementPoolOptionalReset_TextBox_Unset_FirstMount", first?.Text == "hello");

            H.ClickButton("TB_Unset_Next");
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_TextBox_Unset_ReturnedToPool_NoEcho", calls.Count == 0);

            H.ClickButton("TB_Unset_Next");
            await Harness.Render();
            var second = H.FindControl<WinUI.TextBox>(_ => true);
            H.Check("ElementPoolOptionalReset_TextBox_Unset_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("ElementPoolOptionalReset_TextBox_Unset_DefaultText", second?.Text == "");

            if (second is not null) second.Text = "typed";
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_TextBox_Unset_FreshCallback", calls.Count == 1 && calls[0] == "new:typed");
        }

        private async Task TextBox_HasValueRemount_WritesValueAndFreshCallback()
        {
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("TB_Value_Next", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(next, TextBox(Optional<string>.Of("hello"), s => calls.Add("old:" + s))),
                    1 => VStack(next),
                    _ => VStack(next, TextBox(Optional<string>.Of("world"), s => calls.Add("new:" + s))),
                };
            });

            await Harness.Render();
            var first = H.FindControl<WinUI.TextBox>(_ => true);
            H.ClickButton("TB_Value_Next");
            await Harness.Render();
            H.ClickButton("TB_Value_Next");
            await Harness.Render();

            var second = H.FindControl<WinUI.TextBox>(_ => true);
            H.Check("ElementPoolOptionalReset_TextBox_Value_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("ElementPoolOptionalReset_TextBox_Value_WritesWorld", second?.Text == "world");
            H.Check("ElementPoolOptionalReset_TextBox_Value_NoEchoStrandBeforeUser", calls.Count == 0);

            if (second is not null) second.Text = "typed-world";
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_TextBox_Value_FreshCallback", calls.Count == 1 && calls[0] == "new:typed-world");
        }

        private async Task ToggleSwitch_UnsetRemount_UsesDefaultAndFreshCallback()
        {
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("TS_Unset_Next", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(next, ToggleSwitch(Optional<bool>.Of(true), v => calls.Add("old:" + v))),
                    1 => VStack(next),
                    _ => VStack(next, ToggleSwitch(Optional<bool>.Unset, v => calls.Add("new:" + v))),
                };
            });

            await Harness.Render();
            var first = H.FindControl<WinUI.ToggleSwitch>(_ => true);
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Unset_FirstMount", first?.IsOn == true);

            H.ClickButton("TS_Unset_Next");
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Unset_ReturnedToPool_NoEcho", calls.Count == 0);

            H.ClickButton("TS_Unset_Next");
            await Harness.Render();
            var second = H.FindControl<WinUI.ToggleSwitch>(_ => true);
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Unset_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Unset_DefaultIsOff", second?.IsOn == false);

            if (second is not null) second.IsOn = true;
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Unset_FreshCallback", calls.Count == 1 && calls[0] == "new:True");
        }

        private async Task ToggleSwitch_HasValueRemount_WritesValueAndFreshCallback()
        {
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("TS_Value_Next", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(next, ToggleSwitch(Optional<bool>.Of(true), v => calls.Add("old:" + v))),
                    1 => VStack(next),
                    _ => VStack(next, ToggleSwitch(Optional<bool>.Of(false), v => calls.Add("new:" + v))),
                };
            });

            await Harness.Render();
            var first = H.FindControl<WinUI.ToggleSwitch>(_ => true);
            H.ClickButton("TS_Value_Next");
            await Harness.Render();
            H.ClickButton("TS_Value_Next");
            await Harness.Render();

            var second = H.FindControl<WinUI.ToggleSwitch>(_ => true);
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Value_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Value_WritesFalse", second?.IsOn == false);
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Value_NoEchoStrandBeforeUser", calls.Count == 0);

            if (second is not null) second.IsOn = true;
            await Harness.Render();
            H.Check("ElementPoolOptionalReset_ToggleSwitch_Value_FreshCallback", calls.Count == 1 && calls[0] == "new:True");
        }
    }
}
