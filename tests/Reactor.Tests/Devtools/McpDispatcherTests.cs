using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Integration coverage for the JSON-RPC → tool-registry dispatch pipeline.
/// Uses a purely in-memory tool registry so the dispatcher is exercised end-to-end
/// without needing a running WinUI window or HTTP listener.
/// </summary>
public class McpDispatcherTests
{
    private static McpDispatcher BuildDispatcher(out McpToolRegistry reg)
    {
        reg = new McpToolRegistry();
        reg.Register(
            new McpToolDescriptor("echo", "Echo the input text back.", new { type = "object" }),
            @params => new { echoed = DevtoolsTools.ReadString(@params, "text") });
        reg.Register(
            new McpToolDescriptor("badparam", "Always complains about params.", new { type = "object" }),
            _ => throw new McpToolException(
                "required",
                JsonRpcErrorCodes.InvalidParams,
                new { code = "missing-field", field = "name" }));
        var captured = reg;
        return new McpDispatcher(captured);
    }

    [Fact]
    public void ToolsList_ReturnsRegisteredDescriptors()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Null(resp.Error);
        Assert.NotNull(resp.Result);
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"name\":\"echo\"", json);
        Assert.Contains("\"name\":\"badparam\"", json);
    }

    [Fact]
    public void ToolsList_EmitsRegisteredInputSchema()
    {
        // Regression: tools/list must echo each tool's registered inputSchema
        // (not an empty object) so MCP clients can discover argument shapes.
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"inputSchema\":{\"type\":\"object\"}", json);
        Assert.DoesNotContain("\"inputSchema\":{}", json);
    }

    [Fact]
    public void ToolsList_IncludesSelectorGrammarAndTreeSchemaVersion()
    {
        // Agents that read tools/list directly (no GET /mcp) still want the
        // selector grammar without having to read the source. Embed it as an
        // underscore-prefixed extension so strict MCP clients ignore it.
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"_selectorGrammar\"", json);
        Assert.Contains("Node id", json);
        Assert.Contains("AutomationName", json);
        Assert.Contains("TypePath", json);
        Assert.Contains("Reactor source", json);
        Assert.Contains("\"_treeSchemaVersion\":\"reactor-tree/1\"", json);
    }

    [Fact]
    public void ToolsCall_RoutesToHandler()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}""");

        Assert.Null(resp.Error);
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"echoed\":\"hi\"", json);
    }

    [Fact]
    public void DirectMethod_AlsoRoutesToHandler()
    {
        // Bare method name (e.g. `"method":"echo"`) is the legacy form; we keep it
        // working so both shapes reach the same handler.
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":2,"method":"echo","params":{"text":"direct"}}""");

        Assert.Null(resp.Error);
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"echoed\":\"direct\"", json);
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":3,"method":"nope"}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, resp.Error!.Code);
    }

    [Fact]
    public void ToolsCall_MissingNameField_IsInvalidParams()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{}}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
    }

    [Fact]
    public void ToolHandlerException_ShapesAsStructuredError()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"badparam","arguments":{}}}""");

        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
        var dataJson = JsonSerializer.Serialize(resp.Error.Data, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"missing-field\"", dataJson);
    }

    [Fact]
    public void MalformedJson_ReturnsParseError_WithNullId()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("{ not json");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.ParseError, resp.Error!.Code);
        Assert.Null(resp.Id);
    }

    [Fact]
    public void InvalidRequest_WrongJsonRpcVersion_Errors()
    {
        var d = BuildDispatcher(out _);
        var resp = d.Dispatch("""{"jsonrpc":"1.0","id":6,"method":"echo"}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, resp.Error!.Code);
    }
}
