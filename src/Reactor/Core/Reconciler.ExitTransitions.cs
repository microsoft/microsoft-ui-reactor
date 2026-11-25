using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Exit-transition bookkeeping for controls removed from a child collection.
public sealed partial class Reconciler
{
    /// <summary>
    /// Unmounts and returns all descendants + root to the pool.
    /// Call AFTER the root has been detached from the visual tree.
    /// Collects all controls first, then pools bottom-up so DetachFromParent
    /// removes children before parents clear their collections.
    /// </summary>
    // Controls still in a child collection only while their exit transition plays (removal is
    // deferred to its completion). ChildReconciler steps past them: they belong to no element of
    // the old child list, so they would otherwise misalign it with the collection.
    // Each exit is keyed to a token, so a completion that outlived its exit (the control was torn
    // down with an ancestor, pooled and possibly rented again) can tell it is no longer current.
    private Dictionary<UIElement, object>? _exitingControls;

    internal bool HasExitingControls => _exitingControls is { Count: > 0 };

    internal bool IsExiting(UIElement control) => _exitingControls?.ContainsKey(control) == true;

    // Up to this many in-flight exits, probing the collection once per exit beats reading it.
    // A probe is one native IndexOf (a pointer scan, no managed allocation); a read is a
    // WinRT call per child that allocates its projection. Past it the probes' combined native
    // scans approach the cost of one managed pass, and only a mass exit gets there.
    internal const int ExitProbeLimit = 64;

    /// <summary>
    /// Whether <paramref name="children"/> holds a control that is still playing its exit.
    /// Probes the collection for each in-flight exit (one native IndexOf apiece) rather than
    /// reading every child: each projected read is a WinRT call that allocates, and an exit
    /// playing in one panel would otherwise tax every keyed panel the reconciler visits.
    /// </summary>
    internal bool HoldsExitingControlIn(IChildCollection children)
    {
        if (_exitingControls is not { Count: > 0 } exiting) return false;
        if (exiting.Count <= ExitProbeLimit)
        {
            foreach (var control in exiting.Keys)
                if (children.IndexOf(control) >= 0) return true;
            return false;
        }
        int count = children.Count;
        for (int i = 0; i < count; i++)
            if (exiting.ContainsKey(children.Get(i))) return true;
        return false;
    }

    // An exit animation starts now; the removal (and its unmount count) comes after it, outside the pass.
    private void BeginExit(UIElement control, object exit)
    {
        CountPerfChanges();
        (_exitingControls ??= new())[control] = exit;
    }

    private void EndExit(UIElement control)
    {
        if (_exitingControls is { Count: > 0 } exiting)
            exiting.Remove(control);
    }

    // Ends the exit only while it is still the control's current one. A control torn down with
    // an ancestor mid-exit already ended it and may be back in the pool, possibly rented again.
    private bool EndExit(UIElement control, object exit)
    {
        if (_exitingControls is not { Count: > 0 } exiting
            || !exiting.TryGetValue(control, out var current) || !ReferenceEquals(current, exit))
            return false;
        exiting.Remove(control);
        return true;
    }

