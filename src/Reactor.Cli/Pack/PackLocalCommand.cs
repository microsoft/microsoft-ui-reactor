// `mur pack-local` — packs the in-source Reactor framework into a local NuGet
// nupkg under <repo>/local-nupkgs/, so apps in this clone (recipes, samples,
// scaffolded projects) can consume it via:
//
//   #:package Microsoft.UI.Reactor@0.0.0-local
//   #:package Microsoft.UI.Reactor.Advanced@0.0.0-local
//
// The same code path consumers use against a real NuGet — but rebuilt from the
// current source. Includes the analyzers and agentkit/reactor.api.txt
// automatically (already wired in Reactor.csproj).
//
// Run after framework changes whenever you want recipes / scaffolded apps to
// pick them up.
//
// Flags:
//   --version <v>            package version stamped on the produced nupkgs
//                            (default 0.0.0-local).
//   --configuration <c>      build configuration (default Debug).
//   --framework-version <v>  version the packed `reactorapp` template tells
//                            generated apps to reference. Accepts an explicit
//                            version, or `latest` to resolve the newest published
//                            Microsoft.UI.Reactor from NuGet. Omitted → the
//                            template's csproj default. `bootstrap.ps1` passes
//                            `latest`, so a fresh clone scaffolds against the
//                            current release without a manual version bump.

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Pack;

public static class PackLocalCommand
{
    public const string DefaultLocalVersion = "0.0.0-local";

    public static int Run(string[] args)
    {
        var version = ParseFlag(args, "--version") ?? DefaultLocalVersion;
        var configuration = ParseFlag(args, "--configuration") ?? "Debug";
        var frameworkVersionArg = ParseFlag(args, "--framework-version");

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
        // Cleanup failures are non-fatal but surfaced as warnings so they aren't silent
        // when `mur pack-local` reports success but the consumer keeps resolving stale.
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.{version}.*nupkg"))
            DeleteStaleNupkg(stale);
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{version}.*nupkg"))
            DeleteStaleNupkg(stale);
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.Advanced.{version}.*nupkg"))
            DeleteStaleNupkg(stale);
        foreach (var stale in Directory.EnumerateFiles(feed, $"Microsoft.UI.Reactor.Devtools.{version}.*nupkg"))
            DeleteStaleNupkg(stale);

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

        // 2. Advanced components — Microsoft.UI.Reactor.Advanced.<version>.nupkg
        Console.WriteLine($"Packing Microsoft.UI.Reactor.Advanced {version} → {feed}");
        rc = RunPack(repoRoot, Path.Join("src", "Reactor.Advanced", "Reactor.Advanced.csproj"), configuration, version, feed, arch);
        if (rc != 0)
        {
            Console.Error.WriteLine("advanced pack failed.");
            return rc;
        }

        // 3. Devtools host — Microsoft.UI.Reactor.Devtools.<version>.nupkg.
        // Referenced by the scaffolded `dotnet new reactorapp` csproj in a Debug-only
        // ItemGroup so the devtools menu (and the Reactor VS embedded-preview extension)
        // works against this feed without falling through to NuGet.org.
        Console.WriteLine($"Packing Microsoft.UI.Reactor.Devtools {version} → {feed}");
        rc = RunPack(repoRoot, Path.Combine("src", "Reactor.Devtools", "Reactor.Devtools.csproj"), configuration, version, feed, arch);
        if (rc != 0)
        {
            Console.Error.WriteLine("devtools pack failed.");
            return rc;
        }

        // 4. Project templates — Microsoft.UI.Reactor.ProjectTemplates.<version>.nupkg.
        // Powers `dotnet new reactorapp -n MyApp` against this clone. Templates pack
        // is AnyCPU (no arch needed). The framework version the generated app
        // references is baked into template.json at pack time from
        // MicrosoftUIReactorVersion:
        //   • no --framework-version  → the csproj default (a real *public* package),
        //     so a scaffold restores from NuGet.org and builds standalone.
        //   • --framework-version <v> → stamp <v>. `bootstrap.ps1` passes `latest`,
        //     which resolves the newest published package so the local scaffold
        //     default tracks releases automatically instead of drifting behind them.
        // Either way it's a public NuGet version; pass `--MSUIReactorVersion
        // 0.0.0-local` to `dotnet new reactorapp` to instead consume the
        // source-built framework packed into this feed.
        var templateFrameworkVersion = ResolveTemplateFrameworkVersion(frameworkVersionArg);
        Console.WriteLine($"Packing Microsoft.UI.Reactor.ProjectTemplates {version} → {feed}");
        rc = RunPack(repoRoot, Path.Combine("tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj"), configuration, version, feed, arch: null, frameworkVersion: templateFrameworkVersion);
        if (rc != 0)
        {
            Console.Error.WriteLine("templates pack failed.");
            return rc;
        }

        // Bust NuGet's HTTP cache for our local source so the new build is picked up
        // immediately on the next restore. Failure here is non-fatal but surfaced as
        // a warning — stale caches are a common "why doesn't my change take effect"
        // pitfall and silent failure makes the diagnostic worse.
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
            if (clearProc is not null && clearProc.ExitCode != 0)
                Console.Error.WriteLine($"warning: 'dotnet nuget locals http-cache --clear' exited with code {clearProc.ExitCode}; consumers may continue to resolve cached versions of {version}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not run 'dotnet nuget locals http-cache --clear' ({ex.GetType().Name}: {ex.Message}); consumers may continue to resolve cached versions of {version}.");
        }

