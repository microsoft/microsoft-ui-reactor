// Pure managed: linked into Reactor.Tests, so nothing here may touch WinUI.
namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;

// Spec 066 §12.1 B. Each enum is one matrix dimension, in ledger order. Adding a value appends
// cases (and changes pairwise assignments, which shows up in the ledger's `dims` lines). Layer 5
// adds TransformOrigin to TransformAxis.
internal enum TransformAxis { None, Identity, Scale, Rotation, Translation, CenterPoint, ScaleRotationCenter }

internal enum TransitionAxis { None, Opacity, Scale, Rotation, Translation, Background }

internal enum FeatureAxis { None, Animate, WithAnimation, InteractionStates, TransitionEnter, TransitionExit, Keyframes, LayoutAnimation, Stagger }

internal enum WrapperAxis { Direct, Memo, Decorator }

internal enum FlowAxis { Ltr, Rtl }

internal enum MotionAxis { Enabled, Disabled }

internal enum UserCodeAxis { None, SetFacade, TranslationDrag }

/// <summary>
/// Sub-variant for the dimensions the spec counts once. <c>Keyframes</c>: A is one-shot, B loops.
/// <c>TranslationTransition</c>: A animates every axis, B sets <c>Components = X</c>.
/// </summary>
internal enum VariantAxis { A, B }

internal enum MatrixStep { Mount, Change, RemoveFeature, AddFeature, RemoveModifier, Unmount, RentPlain, RentTransformed }

internal sealed record MatrixCase(
    TransformAxis Transform,
    TransitionAxis Transition,
    FeatureAxis Feature,
    WrapperAxis Wrapper,
    FlowAxis Flow,
    MotionAxis Motion,
    UserCodeAxis UserCode,
    VariantAxis Variant)
{
    /// <summary>Stable id: the triple alone identifies a case, so pairwise reassignments never rename one.</summary>
    public string Id => $"{Transform}.{Transition}.{Feature}";

    public string Dims => $"wrapper={Wrapper} flow={Flow} motion={Motion} user={UserCode} variant={Variant}";

    /// <summary>
    /// §12.1 C.3: no transform, transition or compositor feature. Its fields must stay byte-identical
    /// across every layer, except the rent-as-transformed step, which layer 4 changes on purpose.
    /// </summary>
    public bool IsControlGroup => Transform == TransformAxis.None && Transition == TransitionAxis.None && Feature == FeatureAxis.None;

    public static string StepName(MatrixStep step) => step switch
    {
        MatrixStep.Mount => "mount",
        MatrixStep.Change => "change",
        MatrixStep.RemoveFeature => "removeFeature",
        MatrixStep.AddFeature => "addFeature",
        MatrixStep.RemoveModifier => "removeModifier",
        MatrixStep.Unmount => "unmount",
        MatrixStep.RentPlain => "rentPlain",
        _ => "rentTransformed",
    };
}

/// <summary>
/// Builds the §12.1 B matrix: every Transform × Transition × Feature triple, with the remaining
/// dimensions assigned so that every pair of values across all eight dimensions occurs in at least
/// one case. Deterministic for a seed.
/// </summary>
internal static class MatrixGenerator
{
    public const int DefaultSeed = 1630;

    private static readonly int[] Cardinalities =
    [
        Enum.GetValues<TransformAxis>().Length,
        Enum.GetValues<TransitionAxis>().Length,
        Enum.GetValues<FeatureAxis>().Length,
        Enum.GetValues<WrapperAxis>().Length,
        Enum.GetValues<FlowAxis>().Length,
        Enum.GetValues<MotionAxis>().Length,
        Enum.GetValues<UserCodeAxis>().Length,
        Enum.GetValues<VariantAxis>().Length,
    ];

    private const int TripleDims = 3;

    public static int DimensionCount => Cardinalities.Length;

    public static IReadOnlyList<int> Cardinality => Cardinalities;

    public static IReadOnlyList<MatrixCase> Generate(int seed = DefaultSeed)
    {
        var random = new Random(seed);
        var combos = PairwiseCombos();
        var covered = new HashSet<(int, int, int, int)>();
        var cases = new List<MatrixCase>();

        foreach (var transform in Enum.GetValues<TransformAxis>())
            foreach (var transition in Enum.GetValues<TransitionAxis>())
                foreach (var feature in Enum.GetValues<FeatureAxis>())
                {
                    int[] triple = [(int)transform, (int)transition, (int)feature];
                    // The control group keeps every other dimension at its default too (no user code, LTR,
                    // direct), so it stays the baseline no layer may change. Every other case: seeded
                    // shuffle, then greedy, taking the assignment that covers the most new pairs.
                    int[] best = new int[Cardinalities.Length - TripleDims];
                    if (triple.Any(static v => v != 0))
                    {
                        var order = combos.OrderBy(_ => random.Next()).ToArray();
                        int bestGain = -1;
                        foreach (var combo in order)
                        {
                            int gain = Pairs(Concat(triple, combo)).Count(p => !covered.Contains(p));
                            if (gain > bestGain) { best = combo; bestGain = gain; }
                        }
                    }

                    var row = Concat(triple, best);
                    foreach (var pair in Pairs(row)) covered.Add(pair);
                    cases.Add(new MatrixCase(
                        transform, transition, feature,
                        (WrapperAxis)row[3], (FlowAxis)row[4], (MotionAxis)row[5], (UserCodeAxis)row[6], (VariantAxis)row[7]));
                }

        return cases;
    }

    /// <summary>
    /// The fixed sample that every PR's normal selftest run checks. It starts with the control group
    /// and then greedily adds the case that covers the most still-unseen single values, until every
    /// value of every dimension appears at least once. Ties go to the lowest index, which keeps the
    /// sample stable for a given matrix.
    /// </summary>
    public static IReadOnlyList<MatrixCase> Sample(IReadOnlyList<MatrixCase> all)
    {
        var picked = new List<MatrixCase> { all.Single(c => c.IsControlGroup) };
        var seen = new HashSet<(int Dim, int Value)>(Values(picked[0]));
        int values = Cardinalities.Sum();
        while (seen.Count < values)
        {
            var next = all.Where(c => !picked.Contains(c))
                .Select((c, i) => (Case: c, Index: i, Gain: Values(c).Count(v => !seen.Contains(v))))
                .OrderByDescending(x => x.Gain).ThenBy(x => x.Index).First().Case;
            picked.Add(next);
            foreach (var v in Values(next)) seen.Add(v);
        }

        return picked;
    }

    public static int[] Row(MatrixCase c) =>
        [(int)c.Transform, (int)c.Transition, (int)c.Feature, (int)c.Wrapper, (int)c.Flow, (int)c.Motion, (int)c.UserCode, (int)c.Variant];

    /// <summary>Every (dimension, value, dimension, value) pair a row covers.</summary>
    public static IEnumerable<(int, int, int, int)> Pairs(int[] row)
    {
        for (int i = 0; i < row.Length; i++)
            for (int j = i + 1; j < row.Length; j++)
                yield return (i, row[i], j, row[j]);
    }

    private static IEnumerable<(int Dim, int Value)> Values(MatrixCase c) => Row(c).Select((v, d) => (d, v));

    private static int[] Concat(int[] a, int[] b) => [.. a, .. b];

    private static List<int[]> PairwiseCombos()
    {
        var combos = new List<int[]> { Array.Empty<int>() };
        for (int d = TripleDims; d < Cardinalities.Length; d++)
            combos = combos.SelectMany(prefix => Enumerable.Range(0, Cardinalities[d]).Select(v => (int[])[.. prefix, v])).ToList();
        return combos;
    }
}
