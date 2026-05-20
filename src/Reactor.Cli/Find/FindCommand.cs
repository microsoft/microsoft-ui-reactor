#nullable enable

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class FindCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(static a => a is "--help" or "-h" or "-?"))
        {
            ShowHelp();
            return 0;
        }

        var maxResults = 5;
        string? category = null;
        var includeAntiPatterns = false;
        var queryParts = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--max":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out maxResults) || maxResults <= 0)
                    {
                        Console.Error.WriteLine("Error: --max requires a positive integer.");
                        return 1;
                    }
                    break;

                case "--category":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --category requires a value.");
                        return 1;
                    }
                    category = args[++i];
                    break;

                case "--include-anti-patterns":
                    includeAntiPatterns = true;
                    break;

                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Error: Unknown option '{args[i]}'.");
                        return 1;
                    }

                    queryParts.Add(args[i]);
                    break;
            }
        }

        if (queryParts.Count == 0)
        {
            ShowHelp();
            return 1;
        }

        var query = string.Join(" ", queryParts);
        var catalogue = DataLoader.Load();
        var engine = new SearchEngine(catalogue);
        var results = engine.Search(query, maxResults, category, includeAntiPatterns);

        if (results.Length == 0)
        {
            Console.WriteLine($"No matches found for \"{query}\".");
            return 0;
        }

        Console.WriteLine($"Found {results.Length} matches for \"{query}\":");
        foreach (var result in results)
        {
            Console.WriteLine($"  {result.Scenario.Id.PadRight(24)}  {result.Scenario.Title.PadRight(40)}  → SKILL: {result.Scenario.Category}");
        }

        Console.WriteLine("To get full code: mur get <id>");
        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: mur find <query> [--max N] [--category <name>] [--include-anti-patterns]");
        Console.WriteLine("Search the sample catalogue.");
    }
}
