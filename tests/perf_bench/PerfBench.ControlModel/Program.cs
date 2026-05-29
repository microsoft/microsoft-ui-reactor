// Spec 047 §15.3 — PerfBench.ControlModel host.
//
// CLI:
//   PerfBench.ControlModel.exe [--test M1 [M2 ...]] [--variant Direct|ReactorToday|Reactor|All]
//                              [--iterations N] [--reps R] [--out path] [--headless]
//
// Defaults: --test=All --variant=All --iterations=10000 --reps=5
//
// Reactor is the production control model variant compared against the
// ReactorToday legacy dispatch baseline in the §15.6 aggregator output.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using PerfBench.ControlModel.Benches;
using PerfBench.ControlModel.Variants;

// The host is a minimal WinUI app — we don't render via Reactor's component
// pipeline because the benches need direct control over Reconciler mount /
// unmount cycles. The Application subclass below opens a single Window with
// an empty Grid which serves as the bench Parent.
ConsoleHelper.EnsureConsole();
Application.Start(_ =>
{
    new BenchHostApp();
});

return;

// ─── Application host ───────────────────────────────────────────────────────

internal sealed partial class BenchHostApp : Microsoft.UI.Xaml.Application
{
    private Window? _window;

    public BenchHostApp() { }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Top-level statements receive args[] but a partial Application subclass
    // can't reach that variable, so re-read from the OS-provided command line.
    var cliArgs = System.Environment.GetCommandLineArgs().Skip(1).ToArray();
    var cli = BenchCli.Parse(cliArgs);
        _window = new Window { Title = "PerfBench.ControlModel" };

        // Two-pane layout: a status TextBlock at the top, the bench Parent
        // panel below. The bench mounts and unmounts UIElements under
        // `benchParent`; the status block is updated between bench/variant
        // transitions so the UI shows progress instead of looking hung.
        var rootStack = new StackPanel { Orientation = Orientation.Vertical };
        var statusBlock = new TextBlock
        {
            Text = "starting...",
            Margin = new Microsoft.UI.Xaml.Thickness(12),
            FontSize = 14,
        };
        var benchParent = new Grid { MinHeight = 400 };
        rootStack.Children.Add(statusBlock);
        rootStack.Children.Add(benchParent);
        _window.Content = rootStack;
        _window.Activate();

        var dq = DispatcherQueue.GetForCurrentThread();

