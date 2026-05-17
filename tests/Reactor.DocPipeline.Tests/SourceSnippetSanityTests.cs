using System.IO;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Sanity check: extract the <c>demo</c> region embedded inside
/// <c>src/Reactor/Hooks/UseMemoCells.cs</c> (task 1.4). This guards against
/// extractor regressions silently dropping markers from a real file.
/// </summary>
public class SourceSnippetSanityTests
{
    [Fact]
    public void Extracts_demo_region_from_UseMemoCells()
    {
        var repoRoot = FindRepoRoot();
        var snip = SnippetExtractor.ExtractFromSource(
            repoRoot, "src/Reactor/Hooks/UseMemoCells.cs", "demo");
        Assert.Contains("SnapshotItems", snip.Code);
        Assert.DoesNotContain("<snippet:demo>", snip.Code);
        Assert.DoesNotContain("</snippet:demo>", snip.Code);
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
