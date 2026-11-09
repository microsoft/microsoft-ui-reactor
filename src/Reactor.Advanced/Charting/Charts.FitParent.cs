// FitParent extensions for the size-baked chart builders (issue #174).

using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Charting;

public static partial class Charts
{
    /// <summary>
    /// Sizes the pie chart to its parent: the chart is rebuilt at the parent's final
    /// width and height (see <see cref="Factories.FitParent(Func{double, double, Element}, FitParentOptions?)"/>).
    /// </summary>
    /// <remarks>
    /// Must be the <b>last</b> builder call, because it returns <see cref="Element"/>.
    /// The parent must give a finite size on both axes. Sharing one builder instance
    /// between two FitParents is unsupported.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public static Element FitParent<T>(this PieChartElement<T> chart, FitParentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Factories.FitParent((w, h) => (Element)chart.Width(w).Height(h), options);
    }

    /// <summary>
    /// Sizes the line/bar/area chart to its parent: the chart is rebuilt at the parent's
    /// final width and height (see <see cref="Factories.FitParent(Func{double, double, Element}, FitParentOptions?)"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be the <b>last</b> builder call, because it returns <see cref="Element"/>.
    /// The parent must give a finite size on both axes. Sharing one builder instance
    /// between two FitParents is unsupported.
    /// </para>
    /// <para>
    /// Set axis margins with <see cref="ChartElement{T}.Margin(double, double, double, double)"/>
    /// <b>before</b> calling this. Chained after it, <c>.Margin(l, t, r, b)</c> still compiles
    /// but binds to the generic layout margin on the returned wrapper, so the axis margins
    /// silently revert to their defaults.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public static Element FitParent<T>(this ChartElement<T> chart, FitParentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Factories.FitParent((w, h) => (Element)chart.Width(w).Height(h), options);
    }

    /// <summary>
    /// Sizes the tree chart to its parent: the chart is rebuilt at the parent's final
    /// width and height (see <see cref="Factories.FitParent(Func{double, double, Element}, FitParentOptions?)"/>).
    /// </summary>
    /// <remarks>
    /// Must be the <b>last</b> builder call, because it returns <see cref="Element"/>.
    /// The parent must give a finite size on both axes. Sharing one builder instance
    /// between two FitParents is unsupported.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public static Element FitParent<T>(this TreeChartElement<T> chart, FitParentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Factories.FitParent((w, h) => (Element)chart.Width(w).Height(h), options);
    }

    /// <summary>
    /// Sizes the force graph to its parent: the graph is rebuilt at the parent's final
    /// width and height (see <see cref="Factories.FitParent(Func{double, double, Element}, FitParentOptions?)"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be the <b>last</b> builder call, because it returns <see cref="Element"/>.
    /// The parent must give a finite size on both axes. Sharing one builder instance
    /// between two FitParents is unsupported.
    /// </para>
    /// <para>
    /// <see cref="ForceGraphElement.OnReady(Action{ForceGraphHandle})"/> runs on every
    /// render, so it can fire once per resize commit and must be idempotent. A stable
    /// builder (hoisted, <c>UseMemo</c>, or memo-skipped) keeps its simulation and
    /// re-centres free nodes by half the size change; nodes with
    /// <see cref="ForceNode.Fx"/>/<see cref="ForceNode.Fy"/> set keep their absolute
    /// position on that axis. The caller-owned input nodes are mutated in place. An inline
    /// builder re-seeds the simulation centred at the new size.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public static Element FitParent(this ForceGraphElement chart, FitParentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Factories.FitParent((w, h) => (Element)chart.Width(w).Height(h), options);
    }
}
