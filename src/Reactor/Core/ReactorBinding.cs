using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 047 §14 Phase 1 (1.4) — public author-facing surface for the echo
/// suppression primitive (§13 Q19). Today this delegates to
/// <see cref="ChangeEchoSuppressor.BeginSuppress(UIElement)"/>; Phase 4
/// swaps the body for per-control tolerance / coercion metadata as part of
/// the §8 cleanup — the signature does not change. The per-binding wrapper
/// <c>ReactorBinding&lt;TElement&gt;</c> (per spec §4) lands in 1.6; this
/// static class is just the primitive entry point.
/// </summary>
public static class ReactorBinding
{
    /// <summary>
    /// Runs <paramref name="mutate"/> after registering a one-shot
    /// suppression token for <paramref name="target"/>. The next
    /// change-event handler invocation against <paramref name="target"/>
    /// consumes the token via <c>ChangeEchoSuppressor.ShouldSuppress</c>
    /// and short-circuits before the user's OnChanged callback fires.
    ///
    /// No try/finally — the consumption is event-driven and the counter
    /// is harmless if a single token is left at +1 after an exceptional
    /// path (it gets consumed by the next change event for that control,
    /// which by contract should be one the engine itself just provoked).
    /// </summary>
    public static void WriteSuppressed(UIElement target, Action mutate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mutate);
        ChangeEchoSuppressor.BeginSuppress(target);
        mutate();
    }

    /// <summary>
    /// Typed overload — saves a cast at the call site for the common case
    /// where the handler already has a strongly-typed control reference.
    /// </summary>
    public static void WriteSuppressed<T>(T target, Action<T> mutate) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mutate);
        ChangeEchoSuppressor.BeginSuppress(target);
        mutate(target);
    }
}
