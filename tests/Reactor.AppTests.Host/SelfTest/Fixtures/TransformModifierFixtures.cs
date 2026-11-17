using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
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
            H.Check(prefix + "_ScaleApplied", target.Scale == active.Modifiers!.Scale);
            H.Check(prefix + "_RotationApplied", target.Rotation == 30);
            H.Check(prefix + "_TranslationApplied", target.Translation == active.Modifiers!.Translation);
            H.Check(prefix + "_CenterPointApplied", target.CenterPoint == active.Modifiers!.CenterPoint);
            H.Check(prefix + "_OpacityApplied", Math.Abs(target.Opacity - 0.4) < 0.001);
        }
        else
        {
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check(prefix + "_NotPoolableBeforeUnset", ElementPool.IsCompositorTainted(target));
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
            H.Check(prefix + "_ScaleReset", target.Scale == Vector3.One, $"scale={target.Scale}");
            H.Check(prefix + "_RotationReset", target.Rotation == 0, $"rotation={target.Rotation}");
            H.Check(prefix + "_TranslationReset", target.Translation == Vector3.Zero, $"translation={target.Translation}");
            H.Check(prefix + "_CenterPointReset", target.CenterPoint == Vector3.Zero, $"center={target.CenterPoint}");
            H.Check(prefix + "_NotHandedOut", !ElementPool.IsCompositorTainted(target));
            // Prove the reset did not disable the XAML implicit transition API.
            target.ScaleTransition = new Vector3Transition();
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
            H.Check(prefix + "_NotPoolable", ElementPool.IsCompositorTainted(target));
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
                H.Check(name + "_NotPoolableBeforeUnset", ElementPool.IsCompositorTainted(target));
                reconciler.Update(after, plain, target, () => { });
                await Harness.Render();
                H.Check(name + "_Reset", visual.Scale == Vector3.One && visual.RotationAngle == 0
                    && visual.CenterPoint == Vector3.Zero
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
            H.Check($"TransformExternal_{property}_NotMarked", !ElementPool.IsCompositorTainted(target));
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
            reconciler.Update(before, after, target, () => { });
            H.Check($"TransformOrdering_{nested}_{property}_SetterPreserved", property switch
            {
                "Scale" => target.Scale == new Vector3(3),
                "Rotation" => target.Rotation == 30,
                "Translation" => target.Translation == new Vector3(15, 7, 0),
                _ => target.CenterPoint == new Vector3(15, 7, 0),
            });
            H.SetContent(null);
        }
    }
}

internal sealed class TransformModifierDecoratorFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        ControlRegistry.RegisterDecorator<Forwarder>(static () => new ForwarderHandler());
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
                "Scale" => target.Scale == new Vector3(expected, expected, 1),
                "Rotation" => target.Rotation == expected * 15,
                "Translation" => target.Translation == new Vector3(expected * 8, expected * 4, 0),
                _ => target.CenterPoint == new Vector3(expected * 10, expected * 5, 0),
            };
            H.Check($"TransformDecorator_{property}_{custom}_{legacy}_{removeOuter}", correct,
                $"scale={target.Scale},rotation={target.Rotation},translation={target.Translation},center={target.CenterPoint}");
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

    private sealed record Forwarder(Element Target) : Element;
    private sealed class ForwarderHandler : IDecoratorElementHandler<Forwarder>
    {
        public UIElement Mount(MountContext context, Forwarder element) => context.MountChild(element.Target)!;
        public UIElement Update(UpdateContext context, Forwarder oldElement, Forwarder newElement, UIElement control)
            => context.Reconciler.Update(oldElement.Target, newElement.Target, control, context.RequestRerender) ?? control;
        public V1UnmountDisposition Unmount(UnmountContext context, Forwarder? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;
        public Element? GetSourceTarget(Forwarder element) => element.Target;
    }
}

/// <summary>Property ownership survives wrapper collection; the pool's wrapper-keyed marker does not.</summary>
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
                H.Check(name + "_TaintMarkerLost", !ElementPool.IsCompositorTainted(target));
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
                H.Check(name + "_TaintMarkerRestored", ElementPool.IsCompositorTainted(target));

                reconciler.Update(direct, plain, target, () => { });
                H.Check(name + "_OwnershipReleasedButTaintRemains", state is not null
                    && (state.CompositionModifierProperties & flag) == 0
                    && ElementPool.IsCompositorTainted(target));
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
        H.Check(name + "_TaintMarkerBeforeCollection", ElementPool.IsCompositorTainted(target));
        var wrappers = new ConditionalWeakTable<FrameworkElement, object>();
        wrappers.Add(target, new object());
        return (wrappers, (Reconciler.ReactorState)target.GetValue(Reconciler.ReactorAttached.StateProperty));
    }
}
