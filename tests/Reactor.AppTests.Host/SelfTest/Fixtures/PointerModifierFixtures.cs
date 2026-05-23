using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-based fixtures for the Phase 1 input modifiers (spec 027 §Tier 1):
/// verify that attaching new .On* handlers correctly auto-enables the matching
/// UIElement Is*Enabled flag, fills Shape backgrounds for hit-testing, and
/// dispatches programmatic focus through the new .OnGotFocus / .OnLostFocus.
/// Actual pointer event dispatch is exercised end-to-end by GestureTests.
/// </summary>
internal static class PointerModifierFixtures
{
    internal class DoubleTappedAutoEnables(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("dt-target")
                .OnDoubleTapped((_, _) => { }));
            await Harness.Render();

            var target = H.FindText("dt-target");
            H.Check("DoubleTapped_AutoEnables_Found", target is not null);
            H.Check("DoubleTapped_AutoEnables_IsDoubleTapEnabled",
                target is not null && target.IsDoubleTapEnabled);
        }
    }

    internal class RightTappedAutoEnables(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("rt-target")
                .OnRightTapped((_, _) => { }));
            await Harness.Render();

            var target = H.FindText("rt-target");
            H.Check("RightTapped_AutoEnables_Found", target is not null);
            H.Check("RightTapped_AutoEnables_IsRightTapEnabled",
                target is not null && target.IsRightTapEnabled);
        }
    }

    internal class HoldingAutoEnables(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("hold-target")
                .OnHolding((_, _) => { }));
            await Harness.Render();

            var target = H.FindText("hold-target");
            H.Check("Holding_AutoEnables_Found", target is not null);
            H.Check("Holding_AutoEnables_IsHoldingEnabled",
                target is not null && target.IsHoldingEnabled);
        }
    }

    internal class ShapePointerHandlerAutoFillsTransparent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(32, 32)
                .OnPointerPressed((_, _) => { }));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("ShapePointerHandler_Found", rect is not null);
            H.Check("ShapePointerHandler_FillIsNotNull",
                rect is not null && rect.Fill is not null);
            H.Check("ShapePointerHandler_FillIsTransparentBrush",
                rect?.Fill is SolidColorBrush scb
                    && scb.Color.A == 0);
        }
    }

    internal class ShapeWithExplicitFillNotOverwritten(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = new SolidColorBrush(global::Microsoft.UI.Colors.Red);
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(32, 32)
                .Fill(red)
                .OnPointerPressed((_, _) => { }));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("ShapeExplicitFill_Found", rect is not null);
            H.Check("ShapeExplicitFill_Preserved",
                rect?.Fill is SolidColorBrush scb
                    && scb.Color.R == 255 && scb.Color.A == 255);
        }
    }

    internal class GotLostFocusFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int gotA = 0, lostA = 0, gotB = 0;
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBox("a")
                    .Set(tb => tb.Name = "tbA")
                    .OnGotFocus((_, _) => gotA++)
                    .OnLostFocus((_, _) => lostA++),
                TextBox("b")
                    .Set(tb => tb.Name = "tbB")
                    .OnGotFocus((_, _) => gotB++)
            ));
            await Harness.Render();

            var tbA = H.FindControl<Microsoft.UI.Xaml.Controls.TextBox>(t => t.Name == "tbA");
            var tbB = H.FindControl<Microsoft.UI.Xaml.Controls.TextBox>(t => t.Name == "tbB");
            H.Check("GotLostFocus_ControlsFound", tbA is not null && tbB is not null);

            tbA?.Focus(FocusState.Programmatic);
            // GotFocus is raised via the dispatcher, and the number of ticks
            // before it lands isn't bounded — a fixed two-pump guard (#152)
            // still flaked at ~0.3% across a 1000x sweep. Pump until the
            // counter updates instead of guessing a count.
            for (int i = 0; i < 10 && gotA == 0; i++)
                await Harness.Render();
            H.Check("GotFocus_FiresOnA", gotA == 1 && lostA == 0);

            tbB?.Focus(FocusState.Programmatic);
            for (int i = 0; i < 10 && gotB == 0; i++)
                await Harness.Render();
            H.Check("LostFocus_FiresOnA", lostA == 1);
            H.Check("GotFocus_FiresOnB", gotB == 1);
        }
    }

    internal class AutoEnableClearsOnDetach(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (armed, setArmed) = ctx.UseState(true);
                return VStack(
                    Button("toggle-handler", () => setArmed(!armed))
                        .Set(b => b.Name = "toggleBtn"),
                    armed
                        ? TextBlock("detach-target").OnDoubleTapped((_, _) => { })
                        : TextBlock("detach-target")
                );
            });
            await Harness.Render();

            var initial = H.FindText("detach-target");
            H.Check("AutoEnableDetach_Armed",
                initial is not null && initial.IsDoubleTapEnabled);

            H.ClickButton("toggle-handler");
            await Harness.Render();

            var after = H.FindText("detach-target");
            H.Check("AutoEnableDetach_Cleared",
                after is not null && !after.IsDoubleTapEnabled);
        }
    }
}
