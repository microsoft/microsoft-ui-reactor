// `mur upgrade` — refresh a Reactor developer install after `git pull`.
//
// Re-runs the source-side steps of bootstrap.ps1:
//   1. Re-pack the framework + ProjectTemplates into local-nupkgs/
//      (delegates to `mur pack-local`).
//   2. Reinstall the `dotnet new reactorapp` template (uninstall first so the
//      template engine drops its cached copy).
//   3. Refresh the Claude Code plugin install.
//
// Does NOT update the `mur` global tool itself — a process can't replace its
// own binary mid-run. To bump `mur`, re-run ./bootstrap.ps1 from the repo
// root (or `dotnet tool update -g --add-source <repo>/local-nupkgs
// Microsoft.UI.Reactor.Cli`). `mur upgrade` prints that hint on completion.

using System.Diagnostics;
using Microsoft.UI.Reactor.Cli.Pack;

namespace Microsoft.UI.Reactor.Cli.Upgrade;

public static class UpgradeCommand
{
    public static int Run(string[] args)
    {
        var skipPlugin = args.Contains("--skip-plugin");

        var repoRoot = RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory())
                    ?? RepoRootFinder.FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("mur upgrade: must be run from inside a Reactor source checkout (could not locate src/Reactor).");
            return 1;
        }

        // 1. Re-pack framework + templates.
        Console.WriteLine("==> Repacking Microsoft.UI.Reactor + ProjectTemplates");
        var rc = PackLocalCommand.Run(Array.Empty<string>());
        if (rc != 0)
        {
            Console.Error.WriteLine("mur upgrade: pack-local failed.");
            return rc;
        }

        // 2. Reinstall the dotnet new template. Uninstall first so the template
        //    engine drops the cached version by id (the installer otherwise wins
        //    against a same-id repack — see getting-started.md caveat).
        Console.WriteLine();
        Console.WriteLine("==> Reinstalling `dotnet new reactorapp` template");
        var feed = Path.Combine(repoRoot, "local-nupkgs");
        var templateNupkg = Path.Combine(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{PackLocalCommand.DefaultLocalVersion}.nupkg");
        if (!File.Exists(templateNupkg))
        {
            Console.Error.WriteLine($"mur upgrade: template nupkg not found at {templateNupkg} after pack-local.");
            return 1;
        }
        // Uninstall is best-effort: non-zero exit just means it wasn't installed.
        RunDotnet(repoRoot, ignoreExitCode: true, "new", "uninstall", CleanLocalCommand.TemplatePackageId);
        rc = RunDotnet(repoRoot, ignoreExitCode: false, "new", "install", templateNupkg);
        if (rc != 0)
        {
            Console.Error.WriteLine("mur upgrade: template install failed.");
            return rc;
        }

        // 3. Refresh Claude plugin (best-effort; not every user has Claude Code).
        if (!skipPlugin)
        {
            Console.WriteLine();
            Console.WriteLine("==> Refreshing Claude plugin");
            var pluginSrc = Path.Combine(repoRoot, "plugins", "reactor");
            if (!Directory.Exists(pluginSrc))
            {
                Console.WriteLine($"  (skipped — {pluginSrc} not present in this checkout)");
            }
            else
            {
                var claudePluginsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude", "plugins");
                var pluginDst = Path.Combine(claudePluginsDir, "reactor");
                try
                {
                    Directory.CreateDirectory(claudePluginsDir);
                    if (Directory.Exists(pluginDst))
                    {
                        // If it's already a symlink to the source, nothing to do.
                        var info = new DirectoryInfo(pluginDst);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                            && string.Equals(info.LinkTarget, pluginSrc, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"  Symlink already current: {pluginDst} -> {pluginSrc}");
                        }
                        else
                        {
                            Directory.Delete(pluginDst, recursive: true);
                            CopyDirectory(pluginSrc, pluginDst);
                            Console.WriteLine($"  Refreshed plugin (copy): {pluginDst}");
                        }
                    }
                    else
                    {
                        CopyDirectory(pluginSrc, pluginDst);
                        Console.WriteLine($"  Installed plugin (copy): {pluginDst}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Plugin refresh failed: {ex.Message}");
                    // Non-fatal — don't bail out of the whole upgrade for a plugin issue.
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Upgrade complete.");
        Console.WriteLine();
        Console.WriteLine("  To bump `mur` itself (which can't update its own running process), run:");
        Console.WriteLine($"    dotnet tool update -g --add-source \"{feed}\" Microsoft.UI.Reactor.Cli");
        Console.WriteLine("  Or just re-run ./bootstrap.ps1 from the repo root.");
        return 0;
    }

    static int RunDotnet(string workingDirectory, bool ignoreExitCode, params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            if (ignoreExitCode) return 0;
            Console.Error.WriteLine($"mur upgrade: failed to start `dotnet {string.Join(' ', arguments)}`: {ex.Message}");
            Console.Error.WriteLine("  Verify .NET 10+ is installed and `dotnet` resolves on PATH.");
            return 1;
        }
        if (proc is null)
        {
            if (ignoreExitCode) return 0;
            Console.Error.WriteLine($"mur upgrade: `dotnet {string.Join(' ', arguments)}` did not start (Process.Start returned null).");
            return 1;
        }
        using (proc)
        {
            proc.WaitForExit();
            return ignoreExitCode ? 0 : proc.ExitCode;
        }
    }

    static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            File.Copy(file, Path.Combine(dst, rel), overwrite: true);
        }
    }
}
