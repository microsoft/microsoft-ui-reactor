using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
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
            var next = Stage(child, stagger);
            Reconciler.Update(_current, next, Root, static () => { });
            _current = next;
            await Harness.Render();
            return Child;
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
                // InteractionStates takes the visual but installs nothing until hovered.
                var renter = Target("B").InteractionStates(static b => b.PointerOver(scale: 1.1f));
                second = await lab.Show(renter);
                H.Check(p + "_Recycled", ReferenceEquals(first, second));
                if (second is null) return;

                H.Check(p + "_IdentityVisual", visual.Opacity == 1 && visual.Scale == Vector3.One && visual.RotationAngle == 0
                    && visual.CenterPoint == Vector3.Zero && visual.TransformMatrix.IsIdentity && visual.Clip is null,
                    $"opacity={visual.Opacity} scale={visual.Scale} rotation={visual.RotationAngle} center={visual.CenterPoint}");
                H.Check(p + "_TranslationZero", second.Translation == Vector3.Zero
                    && (visual.Properties.TryGetVector3("Translation", out var t) != CompositionGetValueStatus.Succeeded || t == Vector3.Zero),
                    $"facade={second.Translation}");
                H.Check(p + "_NoRunningAnimation", !Running(visual, "Opacity") && !Running(visual, "Scale")
                    && !Running(visual, "RotationAngle") && !Running(visual, "Offset"));
                var keys = visual.ImplicitAnimations?.Select(kv => kv.Key).ToArray() ?? [];
                H.Check(p + "_NoImplicitKeys", keys.Length == 0, string.Join(",", keys));

                var rented = await Snap.CaptureAsync(lab.Root);
                using (var fresh = new Reconciler())
                {
                    var freshRoot = (FrameworkElement)fresh.Mount(Stage(renter), static () => { })!;
                    H.SetContent(freshRoot);
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
                // TranslationTransition stays on XAML until spec 066 layer 2, so it isn't demand yet.
                ("TranslationTransitionOnly", Target("R").TranslationTransition(), false, null),
                ("IdentityTransformRef", Target("R").Scale(1).Rotation(0).Ref(probe), false, _ =>
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
            const string p = "ElementPoolLanes_ExitTornDown";
            using var lab = new Lab(H);
            var exitControl = await lab.Show(
                Target("E").Transition(Animation.Transition.Exit(Animation.Transition.Fade), Curve.Linear(400)).WithKey("e"));
            H.Check(p + "_Mounted", exitControl is not null);
            if (exitControl is null) return;

            await lab.Show(null);
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
    }}
