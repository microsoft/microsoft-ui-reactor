using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GitHub.Copilot;

namespace WidgetCreator.Services;

/// <summary>Raised when Copilot reports an auth/authorization failure mid-stream.</summary>
public sealed class AuthExpiredException(string message) : Exception(message);

/// <summary>
/// <see cref="IModelClient"/> backed by the GitHub Copilot SDK
/// (<c>GitHub.Copilot.SDK</c>). The SDK proxies to the bundled Copilot CLI,
/// which rides whichever account <c>gh auth</c> considers active — auth,
/// headers, retries, and model selection are handled by the CLI. No explicit
/// token: <c>UseLoggedInUser</c> defaults on, so it inherits the machine login.
///
/// <para>A <see cref="Conversation"/> wraps one Copilot session and supports
/// multiple streaming turns, which is what powers the build-and-fix loop:
/// generate → build → send errors back to the same agent → regenerate.</para>
/// </summary>
public sealed class CopilotSdkClient : IModelClient, IAsyncDisposable
{
    const string DefaultModel = "claude-sonnet-4.5";

    readonly string _model;
    readonly SemaphoreSlim _initLock = new(1, 1);
    CopilotClient? _client;

    public CopilotSdkClient(string? model = null) => _model = model ?? DefaultModel;

    public string ModelId => _model;

    async Task<CopilotClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null) return _client;
            var options = new CopilotClientOptions();
            // The SDK's default stdio transport looks ONLY for a CLI bundled next to
            // the app (runtimes/<rid>/native/copilot.exe) and does not consult
            // COPILOT_CLI_PATH. On builds where the CLI could not be bundled at build
            // time (offline / restricted-network / authenticated-feed environments —
            // see build/Reactor.CopilotCli.targets), resolve one at run time and pass
            // it explicitly so generation still works.
            var cliPath = ResolveCliPath();
            if (cliPath is not null)
            {
                SessionLog.Write($"[CopilotSdk] no bundled CLI; using resolved CLI at {cliPath}");
                options.Connection = RuntimeConnection.ForStdio(path: cliPath);
            }
            var client = new CopilotClient(options);
            SessionLog.Write($"[CopilotSdk] starting CLI server (model={_model})");
            await client.StartAsync().ConfigureAwait(false);
            SessionLog.Write("[CopilotSdk] CLI server ready");
            _client = client;
            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Resolves a Copilot CLI to spawn when the SDK's bundled binary is absent.
    /// Returns <c>null</c> when the bundled CLI is present (let the SDK use it) or
    /// when nothing could be found (let the SDK throw its descriptive error).
    /// Order: bundled next to the app → <c>COPILOT_CLI_PATH</c> → a locally
    /// installed Copilot CLI in its well-known per-user location. We deliberately
    /// do NOT probe <c>PATH</c> for <c>copilot.exe</c> — that would let any
    /// unrelated (or malicious) binary earlier on <c>PATH</c> be launched. To use
    /// a CLI in a non-standard location, set <c>COPILOT_CLI_PATH</c> explicitly.
    /// </summary>
    static string? ResolveCliPath()
    {
        var rid = "win-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", "copilot.exe");
        if (File.Exists(bundled))
            return null; // SDK's default resolution works.

        var env = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        foreach (var candidate in EnumerateInstalledCliPaths())
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    static IEnumerable<string> EnumerateInstalledCliPaths()
    {
        // Standalone GitHub Copilot CLI install (winget / gh), in its well-known
        // per-user location. Intentionally a fixed, trusted path — never a PATH
        // scan, so an unrelated copilot.exe on PATH can't be picked up and run.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "GitHub CLI", "copilot", "copilot.exe");
    }

    public async Task<IModelConversation> StartConversationAsync(string systemPrompt, CancellationToken ct)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        var session = await client.CreateSessionAsync(BuildConfig(systemPrompt), ct).ConfigureAwait(false);
        SessionLog.Write($"[CopilotSdk] session created id={session.SessionId}");
        return new Conversation(session);
    }

    public async Task<IModelConversation> ResumeConversationAsync(string sessionId, string systemPrompt, CancellationToken ct)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                var resumed = await client.ResumeSessionAsync(sessionId, BuildResumeConfig(systemPrompt), ct).ConfigureAwait(false);
                SessionLog.Write($"[CopilotSdk] resumed session id={resumed.SessionId}");
                return new Conversation(resumed);
            }
            catch (Exception ex)
            {
                // Session may have been pruned (e.g. days later) — fall back to a
                // fresh session. The caller includes the current source in its
                // fix prompt, so correctness doesn't depend on restored history.
                SessionLog.Write($"[CopilotSdk] resume '{sessionId}' failed ({ex.Message}); starting fresh session");
            }
        }
        return await StartConversationAsync(systemPrompt, ct).ConfigureAwait(false);
    }

    SessionConfig BuildConfig(string systemPrompt) => new()
    {
        Model = _model,
        ClientName = "widget-creator",
        OnPermissionRequest = AgentPermissionPolicy.DenyAll,
        Streaming = true,
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Replace,
            Content = systemPrompt,
        },
    };

    ResumeSessionConfig BuildResumeConfig(string systemPrompt) => new()
    {
        Model = _model,
        ClientName = "widget-creator",
        OnPermissionRequest = AgentPermissionPolicy.DenyAll,
        Streaming = true,
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Replace,
            Content = systemPrompt,
        },
    };

    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            try { await client.StopAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch { /* swallow */ }
            _client = null;
        }
        _initLock.Dispose();
    }

    /// <summary>One Copilot session; each <see cref="SendAsync"/> is a turn.</summary>
    sealed class Conversation(CopilotSession session) : IModelConversation
    {
        readonly CopilotSession _session = session;

        public string SessionId => _session.SessionId;

        public async IAsyncEnumerable<string> SendAsync(
            string userPrompt,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

            Exception? terminalError = null;
            bool sawDeltas = false;
            bool turnEnded = false;

            using var subscription = _session.On<SessionEvent>(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        if (delta.Data.DeltaContent.Length > 0)
                        {
                            sawDeltas = true;
                            channel.Writer.TryWrite(delta.Data.DeltaContent);
                        }
                        break;

                    case AssistantMessageEvent final:
                        if (!sawDeltas && final.Data.Content.Length > 0)
                            channel.Writer.TryWrite(final.Data.Content);
                        break;

                    case AssistantTurnEndEvent:
                        turnEnded = true;
                        break;

                    case SessionIdleEvent:
                        if (turnEnded)
                            channel.Writer.TryComplete();
                        break;

                    case SessionErrorEvent err:
                        var msg = $"Copilot {err.Data.ErrorType}: {err.Data.Message}";
                        SessionLog.Write($"[CopilotSdk] {msg}");
                        terminalError = err.Data.ErrorType switch
                        {
                            "authentication" or "authorization" => new AuthExpiredException(msg),
                            _ => new Exception(msg),
                        };
                        channel.Writer.TryComplete(terminalError);
                        break;
                }
            });

            try
            {
                await _session.SendAsync(new MessageOptions { Prompt = userPrompt }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
                throw;
            }

            await foreach (var chunk in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }

            if (terminalError is not null)
                throw terminalError;
        }

        public async ValueTask DisposeAsync()
        {
            try { await _session.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }
    }
}
