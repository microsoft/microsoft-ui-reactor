namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Thrown when a component's hook call sequence diverges from the previous
/// render's sequence — either a hook is called at a different index, or the
/// hook type at a given index doesn't match what was previously declared.
///
/// In normal operation this is a programming bug (hooks must be called in
/// the same order every render). During Hot Reload, however, an edit that
/// reorders or changes hook types is the *expected* outcome of the user
/// editing their code — so the host catches this exception, runs cleanups
/// on the surviving RenderContext, resets its hook state, and re-renders.
/// State is lost (we cannot reliably re-key surviving hook values to a
/// shape that may have changed), but the app keeps running rather than
/// hard-failing the dev loop.
///
/// Derives from <see cref="InvalidOperationException"/> so existing
/// `catch (InvalidOperationException)` handlers around component render
/// continue to work; the host's render catch sniffs the more specific
/// type to gate the hot-reload recovery path.
/// </summary>
public sealed class HookOrderException : InvalidOperationException
{
    public HookOrderException(string message) : base(message) { }
}
