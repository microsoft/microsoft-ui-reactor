using System.Globalization;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Records how many <c>winapp.exe</c> processes each test spawned, so the migration report
/// can quantify winapp's process-per-call overhead (vs WinAppDriver's single persistent
/// session). The test bases snapshot <see cref="WinAppUi.InvocationCount"/> around every
/// test and call <see cref="Record"/>; results are appended to a CSV under TestResults.
/// </summary>
public static class WinAppMetrics
{
    private static readonly object Gate = new();
    private static readonly string CsvPath = ResolveCsvPath();
    private static bool _headerWritten;

    private static string ResolveCsvPath()
    {
        var dir = Environment.GetEnvironmentVariable("WINAPP_METRICS_DIR");
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "winapp-invocations.csv");
    }

    /// <summary>Append one test's winapp invocation count (and wall time) to the CSV.</summary>
    public static void Record(string testName, long invocations, double seconds)
    {
        lock (Gate)
        {
            try
            {
                if (!_headerWritten && !File.Exists(CsvPath))
                    File.AppendAllText(CsvPath, "test,winapp_invocations,seconds\n");
                _headerWritten = true;
                File.AppendAllText(
                    CsvPath,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2:F2}\n",
                        testName, invocations, seconds));
            }
            catch
            {
                // Metrics are best-effort — never fail a test over bookkeeping.
            }
        }
    }
}
