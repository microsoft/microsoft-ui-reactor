#nullable enable

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class ListCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(static a => a is "--help" or "-h" or "-?"))
        {
            ShowHelp();
            return 0;
        }

        string? category = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--category":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --category requires a value.");
                        return 1;
                    }

                    category = args[++i];
                    break;

                default:
                    Console.Error.WriteLine($"Error: Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        var catalogue = DataLoader.Load();
        var scenarios = catalogue.Scenarios.AsEnumerable();
        if (category is not null)
        {
            scenarios = scenarios.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        var groups = scenarios
            .GroupBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0 && category is not null)
        {
            Console.WriteLine($"No scenarios in category '{category}'.");
            return 0;
        }

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            Console.WriteLine(group.Key.ToUpperInvariant());
            foreach (var scenario in group.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {scenario.Id.PadRight(24)}  {scenario.Title}");
            }

            if (index < groups.Count - 1)
            {
                Console.WriteLine();
            }
        }

        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: mur list [--category <name>]");
        Console.WriteLine("List all scenarios.");
    }
}
