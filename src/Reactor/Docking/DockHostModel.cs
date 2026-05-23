using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Which edge of a <see cref="DockManager"/> a <see cref="ToolWindow"/>
/// is pinned to. The model's logical sides; in RTL the visual mapping
/// flips (spec 045 §8.8).
/// </summary>
/// <remarks>Spec 045 §5.3.10.</remarks>
public enum DockSide
{
    /// <summary>Left edge in LTR; right edge visually in RTL.</summary>
    Left,

    /// <summary>Top edge.</summary>
    Top,

    /// <summary>Right edge in LTR; left edge visually in RTL.</summary>
    Right,

    /// <summary>Bottom edge.</summary>
    Bottom,
}

/// <summary>
/// Read-only view of a floating-pane window, surfaced through
/// <see cref="DockHostModel.Floating"/>.
/// </summary>
/// <remarks>Spec 045 §5.3.10 (Phase 2 addition).</remarks>
public sealed record FloatingDockWindow
{
    /// <summary>Stable identifier for the floating window.</summary>
    public required object Id { get; init; }

    /// <summary>The pane(s) inside the floating window.</summary>
    public required IReadOnlyList<DockableContent> Contents { get; init; }

    /// <summary>Last-known position in screen coordinates (DIPs).</summary>
    public double X { get; init; }

    /// <summary>Last-known position in screen coordinates (DIPs).</summary>
    public double Y { get; init; }

    /// <summary>Last-known size in DIPs.</summary>
    public double Width { get; init; }

    /// <summary>Last-known size in DIPs.</summary>
    public double Height { get; init; }
}

/// <summary>
/// Internal source-of-truth for a <see cref="DockManager"/> host's runtime
/// layout state. Mutations land here first and re-render the reconciled
/// element tree; the declarative <see cref="DockManager.Layout"/> prop
/// is the controlled input, the model is the live state, and
/// <c>OnLayoutChanged</c> closes the loop (spec 045 §5.3.10).
/// </summary>
/// <remarks>
/// Spec 045 §5.3.10 / tracking §2.16 (Phase 2).
///
/// <para>
/// <strong>UI-thread-affined.</strong> All mutators must run on the
/// dispatcher that owns the host element. Off-thread access throws
/// <see cref="InvalidOperationException"/> at the boundary; the contract
/// is documented, not enforced with locks (spec 045 §8.10).
/// </para>
///
/// <para>
/// <strong>Not a parallel writable surface.</strong> Apps interact via the
/// controlled <see cref="DockManager.Layout"/> prop plus the round-trip
/// events on <see cref="DockManager"/>. The model surface is exposed to
/// <see cref="IDockLayoutStrategy"/> and devtools (spec 045 §8.2) — not
/// pane-content components.
/// </para>
/// </remarks>
public sealed class DockHostModel
{
    private readonly object _identitySentinel = new();

    /// <summary>Owning dispatcher's managed thread id; checked at mutation boundaries.</summary>
    internal int OwnerThreadId { get; }

    /// <summary>Constructs a host model captured on the current thread.</summary>
    public DockHostModel()
    {
        OwnerThreadId = global::System.Environment.CurrentManagedThreadId;
    }

    // ── Read surface ──────────────────────────────────────────────────────

    /// <summary>The root of the docked layout tree (excludes side and floating panes).</summary>
    public DockNode? Root { get; internal set; }

    /// <summary>Tool windows currently pinned to the left side strip.</summary>
    public IReadOnlyList<ToolWindow> LeftSide { get; internal set; } = Array.Empty<ToolWindow>();

    /// <summary>Tool windows currently pinned to the top side strip.</summary>
    public IReadOnlyList<ToolWindow> TopSide { get; internal set; } = Array.Empty<ToolWindow>();

    /// <summary>Tool windows currently pinned to the right side strip.</summary>
    public IReadOnlyList<ToolWindow> RightSide { get; internal set; } = Array.Empty<ToolWindow>();

    /// <summary>Tool windows currently pinned to the bottom side strip.</summary>
    public IReadOnlyList<ToolWindow> BottomSide { get; internal set; } = Array.Empty<ToolWindow>();

    /// <summary>
    /// Floating-window state for torn-out panes. Populated by the host
    /// renderer from the docking subsystem's per-manager floating-window
    /// tracker, so live snapshots include what's currently floating.
    /// </summary>
    /// <remarks>
    /// Each entry carries the pane + best-effort bounds: today the
    /// dimensions reflect the spec values captured when the window
    /// opened, and <c>X</c>/<c>Y</c> are <c>0</c>. Live position /
    /// size tracking is a §2.6 follow-up — callers that depend on
    /// current bounds should read off the underlying window rather
    /// than trusting this snapshot.
    /// </remarks>
    public IReadOnlyList<FloatingDockWindow> Floating { get; internal set; } = Array.Empty<FloatingDockWindow>();

