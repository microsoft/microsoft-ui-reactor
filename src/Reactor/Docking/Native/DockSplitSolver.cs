namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.1 — split / size constraint solver.
//
//  Pure math, no UI thread. Takes a list of children with current ratios,
//  per-child min/max in DIPs, the total available DIPs along the split
//  axis, and a delta nudged onto a given splitter (= the gap between
//  children at index `splitterIndex` and `splitterIndex + 1`). Returns
//  new ratios that respect:
//    1. each child's min / max
//    2. sum(ratios) == 1.0 (so flex-grow renders correctly)
//    3. ratios that already round to zero stay zero (preserved-collapse)
//
//  WinUI.Dock's upstream LayoutPanel solves this implicitly via Grid's
//  GridSplitter; we lift it out as a testable function so the renderer
//  stays a thin dispatch layer.
// ════════════════════════════════════════════════════════════════════════

/// <summary>One child's size profile along the split axis.</summary>
internal readonly record struct DockSplitChild(double Ratio, double MinDip, double MaxDip);

/// <summary>Resolved ratios after applying a splitter delta; <see cref="ConsumedDip"/> tells the caller how much of the delta was actually applied.</summary>
internal readonly record struct DockSplitSolution(double[] Ratios, double ConsumedDip);

internal static class DockSplitSolver
{
    /// <summary>
    /// Apply a positive/negative delta to the splitter between
    /// <paramref name="splitterIndex"/> and <paramref name="splitterIndex"/>+1.
    /// </summary>
    /// <param name="children">Per-child profile. Length must be ≥ 2.</param>
    /// <param name="splitterIndex">Index of the splitter (0-based, &lt; children.Length - 1).</param>
    /// <param name="deltaDip">Pointer delta in DIPs (positive: grow the trailing child).</param>
    /// <param name="totalDip">Total available DIPs along the axis (excluding splitter handles).</param>
    public static DockSplitSolution ApplyDelta(
        DockSplitChild[] children,
        int splitterIndex,
        double deltaDip,
        double totalDip)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Length < 2)
            throw new ArgumentException("Split must have at least two children.", nameof(children));
        if (splitterIndex < 0 || splitterIndex >= children.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(splitterIndex));
        if (!(totalDip > 0))
            throw new ArgumentOutOfRangeException(nameof(totalDip), "totalDip must be positive.");

        // Convert ratios → DIPs for resolution; ratios are normalized so
        // sum is 1.0 even if input drifted slightly.
        var n = children.Length;
        var ratios = new double[n];
        var sizes = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            ratios[i] = Math.Max(0, children[i].Ratio);
            sum += ratios[i];
        }
        if (sum <= 0)
        {
            // Equal share fallback
            for (int i = 0; i < n; i++) ratios[i] = 1.0 / n;
            sum = 1.0;
        }
        for (int i = 0; i < n; i++)
            sizes[i] = ratios[i] / sum * totalDip;

        // Bias the leading child's DIP delta, then clamp.
        // delta > 0 grows trailing (index splitterIndex + 1) by stealing from leading.
        // delta < 0 grows leading by stealing from trailing.
        // Standard splitter semantics: the leading child *loses* `delta` DIPs.
        int leadingIx = splitterIndex;
        int trailingIx = splitterIndex + 1;

        var leadingMin = Math.Max(0, children[leadingIx].MinDip);
        var leadingMax = Math.Max(leadingMin, children[leadingIx].MaxDip);
        var trailingMin = Math.Max(0, children[trailingIx].MinDip);
        var trailingMax = Math.Max(trailingMin, children[trailingIx].MaxDip);

        // The pair of children shares a fixed total (`pair`); the rest of
        // the panel is untouched. The solver therefore reduces to: pick a
        // newLeading value within the intersection of
        //   [leadingMin, leadingMax]            (this child's own bounds)
        //   [pair - trailingMax, pair - trailingMin]  (the other side's bounds,
        //                                              translated through `pair`)
        // and clamp the user's proposal against that interval. newTrailing
        // is then just `pair - newLeading`.
        double pair = sizes[leadingIx] + sizes[trailingIx];
        double lo = Math.Max(leadingMin, pair - trailingMax);
        double hi = Math.Min(leadingMax, pair - trailingMin);
        if (hi < lo) hi = lo; // contradictory bounds — collapse to the floor.

        double proposed = sizes[leadingIx] - deltaDip;
        double newLeading = Math.Clamp(proposed, lo, hi);
        double newTrailing = pair - newLeading;

        var consumed = sizes[leadingIx] - newLeading; // signed
        sizes[leadingIx] = newLeading;
        sizes[trailingIx] = newTrailing;

        // Normalize back to ratios with sum exactly 1.0
        double newSum = 0;
        for (int i = 0; i < n; i++) newSum += sizes[i];
        var outRatios = new double[n];
        if (newSum > 0)
        {
            for (int i = 0; i < n; i++) outRatios[i] = sizes[i] / newSum;
        }
        else
        {
            for (int i = 0; i < n; i++) outRatios[i] = 1.0 / n;
        }

        // Fix tiny FP drift: bump last element so sum == 1.0 exactly.
        double tally = 0;
        for (int i = 0; i < n - 1; i++) tally += outRatios[i];
        outRatios[n - 1] = 1.0 - tally;
        if (outRatios[n - 1] < 0) outRatios[n - 1] = 0;

        return new DockSplitSolution(outRatios, consumed);
    }

    /// <summary>
    /// Normalize a raw ratio set so that values sum to 1.0 and negatives
    /// become zero. Used when reading from persisted JSON before applying.
    /// </summary>
    public static double[] Normalize(IReadOnlyList<double> rawRatios)
    {
        ArgumentNullException.ThrowIfNull(rawRatios);
        if (rawRatios.Count == 0) return [];
        var n = rawRatios.Count;
        var result = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var v = rawRatios[i];
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) v = 0;
            result[i] = v;
            sum += v;
        }
        if (sum <= 0)
        {
            for (int i = 0; i < n; i++) result[i] = 1.0 / n;
            return result;
        }
        for (int i = 0; i < n; i++) result[i] /= sum;
        double tally = 0;
        for (int i = 0; i < n - 1; i++) tally += result[i];
        result[n - 1] = 1.0 - tally;
        if (result[n - 1] < 0) result[n - 1] = 0;
        return result;
    }

    /// <summary>
    /// Compute equal-share starting ratios for <paramref name="count"/> children.
    /// </summary>
    public static double[] EqualShare(int count)
    {
        if (count <= 0) return [];
        var result = new double[count];
        for (int i = 0; i < count; i++) result[i] = 1.0 / count;
        return result;
    }
}
