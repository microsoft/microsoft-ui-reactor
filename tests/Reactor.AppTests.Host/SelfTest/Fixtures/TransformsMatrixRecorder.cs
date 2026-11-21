using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using Model = Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix.CompositionModel;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §12.1 A: runs one matrix case through its scripted lifecycle (mount, change the
/// declared values, remove the feature, add it back, remove the modifiers, unmount, rent from the
/// pool as a plain element and as a transformed one) and records what each step did. Motion is
/// controller-driven: the recorder pauses each new animation and seeks it, so no verdict depends
/// on wall-clock timing.
/// </summary>
internal sealed class TransformsMatrixRecorder(Border caseHost, Border freshHost, DiagnosticsListener diagnostics, Func<FrameworkElement, Snap>? screen)
{
    /// <summary>A loop long enough never to wrap during a case.</summary>
    public const int LongMs = 60_000;

    /// <summary>
    /// Every other animation. Verdicts seek a paused controller, so the duration doesn't matter to
    /// them, but an animation can only be finished by letting it play out: resuming at progress 1
    /// never completes, and stopping a paused one reverts to its base value.
    /// </summary>
    public const int MotionMs = 1_200;
    private static readonly string[] Channels = ["Opacity", "Scale", "RotationAngle", "Translation", "CenterPoint", "Offset", "Size"];

    private MatrixCase _case = null!;
    private Reconciler _reconciler = null!;
    private StackPanel? _stage;
    private Element? _stageElement;

    /// <summary>Model disagreements found while observing, as <c>step/part</c>.</summary>
    public List<(string Step, string Part)> ModelDifferences { get; } = [];

    /// <summary>Oracle failures that make the record untrustworthy (§12.1 A: <c>UNCLEAR</c> fails).</summary>
    public List<string> Inconclusive { get; } = [];

    private FrameworkElement? Target => _stage is { Children.Count: > 1 } s ? s.Children[1] as FrameworkElement : null;

    private bool Rtl => _case.Flow == FlowAxis.Rtl;

    public async Task<LedgerCase> ObserveAsync(MatrixCase c)
    {
        _case = c;
        ModelDifferences.Clear();
        Inconclusive.Clear();
        var record = new LedgerCase { Dims = c.Dims };
        using var motion = TransitionEngine.OverrideAnimationsEnabled(c.Motion == MotionAxis.Enabled);
        using var reconciler = _reconciler = new Reconciler();
        _stage = null;
        _stageElement = null;
        FrameworkElement? original = null;
        try
        {
            if (!await Step(record, MatrixStep.Mount, async f =>
            {
                Render(StageOf(Declare(MatrixStep.Mount)));
                await Settle();
                original = Target;
                await ObserveActive(f, MatrixStep.Mount);
                if (c.UserCode == UserCodeAxis.TranslationDrag)
                {
                    Target!.Translation = new Vector3(Model.DragX, 0, 0);
                    await Settle();
                }
            }, rest: true, geometry: true))
                return record;

            await Step(record, MatrixStep.Change, async f =>
            {
                var next = StageOf(Declare(MatrixStep.Change));
                await ObserveMotion(f, () =>
                {
                    if (c.Feature == FeatureAxis.WithAnimation) AnimationScope.WithAnimation(Curve.Linear(MotionMs), () => Render(next));
                    else Render(next);
                });
            }, rest: true, channels: true);

            await Step(record, MatrixStep.RemoveFeature, async _ => { Render(StageOf(Declare(MatrixStep.RemoveFeature))); await Settle(); }, rest: true);
            await Step(record, MatrixStep.AddFeature, async _ => { Render(StageOf(Declare(MatrixStep.AddFeature))); await Settle(); }, rest: true);
            await Step(record, MatrixStep.RemoveModifier, async f =>
            {
                Render(StageOf(Declare(MatrixStep.RemoveModifier)));
                await Settle();
                // #1626: a removed transition must not stay installed, idle until the next write.
                if (Target is { } t) f["residue"] = TransitionResidue(t);
            }, rest: true);
            if (!await Step(record, MatrixStep.Unmount, async f =>
            {
                Render(StageOf(null));
                await ObserveActive(f, MatrixStep.Unmount);
                for (int i = 0; i < 20 && Target is not null; i++) await Settle(50);
                f["removed"] = Target is null ? "1" : "0";
            }, rest: false))
                return record;

            FrameworkElement? plain = null;
            await Step(record, MatrixStep.RentPlain, async f =>
            {
                var el = StageOf(Declare(MatrixStep.RentPlain));
                Render(el);
                await Settle();
                plain = Target;
                f["pool"] = $"recycled={(ReferenceEquals(plain, original) ? 1 : 0)} diff={await DiffFromFresh(el)} {ProbePlain(plain!)}";
            }, rest: true);
            if (_stage is not null && Target is not null) { Render(StageOf(null)); await Settle(0); }

            await Step(record, MatrixStep.RentTransformed, async f =>
            {
                var el = StageOf(Declare(MatrixStep.RentTransformed));
                Render(el);
                await Settle();
                var t = Target!;
                string from = ReferenceEquals(t, original) ? "orig" : ReferenceEquals(t, plain) ? "plain" : "fresh";
                f["pool"] = $"recycled={from} diff={await DiffFromFresh(el)} {Residuals(t)}";
            }, rest: true);
        }
        finally
        {
            try { if (_stage is not null) _reconciler.UnmountChild(_stage); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Console.WriteLine($"# TransformsMatrix {c.Id}: teardown threw {ex.GetType().Name}"); }
            caseHost.Child = null;
        }

        return record;
    }

    // ── Declarations ────────────────────────────────────────────────────────

    private readonly record struct Decl(TransformAxis Transform, bool Second, bool Transition, bool Feature, bool UserCode,
        float Opacity, double Shift, bool AltBackground, bool Plain);

    private Decl Declare(MatrixStep step) => step switch
    {
        MatrixStep.Mount => new(_case.Transform, false, true, true, true, Model.OpacityA, 0, false, false),
        MatrixStep.Change => new(_case.Transform, true, true, true, true, Model.OpacityB, Model.Shift(_case, step), _case.Transition == TransitionAxis.Background, false),
        MatrixStep.RemoveFeature => new(_case.Transform, true, true, false, true, Model.OpacityB, Model.Shift(_case, step), _case.Transition == TransitionAxis.Background, false),
        MatrixStep.AddFeature => new(_case.Transform, true, true, true, true, Model.OpacityB, Model.Shift(_case, step), _case.Transition == TransitionAxis.Background, false),
        MatrixStep.RemoveModifier => new(TransformAxis.None, true, false, true, true, Model.OpacityB, Model.Shift(_case, step), _case.Transition == TransitionAxis.Background, false),
        MatrixStep.RentPlain => new(TransformAxis.None, false, false, false, false, Model.OpacityA, 0, false, true),
        _ => new(_case.Transform, false, false, false, false, Model.OpacityA, 0, false, true),
    };

    private Element StageOf(Decl? d)
    {
        var stage = VStack(0, Border(null).Width(0).Height(0), d is { } decl ? TargetOf(decl) : null)
            .Width(Model.StageSize).Height(Model.StageSize)
            .Background("#FFFFFFFF")
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);
        if (Rtl) stage = stage.Set(static sp => sp.FlowDirection = FlowDirection.RightToLeft);
        bool stagger = d is { Feature: true, Plain: false } && _case.Feature == FeatureAxis.Stagger;
        return stagger ? stage.Stagger(TimeSpan.FromMilliseconds(40), Curve.Linear(MotionMs)) : stage;
    }

    private Element TargetOf(Decl d)
    {
        string background = d.AltBackground ? "#FFFF2020" : "#FFFF0000";
        bool memo = !d.Plain && _case.Wrapper == WrapperAxis.Memo;
        var inner = VStack()
            .Width(Model.Width).Height(Model.Height)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);
        if (!memo) inner = inner.Background(background);
        bool backgroundTransition = !d.Plain && d.Transition && _case.Transition == TransitionAxis.Background;
        bool setter = d.UserCode && _case.UserCode == UserCodeAxis.SetFacade;
        if (backgroundTransition) inner = inner.BackgroundTransition(TimeSpan.FromMilliseconds(MotionMs));
        if (setter) inner = inner.Set(static sp => sp.Scale = new Vector3(Model.SetterScale, Model.SetterScale, 1));

        // A keyed Memo replaces its inner subtree when the key changes, so only what the
        // StackElement alone can declare stays inside, keyed by its values. Everything else is
        // declared on the wrapper, which applies to the same control.
        Element e = memo ? Memo((backgroundTransition, setter), () => inner).Background(background) : inner;
        e = e.Margin(Model.Left + d.Shift, Model.Top, 0, 0);
        if (!(d.Plain && d.Transform == TransformAxis.None)) e = e.Opacity(d.Opacity);
        if (d.Transition) e = WithTransition(e);
        if (d.Feature) e = WithFeature(e);
        if (d.Plain || _case.Wrapper != WrapperAxis.Decorator) return WithTransform(e, d.Transform, d.Second);
        return new ModifiedElement(e, WithTransform(TextBlock(""), d.Transform, d.Second).Modifiers ?? new ElementModifiers());
    }

    private static Element WithTransform(Element e, TransformAxis axis, bool second)
    {
        var p = Model.Declared(axis, second);
        return axis switch
        {
            TransformAxis.Identity => e.Rotation(0).Scale(1),
            TransformAxis.Scale => e.Scale(p.Scale),
            TransformAxis.Rotation => e.Rotation(p.Degrees),
            TransformAxis.Translation => e.Translation(p.Translation.X, p.Translation.Y, p.Translation.Z),
            TransformAxis.CenterPoint => e.CenterPoint(p.CenterPoint),
            TransformAxis.ScaleRotationCenter => e.Scale(p.Scale).Rotation(p.Degrees).CenterPoint(p.CenterPoint),
            _ => e,
        };
    }

    private Element WithTransition(Element e)
    {
        var duration = TimeSpan.FromMilliseconds(MotionMs);
        return _case.Transition switch
        {
            TransitionAxis.Opacity => e.OpacityTransition(duration),
            TransitionAxis.Scale => e.ScaleTransition(new Vector3Transition { Duration = duration }),
            TransitionAxis.Rotation => e.RotationTransition(duration),
            TransitionAxis.Translation => e.TranslationTransition(new Vector3Transition
            {
                Duration = duration,
                Components = _case.Variant == VariantAxis.B ? Vector3TransitionComponents.X
                    : Vector3TransitionComponents.X | Vector3TransitionComponents.Y | Vector3TransitionComponents.Z,
            }),
            _ => e, // Background is declared on the StackElement in TargetOf.
        };
    }

    private Element WithFeature(Element e) => _case.Feature switch
    {
        FeatureAxis.Animate => e.Animate(Curve.Linear(MotionMs)),
        FeatureAxis.InteractionStates => e.InteractionStates(static s => s.PointerOver(scale: 1.25f), Curve.Linear(30)),
        FeatureAxis.TransitionEnter => e.Transition(Animation.Transition.Enter(Animation.Transition.Scale(0.5f)), Curve.Linear(MotionMs)),
        FeatureAxis.TransitionExit => e.Transition(Animation.Transition.Exit(Animation.Transition.Scale(0.5f)), Curve.Linear(MotionMs)),
        FeatureAxis.Keyframes when _case.Variant == VariantAxis.A =>
            e.Keyframes("matrix", 1, static k => k.Duration(30).At(0f, opacity: 0.9f).At(1f, opacity: 1f)),
        FeatureAxis.Keyframes => e.Keyframes("matrix", 1, static k => k.Duration(LongMs).Loop().At(0f, opacity: 1f).At(1f, opacity: 0.9f)),
        FeatureAxis.LayoutAnimation => e.LayoutAnimation(TimeSpan.FromMilliseconds(MotionMs)),
        _ => e, // WithAnimation wraps the change step; Stagger is declared on the stage.
    };

    private void Render(Element next)
    {
        if (_stage is null)
        {
            _stage = (StackPanel)_reconciler.Mount(next, static () => { })!;
            caseHost.Child = _stage;
        }
        else
        {
            _reconciler.Update(_stageElement!, next, _stage, static () => { });
        }

        _stageElement = next;
    }

    // ── Steps ───────────────────────────────────────────────────────────────

    private async Task<bool> Step(LedgerCase record, MatrixStep step, Func<OrderedDictionary<string, string>, Task> act,
        bool rest, bool geometry = false, bool channels = false)
    {
        string name = MatrixCase.StepName(step);
        var f = record.Step(name);
        f["threw"] = "-";
        diagnostics.Reset();
        bool ok = true;
        try
        {
            await act(f);
        }
        catch (FrameTimeoutException ex)
        {
            Inconclusive.Add($"{name}: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever the product throws is the observation: it's recorded, and the lifecycle goes on.
            // The model never throws, so it must be attributed like any other disagreement.
            f["threw"] = ex.GetType().Name;
            ModelDifferences.Add((name, "threw"));
            ok = false;
        }

        if (_stage is null) { f["diag"] = diagnostics.Describe(); return false; }

        try
        {
            if (rest && Target is { } t)
            {
                await Frames(2);
                if (await SettleRunning(t, step) is { } running) f["running"] = running;
                var snap = await Snap.CaptureAsync(caseHost);
                var box = snap.RedBox();
                f["pose"] = box?.ToString() ?? "none";
                var expected = Model.Bounds(ExpectedPose(step), Rtl, Model.Shift(_case, step));
                if (box is null || !box.Value.Near(expected, TransformsLedger.Tolerance)) ModelDifferences.Add((name, "pose"));
                if (_case.UserCode == UserCodeAxis.TranslationDrag) f[TransformsLedger.ScreenField] = ObserveScreen(name, expected);
                if (geometry) ObserveGeometry(f, t, step);
                if (channels || geometry) f["channel"] = Channel(t);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            f["observe"] = ex.GetType().Name;
            Inconclusive.Add($"{name}: observing threw {ex.GetType().Name}: {ex.Message}");
        }

        // Screen and throw disagreements are attributed like any other, but they aren't in `model`: their
        // own fields carry the change, and whether the screen can be seen depends on the desktop.
        f["model"] = string.Join(",", ModelDifferences.Where(d => d.Step == name && d.Part is not (TransformsLedger.ScreenField or "threw")).Select(d => d.Part)) is { Length: > 0 } m ? m : "ok";
        f["diag"] = diagnostics.Describe();
        return ok;
    }

    private Pose ExpectedPose(MatrixStep step) => Model.Expected(_case, step);

    /// <summary>
    /// A facade Translation written after the visual's translation is enabled reads applied under
    /// RenderTargetBitmap but isn't on screen (spec 066 §12.2, Q1f), so drag cases also record the
    /// PrintWindow pose, and the model verdict covers it. Without an interactive desktop it's skipped.
    /// </summary>
    private string ObserveScreen(string step, Box expected)
    {
        if (screen is null) return TransformsLedger.ScreenSkipped;
        var box = screen(caseHost).RedBox();
        if (box is null || !box.Value.Near(expected, TransformsLedger.Tolerance)) ModelDifferences.Add((step, TransformsLedger.ScreenField));
        return box?.ToString() ?? "none";
    }

    // A feature's own activation can throw (#1623: a XAML transition blocks the handoff). That's
    // recorded on the `active` field, and the lifecycle goes on.
    private async Task ObserveActive(OrderedDictionary<string, string> f, MatrixStep step)
    {
        try { await ObserveActiveCore(f, step); }
        catch (Exception ex) when (ex is not FrameTimeoutException and not OutOfMemoryException)
        {
            f["active"] = "threw:" + ex.GetType().Name;
            ModelDifferences.Add((MatrixCase.StepName(step), "active"));
        }
    }

    private async Task ObserveActiveCore(OrderedDictionary<string, string> f, MatrixStep step)
    {
        if (Model.Active(_case, step) is not { } pose || Target is not { } t) return;
        string name = MatrixCase.StepName(step);
        if (_case.Feature == FeatureAxis.InteractionStates)
        {
            Reconciler.SimulatePointerOverForTests(t, isOver: true);
            await Settle();
            f["active"] = await Pose(pose, step, name);
            Reconciler.SimulatePointerOverForTests(t, isOver: false);
            await Settle();
            return;
        }

        // A scale enter starts at half the declared size; a scale exit ends there.
        var visual = ElementCompositionPreview.GetElementVisual(t);
        var controller = visual.TryGetAnimationController("Scale");
        if (controller is null)
        {
            f["active"] = "no-animation";
            ModelDifferences.Add((name, "active"));
            return;
        }

        controller.Pause();
        controller.Progress = 0.5f;
        await Frames(3);
        f["active"] = await Pose(pose, step, name);
        controller.Resume();
        await Finish(visual, ["Scale"], MotionMs / 2);
    }

    private async Task<string> Pose(Pose expected, MatrixStep step, string name)
    {
        var box = (await Snap.CaptureAsync(caseHost)).RedBox();
        if (box is null || !box.Value.Near(Model.Bounds(expected, Rtl, Model.Shift(_case, step)), TransformsLedger.Tolerance))
            ModelDifferences.Add((name, "active"));
        return box?.ToString() ?? "none";
    }

    /// <summary>
    /// The change step's motion oracle (§12.1 A). For each channel the step moves: a XAML transition
    /// is <c>unobservable</c> to RenderTargetBitmap; an animation that existed before the write holds
    /// the channel (<c>HELD</c>); a new one is paused and seeked to its start, 25%, 50% and 100%, with
    /// every other channel at its end; no animation means the value jumped, or never arrived.
    /// </summary>
    private async Task ObserveMotion(OrderedDictionary<string, string> f, Action write)
    {
        var target = Target!;
        var channels = Model.ChangedChannels(_case);
        Visual? Taken(FrameworkElement t) =>
            ElementPool.IsVisualTaken(t) || _case.Feature == FeatureAxis.Stagger ? ElementCompositionPreview.GetElementVisual(t) : null;

        var before = Taken(target) is { } v0 ? channels.ToDictionary(ch => ch, ch => Controller(v0, ch)) : [];
        write();
        await Frames(2);
        if (!ReferenceEquals(Target, target))
        {
            // A keyed Memo whose key changed replaced the control, so there is no motion to observe.
            f["motion"] = "remounted";
            return;
        }

        var visual = Taken(target);
        var controllers = visual is null ? []
            : channels.Select(ch => (ch, ctl: Controller(visual, ch))).Where(static x => x.ctl is not null).ToDictionary(static x => x.ch, static x => x.ctl!);
        var fresh = controllers.Where(kv => !(before.TryGetValue(kv.Key, out var b) && Equals(b, kv.Value))).ToDictionary();
        foreach (var ctl in fresh.Values) ctl.Pause();
        foreach (var ctl in fresh.Values) ctl.Progress = 1;

        var verdicts = new List<string>();
        var curves = new List<string>();
        foreach (var ch in channels)
        {
            string verdict;
            if (ch == "Background") verdict = target is Panel { BackgroundTransition: not null } ? "unobservable" : await BackgroundVerdict();
            else if (XamlTransition(target, ch)) verdict = "unobservable";
            else if (controllers.ContainsKey(ch) && !fresh.ContainsKey(ch)) verdict = Model.Held;
            else if (!Visible(ch)) verdict = fresh.ContainsKey(ch) ? "running" : "invisible";
            else if (fresh.TryGetValue(ch, out var ctl))
            {
                // Progress is measured from where this animation starts, not from the declared mount
                // value: with user code the handoff can migrate a facade value first (§9 rows 1 and 3).
                async Task<double> At(float progress)
                {
                    ctl.Progress = progress;
                    await Frames(3);
                    return Progress(ch, await Snap.CaptureAsync(caseHost));
                }

                double start = await At(0.02f), quarter = await At(0.25f), mid = await At(0.5f), end = await At(1);
                double span = end - start;
                verdict = Verdict(Math.Abs(span) < 0.05 ? end : (mid - start) / span, end);
                if (verdict == Model.Animating && Math.Abs(span) >= 0.05)
                    curves.Add(string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $"{ch}={Math.Round((quarter - start) / span / 0.2) * 0.2:F1}"));
            }
            else
            {
                double now = Progress(ch, await Snap.CaptureAsync(caseHost));
                verdict = now >= 0.9 ? Model.Jumped : now <= 0.1 ? Model.NotApplied : string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $"UNCLEAR(now={now:F2})");
            }

            if (verdict.StartsWith("UNCLEAR", StringComparison.Ordinal))
            {
                // §12.1 A: an inconclusive oracle is a failure, not a pass. The case reruns before it fails.
                Console.WriteLine($"# TransformsMatrix {_case.Id}: change {ch} {verdict}");
                Inconclusive.Add($"change: {ch} {verdict}");
                verdict = "UNCLEAR";
            }

            verdicts.Add($"{ch}={verdict}");
            if (verdict == "UNCLEAR" || (Array.IndexOf(Model.Unmodelled, verdict) < 0 && verdict != Model.ExpectedMotion(_case, ch)))
                ModelDifferences.Add(("change", "motion:" + ch));
        }

        f["motion"] = string.Join(" ", verdicts);
        // Progress at a quarter of the time, in steps of 0.2: linear reads 0.2, the Fluent (0.8,0,0.2,1)
        // curve 0.0 and the compositor's default ease-out 0.6, so a layer that changes a curve shows here.
        if (curves.Count > 0) f["curve"] = string.Join(" ", curves);
        foreach (var ctl in fresh.Values) { ctl.Progress = 0.5f; ctl.Resume(); }
        if (fresh.Count > 0) await Finish(visual!, fresh.Keys, MotionMs / 2);
        await Settle();
    }

    /// <summary>
    /// A seeked midpoint strictly between the ends means the value moves. Because the controller is
    /// paused, no timing enters it, so an eased curve still reads <c>ANIMATING</c> (the layout
    /// animation's ease-out is at 0.87, #1652); the <c>curve</c> field records the shape.
    /// </summary>
    private static string Verdict(double mid, double end) =>
        end < 0.1 ? Model.NotApplied
        : mid >= 0.95 ? Model.Jumped
        : mid > 0.05 && end > 0.9 ? Model.Animating
        : string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $"UNCLEAR(mid={mid:F2},end={end:F2})");

    private static bool XamlTransition(UIElement t, string channel) => channel switch
    {
        "Opacity" => t.OpacityTransition is not null,
        "Scale" or "CenterPoint" => t.ScaleTransition is not null,
        "RotationAngle" => t.RotationTransition is not null,
        "Translation" => t.TranslationTransition is not null,
        _ => false,
    };

    private async Task<string> BackgroundVerdict()
    {
        var snap = await Snap.CaptureAsync(caseHost);
        var (x, y) = Model.Point(ExpectedPose(MatrixStep.Change), Rtl, Model.Shift(_case, MatrixStep.Change), 50, 20);
        // #FF2020 at opacity 0.8 over white has green ≈ 77; #FF0000 has ≈ 51.
        return snap.Green(x, y) > 64 ? Model.Jumped : Model.NotApplied;
    }

    /// <summary>The change-step bounds with only <paramref name="channel"/> at fraction <paramref name="p"/> of its move.</summary>
    private Box At(string channel, double p)
    {
        var to = ExpectedPose(MatrixStep.Change);
        var a = Model.Declared(_case.Transform, false);
        var b = Model.Declared(_case.Transform, true);
        float q = (float)p;
        var pose = channel switch
        {
            "Scale" => to with { Scale = Vector3.Lerp(a.Scale, b.Scale, q) },
            "RotationAngle" => to with { Degrees = a.Degrees + (b.Degrees - a.Degrees) * q },
            "Translation" => to with { Translation = to.Translation - b.Translation + Vector3.Lerp(a.Translation, b.Translation, q) },
            "CenterPoint" => to with { CenterPoint = Vector3.Lerp(a.CenterPoint, b.CenterPoint, q) },
            _ => to,
        };
        double shift = Model.Shift(_case, MatrixStep.Change);
        return Model.Bounds(pose, Rtl, channel == "Offset" ? shift * p : shift);
    }

    private bool Visible(string channel) =>
        channel == "Opacity" || Enumerable.Range(0, 4).Any(k => Math.Abs(At(channel, 1)[k] - At(channel, 0)[k]) >= 4);

    /// <summary>
    /// How far <paramref name="channel"/> has moved: the fraction whose modelled bounds best match
    /// the captured ones. A search rather than a projection, because bounds aren't linear in an
    /// angle or a pivot. Fractions outside [−1, 2] read as the nearest end.
    /// </summary>
    private double Progress(string channel, Snap snap)
    {
        if (channel == "Opacity")
        {
            var (x, y) = Model.Point(ExpectedPose(MatrixStep.Change), Rtl, Model.Shift(_case, MatrixStep.Change), 50, 20);
            bool alt = _case.Transition == TransitionAxis.Background;
            double from = Model.Green(Model.OpacityA, alt), to = Model.Green(Model.OpacityB, alt);
            return (snap.Green(x, y) - from) / (to - from);
        }

        if (snap.RedBox() is not { } box) return double.NaN;
        double best = double.NaN, bestError = double.MaxValue;
        for (int i = -100; i <= 200; i++)
        {
            var at = At(channel, i / 100.0);
            double error = Enumerable.Range(0, 4).Max(k => Math.Abs(at[k] - box[k]));
            if (error < bestError) { bestError = error; best = i / 100.0; }
        }

        // A pose the model can't reach at all also shows up as the step's pose disagreement.
        return best;
    }
    private void ObserveGeometry(OrderedDictionary<string, string> f, FrameworkElement t, MatrixStep step)
    {
        var pose = ExpectedPose(step);
        var hits = new List<string>();
        foreach (var (lx, ly) in new (double, double)[] { (50, 20), (90, 35), (10, 5) })
        {
            var (sx, sy) = Model.Point(pose, Rtl, Model.Shift(_case, step), lx, ly);
            var window = caseHost.TransformToVisual(null).TransformPoint(new Point(sx, sy));
            hits.Add(VisualTreeHelper.FindElementsInHostCoordinates(window, caseHost).Contains(t) ? "1" : "0");
        }

        f["hit"] = string.Join(",", hits);
        var peer = FrameworkElementAutomationPeer.FromElement(t) ?? new FrameworkElementAutomationPeer(t);
        var rect = peer.GetBoundingRectangle();
        double scale = t.XamlRoot?.RasterizationScale ?? 1;
        f["uia"] = string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $"{rect.Width / scale:F0},{rect.Height / scale:F0}");
        var gt = t.TransformToVisual(caseHost);
        var p0 = gt.TransformPoint(new Point(0, 0));
        var p1 = gt.TransformPoint(new Point(Model.Width, Model.Height));
        f["ttv"] = string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $"{p0.X:F0},{p0.Y:F0};{p1.X:F0},{p1.Y:F0}");
    }

    /// <summary>Which channel holds each transform: the facade getters, or the handoff visual if Reactor took it.</summary>
    internal static string Channel(FrameworkElement t)
    {
        static string Join(IEnumerable<string> xs) => string.Join(",", xs) is { Length: > 0 } s ? s : "-";
        var facade = new List<string>();
        if (t.Scale != Vector3.One) facade.Add("S");
        if (NonZero(t.Rotation)) facade.Add("R");
        if (t.Translation != Vector3.Zero) facade.Add("T");
        if (t.CenterPoint != Vector3.Zero) facade.Add("C");
        if (!ElementPool.IsVisualTaken(t)) return $"taken=0 facade={Join(facade)}";
        Visual v;
        try { v = ElementCompositionPreview.GetElementVisual(t); }
        catch (UnauthorizedAccessException) { return $"taken=refused facade={Join(facade)}"; }
        var visual = new List<string>();
        if (v.Scale != Vector3.One) visual.Add("S");
        if (NonZero(v.RotationAngle)) visual.Add("R");
        if (v.Properties.TryGetVector3("Translation", out var tr) == CompositionGetValueStatus.Succeeded && tr != Vector3.Zero) visual.Add("T");
        if (v.CenterPoint != Vector3.Zero) visual.Add("C");
        return $"taken=1 facade={Join(facade)} visual={Join(visual)}";
    }

    // ── Pool ────────────────────────────────────────────────────────────────

    /// <summary>Pixel difference from the same declaration mounted fresh: 0, some (≤200 px) or large.</summary>
    private async Task<string> DiffFromFresh(Element stage)
    {
        using var fresh = new Reconciler();
        var control = (FrameworkElement)fresh.Mount(stage, static () => { })!;
        freshHost.Child = control;
        try
        {
            await Settle();
            var a = await Snap.CaptureAsync(caseHost);
            var b = await Snap.CaptureAsync(freshHost);
            int d = a.Diff(b);
            return d < 0 ? "size" : d <= 4 ? "0" : d <= 200 ? "some" : "large";
        }
        finally
        {
            fresh.UnmountChild(control);
            freshHost.Child = null;
        }
    }

    /// <summary>
    /// Residue on a plain renter, probed without taking its visual: XAML transitions left set, and
    /// whether a facade transform write is refused, which means a previous user took the visual.
    /// </summary>
    internal static string ProbePlain(FrameworkElement t)
    {
        var xaml = new List<string>();
        if (t.OpacityTransition is not null) xaml.Add("Opacity");
        if (t.ScaleTransition is not null) xaml.Add("Scale");
        if (t.RotationTransition is not null) xaml.Add("Rotation");
        if (t.TranslationTransition is not null) xaml.Add("Translation");
        if (t is Panel { BackgroundTransition: not null }) xaml.Add("Background");
        if (t is Panel { ChildrenTransitions.Count: > 0 }) xaml.Add("Children");
        string taken;
        try { t.Rotation = 0; taken = "0"; }
        catch (UnauthorizedAccessException) { taken = "1"; }
        return $"taken={taken} xaml={(xaml.Count > 0 ? string.Join(",", xaml) : "-")}";
    }

    /// <summary>XAML transition properties still set, and implicit keys if Reactor took the visual.</summary>
    internal static string TransitionResidue(FrameworkElement t)
    {
        var xaml = new List<string>();
        if (t.OpacityTransition is not null) xaml.Add("Opacity");
        if (t.ScaleTransition is not null) xaml.Add("Scale");
        if (t.RotationTransition is not null) xaml.Add("Rotation");
        if (t.TranslationTransition is not null) xaml.Add("Translation");
        if (t is Panel { BackgroundTransition: not null }) xaml.Add("Background");
        string keys = "untaken";
        if (ElementPool.IsVisualTaken(t))
        {
            try
            {
                var implicitKeys = ElementCompositionPreview.GetElementVisual(t).ImplicitAnimations?.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
                keys = implicitKeys.Length > 0 ? string.Join(",", implicitKeys) : "-";
            }
            catch (UnauthorizedAccessException) { keys = "refused"; }
        }

        return $"xaml={(xaml.Count > 0 ? string.Join(",", xaml) : "-")} keys={keys}";
    }
    /// <summary>Implicit keys and running animations left on a transformed renter's visual.</summary>
    internal static string Residuals(FrameworkElement t)
    {
        // Taking the visual of a control with a facade transform throws (#1613), and it would be
        // the test's own handoff anyway. An untaken control can't hold implicit keys.
        if (!ElementPool.IsVisualTaken(t) && (t.Scale != Vector3.One || NonZero(t.Rotation) || t.CenterPoint != Vector3.Zero))
            return "keys=untaken anims=untaken";
        var v = ElementCompositionPreview.GetElementVisual(t);
        var keys = v.ImplicitAnimations?.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
        var running = Channels.Where(ch => Controller(v, ch) is not null).ToArray();
        return $"keys={(keys.Length > 0 ? string.Join(",", keys) : "-")} anims={(running.Length > 0 ? string.Join(",", running) : "-")}";
    }

    /// <summary>
    /// Lets animations play out, then stops them. A finished animation keeps its controller (and
    /// <c>TryGetAnimationController</c> keeps returning it), so stopping is what tells a later step
    /// which animations are new; once finished, stopping keeps the final value. A paused one can't
    /// be stopped early: that reverts it to its base value, and resuming it at progress 1 never
    /// finishes, so callers resume from the middle.
    /// </summary>
    private static async Task Finish(Visual visual, IEnumerable<string> channels, int remainingMs)
    {
        await Task.Delay(remainingMs + 300);
        await Frames(2);
        foreach (var ch in channels) visual.StopAnimation(ch);
        await Frames(2);
    }
    /// <summary>
    /// Waits out whatever still animates a transform or opacity channel when a step settles, so the
    /// rest pose is the end state (<c>.Animate()</c> animates mount values in, for example).
    /// Returns those channels, or null when Reactor never took the visual, so there is nothing a test
    /// may safely read. A looping keyframe is left alone.
    /// </summary>
    private async Task<string?> SettleRunning(FrameworkElement t, MatrixStep step)
    {
        // Stagger took the original control's visual (marked since #1643). A renter in the rent
        // steps must not be touched.
        bool staggered = _case.Feature == FeatureAxis.Stagger && step < MatrixStep.Unmount;
        if (!ElementPool.IsVisualTaken(t) && !staggered) return null;
        Visual visual;
        try { visual = ElementCompositionPreview.GetElementVisual(t); }
        catch (UnauthorizedAccessException) { return null; }
        var running = new[] { "Scale", "RotationAngle", "Translation", "CenterPoint", "Opacity" }
            .Where(ch => !(ch == "Opacity" && _case.Feature == FeatureAxis.Keyframes && _case.Variant == VariantAxis.B))
            .Where(ch => Controller(visual, ch) is not null)
            .ToList();
        if (running.Count > 0) await Finish(visual, running, MotionMs);
        return running.Count > 0 ? string.Join(",", running) : "-";
    }
    private static bool NonZero(float value) => MathF.Abs(value) > 1e-4f;

    /// <summary><c>TryGetAnimationController</c> throws for a property the visual doesn't have yet (Translation, #1641).</summary>
    internal static AnimationController? Controller(Visual v, string channel)
    {
        try { return v.TryGetAnimationController(channel); }
        catch (ArgumentException) { return null; }
    }

    // ── Timing ──────────────────────────────────────────────────────────────

    internal static async Task Frames(int count)
    {
        int frames = 0;
        var done = new TaskCompletionSource();
        EventHandler<object> onRendering = (_, _) => { if (++frames >= count) done.TrySetResult(); };
        CompositionTarget.Rendering += onRendering;
        try
        {
            if (await Task.WhenAny(done.Task, Task.Delay(2000)) != done.Task)
                throw new FrameTimeoutException($"waited 2 s for {count} frame(s) and got {frames}, so the compositor isn't rendering (a locked or disconnected session?)");
        }
        finally { CompositionTarget.Rendering -= onRendering; }
    }

    internal static async Task Settle(int ms = 60)
    {
        await Frames(4);
        if (ms > 0) await Task.Delay(ms);
    }
}

