using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Component lookup for <see cref="Diagnostics.ReactorDiagnostics"/>: maps a mounted
/// component's wrapper <see cref="UIElement"/> (or a host's root content) back to the
/// reconciler state that owns it.
///
/// <para>Each host owns its own <see cref="Reconciler"/>, and a component node lives
/// in that reconciler's private <c>_componentNodes</c> table, so a lookup has to know
/// every live reconciler. The registry below is the only cost this adds to a running
/// app: one weak reference per reconciler (i.e. per host), taken at construction and
/// dropped at <see cref="Dispose"/>. Nothing is recorded per component or per render;
/// every query is resolved on demand.</para>
/// </summary>
public sealed partial class Reconciler
{
    private static readonly object s_liveGate = new();
    private static readonly List<WeakReference<Reconciler>> s_live = new();

    /// <summary>
    /// Set by the owning host (<c>ReactorHost</c> / <c>ReactorHostControl</c>). The root
    /// component is rendered by the host, not mounted through <see cref="MountComponent"/>,
    /// so it has no <c>_componentNodes</c> entry; this resolver is how a diagnostics query
    /// reaches it. Returns null when <c>element</c> is not this host's root anchor.
    /// </summary>
    internal Func<UIElement, RootComponentSource?>? DiagnosticsRootResolver { get; set; }

    /// <summary>
    /// The owning host's UI dispatcher, set alongside <see cref="DiagnosticsRootResolver"/>.
    /// The registry is process-wide while hosts can live on different UI threads, so a lookup
    /// skips every reconciler whose dispatcher is not the calling thread's before touching its
    /// unsynchronized tables or controls. A reconciler with no dispatcher recorded is not inspected.
    /// </summary>
    internal Microsoft.UI.Dispatching.DispatcherQueue? DiagnosticsDispatcher { get; set; }

    private bool _diagnosticsDispatcherProbed;

    /// <summary>
    /// Records the owning UI thread of a reconciler driven directly (no host to supply it) the
    /// first time it records a component, which only happens on that thread with a live wrapper.
    /// One probe per reconciler; a no-op once a host or an earlier probe set it.
    /// </summary>
    private void CaptureDiagnosticsDispatcher()
    {
        if (_diagnosticsDispatcherProbed) return;
        _diagnosticsDispatcherProbed = true;
        DiagnosticsDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // Inspectable only from the UI thread it has recorded. A reconciler with no dispatcher yet
    // (constructed, not yet hosted or mounting) is skipped: it has no live element to resolve,
    // and its first mount may be mutating its tables on another thread.
    private bool IsInspectableFromThisThread => DiagnosticsDispatcher is { HasThreadAccess: true };

    private void RegisterForDiagnostics()
    {
        lock (s_liveGate)
        {
            // Prune collected entries on the way in so the list tracks live hosts,
            // not every reconciler a long-running process (or test run) ever created.
            s_live.RemoveAll(static w => !w.TryGetTarget(out _));
            s_live.Add(new WeakReference<Reconciler>(this));
        }
    }

    private void UnregisterForDiagnostics()
    {
        lock (s_liveGate)
        {
            s_live.RemoveAll(w => !w.TryGetTarget(out var r) || ReferenceEquals(r, this));
        }
    }

    private static List<Reconciler> SnapshotLiveReconcilers()
    {
        lock (s_liveGate)
        {
            var list = new List<Reconciler>(s_live.Count);
            foreach (var r in s_live.Select(static w => w.TryGetTarget(out var target) ? target : null).Where(static r => r is not null))
                list.Add(r!);
            return list;
        }
    }

    /// <summary>Test seam: whether this reconciler is in the live registry.</summary>
    internal bool IsRegisteredForDiagnostics
    {
        get
        {
            lock (s_liveGate)
            {
                return s_live.Any(w => w.TryGetTarget(out var r) && ReferenceEquals(r, this));
            }
        }
    }

    /// <summary>
    /// Finds the component whose wrapper is <paramref name="element"/> in any live
    /// reconciler. Must be called on the UI thread (the node tables are not locked).
    /// </summary>
    internal static bool TryFindComponentNode(UIElement element, out Reconciler owner, out ComponentNode node)
    {
        foreach (var r in SnapshotLiveReconcilers().Where(static r => r.IsInspectableFromThisThread))
        {
            if (r._componentNodes.TryGetValue(element, out var found))
            {
                owner = r;
                node = found;
                return true;
            }
        }
        owner = null!;
        node = null!;
        return false;
    }

    /// <summary>Finds the host root component anchored at <paramref name="element"/>.</summary>
    internal static bool TryFindRootComponent(UIElement element, out RootComponentSource root)
    {
        foreach (var r in SnapshotLiveReconcilers().Where(static r => r.IsInspectableFromThisThread))
        {
            if (r.DiagnosticsRootResolver?.Invoke(element) is { } found)
            {
                root = found;
                return true;
            }
        }
        root = default;
        return false;
    }

    /// <summary>
    /// True while <paramref name="wrapper"/> still maps to <paramref name="node"/> — false
    /// once the component unmounts or its wrapper is re-used for a different instance.
    /// </summary>
    internal bool IsLiveComponentNode(UIElement wrapper, ComponentNode node)
        => !_disposedForDiagnostics
            && _componentNodes.TryGetValue(wrapper, out var current)
            && ReferenceEquals(current, node);

    private bool _disposedForDiagnostics;
}

/// <summary>
/// A host's root component as seen by diagnostics. Exactly one of
/// <see cref="Component"/> (a class component passed to <c>Mount(Component)</c>) or
/// <see cref="FuncContext"/> + <see cref="RenderFunc"/> (a render function passed to
/// <c>Mount(Func&lt;RenderContext, Element&gt;)</c>) is set.
/// </summary>
internal readonly record struct RootComponentSource(
    Component? Component,
    RenderContext? FuncContext,
    Delegate? RenderFunc,
    UIElement? RenderedControl,
    Element? RenderedElement,
    Func<bool> IsAlive);

/// <summary>
/// The root that produced a host's current control, published by the host in the same step
/// that publishes the control. <c>Mount</c> swaps the requested root before the queued render
/// replaces the control, so diagnostics resolve against this rather than the requested root.
/// </summary>
internal readonly struct RenderedRoot
{
    internal RenderedRoot(Component? component, RenderContext? funcContext, Delegate? renderFunc)
    {
        Component = component;
        FuncContext = component is null ? funcContext : null;
        RenderFunc = component is null ? renderFunc : null;
    }

    internal Component? Component { get; }
    internal RenderContext? FuncContext { get; }
    internal Delegate? RenderFunc { get; }
    internal bool IsEmpty => Component is null && FuncContext is null;

    internal bool SameRootAs(in RenderedRoot other)
        => ReferenceEquals(Component, other.Component) && ReferenceEquals(FuncContext, other.FuncContext);
}
