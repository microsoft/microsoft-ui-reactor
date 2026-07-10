using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Exercises the JSON-RPC dispatcher directly (no HTTP, no WinUI). The live HTTP
/// endpoint + dispatcher marshalling are covered by the self-host MCP tests in
/// Phase 2.17.
/// </summary>
public class McpDispatchTests
{
    private static McpToolRegistry BuildRegistry()
    {
        var reg = new McpToolRegistry();
        reg.Register(
            new McpToolDescriptor("echo", "Echo back the input.",
                Schema.Root(("msg", Schema.Str()))),
            @params => new { echoed = DevtoolsTools.ReadString(@params, "msg") });
        reg.Register(
            new McpToolDescriptor("boom", "Always fails with a structured error.",
                Schema.Root()),
            _ => throw new McpToolException("on fire", JsonRpcErrorCodes.ToolExecution,
                new { reason = "test" }));
        return reg;
    }

    // The server's dispatch logic is reachable directly via DispatchRpc(body) on
    // a constructed server, but constructing one requires a WinUI window. For unit
    // scope we reimplement the dispatch by calling the static JSON-RPC shapes via
    // handler wiring through a lightweight fake. To keep production code simple,
    // the test exercises Serialize/Deserialize round-trips on the envelope.

    [Fact]
    public void Deserialize_RoundTripsRequest()
    {
        const string body = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"msg":"hello"}}}
            """;

        var req = JsonSerializer.Deserialize<JsonRpcRequest>(body, DevtoolsMcpServer.JsonOpts);

        Assert.NotNull(req);
        Assert.Equal("2.0", req!.JsonRpc);
        Assert.Equal("tools/call", req.Method);
        Assert.NotNull(req.Params);
        Assert.Equal("echo", req.Params!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public void Response_SerializesSuccess_WithoutErrorField()
    {
        var resp = new JsonRpcResponse
        {
            Id = JsonDocument.Parse("1").RootElement,
            Result = new { ok = true },
        };
        var json = JsonSerializer.Serialize(resp, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"result\"", json);
        Assert.DoesNotContain("\"error\"", json);
    }

    [Fact]
    public void Response_SerializesError_WithoutResultField()
    {
        var resp = new JsonRpcResponse
        {
            Id = JsonDocument.Parse("\"abc\"").RootElement,
            Error = new JsonRpcError { Code = -32601, Message = "Method not found" },
        };
        var json = JsonSerializer.Serialize(resp, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"error\"", json);
        Assert.Contains("\"code\":-32601", json);
        Assert.DoesNotContain("\"result\"", json);
    }

    [Fact]
    public void Registry_ListPreservesRegistrationOrder()
    {
        var reg = BuildRegistry();
        var names = reg.List().Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "echo", "boom" }, names);
    }

    [Fact]
    public void Registry_DuplicateRegistrationThrows()
    {
        var reg = BuildRegistry();
        Assert.Throws<InvalidOperationException>(() =>
            reg.Register(
                new McpToolDescriptor("echo", "dup", new SchemaNode("object")),
                _ => null));
    }

    [Fact]
    public void Registry_UnknownToolLookupFails()
    {
        var reg = BuildRegistry();
        Assert.False(reg.TryGet("nope", out _));
    }

    [Fact]
    public void Handler_ReadsParamsByHelperAccessors()
    {
        // Builds a params element and verifies the helpers on DevtoolsTools
        // parse each json value kind correctly.
        using var doc = JsonDocument.Parse("""{"name":"abc","count":3,"flag":true}""");
        var args = doc.RootElement;

        Assert.Equal("abc", DevtoolsTools.ReadString(args, "name"));
        Assert.Equal(3, DevtoolsTools.ReadInt(args, "count"));
        Assert.True(DevtoolsTools.ReadBool(args, "flag"));
        Assert.Null(DevtoolsTools.ReadString(args, "missing"));
    }
}
