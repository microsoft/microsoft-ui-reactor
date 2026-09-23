using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// The ambient, render-pass-scoped link between a component's
/// <see cref="ValidationContext"/> and the <c>.Validate(field, value, …)</c> modifier.
///
/// <para><b>Why this exists.</b> <c>.Validate()</c> is an extension method on
/// <see cref="Element"/>: it is handed an element and a value, and has no path back to
/// the component that is rendering. Before this scope existed it could only stash a
/// <see cref="ValidationAttached"/> record and hope something downstream ran it — which
/// only <c>FormField</c> and the visualizers ever did, so validators attached to a plain
/// control silently never ran (issue #1262).</para>
///
/// <para><b>Why a render-pass scope specifically.</b> Validation results have to be
/// observable by the same <c>Render()</c> that produced them. The documented pattern
/// reads the context inline —
/// <c>When(ctx.IsTouched("email") &amp;&amp; ctx.HasError("email"), …)</c> — and C#
/// evaluates arguments left to right, so a <c>.Validate(…)</c> argument runs before a
/// later <c>When(…)</c> sibling in the same call. Running validators during reconcile
/// instead (as <c>FormField</c> does) is always one pass too late for that read.</para>
///
/// <para><b>Lifetime is owned by the reconciler, never by the hook.</b> The frame is
/// opened immediately before <c>Render()</c> and closed immediately after, so the
/// ambient cannot outlive a render or leak into an event handler, an effect, or — the
/// case that actually bites — an unrelated unit test running later on the same thread.
/// A headless <see cref="RenderContext"/> test that calls the hook directly has no open
/// frame, so <c>.Validate()</c> stays attach-only there.</para>
/// </summary>
internal static class ValidationRenderScope
{
    [ThreadStatic] private static ValidationContext? t_context;
    [ThreadStatic] private static ValidationContext? t_pendingProvide;
    [ThreadStatic] private static int t_depth;

    /// <summary>
    /// The context <c>.Validate()</c> should push results into, or <c>null</c> when no
    /// render is in flight on this thread or no context is reachable.
    /// </summary>
    internal static ValidationContext? Current => t_context;

    /// <summary>
    /// True while a component render is in flight on this thread.
    /// <para>
    /// <see cref="ValidationContext"/> uses this to suppress its change notification:
    /// a mutation made *during* a render needs no re-render, because the component doing
    /// the rendering observes the new value later in the very same pass. Notifying
    /// anyway would re-enter <c>requestRerender</c> from inside <c>Render()</c>, which
    /// the reconciler treats as a render loop.
    /// </para>
    /// </summary>
    internal static bool InRender => t_depth > 0;

    /// <summary>
    /// Opens a frame for one component render. <paramref name="inherited"/> is the
    /// <see cref="ValidationContext"/> already visible through the reconciler's context
    /// scope, so a child component whose ancestor provided a context gets eager
    /// validation without having to call <c>UseValidationContext()</c> itself.
    /// </summary>
    internal static Frame Begin(ValidationContext? inherited)
    {
        var frame = new Frame(t_context, t_pendingProvide);
        t_context = inherited;
        t_pendingProvide = null;
        t_depth++;
        return frame;
    }

    /// <summary>
    /// Called by <c>UseValidationContext()</c> with the context it resolved.
    /// <paramref name="autoProvide"/> is true when the hook fell back to a
    /// component-local context, meaning nothing up the tree has provided one and the
    /// reconciler should publish it to the rendered subtree on the component's behalf.
    /// </summary>
    internal static void Publish(ValidationContext context, bool autoProvide)
    {
        if (t_depth == 0) return;
        t_context = context;
        if (autoProvide) t_pendingProvide = context;
    }

    /// <summary>
    /// Wraps the element a component just returned so descendants — <c>FormField</c>,
    /// the visualizers, <c>ValidationRule</c>, and nested components — can read the
    /// context through the normal provider mechanism.
    /// <para>
    /// An explicit <c>.Provide(ValidationContexts.Current, …)</c> written by the caller
    /// always wins; this only fills an empty slot.
    /// </para>
    /// </summary>
    internal static Element ApplyProvide(Element rendered)
    {
        var pending = t_pendingProvide;
        if (pending is null) return rendered;

        var existing = rendered.ContextValues;
        if (existing is not null && existing.ContainsKey(ValidationContexts.Current))
            return rendered;

        return rendered.Provide(ValidationContexts.Current, pending);
    }

    /// <summary>
    /// Restores the enclosing frame. A struct so the per-render cost is a few stack
    /// slots rather than an allocation on a path that runs for every component.
    /// </summary>
    internal readonly struct Frame : IDisposable
    {
        private readonly ValidationContext? _previousContext;
        private readonly ValidationContext? _previousPendingProvide;

        internal Frame(ValidationContext? previousContext, ValidationContext? previousPendingProvide)
        {
            _previousContext = previousContext;
            _previousPendingProvide = previousPendingProvide;
        }

        public void Dispose()
        {
            t_context = _previousContext;
            t_pendingProvide = _previousPendingProvide;
            if (t_depth > 0) t_depth--;
        }
    }
}