        if (cli.DemoMode)
        {
            ScheduleDemoRun(dq, cli, benchParent, statusBlock);
        }
        else
        {
            // Kick off the bench schedule. The runner schedules one
            // (bench, variant) pair per dispatcher tick at Low priority — so
            // between pairs the WM_PAINT pump runs and `statusBlock` paints.
            ScheduleBenchRun(dq, cli, benchParent, statusBlock);
        }
    }

    private void ScheduleDemoRun(DispatcherQueue dq, BenchCli cli, Panel benchParent, TextBlock statusBlock)
    {
        var screenshotDir = cli.ScreenshotDir ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        Console.WriteLine($"[demo] screenshots → {screenshotDir}");

        var benches = cli.SelectedTests is { Count: > 0 } sel
            ? BenchCatalog.All.Where(b => sel.Contains(b.Id)).ToList()
            : BenchCatalog.All.ToList();
        var variants = cli.SelectedVariants is { Count: > 0 } sv
            ? sv.ToList()
            : new List<BenchVariant> { BenchVariant.Direct, BenchVariant.ReactorToday, BenchVariant.Reactor };

        var jobs = new List<(IBench Bench, BenchVariant Variant)>();
        foreach (var b in benches)
            foreach (var v in variants)
                jobs.Add((b, v));

        int idx = 0;
        async void RunNext()
        {
            if (idx >= jobs.Count)
            {
                statusBlock.Text = $"[demo done] {jobs.Count} screenshots in {screenshotDir}";
                Console.WriteLine($"[demo done] {jobs.Count} screenshots in {screenshotDir}");
                dq.TryEnqueue(DispatcherQueuePriority.Low, Exit);
                return;
            }

            var (bench, variant) = jobs[idx++];
            statusBlock.Text = $"[demo {idx}/{jobs.Count}] {bench.Id} {bench.Name} {variant}";
            Console.WriteLine($"[demo] {bench.Id} {bench.Name} {variant}");

            benchParent.Children.Clear();
            var rec = new Reconciler();
            if (bench.Id == "M6")
            {
                rec.RegisterType<M06_DispatchExternalType.ExtElement, TextBlock>(
                    mount: static (_, el, _) => new TextBlock { Text = el.Label },
                    update: static (_, _, n, c, _) => { c.Text = n.Label; return null; });
            }
            var ctx = new BenchContext { Parent = benchParent, Reconciler = rec };

            try
            {
                bench.DemoMount(variant, ctx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  DemoMount ERROR: {ex.Message}");
                benchParent.Children.Add(new TextBlock { Text = $"ERROR: {ex.Message}", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) });
            }

            // Give layout a chance to run, then snapshot.
            await Task.Delay(cli.DemoPauseMs);

            try
            {
                // Use win32 PrintWindow to capture the bench window's HWND.
                // RenderTargetBitmap fails on top-level Window.Content in
                // WinUI 3 due to the swapchain rooting.
                var path = Path.Combine(screenshotDir, $"{bench.Id}_{variant}.png");
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window!);
                if (PerfBench.ControlModel.WindowCapture.CaptureWindow(hwnd, path))
                    Console.WriteLine($"  saved {path}");
                else
                    Console.WriteLine($"  screenshot ERROR: PrintWindow returned 0 for {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  screenshot ERROR: {ex.GetType().Name}: {ex.Message}");
            }

            dq.TryEnqueue(DispatcherQueuePriority.Low, RunNext);
        }

        dq.TryEnqueue(DispatcherQueuePriority.Low, RunNext);
    }

    private void ScheduleBenchRun(DispatcherQueue dq, BenchCli cli, Panel benchParent, TextBlock statusBlock)
    {
        var outPath = cli.OutPath ?? Path.Combine(AppContext.BaseDirectory, "results.jsonl");
        var writer = new StreamWriter(outPath, append: false);
        Console.WriteLine($"[PerfBench.ControlModel] writing {outPath}");

        var runner = new BenchRunner
        {
            Iterations = cli.Iterations,
            Repetitions = cli.Reps,
        };

        var benches = cli.SelectedTests is { Count: > 0 } sel
            ? BenchCatalog.All.Where(b => sel.Contains(b.Id)).ToList()
            : BenchCatalog.All.ToList();
        var variants = cli.SelectedVariants is { Count: > 0 } sv
            ? sv.ToList()
            : new List<BenchVariant>
            {
                BenchVariant.Direct,
                BenchVariant.ReactorToday,
                BenchVariant.Reactor,
            };

        // Flatten into a job list of (bench, variant) pairs.
        var jobs = new List<(IBench Bench, BenchVariant Variant)>();
        foreach (var b in benches)
            foreach (var v in variants)
                jobs.Add((b, v));

        int jobIndex = 0;
        int totalJobs = jobs.Count;

        void RunNext()
        {
            if (jobIndex >= totalJobs)
            {
                writer.Flush();
                writer.Dispose();
                statusBlock.Text = $"[done] {outPath}";
                Console.WriteLine($"[done] {outPath}");
                dq.TryEnqueue(DispatcherQueuePriority.Low, Exit);
                return;
            }

            var (bench, variant) = jobs[jobIndex++];
            statusBlock.Text = $"[{jobIndex}/{totalJobs}] {bench.Id} {bench.Name} {variant}";
            Console.WriteLine($"[run] {bench.Id} {bench.Name} {variant}");

            // Schedule the actual work at Low priority — gives the
            // status update above a chance to paint first.
            dq.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                BenchContext Factory()
                {
                    benchParent.Children.Clear();
                    // The Reactor variant exercises the unconditional production
                    // protocol path with hand-coded handlers.
                    Reconciler rec = new Reconciler();
                    if (bench.Id == "M6")
                    {
                        rec.RegisterType<M06_DispatchExternalType.ExtElement, TextBlock>(
                            mount: static (_, el, _) => new TextBlock { Text = el.Label },
                            update: static (_, _, n, c, _) => { c.Text = n.Label; return null; });
                    }
                    return new BenchContext { Parent = benchParent, Reconciler = rec };
                }

                try
                {
                    foreach (var result in runner.Run(bench, variant, Factory))
                    {
                        writer.WriteLine(result.ToJsonLine());
                        Console.WriteLine(
                            $"  rep={result.Repetition} mean={result.MeanNs:F1}ns " +
                            $"alloc={result.AllocBytes}B gen0={result.Gen0} " +
                            (result.Counter != 0 ? $"counter[{result.CounterLabel}]={result.Counter}" : ""));
                    }
                }
                catch (Exception ex)
                {
                    var stub = new MeasurementResult
                    {
                        BenchId = bench.Id, BenchName = bench.Name, Variant = variant,
                        Iterations = cli.Iterations, Repetition = 0,
                        TotalMs = 0, MeanNs = 0, AllocBytes = 0,
                        Gen0 = 0, Gen1 = 0, Gen2 = 0, HeapDeltaBytes = 0,
                        Status = "error", Note = ex.GetType().Name + ": " + ex.Message,
                        MachineSku = Env.MachineSku, Cpu = Env.Cpu, OsBuild = Env.OsBuild,
                        DotnetVersion = Env.DotnetVersion, Architecture = Env.Architecture,
                        Configuration = Env.Configuration,
                    };
                    writer.WriteLine(stub.ToJsonLine());
                    Console.WriteLine($"  ERROR: {ex.Message}");
                }
                writer.Flush();
                // Schedule the next job on the dispatcher — this is the
                // critical bit: returning to the dispatcher between
                // (bench, variant) pairs lets WinUI paint the status
                // update for the *next* pair before its work blocks
                // the thread.
                dq.TryEnqueue(DispatcherQueuePriority.Low, RunNext);
            });
        }

        dq.TryEnqueue(DispatcherQueuePriority.Low, RunNext);
    }

}

