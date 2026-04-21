using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-based fixtures for the Phase 3 gesture recognizers (spec 027 §Tier 3).
/// We can't dispatch a real ManipulationDelta from a self-test (args are sealed),
/// so these fixtures verify ManipulationMode flag computation on the mounted
/// control — which is the contract that lets Appium-driven gestures reach the
/// recognizer in the E2E pass.
/// </summary>
internal static class GestureFixtures
{
    internal class OnPanSetsManipulationMode(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(80, 80)
                .Set(r => r.Name = "panTarget")
                .OnPan(_ => { }, axis: PanAxis.Both));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(r => r.Name == "panTarget");
            H.Check("OnPan_Mounted", rect is not null);
            H.Check("OnPan_ManipulationMode_TranslateX",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateX));
            H.Check("OnPan_ManipulationMode_TranslateY",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateY));
            H.Check("OnPan_ManipulationMode_NoInertiaByDefault",
                rect is not null && !rect.ManipulationMode.HasFlag(ManipulationModes.TranslateInertia));
        }
    }

    internal class OnPanWithInertiaAddsInertiaFlag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(80, 80)
                .Set(r => r.Name = "panInertia")
                .OnPan(_ => { }, axis: PanAxis.Both, withInertia: true));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(r => r.Name == "panInertia");
            H.Check("OnPanInertia_Mounted", rect is not null);
            H.Check("OnPanInertia_HasInertiaFlag",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateInertia));
        }
    }

    internal class OnPinchSetsScaleFlag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(80, 80)
                .Set(r => r.Name = "pinchTarget")
                .OnPinch(_ => { }));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(r => r.Name == "pinchTarget");
            H.Check("OnPinch_Mounted", rect is not null);
            H.Check("OnPinch_HasScaleFlag",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.Scale));
        }
    }

    internal class OnRotateSetsRotateFlag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(80, 80)
                .Set(r => r.Name = "rotateTarget")
                .OnRotate(_ => { }));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(r => r.Name == "rotateTarget");
            H.Check("OnRotate_Mounted", rect is not null);
            H.Check("OnRotate_HasRotateFlag",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.Rotate));
        }
    }

    internal class PanAndPinchCombine(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => Factories.Rectangle()
                .Size(80, 80)
                .Set(r => r.Name = "combined")
                .OnPan(_ => { }, axis: PanAxis.Both)
                .OnPinch(_ => { }));
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(r => r.Name == "combined");
            H.Check("Combined_Mounted", rect is not null);
            H.Check("Combined_HasTranslateX",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateX));
            H.Check("Combined_HasTranslateY",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateY));
            H.Check("Combined_HasScale",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.Scale));
        }
    }

    // ── Phase 4 LongPress (spec 027 Tier 3 Part 2) ──────────────────

    internal class OnLongPressAutoEnablesHolding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("lp-target")
                .OnLongPress(_ => { }));
            await Harness.Render();

            var target = H.FindText("lp-target");
            H.Check("OnLongPress_Mounted", target is not null);
            H.Check("OnLongPress_IsHoldingEnabled",
                target is not null && target.IsHoldingEnabled);
        }
    }

    internal class OnLongPressMouseEmulationOptIn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("lp-mouse")
                .OnLongPress(_ => { }, enableMouseEmulation: true));
            await Harness.Render();

            var target = H.FindText("lp-mouse");
            H.Check("OnLongPressMouseEmulation_Mounted", target is not null);
            // IsHoldingEnabled still true so touch/pen keeps working.
            H.Check("OnLongPressMouseEmulation_IsHoldingEnabled",
                target is not null && target.IsHoldingEnabled);
        }
    }
}
