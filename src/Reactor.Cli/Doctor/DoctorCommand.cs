// `mur doctor` — diagnose a Reactor developer install.
//
// Verifies the prerequisites and post-bootstrap state that getting-started.md
// depends on. Prints a one-line PASS / WARN / FAIL per check and exits non-zero
// only when there are FAILs — WARNs (e.g. missing-template-enumeration or a
// stale-looking checkout) still exit 0 since the install is usable. Designed
// to be the first thing a confused user runs after `dotnet new reactorapp`
// fails — every FAIL prints a copy-pasteable next step.
//
// Checks (in order):
//   1. .NET SDK >= 10
//   2. `mur` itself: global-tool install or PATH-resolved bin-mirror; with
//      --verbose, also the resolved version string
//   3. Repo-checkout discovery (warns if not inside a Reactor source checkout,
//      and skips the two `local-nupkgs/` checks below)
//   4. local-nupkgs/Microsoft.UI.Reactor.<ver>.nupkg present (framework)
//   5. local-nupkgs/Microsoft.UI.Reactor.ProjectTemplates.<ver>.nupkg present
//   6. `dotnet new` template list includes `reactorapp` (always runs — does
//      not depend on the repo checkout being found)
//   7. Claude plugin at ~/.claude/plugins/reactor (informational only; not
//      every developer uses Claude Code)

using System.Diagnostics;
using Microsoft.UI.Reactor.Cli.Pack;

namespace Microsoft.UI.Reactor.Cli.Doctor;

