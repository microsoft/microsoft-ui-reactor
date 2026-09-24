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
//   --source <folder>    Folder of .nupkg files, to test an unpublished build of
//                        the pack. Must be a local folder, not a feed URL:
//                        `dotnet new install` cannot be restricted to one feed
//                        (--add-source only adds one), so a URL source can be
//                        silently satisfied from nuget.org instead.
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
        // Help must never mutate the machine: `mur templates install --help`
        // previously fell straight through to a real install.
        if (args.Any(a => a is "--help" or "-h"))
        {
            ShowInstallHelp();
            return 0;
        }

        // Reject anything we don't understand rather than silently ignoring it —
        // a typo like `--sorce ./pkgs` would otherwise install from the wrong place.
        if (!TryParseInstallArgs(args, out var source, out var version, out var feed, out var error))
        {
            Console.Error.WriteLine($"mur templates install: {error}");
            Console.Error.WriteLine();
            ShowInstallHelp();
            return 1;
        }

        Console.WriteLine($"Installing {WinAppSdkTemplates.PackageId} (`dotnet new {WinAppSdkTemplates.BlankShortName}`)");

        // Resolve a local folder to an absolute path: `dotnet new install` runs
        // with its own working directory and won't see a relative one.
        if (!string.IsNullOrWhiteSpace(source) && Directory.Exists(source))
            source = Path.GetFullPath(source!);

        var outcome = WinAppSdkTemplates.Install(Directory.GetCurrentDirectory(), source, version, feed);
        if (outcome == WinAppSdkTemplates.InstallOutcome.Failed)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"mur templates install: `dotnet new install` failed.");
            Console.Error.WriteLine("  To install an unpublished build, pass a folder of nupkgs:");
            Console.Error.WriteLine("    mur templates install --source <folder>");
            Console.Error.WriteLine("  To pin an explicit version:");
            Console.Error.WriteLine("    mur templates install --version <version>");
            Console.Error.WriteLine("  Note: `dotnet new install` does not use the NuGet credential provider, so an");
            Console.Error.WriteLine("  authenticated feed reports \"the package does not exist\". Restore the package");
            Console.Error.WriteLine("  first, then pass the cached .nupkg path to --source.");
            return 1;
        }

        // Don't claim an install happened when the existing pack was simply kept
        // or was already current — the user needs to know whether anything moved.
        Console.WriteLine();
        Console.WriteLine(DescribeOutcome(outcome));

        // "Installed" is not "usable". KeptExisting in particular means version
        // resolution failed and an older pack was left alone — and 0.0.6-alpha
        // shipped without the Reactor templates, so printing scaffold commands
        // here would hand the user four lines that immediately fail. Print them
        // only once the short name actually resolves.
        var available = WinAppSdkTemplates.AreTemplatesAvailable();
        if (available == true)
        {
            Console.WriteLine("Scaffold an app with:");
            foreach (var name in WinAppSdkTemplates.ShortNames)
                Console.WriteLine($"    dotnet new {name} -n MyApp");
            return ExitCodeForAvailability(available);
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(available is null
            ? $"mur templates install: could not enumerate `dotnet new` templates, so `dotnet new " +
              $"{WinAppSdkTemplates.BlankShortName}` is unverified. Check with `mur templates status`."
            : $"mur templates install: {WinAppSdkTemplates.PackageId} is installed but does not provide " +
              $"`dotnet new {WinAppSdkTemplates.BlankShortName}` — that version predates the Reactor " +
              $"templates. Pin a newer one with `mur templates install --version <version>`.");
        return TemplatesUnavailableExit;
    }

    /// <summary>
    /// Exit code for "the install itself worked, but `dotnet new reactor` still
    /// doesn't resolve" — distinct from 1, which means the install failed.
    /// </summary>
    /// <remarks>
    /// bootstrap.ps1 needs to tell these apart. A genuine install failure is fatal
    /// there, but an old-but-installed pack has its own warning path and next-step
    /// guidance; collapsing both onto 1 makes that path unreachable.
    /// </remarks>
    internal const int TemplatesUnavailableExit = 2;

    /// <summary>
    /// Exit code for an install that ran, given the post-install availability probe
    /// (<c>null</c> = could not enumerate). Pure, so the contract bootstrap.ps1
    /// depends on is testable without touching the machine.
    /// </summary>
    internal static int ExitCodeForAvailability(bool? available) =>
        available == true ? 0 : TemplatesUnavailableExit;

    static int Status()
    {
        // Report the question that matters — "can I scaffold?" — not merely
        // whether the package id appears in the installed list.
        var available = WinAppSdkTemplates.AreTemplatesAvailable();
        if (available is null)
        {
            Console.Error.WriteLine("mur templates status: could not enumerate `dotnet new` templates.");
            return 1;
        }

        var version = WinAppSdkTemplates.GetInstalledVersion();
        if (available.Value)
        {
            Console.WriteLine(version is null
                ? $"`dotnet new {WinAppSdkTemplates.BlankShortName}` is available."
                : $"`dotnet new {WinAppSdkTemplates.BlankShortName}` is available ({WinAppSdkTemplates.PackageId} {version}).");
            return 0;
        }

        if (WinAppSdkTemplates.IsPackageInstalled() == true)
        {
            Console.WriteLine(
                $"{WinAppSdkTemplates.PackageId} {version ?? "(unknown)"} is installed, but it does not provide " +
                $"`dotnet new {WinAppSdkTemplates.BlankShortName}`. Update it with `mur templates install`.");
            return 1;
        }

        Console.WriteLine($"{WinAppSdkTemplates.PackageId} is NOT installed. Run `mur templates install`.");
        return 1;
    }

    /// <summary>
    /// Human-readable summary of what an install actually did. Split out (and
    /// internal) so the mapping is testable: the bug this guards is reporting
    /// "Installed." when the command deliberately kept an existing pack.
    /// </summary>
    internal static string DescribeOutcome(WinAppSdkTemplates.InstallOutcome outcome) => outcome switch
    {
        WinAppSdkTemplates.InstallOutcome.KeptExisting =>
            "Kept the existing install (could not resolve a published version).",
        WinAppSdkTemplates.InstallOutcome.AlreadyCurrent => "Already up to date.",
        WinAppSdkTemplates.InstallOutcome.Updated => "Updated.",
        WinAppSdkTemplates.InstallOutcome.Installed => "Installed.",
        _ => "Install failed.",
    };

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
        Console.WriteLine("Run `mur templates install --help` for install options.");
    }

    static void ShowInstallHelp()
    {
        Console.WriteLine("Usage: mur templates install [--source <folder>] [--version <version>] [--feed <url>]");
        Console.WriteLine();
        Console.WriteLine($"Installs {WinAppSdkTemplates.PackageId}. With no options it resolves the");
        Console.WriteLine("newest published version (newest stable, else newest prerelease).");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source <folder>     Folder of .nupkg files, to install an unpublished build.");
        Console.WriteLine("                        Must be a local folder — feed URLs are rejected, because");
        Console.WriteLine("                        `dotnet new install` cannot be restricted to one feed and");
        Console.WriteLine("                        would silently accept the package from another. Restore");
        Console.WriteLine("                        the package first, then point at the cache folder.");
        Console.WriteLine("  --version <version>   Pin an explicit version instead of resolving.");
        Console.WriteLine("  --feed <url>          NuGet v3 service index to resolve the version from, for");
        Console.WriteLine("                        machines that reach a mirror but not nuget.org. Used only");
        Console.WriteLine("                        for version lookup; falls back to nuget.org.");
        Console.WriteLine("  --help, -h            Show this help.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0  installed (or already current) and `dotnet new reactor` resolves");
        Console.WriteLine("  1  the install failed, or the arguments were rejected");
        Console.WriteLine("  2  the pack is installed but `dotnet new reactor` does not resolve");
    }

    /// <summary>
    /// Strict argv parsing for `install`. Rejects unknown flags, bare positional
    /// arguments, and flags with a missing value, so a typo cannot silently change
    /// what gets installed.
    /// </summary>
    static bool TryParseInstallArgs(string[] args, out string? source, out string? version, out string? feed, out string? error)
    {
        source = null;
        version = null;
        feed = null;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--source":
                case "--version":
                case "--feed":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"'{arg}' requires a value.";
                        return false;
                    }
                    if (arg == "--source") source = args[++i];
                    else if (arg == "--feed") feed = args[++i];
                    else version = args[++i];
                    break;
                default:
                    error = arg.StartsWith("-", StringComparison.Ordinal)
                        ? $"unknown option '{arg}'."
                        : $"unexpected argument '{arg}'.";
                    return false;
            }
        }
        return true;
    }
}
