// Spec 047 §15.6 reporting aggregator.
//
// Reads JSON-Lines files produced by PerfBench.ControlModel and the macro
// scenario executables. Produces the three required tables plus a flagged
// list of non-comparable rows (environment mismatches per §15.5).
//
// CLI:
//   spec047-aggregator --in <path-or-glob> [--in <path>...] --out <dir>
//                      [--baseline <machine-sku>] [--ci-min-reps 3]
//
// Output files in --out dir:
//   summary-absolute.md      — table (a): variant-by-variant absolute values
//   summary-delta.md         — table (b): V2 vs Today % with CI
//   summary-gap.md           — table (c): V2 vs Direct absolute overhead
//   trend.csv                — flat per-(scenario,variant) row, for CI plotting
//   excluded.txt             — rows rejected for environment-metadata mismatch

using System.Globalization;
using System.Text;
using System.Text.Json;

if (args.Length == 0) { PrintUsage(); return 0; }

string outDir = "spec047-aggregator-out";
var inputs = new List<string>();
int minReps = 3;
string? baselineSku = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--in" when i + 1 < args.Length:
            inputs.Add(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            outDir = args[++i];
            break;
        case "--baseline" when i + 1 < args.Length:
            baselineSku = args[++i];
            break;
        case "--ci-min-reps" when i + 1 < args.Length:
            minReps = int.Parse(args[++i]);
            break;
        case "--help":
            PrintUsage();
            return 0;
    }
}

if (inputs.Count == 0)
{
    Console.Error.WriteLine("error: no --in inputs");
    PrintUsage();
    return 2;
}

Directory.CreateDirectory(outDir);

var rows = new List<Row>();
var excluded = new List<string>();
int total = 0;
foreach (var path in ExpandGlobs(inputs))
{
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        total++;
        Row? row;
        try
        {
            row = JsonSerializer.Deserialize<Row>(line, JsonStatics.JsonOpts);
        }
        catch (Exception ex)
        {
            excluded.Add($"{path}:?:parse-error: {ex.Message}");
            continue;
        }
        if (row is null) continue;

        var missing = RequiredFieldsMissing(row);
        if (missing is not null)
        {
            excluded.Add($"{path}:{row.BenchId}/{row.Variant}: missing {missing}");
            continue;
        }
        if (baselineSku is not null && !string.Equals(row.MachineSku, baselineSku, StringComparison.OrdinalIgnoreCase))
        {
            excluded.Add($"{path}:{row.BenchId}/{row.Variant}: machineSku={row.MachineSku} != baseline {baselineSku}");
            continue;
        }
        rows.Add(row);
    }
}

Console.WriteLine($"[agg] {total} rows read, {rows.Count} retained, {excluded.Count} excluded");

// Group rows by (BenchId, Variant, Architecture). Architecture is the
// load-bearing axis at Phase 0 — ARM64-native and x64-emulated runs share
// the same MachineSku (Snapdragon X laptops run both) and silently mixing
// them produces meaningless means. Spec §15.5 requires non-comparable rows
// to be flagged.
//
// LockedRefreshHz / PowerState stamping is deferred to Phase 1 once the
// macro harness emits those fields; once available they join this key.
var groups = rows.GroupBy(r => (r.BenchId, r.Variant, r.Architecture)).ToList();

// Table (a): absolute comparison.
EmitAbsoluteTable(Path.Combine(outDir, "summary-absolute.md"), groups, minReps);

// Table (b): Reactor delta (V2 vs Today).
EmitDeltaTable(Path.Combine(outDir, "summary-delta.md"), groups, minReps);

// Table (c): WinUI gap (V2 vs Direct).
EmitGapTable(Path.Combine(outDir, "summary-gap.md"), groups, minReps);

// Trend CSV.
EmitTrendCsv(Path.Combine(outDir, "trend.csv"), rows);

File.WriteAllLines(Path.Combine(outDir, "excluded.txt"), excluded);

Console.WriteLine($"[agg] wrote {outDir}/{{summary-absolute,summary-delta,summary-gap,trend,excluded}}");
return 0;

// ─── helpers ────────────────────────────────────────────────────────────────

