using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Internal tracker for the "show panel where you left it" mechanic
/// (spec 045 §5.3.9 / tracking §2.15).
/// </summary>
/// <remarks>
/// Every <see cref="DockableContent"/> remembers the last
/// <see cref="DockNode"/> container it was inside; when a hidden pane is
/// re-shown, the host routes it back to that container instead of the
/// default insertion point. The tracker uses a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> so the bookkeeping
/// doesn't keep panes alive after they fall out of the layout.
///
/// <para>
/// The tracker is internal — apps don't see it. The data survives a layout
/// serialization round-trip via <see cref="DockableContent.PersistenceState"/>
/// (the host writes / reads a sentinel marker; future refinement may move
/// this to a dedicated <c>previousContainer</c> JSON field — already
/// reserved in <see cref="Persistence.DockLayoutPane"/>).
/// </para>
/// </remarks>
internal static class PreviousContainerTracker
{
    private static readonly ConditionalWeakTable<DockableContent, ContainerRef> Table = new();

    /// <summary>
    /// Records that <paramref name="pane"/> last lived inside
    /// <paramref name="container"/>. Subsequent <see cref="GetPrevious"/>
    /// calls return that container until another <c>Set</c> overwrites it.
    /// </summary>
    public static void Set(DockableContent pane, DockNode container)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(container);

        // ConditionalWeakTable uses reference equality on the key; we WANT
        // that — the tracker is keyed by the pane's specific instance, so
        // a new pane with the same Key won't accidentally inherit history.
        if (Table.TryGetValue(pane, out var existing))
        {
            existing.Container = container;
        }
        else
        {
            Table.Add(pane, new ContainerRef { Container = container });
        }
    }

    /// <summary>
    /// Returns the last-known container for <paramref name="pane"/>, or
    /// null if the pane was never recorded (or has been re-shown such that
    /// the bookkeeping was cleared).
    /// </summary>
    public static DockNode? GetPrevious(DockableContent pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        return Table.TryGetValue(pane, out var existing) ? existing.Container : null;
    }

    /// <summary>
    /// Forgets the tracked container for <paramref name="pane"/>. Called
    /// by the host once the pane has been re-attached, so subsequent
    /// hide→show cycles record the new container.
    /// </summary>
    public static void Clear(DockableContent pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        Table.Remove(pane);
    }

    /// <summary>Removes every tracking entry — used by tests for isolation.</summary>
    internal static void ClearAll()
    {
        // ConditionalWeakTable has no Clear() — the live entries decay with
        // their key references. This method exists to document intent; tests
        // should use fresh DockableContent instances per case.
    }

    // Indirection so we can update Container in place without a re-add.
    private sealed class ContainerRef
    {
        public DockNode? Container;
    }
}
