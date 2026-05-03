using System;
using System.Diagnostics;
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