    /// <summary>The currently-active content, or null if none.</summary>
    public DockableContent? ActiveContent { get; internal set; }

    /// <summary>
    /// Insertion-policy hook supplied by the owning <see cref="DockManager"/>
    /// (mirrored from <see cref="DockManager.LayoutStrategy"/> each render).
    /// The model dispatches into <see cref="IDockLayoutStrategy.BeforeInsertDocument"/>
    /// / <see cref="IDockLayoutStrategy.BeforeInsertToolWindow"/> on programmatic
    /// <see cref="Dock"/>; a <c>true</c> return short-circuits the default insertion.
    /// </summary>
    /// <remarks>Spec 045 §5.3.6 / tracking §2.13.</remarks>
    public IDockLayoutStrategy? LayoutStrategy { get; internal set; }

    /// <summary>Enumerates every pane in the model (docked, side, floating). Order is unspecified.</summary>
    public IEnumerable<DockableContent> AllContent()
    {
        foreach (var c in Descendants()) yield return c;
        foreach (var c in LeftSide) yield return c;
        foreach (var c in TopSide) yield return c;
        foreach (var c in RightSide) yield return c;
        foreach (var c in BottomSide) yield return c;
        foreach (var fw in Floating)
            foreach (var c in fw.Contents) yield return c;
    }

