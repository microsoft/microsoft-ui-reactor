using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Drives the internal NavigationDiagnostics OnX entry points so the
/// Debug.WriteLine + event-invoke bodies execute. Subscribers verify
/// the right payload made it through.
/// </summary>
public class NavigationDiagnosticsCoverageTests
{
    // NavigationDiagnostics events are process-wide static delegates. Real navigation
    // tests running in parallel fire these same events, so handlers must filter to
    // their own payload (unique From/To keys per test) or risk capturing a sibling
    // test's event.
    private static (string From, string To) UniqueKeys()
    {
        var g = Guid.NewGuid().ToString("N");
        return ($"From-{g}", $"To-{g}");
    }

    [Fact]
    public void OnNavigationRequested_Fires_Event_With_Payload()
    {
        var (from, to) = UniqueKeys();
        NavigationDiagnosticEvent? captured = null;
        Action<NavigationDiagnosticEvent> handler = e =>
        {
            // From/To are typed `object`; compare via Equals to avoid CS0252.
            if (Equals(e.From, from) && Equals(e.To, to)) captured = e;
        };
        NavigationDiagnostics.NavigationRequested += handler;
        try
        {
            NavigationDiagnostics.OnNavigationRequested(from, to, NavigationMode.Push);
        }
        finally
        {
            NavigationDiagnostics.NavigationRequested -= handler;
        }
        Assert.NotNull(captured);
        Assert.Equal(from, captured!.From);
        Assert.Equal(to, captured.To);
        Assert.Equal(NavigationMode.Push, captured.Mode);
    }

    [Fact]
    public void OnNavigationCompleted_Fires_Event()
    {
        var (from, to) = UniqueKeys();
        NavigationDiagnosticEvent? captured = null;
        Action<NavigationDiagnosticEvent> handler = e =>
        {
            // From/To are typed `object`; compare via Equals to avoid CS0252.
            if (Equals(e.From, from) && Equals(e.To, to)) captured = e;
        };
        NavigationDiagnostics.NavigationCompleted += handler;
        try
        {
            NavigationDiagnostics.OnNavigationCompleted(from, to, NavigationMode.Pop);
        }
        finally
        {
            NavigationDiagnostics.NavigationCompleted -= handler;
        }
        Assert.NotNull(captured);
        Assert.Equal(NavigationMode.Pop, captured!.Mode);
    }

    [Fact]
    public void OnNavigationCancelled_Captures_Reason()
    {
        var (from, to) = UniqueKeys();
        NavigationDiagnosticEvent? captured = null;
        Action<NavigationDiagnosticEvent> handler = e =>
        {
            // From/To are typed `object`; compare via Equals to avoid CS0252.
            if (Equals(e.From, from) && Equals(e.To, to)) captured = e;
        };
        NavigationDiagnostics.NavigationCancelled += handler;
        try
        {
            NavigationDiagnostics.OnNavigationCancelled(from, to, NavigationMode.Push, "guard");
        }
        finally
        {
            NavigationDiagnostics.NavigationCancelled -= handler;
        }
        Assert.NotNull(captured);
        Assert.Equal("guard", captured!.Reason);
    }

    [Fact]
    public void OnCacheHit_OnCacheMiss_OnCacheEviction_Fire()
    {
        var g = Guid.NewGuid().ToString("N");
        var rHit = $"Hit-{g}";
        var rMiss = $"Miss-{g}";
        var rEvict = $"Evict-{g}";
        var hits = new List<CacheDiagnosticEvent>();
        var misses = new List<CacheDiagnosticEvent>();
        var evicts = new List<CacheDiagnosticEvent>();
        // CacheDiagnosticEvent.Route is typed as object; compare via Equals to
        // avoid the reference-comparison footgun (CS0252).
        Action<CacheDiagnosticEvent> hh = e => { if (Equals(e.Route, rHit)) hits.Add(e); };
        Action<CacheDiagnosticEvent> mh = e => { if (Equals(e.Route, rMiss)) misses.Add(e); };
        Action<CacheDiagnosticEvent> eh = e => { if (Equals(e.Route, rEvict)) evicts.Add(e); };

        NavigationDiagnostics.CacheHit += hh;
        NavigationDiagnostics.CacheMiss += mh;
        NavigationDiagnostics.CacheEviction += eh;
        try
        {
            NavigationDiagnostics.OnCacheHit(rHit);
            NavigationDiagnostics.OnCacheMiss(rMiss);
            NavigationDiagnostics.OnCacheEviction(rEvict);
        }
        finally
        {
            NavigationDiagnostics.CacheHit -= hh;
            NavigationDiagnostics.CacheMiss -= mh;
            NavigationDiagnostics.CacheEviction -= eh;
        }
        Assert.Equal(rHit, Assert.Single(hits).Route);
        Assert.Equal(rMiss, Assert.Single(misses).Route);
        Assert.Equal(rEvict, Assert.Single(evicts).Route);
    }

