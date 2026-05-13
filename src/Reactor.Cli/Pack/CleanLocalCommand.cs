// `mur clean-local` — removes local NuGet packages produced by `mur pack-local`,
// clears the matching entries from the NuGet global packages cache, and uninstalls
// any `dotnet new` project templates that were installed from the local feed.
//
// This is the inverse of `mur pack-local` and addresses the "need uninstall"
// half of https://github.com/microsoft/microsoft-ui-reactor/issues/238.

using System.Diagnostics;

namespace Microsoft.UI.Reactor.Cli.Pack;

public static class CleanLocalCommand
{
    // Package IDs that `pack-local` produces — lowercase to match the NuGet
    // global-packages folder convention.
    internal static readonly string[] PackageIds =
    [
        "microsoft.ui.reactor",
        "microsoft.ui.reactor.projecttemplates",
    ];

    // Template package ID used by `dotnet new install`.
    internal const string TemplatePackageId = "Microsoft.UI.Reactor.ProjectTemplates";

    public static int Run(string[] args)
    {
        var repoRoot = RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory())
                    ?? RepoRootFinder.FindRepoRoot();

        if (repoRoot is null)
        {
            Console.Error.WriteLine("mur clean-local: must be run from a Reactor source checkout (could not locate src/Reactor).");
            return 1;
        }

        var feed = Path.Combine(repoRoot, "local-nupkgs");
        var removed = 0;

        // 1. Delete .nupkg / .snupkg files from the local feed.
        if (Directory.Exists(feed))
        {
            foreach (var file in Directory.EnumerateFiles(feed, "*.nupkg")
                        .Concat(Directory.EnumerateFiles(feed, "*.snupkg")))
            {
                try
                {
                    File.Delete(file);
                    Console.WriteLine($"  Deleted {Path.GetFileName(file)}");
                    removed++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Could not delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        if (removed == 0)
            Console.WriteLine("  No local packages found — nothing to remove.");

        // 2. Remove cached copies from the NuGet global-packages folder so that
        //    subsequent restores don't silently resolve a stale local version.
        var globalPackages = GetGlobalPackagesPath();
        if (globalPackages is not null)
        {
            foreach (var id in PackageIds)
            {
                var pkgDir = Path.Combine(globalPackages, id);
                if (!Directory.Exists(pkgDir)) continue;

                // Only delete versions that look like local builds.
                foreach (var versionDir in Directory.EnumerateDirectories(pkgDir))
                {
                    var versionName = Path.GetFileName(versionDir);
                    if (IsLocalVersion(versionName))
                    {
                        try
                        {
                            Directory.Delete(versionDir, recursive: true);
                            Console.WriteLine($"  Removed cached {id}/{versionName}");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  Could not remove cached {id}/{versionName}: {ex.Message}");
                        }
                    }
                }
            }
        }

        // 3. Clear the NuGet HTTP cache so stale metadata doesn't linger.
        RunDotnet(repoRoot, "nuget", "locals", "http-cache", "--clear");

        // 4. Uninstall project templates (non-fatal).
        UninstallTemplates(repoRoot);

        Console.WriteLine();
        Console.WriteLine("Done.");
        return 0;
    }

    internal static string? GetGlobalPackagesPath()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "nuget", "locals", "global-packages", "--list" },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // Output is: "global-packages: C:\Users\...\.nuget\packages\"
            var idx = output.IndexOf(':');
            return idx >= 0 ? output[(idx + 1)..].Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsLocalVersion(string version)
    {
        // Match PackLocalCommand.DefaultLocalVersion ("0.0.0-local") and any
        // user-supplied version containing "local" (e.g. "1.0.0-local.42").
        return version.Contains("local", StringComparison.OrdinalIgnoreCase);
    }

    static void UninstallTemplates(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "new", "uninstall", TemplatePackageId },
            };
            using var proc = Process.Start(psi);
            if (proc is null) return;
            proc.WaitForExit();
            if (proc.ExitCode == 0)
                Console.WriteLine($"  Uninstalled dotnet new template: {TemplatePackageId}");
            // Exit code != 0 means the template wasn't installed — that's fine.
        }
        catch { /* non-fatal */ }
    }

    static void RunDotnet(string workingDirectory, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in arguments) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch { /* non-fatal */ }
    }
}