    /// <summary>Walks the <see cref="Root"/> tree depth-first, yielding every <see cref="DockableContent"/> leaf.</summary>
    public IEnumerable<DockableContent> Descendants()
    {
        if (Root is null) yield break;
        var stack = new Stack<DockNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            switch (node)
            {
                case DockableContent leaf:
                    yield return leaf;
                    break;
                case DockSplit split:
                    for (int i = split.Children.Count - 1; i >= 0; i--) stack.Push(split.Children[i]);
                    break;
                case DockTabGroup grp:
                    for (int i = grp.Documents.Count - 1; i >= 0; i--) stack.Push(grp.Documents[i]);
                    break;
            }
        }
    }

    // ── Mutation surface (UI-thread-affined) ──────────────────────────────
    //
    // Phase 2 attaches these to the live reconciler; for now the methods
    // update model state and the wrapper re-renders. Full integration with
    // the native renderer happens under §2.16.

    private void ThrowIfOffThread()
    {
        if (global::System.Environment.CurrentManagedThreadId != OwnerThreadId)
            throw new InvalidOperationException(
                "DockHostModel mutations must run on the UI dispatcher that owns the host. " +
                "See spec 045 §8.10.");
    }

    /// <summary>Programmatically docks <paramref name="content"/> at <paramref name="target"/>.</summary>
    /// <remarks>
    /// Spec 045 §5.3.10.
    ///
    /// <para>
    /// When <see cref="LayoutStrategy"/> is supplied (§5.3.6 / tracking
    /// §2.13), the strategy's <c>BeforeInsert*</c> hook runs first.
    /// Returning <c>true</c> short-circuits the default insertion — the
    /// strategy is responsible for whatever placement it performed via
    /// the model surface. Returning <c>false</c> queues the default
    /// <see cref="PendingMutation.DockOp"/> and then fires the
    /// <c>AfterInsert*</c> hook so apps can adjust dimensions, activate
    /// the pane, or pin to a side after the manager's routing.
    /// </para>
    /// </remarks>
    public void Dock(DockableContent content, DockTarget target)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfOffThread();

        // §2.13 — Before* dispatch. The strategy may pin to a side, dock
        // elsewhere, or skip the operation entirely. Subtype-dispatch
        // routes Document vs ToolWindow to the right interface method;
        // bare DockableContent (the P1 source-compat shape) bypasses the
        // strategy (the contract is typed against the §2.8 subclasses).
        if (LayoutStrategy is { } strategy)
        {
            bool handled = content switch
            {
                Document doc => strategy.BeforeInsertDocument(this, doc),
                ToolWindow tw => strategy.BeforeInsertToolWindow(this, tw),
                _ => false,
            };
            if (handled)
            {
                // BeforeInsert* placed the pane (and queued PinToSide / etc.
                // on its behalf). Notify the host so the drain runs even
                // when the original Dock() short-circuits.
                OnMutationQueued?.Invoke();
                return;
            }
        }

        Pending.Add(new PendingMutation.DockOp(content, target));

        // §2.13 — After* dispatch. Runs after the default routing has
        // been queued so the strategy can layer adjustments on top
        // (size hints, side pinning, activation).
        if (LayoutStrategy is { } postStrategy)
        {
            switch (content)
            {
                case Document doc: postStrategy.AfterInsertDocument(this, doc); break;
                case ToolWindow tw: postStrategy.AfterInsertToolWindow(this, tw); break;
            }
        }
        OnMutationQueued?.Invoke();
    }

    /// <summary>Tears <paramref name="content"/> out into a new floating window.</summary>
    public void Float(DockableContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.FloatOp(content));
        OnMutationQueued?.Invoke();
    }

    /// <summary>Auto-hides <paramref name="toolWindow"/> to its remembered side strip.</summary>
    public void Hide(ToolWindow toolWindow)
    {
        ArgumentNullException.ThrowIfNull(toolWindow);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.HideOp(toolWindow));
        OnMutationQueued?.Invoke();
    }

    /// <summary>Restores <paramref name="content"/> from its hidden state into its previous container.</summary>
    public void Show(DockableContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.ShowOp(content));
        OnMutationQueued?.Invoke();
    }

    /// <summary>Closes <paramref name="content"/>. For ToolWindows with <c>CanHide</c>, hides instead.</summary>
    public void Close(DockableContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.CloseOp(content));
        OnMutationQueued?.Invoke();
    }

    /// <summary>Activates (focuses) <paramref name="content"/>.</summary>
    public void Activate(DockableContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.ActivateOp(content));
        OnMutationQueued?.Invoke();
    }

    /// <summary>Pins <paramref name="toolWindow"/> to <paramref name="side"/>.</summary>
    /// <remarks>Spec 045 §5.3.6 — strategies use this to route by category.</remarks>
    public void PinToSide(ToolWindow toolWindow, DockSide side)
    {
        ArgumentNullException.ThrowIfNull(toolWindow);
        ThrowIfOffThread();
        Pending.Add(new PendingMutation.PinToSideOp(toolWindow, side));
        OnMutationQueued?.Invoke();
    }

    // Pending mutation queue — the reconciler drains it on each render pass.
    // Tests and strategies read this to verify decisions.
    internal List<PendingMutation> Pending { get; } = new();

    /// <summary>
    /// Invoked synchronously after each mutator queues into <see cref="Pending"/>.
    /// The Phase-2 native host installs a callback that bumps a re-render
    /// tick so the reconciler drains the queue on the next render pass.
    /// Null when the model is detached (unit-test usage or pre-mount).
    /// </summary>
    /// <remarks>Spec 045 §2.16 — drain trigger.</remarks>
    internal Action? OnMutationQueued { get; set; }

    /// <summary>
    /// Spec 045 §4.2 cross-window dock-in. Invoked by a different window's
    /// drop-target overlay (notably <see cref="DockFloatingWindow"/>) when
    /// a pane belonging to this host's layout was dropped into a tab group
    /// in that other window. The native host wires a closure that removes
    /// the pane from this host's effective layout, stores the override,
    /// fires <c>OnContentFloated</c> / <c>OnLiveLayoutChanged</c>, and
    /// emits a diagnostics op-log entry. Null when the model is detached.
    /// </summary>
    /// <remarks>
    /// The receiving overlay is responsible for adding the pane into its
    /// own tab group BEFORE invoking this hook; calling order matters
    /// only inasmuch as the source removal and destination insertion
    /// both run synchronously on the UI thread.
    /// </remarks>
    internal Action<DockableContent>? OnExternalCrossWindowDrop { get; set; }
}

/// <summary>
/// Sealed algebra of model mutations queued for the reconciler to apply on
/// the next render pass. Internal — exposed to strategy tests via
/// <see cref="DockHostModel.Pending"/> through InternalsVisibleTo.
/// </summary>
internal abstract record PendingMutation
{
    internal sealed record DockOp(DockableContent Content, DockTarget Target) : PendingMutation;
    internal sealed record FloatOp(DockableContent Content) : PendingMutation;
    internal sealed record HideOp(ToolWindow ToolWindow) : PendingMutation;
    internal sealed record ShowOp(DockableContent Content) : PendingMutation;
    internal sealed record CloseOp(DockableContent Content) : PendingMutation;
    internal sealed record ActivateOp(DockableContent Content) : PendingMutation;
    internal sealed record PinToSideOp(ToolWindow ToolWindow, DockSide Side) : PendingMutation;
}
