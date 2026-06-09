using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Spec 057 §11 Phase 3 (3.1) — the reference-graph overlay. Reactor reference
/// properties (<c>descriptor.Reference</c>/<c>.ReferenceList</c>, the
/// <c>binding.Reference</c> bridge, and the modifier-level edges such as
/// <c>.LabeledBy</c>/<c>.XYFocusDown</c>) form a reactive overlay on top of the
/// visual tree (spec §3.1). This walks the per-control reference-edge bags the
/// reconciler maintains (<see cref="Reconciler.ReactorState.ReferenceEdges"/>) and
/// emits the edges plus cycle / unresolved diagnostics for the element-tree
/// inspector. Cycles are a tested, supported topology (spec §3.3, §9) — they are
/// reported as informational, not errors.
/// </summary>
internal static class ReferenceOverlay
{
    public const string SchemaVersion = "reactor-references/1";

    /// <summary>
    /// Builds the reference overlay for the elements the supplied <paramref name="walker"/>
    /// last visited. Must be called on the UI dispatcher (reads attached DPs) and after
    /// <see cref="TreeWalker.Walk"/> so the element→id map is populated.
    /// </summary>
    public static ReferenceGraphResult Build(TreeWalker walker, string? windowId)
    {
        var ids = walker.ElementIds;
        var edges = new List<ReferenceEdgeInfo>();

        foreach (var fe in walker.WalkedElements)
        {
            if (!ids.TryGetValue(fe, out var fromId))
                continue;
            if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState state
                || state.ReferenceEdges is not ReferenceEdgeBag bag)
                continue;

            foreach (var (slot, edge) in bag.Edges)
            {
                // A null cell is a torn-down / never-set slot, not an active reference.
                if (edge.Cell is null)
                    continue;
                edges.Add(MakeEdge(fromId, slot, "scalar", edge.Cell.Current, ids));
            }

            foreach (var (slot, listEdge) in bag.ListEdges)
            {
                foreach (var cell in listEdge.Cells)
                    edges.Add(MakeEdge(fromId, slot, "list", cell.Current, ids));
            }
        }

        var diagnostics = BuildDiagnostics(edges);

