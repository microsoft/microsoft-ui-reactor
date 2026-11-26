using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// The API reference groups in <c>docs/_website/nav.json</c> are pasted from
/// <c>compile --reference-nav-out</c>. This test generates that fragment from the
/// real map and the built assembly and compares it with the committed file, so a
/// new category, section or hook group cannot leave the sidebar behind.
/// </summary>
/// <remarks>
/// Only the groups whose <c>dir</c> is <c>reference</c> or starts with
/// <c>reference/</c> are compared, so the rest of <c>nav.json</c> can be
/// restructured freely. The test needs <c>src/Reactor</c> built. In CI it runs
/// after the docs compile has built it, and there a missing build is a failure,
/// not a skip.
/// </remarks>
public sealed class ReferenceNavTests
{
    [Fact]
    public void Nav_json_reference_groups_match_the_generated_fragment()
    {
        var root = TestRepo.Root;
        var choice = CompileCommand.FindReactorXml(root);
        var dll = choice is null ? null : Path.ChangeExtension(choice.Value.Path, ".dll");
        if (choice is null || !File.Exists(dll))
        {
            const string why = "This check needs Microsoft.UI.Reactor.xml and .dll under src/Reactor/bin. " +
                "Run `dotnet build src/Reactor` (or the docs compile) first.";
            Assert.False(RunningInCi, why);
            Assert.Skip(why);
        }

        var map = ReferenceMap.Load(Path.Join(root, "docs", "_pipeline", "reference-map.yaml"));
        var result = new ReferenceGenerator().Generate(choice.Value.Path, map, dllPath: dll);
        var generated = JsonNode.Parse(result.NavFragment)!.AsArray().Select(g => g!).ToList();
        Assert.NotEmpty(generated);
        Assert.All(generated, g => Assert.True(IsReferenceGroup(g), $"Fragment group outside reference/: {g.ToJsonString()}"));

        var navPath = Path.Join(root, "docs", "_website", "nav.json");
        var committed = JsonNode.Parse(File.ReadAllText(navPath))!["groups"]!.AsArray()
            .Select(g => g!).Where(IsReferenceGroup).ToList();

        var expected = generated.Select(Canonical).ToList();
        var actual = committed.Select(Canonical).ToList();
        var first = Enumerable.Range(0, Math.Max(expected.Count, actual.Count))
            .FirstOrDefault(i => i >= expected.Count || i >= actual.Count || expected[i] != actual[i], -1);
        if (first < 0)
        {
            return;
        }

        Assert.Fail(
            $"docs/_website/nav.json is out of date with the generated API reference: reference group #{first + 1} differs.\n" +
            $"  generated: {(first < expected.Count ? expected[first] : "(none)")}\n" +
            $"  nav.json:  {(first < actual.Count ? actual[first] : "(none)")}\n" +
            "Run `dotnet run --project tools/Reactor.DocPipeline -- compile --reference-only --reference-nav-out <file>` " +
            "and replace the reference groups in nav.json with the file's contents.");
    }

    private static bool RunningInCi =>
        string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsReferenceGroup(JsonNode group)
    {
        var dir = (string?)group["dir"];
        return dir is not null && (dir == "reference" || dir.StartsWith("reference/", StringComparison.Ordinal));
    }

    /// <summary>Compact JSON with object keys sorted, so only content differences count.</summary>
    private static string Canonical(JsonNode node) => Sort(node)?.ToJsonString() ?? "null";

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
