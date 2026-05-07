using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Threading-invariant helpers for the spec-036 Window primitive. All public
/// mutators on <see cref="ReactorWindow"/>, <see cref="ReactorAppContext"/>, and
/// the <see cref="ReactorApp"/> open/find/exit surface must be called on the UI
/// thread; this helper raises a clear exception if they are not.
/// </summary>
/// <remarks>
/// Read-only properties that snapshot a <c>Volatile.Read</c> field
/// (<see cref="ReactorApp.Windows"/>, <see cref="ReactorWindow.Spec"/>,
/// <see cref="ReactorWindow.Dpi"/>, etc.) are intentionally allowed off-thread.
/// </remarks>
internal static class ThreadAffinity
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the calling thread is not
    /// the UI thread captured by <see cref="ReactorApp.UIDispatcher"/>. When the
    /// dispatcher hasn't been captured yet (no <c>Run</c> in flight) the call is
    /// permitted — startup-callback construction, unit-test fixtures, and the
    /// pre-Application.Start configuration phase all run before there is a UI
    /// thread to gate against.
    /// </summary>
    public static void ThrowIfNotOnUIThread(string memberName)
    {
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null) return;
        if (dispatcher.HasThreadAccess) return;
        throw new InvalidOperationException(
            $"{memberName} must be called on the UI thread. " +
            "Use ReactorApp.UIDispatcher.TryEnqueue(...) to marshal the call. (spec 036 §0.4)");
    }
}
