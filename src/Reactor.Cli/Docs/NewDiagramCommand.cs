namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// <c>mur docs new-diagram &lt;topic&gt; &lt;id&gt;</c> — scaffold a starter
/// <c>.mmd</c> file under <c>docs/_pipeline/diagrams/&lt;topic&gt;/</c>.
/// </summary>
internal static class NewDiagramCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: mur docs new-diagram <topic> <id>");
            return 1;
        }

        var topic = args[0];
        var id = args[1];

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Error: Could not find repository root.");
            return 1;
        }

        var diagramsRoot = Path.Combine(repoRoot, "docs", "_pipeline", "diagrams");
        try
        {
            var path = DiagramProcessor.ScaffoldDiagram(diagramsRoot, topic, id);
            Console.WriteLine(path);
            return 0;
        }
        catch (DocPipelineException ex)
        {
            Console.Error.WriteLine($"{ex.Code}: {ex.Message}");
            return 1;
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
