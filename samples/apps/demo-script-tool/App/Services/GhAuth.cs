using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Wraps the GitHub CLI for token acquisition. We do not vendor the OAuth
/// flow ourselves — `gh auth login` is canonical, runs interactively in a
/// console window, and persists its token to the user profile so subsequent
/// launches reuse it (spec §GitHub auth flow).
/// </summary>
public sealed class GhAuth
{
    int _retryCount;

    public event Action<string>? StatusChanged;

    /// <summary>
    /// Snapshot of the auth state — which gh account, what token source, what
    /// scopes — for the diagnostic surface. Populated lazily by
    /// <see cref="GetIdentityAsync"/> so we don't hit github.com on every
    /// generation.
    /// </summary>
    public sealed record AuthIdentity(string Source, string? Login, string[] Scopes, string? Error)
    {
        public override string ToString() =>
            Error is not null
                ? $"[{Source}] error: {Error}"
                : $"[{Source}] login={Login ?? "<unknown>"} scopes=[{string.Join(", ", Scopes)}]";
    }

    /// <summary>
    /// Resolve a token, then call GET https://api.github.com/user with it to
    /// confirm which GitHub login the token belongs to and which OAuth scopes
    /// it carries. Useful when `gh auth token` returns a different account's
    /// token than the user expected (multi-account gh setups).
    /// </summary>
    public async Task<AuthIdentity> GetIdentityAsync(CancellationToken ct)
    {
        string source;
        string? token;

        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
        {
            source = "GITHUB_TOKEN env";
            token = envToken;
        }
        else
        {
            try
            {
                var cached = await RunGhAsync("auth token", captureStdout: true, ct).ConfigureAwait(false);
                if (cached.ExitCode != 0 || string.IsNullOrWhiteSpace(cached.Stdout))
                    return new AuthIdentity("gh auth token", null, Array.Empty<string>(),
                        $"`gh auth token` exited {cached.ExitCode}: {cached.Stderr.Trim()}");
                source = "gh auth token";
                token = cached.Stdout.Trim();
            }
            catch (Exception ex)
            {
                return new AuthIdentity("gh auth token", null, Array.Empty<string>(), ex.Message);
            }
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd("DemoScriptTool/1.0");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new AuthIdentity(source, null, Array.Empty<string>(),
                    $"GET /user → {(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");

            using var doc = JsonDocument.Parse(body);
            var login = doc.RootElement.TryGetProperty("login", out var l) ? l.GetString() : null;
            var scopes = Array.Empty<string>();
            if (resp.Headers.TryGetValues("x-oauth-scopes", out var headerVals))
                scopes = string.Join(",", headerVals).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new AuthIdentity(source, login, scopes, null);
        }
        catch (Exception ex)
        {
            return new AuthIdentity(source, null, Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>
    /// Read the current GitHub token from the environment or the gh CLI cache.
    /// Returns <see langword="null"/> when no token can be obtained and the
    /// caller should surface auth UI to the user.
    /// </summary>
    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
            return envToken;

        try
        {
            var cached = await RunGhAsync("auth token", captureStdout: true, ct).ConfigureAwait(false);
            if (cached.ExitCode == 0 && !string.IsNullOrWhiteSpace(cached.Stdout))
                return cached.Stdout.Trim();
        }
        catch (System.IO.FileNotFoundException ex)
        {
            throw new AuthUnavailableException(
                "The GitHub CLI ('gh') was not found on PATH. Install from https://cli.github.com/ and re-launch.")
                { Source = ex.Message };
        }
        catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.HResult == 0x80004005 || ex.NativeErrorCode == 2)
        {
            throw new AuthUnavailableException(
                "The GitHub CLI ('gh') was not found on PATH. Install from https://cli.github.com/ and re-launch.");
        }

        return null;
    }

    /// <summary>
    /// Spawn an interactive <c>gh auth login</c> session in a console window
    /// and return the resulting token. Retries are capped at one per process
    /// (spec §3.1) so we cannot loop on a misconfigured machine.
    /// </summary>
    public async Task<string?> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        var existing = await GetTokenAsync(ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        if (_retryCount >= 1)
            throw new AuthUnavailableException("GitHub authentication failed twice. Please run `gh auth login` manually and retry.");
        _retryCount++;

        StatusChanged?.Invoke("Authenticating…");
        try
        {
            var psi = new ProcessStartInfo("gh", "auth login --web --scopes \"models:read\"")
            {
                UseShellExecute = true, // launch a console window the user can interact with
            };
            using var p = Process.Start(psi)
                ?? throw new AuthUnavailableException("Failed to start the gh auth login process.");
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            StatusChanged?.Invoke("");
        }

        return await GetTokenAsync(ct).ConfigureAwait(false);
    }

    static async Task<(int ExitCode, string Stdout, string Stderr)> RunGhAsync(string args, bool captureStdout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("gh", args)
        {
            RedirectStandardOutput = captureStdout,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new System.IO.FileNotFoundException("gh", "gh");

        var stdoutTask = captureStdout ? p.StandardOutput.ReadToEndAsync(ct) : Task.FromResult("");
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        return (p.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }
}
