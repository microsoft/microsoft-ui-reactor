using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Source-generated JSON serialization metadata for the devtools / MCP subsystem.
/// Registered on <see cref="DevtoolsMcpServer.JsonOpts"/> so the serializer
/// can resolve types at compile time, enabling Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(ScreenshotResult))]
[JsonSerializable(typeof(McpErrorData))]
[JsonSerializable(typeof(FireOkResult))]
[JsonSerializable(typeof(StateResult))]
[JsonSerializable(typeof(LogsResult))]
[JsonSerializable(typeof(DockListResult))]
[JsonSerializable(typeof(DockSnapshotResult))]
[JsonSerializable(typeof(DockActionResult))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(ResourcesListResult))]
[JsonSerializable(typeof(PromptsListResult))]
[JsonSerializable(typeof(InitializeResult))]
internal partial class DevtoolsJsonContext : JsonSerializerContext
{
}
