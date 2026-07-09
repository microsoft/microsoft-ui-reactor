using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Cli;
using Microsoft.UI.Reactor.Cli.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Contract tests for the CLI's devtools JSON paths that moved from the
/// reflection-based <c>JsonSerializer</c> to source-generated
/// <see cref="CliJsonContext"/> / hand-built <see cref="JsonNode"/> trees
/// (AOT-cleanup for PR "treat warnings as errors"). These pin the exact wire
/// shape so the migration is provably behavior-preserving: every assertion here
/// held against the previous reflection-based implementation too.
/// </summary>
public class CliJsonContractTests
{
    // ── ArgsFromDict: Dictionary<string, object?> → JsonElement ──────────────

    [Fact]
    public void ArgsFromDict_Emits_Primitives_And_Drops_Nulls()
    {
        var args = DevtoolsVerbs.ArgsFromDict(new Dictionary<string, object?>
        {
            ["selector"] = "#root",
            ["clear"] = true,
            ["window"] = null, // must be omitted
        });

        Assert.Equal(JsonValueKind.Object, args.ValueKind);
        Assert.Equal("#root", args.GetProperty("selector").GetString());
        Assert.True(args.GetProperty("clear").GetBoolean());
        Assert.False(args.TryGetProperty("window", out _));
    }

    [Fact]
    public void ArgsFromDict_Supports_Nested_JsonObject()
    {
        // Mirrors `scroll --by H,V` which sets fields["by"] = new JsonObject{...}.
        var args = DevtoolsVerbs.ArgsFromDict(new Dictionary<string, object?>
        {
            ["selector"] = "#list",
            ["by"] = new JsonObject { ["horizontal"] = 12.5, ["vertical"] = 40.0 },
        });

        var by = args.GetProperty("by");
        Assert.Equal(JsonValueKind.Object, by.ValueKind);
        Assert.Equal(12.5, by.GetProperty("horizontal").GetDouble());
        Assert.Equal(40.0, by.GetProperty("vertical").GetDouble());
    }

    [Fact]
    public void ArgsFromDict_Passes_Through_JsonElement_Values()
    {
        using var inner = JsonDocument.Parse("""{"a":1,"b":["x","y"]}""");
        var args = DevtoolsVerbs.ArgsFromDict(new Dictionary<string, object?>
        {
            ["payload"] = inner.RootElement.Clone(),
        });

        var payload = args.GetProperty("payload");
        Assert.Equal(1, payload.GetProperty("a").GetInt32());
        Assert.Equal("y", payload.GetProperty("b")[1].GetString());
    }

    [Fact]
    public void ArgsFromDict_Empty_Is_Empty_Object()
    {
        var args = DevtoolsVerbs.ArgsFromDict([]);
        Assert.Equal(JsonValueKind.Object, args.ValueKind);
        Assert.False(args.EnumerateObject().MoveNext());
    }

    // ── WriteElement: JsonElement → string (compact / indented) ─────────────

    [Fact]
    public void WriteElement_Compact_Has_No_Whitespace()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":"x"}""");
        var s = DevtoolsVerbs.WriteElement(doc.RootElement, indented: false);
        Assert.Equal("""{"a":1,"b":"x"}""", s);
    }

    [Fact]
    public void WriteElement_Indented_Preserves_Values_With_Newlines()
    {
        using var doc = JsonDocument.Parse("""{"a":1}""");
        var s = DevtoolsVerbs.WriteElement(doc.RootElement, indented: true);
        Assert.Contains("\n", s);
        // Re-parse to prove it's still the same document, just formatted.
        using var round = JsonDocument.Parse(s);
        Assert.Equal(1, round.RootElement.GetProperty("a").GetInt32());
    }

    // ── JSON-RPC envelope (McpCliClient records via CliJsonContext) ─────────

    [Fact]
    public void ToolCallRequest_Serializes_JsonRpc_Envelope()
    {
        using var argsDoc = JsonDocument.Parse("""{"selector":"#x"}""");
        var req = new ToolCallRequest("2.0", 1, "tools/call",
            new ToolCallParams("tree", argsDoc.RootElement.Clone()));

        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.ToolCallRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("tools/call", root.GetProperty("method").GetString());
        var p = root.GetProperty("params");
        Assert.Equal("tree", p.GetProperty("name").GetString());
        Assert.Equal("#x", p.GetProperty("arguments").GetProperty("selector").GetString());
    }

    [Fact]
    public void MethodRequest_Drops_Null_Params()
    {
        var req = new MethodRequest("2.0", 1, "ping", @params: null);
        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.MethodRequest);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ping", doc.RootElement.GetProperty("method").GetString());
        Assert.False(doc.RootElement.TryGetProperty("params", out _));
    }

    [Fact]
    public void MethodRequest_Keeps_Present_Params()
    {
        using var pDoc = JsonDocument.Parse("""{"k":true}""");
        var req = new MethodRequest("2.0", 7, "resources/list", pDoc.RootElement.Clone());
        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.MethodRequest);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(7, doc.RootElement.GetProperty("id").GetInt32());
        Assert.True(doc.RootElement.GetProperty("params").GetProperty("k").GetBoolean());
    }

    // ── ScreenshotMeta ──────────────────────────────────────────────────────

    [Fact]
    public void ScreenshotMeta_Emits_Present_Values_And_Drops_Absent()
    {
        using var dims = JsonDocument.Parse("""{"w":800,"h":600}""");
        var meta = new ScreenshotMeta(
            width: dims.RootElement.GetProperty("w"),
            height: dims.RootElement.GetProperty("h"),
            bounds: null);

        var json = JsonSerializer.Serialize(meta, CliJsonContext.Default.ScreenshotMeta);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(800, doc.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(600, doc.RootElement.GetProperty("height").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("bounds", out _));
    }

    // ── FigmaEvent (the `@event` member must serialize as `event`) ──────────

    [Fact]
    public void FigmaEvent_Serializes_Event_Key_And_Drops_Null_NodeId()
    {
        var evt = new global::Microsoft.UI.Reactor.Cli.Figma.FigmaEvent(
            @event: "updated",
            fileKey: "abc",
            nodeId: null,
            fileName: "Design",
            lastModified: "2026-01-01T00:00:00.0000000",
            version: "1",
            figmaUrl: "https://www.figma.com/design/abc");

        var json = JsonSerializer.Serialize(evt, CliJsonContext.Default.FigmaEvent);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("updated", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("abc", doc.RootElement.GetProperty("fileKey").GetString());
        Assert.False(doc.RootElement.TryGetProperty("nodeId", out _));
    }
}
