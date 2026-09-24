// The Windows App SDK `dotnet new` template pack, which is where Reactor's app
// templates now live.
//
// Reactor used to ship its own `Microsoft.UI.Reactor.ProjectTemplates` pack
// (`dotnet new reactorapp`, unpackaged). The Windows App SDK template pack now
// carries first-class Reactor templates alongside the WinUI 3 XAML ones, so
// that's what `bootstrap.ps1` installs and what `mur doctor` looks for. The
// legacy pack's source has been deleted from this repo and it is no longer
// built or published — the versions already on NuGet.org are all that remain,
// and they are deprecated.
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

using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// The `PackageBaseAddress/3.0.0` resource (the flat container) advertised by a
    /// NuGet v3 service index, or null when the document doesn't declare one.
    /// </summary>
    /// <remarks>
    /// A service index host generally has no `/flatcontainer/` path of its own, so
    /// guessing one returns 404 for every package — including ones that certainly
    /// exist. The base address has to be read out of the index.
    /// </remarks>
    internal static string? ParsePackageBaseAddress(string serviceIndexJson)
    {
        // The version suffix has moved across service-index revisions (3.0.0,
        // 3.0.0-beta), so match the family rather than one literal.
        static string? BaseAddressOf(JsonElement resource)
        {
            if (!resource.TryGetProperty("@type", out var type) || type.ValueKind != JsonValueKind.String)
                return null;
            if (!(type.GetString() ?? string.Empty)
                    .StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!resource.TryGetProperty("@id", out var id) || id.ValueKind != JsonValueKind.String)
                return null;
            return id.GetString();
        }

        try
        {
            using var doc = JsonDocument.Parse(serviceIndexJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
                return null;

            var value = resources.EnumerateArray()
                .Where(resource => resource.ValueKind == JsonValueKind.Object)
                .Select(BaseAddressOf)
                .FirstOrDefault(address => !string.IsNullOrWhiteSpace(address));

            if (value is null) return null;
            return value.EndsWith('/') ? value : value + "/";
        }
        catch (JsonException)
        {
            // Not a service index — fall back to the public flat container.
            return null;
        }
    }

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

        // Match the short name as a whole token. A plain Contains (or a \b regex)
        // also matches `reactor-mvu` and `winui-reactor`, because '-' is a word
        // boundary — so a listing that has the richer shells but not the blank
        // template would be read as success. Short names are comma-separated
        // within a whitespace-delimited column, so require one of those delimiters
        // on each side.
        //
        // Deliberately case-sensitive: the Template Name column carries the
        // capitalised prose word ("Reactor MVU App"), which is a standalone token
        // and would match case-insensitively even when the blank template is
        // absent. Short names are lowercase and BlankShortName is a constant.
        return Regex.IsMatch(output, BlankShortNameTokenPattern);
    }

    /// <summary>
    /// <see cref="BlankShortName"/> as a whole token: preceded and followed by a
    /// comma, whitespace, or the edge of the text.
    /// </summary>
    private static readonly string BlankShortNameTokenPattern =
        @"(?<![^\s,])" + Regex.Escape(BlankShortName) + @"(?![^\s,])";

    /// <summary>
    /// The installed version of the template pack, or null when it isn't
    /// installed (or the listing couldn't be read).
    /// </summary>
    public static string? GetInstalledVersion()
    {
        var output = RunCapture("new", "uninstall");
        return output is null ? null : InterpretInstalledVersionOutput(output);
    }

    /// <summary>
    /// Pulls this pack's version out of `dotnet new uninstall` output. Split out
    /// (and internal) so the parser is testable against captured CLI text without
    /// invoking the template engine.
    /// </summary>
    internal static string? InterpretInstalledVersionOutput(string output)
    {
        // The listing indents each package id, then its metadata:
        //     Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
        //        Version: 0.0.6-alpha
        // Several packages can be listed, so match the id line exactly rather than
        // by substring — other ids legitimately contain this one as a prefix.
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

    /// <summary>What an <see cref="Install"/> call actually did.</summary>
    public enum InstallOutcome
    {
        /// <summary>The pack was not present and is now installed.</summary>
        Installed,
        /// <summary>An older pack was replaced with a newer one.</summary>
        Updated,
        /// <summary>The resolved version was already installed; nothing changed.</summary>
        AlreadyCurrent,
        /// <summary>No version could be resolved, so the existing install was left untouched.</summary>
        KeptExisting,
        /// <summary>The install was attempted and failed.</summary>
        Failed,
    }

    /// <summary>What an <see cref="Install"/> call should do, decided from inputs alone.</summary>
    /// <remarks>
    /// Split out from <see cref="Install"/> so the decision table is unit-testable
    /// without shelling out to the template engine or touching the machine. The
    /// destructive case is <see cref="InstallAction.ForcedReplace"/>: it is the only
    /// path that passes `--force`, which uninstalls before downloading.
    /// </remarks>
    internal enum InstallAction
    {
        /// <summary>Leave the existing install alone (nothing resolvable to move to).</summary>
        KeepExisting,
        /// <summary>Install without `--force` — nothing is installed, so there is nothing to lose.</summary>
        PlainInstall,
        /// <summary>Replace an existing install with a version known to exist.</summary>
        ForcedReplace,
        /// <summary>The resolved target is already installed; do nothing.</summary>
        AlreadyCurrent,
        /// <summary>
        /// An explicit version was pinned but could not be confirmed to exist. Refuse
        /// rather than `--force`, which would uninstall the working pack and then fail.
        /// </summary>
        RefuseUnverifiedPin,
    }

    /// <summary>
    /// Pure decision table for <see cref="Install"/>.
    /// </summary>
    /// <param name="installed">Currently installed version, or null.</param>
    /// <param name="target">Version we want, or null when none could be resolved.</param>
    /// <param name="targetExists">
    /// True only when <paramref name="target"/> was confirmed present in the feed or
    /// folder. A pinned version that could not be confirmed must never be forced.
    /// </param>
    /// <param name="hasSource">True when an extra NuGet source was supplied.</param>
    internal static InstallAction PlanInstall(string? installed, string? target, bool targetExists, bool hasSource)
    {
        // An explicit source says "install the build that is *here*". But
        // `dotnet new install` has no feed-isolation switch — `--add-source` only
        // *adds* to the configured feeds — so anything we cannot confirm in that
        // source gets resolved from nuget.org instead. That is a different package,
        // frequently under the very same version string (a locally packed
        // 0.0.7-alpha vs the published 0.0.7-alpha), so neither the id nor the pin
        // would reveal the substitution. Sources are local folders by the time we
        // get here (URL sources are rejected up front), so the listing is
        // authoritative and absence is definitive: refuse rather than install
        // something the caller did not point at.
        if (hasSource && (target is null || !targetExists))
            return InstallAction.RefuseUnverifiedPin;

        if (target is null)
            return installed is not null ? InstallAction.KeepExisting : InstallAction.PlainInstall;

        // An explicit source means "get it from here even if the id/version matches",
        // so don't short-circuit on an equal version string in that case.
        if (installed is not null && !hasSource &&
            string.Equals(installed, target, StringComparison.OrdinalIgnoreCase))
            return InstallAction.AlreadyCurrent;

        // Nothing installed: a plain install cannot destroy anything.
        if (installed is null)
            return InstallAction.PlainInstall;

        // Replacing an existing install requires --force, which uninstalls first.
        // Only take that path for a version we know is actually there.
        return targetExists ? InstallAction.ForcedReplace : InstallAction.RefuseUnverifiedPin;
    }

    /// <summary>
    /// Masks credentials in a NuGet source before it is echoed. Feed URLs can carry a
    /// PAT in the user-info or query segment, and this command line is printed to the
    /// console and into CI logs.
    /// </summary>
    internal static string RedactSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return source;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.IsFile)
            return source; // local folder path — nothing secret in it

        var builder = new UriBuilder(uri)
        {
            UserName = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : "***",
            Password = string.Empty,
            Query = string.IsNullOrEmpty(uri.Query) ? string.Empty : "***",
            Fragment = string.IsNullOrEmpty(uri.Fragment) ? string.Empty : "***",
        };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Installs (or updates) the template pack.
    /// </summary>
    /// <param name="workingDirectory">Working directory for the `dotnet` process.</param>
    /// <param name="source">
    /// A local folder holding the nupkg, passed to `dotnet new install --add-source`.
    /// Feed URLs are rejected: `dotnet new install` has no feed-isolation switch
    /// (`--add-source` only adds to the configured sources), so a URL source can be
    /// silently satisfied from another feed — often under the very same version string.
    /// </param>
    /// <param name="version">Explicit version to pin. When omitted the newest published version is resolved.</param>
    /// <param name="feed">
    /// NuGet v3 service-index URL used for version *resolution* only (never passed
    /// to `dotnet new install`). bootstrap supplies the clone's configured feed so
    /// a machine that cannot reach nuget.org still resolves a version.
    /// </param>
    /// <remarks>
    /// Returns what actually happened rather than a bare exit code: "kept the
    /// existing install because nothing could be resolved" is a success for
    /// exit-code purposes but must not be reported to the user as "installed".
    /// </remarks>
    public static InstallOutcome Install(string workingDirectory, string? source = null, string? version = null, string? feed = null)
    {
        var hasSource = !string.IsNullOrWhiteSpace(source);
        var pinned = !string.IsNullOrWhiteSpace(version);
        // `--source` must be a local folder of nupkgs.
        //
        // `dotnet new install` has no feed-isolation switch: `--add-source` only
        // *augments* the configured sources. So `<id>::<version>` passed alongside
        // a feed URL can be satisfied from nuget.org instead, silently installing a
        // different package than the one asked for. That is not theoretical — an
        // unpublished build and the published pack routinely carry the *same*
        // version string (a local `0.0.7-alpha` vs the published `0.0.7-alpha`), so
        // the version pin cannot disambiguate them either.
        //
        // A folder has neither problem: it is enumerable, so a pin is confirmable,
        // and `--add-source <folder>` plus an exact version resolves to the file
        // that is actually there. `dotnet new install` also ignores the NuGet
        // credential provider, so an authenticated feed URL fails anyway with a
        // misleading "the package does not exist". Restore first, then point at the
        // cache folder.
        if (hasSource && !Directory.Exists(source))
        {
            // Redact before echoing: a feed URL can carry a PAT, and this line lands
            // in console output and CI logs.
            Console.Error.WriteLine(
                $"  error: --source '{RedactSource(source!)}' is not a local folder. `dotnet new install` " +
                "cannot be restricted to a single feed (--add-source only adds one), so a feed URL can " +
                "silently resolve the package from somewhere else. Restore the package first, then pass " +
                "the folder holding the .nupkg:");
            Console.Error.WriteLine(
                $"    mur templates install --source %USERPROFILE%\\.nuget\\packages\\{PackageId.ToLowerInvariant()}\\<version>");
            return InstallOutcome.Failed;
        }

        var installed = GetInstalledVersion();

        string? target;
        bool targetExists;
        if (pinned)
        {
            target = version!.Trim();
            // Confirm the pin before considering --force. Null means "couldn't tell",
            // which is treated as unverified — never destructive on a maybe. With a
            // source, the source is a local folder (URL sources are rejected above),
            // so its listing is authoritative: absent really means absent.
            var available = ResolveAvailableVersions(source, feed);
            targetExists = available is not null &&
                           available.Any(v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            target = ResolveLatestVersion(source, feed);
            // A resolved target came out of the feed listing, so it exists by construction.
            targetExists = target is not null;
        }

        switch (PlanInstall(installed, target, targetExists, hasSource))
        {
            case InstallAction.KeepExisting:
                Console.Error.WriteLine(
                    $"  warning: could not resolve a published version of {PackageId}; " +
                    $"keeping the installed {installed}. Re-run with network access, or pass an explicit version.");
                return InstallOutcome.KeptExisting;

            case InstallAction.AlreadyCurrent:
                Console.WriteLine($"  Already installed: {PackageId} {installed}");
                return InstallOutcome.AlreadyCurrent;

            case InstallAction.RefuseUnverifiedPin:
                Console.Error.WriteLine(hasSource
                    ? (target is null
                        ? $"  error: no {PackageId} .nupkg found in --source '{RedactSource(source!)}'. " +
                          $"Installing anyway would resolve the package from another configured feed, " +
                          $"because `--add-source` only adds to the configured sources. Point --source at " +
                          $"a folder that has it."
                        : $"  error: {PackageId} {target} is not in --source '{RedactSource(source!)}'. " +
                          $"Installing anyway would let `--add-source` resolve that version from another " +
                          $"configured feed — a different package under the same version string. Check the " +
                          $"version, or point --source at the folder that has it.")
                    : $"  error: {PackageId} {target} could not be found in the configured sources, and " +
                      $"replacing an install requires `--force`, which uninstalls the current {installed} " +
                      $"before downloading. Refusing, so your working install survives. " +
                      $"Check the version, or pass --source with the folder that has it.");
                return InstallOutcome.Failed;

            case InstallAction.PlainInstall:
                return RunInstall(workingDirectory, target, source, force: false, installed);

            default: // ForcedReplace
                return RunInstall(workingDirectory, target, source, force: true, installed);
        }
    }

    static InstallOutcome RunInstall(string workingDirectory, string? target, string? source, bool force, string? installed)
    {
        Console.WriteLine(installed is null
            ? $"  Installing {PackageId} {target ?? "(latest stable)"}"
            : $"  Updating {PackageId} {installed} → {target}");

        // `<id>::<version>` is `dotnet new install`'s explicit-version syntax and
        // the only way to reach a prerelease — a bare id resolves stable-only.
        var spec = target is null ? PackageId : $"{PackageId}::{target}";
        var args = new List<string> { "new", "install", spec };
        if (force) args.Add("--force");
        if (!string.IsNullOrWhiteSpace(source))
        {
            args.Add("--add-source");
            args.Add(source!);
        }

        // Echo with the source redacted — a feed URL can carry a PAT, and this line
        // lands in console output and CI logs.
        var echo = args.Select(a => string.Equals(a, source, StringComparison.Ordinal) ? RedactSource(a) : a);
        Console.WriteLine($"  dotnet {string.Join(' ', echo)}");

        var rc = Run(workingDirectory, args.ToArray());
        if (rc != 0)
        {
            if (force && installed is not null)
            {
                Console.Error.WriteLine(
                    $"  warning: the update failed and `dotnet new install --force` removes the old package first, " +
                    $"so {PackageId} may no longer be installed. Restore it with: " +
                    $"dotnet new install {PackageId}::{installed}");
            }
            Console.Error.WriteLine(
                "  note: `dotnet new install` does not use the NuGet credential provider, so an authenticated " +
                "feed reports \"the package does not exist\". Restore the package first, then pass the cached " +
                ".nupkg folder to --source.");
            return InstallOutcome.Failed;
        }
        return installed is null ? InstallOutcome.Installed : InstallOutcome.Updated;
    }

    /// <summary>
    /// Whether a NuGet service-index URL is safe to query: HTTPS (or loopback
    /// HTTP), with no credentials in the user-info, query string, or fragment.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Test-ReactorPackageFeedUrl</c> in tools/BootstrapFeedResolver.ps1,
    /// which applies the same policy to <c>-NuGetSource</c>. Version metadata is
    /// what picks the package to install, so fetching it over plaintext lets a
    /// network attacker choose the version; and a credential in the URL would be
    /// sent to whatever endpoint the URL names.
    /// </remarks>
    internal static bool IsAllowedFeedUrl(string? feed)
    {
        if (string.IsNullOrWhiteSpace(feed)) return false;
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri)) return false;

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return true;

        // Plaintext only where it cannot leave the machine.
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               uri.IsLoopback;
    }

    /// <summary>
    /// Every version the configured source offers, or null when the listing could
    /// not be obtained (offline, unreachable feed). Null means "couldn't tell" and
    /// must never be read as "the version is absent".
    /// </summary>
    /// <param name="source">Local folder of nupkgs, read instead of any feed.</param>
    /// <param name="feed">
    /// NuGet v3 service-index URL to enumerate from. Supplied by bootstrap when the
    /// clone is configured against a mirror, so a machine that cannot reach
    /// nuget.org still resolves a version instead of falling back to a bare package
    /// id that cannot reach this prerelease-only pack.
    /// </param>
    internal static IReadOnlyList<string>? ResolveAvailableVersions(string? source = null, string? feed = null)
    {
        // A local folder source is the unpublished-build test path: read the
        // versions straight off the nupkg filenames rather than hitting NuGet.
        if (!string.IsNullOrWhiteSpace(source) && Directory.Exists(source))
            return EnumerateLocalVersions(source!);

        using var http = CreateFeedHttpClient();

        if (!string.IsNullOrWhiteSpace(feed))
        {
            if (!IsAllowedFeedUrl(feed))
            {
                Console.Error.WriteLine(
                    $"  warning: ignoring --feed '{RedactSource(feed!)}' — a version feed must be an HTTPS URL " +
                    $"(or loopback HTTP) with no credentials in its user-info, query string or fragment.");
            }
            else
            {
                var versions = TryEnumerateFromFeed(http, feed!);
                if (versions is not null) return versions;
                Console.Error.WriteLine(
                    $"  warning: could not enumerate {PackageId} from '{RedactSource(feed!)}'; " +
                    $"falling back to nuget.org.");
            }
        }

        return TryGetVersions(http, FlatContainerIndexUrl);
    }

    /// <summary>
    /// An <see cref="HttpClient"/> that does <em>not</em> follow redirects on its own.
    /// </summary>
    /// <remarks>
    /// Automatic redirects would defeat <see cref="IsAllowedFeedUrl"/>: a validated
    /// HTTPS URL can redirect to plaintext HTTP, or to a host carrying credentials in
    /// the URL, and the body would be accepted without the policy ever seeing that
    /// address. <see cref="GetStringPolicyChecked"/> follows them itself and re-applies
    /// the policy at every hop.
    /// </remarks>
    static HttpClient CreateFeedHttpClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

    /// <summary>
    /// GETs <paramref name="url"/>, re-applying <see cref="IsAllowedFeedUrl"/> to every
    /// redirect hop. Returns null when the policy rejects a hop, the chain is too long,
    /// or the request fails.
    /// </summary>
    internal static string? GetStringPolicyChecked(HttpClient http, string url)
    {
        // Enough for the CDN/vanity-host hops real feeds use, few enough to stop a loop.
        const int MaxHops = 5;

        var current = url;
        for (var hop = 0; hop < MaxHops; hop++)
        {
            if (!IsAllowedFeedUrl(current))
            {
                Console.Error.WriteLine(
                    $"  warning: refusing to follow '{RedactSource(current)}' — a version feed must be an " +
                    $"HTTPS URL (or loopback HTTP) with no credentials in its user-info, query string or fragment.");
                return null;
            }

            using var response = http.GetAsync(current).GetAwaiter().GetResult();
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null) return null;
                // A relative Location is resolved against the hop it came from, then
                // re-checked at the top of the next iteration.
                current = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(current), location).AbsoluteUri;
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        Console.Error.WriteLine($"  warning: too many redirects following '{RedactSource(url)}'.");
        return null;
    }

    /// <summary>
    /// Resolves a service index to its flat container and lists the pack's versions
    /// there. Null on any failure, so the caller can fall back.
    /// </summary>
    static IReadOnlyList<string>? TryEnumerateFromFeed(HttpClient http, string serviceIndexUrl)
    {
        string? indexJson;
        try
        {
            indexJson = GetStringPolicyChecked(http, serviceIndexUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
        if (indexJson is null) return null;

        var baseAddress = ParsePackageBaseAddress(indexJson);
        if (baseAddress is null) return null;

        // The index is fetched from a validated URL, but what it *advertises* is
        // not covered by that check: a compromised or misconfigured index can point
        // PackageBaseAddress at plaintext HTTP, or at a URL carrying credentials.
        // Re-apply the policy before following it.
        if (!IsAllowedFeedUrl(baseAddress))
        {
            Console.Error.WriteLine(
                $"  warning: ignoring the package base address advertised by '{RedactSource(serviceIndexUrl)}' " +
                $"— it must be an HTTPS URL (or loopback HTTP) with no credentials in its user-info, " +
                $"query string or fragment.");
            return null;
        }

        // Flat-container paths are lowercase.
        return TryGetVersions(http, $"{baseAddress}{PackageId.ToLowerInvariant()}/index.json");
    }

    static IReadOnlyList<string>? TryGetVersions(HttpClient http, string flatContainerIndexUrl)
    {
        try
        {
            var json = GetStringPolicyChecked(http, flatContainerIndexUrl);
            return json is null ? null : PackLocalCommand.ParseFlatContainerVersions(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"  warning: could not query NuGet for {PackageId} versions " +
                $"({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// Newest published version of the pack — the newest stable when one exists,
    /// otherwise the newest prerelease. Returns null when nothing could be
    /// resolved (offline, unreachable feed, empty folder).
    /// </summary>
    public static string? ResolveLatestVersion(string? source = null, string? feed = null)
    {
        var versions = ResolveAvailableVersions(source, feed);
        return versions is null ? null : SelectPreferStable(versions);
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
            versions.AddRange(Directory
                .EnumerateFiles(folder, $"{PackageId}.*.nupkg")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null &&
                               name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => name![prefix.Length..]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
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
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // Redact here too — this path formats the same argument list that the
            // normal echo redacts, and `--add-source` may carry a feed URL.
            Console.Error.WriteLine(
                $"  failed to run `dotnet {string.Join(' ', arguments.Select(RedactSource))}`: {ex.Message}");
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
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
