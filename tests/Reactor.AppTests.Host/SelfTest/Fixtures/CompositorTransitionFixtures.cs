using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §5 (layer 2): the implicit transitions run on the composition visual with the XAML
/// curve, its axis mask and its reduced-motion behaviour, and never block the handoff (#1623).
/// </summary>
/// <remarks>
/// Motion is read from the visual's animation controller, which a write that animates leaves on
/// its channel. Curves are read from a RenderTargetBitmap of a red 100x40 target at (50,50) in a
/// white stage, with the controller paused and seeked, so no verdict depends on wall-clock time.
/// </remarks>
internal static class CompositorTransitionFixtures
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(3);
    private const double Left = 50, Top = 50;

    private static BorderElement Target() => Border(TextBlock(" "))
        .Width(100).Height(40)
        .Background(new SolidColorBrush(Microsoft.UI.Colors.Red))
        .HAlign(HorizontalAlignment.Left)
        .Margin(Left, Top, 0, 0);

    private static Vector3Transition Vector(TimeSpan duration, Vector3TransitionComponents? components = null)
    {
        var t = new Vector3Transition { Duration = duration };
        if (components is { } c) t.Components = c;
        return t;
    }

    internal abstract class TransitionFixture(Harness h, string name) : SelfTestFixtureBase(h)
    {
        private Reconciler? _reconciler;
        private StackPanel? _stage;
        private Element? _current;
        // RenderTargetBitmap of the stage itself doesn't show its children's composition animations
        // in flight, so captures go through a host around it, as the transforms matrix does.
        private readonly Border _host = new();

        protected string Name { get; } = name;
        protected FrameworkElement Control => (FrameworkElement)_stage!.Children[0];
        protected StackPanel Stage => _stage!;

        public sealed override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            try { await Run(); }
            catch (FrameTimeoutException ex) { H.Skip(Name, ex.Message); }
            catch (Exception ex) { H.Check(Name + "_NoThrow", false, $"{ex.GetType().Name}: {ex.Message}"); }
            finally { await Reset(); }
        }

        protected abstract Task Run();

        protected async Task Reset()
        {
            _host.Child = null;
            if (_stage is not null) _reconciler!.UnmountChild(_stage);
            _reconciler?.Dispose();
            _reconciler = null;
            _stage = null;
            _current = null;
            H.SetContent(null);
            await Task.Yield();
        }

        private static Element StageOf(Element child) => VStack(child)
            .Width(400).Height(400)
            .Background(new SolidColorBrush(Microsoft.UI.Colors.White))
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);

        protected async Task Mount(Element child)
        {
            await Reset();
            _reconciler = new Reconciler();
            _current = StageOf(child);
            _stage = (StackPanel)_reconciler.Mount(_current, static () => { })!;
            _host.Child = _stage;
            H.SetContent(_host);
            await TransformsMatrixRecorder.Settle();
        }

        protected async Task Update(Element child, bool settle = true)
        {
            var next = StageOf(child);
            _reconciler!.Update(_current!, next, _stage!, static () => { });
            _current = next;
            if (settle) await TransformsMatrixRecorder.Settle();
        }

        protected Visual Visual => ElementCompositionPreview.GetElementVisual(Control);

        /// <summary>Renders <paramref name="child"/> and reports whether it started an animation on <paramref name="channel"/>.</summary>
        protected async Task<bool> Animates(string channel, Element child)
        {
            var visual = Visual;
            StopIfPresent(visual, channel);
            var before = TransformsMatrixRecorder.Controller(visual, channel);
            await Update(child);
            var after = TransformsMatrixRecorder.Controller(visual, channel);
            StopIfPresent(visual, channel);
            return after is not null && !Equals(before, after);
        }

        protected static void StopIfPresent(Visual visual, string channel)
        {
            if (channel == "Translation" && visual.Properties.TryGetVector3("Translation", out _) != CompositionGetValueStatus.Succeeded)
                return;
            visual.StopAnimation(channel);
        }

        protected async Task<Box?> Box()
            => (await Snap.CaptureAsync(_host)).RedBox();

        /// <summary>
        /// The curve oracles' positive control: a seeked linear Scale animation reads half way. A
        /// desktop that doesn't render composition animations into RenderTargetBitmap (a locked or
        /// disconnected session) fails it, and the fixture skips rather than reporting a curve.
        /// </summary>
        protected async Task<bool> SeekedMotionVisible()
        {
            await Mount(Target());
            var visual = Visual;
            var anim = visual.Compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(1, new Vector3(2, 2, 1), visual.Compositor.CreateLinearEasingFunction());
            anim.Duration = TimeSpan.FromSeconds(10);
            visual.StartAnimation("Scale", anim);
            double mid = double.NaN;
            if (visual.TryGetAnimationController("Scale") is { } controller)
            {
                controller.Pause();
                controller.Progress = 0.5f;
                await TransformsMatrixRecorder.Settle();
                mid = await Box() is { } box ? (box.X1 - box.X0 - 100) / 100 : double.NaN;
            }
            visual.StopAnimation("Scale");
            if (mid is > 0.4 and < 0.6) return true;
            H.Skip(Name, string.Create(global::System.Globalization.CultureInfo.InvariantCulture,
                $"RenderTargetBitmap shows no seeked composition animation on this desktop (mid={mid:F2}), the same instrument failure TransformsMatrix_Instrument_Motion reports"));
            return false;
        }

        protected string Keys()
        {
            var keys = Visual.ImplicitAnimations?.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
            return keys.Length > 0 ? string.Join(",", keys) : "-";
        }

        protected bool XamlTransitionsUnset()
            => Control.OpacityTransition is null && Control.ScaleTransition is null
                && Control.RotationTransition is null && Control.TranslationTransition is null;
    }

    // ── #1623 ────────────────────────────────────────────────────────────────

    private static readonly string[] Transitions = ["Opacity", "Scale", "Rotation", "Translation"];
    private static readonly string[] Features = ["LayoutAnimation", "Animate", "TransitionEnter", "InteractionStates", "Keyframes"];

    private static T WithTransition<T>(T e, string transition) where T : Element => transition switch
    {
        "Opacity" => e.OpacityTransition(Long),
        "Scale" => e.ScaleTransition(Vector(Long)),
        "Rotation" => e.RotationTransition(Long),
        _ => e.TranslationTransition(Vector(Long)),
    };

    private static Element WithChange(Element e, string transition) => transition switch
    {
        "Opacity" => e.Opacity(0.5),
        "Scale" => e.Scale(1.5f),
        "Rotation" => e.Rotation(30),
        _ => e.Translation(20, 0, 0),
    };

    private static string Channel(string transition) => transition switch
    {
        "Rotation" => "RotationAngle",
        _ => transition,
    };

    private static Element WithFeature(Element e, string feature) => feature switch
    {
        "LayoutAnimation" => e.LayoutAnimation(),
        // CenterPoint only, so the transition keeps its own key (.Animate() wins a shared one).
        "Animate" => e.Animate(Animation.Curve.Linear(30), AnimateProperty.CenterPoint),
        "TransitionEnter" => e.Transition(Animation.Transition.Fade, Animation.Curve.Linear(30)),
        "InteractionStates" => e.InteractionStates(static s => s.PointerOver(scale: 1.1f), Animation.Curve.Linear(30)),
        _ => e.Keyframes("t", 1, static k => k.Duration(30).At(0f, opacity: 0.5f).At(1f, opacity: 1f)),
    };

    /// <summary>
    /// #1623: each transition combined with each compositor feature, mounted together, the feature
    /// added later, and the transition added later. Nothing throws, the XAML property stays unset,
    /// and a value change still animates on the visual.
    /// </summary>
    internal sealed class NoBlock(Harness h, string transition) : TransitionFixture(h, "CompositorTransitions_NoBlock_" + transition)
    {
        protected override async Task Run()
        {
            // Positive control: a XAML transition left by user code blocks the same handoff.
            bool blocked = false;
            try { await Mount(WithFeature(Target().Set(static b => b.OpacityTransition = new ScalarTransition()), "LayoutAnimation")); }
            catch (UnauthorizedAccessException) { blocked = true; }
            H.Check(Name + "_PositiveControl_XamlTransitionBlocks", blocked);

            foreach (var feature in Features)
                foreach (var order in new[] { "Mount", "Update", "FeatureFirst" })
                    await Case(feature, order);
        }

        private async Task Case(string feature, string order)
        {
            string p = $"{Name}_{feature}_{order}";
            Element Full(Element e) => WithFeature(WithTransition(e, transition), feature);
            try
            {
                await Mount(order switch
                {
                    "Mount" => Full(Target()),
                    "Update" => WithTransition(Target(), transition),
                    _ => WithFeature(Target(), feature),
                });
                if (order != "Mount") await Update(Full(Target()));
                if (feature == "InteractionStates")
                {
                    Reconciler.SimulatePointerOverForTests(Control, isOver: true);
                    await TransformsMatrixRecorder.Settle();
                    Reconciler.SimulatePointerOverForTests(Control, isOver: false);
                    await TransformsMatrixRecorder.Settle(400);
                }
            }
            catch (Exception ex) when (ex is not FrameTimeoutException)
            {
                H.Check(p + "_NoThrow", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }

            H.Check(p + "_VisualTaken", ElementPool.IsVisualTaken(Control));
            H.Check(p + "_XamlUnset", XamlTransitionsUnset());
            bool animates;
            try { animates = await Animates(Channel(transition), WithChange(Full(Target()), transition)); }
            catch (Exception ex) when (ex is not FrameTimeoutException)
            {
                H.Check(p + "_ChangeNoThrow", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            H.Check(p + "_ChangeAnimates", animates);
        }
    }

    // ── Curve (§5.1) ─────────────────────────────────────────────────────────

    /// <summary>The XAML transition curve, cubic-bezier (0.8,0,0.2,1), at time fraction <paramref name="x"/>.</summary>
    internal static double XamlCurve(double x)
    {
        const double x1 = 0.8, x2 = 0.2;
        static double Bezier(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double lo = 0, hi = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (Bezier(mid, x1, x2) < x) lo = mid; else hi = mid;
        }
        return Bezier((lo + hi) / 2, 0, 1);
    }

    /// <summary>
    /// Whether <paramref name="measured"/> progress at time fraction <paramref name="at"/> follows the
    /// XAML curve: within one 60 Hz frame of time (§12.1 A), plus <paramref name="pixel"/> of pose
    /// quantization.
    /// </summary>
    internal static bool FollowsXamlCurve(double measured, double at, TimeSpan duration, double pixel)
    {
        double frame = 1000.0 / 60 / duration.TotalMilliseconds;
        double lo = XamlCurve(Math.Max(0, at - frame)) - pixel, hi = XamlCurve(Math.Min(1, at + frame)) + pixel;
        return measured >= lo && measured <= hi;
    }

    /// <summary>
    /// §5.1: a Scale transition and a Translation transition follow (0.8,0,0.2,1) at 25, 50 and 75%
    /// of the transition's duration. The positive control is a linear <c>.Animate()</c>, which the
    /// same oracle must reject.
    /// </summary>
    internal sealed class CurveParity(Harness h) : TransitionFixture(h, "CompositorTransitions_Curve")
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

        protected override async Task Run()
        {
            if (!await SeekedMotionVisible()) return;
            // Scale: the target is anchored top-left, so its width is 100 × scale.
            await Sample("Scale", "Scale",
                Target().Scale(1).ScaleTransition(Vector(Duration)), Target().Scale(2).ScaleTransition(Vector(Duration)),
                box => box.X1 - box.X0 - 100, 100, expectXaml: true);
            await Sample("Translation", "Translation",
                Target().Translation(0, 0, 0).TranslationTransition(Vector(Duration)), Target().Translation(100, 0, 0).TranslationTransition(Vector(Duration)),
                box => box.X0 - Left, 100, expectXaml: true);
            await Sample("PositiveControl_Linear", "Scale",
                Target().Scale(1).Animate(Animation.Curve.Linear((int)Duration.TotalMilliseconds), AnimateProperty.Scale),
                Target().Scale(2).Animate(Animation.Curve.Linear((int)Duration.TotalMilliseconds), AnimateProperty.Scale),
                box => box.X1 - box.X0 - 100, 100, expectXaml: false);
        }

        private async Task Sample(string label, string channel, Element from, Element to, Func<Box, double> travel, double span, bool expectXaml)
        {
            string p = $"{Name}_{label}";
            await Mount(from);
            await Update(to, settle: false);
            await TransformsMatrixRecorder.Frames(2);
            var visual = Visual;
            if (TransformsMatrixRecorder.Controller(visual, channel) is not { } controller)
            {
                H.Check(p + "_Animates", false, "no controller");
                return;
            }
            controller.Pause();
            var readings = new List<string>();
            bool allFollow = true;
            foreach (var at in new[] { 0.25, 0.5, 0.75 })
            {
                controller.Progress = (float)at;
                await TransformsMatrixRecorder.Settle();
                double progress = await Box() is { } box ? travel(box) / span : double.NaN;
                bool follows = FollowsXamlCurve(progress, at, Duration, 2.0 / span);
                allFollow &= follows;
                readings.Add(string.Create(global::System.Globalization.CultureInfo.InvariantCulture,
                    $"{at:F2}->{progress:F3} (xaml {XamlCurve(at):F3})"));
            }
            controller.Progress = 0.5f;
            controller.Resume();
            visual.StopAnimation(channel);
            H.Check(p + (expectXaml ? "_FollowsXamlCurve" : "_Rejected"), allFollow == expectXaml, string.Join("; ", readings));
        }
    }

    // ── TranslationTransition (§5.3) ─────────────────────────────────────────

    /// <summary>
    /// §5.3: <c>.TranslationTransition()</c> animates <c>.Translation()</c> writes, honours its axis
    /// mask, doesn't animate a layout move, and an ambient <c>WithAnimation</c> curve wins over it.
    /// </summary>
    internal sealed class Translation(Harness h) : TransitionFixture(h, "CompositorTransitions_Translation")
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

        protected override async Task Run()
        {
            // A layout move doesn't animate under a TranslationTransition (Q1b). The control is a
            // layout animation, which does.
            await Mount(Target().TranslationTransition(Vector(Long)));
            await Update(Target().Margin(Left + 100, Top, 0, 0).TranslationTransition(Vector(Long)), settle: false);
            await TransformsMatrixRecorder.Frames(2);
            H.Check(Name + "_LayoutMoveJumps", TransformsMatrixRecorder.Controller(Visual, "Offset") is null);
            await Mount(Target().LayoutAnimation(Long));
            await Update(Target().Margin(Left + 100, Top, 0, 0).LayoutAnimation(Long), settle: false);
            await TransformsMatrixRecorder.Frames(2);
            H.Check(Name + "_PositiveControl_LayoutMoveAnimates", TransformsMatrixRecorder.Controller(Visual, "Offset") is not null);
            // The implicit-key form of the mask (§5.2): a Components = X Scale key starts Y and Z at
            // their final value, which takes a second keyframe. The full-mask control has one.
            await Mount(Target().ScaleTransition(Vector(Long, Vector3TransitionComponents.X)));
            uint masked = ScaleKeyFrames();
            await Mount(Target().ScaleTransition(Vector(Long)));
            uint full = ScaleKeyFrames();
            H.Check(Name + "_ScaleMaskKeyFrames", masked == 2 && full == 1, $"masked={masked} full={full}");

            if (!await SeekedMotionVisible()) return;

            // Seeked: width eases while height has already jumped.
            await Mount(Target().Scale(1).ScaleTransition(Vector(Duration, Vector3TransitionComponents.X)));
            await Update(Target().Scale(2).ScaleTransition(Vector(Duration, Vector3TransitionComponents.X)), settle: false);
            await TransformsMatrixRecorder.Frames(2);
            var scaleVisual = Visual;
            if (TransformsMatrixRecorder.Controller(scaleVisual, "Scale") is { } scaleController)
            {
                scaleController.Pause();
                scaleController.Progress = 0.5f;
                await TransformsMatrixRecorder.Settle();
                var box = await Box();
                double w = box is { } wb ? (wb.X1 - wb.X0 - 100) / 100 : double.NaN, hgt = box is { } hb ? (hb.Y1 - hb.Y0 - 40) / 40 : double.NaN;
                scaleController.Progress = 0.5f;
                scaleController.Resume();
                scaleVisual.StopAnimation("Scale");
                H.Check(Name + "_ScaleMaskX_WidthEases_HeightJumps", Math.Abs(w - 0.5) < 0.1 && Math.Abs(hgt - 1) < 0.06, $"w={w:F3} h={hgt:F3}");
            }
            else H.Check(Name + "_ScaleMaskX_WidthEases_HeightJumps", false, "no controller");

            // Mask: X animates, Y jumps. The positive control animates both.
            var (maskX, maskY) = await Midpoint(Vector(Duration, Vector3TransitionComponents.X), ambient: null);
            H.Check(Name + "_MaskX_XAnimates", Math.Abs(maskX - 0.5) < 0.1, $"x={maskX:F3}");
            H.Check(Name + "_MaskX_YJumps", Math.Abs(maskY - 1) < 0.06, $"y={maskY:F3}");
            var (fullX, fullY) = await Midpoint(Vector(Duration), ambient: null);
            H.Check(Name + "_PositiveControl_FullMaskAnimatesY", Math.Abs(fullY - 0.5) < 0.1 && Math.Abs(fullX - 0.5) < 0.1, $"x={fullX:F3} y={fullY:F3}");

            // WithAnimation wins: linear on every axis, so Y is a quarter of the way at 25%.
            var (ambX, ambY) = await Midpoint(Vector(Duration, Vector3TransitionComponents.X),
                ambient: Animation.Curve.Linear((int)Duration.TotalMilliseconds), at: 0.25f);
            H.Check(Name + "_WithAnimationWins", Math.Abs(ambX - 0.25) < 0.06 && Math.Abs(ambY - 0.25) < 0.06, $"x={ambX:F3} y={ambY:F3}");

        }

        private uint ScaleKeyFrames() =>
            Visual.ImplicitAnimations is { } keys && keys.TryGetValue("Scale", out var anim) && anim is KeyFrameAnimation kfa
                ? (uint)kfa.KeyFrameCount : 0;

        /// <summary>Progress of X (0→100) and Y (0→40) with the Translation animation seeked to <paramref name="at"/>.</summary>
        private async Task<(double X, double Y)> Midpoint(Vector3Transition transition, Animation.Curve? ambient, float at = 0.5f)
        {
            await Mount(Target().Translation(0, 0, 0).TranslationTransition(transition));
            var next = Target().Translation(100, 40, 0).TranslationTransition(transition);
            if (ambient is null) await Update(next, settle: false);
            else AnimationScope.WithAnimation(ambient, () => _ = Update(next, settle: false));
            await TransformsMatrixRecorder.Frames(2);
            var visual = Visual;
            if (TransformsMatrixRecorder.Controller(visual, "Translation") is not { } controller)
                return (double.NaN, double.NaN);
            controller.Pause();
            controller.Progress = at;
            await TransformsMatrixRecorder.Frames(3);
            var box = await Box();
            controller.Progress = 0.5f;
            controller.Resume();
            visual.StopAnimation("Translation");
            return box is { } b ? ((b.X0 - Left) / 100, (b.Y0 - Top) / 40) : (double.NaN, double.NaN);
        }
    }

    // ── Reduced motion (§5.5) ────────────────────────────────────────────────

    /// <summary>
    /// §5.5: with "Animation effects" off, transitions jump, as XAML's do. The visual is still taken,
    /// so readback and the pool lane don't depend on the setting. The positive control runs the same
    /// writes with animations on.
    /// </summary>
    internal sealed class ReducedMotion(Harness h) : TransitionFixture(h, "CompositorTransitions_ReducedMotion")
    {
        protected override async Task Run()
        {
            // The override isn't nestable (disposing clears it), so every block sets its own.
            foreach (bool enabled in new[] { true, false })
            {
                using var motion = TransitionEngine.OverrideAnimationsEnabled(enabled);
                string p = Name + (enabled ? "_PositiveControl_Enabled" : "_Disabled");
                foreach (var transition in Transitions)
                {
                    await Mount(WithTransition(Target(), transition));
                    H.Check($"{p}_{transition}_VisualTaken", ElementPool.IsVisualTaken(Control));
                    bool animates = await Animates(Channel(transition), WithChange(WithTransition(Target(), transition), transition));
                    H.Check($"{p}_{transition}_{(enabled ? "Animates" : "Jumps")}", animates == enabled);
                }
            }

            // The setting takes effect at the element's next transition apply.
            using (TransitionEngine.OverrideAnimationsEnabled(true))
                await Mount(Target().ScaleTransition(Vector(Long)));
            using (TransitionEngine.OverrideAnimationsEnabled(false))
            {
                bool animates = await Animates("Scale", Target().Scale(1.5f).ScaleTransition(Vector(Long)));
                H.Check(Name + "_TurnedOffLater_Jumps", !animates && Keys() == "-", $"keys={Keys()}");
            }
            using (TransitionEngine.OverrideAnimationsEnabled(true))
            {
                bool restored = await Animates("Scale", Target().Scale(1.2f).ScaleTransition(Vector(Long)));
                H.Check(Name + "_TurnedOnAgain_Animates", restored);
            }
        }
    }

    // ── Ordering (§6.3) ──────────────────────────────────────────────────────

    /// <summary>
    /// §6.3: on update the transition policy runs before setters and modifier writes, so dropping a
    /// transition and changing the value in one reconcile jumps, and adding one animates.
    /// </summary>
    internal sealed class SameReconcile(Harness h) : TransitionFixture(h, "CompositorTransitions_SameReconcile")
    {
        protected override async Task Run()
        {
            foreach (var transition in Transitions)
            {
                string p = $"{Name}_{transition}";
                // Positive control: the transition kept, the value changed.
                await Mount(WithTransition(Target(), transition));
                H.Check(p + "_PositiveControl_Kept", await Animates(Channel(transition), WithChange(WithTransition(Target(), transition), transition)));

                await Mount(WithTransition(Target(), transition));
                H.Check(p + "_DroppedJumps", !await Animates(Channel(transition), WithChange(Target().Animate(Animation.Curve.Linear(30), AnimateProperty.CenterPoint), transition)));

                // A one-shot keyframe takes the visual first, so the controller can be read either way.
                await Mount(TakesVisual(Target()));
                H.Check(p + "_AddedAnimates", await Animates(Channel(transition), WithChange(WithTransition(TakesVisual(Target()), transition), transition)));
            }
        }

        private static Element TakesVisual(Element e) =>
            e.Keyframes("same", 1, static k => k.Duration(30).At(0f, scale: Vector3.One).At(1f, scale: Vector3.One));
    }

    // ── Readback and Background (§9 row 2, §5.6) ─────────────────────────────

    /// <summary>
    /// §5.3: <c>.Animate()</c> resolves per channel across the decorator chain. An outer declaration
    /// that excludes Translation doesn't hide an inner one that includes it. The control has no
    /// inner declaration, so the same write jumps.
    /// </summary>
    internal sealed class AnimateChannelLookup(Harness h) : TransitionFixture(h, "CompositorTransitions_AnimateChannelLookup")
    {
        private static Element Chain(float x, bool inner)
        {
            var target = inner ? Target().Animate(Animation.Curve.Linear(3000), AnimateProperty.Offset) : Target();
            return Flyout(target, TextBlock("content")).Animate(Animation.Curve.Linear(1000), AnimateProperty.Opacity).Translation(x, 0, 0);
        }

        // The inner layer writes Translation; only the outer layer declares it.
        private static Element InnerWrite(float x) =>
            Flyout(Target().Animate(Animation.Curve.Linear(1000), AnimateProperty.Opacity).Translation(x, 0, 0), TextBlock("content"))
                .Animate(Animation.Curve.Linear(3000), AnimateProperty.Offset);

        protected override async Task Run()
        {
            await Mount(Chain(0, inner: true));
            H.Check(Name + "_InnerDeclarationAnimates", await Animates("Translation", Chain(40, inner: true)));
            await Mount(InnerWrite(0));
            H.Check(Name + "_OuterDeclarationAnimatesInnerWrite", await Animates("Translation", InnerWrite(40)));
            await Mount(Chain(0, inner: false));
            H.Check(Name + "_NegativeControl_NoInnerJumps", !await Animates("Translation", Chain(40, inner: false)));
        }
    }

    /// <summary>
    /// §9 row 2: an element with a transition keeps its transforms on the visual, so the facade getters
    /// read identity. The positive control has no transition and reads the facade (layer 4 changes it).
    /// </summary>
    internal sealed class Readback(Harness h) : TransitionFixture(h, "CompositorTransitions_Readback")
    {
        protected override async Task Run()
        {
            await Mount(Target().Scale(2).Rotation(30).OpacityTransition());
            H.Check(Name + "_FacadeIdentity", Control.Scale == Vector3.One && Control.Rotation == 0, $"scale={Control.Scale} rotation={Control.Rotation}");
            H.Check(Name + "_VisualHoldsValue", Visual.Scale == new Vector3(2, 2, 1) && Math.Abs(Visual.RotationAngleInDegrees - 30) < 0.01f,
                $"scale={Visual.Scale} rotation={Visual.RotationAngleInDegrees}");
            await Mount(Target().Scale(2).Rotation(30));
            H.Check(Name + "_PositiveControl_PlainReadsFacade", Control.Scale == new Vector3(2, 2, 1) && Control.Rotation == 30);
        }
    }

    /// <summary>
    /// The transition-animation cache doesn't keep a removed <c>.Stagger()</c> delay: after the
    /// parent drops it, the child's Opacity key has no delay. While the stagger stays, the
    /// second child's key keeps its delay (the positive control).
    /// </summary>
    internal sealed class StaggerRemoved(Harness h) : TransitionFixture(h, "CompositorTransitions_StaggerRemoved")
    {
        private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

        private static Element Staggered(double opacity, bool stagger)
        {
            var panel = VStack(Target().OpacityTransition(Long), Target().Opacity(opacity).OpacityTransition(Long));
            return stagger ? panel.Stagger(Step) : panel;
        }

        private TimeSpan? SecondDelay()
        {
            var child = ((Panel)Control).Children[1];
            return ElementCompositionPreview.GetElementVisual(child).ImplicitAnimations is { } keys
                && keys.TryGetValue("Opacity", out var anim) && anim is KeyFrameAnimation kfa ? kfa.DelayTime : null;
        }

        protected override async Task Run()
        {
            await Mount(Staggered(1, stagger: true));
            await Update(Staggered(0.9, stagger: true));
            H.Check(Name + "_PositiveControl_KeptWhileStaggered", SecondDelay() == Step, $"delay={SecondDelay()}");
            await Update(Staggered(0.9, stagger: false));
            await Update(Staggered(0.8, stagger: false));
            H.Check(Name + "_DelayDroppedWithStagger", SecondDelay() == TimeSpan.Zero, $"delay={SecondDelay()}");
        }
    }

    /// <summary>#1626, Background arm: dropping <c>.BackgroundTransition()</c> clears the XAML property.</summary>
    internal sealed class BackgroundRemoved(Harness h) : TransitionFixture(h, "CompositorTransitions_BackgroundRemoved")
    {
        protected override async Task Run()
        {
            await Mount(VStack(TextBlock("s")).BackgroundTransition());
            H.Check(Name + "_StackApplied", Control is StackPanel { BackgroundTransition: not null });
            await Update(VStack(TextBlock("s")).BackgroundTransition(TimeSpan.FromMilliseconds(200)));
            H.Check(Name + "_PositiveControl_Kept", Control is StackPanel { BackgroundTransition: not null });
            await Update(VStack(TextBlock("s")));
            H.Check(Name + "_StackCleared", Control is StackPanel { BackgroundTransition: null });

            await Mount(Grid([], [], TextBlock("g")).BackgroundTransition());
            await Update(Grid([], [], TextBlock("g")));
            H.Check(Name + "_GridCleared", Control is Grid { BackgroundTransition: null });
            H.Check(Name + "_BackgroundDoesNotTakeVisual", !ElementPool.IsVisualTaken(Control));
        }
    }

    // ── Diagnostic (§9 rows 1 and 4) ─────────────────────────────────────────

    /// <summary>
    /// §9 rows 1 and 4: when WinUI rejects a setter's facade write after the handoff, or a user
    /// facade blocks the handoff, a warning names the modifier, and the exception is unchanged.
    /// The negative control is the same setter on an element without a transition.
    /// </summary>
    internal sealed class Diagnostic(Harness h) : TransitionFixture(h, "CompositorTransitions_Diagnostic")
    {
        // EventListener callbacks don't flow under NativeAOT (TESTING.md), so the warning oracle
        // can't observe anything there.
        private void Warns(string name, bool condition, string detail)
        {
            if (global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) H.Check(name, condition, detail);
            else H.Skip(name, "EventListener callbacks don't flow under NativeAOT");
        }

        protected override async Task Run()
        {
            using var listener = new DiagnosticsListener();

            listener.Reset();
            Exception? handoff = null;
            try { await Mount(Target().Set(static b => b.RotationAxis = Vector3.UnitX).OpacityTransition()); }
            catch (Exception ex) { handoff = ex; }
            H.Check(Name + "_Handoff_Throws", handoff is UnauthorizedAccessException, handoff?.GetType().Name ?? "none");
            Warns(Name + "_Handoff_Warns", listener.Describe().Contains("W:Reactor/" + FacadeConflictDiagnostic.HandoffOperation, StringComparison.Ordinal), listener.Describe());

            static Element WithSetter(float rotation, bool transition)
            {
                var e = Target().Set(b => b.Rotation = rotation);
                return transition ? e.OpacityTransition() : e;
            }

            await Mount(WithSetter(0, transition: true));
            listener.Reset();
            Exception? setter = null;
            try { await Update(WithSetter(10, transition: true)); }
            catch (Exception ex) { setter = ex; }
            H.Check(Name + "_Setter_Throws", setter is UnauthorizedAccessException, setter?.GetType().Name ?? "none");
            Warns(Name + "_Setter_Warns", listener.Describe().Contains("W:Reactor/" + FacadeConflictDiagnostic.SetterOperation, StringComparison.Ordinal), listener.Describe());

            await Mount(WithSetter(0, transition: false));
            listener.Reset();
            await Update(WithSetter(10, transition: false));
            H.Check(Name + "_NegativeControl_PlainSetterSilent", Control.Rotation == 10 && listener.Describe() == "-", listener.Describe());

            // A decorator that replaces a blocked control: the transitions go to the new control,
            // and the departing one's blocked visual doesn't fail the update.
            await Mount(Flyout(Target().Set(static b => b.OpacityTransition = new ScalarTransition()), TextBlock("content")));
            var departing = Control;
            Exception? replaced = null;
            try { await Update(Flyout(Button("new").Width(100).Height(40), TextBlock("content")).OpacityTransition(Long)); }
            catch (Exception ex) { replaced = ex; }
            H.Check(Name + "_ReplacedBlockedControl_NoThrow", replaced is null, replaced?.GetType().Name ?? "");
            H.Check(Name + "_ReplacedBlockedControl_Transitions", replaced is null && !ReferenceEquals(departing, Control)
                && Control is Button && Keys() == "Opacity", $"keys={(replaced is null ? Keys() : "-")}");
        }
    }
}
