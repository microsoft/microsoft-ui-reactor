using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class KeyframeBuilderTests
{
    internal class BuilderProducesCorrectDef(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            var builder = new KeyframeBuilder()
                .Duration(600)
                .At(0.0f, scale: Vector3.One)
                .At(0.4f, scale: new Vector3(1.3f, 1.3f, 1f), easing: Easing.Decelerate)
                .At(0.7f, scale: new Vector3(0.95f, 0.95f, 1f))
                .At(1.0f, scale: Vector3.One, easing: Easing.Accelerate);

            var def = builder.Build();

            H.Check("Keyframe_Duration", def.Duration.TotalMilliseconds == 600);
            H.Check("Keyframe_NotLoop", !def.Loop);
            H.Check("Keyframe_KeyframeCount", def.Keyframes.Length == 4);
            H.Check("Keyframe_FirstProgress", def.Keyframes[0].Progress == 0.0f);
            H.Check("Keyframe_LastProgress", def.Keyframes[3].Progress == 1.0f);
            H.Check("Keyframe_FirstScale", def.Keyframes[0].Scale == Vector3.One);
            H.Check("Keyframe_SecondEasing", def.Keyframes[1].Easing == Easing.Decelerate);
        }
    }

    internal class TriggerValueChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var reconciler = new Reconciler();
            ButtonElement Render(int trigger) => Button("Pulse")
                .Keyframes("pulse", trigger, kf => kf
                    .Duration(1000).Loop()
                    .At(0.0f, opacity: 1f)
                    .At(0.5f, opacity: 0.5f)
                    .At(1.0f, opacity: 1f));
            var current = Render(0);
            var target = (Button)reconciler.Mount(current, static () => { })!;
            H.SetContent(target);
            try
            {
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(target);
                var initial = visual.TryGetAnimationController("Opacity");
                H.Check("Keyframe_InitialAnimationStarted", initial is not null);
                if (initial is null) return;

                // Mark the controller so a restart is observable without timing animation progress.
                initial.PlaybackRate = 0.5f;
                H.Check("Keyframe_InitialControllerMarked", initial.PlaybackRate == 0.5f);

                var sameTrigger = Render(0);
                H.Check("Keyframe_SameTriggerKeepsControl",
                    reconciler.Update(current, sameTrigger, target, static () => { }) is null);
                current = sameTrigger;
                await Harness.Render();
                H.Check("Keyframe_SameTriggerDoesNotRestart",
                    visual.TryGetAnimationController("Opacity") is { PlaybackRate: 0.5f });

                var changedTrigger = Render(1);
                H.Check("Keyframe_ChangedTriggerKeepsControl",
                    reconciler.Update(current, changedTrigger, target, static () => { }) is null);
                await Harness.Render();
                H.Check("Keyframe_ChangedTriggerRestartsAnimation",
                    visual.TryGetAnimationController("Opacity") is { PlaybackRate: 1f });
            }
            finally
            {
                reconciler.UnmountChild(target);
                H.SetContent(null);
            }
        }
    }

    internal class LoopFlag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            var def = new KeyframeBuilder()
                .Duration(1200)
                .Loop()
                .At(0.0f, opacity: 0.3f)
                .At(0.5f, opacity: 0.7f)
                .At(1.0f, opacity: 0.3f)
                .Build();

            H.Check("Keyframe_LoopEnabled", def.Loop);
            H.Check("Keyframe_LoopDuration", def.Duration.TotalMilliseconds == 1200);
        }
    }
}
