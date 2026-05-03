using System.Collections.Generic;
using System.Threading;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Streaming chat completion contract. <see cref="GithubModelsClient"/> is the
/// production implementation; tests substitute a fake to drive deterministic
/// token sequences.
/// </summary>
public interface IModelClient
{
    /// <summary>
    /// Stream the model's response token-by-token. The returned sequence is
    /// produced as deltas arrive over the wire — implementations must abort
    /// the underlying HTTP read when <paramref name="ct"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

/// <summary>Thrown when the GitHub Models API rejects the call as unauthenticated.</summary>
public sealed class AuthExpiredException : System.Exception
{
    public AuthExpiredException(string message) : base(message) { }
    public AuthExpiredException(string message, System.Exception inner) : base(message, inner) { }
}

/// <summary>Surfaced when <c>gh</c> is not on PATH or interactive auth is unavailable.</summary>
public sealed class AuthUnavailableException : System.Exception
{
    public AuthUnavailableException(string message) : base(message) { }
}