static void PrintUsage()
{
    Console.WriteLine(@"spec047-aggregator — read PerfBench.ControlModel JSON-Lines and emit §15.6 tables.

usage:
  spec047-aggregator --in <path or glob> [--in <path>...] --out <dir>
                     [--baseline <machine-sku>] [--ci-min-reps N]

Inputs: one or more results.jsonl files (or globs). Each line is a
MeasurementResult record. Rows missing required environment metadata
(machineSku/cpu/osBuild/dotnetVersion/architecture) are written to
excluded.txt and not included in the comparison tables.");
}

static IEnumerable<string> ExpandGlobs(List<string> inputs)
{
    foreach (var pattern in inputs)
    {
        if (File.Exists(pattern))
        {
            yield return pattern;
            continue;
        }
        // simple glob — directory + pattern
        var dir = Path.GetDirectoryName(pattern);
        var leaf = Path.GetFileName(pattern);
        if (string.IsNullOrEmpty(dir)) dir = ".";
        if (!Directory.Exists(dir)) continue;
        foreach (var f in Directory.EnumerateFiles(dir, leaf, SearchOption.AllDirectories))
            yield return f;
    }
}

static string? RequiredFieldsMissing(Row r)
{
    var missing = new List<string>();
    if (string.IsNullOrEmpty(r.MachineSku)) missing.Add("machineSku");
    if (string.IsNullOrEmpty(r.Cpu)) missing.Add("cpu");
    if (string.IsNullOrEmpty(r.OsBuild)) missing.Add("osBuild");
    if (string.IsNullOrEmpty(r.DotnetVersion)) missing.Add("dotnetVersion");
    if (string.IsNullOrEmpty(r.Architecture)) missing.Add("architecture");
    return missing.Count == 0 ? null : string.Join(",", missing);
}

static (double mean, double ciHalfWidth) MeanCi95(IEnumerable<double> values)
{
    var arr = values.ToArray();
    if (arr.Length == 0) return (0, 0);
    var mean = arr.Average();
    if (arr.Length < 2) return (mean, 0);
    var variance = arr.Sum(x => (x - mean) * (x - mean)) / (arr.Length - 1);
    var stderr = Math.Sqrt(variance / arr.Length);
    // 95% CI multiplier ~1.96 for large N; t-distribution would be tighter
    // for N=3-5 but we keep z=1.96 for clarity. Operators wanting tighter
    // bounds run more reps.
    return (mean, 1.96 * stderr);
}

// Each row is keyed by (BenchId, Architecture) so ARM64-native and
// x64-emulated runs render as separate rows. The variant column lookup
// still works inside each (bench, arch) bucket.
static IOrderedEnumerable<IGrouping<(string Bench, string Arch), IGrouping<(string, string, string), Row>>>
    ByBenchArch(List<IGrouping<(string, string, string), Row>> groups)
    => groups.GroupBy(g => (Bench: g.Key.Item1, Arch: g.Key.Item3))
             .OrderBy(g => g.Key.Bench).ThenBy(g => g.Key.Arch);

static void EmitAbsoluteTable(string path, List<IGrouping<(string, string, string), Row>> groups, int minReps)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Spec 047 §15.6 (a) — Absolute Comparison");
    sb.AppendLine();
    sb.AppendLine("Mean ns per op + alloc bytes, per variant. Columns are dashes when a variant has < min-reps repetitions. Architecture column distinguishes ARM64-native from x64-emulated runs (spec §15.5 — non-comparable across architectures).");
    sb.AppendLine();
    sb.AppendLine("| Bench | Arch | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
    foreach (var bench in ByBenchArch(groups))
    {
        var direct = bench.FirstOrDefault(g => g.Key.Item2 == "Direct");
        var today = bench.FirstOrDefault(g => g.Key.Item2 == "ReactorToday");
        var v2 = bench.FirstOrDefault(g => g.Key.Item2 == "ReactorV2");
        sb.Append("| ").Append(bench.Key.Bench).Append(" | ").Append(bench.Key.Arch).Append(' ');
        AppendCell(sb, direct, minReps, r => r.MeanNs, "F1");
        AppendCell(sb, today, minReps, r => r.MeanNs, "F1");
        AppendCell(sb, v2, minReps, r => r.MeanNs, "F1");
        AppendCell(sb, direct, minReps, r => r.AllocBytes, "F0");
        AppendCell(sb, today, minReps, r => r.AllocBytes, "F0");
        AppendCell(sb, v2, minReps, r => r.AllocBytes, "F0");
        sb.AppendLine("|");
    }
    File.WriteAllText(path, sb.ToString());
}

static void EmitDeltaTable(string path, List<IGrouping<(string, string, string), Row>> groups, int minReps)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Spec 047 §15.6 (b) — Reactor Delta (V2 vs Today)");
    sb.AppendLine();
    sb.AppendLine("Positive % = V2 slower / larger than Today. Negative = improvement. One row per (bench, architecture).");
    sb.AppendLine();
    sb.AppendLine("| Bench | Arch | ns delta % | ns 95% CI half-width | alloc delta % |");
    sb.AppendLine("|---|---|---:|---:|---:|");
    foreach (var bench in ByBenchArch(groups))
    {
        var today = bench.FirstOrDefault(g => g.Key.Item2 == "ReactorToday");
        var v2 = bench.FirstOrDefault(g => g.Key.Item2 == "ReactorV2");
        if (today is null || v2 is null) continue;
        if (today.Count() < minReps || v2.Count() < minReps) continue;

        var (todayNs, _) = MeanCi95(today.Select(r => r.MeanNs));
        var (v2Ns, v2NsCi) = MeanCi95(v2.Select(r => r.MeanNs));
        var (todayAlloc, _) = MeanCi95(today.Select(r => (double)r.AllocBytes));
        var (v2Alloc, _) = MeanCi95(v2.Select(r => (double)r.AllocBytes));

        var nsDeltaPct = todayNs == 0 ? 0 : (v2Ns - todayNs) / todayNs * 100.0;
        var allocDeltaPct = todayAlloc == 0 ? 0 : (v2Alloc - todayAlloc) / todayAlloc * 100.0;
        var ciPct = todayNs == 0 ? 0 : v2NsCi / todayNs * 100.0;

        sb.AppendLine(
            $"| {bench.Key.Bench} | {bench.Key.Arch} " +
            $"| {nsDeltaPct.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}% " +
            $"| ±{ciPct.ToString("F1", CultureInfo.InvariantCulture)}% " +
            $"| {allocDeltaPct.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}% |");
    }
    File.WriteAllText(path, sb.ToString());
}

