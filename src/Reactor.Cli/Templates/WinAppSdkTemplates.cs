// The Windows App SDK `dotnet new` template pack, which is where Reactor's app
// templates now live.
//
// Reactor used to ship its own `Microsoft.UI.Reactor.ProjectTemplates` pack
// (`dotnet new reactorapp`, unpackaged). The Windows App SDK template pack now
// carries first-class Reactor templates alongside the WinUI 3 XAML ones, so
// that's what `bootstrap.ps1` installs and what `mur doctor` looks for. The
// legacy pack is still built by `mur pack-local` and published from the release
// workflow, but nothing installs it automatically any more.
//
// Two user-visible differences from the legacy `reactorapp` template:
//   • the short name is `reactor` (plus `reactor-mvu` / `reactor-navview` /
//     `reactor-tabview` for the richer shells), and
//   • scaffolded apps are **packaged** (single-project MSIX) rather than
//     unpackaged, so `dotnet run` launches them with package identity.
//
// Why this resolves a version instead of just installing the bare package id:
// `dotnet new install <id>` has no `--prerelease` switch and resolves
// stable-only, so it fails outright while the pack is publishing prereleases.
// We query the NuGet flat-container index ourselves and install an explicit
// `<id>::<version>`, preferring the newest stable and falling back to the newest
// prerelease — so a fresh clone works against whatever is published today.
//
// Why `--force` is used sparingly: `dotnet new install --force` uninstalls the
// existing package *before* downloading the replacement, so a failed install
// leaves the machine with no templates at all. (Observed: `--force` with a bare
// package id that has no stable version uninstalled the working prerelease and
// then failed with "the package does not exist".) We therefore only pass
// `--force` when replacing an install with a version we already know exists.

using System.Diagnostics;
using System.Net.Http;
using Microsoft.UI.Reactor.Cli.Pack;

namespace Microsoft.UI.Reactor.Cli.Templates;

public static class WinAppSdkTemplates
{
    /// <summary>NuGet id of the template pack that ships the Reactor templates.</summary>
    public const string PackageId = "Microsoft.WindowsAppSDK.WinUI.CSharp.Templates";

    /// <summary>`dotnet new` short name of the blank Reactor template.</summary>
    public const string BlankShortName = "reactor";

    /// <summary>Every Reactor short name the pack registers.</summary>
    public static readonly string[] ShortNames =
    [
        "reactor",
        "reactor-mvu",
        "reactor-navview",
        "reactor-tabview",
    ];

    // Lists every published version (including prereleases) as a JSON string array.
    const string FlatContainerIndexUrl =
        "https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk.winui.csharp.templates/index.json";

