using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Figma;

/// <summary>
/// Minimal Figma REST API client for polling <c>lastModified</c> timestamps.
/// Does NOT fetch full design trees — the agent uses the Figma MCP server
/// (<c>figma-developer-mcp</c>) for that. This client only checks whether
/// a file has changed since the last poll.
/// </summary>
internal sealed class FigmaClient : IDisposable
{
    private readonly HttpClient _http;

    public FigmaClient(string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.figma.com/") };
        _http.DefaultRequestHeaders.Add("X-Figma-Token", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Returns the <c>lastModified</c> timestamp and file name for a Figma file,
    /// using a lightweight metadata-only request (no document tree).
    /// On a 429, <see cref="RetryAfterSeconds"/> is set from the Retry-After header.
    /// </summary>
    public int? RetryAfterSeconds { get; private set; }

    public async Task<FigmaFileInfo?> GetFileInfoAsync(string fileKey, CancellationToken ct = default)
    {
        RetryAfterSeconds = null;

        // depth=1 returns only top-level metadata without traversing the full tree
        var response = await _http.GetAsync($"v1/files/{fileKey}?depth=1", ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            if (status == 429)
            {
                // Respect Retry-After header if present, otherwise default to 60s
                RetryAfterSeconds = 60;
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    RetryAfterSeconds = Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
                else if (response.Headers.RetryAfter?.Date is { } date)
                    RetryAfterSeconds = Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
                Console.Error.WriteLine($"[mur figma] Rate limited (429). Waiting {RetryAfterSeconds}s before next poll.");
            }
            else if (status == 403)
                Console.Error.WriteLine("[mur figma] Figma API key is invalid or expired (403).");
            else
                Console.Error.WriteLine($"[mur figma] Figma API returned HTTP {status}.");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var lastModified = root.TryGetProperty("lastModified", out var lm)
            ? lm.GetString() : null;
        var name = root.TryGetProperty("name", out var n)
            ? n.GetString() : null;
        var version = root.TryGetProperty("version", out var v)
            ? v.GetString() : null;

        if (lastModified == null) return null;

        return new FigmaFileInfo(
            LastModified: DateTimeOffset.Parse(lastModified),
            FileName: name ?? "unknown",
            Version: version ?? "");
    }

    /// <summary>
    /// Parses a Figma design URL into its constituent parts.
    /// Handles both <c>/design/</c> and <c>/file/</c> URL formats.
    /// </summary>
    public static FigmaUrlParts? ParseUrl(string url)
    {
        // Match: https://www.figma.com/(design|file)/<fileKey>/<name>?node-id=<nodeId>&...
        var match = Regex.Match(url,
            @"figma\.com/(?:design|file)/([a-zA-Z0-9]+)(?:/[^?]*)?(?:\?.*node-id=([0-9]+-[0-9]+))?",
            RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        var fileKey = match.Groups[1].Value;
        var nodeId = match.Groups[2].Success ? match.Groups[2].Value : null;

        // Figma URLs use "123-456" in query params but the API uses "123:456"
        var apiNodeId = nodeId?.Replace('-', ':');

        return new FigmaUrlParts(fileKey, nodeId, apiNodeId);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Lightweight file metadata from the Figma REST API.</summary>
internal sealed record FigmaFileInfo(
    DateTimeOffset LastModified,
    string FileName,
    string Version);

/// <summary>Parsed components of a Figma design URL.</summary>
internal sealed record FigmaUrlParts(
    string FileKey,
    string? NodeId,
    string? ApiNodeId);