        // Bust the per-package extracted cache directories under
        // ~/.nuget/packages/<id>/<version>/. NuGet caches extracted packages by
        // id+version and `dotnet restore` will reuse an existing extracted copy
        // even after the source nupkg's bytes change — same version string = no
        // refresh. For local-iteration `0.0.0-local` workflows this is the
        // single most common "I packed a fix but the consumer still sees the
        // old behavior" footgun: a stale Reactor.dll missing newly-added APIs
        // produces a MissingMethodException at runtime instead of a build error.
        // Clearing the extracted directories here forces the next restore to
        // re-extract from the freshly-packed feed.
        foreach (var globalPackages in ResolveNuGetGlobalPackagesPaths(repoRoot))
        {
            if (!Directory.Exists(globalPackages))
            {
                continue;
            }

            foreach (var packageId in new[]
                {
                    "microsoft.ui.reactor",
                    "microsoft.ui.reactor.advanced",
                    "microsoft.ui.reactor.devtools",
                    "microsoft.ui.reactor.projecttemplates",
                })
            {
                var cached = Path.Combine(globalPackages, packageId, version);
                if (Directory.Exists(cached))
                {
                    try
                    {
                        Directory.Delete(cached, recursive: true);
                        Console.WriteLine($"  Cleared stale extracted cache: {cached}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"warning: could not clear extracted cache at {cached} ({ex.GetType().Name}: {ex.Message}); consumers may resolve a stale {packageId} {version} and hit MissingMethodException at runtime. Delete the directory manually before the next restore.");
                    }
                }
            }
        }

        var templatesNupkg = Path.Combine(feed, $"Microsoft.UI.Reactor.ProjectTemplates.{version}.nupkg");

        Console.WriteLine();
        Console.WriteLine($"Done. Apps in this repo can now reference:");
        Console.WriteLine($"    #:package Microsoft.UI.Reactor@{version}");
        Console.WriteLine($"    #:package Microsoft.UI.Reactor.Advanced@{version}");
        Console.WriteLine($"or in a .csproj:");
        Console.WriteLine($"    <PackageReference Include=\"Microsoft.UI.Reactor\" Version=\"{version}\" />");
        Console.WriteLine($"    <PackageReference Include=\"Microsoft.UI.Reactor.Advanced\" Version=\"{version}\" />");
        Console.WriteLine();
        Console.WriteLine($"To use `dotnet new reactorapp` against this feed:");
        Console.WriteLine($"    dotnet new install \"{templatesNupkg}\"");
        Console.WriteLine($"    # then, from anywhere inside this clone (so nuget.config applies):");
        Console.WriteLine($"    dotnet new reactorapp -n MyApp");
        Console.WriteLine($"Outside the clone, copy nuget.config to your project parent or add the absolute");
        Console.WriteLine($"path '{feed}' as a NuGet source on your machine.");
        return 0;
    }

