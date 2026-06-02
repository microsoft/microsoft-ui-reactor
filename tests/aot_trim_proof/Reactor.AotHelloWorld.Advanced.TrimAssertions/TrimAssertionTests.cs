using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Reactor.AotHelloWorld.Advanced.TrimAssertions;

/// <summary>
/// Spec 053 §10 — negative AOT trim assertion for Reactor.Advanced. The
/// companion app references Reactor.Advanced but never names a Win2D canvas
/// factory; these Reactor-owned handler symbols must therefore be absent from
/// the published binary.
/// </summary>
public sealed class TrimAssertionTests
{
    private static readonly string[] ForbiddenSymbols =
    [
        "Win2DCanvasHandler",
        "Win2DAnimatedCanvasHandler",
        "Win2DVirtualCanvasHandler",
    ];

    [SkippableFact]
    public void PublishedBinary_DoesNotContain_Win2DHandlerSymbols()
    {
        var publishDir = ResolvePublishDir("REACTOR_AOT_ADVANCED_PUBLISH_DIR", "Reactor.AotHelloWorld.Advanced");
        Skip.If(string.IsNullOrEmpty(publishDir),
            "AOT publish folder not found. Set REACTOR_AOT_ADVANCED_PUBLISH_DIR or publish Reactor.AotHelloWorld.Advanced first.");

        var scannedFiles = AssembliesToScan(publishDir).ToArray();
        Skip.If(scannedFiles.Length == 0, $"No Reactor*.dll/.exe found under {publishDir}.");

        var hits = FindSymbols(scannedFiles, ForbiddenSymbols).ToArray();
        Assert.Empty(hits);
    }

    [SkippableFact]
    public void PublishedBinary_Contains_PositiveControlSymbol()
    {
        var publishDir = ResolvePublishDir("REACTOR_AOT_ADVANCED_PUBLISH_DIR", "Reactor.AotHelloWorld.Advanced");
        Skip.If(string.IsNullOrEmpty(publishDir), "AOT publish folder not found — see negative assertion.");

        var scannedFiles = AssembliesToScan(publishDir).ToArray();
        Skip.If(scannedFiles.Length == 0, $"No Reactor*.dll/.exe found under {publishDir}.");

        Assert.True(scannedFiles.Any(f => BinaryContains(f, "App")),
            $"Positive control failed: App was not found under {publishDir}.");
    }

    private static string ResolvePublishDir(string environmentVariable, string appDirectoryName)
    {
        var fromEnv = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && here.GetFiles("Reactor.slnx").Length == 0)
            here = here.Parent;
        if (here is null) return string.Empty;

        // Defense-in-depth against the CodeQL Path.Combine warning: appDirectoryName is a
        // compile-time constant from the assertion-project pair (e.g. "Reactor.AotHelloWorld.Advanced"),
        // never a rooted path; assert that so the combine is unambiguously a child segment.
        if (string.IsNullOrEmpty(appDirectoryName) || Path.IsPathRooted(appDirectoryName))
            return string.Empty;
        var appBin = Path.Join(here.FullName, "tests", "aot_trim_proof", appDirectoryName, "bin");
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

    private static IEnumerable<string> FindSymbols(IEnumerable<string> files, IEnumerable<string> symbols)
    {
        foreach (var file in files)
        foreach (var symbol in symbols)
            if (BinaryContains(file, symbol))
                yield return $"{Path.GetFileName(file)} contains {symbol}";
    }

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
