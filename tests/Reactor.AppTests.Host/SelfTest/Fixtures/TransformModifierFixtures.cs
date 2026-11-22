using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Set-to-unset updates on the same native control (#1001).</summary>
internal sealed class TransformModifierFixture(Harness h, string mode, bool button = false, bool styled = false) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        using var reconciler = new Reconciler();
        // The Button variant deliberately has no other modifiers: dropping the
        // transform family must also work when the entire modifier bag becomes null.
        Element empty = button ? Button("Go") : Border(TextBlock("Transform target")).Width(120).Height(40);
        var active = empty.Scale(new Vector3(1.5f, 1.8f, 1))
            .Rotation(30).Translation(12, 18, 0)
            .CenterPoint(new Vector3(20, 10, 0)).Opacity(0.4);
        if (mode.StartsWith("Animate", StringComparison.Ordinal)) active = active.Animate(Curve.Linear(10000));

        // Start the styled case with a direct local value, then animate it. The
        // final unset must release that local override back to the Style as well.
        var initial = styled ? empty.Opacity(1) : empty;
        var target = (FrameworkElement)reconciler.Mount(initial, () => { })!;
        double restoredOpacity = mode == "Direct" || styled ? 0.65 : 1;
        if (mode == "Direct" || styled) target.Style = new Style(button ? typeof(Button) : typeof(Border))
        {
            Setters = { new Setter(UIElement.OpacityProperty, restoredOpacity) },
        };
        H.SetContent(target);
        await Harness.Render();

        if (mode != "Direct")
        {
            // Non-default compositor base values ensure the reset checks distinguish
            // restoring identity from merely disconnecting an animation.
            var visual = ElementCompositionPreview.GetElementVisual(target);
            visual.Scale = new Vector3(1.1f, 1.2f, 1);
            visual.RotationAngle = 0.2f;
            visual.CenterPoint = new Vector3(3, 4, 0);
        }

        void Apply() => reconciler.Update(initial, active, target, () => { });
        if (mode == "Ambient") AnimationScope.WithAnimation(Curve.Linear(10000), Apply);
        else Apply();
        await Harness.Render();

        string prefix = $"TransformUnset_{(button ? "Button_" : "")}{mode}{(styled ? "_Styled" : "")}";
        if (button) H.Check(prefix + "_EmptyModifierBag", empty.Modifiers is null && target is Button);
        if (styled) H.Check(prefix + "_LocalOpacityBeforeUnset",
            target.ReadLocalValue(UIElement.OpacityProperty) is double local && local == 1);
        string[] properties = ["Scale", "RotationAngle", "Translation", "CenterPoint", "Opacity"];
        if (mode == "Direct")
        {
            // Spec 066 layer 4: the transforms write the visual, and the facades read identity.
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check(prefix + "_VisualTaken", ElementPool.IsVisualTaken(target));
            H.Check(prefix + "_ScaleApplied", visual.Scale == active.Modifiers!.Scale && target.Scale == Vector3.One);
            H.Check(prefix + "_RotationApplied", Math.Abs(visual.RotationAngleInDegrees - 30) < 0.001 && target.Rotation == 0);
            H.Check(prefix + "_TranslationApplied", visual.Properties.TryGetVector3("Translation", out var applied)
                == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && applied == active.Modifiers!.Translation);
            H.Check(prefix + "_CenterPointApplied", visual.CenterPoint == active.Modifiers!.CenterPoint && target.CenterPoint == Vector3.Zero);
            H.Check(prefix + "_OpacityApplied", Math.Abs(target.Opacity - 0.4) < 0.001);
        }
        else
        {
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check(prefix + "_NotPoolableBeforeUnset", ElementPool.IsVisualTaken(target));
            foreach (string property in properties)
            {
                var controller = visual.TryGetAnimationController(property);
                H.Check(prefix + "_" + property + "Started", controller is not null);
                if (controller is not null)
                {
                    controller.Pause();
                    controller.Progress = 0.5f;
                }
            }
        }

        var removed = mode == "AnimateNarrowed" ? empty.Animate(Curve.Linear(10000), AnimateProperty.Opacity)
            : mode == "AnimateRetained" ? empty.Animate(Curve.Linear(10000)) : empty;
        H.Check(prefix + "_SameControl", reconciler.Update(active, removed, target, () => { }) is null);
        await Harness.Render();
        if (mode == "Direct")
        {
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check(prefix + "_ScaleReset", visual.Scale == Vector3.One, $"scale={visual.Scale}");
            H.Check(prefix + "_RotationReset", visual.RotationAngle == 0, $"rotation={visual.RotationAngle}");
            H.Check(prefix + "_TranslationReset", visual.Properties.TryGetVector3("Translation", out var reset)
                == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && reset == Vector3.Zero, $"translation={reset}");
            H.Check(prefix + "_CenterPointReset", visual.CenterPoint == Vector3.Zero, $"center={visual.CenterPoint}");
            // The handoff is permanent: the control goes to the pool's visual lane.
            H.Check(prefix + "_StillHandedOut", ElementPool.IsVisualTaken(target));
        }
        H.Check(prefix + "_OpacityReleased", target.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue
            && Math.Abs(target.Opacity - restoredOpacity) < 0.001, $"opacity={target.Opacity}");
        if (mode != "Direct")
        {
            var visual = ElementCompositionPreview.GetElementVisual(target);
            if (mode == "AnimateNarrowed")
            {
                H.Check(prefix + "_OpacityChannelRetained", visual.ImplicitAnimations?.ContainsKey("Opacity") == true);
                foreach (string property in new[] { "Scale", "RotationAngle", "CenterPoint" })
                    H.Check(prefix + "_" + property + "ChannelRemoved", visual.ImplicitAnimations?.ContainsKey(property) != true);
            }
            H.Check(prefix + "_ScaleReset", visual.Scale == Vector3.One, $"scale={visual.Scale}");
            H.Check(prefix + "_RotationReset", visual.RotationAngle == 0, $"rotation={visual.RotationAngle}");
            H.Check(prefix + "_TranslationReset", visual.Properties.TryGetVector3("Translation", out var translation)
                == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && translation == Vector3.Zero,
                $"translation={translation}");
            H.Check(prefix + "_CenterPointReset", visual.CenterPoint == Vector3.Zero, $"center={visual.CenterPoint}");
            H.Check(prefix + "_OpacityReset", Math.Abs(visual.Opacity - restoredOpacity) < 0.001, $"opacity={visual.Opacity}");
            H.Check(prefix + "_NotPoolable", ElementPool.IsVisualTaken(target));
            foreach (string property in properties)
                H.Check(prefix + "_" + property + "Stopped", visual.TryGetAnimationController(property) is null);

            // Handing out the Visual is permanent. A later ordinary update must still
            // work, and another unset must restore it even though the old animation is gone.
            var reapplied = empty.Scale(new Vector3(1.2f, 1.3f, 1)).Rotation(15).Translation(6, 9, 0)
                .CenterPoint(new Vector3(8, 4, 0)).Opacity(0.3);
            reconciler.Update(removed, reapplied, target, () => { });
            await Harness.Render();
            H.Check(prefix + "_ReappliedScale", visual.Scale == new Vector3(1.2f, 1.3f, 1));
            H.Check(prefix + "_ReappliedRotation", Math.Abs(visual.RotationAngleInDegrees - 15) < 0.001);
            H.Check(prefix + "_ReappliedTranslation", visual.Properties.TryGetVector3("Translation", out translation)
                == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && translation == new Vector3(6, 9, 0));
            H.Check(prefix + "_ReappliedCenterPoint", visual.CenterPoint == new Vector3(8, 4, 0));
            H.Check(prefix + "_ReappliedOpacity", Math.Abs(visual.Opacity - 0.3) < 0.001);
            reconciler.Update(reapplied, empty, target, () => { });
            await Harness.Render();
            H.Check(prefix + "_SecondReset", visual.Scale == Vector3.One && visual.RotationAngle == 0
                && visual.CenterPoint == Vector3.Zero
                && visual.Properties.TryGetVector3("Translation", out translation)
                    == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && translation == Vector3.Zero
                && Math.Abs(visual.Opacity - restoredOpacity) < 0.001);
            foreach (string property in properties)
                H.Check(prefix + "_Second" + property + "Stopped", visual.TryGetAnimationController(property) is null);
        }
        H.SetContent(null);
    }
}

