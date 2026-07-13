using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>
/// Minimal streaming text-completion contract. Implemented by
/// <see cref="CopilotSdkClient"/>; kept as a seam so the pipeline can be
/// unit-tested or pointed at a different backend without touching the UI.
/// </summary>
public interface IModelClient
{
    string ModelId { get; }

    /// <summary>
    /// Open a multi-turn conversation under <paramref name="systemPrompt"/>.
    /// The returned conversation retains context across <c>SendAsync</c> turns —
    /// this is what lets the build-and-fix loop feed compiler errors back to the
    /// same agent.
    /// </summary>
    Task<IModelConversation> StartConversationAsync(string systemPrompt, CancellationToken ct);

    /// <summary>
    /// Resume the prior conversation identified by <paramref name="sessionId"/>
    /// so a fix can continue the original creating conversation — even days
    /// later. Falls back to a fresh conversation if the session can no longer be
    /// resumed (callers should still include the current source in their prompt
    /// so correctness does not depend on restored history).
    /// </summary>
    Task<IModelConversation> ResumeConversationAsync(string sessionId, string systemPrompt, CancellationToken ct);
}

/// <summary>A stateful, multi-turn model conversation.</summary>
public interface IModelConversation : System.IAsyncDisposable
{
    /// <summary>The Copilot session id — persist this to resume the conversation later.</summary>
    string SessionId { get; }

    /// <summary>Send one user turn and stream the assistant's token deltas.</summary>
    IAsyncEnumerable<string> SendAsync(string userPrompt, CancellationToken ct);
}
