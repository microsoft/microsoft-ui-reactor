using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hooks;

/// <summary>
/// Hook that returns a strongly-typed <see cref="ElementRef{T}"/> for binding to a
/// concrete WinUI control via the <c>.Ref(...)</c> modifier. The typed ref removes
/// the <c>(Button)ref.Current</c> cast at consumers (Composition, Ink, focus, …).
/// </summary>
/// <remarks>
/// Spec 033 §3. The same <see cref="ElementRef{T}"/> instance is returned across
/// re-renders (identity stable), so storing the ref in a deps array or comparing
/// with <see cref="ReferenceEquals"/> is safe.
/// </remarks>
/// <example>
/// <code>
/// var btn = ctx.UseElementRef&lt;Button&gt;();
/// ctx.UseEffect(() => btn.Current?.Focus(FocusState.Programmatic), Array.Empty&lt;object&gt;());
/// return Button("Press me", onPress).Ref(btn);
/// </code>
/// </example>
public static class UseElementRefExtensions
{
    /// <summary>
    /// Returns a stable <see cref="ElementRef{T}"/> for the current component scope.
    /// </summary>
    /// <typeparam name="T">The concrete control type the ref will attach to.</typeparam>
    public static ElementRef<T> UseElementRef<T>(this RenderContext ctx)
        where T : FrameworkElement
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        // We back the typed wrapper with UseState so the same instance is
        // returned across re-renders (identity stable, matching useRef
        // semantics in React). The initial-value argument is captured into
        // the hook on first call and ignored afterward.
        var (typed, _) = ctx.UseState(new ElementRef<T>(new ElementRef()));
        return typed;
    }

    /// <inheritdoc cref="UseElementRef{T}(RenderContext)"/>
    public static ElementRef<T> UseElementRef<T>(this Component component)
        where T : FrameworkElement
        => component.Context.UseElementRef<T>();
}