internal sealed class BenchCli
{
    public List<string>? SelectedTests;
    public List<BenchVariant>? SelectedVariants;
    public int Iterations = 10_000;
    public int Reps = 5;
    public string? OutPath;
    public bool Headless;
    public bool DemoMode;
    public string? ScreenshotDir;
    public int DemoPauseMs = 1500;

    public static BenchCli Parse(string[] args)
    {
        var cli = new BenchCli();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--test" when i + 1 < args.Length:
                    cli.SelectedTests ??= new();
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        cli.SelectedTests.Add(args[++i]);
                    break;
                case "--variant" when i + 1 < args.Length:
                    cli.SelectedVariants ??= new();
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        var v = args[++i];
                        if (v.Equals("All", StringComparison.OrdinalIgnoreCase)) { cli.SelectedVariants = null; break; }
                        if (Enum.TryParse<BenchVariant>(v, ignoreCase: true, out var bv)) cli.SelectedVariants.Add(bv);
                    }
                    break;
                case "--iterations" when i + 1 < args.Length:
                    cli.Iterations = int.Parse(args[++i]);
                    break;
                case "--reps" when i + 1 < args.Length:
                    cli.Reps = int.Parse(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    cli.OutPath = args[++i];
                    break;
                case "--headless":
                    cli.Headless = true;
                    break;
                case "--demo":
                    cli.DemoMode = true;
                    break;
                case "--screenshot-dir" when i + 1 < args.Length:
                    cli.ScreenshotDir = args[++i];
                    break;
                case "--demo-pause-ms" when i + 1 < args.Length:
                    cli.DemoPauseMs = int.Parse(args[++i]);
                    break;
            }
        }
        return cli;
    }
}

internal static partial class ConsoleHelper
{
    // Mirrors the PerfBench.Shared ConsoleHelper — attach a console to a
    // WinExe so Console.WriteLine output is visible when the user runs
    // from a shell.
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    public static void EnsureConsole()
    {
        const int ATTACH_PARENT_PROCESS = -1;
        AttachConsole(ATTACH_PARENT_PROCESS);
    }
}