/// <summary>Releases removed facade writes before another property starts animating.</summary>
internal sealed class TransformModifierTransitionsFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        string[] properties = ["Scale", "Rotation", "Translation", "CenterPoint"];
        foreach (string property in properties)
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Removed facade")).Width(120).Height(40);
            Element Configure(float value) => property switch
            {
                "Scale" => plain.Scale(new Vector3(value, value, 1)),
                "Rotation" => plain.Rotation(value * 15),
                "Translation" => plain.Translation(value * 8, value * 4, 0),
                _ => plain.CenterPoint(new Vector3(value * 10, value * 5, 0)),
            };
            var before = Configure(1.5f);
            var after = plain.Opacity(0.4);
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            string name = $"TransformTransition_{property}_RemoveWithOpacity";
            try
            {
                AnimationScope.WithAnimation(Curve.Linear(60000),
                    () => reconciler.Update(before, after, target, () => { }));
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check(name + "_AnimationStarted", visual.TryGetAnimationController("Opacity") is not null);
                H.Check(name + "_NotPoolableBeforeUnset", ElementPool.IsVisualTaken(target));
                reconciler.Update(after, plain, target, () => { });
                await Harness.Render();
                H.Check(name + "_Reset", visual.Scale == Vector3.One && visual.RotationAngle == 0
                    && visual.CenterPoint == Vector3.Zero
                    && target.Translation == Vector3.Zero
                    && visual.TryGetAnimationController("Opacity") is null);
            }
            catch (Exception ex)
            {
                H.Check(name + "_Transition", false, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally { H.SetContent(null); }
        }

        foreach (string property in properties)
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("External visual")).Width(120).Height(40);
            var before = property switch
            {
                "Scale" => plain.Scale(1),
                "Rotation" => plain.Rotation(0),
                "Translation" => plain.Translation(0, 0, 0),
                _ => plain.CenterPoint(Vector3.Zero),
            };
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check($"TransformExternal_{property}_NotMarked", !ElementPool.IsVisualTaken(target));
            switch (property)
            {
                case "Scale": visual.Scale = new Vector3(1.7f); break;
                case "Rotation": visual.RotationAngleInDegrees = 25; break;
                case "Translation":
                    ElementCompositionPreview.SetIsTranslationEnabled(target, true);
                    visual.Properties.InsertVector3("Translation", new Vector3(8, 4, 0));
                    break;
                case "CenterPoint": visual.CenterPoint = new Vector3(7, 9, 0); break;
            }
            reconciler.Update(before, plain, target, () => { });
            await Harness.Render();
            H.Check($"TransformExternal_{property}_Reset", property switch
            {
                "Scale" => visual.Scale == new Vector3(1.7f),
                "Rotation" => Math.Abs(visual.RotationAngleInDegrees - 25) < 0.001,
                "Translation" => visual.Properties.TryGetVector3("Translation", out var offset)
                    == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded && offset == new Vector3(8, 4, 0)
                    && target.Translation == Vector3.Zero,
                _ => visual.CenterPoint == new Vector3(7, 9, 0),
            });
            H.SetContent(null);
        }

        using var loopingReconciler = new Reconciler();
        var plainTarget = Border(TextBlock("Looping Scale"));
        var animated = plainTarget.Animate(Curve.Linear(60000), AnimateProperty.Scale);
        static KeyframeBuilder Pulse(KeyframeBuilder kf) => kf.Loop().Duration(60000)
            .At(0, scale: Vector3.One).At(1, scale: new Vector3(1.5f, 1.5f, 1));
        var looping = animated.Keyframes("pulse", 1, Pulse);
        var initialScale = plainTarget.Scale(2);
        var scaled = looping.Scale(2);
        var loopingTarget = (Border)loopingReconciler.Mount(animated, () => { })!;
        H.SetContent(loopingTarget);
        await Harness.Render();
        var loopingVisual = ElementCompositionPreview.GetElementVisual(loopingTarget);
        // Leave a non-default base value behind the loop so a missing reset is observable.
        loopingReconciler.Update(animated, initialScale, loopingTarget, () => { });
        H.Check("TransformKeyframes_NonDefaultScaleBase", loopingVisual.Scale == new Vector3(2, 2, 1));
        loopingReconciler.Update(initialScale, scaled, loopingTarget, () => { });
        await Harness.Render();
        var controller = loopingVisual.TryGetAnimationController("Scale");
        H.Check("TransformKeyframes_ScaleLoopStarted", controller is not null);
        if (controller is not null)
        {
            controller.Pause();
            controller.Progress = 0.5f;
        }
        H.Check("TransformKeyframes_SameControl", loopingReconciler.Update(scaled, looping, loopingTarget, () => { }) is null);
        var retained = loopingVisual.TryGetAnimationController("Scale");
        H.Check("TransformKeyframes_ScaleLoopRetained",
            retained is not null && Math.Abs(retained.Progress - 0.5f) < 0.001f,
            $"progress={retained?.Progress}");
        loopingReconciler.Update(looping, plainTarget, loopingTarget, () => { });
        H.Check("TransformKeyframes_ScaleResetAfterLoopRemoval", loopingVisual.Scale == Vector3.One
            && loopingVisual.TryGetAnimationController("Scale") is null, $"scale={loopingVisual.Scale}");

        loopingReconciler.Update(plainTarget, scaled, loopingTarget, () => { });
        var restarted = loopingVisual.TryGetAnimationController("Scale");
        if (restarted is not null)
        {
            restarted.Pause();
            restarted.Progress = 0.5f;
        }
        var rescaled = scaled.Scale(3);
        loopingReconciler.Update(scaled, rescaled, loopingTarget, () => { });
        var replacement = loopingVisual.TryGetAnimationController("Scale");
        H.Check("TransformKeyframes_ScaleModifierReplacedLoop", restarted is not null
            && replacement is not null && replacement.Progress < 0.1f, $"progress={replacement?.Progress}");
        loopingReconciler.Update(rescaled, looping, loopingTarget, () => { });
        bool hasModifierAnimation = loopingVisual.TryGetAnimationController("Scale") is not null;
        H.Check("TransformKeyframes_ScaleModifierReset", loopingVisual.Scale == Vector3.One && !hasModifierAnimation,
            $"scale={loopingVisual.Scale}, animation={hasModifierAnimation}");
        loopingReconciler.Update(looping, plainTarget, loopingTarget, () => { });

        var fading = animated.Keyframes("fade", 1, kf => kf.Loop().Duration(60000)
            .At(0, opacity: 1).At(1, opacity: 0.5f));
        foreach (bool deferReset in new[] { false, true })
        {
            string name = deferReset ? "TransformKeyframes_DeferredPartial" : "TransformKeyframes_Partial";
            // The retained standby entry already lost the Scale channel to pulse.
            var remaining = deferReset ? fading.Keyframes("standby", 1, Pulse) : fading;
            var withPulse = remaining.Keyframes("pulse", 1, Pulse);
            var before = withPulse.Scale(2);
            loopingReconciler.Update(plainTarget, initialScale, loopingTarget, () => { });
            H.Check(name + "NonDefaultScaleBase", loopingVisual.Scale == new Vector3(2, 2, 1));
            loopingReconciler.Update(initialScale, before, loopingTarget, () => { });
            await Harness.Render();
            var fade = loopingVisual.TryGetAnimationController("Opacity");
            H.Check(name + "LoopsStarted", loopingVisual.TryGetAnimationController("Scale") is not null && fade is not null);
            if (fade is not null)
            {
                fade.Pause();
                fade.Progress = 0.5f;
            }
            if (deferReset)
            {
                loopingReconciler.Update(before, withPulse, loopingTarget, () => { });
                H.Check(name + "PulseStillPlaying", loopingVisual.TryGetAnimationController("Scale") is not null);
                before = withPulse;
                // Keyframe-only changes can be shallow-skipped (#1536). Drive Update through a layout change.
                remaining = remaining.Width(120);
            }
            H.Check(name + "SameControl", loopingReconciler.Update(before, remaining, loopingTarget, () => { }) is null);
            bool pulseRetained = loopingVisual.TryGetAnimationController("Scale") is not null;
            H.Check(name + "ScaleReset", loopingVisual.Scale == Vector3.One && !pulseRetained,
                $"scale={loopingVisual.Scale}, pulse={pulseRetained}");
            var retainedFade = loopingVisual.TryGetAnimationController("Opacity");
            H.Check(name + "FadeRetained", retainedFade is not null && Math.Abs(retainedFade.Progress - 0.5f) < 0.001f,
                $"progress={retainedFade?.Progress}");
            var readded = withPulse.Width(121);
            loopingReconciler.Update(remaining, readded, loopingTarget, () => { });
            await Harness.Render();
            H.Check(name + "PulseRestartsWithSameTrigger", loopingVisual.TryGetAnimationController("Scale") is not null);
            loopingReconciler.Update(readded, plainTarget, loopingTarget, () => { });
        }

        static KeyframeBuilder OpacityPulse(KeyframeBuilder kf) => kf.Loop().Duration(60000)
            .At(0, opacity: 1).At(1, opacity: 0.5f);
        var opacityAtOne = animated.Keyframes("pulse", 1, OpacityPulse);
        var opacityAtTwo = animated.Keyframes("pulse", 2, OpacityPulse);
        foreach (var (scenario, next, replaysOpacity) in new[]
        {
            ("PulseTriggerUnchanged", opacityAtOne, false),
            ("PulseTriggerChanged", opacityAtTwo, true),
            ("RepeatedPulseTriggerUnchanged", opacityAtOne.Keyframes("pulse", 1, OpacityPulse), false),
            ("RepeatedPulseTriggerChanged", opacityAtOne.Keyframes("pulse", 2, OpacityPulse), true),
            ("RepeatedPulseSkipsSameTriggerScale", opacityAtTwo.Keyframes("pulse", 2, Pulse), true),
        })
        {
            string name = "TransformKeyframes_" + scenario;
            loopingReconciler.Update(plainTarget, initialScale, loopingTarget, () => { });
            H.Check(name + "_NonDefaultScaleBase", loopingVisual.Scale == new Vector3(2, 2, 1));
            loopingReconciler.Update(initialScale, scaled, loopingTarget, () => { });
            await Harness.Render();
            var scaleLoop = loopingVisual.TryGetAnimationController("Scale");
            H.Check(name + "_ScaleLoopStarted", scaleLoop is not null);
            if (scaleLoop is not null)
            {
                scaleLoop.Pause();
                scaleLoop.Progress = 0.5f;
            }

            H.Check(name + "_SameControl", loopingReconciler.Update(scaled, next, loopingTarget, () => { }) is null);
            await Harness.Render();
            var currentScale = loopingVisual.TryGetAnimationController("Scale");
            if (replaysOpacity)
                H.Check(name + "_ScaleReset", loopingVisual.Scale == Vector3.One && currentScale is null,
                    $"scale={loopingVisual.Scale}, loop={currentScale is not null}");
            else
                H.Check(name + "_ScaleLoopRetained", currentScale is not null && Math.Abs(currentScale.Progress - 0.5f) < 0.001f,
                    $"progress={currentScale?.Progress}");
            H.Check(name + "_OpacityMatchesTrigger",
                (loopingVisual.TryGetAnimationController("Opacity") is not null) == replaysOpacity);
            loopingReconciler.Update(next, plainTarget, loopingTarget, () => { });
        }
        H.SetContent(null);

        ControlRegistry.RegisterDecorator<TransformModifierDecoratorFixture.Forwarder>(static () => new TransformModifierDecoratorFixture.ForwarderHandler());
        foreach (var (scenario, removeChildPulse, childWritesScale, useForwarder) in new[]
        {
            ("FlyoutChildAdvancesTrigger", false, false, false),
            ("FlyoutChildDropsPulse", true, false, false),
            ("FlyoutChildWritesScale", false, true, false),
            ("ForwarderChildAdvancesTrigger", false, false, true),
        })
        {
            using var reconciler = new Reconciler();
            string name = "TransformKeyframes_" + scenario;
            var leaf = Border(TextBlock("Shared Keyframes"));
            var fadeOnly = leaf.Keyframes("fade", 0, OpacityPulse);
            var beforeChild = removeChildPulse ? fadeOnly.Keyframes("pulse", 1, OpacityPulse) : leaf;
            var afterChild = (removeChildPulse ? fadeOnly : leaf.Keyframes("pulse", 1, OpacityPulse)).Width(120);
            if (childWritesScale)
                afterChild = afterChild.Set(static control =>
                {
                    var childVisual = ElementCompositionPreview.GetElementVisual(control);
                    var implicitAnimations = childVisual.ImplicitAnimations;
                    childVisual.ImplicitAnimations = null;
                    childVisual.Scale = new Vector3(3, 3, 1);
                    childVisual.ImplicitAnimations = implicitAnimations;
                });
            var menu = TextBlock("Menu");
            var curve = Curve.Linear(60000);
            Element Decorate(Element child) => useForwarder ? new TransformModifierDecoratorFixture.Forwarder(child) : Flyout(child, menu);
            Element Wrap(Element child) => Decorate(child).Animate(curve, AnimateProperty.Scale);

            var initial = Wrap(beforeChild);
            var seed = Decorate(beforeChild).Scale(2);
            var before = initial.Scale(2).Keyframes("pulse", 2, Pulse);
            var after = Wrap(afterChild).Keyframes("pulse", 2, removeChildPulse ? Pulse : OpacityPulse);
            var target = (Border)reconciler.Mount(initial, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            var visual = ElementCompositionPreview.GetElementVisual(target);
            reconciler.Update(initial, seed, target, () => { });
            H.Check(name + "_NonDefaultScaleBase", visual.Scale == new Vector3(2, 2, 1));
            reconciler.Update(seed, before, target, () => { });
            await Harness.Render();
            var scale = visual.TryGetAnimationController("Scale");
            H.Check(name + "_ScaleLoopStarted", scale is not null);
            if (scale is not null)
            {
                scale.Pause();
                scale.Progress = 0.5f;
            }
            H.Check(name + "_SameControl", reconciler.Update(before, after, target, () => { }) is null && target.Width == 120);
            await Harness.Render();
            var currentScale = visual.TryGetAnimationController("Scale");
            if (removeChildPulse)
                H.Check(name + "_ScaleLoopRetained", currentScale is not null && Math.Abs(currentScale.Progress - 0.5f) < 0.001f,
                    $"progress={currentScale?.Progress}");
            else
                H.Check(name + (childWritesScale ? "_SetterPreserved" : "_ScaleReset"),
                    currentScale is null && visual.Scale == (childWritesScale ? new Vector3(3, 3, 1) : Vector3.One)
                    && visual.TryGetAnimationController("Opacity") is not null,
                    $"scale={visual.Scale}, loop={currentScale is not null}");
            reconciler.Update(after, Decorate(leaf), target, () => { });
            H.SetContent(null);
        }

        using var opacityReconciler = new Reconciler();
        var opacityTarget = Border(TextBlock("Looping Opacity"));
        var opacityAnimated = opacityTarget.Animate(Curve.Linear(60000), AnimateProperty.Opacity);
        var opacityLoop = opacityAnimated.Keyframes("fade", 1, OpacityPulse);
        var withOpacity = opacityLoop.Opacity(0.4);
        var opacityControl = (Border)opacityReconciler.Mount(opacityAnimated, () => { })!;
        opacityControl.Style = new Style(typeof(Border)) { Setters = { new Setter(UIElement.OpacityProperty, 0.65d) } };
        H.SetContent(opacityControl);
        await Harness.Render();
        var opacityVisual = ElementCompositionPreview.GetElementVisual(opacityControl);
        // Seed a base value that differs from the style so the deferred reset is observable.
        var opacityImplicit = opacityVisual.ImplicitAnimations;
        opacityVisual.ImplicitAnimations = null;
        opacityVisual.Opacity = 0.4f;
        opacityVisual.ImplicitAnimations = opacityImplicit;
        H.Check("TransformKeyframes_OpacityNonDefaultBase", Math.Abs(opacityVisual.Opacity - 0.4f) < 0.001f);
        opacityReconciler.Update(opacityAnimated, withOpacity, opacityControl, () => { });
        await Harness.Render();
        var opacityController = opacityVisual.TryGetAnimationController("Opacity");
        H.Check("TransformKeyframes_OpacityLoopStarted", opacityController is not null);
        if (opacityController is not null)
        {
            opacityController.Pause();
            opacityController.Progress = 0.5f;
        }
        opacityReconciler.Update(withOpacity, opacityLoop, opacityControl, () => { });
        await Harness.Render();
        var retainedOpacity = opacityVisual.TryGetAnimationController("Opacity");
        H.Check("TransformKeyframes_OpacityLoopRetained", retainedOpacity is not null
            && Math.Abs(retainedOpacity.Progress - 0.5f) < 0.001f, $"progress={retainedOpacity?.Progress}");
        H.Check("TransformKeyframes_OpacityStyleReleased", opacityControl.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue
            && Math.Abs(opacityControl.Opacity - 0.65) < 0.001);
        opacityReconciler.Update(opacityLoop, opacityTarget, opacityControl, () => { });
        H.Check("TransformKeyframes_OpacityResetAfterLoopRemoval", opacityVisual.TryGetAnimationController("Opacity") is null
            && Math.Abs(opacityVisual.Opacity - 0.65f) < 0.001f, $"opacity={opacityVisual.Opacity}");
        H.SetContent(null);
    }
}

