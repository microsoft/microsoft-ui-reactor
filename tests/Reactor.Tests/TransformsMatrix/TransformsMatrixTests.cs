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
        Assert.Equal(378, triples);
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
    [InlineData("pool", "recycled=1 diff=0", "recycled=0 diff=0", false)]
    [InlineData("motion", "Scale=ANIMATING", "Scale=JUMPED", false)]
    public void FieldEquals_AllowsTwoDipsOnGeometryOnly(string field, string a, string b, bool equal) =>
        Assert.Equal(equal, TransformsLedger.FieldEquals(field, a, b));

    [Fact]
    public void KeepUnchanged_KeepsTheCommittedValueWithinTolerance()
    {
        var committed = Ledger(("X", "mount", "pose", "120,150,220,190"), ("X", "mount", "threw", "-")).Case("X");
        var observed = Ledger(("X", "mount", "pose", "121,150,220,190"), ("X", "mount", "threw", "InvalidOperationException")).Case("X");
        TransformsLedger.KeepUnchanged(committed, observed);
        Assert.Equal("120,150,220,190", observed.Steps["mount"]["pose"]);
        Assert.Equal("InvalidOperationException", observed.Steps["mount"]["threw"]);
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

    // ── The committed files ─────────────────────────────────────────────────

    private static string BaselinePath(string file)
    {
        var root = RepoRootFinder.FindRepoRoot() ?? throw new InvalidOperationException("repo root not found");
        return Path.Combine(root, TransformsLedger.BaselineDirectory, file);
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
    /// test job writes the base copy to <c>REACTOR_TRANSFORMS_LEDGER_BASE</c> (<c>absent</c> when the base
    /// has none, <c>skip</c> outside a pull request). Locally the base is the merge base with <c>origin/main</c>.
    /// </summary>
    [Fact]
    public void Ledger_ChangesFromTheBase_AreAllDeclared()
    {
        string head = File.ReadAllText(BaselinePath(TransformsLedger.LedgerFile));
        var expectations = LedgerExpectation.ParseFile(File.ReadAllText(BaselinePath(TransformsLedger.ExpectedFile)));
        string? baseText = BaseLedger();
        if (baseText is null)
        {
            // The ledger is new on this branch (layer V), so no behaviour has changed yet.
            Assert.True(expectations.Count == 0, "The base has no ledger, so there is nothing to declare: empty transforms-ledger.expected.json.");
            return;
        }

        var check = LedgerCheck.Run(TransformsLedger.Parse(baseText), TransformsLedger.Parse(head), expectations);
        Assert.True(check.Passed, check.Describe());
    }

    private static string? BaseLedger()
    {
        string? env = Environment.GetEnvironmentVariable("REACTOR_TRANSFORMS_LEDGER_BASE");
        if (env == "absent") return null;
        if (env == "skip") Assert.Skip("Not a pull request: there is no base ledger to diff against.");
        if (!string.IsNullOrEmpty(env)) return File.ReadAllText(env);

        string root = RepoRootFinder.FindRepoRoot()!;
        string? mergeBase = Git(root, "merge-base HEAD origin/main")?.Trim();
        if (string.IsNullOrEmpty(mergeBase)) Assert.Skip("No origin/main to diff the transforms ledger against.");
        // Null when the merge base has no ledger yet.
        return Git(root, $"show {mergeBase}:{TransformsLedger.BaselineDirectory}/{TransformsLedger.LedgerFile}");
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
