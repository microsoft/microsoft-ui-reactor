namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Implements `mur loc status`: shows translation coverage per locale as a table.
/// </summary>
internal static class StatusCommand
{
    public static int Run(string[] args)
    {
        string? resourcesPath = null;
        string defaultLocale = "en-US";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--resources":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --resources requires a value."); return 1; }
                    resourcesPath = args[++i];
                    break;
                case "--default-locale":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --default-locale requires a value."); return 1; }
                    defaultLocale = args[++i];
                    break;
                case "--help" or "-h":
                    ShowHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        resourcesPath ??= "Strings";

        if (!Directory.Exists(resourcesPath))
        {
            Console.Error.WriteLine($"Error: Resources directory '{resourcesPath}' does not exist.");
            return 1;
        }

        var allFiles = ReswReader.ReadAll(resourcesPath);
        if (allFiles.Count == 0)
        {
            Console.Error.WriteLine($"Error: No .resw files found in '{resourcesPath}'.");
            return 1;
        }

        // Build source key set
        var sourceFiles = allFiles.Where(f => f.Locale == defaultLocale).ToList();
        if (sourceFiles.Count == 0)
        {
            Console.Error.WriteLine($"WARN: No .resw files for default locale '{defaultLocale}'.");
        }
        var sourceKeySet = new HashSet<(string ns, string key)>();
        foreach (var file in sourceFiles)
        {
            foreach (var entry in file.Entries)
            {
                sourceKeySet.Add((file.Namespace, entry.Key));
            }
        }

        int totalKeys = sourceKeySet.Count;

        // Group by locale and compute stats
        var byLocale = allFiles.GroupBy(f => f.Locale)
            .OrderBy(g => g.Key == defaultLocale ? "" : g.Key) // Source locale first
            .ToList();

        var rows = new List<StatusRow>();

        foreach (var group in byLocale)
        {
            var locale = group.Key;
            var localeEntries = group.SelectMany(f => f.Entries).ToList();

            int translated = 0;
            int aiDraft = 0;
            int present = 0;

            if (locale == defaultLocale)
            {
                // Source locale is always 100% translated
                rows.Add(new StatusRow(locale, totalKeys, totalKeys, 0, 0));
                continue;
            }

            // Build set of keys present in this locale
            var localeKeys = new HashSet<(string ns, string key)>();
            foreach (var file in group)
            {
                foreach (var entry in file.Entries)
                {
                    localeKeys.Add((file.Namespace, entry.Key));

                    // Only count if it's a source key
                    if (!sourceKeySet.Contains((file.Namespace, entry.Key))) continue;

                    present++;
                    if (entry.IsAiDraft)
                        aiDraft++;
                    else
                        translated++;
                }
            }

            int missing = totalKeys - present;
            rows.Add(new StatusRow(locale, totalKeys, translated, aiDraft, missing));
        }

        // Print table
        PrintTable(rows);
        return 0;
    }

    private static void PrintTable(List<StatusRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Locale",-10} {"Keys",6} {"Translated",12} {"AI-Draft",10} {"Missing",9} {"Coverage",10}");

        foreach (var row in rows)
        {
            double coverage = row.Keys > 0
                ? (row.Keys - row.Missing) * 100.0 / row.Keys
                : 100.0;

            // `mur loc status` is a dev-tool table whose numbers are meant to be stable
            // across machines — under the current culture this printed "100,0%" on any
            // comma-decimal locale.
            Console.WriteLine(string.Create(
                global::System.Globalization.CultureInfo.InvariantCulture,
                $"{row.Locale,-10} {row.Keys,6} {row.Translated,12} {row.AiDraft,10} {row.Missing,9} {coverage,9:F1}%"));
        }

        Console.WriteLine();
    }

    private static void ShowHelp()
    {
        Console.WriteLine("mur loc status — Show translation coverage per locale");
        Console.WriteLine();
        Console.WriteLine("Usage: mur loc status [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --resources <dir>      Strings directory (default: Strings/)");
        Console.WriteLine("  --default-locale <loc>  Source locale (default: en-US)");
    }

    internal record StatusRow(string Locale, int Keys, int Translated, int AiDraft, int Missing);
}
