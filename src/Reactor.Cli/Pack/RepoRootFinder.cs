namespace Microsoft.UI.Reactor.Cli.Pack;

internal static class RepoRootFinder
{
    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a directory
    /// that contains <c>src/Reactor/Reactor.csproj</c>.  Falls back to
    /// <see cref="AppContext.BaseDirectory"/> when <paramref name="startDirectory"/>
    /// is <c>null</c>.
    /// </summary>
    public static string? FindRepoRoot(string? startDirectory = null)
    {
        var start = startDirectory ?? AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Reactor"))
                && File.Exists(Path.Combine(d.FullName, "src", "Reactor", "Reactor.csproj")))
            {
                return d.FullName;
            }
        }
        return null;
    }
}
