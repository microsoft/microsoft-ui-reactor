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
[JsonSerializable(typeof(ComponentsResult))]
[JsonSerializable(typeof(VersionResult))]
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
[JsonSerializable(typeof(ScrollResult))]
[JsonSerializable(typeof(TreeResult))]
[JsonSerializable(typeof(SchemaNode))]
[JsonSerializable(typeof(SchemaDocument))]
[JsonSerializable(typeof(ReferenceGraphResult))]
[JsonSerializable(typeof(PropertyResult))]
[JsonSerializable(typeof(PropertiesResult))]
[JsonSerializable(typeof(SetPropertyResult))]
[JsonSerializable(typeof(ResourcesResult))]
[JsonSerializable(typeof(SetResourceResult))]
[JsonSerializable(typeof(StylesResult))]
[JsonSerializable(typeof(AncestorsResult))]
internal partial class DevtoolsJsonContext : JsonSerializerContext
{
}
