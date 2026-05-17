namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// React-style keyed-children diff that maps an immutable
/// <c>IReadOnlyList&lt;T&gt;</c> of user items onto the internal
/// <see cref="ReactorListState.Source"/> by emitting the minimal-shape
/// sequence of <c>Insert</c> / <c>Move</c> / <c>RemoveAt</c> operations
/// WinUI needs to animate row containers incrementally. See spec 042 §4.3
/// for the algorithm; populated in task 1.4.
/// </summary>
internal static class KeyedListDiff
{
    /// <summary>
    /// Per-diff bookkeeping returned to callers (tests, telemetry, the
    /// Phase 3 animation pipeline) so they can observe the op shape
    /// without walking the OC.
    /// </summary>
    internal readonly record struct DiffStats(int Inserts, int Removes, int Moves, int Survivors, bool Bailout)
    {
        public static readonly DiffStats Empty = new(0, 0, 0, 0, false);
    }
}
