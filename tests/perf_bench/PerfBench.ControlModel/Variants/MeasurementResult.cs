using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerfBench.ControlModel.Variants;

/// <summary>
/// One row of measurement output. The §15.6 reporting aggregator consumes
/// these as JSON-Lines.
/// </summary>
public sealed record MeasurementResult
{
    public required string BenchId { get; init; }
    public required string BenchName { get; init; }
    public required BenchVariant Variant { get; init; }
    public required int Iterations { get; init; }
    public required int Repetition { get; init; }
    public required double TotalMs { get; init; }
    public required double MeanNs { get; init; }
    public required long AllocBytes { get; init; }
    public required int Gen0 { get; init; }
    public required int Gen1 { get; init; }
    public required int Gen2 { get; init; }
    public required long HeapDeltaBytes { get; init; }

    /// <summary>Optional bench-specific counter — e.g., M11 ModifierEHS count, M13 callback fires.</summary>
    public long Counter { get; init; }
    public string? CounterLabel { get; init; }

    public string Status { get; init; } = "ok";
    public string? Note { get; init; }

    // Environment stamping (§15.5)
    public required string MachineSku { get; init; }
    public required string Cpu { get; init; }
    public required string OsBuild { get; init; }
    public required string DotnetVersion { get; init; }
    public required string Architecture { get; init; }
    public required string Configuration { get; init; }
    public string PowerState { get; init; } = "unknown";
    public string PowerPlan { get; init; } = "unknown";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string ToJsonLine()
    {
        return JsonSerializer.Serialize(this, MeasurementJsonContext.Default.MeasurementResult);
    }
}

/// <summary>
/// Source-generated (trim / native-AOT safe) JSON metadata for
/// <see cref="MeasurementResult"/>. Replaces the reflection-based
/// <c>JsonSerializer.Serialize(obj, JsonSerializerOptions)</c> + runtime
/// <see cref="JsonStringEnumConverter"/> path (IL2026 / IL3050) so the bench can be
/// native-AOT published. The options here reproduce the previous serializer's output
/// exactly: <see cref="JsonSourceGenerationOptionsAttribute.UseStringEnumConverter"/>
/// emits enums as their member names (as the untyped converter did), and
/// <see cref="JsonKnownNamingPolicy.CamelCase"/> matches the former
/// <c>PropertyNamingPolicy</c>, so the emitted JSON-Lines shape is unchanged for the
/// §15.6 aggregator.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(MeasurementResult))]
internal sealed partial class MeasurementJsonContext : JsonSerializerContext;

public static class Env
{
    public static readonly string MachineSku =
        Environment.GetEnvironmentVariable("BENCH_MACHINE_SKU") ?? Environment.MachineName;

    public static readonly string Cpu = OperatingSystem.IsWindows()
        ? (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown")
        : "unknown";

    public static readonly string OsBuild = Environment.OSVersion.VersionString;
    public static readonly string DotnetVersion =
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public static readonly string Architecture =
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

#if DEBUG
    public static readonly string Configuration = "Debug";
#else
    public static readonly string Configuration = "Release";
#endif
}
