using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// Provides the Context definition for propagating ValidationContext through the tree.
/// </summary>
public static class ValidationContexts
{
    /// <summary>
    /// The Context definition used to propagate ValidationContext through the tree.
    /// Default value is null (no validation context provided by any ancestor).
    /// </summary>
    public static readonly Context<ValidationContext?> Current = new(null, "ValidationContext");
}

/// <summary>
/// Extension methods for the UseValidationContext hook on RenderContext.
/// </summary>
public static class ValidationContextHookExtensions
{
    /// <summary>
    /// Returns the nearest ancestor's ValidationContext, or creates a new one
    /// scoped to this component. The created context persists across re-renders.
    /// <para>
    /// When the context is component-local (no ancestor provided one) it is published to
    /// the rendered subtree automatically, so <c>FormField</c>, the visualizers, and
    /// nested components find it without an explicit
    /// <c>.Provide(ValidationContexts.Current, ctx)</c>. Writing that call yourself still
    /// works and takes precedence.
    /// </para>
    /// <para>
    /// The component also re-renders when the context changes, which is what makes
    /// <c>ctx.MarkAllTouched()</c> in a submit handler visible: it mutates the context
    /// without touching component state, so nothing else would schedule a repaint.
    /// </para>
    /// </summary>
    public static ValidationContext UseValidationContext(this RenderContext ctx)
    {
        var parent = ctx.UseContext(ValidationContexts.Current);
        // UseState captures initial value only on first render; subsequent renders reuse stored value.
        var (local, _) = ctx.UseState(new ValidationContext());

        var resolved = parent ?? local;
        ValidationRenderScope.Publish(resolved, autoProvide: parent is null);
        SubscribeForRerender(ctx, resolved);
        return resolved;
    }

    /// <summary>
    /// Creates a child validation context independent from any parent.
    /// Returns both the child context and the parent (if one exists).
    /// The child collects its own messages; use the parent reference to bubble manually.
    /// </summary>
    public static (ValidationContext Child, ValidationContext? Parent) UseChildValidationContext(
        this RenderContext ctx)
    {
        var parent = ctx.UseContext(ValidationContexts.Current);
        var (child, _) = ctx.UseState(new ValidationContext());

        // The child is deliberately independent of the parent, so it is the one that
        // both collects this subtree's results and drives this component's repaints.
        ValidationRenderScope.Publish(child, autoProvide: true);
        SubscribeForRerender(ctx, child);
        return (child, parent);
    }

    /// <summary>
    /// Keeps one live subscription to <paramref name="context"/>'s change event for the
    /// lifetime of the component, re-subscribing only if the component is handed a
    /// different context instance.
    /// <para>
    /// The re-render is requested by moving a counter through <c>UseState</c> rather than
    /// by calling the re-render callback directly, so it inherits the hook's existing
    /// UI-thread marshalling — a background async validator that adds a message is
    /// marshalled rather than racing the reconciler.
    /// </para>
    /// </summary>
    private static void SubscribeForRerender(RenderContext ctx, ValidationContext context)
    {
        // A monotonic ticket source that survives re-renders. The state setter only
        // schedules a repaint when the new value differs from the old, so the ticket has
        // to keep climbing — feeding it a counter captured from render scope would go
        // stale and silently drop notifications.
        //
        // The setter is deliberately the default (marshaling) one rather than
        // threadSafe: Interlocked already makes the counter safe, while threadSafe would
        // invoke the re-render callback on whatever thread raised Changed — which an
        // async validator can do from a worker.
        var (ticket, _) = ctx.UseState(new global::System.Runtime.CompilerServices.StrongBox<int>(0));
        var (revision, setRevision) = ctx.UseState(0);
        _ = revision;

        ctx.UseEffect(() =>
        {
            void OnChanged() => setRevision(global::System.Threading.Interlocked.Increment(ref ticket.Value));
            context.Changed += OnChanged;
            return () => context.Changed -= OnChanged;
        }, context);
    }
}

/// <summary>
/// Convenience hook wrappers on Component base class.
/// </summary>
public static class ValidationContextComponentExtensions
{
    /// <summary>
    /// Returns the nearest ancestor's ValidationContext, or creates a new one.
    /// </summary>
    public static ValidationContext UseValidationContext(this Component component)
        => component.Context.UseValidationContext();

    /// <summary>
    /// Creates a child validation context independent from any parent.
    /// </summary>
    public static (ValidationContext Child, ValidationContext? Parent) UseChildValidationContext(
        this Component component)
        => component.Context.UseChildValidationContext();
}
