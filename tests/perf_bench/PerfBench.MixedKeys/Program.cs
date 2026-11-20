using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

if (args.Length != 1)
    throw new ArgumentException("Expected an output JSON path.");
var output = args[0];
Application.Start(_ => new BenchApp(output));

internal sealed partial class BenchApp : Application
{
    private readonly string _output;

    internal BenchApp(string output) => _output = output;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Run();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }
    }

    private void Run()
    {
        const int iterations = 200;
        const int repetitions = 6;
        var samples = new List<Sample>();
        foreach (int count in new[] { 32, 128 })
        foreach (string shape in new[] { "mixed-reorder", "explicit-reorder", "mixed-stable", "explicit-stable", "positional-stable" })
        {
            bool mixed = shape.StartsWith("mixed", StringComparison.Ordinal);
            bool positional = shape.StartsWith("positional", StringComparison.Ordinal);
            bool reorder = shape.EndsWith("reorder", StringComparison.Ordinal);
            var before = new Element[count];
            var after = new Element[count];
            for (int i = 0; i < count; i++)
            {
                string? key = positional || (mixed && i > 0 && i < count - 1) ? null : $"key-{i}";
                before[i] = TextBlock($"row-{i}").WithKeyIfPresent(key);
                after[i] = TextBlock($"row-{i}").WithKeyIfPresent(key);
            }
            if (reorder)
                (after[0], after[^1]) = (after[^1], after[0]);
            var a = VStack(before);
            var b = VStack(after);
            using var reconciler = new Reconciler();
            var panel = (StackPanel)reconciler.Mount(a, NoOp)!;
            var identities = panel.Children.ToArray();
            try
            {
                for (int i = 0; i < 50; i++)
                    Cycle(reconciler, a, b, panel);
                Verify(reconciler, a, b, panel, identities, reorder);
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    long bytes = GC.GetAllocatedBytesForCurrentThread();
                    int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                    long started = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++)
                        Cycle(reconciler, a, b, panel);
                    long elapsed = Stopwatch.GetTimestamp() - started;
                    bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    samples.Add(new(shape, count, repetition,
                        elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / (iterations * 2),
                        (double)bytes / (iterations * 2),
                        GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2));
                    Verify(reconciler, a, b, panel, identities, reorder);
                }
            }
            finally
            {
                reconciler.UnmountChild(panel);
            }
        }
        File.WriteAllText(_output, JsonSerializer.Serialize(samples, BenchJson.Default.ListSample));
        foreach (var group in samples.GroupBy(sample => (sample.Name, sample.Count)))
            Console.WriteLine($"{group.Key}: {group.Average(x => x.BytesPerOp):F0} B/op, {group.Average(x => x.NsPerOp):F0} ns/op");
    }

    private static readonly Action NoOp = static () => { };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Cycle(Reconciler reconciler, Element a, Element b, StackPanel panel)
    {
        Sink.Control = reconciler.Reconcile(a, b, panel, NoOp);
        Sink.Control = reconciler.Reconcile(b, a, panel, NoOp);
    }

    private static void Verify(Reconciler reconciler, Element a, Element b, StackPanel panel, UIElement[] identities, bool reorder)
    {
        reconciler.Reconcile(a, b, panel, NoOp);
        Check(reversed: reorder);
        reconciler.Reconcile(b, a, panel, NoOp);
        Check(reversed: false);

        void Check(bool reversed)
        {
            if (panel.Children.Count != identities.Length)
                throw new InvalidOperationException("Reconciliation changed the child count.");
            for (int i = 0; i < identities.Length; i++)
            {
                int expected = reversed && (i == 0 || i == identities.Length - 1) ? identities.Length - 1 - i : i;
                if (!ReferenceEquals(panel.Children[i], identities[expected])
                    || panel.Children[i] is not TextBlock text || text.Text != $"row-{expected}")
                    throw new InvalidOperationException($"Identity/content mismatch at {i}.");
            }
        }
    }
}

internal static class BenchExtensions
{
    internal static TextBlockElement WithKeyIfPresent(this TextBlockElement element, string? key) =>
        key is null ? element : element.WithKey(key);
}

internal static class Sink
{
    internal static UIElement? Control;
}

internal sealed record Sample(string Name, int Count, int Repetition, double NsPerOp, double BytesPerOp,
    int Gen0, int Gen1, int Gen2);

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
