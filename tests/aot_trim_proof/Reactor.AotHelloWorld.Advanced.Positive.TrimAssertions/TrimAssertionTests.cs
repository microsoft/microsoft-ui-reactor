using System.Text;
using Xunit;

namespace Reactor.AotHelloWorld.Advanced.Positive.TrimAssertions;

/// <summary>
/// Spec 053 §10 — positive AOT trim assertion for Reactor.Advanced. The
/// companion app calls <c>Win2DCanvas.Of(...)</c>, so at least the static-canvas
/// Reactor.Advanced handler symbol must survive in the published binary.
/// </summary>
public sealed class TrimAssertionTests
{
    private static readonly string[] ExpectedSymbols =
    [
        "Win2DCanvasHandler",
    ];

    [SkippableFact]
    public void PublishedBinary_Contains_UsedWin2DHandlerSymbols()
    {
        var publishDir = ResolvePublishDir("REACTOR_AOT_ADVANCED_POSITIVE_PUBLISH_DIR", "Reactor.AotHelloWorld.Advanced.Positive");
        Skip.If(string.IsNullOrEmpty(publishDir),
            "AOT publish folder not found. Set REACTOR_AOT_ADVANCED_POSITIVE_PUBLISH_DIR or publish Reactor.AotHelloWorld.Advanced.Positive first.");

        var scannedFiles = AssembliesToScan(publishDir).ToArray();
        Skip.If(scannedFiles.Length == 0, $"No Reactor*.dll/.exe found under {publishDir}.");

        var missing = ExpectedSymbols
            .Where(symbol => !scannedFiles.Any(file => BinaryContains(file, symbol)))
            .ToArray();

        Assert.Empty(missing);
    }

    private static string ResolvePublishDir(string environmentVariable, string appDirectoryName)
    {
        var fromEnv = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Reactor.slnx")))
            here = here.Parent;
        if (here is null) return string.Empty;

        var appBin = Path.Combine(here.FullName, "tests", "aot_trim_proof", appDirectoryName, "bin");
        if (!Directory.Exists(appBin)) return string.Empty;

        return Directory.EnumerateDirectories(appBin, "publish", SearchOption.AllDirectories)
            .Select(p => new DirectoryInfo(p))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName ?? string.Empty;
    }

    private static IEnumerable<string> AssembliesToScan(string publishDir) =>
        Directory.EnumerateFiles(publishDir, "Reactor*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    private static bool BinaryContains(string filePath, string needle)
    {
        var bytes = File.ReadAllBytes(filePath);
        return IndexOf(bytes, Encoding.ASCII.GetBytes(needle)) >= 0
            || IndexOf(bytes, Encoding.Unicode.GetBytes(needle)) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
