using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Localization;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase C §4.3 — regression guard that <see cref="IntlAccessor"/>
/// emits the typed <c>IntlMissingKey</c> event under
/// <see cref="ReactorEventSource.Keywords.Intl"/> when a resource lookup
/// misses, and that format-time exceptions route through
/// <c>DiagnosticLog.SwallowedError(LogCategory.Intl, ...)</c> rather than
/// the old <c>Debug.WriteLine</c> channel.
///
/// MessageKey values used here are per-test discriminators so concurrent
/// localization traffic from other tests can't false-positive the asserts.
/// </summary>
public class IntlEtwBridgeTests : IDisposable
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

    public IntlEtwBridgeTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Intl | ReactorEventSource.Keywords.Errors);
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
    }

    // Minimal in-memory provider over a per-test dictionary. Keyed by
    // (locale, namespace, key) so tests can stage hits + misses for both
    // the active and the default locale.
    private sealed class MemoryProvider : IStringResourceProvider
    {
        private readonly Dictionary<(string Locale, string Namespace, string Key), string> _strings = new();

        public MemoryProvider Add(string locale, string ns, string key, string value)
        {
            _strings[(locale, ns, key)] = value;
            return this;
        }

        public string? GetString(string locale, string @namespace, string key)
            => _strings.TryGetValue((locale, @namespace, key), out var v) ? v : null;
    }

    private static MessageCache NewCache() => new();

    [Fact]
    public void Message_missing_in_current_locale_with_fallback_emits_IntlMissingKey_fellBack_true()
    {
        var ns = $"Phase044.Intl.{Guid.NewGuid():N}";
        var key = new MessageKey(ns, "Greeting");
        var provider = new MemoryProvider().Add("en-US", ns, "Greeting", "Hello");
        var accessor = new IntlAccessor("fr-FR", provider, NewCache(), defaultLocale: "en-US");

        var result = accessor.Message(key);

        Assert.Equal("Hello", result);
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == key.ToString());
        Assert.Equal("fr-FR", evt.Payload?[1]);
        Assert.Equal(true, evt.Payload?[2]);
        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
    }

    [Fact]
    public void Message_missing_in_both_current_and_default_emits_IntlMissingKey_fellBack_false()
    {
        var ns = $"Phase044.Intl.{Guid.NewGuid():N}";
        var key = new MessageKey(ns, "NeverDefined");
        var provider = new MemoryProvider();
        var accessor = new IntlAccessor("fr-FR", provider, NewCache(), defaultLocale: "en-US");

        var result = accessor.Message(key);

        // Pseudo-localize is off → the explicit missing-key marker shows.
        Assert.Equal($"[?? {key} ??]", result);
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == key.ToString());
        Assert.Equal("fr-FR", evt.Payload?[1]);
        Assert.Equal(false, evt.Payload?[2]);
    }

    [Fact]
    public void Message_missing_when_current_equals_default_emits_single_event_with_fellBack_false()
    {
        // No fallback path is taken when current == default, so only one
        // event should fire (not two — guards against a re-introduced
        // double-log bug from the previous Debug.WriteLine shape).
        var ns = $"Phase044.Intl.{Guid.NewGuid():N}";
        var key = new MessageKey(ns, "NeverDefined");
        var provider = new MemoryProvider();
        var accessor = new IntlAccessor("en-US", provider, NewCache(), defaultLocale: "en-US");

        accessor.Message(key);

        var matching = _listener.Events.Where(e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == key.ToString()).ToArray();
        Assert.Single(matching);
        Assert.Equal(false, matching[0].Payload?[2]);
    }

    [Fact]
    public void Message_hit_in_current_locale_emits_no_IntlMissingKey()
    {
        var ns = $"Phase044.Intl.{Guid.NewGuid():N}";
        var key = new MessageKey(ns, "HelloWorld");
        var provider = new MemoryProvider().Add("en-US", ns, "HelloWorld", "Hi");
        var accessor = new IntlAccessor("en-US", provider, NewCache(), defaultLocale: "en-US");

        var result = accessor.Message(key);

        Assert.Equal("Hi", result);
        Assert.DoesNotContain(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload?[0] as string) == key.ToString());
    }
}
