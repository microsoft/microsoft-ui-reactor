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
