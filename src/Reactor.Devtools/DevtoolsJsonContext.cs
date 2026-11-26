using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Source-generated JSON metadata for the preview capture server's <c>/references</c>
/// payload (the VS Code panel's References overlay), so it serializes without reflection
/// and stays NativeAOT-safe. The MCP payloads that used to live here went with the MCP server.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReferenceGraphResult))]
internal partial class DevtoolsJsonContext : JsonSerializerContext
{
}