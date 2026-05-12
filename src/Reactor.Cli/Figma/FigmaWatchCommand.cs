namespace Microsoft.UI.Reactor.Cli.Figma;

/// <summary>
/// <c>mur figma watch &lt;url&gt; [--interval N]</c>
///
/// Polls the Figma REST API for changes to a file and prints a notification
/// to stdout when the file's <c>lastModified</c> timestamp advances. The
/// agent reads this output and re-fetches the design via
/// <c>figma-get_figma_data</c> MCP tool.
///
/// No open ports. No bridge. Auth via FIGMA_API_KEY env var (same token
/// the Figma MCP server uses).
/// </summary>
internal static class FigmaWatchCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        // Parse args: mur figma watch <url> [--interval N]
        string? url = null;
        int intervalSeconds = 10;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--interval" or "-i" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out intervalSeconds) || intervalSeconds < 1)
                {
                    Console.Error.WriteLine("Error: --interval must be a positive integer (seconds).");
                    return 1;
                }
            }
            else if (!args[i].StartsWith('-') && url == null)
            {
                url = args[i];
            }
        }

        if (url == null)
        {
            Console.Error.WriteLine("Usage: mur figma watch <figma-url> [--interval <seconds>]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Polls a Figma file for changes and prints a notification when");
            Console.Error.WriteLine("the design is modified. The agent then re-fetches via Figma MCP.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --interval, -i   Poll interval in seconds (default: 10)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Environment / Config (checked in order):");
            Console.Error.WriteLine("  FIGMA_API_KEY              Environment variable");
            Console.Error.WriteLine("  ~/.copilot/mcp-config.json --figma-api-key in Figma MCP server args");
            Console.Error.WriteLine("  .vscode/mcp.json           --figma-api-key in Figma MCP server args");
            return 1;
        }

        var apiKey = FigmaApiKeyResolver.Resolve();
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Error: Figma API key not found.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Searched (in order):");
            Console.Error.WriteLine("  1. FIGMA_API_KEY environment variable");
            Console.Error.WriteLine("  2. ~/.copilot/mcp-config.json (--figma-api-key in args)");
            Console.Error.WriteLine("  3. .vscode/mcp.json (--figma-api-key in args)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Create a Figma personal access token at:");
            Console.Error.WriteLine("  https://help.figma.com/hc/en-us/articles/8085703771159");
            return 1;
        }

        var parsed = FigmaClient.ParseUrl(url);
        if (parsed == null)
        {
            Console.Error.WriteLine($"Error: could not parse Figma URL: {url}");
            Console.Error.WriteLine("Expected format: https://www.figma.com/design/<key>/...");
            return 1;
        }

        using var client = new FigmaClient(apiKey);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Initial fetch to establish baseline (retry up to 3 times for rate limits)
        FigmaFileInfo? info = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            info = await client.GetFileInfoAsync(parsed.FileKey, cts.Token);
            if (info != null) break;
            if (attempt < 3)
            {
                var delay = attempt * 15;
                Console.Error.WriteLine($"[mur figma watch] Retrying in {delay}s (attempt {attempt}/3)...");
                try { await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token); }
                catch (OperationCanceledException) { return 1; }
            }
        }
        if (info == null)
        {
            Console.Error.WriteLine("Error: could not fetch file info after 3 attempts.");
            Console.Error.WriteLine("If rate-limited (429), wait a minute and try again.");
            return 1;
        }

        var lastModified = info.LastModified;

        Console.Error.WriteLine($"[mur figma watch] Watching: {SanitizeForStderr(info.FileName)}");
        Console.Error.WriteLine($"[mur figma watch] File key: {parsed.FileKey}");
        if (parsed.NodeId != null)
            Console.Error.WriteLine($"[mur figma watch] Node: {parsed.NodeId}");
        Console.Error.WriteLine($"[mur figma watch] Interval: {intervalSeconds}s");
        Console.Error.WriteLine($"[mur figma watch] Baseline: {lastModified:yyyy-MM-dd HH:mm:ss}");
        Console.Error.WriteLine($"[mur figma watch] Press Ctrl+C to stop.");
        Console.Error.WriteLine();

        // Emit initial ready event (stdout — machine-readable for agents)
        EmitEvent("ready", parsed, info);

        // Poll loop
        int consecutiveErrors = 0;
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cts.Token);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                var updated = await client.GetFileInfoAsync(parsed.FileKey, cts.Token);
                if (updated == null)
                {
                    consecutiveErrors++;
                    if (consecutiveErrors >= 5)
                    {
                        Console.Error.WriteLine("[mur figma watch] Too many consecutive errors. Stopping.");
                        return 1;
                    }

                    // Back off: use Retry-After from the API, or exponential fallback
                    var backoff = client.RetryAfterSeconds
                        ?? Math.Min(120, intervalSeconds * (1 << consecutiveErrors));
                    Console.Error.WriteLine($"[mur figma watch] Failed to fetch — retrying in {backoff}s...");
                    try { await Task.Delay(TimeSpan.FromSeconds(backoff), cts.Token); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                consecutiveErrors = 0;

                if (updated.LastModified > lastModified)
                {
                    lastModified = updated.LastModified;
                    Console.Error.WriteLine($"[mur figma watch] Change detected at {lastModified:HH:mm:ss}");

                    // Emit change event to stdout for agent consumption
                    EmitEvent("changed", parsed, updated);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                consecutiveErrors++;
                Console.Error.WriteLine($"[mur figma watch] Error: {ex.Message}");
                if (consecutiveErrors >= 5)
                {
                    Console.Error.WriteLine("[mur figma watch] Too many errors. Stopping.");
                    return 1;
                }
            }
        }

        Console.Error.WriteLine("[mur figma watch] Stopped.");
        return 0;
    }

    /// <summary>
    /// Emits a structured JSON event to stdout. Status messages go to stderr
    /// so agents can cleanly parse stdout as a stream of events.
    /// </summary>
    private static void EmitEvent(string type, FigmaUrlParts parts, FigmaFileInfo info)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            @event = type,
            fileKey = parts.FileKey,
            nodeId = parts.ApiNodeId,
            fileName = info.FileName,
            lastModified = info.LastModified.ToString("o"),
            version = info.Version,
            figmaUrl = $"https://www.figma.com/design/{parts.FileKey}" +
                (parts.NodeId != null ? $"?node-id={parts.NodeId}" : ""),
        });
        Console.WriteLine(json);
    }

    /// <summary>
    /// Strips control characters (U+0000–U+001F except tab/newline) from a
    /// string before writing to stderr. Prevents ANSI escape injection from
    /// attacker-controlled Figma file/frame names.
    /// </summary>
    internal static string SanitizeForStderr(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c < '\u0020' && c != '\t' && c != '\n' && c != '\r')
                continue; // strip control characters
            sb.Append(c);
        }
        return sb.ToString();
    }
}