    /// <summary>
    /// True when the template *package* is registered with the `dotnet new`
    /// engine. Returns null when the installed-package list could not be
    /// enumerated at all (no `dotnet` on PATH, engine error) so callers can
    /// distinguish "definitely missing" from "couldn't tell".
    /// </summary>
    /// <remarks>
    /// This answers "is the pack installed?", NOT "can I run `dotnet new
    /// reactor`?" — the pack shipped versions (e.g. 0.0.6-alpha) that predate
    /// the Reactor templates, so it can be installed and still not provide
    /// them. Use <see cref="AreTemplatesAvailable"/> for the user-facing
    /// question; this one exists to decide whether an install would be
    /// replacing something.
    /// </remarks>
    public static bool? IsPackageInstalled()
    {
        // `dotnet new uninstall` with no arguments lists installed template
        // *packages* by id. `dotnet new list` only shows template short names,
        // which can't tell our legacy `reactorapp` pack apart from the Windows
        // App SDK one when both happen to be installed.
        var output = RunCapture("new", "uninstall");
        if (output is null) return null;
        return output.Contains(PackageId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when `dotnet new reactor` will actually resolve — i.e. the blank
    /// Reactor template short name is registered. Returns null when the
    /// template engine could not be queried at all.
    /// </summary>
    /// <remarks>
    /// Checking the package id alone is a false PASS: `dotnet new list reactor`
    /// reports "No templates found" against an installed-but-too-old pack, so a
    /// package-only probe tells a developer they're ready to scaffold when the
    /// very next command fails. Ask the engine the question the user cares about.
    /// </remarks>
    public static bool? AreTemplatesAvailable()
    {
        // `dotnet new list <name>` exits non-zero (103) and prints
        // "No templates found matching" when nothing matches. Match on the
        // short name in the output rather than the exit code alone so an
        // unrelated non-zero exit doesn't read as a definitive "missing".
        var output = RunCapture("new", "list", BlankShortName);
        if (output is null) return null;
        return InterpretTemplateListOutput(output);
    }

    /// <summary>
    /// Interprets `dotnet new list reactor` output. Split out (and internal) so
    /// the rule is unit-testable without shelling out to the template engine.
    /// </summary>
    internal static bool InterpretTemplateListOutput(string output)
    {
        // The "not found" message also contains the search term ("No templates
        // found matching: 'reactor'." plus a "dotnet new search reactor" hint),
        // so a naive short-name substring match reports the template as present
        // precisely when it is absent. Check the negative marker first.
        if (output.Contains("No templates found", StringComparison.OrdinalIgnoreCase))
            return false;
        return output.Contains(BlankShortName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The installed version of the template pack, or null when it isn't
    /// installed (or the listing couldn't be read).
    /// </summary>
    public static string? GetInstalledVersion()
    {
        var output = RunCapture("new", "uninstall");
        if (output is null) return null;

        // The listing indents each package id, then its metadata:
        //     Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
        //        Version: 0.0.6-alpha
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Trim().Equals(PackageId, StringComparison.OrdinalIgnoreCase))
                continue;

            for (var j = i + 1; j < lines.Length && j <= i + 4; j++)
            {
                var trimmed = lines[j].Trim();
                if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["Version:".Length..].Trim();
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// Installs (or updates) the template pack.
    /// </summary>
    /// <param name="workingDirectory">Working directory for the `dotnet` process.</param>
    /// <param name="source">Extra NuGet source — a local folder holding the nupkg, or a feed URL. This is how an unpublished build gets tested.</param>
    /// <param name="version">Explicit version to pin. When omitted the newest published version is resolved.</param>
    public static int Install(string workingDirectory, string? source = null, string? version = null)
    {
        var installed = GetInstalledVersion();
        var target = string.IsNullOrWhiteSpace(version) ? ResolveLatestVersion(source) : version!.Trim();

        // `dotnet new install --force` uninstalls the existing package *before*
        // downloading the replacement, so a failed install leaves the machine
        // with no templates at all. Never take that path unless we have a
        // concrete version we know exists (resolved from the live NuGet index or
        // from a nupkg filename on disk). Without one, keep what's installed.
        if (target is null)
        {
            if (installed is not null)
            {
                Console.Error.WriteLine(
                    $"  warning: could not resolve a published version of {PackageId}; " +
                    $"keeping the installed {installed}. Re-run with network access, or pass an explicit version.");
                return 0;
            }

            // Nothing installed and nothing resolved — try a plain install (no
            // --force, so there is nothing to lose) and let NuGet report why.
            Console.WriteLine($"  dotnet new install {PackageId}");
            return Run(workingDirectory, "new", "install", PackageId);
        }

        if (installed is not null &&
            string.Equals(installed, target, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(source))
        {
            Console.WriteLine($"  Already installed: {PackageId} {installed}");
            return 0;
        }

        Console.WriteLine(installed is null
            ? $"  Installing {PackageId} {target}"
            : $"  Updating {PackageId} {installed} → {target}");

        // `<id>::<version>` is `dotnet new install`'s explicit-version syntax and
        // the only way to reach a prerelease — a bare id resolves stable-only.
        var args = new List<string> { "new", "install", $"{PackageId}::{target}" };

        // --force is required to replace an existing install; skip it otherwise
        // so a first-time install can never uninstall anything.
        if (installed is not null)
            args.Add("--force");

        if (!string.IsNullOrWhiteSpace(source))
        {
            args.Add("--add-source");
            args.Add(source!);
        }

        Console.WriteLine($"  dotnet {string.Join(' ', args)}");
        var rc = Run(workingDirectory, args.ToArray());
        if (rc != 0 && installed is not null)
        {
            Console.Error.WriteLine(
                $"  warning: the update failed and `dotnet new install --force` removes the old package first, " +
                $"so {PackageId} may no longer be installed. Restore it with: " +
                $"dotnet new install {PackageId}::{installed}");
        }
        return rc;
    }

    /// <summary>
    /// Newest published version of the pack — the newest stable when one exists,
    /// otherwise the newest prerelease. Returns null when nothing could be
    /// resolved (offline, unreachable feed, empty folder).
    /// </summary>
    public static string? ResolveLatestVersion(string? source = null)
    {
        // A local folder source is the unpublished-build test path: read the
        // versions straight off the nupkg filenames rather than hitting NuGet.
        if (!string.IsNullOrWhiteSpace(source) && Directory.Exists(source))
            return SelectPreferStable(EnumerateLocalVersions(source!));

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = http.GetStringAsync(FlatContainerIndexUrl).GetAwaiter().GetResult();
            return SelectPreferStable(PackLocalCommand.ParseFlatContainerVersions(json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"  warning: could not query NuGet for {PackageId} versions " +
                $"({ex.GetType().Name}: {ex.Message}); falling back to the default resolution.");
            return null;
        }
    }

    /// <summary>
    /// Highest stable version, or the highest prerelease when no stable exists.
    /// Split out (and internal) so the preference rule is unit-testable without
    /// a network round-trip.
    /// </summary>
    internal static string? SelectPreferStable(IEnumerable<string> versions)
    {
        var all = versions as IReadOnlyList<string> ?? versions.ToList();
        // A '-' after the core triple marks a SemVer prerelease (1.2.3-alpha).
        var stable = all.Where(v => !string.IsNullOrWhiteSpace(v) && !v.Contains('-')).ToList();
        return PackLocalCommand.SelectLatestVersion(stable.Count > 0 ? stable : all);
    }

    // "<PackageId>.<version>.nupkg" → "<version>", case-insensitively.
    internal static IReadOnlyList<string> EnumerateLocalVersions(string folder)
    {
        var prefix = PackageId + ".";
        var versions = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, $"{PackageId}.*.nupkg"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    versions.Add(name[prefix.Length..]);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  warning: could not enumerate '{folder}' ({ex.GetType().Name}: {ex.Message}).");
        }
        return versions;
    }

    static int Run(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return 1;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  failed to run `dotnet {string.Join(' ', arguments)}`: {ex.Message}");
            return 1;
        }
    }

    static string? RunCapture(params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            // `dotnet new uninstall` exits non-zero when nothing is installed
            // while still printing a usable listing, so don't gate on ExitCode.
            return stdout + stderr;
        }
        catch
        {
            return null;
        }
    }
}
