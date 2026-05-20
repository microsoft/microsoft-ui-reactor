using System.Text.Json;
using System.Text.Json.Serialization;

try
{
    return Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Run(string[] args)
{
    var repoRoot = ResolveRepoRoot(args);
    var scenariosRoot = Path.Combine(repoRoot, "samples", "scenarios");
    var scenarioPaths = Directory.Exists(scenariosRoot)
        ? ScenarioWalker.FindScenarios(scenariosRoot)
        : [];

    if (scenarioPaths.Length == 0)
    {
        Console.WriteLine($"warning: no scenarios found under {scenariosRoot}");
        return 0;
    }

    var scenarios = new List<Scenario>(scenarioPaths.Length);

    foreach (var scenarioJsonPath in scenarioPaths)
    {
        var metadata = LoadMetadata(scenarioJsonPath);
        var scenarioDirectory = Path.GetDirectoryName(scenarioJsonPath)!;
        var scenarioCodePath = Path.Combine(scenarioDirectory, "Scenario.cs");

        if (!File.Exists(scenarioCodePath))
        {
            throw new InvalidOperationException($"missing Scenario.cs next to {scenarioJsonPath}");
        }

        var rawCode = File.ReadAllText(scenarioCodePath);
        var code = StripMetadataHeaders(rawCode);

        scenarios.Add(new Scenario(
            metadata.Id!,
            metadata.Category!,
            metadata.Title!,
            metadata.Intent!,
            metadata.Tags!,
            metadata.FactoryAnchors!,
            metadata.NotesKey,
            metadata.RelatedIds ?? [],
            metadata.Priority!,
            code,
            rawCode));
    }

    var outputPath = Path.Combine(repoRoot, "samples", "scenarios", "_generated", "scenarios.json");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

    var catalogue = new ScenarioCatalogue(
        [.. scenarios],
        DateTimeOffset.UtcNow.ToString("O"));

    var json = JsonSerializer.Serialize(catalogue, SampleCatalogueJsonContext.Default.ScenarioCatalogue);
    File.WriteAllText(outputPath, json);

    Console.WriteLine($"Extracted {scenarios.Count} scenarios → {outputPath}");
    return 0;
}

static string ResolveRepoRoot(string[] args)
{
    if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
    {
        return Path.GetFullPath(args[0]);
    }

    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Reactor.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repo root (expected Reactor.slnx).");
}

static ScenarioMetadata LoadMetadata(string scenarioJsonPath)
{
    ScenarioMetadata? metadata;

    try
    {
        metadata = JsonSerializer.Deserialize(
            File.ReadAllText(scenarioJsonPath),
            SampleCatalogueJsonContext.Default.ScenarioMetadata);
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Invalid JSON in {scenarioJsonPath}: {ex.Message}", ex);
    }

    if (metadata is null)
    {
        throw new InvalidOperationException($"Empty scenario metadata in {scenarioJsonPath}.");
    }

    var missingFields = new List<string>();

    if (string.IsNullOrWhiteSpace(metadata.Id)) missingFields.Add("id");
    if (string.IsNullOrWhiteSpace(metadata.Category)) missingFields.Add("category");
    if (string.IsNullOrWhiteSpace(metadata.Title)) missingFields.Add("title");
    if (string.IsNullOrWhiteSpace(metadata.Intent)) missingFields.Add("intent");
    if (metadata.Tags is null) missingFields.Add("tags");
    if (metadata.FactoryAnchors is null) missingFields.Add("factoryAnchors");
    if (string.IsNullOrWhiteSpace(metadata.Priority)) missingFields.Add("priority");

    if (missingFields.Count > 0)
    {
        throw new InvalidOperationException(
            $"Missing required field(s) in {scenarioJsonPath}: {string.Join(", ", missingFields)}");
    }

    return metadata with
    {
        Tags = metadata.Tags ?? [],
        FactoryAnchors = metadata.FactoryAnchors ?? [],
        RelatedIds = metadata.RelatedIds ?? []
    };
}

static string StripMetadataHeaders(string rawCode)
{
    var lines = rawCode.Replace("\r\n", "\n").Split('\n');
    var output = new List<string>(lines.Length);
    var inLeadingHeader = true;

    foreach (var line in lines)
    {
        var trimmed = line.TrimStart();

        if (inLeadingHeader)
        {
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("#:", StringComparison.Ordinal) ||
                trimmed.Length == 0)
            {
                continue;
            }

            inLeadingHeader = false;
        }

        if (trimmed.StartsWith("#:", StringComparison.Ordinal))
        {
            continue;
        }

        output.Add(line);
    }

    while (output.Count > 0 && string.IsNullOrWhiteSpace(output[0]))
    {
        output.RemoveAt(0);
    }

    return string.Join(Environment.NewLine, output);
}

internal sealed record Scenario(
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
    string RawCode);

internal sealed record ScenarioCatalogue(
    Scenario[] Scenarios,
    string GeneratedAt);

internal sealed record ScenarioMetadata(
    string? Id,
    string? Category,
    string? Title,
    string? Intent,
    string[]? Tags,
    string[]? FactoryAnchors,
    string? NotesKey,
    string[]? RelatedIds,
    string? Priority);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ScenarioCatalogue))]
[JsonSerializable(typeof(Scenario))]
[JsonSerializable(typeof(Scenario[]))]
[JsonSerializable(typeof(ScenarioMetadata))]
internal partial class SampleCatalogueJsonContext : JsonSerializerContext
{
}
