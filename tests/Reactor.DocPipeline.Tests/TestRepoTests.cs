using System.IO;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

[Collection(WorkingDirectoryCollection.Name)]
public sealed class TestRepoTests
{
    [Fact]
    public void Root_Uses_Assembly_Checkout_When_Working_Directory_Is_Another_Repo()
    {
        var expectedRoot = RepoRootFinder.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(expectedRoot);

        var originalDirectory = Directory.GetCurrentDirectory();
        var otherRoot = Directory.CreateTempSubdirectory("reactor-doc-cwd-").FullName;
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Join(otherRoot, "src", "Reactor"));
            File.WriteAllText(Path.Join(projectDirectory.FullName, "Reactor.csproj"), "<Project />");
            Directory.SetCurrentDirectory(otherRoot);

            Assert.Equal(otherRoot, RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory()));
            Assert.Equal(expectedRoot, TestRepo.Root);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            FixtureCleanup.DeleteTree(otherRoot);
        }
    }
}

// The current directory is shared by the entire test process.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkingDirectoryCollection
{
    public const string Name = "WorkingDirectory";
}
