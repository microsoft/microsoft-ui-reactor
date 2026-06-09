using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core
{
    internal sealed class ReferenceEdgeBag
    {
        public readonly Dictionary<int, ReferenceEdge> Edges = new();
    }

    internal sealed class ReferenceEdge
    {
        public ElementRef? Cell;
        public Action<FrameworkElement?>? Handler;
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