        return new ReferenceGraphResult
        {
            Schema = SchemaVersion,
            WindowId = windowId,
            Edges = edges,
            Diagnostics = diagnostics,
        };
    }

    private static ReferenceEdgeInfo MakeEdge(
        string fromId,
        int slot,
        string kind,
        FrameworkElement? target,
        IReadOnlyDictionary<FrameworkElement, string> ids)
    {
        string? toId = null;
        var outOfTree = false;
        if (target is not null)
        {
            if (ids.TryGetValue(target, out var resolvedId))
                toId = resolvedId;
            else
                outOfTree = true; // resolved, but the target lives outside the walked scope
        }

        return new ReferenceEdgeInfo
        {
            From = fromId,
            To = toId,
            Label = LabelForSlot(slot),
            Slot = slot,
            Kind = kind,
            Resolved = target is not null,
            OutOfTree = outOfTree ? true : null,
        };
    }

    /// <summary>
    /// Maps a reference-entry slot to a human-readable label. Modifier-level edges
    /// have stable, named slots (<see cref="ReferenceSlots"/>); descriptor and
    /// imperative-binding edges allocate ascending slots that carry no author name,
    /// so they fall back to a kind-tagged generic label.
    /// </summary>
    internal static string LabelForSlot(int slot) => slot switch
    {
        ReferenceSlots.ModifierRef_LabeledBy => "LabeledBy",
        ReferenceSlots.ModifierRef_DescribedBy => "DescribedBy",
        ReferenceSlots.ModifierRef_FlowsTo => "FlowsTo",
        ReferenceSlots.ModifierRef_FlowsFrom => "FlowsFrom",
        ReferenceSlots.ModifierRef_XYFocusUp => "XYFocusUp",
        ReferenceSlots.ModifierRef_XYFocusDown => "XYFocusDown",
        ReferenceSlots.ModifierRef_XYFocusLeft => "XYFocusLeft",
        ReferenceSlots.ModifierRef_XYFocusRight => "XYFocusRight",
        >= 200_000 => $"modifier#{slot}",
        >= 100_000 => $"binding#{slot - 100_000}",
        _ => $"reference#{slot}",
    };

    internal static List<ReferenceDiagnostic> BuildDiagnostics(List<ReferenceEdgeInfo> edges)
    {
        var diagnostics = new List<ReferenceDiagnostic>();

        // Unresolved: a declared reference whose cell is currently null. Legitimate
        // transiently (target not yet mounted), but a perpetually-null ref is a likely
        // author bug, so surface it (spec §11 Phase 3, §12 Q open-question note).
        foreach (var edge in edges)
        {
            if (!edge.Resolved)
            {
                diagnostics.Add(new ReferenceDiagnostic
                {
                    Kind = "unresolved",
                    Message = $"Reference '{edge.Label}' on {edge.From} is unresolved (target not mounted).",
                    NodeIds = new List<string> { edge.From },
                });
            }
        }

        // Cycles: built only from resolved, in-scope edges. Reported informationally —
        // push resolution converges on cycles by design (spec §3.3).
        foreach (var cycle in FindCycles(edges))
        {
            diagnostics.Add(new ReferenceDiagnostic
            {
                Kind = "cycle",
                Message = "Reference cycle: " + string.Join(" → ", cycle.Append(cycle[0])),
                NodeIds = cycle,
            });
        }

        return diagnostics;
    }

    /// <summary>
    /// Detects directed cycles over the resolved, in-scope reference edges via DFS
    /// back-edge detection. Returns each cycle once as the ordered ring of node ids.
    /// </summary>
    private static List<List<string>> FindCycles(List<ReferenceEdgeInfo> edges)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var edge in edges)
        {
            if (edge.To is null)
                continue; // unresolved or out-of-tree — not a graph edge
            if (!adjacency.TryGetValue(edge.From, out var list))
                adjacency[edge.From] = list = new List<string>();
            if (!list.Contains(edge.To))
                list.Add(edge.To);
        }

        var cycles = new List<List<string>>();
        var seenCycleKeys = new HashSet<string>();
        var state = new Dictionary<string, int>(); // 0=white,1=gray,2=black
        var stack = new List<string>();

        void Visit(string node)
        {
            state[node] = 1;
            stack.Add(node);

            if (adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    var color = state.TryGetValue(next, out var c) ? c : 0;
                    if (color == 0)
                    {
                        Visit(next);
                    }
                    else if (color == 1)
                    {
                        // Back-edge: reconstruct the ring from `next` to the top of the stack.
                        var start = stack.LastIndexOf(next);
                        if (start >= 0)
                        {
                            var ring = stack.GetRange(start, stack.Count - start);
                            var key = CanonicalCycleKey(ring);
                            if (seenCycleKeys.Add(key))
                                cycles.Add(ring);
                        }
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
        }

        foreach (var node in adjacency.Keys)
        {
            if (!state.ContainsKey(node))
                Visit(node);
        }

        return cycles;
    }

    // Rotation-invariant key so the same ring discovered from different entry points
    // (e.g. A→B→A vs B→A→B) is reported once.
    private static string CanonicalCycleKey(List<string> ring)
    {
        var n = ring.Count;
        var minIndex = 0;
        for (var i = 1; i < n; i++)
        {
            if (string.CompareOrdinal(ring[i], ring[minIndex]) < 0)
                minIndex = i;
        }

        var rotated = new List<string>(n);
        for (var i = 0; i < n; i++)
            rotated.Add(ring[(minIndex + i) % n]);
        return string.Join("\u0001", rotated);
    }
}

/// <summary>Result payload emitted by the <c>references</c> MCP tool (spec 057 §11 Phase 3).</summary>
internal sealed class ReferenceGraphResult
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = ReferenceOverlay.SchemaVersion;

    public string? WindowId { get; set; }
    public List<ReferenceEdgeInfo> Edges { get; set; } = new();
    public List<ReferenceDiagnostic> Diagnostics { get; set; } = new();
}

/// <summary>A single reference edge from a referrer node to its target node.</summary>
internal sealed class ReferenceEdgeInfo
{
    /// <summary>Node id of the referrer (the control that declares the reference).</summary>
    public string From { get; set; } = "";

    /// <summary>Node id of the resolved target, or null when unresolved / out of scope.</summary>
    public string? To { get; set; }

    /// <summary>Friendly label for the reference (e.g. <c>LabeledBy</c>, <c>reference#0</c>).</summary>
    public string Label { get; set; } = "";

    /// <summary>Reference-entry slot index (descriptor 0+, binding 100000+, modifier 200000+).</summary>
    public int Slot { get; set; }

    /// <summary><c>scalar</c> for a single-valued reference, <c>list</c> for a list-valued one.</summary>
    public string Kind { get; set; } = "scalar";

    /// <summary>True when the referenced cell currently points at a mounted control.</summary>
    public bool Resolved { get; set; }

    /// <summary>Set when the target is resolved but lives outside the walked subtree.</summary>
    public bool? OutOfTree { get; set; }
}

/// <summary>A reference-overlay diagnostic: an unresolved reference or a cycle.</summary>
internal sealed class ReferenceDiagnostic
{
    /// <summary><c>unresolved</c> or <c>cycle</c>.</summary>
    public string Kind { get; set; } = "";

    public string Message { get; set; } = "";

    /// <summary>Node ids the diagnostic concerns (the referrer, or the cycle ring).</summary>
    public List<string> NodeIds { get; set; } = new();
}
