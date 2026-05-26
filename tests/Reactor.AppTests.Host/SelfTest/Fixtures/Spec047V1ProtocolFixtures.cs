using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11–1.15) — behavior parity fixtures for the
/// five ported built-in controls. These fixtures host a real WinUI
/// dispatcher so the V1 dispatch path can be exercised end-to-end.
///
/// <para><b>Toggling V1 ON:</b> the fixtures flip the
/// <c>Reactor.UseV1Protocol</c> <see cref="AppContext"/> switch ON for
/// the duration of <see cref="SelfTestFixtureBase.RunAsync"/>, restoring
/// the previous value on exit. The harness creates a fresh
/// <c>ReactorHost</c> (and thus a fresh <c>Reconciler</c>) per fixture,
/// so the switch takes effect immediately.</para>
///
/// <para>Each fixture mirrors the V1-OFF behavior of the legacy
/// <c>MountXxx</c> body — the same element renders to the same DP values,
/// the same callbacks fire on the same interactions. Diff failures
/// indicate a V1 parity bug that must be fixed before Phase 1 closes.</para>
/// </summary>
internal static class Spec047V1ProtocolFixtures
{
    private const string V1Switch = "Reactor.UseV1Protocol";

    // ────────────────────────────────────────────────────────────────────
    //  1.11 ToggleSwitchHandler
    // ────────────────────────────────────────────────────────────────────

