using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Implements `mur loc translate`: AI-translates .resw files to target locales
/// using GitHub Copilot SDK.
/// </summary>
internal static class TranslateCommand
{
    private const int BatchSize = 25;

    public static int Run(string[] args)
    {
        string? sourcePath = null;
        string? targetLocales = null;
        bool missingOnly = false;
        string? model = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --source requires a value."); return 1; }
                    sourcePath = args[++i];
                    break;
                case "--target":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --target requires a value."); return 1; }
                    targetLocales = args[++i];
                    break;
                case "--missing-only":
                    missingOnly = true;
                    break;
                case "--model":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --model requires a value."); return 1; }
                    model = args[++i];
                    break;
                case "--help" or "-h":
                    ShowHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        sourcePath ??= Path.Combine("Strings", "en-US");

        if (targetLocales == null)
        {
            Console.Error.WriteLine("Error: --target is required (e.g., --target fr-FR,ar-SA).");
            return 1;
        }

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: Source directory '{sourcePath}' does not exist.");
            return 1;
        }

        var targets = targetLocales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets.Length == 0)
        {
            Console.Error.WriteLine("Error: No target locales specified.");
            return 1;
        }

        // Load source .resw files
        var sourceFiles = ReswReader.ReadLocale(sourcePath);
        if (sourceFiles.Count == 0)
        {
            Console.Error.WriteLine($"Error: No .resw files found in '{sourcePath}'.");
            return 1;
        }

        var sourceLocale = Path.GetFileName(sourcePath);
        var stringsDir = Path.GetDirectoryName(sourcePath)!;

        ITranslationProvider provider = new CopilotTranslationProvider(model);
        Console.WriteLine($"Using provider: {provider.Name}");

        // Run async translation
        return RunAsync(provider, sourceFiles, sourceLocale, targets, stringsDir, missingOnly)
            .GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(
        ITranslationProvider provider,
        List<ReswFileData> sourceFiles,
        string sourceLocale,
        string[] targets,
        string stringsDir,
        bool missingOnly)
    {
        int totalTranslated = 0;
        int totalErrors = 0;

        foreach (var targetLocale in targets)
        {
            Console.WriteLine($"\nTranslating to {targetLocale}...");

            var targetDir = Path.Combine(stringsDir, targetLocale);
            Directory.CreateDirectory(targetDir);

            // Load existing target translations
            var existingTargetFiles = ReswReader.ReadLocale(targetDir);
            var existingByNs = existingTargetFiles.ToDictionary(f => f.Namespace, f => f);

            foreach (var sourceFile in sourceFiles)
            {
                var existingEntries = existingByNs.GetValueOrDefault(sourceFile.Namespace);
                var existingKeys = existingEntries?.Entries
                    .ToDictionary(e => e.Key, e => e.Value) ?? new();

                // Determine which keys need translation
                var entriesToTranslate = new List<TranslationEntry>();

                foreach (var entry in sourceFile.Entries)
                {
                    if (missingOnly)
                    {
                        // Skip keys that already have any translation
                        if (existingKeys.ContainsKey(entry.Key))
                            continue;
                    }
                    else
                    {
                        // Skip keys that already have a human-reviewed translation
                        if (existingKeys.ContainsKey(entry.Key))
                        {
                            var existingEntry = existingEntries!.Entries.First(e => e.Key == entry.Key);
                            if (!existingEntry.IsAiDraft)
                                continue;
                        }
                    }

                    entriesToTranslate.Add(new TranslationEntry
                    {
                        Key = entry.Key,
                        Value = entry.Value,
                        Comment = entry.Comment,
                    });
                }

                if (entriesToTranslate.Count == 0)
                {
                    Console.WriteLine($"  {sourceFile.Namespace}: all keys already translated");
                    continue;
                }

                Console.Write($"  {sourceFile.Namespace}: {entriesToTranslate.Count} key(s) to translate...");

                // Translate in batches
                var allTranslations = new Dictionary<string, string>();
                var allErrors = new Dictionary<string, string>();

                for (int batchStart = 0; batchStart < entriesToTranslate.Count; batchStart += BatchSize)
                {
                    var batchEntries = entriesToTranslate
                        .Skip(batchStart)
                        .Take(BatchSize)
                        .ToList();

                    var batch = new TranslationBatch
                    {
                        TargetLocale = targetLocale,
                        SourceLocale = sourceLocale,
                        Entries = batchEntries,
                        ExistingTranslations = existingKeys,
                    };

                    try
                    {
                        var result = await provider.TranslateAsync(batch);

                        foreach (var (key, value) in result.Translations)
                            allTranslations[key] = value;

                        foreach (var (key, error) in result.Errors)
                            allErrors[key] = error;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"\n    Error translating batch: {ex.Message}");
                        foreach (var entry in batchEntries)
                            allErrors[entry.Key] = ex.Message;
                    }
                }

                // Write translations to .resw file
                if (allTranslations.Count > 0)
                {
                    WriteTranslations(targetDir, sourceFile.Namespace, allTranslations, existingEntries);
                }

                totalTranslated += allTranslations.Count;
                totalErrors += allErrors.Count;

                Console.WriteLine($" {allTranslations.Count} translated, {allErrors.Count} failed");

                foreach (var (key, error) in allErrors)
                {
                    Console.Error.WriteLine($"    [ERROR] {key}: {error}");
                }
            }
        }

        Console.WriteLine($"\nDone: {totalTranslated} key(s) translated, {totalErrors} error(s).");
        Console.WriteLine("All translations marked as ai-translated: pending-review");
        return totalErrors > 0 ? 1 : 0;
    }

    /// <summary>
    /// Writes translated entries to a target .resw file, marking them with the AI draft comment.
    /// </summary>
    private static void WriteTranslations(
        string targetDir,
        string ns,
        Dictionary<string, string> translations,
        ReswFileData? existingFile)
    {
        var filePath = Path.Combine(targetDir, $"{ns}.resw");

        XDocument doc;
        XElement root;

        if (File.Exists(filePath))
        {
            doc = XDocument.Load(filePath);
            root = doc.Root!;
        }
        else
        {
            root = new XElement("root",
                new XElement("resheader",
                    new XAttribute("name", "resmimetype"),
                    new XElement("value", "text/microsoft-resx")),
                new XElement("resheader",
                    new XAttribute("name", "version"),
                    new XElement("value", "2.0")),
                new XElement("resheader",
                    new XAttribute("name", "reader"),
                    new XElement("value", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")),
                new XElement("resheader",
                    new XAttribute("name", "writer"),
                    new XElement("value", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"))
            );
            doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                root);
        }

        foreach (var (key, value) in translations)
        {
            // Remove existing entry if present (we're replacing it)
            var existing = root.Elements("data")
                .FirstOrDefault(d => d.Attribute("name")?.Value == key);
            existing?.Remove();

            var dataElement = new XElement("data",
                new XAttribute("name", key),
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", value),
                new XElement("comment", "ai-translated: pending-review"));

            root.Add(dataElement);
        }

        // Sort data elements alphabetically
        var headers = root.Elements("resheader").ToList();
        var dataElements = root.Elements("data")
            .OrderBy(d => d.Attribute("name")?.Value, StringComparer.Ordinal)
            .ToList();

        root.RemoveNodes();
        foreach (var h in headers) root.Add(h);
        foreach (var d in dataElements) root.Add(d);

        doc.Save(filePath);
    }

    private static void ShowHelp()
    {
        Console.WriteLine("mur loc translate — AI-translate .resw files to target locales");
        Console.WriteLine();
        Console.WriteLine("Usage: mur loc translate [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source <dir>     Source locale directory (default: Strings/en-US/)");
        Console.WriteLine("  --target <locales>  Comma-separated target locales (e.g., fr-FR,ar-SA)");
        Console.WriteLine("  --missing-only     Only translate missing or AI-draft keys");
        Console.WriteLine("  --model <name>     Model to use (default: gpt-5.4-mini, or COPILOT_MODEL env var)");
        Console.WriteLine();
        Console.WriteLine("Requires GitHub CLI (gh) to be installed and authenticated with a");
        Console.WriteLine("Copilot-enabled GitHub account.");
    }
}
