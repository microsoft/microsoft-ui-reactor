namespace Microsoft.UI.Reactor.Cli.Figma;

/// <summary>
/// Dispatches <c>mur figma &lt;verb&gt;</c> subcommands.
/// </summary>
internal static class FigmaCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var verb = args[0].ToLowerInvariant();
        var verbArgs = args.Skip(1).ToArray();

        return verb switch
        {
            "watch" => FigmaWatchCommand.RunAsync(verbArgs).GetAwaiter().GetResult(),
            "--help" or "-h" or "-?" => ShowHelp(),
            _ => ShowHelp($"Unknown verb: {verb}")
        };
    }

    private static int ShowHelp(string? error = null)
    {
        if (error != null)
        {
            Console.Error.WriteLine($"Error: {error}");
            Console.Error.WriteLine();
        }

        Console.Error.WriteLine("Usage: mur figma <command>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  watch <url> [--interval N]   Poll Figma file for changes");
        Console.Error.WriteLine();
        Console.Error.WriteLine("The 'watch' command polls a Figma file's lastModified timestamp");
        Console.Error.WriteLine("and emits JSON events to stdout when the design changes. The AI");
        Console.Error.WriteLine("agent reads these events and re-fetches design data via the");
        Console.Error.WriteLine("Figma MCP server (figma-developer-mcp).");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Environment / Config (checked in order):");
        Console.Error.WriteLine("  FIGMA_API_KEY              Environment variable");
        Console.Error.WriteLine("  ~/.copilot/mcp-config.json --figma-api-key in Figma MCP server args");
        Console.Error.WriteLine("  .vscode/mcp.json           --figma-api-key in Figma MCP server args");
        return error != null ? 1 : 0;
    }
}
