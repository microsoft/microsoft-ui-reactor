using Microsoft.UI.Reactor.Cli.Pack;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

internal static class TestRepo
{
    public static string Root => RepoRootFinder.FindRepoRoot()
        ?? throw new InvalidOperationException("Could not locate the Reactor repo root from the test output directory.");
}
