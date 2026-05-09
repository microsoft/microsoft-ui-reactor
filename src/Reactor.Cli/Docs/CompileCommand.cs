using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Orchestrates the doc compile pipeline: validate → build → capture → extract → (AI) → assemble.
/// </summary>
internal static partial class CompileCommand
{
    public static int Run(string[] args)
    {
        var topic = GetOption(args, "--topic");
        var noScreenshots = HasFlag(args, "--no-screenshots");
        var noAi = HasFlag(args, "--no-ai");
        var noBuild = HasFlag(args, "--no-build");
        var validateOnly = HasFlag(args, "--validate-only");
        var ci = HasFlag(args, "--ci");

        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (looking for Reactor.slnx or .git).");
            return 1;
        }

        var docsRoot = Path.Combine(repoRoot, "docs");
        var appsDir = Path.Combine(docsRoot, "_pipeline", "apps");
        var templatesDir = Path.Combine(docsRoot, "_pipeline", "templates");
        var outputDir = Path.Combine(docsRoot, "guide");
        var imagesDir = Path.Combine(outputDir, "images");

        // ── Phase 1: Validate ─────────────────────────────────────────────
        Console.WriteLine("═══ Phase 1: Validate ═══");

        var apps = DiscoverApps(appsDir, topic);
        Console.WriteLine($"  Found {apps.Count} doc app(s)");
        foreach (var (id, dir) in apps)
            Console.WriteLine($"    • {id} → {Path.GetRelativePath(repoRoot, dir)}");

        var templates = DiscoverTemplates(templatesDir, topic);
        Console.WriteLine($"  Found {templates.Count} template(s)");
        foreach (var (id, t) in templates)
            Console.WriteLine($"    • {id} → {Path.GetRelativePath(repoRoot, t.FilePath)}");

        if (apps.Count == 0 && templates.Count == 0)
        {
            Console.Error.WriteLine("  No doc apps or templates found.");
            return 1;
        }

        // ── Phase 4 (early): Extract snippets ─────────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Phase 4: Extract Snippets ═══");

