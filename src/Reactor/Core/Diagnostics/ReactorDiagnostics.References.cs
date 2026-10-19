using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// One reference a control declares to another control — resolved, or still pending because the
/// target has not mounted.
/// </summary>
/// <param name="Property">The referencing property: <c>"LabeledBy"</c>, <c>"DescribedBy"</c>,
/// <c>"FlowsTo"</c>, <c>"FlowsFrom"</c>, <c>"XYFocusUp"</c>, <c>"XYFocusDown"</c>,
/// <c>"XYFocusLeft"</c>, <c>"XYFocusRight"</c> or <c>"ToolTipPlacementTarget"</c>. A control
/// descriptor's or binding's reference carries no author-visible name and reports
/// <c>"reference#N"</c> / <c>"binding#N"</c>.</param>
/// <param name="IsList">True for a list-valued reference (<c>DescribedBy</c>, <c>FlowsTo</c>,
/// <c>FlowsFrom</c>, a descriptor's <c>ReferenceList</c>); each target is its own entry.</param>
/// <param name="Index">Position of this target within a list-valued reference (0 for a scalar one),
/// counting references only: a null entry in the authored list is not a reference and is skipped.</param>
/// <param name="TargetAutomationId">The AutomationId the author passed to <c>.LabeledBy("id")</c>;
/// <c>null</c> for an <c>ElementRef</c> reference.</param>
/// <param name="ExpectedTargetTypeName">For a typed <c>ElementRef&lt;T&gt;</c>, <c>T</c> as C# spells
/// it (<c>Button</c>); otherwise <c>null</c>.</param>
/// <param name="IsResolved">True when the target is mounted.</param>
/// <param name="Target">The mounted control the reference resolves to; <c>null</c> while pending.
/// Unlike the rest of the diagnostics surface this is a live WinUI control, not text — use it
/// in-process, on the UI thread, to locate the target (e.g. to inspect or highlight it).</param>
public sealed record ReferenceEdgeSnapshot(
    string Property,
    bool IsList,
    int Index,
    string? TargetAutomationId,
    string? ExpectedTargetTypeName,
    bool IsResolved,
    UIElement? Target);

public static partial class ReactorDiagnostics
{
    /// <summary>
    /// The references <paramref name="control"/> declares to other controls — including pending
    /// ones whose target has not mounted yet, for which the WinUI property itself (e.g.
    /// <c>XYFocusRight</c>, <c>AutomationProperties.LabeledBy</c>) still reads null. Grouped by
    /// property in a fixed order (control-descriptor references, then bindings, then the
    /// modifier references), then list position; both <c>LabeledBy</c> forms are adjacent, the
    /// <c>ElementRef</c> one first.
    /// </summary>
    /// <remarks>
    /// <c>ElementRef</c> references (modifiers such as <c>.LabeledBy(ref)</c> or
    /// <c>.XYFocusRight(ref)</c>, control descriptors and bindings) are always reported. An
    /// AutomationId <c>.LabeledBy("id")</c> is reported while pending; once resolved it is reported
    /// only while the control is loaded and tagged with its element (turn source mapping on). Reads existing
    /// reconciler state on demand. Must be called on the UI thread.
    /// </remarks>
    [Microsoft.UI.Reactor.Hosting.UIThreadOnly]
    public static IReadOnlyList<ReferenceEdgeSnapshot> GetReferenceEdges(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        EnsureUIThread(control);

        Reconciler.ReactorState? state = null;
        if (control is FrameworkElement fe) Reconciler.TryGetReactorState(fe, out state);
        var bag = state?.ReferenceEdges;
        var pendingLabeledBy = state?.PendingLabeledBy;

        // The resolved AutomationId form leaves no bookkeeping behind, so recover what the
        // author wrote from the element tag (present when tagged) and confirm the live
        // LabeledBy is the control it names rather than one an ElementRef edge wrote.
        string? authoredLabeledBy = null;
        UIElement? resolvedLabeledBy = null;
        if (pendingLabeledBy is null
            // An unmounted (unpooled) control keeps its tag, but its reference state was torn
            // down; only a control still in the live tree has an authored edge to recover.
            && control is FrameworkElement { IsLoaded: true }
            && Reconciler.GetElementTag(control) is { } tag
            && Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorChain(tag) is var chain
            && !Microsoft.UI.Reactor.Core.V1Protocol.OverlayLifecycle.IsAttributionOnlyTag(control, chain[^1])
            && ReferenceEdgeMap.EffectiveLabeledById(chain) is { } id)
        {
            authoredLabeledBy = id;
            // The live property can outlive its target: an unmounted label stays referenced until
            // something rewrites LabeledBy. Only a target still loaded in the control's own tree counts.
            if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetLabeledBy(control) is FrameworkElement { IsLoaded: true } live
                && ReferenceEquals(live.XamlRoot, control.XamlRoot)
                && string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(live), id, StringComparison.Ordinal))
                resolvedLabeledBy = live;
        }

        return ReferenceEdgeMap.Describe(bag, pendingLabeledBy, authoredLabeledBy, resolvedLabeledBy);
    }
}

