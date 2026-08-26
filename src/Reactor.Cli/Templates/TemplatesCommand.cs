// `mur templates` — manage the `dotnet new` template pack that provides
// `dotnet new reactor`.
//
// Reactor's app templates ship inside the Windows App SDK template pack
// (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`) rather than being built
// from this checkout. `bootstrap.ps1` §5 and `mur upgrade` both route through
// here so there is a single place that knows how to resolve and install it.
//
// Subcommands:
//   install   Install (or reinstall) the pack. Resolves the newest published
//             version — newest stable, else newest prerelease — because
//             `dotnet new install` has no --prerelease switch and would
//             otherwise fail while the pack is prerelease-only.
//   status    Report whether the pack is registered.
//
// Flags (install):
//   --source <path|url>  Extra NuGet source. Point at a folder of nupkgs to
//                        test an unpublished build of the pack.
//   --version <v>        Pin an explicit version instead of resolving.

namespace Microsoft.UI.Reactor.Cli.Templates;

public static class TemplatesCommand
{
    public static int Run(string[] args)
    {
        var sub = args.FirstOrDefault();

        if (sub is null or "--help" or "-h" or "help")
        {
            ShowHelp();
            return sub is null ? 1 : 0;
        }

        switch (sub)
        {
            case "install":
                return Install(args.Skip(1).ToArray());
            case "status":
                return Status();
            default:
                Console.Error.WriteLine($"mur templates: unknown subcommand '{sub}'.");
                Console.Error.WriteLine();
                ShowHelp();
                return 1;
        }
    }

    static int Install(string[] args)
    {
        var source = ParseFlag(args, "--source");
        var version = ParseFlag(args, "--version");

        Console.WriteLine($"Installing {WinAppSdkTemplates.PackageId} (`dotnet new {WinAppSdkTemplates.BlankShortName}`)");

        // Resolve a local folder to an absolute path: `dotnet new install` runs
        // with its own working directory and won't see a relative one.
        if (!string.IsNullOrWhiteSpace(source) && Directory.Exists(source))
            source = Path.GetFullPath(source!);

        var rc = WinAppSdkTemplates.Install(Directory.GetCurrentDirectory(), source, version);
        if (rc != 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"mur templates install: `dotnet new install` failed (exit {rc}).");
            Console.Error.WriteLine("  To install an unpublished build, pass a folder of nupkgs:");
            Console.Error.WriteLine("    mur templates install --source <folder>");
            Console.Error.WriteLine("  To pin an explicit version:");
            Console.Error.WriteLine("    mur templates install --version <version>");
            return rc;
        }

        Console.WriteLine();
        Console.WriteLine($"Installed. Scaffold an app with:");
        foreach (var name in WinAppSdkTemplates.ShortNames)
            Console.WriteLine($"    dotnet new {name} -n MyApp");
        return 0;
    }

    static int Status()
    {
        var installed = WinAppSdkTemplates.IsInstalled();
        if (installed is null)
        {
            Console.Error.WriteLine("mur templates status: could not enumerate installed `dotnet new` template packages.");
            return 1;
        }

        if (installed.Value)
        {
            Console.WriteLine($"{WinAppSdkTemplates.PackageId} is installed (`dotnet new {WinAppSdkTemplates.BlankShortName}`).");
            return 0;
        }

        Console.WriteLine($"{WinAppSdkTemplates.PackageId} is NOT installed. Run `mur templates install`.");
        return 1;
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: mur templates <install|status> [options]");
        Console.WriteLine();
        Console.WriteLine($"Manages {WinAppSdkTemplates.PackageId}, the Windows App SDK");
        Console.WriteLine($"`dotnet new` pack that provides `dotnet new {WinAppSdkTemplates.BlankShortName}` and friends.");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  install    Install or reinstall the template pack");
        Console.WriteLine("  status     Report whether the pack is registered");
        Console.WriteLine();
        Console.WriteLine("Options (install):");
        Console.WriteLine("  --source <path|url>   Extra NuGet source; use a folder of nupkgs to test an unpublished build");
        Console.WriteLine("  --version <version>   Pin an explicit version instead of resolving the newest published one");
    }

    static string? ParseFlag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        }
        return null;
    }
}