    static IReadOnlyList<string> ResolveNuGetGlobalPackagesPaths(string repoRoot)
    {
        var paths = new List<string>();

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(fullPath);
            }
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "nuget", "locals", "global-packages", "--list" },
            });
            if (proc is not null)
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        const string prefix = "global-packages:";
                        if (line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            AddPath(line[(line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) + prefix.Length)..]);
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"warning: 'dotnet nuget locals global-packages --list' exited with code {proc.ExitCode}: {stderr.Trim()}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not resolve NuGet global packages path ({ex.GetType().Name}: {ex.Message}); falling back to the default user profile cache.");
        }

        AddPath(Environment.GetEnvironmentVariable("NUGET_PACKAGES"));
        AddPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));
        return paths;
    }

    static int RunPack(string repoRoot, string projectRelative, string configuration, string version, string feed, string? arch, string? frameworkVersion = null)
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
        // Stamp the framework version the generated template references (baked into
        // template.json by the templates csproj's BeforePack target). Supplied only
        // for the templates pack; null leaves the csproj default in place.
        if (frameworkVersion is not null) psi.ArgumentList.Add($"-p:MicrosoftUIReactorVersion={frameworkVersion}");
        psi.ArgumentList.Add($"-o:{feed}");
        if (arch is not null) psi.ArgumentList.Add($"-p:Platform={arch}");

        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
    }

    static void DeleteStaleNupkg(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"warning: could not delete stale '{path}' ({ex.GetType().Name}: {ex.Message}); pack may write next to a stale package — consumers may resolve the wrong one.");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"warning: could not delete stale '{path}' ({ex.GetType().Name}: {ex.Message}); pack may write next to a stale package — consumers may resolve the wrong one.");
        }
        catch (System.Security.SecurityException ex)
        {
            Console.Error.WriteLine($"warning: could not delete stale '{path}' ({ex.GetType().Name}: {ex.Message}); pack may write next to a stale package — consumers may resolve the wrong one.");
        }
    }

    static string? ParseFlag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    // Resolve the framework version to bake into the packed template's
    // <PackageReference>, from the --framework-version flag value:
    //   • null/empty  → null (leave the csproj default in place; today's behavior).
    //   • "latest"    → newest published Microsoft.UI.Reactor on NuGet.org, or null
    //                   (warn + fall back to the csproj default) when it can't be
    //                   resolved. Best-effort so `bootstrap.ps1` on a flaky network
    //                   still produces a usable feed.
    //   • otherwise   → the literal version supplied.
    internal static string? ResolveTemplateFrameworkVersion(string? frameworkVersionArg)
    {
        if (string.IsNullOrWhiteSpace(frameworkVersionArg))
            return null;

        var trimmed = frameworkVersionArg.Trim();
        if (!trimmed.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var latest = TryResolveLatestPublishedFrameworkVersion();
        if (latest is null)
        {
            Console.Error.WriteLine(
                "warning: could not resolve the latest published Microsoft.UI.Reactor version from NuGet; " +
                "scaffolded apps will reference the template's built-in default version. Re-run with network " +
                "access, or pass an explicit --framework-version <version>.");
            return null;
        }

        Console.WriteLine($"Latest published Microsoft.UI.Reactor: {latest} (stamped into the template)");
        return latest;
    }

    // NuGet flat-container index for the published framework package. Lists every
    // published version (including prereleases) as a JSON string array.
    const string FrameworkFlatContainerIndexUrl =
        "https://api.nuget.org/v3-flatcontainer/microsoft.ui.reactor/index.json";

    static string? TryResolveLatestPublishedFrameworkVersion()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = http.GetStringAsync(FrameworkFlatContainerIndexUrl).GetAwaiter().GetResult();
            return SelectLatestVersion(ParseFlatContainerVersions(json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"warning: querying NuGet for the latest Microsoft.UI.Reactor version failed " +
                $"({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }

    // Extract the "versions" string array from a NuGet flat-container index.json.
    // Returns an empty list for malformed / oddly-shaped payloads (best-effort).
    internal static IReadOnlyList<string> ParseFlatContainerVersions(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("versions", out var versions) &&
                versions.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var element in versions.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                        continue;
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value!);
                }
                return list;
            }
        }
        catch (JsonException)
        {
            // Malformed payload → treat as "no versions"; caller falls back.
        }
        return Array.Empty<string>();
    }

    // Pick the highest SemVer from a set of version strings. Ordering is numeric on
    // major/minor/patch (so preview.10 correctly beats preview.5 — a plain string
    // sort gets this backwards), a release outranks a prerelease of the same core,
    // and prereleases order by label then trailing numeric identifier. Unparseable
    // entries are ignored. Returns null when nothing parses.
    internal static string? SelectLatestVersion(IEnumerable<string> versions)
    {
        string? best = null;
        VersionKey bestKey = default;
        foreach (var candidate in versions)
        {
            if (!TryParseVersionKey(candidate, out var key))
                continue;
            if (best is null || key.CompareTo(bestKey) > 0)
            {
                best = candidate.Trim();
                bestKey = key;
            }
        }
        return best;
    }

    // Parse "MAJOR.MINOR.PATCH[-prerelease][+build]" into an orderable key. Build
    // metadata is ignored (SemVer). The prerelease is split into a label and an
    // optional trailing numeric identifier so "preview.11" > "preview.2".
    internal static bool TryParseVersionKey(string? version, out VersionKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var text = version.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];

        string core;
        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            core = text[..dash];
            prerelease = text[(dash + 1)..];
        }
        else
        {
            core = text;
        }

        var parts = core.Split('.');
        if (parts.Length < 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        if (string.IsNullOrEmpty(prerelease))
        {
            key = new VersionKey(major, minor, patch, IsRelease: true, PreLabel: string.Empty, PreNumber: 0);
            return true;
        }

        var preParts = prerelease.Split('.');
        string label;
        long number;
        if (preParts.Length >= 2 && long.TryParse(preParts[^1], out number))
            label = string.Join('.', preParts[..^1]);
        else
        {
            label = prerelease;
            number = 0;
        }

        key = new VersionKey(major, minor, patch, IsRelease: false, label, number);
        return true;
    }

    // Orderable SemVer key. A release (no prerelease tag) sorts above any
    // prerelease of the same core; prereleases sort by label then numeric suffix.
    internal readonly record struct VersionKey(int Major, int Minor, int Patch, bool IsRelease, string PreLabel, long PreNumber)
        : IComparable<VersionKey>
    {
        public int CompareTo(VersionKey other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            // Release (true) outranks prerelease (false) at the same core.
            c = IsRelease.CompareTo(other.IsRelease);
            if (c != 0) return c;
            if (IsRelease) return 0;
            c = string.CompareOrdinal(PreLabel, other.PreLabel);
            if (c != 0) return c;
            return PreNumber.CompareTo(other.PreNumber);
        }
    }

    // Prefer CWD so a globally-installed `mur` (under ~/.dotnet/tools) still
    // discovers the source checkout the user is sitting in. Fall back to the
    // tool's own location for the legacy `bin/<arch>/mur.exe` install layout.
    static string? FindRepoRoot()
        => RepoRootFinder.FindRepoRoot(Directory.GetCurrentDirectory())
        ?? RepoRootFinder.FindRepoRoot();
}
