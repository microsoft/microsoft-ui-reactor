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
/// with <see cref="object.ReferenceEquals"/> is safe.
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
    // <snippet:use-elementref>
    public static ElementRef<T> UseElementRef<T>(this RenderContext ctx)
        where T : FrameworkElement
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        // UseMemo with empty deps allocates the typed+inner ref on the first
        // render only; subsequent renders return the cached instance. Using
        // UseState here would eagerly evaluate `new ElementRef<T>(new ElementRef())`
        // on every render and throw the result away (UseState only consults
        // the initial value on first call), which would defeat the point of a
        // cheap stable ref hook.
        return ctx.UseMemo(static () => new ElementRef<T>(new ElementRef()), Array.Empty<object>());
    }
    // </snippet:use-elementref>

    /// <summary>
    /// Component-extension overload of <see cref="UseElementRef{T}(RenderContext)"/>.
    /// Equivalent to calling the <see cref="RenderContext"/>-extension form against
    /// <c>component.Context</c>.
    /// </summary>
    /// <typeparam name="T">The concrete control type the ref will attach to.</typeparam>
    /// <param name="component">The component whose render context owns the hook slot.</param>
    public static ElementRef<T> UseElementRef<T>(this Component component)
        where T : FrameworkElement
        => component.Context.UseElementRef<T>();
}
