using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlAnim = Microsoft.UI.Xaml.Media.Animation;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class AnimateModifierTests
{
    internal class ImplicitAnimationsCreated(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Animated"))
                        .Animate(Curve.Spring(0.65f))
                        .AutomationId("animate-target")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "animate-target");

            H.Check("Animate_TargetMounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("Animate_HasImplicitAnimations",
                    visual.ImplicitAnimations is not null);

                if (visual.ImplicitAnimations is not null)
                {
                    var hasOpacity = false;
                    var hasScale = false;
                    var hasRotation = false;
                    try { var _ = visual.ImplicitAnimations["Opacity"]; hasOpacity = true; } catch { }
                    try { var _ = visual.ImplicitAnimations["Scale"]; hasScale = true; } catch { }
                    try { var _ = visual.ImplicitAnimations["RotationAngle"]; hasRotation = true; } catch { }

                    H.Check("Animate_HasOpacity", hasOpacity);
                    H.Check("Animate_HasScale", hasScale);
                    H.Check("Animate_HasRotation", hasRotation);
                }
            }
        }
    }

    internal class TargetedProperties(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Targeted"))
                        .Animate(Curve.Ease(200), AnimateProperty.Opacity | AnimateProperty.Scale)
                        .AutomationId("animate-targeted")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "animate-targeted");

            H.Check("AnimateTargeted_Mounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("AnimateTargeted_HasImplicit", visual.ImplicitAnimations is not null);

                if (visual.ImplicitAnimations is not null)
                {
                    var hasOpacity = false;
                    var hasScale = false;
                    var hasRotation = false;
                    try { var _ = visual.ImplicitAnimations["Opacity"]; hasOpacity = true; } catch { }
                    try { var _ = visual.ImplicitAnimations["Scale"]; hasScale = true; } catch { }
                    try { var _ = visual.ImplicitAnimations["RotationAngle"]; hasRotation = true; } catch { }

                    H.Check("AnimateTargeted_HasOpacity", hasOpacity);
                    H.Check("AnimateTargeted_HasScale", hasScale);
                    H.Check("AnimateTargeted_NoRotation", !hasRotation);
                }
            }
        }
    }

    internal class ConfigurationAcrossUpdates(Harness h, bool childSkip) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var reconciler = new Reconciler();
            Element Wrap(Element button) => childSkip ? VStack(button) : button;
            var current = Wrap(Button("Go").Animate(Curve.Ease(300), AnimateProperty.Opacity));
            var root = reconciler.Mount(current, static () => { })!;
            var button = childSkip ? (Button)((StackPanel)root).Children[0] : (Button)root;
            var name = childSkip ? "AnimateConfig_ChildSkip" : "AnimateConfig_Direct";
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(button);
                bool HasOpacityDuration(int milliseconds) =>
                    visual.ImplicitAnimations?.ContainsKey("Opacity") == true
                    && visual.ImplicitAnimations["Opacity"] is ScalarKeyFrameAnimation animation
                    && animation.Duration == TimeSpan.FromMilliseconds(milliseconds);

                void Update(ButtonElement nextButton, string step)
                {
                    // Only animation configuration changes; ordinary properties stay equal.
                    var next = Wrap(nextButton);
                    H.Check(name + step + "_SameRoot", reconciler.Update(current, next, root, static () => { }) is null);
                    if (childSkip)
                        H.Check(name + step + "_SameButton", ReferenceEquals(((StackPanel)root).Children[0], button));
                    current = next;
                }

                H.Check(name + "_InitialCurve", HasOpacityDuration(300));
                Update(Button("Go").Animate(Curve.Ease(600), AnimateProperty.Opacity), "_Change");
                H.Check(name + "_ChangedCurve", HasOpacityDuration(600));

                int skipped = reconciler.DebugElementsSkipped;
                Update(Button("Go").Animate(Curve.Ease(600), AnimateProperty.Opacity), "_Equal");
                H.Check(name + "_EqualConfigSkipped", reconciler.DebugElementsSkipped == skipped + 1);

                Update(Button("Go").Animate(Curve.Ease(600), AnimateProperty.Opacity | AnimateProperty.Scale), "_Expand");
                H.Check(name + "_ChangedProperties", visual.ImplicitAnimations?.ContainsKey("Scale") == true);
                Update(Button("Go").Animate(Curve.Ease(600), AnimateProperty.Opacity), "_Narrow");
                H.Check(name + "_DroppedScale", visual.ImplicitAnimations?.ContainsKey("Scale") != true);
                H.Check(name + "_KeptOpacity", HasOpacityDuration(600));
                Update(Button("Go"), "_Remove");
                H.Check(name + "_RemovedCurve", visual.ImplicitAnimations is null);
                Update(Button("Go").Animate(Curve.Ease(900), AnimateProperty.Opacity), "_Add");
                H.Check(name + "_AddedCurve", HasOpacityDuration(900));
            }
            finally
            {
                reconciler.UnmountChild(root);
                H.SetContent(null);
            }
        }
    }

    internal class ExtrasAcrossUpdates(Harness h, bool childSkip) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Transitions honour the system's animation setting (spec 066 §5.5); this checks their keys.
            using var motion = Microsoft.UI.Reactor.Navigation.TransitionEngine.OverrideAnimationsEnabled(true);
            using var reconciler = new Reconciler();
            var child = TextBlock("Content");
            Element Wrap(Element element) => childSkip ? VStack(element) : element;
            BorderElement Render(TimeSpan duration) => Border(child).LayoutAnimation(duration);
            var current = Wrap(Render(TimeSpan.FromMilliseconds(200)));
            var root = reconciler.Mount(current, static () => { })!;
            var border = childSkip ? (Border)((StackPanel)root).Children[0] : (Border)root;
            var name = childSkip ? "AnimationExtras_Child" : "AnimationExtras_Direct";
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(border);
                bool HasDuration(string property, int milliseconds) =>
                    visual.ImplicitAnimations?.ContainsKey(property) == true
                    && visual.ImplicitAnimations[property] is ScalarKeyFrameAnimation scalar
                        && scalar.Duration == TimeSpan.FromMilliseconds(milliseconds);
                bool HasOffsetDuration(int milliseconds) => visual.ImplicitAnimations?["Offset"]
                    is Vector3KeyFrameAnimation offset && offset.Duration == TimeSpan.FromMilliseconds(milliseconds);
                void Update(Element next)
                {
                    var wrapped = Wrap(next);
                    H.Check(name + "_SameControl", reconciler.Update(current, wrapped, root, static () => { }) is null);
                    current = wrapped;
                }

                H.Check(name + "_InitialLayout", HasOffsetDuration(200));
                Update(Render(TimeSpan.FromMilliseconds(800)));
                H.Check(name + "_ChangedLayout", HasOffsetDuration(800));
                Update(Border(child));
                H.Check(name + "_RemovedLayout", visual.ImplicitAnimations is null);

                Update(Border(child).OpacityTransition(TimeSpan.FromMilliseconds(200)));
                H.Check(name + "_InitialTransition", HasDuration("Opacity", 200));
                Update(Border(child).OpacityTransition(TimeSpan.FromMilliseconds(800)));
                H.Check(name + "_ChangedTransition", HasDuration("Opacity", 800));

                // Only compare scroll declarations here; no headless native placeholders.
                var viewer = new ScrollViewer();
                var expressions = new[] { new ScrollExpression("Opacity", "1") };
                var first = Border(child) with { ScrollAnimation = new ScrollAnimationConfig(viewer, expressions) };
                var equal = Border(child) with { ScrollAnimation = new ScrollAnimationConfig(viewer, expressions) };
                var changed = Border(child) with { ScrollAnimation = new ScrollAnimationConfig(viewer, [new("Opacity", "0")]) };
                H.Check(name + "_EqualScrollSkips", Element.CanSkipUpdate(first, equal));
                H.Check(name + "_ChangedScrollUpdates", !Element.CanSkipUpdate(first, changed));
                H.Check(name + "_RemovedScrollUpdates", !Element.CanSkipUpdate(first, Border(child)));
            }
            finally
            {
                reconciler.UnmountChild(root);
                H.SetContent(null);
            }
        }
    }

    /// <summary>
    /// Issue #1702: modifiers rebuild their WinUI transitions, brushes and arrays on every render,
    /// so an element declaring the same animations again must still skip, while a changed or
    /// removed declaration still updates (#1536).
    /// </summary>
    internal class RebuiltExtrasSkip(Harness h, bool childSkip) : SelfTestFixtureBase(h)
    {
        private readonly ScrollViewer _viewer = new();

        public override async Task RunAsync()
        {
            using var motion = Microsoft.UI.Reactor.Navigation.TransitionEngine.OverrideAnimationsEnabled(true);
            var name = childSkip ? "RebuiltExtras_Child" : "RebuiltExtras_Direct";
            await Reconciles(name);
            await ScrollRestarts(name);
            if (!childSkip) Declarations();
        }

        private Element Wrap(Element element) => childSkip ? VStack(element) : element;

        private ButtonElement Render(int ms = 200, byte red = 255, double entrance = 20, int trigger = 1, string expression = "0")
            => Button("Go")
                .OpacityTransition(TimeSpan.FromMilliseconds(ms))
                .RotationTransition(TimeSpan.FromMilliseconds(ms))
                .ScaleTransition(new Vector3Transition { Duration = TimeSpan.FromMilliseconds(ms), Components = Vector3TransitionComponents.X })
                .TranslationTransition(new Vector3Transition { Duration = TimeSpan.FromMilliseconds(ms) })
                .WithTransitions(new XamlAnim.EntranceThemeTransition { FromVerticalOffset = entrance }, new XamlAnim.RepositionThemeTransition())
                .InteractionStates(s => s.PointerOver(scale: 1.05f, background: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, red, 0, 0))))
                .Keyframes("pulse", trigger, static k => k.Duration(300).At(0f, scale: Vector3.One).At(1f, scale: new Vector3(1.1f)))
                .ScrollLinked(_viewer, s => s.Expression("CenterPoint.X", expression));

        private async Task Reconciles(string name)
        {
            using var reconciler = new Reconciler();
            var current = Wrap(Render());
            var root = reconciler.Mount(current, static () => { })!;
            var button = childSkip ? (Button)((StackPanel)root).Children[0] : (Button)root;
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(button);
                bool HasOpacityDuration(int milliseconds) =>
                    visual.ImplicitAnimations?.ContainsKey("Opacity") == true
                    && visual.ImplicitAnimations["Opacity"] is ScalarKeyFrameAnimation scalar
                    && scalar.Duration == TimeSpan.FromMilliseconds(milliseconds);
                bool Skipped(Element next)
                {
                    int skipped = reconciler.DebugElementsSkipped;
                    var wrapped = Wrap(next);
                    H.Check(name + "_SameControl", reconciler.Update(current, wrapped, root, static () => { }) is null);
                    current = wrapped;
                    return reconciler.DebugElementsSkipped == skipped + 1;
                }

                H.Check(name + "_InitialTransition", HasOpacityDuration(200));
                H.Check(name + "_EqualSkips", Skipped(Render()));
                H.Check(name + "_ChangedDurationUpdates", !Skipped(Render(ms: 800)));
                H.Check(name + "_ChangedDurationApplied", HasOpacityDuration(800));
                H.Check(name + "_EqualAfterChangeSkips", Skipped(Render(ms: 800)));
                H.Check(name + "_ChangedBrushUpdates", !Skipped(Render(ms: 800, red: 128)));
                H.Check(name + "_ChangedThemeTransitionUpdates", !Skipped(Render(ms: 800, red: 128, entrance: 40)));
                H.Check(name + "_ChangedThemeTransitionApplied", button.ContentTransitions is { Count: 2 } transitions
                    && transitions[0] is XamlAnim.EntranceThemeTransition { FromVerticalOffset: 40 });
                H.Check(name + "_ChangedTriggerUpdates", !Skipped(Render(ms: 800, red: 128, entrance: 40, trigger: 2)));
                H.Check(name + "_ChangedScrollUpdates", !Skipped(Render(ms: 800, red: 128, entrance: 40, trigger: 2, expression: "1")));
                H.Check(name + "_RemovedUpdates", !Skipped(Button("Go")));
                H.Check(name + "_RemovedTransition", visual.ImplicitAnimations?.ContainsKey("Opacity") != true);
            }
            finally
            {
                reconciler.UnmountChild(root);
                H.SetContent(null);
            }
        }

        // Expressions are stateless, so an update that leaves .ScrollLinked() unchanged keeps them running.
        private async Task ScrollRestarts(string name)
        {
            using var reconciler = new Reconciler();
            ButtonElement Linked(double margin = 0, string value = "0")
                => Button("Scroll").Margin(margin).ScrollLinked(_viewer, s => s.Expression("CenterPoint.X", value));
            int starts = Reconciler.DebugScrollAnimationStarts;
            var current = Wrap(Linked());
            var root = reconciler.Mount(current, static () => { })!;
            H.SetContent(root);
            try
            {
                await Harness.Render();
                H.Check(name + "_ScrollMountStarts", Reconciler.DebugScrollAnimationStarts == starts + 1);
                int Restarts(Element next)
                {
                    int before = Reconciler.DebugScrollAnimationStarts;
                    var wrapped = Wrap(next);
                    H.Check(name + "_ScrollSameControl", reconciler.Update(current, wrapped, root, static () => { }) is null);
                    current = wrapped;
                    return Reconciler.DebugScrollAnimationStarts - before;
                }

                H.Check(name + "_ScrollEqualKeeps", Restarts(Linked()) == 0);
                H.Check(name + "_ScrollUnrelatedUpdateKeeps", Restarts(Linked(margin: 4)) == 0);
                H.Check(name + "_ScrollChangedRestarts", Restarts(Linked(margin: 4, value: "1")) == 1);
                // Another animation on the element may have replaced an expression, so restart then.
                H.Check(name + "_ScrollWithTransitionRestarts", Restarts(Linked(margin: 8, value: "1").OpacityTransition()) == 1);
            }
            finally
            {
                reconciler.UnmountChild(root);
                H.SetContent(null);
            }
        }

        private void Declarations()
        {
            static bool Same(Element a, Element b) => Element.CanSkipUpdate(a, b) && Element.CanSkipUpdate(b, a);
            H.Check("RebuiltExtras_BackgroundEqual", Same(VStack().BackgroundTransition(TimeSpan.FromMilliseconds(200)),
                VStack().BackgroundTransition(TimeSpan.FromMilliseconds(200))));
            H.Check("RebuiltExtras_BackgroundChanged", !Same(VStack().BackgroundTransition(TimeSpan.FromMilliseconds(200)),
                VStack().BackgroundTransition(TimeSpan.FromMilliseconds(800))));
            H.Check("RebuiltExtras_ScaleComponentsChanged", !Same(
                Button("Go").ScaleTransition(new Vector3Transition { Components = Vector3TransitionComponents.X }),
                Button("Go").ScaleTransition(new Vector3Transition { Components = Vector3TransitionComponents.Y })));
            H.Check("RebuiltExtras_OtherViewerChanged", !Same(
                Button("Go").ScrollLinked(_viewer, static s => s.FadeOut(0, 100)),
                Button("Go").ScrollLinked(new ScrollViewer(), static s => s.FadeOut(0, 100))));
            H.Check("RebuiltExtras_GradientBrushByReference", !Same(
                Button("Go").InteractionStates(static s => s.PointerOver(background: new LinearGradientBrush())),
                Button("Go").InteractionStates(static s => s.PointerOver(background: new LinearGradientBrush()))));
            H.Check("RebuiltExtras_ItemContainerEqual", Same(
                Button("Go").ItemContainerTransitions(new XamlAnim.AddDeleteThemeTransition()),
                Button("Go").ItemContainerTransitions(new XamlAnim.AddDeleteThemeTransition())));

            void Theme(string type, Func<XamlAnim.Transition> make, Func<XamlAnim.Transition>? changed)
            {
                H.Check("RebuiltExtras_" + type + "_Equal", Same(Button("Go").WithTransitions(make()), Button("Go").WithTransitions(make())));
                if (changed is not null)
                    H.Check("RebuiltExtras_" + type + "_Changed", !Same(Button("Go").WithTransitions(make()), Button("Go").WithTransitions(changed())));
            }
            Theme("Entrance", static () => new XamlAnim.EntranceThemeTransition { FromHorizontalOffset = 10 },
                static () => new XamlAnim.EntranceThemeTransition { FromHorizontalOffset = 20 });
            Theme("EntranceStagger", static () => new XamlAnim.EntranceThemeTransition { IsStaggeringEnabled = true },
                static () => new XamlAnim.EntranceThemeTransition { IsStaggeringEnabled = false });
            Theme("Content", static () => new XamlAnim.ContentThemeTransition { VerticalOffset = 10 },
                static () => new XamlAnim.ContentThemeTransition { VerticalOffset = 20 });
            Theme("Popup", static () => new XamlAnim.PopupThemeTransition { FromVerticalOffset = 10 },
                static () => new XamlAnim.PopupThemeTransition { FromVerticalOffset = 20 });
            Theme("Reposition", static () => new XamlAnim.RepositionThemeTransition { IsStaggeringEnabled = true },
                static () => new XamlAnim.RepositionThemeTransition { IsStaggeringEnabled = false });
            Theme("EdgeUI", static () => new XamlAnim.EdgeUIThemeTransition { Edge = EdgeTransitionLocation.Left },
                static () => new XamlAnim.EdgeUIThemeTransition { Edge = EdgeTransitionLocation.Right });
            Theme("Pane", static () => new XamlAnim.PaneThemeTransition { Edge = EdgeTransitionLocation.Left },
                static () => new XamlAnim.PaneThemeTransition { Edge = EdgeTransitionLocation.Right });
            var info = new XamlAnim.SlideNavigationTransitionInfo();
            Theme("Navigation", () => new XamlAnim.NavigationThemeTransition { DefaultNavigationTransitionInfo = info },
                static () => new XamlAnim.NavigationThemeTransition { DefaultNavigationTransitionInfo = new XamlAnim.SlideNavigationTransitionInfo() });
            Theme("AddDelete", static () => new XamlAnim.AddDeleteThemeTransition(), static () => new XamlAnim.ReorderThemeTransition());
            Theme("Reorder", static () => new XamlAnim.ReorderThemeTransition(), null);
        }
    }

    internal class MergesWithLayoutAnimation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Merged"))
                        .LayoutAnimation()
                        .Animate(Curve.Spring(0.8f))
                        .AutomationId("animate-merged")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "animate-merged");

            H.Check("AnimateMerge_Mounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("AnimateMerge_HasImplicit", visual.ImplicitAnimations is not null);

                if (visual.ImplicitAnimations is not null)
                {
                    // Should have both layout (Offset) and property (Opacity, Scale, Rotation)
                    var hasOffset = false;
                    var hasOpacity = false;
                    try { var _ = visual.ImplicitAnimations["Offset"]; hasOffset = true; } catch { }
                    try { var _ = visual.ImplicitAnimations["Opacity"]; hasOpacity = true; } catch { }

                    H.Check("AnimateMerge_HasOffset", hasOffset);
                    H.Check("AnimateMerge_HasOpacity", hasOpacity);
                }
            }
        }
    }
}
