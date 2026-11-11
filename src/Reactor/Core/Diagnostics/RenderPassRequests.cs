namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Who scheduled a host's next render pass, for the <c>requesterId</c> / <c>requesterName</c> /
/// <c>requestCount</c> fields of the perf <c>RenderPass</c> Start. Shared by <c>ReactorHost</c>
/// and <c>ReactorHostControl</c> so the two hosts cannot drift.
/// </summary>
/// <remarks>
/// <para><b>How a request is attributed.</b> A component's re-render request runs through its
/// wrapper (<c>Reconciler.CreateComponentRerender</c>), then each ancestor component's wrapper,
/// then the host's <c>RequestRender</c>, all synchronously on one thread. The first wrapper to
/// <see cref="Claim"/> the thread is the component that asked; the ancestors it passes through
/// find the claim taken. The host root's own hooks claim through the host's root rerender
/// action, with the root's <see cref="RootRenderDiagnostics"/> (replaced when the root is), so
/// a request from a root that was remounted before the pass names nobody rather than its
/// replacement. A claim is consumed by the one request it was made for (<see cref="Note"/>),
/// so a pass that request runs synchronously does not inherit it. A request nobody claimed
/// (the first mount, a theme or resource change, hot reload, app code calling the host
/// directly) counts, but names no component.</para>
///
/// <para><b>Which requester a pass reports.</b> The first since the previous pass started: it
/// is what scheduled the pass; later ones only coalesced into it. If that first request was
/// unclaimed, the pass names nobody, even when a component's request coalesced into it later.
/// <see cref="Take"/> runs when a pass starts, so a request made during a pass belongs to the
/// next one. Noting (any thread) and taking (the UI thread) share one lock, so a request's
/// count and requester always land in the same pass.</para>
///
/// <para><b>Cost.</b> Everything here runs only while the <c>Pass</c> keyword is on; with no
/// session listening the hosts and wrappers make one enable check and nothing else, and an
/// untraced pass makes two plain reads.</para>
/// </remarks>
internal sealed class RenderPassRequests
{
    // The requester whose request is running on this thread: a Reconciler.ComponentNode, or a
    // RootRenderDiagnostics (standing for a host's root component). Set only while the Pass
    // keyword is on, and cleared once a request is noted.
    [ThreadStatic] private static object? t_requester;

    // _first's value when the first request since the last Take was unclaimed: it holds the
    // slot, so a later claimed request cannot take credit for a pass it did not schedule.
    private static readonly object Unclaimed = new();

    private readonly Lock _gate = new();
    private object? _first;
    private int _count;

    // Test-only: runs inside the lock, between counting a request and recording its requester.
    internal Action? BetweenCountAndRequesterForTests { get; set; }

    /// <summary>
    /// Marks <paramref name="requester"/> as the one whose request runs on this thread, unless
    /// a request already running here claimed it first. Returns whether it claimed; a caller
    /// that did calls <see cref="Release"/> when its request returns.
    /// </summary>
    internal static bool Claim(object requester)
    {
        if (t_requester is not null) return false;
        t_requester = requester;
        return true;
    }

    internal static void Release() => t_requester = null;

    /// <summary>
    /// Runs <paramref name="request"/> (a re-render request on its way to the host) attributed
    /// to <paramref name="requester"/>: claimed while the Pass keyword is on, released after.
    /// With no session listening it is one enable check and the call.
    /// </summary>
    internal static void InvokeAs(object requester, Action request)
    {
        bool claimed = ReactorPerfTrace.PassEnabled && Claim(requester);
        try { request(); }
        finally
        {
            if (claimed) Release();
        }
    }

    internal static object? CurrentForTests => t_requester;

    /// <summary>
    /// Records a render request while the Pass keyword is on; with no session listening it is
    /// one enable check. Every host's <c>RequestRender</c> calls it first.
    /// </summary>
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void Note()
    {
        if (ReactorPerfTrace.PassEnabled) NoteCore();
    }

    private void NoteCore()
    {
        // Consumed by the one request it was claimed for: a pass that request runs
        // synchronously (a host's first render) must not credit its own requests to it.
        object requester = ConsumeClaim() ?? Unclaimed;
        lock (_gate)
        {
            _count++;
            BetweenCountAndRequesterForTests?.Invoke();
            _first ??= requester;
        }
    }

    private static object? ConsumeClaim()
    {
        object? requester = t_requester;
        t_requester = null;
        return requester;
    }

    /// <summary>Whether anything was noted since the last <see cref="Take"/> (plain reads, no lock).</summary>
    internal bool HasPending => _first is not null || _count != 0;

    /// <summary>
    /// The requester of the first request noted since the last call (null when that request
    /// was unclaimed, or there was none), and how many requests were; clears both.
    /// </summary>
    internal object? Take(out uint count)
    {
        object? first;
        lock (_gate)
        {
            count = (uint)_count;
            _count = 0;
            first = _first;
            _first = null;
        }
        return ReferenceEquals(first, Unclaimed) ? null : first;
    }

    /// <summary>
    /// Drops what was noted for a pass that is starting untraced (the session stopped after the
    /// request), so a later traced pass never reports a requester that did not schedule it.
    /// With nothing noted it is one plain read.
    /// </summary>
    internal void DiscardStale()
    {
        if (HasPending) Take(out _);
    }
}