/// <summary>All departing facades must be cleared before any new animated write, including nested targets.</summary>
internal sealed class TransformModifierOrderingFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        foreach (bool nested in new[] { false, true })
        foreach (bool removeScale in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Removal ordering")).Width(120).Height(40);
            Element Scale(Element element, float value) => element.Scale(value);
            Element Center(Element element, float value) => element.CenterPoint(new Vector3(value, value, 0));
            Element Wrap(Element element) => nested ? Flyout(element, TextBlock("Menu")) : element;
            var before = removeScale ? Scale(Wrap(plain), 1.5f) : Center(Wrap(plain), 10);
            var after = Wrap(removeScale ? Center(plain, 20) : Scale(plain, 2));
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            string name = $"TransformOrdering_{nested}_{removeScale}";
            AnimationScope.WithAnimation(Curve.Linear(60000), () =>
                H.Check(name + "_SameControl", reconciler.Update(before, after, target, () => { }) is null));
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check(name + "_OldValueReset", removeScale ? visual.Scale == Vector3.One : visual.CenterPoint == Vector3.Zero);
            string added = removeScale ? "CenterPoint" : "Scale";
            H.Check(name + "_NewAnimationStarted", visual.TryGetAnimationController(added) is not null);
            reconciler.Update(after, Wrap(plain), target, () => { });
            H.Check(name + "_NewAnimationStopped", visual.TryGetAnimationController(added) is null);
            H.SetContent(null);
        }

        foreach (bool nested in new[] { false, true })
        foreach (string property in new[] { "Scale", "Rotation", "Translation", "CenterPoint" })
        {
            using var reconciler = new Reconciler();
            var plain = Border(TextBlock("Setter after removal")).Width(120).Height(40);
            Element Wrap(Element element) => nested ? Flyout(element, TextBlock("Menu")) : element;
            var before = property switch
            {
                "Scale" => Wrap(plain).Scale(2),
                "Rotation" => Wrap(plain).Rotation(20),
                "Translation" => Wrap(plain).Translation(10, 5, 0),
                _ => Wrap(plain).CenterPoint(new Vector3(10, 5, 0)),
            };
            var after = Wrap(plain.Set(control =>
            {
                switch (property)
                {
                    case "Scale": control.Scale = new Vector3(3); break;
                    case "Rotation": control.Rotation = 30; break;
                    case "Translation": control.Translation = new Vector3(15, 7, 0); break;
                    case "CenterPoint": control.CenterPoint = new Vector3(15, 7, 0); break;
                }
            }));
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            Exception? thrown = null;
            try { reconciler.Update(before, after, target, () => { }); }
            catch (Exception ex) { thrown = ex; }
            // Spec 066 §9 row 1: the transform took the visual, which locks Rotation, Scale and
            // CenterPoint for the control's lifetime. Translation stays writable, and with
            // translation enabled by the departed .Translation() the facade value is ignored on
            // screen, but it isn't reset either.
            H.Check($"TransformOrdering_{nested}_{property}_SetterAfterRemoval", property switch
            {
                "Scale" => thrown is UnauthorizedAccessException,
                "Rotation" => thrown is UnauthorizedAccessException,
                "Translation" => thrown is null && target.Translation == new Vector3(15, 7, 0),
                _ => thrown is UnauthorizedAccessException,
            }, thrown?.GetType().Name ?? "none");
            H.SetContent(null);
        }
    }
}

