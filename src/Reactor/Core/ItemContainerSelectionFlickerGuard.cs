using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Mitigation for issue #383 — the multi-select checkmark flickers (fades
/// out/in) on every realized row while the window is drag-resized.
///
/// <para>The mechanism is intrinsic to WinUI's <c>ItemsView</c>: on every
/// <c>OnItemsRepeaterElementClearing</c> / <c>OnItemsRepeaterElementPrepared</c>
/// round-trip it flips <c>ItemContainer.MultiSelectMode</c> between
/// <c>Auto</c> and <c>Auto | Multiple</c>, and each flip runs
/// <c>VisualStateManager.GoToState(&#8230;, useTransitions: true)</c> on the
/// container's <c>MultiSelectStates</c> group — re-running the storyboard that
/// animates <c>PART_SelectionCheckbox.Opacity</c> 0&#8594;1. During a window
/// resize Reactor's host re-runs a full layout pass on every tick, which
/// drives the inner <c>ItemsRepeater</c> to recycle/realize its working set
/// (even though nothing the ItemsView owns actually moved), so the storyboard
/// re-fires dozens of times per gesture and the checkmark visibly flickers.
/// The recycle itself is a benign pooled round-trip; only the
/// <c>useTransitions: true</c> opacity animation is visible.</para>
///
/// <para>We cannot suppress the recycle without regressing layout (the
/// repeater re-realizes on every ancestor arrange pass — see issue #383
/// investigation notes), and <c>ItemContainer.MultiSelectMode</c> /
/// <c>ItemContainerMultiSelectMode</c> are <c>[MUX_INTERNAL]</c> so we cannot
/// observe or set the property directly. Instead, once the container's
/// template is applied, we reach into the public <c>MultiSelectStates</c>
/// <see cref="VisualStateGroup"/>, find the <c>Multiple</c> state's storyboard,
/// and collapse its keyframe times to zero. WinUI keeps calling
/// <c>GoToState("Multiple", useTransitions: true)</c> exactly as before, but a
/// zero-duration storyboard snaps the checkmark to full opacity in the same UI
/// tick instead of fading it — eliminating the visible flicker while leaving
/// selection behavior, the recycle, and the final checkmark visibility
/// unchanged. This is purely a per-instance, one-time mutation of the
/// container's own template storyboard; it never calls <c>GoToState</c> (which
/// is not re-entrancy-safe from inside WinUI's own state transitions) and
/// touches no shared resource.</para>
/// </summary>
internal static class ItemContainerSelectionFlickerGuard
{
    private const string MultiSelectStatesGroup = "MultiSelectStates";
    private const string MultipleState = "Multiple";

