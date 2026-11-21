using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using Model = Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix.CompositionModel;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 §12.1: the transforms validation matrix and behaviour ledger.
/// </summary>
/// <remarks>
/// <para>The mode comes from <c>REACTOR_TRANSFORMS_MATRIX</c>. Unset (the normal selftest corpus):
/// the instrument controls, a fixed seeded sample of cases and a short pool soak, each checked
/// against the committed ledger. <c>full</c>: every case, the full soak and the probes (the
/// <c>Transforms matrix</c> workflow). <c>record</c>: the same as <c>full</c>, but it writes the
/// observations to the ledger instead of comparing. <c>REACTOR_TRANSFORMS_MATRIX_FILTER</c> narrows
/// a full or record run to the case ids matching a <c>*</c> pattern.</para>
/// <para>Every observed field must match the ledger, poses within ±2 DIP. A mismatch reruns the case
/// up to three times before it fails (§12.1 C.5). Every step's disagreement with
/// <see cref="CompositionModel"/> must be listed in <c>transforms-ledger.known.json</c>.</para>
/// </remarks>
internal sealed class TransformsMatrixFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const int Retries = 3;

    private enum Mode { Sample, Full, Record }

    private static Mode CurrentMode => Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_MATRIX")?.Trim().ToLowerInvariant() switch
    {
        "full" => Mode.Full,
        "record" => Mode.Record,
        _ => Mode.Sample,
    };

    private static string? CaseFilter => Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_MATRIX_FILTER") is { Length: > 0 } f ? f : null;

    public override TimeSpan FixtureTimeout => CurrentMode == Mode.Sample ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(120);

    private Border _caseHost = null!, _freshHost = null!;

    // The PrintWindow oracle, once its controls pass; null without an interactive desktop.
    private Func<FrameworkElement, Snap>? _screen;

    // False once any check fails, so record mode never overwrites the ledger with an invalid recording.
    private bool _clean = true;

    private void Check(string name, bool ok, string detail)
    {
        H.Check(name, ok, detail);
        _clean &= ok;
    }

    public override async Task RunAsync()
    {
        var mode = CurrentMode;
        var committed = TransformsLedger.Parse(ReadBaseline(TransformsLedger.LedgerFile));
        var known = KnownDifference.ParseFile(ReadBaseline(TransformsLedger.KnownFile));
        Console.WriteLine($"# TransformsMatrix mode={mode} filter={CaseFilter ?? "-"}");

        _caseHost = HostBorder();
        _freshHost = HostBorder();
        H.SetContent(new StackPanel { Orientation = Orientation.Horizontal, Children = { _caseHost, _freshHost } });
        using var diagnostics = new DiagnosticsListener();
        try
        {
            if (!await RunInstruments(diagnostics))
            {
                H.Check("TransformsMatrix_InstrumentsValid", false, "an oracle failed its controls; nothing was recorded or compared");
                return;
            }

            var all = MatrixGenerator.Generate();
            var cases = mode == Mode.Sample ? MatrixGenerator.Sample(all) : all;
            if (mode != Mode.Sample && CaseFilter is { } filter) cases = cases.Where(c => Matches(filter, c.Id)).ToList();
            bool everything = mode != Mode.Sample && CaseFilter is null;

            var recorder = new TransformsMatrixRecorder(_caseHost, _freshHost, diagnostics, _screen);
            var observed = new TransformsLedger();
            var usedKnown = new Dictionary<KnownDifference, int>();
            var times = new List<double>();
            foreach (var c in cases)
            {
                var sw = Stopwatch.StartNew();
                var record = await ObserveCase(recorder, c, committed, mode);
                times.Add(sw.Elapsed.TotalSeconds);
                if (sw.Elapsed.TotalSeconds > 8) Console.WriteLine($"# TransformsMatrix {c.Id}: slow case, {sw.Elapsed.TotalSeconds:F1} s");
                observed.Cases[c.Id] = record;
                CheckModel(c, recorder, known, usedKnown);
            }

            if (times.Count > 0)
                Console.WriteLine($"# TransformsMatrix {times.Count} cases: total {times.Sum():F0} s, mean {times.Average():F2} s, max {times.Max():F2} s");

            foreach (var (id, steps) in mode == Mode.Sample ? [("@soak.short", 200)] : new[] { ("@soak.short", 200), ("@soak.full", 2000) })
            {
                if (CaseFilter is { } f && !Matches(f, id)) continue;
                observed.Cases[id] = await Compare(id, committed, mode, () => new PoolSoak(_caseHost).RunAsync(steps));
                CheckConclusive(id, observed.Cases[id]);
            }

            if (mode != Mode.Sample && (CaseFilter is null || Matches(CaseFilter, "@probe")))
            {
                observed.Cases["@probe"] = await Compare("@probe", committed, mode, () => TransformsMatrixProbes.RunAsync(_caseHost, H));
                CheckConclusive("@probe", observed.Cases["@probe"]);
            }

            if (everything)
            {
                foreach (var k in known) Console.WriteLine($"# TransformsMatrix known rule {k}: {usedKnown.GetValueOrDefault(k)} disagreement(s)");
                var stale = known.Where(k => !usedKnown.ContainsKey(k)).ToList();
                Check("TransformsMatrix_KnownDifferencesCurrent", stale.Count == 0,
                    $"{stale.Count} entries in {TransformsLedger.KnownFile} match no model disagreement anymore. Remove them "
                    + "(and declare the ledger change they reflect): " + string.Join("; ", stale));
            }

            if (mode == Mode.Record) WriteLedger(committed, observed, everything);
        }
        catch (FrameTimeoutException ex)
        {
            Check("TransformsMatrix_Rendering", false, $"{ex.Message}. The run stopped, and the ledger wasn't written.");
        }
        finally
        {
            H.SetContent(null);
        }
    }

    private async Task<LedgerCase> ObserveCase(TransformsMatrixRecorder recorder, MatrixCase c, TransformsLedger committed, Mode mode)
    {
        var record = await Compare(c.Id, committed, mode, () => recorder.ObserveAsync(c), recorder);
        Check($"TransformsMatrix_{c.Id}_Conclusive", recorder.Inconclusive.Count == 0, string.Join("; ", recorder.Inconclusive));
        return record;
    }

    /// <summary>
    /// Observes a case; on a ledger mismatch or an inconclusive oracle, reruns it up to
    /// <see cref="Retries"/> times (§12.1 C.5). Record mode reruns until one observation matches the
    /// committed ledger or two agree, at most twice, so an intended change costs two runs, not four. A
    /// case the ledger doesn't have yet always needs two agreeing runs.
    /// </summary>
    private async Task<LedgerCase> Compare(string id, TransformsLedger committed, Mode mode, Func<Task<LedgerCase>> observe,
        TransformsMatrixRecorder? recorder = null)
    {
        committed.Cases.TryGetValue(id, out var expected);
        var record = await observe();
        var diffs = Differences(id, expected, record);
        LedgerCase? previous = null;
        for (int run = 1; run <= (mode == Mode.Record ? 2 : Retries); run++)
        {
            bool inconclusive = recorder?.Inconclusive.Count > 0 || Unclear(record);
            if (!inconclusive && expected is not null && diffs.Count == 0) break;
            if (mode != Mode.Record && !inconclusive && expected is null) break;
            if (mode == Mode.Record && !inconclusive && previous is not null && Differences(id, previous, record).Count == 0) break;
            Console.WriteLine($"# TransformsMatrix {id}: {diffs.Count} difference(s) from the ledger{(inconclusive ? ", inconclusive" : "")}, rerun {run}");
            previous = record;
            record = await observe();
            diffs = Differences(id, expected, record);
        }

        if (mode == Mode.Record)
        {
            // A change or a new case is recorded only once two observations agree; three different ones are noise.
            bool confirmed = (expected is not null && diffs.Count == 0) || (previous is not null && Differences(id, previous, record).Count == 0);
            Check($"TransformsMatrix_{id}_Stable", confirmed, expected is null
                ? $"{id} is new and its observations disagreed, so the recording isn't reproducible: {string.Join(" | ", Differences(id, previous, record).Take(8))}"
                : $"{id} differed from the ledger and between its reruns, so the recording isn't reproducible: {string.Join(" | ", diffs.Take(8))}");
            foreach (var d in diffs) Console.WriteLine($"# TransformsMatrix ledger change: {d}");
            if (expected is not null) TransformsLedger.KeepUnchanged(expected, record);
            return record;
        }

        Check($"TransformsMatrix_{id}", diffs.Count == 0, expected is null
            ? $"{id} isn't in {TransformsLedger.LedgerFile}. Regenerate the ledger (see TESTING.md, \"Transforms matrix and behaviour ledger\")."
            : $"observed behaviour differs from {TransformsLedger.LedgerFile}: {string.Join(" | ", diffs.Take(8))}. "
              + "If the change is intended, regenerate the ledger with REACTOR_TRANSFORMS_MATRIX=record and declare each change "
              + $"in {TransformsLedger.ExpectedFile} with its §9 row or issue (TESTING.md, \"Transforms matrix and behaviour ledger\").");
        return record;
    }

    /// <summary>An <c>UNCLEAR</c> verdict means an oracle couldn't decide, so the observation can't become a baseline.</summary>
    private static bool Unclear(LedgerCase record) =>
        record.Steps.Values.Any(static s => s.Values.Any(static v => v.Contains("UNCLEAR", StringComparison.Ordinal)));

    private void CheckConclusive(string id, LedgerCase record) =>
        Check($"TransformsMatrix_{id}_Conclusive", !Unclear(record), $"{id} has a verdict no oracle could decide after its reruns: "
            + string.Join("; ", record.Steps.SelectMany(s => s.Value.Where(static f => f.Value.Contains("UNCLEAR", StringComparison.Ordinal)).Select(f => $"{s.Key}.{f.Key}={f.Value}"))));

    /// <summary>Each model disagreement must be attributed by exactly one rule in the known file.</summary>
    private void CheckModel(MatrixCase c, TransformsMatrixRecorder recorder, List<KnownDifference> known, Dictionary<KnownDifference, int> used)
    {
        var problems = new List<string>();
        foreach (var (step, part) in recorder.ModelDifferences)
        {
            var matches = known.Where(k => k.Matches(c.Id, c.Dims, step, part)).ToList();
            if (matches.Count == 1) used[matches[0]] = used.GetValueOrDefault(matches[0]) + 1;
            else if (matches.Count == 0) problems.Add($"{step}/{part} has no rule, for example {KnownDifference.Suggest(c.Id, step, part)}");
            else problems.Add($"{step}/{part} matches {matches.Count} rules ({string.Join("; ", matches)}); narrow them so one applies");
        }

        Check($"TransformsMatrix_{c.Id}_Model", problems.Count == 0,
            $"{c.Id} ({c.Dims}) disagrees with the composition model: {string.Join(" | ", problems)}. If main is wrong, attribute it in "
            + $"{TransformsLedger.KnownFile} with the issue or §9 row; if the model is wrong, fix CompositionModel. Never bend the model to fit main.");
    }

    private void WriteLedger(TransformsLedger committed, TransformsLedger observed, bool everything)
    {
        var output = new TransformsLedger { Seed = MatrixGenerator.DefaultSeed };
        if (everything)
        {
            foreach (var (id, c) in observed.Cases) output.Cases[id] = c;
        }
        else
        {
            foreach (var (id, c) in committed.Cases) output.Cases[id] = observed.Cases.TryGetValue(id, out var o) ? o : c;
            foreach (var (id, c) in observed.Cases) output.Cases.TryAdd(id, c);
        }

        string path = Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_LEDGER_OUT") is { Length: > 0 } p ? p
            : global::System.IO.Path.Join(RepoRoot() ?? AppContext.BaseDirectory, TransformsLedger.BaselineDirectory, TransformsLedger.LedgerFile);
        if (!_clean)
        {
            // Keep the observations for triage, but never where a commit or the workflow artifact would take them as the ledger.
            path = global::System.IO.Path.ChangeExtension(path, ".rejected.json");
            Console.WriteLine("# TransformsMatrix a check failed, so the ledger was not updated");
        }

        global::System.IO.File.WriteAllText(path, output.Write());
        Console.WriteLine($"# TransformsMatrix wrote {output.Cases.Count} cases to {path}");
    }

    /// <summary>
    /// EventListener callbacks don't flow under NativeAOT, so the AOT selftest job can't observe
    /// the <c>diag</c> field. It still compares every other field.
    /// </summary>
    private static bool DiagnosticsObservable => global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    private static List<LedgerDifference> Differences(string id, LedgerCase? expected, LedgerCase observed) =>
        TransformsLedger.CompareCase(id, expected, observed).Where(d => DiagnosticsObservable || d.Field != "diag").ToList();

    /// <summary>A filter is one or more <c>*</c> patterns separated by <c>|</c>.</summary>
    private static bool Matches(string filter, string id) => filter.Split('|').Any(p => Glob.IsMatch(p.Trim(), id));

    private static string? RepoRoot()
    {
        for (var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx"))) return dir.FullName;
        return null;
    }

    /// <summary>
    /// The repo copy when the host runs from a checkout, so an edited or re-recorded baseline is used
    /// without a rebuild; else the copy embedded at build time.
    /// </summary>
    private static string ReadBaseline(string file)
    {
        if (RepoRoot() is { } root && global::System.IO.Path.Join(root, TransformsLedger.BaselineDirectory, file) is var path
            && global::System.IO.File.Exists(path))
            return global::System.IO.File.ReadAllText(path);
        using var stream = typeof(TransformsMatrixFixture).Assembly.GetManifestResourceStream("TransformsMatrix." + file)
            ?? throw new InvalidOperationException($"Embedded baseline TransformsMatrix.{file} is missing from the host.");
        using var reader = new global::System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Border HostBorder() => new()
    {
        Width = Model.StageSize,
        Height = Model.StageSize,
        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    // ── §12.1 D: positive and negative controls for every oracle ───────────

    private async Task<bool> RunInstruments(DiagnosticsListener diagnostics)
    {
        bool ok = true;
        void Check(string name, bool pass, string detail)
        {
            H.Check("TransformsMatrix_Instrument_" + name, pass, detail);
            ok &= pass;
        }

        var canvas = new XCanvas { Width = Model.StageSize, Height = Model.StageSize };
        _caseHost.Child = canvas;
        Border Rect()
        {
            var b = new Border { Width = Model.Width, Height = Model.Height, Background = new SolidColorBrush(Microsoft.UI.Colors.Red) };
            XCanvas.SetLeft(b, Model.Left);
            XCanvas.SetTop(b, Model.Top);
            canvas.Children.Clear();
            canvas.Children.Add(b);
            return b;
        }

        async Task<Snap> Shot() { await TransformsMatrixRecorder.Settle(); return await Snap.CaptureAsync(_caseHost); }
        var identity = Model.Bounds(Pose.Identity, false);
        var doubled = Model.Bounds(Pose.Identity with { Scale = new Vector3(2, 2, 1) }, false);

        // Pose: must see a known facade transform and the model must predict it.
        var r = Rect();
        var plain = (await Shot()).RedBox();
        r.Scale = new Vector3(2, 2, 1);
        var scaled = (await Shot()).RedBox();
        Check("Pose", plain is { } p0 && p0.Near(identity, 2) && scaled is { } p1 && p1.Near(doubled, 2) && !p1.Near(identity, 2),
            $"identity={plain} scaled={scaled} model={identity}/{doubled}");

        // Hit-test, UIA and TransformToVisual must all see the same scale.
        var window = _caseHost.TransformToVisual(null).TransformPoint(new Point(Model.Left + 150, Model.Top + 30));
        bool hitScaled = VisualTreeHelper.FindElementsInHostCoordinates(window, _caseHost).Contains(r);
        double uiaScaled = (FrameworkElementAutomationPeer.FromElement(r) ?? new FrameworkElementAutomationPeer(r)).GetBoundingRectangle().Width / r.XamlRoot.RasterizationScale;
        var ttvScaled = r.TransformToVisual(_caseHost).TransformPoint(new Point(Model.Width, 0));
        r = Rect();
        await Shot();
        bool hitPlain = VisualTreeHelper.FindElementsInHostCoordinates(window, _caseHost).Contains(r);
        double uiaPlain = new FrameworkElementAutomationPeer(r).GetBoundingRectangle().Width / r.XamlRoot.RasterizationScale;
        var ttvPlain = r.TransformToVisual(_caseHost).TransformPoint(new Point(Model.Width, 0));
        Check("HitTest", hitScaled && !hitPlain, $"scaled={hitScaled} plain={hitPlain}");
        Check("Uia", Math.Abs(uiaScaled - 200) <= 2 && Math.Abs(uiaPlain - 100) <= 2, $"scaled={uiaScaled:F1} plain={uiaPlain:F1}");
        Check("TransformToVisual", Math.Abs(ttvScaled.X - (Model.Left + 200)) <= 1 && Math.Abs(ttvPlain.X - (Model.Left + 100)) <= 1,
            $"scaled={ttvScaled} plain={ttvPlain}");

        // Opacity: red at 0.8 over white reads green ≈ 51; opaque red reads 0.
        var center = (Model.Left + 50, Model.Top + 20);
        double g1 = (await Shot()).Green(center.Item1, center.Item2);
        r.Opacity = Model.OpacityB;
        double g08 = (await Shot()).Green(center.Item1, center.Item2);
        Check("Opacity", g1 < 10 && Math.Abs(g08 / (255 * (1 - Model.OpacityB)) - 1) < 0.2, $"opaque G={g1:F0} 0.8 G={g08:F0}");

        // Settled opacity: the recorder's reading tells a lost update (1) from the declared 0.8.
        r.Opacity = 1;
        var opaqueShot = await Shot();
        string settled1 = opaqueShot.RedBox() is { } ob ? TransformsMatrixRecorder.ReadOpacity(opaqueShot, ob, false) : "none";
        r.Opacity = Model.OpacityB;
        var fadedShot = await Shot();
        string settled08 = fadedShot.RedBox() is { } fb ? TransformsMatrixRecorder.ReadOpacity(fadedShot, fb, false) : "none";
        Check("SettledOpacity", settled1 == "1.00" && settled08 == "0.80", $"opacity 1 read {settled1}, 0.8 read {settled08}");

        // Motion: a seeked compositor animation reads mid-way at 50%; a direct write reads JUMPED.
        r = Rect();
        await Shot();
        var visual = ElementCompositionPreview.GetElementVisual(r);
        var compositor = visual.Compositor;
        var anim = compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1, new Vector3(2, 2, 1), compositor.CreateLinearEasingFunction());
        anim.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        visual.StartAnimation("Scale", anim);
        var ctl = visual.TryGetAnimationController("Scale");
        double Width(Snap s) => s.RedBox() is { } b ? (b.X1 - b.X0 - Model.Width) / Model.Width : double.NaN;
        double mid = double.NaN;
        if (ctl is not null) { ctl.Pause(); ctl.Progress = 0.5f; mid = Width(await Shot()); }
        visual.StopAnimation("Scale");
        visual.Scale = Vector3.One;
        await Shot();
        visual.Scale = new Vector3(2, 2, 1);
        await TransformsMatrixRecorder.Frames(2);
        double jumped = Width(await Snap.CaptureAsync(_caseHost));
        Check("Motion", ctl is not null && mid > 0.4 && mid < 0.6 && jumped > 0.95, $"controller={ctl is not null} mid={mid:F2} direct={jumped:F2}");

        // §12.1 A: implicit animations expose a controller, so transitions use the seeked path too.
        visual.Scale = Vector3.One;
        await Shot();
        var key = compositor.CreateVector3KeyFrameAnimation();
        key.InsertExpressionKeyFrame(1, "this.FinalValue");
        key.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        key.Target = "Scale";
        var implicitAnimations = compositor.CreateImplicitAnimationCollection();
        implicitAnimations["Scale"] = key;
        visual.ImplicitAnimations = implicitAnimations;
        visual.Scale = new Vector3(2, 2, 1);
        await TransformsMatrixRecorder.Frames(2);
        Check("ImplicitController", visual.TryGetAnimationController("Scale") is not null, "an implicit Scale animation exposes no controller");
        visual.ImplicitAnimations = null;
        visual.StopAnimation("Scale");

        // Channel: a taken visual answers through the visual, an untouched control through its facades.
        var facadeOnly = Rect();
        facadeOnly.Scale = new Vector3(2, 2, 1);
        await Shot();
        string facadeChannel = TransformsMatrixRecorder.Channel(facadeOnly);
        var handed = Rect();
        ElementPool.MarkCompositorTainted(handed);
        ElementCompositionPreview.GetElementVisual(handed).Scale = new Vector3(2, 2, 1);
        string visualChannel = TransformsMatrixRecorder.Channel(handed);
        Check("Channel", facadeChannel == "taken=0 facade=S" && visualChannel == "taken=1 facade=- visual=S", $"facade='{facadeChannel}' visual='{visualChannel}'");

        // Pool probes: a refused facade write means the visual was taken; pixel diff sees a change.
        string takenProbe = TransformsMatrixRecorder.ProbePlain(handed);
        string freshProbe = TransformsMatrixRecorder.ProbePlain(Rect());
        Check("PoolProbe", takenProbe.StartsWith("taken=1", StringComparison.Ordinal) && freshProbe.StartsWith("taken=0", StringComparison.Ordinal),
            $"taken='{takenProbe}' fresh='{freshProbe}'");
        var a = await Shot();
        Rect().Width = Model.Width + 20;
        var b2 = await Shot();
        Check("PixelDiff", a.Diff(a) == 0 && a.Diff(b2) > 200, $"self={a.Diff(a)} changed={a.Diff(b2)}");

        // Curve: a quarter of the way through, linear reads 0.25 and the Fluent ease about 0.05.
        void SeekToQuarter(CompositionEasingFunction easing)
        {
            var b = Rect();
            var v = ElementCompositionPreview.GetElementVisual(b);
            var a = v.Compositor.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(1, new Vector3(2, 2, 1), easing);
            a.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
            v.StartAnimation("Scale", a);
            var c = v.TryGetAnimationController("Scale")!;
            c.Pause();
            c.Progress = 0.25f;
        }

        SeekToQuarter(compositor.CreateLinearEasingFunction());
        double linear = Width(await Shot());
        SeekToQuarter(compositor.CreateCubicBezierEasingFunction(new Vector2(0.8f, 0), new Vector2(0.2f, 1)));
        double fluent = Width(await Shot());
        Check("Curve", Math.Abs(linear - 0.25) < 0.06 && fluent < 0.1, $"linear={linear:F2} fluent={fluent:F2}");

        // Pool residue: a taken visual with an implicit key and a running animation reads as such.
        var dirty = Rect();
        ElementPool.MarkCompositorTainted(dirty);
        var dv = ElementCompositionPreview.GetElementVisual(dirty);
        var dkey = compositor.CreateScalarKeyFrameAnimation();
        dkey.InsertExpressionKeyFrame(1, "this.FinalValue");
        dkey.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.LongMs);
        dkey.Target = "Opacity";
        var dkeys = compositor.CreateImplicitAnimationCollection();
        dkeys["Opacity"] = dkey;
        dv.ImplicitAnimations = dkeys;
        dv.Opacity = 0.8f;
        await TransformsMatrixRecorder.Frames(2);
        string residueDirty = TransformsMatrixRecorder.Residuals(dirty);
        string residueClean = TransformsMatrixRecorder.Residuals(Rect());
        Check("PoolResidue", residueDirty == "keys=Opacity anims=Opacity" && residueClean == "keys=- anims=-",
            $"dirty='{residueDirty}' clean='{residueClean}'");
        dv.ImplicitAnimations = null;

        // Layout Offset: a .LayoutAnimation() move exposes an Offset controller that seeks like any
        // other channel; a plain move has none and lands at once.
        var animatedMove = await LayoutMove(animated: true);
        var plainMove = await LayoutMove(animated: false);
        Check("LayoutOffset", animatedMove.Mid > 0.05 && animatedMove.Mid < 0.95 && animatedMove.End > 0.9
            && plainMove.Mid is double.NaN && plainMove.End > 0.9, $"animated={animatedMove} plain={plainMove}");
        // Screen (§12.2): with an interactive desktop, PrintWindow must show a facade Translation write
        // and then its reset. Q1f, where the screen drops a write that RenderTargetBitmap reports
        // applied, reproduced over RDP in the spike but not on hosted runners, so it's logged as a
        // platform fact rather than gating the oracle. Drag cases then record the screen pose; without a desktop they record "skip".
        _screen = null;
        _caseHost.Child = canvas;
        Rect();
        await Shot();
        if (TransformsMatrixProbes.Screen(_caseHost, H).RedBox() is { } seen && seen.Near(identity, 3))
        {
            r = Rect();
            await Shot();
            r.Translation = new Vector3(100, 0, 0);
            var applied = await ReadTranslation();
            r.Translation = Vector3.Zero;
            var reset = await ReadTranslation();
            // Q1f as the spike ran it: visual taken, translation enabled, an implicit "Translation" key, rendered.
            r = Rect();
            await Shot();
            var tv = ElementCompositionPreview.GetElementVisual(r);
            ElementCompositionPreview.SetIsTranslationEnabled(r, true);
            var tkey = compositor.CreateVector3KeyFrameAnimation();
            tkey.InsertExpressionKeyFrame(1, "this.FinalValue");
            tkey.Duration = TimeSpan.FromMilliseconds(TransformsMatrixRecorder.MotionMs);
            tkey.Target = "Translation";
            var tkeys = compositor.CreateImplicitAnimationCollection();
            tkeys["Translation"] = tkey;
            tv.ImplicitAnimations = tkeys;
            await Shot();
            r.Translation = new Vector3(100, 0, 0);
            var q1f = await ReadTranslation(TransformsMatrixRecorder.MotionMs + 300);
            tv.ImplicitAnimations = null;
            Check("Screen", applied.Rtb > 0.9 && applied.Screen > 0.9 && reset.Rtb < 0.1 && reset.Screen < 0.1,
                $"written rtb={applied.Rtb:F2} screen={applied.Screen:F2}, reset rtb={reset.Rtb:F2} screen={reset.Screen:F2} (expected 1/1, then 0/0)");
            Console.WriteLine($"# TransformsMatrix screen: Q1f rtb={q1f.Rtb:F2} screen={q1f.Screen:F2} "
                + (q1f.Rtb > 0.9 && q1f.Screen < 0.1 ? "(reproduced: drag cases need the screen pose)" : "(not reproduced on this desktop)"));
            _screen = target => TransformsMatrixProbes.Screen(target, H);
        }
        else
        {
            H.Skip("TransformsMatrix_Instrument_Screen", "PrintWindow can't see the window (no interactive desktop), so drag cases record screen=skip");
        }

        async Task<(double Rtb, double Screen)> ReadTranslation(int ms = 300)
        {
            await TransformsMatrixRecorder.Settle(ms);
            static double X(Snap s) => s.RedBox() is { } b ? (b.X0 - Model.Left) / 100 : double.NaN;
            return (X(await Snap.CaptureAsync(_caseHost)), X(TransformsMatrixProbes.Screen(_caseHost, H)));
        }

        // Diagnostics: a warning emitted during a step is seen, and nothing is reported otherwise.
        diagnostics.Reset();
        string quiet = diagnostics.Describe();
        DiagnosticLog.Warning(LogCategory.Reactor, "TransformsMatrix.Control", "positive control");
        string heard = diagnostics.Describe();
        if (DiagnosticsObservable)
            Check("Diagnostics", quiet == "-" && heard.Contains("TransformsMatrix.Control", StringComparison.Ordinal), $"quiet='{quiet}' heard='{heard}'");
        else
            H.Skip("TransformsMatrix_Instrument_Diagnostics", "EventListener callbacks don't flow under NativeAOT, so the diag field isn't compared");

        canvas.Children.Clear();
        _caseHost.Child = null;
        return ok;
    }
    /// <summary>
    /// A 20 px margin move: the move's progress with its Offset controller seeked to the middle (NaN
    /// when there's no controller), and the progress once it has settled.
    /// </summary>
    private async Task<(double Mid, double End)> LayoutMove(bool animated)
    {
        using var reconciler = new Reconciler();
        Element Stage(double left)
        {
            Element target = VStack().Width(Model.Width).Height(Model.Height).Background("#FFFF0000")
                .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Margin(left, Model.Top, 0, 0);
            if (animated) target = target.LayoutAnimation(TimeSpan.FromMilliseconds(TransformsMatrixRecorder.MotionMs));
            return VStack(0, target).Width(Model.StageSize).Height(Model.StageSize).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);
        }

        var first = Stage(Model.Left);
        var stage = (Panel)reconciler.Mount(first, static () => { })!;
        _caseHost.Child = stage;
        try
        {
            await TransformsMatrixRecorder.Settle();
            reconciler.Update(first, Stage(Model.Left + Model.LayoutShift), stage, static () => { });
            await TransformsMatrixRecorder.Frames(2);
            double Moved(Snap s) => s.RedBox() is { } b ? (b.X0 - Model.Left) / Model.LayoutShift : double.NaN;
            double mid = double.NaN;
            if (ElementCompositionPreview.GetElementVisual(stage.Children[0]).TryGetAnimationController("Offset") is { } controller)
            {
                controller.Pause();
                controller.Progress = 0.5f;
                await TransformsMatrixRecorder.Frames(3);
                mid = Moved(await Snap.CaptureAsync(_caseHost));
                controller.Resume();
            }

            await Task.Delay(TransformsMatrixRecorder.MotionMs + 300);
            await TransformsMatrixRecorder.Frames(2);
            return (mid, Moved(await Snap.CaptureAsync(_caseHost)));
        }
        finally
        {
            reconciler.UnmountChild(stage);
            _caseHost.Child = null;
        }
    }
}
/// <summary>
/// §12.1 E pool soak: seeded random churn of elements with random features. After every step that
/// returned controls to the pool, plain probe elements rent them and must pass the reset invariants:
/// identity facades, no XAML transitions, and a visual that was never taken. The counts go into
/// the ledger, so a layer that fixes a leak (#1624, #1639, #1643) declares it there.
/// </summary>
internal sealed class PoolSoak(Border host)
{
    private const int Seed = 1630;
    private const int MaxElements = 500;
    private const int MaxLive = 12;

