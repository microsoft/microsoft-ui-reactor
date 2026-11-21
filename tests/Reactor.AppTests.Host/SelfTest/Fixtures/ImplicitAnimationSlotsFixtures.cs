using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §6 (layer 1): the features that share a visual's implicit animation keys claim and
/// release them through <c>ImplicitAnimationSlots</c>, so one feature never drops another's key
/// (#1625) and a removed transition stops animating (#1626).
/// </summary>
/// <remarks>
/// Motion is read from the visual's animation controller: a write that starts an implicit
/// animation leaves a new controller on its channel, and one that jumps leaves none. Each check
/// stops the channel first, so a controller from an earlier step can't count.
/// </remarks>
internal static class ImplicitAnimationSlotsFixtures
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(3);

    private static BorderElement Target() => Border(TextBlock(" "))
        .Width(100).Height(40)
        .Background(new SolidColorBrush(Microsoft.UI.Colors.Red))
        .HAlign(HorizontalAlignment.Left);

    // A one-shot keyframe hands out the visual without claiming an implicit key.
    private static T TakesVisual<T>(T e) where T : Element =>
        e.Keyframes("slots", 1, static k => k.Duration(30).At(0f, scale: Vector3.One).At(1f, scale: Vector3.One));

    internal abstract class SlotsFixture(Harness h, string name) : SelfTestFixtureBase(h)
    {
        private Reconciler? _reconciler;
        private StackPanel? _stage;
        private Element? _current;

        protected string Name { get; } = name;
        protected FrameworkElement Control => (FrameworkElement)_stage!.Children[0];

        public sealed override async Task RunAsync()
        {
            try { await Run(); }
            catch (Exception ex) { H.Check(Name + "_NoThrow", false, $"{ex.GetType().Name}: {ex.Message}"); }
            finally
            {
                if (_stage is not null) _reconciler!.UnmountChild(_stage);
                _reconciler?.Dispose();
                H.SetContent(null);
            }
        }

        protected abstract Task Run();

        private static Element Stage(Element child) => VStack(child)
            .Width(400).Height(400)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);

        protected async Task Mount(Element child)
        {
            _reconciler = new Reconciler();
            _current = Stage(child);
            _stage = (StackPanel)_reconciler.Mount(_current, static () => { })!;
            H.SetContent(_stage);
            await Settle();
        }

        protected async Task Update(Element child)
        {
            var next = Stage(child);
            _reconciler!.Update(_current!, next, _stage!, static () => { });
            _current = next;
            await Settle();
        }

        /// <summary>Renders <paramref name="child"/> and reports whether it started an animation on <paramref name="channel"/>.</summary>
        protected async Task<bool> Animates(string channel, Element child)
        {
            var visual = ElementCompositionPreview.GetElementVisual(Control);
            visual.StopAnimation(channel);
            var before = visual.TryGetAnimationController(channel);
            await Update(child);
            var after = visual.TryGetAnimationController(channel);
            visual.StopAnimation(channel);
            return after is not null && !Equals(before, after);
        }

        protected string Keys()
        {
            var keys = ElementCompositionPreview.GetElementVisual(Control).ImplicitAnimations?
                .Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
            return keys.Length > 0 ? string.Join(",", keys) : "-";
        }

        protected TimeSpan? KeyDuration(string key)
        {
            var collection = ElementCompositionPreview.GetElementVisual(Control).ImplicitAnimations;
            return collection is not null && collection.TryGetValue(key, out var anim) && anim is KeyFrameAnimation kfa
                ? kfa.Duration : null;
        }

        private static async Task Settle()
        {
            int frames = 0;
            var done = new TaskCompletionSource();
            EventHandler<object> onRendering = (_, _) => { if (++frames >= 3) done.TrySetResult(); };
            CompositionTarget.Rendering += onRendering;
            try { await Task.WhenAny(done.Task, Task.Delay(2000)); }
            finally { CompositionTarget.Rendering -= onRendering; }
        }
    }

    /// <summary>
    /// #1625: a transition the compositor fallback installs survives <c>.LayoutAnimation()</c>
    /// being applied and being removed. The transition is added after the layout animation took
    /// the visual, which is the order that reaches the fallback.
    /// </summary>
    internal sealed class LayoutKeepsTransition(Harness h) : SlotsFixture(h, "ImplicitSlots_LayoutKeepsTransition")
    {
        protected override async Task Run()
        {
            await Mount(Target().LayoutAnimation());
            await Update(Target().OpacityTransition(Long).LayoutAnimation());
            H.Check(Name + "_SharedKeys", Keys() == "Offset,Opacity", $"keys={Keys()}");

            bool withLayout = await Animates("Opacity", Target().Opacity(0.5).OpacityTransition(Long).LayoutAnimation());
            H.Check(Name + "_AnimatesWithLayout", withLayout);

            bool afterLayout = await Animates("Opacity", Target().Opacity(1).OpacityTransition(Long));
            H.Check(Name + "_AnimatesAfterLayoutRemoved", afterLayout);
            H.Check(Name + "_LayoutKeyReleased", Keys() == "Opacity", $"keys={Keys()}");
        }
    }

    /// <summary>#1626, XAML arm: dropping a transition modifier clears the XAML transition property.</summary>
    internal sealed class RemovedXamlTransitionClears(Harness h) : SlotsFixture(h, "ImplicitSlots_RemovedXamlTransitionClears")
    {
        protected override async Task Run()
        {
            await Mount(Target().OpacityTransition().ScaleTransition().RotationTransition().TranslationTransition());
            var c = Control;
            H.Check(Name + "_Installed", c.OpacityTransition is not null && c.ScaleTransition is not null
                && c.RotationTransition is not null && c.TranslationTransition is not null);

            // Dropping one keeps the others.
            await Update(Target().ScaleTransition().RotationTransition().TranslationTransition());
            H.Check(Name + "_OpacityCleared", c.OpacityTransition is null);
            H.Check(Name + "_OthersKept", c.ScaleTransition is not null && c.RotationTransition is not null
                && c.TranslationTransition is not null);

            await Update(Target());
            H.Check(Name + "_ScaleCleared", c.ScaleTransition is null);
            H.Check(Name + "_RotationCleared", c.RotationTransition is null);
            H.Check(Name + "_TranslationCleared", c.TranslationTransition is null);
        }
    }

    /// <summary>#1626, fallback arm: dropping a transition removes its implicit key, so the channel jumps.</summary>
    internal sealed class RemovedFallbackTransitionClears(Harness h) : SlotsFixture(h, "ImplicitSlots_RemovedFallbackTransitionClears")
    {
        protected override async Task Run()
        {
            await Mount(TakesVisual(Target()));
            await Update(TakesVisual(Target()).OpacityTransition(Long).RotationTransition(Long)
                .ScaleTransition(new Vector3Transition { Duration = Long }));
            H.Check(Name + "_Installed", Keys() == "Opacity,RotationAngle,Scale", $"keys={Keys()}");
            bool installed = await Animates("Opacity", TakesVisual(Target()).Opacity(0.5).OpacityTransition(Long)
                .RotationTransition(Long).ScaleTransition(new Vector3Transition { Duration = Long }));
            H.Check(Name + "_AnimatesWhileDeclared", installed);

            await Update(TakesVisual(Target()).Opacity(0.5).RotationTransition(Long));
            H.Check(Name + "_KeysReleased", Keys() == "RotationAngle", $"keys={Keys()}");
            bool removed = await Animates("Opacity", TakesVisual(Target()).Opacity(1).RotationTransition(Long));
            H.Check(Name + "_JumpsOnceRemoved", !removed);
        }
    }

    /// <summary>
    /// §6.1 precedence: <c>.Animate()</c> wins Opacity over a transition, and releasing it
    /// restores the transition's animation instead of dropping the key.
    /// </summary>
    internal sealed class LoserRestored(Harness h) : SlotsFixture(h, "ImplicitSlots_LoserRestored")
    {
        private static readonly TimeSpan TransitionTime = TimeSpan.FromMilliseconds(777);
        private static readonly TimeSpan AnimateTime = TimeSpan.FromMilliseconds(1234);

        protected override async Task Run()
        {
            await Mount(TakesVisual(Target()));
            await Update(TakesVisual(Target()).OpacityTransition(TransitionTime));
            H.Check(Name + "_TransitionKey", KeyDuration("Opacity") == TransitionTime, $"duration={KeyDuration("Opacity")}");

            await Update(TakesVisual(Target()).OpacityTransition(TransitionTime)
                .Animate(Curve.Linear((int)AnimateTime.TotalMilliseconds), AnimateProperty.Opacity));
            H.Check(Name + "_AnimateWins", KeyDuration("Opacity") == AnimateTime, $"duration={KeyDuration("Opacity")}");

            await Update(TakesVisual(Target()).OpacityTransition(TransitionTime));
            H.Check(Name + "_TransitionRestored", KeyDuration("Opacity") == TransitionTime, $"duration={KeyDuration("Opacity")}");
        }
    }

    /// <summary>
    /// §6.1 claim identity: a layout animation on a keyed Memo and on its target share one control.
    /// The outer one wins, value-only updates replace its claim in place, and removing it
    /// restores the inner one.
    /// </summary>
    internal sealed class ChainClaimIdentity(Harness h) : SlotsFixture(h, "ImplicitSlots_ChainClaimIdentity")
    {
        private static readonly TimeSpan Inner = TimeSpan.FromMilliseconds(700);

        private static Element Wrapped(TimeSpan? outer)
        {
            var memo = Memo("slots", static () => Target().LayoutAnimation(Inner));
            return outer is { } duration ? memo.LayoutAnimation(duration) : memo;
        }

        protected override async Task Run()
        {
            await Mount(Wrapped(TimeSpan.FromMilliseconds(900)));
            H.Check(Name + "_OuterWins", KeyDuration("Offset") == TimeSpan.FromMilliseconds(900), $"duration={KeyDuration("Offset")}");

            await Update(Wrapped(TimeSpan.FromMilliseconds(950)));
            await Update(Wrapped(TimeSpan.FromMilliseconds(990)));
            H.Check(Name + "_ValueUpdate", KeyDuration("Offset") == TimeSpan.FromMilliseconds(990), $"duration={KeyDuration("Offset")}");

            await Update(Wrapped(null));
            H.Check(Name + "_InnerRestored", KeyDuration("Offset") == Inner, $"duration={KeyDuration("Offset")}");
            int claims = (Control.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState)?.ImplicitClaims?.Count ?? 0;
            H.Check(Name + "_OneClaimLeft", claims == 1, $"claims={claims}");
        }
    }
}