internal sealed class TransformModifierDecoratorFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        ControlRegistry.RegisterDecorator<Forwarder>(static () => new ForwarderHandler());
        ControlRegistry.RegisterDecorator<OpaqueForwarder>(static () => new OpaqueForwarderHandler());
        foreach (bool legacy in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            var after = Flyout(Border(TextBlock("Unrelated target")).Scale(2), TextBlock("Menu"));
            if (legacy)
                reconciler.RegisterType<FlyoutElement, Border>(static (_, _, _) => new Border(), static (_, _, _, _, _) => null);
            else
                reconciler.RegisterHandler<FlyoutElement, Border>(new IndependentFlyoutHandler());

            var before = after.Scale(3);
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            string name = $"TransformDecorator_PerHostFlyout_{(legacy ? "RegisterType" : "RegisterHandler")}";
            H.Check(name + "_OuterScaleApplied", target.Child is null && Effective.Scale(target) == new Vector3(3, 3, 1));
            H.Check(name + "_SameControl", reconciler.Update(before, after, target, () => { }) is null);
            H.Check(name + "_ScaleReset", Effective.Scale(target) == Vector3.One, $"scale={Effective.Scale(target)}");
            H.SetContent(null);
        }
        using (var reconciler = new Reconciler())
        {
            var leaf = TextBlock("Plain memo output") with { CallSite = null };
            var memo = Memo(1, () => leaf);
            var direct = (TextBlock)reconciler.Mount(leaf, static () => { })!;
            var wrapped = (TextBlock)reconciler.Mount(memo, static () => { })!;
            H.Check("TransformMountedOwner_PlainLeafUntagged", !Reconciler.TryGetReactorState(direct, out _));
            H.Check("TransformMountedOwner_PlainMemoUntagged", !Reconciler.TryGetReactorState(wrapped, out var state),
                $"targets={state?.ModifierTargets?.Count}");
        }
        foreach (string wrapper in new[] { "Memo", "Forwarder", "Nested" })
        foreach (string property in new[] { "Scale", "Rotation", "Translation", "CenterPoint", "Opacity" })
        {
            using var reconciler = new Reconciler();
            Element Configure(Element value, float amount) => property switch
            {
                "Scale" => value.Scale(amount),
                "Rotation" => value.Rotation(amount * 15),
                "Translation" => value.Translation(amount * 8, amount * 4, 0),
                "CenterPoint" => value.CenterPoint(new Vector3(amount * 10, amount * 5, 0)),
                _ => value.Opacity(amount / 4),
            };
            Element plain = Border(TextBlock("Mounted modifier owner"));
            Element inner = Configure(plain, 2);
            int factoryCalls = 0;
            Element Wrap(bool retained = true) => wrapper switch
            {
                "Memo" => Memo(1, () => { factoryCalls++; return inner; }),
                "Forwarder" => new OpaqueForwarder(retained ? inner : plain),
                _ => new OpaqueForwarder(retained ? new OpaqueForwarder(inner).Scale(1.5f) : new OpaqueForwarder(plain)),
            };
            var before = Configure(Wrap(), 3);
            var unchanged = before;
            var after = Wrap();
            var tree = VStack(before).Width(120);
            var parent = (Panel)reconciler.Mount(tree, () => { })!;
            var target = (Border)parent.Children[0];
            H.SetContent(parent);
            await Harness.Render();
            int skipped = reconciler.DebugElementsSkipped;
            reconciler.Update(tree, VStack(unchanged).Width(121), parent, () => { });
            H.Check($"TransformMountedOwner_{wrapper}_{property}_ParentSkippedChild", reconciler.DebugElementsSkipped > skipped);
            var replacement = reconciler.Update(unchanged, after, target, () => { });
            await Harness.Render();
            float expectedScale = wrapper == "Nested" ? 1.5f : 2;
            bool correct = property switch
            {
                "Scale" => Effective.Scale(target) == new Vector3(expectedScale, expectedScale, 1),
                "Rotation" => Effective.Rotation(target) == 30,
                "Translation" => Effective.Translation(target) == new Vector3(16, 8, 0),
                "CenterPoint" => Effective.CenterPoint(target) == new Vector3(20, 10, 0),
                _ => Math.Abs(target.Opacity - 0.5) < 0.001,
            };
            string name = $"TransformMountedOwner_{wrapper}_{property}";
            H.Check(name + "_Retained", correct,
                $"scale={Effective.Scale(target)},rotation={Effective.Rotation(target)},translation={Effective.Translation(target)},center={Effective.CenterPoint(target)},opacity={target.Opacity}");
            H.Check(name + "_SameControl", replacement is null || ReferenceEquals(replacement, target));
            H.Check(name + "_FactoryCalls", factoryCalls == (wrapper == "Memo" ? 1 : 0), $"calls={factoryCalls}");
            if (wrapper != "Memo")
            {
                reconciler.Update(after, Wrap(false), target, () => { });
                H.Check(name + "_LastOwnerRemoved", Effective.Scale(target) == Vector3.One && Effective.Rotation(target) == 0
                    && Effective.Translation(target) == Vector3.Zero && Effective.CenterPoint(target) == Vector3.Zero && target.Opacity == 1);
            }
            H.SetContent(null);
        }
        foreach (bool nested in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element Wrap(Element value) => new OpaqueForwarder(nested ? new OpaqueForwarder(value) : value);
            var before = Wrap(Border(TextBlock("Old target")).Opacity(0.5)).Opacity(0.75);
            var after = Wrap(TextBlock("New target").Opacity(0.25));
            var target = reconciler.Mount(before, () => { })!;
            var replacement = reconciler.Update(before, after, target, () => { });
            H.Check($"TransformMountedOwner_ReplacedTarget_{nested}", replacement is TextBlock { Opacity: 0.25 },
                $"opacity={(replacement as UIElement)?.Opacity}");
        }
        using (var reconciler = new Reconciler())
        {
            // Expander owns its own native root and only hosts this ordinary child.
            var child = TextBlock("Independent child") with { CallSite = null };
            var expander = (Expander)reconciler.Mount(Expander("Header", child), () => { })!;
            var content = (TextBlock)expander.Content;
            H.Check("TransformMountedOwner_IndependentChildUntagged", !Reconciler.TryGetReactorState(content, out _));
        }
        using (var reconciler = new Reconciler())
        {
            var leaf = Border(TextBlock("Late declaration"));
            Element Wrap(Element value) => new OpaqueForwarder(new OpaqueForwarder(value));
            var before = Wrap(leaf);
            var added = Wrap(leaf.Scale(2)).Scale(3);
            var target = (Border)reconciler.Mount(before, static () => { })!;
            H.Check("TransformMountedOwner_PlainForwarderUntracked",
                Reconciler.TryGetReactorState(target, out var state) && state.ModifierTargets is null);
            reconciler.Update(before, added, target, static () => { });
            H.Check("TransformMountedOwner_LateOuterScaleApplied", Effective.Scale(target) == new Vector3(3, 3, 1));
            reconciler.Update(added, Wrap(leaf.Scale(2)), target, static () => { });
            H.Check("TransformMountedOwner_LateInnerScaleRetained", Effective.Scale(target) == new Vector3(2, 2, 1));
        }
        await CheckMemoResourceRefresh();
        foreach (string property in new[] { "Scale", "Rotation", "Translation", "CenterPoint" })
        foreach (bool removeOuter in new[] { true, false })
        foreach (bool custom in new[] { false, true })
        foreach (string legacy in new[] { "None", "Outer", "Target" })
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Decorator transforms")).Width(120).Height(40);
            Element Configure(Element element, float value) => property switch
            {
                "Scale" => element.Scale(new Vector3(value, value, 1)),
                "Rotation" => element.Rotation(value * 15),
                "Translation" => element.Translation(value * 8, value * 4, 0),
                _ => element.CenterPoint(new Vector3(value * 10, value * 5, 0)),
            };
            Element Wrap(Element inner)
            {
                if (legacy == "Target")
                    inner = new ModifiedElement(inner with { Modifiers = null }, inner.Modifiers ?? new());
                return custom ? new Forwarder(inner) : Flyout(inner, TextBlock("Menu"));
            }
            Element before = Configure(Wrap(removeOuter ? plain : Configure(plain, 1.5f)), 2);
            Element after = removeOuter ? Wrap(Configure(plain, 1.5f)) : Configure(Wrap(plain), 2);
            if (legacy == "Outer")
            {
                before = new ModifiedElement(before with { Modifiers = null }, before.Modifiers ?? new());
                after = new ModifiedElement(after with { Modifiers = null }, after.Modifiers ?? new());
            }
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            reconciler.Update(before, after, target, () => { });
            await Harness.Render();
            float expected = removeOuter ? 1.5f : 2;
            bool correct = property switch
            {
                "Scale" => Effective.Scale(target) == new Vector3(expected, expected, 1),
                "Rotation" => Effective.Rotation(target) == expected * 15,
                "Translation" => Effective.Translation(target) == new Vector3(expected * 8, expected * 4, 0),
                _ => Effective.CenterPoint(target) == new Vector3(expected * 10, expected * 5, 0),
            };
            H.Check($"TransformDecorator_{property}_{custom}_{legacy}_{removeOuter}", correct,
                $"scale={Effective.Scale(target)},rotation={Effective.Rotation(target)},translation={Effective.Translation(target)},center={Effective.CenterPoint(target)}");
            H.SetContent(null);
        }

        foreach (string property in new[] { "Scale", "Opacity" })
        foreach (bool custom in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Inherited animation")).Width(120).Height(40);
            Element Configure(Element value, bool initial) => property == "Scale"
                ? value.Scale(initial ? 2 : 1.5f) : value.Opacity(initial ? 0.2 : 0.7);
            Element Wrap(Element inner) => custom ? new Forwarder(inner) : Flyout(inner, TextBlock("Menu"));
            var before = Configure(Wrap(plain), true).Animate(Curve.Linear(60000));
            var after = Wrap(Configure(plain, false)).Animate(Curve.Linear(60000));
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            reconciler.Update(before, after, target, () => { });
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check($"TransformDecorator_Inherited_{property}_{custom}", visual.TryGetAnimationController(property) is not null);
            reconciler.Update(after, Wrap(plain), target, () => { });
            H.Check($"TransformDecorator_Inherited_{property}_{custom}_Removed", visual.TryGetAnimationController(property) is null);
            H.SetContent(null);
        }

        foreach (string property in new[] { "Scale", "Opacity" })
        foreach (bool custom in new[] { false, true })
        foreach (bool ambient in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Narrowed inherited animation")).Width(120).Height(40);
            Element Configure(Element value, bool outer) => property == "Scale"
                ? value.Scale(outer ? 2 : 1.5f) : value.Opacity(outer ? 0.2 : 0.7);
            Element Wrap(Element inner) => custom ? new Forwarder(inner) : Flyout(inner, TextBlock("Menu"));
            var inner = Configure(plain, false);
            var before = Configure(Wrap(inner.Animate(Curve.Linear(60000))), true)
                .Animate(Curve.Linear(60000), AnimateProperty.Scale | AnimateProperty.Opacity);
            var after = Wrap(inner).Animate(Curve.Linear(60000),
                property == "Scale" ? AnimateProperty.Opacity : AnimateProperty.Scale);
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            var visual = ElementCompositionPreview.GetElementVisual(target);
            string name = $"TransformDecorator_Narrowed_{property}_{custom}_{ambient}";
            H.Check(name + "_ChannelInitiallyPresent", visual.ImplicitAnimations?.ContainsKey(property) == true);
            void Update() => reconciler.Update(before, after, target, () => { });
            if (ambient) AnimationScope.WithAnimation(Curve.Linear(60000), Update);
            else Update();
            await Harness.Render();
            H.Check(name + "_AnimationMatchesScope", (visual.TryGetAnimationController(property) is not null) == ambient);
            if (!ambient)
                H.Check(name + "_Immediate", property == "Scale"
                    ? visual.Scale == new Vector3(1.5f, 1.5f, 1)
                    : Math.Abs(visual.Opacity - 0.7f) < 0.001, $"scale={visual.Scale},opacity={visual.Opacity}");
            H.Check(name + "_ChannelRemoved", visual.ImplicitAnimations?.ContainsKey(property) != true);
            H.SetContent(null);
        }

        foreach (bool custom in new[] { false, true })
        foreach (bool removeOuter in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element plain = Border(TextBlock("Opacity owners")).Width(120).Height(40);
            Element Wrap(Element inner) => custom ? new Forwarder(inner) : Flyout(inner, TextBlock("Menu"));
            Element Animated(Element element, double value) => element.Opacity(value).Animate(Curve.Linear(60000));
            var before = Animated(Wrap(removeOuter ? plain : Animated(plain, 0.7)), 0.2);
            var after = removeOuter ? Wrap(Animated(plain, 0.7)) : Animated(Wrap(plain), 0.2);
            var target = (Border)reconciler.Mount(before, () => { })!;
            H.SetContent(target);
            await Harness.Render();
            var visual = ElementCompositionPreview.GetElementVisual(target);
            string name = $"TransformDecorator_Opacity_{custom}_{removeOuter}";
            H.Check(name + "_Started", visual.TryGetAnimationController("Opacity") is not null);
            reconciler.Update(before, after, target, () => { });
            await Harness.Render();
            H.Check(name + "_Retained", visual.TryGetAnimationController("Opacity") is not null);
            reconciler.Update(after, Wrap(plain), target, () => { });
            await Harness.Render();
            H.Check(name + "_AllRemoved", visual.TryGetAnimationController("Opacity") is null
                && Math.Abs(visual.Opacity - 1) < 0.001);
            H.SetContent(null);
        }
    }

    private async Task CheckMemoResourceRefresh()
    {
        var parent = new Border();
        H.SetContent(parent);
        using var host = new ReactorHost(H.Window) { ContentTarget = parent };
        using var isolation = ThemeResourceListeners.IsolateForTest(host);
        int calls = 0;
        Element Output()
        {
            calls++;
            return new OpaqueForwarder(new OpaqueForwarder(Border(TextBlock("Memo refresh")).Scale(2)));
        }

        KeyedMemoElement memo = Memo(1, Output);
        var scaledMemo = memo.Scale(3);
        bool outer = true;
        host.Mount(_ => outer ? scaledMemo : memo);
        await host.WaitForIdleAsync();
        var target = (Border)parent.Child;
        int initialCalls = calls;
        for (int i = 0; i < 4; i++)
        {
            Theme.NotifyResourcesChanged();
            await host.WaitForIdleAsync();
        }
        H.Check("TransformMountedOwner_MemoRefreshReusedTarget", calls == initialCalls + 4
            && ReferenceEquals(parent.Child, target), $"calls={calls}, initial={initialCalls}");
        var count = Reconciler.GetOrCreateReactorState(target).ModifierTargets?.Count;
        H.Check("TransformMountedOwner_MemoRefreshBounded", count == 3, $"entries={count}");
        outer = false;
        host.RequestRender();
        await host.WaitForIdleAsync();
        H.Check("TransformMountedOwner_MemoRefreshRetained", ReferenceEquals(parent.Child, target)
            && Effective.Scale(target) == new Vector3(2, 2, 1) && calls == initialCalls + 4,
            $"scale={Effective.Scale(target)}, calls={calls}");
        H.SetContent(null);
    }

    internal sealed record Forwarder(Element Target) : Element;
    private sealed record OpaqueForwarder(Element Target) : Element;
    private sealed class IndependentFlyoutHandler : IElementHandler<FlyoutElement, Border>
    {
        public Border Mount(MountContext context, FlyoutElement element) => new();
        public void Update(UpdateContext context, FlyoutElement oldElement, FlyoutElement newElement, Border control) { }
    }

    private sealed class OpaqueForwarderHandler : IDecoratorElementHandler<OpaqueForwarder>
    {
        public UIElement Mount(MountContext context, OpaqueForwarder element) => context.MountChild(element.Target)!;
        public UIElement Update(UpdateContext context, OpaqueForwarder oldEl, OpaqueForwarder newEl, UIElement control)
            => context.ReconcileChild(oldEl.Target, newEl.Target, control)!;
        public V1UnmountDisposition Unmount(UnmountContext context, OpaqueForwarder? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;
    }
    internal sealed class ForwarderHandler : IDecoratorElementHandler<Forwarder>
    {
        public UIElement Mount(MountContext context, Forwarder element) => context.MountChild(element.Target)!;
        public UIElement Update(UpdateContext context, Forwarder oldElement, Forwarder newElement, UIElement control)
            => context.Reconciler.Update(oldElement.Target, newElement.Target, control, context.RequestRerender) ?? control;
        public V1UnmountDisposition Unmount(UnmountContext context, Forwarder? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;
        public Element? GetSourceTarget(Forwarder element) => element.Target;
    }
}

/// <summary>Property ownership and the pool's visual-taken marker both survive wrapper collection (#1643).</summary>
internal sealed class TransformCompositionOwnershipFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        foreach (string property in new[] { "Opacity", "Translation" })
        {
            using var reconciler = new Reconciler();
            Element plain = TextBlock("Composition ownership").Width(120).Height(40);
            Element Configure(double value) => property == "Opacity"
                ? plain.Opacity(value) : plain.Translation((float)value * 20, 7, 0);
            var active = Configure(0.4);
            var direct = Configure(0.7);
            string name = "TransformOwnership_" + property;
            var parent = (Border)reconciler.Mount(Border(plain), () => { })!;
            H.SetContent(parent);
            try
            {
                var tracked = StartAndMark(reconciler, parent, plain, active, name);
                await Harness.Render();
                await Task.Run(static () =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });

                var target = (FrameworkElement)parent.Child!;
                H.Check(name + "_FreshWrapper", !tracked.Wrappers.TryGetValue(target, out _));
                H.Check(name + "_VisualTakenSurvivesWrapper", ElementPool.IsVisualTaken(target));
                var flag = property == "Opacity" ? AnimateProperty.Opacity : AnimateProperty.Offset;
                var state = target.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState;
                H.Check(name + "_NativeOwnershipSurvives", state is not null
                    && ReferenceEquals(state, tracked.State) && (state.CompositionModifierProperties & flag) != 0);
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check(name + "_AnimationStillRunning", visual.TryGetAnimationController(property) is not null);

                reconciler.Update(active, direct, target, () => { });
                H.Check(name + "_DirectWriteReplacesAnimation", visual.TryGetAnimationController(property) is null);
                H.Check(name + "_DirectValue", property == "Opacity"
                    ? Math.Abs(visual.Opacity - 0.7f) < 0.001
                    : visual.Properties.TryGetVector3("Translation", out var translation)
                        == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded
                        && translation == new Vector3(14, 7, 0) && target.Translation == Vector3.Zero);
                H.Check(name + "_TaintMarkerRestored", ElementPool.IsVisualTaken(target));

                reconciler.Update(direct, plain, target, () => { });
                H.Check(name + "_OwnershipReleasedButTaintRemains", state is not null
                    && (state.CompositionModifierProperties & flag) == 0
                    && ElementPool.IsVisualTaken(target));
            }
            finally { H.SetContent(null); }
        }
    }

    // Keep the original wrapper out of the async state machine across collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (ConditionalWeakTable<FrameworkElement, object> Wrappers, Reconciler.ReactorState State) StartAndMark(
        Reconciler reconciler, Border parent, Element plain, Element active, string name)
    {
        var target = (FrameworkElement)parent.Child!;
        AnimationScope.WithAnimation(Curve.Linear(60000),
            () => reconciler.Update(plain, active, target, () => { }));
        H.Check(name + "_TaintMarkerBeforeCollection", ElementPool.IsVisualTaken(target));
        var wrappers = new ConditionalWeakTable<FrameworkElement, object>();
        wrappers.Add(target, new object());
        return (wrappers, (Reconciler.ReactorState)target.GetValue(Reconciler.ReactorAttached.StateProperty));
    }
}

/// <summary>
/// Spec 066 layer 4: a transform modifier writes the composition visual, so its value is read
/// there once the visual is taken, and from the facade (where user code may have written it) before.
/// </summary>
internal static class Effective
{
    private static Microsoft.UI.Composition.Visual? Taken(UIElement e)
        => ElementPool.IsVisualTaken(e) ? ElementCompositionPreview.GetElementVisual(e) : null;

    public static Vector3 Scale(UIElement e) => Taken(e)?.Scale ?? e.Scale;

    public static float Rotation(UIElement e)
        => Taken(e) is { } v ? MathF.Round(v.RotationAngleInDegrees, 3) : e.Rotation;

    public static Vector3 CenterPoint(UIElement e) => Taken(e)?.CenterPoint ?? e.CenterPoint;

    public static Vector3 Translation(UIElement e)
        => Taken(e) is { } v && v.Properties.TryGetVector3("Translation", out var t) == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded
            ? t + e.Translation : e.Translation;
}
