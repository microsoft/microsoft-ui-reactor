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
    [ThreadStatic] private static List<ValidationContext>? t_deferred;
    [ThreadStatic] private static Dictionary<ValidationAttached, Ownership>? t_owned;
    [ThreadStatic] private static HashSet<(ValidationContext Context, string Field)>? t_adopted;

    /// <summary>
    /// What an eager <c>.Validate()</c> write claimed: the context it actually reached
    /// and the stamp its own write was issued.
    /// </summary>
    internal readonly record struct Ownership(ValidationContext Context, long Stamp);

    private sealed class ByReference : IEqualityComparer<ValidationAttached>
    {
        internal static readonly ByReference Instance = new();
        public bool Equals(ValidationAttached? a, ValidationAttached? b) => ReferenceEquals(a, b);
        public int GetHashCode(ValidationAttached obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Records which context an attachment's eager verdict went to, and the stamp it was
    /// issued, so the mounted control can inherit that exact claim.
    /// <para>
    /// Resolving it again at mount time instead would be guesswork: the reconciler's
    /// context at that point is not necessarily the one the render scope reached — an
    /// explicit <c>.Provide(...)</c> inside a component that owns a local context
    /// separates them — and re-reading the field's current stamp would hand one control
    /// a sibling's claim, letting either retract the other's verdict.
    /// </para>
    /// <para>
    /// Keyed by reference. <see cref="ValidationAttached"/> is a record, so two links of
    /// a chain that happen to carry equal values are the same key by value and different
    /// keys by identity — and identity is what "the write I made" means here.
    /// </para>
    /// </summary>
    internal static void RecordOwnership(ValidationAttached attached, ValidationContext context, long stamp)
    {
        if (t_depth == 0) return;
        (t_owned ??= new Dictionary<ValidationAttached, Ownership>(ByReference.Instance))[attached] =
            new Ownership(context, stamp);
    }

    /// <summary>
    /// Claims an attachment's recorded ownership, removing it. Returns false when the
    /// attachment never wrote anything in this pass — an element assembled outside a
    /// render, or one whose validators were only ever attached.
    /// </summary>
    internal static bool TryTakeOwnership(ValidationAttached attached, out Ownership ownership)
    {
        var owned = t_owned;
        if (owned is not null && owned.Remove(attached, out ownership)) return true;
        ownership = default;
        return false;
    }

    /// <summary>
    /// Whether an attachment already has a claim from this render pass <em>against
    /// <paramref name="context"/></em> — a verdict <c>.Validate()</c> installed while the
    /// tree was being built, which a control is about to take ownership of. Does not
    /// consume the claim.
    /// <para>
    /// The context has to match. An explicit
    /// <c>.Provide(ValidationContexts.Current, other)</c> written inside a component that
    /// also owns a hook-local context separates the two: the eager write went to the
    /// hook's context, while <c>FormField</c> resolves the provided one. A claim against
    /// the wrong context says nothing about whether this context has been validated.
    /// </para>
    /// </summary>
    internal static bool HasOwnership(ValidationAttached attached, ValidationContext context)
        => t_owned is not null
            && t_owned.TryGetValue(attached, out var owned)
            && ReferenceEquals(owned.Context, context);

    /// <summary>
    /// Records that a mounted control has taken over a field's synchronous slot, so an
    /// unconsumed claim on that same slot is not withdrawn at the end of the pass.
    /// <para>
    /// Every value-carrying <c>.Validate()</c> on a field shares one producer key, so a
    /// render that returns one validated element and also builds and drops another
    /// naming the same field leaves the dropped element holding the newer stamp. Retiring
    /// it would clear the slot the mounted control is relying on and report an invalid
    /// field as valid — the exact failure this ownership model exists to prevent
    /// (issue #1262 review). The dropped element's verdict still wins the slot's
    /// contents, which is the pre-existing last-writer-wins behaviour for two elements
    /// naming one field; only the erasure is prevented here.
    /// </para>
    /// </summary>
    internal static void MarkAdopted(ValidationContext context, string field)
    {
        if (t_depth == 0) return;
        (t_adopted ??= new HashSet<(ValidationContext, string)>()).Add((context, field));
    }

    /// <summary>
    /// The context <c>.Validate()</c> should push results into, or <c>null</c> when no
    /// render is in flight on this thread or no context is reachable.
    /// </summary>
    internal static ValidationContext? Current => t_context;

    /// <summary>
    /// True while a component render is in flight on this thread.
    /// <para>
    /// <see cref="ValidationContext"/> uses this to defer its change notification: the
    /// component doing the rendering observes the new value later in the same pass, and
    /// notifying inline would re-enter <c>requestRerender</c> from inside
    /// <c>Render()</c>, which the reconciler treats as a render loop. The notification
    /// is delivered once the outermost render frame closes.
    /// </para>
    /// </summary>
    internal static bool InRender => t_depth > 0;

    /// <summary>
    /// Records a context whose change was raised mid-render, to be announced once the
    /// outermost frame closes.
    /// </summary>
    internal static void DeferNotification(ValidationContext context)
    {
        var pending = t_deferred ??= new List<ValidationContext>(2);
        foreach (var existing in pending)
        {
            if (ReferenceEquals(existing, context)) return;
        }
        pending.Add(context);
    }

    private static void FlushDeferredNotifications()
    {
        var pending = t_deferred;
        if (pending is null || pending.Count == 0) return;

        t_deferred = null;
        foreach (var context in pending)
            context.NotifyDeferred();
    }

    /// <summary>
    /// Opens a frame for one component render. <paramref name="inherited"/> is the
    /// <see cref="ValidationContext"/> already visible through the reconciler's context
    /// scope, so a child component whose ancestor provided a context gets eager
    /// validation without having to call <c>UseValidationContext()</c> itself.
    /// </summary>
    internal static Frame Begin(ValidationContext? inherited) => BeginCore(inherited, isReconcile: false);

    private static Frame BeginCore(ValidationContext? inherited, bool isReconcile)
    {
        // A render frame opening at depth 0 starts a new pass, so the previous pass's
        // claims are settled here rather than when the last frame closed: the mount that
        // consumes a claim does not always run inside the frame that made it.
        //
        // A reconcile frame must NOT settle them. It is the *consumer* — a host's root
        // render closes its own frame before Reconcile opens this one, so clearing here
        // would discard every root-level `.Validate()` claim before the controls that
        // inherit them exist (issue #1262 review).
        Dictionary<ValidationAttached, Ownership>? abandoned = null;
        HashSet<(ValidationContext Context, string Field)>? abandonedAdopted = null;
        if (t_depth == 0 && !isReconcile)
        {
            abandoned = t_owned;
            abandonedAdopted = t_adopted;
            t_owned = null;
            t_adopted = null;
        }

        var frame = new Frame(t_context, t_pendingProvide, isReconcile);
        t_context = inherited;
        t_pendingProvide = null;
        t_depth++;

        // Anything still held when a new pass opens belongs to a pass that never
        // reached reconciliation — a root render that threw, or a host that returned
        // without one. Its verdict is in the context with nothing to own it, so the
        // claims are withdrawn rather than dropped. Done after the frame is open so the
        // retractions defer into this pass instead of announcing inline at depth 0.
        if (abandoned is not null) RetireClaims(abandoned, abandonedAdopted);

        return frame;
    }

    /// <summary>
    /// Opens a deferral-only frame around a whole reconcile pass.
    /// <para>
    /// Mounting, updating, and unmounting <c>ValidationRule</c> and <c>FormField</c>
    /// happen *between* component renders, so they used to fall outside every frame and
    /// announce their changes synchronously. A rule leaving the tree retracts its
    /// contribution during unmount; if the owning <c>UseValidationContext()</c> lives in
    /// a child component, that notification re-entered the reconciler's inline re-render
    /// path while the subtree was still being torn down. Holding a frame for the
    /// duration of the pass defers every such notification to the end of it.
    /// </para>
    /// <para>
    /// The frame carries no context: no component is rendering, so <c>.Validate()</c>
    /// reached from reconcile code stays attach-only exactly as before.
    /// </para>
    /// </summary>
    internal static Frame BeginReconcile() => BeginCore(null, isReconcile: true);

    /// <summary>
    /// Withdraws the claims of a render that will never reach reconciliation.
    /// <para>
    /// A root render that throws installs an error fallback and returns without calling
    /// <c>Reconcile</c>, so no reconcile frame ever opens to consume or retire what it
    /// claimed. Leaving that to the *next* render is not good enough: if none follows —
    /// the fallback is terminal for that host — the verdict sits in the context owned by
    /// nothing and the thread-static map keeps the abandoned attachment alive
    /// (issue #1262 review).
    /// </para>
    /// <para>
    /// Safe on any abort path, including one taken after reconciliation started: a claim
    /// a control already adopted is no longer in the map, and every withdrawal is stamped
    /// so it cannot disturb a slot another writer owns.
    /// </para>
    /// </summary>
    internal static void AbandonPendingClaims() => RetireUnconsumedClaims();

    /// <summary>
    /// Withdraws every claim the pass made that no control took over.
    /// <para>
    /// A claim is created by the eager write and taken by the control that gets mounted
    /// or updated with it. One left behind belongs to an element that was validated and
    /// then never mounted — built inside a render, then dropped — whose verdict would
    /// otherwise sit in the context owned by nothing. The withdrawal is stamped, so it
    /// cannot disturb a slot something else has written since.
    /// </para>
    /// <para>
    /// A slot a mounted control has already adopted is left alone: the two share one
    /// producer key, so withdrawing the dropped element's claim would take the mounted
    /// control's verdict with it. See <see cref="MarkAdopted"/>.
    /// </para>
    /// </summary>
    private static void RetireUnconsumedClaims()
    {
        var owned = t_owned;
        var adopted = t_adopted;
        t_owned = null;
        t_adopted = null;
        RetireClaims(owned, adopted);
    }

    private static void RetireClaims(
        Dictionary<ValidationAttached, Ownership>? claims,
        HashSet<(ValidationContext Context, string Field)>? adopted)
    {
        if (claims is null || claims.Count == 0) return;

        foreach (var (attached, claim) in claims)
        {
            if (adopted is not null && adopted.Contains((claim.Context, attached.FieldName)))
                continue;

            claim.Context.RetireProducer(
                attached.FieldName, ValidationContext.SyncProducer, claim.Stamp);
        }
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
        private readonly bool _isReconcile;

        internal Frame(
            ValidationContext? previousContext,
            ValidationContext? previousPendingProvide,
            bool isReconcile)
        {
            _previousContext = previousContext;
            _previousPendingProvide = previousPendingProvide;
            _isReconcile = isReconcile;
        }

        public void Dispose()
        {
            t_context = _previousContext;
            t_pendingProvide = _previousPendingProvide;

            // Before the depth drops, so the retractions defer into this pass's batch
            // rather than announcing one at a time on the way out.
            if (_isReconcile && t_depth == 1) RetireUnconsumedClaims();

            if (t_depth > 0) t_depth--;
            if (t_depth == 0)
            {
                FlushDeferredNotifications();
            }
        }
    }
}
