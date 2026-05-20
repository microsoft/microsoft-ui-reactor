#nullable enable

using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class DataLoader
{
    public static ScenarioCatalogue Load()
    {
        var assembly = typeof(DataLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream("scenarios.json")
            ?? throw new InvalidOperationException("Embedded scenarios.json not found.");
        return JsonSerializer.Deserialize(stream, FindJsonContext.Default.ScenarioCatalogue)
            ?? throw new InvalidOperationException("Failed to deserialize scenarios.json.");
    }
}