    // An element either has XAML transitions or compositor features, never both: combining them
    // throws (#1623), which is the matrix's business, and would end the soak at its first throw.
    private sealed record Spec(int Id, bool Xaml, int Transform, int Transition, int Feature)
    {
        public static Spec Next(Random r, int id, bool xaml) =>
            new(id, xaml, r.Next(4), xaml ? r.Next(4) : 0, xaml ? (r.Next(2) == 0 ? 0 : 5) : r.Next(5));
    }

    public async Task<LedgerCase> RunAsync(int steps)
    {
        var random = new Random(Seed);
        using var reconciler = new Reconciler();
        var live = new List<Spec>();
        int created = 0, rented = 0;
        var pooled = new HashSet<UIElement>();
        var violations = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["facade"] = 0, ["taken"] = 0, ["xaml"] = 0 };
        string threw = "-";
        Element Stage(IEnumerable<Element> children) => VStack([.. children]).Width(Model.StageSize).Height(Model.StageSize);
        Element current = Stage([]);
        var stage = (StackPanel)reconciler.Mount(current, static () => { })!;
        host.Child = stage;
        void Render(Element next) { reconciler.Update(current, next, stage, static () => { }); current = next; }

        int step = 0;
        try
        {
            for (; step < steps; step++)
            {
                double op = random.NextDouble();
                var before = stage.Children.ToHashSet();
                if ((live.Count < 3 || op < 0.4) && created < MaxElements && live.Count < MaxLive)
                    live.Add(Spec.Next(random, created++, random.Next(2) == 0));
                else if (op < 0.75 && live.Count > 0)
                {
                    int i = random.Next(live.Count);
                    live[i] = Spec.Next(random, live[i].Id, live[i].Xaml);
                }
                else if (live.Count > 0)
                    live.RemoveAt(random.Next(live.Count));

                Render(Stage(live.Select(Build)));
                await TransformsMatrixRecorder.Frames(1);
                var removed = before.Except(stage.Children).ToList();
                if (removed.Count == 0) continue;
                pooled.UnionWith(removed);

                // Plain probes rent whatever the pool now holds, are checked, then go back.
                Render(Stage(live.Select(Build).Concat(Enumerable.Range(0, removed.Count).Select(i => Probe().WithKey($"probe{i}")))));
                await TransformsMatrixRecorder.Frames(1);
                var probes = stage.Children.OfType<StackPanel>().Where(static p => Math.Abs(p.Width - ProbeWidth) < 0.5).ToList();
                foreach (var probe in probes.Where(pooled.Contains))
                {
                    rented++;
                    if (probe.Scale != Vector3.One || Math.Abs(probe.Rotation) > 1e-4f || probe.Translation != Vector3.Zero || probe.CenterPoint != Vector3.Zero)
                        violations["facade"]++;
                    if (probe.OpacityTransition is not null || probe.BackgroundTransition is not null || probe.ScaleTransition is not null
                        || probe.RotationTransition is not null || probe.TranslationTransition is not null || probe.ChildrenTransitions is { Count: > 0 })
                        violations["xaml"]++;
                    try { probe.Rotation = 0; }
                    catch (UnauthorizedAccessException) { violations["taken"]++; }
                }

                Render(Stage(live.Select(Build)));
                await TransformsMatrixRecorder.Frames(1);
                pooled.UnionWith(probes);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not FrameTimeoutException)
        {
            threw = $"{ex.GetType().Name}@{step}";
        }
        finally
        {
            reconciler.UnmountChild(stage);
            host.Child = null;
        }

        var record = new LedgerCase();
        var f = record.Step("run");
        f["threw"] = threw;
        f["steps"] = steps.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        f["created"] = created.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        f["rented"] = rented.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        f["violations"] = string.Join(" ", violations.Select(kv => $"{kv.Key}={kv.Value}"));
        return record;
    }

