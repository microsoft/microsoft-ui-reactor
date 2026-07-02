// Issue #327 / PR #753 — opt-in keyed row-memoization micro-bench host.
//
// CLI:
//   PerfBench.RowMemo.exe [--out <path>] [--headless]
//
// Runs the measurement directly (no WinUI window) and exits. Prints a
// human-readable report plus machine-parseable `key=value` lines to stdout, and
// — when --out is given — writes the same `key=value` lines to that file for the
// Run-PerfBenchmark.ps1 harness to parse deterministically. Unknown flags (e.g.
// --headless, passed by the harness for parity with the other legs) are ignored.

using PerfBench.RowMemo;

string? outPath = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        default:
            // Ignore unrecognized flags (--headless and friends) so the harness can
            // launch this exe with the same boilerplate it uses for the other legs.
            break;
    }
}

RowMemoBench.Run(outPath);
return 0;
