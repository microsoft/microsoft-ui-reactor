using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;

namespace Microsoft.UI.Reactor.Hooks;

/// <summary>
/// Props for <see cref="PendingComponent"/>.
/// </summary>
public sealed record PendingProps(Element Fallback, Element Child);

/// <summary>
/// Entry-point factory for the <c>Pending</c> bubble-up element. See
/// <c>docs/specs/020-async-resources-design.md</c> §10 for the full mechanism.
/// </summary>
public static class PendingFactory
{
    /// <summary>
    /// Wraps <paramref name="child"/> with a fresh <see cref="PendingScope"/>. Renders
    /// <paramref name="fallback"/> instead of <paramref name="child"/> while any
    /// <c>UseResource</c>/<c>UseInfiniteResource</c> in the subtree is in the
    /// <c>Loading</c> state. <c>Reloading(previous)</c> does <b>not</b> trigger the
    /// fallback — spec §10.1.
    /// </summary>
    /// <remarks>
    /// The child subtree is always mounted so its hooks register with the scope. The
    /// element simply chooses which rendered tree to show — there is no unwinding
    /// of rendering, and no reconciler involvement.
    /// <para>
    /// Marked <see cref="ReactorSourceTransparentAttribute"/> (spec 010) because it is a
    /// pure forwarder: the element is really built by the <c>Component&lt;,&gt;</c> call
    /// on the next line, inside Reactor's own assembly, where no consumer call site
    /// exists to intercept. Without the annotation a <c>Pending(...)</c> element reports
    /// no location at all. With it, the interceptor in the consumer's compilation stamps
    /// the line they wrote <c>Pending(</c> on, which is the answer they were looking for.
    /// </para>
    /// </remarks>
    [ReactorSourceTransparent]
    public static Element Pending(Element fallback, Element child)
        => Microsoft.UI.Reactor.Factories.Component<PendingComponent, PendingProps>(new PendingProps(fallback, child));
}

/// <summary>
/// Component backing the <c>Pending</c> element. Hosts a <see cref="PendingScope"/>,
/// subscribes to its <see cref="PendingScope.Changed"/> event, and chooses between
/// <see cref="PendingProps.Fallback"/> and <see cref="PendingProps.Child"/>.
/// </summary>
/// <remarks>
/// The child subtree is always rendered. When the scope reports any loading resource,
/// the child's <c>Visibility</c> is flipped to <c>Collapsed</c> and the fallback is
/// shown instead. Because the subtree stays mounted, its hooks keep running, their
/// fetches complete in the background, and the UI swaps to the real content on the
/// next render frame after the scope reports clean.
/// </remarks>
public sealed class PendingComponent : Component<PendingProps>
{
    public override Element Render()
    {
        var scopeRef = UseRef<PendingScope?>(null);
        scopeRef.Current ??= new PendingScope();
        var scope = scopeRef.Current!;

        var (_, tick) = UseReducer(0, threadSafe: true);

        // Re-render whenever the scope's loading state changes. The subscription is
        // re-armed on every render so a stale handler from a previous mount cannot fire.
        UseEffect(() =>
        {
            Action handler = () => tick(n => n + 1);
            scope.Changed += handler;
            return () => scope.Changed -= handler;
        });

        bool showFallback = scope.AnyLoading;

        // Always mount both. Visibility toggles between fallback and child so the child
        // subtree's hooks keep running, even while the fallback is visible.
        return Microsoft.UI.Reactor.Factories.Grid(
            columns: new[] { Microsoft.UI.Reactor.GridSize.Star() },
            rows: new[] { Microsoft.UI.Reactor.GridSize.Star() },
            Props.Child.IsVisible(!showFallback),
            Props.Fallback.IsVisible(showFallback))
            .Provide(AppContexts.PendingScope, (PendingScope?)scope);
    }

    // Pending must re-render when its parent re-renders so a new Props (Fallback/Child
    // swap at call-site) is picked up.
    protected internal override bool ShouldUpdate() => true;
}