public static class DoctorCommand
{
    public static int Run(string[] args)
    {
        var verbose = args.Contains("--verbose") || args.Contains("-v");
        var failures = 0;
        var warnings = 0;

        Console.WriteLine("mur doctor — checking your Reactor install");
        Console.WriteLine();

        // 1. .NET SDK
        var sdks = GetDotnetSdks();
        if (sdks is null)
        {
            Fail("dotnet SDK", "`dotnet` not found on PATH. Install .NET 10+: https://dotnet.microsoft.com/download");
            failures++;
        }
        else
        {
            var has10 = sdks.Any(v => v.Major >= 10);
            if (has10)
            {
                var newest = sdks.Max()!;
                Pass(".NET SDK", $"{newest} installed");
            }
            else
            {
                var newest = sdks.Count > 0 ? sdks.Max()!.ToString() : "(none)";
                Fail(".NET SDK", $"latest installed is {newest}; Reactor requires .NET 10+. https://dotnet.microsoft.com/download");
                failures++;
            }
        }

        // 2. mur location and version
        var murCmd = ResolveCommand("mur");
        var globalTool = IsGlobalToolInstalled();
        if (murCmd is null && !globalTool)
        {
            Fail("mur on PATH", "`mur` is not resolvable. Run ./bootstrap.ps1 from the repo root.");
            failures++;
        }
        else
        {
            var kind = globalTool ? "global tool" : "PATH";
            var loc = murCmd ?? "(global tool)";
            Pass("mur installed", $"{kind} — {loc}");
            if (verbose)
            {
                var ver = TryGetMurVersion();
                if (ver is not null) Console.WriteLine($"           version: {ver}");
            }
        }

        // 3. local-nupkgs feed
        var repoRoot = RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory())
                    ?? RepoRootFinder.FindRepoRoot();
        if (repoRoot is null)
        {
            Warn("repo checkout", "not running inside a Reactor source checkout — skipping local-feed checks (the `dotnet new` template check below still runs)");
            warnings++;
        }
        else
        {
            var feed = Path.Combine(repoRoot, "local-nupkgs");
            var frameworkNupkg = Path.Combine(feed, $"Microsoft.UI.Reactor.{PackLocalCommand.DefaultLocalVersion}.nupkg");
            var templateNupkg = Path.Combine(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{PackLocalCommand.DefaultLocalVersion}.nupkg");

            if (!File.Exists(frameworkNupkg))
            {
                Fail("local Reactor nupkg", $"missing {frameworkNupkg}. Run `mur pack-local` (or `mur upgrade`).");
                failures++;
            }
            else
            {
                Pass("local Reactor nupkg", $"{Path.GetFileName(frameworkNupkg)} ({FormatAge(File.GetLastWriteTimeUtc(frameworkNupkg))})");
            }

            if (!File.Exists(templateNupkg))
            {
                Fail("local template nupkg", $"missing {templateNupkg}. Run `mur pack-local`.");
                failures++;
            }
            else
            {
                Pass("local template nupkg", $"{Path.GetFileName(templateNupkg)}");
            }
        }

        // 4. dotnet new reactorapp template
        var templates = ListInstalledTemplates();
        if (templates is null)
        {
            Warn("dotnet new template", "could not enumerate `dotnet new` templates");
            warnings++;
        }
        else if (templates.Any(t => t.IndexOf("reactorapp", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            Pass("dotnet new template", "reactorapp registered");
        }
        else
        {
            Fail("dotnet new template", "reactorapp not registered. Run `mur upgrade` or `dotnet new install <repo>/local-nupkgs/Microsoft.UI.Reactor.ProjectTemplates.0.0.0-local.nupkg`.");
            failures++;
        }

        // 5. Claude plugin (informational only — many devs don't use it)
        var claudePluginDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "plugins", "reactor");
        if (Directory.Exists(claudePluginDir))
        {
            var pluginJson = Path.Combine(claudePluginDir, "plugin.json");
            if (File.Exists(pluginJson))
                Pass("Claude plugin", $"installed at {claudePluginDir}");
            else
                Warn("Claude plugin", $"{claudePluginDir} exists but missing plugin.json");
        }
        else
        {
            Info("Claude plugin", "not installed (run `mur upgrade` or `./bootstrap.ps1` if you use Claude Code)");
        }

        Console.WriteLine();
        if (failures > 0)
        {
            Console.WriteLine($"  {failures} failure(s), {warnings} warning(s). Fix the FAIL lines above, then re-run `mur doctor`.");
            return 1;
        }
        if (warnings > 0)
        {
            Console.WriteLine($"  OK — {warnings} warning(s). Your install is functional.");
            return 0;
        }
        Console.WriteLine("  All checks passed. You're ready to `dotnet new reactorapp -n MyApp`.");
        return 0;
    }

    // -----------------------------------------------------------------------
    //  Output formatting
    // -----------------------------------------------------------------------

    static void Pass(string label, string detail) => Line("PASS", label, detail, ConsoleColor.Green);
    static void Warn(string label, string detail) { Line("WARN", label, detail, ConsoleColor.Yellow); }
    static void Fail(string label, string detail) { Line("FAIL", label, detail, ConsoleColor.Red); }
    static void Info(string label, string detail) => Line("info", label, detail, ConsoleColor.Gray);

    static void Line(string status, string label, string detail, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"  [{status}]");
        Console.ForegroundColor = prev;
        Console.WriteLine($" {label,-22} {detail}");
    }

    // -----------------------------------------------------------------------
    //  Probes
    // -----------------------------------------------------------------------

    static List<Version>? GetDotnetSdks()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "--list-sdks" },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            var versions = new List<Version>();
            foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                var space = line.IndexOf(' ');
                if (space <= 0) continue;
                var verStr = line[..space];
                // Trim preview/RC suffixes — Version.Parse can't eat them.
                var dash = verStr.IndexOf('-');
                if (dash > 0) verStr = verStr[..dash];
                if (Version.TryParse(verStr, out var v)) versions.Add(v);
            }
            return versions;
        }
        catch
        {
            return null;
        }
    }

    static string? ResolveCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "where" : "which")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { command },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return null;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim())
                         .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    static bool IsGlobalToolInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "tool", "list", "-g" },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output.IndexOf("microsoft.ui.reactor.cli", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    static string? TryGetMurVersion()
    {
        try
        {
            var psi = new ProcessStartInfo("mur")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "--version" },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    static List<string>? ListInstalledTemplates()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "new", "list" },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.TrimEnd())
                         .ToList();
        }
        catch
        {
            return null;
        }
    }

    static string FormatAge(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays}d ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
