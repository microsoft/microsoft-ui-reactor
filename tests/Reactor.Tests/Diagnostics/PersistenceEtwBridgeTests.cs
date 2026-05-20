using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase C §4.7 — regression guard that the persistence layer
/// (<see cref="JsonFileStore"/>, <see cref="PackagedSettingsStore"/>,
/// <see cref="WindowPlacementCodec"/>) routes its swallowed exceptions
/// through <c>DiagnosticLog.SwallowedError(LogCategory.Persistence, ...)</c>
/// and its explicit rejection paths through the typed
/// <c>PersistenceRejected</c> event under <see cref="ReactorEventSource.Keywords.Persistence"/>.
///
/// PII discipline (§6.2.1): file paths are never on the ETW payload. The
/// <c>storeKind</c> field is a short developer-authored label
/// (<c>"json-file"</c>, <c>"packaged-settings"</c>, <c>"placement"</c>);
/// rejection <c>reason</c> labels are similarly bounded.
/// </summary>
public class PersistenceEtwBridgeTests : IDisposable
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
    private readonly string _path;

    public PersistenceEtwBridgeTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Persistence | ReactorEventSource.Keywords.Errors);
        _path = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"reactor-windows-persist-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
        try { if (global::System.IO.File.Exists(_path)) global::System.IO.File.Delete(_path); } catch { }
    }

    private static EventWrittenEventArgs? FindByName(IReadOnlyList<EventWrittenEventArgs> events, string name)
        => events.FirstOrDefault(e => e.EventName == name);

    // ── JsonFileStore round-trip emits Read + Write ─────────────────────

    [Fact]
    public void JsonFileStore_Write_emits_PersistenceWrite_with_storeKind_no_path()
    {
        var store = new JsonFileStore(_path);

        store.Write("main", new byte[] { 1, 2, 3 });

        var evt = FindByName(_listener.Events, nameof(ReactorEventSource.PersistenceWrite));
        Assert.NotNull(evt);
        Assert.Equal("json-file", evt!.Payload?[0]);
        Assert.True((int)(evt.Payload?[1] ?? 0) > 0);
        // PII: serialized payload size is on the event, but the file path
        // must not appear anywhere on the payload list.
        Assert.DoesNotContain(_listener.Events, e =>
            e.Payload?.Any(p => p is string s && s.Contains(_path, StringComparison.OrdinalIgnoreCase)) == true);
    }

    [Fact]
    public void JsonFileStore_TryRead_emits_PersistenceRead_on_hit()
    {
        var store = new JsonFileStore(_path);
        store.Write("main", new byte[] { 7, 8, 9 });

        Assert.True(store.TryRead("main", out var data));
        Assert.NotNull(data);

        var evt = FindByName(_listener.Events, nameof(ReactorEventSource.PersistenceRead));
        Assert.NotNull(evt);
        Assert.Equal("json-file", evt!.Payload?[0]);
    }

    // ── JsonFileStore explicit rejects → PersistenceRejected ────────────

    [Fact]
    public void JsonFileStore_oversize_file_emits_PersistenceRejected_oversize_read()
    {
        var oversize = new byte[(int)(JsonFileStore.MaxFileSizeBytes + 64)];
        global::System.IO.File.WriteAllBytes(_path, oversize);

        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out _));

        var evt = FindByName(_listener.Events, nameof(ReactorEventSource.PersistenceRejected));
        Assert.NotNull(evt);
        Assert.Equal("json-file", evt!.Payload?[0]);
        Assert.Equal("oversize-read", evt.Payload?[1]);
    }

    // ── JsonFileStore malformed inputs → SwallowedError ─────────────────

    [Fact]
    public void JsonFileStore_malformed_json_emits_SwallowedError_JsonException()
    {
        global::System.IO.File.WriteAllText(_path, "this is not json{{{");
        var store = new JsonFileStore(_path);

        Assert.False(store.TryRead("main", out _));

        var evt = FindByName(_listener.Events, nameof(ReactorEventSource.SwallowedError));
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Persistence), evt!.Payload?[0]);
        Assert.Equal("JsonFileStore.TryRead.parse", evt.Payload?[1]);
        // The payload carries ex.GetType().Name — the concrete runtime
        // type, which for malformed JSON is the JsonException-derived
        // JsonReaderException. Assert by IsAssignableFrom-style prefix
        // so we don't pin to a private internal name.
        var thrown = evt.Payload?[2] as string;
        Assert.NotNull(thrown);
        Assert.StartsWith("Json", thrown);
        Assert.EndsWith("Exception", thrown);
        // PII: malformed body must not appear in the payload.
        Assert.DoesNotContain("not json", string.Join("|", evt.Payload?.OfType<string>() ?? Array.Empty<string>()));
    }

    [Fact]
    public void JsonFileStore_malformed_base64_emits_SwallowedError_FormatException()
    {
        global::System.IO.File.WriteAllText(_path, "{\"main\":\"not_valid_base64!@#\"}");
        var store = new JsonFileStore(_path);

        Assert.False(store.TryRead("main", out _));

        var evt = _listener.Events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload?[1] as string) == "JsonFileStore.TryRead.base64");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Persistence), evt!.Payload?[0]);
        Assert.Equal(nameof(FormatException), evt.Payload?[2]);
    }

    // ── PackagedSettingsStore (unpackaged context throws WinRT) ─────────

    [Fact]
    public void PackagedSettingsStore_TryRead_in_unpackaged_emits_SwallowedError()
    {
        // xUnit host has no package identity → ApplicationData.Current
        // throws InvalidOperationException (0x80073D54). The narrow catch
        // must route through DiagnosticLog and never propagate.
        var store = new PackagedSettingsStore();

        var result = store.TryRead("anything", out _);

        Assert.False(result);
        var evt = _listener.Events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload?[1] as string) == "PackagedSettingsStore.TryRead");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Persistence), evt!.Payload?[0]);
    }

    [Fact]
    public void PackagedSettingsStore_Write_in_unpackaged_emits_SwallowedError()
    {
        var store = new PackagedSettingsStore();

        store.Write("anything", new byte[] { 1, 2, 3 });

        var evt = _listener.Events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload?[1] as string) == "PackagedSettingsStore.Write");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Persistence), evt!.Payload?[0]);
    }

    // ── WindowPlacementCodec rejects ────────────────────────────────────

    [Fact]
    public void WindowPlacementCodec_implausible_monitor_count_emits_PersistenceRejected()
    {
        // Hand-craft a payload that decodes to a too-large monitor count
        // (encoded as int32 = 999, which exceeds the 64-monitor cap).
        using var ms = new global::System.IO.MemoryStream();
        using var bw = new global::System.IO.BinaryWriter(ms);
        bw.Write(999);
        bw.Flush();

        var monitors = new[] { new MonitorRect(null, 0, 0, 1920, 1080) };
        Assert.False(WindowPlacementCodec.Restore(hwnd: 0, ms.ToArray(), monitors));

        var evt = _listener.Events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.PersistenceRejected)
            && (e.Payload?[1] as string) == "implausible-monitor-count");
        Assert.NotNull(evt);
        Assert.Equal("placement", evt!.Payload?[0]);
    }

    [Fact]
    public void WindowPlacementCodec_truncated_payload_emits_PersistenceRejected_truncated()
    {
        // Payload claims 1 monitor but contains nothing past the count.
        using var ms = new global::System.IO.MemoryStream();
        using var bw = new global::System.IO.BinaryWriter(ms);
        bw.Write(1);
        bw.Flush();

        var monitors = new[] { new MonitorRect(null, 0, 0, 1920, 1080) };
        Assert.False(WindowPlacementCodec.Restore(hwnd: 0, ms.ToArray(), monitors));

        var evt = _listener.Events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.PersistenceRejected)
            && (e.Payload?[1] as string) == "truncated");
        Assert.NotNull(evt);
        Assert.Equal("placement", evt!.Payload?[0]);
    }
}