static void EmitGapTable(string path, List<IGrouping<(string, string, string), Row>> groups, int minReps)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Spec 047 §15.6 (c) — WinUI Gap (V2 vs Direct)");
    sb.AppendLine();
    sb.AppendLine("Absolute overhead Reactor still adds on top of raw WinUI. One row per (bench, architecture).");
    sb.AppendLine();
    sb.AppendLine("| Bench | Arch | V2 ns | Direct ns | V2 - Direct ns | V2 alloc - Direct alloc |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|");
    foreach (var bench in ByBenchArch(groups))
    {
        var direct = bench.FirstOrDefault(g => g.Key.Item2 == "Direct");
        var v2 = bench.FirstOrDefault(g => g.Key.Item2 == "ReactorV2");
        if (direct is null || v2 is null) continue;
        if (direct.Count() < minReps || v2.Count() < minReps) continue;

        var (directNs, _) = MeanCi95(direct.Select(r => r.MeanNs));
        var (v2Ns, _) = MeanCi95(v2.Select(r => r.MeanNs));
        var (directAlloc, _) = MeanCi95(direct.Select(r => (double)r.AllocBytes));
        var (v2Alloc, _) = MeanCi95(v2.Select(r => (double)r.AllocBytes));

        sb.AppendLine(
            $"| {bench.Key.Bench} | {bench.Key.Arch} " +
            $"| {v2Ns.ToString("F1", CultureInfo.InvariantCulture)} " +
            $"| {directNs.ToString("F1", CultureInfo.InvariantCulture)} " +
            $"| {(v2Ns - directNs).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)} " +
            $"| {(v2Alloc - directAlloc).ToString("+0;-0;0", CultureInfo.InvariantCulture)} |");
    }
    File.WriteAllText(path, sb.ToString());
}

static void EmitTrendCsv(string path, List<Row> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine("benchId,benchName,variant,machineSku,architecture,configuration,timestamp,iterations,repetition,meanNs,allocBytes,gen0,heapDeltaBytes,counter,counterLabel,status");
    foreach (var r in rows)
    {
        sb.Append(r.BenchId).Append(',')
          .Append(r.BenchName).Append(',')
          .Append(r.Variant).Append(',')
          .Append(r.MachineSku).Append(',')
          .Append(r.Architecture).Append(',')
          .Append(r.Configuration).Append(',')
          .Append(r.Timestamp.ToString("O")).Append(',')
          .Append(r.Iterations).Append(',')
          .Append(r.Repetition).Append(',')
          .Append(r.MeanNs.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
          .Append(r.AllocBytes).Append(',')
          .Append(r.Gen0).Append(',')
          .Append(r.HeapDeltaBytes).Append(',')
          .Append(r.Counter).Append(',')
          .Append(r.CounterLabel ?? "").Append(',')
          .Append(r.Status)
          .AppendLine();
    }
    File.WriteAllText(path, sb.ToString());
}

static void AppendCell(StringBuilder sb, IGrouping<(string, string, string), Row>? g, int minReps, Func<Row, double> selector, string format)
{
    if (g is null || g.Count() < minReps) { sb.Append("| — "); return; }
    var (mean, _) = MeanCi95(g.Select(selector));
    sb.Append("| ").Append(mean.ToString(format, CultureInfo.InvariantCulture)).Append(' ');
}

internal static class JsonStatics
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new VariantConverter(),
        },
    };
}

internal sealed class VariantConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    private static readonly string[] _names = ["Direct", "ReactorToday", "ReactorV2"];

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString() ?? "";
        if (reader.TokenType == JsonTokenType.Number)
        {
            var i = reader.GetInt32();
            return (i >= 0 && i < _names.Length) ? _names[i] : i.ToString();
        }
        return reader.GetString() ?? "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

internal sealed record Row(
    string BenchId,
    string BenchName,
    string Variant,
    int Iterations,
    int Repetition,
    double TotalMs,
    double MeanNs,
    long AllocBytes,
    int Gen0,
    int Gen1,
    int Gen2,
    long HeapDeltaBytes,
    long Counter,
    string? CounterLabel,
    string Status,
    string? Note,
    string MachineSku,
    string Cpu,
    string OsBuild,
    string DotnetVersion,
    string Architecture,
    string Configuration,
    string PowerState,
    string PowerPlan,
    DateTimeOffset Timestamp);
