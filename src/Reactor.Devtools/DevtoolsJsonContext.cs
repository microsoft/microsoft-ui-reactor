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
// NOTE: StateResult is intentionally NOT registered here. Its HookSnapshot.Value
// is an `object` holding arbitrary { $type, $shape } Dictionary<string, object?>
// maps (see DevtoolsStateTool.ShapeValue). Source-gen can't serve those nested
// dictionaries, so the state tool stays on the reflection resolver (JIT) and is
// AOT-skip-listed. Registering it makes STJ pick the source-gen path and throw
// NotSupportedException on the Dictionary value.
[JsonSerializable(typeof(LogsResult))]
[JsonSerializable(typeof(DockListResult))]
[JsonSerializable(typeof(DockSnapshotResult))]
[JsonSerializable(typeof(DockActionResult))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(ResourcesListResult))]
[JsonSerializable(typeof(PromptsListResult))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ComponentsResult))]
[JsonSerializable(typeof(ComponentInfo))]
[JsonSerializable(typeof(SwitchComponentResult))]
[JsonSerializable(typeof(ExitResult))]
[JsonSerializable(typeof(WindowsResult))]
[JsonSerializable(typeof(WindowsListResult))]
[JsonSerializable(typeof(WindowOkResult))]
[JsonSerializable(typeof(WindowCloseResult))]
[JsonSerializable(typeof(OkResult))]
[JsonSerializable(typeof(OkStateResult))]
[JsonSerializable(typeof(OkSelectedResult))]
[JsonSerializable(typeof(OkViaResult))]
[JsonSerializable(typeof(WaitForResult))]
[JsonSerializable(typeof(WaitObserved))]
[JsonSerializable(typeof(TreeResult))]
[JsonSerializable(typeof(SchemaNode))]
[JsonSerializable(typeof(SchemaDocument))]
internal partial class DevtoolsJsonContext : JsonSerializerContext
{
}