/// <summary>
/// The compositor stopped producing frames, so any capture would be stale. It makes an observation
/// inconclusive; it's never recorded as something the product threw.
/// </summary>
internal sealed class FrameTimeoutException(string message) : TimeoutException(message);

/// <summary>A RenderTargetBitmap capture of a host, with the pose and opacity oracles.</summary>
internal sealed record Snap(byte[] Pixels, int Width, int Height, double Sx, double Sy)
{
    public static async Task<Snap> CaptureAsync(FrameworkElement root)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var px = (await rtb.GetPixelsAsync()).ToArray();
        return new Snap(px, rtb.PixelWidth, rtb.PixelHeight, rtb.PixelWidth / root.ActualWidth, rtb.PixelHeight / root.ActualHeight);
    }

    /// <summary>Bounding box of the red target (BGRA: R &gt; 200, G and B &lt; 90), or null if none.</summary>
    public Box? RedBox()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                if (Pixels[i + 2] > 200 && Pixels[i + 1] < 90 && Pixels[i] < 90)
                {
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            }

        return maxX < 0 ? null : new Box(minX / Sx, minY / Sy, (maxX + 1) / Sx, (maxY + 1) / Sy);
    }

    /// <summary>Mean green over a 3×3 patch at a host point. Red at opacity a over white has G = 255(1 − a).</summary>
    public double Green(double x, double y)
    {
        int cx = (int)(x * Sx), cy = (int)(y * Sy);
        double sum = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                sum += Pixels[((Math.Clamp(cy + dy, 0, Height - 1) * Width) + Math.Clamp(cx + dx, 0, Width - 1)) * 4 + 1];
        return sum / 9;
    }

    /// <summary>Count of pixels whose channels differ by more than 8, or −1 when the sizes differ.</summary>
    public int Diff(Snap other)
    {
        if (other.Pixels.Length != Pixels.Length) return -1;
        int d = 0;
        for (int i = 0; i < Pixels.Length; i += 4)
            if (Math.Abs(Pixels[i] - other.Pixels[i]) > 8 || Math.Abs(Pixels[i + 1] - other.Pixels[i + 1]) > 8
                || Math.Abs(Pixels[i + 2] - other.Pixels[i + 2]) > 8)
                d++;
        return d;
    }
}

/// <summary>Collects <c>DiagnosticLog</c> warnings and swallowed errors emitted during a step.</summary>
internal sealed class DiagnosticsListener : EventListener
{
    // Field initializers run before the base constructor, which already calls OnEventSourceCreated.
    private readonly Lock _gate = new();
    private readonly List<string> _events = [];

    public void Reset() { lock (_gate) _events.Clear(); }

    public string Describe()
    {
        lock (_gate)
            return _events.Count > 0 ? string.Join(";", _events.Distinct(StringComparer.Ordinal)) : "-";
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-UI-Reactor") EnableEvents(source, EventLevel.Warning, (EventKeywords)0x20);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is not ("Warning" or "SwallowedError")) return;
        string category = e.Payload is { Count: > 0 } p ? p[0]?.ToString() ?? "" : "";
        string operation = e.Payload is { Count: > 1 } q ? q[1]?.ToString() ?? "" : "";
        lock (_gate) _events.Add($"{(e.EventName == "Warning" ? "W" : "E")}:{category}/{operation}");
    }
}
