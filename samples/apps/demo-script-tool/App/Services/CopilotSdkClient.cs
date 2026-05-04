using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;

namespace DemoScriptTool.App.Services;

/// <summary>
/// <see cref="IModelClient"/> implementation backed by the GitHub Copilot
/// SDK (<c>GitHub.Copilot.SDK</c>). The SDK proxies to the bundled Copilot CLI,
/// which speaks Copilot's first-party agent protocol — auth, headers, retries,
/// rate-limit attribution, and model selection are all handled by the CLI.
///
/// <para>
/// We use it as a plain text completion engine: one session per generation
/// call, no tools (the demo-script-tool has no work for the agent to do
/// outside of returning the assistant's response), <see cref="SystemMessageMode.Replace"/>
/// to swap our spec 035 envelope-emitting system prompt in for the CLI's
/// default agent prompt, and streaming on so the UI can render token deltas.
/// </para>
///
/// <para>
/// Replaces the earlier <c>GithubModelsClient</c> which made raw HTTP calls
/// to <c>https://models.github.ai/inference</c> — that path triggered
/// github.com's anti-scraping 429 on UA-less traffic and didn't ride the
/// user's Copilot subscription.
/// </para>
/// </summary>
public sealed class CopilotSdkClient : IModelClient, IAsyncDisposable
{
    /// <summary>
    /// Default Copilot model. <c>claude-sonnet-4.5</c> is broadly available
    /// on Copilot Pro+ / enterprise subscriptions and produces high-quality
    /// code. <c>gpt-5</c> is gated behind specific plan tiers; use the dev
    /// menu's "Log available Copilot models" to see what your account
    /// actually has.
    /// </summary>
    const string DefaultModel = "claude-sonnet-4.5";

    readonly string _model;
    readonly string? _explicitToken;
    readonly SemaphoreSlim _initLock = new(1, 1);
    CopilotClient? _client;

    public CopilotSdkClient(string? model = null, string? explicitToken = null)
    {
        _model = model ?? DefaultModel;
        _explicitToken = explicitToken;
    }

    public string ModelId => _model;

