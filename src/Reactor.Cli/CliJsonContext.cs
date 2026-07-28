using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Devtools;
using Microsoft.UI.Reactor.Cli.Figma;

namespace Microsoft.UI.Reactor.Cli;

// System.Text.Json source-generated (reflection-free / NativeAOT-safe) metadata
// for every DTO the CLI serializes. Using the generated JsonTypeInfo overloads
// (JsonSerializer.Serialize(value, CliJsonContext.Default.Type)) keeps `mur`
// trim/AOT-clean instead of falling back to the reflection-based serializer.
//
// Compact context: WhenWritingNull matches the previous hand-rolled options and
// keeps JSONL rows / JSON-RPC envelopes free of explicit null members.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TraceWriter.TraceRow))]
[JsonSerializable(typeof(TraceWriter.CommandRow))]
[JsonSerializable(typeof(TraceWriter.RuleSelfDisabledRow))]
[JsonSerializable(typeof(TraceWriter.RuleFiredRow))]
[JsonSerializable(typeof(Telemetry.TelemetryRow))]
[JsonSerializable(typeof(LockfileEntry))]
[JsonSerializable(typeof(ToolCallRequest))]
[JsonSerializable(typeof(MethodRequest))]
[JsonSerializable(typeof(ScreenshotMeta))]
[JsonSerializable(typeof(FigmaEvent))]
internal partial class CliJsonContext : JsonSerializerContext
{
}

// Indented context: the `mur devtools --print-config` MCP client-config snippets
// are pretty-printed for humans to paste into settings files.
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpServersConfig))]
[JsonSerializable(typeof(McpServersAltConfig))]
internal partial class CliJsonIndentedContext : JsonSerializerContext
{
}
