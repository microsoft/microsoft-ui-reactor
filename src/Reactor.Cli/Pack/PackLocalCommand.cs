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

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(Path.Combine("src", "Reactor", "Reactor.csproj"));
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("-v:m");
        psi.ArgumentList.Add($"-c:{configuration}");
        psi.ArgumentList.Add($"-p:Version={version}");
        psi.ArgumentList.Add($"-o:{feed}");
        // pack honors Platform-specific build outputs; pick host arch.
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            _ => null,
        };
        if (arch is not null) psi.ArgumentList.Add($"-p:Platform={arch}");

        Console.WriteLine($"Packing Microsoft.UI.Reactor {version} → {feed}");
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine("pack failed.");
            return proc.ExitCode;
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

        Console.WriteLine();
        Console.WriteLine($"Done. Apps in this repo can now reference:");
        Console.WriteLine($"    #:package Microsoft.UI.Reactor@{version}");
        Console.WriteLine($"or in a .csproj:");
        Console.WriteLine($"    <PackageReference Include=\"Microsoft.UI.Reactor\" Version=\"{version}\" />");
        return 0;
    }

    static string? ParseFlag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static string? FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Reactor"))
                && File.Exists(Path.Combine(d.FullName, "src", "Reactor", "Reactor.csproj")))
            {
                return d.FullName;
            }
        }
        return null;
    }
}
