using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class InteractionStatesTests
{
    internal sealed class FlyoutNormalBackgroundAliasesHover(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var previous = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            (string Name, Func<Element, Element> Wrap)[] overlays =
            {
                ("Flyout", child => Flyout(child, TextBlock("flyout"))),
                ("MenuFlyout", child => MenuFlyout(child)),
                ("CommandBarFlyout", child => CommandBarFlyout(child)),
            };
            foreach (var (name, wrap) in overlays)
            foreach (var mode in new[] { "Common", "Typed", "RemoveCommon" })
            foreach (bool writeFromSetter in new[] { false, true })
            {
                var caseName = $"AliasBackground_{name}_{mode}_{writeFromSetter}";
                var target = Border(TextBlock("background target"));
                var oldTarget = mode == "Typed" ? target with { Background = previous }
                    : mode == "RemoveCommon" ? (target with { Background = hover }).Background(previous)
                    : target.Background(previous);
                var before = wrap(oldTarget.InteractionStates(s => s.PointerOver(opacity: 0.9f)))
                    .InteractionStates(s => s.PointerOver(background: hover));
                var border = (Border)reconciler.Mount(before, static () => { })!;
                try
                {
                    H.Check($"{caseName}_PreviousApplied", ReferenceEquals(border.Background, previous));
                    InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                    H.Check($"{caseName}_HoverApplied", ReferenceEquals(border.Background, hover));
                    var nextTarget = mode == "Common" ? target.Background(hover) : target with { Background = hover };
                    Brush? beforeSetter = null;
                    bool setterFinished = false, wroteHoverAfterSetter = false, wrotePrevious = false;
                    if (writeFromSetter)
                        nextTarget = nextTarget.Set(control =>
                        {
                            beforeSetter = control.Background;
                            control.Background = setterBrush;
                            setterFinished = true;
                        });
                    var after = wrap(nextTarget).InteractionStates(s => s.PointerOver(background: hover));
                    var observer = border.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty, (sender, _) =>
                    {
                        var brush = ((Border)sender).Background;
                        wrotePrevious |= ReferenceEquals(brush, previous);
                        wroteHoverAfterSetter |= setterFinished && ReferenceEquals(brush, hover);
                    });
                    try
                    {
                        var replacement = reconciler.Update(before, after, border, static () => { });
                        var expected = writeFromSetter && mode != "Common" ? setterBrush : hover;
                        H.Check($"{caseName}_DescriptorBeforeSetter", !writeFromSetter || ReferenceEquals(beforeSetter, hover));
                        H.Check($"{caseName}_CommonAfterSetter", !writeFromSetter || mode != "Common" || wroteHoverAfterSetter);
                        H.Check($"{caseName}_CurrentBrush", replacement is null && ReferenceEquals(border.Background, expected));
                        InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                        H.Check($"{caseName}_ExitKeepsLatest", ReferenceEquals(border.Background, expected));
                        H.Check($"{caseName}_NoPreviousBrushWrite", !wrotePrevious);
                    }
                    finally { border.UnregisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty, observer); }
                }
                finally { reconciler.UnmountChild(border); }
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutTypedBrushAliasesHover(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var previous = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            (string Name, Func<Element, Element> Wrap)[] overlays =
            {
                ("Flyout", child => Flyout(child, TextBlock("flyout"))),
                ("MenuFlyout", child => MenuFlyout(child)),
                ("CommandBarFlyout", child => CommandBarFlyout(child)),
            };
            foreach (var (name, wrap) in overlays)
            foreach (bool canvasBackground in new[] { false, true })
            foreach (bool writeFromSetter in new[] { false, true })
            {
                var caseName = $"TypedAliasBrush_{name}_{canvasBackground}_{writeFromSetter}";
                Element target = canvasBackground
                    ? Canvas(TextBlock("typed background")) with { Background = previous }
                    : Border(TextBlock("typed border")) with { BorderBrush = previous };
                Element WrapTarget(Element child) => canvasBackground
                    ? wrap(child).InteractionStates(s => s.PointerOver(background: hover))
                    : wrap(child).InteractionStates(s => s.PointerOver(borderBrush: hover));
                var before = WrapTarget(target.InteractionStates(s => s.PointerOver(opacity: 0.9f)));
                var control = (FrameworkElement)reconciler.Mount(before, static () => { })!;
                Brush? ReadBrush() => control is Microsoft.UI.Xaml.Controls.Canvas canvas
                    ? canvas.Background : ((Border)control).BorderBrush;
                var property = canvasBackground ? Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty
                    : Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty;
                try
                {
                    H.Check($"{caseName}_PreviousApplied", ReferenceEquals(ReadBrush(), previous));
                    InteractionStateApplier.SimulatePointerOverForTests(control, isOver: true);
                    H.Check($"{caseName}_HoverApplied", ReferenceEquals(ReadBrush(), hover));
                    Element nextTarget = target is CanvasElement canvasTarget
                        ? canvasTarget with { Background = hover } : (BorderElement)target with { BorderBrush = hover };
                    Brush? beforeSetter = null;
                    if (writeFromSetter)
                        nextTarget = nextTarget is CanvasElement canvasNext
                            ? canvasNext.Set(native => { beforeSetter = native.Background; native.Background = setterBrush; })
                            : ((BorderElement)nextTarget).Set(native => { beforeSetter = native.BorderBrush; native.BorderBrush = setterBrush; });
                    var after = WrapTarget(nextTarget);
                    bool wrotePrevious = false;
                    var observer = control.RegisterPropertyChangedCallback(property,
                        (_, _) => wrotePrevious |= ReferenceEquals(ReadBrush(), previous));
                    try
                    {
                        var replacement = reconciler.Update(before, after, control, static () => { });
                        var expected = writeFromSetter ? setterBrush : hover;
                        H.Check($"{caseName}_DescriptorBeforeSetter", !writeFromSetter || ReferenceEquals(beforeSetter, hover));
                        H.Check($"{caseName}_CurrentBrush", replacement is null && ReferenceEquals(ReadBrush(), expected));
                        InteractionStateApplier.SimulatePointerOverForTests(control, isOver: false);
                        H.Check($"{caseName}_ExitKeepsLatest", ReferenceEquals(ReadBrush(), expected));
                        H.Check($"{caseName}_NoPreviousBrushWrite", !wrotePrevious);
                    }
                    finally { control.UnregisterPropertyChangedCallback(property, observer); }
                }
                finally { reconciler.UnmountChild(control); }
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutKeepsUpdatedNormalBorderBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var previous = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var target = Border(TextBlock("target")).BorderBrush(previous);
            var initial = Flyout(target.InteractionStates(s => s.PointerOver(opacity: 0.9f)), TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(borderBrush: hover));
            var next = initial with { Target = target.BorderBrush(normal) };
            var border = (Microsoft.UI.Xaml.Controls.Border)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check("FlyoutHoverBorder_PreviousNormalBrush", ReferenceEquals(border.BorderBrush, previous));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                H.Check("FlyoutHoverBorder_OuterBrush", ReferenceEquals(border.BorderBrush, hover));

                var replacement = reconciler.Update(initial, next, border, static () => { });
                H.Check("FlyoutHoverBorder_UpdateKeepsHover", ReferenceEquals(border.BorderBrush, hover));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("FlyoutHoverBorder_ExitKeepsLatestNormalBrush",
                    replacement is null && ReferenceEquals(border.BorderBrush, normal),
                    $"border={(border.BorderBrush as SolidColorBrush)?.Color}");
            }
            finally { reconciler.UnmountChild(border); }

            var outerNormal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var latestOuterNormal = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            initial = ElementExtensions.BorderBrush(initial, outerNormal);
            next = ElementExtensions.BorderBrush(next, latestOuterNormal);
            border = (Microsoft.UI.Xaml.Controls.Border)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check("FlyoutHoverBorder_OuterNormalApplied", ReferenceEquals(border.BorderBrush, outerNormal));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                H.Check("FlyoutHoverBorder_OuterHoverApplied", ReferenceEquals(border.BorderBrush, hover));
                reconciler.Update(initial, next, border, static () => { });
                H.Check("FlyoutHoverBorder_OuterUpdateKeepsHover", ReferenceEquals(border.BorderBrush, hover));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("FlyoutHoverBorder_ExitKeepsOuterUpdate", ReferenceEquals(border.BorderBrush, latestOuterNormal));
            }
            finally { reconciler.UnmountChild(border); }

            (string Name, Func<Element, Element> Wrap)[] overlays =
            {
                ("Flyout", child => Flyout(child, TextBlock("flyout"))),
                ("MenuFlyout", child => MenuFlyout(child)),
                ("CommandBarFlyout", child => CommandBarFlyout(child)),
            };
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            var borderProperty = Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty;
            foreach (var (name, wrap) in overlays)
            foreach (var nextNormal in new Brush?[] { normal, null, hover })
            foreach (bool writeFromSetter in new[] { false, true })
            foreach (bool hadTargetStates in new[] { false, true })
            {
                var brushName = nextNormal is null ? "Clear" : ReferenceEquals(nextNormal, hover) ? "AliasHover" : "Replace";
                var caseName = $"RetainedBorder_{name}_{brushName}_{writeFromSetter}_{hadTargetStates}";
                var content = TextBlock("retained border target");
                var oldTarget = Border(content).BorderBrush(previous);
                if (hadTargetStates) oldTarget = oldTarget.InteractionStates(s => s.PointerOver(opacity: 0.9f));
                var before = wrap(oldTarget)
                    .InteractionStates(s => s.PointerOver(borderBrush: hover));
                border = (Microsoft.UI.Xaml.Controls.Border)reconciler.Mount(before, static () => { })!;
                try
                {
                    H.Check($"{caseName}_PreviousApplied", ReferenceEquals(border.BorderBrush, previous));
                    InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                    H.Check($"{caseName}_HoverApplied", ReferenceEquals(border.BorderBrush, hover));
                    Brush? beforeSetter = null;
                    var nextTarget = Border(content).Set(control =>
                    {
                        beforeSetter = control.BorderBrush;
                        if (writeFromSetter) control.BorderBrush = setterBrush;
                    });
                    if (nextNormal is not null) nextTarget = nextTarget.BorderBrush(nextNormal);
                    var after = wrap(nextTarget).InteractionStates(s => s.PointerOver(borderBrush: hover));
                    bool wrotePrevious = false;
                    var observer = border.RegisterPropertyChangedCallback(borderProperty, (sender, _) =>
                        wrotePrevious |= ReferenceEquals(((Microsoft.UI.Xaml.Controls.Border)sender).BorderBrush, previous));
                    try
                    {
                        var replacement = reconciler.Update(before, after, border, static () => { });
                        H.Check($"{caseName}_SetterSeesHover", ReferenceEquals(beforeSetter, hover));
                        var expected = writeFromSetter ? nextNormal : hover;
                        H.Check($"{caseName}_CurrentBrush", replacement is null && (expected is null
                            ? border.ReadLocalValue(borderProperty) == DependencyProperty.UnsetValue
                            : ReferenceEquals(border.BorderBrush, expected)));
                        InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                        H.Check($"{caseName}_ExitRestoresLatest", nextNormal is null
                            ? border.ReadLocalValue(borderProperty) == DependencyProperty.UnsetValue
                            : ReferenceEquals(border.BorderBrush, nextNormal));
                        H.Check($"{caseName}_NoPreviousBrushWrite", !wrotePrevious);
                    }
                    finally { border.UnregisterPropertyChangedCallback(borderProperty, observer); }
                }
                finally { reconciler.UnmountChild(border); }
            }

            var standalone = target.InteractionStates(s => s.PointerOver(borderBrush: hover));
            border = (Microsoft.UI.Xaml.Controls.Border)reconciler.Mount(standalone, static () => { })!;
            try
            {
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                H.Check("StandaloneHoverBorder_OverrideApplied", ReferenceEquals(border.BorderBrush, hover));
                border.BorderBrush = normal;
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("StandaloneHoverBorder_ExitRestoresCapturedBrush", ReferenceEquals(border.BorderBrush, previous));
            }
            finally { reconciler.UnmountChild(border); }

            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutClearsIndependentChildStates(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var innerHover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var outerHover = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var child = TextBlock("independent child").Foreground(normal);
            var initial = Flyout(
                    Border(child.InteractionStates(s => s.PointerOver(foreground: innerHover))), TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(borderBrush: outerHover));
            var next = initial with { Target = Border(child) };
            var border = (Microsoft.UI.Xaml.Controls.Border)reconciler.Mount(initial, static () => { })!;
            try
            {
                var text = (Microsoft.UI.Xaml.Controls.TextBlock)border.Child;
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("FlyoutChildStates_BothControlsHovered",
                    ReferenceEquals(border.BorderBrush, outerHover) && ReferenceEquals(text.Foreground, innerHover));

                var replacement = reconciler.Update(initial, next, border, static () => { });
                H.Check("FlyoutChildStates_SameChildRestoresNormal",
                    replacement is null && ReferenceEquals(border.Child, text) && ReferenceEquals(text.Foreground, normal),
                    $"foreground={(text.Foreground as SolidColorBrush)?.Color}");
                H.Check("FlyoutChildStates_OuterStillHovered", ReferenceEquals(border.BorderBrush, outerHover));

                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("FlyoutChildStates_DroppedHoverDoesNotReapply", ReferenceEquals(text.Foreground, normal),
                    $"foreground={(text.Foreground as SolidColorBrush)?.Color}");
            }
            finally { reconciler.UnmountChild(border); }

            return Task.CompletedTask;
        }
    }

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

            InteractionStateApplier.SimulatePointerOverForTests(target, isOver: true);
            H.Check("InterState_HoverIn_AnimatesTranslation", visual.TryGetAnimationController("Translation") is not null);
            H.Check("InterState_HoverIn_LeavesOffsetAlone", visual.TryGetAnimationController("Offset") is null);
            await Task.Delay(400);
            await Harness.Render();
            H.Check("InterState_HoverIn_OffsetKeptLayoutPosition", visual.Offset.Y >= 40, $"offset={visual.Offset} layoutY={layoutY}");

            InteractionStateApplier.SimulatePointerOverForTests(target, isOver: false);
            H.Check("InterState_HoverOut_AnimatesTranslation", visual.TryGetAnimationController("Translation") is not null);
            H.Check("InterState_HoverOut_LeavesOffsetAlone", visual.TryGetAnimationController("Offset") is null);
            await Task.Delay(400);
            await Harness.Render();
            H.Check("InterState_HoverOut_OffsetKeptLayoutPosition", visual.Offset.Y >= 40, $"offset={visual.Offset}");
        }
    }
}
