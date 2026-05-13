using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class RepoRootFinderTests
{
    [Fact]
    public void FindRepoRoot_FromRepoDirectory_ReturnsRoot()
    {
        // We're running inside the repo, so starting from the test assembly
        // base directory should find the root.
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "src", "Reactor", "Reactor.csproj")));
    }

    [Fact]
    public void FindRepoRoot_FromExplicitRepoPath_ReturnsRoot()
    {
        // Start from a known subdirectory within the repo.
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var subDir = Path.Combine(root, "src", "Reactor");
        var found = RepoRootFinder.FindRepoRoot(subDir);
        Assert.Equal(root, found);
    }

    [Fact]
    public void FindRepoRoot_FromUnrelatedPath_ReturnsNull()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Assert.Null(RepoRootFinder.FindRepoRoot(temp));
        }
        finally
        {
            Directory.Delete(temp);
        }
    }
}
