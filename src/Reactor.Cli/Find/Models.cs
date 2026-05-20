#nullable enable

using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Cli.Find;

public record Scenario(
    string Id,
    string Category,
    string Title,
    string Intent,
    string[] Tags,
    string[] FactoryAnchors,
    string? NotesKey,
    string[] RelatedIds,
    string Priority,
    string Code,
    string RawCode
);

public record ScenarioCatalogue(
    Scenario[] Scenarios,
    string GeneratedAt
);

public record SearchResult(
    Scenario Scenario,
    double Score
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScenarioCatalogue))]
[JsonSerializable(typeof(Scenario))]
[JsonSerializable(typeof(Scenario[]))]
internal partial class FindJsonContext : JsonSerializerContext
{
}