using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §7: the pool's two lanes. A control whose composition visual was taken goes to the
/// visual lane, reset by the §7.3 list, and only a renter that takes the visual itself and has no
/// setters gets it back (#1624, #1643). Both lanes clear the theme transitions (#1639).
/// </summary>
internal static class PoolLaneFixtures
{
    private const string Red = "#FFFF0000";

    private static BorderElement Target(string text) => Border(TextBlock(text)).Width(100).Height(40)
        .Background(Red).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);

    private static Element Stage(Element? child, bool stagger = false)
    {
        var stage = VStack(0, child).Width(300).Height(120)
            .Background("#FFFFFFFF").HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);
        return stagger ? stage.Stagger(TimeSpan.FromMilliseconds(40)) : stage;
    }

    /// <summary>
    /// A reconciler mounting one child directly under a fixed stage; the pool is the reconciler's
    /// own. The child is a direct child because a control nested in a removed panel isn't recycled.
    /// </summary>
    private sealed class Lab : IDisposable
    {
        private readonly Harness _h;
        private Element _current = Stage(null);
        public readonly Reconciler Reconciler = new();
        public readonly WinUI.StackPanel Root;

        public Lab(Harness h)
        {
            _h = h;
            Root = (WinUI.StackPanel)Reconciler.Mount(_current, static () => { })!;
            h.SetContent(Root);
        }

        public FrameworkElement? Child => Root.Children.Count > 0 ? (FrameworkElement)Root.Children[0] : null;

        public async Task<FrameworkElement?> Show(Element? child, bool stagger = false)
        {
            Set(child, stagger);
            await Harness.Render();
            return Child;
        }

        public void Set(Element? child, bool stagger = false)
        {
            var next = Stage(child, stagger);
            Reconciler.Update(_current, next, Root, static () => { });
            _current = next;
        }

        public void Dispose()
        {
            Reconciler.UnmountChild(Root);
            _h.SetContent(null);
            Reconciler.Dispose();
        }
    }

    private static bool Running(Visual v, string channel)
    {
        try { return v.TryGetAnimationController(channel) is not null; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Rent after return through the visual lane: the same control comes back, pixel-identical to
    /// a fresh one, with an identity visual, no previous implicit key and no running animation.
    /// </summary>
    internal sealed class VisualLane(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_VisualLane";
            FrameworkElement? first, second;
            Visual visual;
            using (var lab = new Lab(H))
            {
                // InteractionStates takes the visual without claiming any key, so whatever the
                // visual holds below was left by user code, and only the reset can clear it.
                first = await lab.Show(Target("A").InteractionStates(static b => b.PointerOver(scale: 1.1f)));
                H.Check(p + "_Mounted", first is WinUI.Border);
                if (first is null) return;
                H.Check(p + "_VisualTaken", ElementPool.IsVisualTaken(first));
                visual = ElementCompositionPreview.GetElementVisual(first);
                var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1, 0.2f);
                fade.Duration = TimeSpan.FromSeconds(60);
                visual.StartAnimation("Opacity", fade);
                // Whoever left them, only the reset's identity writes restore these.
                visual.Scale = new Vector3(1.5f, 1.5f, 1);
                visual.RotationAngleInDegrees = 20;
                visual.CenterPoint = new Vector3(10, 5, 0);
                visual.IsVisible = false;
                // The reset writes no Offset (layout owns it), so only its StopAnimation ends this one.
                var offset = visual.Compositor.CreateVector3KeyFrameAnimation();
                offset.InsertKeyFrame(1, new Vector3(40, 0, 0));
                offset.Duration = TimeSpan.FromSeconds(60);
                visual.StartAnimation("Offset", offset);
                // A key no Reactor claim owns, so only the reset's collection clear drops it.
                var stray = visual.Compositor.CreateScalarKeyFrameAnimation();
                stray.InsertExpressionKeyFrame(1, "this.FinalValue");
                stray.Duration = TimeSpan.FromSeconds(60);
                stray.Target = "RotationAngle";
                (visual.ImplicitAnimations ??= visual.Compositor.CreateImplicitAnimationCollection())["RotationAngle"] = stray;
                await Harness.Render();
                H.Check(p + "_StateBeforeReturn", Running(visual, "Opacity") && Running(visual, "Offset") && visual.Scale.X > 1.4f
                    && visual.RotationAngleInDegrees > 19 && visual.ImplicitAnimations?.ContainsKey("RotationAngle") == true,
                    $"opacityAnim={Running(visual, "Opacity")} offsetAnim={Running(visual, "Offset")} key={visual.ImplicitAnimations?.ContainsKey("RotationAngle")} scale={visual.Scale} rotation={visual.RotationAngleInDegrees}");
                // User code may write the Translation facade after the handoff; the reset zeroes it.
                first.Translation = new Vector3(12, 0, 0);

                await lab.Show(null);
                var renter = Target("B").LayoutAnimation();
                second = await lab.Show(renter);
                H.Check(p + "_Recycled", ReferenceEquals(first, second));
                if (second is null) return;

                H.Check(p + "_IdentityVisual", visual.Opacity == 1 && visual.Scale == Vector3.One && visual.RotationAngle == 0
                    && visual.CenterPoint == Vector3.Zero && visual.TransformMatrix.IsIdentity && visual.Clip is null && visual.IsVisible,
                    $"opacity={visual.Opacity} scale={visual.Scale} rotation={visual.RotationAngle} center={visual.CenterPoint}");
                H.Check(p + "_TranslationZero", second.Translation == Vector3.Zero
                    && (visual.Properties.TryGetVector3("Translation", out var t) != CompositionGetValueStatus.Succeeded || t == Vector3.Zero),
                    $"facade={second.Translation}");
                H.Check(p + "_NoRunningAnimation", !Running(visual, "Opacity") && !Running(visual, "Scale")
                    && !Running(visual, "RotationAngle") && !Running(visual, "Offset"));
                // Only the renter's own .LayoutAnimation() key.
                var keys = visual.ImplicitAnimations?.Select(kv => kv.Key).ToArray() ?? [];
                H.Check(p + "_OnlyRenterKeys", keys.SequenceEqual(["Offset"]), string.Join(",", keys));

                var rented = await Snap.CaptureAsync(lab.Root);
                using (var fresh = new Reconciler())
                {
                    var freshRoot = (FrameworkElement)fresh.Mount(Stage(renter), static () => { })!;
                    H.SetContent(freshRoot);
                    // Capture only once the fresh stage has the same layout size as the rented one.
                    for (int i = 0; i < 20 && (freshRoot.ActualWidth != lab.Root.ActualWidth
                        || freshRoot.ActualHeight != lab.Root.ActualHeight); i++)
                        await Harness.Render(16);
                    await Harness.Render();
                    int diff = rented.Diff(await Snap.CaptureAsync(freshRoot));
                    H.Check(p + "_PixelIdenticalToFresh", diff is >= 0 and <= 4, $"diff={diff}");
                    fresh.UnmountChild(freshRoot);
                    H.SetContent(lab.Root);
                }

                // The stray key is gone, so a write lands without animating.
                await lab.Show(renter.Opacity(0.5));
                H.Check(p + "_WriteDoesNotAnimate", !Running(visual, "Opacity") && Math.Abs(visual.Opacity - 0.5) < 0.001,
                    $"opacity={visual.Opacity}");
            }

            // A brush transition coexists with the handoff, so the visual lane clears it.
            using (var lab = new Lab(H))
            {
                var transitioned = await lab.Show(VStack(TextBlock("bg")).BackgroundTransition(TimeSpan.FromSeconds(1)).LayoutAnimation());
                H.Check(p + "_BackgroundTransitionApplied", transitioned is WinUI.StackPanel { BackgroundTransition: not null });
                await lab.Show(null);
                var renter = await lab.Show(VStack(TextBlock("bg2")).LayoutAnimation());
                H.Check(p + "_BackgroundTransitionCleared", ReferenceEquals(renter, transitioned)
                    && renter is WinUI.StackPanel { BackgroundTransition: null });
            }
        }
    }

    /// <summary>
    /// The lane rules (§7.2): only a renter with demand and no setters, mount callbacks or
    /// identity-only transforms gets a visual-lane control, so user code never meets a control it
    /// couldn't have met fresh.
    /// </summary>
    internal sealed class LaneRules(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new ElementRef();
            var cases = new (string Name, Element Renter, bool Recycled, Func<FrameworkElement, string?>? After)[]
            {
                // Positive control: without it the negative cases could pass on a broken pool.
                ("Demand", Target("R").Animate(Curve.Linear(100)), true, null),
                ("Plain", Target("R"), false, null),
                ("Setter", Target("R").Set(static b => b.Rotation = 10), false,
                    static c => c.Rotation == 10 ? null : $"rotation={c.Rotation}"),
                ("SetterWithDemand", Target("R").Set(static b => b.Rotation = 10).Animate(Curve.Linear(100)), false, null),
                ("OnMountWithDemand", Target("R").Animate(Curve.Linear(100)).OnMount(static fe => fe.Rotation = 10), false, null),
                // Since spec 066 layer 2 every .XxxTransition() but Background takes the visual at mount.
                ("TranslationTransitionOnly", Target("R").TranslationTransition(), true, null),
                ("OpacityTransitionOnly", Target("R").OpacityTransition(), true, null),
                ("IdentityTransformRef", Target("R").Scale(1).Rotation(0).Ref(probe), false, _ =>
                {
                    try { probe.Current!.Rotation = 10; return null; }
                    catch (UnauthorizedAccessException) { return "the ref's facade write threw"; }
                }),
                // These take the visual only on hover or removal, so a fresh control's facades
                // stay writable until then (spec 066 §7.2).
                ("InteractionStatesOnly", Target("R").InteractionStates(static b => b.PointerOver(scale: 1.1f)).Ref(probe), false, _ =>
                {
                    try { probe.Current!.Rotation = 10; return null; }
                    catch (UnauthorizedAccessException) { return "the ref's facade write threw"; }
                }),
                ("ExitOnlyTransition", Target("R").Transition(Animation.Transition.Exit(Animation.Transition.Fade)).Ref(probe), false, _ =>
                {
                    try { probe.Current!.Rotation = 10; return null; }
                    catch (UnauthorizedAccessException) { return "the ref's facade write threw"; }
                }),
            };

            foreach (var (name, renter, recycled, after) in cases)
            {
                string p = "ElementPoolLanes_LaneRules_" + name;
                using var lab = new Lab(H);
                var taken = await lab.Show(Target("T").Animate(Curve.Linear(100)));
                await lab.Show(null);
                if (taken is null || !ElementPool.IsVisualTaken(taken)) { H.Check(p + "_Setup", false); continue; }

                FrameworkElement? rented;
                try { rented = await lab.Show(renter); }
                catch (Exception ex)
                {
                    H.Check(p + "_Mounts", false, $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                H.Check(p + "_Mounts", rented is WinUI.Border);
                if (rented is null) continue;
                H.Check(p + (recycled ? "_FromVisualLane" : "_NotFromVisualLane"), ReferenceEquals(taken, rented) == recycled);
                if (after?.Invoke(rented) is { } failure) H.Check(p + "_UserCode", false, failure);
            }
        }
    }

    /// <summary>
    /// #1643: a staggered panel takes every child's visual without any feature of the child's own.
    /// Such a child goes to the visual lane, so a plain renter's facade setter doesn't throw, and a
    /// renter that takes the visual itself gets it back.
    /// </summary>
    internal sealed class StaggerMarker(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_Stagger";
            using var lab = new Lab(H);
            var staggered = await lab.Show(Target("S"), stagger: true);
            H.Check(p + "_ChildMarked", staggered is not null && ElementPool.IsVisualTaken(staggered));
            await lab.Show(null);

            FrameworkElement? plain;
            try { plain = await lab.Show(Target("P").Set(static b => b.Rotation = 10)); }
            catch (Exception ex)
            {
                H.Check(p + "_PlainSetterDoesNotThrow", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            H.Check(p + "_PlainSetterDoesNotThrow", plain is { Rotation: 10 });
            H.Check(p + "_NotRentedByPlain", !ReferenceEquals(plain, staggered));
            await lab.Show(null);

            var demanding = await lab.Show(Target("D").Animate(Curve.Linear(100)));
            H.Check(p + "_RentedFromVisualLane", ReferenceEquals(demanding, staggered));
        }
    }
    /// <summary>
    /// #1624: implicit transitions don't follow a pooled control to its next renter. Background
    /// stays a XAML transition (spec 066 §5.6) and is cleared. A user <c>.Set</c> is the only way left to set
    /// <c>OpacityTransition</c>, and such a control never reaches another renter. A compositor transition goes to the visual lane,
    /// and its key doesn't reach the renter.
    /// </summary>
    internal sealed class ImplicitTransitionResidue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Transitions honour the system's animation setting (spec 066 §5.5); this checks their keys.
            using var motion = Microsoft.UI.Reactor.Navigation.TransitionEngine.OverrideAnimationsEnabled(true);
            const string p = "ElementPoolLanes_ImplicitTransitions";
            using (var lab = new Lab(H))
            {
                var first = await lab.Show(VStack(TextBlock("b")).BackgroundTransition());
                H.Check(p + "_BackgroundApplied", first is WinUI.StackPanel { BackgroundTransition: not null });
                await lab.Show(null);
                var second = await lab.Show(VStack(TextBlock("c")));
                H.Check(p + "_BackgroundRecycled", ReferenceEquals(first, second));
                H.Check(p + "_BackgroundCleared", second is WinUI.StackPanel { BackgroundTransition: null });
            }

            using (var lab = new Lab(H))
            {
                var first = await lab.Show(Target("o").Set(static b => b.OpacityTransition = new ScalarTransition()));
                H.Check(p + "_UserOpacityApplied", first is { OpacityTransition: not null });
                await lab.Show(null);
                var second = await lab.Show(Target("p"));
                // Like a transform transition, it locks RenderTransform, so the pool's reset is refused
                // and the control is dropped rather than handed on.
                H.Check(p + "_UserOpacityDropped", second is not null && !ReferenceEquals(first, second));
                H.Check(p + "_UserOpacityNotInherited", second is { OpacityTransition: null });
            }

            using (var lab = new Lab(H))
            {
                var first = await lab.Show(Target("t").OpacityTransition().RotationTransition());
                H.Check(p + "_CompositorTaken", first is not null && ElementPool.IsVisualTaken(first) && first.OpacityTransition is null);
                await lab.Show(null);
                var second = await lab.Show(Target("u").ScaleTransition());
                H.Check(p + "_CompositorRecycled", ReferenceEquals(first, second));
                if (second is null) return;
                var keys = ElementCompositionPreview.GetElementVisual(second).ImplicitAnimations?.Select(kv => kv.Key).ToArray() ?? [];
                H.Check(p + "_OnlyRenterKeys", keys.SequenceEqual(["Scale"]), string.Join(",", keys));
            }
        }
    }

    /// <summary>#1639: theme transitions don't follow a pooled panel, border or button to its next renter.</summary>
    internal sealed class ThemeTransitions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Case<WinUI.StackPanel>("StackPanel",
                text => VStack(TextBlock(text)), static c => c.ChildrenTransitions);
            await Case<WinUI.Border>("Border",
                text => Border(TextBlock(text)), static c => c.ChildTransitions);
            await Case<WinUI.Button>("Button",
                text => Button(text), static c => c.ContentTransitions);
        }

        private async Task Case<TControl>(string name, Func<string, Element> build, Func<TControl, TransitionCollection?> read)
            where TControl : FrameworkElement
        {
            string p = "ElementPoolLanes_ThemeTransitions_" + name;
            using var lab = new Lab(H);
            var first = await lab.Show(build("first").WithTransitions(new EntranceThemeTransition()));
            H.Check(p + "_Applied", first is TControl t && read(t) is { Count: > 0 });
            await lab.Show(null);
            var second = await lab.Show(build("second"));
            H.Check(p + "_Recycled", ReferenceEquals(first, second));
            H.Check(p + "_Cleared", second is TControl s && read(s) is null or { Count: 0 });
        }
    }

    /// <summary>
    /// A control torn down with an ancestor while its exit transition plays goes to the visual
    /// lane and can be rented again before that exit completes. The exit's completion must then
    /// neither reset nor remove and unmount the new renter. The teardown is driven directly,
    /// because a control nested in a removed panel isn't recycled.
    /// </summary>
    internal sealed class ExitTornDown(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // A .Transition() exit, and the fade an ambient Animations.Animate removal plays.
            await Case("ExitTornDown",
                Target("E").Transition(Animation.Transition.Exit(Animation.Transition.Fade), Curve.Linear(400)).WithKey("e"),
                static lab => lab.Set(null));
            await Case("ExitTornDown_Ambient", Target("E").WithKey("e"),
                static lab => Animations.Animate(AnimationKind.EaseIn, () => { lab.Set(null); return 0; }));
        }

        private async Task Case(string p, Element exiting, Action<Lab> remove)
        {
            p = "ElementPoolLanes_" + p;
            using var lab = new Lab(H);
            var exitControl = await lab.Show(exiting);
            H.Check(p + "_Mounted", exitControl is not null);
            if (exitControl is null) return;

            remove(lab);
            await Harness.Render();
            H.Check(p + "_ExitPlaying", lab.Reconciler.IsExiting(exitControl) && ReferenceEquals(lab.Child, exitControl));
            // What an ancestor's teardown does to a child it reaches mid-exit.
            lab.Root.Children.Remove(exitControl);
            lab.Reconciler.UnmountAndPool(exitControl);
            H.Check(p + "_TornDown", !lab.Reconciler.IsExiting(exitControl));
            // The reset stops the exit, so its batch completes now. A completion that still acted
            // would return the control to the pool a second time.
            await Task.Delay(300);
            await Harness.Render();

            var renter = await lab.Show(Target("N").Animate(Curve.Linear(100)));
            H.Check(p + "_RentedFromVisualLane", ReferenceEquals(renter, exitControl));
            var another = lab.Reconciler.Mount(Target("N2").Animate(Curve.Linear(100)), static () => { });
            H.Check(p + "_PooledOnce", another is not null && !ReferenceEquals(another, exitControl));
            if (another is not null) lab.Reconciler.UnmountChild(another);
            await Task.Delay(600);
            await Harness.Render();
            H.Check(p + "_RenterStillMounted", lab.Child is { } live && ReferenceEquals(live, renter)
                && ReferenceEquals(live.Parent, lab.Root));
            H.Check(p + "_RenterOpaque", renter is not null
                && Math.Abs(ElementCompositionPreview.GetElementVisual(renter).Opacity - 1) < 0.001);
        }
    }

    /// <summary>
    /// An app handler for a built-in element may write a facade in its Mount, which works on a
    /// fresh control. It must never be handed a visual-lane control (spec 066 §7.2).
    /// </summary>
    internal sealed class AppHandler(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class RotatingBorderHandler : IElementHandler<BorderElement, WinUI.Border>
        {
            public WinUI.Border Mount(MountContext ctx, BorderElement element)
            {
                var border = ctx.RentControl<WinUI.Border>();
                border.Rotation = 10;
                return border;
            }

            public void Update(UpdateContext ctx, BorderElement oldEl, BorderElement newEl, WinUI.Border control) { }
        }

        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_AppHandler";
            using var lab = new Lab(H);
            lab.Reconciler.RegisterHandler(new RotatingBorderHandler());
            var taken = await lab.Show(Target("T").Animate(Curve.Linear(100)));
            H.Check(p + "_FirstTakesVisual", taken is not null && ElementPool.IsVisualTaken(taken));
            await lab.Show(null);

            FrameworkElement? second;
            try { second = await lab.Show(Target("R").Animate(Curve.Linear(100))); }
            catch (Exception ex)
            {
                H.Check(p + "_FacadeWriteWorks", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            H.Check(p + "_FacadeWriteWorks", second is not null);
            H.Check(p + "_NotFromVisualLane", !ReferenceEquals(second, taken));
        }
    }

    /// <summary>A control returned twice is pooled once, in either lane, so two renters never share it.</summary>
    internal sealed class ReturnTwice(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;
            foreach (bool taken in new[] { false, true })
            {
                string p = "ElementPoolLanes_ReturnTwice_" + (taken ? "Visual" : "Plain");
                using var pool = new ElementPool();
                var border = new WinUI.Border();
                if (taken) AnimationHelper.GetElementVisual(border);
                pool.Return(border);
                pool.Return(border);
                H.Check(p + "_RentedOnce", ReferenceEquals(pool.TryRent(typeof(WinUI.Border), visualLane: taken), border)
                    && pool.TryRent(typeof(WinUI.Border), visualLane: taken) is null);
            }
        }
    }

    /// <summary>
    /// A keyed <c>Memo</c> over a control can declare keyframes the control's own unmount cleanup
    /// doesn't reach. The visual reset drops their trigger entries, so a renter with the same
    /// keyframe name and trigger still plays.
    /// </summary>
    internal sealed class MemoKeyframes(Harness h) : SelfTestFixtureBase(h)
    {
        private static KeyframeBuilder Pulse(KeyframeBuilder k) => k.Duration(60000).At(0f, opacity: 0.5f).At(1f, opacity: 1f);

        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_MemoKeyframes";
            using var lab = new Lab(H);
            var first = await lab.Show(Memo("k", static () => Target("M")).Keyframes("pulse", 1, Pulse));
            H.Check(p + "_Mounted", first is WinUI.Border && ElementPool.IsVisualTaken(first));
            await lab.Show(null);

            var renter = await lab.Show(Target("R").Keyframes("pulse", 1, Pulse));
            H.Check(p + "_Recycled", ReferenceEquals(renter, first));
            H.Check(p + "_RenterPlays", renter is not null
                && Running(ElementCompositionPreview.GetElementVisual(renter), "Opacity"));
        }
    }

    /// <summary>
    /// A control removed mid-hover still carries its interaction state's brush. Unmount puts the
    /// normal brush back, so the next visual-lane renter doesn't show the hover color.
    /// </summary>
    internal sealed class HoverBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_HoverBrush";
            using var lab = new Lab(H);
            var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            var hovered = await lab.Show(TextBlock("H").InteractionStates(b => b.PointerOver(foreground: hoverBrush)));
            H.Check(p + "_Mounted", hovered is WinUI.TextBlock);
            if (hovered is null) return;
            Reconciler.SimulatePointerOverForTests(hovered, isOver: true);
            H.Check(p + "_HoverApplied", hovered.ReadLocalValue(WinUI.TextBlock.ForegroundProperty)
                is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.R: 255, Color.G: 0 });
            await lab.Show(null);

            var renter = await lab.Show(TextBlock("R").Animate(Curve.Linear(100)));
            H.Check(p + "_Recycled", ReferenceEquals(renter, hovered));
            var local = renter?.ReadLocalValue(WinUI.TextBlock.ForegroundProperty);
            H.Check(p + "_ForegroundRestored", renter is not null && local == DependencyProperty.UnsetValue, $"local={local}");
        }
    }

    /// <summary>
    /// Visual state no layer of Reactor tracks: relative adjustments user code wrote through the
    /// visual, and a brush transition on any poolable panel or border. The visual reset clears both.
    /// </summary>
    internal sealed class ResetLeftovers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_ResetLeftovers";
            using (var lab = new Lab(H))
            {
                var first = await lab.Show(Target("A").Animate(Curve.Linear(100)));
                if (first is null) { H.Check(p + "_Mounted", false); return; }
                var visual = ElementCompositionPreview.GetElementVisual(first);
                visual.RelativeOffsetAdjustment = new Vector3(0.5f, 0, 0);
                visual.RelativeSizeAdjustment = new Vector2(0.5f, 0);
                await lab.Show(null);
                var renter = await lab.Show(Target("R").Animate(Curve.Linear(100)));
                H.Check(p + "_Recycled", ReferenceEquals(renter, first));
                H.Check(p + "_RelativeAdjustmentsZero", visual.RelativeOffsetAdjustment == Vector3.Zero
                    && visual.RelativeSizeAdjustment == Vector2.Zero,
                    $"offset={visual.RelativeOffsetAdjustment} size={visual.RelativeSizeAdjustment}");
            }

            await BrushTransition<WinUI.Canvas>(p + "_Canvas", text => Canvas(TextBlock(text)),
                static c => c.BackgroundTransition, static (c, t) => c.BackgroundTransition = t);
            await BrushTransition<WinUI.Border>(p + "_Border", text => Border(TextBlock(text)),
                static c => c.BackgroundTransition, static (c, t) => c.BackgroundTransition = t);
        }

        private async Task BrushTransition<TControl>(string p, Func<string, Element> build,
            Func<TControl, BrushTransition?> read, Action<TControl, BrushTransition> write) where TControl : FrameworkElement
        {
            using var lab = new Lab(H);
            var first = await lab.Show(build("A").OnMount(fe => write((TControl)fe, new BrushTransition())).Animate(Curve.Linear(100)));
            H.Check(p + "_TransitionApplied", first is TControl t && read(t) is not null && ElementPool.IsVisualTaken(t));
            await lab.Show(null);
            var renter = await lab.Show(build("R").Animate(Curve.Linear(100)));
            H.Check(p + "_Recycled", ReferenceEquals(renter, first));
            H.Check(p + "_TransitionCleared", renter is TControl r && read(r) is null);
        }
    }

    /// <summary>
    /// Layout writes a visual's Offset only when the position changes, so whatever moved the visual
    /// last would stay on a renter at the same position: a stopped <c>.ScrollLinked</c> expression
    /// (direct, or a keyed <c>Memo</c>'s, which the control's unmount doesn't reach) or a direct write
    /// through the visual. The visual reset puts the layout offset back.
    /// </summary>
    internal sealed class LayoutOffset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var viewer = new WinUI.ScrollViewer();
            await Case("Memo", Memo("k", static () => Target("M"))
                .ScrollLinked(viewer, static b => b.Expression("Offset.Y", "40")), null);
            await Case("Direct", Target("D").ScrollLinked(viewer, static b => b.Expression("Offset.Y", "40")), null);
            await Case("VisualWrite", Target("W").Animate(Curve.Linear(100)),
                static fe => ElementCompositionPreview.GetElementVisual(fe).Offset = new Vector3(0, 40, 0));
        }

        private async Task Case(string name, Element moved, Action<FrameworkElement>? move)
        {
            string p = "ElementPoolLanes_LayoutOffset_" + name;
            using var lab = new Lab(H);
            var first = await lab.Show(moved);
            H.Check(p + "_Mounted", first is WinUI.Border && ElementPool.IsVisualTaken(first));
            if (first is null) return;
            move?.Invoke(first);
            await Harness.Render();
            await lab.Show(null);

            var renter = Target("R").Animate(Curve.Linear(100));
            var rentedControl = await lab.Show(renter);
            H.Check(p + "_Recycled", ReferenceEquals(rentedControl, first));
            if (rentedControl is null) return;
            var offset = ElementCompositionPreview.GetElementVisual(rentedControl).Offset;
            H.Check(p + "_OffsetIsLayout", Math.Abs(offset.Y - rentedControl.ActualOffset.Y) < 0.5f,
                $"visual={offset} layout={rentedControl.ActualOffset}");
            var rented = await Snap.CaptureAsync(lab.Root);
            using var fresh = new Reconciler();
            var freshRoot = (FrameworkElement)fresh.Mount(Stage(renter), static () => { })!;
            H.SetContent(freshRoot);
            for (int i = 0; i < 20 && (freshRoot.ActualWidth != lab.Root.ActualWidth
                || freshRoot.ActualHeight != lab.Root.ActualHeight); i++)
                await Harness.Render(16);
            await Harness.Render();
            int diff = rented.Diff(await Snap.CaptureAsync(freshRoot));
            H.Check(p + "_PixelIdenticalToFresh", diff is >= 0 and <= 4, $"diff={diff}");
            fresh.UnmountChild(freshRoot);
            H.SetContent(lab.Root);
        }
    }

    /// <summary>
    /// A handler registered for <c>KeyedMemoElement</c> is app code: it may write a facade on the
    /// control its child mounts, so that child never rents from the visual lane.
    /// </summary>
    internal sealed class MemoAppHandler(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class RotatingMemoHandler : IElementHandler<KeyedMemoElement, WinUI.Border>
        {
            public WinUI.Border Mount(MountContext ctx, KeyedMemoElement element)
            {
                var border = (WinUI.Border)ctx.MountChild(element.Factory())!;
                border.Rotation = 10;
                return border;
            }

            public void Update(UpdateContext ctx, KeyedMemoElement oldEl, KeyedMemoElement newEl, WinUI.Border control) { }
        }

        public override async Task RunAsync()
        {
            const string p = "ElementPoolLanes_MemoAppHandler";
            using var lab = new Lab(H);
            var taken = await lab.Show(Target("T").Animate(Curve.Linear(100)));
            H.Check(p + "_FirstTakesVisual", taken is not null && ElementPool.IsVisualTaken(taken));
            await lab.Show(null);

            lab.Reconciler.RegisterHandler(new RotatingMemoHandler());
            FrameworkElement? second;
            try { second = await lab.Show(Memo("k", static () => Target("R")).Animate(Curve.Linear(100))); }
            catch (Exception ex)
            {
                H.Check(p + "_FacadeWriteWorks", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            H.Check(p + "_FacadeWriteWorks", second is not null);
            H.Check(p + "_NotFromVisualLane", !ReferenceEquals(second, taken));
        }
    }
}
