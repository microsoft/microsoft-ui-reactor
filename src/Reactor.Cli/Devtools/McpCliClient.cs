using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>JSON-RPC request envelope for a <c>tools/call</c> (name + arguments).</summary>
internal sealed record ToolCallParams(string name, JsonElement arguments);
internal sealed record ToolCallRequest(string jsonrpc, int id, string method, ToolCallParams @params);
/// <summary>JSON-RPC request envelope for a bare method with opaque params.</summary>
internal sealed record MethodRequest(string jsonrpc, int id, string method, JsonElement? @params);

/// <summary>
/// Thin JSON-RPC client for the devtools MCP endpoint. One
/// <see cref="InvokeTool"/> entry point; everything else in the CLI
/// (named verbs, the generic <c>call</c> escape hatch) layers on top.
/// Spec 025 §4.
/// </summary>
// <snippet:mcp-cli-client>
internal sealed class McpCliClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string? _token;

    public McpCliClient(string endpoint, TimeSpan? timeout = null, string? token = null)
    {
        _endpoint = endpoint;
        _token = token;
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new global::System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }
    // </snippet:mcp-cli-client>

    public void Dispose() => _http.Dispose();

    public JsonDocument InvokeTool(string toolName, JsonElement? arguments)
    {
        // Empty fallback needs a self-owned JsonElement. Using
        // `JsonDocument.Parse("{}").RootElement` directly would leave the
        // element tied to a document the GC is free to reclaim.
        using var emptyDoc = JsonDocument.Parse("{}");
        var payload = new ToolCallRequest(
            jsonrpc: "2.0",
            id: 1,
            method: "tools/call",
            @params: new ToolCallParams(toolName, arguments ?? emptyDoc.RootElement.Clone()));
        return Post(payload, CliJsonContext.Default.ToolCallRequest);
    }

    public JsonDocument InvokeMethod(string method, JsonElement? @params)
    {
        var payload = new MethodRequest(jsonrpc: "2.0", id: 1, method: method, @params: @params);
        return Post(payload, CliJsonContext.Default.MethodRequest);
    }

    private JsonDocument Post<T>(T payload, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = _http.PostAsync(_endpoint, content).GetAwaiter().GetResult();
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        // Non-success status: surface as a transport error so the verb's
        // exit-code mapping treats it consistently (exit 2). A 500 from the
        // server with an HTML error page would otherwise throw JsonException
        // at Parse and bypass the mapping.
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MCP {_endpoint} returned HTTP {(int)resp.StatusCode} {resp.StatusCode}" +
                (string.IsNullOrWhiteSpace(body) ? "" : $": {body}"));
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException(
                $"MCP {_endpoint} returned a non-JSON response body.", ex);
        }
    }
}
