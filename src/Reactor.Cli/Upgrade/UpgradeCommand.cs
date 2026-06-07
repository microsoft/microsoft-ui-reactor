// `mur upgrade` — refresh a Reactor developer install after `git pull`.
//
// Re-runs the source-side steps of bootstrap.ps1:
//   1. Re-pack the framework + ProjectTemplates into local-nupkgs/
//      (delegates to `mur pack-local`).
//   2. Reinstall the `dotnet new reactorapp` template (uninstall first so the
//      template engine drops its cached copy).
//   3. Refresh the Claude Code plugin install.
//   4. Rebuild + reinstall the Reactor VS preview extension (best-effort —
//      skipped if VS / the VSIX-dev workload aren't installed, same probe
//      logic as bootstrap.ps1 §7).
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
        var skipVsExtension = args.Contains("--skip-vs-extension");

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

        // 4. Rebuild + reinstall the Reactor VS preview extension (best-effort).
        //    Probes vswhere for an instance with the VSIX-dev workload; silently
        //    skips when VS / the workload aren't installed (same shape as
        //    bootstrap.ps1 §7). The rest of the upgrade isn't blocked by a
        //    missing VS install.
        if (!skipVsExtension)
        {
            Console.WriteLine();
            Console.WriteLine("==> Refreshing Reactor VS preview extension");
            TryReinstallVsExtension(repoRoot);
        }

        Console.WriteLine();
        Console.WriteLine("Upgrade complete.");
        Console.WriteLine();
        Console.WriteLine("  To bump `mur` itself (which can't update its own running process), run:");
        Console.WriteLine($"    dotnet tool update -g --add-source \"{feed}\" Microsoft.UI.Reactor.Cli");
        Console.WriteLine("  Or just re-run ./bootstrap.ps1 from the repo root.");
        return 0;
    }

    static void TryReinstallVsExtension(string repoRoot)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            Console.WriteLine("  (skipped — vswhere.exe not found; Visual Studio is not installed)");
            return;
        }

        var reinstall = Path.Combine(repoRoot, "src", "vs-reactor", "Reinstall-Vsix.ps1");
        if (!File.Exists(reinstall))
        {
            Console.WriteLine($"  (skipped — {reinstall} not present in this checkout)");
            return;
        }

        // Filter to instances that have the 'Visual Studio extension development'
        // workload (desktop MSBuild + VSSDK targets). Without that workload
        // Build-Vsix.ps1 fails with "Desktop MSBuild was not found" — better to
        // skip cleanly here than dump a wall of MSBuild errors into the upgrade log.
        var probe = new ProcessStartInfo(vswhere)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        probe.ArgumentList.Add("-all");
        probe.ArgumentList.Add("-prerelease");
        probe.ArgumentList.Add("-requires");
        probe.ArgumentList.Add("Microsoft.VisualStudio.Workload.VisualStudioExtension");
        probe.ArgumentList.Add("-property");
        probe.ArgumentList.Add("instanceId");

        string instances;
        try
        {
            using var probeProc = Process.Start(probe);
            if (probeProc is null)
            {
                Console.WriteLine("  (skipped — could not run vswhere)");
                return;
            }
            instances = probeProc.StandardOutput.ReadToEnd();
            probeProc.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (skipped — vswhere failed: {ex.Message})");
            return;
        }

        var instanceLines = instances.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (instanceLines.Length == 0)
        {
            Console.WriteLine("  (skipped — no Visual Studio instance has the 'Visual Studio extension development' workload installed)");
            return;
        }

        var pwsh = ResolvePowerShell();
        if (pwsh is null)
        {
            Console.WriteLine("  (skipped — powershell.exe / pwsh.exe not on PATH)");
            return;
        }

        var psi = new ProcessStartInfo(pwsh)
        {
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(reinstall);
        // Pick the highest-version instance the script finds (matches the
        // default behavior of Reinstall-Vsix.ps1 when no -VsInstanceId is set).

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Console.WriteLine("  (skipped — failed to launch Reinstall-Vsix.ps1)");
                return;
            }
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"  VS extension reinstall exited with code {proc.ExitCode}; the rest of the upgrade completed.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  VS extension reinstall threw: {ex.Message}; the rest of the upgrade completed.");
        }
    }

    static string? ResolvePowerShell()
    {
        // Prefer pwsh (PowerShell 7+) when available, fall back to Windows
        // PowerShell 5.1. Reinstall-Vsix.ps1 is `#requires -Version 5.1`-clean
        // so either works.
        foreach (var name in new[] { "pwsh.exe", "powershell.exe" })
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path is null) continue;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* ignore malformed PATH entries */ }
            }
        }
        return null;
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
