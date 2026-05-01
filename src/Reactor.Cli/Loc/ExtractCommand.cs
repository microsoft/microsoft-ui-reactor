namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Implements `duct loc extract`: scans C# source files for localizable strings,
/// generates .resw entries, and optionally rewrites source to use t.Message().
/// </summary>
internal static class ExtractCommand
{
    public static int Run(string[] args)
    {
        string? sourcePath = null;
        string? outputPath = null;
        bool dryRun = false;
        bool rewrite = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --source requires a value."); return 1; }
                    sourcePath = args[++i];
                    break;
                case "--output":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --output requires a value."); return 1; }
                    outputPath = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--rewrite":
                    rewrite = true;
                    break;
                case "--help" or "-h":
                    ShowHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        sourcePath ??= ".";
        outputPath ??= Path.Combine("Strings", "en-US");

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: Source directory '{sourcePath}' does not exist.");
            return 1;
        }

        // SECURITY (TASK-035): skip reparse points so a junction in the
        // source tree can't redirect us out of the repo. EnumerationOptions
        // works recursively and respects AttributesToSkip.
        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        var csFiles = Directory.GetFiles(sourcePath, "*.cs", enumOpts);
        if (csFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: No .cs files found in '{sourcePath}'.");
            return 1;
        }

        // Scan all source files
        var allExtractions = new List<LocalizableString>();
        int skippedFiles = 0;

        foreach (var file in csFiles)
        {
            try
            {
                var source = File.ReadAllText(file);
                var extractions = LocalizableStringScanner.Scan(source, file);
                allExtractions.AddRange(extractions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to parse {file}: {ex.Message}");
                skippedFiles++;
            }
        }

        if (allExtractions.Count == 0)
        {
            Console.WriteLine("No localizable strings found.");
            return 0;
        }

        // Generate keys and deduplicate
        var keyed = KeyNamer.AssignKeys(allExtractions);

        // Load existing .resw entries to preserve them
        var existingEntries = ReswWriter.LoadExisting(outputPath);

        // Determine new entries
        var newEntries = new List<KeyedLocString>();
        var skipCount = 0;

        foreach (var entry in keyed)
        {
            var fullKey = entry.Namespace == null ? entry.Key : $"{entry.Namespace}.{entry.Key}";
            if (existingEntries.ContainsKey((entry.ReswFileName, entry.Key)))
            {
                Console.WriteLine($"[SKIP] {fullKey} (already in {entry.ReswFileName})");
                skipCount++;
            }
            else
            {
                Console.WriteLine($"[NEW]  {fullKey} = \"{entry.Value}\"");
                newEntries.Add(entry);
            }
        }

        // Report warnings
        foreach (var entry in keyed)
        {
            if (entry.Warning != null)
                Console.WriteLine($"[WARN] {entry.Warning}");
        }

        if (newEntries.Count == 0 && !rewrite)
        {
            Console.WriteLine($"\nNo new strings to extract ({skipCount} already extracted).");
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine($"\nDry run: {newEntries.Count} new keys would be added, {skipCount} already exist.");
            if (rewrite)
                Console.WriteLine($"         {keyed.Count} source locations would be rewritten.");
            return newEntries.Count > 0 ? 1 : 0;
        }

        // Write .resw files
        ReswWriter.Write(outputPath, newEntries);
        Console.WriteLine($"\nWrote {newEntries.Count} new keys to {outputPath}/");

        // Rewrite source files
        if (rewrite)
        {
            var rewriteCount = SourceRewriter.Rewrite(keyed);
            Console.WriteLine($"Rewrote {rewriteCount} source locations.");
        }

        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("duct loc extract — Extract localizable strings from source files");
        Console.WriteLine();
        Console.WriteLine("Usage: duct loc extract [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source <dir>   Source directory to scan (default: .)");
        Console.WriteLine("  --output <dir>   Output directory for .resw files (default: Strings/en-US/)");
        Console.WriteLine("  --rewrite        Rewrite source files to use t.Message()");
        Console.WriteLine("  --dry-run        Report changes without writing files");
    }
}
