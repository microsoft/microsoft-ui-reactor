using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Diagnostic events for the navigation system. Subscribe to these events
/// to observe navigation operations for debugging and telemetry.
/// All events fire synchronously on the UI thread.
///
/// Each event also emits to the <c>Microsoft-UI-Reactor</c> ETW provider
/// under the <see cref="ReactorEventSource.Keywords.Navigation"/> keyword
/// (spec 044 §4.2). Use <c>dotnet-trace</c>/<c>PerfView</c> for capture in
/// Release; the in-process <see cref="Diagnostics.ReactorTrace.Subscribe"/>
/// helper exposes the same stream programmatically.
///
/// PII: ETW payloads carry only the destination route's type name (a
/// developer-authored identifier), the navigation mode, and structural
/// values like cache reasons. Concrete route instances, deep-link paths,
/// and any user-controllable strings are intentionally omitted per §6.2.1.
/// </summary>
public static class NavigationDiagnostics
{
    /// <summary>Fires when a navigation is attempted (before guards run).</summary>
    public static event EventHandler<NavigationDiagnosticEvent>? NavigationRequested;

    /// <summary>Fires when a navigation completes successfully.</summary>
    public static event EventHandler<NavigationDiagnosticEvent>? NavigationCompleted;

    /// <summary>Fires when a navigation guard cancels a navigation.</summary>
    public static event EventHandler<NavigationDiagnosticEvent>? NavigationCancelled;

    /// <summary>Fires on a cache hit during navigation.</summary>
    public static event EventHandler<CacheDiagnosticEvent>? CacheHit;

    /// <summary>Fires on a cache miss during navigation.</summary>
    public static event EventHandler<CacheDiagnosticEvent>? CacheMiss;

    /// <summary>Fires when a page is evicted from the cache.</summary>
    public static event EventHandler<CacheDiagnosticEvent>? CacheEviction;

    /// <summary>Fires when a transition animation starts.</summary>
    public static event EventHandler<TransitionDiagnosticEvent>? TransitionStarted;

    /// <summary>Fires when a transition animation completes.</summary>
    public static event EventHandler<TransitionDiagnosticEvent>? TransitionCompleted;

    /// <summary>Fires when a deep link resolves (match or miss).</summary>
    public static event EventHandler<DeepLinkDiagnosticEvent>? DeepLinkResolved;

    // Helper: derive a route template string from a route instance.
    // The TYPE NAME is the closest stable "template" we have for arbitrary
    // user route models — it's developer-authored and contains no instance
    // data. Null routes (e.g. initial navigation) collapse to "(none)".
    private static string TemplateOf(object? route) =>
        route?.GetType().FullName ?? "(none)";

    internal static void OnNavigationRequested(object from, object to, NavigationMode mode)
    {
        ReactorEventSource.Log.NavigationRequested(TemplateOf(to));
        NavigationRequested?.Invoke(null, new(from, to, mode));
    }

    internal static void OnNavigationCompleted(object from, object to, NavigationMode mode)
    {
        // durationMs is not tracked at this layer yet; pass 0 so the
        // payload shape stays compatible with future timing instrumentation.
        ReactorEventSource.Log.NavigationCompleted(TemplateOf(to), 0d);
        NavigationCompleted?.Invoke(null, new(from, to, mode));
    }

    internal static void OnNavigationCancelled(object from, object to, NavigationMode mode, string reason)
    {
        ReactorEventSource.Log.NavigationCancelled(TemplateOf(to), reason ?? string.Empty);
        NavigationCancelled?.Invoke(null, new(from, to, mode) { Reason = reason });
    }

    internal static void OnCacheHit(object route)
    {
        ReactorEventSource.Log.NavigationCacheHit(TemplateOf(route));
        CacheHit?.Invoke(null, new(route));
    }

    internal static void OnCacheMiss(object route)
    {
        ReactorEventSource.Log.NavigationCacheMiss(TemplateOf(route));
        CacheMiss?.Invoke(null, new(route));
    }

    internal static void OnCacheEviction(object route)
    {
        // No structured reason at this call site; payload requires a string.
        ReactorEventSource.Log.NavigationCacheEvict(TemplateOf(route), string.Empty);
        CacheEviction?.Invoke(null, new(route));
    }

    internal static void OnTransitionStarted(NavigationTransition transition, NavigationMode mode)
    {
        ReactorEventSource.Log.NavigationTransitionStarted(transition.GetType().Name, mode.ToString());
        TransitionStarted?.Invoke(null, new(transition, mode));
    }

    internal static void OnTransitionCompleted(NavigationTransition transition, NavigationMode mode)
    {
        ReactorEventSource.Log.NavigationTransitionCompleted(transition.GetType().Name, mode.ToString());
        TransitionCompleted?.Invoke(null, new(transition, mode));
    }

    internal static void OnDeepLinkResolved(string path, bool matched, int routeCount)
    {
        // PII: `path` is attacker-controllable input (clipboard / external
        //      link). Only the resolution outcome lands on the ETW stream.
        ReactorEventSource.Log.NavigationDeepLinkResolved(matched, routeCount);
        DeepLinkResolved?.Invoke(null, new(path, matched, routeCount));
    }
}

public sealed class NavigationDiagnosticEvent(object from, object to, NavigationMode mode)
{
    public object From { get; } = from;
    public object To { get; } = to;
    public NavigationMode Mode { get; } = mode;
    public string? Reason { get; init; }
}

public sealed class CacheDiagnosticEvent(object route)
{
    public object Route { get; } = route;
}

public sealed class TransitionDiagnosticEvent(NavigationTransition transition, NavigationMode mode)
{
    public NavigationTransition Transition { get; } = transition;
    public NavigationMode Mode { get; } = mode;
}

public sealed class DeepLinkDiagnosticEvent(string path, bool matched, int routeCount)
{
    public string Path { get; } = path;
    public bool Matched { get; } = matched;
    public int RouteCount { get; } = routeCount;
}
