using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Figma;

/// <summary>
/// Resolves the Figma API key from multiple sources in priority order:
/// 1. <c>FIGMA_API_KEY</c> environment variable
/// 2. Copilot CLI MCP config (<c>~/.copilot/mcp-config.json</c>)
/// 3. VS Code workspace MCP config (<c>.vscode/mcp.json</c>)
/// </summary>
internal static class FigmaApiKeyResolver
{
    /// <summary>
    /// Returns the Figma API key or <c>null</c> if none found. When found from
    /// an MCP config, logs the source to stderr so the user knows where it came from.
    /// </summary>
    public static string? Resolve()
    {
        // 1. Environment variable (highest priority)
        var envKey = Environment.GetEnvironmentVariable("FIGMA_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            return envKey;

        // 2. Copilot CLI MCP config
        var copilotConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "mcp-config.json");
        var key = TryExtractFromMcpConfigFile(copilotConfig);
        if (key != null)
        {
            Console.Error.WriteLine($"[mur figma] Using API key from {copilotConfig}");
            return key;
        }

        // 3. VS Code workspace MCP config (relative to cwd)
        var vscodeConfig = Path.Combine(Directory.GetCurrentDirectory(), ".vscode", "mcp.json");
        key = TryExtractFromMcpConfigFile(vscodeConfig);
        if (key != null)
        {
            Console.Error.WriteLine($"[mur figma] Using API key from {vscodeConfig}");
            return key;
        }

        return null;
    }

    /// <summary>
    /// Reads an MCP config JSON file and extracts a Figma API key from any server
    /// entry whose args contain <c>--figma-api-key=...</c> or whose env block
    /// contains <c>FIGMA_API_KEY</c>.
    /// </summary>
    internal static string? TryExtractFromMcpConfigFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = doc.RootElement;

            // Handle top-level object variants: { "mcpServers": {...} }, { "servers": {...} }, or flat { "figma": {...} }
            JsonElement servers;
            if (root.TryGetProperty("mcpServers", out var ms))
                servers = ms;
            else if (root.TryGetProperty("servers", out var s))
                servers = s;
            else
                servers = root;

            foreach (var server in servers.EnumerateObject())
            {
                // Check args array for --figma-api-key=VALUE
                if (server.Value.TryGetProperty("args", out var args) &&
                    args.ValueKind == JsonValueKind.Array)
                {
                    foreach (var arg in args.EnumerateArray())
                    {
                        var argStr = arg.GetString();
                        if (argStr == null) continue;

                        var match = Regex.Match(argStr, @"--figma-api-key[=:](.+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                            return match.Groups[1].Value.Trim();
                    }
                }

                // Check env block for FIGMA_API_KEY
                if (server.Value.TryGetProperty("env", out var env) &&
                    env.ValueKind == JsonValueKind.Object &&
                    env.TryGetProperty("FIGMA_API_KEY", out var envKey))
                {
                    var val = envKey.GetString();
                    if (!string.IsNullOrEmpty(val))
                        return val;
                }
            }
        }
        catch
        {
            // Malformed config — silently skip
        }

        return null;
    }
}
