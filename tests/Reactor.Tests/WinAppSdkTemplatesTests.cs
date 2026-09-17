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
        var dir = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"wasdk-templates-{Guid.NewGuid():N}");
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            var id = WinAppSdkTemplates.PackageId;
            global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(dir, $"{id}.0.0.6-alpha.nupkg"), "");
            global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(dir, $"{id}.0.0.7-alpha.nupkg"), "");
            // An unrelated package in the same folder must not be picked up.
            global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(dir, "Microsoft.UI.Reactor.9.9.9.nupkg"), "");

            var versions = WinAppSdkTemplates.EnumerateLocalVersions(dir);

            Assert.Equal(2, versions.Count);
            Assert.Contains("0.0.6-alpha", versions);
            Assert.Contains("0.0.7-alpha", versions);
            Assert.DoesNotContain("9.9.9", versions);
            Assert.Equal("0.0.7-alpha", WinAppSdkTemplates.SelectPreferStable(versions));
        }
        finally
        {
            try { global::System.IO.Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void EnumerateLocalVersions_returns_empty_for_a_missing_folder()
    {
        var missing = global::System.IO.Path.Combine(
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
        var dir = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"wasdk-templates-empty-{Guid.NewGuid():N}");
        global::System.IO.Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(WinAppSdkTemplates.ResolveLatestVersion(dir));
        }
        finally
        {
            try { global::System.IO.Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Install_never_pairs_force_with_an_unresolved_package_spec()
    {
        // Source-level guard. Install() shells out to `dotnet new`, so driving it
        // for real would mutate the developer's machine — exactly the damage being
        // guarded against. Instead assert the invariant on the source: every
        // "--force" must be added on a path that has a concrete version, and the
        // bare-id install (the `target is null` fallback) must not add --force.
        var (path, text) = ReadCliSource();

        // The bare-id fallback line — the one that runs when no version resolved.
        Assert.Contains("\"new\", \"install\", PackageId)", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("\"new\", \"install\", PackageId, \"--force\"", text);

        // --force must be conditional on something already being installed.
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                text.Replace("\r\n", "\n"),
                @"if \(installed is not null\)\s*\n\s*args\.Add\(""--force""\);"),
            $"'{path}' must only add --force when replacing an existing install. " +
            "`dotnet new install --force` uninstalls before downloading, so pairing it with a spec " +
            "that may not resolve destroys a working template install (exit 103).");
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
        var (path, text) = ReadRepoFile(global::System.IO.Path.Combine(
            "src", "Reactor.Cli", "Doctor", "DoctorCommand.cs"));
        Assert.Contains("AreTemplatesAvailable()", text, StringComparison.Ordinal);
    }

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = global::System.IO.Path.Combine(FindRoot(), repoRelativePath);
        Assert.True(global::System.IO.File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, global::System.IO.File.ReadAllText(path));
    }

    static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !global::System.IO.File.Exists(global::System.IO.Path.Combine(dir, "Reactor.slnx")))
            dir = global::System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    static (string path, string text) ReadCliSource()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !global::System.IO.File.Exists(global::System.IO.Path.Combine(dir, "Reactor.slnx")))
            dir = global::System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        var path = global::System.IO.Path.Combine(
            dir!, "src", "Reactor.Cli", "Templates", "WinAppSdkTemplates.cs");
        Assert.True(global::System.IO.File.Exists(path), $"Expected '{path}' to exist; file moved or renamed?");
        return (path, global::System.IO.File.ReadAllText(path));
    }
}