        var allSnippets = new Dictionary<string, SnippetExtractor.Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var (topicId, appDir) in apps)
        {
            var snippets = SnippetExtractor.ExtractFromApp(appDir, topicId);
            foreach (var (key, value) in snippets)
            {
                allSnippets[key] = value;
                var lineCount = value.Code.Split('\n').Length;
                Console.WriteLine($"  {key} ({lineCount} lines from {Path.GetFileName(value.SourceFile)}:{value.StartLine})");
            }
        }
        Console.WriteLine($"  Total: {allSnippets.Count} snippet(s)");

        // Build screenshot registry from manifests
        var allScreenshots = new Dictionary<string, ScreenshotInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (topicId, appDir) in apps)
        {
            var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
            if (!File.Exists(manifestPath)) continue;
            var manifest = ManifestParser.Parse(manifestPath);
            foreach (var ss in manifest.Screenshots)
            {
                var fullId = $"{topicId}/{ss.Id}";
                allScreenshots[fullId] = new ScreenshotInfo(ss.Id, topicId, ss.Description, ss.Format);
            }
        }
        Console.WriteLine($"  Screenshot definitions: {allScreenshots.Count}");

        // Validate references
        var hasErrors = false;
        foreach (var (topicId, template) in templates)
        {
            foreach (var snippetRef in ExtractSnippetRefs(template.Body))
            {
                if (!allSnippets.ContainsKey(snippetRef))
                {
                    Console.Error.WriteLine($"  ✗ Template '{topicId}': missing snippet '{snippetRef}'");
                    hasErrors = true;
                }
                else
                {
                    Console.WriteLine($"  ✓ snippet \"{snippetRef}\" resolved");
                }
            }

            foreach (var ssRef in ExtractScreenshotRefs(template.Body))
            {
                if (!allScreenshots.ContainsKey(ssRef))
                    Console.WriteLine($"  ⚠ Template '{topicId}': no screenshot definition for '{ssRef}'");
                else
                    Console.WriteLine($"  ✓ screenshot \"{ssRef}\" resolved");
            }
        }

        if (hasErrors && ci)
        {
            Console.Error.WriteLine("Validation failed.");
            return 1;
        }

        if (validateOnly)
        {
            Console.WriteLine();
            Console.WriteLine(hasErrors ? "Validation finished with errors." : "Validation passed.");
            return hasErrors ? 1 : 0;
        }

        // ── Phase 2: Build ────────────────────────────────────────────────
        Console.WriteLine();
        if (noBuild)
        {
            Console.WriteLine("═══ Phase 2: Build (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 2: Build ═══");
            foreach (var (topicId, appDir) in apps)
            {
                Console.Write($"  Building {topicId}...");
                var exitCode = BuildApp(appDir);
                if (exitCode != 0)
                {
                    Console.Error.WriteLine($" ✗ build failed (exit code {exitCode})");
                    return 1;
                }
                Console.WriteLine(" ✓");
            }
        }

        // ── Phase 3: Capture ──────────────────────────────────────────────
        Console.WriteLine();
        if (noScreenshots)
        {
            Console.WriteLine("═══ Phase 3: Capture (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 3: Capture ═══");
            foreach (var (topicId, appDir) in apps)
            {
                var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
                if (!File.Exists(manifestPath)) continue;
                var manifest = ManifestParser.Parse(manifestPath);
                if (manifest.Screenshots.Count == 0) continue;

                Console.WriteLine($"  Capturing for {topicId}...");
                ScreenshotCapture.CaptureAsync(appDir, topicId, manifest, imagesDir)
                    .GetAwaiter().GetResult();
            }
        }

        // ── Phase 5: AI Author ────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine($"═══ Phase 5: AI Author {(noAi ? "(skipped)" : "(not yet implemented)")} ═══");

        // ── Phase 6: Assemble ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Phase 6: Assemble ═══");
        Directory.CreateDirectory(outputDir);

        foreach (var (topicId, template) in templates)
        {
            Console.Write($"  Assembling {topicId}...");

            var assembled = DocAssembler.Assemble(
                template.Body, allSnippets, allScreenshots,
                out var errors, out var warnings);

            foreach (var e in errors) Console.Error.WriteLine($"\n    ✗ {e}");
            foreach (var w in warnings) Console.WriteLine($"\n    ⚠ {w}");

            var outputPath = Path.Combine(outputDir, $"{topicId}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, assembled);
            Console.WriteLine($" ✓ → {Path.GetRelativePath(repoRoot, outputPath)}");
        }

        Console.WriteLine();
        Console.WriteLine("Documentation compiled successfully.");
        return 0;
    }

    // ── Discovery ─────────────────────────────────────────────────────────

    private static List<(string topicId, string dir)> DiscoverApps(string appsDir, string? topic)
    {
        var result = new List<(string, string)>();
        if (!Directory.Exists(appsDir)) return result;

        foreach (var dir in Directory.GetDirectories(appsDir))
        {
            var topicId = Path.GetFileName(dir);
            if (topic != null && !topicId.Equals(topic, StringComparison.OrdinalIgnoreCase))
                continue;
            // Must have at least one .cs file
            if (Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Length > 0)
                result.Add((topicId, dir));
        }
        return result;
    }

    private static List<(string topicId, DocTemplate template)> DiscoverTemplates(string templatesDir, string? topic)
    {
        var result = new List<(string, DocTemplate)>();
        if (!Directory.Exists(templatesDir)) return result;

        foreach (var file in Directory.GetFiles(templatesDir, "*.md.dt"))
        {
            var topicId = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
            if (topic != null && !topicId.Equals(topic, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add((topicId, TemplateParser.Parse(file)));
        }

        return result.OrderBy(t => t.Item2.Order).ToList();
    }

    // ── Build ─────────────────────────────────────────────────────────────

    private static int BuildApp(string appDir)
    {
        var csproj = Directory.GetFiles(appDir, "*.csproj").FirstOrDefault();
        if (csproj == null) return 1;

        // WindowsAppSDK self-contained builds reject the AnyCPU default and
        // require an explicit architecture. Match the host so x64 boxes get
        // x64 binaries and ARM64 boxes get ARM64 binaries.
        var platform = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            _ => "x64",
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csproj}\" -v q --nologo -nowarn:MSB3277 -p:Platform={platform}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        // Read stdout and stderr in parallel to avoid deadlock
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine();
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.Write(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);
        }

        return process.ExitCode;
    }

    // ── Reference extraction (for validation) ─────────────────────────────

    [GeneratedRegex(@"snippet=""([^""]+)""")]
    private static partial Regex SnippetRefPattern();

    [GeneratedRegex("""screenshot://([^)]+)""")]
    private static partial Regex ScreenshotRefPattern();

    private static List<string> ExtractSnippetRefs(string body) =>
        SnippetRefPattern().Matches(body).Select(m => m.Groups[1].Value).ToList();

    private static List<string> ExtractScreenshotRefs(string body) =>
        ScreenshotRefPattern().Matches(body).Select(m => m.Groups[1].Value).ToList();

    // ── Arg parsing ───────────────────────────────────────────────────────

    private static string? FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? GetOption(string[] args, string name)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
