using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

const int iterations = 100_000;
const int repetitions = 12;
var output = args.Length == 1 ? args[0] : throw new ArgumentException("Expected an output JSON path.");
var keys = Enumerable.Range(0, 500).Select(i => $"row-{i}").ToArray();
var equalKeys = keys.Select(key => new string(key.AsSpan())).ToArray();
var keyed = keys.Select(key => new TextBlockElement("row") { Key = key }).ToArray();
var registry = new TypeRegistry();
var target = new object();
Func<int, Element> render = static _ => new TextBlockElement("row");
var cases = new (string Name, Func<int, Element> Build)[]
{
    ("text/formatted-with-key", i => new TextBlockElement("row").WithKey($"row-{i}")),
    ("text/cached-with-key", i => new TextBlockElement("row").WithKey(keys[i])),
    ("text/formatted-initializer", i => new TextBlockElement("row") { Key = $"row-{i}" }),
    ("text/cached-initializer", i => new TextBlockElement("row") { Key = keys[i] }),
    ("text/same-key", i => keyed[i].WithKey(keys[i])),
    ("text/equal-distinct-key", i => keyed[i].WithKey(equalKeys[i])),
    ("text/changed-key", i => keyed[i].WithKey(keys[(i + 1) % keys.Length])),
    ("virtual-list/factory", _ => VirtualList(500, render)),
    ("property-grid/factory", _ => PropertyGrid(target, registry)),
};

// Assert equivalent output before timing; a missing assignment must not look like a win.
for (int i = 0; i < keys.Length; i++)
{
    for (int c = 0; c < 6; c++)
    {
        var element = (TextBlockElement)cases[c].Build(i);
        if (element.Key != keys[i] || element.Content != "row")
            throw new InvalidOperationException($"Incorrect output for {cases[c].Name}.");
    }
    if (cases[6].Build(i).Key != keys[(i + 1) % keys.Length] || keyed[i].Key != keys[i])
        throw new InvalidOperationException("Rekeying must preserve the original element.");
    if (!ReferenceEquals(cases[5].Build(i).Key, equalKeys[i]))
        throw new InvalidOperationException("The assigned key reference was lost.");
}
if (cases[7].Build(0).Key != "vl-500"
    || cases[8].Build(0).Key != "pg-System.Object")
    throw new InvalidOperationException("Factory key semantics changed.");

foreach (var benchmark in cases)
    Run(benchmark.Build, iterations, keys.Length);

var samples = new List<Sample>();
for (int repetition = 0; repetition < repetitions; repetition++)
{
    // Alternate direction so the same case is not always first/last.
    for (int offset = 0; offset < cases.Length; offset++)
    {
        int c = repetition % 2 == 0 ? offset : cases.Length - 1 - offset;
        var benchmark = cases[c];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        Run(benchmark.Build, iterations, keys.Length);
        long elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        samples.Add(new(benchmark.Name, repetition,
            elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations,
            (double)allocated / iterations));
    }
}

File.WriteAllText(output, JsonSerializer.Serialize(samples, BenchJson.Default.ListSample));
foreach (var group in samples.GroupBy(sample => sample.Name))
{
    var times = group.Select(sample => sample.NsPerOp).Order().ToArray();
    var bytes = group.Select(sample => sample.BytesPerOp).Order().ToArray();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{group.Key,-30} {Median(times),10:F2} ns/op {Median(bytes),10:F2} B/op"));
}

static double Median(double[] values) => (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run(Func<int, Element> build, int count, int keyCount)
{
    for (int i = 0; i < count; i++)
        Sink.Element = build(i % keyCount);
}

internal static class Sink
{
    // Keep every result observable so JIT escape analysis cannot erase the allocation.
    internal static Element? Element;
}

internal sealed record Sample(string Name, int Repetition, double NsPerOp, double BytesPerOp);

[JsonSerializable(typeof(List<Sample>))]
internal partial class BenchJson : JsonSerializerContext;

internal static partial class RuntimeInitialize
{
    [LibraryImport("Microsoft.WindowsAppRuntime.dll")]
    private static partial int WindowsAppRuntime_EnsureIsLoaded();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        Marshal.ThrowExceptionForHR(WindowsAppRuntime_EnsureIsLoaded());
        WinRT.ComWrappersSupport.InitializeComWrappers();
    }
}
