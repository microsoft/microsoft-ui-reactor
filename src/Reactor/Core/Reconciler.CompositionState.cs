using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: per-control composition and animation bookkeeping (CompositionState).
public sealed partial class Reconciler
{
    /// <summary>
    /// The composition and animation bookkeeping of one native control: who owns each channel of its
    /// handed-out visual, what its transforms rest at, its keyframe triggers and its interaction
    /// states. Hung off <see cref="ReactorState.Composition"/> and allocated by the first composition
    /// feature the control uses, so a control that never animates pays one reference.
    /// </summary>
    /// <remarks>
    /// Lives on the native control's attached state, not in a process-wide table, so it goes with
    /// the control, every managed wrapper sees it, and hosts on different UI threads never share a
    /// table. Once allocated it is never replaced: <see cref="VisualTaken"/> and
    /// <see cref="HandedOutVisual"/> last for the control's lifetime, and the pool's visual reset
    /// (<see cref="ResetForPool"/>) clears only the renter-owned fields.
    /// </remarks>
    internal sealed class CompositionState
    {
        // Set by every AnimationHelper.GetElementVisual and never cleared: the handoff is permanent,
        // and ElementPool picks the control's lane from it (spec 066 §7.2, #1643).
        internal bool VisualTaken;
        // Set by AnimationHelper.GetElementVisual once the handoff succeeded, unlike VisualTaken,
        // which some features set ahead of it. WinUI then rejects facade writes, so the facades
        // read identity and a later handoff returns this visual (the same instance for the
        // control's lifetime) without asking WinUI again.
        internal Visual? HandedOutVisual;
        // Properties written on the handed-out Visual by a modifier. Kept on native
        // attached state so a replacement WinRT wrapper cannot lose cleanup ownership.
        internal AnimateProperty ModifierProperties;
        // The transform the modifiers last wrote to the handed-out Visual. Interaction states
        // and Transition.Scale return to it, whichever element or wrapper declared it.
        internal TransformRest? TransformRest;
        // Track the entry that took over each modifier's Visual channel so
        // removing one named animation can release its deferred reset.
        internal Dictionary<AnimateProperty, KeyframeChannelOwner>? KeyframeChannelOwners;
        internal AnimateProperty DeferredModifierResets;
        // An opacity restart (a keyframe's, an enter fade's) waiting for XAML's push (#1669, #1680).
        // Allocated only while one is pending.
        internal OpacityRestartCheck? OpacityRestart;
        // Who claims each implicit animation key (spec 066 §6). See ImplicitAnimationSlots.
        internal ImplicitAnimationClaims? ImplicitClaims;
        // The delay a parent's .Stagger() gives this child's implicit animations, so a key
        // published after the delays were applied (a new or retimed transition) starts staggered.
        internal TimeSpan? StaggerDelay;
        // Each keyframe entry's previous trigger, by name, and the controller of each channel it
        // started, by compositor property. An unchanged trigger skips playback, so the started
        // channels can differ from the entry's latest definition. Whichever layer declared the
        // entries, the pool's visual reset drops them all (spec 066 §7.3 step 4).
        internal Dictionary<KeyframeName, KeyframeTriggerState>? Keyframes;
        // The .InteractionStates() tracker while its pointer and focus handlers are attached.
        internal InteractionStateTracker? InteractionTracker;
        // Reactor enabled translation on the handed-out visual for a declared Translation channel
        // (spec 066 §4.3). The pool's visual reset disables it again and clears this.
        internal bool TranslationEnabled;
        // The .TransformOrigin pivot expression runs on the handed-out visual's CenterPoint (spec 066
        // §8.2), started by an origin or by navigation's DrillIn. The pool's visual reset stops it.
        internal bool OriginExpressionRunning;
        // Set while a visual-lane renter waits to get its XAML opacity on the visual (ElementPool).
        internal EventHandler<object>? OpacitySyncLayoutHandler;
        // The visual offset the pool's reset wrote, so the renter's sync can tell a later write from it.
        internal global::System.Numerics.Vector3? PoolResetOffset;

        /// <summary>
        /// Spec 066 §7.3 step 4: forgets what the previous renter owned on a visual the pool reset.
        /// The handoff (<see cref="VisualTaken"/>, <see cref="HandedOutVisual"/>) is permanent, and
        /// the renter's opacity sync (<see cref="OpacitySyncLayoutHandler"/>,
        /// <see cref="PoolResetOffset"/>) belongs to the pool, so those stay. The caller also cancels
        /// a pending opacity restart and clears the interaction states, since both detach handlers.
        /// </summary>
        internal void ResetForPool()
        {
            ModifierProperties = default;
            TransformRest = null;
            KeyframeChannelOwners = null;
            DeferredModifierResets = default;
            ImplicitClaims = null;
            StaggerDelay = null;
            Keyframes = null;
            TranslationEnabled = false;
            OriginExpressionRunning = false;
        }
    }

    /// <summary>A keyframe entry's previous trigger and the controllers of the channels it started.</summary>
    internal record struct KeyframeTriggerState(object? Trigger, Dictionary<string, AnimationController>? Started);

    /// <summary>
    /// A keyframe entry's name as a dictionary key. <c>.Keyframes()</c> doesn't reject a null name,
    /// so this keeps one usable as a key; names compare ordinally.
    /// </summary>
    internal readonly record struct KeyframeName(string? Value)
    {
        public static implicit operator KeyframeName(string? value) => new(value);
    }

    /// <summary>The control's composition state, or <see langword="null"/> when it never used a composition feature.</summary>
    internal static CompositionState? GetCompositionState(UIElement uie)
        => (uie.GetValue(ReactorAttached.StateProperty) as ReactorState)?.Composition;

    /// <summary>The control's composition state, allocating it (and the control's attached state) on first use.</summary>
    internal static CompositionState GetOrCreateCompositionState(UIElement uie)
        => GetOrCreateReactorState(uie).Composition ??= new CompositionState();
}