    internal class V1ToggleSwitchMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.TryGetSwitch(V1Switch, out var prev);
            AppContext.SetSwitch(V1Switch, true);
            try
            {
                int fireCount = 0;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (isOn, setIsOn) = ctx.UseState(false);
                    return VStack(
                        ToggleSwitch(isOn, v => { fireCount++; setIsOn(v); }),
                        Button("Flip", () => setIsOn(!isOn))
                    );
                });

                await Harness.Render();
                var ts = H.FindControl<ToggleSwitch>(_ => true);
                H.Check("V1_ToggleSwitch_Mounted", ts is not null);
                H.Check("V1_ToggleSwitch_InitialIsOff", ts?.IsOn == false);

                // Programmatic flip via Button → reconciles to IsOn=true via the
                // V1 handler. Echo-suppression should drop the synthesized
                // Toggled event so fireCount stays 0 (this is a programmatic
                // write, not a user interaction).
                H.ClickButton("Flip");
                await Harness.Render();
                ts = H.FindControl<ToggleSwitch>(_ => true);
                H.Check("V1_ToggleSwitch_UpdatedIsOn", ts?.IsOn == true);
                H.Check("V1_ToggleSwitch_NoEchoOnProgrammaticFlip", fireCount == 0);
            }
            finally
            {
                AppContext.SetSwitch(V1Switch, prev);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  1.12 SliderHandler
    // ────────────────────────────────────────────────────────────────────

    internal class V1SliderCoercionTolerance(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.TryGetSwitch(V1Switch, out var prev);
            AppContext.SetSwitch(V1Switch, true);
            try
            {
                int fireCount = 0;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (min, setMin) = ctx.UseState(0.0);
                    return VStack(
                        Slider(50, min, 100, v => fireCount++),
                        Button("RaiseMin", () => setMin(60.0))
                    );
                });

                await Harness.Render();
                var sl = H.FindControl<Slider>(_ => true);
                H.Check("V1_Slider_Mounted", sl is not null);
                H.Check("V1_Slider_InitialValue", sl?.Value == 50);

                // Raise Min to 60 → coerces Value from 50 → 60. The coercion
                // tolerance suppresses the echo, so fireCount stays 0.
                H.ClickButton("RaiseMin");
                await Harness.Render();
                sl = H.FindControl<Slider>(_ => true);
                H.Check("V1_Slider_MinRaised", sl?.Minimum == 60);
                H.Check("V1_Slider_ValueCoerced", sl?.Value == 60);
                H.Check("V1_Slider_NoEchoOnCoercion", fireCount == 0);
            }
            finally
            {
                AppContext.SetSwitch(V1Switch, prev);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  1.13 TextBoxHandler
    // ────────────────────────────────────────────────────────────────────

    internal class V1TextBoxControlled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.TryGetSwitch(V1Switch, out var prev);
            AppContext.SetSwitch(V1Switch, true);
            try
            {
                int fireCount = 0;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (text, setText) = ctx.UseState("hello");
                    return VStack(
                        TextBox(text, t => { fireCount++; setText(t); }),
                        Button("SetWorld", () => setText("world"))
                    );
                });

                await Harness.Render();
                var tb = H.FindControl<TextBox>(_ => true);
                H.Check("V1_TextBox_Mounted", tb is not null);
                H.Check("V1_TextBox_InitialText", tb?.Text == "hello");

                // Programmatic Text change via reconcile — must NOT round-trip
                // OnChanged (echo-suppressed).
                H.ClickButton("SetWorld");
                await Harness.Render();
                tb = H.FindControl<TextBox>(_ => true);
                H.Check("V1_TextBox_TextUpdated", tb?.Text == "world");
                H.Check("V1_TextBox_NoRoundTrip", fireCount == 0);
            }
            finally
            {
                AppContext.SetSwitch(V1Switch, prev);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  1.14 BorderHandler — SingleContent strategy + modifier precedence
    // ────────────────────────────────────────────────────────────────────

    internal class V1BorderSingleContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.TryGetSwitch(V1Switch, out var prev);
            AppContext.SetSwitch(V1Switch, true);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (phase, set) = ctx.UseState(0);
                    Element child = phase == 0
                        ? TextBlock("inside")
                        : TextBlock("swapped");
                    return VStack(
                        Border(child).CornerRadius(10),
                        Button("Swap", () => set(1))
                    );
                });

                await Harness.Render();
                var bdr = H.FindControl<Border>(_ => true);
                H.Check("V1_Border_Mounted", bdr is not null);
                H.Check("V1_Border_HasChild", bdr?.Child is not null);
                H.Check("V1_Border_CornerRadius", bdr?.CornerRadius.TopLeft == 10);

                H.ClickButton("Swap");
                await Harness.Render();
                bdr = H.FindControl<Border>(_ => true);
                H.Check("V1_Border_ChildSwapped", bdr?.Child is TextBlock tb && tb.Text == "swapped");
            }
            finally
            {
                AppContext.SetSwitch(V1Switch, prev);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  1.15 ListViewHandler — Path B (delegate + ItemsHost shape)
    // ────────────────────────────────────────────────────────────────────

    internal class V1ListViewMountSelect(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.TryGetSwitch(V1Switch, out var prev);
            AppContext.SetSwitch(V1Switch, true);
            try
            {
                int selChange = 0;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (sel, setSel) = ctx.UseState(-1);
                    Element[] items =
                    [
                        TextBlock("Item0"),
                        TextBlock("Item1"),
                        TextBlock("Item2"),
                    ];
                    return VStack(
                        new ListViewElement(items)
                        {
                            SelectedIndex = sel,
                            OnSelectedIndexChanged = i => { selChange++; setSel(i); },
                        },
                        Button("Select1", () => setSel(1))
                    );
                });

                await Harness.Render();
                var lv = H.FindControl<ListView>(_ => true);
                H.Check("V1_ListView_Mounted", lv is not null);

                H.ClickButton("Select1");
                await Harness.Render();
                lv = H.FindControl<ListView>(_ => true);
                H.Check("V1_ListView_SelectedIndex", lv?.SelectedIndex == 1);
            }
            finally
            {
                AppContext.SetSwitch(V1Switch, prev);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  1.15 Pool-survival under V1 dispatch (deferred — needs realized
    //  viewport drag; documented here as the future seat for the L1-style
    //  micro-fixture so a human can wire it up in Phase 1.19 perf work).
    // ────────────────────────────────────────────────────────────────────

    // TODO(1.15 / 1.19): scroll 1000 items through a 20-row viewport, assert
    // pool rent/return cycle with no residual state. Requires a programmatic
    // ScrollIntoView loop the current harness doesn't yet expose.
}
