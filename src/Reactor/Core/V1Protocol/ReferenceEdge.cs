using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core
{
    internal sealed class ReferenceEdgeBag
    {
        public readonly Dictionary<int, ReferenceEdge> Edges = new();
        public readonly Dictionary<int, ReferenceListEdge> ListEdges = new();
    }

    internal sealed class ReferenceEdge
    {
        public ElementRef? Cell;
        public Action<FrameworkElement?>? Handler;
        /// <summary>
        /// The target-property writer for this edge (e.g. set XYFocusRight / LabeledBy).
        /// Retained so teardown can clear the property — <c>Apply(ctrl, null)</c> — and not
        /// leave a stale relationship on a held or pooled control (spec 057 CR-002).
        /// </summary>
        public Action<FrameworkElement, FrameworkElement?>? Apply;
    }

    internal sealed class ReferenceListEdge
    {
        public readonly List<ElementRef> Cells = new();
        /// <summary>
        /// The list exactly as last authored (order, duplicates and nulls kept). <see cref="Cells"/>
        /// is deduplicated subscription bookkeeping; diagnostics project this instead.
        /// </summary>
        public IReadOnlyList<ElementRef?>? Authored;
        public Action<FrameworkElement?>? Handler;
        public Action<FrameworkElement>? Recompute;
        /// <summary>
        /// Empties the target list on teardown so a held/pooled control doesn't retain
        /// stale relationship entries (spec 057 CR-002).
        /// </summary>
        public Action<FrameworkElement>? Clear;
    }

    internal static class ReferenceSlots
    {
        public const int ModifierRef_LabeledBy = 200_000;
        public const int ModifierRef_DescribedBy = 200_001;
        public const int ModifierRef_FlowsTo = 200_002;
        public const int ModifierRef_FlowsFrom = 200_003;
        public const int ModifierRef_XYFocusUp = 200_010;
        public const int ModifierRef_XYFocusDown = 200_011;
        public const int ModifierRef_XYFocusLeft = 200_012;
        public const int ModifierRef_XYFocusRight = 200_013;
        public const int ModifierRef_ToolTipPlacementTarget = 200_020;

        /// <summary>
        /// Diagnostics-only pseudo-slot for the AutomationId form of <c>.LabeledBy("id")</c>,
        /// which resolves through a visual-tree search rather than a reference edge and so has
        /// no bag entry. Never used as a <see cref="ReferenceEdgeBag"/> key.
        /// </summary>
        public const int ModifierRef_LabeledById = 200_030;

        /// <summary>First slot of the imperative <c>ReactorBinding.Reference</c> bridge.</summary>
        public const int BindingBase = 100_000;

        /// <summary>First slot of the named modifier-level edges above.</summary>
        public const int ModifierBase = 200_000;

        /// <summary>
        /// Human-readable name for a slot. Modifier slots are named; descriptor and binding
        /// slots are allocated in declaration order and carry no author-visible name. Shared by
        /// <c>ReactorDiagnostics.GetReferenceEdges</c> and the devtools reference overlay.
        /// </summary>
        public static string Label(int slot) => slot switch
        {
            ModifierRef_LabeledBy or ModifierRef_LabeledById => "LabeledBy",
            ModifierRef_DescribedBy => "DescribedBy",
            ModifierRef_FlowsTo => "FlowsTo",
            ModifierRef_FlowsFrom => "FlowsFrom",
            ModifierRef_XYFocusUp => "XYFocusUp",
            ModifierRef_XYFocusDown => "XYFocusDown",
            ModifierRef_XYFocusLeft => "XYFocusLeft",
            ModifierRef_XYFocusRight => "XYFocusRight",
            ModifierRef_ToolTipPlacementTarget => "ToolTipPlacementTarget",
            >= ModifierBase => $"modifier#{slot}",
            >= BindingBase => $"binding#{slot - BindingBase}",
            _ => $"reference#{slot}",
        };
    }
}

namespace Microsoft.UI.Reactor.Core.V1Protocol
{
    internal static class ReferenceDirtySet
    {
        [ThreadStatic]
        private static HashSet<ElementRef>? s_dirty;

        [ThreadStatic]
        private static int s_depth;

        internal static void BeginCommit() => s_depth++;

        internal static bool TryEnqueue(ElementRef cell)
        {
            if (s_depth == 0) return false;
            (s_dirty ??= new()).Add(cell);
            return true;
        }

        internal static void EndCommitAndFlush()
        {
            if (--s_depth > 0) return;

            var set = s_dirty;
            if (set is null || set.Count == 0) return;

            int guard = 0;
            while (set.Count > 0 && guard++ < 64)
            {
                var arr = set.ToArray();
                set.Clear();
                foreach (var cell in arr)
                    cell.FlushDispatch();
            }
        }
    }
}