/// <summary>
/// Pure projection of the reconciler's reference bookkeeping onto
/// <see cref="ReferenceEdgeSnapshot"/>; split from <see cref="ReactorDiagnostics.GetReferenceEdges"/>
/// so it is testable without a live control.
/// </summary>
internal static class ReferenceEdgeMap
{
    /// <summary>
    /// The AutomationId <c>.LabeledBy("id")</c> in effect across a decorator chain (outermost
    /// first): decorators apply their modifiers after their target's, so the outermost level that
    /// sets one wins.
    /// </summary>
    internal static string? EffectiveLabeledById(IReadOnlyList<Element> chain)
    {
        foreach (var element in chain)
            if (element.Modifiers?.Accessibility?.LabeledBy is { } id) return id;
        return null;
    }

    internal static IReadOnlyList<ReferenceEdgeSnapshot> Describe(
        ReferenceEdgeBag? bag,
        string? pendingLabeledBy,
        string? authoredLabeledBy,
        UIElement? resolvedLabeledBy)
    {
        List<(int Slot, ReferenceEdgeSnapshot Edge)>? result = null;

        if (bag is not null)
        {
            foreach (var (slot, edge) in bag.Edges)
            {
                // A null cell is an unset / torn-down slot, not an active reference.
                if (edge.Cell is not { } cell) continue;
                (result ??= new()).Add((slot, ForCell(slot, isList: false, index: 0, cell)));
            }

            foreach (var (slot, listEdge) in bag.ListEdges)
            {
                // The authored list, not the deduplicated subscription set, so order and repeats
                // match what the author wrote; Index is the position among its references.
                var authored = listEdge.Authored;
                if (authored is null) continue;
                for (int i = 0; i < authored.Count; i++)
                    (result ??= new()).Add((slot, ForCell(slot, isList: true, index: i, authored[i])));
            }
        }

        if ((pendingLabeledBy ?? authoredLabeledBy) is { } automationId)
        {
            var target = pendingLabeledBy is null ? resolvedLabeledBy : null;
            (result ??= new()).Add((ReferenceSlots.ModifierRef_LabeledById, new ReferenceEdgeSnapshot(
                "LabeledBy",
                IsList: false,
                Index: 0,
                TargetAutomationId: automationId,
                ExpectedTargetTypeName: null,
                IsResolved: target is not null,
                Target: target)));
        }

        if (result is null) return global::System.Array.Empty<ReferenceEdgeSnapshot>();
        // Group by public property: the AutomationId LabeledBy pseudo-slot sorts with the
        // ElementRef LabeledBy slot (after it), so no other property lands between them.
        static (int Group, int Sub) Key(int slot) => slot == ReferenceSlots.ModifierRef_LabeledById
            ? (ReferenceSlots.ModifierRef_LabeledBy, 1)
            : (slot, 0);
        result.Sort(static (a, b) =>
        {
            var (ka, kb) = (Key(a.Slot), Key(b.Slot));
            if (ka.Group != kb.Group) return ka.Group.CompareTo(kb.Group);
            if (ka.Sub != kb.Sub) return ka.Sub.CompareTo(kb.Sub);
            return a.Edge.Index.CompareTo(b.Edge.Index);
        });
        var edges = new ReferenceEdgeSnapshot[result.Count];
        for (int i = 0; i < edges.Length; i++) edges[i] = result[i].Edge;
        return edges;
    }

    private static ReferenceEdgeSnapshot ForCell(int slot, bool isList, int index, ElementRef cell)
    {
        var target = cell.Current;
        return new ReferenceEdgeSnapshot(
            ReferenceSlots.Label(slot),
            isList,
            index,
            TargetAutomationId: null,
            cell.ExpectedType is { } t ? DiagnosticText.FriendlyTypeName(t) : null,
            IsResolved: target is not null,
            target);
    }
}