    [Fact]
    public void OnTransitionStarted_OnTransitionCompleted_Fire()
    {
        // Filter by transition instance identity — concurrent navigation tests fire
        // their own transitions through the same global event.
        var transition = new SlideTransition();
        TransitionDiagnosticEvent? start = null;
        TransitionDiagnosticEvent? end = null;
        Action<TransitionDiagnosticEvent> sh = e => { if (ReferenceEquals(e.Transition, transition)) start = e; };
        Action<TransitionDiagnosticEvent> eh = e => { if (ReferenceEquals(e.Transition, transition)) end = e; };
        NavigationDiagnostics.TransitionStarted += sh;
        NavigationDiagnostics.TransitionCompleted += eh;
        try
        {
            NavigationDiagnostics.OnTransitionStarted(transition, NavigationMode.Push);
            NavigationDiagnostics.OnTransitionCompleted(transition, NavigationMode.Pop);
        }
        finally
        {
            NavigationDiagnostics.TransitionStarted -= sh;
            NavigationDiagnostics.TransitionCompleted -= eh;
        }
        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.Equal(NavigationMode.Push, start!.Mode);
        Assert.Equal(NavigationMode.Pop, end!.Mode);
    }

    [Fact]
    public void OnDeepLinkResolved_Fires_With_Match_And_Miss()
    {
        var pathHit = $"/home-{Guid.NewGuid():N}";
        DeepLinkDiagnosticEvent? captured = null;
        Action<DeepLinkDiagnosticEvent> handler = e => { if (e.Path == pathHit) captured = e; };
        NavigationDiagnostics.DeepLinkResolved += handler;
        try
        {
            NavigationDiagnostics.OnDeepLinkResolved(pathHit, true, 2);
        }
        finally
        {
            NavigationDiagnostics.DeepLinkResolved -= handler;
        }
        Assert.NotNull(captured);
        Assert.Equal(pathHit, captured!.Path);
        Assert.True(captured.Matched);
        Assert.Equal(2, captured.RouteCount);

        // Re-fire for the miss branch (matched=false formats differently)
        var pathMiss = $"/missing-{Guid.NewGuid():N}";
        DeepLinkDiagnosticEvent? miss = null;
        Action<DeepLinkDiagnosticEvent> handler2 = e => { if (e.Path == pathMiss) miss = e; };
        NavigationDiagnostics.DeepLinkResolved += handler2;
        try
        {
            NavigationDiagnostics.OnDeepLinkResolved(pathMiss, false, 0);
        }
        finally
        {
            NavigationDiagnostics.DeepLinkResolved -= handler2;
        }
        Assert.False(miss!.Matched);
    }

    [Fact]
    public void Diagnostic_Events_Without_Subscribers_Do_Not_Throw()
    {
        // No subscribers — should not throw.
        NavigationDiagnostics.OnNavigationRequested("A", "B", NavigationMode.Replace);
        NavigationDiagnostics.OnNavigationCompleted("A", "B", NavigationMode.Replace);
        NavigationDiagnostics.OnCacheHit("R");
        NavigationDiagnostics.OnCacheMiss("R");
        NavigationDiagnostics.OnCacheEviction("R");
        NavigationDiagnostics.OnTransitionStarted(new SlideTransition(), NavigationMode.Push);
        NavigationDiagnostics.OnTransitionCompleted(new SlideTransition(), NavigationMode.Push);
        NavigationDiagnostics.OnDeepLinkResolved("/", false, 0);
        NavigationDiagnostics.OnNavigationCancelled("A", "B", NavigationMode.Push, "x");
    }
}
