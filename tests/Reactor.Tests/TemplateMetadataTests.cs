// Repository-content validation for project-template metadata.
//
// The bug this test was added against:
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
