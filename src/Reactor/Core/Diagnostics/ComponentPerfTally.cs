namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Per-thread tallies behind the render-outcome fields of the <c>Microsoft-UI-Reactor-Perf</c>
/// provider (schema revision 3, issue #1538): the <c>ComponentRender</c> Stop's <c>changes</c>
/// and <c>stateSets</c>, and the <c>Effects</c> Stop's <c>stateSets</c>.
/// </summary>
/// <remarks>
/// <para>A span opens a <see cref="Scope"/> on Start and closes it on Stop. Spans nest on the
/// UI thread that renders them, so the scopes do too.</para>
/// <list type="bullet">
/// <item><c>changes</c> is <b>inclusive</b>: a monotonic count of the controls the reconciler
/// created, updated, removed or moved on this thread, read as a difference, so a component's
/// count includes its nested components'.</item>
/// <item><c>stateSets</c> is <b>exclusive</b>: opening a scope sets the count aside and closing
/// it restores it, so a setter call counts only toward the innermost open span — the component
/// whose Render(), effects or output reconcile made it.</item>
/// </list>
/// <para>Nothing here runs while no session listens: the reconciler and the state setters
/// check the provider's enable gate first, and scopes open only for spans that wrote a Start.</para>
/// </remarks>
internal static class ComponentPerfTally
{
    [ThreadStatic] private static Counters? t_counters;

    private sealed class Counters
    {
        public ulong Changes;
        public uint StateSets;
        public int OpenScopes;
    }

    /// <summary>The counts as they stood when a span opened.</summary>
    internal readonly struct Scope(ulong changesAtOpen, uint outerStateSets)
    {
        internal ulong ChangesAtOpen { get; } = changesAtOpen;
        internal uint OuterStateSets { get; } = outerStateSets;
    }

    /// <summary>
    /// The reconciler changed <paramref name="count"/> controls on this thread. Callers check
    /// <see cref="ReactorPerfTrace.ComponentEnabled"/> first.
    /// </summary>
    internal static void AddChanges(uint count)
    {
        var counters = t_counters ??= new Counters();
        counters.Changes += count;
    }

    /// <summary>
    /// A state setter ran on this thread; it counts toward the innermost open span, if any.
    /// Callers check <see cref="ReactorPerfTrace.StateSetsEnabled"/> first.
    /// </summary>
    internal static void NoteStateSet()
    {
        if (t_counters is { OpenScopes: > 0 } counters)
            counters.StateSets++;
    }

    internal static Scope Open()
    {
        var counters = t_counters ??= new Counters();
        var scope = new Scope(counters.Changes, counters.StateSets);
        counters.StateSets = 0;
        counters.OpenScopes++;
        return scope;
    }

    /// <summary>Closes <paramref name="scope"/>, the thread's innermost open one.</summary>
    internal static void Close(in Scope scope, out uint changes, out uint stateSets)
    {
        var counters = t_counters;
        if (counters is null || counters.OpenScopes == 0)
        {
            changes = 0;
            stateSets = 0;
            return;
        }
        ulong delta = counters.Changes - scope.ChangesAtOpen;
        changes = delta > uint.MaxValue ? uint.MaxValue : (uint)delta;
        stateSets = counters.StateSets;
        counters.StateSets = scope.OuterStateSets;
        counters.OpenScopes--;
    }

    /// <summary>Open scopes on this thread; must be 0 between spans.</summary>
    internal static int OpenScopesForTests => t_counters?.OpenScopes ?? 0;
}
