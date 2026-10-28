using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for "opening a Flyout from state replaces the window with an error screen".
///
/// <c>FlyoutElement.IsOpen</c> used to open through <c>FlyoutBase.ShowAttachedFlyout(target)</c>,
/// but <c>Reconciler.SetFlyoutOnControl</c> installs the flyout in a Button/SplitButton target's
/// own <c>Flyout</c> property, not as attached metadata. On such a target WinUI throws
/// <c>ArgumentException: ShowAttached was called on an element without an attached FlyoutBase</c>,
/// so <c>Flyout(Button(...), ...) with { IsOpen = open }</c> + <c>setOpen(true)</c> turned the whole
/// tree into the render-error fallback. Opening now goes through the installed flyout's
/// <c>ShowAt</c>, which works for both slots (the same path CommandBarFlyout already used).
/// </summary>
internal static class FlyoutIsOpenStateFixtures
{
    // Null-guarded identity: two missing controls must not count as "the same control".
    private static bool SameInstance(object? a, object? b) => a is not null && ReferenceEquals(a, b);
    private static async Task<bool> WaitOpen(WinPrim.FlyoutBase? flyout)
    {
        for (int i = 0; i < 40 && flyout?.IsOpen != true; i++)
            await Harness.Render(25);
        return flyout?.IsOpen == true;
    }

    private static async Task<bool> WaitClosed(WinPrim.FlyoutBase? flyout)
    {
        for (int i = 0; i < 40 && flyout?.IsOpen == true; i++)
            await Harness.Render(25);
        return flyout?.IsOpen == false;
    }

    private static async Task CloseAndSettle(WinPrim.FlyoutBase? flyout)
    {
        flyout?.Hide();
        await WaitClosed(flyout);
        await Harness.Render(50);
    }

    private static List<RenderError> RecordErrors(ReactorHost host)
    {
        var errors = new List<RenderError>();
        host.RenderErrorHandler = e => { errors.Add(e); return null; };
        return errors;
    }

    private static string Describe(List<RenderError> errors) =>
        errors.Count == 0 ? "none" : $"{errors[0].Source}:{errors[0].Exception.GetType().Name}:{errors[0].Exception.Message}";

    // ════════════════════════════════════════════════════════════════════
    //  The reported repro: Button target, IsOpen driven by state, re-opened
    //  after the user dismisses it (OnClosed resets the state).
    // ════════════════════════════════════════════════════════════════════
    internal class ButtonTargetOpensFromState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var errors = RecordErrors(host);
            int closed = 0;
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                return VStack(
                    Button("FlyState_Go", () => setOpen(true)),
                    Flyout(
                        Button("flystate-target", () => { }),
                        TextBlock("flystate-content")) with
                    {
                        Placement = WinPrim.FlyoutPlacementMode.Top,
                        IsOpen = open,
                        OnClosed = () => { closed++; setOpen(false); },
                    });
            });

            await Harness.Render();
            var target = H.FindButton("flystate-target");
            var flyout = target?.Flyout as Flyout;
            H.Check("FlyState_FlyoutInButtonSlot", flyout is not null);
            H.Check("FlyState_ClosedWhenFalse", flyout?.IsOpen == false);

            H.ClickButton("FlyState_Go");
            await Harness.Render();
            H.Check($"FlyState_NoRenderError[{Describe(errors)}]", errors.Count == 0);
            H.Check("FlyState_TargetSurvives", SameInstance(H.FindButton("flystate-target"), target));
            H.Check("FlyState_OpenedOnRisingEdge", await WaitOpen(flyout));

            // Dismiss → OnClosed resets the state → a second rising edge re-opens it.
            flyout?.Hide();
            H.Check("FlyState_Dismissed", await WaitClosed(flyout));
            // Closed is raised after the close animation, not when IsOpen drops.
            H.Check("FlyState_OnClosedFired", await Harness.WaitFor(() => closed >= 1, 40, 25));
            await Harness.Render(50);

            H.ClickButton("FlyState_Go");
            await Harness.Render();
            H.Check($"FlyState_ReopenNoRenderError[{Describe(errors)}]", errors.Count == 0);
            H.Check("FlyState_ReopenedOnSecondEdge", await WaitOpen(flyout));

            await CloseAndSettle(flyout);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  IsOpen already true at mount on a Button target. The target has no
    //  XamlRoot while the tree is built, so the show defers to its Loaded.
    // ════════════════════════════════════════════════════════════════════
    internal class ButtonTargetOpensOnMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var errors = RecordErrors(host);
            host.Mount(ctx => VStack(
                Flyout(
                    Button("flymount-target", () => { }),
                    TextBlock("flymount-content")) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                    IsOpen = true,
                }));

            await Harness.Render();
            H.Check($"FlyMount_NoRenderError[{Describe(errors)}]", errors.Count == 0);
            var flyout = H.FindButton("flymount-target")?.Flyout as Flyout;
            H.Check("FlyMount_FlyoutInButtonSlot", flyout is not null);
            H.Check("FlyMount_OpenedAfterLoaded", await WaitOpen(flyout));

            await CloseAndSettle(flyout);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  A non-button target keeps the attached-flyout slot, and opening from
    //  state still works through it.
    // ════════════════════════════════════════════════════════════════════
    internal class NonButtonTargetOpensFromState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var errors = RecordErrors(host);
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                return VStack(
                    Button("FlyStateTb_Go", () => setOpen(true)),
                    Flyout(
                        TextBlock("flystatetb-target"),
                        TextBlock("flystatetb-content")) with
                    {
                        Placement = WinPrim.FlyoutPlacementMode.Top,
                        IsOpen = open,
                        OnClosed = () => setOpen(false),
                    });
            });

            await Harness.Render();
            var target = H.FindText("flystatetb-target");
            var flyout = target is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(target) as Flyout;
            H.Check("FlyStateTb_FlyoutInAttachedSlot", flyout is not null);

            H.ClickButton("FlyStateTb_Go");
            await Harness.Render();
            H.Check($"FlyStateTb_NoRenderError[{Describe(errors)}]", errors.Count == 0);
            H.Check("FlyStateTb_Opened", await WaitOpen(flyout));

            await CloseAndSettle(flyout);
        }
    }
}
