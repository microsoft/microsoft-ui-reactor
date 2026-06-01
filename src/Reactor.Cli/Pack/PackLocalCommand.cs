// `mur pack-local` — packs the in-source Reactor framework into a local NuGet
// nupkg under <repo>/local-nupkgs/, so apps in this clone (recipes, samples,
// scaffolded projects) can consume it via:
//
//   #:package Microsoft.UI.Reactor@0.0.0-local
//
// The same code path consumers use against a real NuGet — but rebuilt from the
// current source. Includes the analyzers and agentkit/reactor.api.txt
// automatically (already wired in Reactor.csproj).
//
// Run after framework changes whenever you want recipes / scaffolded apps to
// pick them up.

using System.Diagnostics;

namespace Microsoft.UI.Reactor.Cli.Pack;

public static class PackLocalCommand
{
    public const string DefaultLocalVersion = "0.0.0-local";

    public static int Run(string[] args)
    {
        var version = ParseFlag(args, "--version") ?? DefaultLocalVersion;
        var configuration = ParseFlag(args, "--configuration") ?? "Debug";

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("mur pack-local: must be run from a Reactor source checkout (could not locate src/Reactor).");
            return 1;
        }

        var feed = Path.Combine(repoRoot, "local-nupkgs");
        Directory.CreateDirectory(feed);

        // Clean prior nupkgs of this version so package restore picks up the new one
        // even if NuGet has cached the previous build by the same version string.
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.{version}.*nupkg"))
        {
            try { File.Delete(stale); } catch { /* best effort */ }
        }
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{version}.*nupkg"))
        {
            try { File.Delete(stale); } catch { /* best effort */ }
        }

        // pack honors Platform-specific build outputs; pick host arch.
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            _ => null,
        };

        // 1. Framework — Microsoft.UI.Reactor.<version>.nupkg
        Console.WriteLine($"Packing Microsoft.UI.Reactor {version} → {feed}");
        var rc = RunPack(repoRoot, Path.Combine("src", "Reactor", "Reactor.csproj"), configuration, version, feed, arch);
        if (rc != 0)
        {
            Console.Error.WriteLine("pack failed.");
            return rc;
        }

        // 2. Project templates — Microsoft.UI.Reactor.ProjectTemplates.<version>.nupkg.
        // Powers `dotnet new reactorapp -n MyApp` against this clone. Templates pack
        // is AnyCPU (no arch needed); the template's <PackageReference> resolves the
        // matching framework version through this same feed.
        Console.WriteLine($"Packing Microsoft.UI.Reactor.ProjectTemplates {version} → {feed}");
        rc = RunPack(repoRoot, Path.Combine("tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj"), configuration, version, feed, arch: null);
        if (rc != 0)
        {
            Console.Error.WriteLine("templates pack failed.");
            return rc;
        }

        // Bust NuGet's HTTP cache for our local source so the new build is picked up
        // immediately on the next restore.
        try
        {
            var clearProc = Process.Start(new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = repoRoot,
                ArgumentList = { "nuget", "locals", "http-cache", "--clear" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            clearProc?.WaitForExit();
        }
        catch { /* non-fatal */ }

        var templatesNupkg = Path.Combine(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{version}.nupkg");

        Console.WriteLine();
        Console.WriteLine($"Done. Apps in this repo can now reference:");
        Console.WriteLine($"    #:package Microsoft.UI.Reactor@{version}");
        Console.WriteLine($"or in a .csproj:");
        Console.WriteLine($"    <PackageReference Include=\"Microsoft.UI.Reactor\" Version=\"{version}\" />");
        Console.WriteLine();
        Console.WriteLine($"To use `dotnet new reactorapp` against this feed:");
        Console.WriteLine($"    dotnet new install \"{templatesNupkg}\"");
        Console.WriteLine($"    # then, from anywhere inside this clone (so nuget.config applies):");
        Console.WriteLine($"    dotnet new reactorapp -n MyApp");
        Console.WriteLine($"Outside the clone, copy nuget.config to your project parent or add the absolute");
        Console.WriteLine($"path '{feed}' as a NuGet source on your machine.");
        return 0;
    }

    static int RunPack(string repoRoot, string projectRelative, string configuration, string version, string feed, string? arch)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(projectRelative);
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("-v:m");
        psi.ArgumentList.Add($"-c:{configuration}");
        psi.ArgumentList.Add($"-p:Version={version}");
        psi.ArgumentList.Add($"-o:{feed}");
        if (arch is not null) psi.ArgumentList.Add($"-p:Platform={arch}");

        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
    }

    static string? ParseFlag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    // Prefer CWD so a globally-installed `mur` (under ~/.dotnet/tools) still
    // discovers the source checkout the user is sitting in. Fall back to the
    // tool's own location for the legacy `bin/<arch>/mur.exe` install layout.
    static string? FindRepoRoot()
        => RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory())
        ?? RepoRootFinder.FindRepoRoot();
}
