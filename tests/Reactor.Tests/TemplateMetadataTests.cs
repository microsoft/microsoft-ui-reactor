// Repository-content validation for the legacy project-template metadata.
//
// These guard `tools/Templates/` — the in-repo `Microsoft.UI.Reactor.ProjectTemplates`
// pack that provides `dotnet new reactorapp`. That pack is still built by
// `mur pack-local` and published by the release workflow, but as of the move to
// the Windows App SDK template pack it is no longer installed by `bootstrap.ps1`.
// `dotnet new reactor` (packaged, from Microsoft.WindowsAppSDK.WinUI.CSharp.Templates)
// is the supported scaffolding path; see WinAppSdkTemplatesTests.
//
// The bug this file was originally added against:
//   `tools/Templates/templates/WinUIApp-CSharp/.template.config/template.json`
//   shipped with `identity` = "Micrsoft.UI.Reactor.CSharp" (missing the
//   second 'o') from at least Phase 1 onward. The existing integration test
//   `CreateTemplateTests` did not catch this because it installs the
//   template into a per-test ephemeral hive via `--debug:custom-hive`,
//   where the misspelled identity is unique (no duplicates), so
//   `dotnet new reactorapp` resolves correctly inside the fresh hive.
//
//   The typo only surfaces against the user's *real* template cache
//   (~/.templateengine/dotnetcli/<sdk>/templatecache.json), where every
//   harness run that does `dotnet new install ... --force` accumulates
//   duplicate entries for the same misspelled identity. Eventually the
//   `dotnet new reactorapp` short-name lookup finds more than one match
//   and throws "Sequence contains more than one matching element" with
//   exit code 70. The EC3 eval batch hit this 20/20 runs.
//
// What this test asserts:
//   - The template's `identity` and `groupIdentity` use the canonical
//     Microsoft.UI.Reactor brand namespace, not any spelling variant.
//   - The file contains no `Micrsoft` substring anywhere (catches the
//     same typo if it sneaks back in via copy-paste in a new symbol /
//     description / etc.).
//
// What this test deliberately does NOT do:
//   - Run `dotnet new install`. That path is covered by
//     `tests/Reactor.IntegrationTests/Packaging/CreateTemplateTests.cs`.
//     Content validation belongs in fast unit tests so a typo lights up
//     in seconds rather than minutes.

