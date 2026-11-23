using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §8 (#773): <c>.TransformOrigin(x, y)</c>, a size-relative pivot bound as an expression on
/// the handoff visual. Each case reads a RenderTargetBitmap bounding box of a red 100×40 target laid out
/// at (120,150) in a 400×400 white stage and compares it with the box its pivot predicts. Every case
/// that expects a pivot also checks that the box it expects differs from the box the wrong pivot gives,
/// so the oracle can tell them apart.
/// </summary>
internal sealed class TransformOriginFixture(Harness h, string scenario) : SelfTestFixtureBase(h)
{
    private const string P = "TransformOrigin";
    private const float Left = 120, Top = 150, Width = 100, Height = 40, StageSize = 400, Tolerance = 2;
    private const string Red = "#FFFF0000";
    private static readonly Vector2 Centre = new(0.5f, 0.5f);

    // One registered fixture per scenario, so each stays inside the per-fixture time limit.
    public override async Task RunAsync()
    {
        await (scenario switch
        {
            "Centre" => ControlAndCentre(),
            "Resize" => Resize(),
            "Rtl" => RightToLeft(),
            "Unset" => Unset(),
            "Offset" => Offset(),
            "WithAnimationOffset" => AnimatedOffset(animate: false),
            "AnimateOffset" => AnimatedOffset(animate: true),
            "Hover" => Hover(),
            "Pool" => Pool(),
            "Navigation" => Navigation(),
            _ => throw new ArgumentException($"Unknown TransformOrigin scenario {scenario}."),
        });
        H.SetContent(null);
    }

    private static BorderElement Target(float width = Width) => Border(TextBlock(" ")).Width(width).Height(Height)
        .Background(Red).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Margin(Left, Top, 0, 0);

    // The box of a target of the given width scaled and rotated about (px, py), in flow coordinates
    // (the transform space is flow-relative, Q9), mirrored about the stage in RTL.
    private static Box Expect(float px, float py, float scale = 1, float degrees = 0, float width = Width, bool rtl = false)
    {
        double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (cx, cy) in new (double, double)[] { (0, 0), (width, 0), (0, Height), (width, Height) })
        {
            double vx = (cx - px) * scale, vy = (cy - py) * scale;
            double x = vx * cos - vy * sin + px + Left, y = vx * sin + vy * cos + py + Top;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        return rtl ? new Box(StageSize - x1, y0, StageSize - x0, y1) : new Box(x0, y0, x1, y1);
    }

    // ── Cases ───────────────────────────────────────────────────────────────

    private async Task ControlAndCentre()
    {
        using var lab = new Lab(H);
        // Positive control: the oracle sees a top-left scale, and a point only that pose covers hits.
        var control = await lab.Show(Target().Scale(2));
        await CheckPose(P + "_Control_TopLeftScale", lab, Expect(0, 0, scale: 2));
        H.Check(P + "_Control_HitsTopLeftOnlyPoint", Hits(lab, control!, 300, 220));
        Distinct(P + "_Control_CentreDiffers", Expect(0, 0, scale: 2), Expect(50, 20, scale: 2));

        var scaled = await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(2));
        H.Check(P + "_Centre_VisualTaken", scaled is not null && ElementPool.IsVisualTaken(scaled));
        await CheckPose(P + "_Centre_Scale", lab, Expect(50, 20, scale: 2));
        // Hit-testing honours the expression pivot: inside only the centred pose, then only the top-left one.
        H.Check(P + "_Centre_HitsCentredPoint", Hits(lab, scaled!, 80, 140));
        H.Check(P + "_Centre_MissesTopLeftOnlyPoint", !Hits(lab, scaled!, 300, 220));
        H.Check(P + "_Centre_FacadeIdentity", scaled is not null && scaled.CenterPoint == Vector3.Zero && scaled.Scale == Vector3.One);

        await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Rotation(90));
        await CheckPose(P + "_Centre_Rotation", lab, Expect(50, 20, degrees: 90));
        Distinct(P + "_Centre_RotationDiffersFromTopLeft", Expect(50, 20, degrees: 90), Expect(0, 0, degrees: 90));

        // Changing a live origin only writes the property; (0, 0) puts it into the inactive state.
        var changed = await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(2));
        await lab.Show(Target().TransformOrigin(1f, 0f).Scale(2));
        Distinct(P + "_Changed_DiffersFromCentre", Expect(100, 0, scale: 2), Expect(50, 20, scale: 2));
        await CheckPose(P + "_Changed_Origin", lab, Expect(100, 0, scale: 2));
        await lab.Show(Target().TransformOrigin(0, 0).Scale(2));
        await CheckPose(P + "_Changed_ToZero", lab, Expect(0, 0, scale: 2));
        var zeroVisual = ElementCompositionPreview.GetElementVisual(changed!);
        var zeroRead = zeroVisual.Properties.TryGetVector2(TransformOriginBinding.OriginProperty, out var zeroOrigin);
        H.Check(P + "_Changed_ZeroInactive", zeroRead == CompositionGetValueStatus.Succeeded && zeroOrigin == Vector2.Zero, $"origin={zeroOrigin}");
    }

    private async Task Resize()
    {
        using var lab = new Lab(H);
        await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(1.5f));
        await CheckPose(P + "_Resize_Before", lab, Expect(50, 20, scale: 1.5f));
        // A stale pivot (still 50) and a tracking one (80) give different boxes.
        Distinct(P + "_Resize_StalePivotDiffers", Expect(80, 20, scale: 1.5f, width: 160), Expect(50, 20, scale: 1.5f, width: 160));
        await lab.Show(Target(160).TransformOrigin(0.5f, 0.5f).Scale(1.5f));
        await CheckPose(P + "_Resize_Tracks", lab, Expect(80, 20, scale: 1.5f, width: 160));
    }

    private async Task RightToLeft()
    {
        using var lab = new Lab(H, rtl: true);
        // x = 0 is the leading edge, which is the physical right in RTL; the physical left would be x = 1.
        Distinct(P + "_Rtl_LeadingDiffersFromPhysicalLeft", Expect(0, 20, degrees: 90, rtl: true), Expect(Width, 20, degrees: 90, rtl: true));
        await lab.Show(Target().TransformOrigin(0, 0.5f).Rotation(90));
        await CheckPose(P + "_Rtl_LeadingEdge", lab, Expect(0, 20, degrees: 90, rtl: true));
    }

    private async Task Unset()
    {
        using var lab = new Lab(H);
        var target = await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(2));
        await CheckPose(P + "_Unset_Set", lab, Expect(50, 20, scale: 2));
        var visual = ElementCompositionPreview.GetElementVisual(target!);
        // A running offset animation, which the unset must stop (positive control first).
        AnimationScope.WithAnimation(Curve.Linear(60000), () => lab.Set(Target().TransformOrigin(0.5f, 0.5f).CenterPoint(new Vector3(20, 0, 0)).Scale(2)));
        await Harness.Render();
        H.Check(P + "_Unset_OffsetAnimating", Running(visual.Properties, TransformOriginBinding.OffsetProperty));
        // Only the origin goes: the static pivot is the remaining .CenterPoint() value.
        await lab.Show(Target().CenterPoint(new Vector3(20, 0, 0)).Scale(2));
        Distinct(P + "_Unset_StaticDiffersFromTopLeft", Expect(20, 0, scale: 2), Expect(0, 0, scale: 2));
        await CheckPose(P + "_Unset_StaticPivot", lab, Expect(20, 0, scale: 2));
        await lab.Show(Target().Scale(2));
        await CheckPose(P + "_Unset_TopLeft", lab, Expect(0, 0, scale: 2));
        var originRead = visual.Properties.TryGetVector2(TransformOriginBinding.OriginProperty, out var origin);
        var offsetRead = visual.Properties.TryGetVector3(TransformOriginBinding.OffsetProperty, out var offset);
        H.Check(P + "_Unset_Inactive",
            originRead == CompositionGetValueStatus.Succeeded && origin == Vector2.Zero
            && offsetRead == CompositionGetValueStatus.Succeeded && offset == Vector3.Zero
            && !Running(visual.Properties, TransformOriginBinding.OffsetProperty),
            $"origin={origin} offset={offset}");
        // A new offset is used, not the stopped animation's.
        await lab.Show(Target().TransformOrigin(0.5f, 0.5f).CenterPoint(new Vector3(-10, 0, 0)).Scale(2));
        await CheckPose(P + "_Unset_SetAgain", lab, Expect(40, 20, scale: 2));
    }

    private async Task Offset()
    {
        var offset = new Vector3(10, 0, 0);
        Distinct(P + "_Offset_DiffersFromCentre", Expect(60, 20, scale: 2), Expect(50, 20, scale: 2));
        using (var lab = new Lab(H))
        {
            await lab.Show(Target().TransformOrigin(0.5f, 0.5f).CenterPoint(offset).Scale(2));
            await CheckPose(P + "_Offset_Added", lab, Expect(60, 20, scale: 2));
            await lab.Show(Target().TransformOrigin(0.5f, 0.5f).CenterPoint(-offset).Scale(2));
            await CheckPose(P + "_Offset_Changed", lab, Expect(40, 20, scale: 2));
            await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(2));
            await CheckPose(P + "_Offset_Removed", lab, Expect(50, 20, scale: 2));
            // The removed offset isn't the static pivot either once the origin goes too.
            await lab.Show(Target().Scale(2));
            await CheckPose(P + "_Offset_RemovedThenOrigin", lab, Expect(0, 0, scale: 2));
            await lab.Show(Target().CenterPoint(offset).TransformOrigin(0.5f, 0.5f).Scale(2));
            await CheckPose(P + "_Offset_Readded", lab, Expect(60, 20, scale: 2));
            // Without the origin the offset is the static pixel pivot again.
            await lab.Show(Target().CenterPoint(offset).Scale(2));
            await CheckPose(P + "_Offset_OriginRemoved", lab, Expect(10, 0, scale: 2));
        }
        using (var lab = new Lab(H))
        {
            // The order of the two calls doesn't matter, and the static pivot becomes the offset at once.
            await lab.Show(Target().CenterPoint(offset).Scale(2));
            await lab.Show(Target().CenterPoint(offset).TransformOrigin(0.5f, 0.5f).Scale(2));
            await CheckPose(P + "_Offset_OriginAddedLater", lab, Expect(60, 20, scale: 2));
        }
        using (var lab = new Lab(H))
        {
            // A facade CenterPoint a setter wrote before the handoff migrates, and becomes the offset.
            await lab.Show(Target().Set(static b => b.CenterPoint = new Vector3(10, 0, 0)).TransformOrigin(0.5f, 0.5f).Scale(2));
            await CheckPose(P + "_Offset_SetterMigrated", lab, Expect(60, 20, scale: 2));
        }
    }

    // §8.2: under an origin, a .CenterPoint change under a curve animates the offset property, so the
    // expression keeps running and the pivot moves through the size-relative midpoint. The control
    // animates the static pivot by the same amount without an origin, which shows the oracle sees a
    // seeked pivot at all (a session that finishes every animation at once fails it).
    private async Task AnimatedOffset(bool animate)
    {
        string name = P + (animate ? "_AnimateOffset" : "_WithAnimationOffset");
        var curve = Curve.Linear(60000);
        const float s = 1.5f;
        BorderElement Declared(float x, bool origin, float width = Width, bool animated = true)
        {
            var e = Target(width).CenterPoint(new Vector3(x, 0, 0)).Scale(s);
            if (origin) e = e.TransformOrigin(0.5f, 0.5f);
            return animate && animated ? e.Animate(curve, AnimateProperty.CenterPoint) : e;
        }
        void Change(Lab lab, BorderElement next)
        {
            if (animate) lab.Set(next);
            else AnimationScope.WithAnimation(curve, () => lab.Set(next));
        }

        Distinct(name + "_MidDiffersFromEnds", Expect(70, 20, s), Expect(90, 20, s));
        using (var control = new Lab(H))
        {
            // Mounted without .Animate(), which would animate the mount write in from zero too.
            var target = await control.Show(Declared(50, origin: false, animated: false));
            Change(control, Declared(90, origin: false));
            await Harness.Render();
            var visual = ElementCompositionPreview.GetElementVisual(target!);
            var controller = visual.TryGetAnimationController("CenterPoint");
            H.Check(name + "_Control_Animates", controller is not null);
            if (controller is null) return;
            controller.Pause();
            controller.Progress = 0.5f;
            // The static pivot isn't size-relative: 50 → 90 passes 70 at y = 0.
            await CheckPose(name + "_Control_SeeksMidway", control, Expect(70, 0, s));
        }

        // The origin and an animated offset arrive in the same render: on an update, and on mount.
        using (var together = new Lab(H))
        {
            var t = await together.Show(Declared(0, origin: false));
            Change(together, Declared(40, origin: true));
            await Harness.Render();
            await SeekOffset(name + "_AddedTogether", together, ElementCompositionPreview.GetElementVisual(t!));
        }
        // On mount only .Animate(CenterPoint) isolates the channel: WithAnimation would animate the scale in too.
        if (animate)
        using (var mount = new Lab(H))
        {
            Change(mount, Declared(40, origin: true));
            await Harness.Render();
            var mounted = mount.Root.Children.Count > 0 ? (FrameworkElement)mount.Root.Children[0] : null;
            H.Check(name + "_Mount_Mounted", mounted is not null);
            if (mounted is not null) await SeekOffset(name + "_Mount", mount, ElementCompositionPreview.GetElementVisual(mounted));
        }
        async Task SeekOffset(string check, Lab l, Visual visual)
        {
            var c = visual.Properties.TryGetAnimationController(TransformOriginBinding.OffsetProperty);
            H.Check(check + "_AnimatesOffset", c is not null);
            if (c is null) return;
            c.Pause();
            c.Progress = 0.5f;
            await CheckPose(check + "_Mid", l, Expect(70, 20, s));
        }

        using var lab = new Lab(H);
        var scaled = await lab.Show(Declared(0, origin: true));
        await CheckPose(name + "_Start", lab, Expect(50, 20, s));
        Change(lab, Declared(40, origin: true));
        await Harness.Render();
        var v = ElementCompositionPreview.GetElementVisual(scaled!);
        var offset = v.Properties.TryGetAnimationController(TransformOriginBinding.OffsetProperty);
        H.Check(name + "_AnimatesOffset", offset is not null);
        H.Check(name + "_ExpressionKept", !Running(v, "CenterPoint"));
        if (offset is null) return;
        offset.Pause();
        offset.Progress = 0.5f;
        await CheckPose(name + "_Mid", lab, Expect(70, 20, s));
        offset.Progress = 1;
        await CheckPose(name + "_End", lab, Expect(90, 20, s));
        // The expression still follows the size: same offset, wider element.
        await lab.Show(Declared(40, origin: true, width: 160));
        await CheckPose(name + "_StillTracksSize", lab, Expect(120, 20, s, width: 160));
        // Dropping the origin restores the static pivot at once, even with a CenterPoint key or scope.
        if (animate) lab.Set(Declared(40, origin: false, width: 160));
        else AnimationScope.WithAnimation(curve, () => lab.Set(Declared(40, origin: false, width: 160)));
        await Harness.Render();
        H.Check(name + "_UnsetDoesNotAnimate", !Running(v, "CenterPoint") && !Running(v.Properties, TransformOriginBinding.OffsetProperty));
        await CheckPose(name + "_UnsetStaticPivot", lab, Expect(40, 0, s, width: 160));
    }
    private async Task Hover()
    {
        using var lab = new Lab(H);
        var target = await lab.Show(Target().TransformOrigin(0.5f, 0.5f)
            .InteractionStates(static s => s.PointerOver(scale: 2f), Curve.Linear(30)));
        Reconciler.SimulatePointerOverForTests(target!, isOver: true);
        await CheckPose(P + "_Hover_In", lab, Expect(50, 20, scale: 2));
        Reconciler.SimulatePointerOverForTests(target!, isOver: false);
        await CheckPose(P + "_Hover_Out", lab, Expect(50, 20));
    }

    // §7.3 step 4: a control returned with an origin comes back inactive, and a later origin works.
    private async Task Pool()
    {
        using var lab = new Lab(H);
        var first = await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Scale(2));
        AnimationScope.WithAnimation(Curve.Linear(60000), () => lab.Set(Target().TransformOrigin(0.5f, 0.5f).CenterPoint(new Vector3(20, 0, 0)).Scale(2)));
        await Harness.Render();
        H.Check(P + "_Pool_OffsetAnimating", Running(ElementCompositionPreview.GetElementVisual(first!).Properties, TransformOriginBinding.OffsetProperty));
        await lab.Show(null);
        var second = await lab.Show(Target().Scale(2));
        H.Check(P + "_Pool_Recycled", first is not null && ReferenceEquals(first, second));
        await CheckPose(P + "_Pool_RenterTopLeft", lab, Expect(0, 0, scale: 2));
        var visual = ElementCompositionPreview.GetElementVisual(second!);
        var originRead = visual.Properties.TryGetVector2(TransformOriginBinding.OriginProperty, out var origin);
        H.Check(P + "_Pool_Inactive", originRead == CompositionGetValueStatus.Succeeded && origin == Vector2.Zero
            && !Running(visual.Properties, TransformOriginBinding.OffsetProperty), $"origin={origin}");
        await lab.Show(null);
        var third = await lab.Show(Target().TransformOrigin(0.5f, 0.5f).Rotation(90));
        H.Check(P + "_Pool_RecycledAgain", ReferenceEquals(first, third));
        await CheckPose(P + "_Pool_OriginAgain", lab, Expect(50, 20, degrees: 90));
    }

    // §8.2: DrillIn pivots on the centre through the same binding, and NormalizeVisual restores the
    // element's own pivot afterwards, or the static one when it declares no origin.
    private async Task Navigation()
    {
        Distinct(P + "_Navigation_CentreDiffersFromLeading", Expect(50, 20, degrees: 90), Expect(0, 20, degrees: 90));
        foreach (bool declared in new[] { true, false })
        {
            string name = P + (declared ? "_Navigation_Origin" : "_Navigation_NoOrigin");
            using var lab = new Lab(H);
            var element = Target().Rotation(90);
            var target = await lab.Show(declared ? element.TransformOrigin(0, 0.5f) : element);
            var visual = ElementCompositionPreview.GetElementVisual(target!);
            TransitionEngine.PrepareTransitionAnimations(target!, visual, target!, visual, new DrillInTransition());
            await CheckPose(name + "_DrillInCentre", lab, Expect(50, 20, degrees: 90));
            TransitionEngine.NormalizeVisual(target!, visual);
            // NormalizeVisual zeroes Offset for a page root; this target sits at a margin, so put layout's back.
            visual.Offset = new Vector3(target!.ActualOffset.X, target.ActualOffset.Y, 0);
            await CheckPose(name + "_Restored", lab, declared ? Expect(0, 20, degrees: 90) : Expect(0, 0, degrees: 90));
        }
    }

    // ── Oracles ─────────────────────────────────────────────────────────────

    private void Distinct(string name, Box a, Box b) => H.Check(name, !a.Near(b, Tolerance * 2), $"{a} vs {b}");

    private async Task CheckPose(string name, Lab lab, Box expected)
    {
        await Settle();
        var box = (await Snap.CaptureAsync(lab.Root)).RedBox();
        H.Check(name, box is { } b && b.Near(expected, Tolerance), $"actual={box?.ToString() ?? "none"} expected={expected}");
    }

    private static bool Hits(Lab lab, UIElement target, double x, double y)
    {
        var window = lab.Root.TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point(x, y));
        return VisualTreeHelper.FindElementsInHostCoordinates(window, lab.Root).Contains(target);
    }

    private static bool Running(CompositionObject o, string property)
    {
        try { return o.TryGetAnimationController(property) is not null; }
        catch (ArgumentException) { return false; }
    }

    private static async Task Settle()
    {
        int frames = 0;
        var done = new TaskCompletionSource();
        EventHandler<object> onRendering = (_, _) => { if (++frames >= 4) done.TrySetResult(); };
        CompositionTarget.Rendering += onRendering;
        try { await Task.WhenAny(done.Task, Task.Delay(2000)); }
        finally { CompositionTarget.Rendering -= onRendering; }
        await Task.Delay(100);
    }

    /// <summary>One reconciler mounting the target directly under a fixed stage, so a removed target is pooled.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly Harness _h;
        private readonly bool _rtl;
        private Element _current;
        private readonly Reconciler _reconciler = new();
        public readonly WinUI.StackPanel Root;

        public Lab(Harness h, bool rtl = false)
        {
            _h = h;
            _rtl = rtl;
            _current = Stage(null);
            Root = (WinUI.StackPanel)_reconciler.Mount(_current, static () => { })!;
            h.SetContent(Root);
        }

        private Element Stage(Element? child)
        {
            var stage = VStack(0, child).Width(StageSize).Height(StageSize)
                .Background("#FFFFFFFF").HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);
            return _rtl ? stage.Set(static sp => sp.FlowDirection = FlowDirection.RightToLeft) : stage;
        }

        public async Task<FrameworkElement?> Show(Element? child)
        {
            Set(child);
            await Harness.Render();
            return Root.Children.Count > 0 ? (FrameworkElement)Root.Children[0] : null;
        }

        public void Set(Element? child)
        {
            var next = Stage(child);
            _reconciler.Update(_current, next, Root, static () => { });
            _current = next;
        }

        public void Dispose()
        {
            _reconciler.UnmountChild(Root);
            _h.SetContent(null);
            _reconciler.Dispose();
        }
    }
}
