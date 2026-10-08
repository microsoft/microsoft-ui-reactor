using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Component lookup for <see cref="Diagnostics.ReactorDiagnostics"/>: maps a mounted
/// component's wrapper <see cref="UIElement"/> (or a host's root content) back to the
/// reconciler state that owns it.
///
/// <para>Each host owns its own <see cref="Reconciler"/>, and a component node lives
/// in that reconciler's private <c>_componentNodes</c> table, so a lookup walks every live
/// host. It reuses the host registry behind <c>ReactorDiagnostics.GetHosts()</c>
/// (<see cref="Diagnostics.ReactorHostRegistry"/>) rather than keeping a second one, so
/// nothing is recorded per component or per render and every query is resolved on demand.
/// A reconciler used directly, without a host, is not inspected.</para>
/// </summary>
public sealed partial class Reconciler
{
    /// <summary>
    /// Set by the owning host (<c>ReactorHost</c> / <c>ReactorHostControl</c>). The root
    /// component is rendered by the host, not mounted through <see cref="MountComponent"/>,
    /// so it has no <c>_componentNodes</c> entry; this resolver is how a diagnostics query
    /// reaches it. Returns null when <c>element</c> is not this host's root anchor.
    /// </summary>
    internal Func<UIElement, RootComponentSource?>? DiagnosticsRootResolver { get; set; }

    /// <summary>
    /// The owning host's UI dispatcher, set alongside <see cref="DiagnosticsRootResolver"/>.
    /// Component rerenders marshal onto it (hosts can live on different UI threads, so the
    /// process-wide <c>ReactorApp.UIDispatcher</c> may be another window's), and diagnostics
    /// lookups skip every reconciler whose dispatcher is not the calling thread's before
    /// touching its unsynchronized tables or controls. A reconciler with no dispatcher recorded
    /// is not inspected, and its rerenders fall back to <c>ReactorApp.UIDispatcher</c>.
    /// </summary>
    internal Microsoft.UI.Dispatching.DispatcherQueue? OwningDispatcher { get; set; }

    private bool IsInspectableFromThisThread => OwningDispatcher is { HasThreadAccess: true };

    /// <summary>The reconcilers of live hosts that the calling thread may inspect.</summary>
    private static IEnumerable<Reconciler> InspectableReconcilers()
        => Diagnostics.ReactorHostRegistry.Snapshot()
            .Select(static host => host.DiagnosticReconciler)
            .Where(static r => r is { IsInspectableFromThisThread: true })!;
    /// <summary>
    /// Finds the component whose wrapper is <paramref name="element"/> in any live
    /// reconciler. Must be called on the UI thread (the node tables are not locked).
    /// </summary>
    internal static bool TryFindComponentNode(UIElement element, out Reconciler owner, out ComponentNode node)
    {
        foreach (var r in InspectableReconcilers())
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
        foreach (var r in InspectableReconcilers())
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
