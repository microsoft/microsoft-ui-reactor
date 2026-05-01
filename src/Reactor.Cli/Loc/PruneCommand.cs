using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Implements `duct loc prune`: finds and removes .resw keys not referenced in source code.
/// </summary>
internal static class PruneCommand
{
    // Matches Loc.Namespace.Key or Loc.Key (flat layout)
    private static readonly Regex LocRefPattern = new(
        @"Loc\.(\w+)\.(\w+)|Loc\.(\w+)",
        RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        string? sourcePath = null;
        string? resourcesPath = null;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --source requires a value."); return 1; }
                    sourcePath = args[++i];
                    break;
                case "--resources":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --resources requires a value."); return 1; }
                    resourcesPath = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
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
        resourcesPath ??= Path.Combine("Strings", "en-US");

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: Source directory '{sourcePath}' does not exist.");
            return 1;
        }

        if (!Directory.Exists(resourcesPath))
        {
            Console.Error.WriteLine($"Error: Resources directory '{resourcesPath}' does not exist.");
            return 1;
        }

        // 1. Scan all .cs files for Loc.X.Y references
        var referencedKeys = ScanSourceReferences(sourcePath);

        // 2. Load all keys from .resw files in the resources directory
        var reswFiles = ReswReader.ReadLocale(resourcesPath);
        if (reswFiles.Count == 0)
        {
            Console.Error.WriteLine($"Error: No .resw files found in '{resourcesPath}'.");
            return 1;
        }

        // Determine if flat layout (single Resources.resw)
        bool isFlatLayout = reswFiles.Count == 1 &&
                            reswFiles[0].Namespace.Equals("Resources", StringComparison.OrdinalIgnoreCase);

        // 3. Find unused keys
        var unusedKeys = new List<(string ns, string key)>();

        foreach (var file in reswFiles)
        {
            foreach (var entry in file.Entries)
            {
                bool referenced;
                if (isFlatLayout)
                {
                    // Flat: Loc.Key
                    referenced = referencedKeys.Contains(("", entry.Key));
                }
                else
                {
                    // Namespaced: Loc.Namespace.Key
                    referenced = referencedKeys.Contains((file.Namespace, entry.Key));
                }

                if (!referenced)
                {
                    Console.WriteLine($"  UNUSED: {file.Namespace}.{entry.Key} (not referenced in any .cs file)");
                    unusedKeys.Add((file.Namespace, entry.Key));
                }
            }
        }

        if (unusedKeys.Count == 0)
        {
            Console.WriteLine("No unused keys found.");
            return 0;
        }

        Console.WriteLine($"  {unusedKeys.Count} unused key(s) found.");

        if (dryRun)
        {
            Console.WriteLine("  Run without --dry-run to remove.");
            return 1; // Non-zero for CI gating
        }

        // 4. Remove unused keys from all locale .resw files in parent Strings/ dir
        var stringsDir = Path.GetDirectoryName(resourcesPath)!;
        int removedCount = RemoveKeys(stringsDir, unusedKeys);

        Console.WriteLine($"  Removed {unusedKeys.Count} key(s) from {removedCount} locale file(s).");
        return 0;
    }

    /// <summary>
    /// Scans .cs files for Loc.Namespace.Key references.
    /// Returns a set of (namespace, key) tuples. For flat layout refs (Loc.Key), namespace is "".
    /// </summary>
    internal static HashSet<(string ns, string key)> ScanSourceReferences(string sourceDir)
    {
        var refs = new HashSet<(string, string)>();

        // SECURITY (TASK-035): skip reparse points to avoid traversing
        // junctions/symlinks that escape the source tree.
        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        foreach (var file in Directory.GetFiles(sourceDir, "*.cs", enumOpts))
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"  Warning: Could not read '{file}': {ex.Message}");
                continue;
            }

            foreach (Match match in LocRefPattern.Matches(content))
            {
                if (match.Groups[1].Success && match.Groups[2].Success)
                {
                    // Loc.Namespace.Key
                    refs.Add((match.Groups[1].Value, match.Groups[2].Value));
                }
                else if (match.Groups[3].Success)
                {
                    // Loc.Key (flat layout) — but could also be Loc.Namespace (first part of a chain)
                    // We add it as a flat key; the caller disambiguates
                    refs.Add(("", match.Groups[3].Value));
                }
            }
        }

        return refs;
    }

    /// <summary>
    /// Removes specified keys from all .resw files across all locale directories under stringsDir.
    /// </summary>
    private static int RemoveKeys(string stringsDir, List<(string ns, string key)> keysToRemove)
    {
        var keySet = new HashSet<(string, string)>(keysToRemove);
        int filesModified = 0;

        foreach (var localeDir in Directory.GetDirectories(stringsDir))
        {
            foreach (var reswFile in Directory.GetFiles(localeDir, "*.resw"))
            {
                var ns = Path.GetFileNameWithoutExtension(reswFile);
                var keysInNs = keySet.Where(k => k.Item1 == ns).Select(k => k.Item2).ToHashSet();
                if (keysInNs.Count == 0) continue;

                try
                {
                    var doc = XDocument.Load(reswFile);
                    var root = doc.Root;
                    if (root == null) continue;

                    var toRemove = root.Elements("data")
                        .Where(d => keysInNs.Contains(d.Attribute("name")?.Value ?? ""))
                        .ToList();

                    if (toRemove.Count == 0) continue;

                    foreach (var elem in toRemove)
                        elem.Remove();

                    doc.Save(reswFile);
                    filesModified++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Warning: Could not update '{reswFile}': {ex.Message}");
                }
            }
        }

        return filesModified;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("duct loc prune — Find and remove unused localization keys");
        Console.WriteLine();
        Console.WriteLine("Usage: duct loc prune [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source <dir>      Source directory to scan for references (default: .)");
        Console.WriteLine("  --resources <dir>   .resw directory to check (default: Strings/en-US/)");
        Console.WriteLine("  --dry-run           Report unused keys without removing them");
    }
}
