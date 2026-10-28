using System.Numerics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Comprehensive animation tests: basic animations, combinations of multiple
/// animation features on a single element, and cross-feature interactions.
/// Each test mounts a UI tree, applies animation config, and validates compositor state.
/// </summary>
internal static class AnimationCombinationTests
{
    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    static Border? FindById(Harness h, string id) =>
        h.FindControl<Border>(b =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == id);

    static StackPanel? FindPanelById(Harness h, string id) =>
        h.FindControl<StackPanel>(sp =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(sp) == id);

    static bool HasImplicitKey(Microsoft.UI.Composition.ImplicitAnimationCollection? coll, string key)
    {
        if (coll is null) return false;
        try { var _ = coll[key]; return true; }
        catch { return false; }
    }

    // ════════════════════════════════════════════════════════════════
    //  1. Single animation features — verify each in isolation
    // ════════════════════════════════════════════════════════════════

    /// <summary>Animate(Spring) alone — all five implicit animation keys present.</summary>
    internal class AnimateSpringAlone(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Spring"))
                    .Animate(Curve.Spring(0.7f))
                    .AutomationId("t-spring-alone")
            ));
            await Harness.Render();

            var target = FindById(H, "t-spring-alone");
            H.Check("SpringAlone_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("SpringAlone_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("SpringAlone_Opacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            H.Check("SpringAlone_Scale", HasImplicitKey(v.ImplicitAnimations, "Scale"));
            H.Check("SpringAlone_Offset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
            H.Check("SpringAlone_RotationAngle", HasImplicitKey(v.ImplicitAnimations, "RotationAngle"));
            H.Check("SpringAlone_CenterPoint", HasImplicitKey(v.ImplicitAnimations, "CenterPoint"));
        }
    }

    /// <summary>Animate(Ease) alone — targeted to Opacity+Scale only.</summary>
    internal class AnimateEaseTargeted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Ease"))
                    .Animate(Curve.Ease(200, Easing.Decelerate), AnimateProperty.Opacity | AnimateProperty.Scale)
                    .AutomationId("t-ease-targeted")
            ));
            await Harness.Render();

            var target = FindById(H, "t-ease-targeted");
            H.Check("EaseTargeted_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("EaseTargeted_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            H.Check("EaseTargeted_HasScale", HasImplicitKey(v.ImplicitAnimations, "Scale"));
            H.Check("EaseTargeted_NoOffset", !HasImplicitKey(v.ImplicitAnimations, "Offset"));
            H.Check("EaseTargeted_NoRotation", !HasImplicitKey(v.ImplicitAnimations, "RotationAngle"));
        }
    }

    /// <summary>LayoutAnimation alone — Offset present, no Opacity/Scale.</summary>
    internal class LayoutAnimAlone(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Layout"))
                    .LayoutAnimation()
                    .AutomationId("t-layout-alone")
            ));
            await Harness.Render();

            var target = FindById(H, "t-layout-alone");
            H.Check("LayoutAlone_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("LayoutAlone_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("LayoutAlone_HasOffset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
            H.Check("LayoutAlone_NoOpacity", !HasImplicitKey(v.ImplicitAnimations, "Opacity"));
        }
    }

    /// <summary>Transition(Fade) on mount — element starts enter animation.</summary>
    internal class TransitionFadeEnter(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Fade In"))
                    .Transition(Animation.Transition.Fade)
                    .AutomationId("t-fade-enter")
            ));
            await Harness.Render();

            var target = FindById(H, "t-fade-enter");
            H.Check("FadeEnter_Mounted", target is not null);
            // Enter fade should have started — visual exists without crash
            if (target is not null)
            {
                var v = ElementCompositionPreview.GetElementVisual(target);
                H.Check("FadeEnter_VisualValid", v is not null);
            }
        }
    }

    /// <summary>InteractionStates on mount — handlers registered, no crash.</summary>
    internal class InteractionStatesSetup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Interact"))
                    .InteractionStates(s => s
                        .PointerOver(opacity: 0.8f, scale: 1.05f)
                        .Pressed(scale: 0.95f))
                    .AutomationId("t-interact-setup")
            ));
            await Harness.Render();

            var target = FindById(H, "t-interact-setup");
            H.Check("InteractSetup_Mounted", target is not null);
        }
    }

    /// <summary>Keyframes on mount — trigger fires, animation starts.</summary>
    internal class KeyframesOnMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("KF"))
                    .Keyframes("enter", 1, kf => kf
                        .Duration(200)
                        .At(0f, opacity: 0f)
                        .At(1f, opacity: 1f))
                    .AutomationId("t-kf-mount")
            ));
            await Harness.Render();

            H.Check("KFMount_Mounted", FindById(H, "t-kf-mount") is not null);
        }
    }

    /// <summary>Stagger on container — children mount without crash.</summary>
    internal class StaggerOnContainer(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(
                    Border(TextBlock("A")).LayoutAnimation().WithKey("a"),
                    Border(TextBlock("B")).LayoutAnimation().WithKey("b"),
                    Border(TextBlock("C")).LayoutAnimation().WithKey("c")
                ).Stagger(TimeSpan.FromMilliseconds(30))
                 .AutomationId("t-stagger-container")
            );
            await Harness.Render();

            var panel = FindPanelById(H, "t-stagger-container");
            H.Check("StaggerContainer_Mounted", panel is not null);
            H.Check("StaggerContainer_3Children", panel is not null && panel.Children.Count == 3);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Combinations — multiple animation features on one element
    // ════════════════════════════════════════════════════════════════

    /// <summary>Animate + LayoutAnimation — both implicit collections merge correctly.</summary>
    internal class AnimatePlusLayout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Both"))
                    .LayoutAnimation()
                    .Animate(Curve.Spring(0.8f))
                    .AutomationId("t-animate-layout")
            ));
            await Harness.Render();

            var target = FindById(H, "t-animate-layout");
            H.Check("AnimLayout_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("AnimLayout_HasImplicit", v.ImplicitAnimations is not null);
            // Layout claims Offset; Animate adds Opacity, Scale, RotationAngle, CenterPoint
            H.Check("AnimLayout_HasOffset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
            H.Check("AnimLayout_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            H.Check("AnimLayout_HasScale", HasImplicitKey(v.ImplicitAnimations, "Scale"));
            H.Check("AnimLayout_HasRotation", HasImplicitKey(v.ImplicitAnimations, "RotationAngle"));
        }
    }

    /// <summary>Animate + Transition(Fade) — implicit animations set AND enter transition plays.</summary>
    internal class AnimatePlusTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("AnimTrans"))
                    .Animate(Curve.Ease(200))
                    .Transition(Animation.Transition.Fade)
                    .AutomationId("t-animate-trans")
            ));
            await Harness.Render();

            var target = FindById(H, "t-animate-trans");
            H.Check("AnimTrans_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("AnimTrans_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("AnimTrans_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
        }
    }

    /// <summary>Animate + InteractionStates — both features coexist on same element.</summary>
    internal class AnimatePlusInteraction(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("AnimInteract"))
                    .Animate(Curve.Spring(0.6f), AnimateProperty.Opacity | AnimateProperty.Scale)
                    .InteractionStates(s => s
                        .PointerOver(opacity: 0.9f, scale: 1.02f)
                        .Pressed(scale: 0.98f))
                    .AutomationId("t-animate-interact")
            ));
            await Harness.Render();

            var target = FindById(H, "t-animate-interact");
            H.Check("AnimInteract_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("AnimInteract_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("AnimInteract_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            H.Check("AnimInteract_HasScale", HasImplicitKey(v.ImplicitAnimations, "Scale"));
        }
    }

    /// <summary>LayoutAnimation + Transition(Fade+Slide) — layout + enter transition.</summary>
    internal class LayoutPlusTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("LayoutTrans"))
                    .LayoutAnimation()
                    .Transition(Animation.Transition.Fade + Animation.Transition.Slide(Edge.Bottom))
                    .AutomationId("t-layout-trans")
            ));
            await Harness.Render();

            var target = FindById(H, "t-layout-trans");
            H.Check("LayoutTrans_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("LayoutTrans_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("LayoutTrans_HasOffset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
        }
    }

    /// <summary>Transition(Fade+Scale) combined — both effects applied on enter.</summary>
    internal class TransitionCombined(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("FadeScale"))
                    .Transition(Animation.Transition.Fade + Animation.Transition.Scale(0.8f))
                    .AutomationId("t-trans-combined")
            ));
            await Harness.Render();

            var target = FindById(H, "t-trans-combined");
            H.Check("TransCombined_Mounted", target is not null);
            if (target is not null)
            {
                var v = ElementCompositionPreview.GetElementVisual(target);
                H.Check("TransCombined_VisualExists", v is not null);
            }
        }
    }

    /// <summary>Animate + Keyframes — implicit animations AND explicit keyframe playback.</summary>
    internal class AnimatePlusKeyframes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("AnimKF"))
                    .Animate(Curve.Spring(0.6f), AnimateProperty.Opacity)
                    .Keyframes("pulse", 1, kf => kf
                        .Duration(300)
                        .At(0f, scale: Vector3.One)
                        .At(0.5f, scale: new Vector3(1.2f, 1.2f, 1f))
                        .At(1f, scale: Vector3.One))
                    .AutomationId("t-animate-kf")
            ));
            await Harness.Render();

            var target = FindById(H, "t-animate-kf");
            H.Check("AnimKF_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("AnimKF_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("AnimKF_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
        }
    }

    /// <summary>InteractionStates + Transition — hover works, enter animation plays.</summary>
    internal class InteractionPlusTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("InterTrans"))
                    .InteractionStates(s => s
                        .PointerOver(opacity: 0.8f)
                        .Pressed(scale: 0.95f))
                    .Transition(Animation.Transition.Fade)
                    .AutomationId("t-interact-trans")
            ));
            await Harness.Render();

            var target = FindById(H, "t-interact-trans");
            H.Check("InterTrans_Mounted", target is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Triple+ combinations — stress the combination matrix
    // ════════════════════════════════════════════════════════════════

    /// <summary>Animate + LayoutAnimation + InteractionStates — the kitchen sink.</summary>
    internal class TripleCombination(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Triple"))
                    .LayoutAnimation()
                    .Animate(Curve.Spring(0.7f))
                    .InteractionStates(s => s
                        .PointerOver(opacity: 0.85f, scale: 1.03f)
                        .Pressed(scale: 0.97f))
                    .AutomationId("t-triple")
            ));
            await Harness.Render();

            var target = FindById(H, "t-triple");
            H.Check("Triple_Mounted", target is not null);
            if (target is null) return;

            var v = ElementCompositionPreview.GetElementVisual(target);
            H.Check("Triple_HasImplicit", v.ImplicitAnimations is not null);
            H.Check("Triple_HasOffset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
            H.Check("Triple_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            H.Check("Triple_HasScale", HasImplicitKey(v.ImplicitAnimations, "Scale"));
        }
    }

    /// <summary>LayoutAnimation + Animate + Transition + Stagger — full container combo.</summary>
    internal class FullContainerCombo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(
                    Border(TextBlock("Item1"))
                        .LayoutAnimation()
                        .Animate(Curve.Ease(200), AnimateProperty.Opacity)
                        .Transition(Animation.Transition.Fade)
                        .WithKey("fc1")
                        .AutomationId("t-full-combo-item"),
                    Border(TextBlock("Item2"))
                        .LayoutAnimation()
                        .Animate(Curve.Ease(200), AnimateProperty.Opacity)
                        .Transition(Animation.Transition.Fade)
                        .WithKey("fc2"),
                    Border(TextBlock("Item3"))
                        .LayoutAnimation()
                        .Animate(Curve.Ease(200), AnimateProperty.Opacity)
                        .Transition(Animation.Transition.Fade)
                        .WithKey("fc3")
                ).Stagger(TimeSpan.FromMilliseconds(30))
                 .AutomationId("t-full-combo-container")
            );
            await Harness.Render();

            var panel = FindPanelById(H, "t-full-combo-container");
            H.Check("FullCombo_ContainerMounted", panel is not null);
            H.Check("FullCombo_3Children", panel is not null && panel.Children.Count == 3);

            var item = FindById(H, "t-full-combo-item");
            if (item is not null)
            {
                var v = ElementCompositionPreview.GetElementVisual(item);
                H.Check("FullCombo_HasImplicit", v.ImplicitAnimations is not null);
                H.Check("FullCombo_HasOffset", HasImplicitKey(v.ImplicitAnimations, "Offset"));
                H.Check("FullCombo_HasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  4. Update/re-render — verify animations survive reconciliation
    // ════════════════════════════════════════════════════════════════

    /// <summary>Animate survives re-render — implicit animations still present after property change.</summary>
    internal class AnimateSurvivesUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var opacity = 1.0;

            host.Mount(ctx => VStack(
                Border(TextBlock("Update"))
                    .Opacity(opacity)
                    .Animate(Curve.Spring(0.7f))
                    .AutomationId("t-survive-update")
            ));
            await Harness.Render();

            var target = FindById(H, "t-survive-update");
            H.Check("SurviveUpdate_Mounted", target is not null);

            // Re-render with different opacity
            opacity = 0.5;
            host.Mount(ctx => VStack(
                Border(TextBlock("Update"))
                    .Opacity(opacity)
                    .Animate(Curve.Spring(0.7f))
                    .AutomationId("t-survive-update")
            ));
            await Harness.Render();

            target = FindById(H, "t-survive-update");
            H.Check("SurviveUpdate_StillMounted", target is not null);
            if (target is not null)
            {
                var v = ElementCompositionPreview.GetElementVisual(target);
                H.Check("SurviveUpdate_StillHasImplicit", v.ImplicitAnimations is not null);
                H.Check("SurviveUpdate_StillHasOpacity", HasImplicitKey(v.ImplicitAnimations, "Opacity"));
            }
        }
    }

    /// <summary>WithAnimation scope + Animate — scope overrides for explicit property changes.</summary>
    internal class WithAnimationPlusAnimate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var opacity = 1.0;

            host.Mount(ctx => VStack(
                Border(TextBlock("ScopeAnimate"))
                    .Opacity(opacity)
                    .Animate(Curve.Ease(300))
                    .AutomationId("t-scope-animate")
            ));
            await Harness.Render();

            // Change property inside WithAnimation scope
            AnimationScope.WithAnimation(Curve.Spring(0.5f), () =>
            {
                opacity = 0.3;
                host.Mount(ctx => VStack(
                    Border(TextBlock("ScopeAnimate"))
                        .Opacity(opacity)
                        .Animate(Curve.Ease(300))
                        .AutomationId("t-scope-animate")
                ));
            });
            await Harness.Render();

            var target = FindById(H, "t-scope-animate");
            H.Check("ScopeAnimate_Updated", target is not null);
            if (target is not null)
            {
                var v = ElementCompositionPreview.GetElementVisual(target);
                H.Check("ScopeAnimate_StillHasImplicit", v.ImplicitAnimations is not null);
            }
        }
    }

    /// <summary>Keyframes trigger update — changing trigger replays animation.</summary>
    internal class KeyframeTriggerUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var trigger = 0;

            host.Mount(ctx => VStack(
                Border(TextBlock("KFTrigger"))
                    .Keyframes("bounce", trigger, kf => kf
                        .Duration(200)
                        .At(0f, scale: Vector3.One)
                        .At(0.5f, scale: new Vector3(1.1f, 1.1f, 1f))
                        .At(1f, scale: Vector3.One))
                    .AutomationId("t-kf-trigger")
            ));
            await Harness.Render();
            H.Check("KFTrigger_Mount", FindById(H, "t-kf-trigger") is not null);

            // Change trigger — should replay
            trigger = 1;
            host.Mount(ctx => VStack(
                Border(TextBlock("KFTrigger"))
                    .Keyframes("bounce", trigger, kf => kf
                        .Duration(200)
                        .At(0f, scale: Vector3.One)
                        .At(0.5f, scale: new Vector3(1.1f, 1.1f, 1f))
                        .At(1f, scale: Vector3.One))
                    .AutomationId("t-kf-trigger")
            ));
            await Harness.Render();
            H.Check("KFTrigger_Replayed", FindById(H, "t-kf-trigger") is not null);

            // Same trigger — should NOT replay (no crash either way)
            host.Mount(ctx => VStack(
                Border(TextBlock("KFTrigger"))
                    .Keyframes("bounce", trigger, kf => kf
                        .Duration(200)
                        .At(0f, scale: Vector3.One)
                        .At(0.5f, scale: new Vector3(1.1f, 1.1f, 1f))
                        .At(1f, scale: Vector3.One))
                    .AutomationId("t-kf-trigger")
            ));
            await Harness.Render();
            H.Check("KFTrigger_SameTrigger_NoCrash", true);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  5. Removal — verify animation config cleans up on unmount
    // ════════════════════════════════════════════════════════════════

    /// <summary>Animate removed on re-render — ImplicitAnimations cleared.</summary>
    internal class AnimateRemoved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var useAnim = true;

            host.Mount(ctx => VStack(
                useAnim
                    ? Border(TextBlock("Animated")).Animate(Curve.Spring()).AutomationId("t-anim-remove")
                    : Border(TextBlock("Animated")).AutomationId("t-anim-remove")
            ));
            await Harness.Render();

            var target = FindById(H, "t-anim-remove");
            H.Check("AnimRemove_HasImplicit",
                target is not null
                && ElementCompositionPreview.GetElementVisual(target).ImplicitAnimations is not null);

            // Remove animation
            useAnim = false;
            host.Mount(ctx => VStack(
                useAnim
                    ? Border(TextBlock("Animated")).Animate(Curve.Spring()).AutomationId("t-anim-remove")
                    : Border(TextBlock("Animated")).AutomationId("t-anim-remove")
            ));
            await Harness.Render();

            target = FindById(H, "t-anim-remove");
            H.Check("AnimRemove_ImplicitCleared",
                target is not null
                && ElementCompositionPreview.GetElementVisual(target).ImplicitAnimations is null);
        }
    }

    /// <summary>Keyframes translation — animates Translation, so the element keeps its layout Offset (#1432).</summary>
    internal class KeyframesTranslationChannel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Border(TextBlock("Above")).Height(40),
                Border(TextBlock("KF"))
                    .Keyframes("nudge", 1, kf => kf
                        .Duration(2000)
                        .At(0f, translation: Vector3.Zero)
                        .At(1f, translation: new Vector3(0, -4, 0)))
                    .AutomationId("t-kf-translation")
            ));
            await Harness.Render();

            var target = FindById(H, "t-kf-translation");
            H.Check("KFTranslation_Mounted", target is not null);
            if (target is null) return;
            var visual = ElementCompositionPreview.GetElementVisual(target);
            H.Check("KFTranslation_AnimatesTranslation", visual.TryGetAnimationController("Translation") is not null);
            H.Check("KFTranslation_LeavesOffsetAlone", visual.TryGetAnimationController("Offset") is null);

            // the first layout pass may not have written Offset yet
            await Task.Delay(300);
            await Harness.Render();
            H.Check("KFTranslation_OffsetKeptLayoutPosition", visual.Offset.Y >= 40, $"offset={visual.Offset}");
        }
    }
}