    private static readonly KeyTime ZeroKeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero);
    private static readonly Duration ZeroDuration = new(TimeSpan.Zero);

    // One holder per container instance; tracks whether the storyboard has been
    // neutralized so repeated GetElement reuse never re-processes a container.
    //
    // Deliberately a private ConditionalWeakTable rather than the reconciler's
    // ReactorAttached.StateProperty store: this guard is a self-contained,
    // reconciler-agnostic mitigation scoped to ItemContainer, and Ensure is
    // designed to be safe on any ItemContainer — including one that never carries
    // a ReactorState (e.g. a container a raw WinUI ItemsView realizes, as the
    // regression fixture exercises directly). Keeping its one-time bookkeeping
    // isolated avoids coupling to the per-element reconcile state's shape and
    // lifetime. The weak key collapses with the container, same as the attached
    // store would.
    private static readonly ConditionalWeakTable<ItemContainer, Holder> Holders = new();

    /// <summary>
    /// Idempotently arm the flicker guard on a realized <see cref="ItemContainer"/>.
    /// Safe to call on every <c>GetElement</c>; only the first successful call
    /// per container instance does any work.
    /// </summary>
    internal static void Ensure(ItemContainer container)
    {
        if (container is null) return;

        if (!Holders.TryGetValue(container, out var holder))
        {
            holder = new Holder();
            Holders.Add(container, holder);
        }

        if (holder.Neutralized) return;

        // The control template (which carries the MultiSelectStates group) may
        // not be applied yet for a freshly mounted container. Try now for an
        // already-templated (pooled / re-Ensured) container; otherwise wait for
        // Loaded. Repeated Ensure calls keep retrying until it succeeds.
        if (TryNeutralize(container, holder))
        {
            // Succeeded synchronously (or on a later Ensure after a deferred
            // hook) — drop any Loaded handler a prior attempt left attached so
            // it doesn't linger on the container until the next Loaded fires.
            DetachLoaded(container, holder);
            return;
        }

        if (!holder.LoadedHooked)
        {
            holder.LoadedHooked = true;
            holder.OnLoaded = (s, _) =>
            {
                if (s is ItemContainer ic && TryNeutralize(ic, holder))
                    DetachLoaded(ic, holder);
            };
            container.Loaded += holder.OnLoaded;
        }
    }

    private static void DetachLoaded(ItemContainer container, Holder holder)
    {
        if (holder.OnLoaded is null) return;
        container.Loaded -= holder.OnLoaded;
        holder.OnLoaded = null;
    }

    private static bool TryNeutralize(ItemContainer container, Holder holder)
    {
        if (holder.Neutralized) return true;
        if (VisualTreeHelper.GetChildrenCount(container) == 0) return false;
        if (VisualTreeHelper.GetChild(container, 0) is not FrameworkElement templateRoot)
            return false;

        var groups = VisualStateManager.GetVisualStateGroups(templateRoot);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var group = groups[gi];
            if (group.Name != MultiSelectStatesGroup) continue;

            var states = group.States;
            for (int si = 0; si < states.Count; si++)
            {
                var state = states[si];
                if (state.Name != MultipleState) continue;

                if (!TryCollapseStoryboard(state.Storyboard))
                    return false; // timeline transiently in use — retry later
                holder.Neutralized = true;
                return true;
            }

            // Group found but no Multiple state (shouldn't happen) — stop
            // retrying so we don't leak a Loaded handler forever.
            holder.Neutralized = true;
            return true;
        }

        // The template is applied (we have a root) but there is no
        // MultiSelectStates group at all — an unusual ItemContainer template.
        // Give up symmetrically with the no-Multiple-state branch above so we
        // don't retain the Loaded handler and re-walk the tree on every recycle.
        holder.Neutralized = true;
        return true;
    }

    private static bool TryCollapseStoryboard(Storyboard? storyboard)
    {
        if (storyboard is null) return true;

        try
        {
            var children = storyboard.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is DoubleAnimationUsingKeyFrames keyframed)
                {
                    var frames = keyframed.KeyFrames;
                    for (int k = 0; k < frames.Count; k++)
                        frames[k].KeyTime = ZeroKeyTime;
                }
                else
                {
                    // Defensive: any non-keyframe timeline (e.g. a plain
                    // DoubleAnimation) also snaps instantly with a zero duration.
                    children[i].Duration = ZeroDuration;
                }
            }

            return true;
        }
        catch (Exception)
        {
            // The storyboard was mid-flight and WinUI rejected the edit; a
            // later realization of this container will retry.
            return false;
        }
    }

    private sealed class Holder
    {
        // Set once the container's Multiple storyboard has been collapsed (or
        // determined to have no MultiSelectStates/Multiple to collapse). Gates
        // re-entry so repeated GetElement reuse of a pooled container never
        // re-walks the visual tree. Note: this intentionally will NOT re-collapse
        // a storyboard if the container is later re-templated with a fresh
        // (non-zero) Multiple storyboard — not observed in practice for ItemsView
        // containers, and re-templating would re-run the whole arming flow anyway.
        public bool Neutralized;
        public bool LoadedHooked;

        // The deferred one-time Loaded handler, retained so a later synchronous
        // TryNeutralize success (or the handler itself) can detach it instead of
        // leaving it subscribed until the container fires Loaded again.
        public RoutedEventHandler? OnLoaded;
    }
}
