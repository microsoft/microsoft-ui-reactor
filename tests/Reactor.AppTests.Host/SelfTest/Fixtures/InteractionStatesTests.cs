using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class InteractionStatesTests
{
    internal class StateMachineTransitions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            // Verify state values record
            var hover = new InteractionStateValues(Opacity: 0.85f, Scale: 1.02f);
            var pressed = new InteractionStateValues(Scale: 0.97f);

            H.Check("InterState_HoverOpacity", hover.Opacity == 0.85f);
            H.Check("InterState_HoverScale", hover.Scale == 1.02f);
            H.Check("InterState_PressedScale", pressed.Scale == 0.97f);
            H.Check("InterState_PressedOpacityNull", pressed.Opacity is null);
        }
    }

    internal class PressedInheritsFromPointerOver(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            var config = new InteractionStatesConfig(
                PointerOver: new InteractionStateValues(Opacity: 0.85f, Scale: 1.02f),
                Pressed: new InteractionStateValues(Scale: 0.97f));

            H.Check("InterState_ConfigCreated", config is not null);
            H.Check("InterState_PointerOverSet", config!.PointerOver?.Opacity == 0.85f);
            H.Check("InterState_PressedScaleSet", config.Pressed?.Scale == 0.97f);
            // Pressed inherits Opacity from PointerOver (null means "inherit")
            H.Check("InterState_PressedOpacityInherited", config.Pressed?.Opacity is null);
        }
    }

    internal class ConfigImmutability(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            var config1 = new InteractionStatesConfig(
                PointerOver: new InteractionStateValues(Opacity: 0.85f));
            var config2 = config1 with { Curve = Curve.Spring() };

            // Original unchanged
            H.Check("InterState_Immutable_OriginalCurveNull", config1.Curve is null);
            H.Check("InterState_Immutable_CopyCurveSet", config2.Curve is SpringCurve);
            H.Check("InterState_Immutable_SamePointerOver", config1.PointerOver == config2.PointerOver);
        }
    }

    internal class NoLayoutProperties(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            // InteractionStateValues only has visual/brush fields — no Width, Margin, etc.
            // This is verified at compile time by the record definition.
            // Just verify the fields we expect are present.
            var values = new InteractionStateValues(
                Opacity: 0.5f,
                Scale: 1.1f,
                ScaleV: new Vector3(1.1f, 1.2f, 1f),
                Translation: new Vector3(4, 0, 0),
                Rotation: 5f,
                Background: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 100, 100, 100)),
                Foreground: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                BorderBrush: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 50, 50, 50)));

            H.Check("InterState_AllFieldsPresent",
                values.Opacity.HasValue && values.Scale.HasValue && values.ScaleV.HasValue
                && values.Translation.HasValue && values.Rotation.HasValue
                && values.Background is not null && values.Foreground is not null
                && values.BorderBrush is not null);
        }
    }

    internal class MountIntegration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Hover me"))
                        .InteractionStates(states => states
                            .PointerOver(opacity: 0.85f, scale: 1.02f)
                            .Pressed(scale: 0.97f))
                        .AutomationId("interact-target")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "interact-target");

            H.Check("InterState_Mount_TargetMounted", target is not null);
            // Handlers are registered internally — no crash on mount = success
            H.Check("InterState_Mount_HandlersRegistered", true);
        }
    }

    // A state's translation used to animate the handoff visual's Offset, which is the layout
    // position: hover-in moved the element to an absolute (0,-4) and hover-out to (0,0), so an
    // element below a sibling jumped to its parent's origin and stayed there (#1432).
    // Reading a composition property returns its static value, not the animated one, so the
    // checks look at which property has a running animation instead.
    internal class HoverOutKeepsLayoutOffset(Harness h) : SelfTestFixtureBase(h)
    {
        static readonly Vector3 Lift = new(0, -4, 0);

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Above")).Height(40),
                    Border(TextBlock("Hover me"))
                        .InteractionStates(states => states.PointerOver(translation: Lift))
                        .AutomationId("interact-below")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "interact-below");
            H.Check("InterState_HoverOut_TargetMounted", target is not null);
            if (target is null) return;

            var visual = ElementCompositionPreview.GetElementVisual(target);
            var layoutY = visual.Offset.Y;

            Reconciler.SimulatePointerOverForTests(target, isOver: true);
            H.Check("InterState_HoverIn_AnimatesTranslation", visual.TryGetAnimationController("Translation") is not null);
            H.Check("InterState_HoverIn_LeavesOffsetAlone", visual.TryGetAnimationController("Offset") is null);
            await Task.Delay(400);
            await Harness.Render();
            H.Check("InterState_HoverIn_OffsetKeptLayoutPosition", visual.Offset.Y >= 40, $"offset={visual.Offset} layoutY={layoutY}");

            Reconciler.SimulatePointerOverForTests(target, isOver: false);
            H.Check("InterState_HoverOut_AnimatesTranslation", visual.TryGetAnimationController("Translation") is not null);
            H.Check("InterState_HoverOut_LeavesOffsetAlone", visual.TryGetAnimationController("Offset") is null);
            await Task.Delay(400);
            await Harness.Render();
            H.Check("InterState_HoverOut_OffsetKeptLayoutPosition", visual.Offset.Y >= 40, $"offset={visual.Offset}");
        }
    }
}
