namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Implements `mur loc validate`: checks ICU syntax validity and parameter consistency
/// across all locale .resw files.
/// </summary>
internal static class ValidateCommand
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

        int errors = 0;
        int warnings = 0;

        // Group by locale
        var byLocale = allFiles.GroupBy(f => f.Locale).ToDictionary(g => g.Key, g => g.ToList());

        // Get source locale data for parameter comparison
        var sourceFiles = byLocale.GetValueOrDefault(defaultLocale) ?? [];
        if (sourceFiles.Count == 0)
        {
            Console.Error.WriteLine($"WARN: No .resw files for '{defaultLocale}'.");
            warnings++;
        }
        var sourceKeys = new Dictionary<(string ns, string key), string>();
        var sourceParams = new Dictionary<(string ns, string key), HashSet<string>>();

        foreach (var file in sourceFiles)
        {
            foreach (var entry in file.Entries)
            {
                sourceKeys[(file.Namespace, entry.Key)] = entry.Value;
                sourceParams[(file.Namespace, entry.Key)] = ReswReader.ExtractIcuParameters(entry.Value);
            }
        }

        // 1. Validate ICU syntax in all files
        foreach (var file in allFiles)
        {
            foreach (var entry in file.Entries)
            {
                var syntaxError = ReswReader.ValidateIcuSyntax(entry.Value);
                if (syntaxError != null)
                {
                    Console.Error.WriteLine(
                        $"ERROR: {file.Locale}/{file.Namespace}.resw:{entry.Key} — ICU parse error: {syntaxError}");
                    errors++;
                }
            }
        }

        // 2. Check parameter consistency for non-source locales
        foreach (var (locale, files) in byLocale)
        {
            if (locale == defaultLocale) continue;

            foreach (var file in files)
            {
                foreach (var entry in file.Entries)
                {
                    var compositeKey = (file.Namespace, entry.Key);

                    if (!sourceParams.TryGetValue(compositeKey, out var expectedParams))
                        continue; // Key doesn't exist in source — that's fine (might be locale-specific)

                    var actualParams = ReswReader.ExtractIcuParameters(entry.Value);

                    // Check for parameters in source missing from translation
                    foreach (var param in expectedParams)
                    {
                        if (!actualParams.Contains(param))
                        {
                            Console.Error.WriteLine(
                                $"WARN:  {locale}/{file.Namespace}.resw:{entry.Key} — parameter {{{param}}} in {defaultLocale} but missing in {locale}");
                            warnings++;
                        }
                    }

                    // Check for unexpected parameters in translation
                    foreach (var param in actualParams)
                    {
                        if (!expectedParams.Contains(param))
                        {
                            Console.Error.WriteLine(
                                $"WARN:  {locale}/{file.Namespace}.resw:{entry.Key} — parameter {{{param}}} in {locale} but not in {defaultLocale}");
                            warnings++;
                        }
                    }
                }
            }
        }

        // 3. Check for missing keys in non-source locales
        foreach (var (locale, files) in byLocale)
        {
            if (locale == defaultLocale) continue;

            var localeKeys = new HashSet<(string ns, string key)>();
            foreach (var file in files)
            {
                foreach (var entry in file.Entries)
                {
                    localeKeys.Add((file.Namespace, entry.Key));
                }
            }

            foreach (var (ns, key) in sourceKeys.Keys)
            {
                if (!localeKeys.Contains((ns, key)))
                {
                    Console.Error.WriteLine(
                        $"WARN:  {locale}/{ns}.resw — missing key: {key}");
                    warnings++;
                }
            }
        }

        // Summary
        Console.WriteLine();
        if (errors == 0 && warnings == 0)
        {
            Console.WriteLine("Validation passed: no errors or warnings.");
            return 0;
        }

        Console.WriteLine($"Validation complete: {errors} error(s), {warnings} warning(s).");
        return errors > 0 ? 1 : 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("mur loc validate — Check ICU syntax and parameter consistency");
        Console.WriteLine();
        Console.WriteLine("Usage: mur loc validate [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --resources <dir>      Strings directory (default: Strings/)");
        Console.WriteLine("  --default-locale <loc>  Source locale (default: en-US)");
    }
}