    private const double ProbeWidth = 30;

    private static Element Probe() => VStack().Width(ProbeWidth).Height(10).Background("#FF0000FF");

    private static Element Build(Spec s)
    {
        var inner = VStack().Width(40).Height(20).Background("#FFFF0000");
        if (s.Transition == 3) inner = inner.BackgroundTransition(TimeSpan.FromMilliseconds(200));
        Element e = inner.WithKey($"e{s.Id}");
        e = s.Transform switch
        {
            1 => e.Scale(1.5f),
            2 => e.Rotation(30),
            3 => e.Translation(10, 5, 0),
            _ => e,
        };
        e = s.Transition switch
        {
            1 => e.OpacityTransition(TimeSpan.FromMilliseconds(200)),
            2 => e.ScaleTransition(),
            _ => e,
        };
        return s.Feature switch
        {
            1 => e.Animate(Curve.Linear(200)),
            2 => e.InteractionStates(static b => b.PointerOver(scale: 1.1f)),
            3 => e.Keyframes("soak", 1, static k => k.Duration(30).At(0f, opacity: 0.9f).At(1f, opacity: 1f)),
            4 => e.LayoutAnimation(TimeSpan.FromMilliseconds(200)),
            5 => e.WithTransitions(new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition()),
            _ => e,
        };
    }
}
