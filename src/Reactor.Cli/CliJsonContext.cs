using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Figma;

namespace Microsoft.UI.Reactor.Cli;

// System.Text.Json source-generated (reflection-free / NativeAOT-safe) metadata
// for every DTO the CLI serializes. Using the generated JsonTypeInfo overloads
// (JsonSerializer.Serialize(value, CliJsonContext.Default.Type)) keeps `mur`
// trim/AOT-clean instead of falling back to the reflection-based serializer.
//
// Compact context: WhenWritingNull matches the previous hand-rolled options and
// keeps JSONL rows free of explicit null members.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TraceWriter.TraceRow))]
[JsonSerializable(typeof(TraceWriter.CommandRow))]
[JsonSerializable(typeof(TraceWriter.RuleSelfDisabledRow))]
[JsonSerializable(typeof(TraceWriter.RuleFiredRow))]
[JsonSerializable(typeof(Telemetry.TelemetryRow))]
[JsonSerializable(typeof(FigmaEvent))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
