using System.Text;
using GitHub.Copilot;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Translates strings using the GitHub Copilot SDK, which routes through the user's
/// authenticated GitHub Copilot subscription. Requires the GitHub CLI (`gh`) to be
/// installed and authenticated with a Copilot-enabled account.
/// </summary>
internal sealed class CopilotTranslationProvider : ITranslationProvider
{
    public string Name => "GitHub Copilot";

    private readonly string _model;

    public CopilotTranslationProvider(string? model = null)
    {
        // Default model: gpt-5.4-mini — a fast, low-cost model well-suited to
        // straightforward string translation (gpt-4o is no longer offered by the
        // Copilot runtime → session.create fails). Override via --model or COPILOT_MODEL.
        _model = model
            ?? Environment.GetEnvironmentVariable("COPILOT_MODEL")
            ?? "gpt-5.4-mini";
    }

    public async Task<TranslationResult> TranslateAsync(TranslationBatch batch, CancellationToken ct = default)
    {
        var systemPrompt = TranslationPrompt.BuildSystemPrompt(batch.SourceLocale, batch.TargetLocale);
        var userMessage = TranslationPrompt.BuildUserMessage(batch);
        var fullPrompt = $"{systemPrompt}\n\n{userMessage}";

        await using var client = new CopilotClient();
        await client.StartAsync();

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
        });

        var tcs = new TaskCompletionSource<string>();
        var responseBuilder = new StringBuilder();

        session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseBuilder.Append(msg.Data.Content);
                    break;
                case AssistantMessageDeltaEvent delta:
                    responseBuilder.Append(delta.Data.DeltaContent);
                    break;
                case SessionIdleEvent:
                    tcs.TrySetResult(responseBuilder.ToString());
                    break;
                case SessionErrorEvent err:
                    tcs.TrySetException(new InvalidOperationException(
                        $"Copilot error: {err.Data.Message}"));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions
        {
            Prompt = fullPrompt,
        });

        string content;
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            try
            {
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);
                if (completed != tcs.Task)
                    throw new TimeoutException("Translation request timed out after 2 minutes.");
                content = await tcs.Task;
            }
            finally
            {
                await registration.DisposeAsync();
            }
        }
        else
        {
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed != tcs.Task)
                throw new TimeoutException("Translation request timed out after 2 minutes.");
            content = await tcs.Task;
        }

        var expectedKeys = batch.Entries.Select(e => e.Key);
        var translations = TranslationPrompt.ParseResponse(content, expectedKeys);

        var errors = new Dictionary<string, string>();
        foreach (var entry in batch.Entries)
        {
            if (!translations.ContainsKey(entry.Key))
                errors[entry.Key] = "No translation returned by model";
        }

        return new TranslationResult { Translations = translations, Errors = errors };
    }
}
