using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Navigation;
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
            // Transitions honour the system's animation setting (spec 066 §5.5); these check motion.
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
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

    /// <summary>
    /// #1626: dropping a transition modifier releases its compositor key (or, for Translation, its
    /// write-time claim). Since spec 066 layer 2 the XAML properties are never set.
    /// </summary>
    internal sealed class RemovedTransitionClears(Harness h) : SlotsFixture(h, "ImplicitSlots_RemovedTransitionClears")
    {
        protected override async Task Run()
        {
            await Mount(Target().OpacityTransition().ScaleTransition().RotationTransition().TranslationTransition());
            var c = Control;
            H.Check(Name + "_Installed", Keys() == "Opacity,RotationAngle,Scale" && ImplicitAnimationSlots.TranslationTransition(c) is not null,
                $"keys={Keys()}");
            H.Check(Name + "_XamlUnset", c.OpacityTransition is null && c.ScaleTransition is null
                && c.RotationTransition is null && c.TranslationTransition is null);

            // Dropping one keeps the others.
            await Update(Target().ScaleTransition().RotationTransition().TranslationTransition());
            H.Check(Name + "_OpacityCleared", Keys() == "RotationAngle,Scale", $"keys={Keys()}");

            await Update(Target());
            H.Check(Name + "_AllCleared", Keys() == "-" && ImplicitAnimationSlots.TranslationTransition(c) is null, $"keys={Keys()}");
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

    /// <summary>
    /// §6.1 claim identity through a flyout, whose target updates on every render: each layer
    /// keeps its own slot across updates, including after the target's control is replaced.
    /// </summary>
    internal sealed class FlyoutChainClaimIdentity(Harness h) : SlotsFixture(h, "ImplicitSlots_FlyoutChainClaimIdentity")
    {
        private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

        private static Element Chain(Element target, int inner, int? outer)
        {
            var flyout = Flyout(target.LayoutAnimation(Ms(inner)), TextBlock("slots"));
            return outer is { } ms ? flyout.LayoutAnimation(Ms(ms)) : flyout;
        }

        private int Claims() =>
            (Control.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState)?.ImplicitClaims?.Count ?? 0;

        protected override async Task Run()
        {
            await Mount(Chain(Target(), 700, 900));
            H.Check(Name + "_OuterWins", KeyDuration("Offset") == Ms(900), $"duration={KeyDuration("Offset")}");

            await Update(Chain(Target(), 710, 910));
            H.Check(Name + "_OuterWinsAfterUpdate", KeyDuration("Offset") == Ms(910) && Claims() == 2,
                $"duration={KeyDuration("Offset")} claims={Claims()}");

            await Update(Chain(Target(), 720, null));
            H.Check(Name + "_LatestInnerRestored", KeyDuration("Offset") == Ms(720) && Claims() == 1,
                $"duration={KeyDuration("Offset")} claims={Claims()}");

            // A target of another type replaces the control under the flyout.
            var replaced = Control;
            await Update(Chain(Button("slots").Width(100).Height(40).HAlign(HorizontalAlignment.Left), 730, 930));
            H.Check(Name + "_Replaced", !ReferenceEquals(replaced, Control));
            H.Check(Name + "_ReplacedOuterWins", KeyDuration("Offset") == Ms(930) && Claims() == 2,
                $"duration={KeyDuration("Offset")} claims={Claims()}");

            await Update(Chain(Button("slots").Width(100).Height(40).HAlign(HorizontalAlignment.Left), 740, null));
            H.Check(Name + "_ReplacedInnerRestored", KeyDuration("Offset") == Ms(740) && Claims() == 1,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
        }
    }

    /// <summary>
    /// An ambient reorder move claims Offset below a declared layout animation, and leaves the
    /// element's other keys alone (spec 066 §6, spec 042 §6).
    /// </summary>
    internal sealed class AmbientMoveKeepsLayout(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly TimeSpan Layout = TimeSpan.FromMilliseconds(777);

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var keys = phase == 0 ? new[] { "a", "b", "c" } : new[] { "a", "c", "b" };
                return VStack(
                    Button("SlotsSwap", () => Animations.Animate(AnimationKind.Spring, () => setPhase(1))),
                    FlexColumn(keys.Select(k =>
                        Border(TextBlock(k).AutomationId($"slots_row_{k}"))
                            .LayoutAnimation(new LayoutAnimationConfig { Duration = Layout, AnimateSize = true })
                            .WithKey(k)).Cast<Element>().ToArray()));
            });
            await Harness.Render();

            H.ClickButton("SlotsSwap");
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "slots_row_c");
            var collection = tb?.Parent is Border row ? ElementCompositionPreview.GetElementVisual(row).ImplicitAnimations : null;
            var offset = collection is not null && collection.TryGetValue("Offset", out var anim) ? anim : null;
            H.Check("ImplicitSlots_AmbientMoveKeepsLayout_LayoutOffsetWins",
                offset is KeyFrameAnimation { Duration: var d } && d == Layout, $"offset={offset?.GetType().Name}");
            H.Check("ImplicitSlots_AmbientMoveKeepsLayout_SizeKept", collection?.ContainsKey("Size") == true);
        }
    }

    /// <summary>
    /// A transition instance reused with a new Duration rebuilds its compositor key.
    /// </summary>
    internal sealed class ReusedTransitionRepublishes(Harness h) : SlotsFixture(h, "ImplicitSlots_ReusedTransitionRepublishes")
    {
        protected override async Task Run()
        {
            var transition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(300) };
            await Mount(TakesVisual(Target()));
            await Update(TakesVisual(Target()).ScaleTransition(transition));
            H.Check(Name + "_Installed", KeyDuration("Scale") == TimeSpan.FromMilliseconds(300), $"duration={KeyDuration("Scale")}");

            transition.Duration = TimeSpan.FromMilliseconds(900);
            await Update(TakesVisual(Target()).Width(101).ScaleTransition(transition));
            H.Check(Name + "_NewDuration", KeyDuration("Scale") == TimeSpan.FromMilliseconds(900), $"duration={KeyDuration("Scale")}");
        }
    }

    /// <summary>
    /// A flyout drops <c>.Animate(Scale)</c> and changes its scale while its target keeps a
    /// transition: the target's re-claim must not bring the transition back before the flyout
    /// writes its scale, and the transition is back once the update ends.
    /// </summary>
    internal sealed class NestedDeferralWaitsForOuterWrites(Harness h) : SlotsFixture(h, "ImplicitSlots_NestedDeferralWaitsForOuterWrites")
    {
        private static readonly TimeSpan TransitionTime = TimeSpan.FromMilliseconds(777);

        private static Element Chain(bool transition, bool animate, float scale)
        {
            var target = TakesVisual(Target());
            var flyout = Flyout(transition ? target.ScaleTransition(new Vector3Transition { Duration = TransitionTime }) : target,
                TextBlock("slots"));
            Element outer = animate ? flyout.Animate(Curve.Linear(3000), AnimateProperty.Scale) : flyout;
            return outer.Scale(scale);
        }

        protected override async Task Run()
        {
            await Mount(Chain(transition: false, animate: true, scale: 1));
            await Update(Chain(transition: true, animate: true, scale: 1));
            H.Check(Name + "_AnimateWins", KeyDuration("Scale") == TimeSpan.FromMilliseconds(3000), $"duration={KeyDuration("Scale")}");

            bool animated = await Animates("Scale", Chain(transition: true, animate: false, scale: 1.5f));
            H.Check(Name + "_OuterWriteJumps", !animated);
            H.Check(Name + "_TransitionBack", KeyDuration("Scale") == TransitionTime, $"duration={KeyDuration("Scale")}");
        }
    }

    /// <summary>
    /// An update that throws after <c>.Animate()</c> released a key still restores the transition
    /// it held back, so a retry on the same control animates with the transition.
    /// </summary>
    internal sealed class DeferredRestoreSurvivesThrow(Harness h) : SlotsFixture(h, "ImplicitSlots_DeferredRestoreSurvivesThrow")
    {
        private static readonly TimeSpan TransitionTime = TimeSpan.FromMilliseconds(777);

        protected override async Task Run()
        {
            await Mount(TakesVisual(Target()));
            await Update(TakesVisual(Target()).OpacityTransition(TransitionTime)
                .Animate(Curve.Linear(3000), AnimateProperty.Opacity));
            H.Check(Name + "_AnimateWins", KeyDuration("Opacity") == TimeSpan.FromMilliseconds(3000), $"duration={KeyDuration("Opacity")}");

            bool threw = false;
            try
            {
                await Update(TakesVisual(Target()).OpacityTransition(TransitionTime)
                    .Set(static _ => throw new InvalidOperationException("slots")));
            }
            catch (InvalidOperationException) { threw = true; }
            H.Check(Name + "_UpdateThrew", threw);
            H.Check(Name + "_RestoredAfterThrow", KeyDuration("Opacity") == TransitionTime, $"duration={KeyDuration("Opacity")}");

            // The retry diffs against the last render that succeeded, so it runs the release (and
            // its flush) again; the transition must still be the key afterwards.
            await Update(TakesVisual(Target()).OpacityTransition(TransitionTime));
            H.Check(Name + "_RetryKeepsTransition", KeyDuration("Opacity") == TransitionTime, $"duration={KeyDuration("Opacity")}");
        }
    }

    /// <summary>
    /// Dropping <c>.Animate(Offset)</c> and <c>.LayoutAnimation()</c> together releases both
    /// before setters run, so a setter's Offset write in that update doesn't animate.
    /// </summary>
    internal sealed class DepartingLayoutReleasedBeforeSetters(Harness h) : SlotsFixture(h, "ImplicitSlots_DepartingLayoutReleasedBeforeSetters")
    {
        protected override async Task Run()
        {
            await Mount(Target().Animate(Curve.Linear(3000), AnimateProperty.Offset)
                .LayoutAnimation(TimeSpan.FromSeconds(5)));
            H.Check(Name + "_Installed", Keys() == "Offset", $"keys={Keys()}");

            var visual = ElementCompositionPreview.GetElementVisual(Control);
            visual.StopAnimation("Offset");
            bool keyDuringSetter = true;
            await Update(Target().Set(c =>
            {
                keyDuringSetter = visual.ImplicitAnimations?.ContainsKey("Offset") == true;
                visual.Offset = visual.Offset + new Vector3(5, 0, 0);
            }));
            H.Check(Name + "_NoKeyDuringSetter", !keyDuringSetter);
            H.Check(Name + "_SetterWriteJumped", visual.TryGetAnimationController("Offset") is null);
            H.Check(Name + "_Released", Keys() == "-", $"keys={Keys()}");

            // A retained layout animation that turns its Offset channel off releases it early too.
            await Update(Target().Animate(Curve.Linear(3000), AnimateProperty.Offset)
                .LayoutAnimation(TimeSpan.FromSeconds(5)));
            visual.StopAnimation("Offset");
            keyDuringSetter = true;
            await Update(Target().LayoutAnimation(new LayoutAnimationConfig { AnimateOffset = false, AnimateSize = true })
                .Set(c =>
                {
                    keyDuringSetter = visual.ImplicitAnimations?.ContainsKey("Offset") == true;
                    visual.Offset = visual.Offset + new Vector3(5, 0, 0);
                }));
            H.Check(Name + "_ChannelOffNoKeyDuringSetter", !keyDuringSetter);
            H.Check(Name + "_ChannelOffWriteJumped", visual.TryGetAnimationController("Offset") is null);
            H.Check(Name + "_ChannelOffKeepsSize", Keys() == "Size", $"keys={Keys()}");
        }
    }

    /// <summary>
    /// A decorator that forwards its child's control without naming it as its source target
    /// still counts as a layer: Update reaches its child one slot deeper, so Mount must too.
    /// </summary>
    internal sealed class OpaqueForwarderChain(Harness h) : SlotsFixture(h, "ImplicitSlots_OpaqueForwarderChain")
    {
        static OpaqueForwarderChain() =>
            ControlRegistry.RegisterDecorator<SlotsForwarder>(static () => new SlotsForwarderHandler());

        private static Element Button40() => Button("slots").Width(100).Height(40).HAlign(HorizontalAlignment.Left);

        protected override async Task Run()
        {
            await Mount(new SlotsForwarder(Target().LayoutAnimation()));
            H.Check(Name + "_Installed", Keys() == "Offset", $"keys={Keys()}");
            await Update(new SlotsForwarder(Target()));
            H.Check(Name + "_InnerRemoved", Keys() == "-", $"keys={Keys()}");

            // The child's type changes, so the forwarder hands back a new control.
            await Update(new SlotsForwarder(Button40().LayoutAnimation()));
            H.Check(Name + "_ReplacedInstalled", Keys() == "Offset", $"keys={Keys()}");
            await Update(new SlotsForwarder(Button40()));
            H.Check(Name + "_ReplacedInnerRemoved", Keys() == "-", $"keys={Keys()}");

            // Three layers, each with its own layout animation, across a replacement.
            static Element Nested(Element child, int inner, int? middle, int? outer)
            {
                Element mid = new SlotsForwarder(child.LayoutAnimation(TimeSpan.FromMilliseconds(inner)));
                if (middle is { } m) mid = mid.LayoutAnimation(TimeSpan.FromMilliseconds(m));
                Element top = new SlotsForwarder(mid);
                return outer is { } o ? top.LayoutAnimation(TimeSpan.FromMilliseconds(o)) : top;
            }
            int Claims() => (Control.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState)?.ImplicitClaims?.Count ?? 0;

            await Update(Nested(Target(), 700, 800, 900));
            H.Check(Name + "_NestedOuterWins", KeyDuration("Offset") == TimeSpan.FromMilliseconds(900) && Claims() == 3,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
            await Update(Nested(Button40(), 730, 830, 930));
            H.Check(Name + "_NestedReplacedOuterWins", KeyDuration("Offset") == TimeSpan.FromMilliseconds(930) && Claims() == 3,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
            await Update(Nested(Button40(), 740, 840, null));
            H.Check(Name + "_NestedMiddleRestored", KeyDuration("Offset") == TimeSpan.FromMilliseconds(840) && Claims() == 2,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
            await Update(Nested(Button40(), 750, null, null));
            H.Check(Name + "_NestedInnerRestored", KeyDuration("Offset") == TimeSpan.FromMilliseconds(750) && Claims() == 1,
                $"duration={KeyDuration("Offset")} claims={Claims()}");

            // Transitions on the outer layer follow a replaced control (spec 066 §5 on the
            // replaced-control path of Update): the child's type changes under them.
            var outerOpacity = TimeSpan.FromMilliseconds(640);
            static Element Outer(Element child, TimeSpan opacity) => new SlotsForwarder(child)
                .OpacityTransition(opacity).TranslationTransition(new Vector3Transition { Duration = opacity });
            await Update(Outer(Target(), outerOpacity));
            var before = Control;
            await Update(Outer(Button40(), outerOpacity));
            H.Check(Name + "_TransitionsReplaced", !ReferenceEquals(before, Control) && ElementPool.IsVisualTaken(Control)
                && KeyDuration("Opacity") == outerOpacity && ImplicitAnimationSlots.TranslationTransition(Control) is not null,
                $"replaced={!ReferenceEquals(before, Control)} keys={Keys()} duration={KeyDuration("Opacity")}");
            H.Check(Name + "_TransitionsReplacedAnimate", await Animates("Opacity", Outer(Button40().Opacity(0.5), outerOpacity)));

            // .Animate() per channel through forwarders that name no source target: the middle
            // layer's Offset declaration animates the leaf's Translation write, past the outer and
            // leaf Opacity-only declarations. Without it, the write jumps.
            static Element Three(float x, bool middle)
            {
                Element inner = new SlotsForwarder(Target().Animate(Curve.Linear(1000), AnimateProperty.Opacity).Translation(x, 0, 0));
                if (middle) inner = inner.Animate(Curve.Linear(3000), AnimateProperty.Offset);
                return new SlotsForwarder(inner).Animate(Curve.Linear(1000), AnimateProperty.Opacity);
            }
            await Update(Three(10, middle: true));
            await Update(Three(20, middle: true));
            H.Check(Name + "_ThreeLayerChannelLookup", await Animates("Translation", Three(40, middle: true)));
            await Update(Three(10, middle: false));
            await Update(Three(20, middle: false));
            H.Check(Name + "_ThreeLayerChannelLookup_NegativeControl", !await Animates("Translation", Three(40, middle: false)));

            // The same lookup when the Translation write comes from the outer layer and the inner
            // layers declare nothing but .Animate(): their shared-control edges are still kept.
            static Element OuterWrite(float x, bool middle)
            {
                Element inner = new SlotsForwarder(Target().Animate(Curve.Linear(1000), AnimateProperty.Opacity));
                if (middle) inner = inner.Animate(Curve.Linear(3000), AnimateProperty.Offset);
                return new SlotsForwarder(inner).Animate(Curve.Linear(1000), AnimateProperty.Opacity).Translation(x, 0, 0);
            }
            await Update(OuterWrite(10, middle: true));
            await Update(OuterWrite(20, middle: true));
            H.Check(Name + "_OuterWriteChannelLookup", await Animates("Translation", OuterWrite(40, middle: true)));
            await Update(OuterWrite(10, middle: false));
            await Update(OuterWrite(20, middle: false));
            H.Check(Name + "_OuterWriteChannelLookup_NegativeControl", !await Animates("Translation", OuterWrite(40, middle: false)));

            // Featureless forwarders around a leaf whose control is replaced: the leaf's claim
            // must still be released when its layout animation goes.
            static Element Bare(Element leaf) => new SlotsForwarder(new SlotsForwarder(leaf));
            await Update(Bare(Target().LayoutAnimation()));
            await Update(Bare(Button40().LayoutAnimation()));
            H.Check(Name + "_BareReplacedInstalled", Keys() == "Offset", $"keys={Keys()}");
            await Update(Bare(Button40()));
            H.Check(Name + "_BareReplacedRemoved", Keys() == "-" && Claims() == 0, $"keys={Keys()} claims={Claims()}");
            await Update(Bare(Target().LayoutAnimation()));
            await Update(Bare(Target()));
            H.Check(Name + "_BareMountedRemoved", Keys() == "-" && Claims() == 0, $"keys={Keys()} claims={Claims()}");

            // The middle forwarder claims in Update on a replaced control its featureless leaf
            // mounted; the outer forwarder must still number it one slot inward.
            static Element Mixed(Element leaf, bool middle) =>
                new SlotsForwarder(middle ? new SlotsForwarder(leaf).LayoutAnimation(TimeSpan.FromMilliseconds(800)) : new SlotsForwarder(leaf))
                    .Animate(Curve.Linear(900), AnimateProperty.Offset);
            await Update(Mixed(Target(), middle: true));
            await Update(Mixed(Button40(), middle: true));
            H.Check(Name + "_MixedReplacedLayoutWins", KeyDuration("Offset") == TimeSpan.FromMilliseconds(800) && Claims() == 2,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
            await Update(Mixed(Button40(), middle: false));
            H.Check(Name + "_MixedMiddleRemoved", KeyDuration("Offset") == TimeSpan.FromMilliseconds(900) && Claims() == 1,
                $"duration={KeyDuration("Offset")} claims={Claims()}");
        }
    }

    internal sealed record SlotsForwarder(Element Target) : Element;

    private sealed class SlotsForwarderHandler : IDecoratorElementHandler<SlotsForwarder>
    {
        public UIElement Mount(MountContext context, SlotsForwarder element) => context.MountChild(element.Target)!;
        public UIElement Update(UpdateContext context, SlotsForwarder oldEl, SlotsForwarder newEl, UIElement control)
            => context.ReconcileChild(oldEl.Target, newEl.Target, control)!;
        public V1UnmountDisposition Unmount(UnmountContext context, SlotsForwarder? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;
    }

    /// <summary>
    /// Releasing a layout animation or a transition still moves a facade Translation that a setter
    /// wrote in the same update onto the visual those features took.
    /// </summary>
    internal sealed class SetterTranslationMigrates(Harness h) : SlotsFixture(h, "ImplicitSlots_SetterTranslationMigrates")
    {
        private static BorderElement Shift(float x) => Target().Set(b => b.Translation = new Vector3(x, 0, 0));

        private Vector3 VisualTranslation()
        {
            ElementCompositionPreview.GetElementVisual(Control).Properties.TryGetVector3("Translation", out var value);
            return value;
        }

        protected override async Task Run()
        {
            await Mount(Shift(10).LayoutAnimation());
            H.Check(Name + "_MountedOnVisual", VisualTranslation() == new Vector3(10, 0, 0), $"translation={VisualTranslation()}");
            await Update(Shift(20));
            H.Check(Name + "_LayoutRemoved", VisualTranslation() == new Vector3(20, 0, 0), $"translation={VisualTranslation()}");

            await Update(Shift(20).Animate(Curve.Linear(30), AnimateProperty.Opacity).OpacityTransition());
            await Update(Shift(30).OpacityTransition());
            H.Check(Name + "_TransitionRetained", VisualTranslation() == new Vector3(30, 0, 0), $"translation={VisualTranslation()}");

            // Dropping the last transition still moves the setter's value: the visual stays taken.
            await Update(Shift(40));
            H.Check(Name + "_TransitionRemoved", VisualTranslation() == new Vector3(40, 0, 0), $"translation={VisualTranslation()}");
        }
    }
}
