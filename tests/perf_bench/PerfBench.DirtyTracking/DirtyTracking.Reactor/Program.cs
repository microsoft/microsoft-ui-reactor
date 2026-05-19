using Microsoft.UI.Reactor;
using PerfBench.DirtyTracking.Reactor;
using PerfBench.Shared;

// <snippet:bench-entrypoint>
var opts = BenchCliOptions.Parse(args);
if (opts.Headless) ConsoleHelper.EnsureConsole();
DirtyTrackingApp.Opts = opts;
ReactorApp.Run<DirtyTrackingApp>("EXP-1 DirtyTracking.Reactor", fullScreen: true);
// </snippet:bench-entrypoint>
