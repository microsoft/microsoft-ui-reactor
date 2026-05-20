using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Shape tests for the <c>logs</c> MCP tool handler. Exercises
/// <see cref="DevtoolsLogsTool.BuildPayload"/> directly so these stay headless.
/// </summary>
public class DevtoolsLogsToolTests
{
    [Fact]
    public void Payload_ReturnsStructuredError_WhenCaptureDisabled()
    {
        var ex = Assert.Throws<McpToolException>(() => DevtoolsLogsTool.BuildPayload(null, null));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
    }

    [Fact]
    public void Payload_EchoesEntries_WhenBufferHasData()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "line-1");
        buf.Append(LogSource.Stderr, null, "line-2");

        var payload = DevtoolsLogsTool.BuildPayload(buf, null);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("stdout", entries[0].GetProperty("source").GetString());
        Assert.Equal("stderr", entries[1].GetProperty("source").GetString());
        Assert.Equal(3L, doc.RootElement.GetProperty("nextSeq").GetInt64());
    }

    [Fact]
    public void Payload_FiltersBySource_FromParams()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "out");
        buf.Append(LogSource.Debug, null, "dbg");

        var args = JsonDocument.Parse("{\"source\":\"debug\"}").RootElement;
        var payload = DevtoolsLogsTool.BuildPayload(buf, args);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("dbg", entries[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Payload_RejectsUnknownSource()
    {
        var buf = new LogCaptureBuffer();
        var args = JsonDocument.Parse("{\"source\":\"bogus\"}").RootElement;
        var ex = Assert.Throws<McpToolException>(() => DevtoolsLogsTool.BuildPayload(buf, args));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
    }

    [Fact]
    public void Payload_AcceptsSourceEvent_AndExposesEventNameAndId()
    {
        // Spec 044 §6.3 — `source=event` filters to ETW-sourced entries and
        // surfaces eventName/eventId. Existing source values still work.
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "stdout-ignored");
        buf.Append(LogSource.Event, "Warning",
            text: "SwallowedError category=Hosting operation=test",
            eventName: "SwallowedError",
            eventId: 16);

        var args = JsonDocument.Parse("{\"source\":\"event\"}").RootElement;
        var payload = DevtoolsLogsTool.BuildPayload(buf, args);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        var e = entries[0];
        Assert.Equal("event", e.GetProperty("source").GetString());
        Assert.Equal("SwallowedError", e.GetProperty("eventName").GetString());
        Assert.Equal(16, e.GetProperty("eventId").GetInt32());
        Assert.Equal("Warning", e.GetProperty("level").GetString());
    }

    [Fact]
    public void Payload_AcceptsEtwAlias_ForSource()
    {
        // `etw` is accepted as an alias for `event` so tooling that came in
        // from a non-MCP ETW background still discovers the filter.
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Event, "Info", "X", eventName: "X", eventId: 1);

        var args = JsonDocument.Parse("{\"source\":\"etw\"}").RootElement;
        var payload = DevtoolsLogsTool.BuildPayload(buf, args);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void Payload_SinceCursor_AdvancesEachCall()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "a");
        buf.Append(LogSource.Stdout, null, "b");

        var page1 = DevtoolsLogsTool.BuildPayload(buf, null);
        using var doc1 = JsonDocument.Parse(JsonSerializer.Serialize(page1));
        long nextSeq = doc1.RootElement.GetProperty("nextSeq").GetInt64();

        buf.Append(LogSource.Stdout, null, "c");

        // Inclusive semantics: pass the previous response's nextSeq directly.
        var args = JsonDocument.Parse($"{{\"since\":{nextSeq}}}").RootElement;
        var page2 = DevtoolsLogsTool.BuildPayload(buf, args);
        var json = JsonSerializer.Serialize(page2);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("c", entries[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Payload_AcceptsLongSince_AboveInt32Max()
    {
        // A long-running process can emit > int.MaxValue log entries. The
        // handler must accept a long `since` or follow breaks after 2B lines.
        var buf = new LogCaptureBuffer();
        long huge = (long)int.MaxValue + 100L;
        var args = JsonDocument.Parse($"{{\"since\":{huge}}}").RootElement;
        var payload = DevtoolsLogsTool.BuildPayload(buf, args);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        // No throw; empty entries; nextSeq is the buffer's _nextSeq (1).
        Assert.Equal(0, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void Payload_TraceSourceIsAliasForDebug()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Debug, null, "from-debug");

        var args = JsonDocument.Parse("{\"source\":\"trace\"}").RootElement;
        var payload = DevtoolsLogsTool.BuildPayload(buf, args);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.Equal(1, doc.RootElement.GetProperty("entries").GetArrayLength());
    }
}
