using System.Globalization;

namespace StressPerf.Shared;

public sealed class CliOptions
{
    public double Percent { get; set; } = 10;
    public int DurationSeconds { get; set; } = 10;
    public bool Headless { get; set; }

    /// <summary>
    /// When set (via <c>--json</c>), the harness also writes a machine-readable
    /// <c>{AppName}.metrics.json</c> next to the executable and echoes a single
    /// <c>REACTOR_PERF_JSON {…}</c> line to the console. Used by the on-demand
    /// perf-comparison CI workflow (.github/workflows/perf-compare.yml) to parse
    /// the four headline metrics without scraping the human-readable report.
    /// </summary>
    public bool Json { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var opts = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--percent" when i + 1 < args.Length:
                    opts.Percent = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--duration" when i + 1 < args.Length:
                    opts.DurationSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--headless":
                    opts.Headless = true;
                    break;
                case "--json":
                    opts.Json = true;
                    break;
            }
        }
        return opts;
    }
}
