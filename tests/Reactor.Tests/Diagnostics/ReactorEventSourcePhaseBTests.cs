using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase B (§2.7) — smoke-tests the new subsystem events on
/// <see cref="ReactorEventSource"/> (Hosting / Persistence / Navigation /
/// Intl / Theme) and the cross-keyword end-to-end regression guard that
/// verifies the new keyword bits do not overlap.
///
/// Each test fires the event with a per-test discriminator string so
/// concurrent emissions from other tests can't false-positive.
/// </summary>
public class ReactorEventSourcePhaseBTests : IDisposable
{
    private sealed class CapturingListener : EventListener
    {
        private readonly List<EventWrittenEventArgs> _events = new();

        public IReadOnlyList<EventWrittenEventArgs> Events
        {
            get { lock (_events) return _events.ToArray(); }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            lock (_events) _events.Add(eventData);
        }
    }

    private readonly CapturingListener _listener = new();

    public ReactorEventSourcePhaseBTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Hosting
                | ReactorEventSource.Keywords.Persistence
                | ReactorEventSource.Keywords.Navigation
                | ReactorEventSource.Keywords.Intl
                | ReactorEventSource.Keywords.Theme
                | ReactorEventSource.Keywords.Errors);
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
    }

    // ── Hosting ─────────────────────────────────────────────────────────

    [Fact]
    public void WindowOpened_writes_type_and_hwnd()
    {
        const string windowType = "PhaseBTests.WindowOpened.MyWindow";
        ReactorEventSource.Log.WindowOpened(windowType, 0x1234L);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.WindowOpened)
            && (e.Payload?[0] as string) == windowType);
        Assert.Equal(0x1234L, evt.Payload?[1]);
    }

    [Fact]
    public void WindowClosed_writes_type_and_hwnd()
    {
        const string windowType = "PhaseBTests.WindowClosed.MyWindow";
        ReactorEventSource.Log.WindowClosed(windowType, 0xABCDL);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.WindowClosed)
            && (e.Payload?[0] as string) == windowType);
        Assert.Equal(0xABCDL, evt.Payload?[1]);
    }

    [Fact]
    public void WindowDpiChanged_writes_old_and_new()
    {
        const string windowType = "PhaseBTests.WindowDpiChanged.MyWindow";
        ReactorEventSource.Log.WindowDpiChanged(windowType, 96, 144);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.WindowDpiChanged)
            && (e.Payload?[0] as string) == windowType);
        Assert.Equal(96, evt.Payload?[1]);
        Assert.Equal(144, evt.Payload?[2]);
    }

    [Fact]
    public void BackdropMaterializationFailed_writes_kind_and_exception_type()
    {
        const string kind = "PhaseBTests.Backdrop.Mica";
        ReactorEventSource.Log.BackdropMaterializationFailed(kind, nameof(InvalidOperationException));

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.BackdropMaterializationFailed)
            && (e.Payload?[0] as string) == kind);
        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload?[1]);
    }

    // ── Persistence ─────────────────────────────────────────────────────

    [Fact]
    public void PersistenceRead_writes_store_and_size()
    {
        const string storeKind = "PhaseBTests.Persistence.settings-read";
        ReactorEventSource.Log.PersistenceRead(storeKind, 4096);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.PersistenceRead)
            && (e.Payload?[0] as string) == storeKind);
        Assert.Equal(4096, evt.Payload?[1]);
    }

    [Fact]
    public void PersistenceWrite_writes_store_and_size()
    {
        const string storeKind = "PhaseBTests.Persistence.settings-write";
        ReactorEventSource.Log.PersistenceWrite(storeKind, 2048);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.PersistenceWrite)
            && (e.Payload?[0] as string) == storeKind);
        Assert.Equal(2048, evt.Payload?[1]);
    }

    [Fact]
    public void PersistenceRejected_writes_store_and_reason()
    {
        const string storeKind = "PhaseBTests.Persistence.placement-rejected";
        ReactorEventSource.Log.PersistenceRejected(storeKind, "oversize");

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.PersistenceRejected)
            && (e.Payload?[0] as string) == storeKind);
        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        Assert.Equal("oversize", evt.Payload?[1]);
    }

    // ── Navigation ──────────────────────────────────────────────────────

    [Fact]
    public void NavigationRequested_writes_route_template()
    {
        const string route = "/phaseb-tests/nav-requested/{id}";
        ReactorEventSource.Log.NavigationRequested(route);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationRequested)
            && (e.Payload?[0] as string) == route);
        Assert.Equal(route, evt.Payload?[0]);
    }

    [Fact]
    public void NavigationCompleted_writes_route_and_duration()
    {
        const string route = "/phaseb-tests/nav-completed/{id}";
        ReactorEventSource.Log.NavigationCompleted(route, 12.5);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCompleted)
            && (e.Payload?[0] as string) == route);
        Assert.Equal(12.5, evt.Payload?[1]);
    }

    [Fact]
    public void NavigationCancelled_writes_route_and_reason()
    {
        const string route = "/phaseb-tests/nav-cancelled/{id}";
        ReactorEventSource.Log.NavigationCancelled(route, "user-back");

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCancelled)
            && (e.Payload?[0] as string) == route);
        Assert.Equal("user-back", evt.Payload?[1]);
    }

    [Fact]
    public void NavigationCache_events_emit_at_verbose_level()
    {
        const string route = "/phaseb-tests/nav-cache/{id}";
        ReactorEventSource.Log.NavigationCacheHit(route);
        ReactorEventSource.Log.NavigationCacheMiss(route);
        ReactorEventSource.Log.NavigationCacheEvict(route, "lru");

        var hit = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheHit)
            && (e.Payload?[0] as string) == route);
        Assert.Equal((int)EventLevel.Verbose, (int)hit.Level);

        var miss = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheMiss)
            && (e.Payload?[0] as string) == route);
        Assert.Equal((int)EventLevel.Verbose, (int)miss.Level);

        var evict = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheEvict)
            && (e.Payload?[0] as string) == route);
        Assert.Equal((int)EventLevel.Verbose, (int)evict.Level);
        Assert.Equal("lru", evict.Payload?[1]);
    }

    // ── Intl ────────────────────────────────────────────────────────────

    [Fact]
    public void IntlMissingKey_writes_key_locale_and_fellback_flag()
    {
        const string key = "PhaseBTests.Intl.MissingKey";
        ReactorEventSource.Log.IntlMissingKey(key, "fr-FR", true);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == key);
        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        Assert.Equal("fr-FR", evt.Payload?[1]);
        Assert.Equal(true, evt.Payload?[2]);
    }

    // ── Theme ───────────────────────────────────────────────────────────

    [Fact]
    public void ThemeApplyFailed_writes_target_and_exception_type()
    {
        const string target = "PhaseBTests.Theme.MyControl";
        ReactorEventSource.Log.ThemeApplyFailed(target, nameof(InvalidCastException));

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.ThemeApplyFailed)
            && (e.Payload?[0] as string) == target);
        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        Assert.Equal(nameof(InvalidCastException), evt.Payload?[1]);
    }

    // ── Cross-keyword regression guard ──────────────────────────────────

    [Fact]
    public void All_new_keywords_capture_their_events_concurrently()
    {
        // Regression guard against keyword-bit overlap: with every new
        // keyword + Errors enabled, one event from each must land.
        const string discriminator = "PhaseBTests.AllKeywordsConcurrent";

        ReactorEventSource.Log.WindowOpened(discriminator + ".window", 0x1L);
        ReactorEventSource.Log.PersistenceRead(discriminator + ".store", 1);
        ReactorEventSource.Log.NavigationRequested(discriminator + ".route");
        ReactorEventSource.Log.IntlMissingKey(discriminator + ".key", "en-US", false);
        ReactorEventSource.Log.ThemeApplyFailed(discriminator + ".target", "X");
        ReactorEventSource.Log.SwallowedError("Reactor", discriminator + ".op", "X");

        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.WindowOpened)
            && (e.Payload?[0] as string) == discriminator + ".window");
        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.PersistenceRead)
            && (e.Payload?[0] as string) == discriminator + ".store");
        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationRequested)
            && (e.Payload?[0] as string) == discriminator + ".route");
        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == discriminator + ".key");
        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.ThemeApplyFailed)
            && (e.Payload?[0] as string) == discriminator + ".target");
        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload?[1] as string) == discriminator + ".op");
    }

    [Fact]
    public void New_keyword_bits_are_disjoint_from_existing_ones()
    {
        // The new keyword bits must not collide with existing ones —
        // otherwise enabling one would silently enable another. This is
        // a defensive constant-level assertion that survives even if the
        // EventSource is never instantiated.
        var oldBits = (long)ReactorEventSource.Keywords.Reconcile
            | (long)ReactorEventSource.Keywords.Render
            | (long)ReactorEventSource.Keywords.State
            | (long)ReactorEventSource.Keywords.Mcp
            | (long)ReactorEventSource.Keywords.Lifecycle
            | (long)ReactorEventSource.Keywords.Errors
            | (long)ReactorEventSource.Keywords.EventDispatch;

        var newBits = (long)ReactorEventSource.Keywords.Hosting
            | (long)ReactorEventSource.Keywords.Persistence
            | (long)ReactorEventSource.Keywords.Navigation
            | (long)ReactorEventSource.Keywords.Intl
            | (long)ReactorEventSource.Keywords.Theme
            | (long)ReactorEventSource.Keywords.Shell;

        Assert.Equal(0, oldBits & newBits);
    }
}
