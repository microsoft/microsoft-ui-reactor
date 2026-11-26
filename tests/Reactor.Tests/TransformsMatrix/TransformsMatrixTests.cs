using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.TransformsMatrix;

/// <summary>
/// Spec 066 §12.1: the transforms matrix generator, the composition model, and the behaviour
/// ledger's diff and declaration rules. The selftest host records the ledger; this suite checks
/// the pieces that don't need WinUI, including the PR gate that every ledger change is declared.
/// </summary>
public class TransformsMatrixTests
{
    private static readonly IReadOnlyList<MatrixCase> Matrix = MatrixGenerator.Generate();

    // ── Generator ───────────────────────────────────────────────────────────

    [Fact]
    public void Matrix_HasEveryTransformTransitionFeatureTriple_Once()
    {
        int triples = Enum.GetValues<TransformAxis>().Length * Enum.GetValues<TransitionAxis>().Length * Enum.GetValues<FeatureAxis>().Length;
        Assert.Equal(432, triples);
        Assert.Equal(triples, Matrix.Count);
        Assert.Equal(triples, Matrix.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Matrix_CoversEveryPairOfValues_AcrossAllDimensions()
    {
        var covered = Matrix.SelectMany(c => MatrixGenerator.Pairs(MatrixGenerator.Row(c))).ToHashSet();
        var card = MatrixGenerator.Cardinality;
        var missing = new List<string>();
        for (int i = 0; i < card.Count; i++)
            for (int j = i + 1; j < card.Count; j++)
                for (int a = 0; a < card[i]; a++)
                    for (int b = 0; b < card[j]; b++)
                        if (!covered.Contains((i, a, j, b))) missing.Add($"dim{i}={a} dim{j}={b}");
        Assert.True(missing.Count == 0, "uncovered pairs: " + string.Join(", ", missing));
    }

    [Fact]
    public void Matrix_IsStableForASeed_AndIdsNeverDependOnIt()
    {
        Assert.Equal(Matrix, MatrixGenerator.Generate());
        var other = MatrixGenerator.Generate(seed: 7);
        Assert.Equal(Matrix.Select(c => c.Id), other.Select(c => c.Id));
        Assert.NotEqual(Matrix.Select(c => c.Dims), other.Select(c => c.Dims));
    }

    [Fact]
    public void Sample_StartsWithTheControlGroup_AndCoversEveryValue()
    {
        var sample = MatrixGenerator.Sample(Matrix);
        Assert.InRange(sample.Count, 9, 12);
        Assert.True(sample[0].IsControlGroup);
        Assert.Equal("wrapper=Direct flow=Ltr motion=Enabled user=None variant=A", sample[0].Dims);
        Assert.Equal(LedgerCheck.ControlGroupId, sample[0].Id);
        var card = MatrixGenerator.Cardinality;
        for (int d = 0; d < card.Count; d++)
            Assert.Equal(card[d], sample.Select(c => MatrixGenerator.Row(c)[d]).Distinct().Count());
        Assert.Equal(sample, MatrixGenerator.Sample(Matrix));
    }

    // ── Composition model ───────────────────────────────────────────────────

    [Fact]
    public void Bounds_Identity_IsTheLaidOutTarget_AndRtlMirrorsIt()
    {
        Assert.Equal(new Box(120, 150, 220, 190), CompositionModel.Bounds(Pose.Identity, rtl: false));
        Assert.Equal(new Box(180, 150, 280, 190), CompositionModel.Bounds(Pose.Identity, rtl: true));
    }

    [Fact]
    public void Bounds_ScaleAndRotation_TurnAboutTheCenterPoint()
    {
        var scaled = Pose.Identity with { Scale = new Vector3(2, 2, 1), CenterPoint = new Vector3(50, 20, 0) };
        Assert.Equal(new Box(70, 130, 270, 210), CompositionModel.Bounds(scaled, rtl: false));

        // 90° about the top-left corner swings the 100×40 rect to the left of its origin.
        var rotated = CompositionModel.Bounds(Pose.Identity with { Degrees = 90 }, rtl: false);
        Assert.True(rotated.Near(new Box(80, 150, 120, 250), 1e-9), rotated.ToString());

        // Translation adds after the pivot; a lone CenterPoint moves nothing.
        Assert.Equal(new Box(140, 160, 240, 200), CompositionModel.Bounds(Pose.Identity with { Translation = new Vector3(20, 10, 0) }, rtl: false));
        Assert.Equal(CompositionModel.Bounds(Pose.Identity, false), CompositionModel.Bounds(Pose.Identity with { CenterPoint = new Vector3(50, 20, 0) }, false));
    }

    [Fact]
    public void Bounds_TransformOrigin_IsSizeRelative_AndAddsTheCenterPointOffset()
    {
        // Spec 066 §8: pivot = Size × origin + CenterPoint. The centre of the 100×40 target is (50,20).
        var scaled = Pose.Identity with { Scale = new Vector3(2, 2, 1) };
        Assert.Equal(new Box(70, 130, 270, 210), CompositionModel.Bounds(scaled with { Origin = new Vector2(0.5f, 0.5f) }, rtl: false));
        Assert.Equal(CompositionModel.Bounds(scaled with { CenterPoint = new Vector3(60, 20, 0) }, false),
            CompositionModel.Bounds(scaled with { Origin = new Vector2(0.5f, 0.5f), CenterPoint = new Vector3(10, 0, 0) }, false));
        // Off-centre: 40% across pivots at x = 40, and RTL mirrors it (x is flow-relative).
        var offCentre = scaled with { Origin = new Vector2(0.4f, 0.5f) };
        Assert.True(CompositionModel.Bounds(offCentre, rtl: false).Near(new Box(80, 130, 280, 210), 1e-3));
        Assert.True(CompositionModel.Bounds(offCentre, rtl: true).Near(new Box(120, 130, 320, 210), 1e-3));
    }

    [Fact]
    public void Expected_SetterScale_SurvivesOnlyWhereNoModifierDeclaresScale()
    {
        MatrixCase Case(TransformAxis t) => Matrix.First(c => c.Transform == t) with { UserCode = UserCodeAxis.SetFacade };
        Assert.Equal(new Vector3(1.1f, 1.1f, 1), CompositionModel.Expected(Case(TransformAxis.Rotation), MatrixStep.Mount).Scale);
        Assert.Equal(new Vector3(1.5f, 1.5f, 1), CompositionModel.Expected(Case(TransformAxis.Scale), MatrixStep.Mount).Scale);
        Assert.Equal(Vector3.One, CompositionModel.Expected(Case(TransformAxis.Identity), MatrixStep.Change).Scale);
        Assert.Equal(Vector3.One, CompositionModel.Expected(Case(TransformAxis.Rotation), MatrixStep.RentPlain).Scale);
    }

    [Theory]
    [InlineData("Scale", "WithAnimation", "Disabled", "Scale", "ANIMATING")]
    [InlineData("Scale", "None", "Enabled", "Scale", "ANIMATING")]
    [InlineData("Scale", "None", "Disabled", "Scale", "JUMPED")]
    [InlineData("Opacity", "Animate", "Disabled", "Opacity", "ANIMATING")]
    [InlineData("Opacity", "None", "Enabled", "Scale", "JUMPED")]
    [InlineData("None", "LayoutAnimation", "Enabled", "Offset", "ANIMATING")]
    public void ExpectedMotion_FollowsThePrecedence(string transition, string feature, string motion, string channel, string expected)
    {
        var c = Matrix.First(x => x.Transition == Enum.Parse<TransitionAxis>(transition) && x.Feature == Enum.Parse<FeatureAxis>(feature))
            with { Motion = Enum.Parse<MotionAxis>(motion) };
        Assert.Equal(expected, CompositionModel.ExpectedMotion(c, channel));
    }

    [Fact]
    public void ExpectedMotion_APlayingKeyframeLoopHoldsItsChannel()
    {
        var c = Matrix.First(x => x.Feature == FeatureAxis.Keyframes && x.Transition == TransitionAxis.Opacity);
        Assert.Equal("HELD", CompositionModel.ExpectedMotion(c with { Variant = VariantAxis.B }, "Opacity"));
        Assert.Equal("ANIMATING", CompositionModel.ExpectedMotion(c with { Variant = VariantAxis.A, Motion = MotionAxis.Enabled }, "Opacity"));
    }

    // ── Ledger format and diff ──────────────────────────────────────────────

    private static TransformsLedger Ledger(params (string Case, string Step, string Field, string Value)[] rows)
    {
        var ledger = new TransformsLedger();
        foreach (var (c, s, f, v) in rows) ledger.Case(c).Step(s)[f] = v;
        return ledger;
    }

    [Fact]
    public void Ledger_WritesOneStepPerLine_AndRoundTrips()
    {
        var ledger = Ledger(("A.B.C", "mount", "pose", "1,2,3,4"), ("A.B.C", "mount", "threw", "-"), ("A.B.C", "change", "motion", "Scale=JUMPED"));
        ledger.Case("A.B.C").Dims = "wrapper=Direct";
        string text = ledger.Write();
        Assert.Contains("      \"mount\": { \"pose\": \"1,2,3,4\", \"threw\": \"-\" },\n", text, StringComparison.Ordinal);
        Assert.Equal(text, TransformsLedger.Parse(text).Write());
    }

    [Theory]
    [InlineData("pose", "120,150,220,190", "122,148,220,191", true)]
    [InlineData("pose", "120,150,220,190", "123,150,220,190", false)]
    [InlineData("ttv", "1,2;3,4", "2,2;3,5", true)]
    [InlineData("pose", "120,150,220,190", "none", false)]
    [InlineData("pose", "120,150,220,190", "NaN,NaN,NaN,NaN", false)]
    [InlineData("pose", "NaN,150,220,190", "NaN,150,220,190", true)]
    [InlineData("pool", "recycled=1 diff=0", "recycled=0 diff=0", false)]
    [InlineData("motion", "Scale=ANIMATING", "Scale=JUMPED", false)]
    // A run without a desktop says nothing about the screen pose, whichever side it's on.
    [InlineData("screen", "220,150,320,190", "skip", true)]
    [InlineData("screen", "skip", "220,150,320,190", true)]
    [InlineData("screen", "220,150,320,190", "260,150,360,190", false)]
    [InlineData("pose", "220,150,320,190", "skip", false)]
    public void FieldEquals_AllowsTwoDipsOnGeometryOnly(string field, string a, string b, bool equal) =>
        Assert.Equal(equal, TransformsLedger.FieldEquals(field, a, b));

    [Theory]
    [InlineData(1.0, false, "1.00")]
    [InlineData(0.8, false, "0.80")]
    [InlineData(1.0, true, "1.00")]
    [InlineData(0.8, true, "0.80")]
    public void OpacityFromGreen_TellsALostUpdateFromTheDeclaredValue_ThroughPixelNoise(double opacity, bool alt, string expected)
    {
        double green = CompositionModel.Green(opacity, alt);
        Assert.Equal(expected, CompositionModel.OpacityFromGreen(green, alt));
        Assert.Equal(expected, CompositionModel.OpacityFromGreen(Math.Max(0, green - 2), alt));
        Assert.Equal(expected, CompositionModel.OpacityFromGreen(green + 2, alt));
    }

    [Fact]
    public void Curve_IsNotComparedUnderADrag_AndRecordingKeepsTheCommittedValue()
    {
        TransformsLedger Case(string dims, string curve)
        {
            var l = Ledger(("X", "change", "curve", curve), ("X", "change", "motion", "Translation=ANIMATING"));
            l.Case("X").Dims = dims;
            return l;
        }

        const string drag = "wrapper=Direct flow=Ltr motion=Enabled user=TranslationDrag variant=A";
        const string none = "wrapper=Direct flow=Ltr motion=Enabled user=None variant=A";
        Assert.Empty(TransformsLedger.Diff(Case(drag, "Translation=0.2"), Case(drag, "Translation=0.0")));
        Assert.Equal("curve", Assert.Single(TransformsLedger.Diff(Case(none, "Translation=0.2"), Case(none, "Translation=0.0"))).Field);

        var observed = Case(drag, "Translation=0.0").Case("X");
        TransformsLedger.KeepUnchanged(Case(drag, "Translation=0.2").Case("X"), observed);
        Assert.Equal("Translation=0.2", observed.Steps["change"]["curve"]);
    }

    [Fact]
    public void KeepUnchanged_KeepsTheCommittedValueWithinTolerance()
    {
        var committed = Ledger(("X", "mount", "pose", "120,150,220,190"), ("X", "mount", "threw", "-")).Case("X");
        var observed = Ledger(("X", "mount", "pose", "121,150,220,190"), ("X", "mount", "threw", "InvalidOperationException")).Case("X");
        TransformsLedger.KeepUnchanged(committed, observed);
        Assert.Equal("120,150,220,190", observed.Steps["mount"]["pose"]);
        Assert.Equal("InvalidOperationException", observed.Steps["mount"]["threw"]);
    }

    [Fact]
    public void KeepUnchanged_ReplacesASkippedScreenWithARealOne_AndKeepsARealOneOverASkip()
    {
        var withSkip = Ledger(("X", "mount", "screen", "skip")).Case("X");
        var real = Ledger(("X", "mount", "screen", "220,150,320,190")).Case("X");
        TransformsLedger.KeepUnchanged(withSkip, real);
        Assert.Equal("220,150,320,190", real.Steps["mount"]["screen"]);

        var skipped = Ledger(("X", "mount", "screen", "skip")).Case("X");
        TransformsLedger.KeepUnchanged(Ledger(("X", "mount", "screen", "220,150,320,190")).Case("X"), skipped);
        Assert.Equal("220,150,320,190", skipped.Steps["mount"]["screen"]);
    }

    // ── The PR gate: every base-to-head change is declared ──────────────────

    private static readonly TransformsLedger Base = Ledger(
        ("Scale.Opacity.Animate", "change", "motion", "Opacity=NOT-APPLIED"),
        ("Scale.Scale.Animate", "change", "motion", "Scale=NOT-APPLIED"),
        (LedgerCheck.ControlGroupId, "mount", "pose", "120,150,220,190"),
        (LedgerCheck.ControlGroupId, "rentTransformed", "channel", "taken=0"));

    private static TransformsLedger Head(string opacity = "Opacity=NOT-APPLIED", string scale = "Scale=NOT-APPLIED",
        string controlPose = "120,150,220,190", string controlRent = "taken=0") => Ledger(
        ("Scale.Opacity.Animate", "change", "motion", opacity),
        ("Scale.Scale.Animate", "change", "motion", scale),
        (LedgerCheck.ControlGroupId, "mount", "pose", controlPose),
        (LedgerCheck.ControlGroupId, "rentTransformed", "channel", controlRent));

    [Fact]
    public void Check_AnUndeclaredChangeFails_AndTellsTheAuthorHowToDeclareIt()
    {
        var check = LedgerCheck.Run(Base, Head(scale: "Scale=ANIMATING"), []);
        Assert.False(check.Passed);
        var d = Assert.Single(check.Undeclared);
        Assert.Equal(("Scale.Scale.Animate", "change", "motion"), (d.Case, d.Step, d.Field));
        string message = check.Describe();
        Assert.Contains("transforms-ledger.expected.json", message, StringComparison.Ordinal);
        Assert.Contains("\"before\": \"Scale=NOT-APPLIED\", \"after\": \"Scale=ANIMATING\"", message, StringComparison.Ordinal);
        Assert.Contains("§9 row N | fixes #NNNN", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ADeclaredChangePasses_WithWildcardSelectors()
    {
        var head = Head(opacity: "Opacity=ANIMATING", scale: "Scale=ANIMATING");
        var check = LedgerCheck.Run(Base, head, [new("Scale.*.Animate", "change", "motion", "*", "*", "fixes #1625")]);
        Assert.True(check.Passed, check.Describe());
    }

    [Fact]
    public void Check_AnExpectationThatMatchesNothingIsStale()
    {
        var check = LedgerCheck.Run(Base, Head(), [new("Scale.Scale.Animate", "change", "motion", "Scale=NOT-APPLIED", "Scale=ANIMATING", "fixes #1626")]);
        Assert.False(check.Passed);
        Assert.Single(check.Stale);
        Assert.Contains("match no change", check.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_TheControlGroupCantBeExcused_ExceptItsRentAsTransformedStep()
    {
        var everything = new LedgerExpectation("*", "*", "*", "*", "*", "§9 row 7");
        var moved = LedgerCheck.Run(Base, Head(controlPose: "130,150,230,190"), [everything]);
        Assert.False(moved.Passed);
        Assert.Single(moved.ControlGroup);
        Assert.Single(moved.Stale);

        // Byte-identical: even a one-DIP move, inside the observation tolerance, fails.
        var nudged = LedgerCheck.Run(Base, Head(controlPose: "121,150,220,190"), []);
        Assert.Equal("121,150,220,190", Assert.Single(nudged.ControlGroup).After);

        var rented = LedgerCheck.Run(Base, Head(controlRent: "taken=1"), [new(LedgerCheck.ControlGroupId, "rentTransformed", "channel", "*", "*", "§9 row 7")]);
        Assert.True(rented.Passed, rented.Describe());
    }

    [Fact]
    public void Check_AReasonMustCiteASpecRowOrAnIssue()
    {
        var check = LedgerCheck.Run(Base, Head(scale: "Scale=ANIMATING"), [new("Scale.Scale.Animate", "*", "motion", "*", "*", "looks better")]);
        Assert.False(check.Passed);
        Assert.Single(check.BadReason);
    }

    [Theory]
    [InlineData("Scale.Scale.Animate", "change", "motion", "*", "Scale=JUMPED")]
    [InlineData("Scale.Scale.Animate", "mount", "motion", "*", "*")]
    [InlineData("Scale.Scale.Animate", "change", "pose", "*", "*")]
    [InlineData("Rotation.*.Animate", "change", "motion", "*", "*")]
    [InlineData("Scale.Scale.Animate", "change", "motion", "Scale=JUMPED", "*")]
    public void Check_ANearMissExpectationLeavesTheChangeUndeclared(string c, string step, string field, string before, string after)
    {
        var check = LedgerCheck.Run(Base, Head(scale: "Scale=ANIMATING"), [new(c, step, field, before, after, "fixes #1625")]);
        Assert.Equal("Scale.Scale.Animate", Assert.Single(check.Undeclared).Case);
        Assert.Single(check.Stale);
    }

    [Fact]
    public void Check_ExpectationsInheritedFromTheBaseAreIgnored_ButANewSetIsChecked()
    {
        LedgerExpectation landed = new("Scale.Opacity.Animate", "change", "motion", "*", "*", "fixes #1625");

        // An unrelated PR after a layer merged: same ledger, same expectations. Nothing to declare.
        var unrelated = LedgerCheck.Run(Base, Base, [landed], baseExpectations: [landed]);
        Assert.True(unrelated.Passed, unrelated.Describe());

        // The next layer changes the ledger but kept the old file: its change is undeclared.
        var kept = LedgerCheck.Run(Base, Head(scale: "Scale=ANIMATING"), [landed], baseExpectations: [landed]);
        Assert.Single(kept.Undeclared);

        // It appended instead of replacing: the old entry is stale.
        LedgerExpectation mine = new("Scale.Scale.Animate", "change", "motion", "*", "*", "fixes #1626");
        var appended = LedgerCheck.Run(Base, Head(scale: "Scale=ANIMATING"), [landed, mine], baseExpectations: [landed]);
        Assert.Equal(landed, Assert.Single(appended.Stale));
    }

    [Theory]
    [InlineData("*.{Opacity,Scale}.Animate", "Scale.Scale.Animate", true)]
    [InlineData("*.{Opacity,Scale}.Animate", "Scale.Rotation.Animate", false)]
    [InlineData("{None,Identity}.*.*", "Identity.None.Stagger", true)]
    [InlineData("{None,Identity}.*.*", "ScaleRotationCenter.None.None", false)]
    [InlineData("Scale.Scale.Animate", "Scale.Scale.AnimateX", false)]
    [InlineData("Scale.*", "Scale.Opacity.None", true)]
    public void Glob_MatchesWildcardsAndAlternatives(string pattern, string value, bool expected) =>
        Assert.Equal(expected, Glob.IsMatch(pattern, value));

    [Fact]
    public void KnownDifference_MatchesOnEveryAxis()
    {
        var rule = new KnownDifference("*.{Opacity,Scale}.*", "*user=None*", "change", "motion:*", "#1623", "note");
        Assert.True(rule.Matches("Scale.Scale.Animate", "wrapper=Direct user=None variant=A", "change", "motion:Scale"));
        Assert.False(rule.Matches("Scale.Scale.Animate", "wrapper=Direct user=SetFacade variant=A", "change", "motion:Scale"));
        Assert.False(rule.Matches("Scale.Rotation.Animate", "wrapper=Direct user=None variant=A", "change", "motion:Scale"));
        Assert.False(rule.Matches("Scale.Scale.Animate", "wrapper=Direct user=None variant=A", "mount", "motion:Scale"));
        Assert.False(rule.Matches("Scale.Scale.Animate", "wrapper=Direct user=None variant=A", "change", "pose"));
    }

    [Theory]
    [InlineData("""{ "expectations": [ { "case": "A", "field": "pose" } ] }""", "entry 0 needs a string \"reason\"")]
    [InlineData("""{ "expectations": [ { "case": "A", "field": "pose", "reason": "fixes #1", "step": null } ] }""", "entry 0 has a null \"step\"")]
    [InlineData("""{ "expected": [] }""", "top-level \"expectations\" array")]
    public void Expectations_AMalformedEntryNamesTheFileAndEntry(string json, string message)
    {
        var ex = Assert.Throws<FormatException>(() => LedgerExpectation.ParseFile(json));
        Assert.Contains(TransformsLedger.ExpectedFile, ex.Message, StringComparison.Ordinal);
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }
    // ── The committed files ─────────────────────────────────────────────────

    private static string BaselinePath(string file)
    {
        var root = RepoRootFinder.FindRepoRoot() ?? throw new InvalidOperationException("repo root not found");
        return Path.Join(root, TransformsLedger.BaselineDirectory, file);
    }

    [Fact]
    public void CommittedLedger_RecordsEveryCase_InCanonicalForm()
    {
        string text = File.ReadAllText(BaselinePath(TransformsLedger.LedgerFile)).ReplaceLineEndings("\n");
        var ledger = TransformsLedger.Parse(text);
        Assert.Equal(MatrixGenerator.DefaultSeed, ledger.Seed);
        foreach (var c in Matrix)
        {
            Assert.True(ledger.Cases.TryGetValue(c.Id, out var record), $"{c.Id} is missing; regenerate the ledger.");
            Assert.Equal(c.Dims, record!.Dims);
            Assert.True(record.Steps.ContainsKey("mount"), $"{c.Id} has no mount step");
        }

        foreach (var special in new[] { "@soak.short", "@soak.full", "@probe" })
            Assert.True(ledger.Cases.ContainsKey(special), $"{special} is missing; regenerate the ledger.");
        Assert.Equal(Matrix.Count + 3, ledger.Cases.Count);
        Assert.True(text == ledger.Write(), "The ledger isn't in the form the recorder writes. Regenerate it rather than editing it by hand.");
    }

    [Fact]
    public void CommittedKnownDifferences_CiteAnIssueOrSpecRow()
    {
        var known = KnownDifference.ParseFile(File.ReadAllText(BaselinePath(TransformsLedger.KnownFile)));
        Assert.All(known, k => Assert.Matches(KnownDifference.IssuePattern, k.Issue));
        Assert.All(known, k => Assert.False(string.IsNullOrWhiteSpace(k.Note), $"{k} needs a note"));
    }

    /// <summary>
    /// §12.1 C.2: diffs this branch's ledger against the one committed on the PR's base. CI's unit
    /// test job writes the base copies to <c>REACTOR_TRANSFORMS_LEDGER_BASE</c> and
    /// <c>REACTOR_TRANSFORMS_EXPECTED_BASE</c> (<c>absent</c> when the base has none, <c>skip</c>
    /// outside a pull request). Locally the base is <c>REACTOR_TRANSFORMS_BASE_REF</c> (a git ref, for
    /// a PR stacked on another layer), else the merge base with <c>origin/main</c>.
    /// </summary>
    [Fact]
    public void Ledger_ChangesFromTheBase_AreAllDeclared()
    {
        string head = File.ReadAllText(BaselinePath(TransformsLedger.LedgerFile));
        var expectations = LedgerExpectation.ParseFile(File.ReadAllText(BaselinePath(TransformsLedger.ExpectedFile)));
        var (baseLedger, baseExpected) = BaseFiles();
        if (baseLedger is null)
        {
            // The ledger is new on this branch (layer V), so no behaviour has changed yet.
            Assert.True(expectations.Count == 0, "The base has no ledger, so there is nothing to declare: empty transforms-ledger.expected.json.");
            return;
        }

        var check = LedgerCheck.Run(TransformsLedger.Parse(baseLedger), TransformsLedger.Parse(head), expectations,
            baseExpected is null ? null : LedgerExpectation.ParseFile(baseExpected));
        Assert.True(check.Passed, check.Describe());
    }

    /// <summary>The base commit's ledger and expectations; null when the base has none.</summary>
    private static (string? Ledger, string? Expected) BaseFiles()
    {
        string? ledger = Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_LEDGER_BASE");
        if (ledger == "skip") Assert.Skip("Not a pull request: there is no base ledger to diff against.");
        if (!string.IsNullOrEmpty(ledger))
        {
            string? expected = Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_EXPECTED_BASE");
            return (ledger == "absent" ? null : File.ReadAllText(ledger),
                string.IsNullOrEmpty(expected) || expected == "absent" ? null : File.ReadAllText(expected));
        }

        string root = RepoRootFinder.FindRepoRoot()!;
        string? baseRef = Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_BASE_REF") is { Length: > 0 } r ? r
            : Git(root, "merge-base HEAD origin/main")?.Trim();
        string? baseSha = baseRef is null ? null : Git(root, $"rev-parse --verify {baseRef}^{{commit}}")?.Trim();
        if (string.IsNullOrEmpty(baseSha)) Assert.Skip("No base to diff the transforms ledger against: set REACTOR_TRANSFORMS_BASE_REF or fetch origin/main.");
        if (baseSha == Git(root, "rev-parse HEAD")?.Trim()) Assert.Skip("HEAD is its own base (main), so there is no change to declare.");

        string? Show(string file)
        {
            string spec = $"{baseSha}:{TransformsLedger.BaselineDirectory}/{file}";
            if (Git(root, $"cat-file -e {spec}") is null) return null;
            return Git(root, $"show {spec}") ?? throw new InvalidOperationException($"git couldn't read {spec}.");
        }

        return (Show(TransformsLedger.LedgerFile), Show(TransformsLedger.ExpectedFile));
    }
    /// <summary>Standard output of a git command, or null when it fails.</summary>
    private static string? Git(string root, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            string output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output : null;
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
