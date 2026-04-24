using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Test / devtools seam over the layout-cost attribution aggregator. Exposes
/// per-Component snapshots plus the two health signals needed by the overlay
/// menu (dropped events, ETW availability). Not a public API — Reactor
/// exposes it on its hosts as an internal debug hook so self-tests can assert
/// against real attribution state.
/// </summary>
internal interface ILayoutCostReporter
{
    /// <summary>
    /// Current snapshot of per-Component rollups. Safe to enumerate on the UI
    /// thread — the aggregator swaps its read-side buffer atomically on each
    /// drain.
    /// </summary>
    IReadOnlyList<ComponentSnapshot> GetSnapshot();

    /// <summary>
    /// Number of paired layout events dropped due to ring-buffer overflow
    /// since the reporter was created. Monotonic.
    /// </summary>
    long DroppedEventCount { get; }

    /// <summary>
    /// True when the ETW session could not be started — the user is not a
    /// member of <c>Performance Log Users</c>, no elevation available, or
    /// another consumer is already using the provider exclusively. When true,
    /// the overlay draws nothing and the devtools menu surfaces an
    /// "unavailable" subtitle.
    /// </summary>
    bool IsEtwUnavailable { get; }
}