using System.Text.Json;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public sealed class TemplateMetadataTests
{
    static readonly string TemplateJsonPath = Path.Combine(
        "tools", "Templates", "templates", "WinUIApp-CSharp", ".template.config", "template.json");

    [Fact]
    public void Identity_is_canonical_brand_namespace()
    {
        var doc = LoadTemplateJson();
        var identity = doc.RootElement.GetProperty("identity").GetString();
        Assert.Equal("Microsoft.UI.Reactor.CSharp", identity);
    }

    [Fact]
    public void GroupIdentity_is_canonical_brand_namespace()
    {
        var doc = LoadTemplateJson();
        var groupIdentity = doc.RootElement.GetProperty("groupIdentity").GetString();
        Assert.Equal("Microsoft.UI.Reactor", groupIdentity);
    }

    [Fact]
    public void File_contains_no_brand_typos()
    {
        // Broad guard: catches any future typo of the same shape in any
        // field of the file. The exact-match assertions above are the
        // load-bearing checks; this is the belt-and-suspenders sweep.
        var (path, text) = ReadTemplateJson();
        Assert.False(
            text.Contains("Micrsoft", StringComparison.Ordinal),
            $"'{path}' contains the typo 'Micrsoft' (missing the second 'o'). " +
            $"Use 'Microsoft' everywhere — the identity/groupIdentity fields are load-bearing for `dotnet new`'s template-cache lookup.");
    }

    [Fact]
    public void ShortName_resolves_to_reactorapp()
    {
        // Anchors the public CLI command-name the agent docs (and the
        // wordpuzzle smoke pattern) depend on. Changing it is a breaking
        // change; this test surfaces an accidental rename.
        var doc = LoadTemplateJson();
        var shortName = doc.RootElement.GetProperty("shortName").GetString();
        Assert.Equal("reactorapp", shortName);
    }

    // ── Framework-version drift guard ──────────────────────────────────────
    //
    // The template bakes the framework version generated apps reference
    // (`MicrosoftUIReactorVersion` → template.json `MSUIReactorVersion`
    // defaultValue, via the csproj BeforePack target). That version used to be
    // a hardcoded csproj default that had to be hand-bumped every release — and
    // it silently drifted (stuck at preview.4 while the framework shipped
    // through preview.11), so published templates generated apps referencing an
    // ancient package (see issue #866). The fix makes the release workflow STAMP
    // the version it's publishing onto the templates pack. This test fails the
    // instant that automation is removed, so the drift can't silently return.

    [Fact]
    public void ReleaseWorkflow_stamps_framework_version_into_templates_pack()
    {
        // The release "Pack Templates" step must pass -p:MicrosoftUIReactorVersion
        // = the resolved release version, so the published ProjectTemplates
        // package references the framework version published in the same run.
        var (path, text) = ReadRepoFile(Path.Combine(".github", "workflows", "release.yml"));
        var step = ExtractYamlStep(text, "Pack Templates");
        Assert.False(step is null,
            $"'{path}' has no 'Pack Templates' step — the release template pack moved or was renamed.");
        Assert.Contains("Microsoft.UI.Reactor.Templates.csproj", step, StringComparison.Ordinal);
        Assert.True(
            step!.Contains("-p:MicrosoftUIReactorVersion=${{ steps.version.outputs.version }}", StringComparison.Ordinal),
            $"The 'Pack Templates' step in '{path}' must pass " +
            "'-p:MicrosoftUIReactorVersion=${{ steps.version.outputs.version }}' so the published " +
            "template references the framework version shipped in the same release run. Without it, " +
            "the baked reference falls back to the csproj default and drifts behind the framework (issue #866).");
    }

    [Fact]
    public void Bootstrap_packs_templates_with_latest_framework_version()
    {
        // The local side of the same fix: bootstrap.ps1 must pack the legacy
        // templates with `--framework-version latest` so the ProjectTemplates
        // nupkg tracks the newest published package instead of a hand-maintained
        // csproj default. Fails the instant that wiring is dropped from either
        // invocation path (installed `mur` or the `dotnet run` fallback).
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        var matches = global::System.Text.RegularExpressions.Regex.Matches(
            normalized, @"pack-local\s+--framework-version\s+latest");
        Assert.True(
            matches.Count >= 2,
            $"'{path}' must invoke `mur pack-local --framework-version latest` on both the installed-`mur` " +
            $"and `dotnet run` fallback paths so the packed templates track the latest published framework " +
            $"(found {matches.Count}, expected >= 2). Dropping it re-introduces the drift fixed for issue #866.");
    }

    // ── Template-pack migration guard ──────────────────────────────────────
    //
    // Reactor's app templates moved into the Windows App SDK `dotnet new` pack
    // (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`, short name `reactor`).
    // bootstrap.ps1 installs *that* pack and deliberately no longer installs the
    // in-repo `Microsoft.UI.Reactor.ProjectTemplates` one. These two tests pin
    // both halves of that contract — the regression they guard is silent
    // (a bootstrap that quietly re-registers `reactorapp` would hand new
    // developers the unpackaged template the docs no longer describe).

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
    public void Bootstrap_does_not_install_the_legacy_reactorapp_template()
    {
        // `mur pack-local` still *builds* Microsoft.UI.Reactor.ProjectTemplates
        // and the release workflow still publishes it — but nothing in bootstrap
        // may hand it to `dotnet new install`, or a fresh clone silently gets the
        // legacy unpackaged `reactorapp` template back.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");

        // Strip comment lines: the step deliberately documents the manual
        // opt-in command, and that mention must not trip this guard.
        var code = string.Join('\n', normalized
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

        Assert.False(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                code, @"new\s+install.*Microsoft\.UI\.Reactor\.ProjectTemplates"),
            $"'{path}' must not `dotnet new install` Microsoft.UI.Reactor.ProjectTemplates — the Reactor " +
            "templates now ship in the Windows App SDK template pack (`dotnet new reactor`). Install the " +
            "legacy pack by hand if you specifically need the unpackaged `reactorapp` shape.");
    }

    // Returns the text of the YAML step whose `name:` equals stepName (the slice
    // from that step's `- name:` line up to the next `- name:` line or EOF), or
    // null if no such step exists. Deliberately simple line scanning — enough to
    // scope a Contains assertion to one step without a YAML dependency.
    static string? ExtractYamlStep(string yaml, string stepName)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("- name:", StringComparison.Ordinal) &&
                t.Substring("- name:".Length).Trim() == stepName)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return null;

        int end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("- name:", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }
        return string.Join("\n", lines[start..end]);
    }

    static JsonDocument LoadTemplateJson()
    {
        var (_, text) = ReadTemplateJson();
        return JsonDocument.Parse(text, new JsonDocumentOptions
        {
            // template.json files in the wild use trailing commas; the
            // template engine tolerates them and so should our test.
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
    }

    static (string path, string text) ReadTemplateJson()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, TemplateJsonPath);
        Assert.True(File.Exists(path), $"Expected '{path}' to exist; template.json moved or removed?");
        return (path, File.ReadAllText(path));
    }

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = Path.Combine(FindRepoRoot(), repoRelativePath);
        Assert.True(File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, File.ReadAllText(path));
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