    async Task<CopilotClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null) return _client;

            // UseLoggedInUser defaults to true when no explicit token is
            // provided — i.e. the SDK rides whichever account `gh auth` /
            // Copilot considers active. Pass an explicit token only when
            // a caller has resolved one (e.g. for a non-active gh account).
            var options = new CopilotClientOptions
            {
                AutoStart = false, // we'll StartAsync ourselves so failures surface here
                GitHubToken = _explicitToken,
            };
            var client = new CopilotClient(options);
            System.Diagnostics.Debug.WriteLine($"[CopilotSdk] starting CLI server (model={_model}, explicitToken={(_explicitToken is null ? "no" : "yes")})");
            await client.StartAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine("[CopilotSdk] CLI server ready");
            _client = client;
            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[CopilotSdk] StreamAsync model={_model} userPromptBytes={userPrompt.Length}");
        var client = await GetClientAsync(ct).ConfigureAwait(false);

        // Channel bridges the SDK's event-based delivery to our IAsyncEnumerable
        // contract. Single-writer (the event handler) → single-reader (the
        // enumerator). Unbounded because chunks arrive faster than UI can render
        // and we don't want to drop tokens; the channel is short-lived per call
        // so memory pressure is bounded by one model response.
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // Mode=Replace swaps in our spec 035 envelope-emitter prompt; without
        // it the CLI's default agent prompt would steer the model toward
        // tool-using agentic behaviour we don't want here.
        var sessionConfig = new SessionConfig
        {
            Model = _model,
            ClientName = "demo-script-tool",
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Streaming = true,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemPrompt,
            },
        };

        await using var session = await client.CreateSessionAsync(sessionConfig, ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[CopilotSdk] session created id={session.SessionId}");

        Exception? terminalError = null;
        bool sawDeltas = false;
        bool turnEnded = false;

        using var subscription = session.On(evt =>
        {
            // Trace every event type so envelope drift / unexpected sequencing
            // is visible during debugging. The line is kept short so it doesn't
            // dominate the devtools log; payload is only read for the events
            // we care about below.
            System.Diagnostics.Debug.WriteLine($"[CopilotSdk] evt: {evt.GetType().Name}");

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
                    // Some models / configurations emit only the final message
                    // (no deltas). Push the full body as one chunk so the
                    // envelope parser still gets fed. If deltas arrived, the
                    // final repeats the same content — skip to avoid feeding
                    // the parser twice.
                    if (!sawDeltas && final.Data.Content.Length > 0)
                        channel.Writer.TryWrite(final.Data.Content);
                    break;

                case AssistantTurnEndEvent:
                    // The turn is done but the assistant.message event may
                    // arrive AFTER turn_end in some Copilot CLI builds. Defer
                    // the channel-close to session.idle to avoid dropping the
                    // final content on the floor.
                    turnEnded = true;
                    break;

                case SessionIdleEvent:
                    // session.idle fires when nothing is in flight. We only
                    // close the channel if we saw the turn end first — bare
                    // session.idle can land between session creation and
                    // message dispatch (the session is idle by definition
                    // before a message is sent).
                    if (turnEnded)
                        channel.Writer.TryComplete();
                    break;

                case SessionErrorEvent err:
                    var msg = $"Copilot {err.Data.ErrorType}: {err.Data.Message}";
                    System.Diagnostics.Debug.WriteLine($"[CopilotSdk] {msg}");
                    terminalError = err.Data.ErrorType switch
                    {
                        "authentication" or "authorization" => new AuthExpiredException(msg),
                        _ => new Exception(msg),
                    };
                    channel.Writer.TryComplete(terminalError);
                    break;
            }
        });

        // Send and let the events flow. SendAsync returns immediately with
        // the message id; the assistant's response arrives over events.
        try
        {
            await session.SendAsync(new MessageOptions { Prompt = userPrompt }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
            throw;
        }

        // Drain the channel. Cancelled enumeration → ChannelClosedException
        // up to the caller via the standard IAsyncEnumerable cancellation path.
        await foreach (var chunk in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }

        if (terminalError is not null)
            throw terminalError;
    }

    /// <summary>
    /// Probe the SDK for the list of models available on the current account.
    /// Returns a flat string of "id (name)" pairs for log/dev-menu display.
    /// </summary>
    public async Task<string> DescribeAvailableModelsAsync(CancellationToken ct)
    {
        try
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var models = await client.ListModelsAsync(ct).ConfigureAwait(false);
            if (models is null || models.Count == 0)
                return "(no models reported)";
            var sb = new System.Text.StringBuilder();
            foreach (var m in models)
                sb.Append(m.Id).Append(" (").Append(m.Name ?? "?").Append(")\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"failed to list models: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Tear down the cached <see cref="CopilotClient"/> so the next
    /// <see cref="StreamAsync"/> call re-runs <see cref="GetClientAsync"/>
    /// and starts a fresh CLI session. The pipeline calls this after
    /// <c>gh auth refresh</c> — without it the SDK keeps using the
    /// already-started session that was authenticated under the old token,
    /// and the auth-retry path silently fails again.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is { } client)
            {
                System.Diagnostics.Debug.WriteLine("[CopilotSdk] resetting CLI client (auth refresh)");
                try { await client.StopAsync().ConfigureAwait(false); }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CopilotSdk] StopAsync during reset threw: {ex.Message}");
                }
                try { await client.DisposeAsync().ConfigureAwait(false); }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CopilotSdk] DisposeAsync during reset threw: {ex.Message}");
                }
                _client = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            try { await client.StopAsync().ConfigureAwait(false); }
            catch { /* best-effort shutdown */ }
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch { /* swallow */ }
            _client = null;
        }
        _initLock.Dispose();
    }
}
