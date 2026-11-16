using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

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
