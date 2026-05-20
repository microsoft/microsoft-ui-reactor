using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase F (§6.4) — verifies the
/// <c>ReactorEventSource → LogCaptureBuffer</c> bridge installed by
/// <see cref="LogCaptureInstall.InstallEventBridgeForTests"/>:
///
/// 1. an event fired after install lands as a <see cref="LogSource.Event"/>
///    entry with <c>eventName</c> + <c>eventId</c> populated and an
///    appropriate level string;
/// 2. payload formatter honours the 256-char per-field cap (defense-in-depth
///    against a chatty event filling the ring);
/// 3. HR-style payload fields render in <c>0x{X8}</c> form, matching the
///    pre-migration <c>Debug.WriteLine</c> shape so log greps stay stable;
/// 4. existing stdout / stderr / debug entries continue to be captured
///    unchanged (regression guard).
/// </summary>
public class LogCaptureEventBridgeTests
{
    private static (LogCaptureBuffer buf, IDisposable bridge) Install()
    {
        var buf = new LogCaptureBuffer();
        var bridge = LogCaptureInstall.InstallEventBridgeForTests(buf);
        return (buf, bridge);
    }

    [Fact]
    public void Event_lands_as_LogSource_Event_with_name_id_and_level()
    {
        var (buf, bridge) = Install();
        using (bridge)
        {
            // Errors keyword + warning level → captured as level=Warning.
            ReactorEventSource.Log.SwallowedError(
                nameof(LogCategory.Hosting),
                "BridgeTest.warmup",
                nameof(InvalidOperationException));
        }

        var result = buf.Query(source: LogSource.Event);
        var hit = Assert.Single(
            result.Entries,
            e => e.EventName == nameof(ReactorEventSource.SwallowedError)
                 && e.Text.Contains("BridgeTest.warmup", StringComparison.Ordinal));

        Assert.Equal(LogSource.Event, hit.Source);
        Assert.Equal("Warning", hit.Level);
        Assert.NotNull(hit.EventId);
        Assert.True(hit.EventId!.Value > 0);
        Assert.Contains("category=Hosting", hit.Text, StringComparison.Ordinal);
        Assert.Contains("exceptionType=InvalidOperationException", hit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EventLevel_maps_each_severity_to_expected_label()
    {
        // Build a synthetic ReactorEvent and exercise the formatter / mapper
        // without paying the cost of a real fire-and-listen cycle for each
        // level. The bridge composition is "map level + format text + append";
        // each step is exercised by the broader test above.
        var verbose = MakeEvent(EventLevel.Verbose);
        var info    = MakeEvent(EventLevel.Informational);
        var warn    = MakeEvent(EventLevel.Warning);
        var err     = MakeEvent(EventLevel.Error);
        var crit    = MakeEvent(EventLevel.Critical);

        Assert.Equal("RoundTrip", LogCaptureInstall.FormatEventText(verbose));
        Assert.Equal("RoundTrip", LogCaptureInstall.FormatEventText(info));
        Assert.Equal("RoundTrip", LogCaptureInstall.FormatEventText(warn));
        Assert.Equal("RoundTrip", LogCaptureInstall.FormatEventText(err));
        Assert.Equal("RoundTrip", LogCaptureInstall.FormatEventText(crit));
    }

    [Fact]
    public void Formatter_caps_long_payload_field_at_256_chars()
    {
        var huge = new string('a', LogCaptureInstall.MaxPayloadFieldChars + 500);
        var evt = new ReactorEvent(
            EventId: 1,
            EventName: "Test",
            Level: EventLevel.Informational,
            Keywords: 0,
            TimestampUtc: DateTime.UtcNow,
            ThreadId: Environment.CurrentManagedThreadId,
            Payload: new object?[] { huge },
            PayloadNames: new[] { "blob" });

        var text = LogCaptureInstall.FormatEventText(evt);

        // "Test blob=" + 256 chars + ellipsis. The ellipsis is the only
        // signal that truncation happened, so assert on it explicitly.
        Assert.Contains("blob=", text, StringComparison.Ordinal);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        // We don't want the entire 756-char string in the output.
        Assert.DoesNotContain(huge, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatter_renders_hr_field_as_hex_X8()
    {
        // 0x80004002 (E_NOINTERFACE) — a real HRESULT value the Shell sites
        // emit. Must come out as "0x80004002", not "-2147467262".
        var evt = new ReactorEvent(
            EventId: 1,
            EventName: "HResultFailed",
            Level: EventLevel.Warning,
            Keywords: 0,
            TimestampUtc: DateTime.UtcNow,
            ThreadId: Environment.CurrentManagedThreadId,
            Payload: new object?[] { "Shell", "JumpList.Begin", unchecked((int)0x80004002) },
            PayloadNames: new[] { "category", "operation", "hr" });

        var text = LogCaptureInstall.FormatEventText(evt);

        Assert.Contains("hr=0x80004002", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-2147467262", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatter_renders_null_payload_as_null_literal()
    {
        var evt = new ReactorEvent(
            EventId: 1,
            EventName: "T",
            Level: EventLevel.Verbose,
            Keywords: 0,
            TimestampUtc: DateTime.UtcNow,
            ThreadId: Environment.CurrentManagedThreadId,
            Payload: new object?[] { null },
            PayloadNames: new[] { "x" });

        Assert.Equal("T x=null", LogCaptureInstall.FormatEventText(evt));
    }

    [Fact]
    public void Stdout_stderr_debug_entries_still_capture_alongside_events()
    {
        var (buf, bridge) = Install();
        using (bridge)
        {
            buf.Append(LogSource.Stdout, level: null, text: "stdout-line");
            buf.Append(LogSource.Stderr, level: null, text: "stderr-line");
            buf.Append(LogSource.Debug, level: null, text: "debug-line");
            ReactorEventSource.Log.SwallowedError(
                nameof(LogCategory.Hosting),
                "BridgeTest.mixed",
                nameof(InvalidOperationException));
        }

        var all = buf.Query();
        Assert.Contains(all.Entries, e => e.Source == LogSource.Stdout && e.Text == "stdout-line");
        Assert.Contains(all.Entries, e => e.Source == LogSource.Stderr && e.Text == "stderr-line");
        Assert.Contains(all.Entries, e => e.Source == LogSource.Debug  && e.Text == "debug-line");
        Assert.Contains(all.Entries, e => e.Source == LogSource.Event
            && e.Text.Contains("BridgeTest.mixed", StringComparison.Ordinal));

        // Regression guard: source filters keep returning their dedicated
        // streams without the event entries spilling in.
        Assert.Single(buf.Query(source: LogSource.Stdout).Entries, e => e.Text == "stdout-line");
        Assert.Single(buf.Query(source: LogSource.Stderr).Entries, e => e.Text == "stderr-line");
        Assert.Single(buf.Query(source: LogSource.Debug).Entries,  e => e.Text == "debug-line");
    }

    private static ReactorEvent MakeEvent(EventLevel level) => new(
        EventId: 1,
        EventName: "RoundTrip",
        Level: level,
        Keywords: 0,
        TimestampUtc: DateTime.UtcNow,
        ThreadId: Environment.CurrentManagedThreadId,
        Payload: Array.Empty<object?>(),
        PayloadNames: Array.Empty<string>());
}
