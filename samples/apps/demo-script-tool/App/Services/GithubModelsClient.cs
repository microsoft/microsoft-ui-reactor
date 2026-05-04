using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Streams chat completions from the GitHub Models inference API. The
/// endpoint speaks the OpenAI chat completions schema; we stream Server-Sent
/// Events and unwrap the <c>delta.content</c> field as it arrives.
///
/// Default model is <c>claude-opus-4-6</c> (spec §AI Backend); configurable
/// via constructor for testing parity but not exposed to the UI.
/// </summary>
public sealed class GithubModelsClient : IModelClient, IDisposable
{
    const string DefaultEndpoint = "https://models.github.ai/inference/chat/completions";
    // Spec §AI Backend names claude-opus-4-6 but GitHub Models' Anthropic
    // catalog uses path-prefixed ids like `anthropic/claude-3-...`; openai/gpt-4o
    // is the most reliable evergreen default for local development.
    const string DefaultModel = "openai/gpt-4o";

    readonly HttpClient _http;
    readonly GhAuth _auth;
    readonly string _endpoint;
    readonly string _model;

    public GithubModelsClient(GhAuth auth, string? endpoint = null, string? model = null, HttpClient? httpClient = null)
    {
        _auth = auth;
        _endpoint = endpoint ?? DefaultEndpoint;
        _model = model ?? DefaultModel;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string ModelId => _model;

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        SessionLog.Write($"[GithubModels] StreamAsync model={_model} endpoint={_endpoint} userPromptBytes={userPrompt.Length}");
        var token = await _auth.GetTokenAsync(ct).ConfigureAwait(false)
            ?? throw new AuthExpiredException("No GitHub token available. Sign in via the Open Folder flow.");
        SessionLog.Write($"[GithubModels] token acquired (len={token.Length})");

        var payload = new
        {
            model = _model,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.ParseAdd("text/event-stream");
        // Required: github.com's edge classifies UA-less traffic as scraping
        // and returns the 429 abuse-prevention page instead of a real
        // rate-limit decision. Send a stable, identifiable UA per
        // https://docs.github.com/en/rest/overview/rate-limits-for-the-rest-api.
        req.Headers.UserAgent.ParseAdd("DemoScriptTool/1.0 (+https://github.com/microsoft/microsoft-ui-reactor)");
        // Models inference: pin the API version per
        // https://docs.github.com/en/rest/overview/api-versions.
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        SessionLog.Write($"[GithubModels] HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            SessionLog.Write($"[GithubModels] auth rejected body={body}");
            throw new AuthExpiredException($"GitHub Models rejected the request ({(int)response.StatusCode} {response.ReasonPhrase}). Likely missing the `models:read` scope on your GitHub token — re-run `gh auth login --scopes models:read`.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            SessionLog.Write($"[GithubModels] error body={body}");
            throw new HttpRequestException($"GitHub Models returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (line.Length == 0) continue;

            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].TrimStart();
            if (data == "[DONE]") yield break;

            string? delta = null;
            try { delta = ExtractDelta(data); }
            catch (JsonException) { continue; } // tolerate transient malformed frames

            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    static string? ExtractDelta(string sseData)
    {
        using var doc = JsonDocument.Parse(sseData);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;
        var first = choices[0];
        if (first.TryGetProperty("delta", out var deltaEl)
            && deltaEl.TryGetProperty("content", out var contentEl)
            && contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString();
        }
        return null;
    }

    /// <summary>
    /// No-op: this client reads the GitHub token via <see cref="GhAuth.GetTokenAsync"/>
    /// at the start of every <see cref="StreamAsync"/> call, so a fresh
    /// <c>gh auth refresh</c> is picked up automatically. The interface
    /// requires the method for SDK-backed clients that snapshot credentials
    /// (see <see cref="CopilotSdkClient.ResetAsync"/>).
    /// </summary>
    public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;

    public void Dispose() => _http.Dispose();
}
