// `mur templates` — report whether the `dotnet new` template pack that provides
// `dotnet new reactor` is usable.
//
// Reactor's app templates ship inside the Windows App SDK template pack
// (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`) rather than being built
// from this checkout.
//
// This command only *reports*. Installing is the Windows App SDK CLI's job —
// `winapp new` installs the pack on demand and scaffolds in one step, and
// `bootstrap.ps1` §5 drives it through `winapp new --list`. `mur templates
// install` used to exist because `dotnet new install` has no --prerelease switch
// and resolves stable-only, which fails while the pack is prerelease-only;
// `winapp` handles that, so the installer is gone rather than duplicated.
//
// Subcommands:
//   status    Report whether `dotnet new reactor` resolves. Exit codes are
//             load-bearing — see StatusExit.

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
            case "status":
                return Status();
            case "install":
                // Named explicitly rather than falling into "unknown subcommand":
                // this command existed, `bootstrap.ps1` and the docs used to call
                // it, and silently rejecting it as a typo would send someone
                // looking for a misspelling instead of the replacement.
                Console.Error.WriteLine(
                    "mur templates install has been removed. Scaffold with the Windows App SDK CLI instead:");
                Console.Error.WriteLine($"    winapp new -t {WinAppSdkTemplates.BlankShortName} -n MyApp");
                Console.Error.WriteLine(
                    "  It installs the template pack on demand. To install the pack without scaffolding:");
                Console.Error.WriteLine("    winapp new --list");
                return 1;
            default:
                Console.Error.WriteLine($"mur templates: unknown subcommand '{sub}'.");
                Console.Error.WriteLine();
                ShowHelp();
                return 1;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: mur templates status");
        Console.WriteLine();
        Console.WriteLine($"Reports whether `dotnet new {WinAppSdkTemplates.BlankShortName}` resolves, i.e. whether");
        Console.WriteLine($"{WinAppSdkTemplates.PackageId} is installed and carries the Reactor templates.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine($"  {StatusExit.Available}  `dotnet new {WinAppSdkTemplates.BlankShortName}` resolves");
        Console.WriteLine($"  {StatusExit.ProbeFailed}  the `dotnet new` template engine could not be enumerated");
        Console.WriteLine($"  {StatusExit.InstalledButUnusable}  the pack is installed but does not carry the Reactor templates");
        Console.WriteLine($"  {StatusExit.NotInstalled}  the pack is not installed");
        Console.WriteLine();
        Console.WriteLine("To install the pack, use the Windows App SDK CLI:");
        Console.WriteLine($"    winapp new -t {WinAppSdkTemplates.BlankShortName} -n MyApp   # installs on demand, then scaffolds");
        Console.WriteLine("    winapp new --list                  # installs on demand, scaffolds nothing");
    }

    /// <summary>
    /// `mur templates status` exit codes. Three distinct situations that all mean
    /// "cannot scaffold" but call for different remediation, so callers (bootstrap
    /// especially) must not collapse them into one message.
    /// </summary>
    internal static class StatusExit
    {
        /// <summary>`dotnet new reactor` resolves.</summary>
        public const int Available = 0;
        /// <summary>The template engine could not be enumerated at all.</summary>
        public const int ProbeFailed = 1;
        /// <summary>The pack is installed but does not carry the Reactor templates.</summary>
        public const int InstalledButUnusable = 2;
        /// <summary>The pack is not installed.</summary>
        public const int NotInstalled = 3;
    }

    /// <summary>
    /// Maps the two probes to a <see cref="StatusExit"/> code. Pure, so the
    /// distinction bootstrap.ps1 branches on is testable without a machine.
    /// </summary>
    /// <param name="available">null when the template engine could not be enumerated.</param>
    /// <param name="packageInstalled">null when the installed-package list could not be read.</param>
    internal static int StatusExitCode(bool? available, bool? packageInstalled)
    {
        if (available is null) return StatusExit.ProbeFailed;
        if (available.Value) return StatusExit.Available;
        // Absent short name: is the pack there at all? "Installed but too old" and
        // "never installed" need different advice, and an unreadable package list
        // is a probe failure rather than either.
        if (packageInstalled is null) return StatusExit.ProbeFailed;
        return packageInstalled.Value ? StatusExit.InstalledButUnusable : StatusExit.NotInstalled;
    }

    static int Status()
    {
        // Report the question that matters — "can I scaffold?" — not merely
        // whether the package id appears in the installed list.
        var available = WinAppSdkTemplates.AreTemplatesAvailable();
        var packageInstalled = available == false ? WinAppSdkTemplates.IsPackageInstalled() : null;
        var exitCode = StatusExitCode(available, packageInstalled);
        var version = exitCode == StatusExit.ProbeFailed ? null : WinAppSdkTemplates.GetInstalledVersion();

        switch (exitCode)
        {
            case StatusExit.Available:
                Console.WriteLine(version is null
                    ? $"`dotnet new {WinAppSdkTemplates.BlankShortName}` is available."
                    : $"`dotnet new {WinAppSdkTemplates.BlankShortName}` is available ({WinAppSdkTemplates.PackageId} {version}).");
                break;

            case StatusExit.InstalledButUnusable:
                Console.WriteLine(
                    $"{WinAppSdkTemplates.PackageId} {version ?? "(unknown)"} is installed, but it does not provide " +
                    $"`dotnet new {WinAppSdkTemplates.BlankShortName}`. Update it with `winapp new --list`.");
                break;

            case StatusExit.NotInstalled:
                Console.WriteLine(
                    $"{WinAppSdkTemplates.PackageId} is NOT installed. Install it with `winapp new --list`, " +
                    $"or scaffold directly with `winapp new -t {WinAppSdkTemplates.BlankShortName} -n MyApp`.");
                break;

            default:
                Console.Error.WriteLine("mur templates status: could not enumerate `dotnet new` templates.");
                break;
        }

        return exitCode;
    }
}
