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
/// unclaimed, or was made before the session started listening (the host already had the
/// pass pending when the first traced request arrived), the pass names nobody, even when a
/// component's request coalesced into it later.
/// <see cref="Take"/> runs when a pass starts, so a request made during a pass belongs to the
/// next one. Noting (any thread) and taking (the UI thread) share one lock, so a request's
/// count and requester always land in the same pass.</para>
///
/// <para><b>At a pass boundary.</b> Whether the host already had a pass pending comes from the
/// host's needs-rerender flag, which the render loop clears just before the pass takes its
/// requests. A request on another thread can read that flag
/// before the clear and note after the take; the pass sequence number read before the flag
/// detects that (a pass started in between, so the flag was the old pass's) and the request
/// is credited to the next pass. A request that notes between the clear and the take is the
/// starting pass's: its state was written before it asked, so that pass renders it. The flag
/// it then sets may run one more pass, which reports <c>requestCount</c> 0.</para>
///
/// <para><b>Cost.</b> Everything here runs only while the <c>Pass</c> keyword is on; with no
/// session listening the hosts and wrappers make one enable check and nothing else, and an
/// untraced pass makes two plain reads and one plain store.</para>
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

    // The host's needs-rerender flag: set by a request, cleared by the render loop as a pass
    // starts (before that pass takes its requests). Read only while tracing.
    private readonly Func<bool>? _passPending;
    private readonly Lock _gate = new();
    private object? _first;
    private int _count;
    // Passes started (traced or not), incremented as each takes or discards its requests.
    private int _passes;

    /// <param name="passPending">
    /// Whether the host has a pass pending (its needs-rerender flag); null for a host that
    /// never has one queued when a request arrives.
    /// </param>
    internal RenderPassRequests(Func<bool>? passPending = null) => _passPending = passPending;

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
    /// one enable check. Every host's <c>RequestRender</c> calls it first, before it sets its
    /// needs-rerender flag. If the host already had a pass pending and nothing was noted for
    /// it, a request made before the session started listening scheduled that pass, so it
    /// names nobody rather than this request, which only coalesced into it.
    /// </summary>
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void Note()
    {
        if (ReactorPerfTrace.PassEnabled) NoteCore();
    }

    private void NoteCore()
    {
        // The pass count before the flag: if a pass starts after this read, the flag may have
        // been that pass's (read before the render loop cleared it), not one still pending.
        int passes = Volatile.Read(ref _passes);
        bool passPending = _passPending?.Invoke() ?? false;
        // Consumed by the one request it was claimed for: a pass that request runs
        // synchronously (a host's first render) must not credit its own requests to it.
        object requester = ConsumeClaim() ?? Unclaimed;
        lock (_gate)
        {
            _count++;
            BetweenCountAndRequesterForTests?.Invoke();
            if (passPending && passes == _passes) _first ??= Unclaimed;
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
    /// Starts a traced pass: the requester of the first request noted since the last pass
    /// started (null when that request was unclaimed or came before the session, or there was
    /// none), and how many requests were; clears both.
    /// </summary>
    internal object? Take(out uint count)
    {
        object? first;
        lock (_gate)
        {
            _passes++;
            count = (uint)_count;
            _count = 0;
            first = _first;
            _first = null;
        }
        return ReferenceEquals(first, Unclaimed) ? null : first;
    }

    /// <summary>
    /// Starts an untraced pass: drops what was noted for it (the session stopped after the
    /// request), so a later traced pass never reports a requester that did not schedule it.
    /// With nothing noted it is one plain read and one plain store.
    /// </summary>
    internal void DiscardStale()
    {
        if (HasPending) Take(out _);
        // Passes start on the UI thread only, so this is the one writer; the release orders
        // the host's flag clear before it, for NoteCore's read on another thread.
        else Volatile.Write(ref _passes, _passes + 1);
    }
}
