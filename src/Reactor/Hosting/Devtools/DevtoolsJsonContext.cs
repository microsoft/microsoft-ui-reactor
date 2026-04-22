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
[JsonSerializable(typeof(LockfileEntry))]
internal partial class DevtoolsJsonContext : JsonSerializerContext
{
}
