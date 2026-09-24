// Unit coverage for the Windows App SDK `dotnet new` template pack helper
// (Templates/WinAppSdkTemplates.cs).
//
// Reactor's app templates moved out of this repo's own
// `Microsoft.UI.Reactor.ProjectTemplates` pack (`dotnet new reactorapp`) and
// into `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` (`dotnet new reactor`).
// bootstrap.ps1 §5, `mur upgrade`, and `mur doctor` all route through this
// helper.
//
// The load-bearing piece worth unit-testing is version *selection*. The install
// path can't just hand `dotnet new install` a bare package id: that command has
// no `--prerelease` switch and resolves stable-only, so it fails outright while
// the pack is publishing prereleases (it shipped 0.0.6-alpha before any stable).
// So we resolve a version ourselves and pass `<id>::<version>`. These tests pin
// the two rules that resolution has to get right:
//   1. prefer a stable version when one exists, and
//   2. otherwise fall back to the *newest* prerelease — numerically, not
//      lexically ("alpha.10" > "alpha.9").
//
// The network fetch and the `dotnet new install` invocation are not unit-tested
// (they hit nuget.org and the template engine); `mur templates status` and the
// bootstrap CI job cover those.

using Microsoft.UI.Reactor.Cli.Templates;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public sealed class WinAppSdkTemplatesTests
{
    [Fact]
    public void PackageId_is_the_windows_app_sdk_template_pack()
    {
        // Anchors the identity bootstrap.ps1 installs and `mur doctor` probes for.
        // A rename here silently turns the doctor check into a permanent FAIL.
        Assert.Equal("Microsoft.WindowsAppSDK.WinUI.CSharp.Templates", WinAppSdkTemplates.PackageId);
    }

    [Fact]
    public void ShortNames_cover_every_reactor_template_in_the_pack()
    {
        // The four short names the pack registers (microsoft/WindowsAppSDK#6620).
        // `reactor` is the blank one users are pointed at first.
        Assert.Equal(
            new[] { "reactor", "reactor-mvu", "reactor-navview", "reactor-tabview" },
            WinAppSdkTemplates.ShortNames);
        Assert.Equal("reactor", WinAppSdkTemplates.BlankShortName);
        Assert.Contains(WinAppSdkTemplates.BlankShortName, WinAppSdkTemplates.ShortNames);
    }

    [Fact]
    public void SelectPreferStable_prefers_a_stable_over_a_higher_prerelease()
    {
        // A plain "highest SemVer" pick returns 1.1.0-alpha.1 here because its
        // core triple is higher. For a developer bootstrap we want the shipped
        // stable instead.
        var published = new[] { "1.0.0", "1.1.0-alpha.1", "0.9.0" };

        Assert.Equal("1.0.0", WinAppSdkTemplates.SelectPreferStable(published));
    }

    [Fact]
    public void SelectPreferStable_falls_back_to_newest_prerelease_when_no_stable_exists()
    {
        // The state the pack was actually in when this migration landed: only
        // prereleases published. Returning null here would make bootstrap fall
        // back to a bare package id, which `dotnet new install` then fails to
        // resolve (stable-only) — the exact breakage this logic prevents.
        var published = new[] { "0.0.4-alpha", "0.0.6-alpha", "0.0.5-alpha" };

        Assert.Equal("0.0.6-alpha", WinAppSdkTemplates.SelectPreferStable(published));
    }

    [Fact]
    public void SelectPreferStable_orders_prereleases_numerically_not_lexically()
    {
        // A string sort ranks "alpha.9" above "alpha.10". Pinning the older
        // template pack is a silent downgrade, not a hard failure, so assert it.
        var published = new[] { "0.0.6-alpha.9", "0.0.6-alpha.10", "0.0.6-alpha.2" };

        var latest = WinAppSdkTemplates.SelectPreferStable(published);

        Assert.Equal("0.0.6-alpha.10", latest);
        Assert.NotEqual("0.0.6-alpha.9", latest);
    }

    [Fact]
    public void SelectPreferStable_returns_null_for_no_versions()
    {
        // Empty feed / unreachable index. Callers treat null as "couldn't
        // resolve" and fall back to the bare package id rather than installing
        // a bogus "id::" spec.
        Assert.Null(WinAppSdkTemplates.SelectPreferStable(Array.Empty<string>()));
    }

    [Fact]
    public void EnumerateLocalVersions_reads_versions_off_nupkg_filenames()
    {
        // The `-WinAppSdkTemplatesSource <folder>` path used to test an
        // unpublished build of the pack: resolution reads the folder rather
        // than querying NuGet.
        var dir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            $"wasdk-templates-{Guid.NewGuid():N}");
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            var id = WinAppSdkTemplates.PackageId;
            global::System.IO.File.WriteAllText(global::System.IO.Path.Join(dir, $"{id}.0.0.6-alpha.nupkg"), "");
            global::System.IO.File.WriteAllText(global::System.IO.Path.Join(dir, $"{id}.0.0.7-alpha.nupkg"), "");
            // An unrelated package in the same folder must not be picked up.
            global::System.IO.File.WriteAllText(global::System.IO.Path.Join(dir, "Microsoft.UI.Reactor.9.9.9.nupkg"), "");

            var versions = WinAppSdkTemplates.EnumerateLocalVersions(dir);

            Assert.Equal(2, versions.Count);
            Assert.Contains("0.0.6-alpha", versions);
            Assert.Contains("0.0.7-alpha", versions);
            Assert.DoesNotContain("9.9.9", versions);
            Assert.Equal("0.0.7-alpha", WinAppSdkTemplates.SelectPreferStable(versions));
        }
        finally
        {
            try { global::System.IO.Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException) { /* best-effort */ }
        }
    }

    [Fact]
    public void EnumerateLocalVersions_returns_empty_for_a_missing_folder()
    {
        var missing = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            $"wasdk-templates-missing-{Guid.NewGuid():N}");

        Assert.Empty(WinAppSdkTemplates.EnumerateLocalVersions(missing));
    }

    // ── Destructive-install guard ──────────────────────────────────────────
    //
    // Observed for real during this migration: `dotnet new install <id> --force`
    // uninstalls the existing package *before* downloading the replacement. With
    // a bare package id (no version) and only prereleases published, NuGet then
    // reported "the package does not exist" — and the machine was left with **no**
    // templates installed at all. Exit code 103, working install destroyed.
    //
    // The install path therefore must never combine `--force` with a spec it
    // hasn't confirmed exists. These tests pin the two properties that prevent it.

    [Fact]
    public void ResolveLatestVersion_returns_null_for_an_empty_local_source()
    {
        // This is the input that produced the destructive case: nothing resolvable.
        // Returning null is what lets Install() choose the non-destructive branch,
        // so a null here is load-bearing, not an edge case.
        var dir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            $"wasdk-templates-empty-{Guid.NewGuid():N}");
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(WinAppSdkTemplates.ResolveLatestVersion(dir));
        }
        finally
        {
            try { global::System.IO.Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException) { /* best-effort */ }
        }
    }

    // ── Destructive-install decision table ─────────────────────────────────
    //
    // `dotnet new install --force` uninstalls the existing package BEFORE
    // downloading the replacement, so a failed install leaves the machine with no
    // templates at all. Observed for real: `--force` with a spec that did not
    // resolve uninstalled a working prerelease and then failed with exit 103.
    //
    // `PlanInstall` is the pure decision that governs when `--force` is used, so
    // these drive the real behaviour rather than grepping the source for a string.

    [Theory]
    // No target resolved: keep whatever is installed; never force.
    [InlineData("0.0.6-alpha", null, false, false, "KeepExisting")]
    // Nothing installed and nothing resolved: a plain install can't destroy anything.
    [InlineData(null, null, false, false, "PlainInstall")]
    // Nothing installed: plain install even for a confirmed target (no --force needed).
    [InlineData(null, "0.0.7-alpha", true, false, "PlainInstall")]
    // Same version already installed, no source: no-op.
    [InlineData("0.0.7-alpha", "0.0.7-alpha", true, false, "AlreadyCurrent")]
    // Replacing an install with a CONFIRMED version is the only forced path.
    [InlineData("0.0.6-alpha", "0.0.7-alpha", true, false, "ForcedReplace")]
    // THE REGRESSION: a pin that could not be confirmed must NOT force.
    [InlineData("0.0.6-alpha", "0.0.9-nope", false, false, "RefuseUnverifiedPin")]
    // An explicit source means "install from here", so an equal version still installs.
    [InlineData("0.0.7-alpha", "0.0.7-alpha", true, true, "ForcedReplace")]
    // With a source, a version that isn't in it must be refused even with nothing
    // installed: --add-source only ADDS a feed, so `<id>::<version>` would be
    // satisfied from nuget.org instead — a different package, same version string.
    [InlineData(null, "0.0.7-alpha", false, true, "RefuseUnverifiedPin")]
    [InlineData("0.0.6-alpha", "0.0.7-alpha", false, true, "RefuseUnverifiedPin")]
    // Same hazard with no version resolvable at all (an empty --source folder):
    // a bare package id resolves from the configured feeds, not from the folder.
    [InlineData(null, null, false, true, "RefuseUnverifiedPin")]
    [InlineData("0.0.6-alpha", null, false, true, "RefuseUnverifiedPin")]
    public void PlanInstall_only_forces_for_a_confirmed_target(
        string? installed, string? target, bool targetExists, bool hasSource, string expected)
    {
        var actual = WinAppSdkTemplates.PlanInstall(installed, target, targetExists, hasSource);
        Assert.Equal(expected, actual.ToString());
    }

    [Fact]
    public void PlanInstall_never_installs_from_an_unconfirmed_source()
    {
        // Property form: whenever an explicit --source was given, no action that
        // shells out to `dotnet new install` may be chosen unless the target was
        // confirmed to exist in that source. Otherwise `--add-source` silently
        // resolves the package from a different feed.
        foreach (var installed in new[] { null, "0.0.6-alpha" })
        foreach (var target in new[] { null, "0.0.7-alpha" })
        foreach (var exists in new[] { true, false })
        {
            var action = WinAppSdkTemplates.PlanInstall(installed, target, exists, hasSource: true);
            if (action is WinAppSdkTemplates.InstallAction.PlainInstall
                       or WinAppSdkTemplates.InstallAction.ForcedReplace)
            {
                Assert.True(exists && target is not null,
                    $"PlanInstall chose {action} against an unconfirmed --source " +
                    $"(installed={installed ?? "null"}, target={target ?? "null"}, targetExists={exists}).");
            }
        }
    }

    [Fact]
    public void PlanInstall_never_forces_an_unconfirmed_target()
    {
        // Property form of the row above: across every combination, ForcedReplace
        // must imply targetExists. This is the invariant that keeps a bad pin from
        // uninstalling a working pack.
        foreach (var installed in new[] { null, "0.0.6-alpha" })
        foreach (var target in new[] { null, "0.0.7-alpha" })
        foreach (var exists in new[] { true, false })
        foreach (var hasSource in new[] { true, false })
        {
            var action = WinAppSdkTemplates.PlanInstall(installed, target, exists, hasSource);
            if (action == WinAppSdkTemplates.InstallAction.ForcedReplace)
            {
                Assert.True(exists, $"PlanInstall forced a replace for an unconfirmed target " +
                                    $"(installed={installed}, target={target ?? "null"}, hasSource={hasSource}).");
                Assert.NotNull(installed);
            }
        }
    }

    // ── Credential redaction in the echoed command line ────────────────────

    [Theory]
    [InlineData("https://user:pat@pkgs.example.com/v3/index.json", "pat")]
    [InlineData("https://pkgs.example.com/v3/index.json?api-key=SECRET", "SECRET")]
    public void RedactSource_strips_credentials_from_feed_urls(string url, string secret)
    {
        // The install command line is echoed to the console and into CI logs.
        var redacted = WinAppSdkTemplates.RedactSource(url);
        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.Contains("pkgs.example.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSource_leaves_local_folder_paths_alone()
    {
        // A folder path carries nothing secret and must stay readable in the echo.
        const string folder = @"C:\src\WindowsAppSDK\localpackages";
        Assert.Equal(folder, WinAppSdkTemplates.RedactSource(folder));
    }

    // ── Installed-version parsing ──────────────────────────────────────────

    [Fact]
    public void InterpretInstalledVersionOutput_reads_the_version_for_this_pack()
    {
        // Verbatim shape of `dotnet new uninstall` with two packs installed — the
        // other pack's id is a PREFIX-adjacent name, which a substring match would
        // confuse with ours.
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.Templates
                  Version: 0.1.11-prerelease.100
                  Details:
                     Author: Microsoft
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
                  Version: 0.0.7-alpha
                  Details:
                     Author: Microsoft
            """;

        Assert.Equal("0.0.7-alpha", WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretInstalledVersionOutput_returns_null_when_this_pack_is_absent()
    {
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.Templates
                  Version: 0.1.11-prerelease.100
            """;

        Assert.Null(WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretInstalledVersionOutput_returns_null_when_no_version_line_follows()
    {
        // Malformed / truncated listing must not return a neighbouring package's version.
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
                  Details:
                     Author: Microsoft
            """;

        Assert.Null(WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretTemplateListOutput_requires_the_exact_short_name()
    {
        // Two traps in one fixture:
        //   • '-' is a word boundary, so `\breactor\b` / Contains("reactor") also
        //     matches `reactor-mvu` and `winui-reactor`;
        //   • the Template Name column carries the capitalised prose word
        //     "Reactor" as a standalone token, so a case-insensitive token match
        //     passes too.
        // Neither means the blank `reactor` template is installed.
        const string withoutBlank = """
            These templates matched your input: 'reactor'

            Template Name                      Short Name                     Language
            ---------------------------------  -----------------------------  --------
            Reactor MVU App (Experimental)     reactor-mvu,winui-reactor-mvu  [C#]
            """;

        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(withoutBlank));
    }

    [Fact]
    public void InterpretTemplateListOutput_accepts_the_short_name_in_a_comma_list()
    {
        // Real listings put the blank template's aliases in one comma-separated
        // column, so the token match must survive commas on both sides.
        const string withBlank = """
            These templates matched your input: 'reactor'

            Template Name                      Short Name                     Language
            ---------------------------------  -----------------------------  --------
            Reactor Blank App (Experimental)   reactor,reactor-blank          [C#]
            """;

        Assert.True(WinAppSdkTemplates.InterpretTemplateListOutput(withBlank));
    }

    [Fact]
    public void RedactSource_strips_a_query_from_a_file_uri()
    {
        // A file URI is IsFile, so a blanket "local path, nothing to hide" early
        // return skipped masking entirely. User-info is not constructible on the
        // file scheme (Uri rejects it), but a query or fragment is — and either can
        // carry a token.
        var redacted = WinAppSdkTemplates.RedactSource("file://host/share/pkgs?token=SECRET");
        Assert.DoesNotContain("SECRET", redacted, StringComparison.Ordinal);

        // A plain local path still passes through untouched.
        Assert.Equal(@"C:\repo\local-nupkgs", WinAppSdkTemplates.RedactSource(@"C:\repo\local-nupkgs"));
    }

    [Fact]
    public void RedactSource_strips_a_credential_bearing_fragment()
    {
        // UriBuilder preserves the fragment, so it has to be masked explicitly.
        var redacted = WinAppSdkTemplates.RedactSource("https://feed.example.com/v3/index.json#PAT");
        Assert.DoesNotContain("PAT", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePackageBaseAddress_reads_the_flat_container_from_a_service_index()
    {
        // The service-index host generally has no /flatcontainer/ path of its own,
        // so guessing one 404s for every package — including ones that exist. The
        // base address has to come out of the index.
        const string serviceIndex = """
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://example.com/query", "@type": "SearchQueryService/3.0.0" },
                { "@id": "https://ms-feed-25.example.com/_packaging/x/nuget/v3/flat2", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """;

        Assert.Equal(
            "https://ms-feed-25.example.com/_packaging/x/nuget/v3/flat2/",
            WinAppSdkTemplates.ParsePackageBaseAddress(serviceIndex));
    }

    [Fact]
    public void ParsePackageBaseAddress_returns_null_when_no_flat_container_is_declared()
    {
        // Must be null, not a guessed URL: the caller falls back to nuget.org, and
        // a fabricated address would instead 404 and read as "version absent".
        const string serviceIndex = """
            {"version":"3.0.0","resources":[{"@id":"https://example.com/query","@type":"SearchQueryService/3.0.0"}]}
            """;

        Assert.Null(WinAppSdkTemplates.ParsePackageBaseAddress(serviceIndex));
        Assert.Null(WinAppSdkTemplates.ParsePackageBaseAddress("not json at all"));
    }

    [Fact]
    public void Bootstrap_passes_its_configured_feed_to_the_version_resolver()
    {
        // The resolver otherwise only knows nuget.org. On a machine that reaches
        // the configured mirror but not nuget.org it would resolve nothing and fall
        // back to a bare package id, which cannot reach a prerelease-only pack —
        // failing the step with a usable feed sitting right there.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\$templateFeed\s*=\s*\$effectiveNuGetSource"),
            $"'{path}' must seed the template version feed from the resolved NuGet source.");
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(normalized, @"'--feed',\s*\$templateFeed"),
            $"'{path}' must pass that feed to `mur templates install --feed`.");
    }

    [Theory]
    // Version metadata is what picks the package to install, so plaintext lets a
    // network attacker choose the version; credentials in the URL would be sent to
    // whatever endpoint the URL names.
    [InlineData("https://pkgs.example.com/v3/index.json", true)]
    [InlineData("http://localhost:5000/v3/index.json", true)]
    [InlineData("http://127.0.0.1:5000/v3/index.json", true)]
    [InlineData("http://pkgs.example.com/v3/index.json", false)]
    [InlineData("https://user:pat@pkgs.example.com/v3/index.json", false)]
    [InlineData("https://pkgs.example.com/v3/index.json?api-key=SECRET", false)]
    [InlineData("https://pkgs.example.com/v3/index.json#SECRET", false)]
    [InlineData("ftp://pkgs.example.com/v3/index.json", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void IsAllowedFeedUrl_matches_the_bootstrap_feed_policy(string feed, bool allowed)
    {
        Assert.Equal(allowed, WinAppSdkTemplates.IsAllowedFeedUrl(feed));
    }

    [Fact]
    public void Bootstrap_derives_a_version_feed_from_an_explicit_nuget_config()
    {
        // An explicit -NuGetConfig reaches restore as `--configfile`, so it never
        // produces a bare source URL. Without this the explicit-mirror path falls
        // back to nuget.org for version lookup and resolves nothing on a machine
        // that can only reach the mirror.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                text.Replace("\r\n", "\n"),
                @"Get-ReactorFeedSourceFromConfig\s+-ConfigPath\s+\$effectiveNuGetConfig"),
            $"'{path}' must read a version feed out of an explicitly selected NuGet config.");

        var (resolverPath, resolver) = ReadRepoFile(global::System.IO.Path.Join("tools", "BootstrapFeedResolver.ps1"));
        Assert.True(
            resolver.Contains("function Get-ReactorFeedSourceFromConfig", StringComparison.Ordinal),
            $"'{resolverPath}' must define Get-ReactorFeedSourceFromConfig.");
    }

    [Theory]
    // A validated service index can still *advertise* an unsafe base address, and
    // following it would fetch version metadata — the thing that selects the
    // package — over plaintext, or send URL credentials to that endpoint.
    [InlineData("http://evil.example.com/flat2/", false)]
    [InlineData("https://user:pat@feed.example.com/flat2/", false)]
    [InlineData("https://feed.example.com/flat2/", true)]
    public void IsAllowedFeedUrl_also_gates_an_advertised_base_address(string advertised, bool allowed)
    {
        var serviceIndex = $$"""
            {"version":"3.0.0","resources":[{"@id":"{{advertised}}","@type":"PackageBaseAddress/3.0.0"}]}
            """;

        // Parsing is deliberately permissive — the policy check is what stops it.
        var parsed = WinAppSdkTemplates.ParsePackageBaseAddress(serviceIndex);
        Assert.NotNull(parsed);
        Assert.Equal(allowed, WinAppSdkTemplates.IsAllowedFeedUrl(parsed));
    }

    [Fact]
    public void Bootstrap_does_not_advertise_dotnet_new_reactor_when_templates_are_skipped()
    {
        // Two false promises to avoid: -SkipTemplates leaves the pack uninstalled,
        // and a *verified-unavailable* pack (0.0.6-alpha predates the Reactor
        // templates) is installed but cannot scaffold. Printing the command in
        // either case hands the user something that fails immediately.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        var next = normalized[normalized.LastIndexOf("Write-Host 'Next:'", StringComparison.Ordinal)..];
        Assert.True(
            next.Contains("if ($SkipTemplates)", StringComparison.Ordinal),
            $"'{path}' must gate the `dotnet new reactor` next-step guidance on -SkipTemplates.");
        Assert.True(
            next.Contains("elseif (-not $templatesVerified)", StringComparison.Ordinal),
            $"'{path}' must also gate that guidance on the step-5 verification result.");
    }

    [Fact]
    public void Upgrade_verifies_template_availability_after_installing()
    {
        // `mur upgrade` reporting success on an installed-but-unusable pack is the
        // same false PASS bootstrap and `mur templates install` already guard.
        var (path, text) = ReadRepoFile(global::System.IO.Path.Join("src", "Reactor.Cli", "Upgrade", "UpgradeCommand.cs"));
        Assert.True(
            text.Contains("AreTemplatesAvailable() == false", StringComparison.Ordinal),
            $"'{path}' must check template availability after Install, not just the install outcome.");

        // Install() runs `dotnet new install --add-source` with repoRoot as the
        // working directory, so a relative --templates-source resolved there would
        // name a different folder than the one the caller typed.
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                text.Replace("\r\n", "\n"),
                @"templateSource\s*=\s*Path\.GetFullPath\(templateSource!\)"),
            $"'{path}' must resolve a relative --templates-source against the caller's CWD.");
    }

    // ── Redirect policy on the version-metadata fetch ─────────────────────
    //
    // HttpClient follows redirects by default, which would defeat
    // IsAllowedFeedUrl entirely: a validated HTTPS service index can 302 to
    // plaintext HTTP and the body would be accepted without the policy ever
    // seeing that address. The version list is what selects the package to
    // install, so that is a real downgrade vector.

    sealed class StubHandler : global::System.Net.Http.HttpMessageHandler
    {
        readonly global::System.Collections.Generic.Queue<global::System.Net.Http.HttpResponseMessage> _responses;
        readonly global::System.Net.Http.HttpResponseMessage _exhausted =
            new(global::System.Net.HttpStatusCode.NotFound);

        public global::System.Collections.Generic.List<string> Requested { get; } = new();

        public StubHandler(params global::System.Net.Http.HttpResponseMessage[] responses) =>
            _responses = new global::System.Collections.Generic.Queue<global::System.Net.Http.HttpResponseMessage>(responses);

        protected override global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> SendAsync(
            global::System.Net.Http.HttpRequestMessage request,
            global::System.Threading.CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.AbsoluteUri);
            return global::System.Threading.Tasks.Task.FromResult(
                _responses.Count > 0 ? _responses.Dequeue() : _exhausted);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _exhausted.Dispose();
                while (_responses.Count > 0) _responses.Dequeue().Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static global::System.Net.Http.HttpResponseMessage Redirect(string location)
    {
        var response = new global::System.Net.Http.HttpResponseMessage(global::System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    [Fact]
    public void GetStringPolicyChecked_refuses_a_redirect_that_downgrades_to_plaintext()
    {
        var handler = new StubHandler(Redirect("http://evil.example.com/flat2/index.json"));
        using var http = new global::System.Net.Http.HttpClient(handler);

        var body = WinAppSdkTemplates.GetStringPolicyChecked(http, "https://feed.example.com/v3/index.json");

        Assert.Null(body);
        // The load-bearing half: the plaintext hop must never be requested at all.
        Assert.Equal(new[] { "https://feed.example.com/v3/index.json" }, handler.Requested);
    }

    [Fact]
    public void GetStringPolicyChecked_refuses_a_redirect_that_carries_credentials()
    {
        var handler = new StubHandler(Redirect("https://user:pat@feed.example.com/flat2/index.json"));
        using var http = new global::System.Net.Http.HttpClient(handler);

        Assert.Null(WinAppSdkTemplates.GetStringPolicyChecked(http, "https://feed.example.com/v3/index.json"));
        Assert.Single(handler.Requested);
    }

    [Fact]
    public void GetStringPolicyChecked_follows_a_compliant_redirect()
    {
        // The negative cases above prove nothing unless redirects otherwise work:
        // a method that always returned null would pass them.
        var ok = new global::System.Net.Http.HttpResponseMessage(global::System.Net.HttpStatusCode.OK)
        {
            Content = new global::System.Net.Http.StringContent("{\"versions\":[\"1.0.0\"]}"),
        };
        var handler = new StubHandler(Redirect("https://cdn.example.com/flat2/index.json"), ok);
        using var http = new global::System.Net.Http.HttpClient(handler);

        var body = WinAppSdkTemplates.GetStringPolicyChecked(http, "https://feed.example.com/v3/index.json");

        Assert.Equal("{\"versions\":[\"1.0.0\"]}", body);
        Assert.Equal(
            new[] { "https://feed.example.com/v3/index.json", "https://cdn.example.com/flat2/index.json" },
            handler.Requested);
    }

    [Fact]
    public void GetStringPolicyChecked_stops_a_redirect_loop()
    {
        var handler = new StubHandler(
            Redirect("https://a.example.com/1"), Redirect("https://a.example.com/2"),
            Redirect("https://a.example.com/3"), Redirect("https://a.example.com/4"),
            Redirect("https://a.example.com/5"), Redirect("https://a.example.com/6"));
        using var http = new global::System.Net.Http.HttpClient(handler);

        Assert.Null(WinAppSdkTemplates.GetStringPolicyChecked(http, "https://a.example.com/0"));
        Assert.Equal(5, handler.Requested.Count);
    }

    // ── False-PASS guard: "pack installed" != "templates usable" ───────────
    //
    // Observed live during the de-stale merge: the machine had
    // Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.6-alpha installed —
    // a version published *before* the Reactor templates were added. A
    // package-id-only probe reported `mur doctor` PASS and `mur templates
    // status` "installed", while `dotnet new list reactor` said
    // "No templates found matching: 'reactor'." and exited 103.
    //
    // The trap is that the not-found message itself contains the search term
    // ("...matching: 'reactor'" plus a "dotnet new search reactor" hint), so a
    // naive `output.Contains("reactor")` returns true exactly when the template
    // is missing. These pin the negative-marker-first rule.

    [Fact]
    public void InterpretTemplateListOutput_reports_missing_for_the_not_found_message()
    {
        // Verbatim output captured from `dotnet new list reactor` against an
        // installed-but-too-old 0.0.6-alpha pack. Note it mentions "reactor"
        // three times — a substring match on the short name would say "found".
        const string notFound = """
            No templates found matching: 'reactor'.

            To search for the templates on NuGet.org, run:
               dotnet new search reactor

            For details on the exit code, refer to https://aka.ms/templating-exit-codes#103
            """;

        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(notFound));
    }

    [Fact]
    public void InterpretTemplateListOutput_reports_available_for_a_real_listing()
    {
        // Shape of a real `dotnet new list reactor` table.
        const string listing = """
            These templates matched your input: 'reactor'

            Template Name                      Short Name                     Language  Tags
            ---------------------------------  -----------------------------  --------  -------------
            Reactor Blank App (Experimental)   reactor,reactor-blank          [C#]      Windows/WinUI
            Reactor MVU App (Experimental)     reactor-mvu                    [C#]      Windows/WinUI
            """;

        Assert.True(WinAppSdkTemplates.InterpretTemplateListOutput(listing));
    }

    [Fact]
    public void Doctor_probes_template_availability_not_just_package_presence()
    {
        // Source-level guard on the call site. The whole point of the fix is
        // that DoctorCommand asks "can the user scaffold?" — if it reverts to
        // the package-id probe for its PASS branch, the false PASS returns.
        var (path, text) = ReadRepoFile(global::System.IO.Path.Join(
            "src", "Reactor.Cli", "Doctor", "DoctorCommand.cs"));
        Assert.Contains("AreTemplatesAvailable()", text, StringComparison.Ordinal);
    }

    // ── Outcome-reporting guard ────────────────────────────────────────────
    //
    // Found by running `mur templates install` against the real published pack
    // with NuGet unreachable: the guard correctly kept the installed pack and
    // uninstalled nothing, but the command still printed "Installed." — telling
    // the user an install had happened when none had. A bare exit code cannot
    // express the difference, so Install returns an outcome instead.

    [Fact]
    public void DescribeOutcome_never_claims_an_install_that_did_not_happen()
    {
        // Behavioural form of the reporting bug: `mur templates install` printed
        // "Installed." while deliberately keeping an existing pack (nothing
        // resolvable). Only the two outcomes that actually changed the machine may
        // be described as an install/update.
        Assert.Equal("Installed.", TemplatesCommand.DescribeOutcome(WinAppSdkTemplates.InstallOutcome.Installed));
        Assert.Equal("Updated.", TemplatesCommand.DescribeOutcome(WinAppSdkTemplates.InstallOutcome.Updated));

        foreach (var unchanged in new[]
                 {
                     WinAppSdkTemplates.InstallOutcome.KeptExisting,
                     WinAppSdkTemplates.InstallOutcome.AlreadyCurrent,
                     WinAppSdkTemplates.InstallOutcome.Failed,
                 })
        {
            var message = TemplatesCommand.DescribeOutcome(unchanged);
            Assert.False(
                message.Contains("Installed.", StringComparison.Ordinal) ||
                message.Contains("Updated.", StringComparison.Ordinal),
                $"{unchanged} did not change the machine but is reported as \"{message}\".");
        }
    }

    // ── Bootstrap wiring guards ────────────────────────────────────────────
    //
    // Reactor's app templates live in the Windows App SDK `dotnet new` pack
    // (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`, short name `reactor`).
    // The in-repo `Microsoft.UI.Reactor.ProjectTemplates` pack that used to
    // provide `dotnet new reactorapp` has been deleted. These two tests pin both
    // halves of that contract — the regression they guard is silent, since a
    // bootstrap that quietly re-registered `reactorapp` would hand new
    // developers an unpackaged template the docs no longer describe.

    [Fact]
    public void Bootstrap_installs_the_windows_app_sdk_template_pack()
    {
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        Assert.Contains("Microsoft.WindowsAppSDK.WinUI.CSharp.Templates", text, StringComparison.Ordinal);
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                text.Replace("\r\n", "\n"), @"templates',\s*'install'"),
            $"'{path}' must install the Reactor templates via `mur templates install`, which resolves the " +
            "newest published version of the Windows App SDK template pack. `dotnet new install` has no " +
            "--prerelease switch and resolves stable-only, so installing the bare package id fails while " +
            "the pack is prerelease-only.");
    }

    [Fact]
    public void Bootstrap_does_not_install_the_removed_reactorapp_template()
    {
        // The in-repo ProjectTemplates pack was deleted; nothing in bootstrap may
        // resurrect it by handing the id to `dotnet new install`.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");

        // Strip comment lines: the step documents the migration in prose, and
        // those mentions must not trip this guard.
        var code = string.Join('\n', normalized
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

        Assert.False(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                code, @"new\s+install.*Microsoft\.UI\.Reactor\.ProjectTemplates"),
            $"'{path}' must not `dotnet new install` Microsoft.UI.Reactor.ProjectTemplates — that package was " +
            "removed from this repo. Scaffolding goes through the Windows App SDK pack (`dotnet new reactor`).");
    }

    [Fact]
    public void Repo_no_longer_ships_the_in_repo_template_package()
    {
        // The deletion itself. Assert on the tracked *source* rather than the
        // directory: bin/obj under tools/Templates are gitignored, so a
        // contributor who built the project before pulling this change still has
        // the folder on disk. Checking Directory.Exists would fail for them while
        // nothing is actually wrong.
        var root = FindRoot();
        foreach (var relative in new[]
                 {
                     global::System.IO.Path.Join("tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj"),
                     global::System.IO.Path.Join("tools", "Templates", "templates", "WinUIApp-CSharp", ".template.config", "template.json"),
                 })
        {
            Assert.False(
                global::System.IO.File.Exists(global::System.IO.Path.Join(root, relative)),
                $"'{relative}' is back. The in-repo Microsoft.UI.Reactor.ProjectTemplates package was removed " +
                "in favour of the Windows App SDK `dotnet new reactor` templates.");
        }

        var (relPath, release) = ReadRepoFile(global::System.IO.Path.Join(".github", "workflows", "release.yml"));
        Assert.False(
            release.Contains("Microsoft.UI.Reactor.Templates.csproj", StringComparison.Ordinal),
            $"'{relPath}' packs the removed template project again.");
    }

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = global::System.IO.Path.Join(FindRoot(), repoRelativePath);
        Assert.True(global::System.IO.File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, global::System.IO.File.ReadAllText(path));
    }

    static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir, "Reactor.slnx")))
            dir = global::System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}

// ── `mur templates install` argv parsing ──────────────────────────────────
//
// Only the branches that return *before* touching the machine are exercised:
// help, unknown option, missing value, and a bare positional. A real install is
// a global `dotnet new install`, which these tests must never trigger.
//
// Strict parsing is load-bearing: `--sorce ./pkgs` silently ignored would
// install from the configured feeds instead of the folder the user named, and
// the install would look successful.
[Collection("ConsoleTests")]
public sealed class TemplatesCommandArgvTests
{
    static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new global::System.IO.StringWriter();
        using var stderr = new global::System.IO.StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = TemplatesCommand.Run(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Install_help_succeeds_without_installing_anything()
    {
        // `mur templates install --help` used to fall straight through to a real
        // install, so this asserts the help text *and* the absence of the install
        // banner rather than just the exit code.
        var (exitCode, stdout, _) = Run("install", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: mur templates install", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Installing " + WinAppSdkTemplates.PackageId, stdout, StringComparison.Ordinal);
    }

    [Theory]
    // A typo must fail loudly, not install from somewhere else.
    [InlineData(new[] { "install", "--sorce", "./pkgs" }, "unknown option")]
    [InlineData(new[] { "install", "-x" }, "unknown option")]
    // A bare positional is never meaningful here.
    [InlineData(new[] { "install", "0.0.7-alpha" }, "unexpected argument")]
    // A flag with no value would otherwise silently install the resolved latest.
    [InlineData(new[] { "install", "--source" }, "requires a value")]
    [InlineData(new[] { "install", "--version" }, "requires a value")]
    [InlineData(new[] { "install", "--feed" }, "requires a value")]
    // A following flag is not a value.
    [InlineData(new[] { "install", "--source", "--version", "1.0.0" }, "requires a value")]
    public void Install_rejects_bad_argv(string[] args, string expected)
    {
        var (exitCode, stdout, stderr) = Run(args);

        Assert.Equal(1, exitCode);
        Assert.Contains(expected, stderr, StringComparison.Ordinal);
        // Nothing may have been installed on the way to the error.
        Assert.DoesNotContain("Installing " + WinAppSdkTemplates.PackageId, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_subcommand_and_no_subcommand_both_show_help()
    {
        var (missing, missingOut, _) = Run();
        Assert.Equal(1, missing);
        Assert.Contains("mur templates", missingOut, StringComparison.Ordinal);

        var (unknown, _, unknownErr) = Run("instal");
        Assert.Equal(1, unknown);
        Assert.Contains("instal", unknownErr, StringComparison.Ordinal);
    }
}