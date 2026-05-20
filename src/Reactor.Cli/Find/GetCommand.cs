#nullable enable

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class GetCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(static a => a is "--help" or "-h" or "-?"))
        {
            ShowHelp();
            return 0;
        }

        var raw = false;
        string? scenarioId = null;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--raw":
                    raw = true;
                    break;

                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Error: Unknown option '{arg}'.");
                        return 1;
                    }

                    if (scenarioId is not null)
                    {
                        Console.Error.WriteLine("Error: Only one scenario id may be provided.");
                        return 1;
                    }

                    scenarioId = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            ShowHelp();
            return 1;
        }

        var catalogue = DataLoader.Load();
        var scenario = catalogue.Scenarios.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
        {
            Console.WriteLine($"Scenario '{scenarioId}' not found. Use 'mur list' to see all scenarios.");
            return 1;
        }

        Console.WriteLine($"## {scenario.Title}");
        Console.WriteLine($"*Category: {scenario.Category} · Intent: {scenario.Intent}*");
        Console.WriteLine();
        Console.WriteLine("**C#:**");
        Console.WriteLine("```csharp");
        Console.WriteLine(raw ? scenario.RawCode : scenario.Code);
        Console.WriteLine("```");

        var notes = Notes.GetNotes(scenario.NotesKey);
        if (notes is { Length: > 0 } && scenario.NotesKey is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"**Important (Notes for `{scenario.NotesKey}`):**");
            foreach (var note in notes)
            {
                Console.WriteLine($"- {note}");
            }
        }

        if (scenario.RelatedIds.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"**See also:** {string.Join(", ", scenario.RelatedIds.Select(id => $"`{id}`"))}");
        }

        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: mur get <id> [--raw]");
        Console.WriteLine("Show a sample scenario.");
    }
}
