using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Devtools;

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
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments ?? emptyDoc.RootElement.Clone(),
            },
        };
        return Post(payload);
    }

    public JsonDocument InvokeMethod(string method, JsonElement? @params)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = @params,
        };
        return Post(payload);
    }

    private JsonDocument Post(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
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
