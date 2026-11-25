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

// Reconciler partial: .Keyframes() animations, opacity restarts after XAML pushes, and .ScrollLinked() animations.
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════
    //  Keyframe animation (.Keyframes() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks previous trigger values for keyframe animations, keyed by (UIElement, name),
    /// and the controller of each channel the entry started, by compositor property. An
    /// unchanged trigger skips playback, so the started channels can differ from the entry's
    /// latest definition.
    /// </summary>
    private static readonly Dictionary<(UIElement, string), (object? Trigger, Dictionary<string, AnimationController>? Started)> _keyframeTriggerValues = new();

    internal readonly record struct KeyframeChannelOwner(
        KeyframeEntry[] Scope, KeyframeEntry Entry, AnimationController? DeferredController = null);

    private enum KeyframeReleaseStage { BeforeHandler, BeforeSharedTarget, BeforePlayback }

    private static bool KeyframeTriggerChanged(bool hasPrevious, object? previous, object? trigger)
        => !hasPrevious || !Equals(previous, trigger);

    private static AnimateProperty GetKeyframeProperties(KeyframeAnimationDef definition)
    {
        var properties = default(AnimateProperty);
        foreach (var frame in definition.Keyframes)
        {
            if (frame.Opacity.HasValue) properties |= AnimateProperty.Opacity;
            if (frame.Scale.HasValue) properties |= AnimateProperty.Scale;
            if (frame.Translation.HasValue) properties |= AnimateProperty.Offset;
            if (frame.Rotation.HasValue) properties |= AnimateProperty.Rotation;
        }
        return properties;
    }

    /// <summary>
    /// Checks trigger values and starts keyframe animations when they change.
    /// </summary>
    internal static void ApplyKeyframeAnimations(UIElement uie, KeyframeEntry[] entries, Reconciler? owner = null)
    {
        ElementPool.MarkVisualTaken(uie);
        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var state = GetOrCreateReactorState(uie);
        var names = state.KeyframeNames ??= new(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var key = (uie, entry.Name);
            bool hasPrevious = _keyframeTriggerValues.TryGetValue(key, out var previous);
            bool changed = KeyframeTriggerChanged(hasPrevious, previous.Trigger, entry.Trigger);
            _keyframeTriggerValues[key] = (entry.Trigger, previous.Started);
            names.Add(entry.Name);
            if (!changed) continue;

            var def = entry.Definition;
            var group = compositor.CreateAnimationGroup();
            var properties = GetKeyframeProperties(def);
            if ((properties & AnimateProperty.Opacity) == 0
                && state.OpacityKeyframe is { } playback
                && previous.Started?.TryGetValue("Opacity", out var former) == true && Equals(former, playback.Controller))
            {
                if (state.DeclaredOpacity.Deferred || (state.DeferredModifierResets & AnimateProperty.Opacity) != 0)
                {
                    bool cancelledRestart = Equals(state.OpacityRestart?.Controller, former);
                    CancelOpacityRestart(state);
                    AnimationHelper.FinishOpacityKeyframe(uie, visual, former, cancelledRestart: cancelledRestart);
                    StopKeyframeTarget(visual, "Opacity", former);
                }
                else if (playback.Loops)
                {
                    state.OpacityKeyframe = (former, playback.Loops, true);
                }
            }

            if ((properties & AnimateProperty.Opacity) != 0)
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "Opacity";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Opacity.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Opacity.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Opacity.Value);
                }
                group.Add(anim);
            }

            if ((properties & AnimateProperty.Scale) != 0)
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "Scale";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Scale.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Scale.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Scale.Value);
                }
                group.Add(anim);
            }

            if ((properties & AnimateProperty.Offset) != 0)
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.Duration = def.Duration;
                // Translation, not Offset: Offset is the layout position on a XAML handoff visual (#1432)
                AnimationHelper.EnableTranslation(uie, visual);
                anim.Target = "Translation";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Translation.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Translation.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Translation.Value);
                }
                group.Add(anim);
            }

            if ((properties & AnimateProperty.Rotation) != 0)
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "RotationAngle";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Rotation.HasValue) continue;
                    var radians = float.DegreesToRadians(kf.Rotation.Value);
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, radians, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, radians);
                }
                group.Add(anim);
            }

            // Start the animation group via Visual
            var started = previous.Started ?? new Dictionary<string, AnimationController>(4);
            foreach (CompositionAnimation anim in group)
            {
                if (anim.Target == "Opacity")
                {
                    if (StartOpacityKeyframe(uie, visual, state, anim, def.Loop) is { } opacity)
                    {
                        started["Opacity"] = opacity;
                        RestartKeyframeOpacityAfterXamlPush(uie, visual, entry.Name, anim, opacity, owner);
                    }
                    continue;
                }
                visual.StartAnimation(anim.Target, anim);
                if (visual.TryGetAnimationController(anim.Target) is { } controller)
                    started[anim.Target] = controller;
            }
            _keyframeTriggerValues[key] = (entry.Trigger, started);

            if (properties != default)
                TrackKeyframeChannelOwners(state,
                    properties & (state.CompositionModifierProperties | (state.DeferredModifierResets & AnimateProperty.Opacity)),
                    new KeyframeChannelOwner(entries, entry));
        }
    }

    private static AnimationController? StartOpacityKeyframe(UIElement uie, Visual visual, ReactorState state,
        CompositionAnimation animation, bool loops, bool replayedWithoutOpacity = false)
    {
        // Only the opacity channel gates restoration. Sibling channels may be paused.
        var batch = loops ? null : visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        try
        {
            visual.StartAnimation("Opacity", animation);
            if (visual.TryGetAnimationController("Opacity") is not { } controller) return null;
            state.OpacityKeyframe = (controller, loops, replayedWithoutOpacity);
            if (batch is not null)
                batch.Completed += (_, _) => AnimationHelper.FinishOpacityKeyframe(uie, visual, controller,
                    applyDeferredReset: false);
            return controller;
        }
        finally { batch?.End(); }
    }

    /// <summary>
    /// Restarts a keyframe's opacity animation that XAML's first opacity push stopped, while this
    /// entry still owns the channel (#1669). See <see cref="RestartOpacityAfterXamlPush"/>.
    /// </summary>
    private static void RestartKeyframeOpacityAfterXamlPush(UIElement uie, Visual visual, string name,
        CompositionAnimation animation, AnimationController controller, Reconciler? owner)
    {
        RestartOpacityAfterXamlPush(uie, visual, controller, owner, Owns, Restart);

        bool Owns() => _keyframeTriggerValues.TryGetValue((uie, name), out var entry)
            && entry.Started?.TryGetValue("Opacity", out var owned) == true && Equals(owned, controller);

        AnimationController? Restart()
        {
            if (!_keyframeTriggerValues.TryGetValue((uie, name), out var entry) || entry.Started is not { } started
                || uie.GetValue(ReactorAttached.StateProperty) is not ReactorState state
                || state.OpacityKeyframe is not { } playback || !Equals(playback.Controller, controller)
                || StartOpacityKeyframe(uie, visual, state, animation, playback.Loops,
                    playback.ReplayedWithoutOpacity) is not { } restarted)
                return null;
            started["Opacity"] = restarted;
            if (state.KeyframeChannelOwners?.TryGetValue(AnimateProperty.Opacity, out var channelOwner) == true
                && Equals(channelOwner.DeferredController, controller))
                state.KeyframeChannelOwners[AnimateProperty.Opacity] = channelOwner with { DeferredController = restarted };
            return restarted;
        }
    }

    /// <summary>
    /// XAML writes a changed <see cref="UIElement.Opacity"/> (a local value, a style setter) to the
    /// handed-out visual when it next renders the element, and that write stops whatever animation
    /// runs on the visual's Opacity. An opacity animation started before that frame, such as a
    /// keyframe or an enter fade mounted together with <c>.Opacity(x)</c>, would never play. Once
    /// the element renders, this checks after the next commit and calls <paramref name="restart"/>
    /// when the visual's Opacity animation is gone while <paramref name="owns"/> still holds
    /// (#1669, #1680). A modifier or setter write to the opacity replaces the animation on purpose
    /// and cancels the check, and so does disposing <paramref name="owner"/>.
    /// </summary>
    /// <param name="uie">The element whose visual's opacity animation XAML's push may stop.</param>
    /// <param name="visual">The element's handed-out visual.</param>
    /// <param name="controller">The controller of the animation just started on the visual's Opacity, or <see langword="null"/> for a spring, which has none.</param>
    /// <param name="owner">The reconciler whose disposal cancels the check, when known.</param>
    /// <param name="owns">Whether the starter still owns the channel; <see langword="null"/> means always.</param>
    /// <param name="restart">Starts the animation again and returns its controller, or <see langword="null"/>.</param>
    /// <param name="conceal">
    /// Hides the visual (<see cref="Visual.IsVisible"/>, which XAML doesn't push) until the check
    /// resolves. The frame that pushes would otherwise show the pushed opacity before a restarted
    /// enter fade starts again from 0. An enter fade just started from 0, so hiding its first frame
    /// doesn't show either, even on an element already on screen (the ambient enter's insert).
    /// </param>
    /// <param name="stopped">
    /// Whether the animation was stopped, for one without a controller to check (a spring);
    /// <see langword="null"/> means the channel has no controller left.
    /// </param>
    private static void RestartOpacityAfterXamlPush(UIElement uie, Visual visual, AnimationController? controller,
        Reconciler? owner, Func<bool>? owns, Func<AnimationController?> restart, bool conceal = false, Func<bool>? stopped = null)
    {
        var state = GetOrCreateReactorState(uie);
        CancelOpacityRestart(state);
        var check = new OpacityRestartCheck(controller) { EnterFade = conceal };
        state.OpacityRestart = check;
        Interlocked.Increment(ref s_pendingOpacityRestarts);
        if (owner is not null)
        {
            check.Owner = owner;
            (owner._opacityRestarts ??= new()).Add(state);
        }
        var dispatcher = uie.DispatcherQueue;
        if (conceal && visual.IsVisible)
        {
            visual.IsVisible = false;
            check.Concealed = visual;
        }
        if (uie is FrameworkElement unrendered && !IsRendered(unrendered)) WaitForRender(unrendered);
        else RequestCheck();

        void RequestCheck()
        {
            global::Windows.Foundation.IAsyncAction commit;
            try { commit = visual.Compositor.RequestCommitAsync(); }
            catch (Exception ex) when (IsTeardown(ex))
            {
                Drop();
                return; // No compositor (e.g. unit tests) or it is shutting down.
            }
            // Always queued: Completed runs inline when the commit already finished, which can be
            // before ApplyKeyframeAnimations publishes the entry's started controllers.
            commit.Completed = (_, _) => dispatcher.TryEnqueue(SafeCheck);
        }

        // The check and the waits run from the dispatcher after the update returned, so an
        // exception would end the app. An element or compositor torn down meanwhile drops the check.
        static bool IsTeardown(Exception ex) => ex is global::System.Runtime.InteropServices.COMException
            or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException;

        // Drops this check only: a newer one may have replaced it on the state already.
        void Drop()
        {
            if (ReferenceEquals(state.OpacityRestart, check)) CancelOpacityRestart(state);
            else check.DetachWait();
        }

        void SafeCheck()
        {
            try { Check(); }
            catch (Exception ex) when (IsTeardown(ex)) { Drop(); }
        }

        // XAML pushes when it first renders the element. Under a collapsed ancestor in the live tree
        // nothing renders until that ancestor's Visibility changes, so watch it (and Loaded and
        // Unloaded, for a move to another parent) rather than every layout pass. Off the tree, wait for layout, as
        // the pool's renter sync does.
        void WaitForRender(FrameworkElement fe)
        {
            if (!ReferenceEquals(state.OpacityRestart, check)) return;
            check.DetachWait();
            if (CollapsedAncestor(fe) is { } hidden && (fe.IsLoaded || ElementPool.IsInLiveTree(fe)))
            {
                check.Hidden = hidden;
                check.VisibilityToken = hidden.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,
                    (_, _) => Resume(fe));
                check.WaitTarget = fe;
                check.LoadedHandler = (_, _) => Resume(fe);
                fe.Loaded += check.LoadedHandler;
                // A move to a visible parent in the same turn can skip Loaded while Unloaded is
                // pending (ElementPool.SyncOpacityWhenLoaded), but that Unloaded still comes.
                check.UnloadedHandler = (_, _) => Resume(fe);
                fe.Unloaded += check.UnloadedHandler;
                OpacityRestartWaitsForTests++;
                return;
            }
            WaitForLayout(fe);
        }

        void Resume(FrameworkElement fe)
        {
            try
            {
                if (!Owns()) { Drop(); return; }
                // Already rendering (an Unloaded can arrive after the frame that pushed, with no
                // layout pass left to wait for): check after the next commit. Otherwise wait for layout.
                if (IsRendered(fe)) { check.DetachWait(); RequestCheck(); }
                // A Loaded or Unloaded while the same ancestor is still collapsed changes nothing:
                // its Visibility callback stays armed, rather than falling back to every layout pass.
                else if (check.Hidden is { } waitingOn && ReferenceEquals(CollapsedAncestor(fe), waitingOn)
                    && (fe.IsLoaded || ElementPool.IsInLiveTree(fe))) return;
                else WaitForLayout(fe);
            }
            catch (Exception ex) when (IsTeardown(ex)) { Drop(); }
        }

        void WaitForLayout(FrameworkElement fe)
        {
            check.DetachWait();
            EventHandler<object>? onLayout = null;
            onLayout = (_, _) =>
            {
                try
                {
                    if (!Owns()) { Drop(); return; }
                    if (IsRendered(fe)) { check.DetachWait(); RequestCheck(); }
                    // Laid out but now in the live tree under a collapsed ancestor: watch that instead.
                    else if (fe.IsLoaded || ElementPool.IsInLiveTree(fe)) WaitForRender(fe);
                }
                catch (Exception ex) when (IsTeardown(ex)) { Drop(); }
            };
            check.LayoutHandler = onLayout;
            check.WaitTarget = fe;
            fe.LayoutUpdated += onLayout;
            // Content outside the window's root (a popup, a flyout) is never in the live tree, and
            // no layout pass may follow its Loaded, so Loaded requests the check too. Only while it
            // hasn't loaded yet, and it never re-arms a wait: that would subscribe Loaded again
            // inside the raise that is running.
            if (!fe.IsLoaded)
            {
                check.LoadedHandler = (_, _) =>
                {
                    try
                    {
                        if (!Owns()) { Drop(); return; }
                        if (IsRendered(fe)) { check.DetachWait(); RequestCheck(); }
                    }
                    catch (Exception ex) when (IsTeardown(ex)) { Drop(); }
                };
                fe.Loaded += check.LoadedHandler;
            }
            OpacityRestartWaitsForTests++;
        }

        bool Owns() => ReferenceEquals(state.OpacityRestart, check) && owns?.Invoke() != false;

        void Check()
        {
            if (!Owns()) return;
            if (uie is FrameworkElement fe && !IsRendered(fe))
            {
                WaitForRender(fe);
                return;
            }
            // Reveals a concealed visual in the same turn as the restart below, so no frame shows it
            // between the two.
            CancelOpacityRestart(state);
            // An animation that completed keeps its controller, and every deliberate stop (a modifier
            // or setter write, removal, unmount) cancelled this check, so a missing controller
            // means XAML's push stopped it, whether it loops or not. A spring never has one.
            if (stopped is null ? visual.TryGetAnimationController("Opacity") is not null : !stopped()) return;
            // A spring reports no controller even when it runs, so count the restart itself.
            if (restart() is null && controller is not null) return;
            s_opacityRestartsForTests.GetOrCreateValue(uie).Value++;
        }
    }

    /// <summary>A pending <see cref="RestartOpacityAfterXamlPush"/> check and whatever it waits on.</summary>
    internal sealed class OpacityRestartCheck(AnimationController? controller)
    {
        internal readonly AnimationController? Controller = controller;
        // The reconciler that registered the check, so its disposal can cancel it.
        internal Reconciler? Owner;
        // What the check waits on until the element renders: layout while off the tree, or a
        // collapsed ancestor's Visibility (and the element's Loaded) while in the tree.
        internal FrameworkElement? WaitTarget;
        internal EventHandler<object>? LayoutHandler;
        internal RoutedEventHandler? LoadedHandler;
        internal RoutedEventHandler? UnloadedHandler;
        internal UIElement? Hidden;
        internal long VisibilityToken;
        // The visual an enter fade hid until the check resolves (RestartOpacityAfterXamlPush's conceal).
        internal Visual? Concealed;
        // Whether the check restarts an enter fade, which targets the opacity it started with.
        internal bool EnterFade;

        internal void Reveal()
        {
            if (Concealed is not { } visual) return;
            Concealed = null;
            // Cancellation also runs from teardown (disposal, pool reset), where the compositor may be gone.
            try { visual.IsVisible = true; }
            catch (Exception ex) when (ex is global::System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                Diagnostics.DiagnosticLog.SwallowedError(Diagnostics.LogCategory.Reactor, "OpacityRestart.Reveal", ex);
            }
        }
        internal void DetachWait()
        {
            if (WaitTarget is { } fe)
            {
                if (LayoutHandler is { } onLayout) { fe.LayoutUpdated -= onLayout; OpacityRestartWaitsForTests--; }
                if (LoadedHandler is { } onLoaded) fe.Loaded -= onLoaded;
                if (UnloadedHandler is { } onUnloaded) fe.Unloaded -= onUnloaded;
            }
            if (Hidden is { } hidden)
            {
                hidden.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, VisibilityToken);
                OpacityRestartWaitsForTests--;
            }
            WaitTarget = null;
            LayoutHandler = null;
            LoadedHandler = null;
            UnloadedHandler = null;
            Hidden = null;
        }
    }

    // Loaded alone isn't proof: WinUI can skip it for a control re-added while its Unloaded is
    // pending, and a collapsed ancestor keeps a loaded element out of the render walk.
    private static bool IsRendered(FrameworkElement fe)
        => (fe.IsLoaded || ElementPool.IsInLiveTree(fe)) && CollapsedAncestor(fe) is null;

    private static UIElement? CollapsedAncestor(FrameworkElement fe)
    {
        for (DependencyObject? node = fe; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible } hidden) return hidden;
        return null;
    }

    // Checks still pending, so an unmount with none to cancel pays no attached-state read.
    private static int s_pendingOpacityRestarts;

    /// <summary>
    /// Snapshots a control's opacity and the visual's Opacity value and animation around user code (setters,
    /// <c>.OnMount</c>, <c>.OnUpdate</c>). A change to either is a deliberate opacity write, so it
    /// cancels a pending opacity restart as a modifier's write does (#1669). Free when none is pending.
    /// </summary>
    internal readonly struct OpacityRestartProbe
    {
        private readonly UIElement? _element;
        private readonly ReactorState? _state;
        private readonly OpacityRestartCheck? _check;
        private readonly double _opacity;
        private readonly AnimationController? _controller;
        private readonly float _visualOpacity;

        internal OpacityRestartProbe(UIElement element)
        {
            if (Volatile.Read(ref s_pendingOpacityRestarts) == 0
                || element.GetValue(ReactorAttached.StateProperty) is not ReactorState { OpacityRestart: not null } state)
            {
                this = default;
                return;
            }
            _element = element;
            _state = state;
            _check = state.OpacityRestart;
            _opacity = element.Opacity;
            // The restarted animation already took the visual, so this has no handoff side effects.
            var visual = ElementCompositionPreview.GetElementVisual(element);
            _controller = visual.TryGetAnimationController("Opacity");
            _visualOpacity = visual.Opacity;
        }

        internal void Complete()
        {
            // A nested update (a decorator's target) may have armed a newer check meanwhile. Its
            // keyframe is what changed the controller, so it stays.
            if (_element is null || _state is not { } state || !ReferenceEquals(state.OpacityRestart, _check)) return;
            var visual = ElementCompositionPreview.GetElementVisual(_element);
            // The visual's base value counts too: once XAML's push stopped the keyframe, a write to the
            // visual leaves no controller to compare.
            if (global::System.Math.Abs(_element.Opacity - _opacity) > 1e-9
                || !Equals(visual.TryGetAnimationController("Opacity"), _controller)
                || global::System.Math.Abs(visual.Opacity - _visualOpacity) > 1e-6f)
                CancelOpacityRestart(state);
        }
    }

    /// <summary>
    /// The <c>.Opacity()</c> modifier was removed while an enter fade waits for the element's first
    /// frame. The fade targets the removed value, and with XAML's default left unchanged no push would
    /// stop it, so stop it at the element's opacity now, as a modifier write does (#1680). A pending
    /// keyframe restart stays: the keyframe still owns the channel.
    /// </summary>
    internal static void StopPendingEnterFade(UIElement uie)
    {
        if (Volatile.Read(ref s_pendingOpacityRestarts) == 0
            || uie.GetValue(ReactorAttached.StateProperty) is not ReactorState { OpacityRestart.EnterFade: true } state)
            return;
        CancelOpacityRestart(state);
        // The fade already took the visual, so this has no handoff side effects.
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        WithoutImplicitAnimations(visual, () => visual.Opacity = (float)uie.Opacity);
    }

    internal static void CancelOpacityRestart(UIElement uie)
    {
        if (Volatile.Read(ref s_pendingOpacityRestarts) == 0) return;
        if (uie.GetValue(ReactorAttached.StateProperty) is ReactorState state) CancelOpacityRestart(state);
    }

    /// <summary>
    /// Drops a pending <see cref="RestartOpacityAfterXamlPush"/> check and detaches whatever it waits
    /// on, so a control that never renders isn't kept alive by a layout or property callback. A visual
    /// the check concealed is shown again, whatever took the opacity over.
    /// </summary>
    internal static void CancelOpacityRestart(ReactorState state)
    {
        if (state.OpacityRestart is not { } check) return;
        state.OpacityRestart = null;
        Interlocked.Decrement(ref s_pendingOpacityRestarts);
        check.Owner?._opacityRestarts?.Remove(state);
        check.DetachWait();
        check.Reveal();
    }

    // Pending opacity restart checks by control, so disposal (which drops the tree without
    // unmounting it) can cancel them and detach their waits. Created by the first one.
    private HashSet<ReactorState>? _opacityRestarts;

    private void CancelOpacityRestarts()
    {
        if (_opacityRestarts is not { Count: > 0 } checks) return;
        foreach (var state in new List<ReactorState>(checks))
            CancelOpacityRestart(state);
        checks.Clear();
    }

    // Test-only seams (InternalsVisibleTo Reactor.AppTests.Host), kept out of ReactorState so
    // product controls don't carry them: restarts per control, and the waits attached right now.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, global::System.Runtime.CompilerServices.StrongBox<int>>
        s_opacityRestartsForTests = new();

    internal static int OpacityRestartsForTests(UIElement uie)
        => s_opacityRestartsForTests.TryGetValue(uie, out var count) ? count.Value : 0;

    internal static int OpacityRestartWaitsForTests;

    internal static bool OpacityRestartPendingForTests(UIElement uie)
        => uie.GetValue(ReactorAttached.StateProperty) is ReactorState { OpacityRestart: not null };

    internal static bool OpacityRestartWaitsOnLayoutForTests(UIElement uie)
        => uie.GetValue(ReactorAttached.StateProperty) is ReactorState { OpacityRestart.LayoutHandler: not null };

    // Test-only seam, same visibility: the controller the named entry started on a compositor property,
    // so a fixture can tell its keyframe from another animation on the same channel.
    internal static AnimationController? KeyframeControllerForTests(UIElement uie, string name, string property)
        => _keyframeTriggerValues.TryGetValue((uie, name), out var entry)
            && entry.Started?.TryGetValue(property, out var controller) == true ? controller : null;

    private static void TrackKeyframeChannelOwners(ReactorState state, AnimateProperty channels, KeyframeChannelOwner owner)
    {
        if (channels == default) return;
        var tracked = owner.Entry.Definition.Loop ? channels : channels & state.DeferredModifierResets;
        if (tracked == default && state.KeyframeChannelOwners is null) return;
        var owners = state.KeyframeChannelOwners ??= new(4);
        Track(AnimateProperty.Opacity);
        Track(AnimateProperty.Scale);
        Track(AnimateProperty.Rotation);
        Track(AnimateProperty.Offset);

        void Track(AnimateProperty property)
        {
            if ((channels & property) == 0) return;
            if ((tracked & property) != 0) owners[property] = owner;
            else owners.Remove(property);
        }
    }

    private static void ReleaseRemovedKeyframeChannels(UIElement uie, KeyframeEntry[] previous, KeyframeEntry[]? current,
        KeyframeReleaseStage stage)
    {
        if (uie.GetValue(ReactorAttached.StateProperty) is not ReactorState { KeyframeChannelOwners: { Count: > 0 } owners } state)
            return;

        Release(AnimateProperty.Opacity, "Opacity");
        Release(AnimateProperty.Scale, "Scale");
        Release(AnimateProperty.Rotation, "Rotation");
        Release(AnimateProperty.Offset, "Translation");

        void Release(AnimateProperty property, string name)
        {
            if (!owners.TryGetValue(property, out var owner) || !ReferenceEquals(owner.Scope, previous)) return;
            if (current is not null)
            {
                var nameKey = owner.Entry.Name;
                var key = (uie, nameKey);
                bool hasTrigger = _keyframeTriggerValues.TryGetValue(key, out var triggerState);
                var trigger = triggerState.Trigger;
                bool retained = false;
                bool replayed = false;
                // Playback reads and advances the trigger for every matching entry in order.
                // Predict those replays without changing what playback will see.
                foreach (var entry in current)
                {
                    if (entry.Name != nameKey) continue;
                    retained = true;
                    if (stage == KeyframeReleaseStage.BeforeSharedTarget) break;
                    bool changed = KeyframeTriggerChanged(hasTrigger, trigger, entry.Trigger);
                    hasTrigger = true;
                    trigger = entry.Trigger;
                    if (!changed) continue;
                    replayed = true;
                    if ((GetKeyframeProperties(entry.Definition) & property) != 0)
                    {
                        owners[property] = owner with { Scope = current };
                        return;
                    }
                }
                if (!retained)
                {
                    _keyframeTriggerValues.Remove(key);
                    state.KeyframeNames?.Remove(nameKey);
                }
                else if (!replayed)
                {
                    owners[property] = owner with { Scope = current };
                    return;
                }
            }
            owners.Remove(property);
            if ((state.DeferredModifierResets & property) != 0)
                AnimationHelper.ResetCompositionModifier(uie, name,
                    stage == KeyframeReleaseStage.BeforePlayback ? owner.DeferredController : null);
        }
    }

    // ownedOnly stops only the channels whose running animation is still the one these entries
    // started. A retained control passes true, so removing keyframes doesn't stop an animation
    // another feature started on that channel. Unmount passes false and stops every channel a
    // keyframe can animate. Entries whose name is in retained stay, and their playback decides
    // what keeps running.
    internal static void ClearKeyframeAnimations(UIElement uie, KeyframeEntry[] entries, bool ownedOnly = false,
        KeyframeEntry[]? retained = null)
    {
        Visual? owner = null;
        bool noCompositor = false;
        foreach (var entry in entries)
        {
            if (retained is not null && HasKeyframeEntry(retained, entry.Name)) continue;
            if (!_keyframeTriggerValues.Remove((uie, entry.Name), out var state)) continue;
            bool cancelledOpacityRestart = false;
            if (uie.GetValue(ReactorAttached.StateProperty) is ReactorState reactorState)
            {
                reactorState.KeyframeNames?.Remove(entry.Name);
                if (state.Started?.TryGetValue("Opacity", out var opacity) == true
                    && Equals(reactorState.OpacityRestart?.Controller, opacity))
                {
                    cancelledOpacityRestart = true;
                    CancelOpacityRestart(reactorState);
                }
            }
            if (!ownedOnly || noCompositor || state.Started is not { } started)
                continue;
            // Started controllers mean the visual was already handed out. Look it up without
            // moving facade transforms, which the caller does after the handler's setters.
            try { owner ??= ElementCompositionPreview.GetElementVisual(uie); }
            catch { noCompositor = true; continue; }
            foreach (var (property, controller) in started)
            {
                if (property == "Opacity")
                    AnimationHelper.FinishOpacityKeyframe(uie, owner, controller, cancelledRestart: cancelledOpacityRestart);
                StopKeyframeTarget(owner, property, controller);
            }
        }
        if (ownedOnly) return;
        if (uie.GetValue(ReactorAttached.StateProperty) is ReactorState unmounted)
            unmounted.OpacityKeyframe = null;

        // Stop composition animations so they don't keep running on pooled/unmounted controls.
        // StopAnimation is a no-op on an existing property with no running animation.
        Visual visual;
        try { visual = AnimationHelper.GetElementVisual(uie); }
        catch { return; /* No compositor (e.g. unit tests) */ }

        // Each stop is independent, so one failure can't leave a later channel running (#1641).
        StopKeyframeTarget(visual, "Opacity");
        StopKeyframeTarget(visual, "Scale");
        // Translation is a property-set entry that exists only once translation was enabled.
        // StopAnimation on a missing entry throws ArgumentException (spec 066 §7.3 step 1).
        if (visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded)
            StopKeyframeTarget(visual, "Translation");
        StopKeyframeTarget(visual, "RotationAngle");
    }

    private static void StopKeyframeTarget(Visual visual, string property, AnimationController? owned = null)
    {
        try
        {
            if (owned is null || Equals(visual.TryGetAnimationController(property), owned))
                visual.StopAnimation(property);
        }
        catch { /* One failed stop must not skip the others. */ }
    }

    private static bool HasKeyframeEntry(KeyframeEntry[] entries, string name)
    {
        foreach (var entry in entries)
            if (entry.Name == name) return true;
        return false;
    }

    /// <summary>
    /// Spec 066 §7.3 step 4: drops the composition bookkeeping of a control whose visual was
    /// reset for the pool, so the next renter starts without the previous renter's ownership.
    /// Keyframe trigger entries go by the names the control recorded, so a layer unmount didn't
    /// reach (a keyed <c>Memo</c> over the control) leaves none behind.
    /// </summary>
    internal static void ForgetCompositionState(UIElement uie)
    {
        if (uie.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.CompositionModifierProperties = default;
            state.TransformRest = null;
            state.KeyframeChannelOwners = null;
            state.DeferredModifierResets = default;
            state.OpacityKeyframe = null;
            state.DeclaredOpacity = default;
            CancelOpacityRestart(state);
            state.ImplicitClaims = null;
            state.StaggerDelay = null;
            state.TranslationEnabled = false;
            state.OriginExpressionRunning = false;
            if (state.KeyframeNames is { } names)
            {
                foreach (var name in names) _keyframeTriggerValues.Remove((uie, name));
                state.KeyframeNames = null;
            }
        }
        ClearInteractionStates(uie, restoreBrushes: true);
    }

    private static KeyframeEntry[]? WithSharedKeyframes(KeyframeEntry[]? own, KeyframeEntry[]? shared)
        => shared is null || ReferenceEquals(own, shared) ? own : own is null ? shared : [.. own, .. shared];

    // ════════════════════════════════════════════════════════════════
    //  Scroll-linked expression animation (.ScrollLinked() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies scroll-linked expression animations to an element's Visual.
    /// </summary>
    internal static void ApplyScrollAnimation(UIElement uie, ScrollAnimationConfig config)
    {
        if (config.ScrollViewer is null) return;
        ElementPool.MarkVisualTaken(uie);
        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var scrollPropertySet = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(config.ScrollViewer);

        foreach (var expr in config.Expressions)
        {
            // An expression has no AnimationController, so a pending opacity restart would
            // read the channel as free and replace it (#1669).
            if (expr.Property == "Opacity") CancelOpacityRestart(GetOrCreateReactorState(uie));
            var animation = compositor.CreateExpressionAnimation(expr.Expression);
            animation.SetReferenceParameter("scroll", scrollPropertySet);
            visual.StartAnimation(expr.Property, animation);
        }
    }

    internal static void ClearScrollAnimation(UIElement uie, ScrollAnimationConfig config)
    {
        var visual = AnimationHelper.GetElementVisual(uie);
        foreach (var expr in config.Expressions)
            visual.StopAnimation(expr.Property);
    }
}