    /// <summary>
    /// The <c>.Transition()</c> whose exit side plays when <paramref name="control"/> leaves the
    /// tree, or null. Layers are searched outermost first: a keyed Memo wrapping a flyout
    /// decorator, else the decorator's chain (which reaches a keyed Memo it wraps), then a keyed
    /// Memo over the control, then the tag's chain.
    /// </summary>
    private static ElementTransition? FindExitTransition(UIElement control)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is not ReactorState state) return null;
        if (state.MemoTransition is { } outerMemo && (state.DecoratorElement is null || state.MemoWrapsDecorator))
            return outerMemo;
        ElementTransition? transition = null;
        if (state.DecoratorElement is not null)
            transition = FindMountedElementValue(control, static element => element.ElementTransition);
        if (transition is null && state.MemoTransition is { } memo) return memo;
        if (transition is null && state.Element is { } tag)
            transition = FindMountedElementValue(control, static element => element.ElementTransition, tag);
        return transition?.GetExitTransition() is not null ? transition : null;
    }
    /// <summary>
    /// Removes a child from its collection with exit transition support.
    /// If the child has an ElementTransition with an exit side, the removal is deferred
    /// until the exit animation completes. Otherwise, immediate removal.
    /// </summary>
    internal void RemoveChildWithExitTransition(IChildCollection children, int index)
    {
        var control = children.Get(index);
        var transition = FindExitTransition(control);

        if (transition is not null)
        {
            // Defer removal: play exit animation, then remove + pool on completion.
            var exit = new object();
            BeginExit(control, exit);
            ApplyExitTransition(control, transition, () =>
            {
                // Find current index — it may have shifted if earlier items were removed.
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), control))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(control);
            }, exit);
            return;
        }

        // Spec 042 §6 — under an Animations.Animate ambient and with no
        // per-element exit transition, fade the child out in place before
        // removal so the structural change reads visually. The exit is
        // best-effort: if Composition throws we fall through to the
        // synchronous remove below.
        var ambient = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current;
        object? ambientExit = null;
        if (ambient is { HasEffect: true }
            && TryApplyAmbientExit(control, ambient.Kind, ambientExit = new object(), onComplete: () =>
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), control))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(control);
            }))
        {
            BeginExit(control, ambientExit);
            return;
        }

        children.RemoveAt(index);
        UnmountAndPool(control);
    }

    /// <summary>
    /// Spec 042 §6 — fade-out a child whose exit was triggered under an
    /// <see cref="Animations.Animate"/> transaction with no per-element
    /// transition set. Returns <see langword="true"/> when the fade was
    /// scheduled (the caller must wait for the completion callback to
    /// remove the child); <see langword="false"/> when Composition rejects
    /// the request (headless / disposed contexts) so the caller falls back
    /// to synchronous removal.
    /// </summary>
    private bool TryApplyAmbientExit(UIElement control, AnimationKind kind, object exit, Action onComplete)
    {
        var curve = Microsoft.UI.Reactor.Core.Internal.AnimationKindMap.ToCurve(kind);
        if (curve is null) return false;

        // The fade takes the opacity channel, so a pending opacity restart must not replace it (#1669).
        CancelOpacityRestart(control);
        try
        {
            var visual = AnimationHelper.GetElementVisual(control);
            var compositor = visual.Compositor;
            var batch = compositor.CreateScopedBatch(global::Microsoft.UI.Composition.CompositionBatchTypes.Animation);
            var anim = Microsoft.UI.Reactor.Animation.AnimationHelper.CreateScalarTargetAnimation(compositor, 0f, curve);
            visual.StartAnimation("Opacity", anim);
            batch.End();
            batch.Completed += (_, _) =>
            {
                // As ApplyExitTransition: the pool's visual reset already restored a control
                // whose exit ended early, and it may belong to another renter now.
                if (!EndExit(control, exit)) return;
                // Reset opacity so a recycled element doesn't reappear
                // invisible if it gets re-mounted later.
                visual.Opacity = 1f;
                onComplete();
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces a child at an index with exit transition support on the old child.
    /// If the old child has an exit transition, the new child is inserted immediately
    /// and the old child animates out then gets removed. Otherwise, immediate replace.
    /// Returns true when the old child was re-inserted after the new one to play its exit.
    /// </summary>
    internal bool ReplaceChildWithExitTransition(IChildCollection children, int index, UIElement newControl)
    {
        var oldControl = children.Get(index);
        var transition = FindExitTransition(oldControl);

        if (transition is not null)
        {
            // KNOWN LIMITATION — connected animation does not compose with an exit
            // transition on the SAME element. UnmountAndPool (and therefore
            // PrepareToAnimate) is deferred into the completion callback below, which
            // runs long after both hosts have called FlushConnectedAnimations, so the
            // destination's lookup finds nothing and the connected animation silently
            // no-ops. Preparing the source eagerly here looks like the obvious fix and
            // is not: PrepareToAnimate on a control mid-replace throws a non-teardown
            // exception that escapes this method and aborts the entire reconcile, losing
            // the whole UI update — strictly worse than the animation not playing.
            // Fixing it properly needs a way to hand the snapshot over without preparing
            // against a control the exit transition still owns. Until then, put the
            // connected-animation key and the exit transition on different elements.
            //
            // Replace immediately with the new control so the UI updates.
            children.Replace(index, newControl);
            // Re-insert the old control after the new one for exit animation.
            // It will be positioned by layout but the animation (fade/slide) makes
            // it visually leave. We insert it right after the replacement.
            children.Insert(index + 1, oldControl);
            var exit = new object();
            BeginExit(oldControl, exit);
            ApplyExitTransition(oldControl, transition, () =>
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), oldControl))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(oldControl);
            }, exit);
            return true;
        }

        Unmount(oldControl);
        children.Replace(index, newControl);
        return false;
    }
}
