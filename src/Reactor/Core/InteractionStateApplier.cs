using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The <c>.InteractionStates()</c> modifier: attaches pointer and focus handlers to a control and
/// animates its visual and brushes between the Normal, PointerOver, Pressed and Focused states.
/// The per-control tracker lives on <see cref="Reconciler.CompositionState.InteractionTracker"/>.
/// </summary>
internal static class InteractionStateApplier
{
    /// <summary>
    /// The live tracker count, for the test seam below. The trackers themselves live on each
    /// control's <see cref="Reconciler.CompositionState.InteractionTracker"/>; hosts on different
    /// UI threads update this concurrently, hence the interlocked writes.
    /// </summary>
    private static int s_interactionTrackerCount;

    // Test-only accessor (InternalsVisibleTo Reactor.AppTests.Host).
    internal static int InteractionTrackerCountForTests => Volatile.Read(ref s_interactionTrackerCount);

    private static InteractionStateTracker? InteractionTrackerOf(object sender)
        => sender is UIElement uie ? Reconciler.GetCompositionState(uie)?.InteractionTracker : null;

    // Test-only: drives a hover-in / hover-out transition without synthesizing pointer input.
    internal static void SimulatePointerOverForTests(UIElement uie, bool isOver)
    {
        if (InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, isOver ? InteractionState.PointerOver : InteractionState.Normal);
    }

    // Test-only: drives a press / release (back to PointerOver) without synthesizing pointer input.
    internal static void SimulatePressedForTests(UIElement uie, bool isPressed)
    {
        if (InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, isPressed ? InteractionState.Pressed : InteractionState.PointerOver);
    }

    internal sealed class InteractionStateTracker
    {
        public InteractionState CurrentState;
        public InteractionStatesConfig Config = null!;
        // Background/Foreground keep the raw local value (DependencyProperty.UnsetValue when there was
        // none) a state restores when it ends, captured when the state first writes over the
        // property. With no local value the property is cleared, so a {ThemeResource} Style
        // setter or the default style shows again. Active* is the brush the state wrote:
        // anything else there when the state ends came from another writer and stands.
        public object? NormalBackground;
        public Brush? ActiveBackground;
        public bool BackgroundOverrideApplied;
        public object? NormalForeground;
        public Brush? ActiveForeground;
        public bool ForegroundOverrideApplied;
        public Brush? NormalBorderBrush;
        public Brush? ActiveBorderBrush;
        public bool NormalBorderBrushHasLocalValue;
        public bool BorderBrushOverrideApplied;
        public bool PreserveUpdatedBorderBrush;
    }

    internal enum InteractionState { Normal, PointerOver, Pressed, Focused }

    internal static void ReleaseRemovedInteractionForeground(UIElement uie, InteractionStatesConfig? config,
        bool writesForegroundAfterSetters = false)
    {
        if (uie is not FrameworkElement element
            || InteractionTrackerOf(element) is not { ForegroundOverrideApplied: true } tracker
            || GetStateValues(config, tracker.CurrentState)?.Foreground is not null
            || ForegroundModifier.GetProperty(element) is not { } property)
            return;

        if (writesForegroundAfterSetters)
        {
            // The common modifier will replace or clear the property after setters.
            tracker.NormalForeground = null;
            tracker.ActiveForeground = null;
            tracker.ForegroundOverrideApplied = false;
            return;
        }

        ApplyInteractionBrush(element, property, stateBrush: null,
            ref tracker.NormalForeground, ref tracker.ActiveForeground, ref tracker.ForegroundOverrideApplied);
    }

    internal static bool TryReplaceInteractionForegroundRestoreValue(FrameworkElement element, Brush previous, Brush replacement)
    {
        if (ForegroundModifier.GetProperty(element) is not { } property
            || InteractionTrackerOf(element) is not { ForegroundOverrideApplied: true } tracker
            || !ReferenceEquals(tracker.NormalForeground, previous))
            return false;

        tracker.NormalForeground = replacement;
        return ReferenceEquals(element.ReadLocalValue(property), tracker.ActiveForeground);
    }

    internal static bool TryPreserveInteractionForeground(FrameworkElement element, DependencyProperty property, object restoreValue)
    {
        if (InteractionTrackerOf(element) is not { ForegroundOverrideApplied: true } tracker) return false;
        // A common write can reuse the active brush after a setter displaced it. Always
        // refresh the restore value, but leave the native write to the caller when unowned.
        tracker.NormalForeground = restoreValue;
        return ReferenceEquals(element.ReadLocalValue(property), tracker.ActiveForeground);
    }

    internal static void PreserveUpdatedBorderBrush(UIElement element)
    {
        if (InteractionTrackerOf(element) is { BorderBrushOverrideApplied: true } tracker)
            tracker.PreserveUpdatedBorderBrush = true;
    }

    internal static void UpdateInteractionBackgroundRestoreValue(UIElement element, object restoreValue)
    {
        if (InteractionTrackerOf(element) is { BackgroundOverrideApplied: true } tracker)
            tracker.NormalBackground = restoreValue;
    }

    internal static void UpdateInteractionBorderBrushRestoreValue(UIElement element, Brush restoreValue)
    {
        if (InteractionTrackerOf(element) is { BorderBrushOverrideApplied: true } tracker)
        {
            tracker.NormalBorderBrush = restoreValue;
            tracker.NormalBorderBrushHasLocalValue = true;
        }
    }

    internal static bool OwnsInteractionBorderBrush(FrameworkElement element, Brush? activeBrush)
        => GetBorderBrushProperty(element) is { } property && ReferenceEquals(element.ReadLocalValue(property), activeBrush);

    private static DependencyProperty? GetBorderBrushProperty(FrameworkElement element) => element switch
    {
        WinUI.Control => WinUI.Control.BorderBrushProperty,
        WinUI.Border => WinUI.Border.BorderBrushProperty,
        WinUI.Grid => WinUI.Grid.BorderBrushProperty,
        WinUI.StackPanel => WinUI.StackPanel.BorderBrushProperty,
        WinUI.RelativePanel => WinUI.RelativePanel.BorderBrushProperty,
        _ => null,
    };

    /// <summary>
    /// Sets up or updates InteractionStates on an element. Registers pointer event handlers
    /// on first setup; updates cached config on subsequent calls.
    /// </summary>
    internal static void ApplyInteractionStates(UIElement uie, InteractionStatesConfig config)
    {
        ElementPool.MarkVisualTaken(uie);
        var composition = Reconciler.GetOrCreateCompositionState(uie);
        if (composition.InteractionTracker is not { } tracker)
        {
            tracker = new InteractionStateTracker();
            composition.InteractionTracker = tracker;
            Interlocked.Increment(ref s_interactionTrackerCount);

            // Register handlers
            uie.PointerEntered += OnInteractionPointerEntered;
            uie.PointerExited += OnInteractionPointerExited;
            uie.PointerPressed += OnInteractionPointerPressed;
            uie.PointerReleased += OnInteractionPointerReleased;
            uie.PointerCaptureLost += OnInteractionPointerCaptureLost;
            uie.GotFocus += OnInteractionGotFocus;
            uie.LostFocus += OnInteractionLostFocus;
        }

        tracker.Config = config;
    }

    /// <summary>
    /// Removes InteractionStates from an element, unregistering handlers and clearing state.
    /// </summary>
    internal static void ClearInteractionStates(UIElement uie, bool restoreBrushes = false)
    {
        if (Reconciler.GetCompositionState(uie) is not { InteractionTracker: { } tracker } composition) return;
        composition.InteractionTracker = null;
        Interlocked.Decrement(ref s_interactionTrackerCount);

        // A control unmounted mid-hover still carries the state's brushes. Put the normal ones back,
        // so a pooled control does not hand a hover color to its next renter. Not on an update that
        // drops the states: ApplyModifiers has already written that update's brushes.
        if (restoreBrushes && uie is FrameworkElement fe)
            ApplyInteractionBrushes(fe, tracker, values: null);

        uie.PointerEntered -= OnInteractionPointerEntered;
        uie.PointerExited -= OnInteractionPointerExited;
        uie.PointerPressed -= OnInteractionPointerPressed;
        uie.PointerReleased -= OnInteractionPointerReleased;
        uie.PointerCaptureLost -= OnInteractionPointerCaptureLost;
        uie.GotFocus -= OnInteractionGotFocus;
        uie.LostFocus -= OnInteractionLostFocus;
    }

    private static void OnInteractionPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, InteractionState.PointerOver);
    }

    private static void OnInteractionPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void OnInteractionPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, InteractionState.Pressed);
    }

    private static void OnInteractionPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
        {
            // Released goes back to PointerOver (pointer is still over the element)
            TransitionToState(uie, tracker, InteractionState.PointerOver);
        }
    }

    private static void OnInteractionPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void OnInteractionGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker
            && tracker.Config.Focused is not null)
            TransitionToState(uie, tracker, InteractionState.Focused);
    }

    private static void OnInteractionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement uie && InteractionTrackerOf(uie) is { } tracker)
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void TransitionToState(UIElement uie, InteractionStateTracker tracker, InteractionState newState)
    {
        if (tracker.CurrentState == newState) return;
        tracker.CurrentState = newState;

        var config = tracker.Config;
        var curve = config.Curve ?? Curve.Ease(200, Easing.Standard);

        // Resolve effective state values (Pressed inherits from PointerOver)
        var values = GetStateValues(config, newState);

        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;

        // Normal returns the transforms to the values the element's modifiers wrote, not to
        // identity, so a hover-out does not discard them (#1613). The visual was taken above,
        // so a facade transform has already moved and been recorded.
        var rest = Reconciler.RestTransformOf(uie);

        // Compositor properties — animate via Visual. The state takes the opacity channel, so a
        // pending opacity restart must not replace it (#1669).
        Reconciler.CancelOpacityRestart(uie);
        var targetOpacity = values?.Opacity ?? 1.0f;
        var opacityAnim = AnimationHelper.CreateScalarTargetAnimation(compositor, targetOpacity, curve);
        visual.StartAnimation("Opacity", opacityAnim);

        if (values?.Scale.HasValue == true || values?.ScaleV.HasValue == true || newState == InteractionState.Normal)
        {
            var targetScale = values?.ScaleV ?? (values?.Scale is { } uniform ? new Vector3(uniform, uniform, 1f) : rest.Scale);
            var scaleAnim = AnimationHelper.CreateVector3TargetAnimation(compositor, targetScale, curve);
            visual.StartAnimation("Scale", scaleAnim);
        }

        // Translation only for a declared Translation channel (spec 066 §4.3): a state that moves it,
        // or Normal after something enabled it. Enabling it otherwise would make WinUI ignore a
        // facade Translation drag on the element (#1642).
        if (values?.Translation.HasValue == true
            || (newState == InteractionState.Normal && AnimationHelper.TranslationEnabled(uie)))
        {
            // Animate Translation, not Offset: on a XAML element's handoff visual, Offset is the
            // layout position, so animating it to zero pulls the element to its parent's origin.
            AnimationHelper.EnableTranslation(uie, visual);
            var targetTranslation = values?.Translation ?? rest.Translation;
            var translationAnim = AnimationHelper.CreateVector3TargetAnimation(compositor, targetTranslation, curve);
            visual.StartAnimation("Translation", translationAnim);
        }

        if (values?.Rotation.HasValue == true || newState == InteractionState.Normal)
        {
            var targetRotation = float.DegreesToRadians(values?.Rotation ?? rest.RotationDegrees);
            var rotationAnim = AnimationHelper.CreateScalarTargetAnimation(compositor, targetRotation, curve);
            visual.StartAnimation("RotationAngle", rotationAnim);
        }

        if (uie is FrameworkElement fe)
            ApplyInteractionBrushes(fe, tracker, values);
    }

    /// <summary>
    /// Brush properties — direct set. When a state ends, what the property held before the
    /// state wrote over it comes back: an explicit local brush is restored, and a property
    /// with no local value is cleared, so a ThemeRef's {ThemeResource} Style setter takes over
    /// again instead of a brush captured earlier that would pin the theme it had then.
    /// <paramref name="values"/> null is the Normal state.
    /// </summary>
    private static void ApplyInteractionBrushes(FrameworkElement fe, InteractionStateTracker tracker, InteractionStateValues? values)
    {
        var backgroundProperty = fe switch
        {
            WinUI.Panel => WinUI.Panel.BackgroundProperty,
            WinUI.Control => WinUI.Control.BackgroundProperty,
            WinUI.Border => WinUI.Border.BackgroundProperty,
            _ => null,
        };
        if (backgroundProperty is not null)
            ApplyInteractionBrush(fe, backgroundProperty, values?.Background,
                ref tracker.NormalBackground, ref tracker.ActiveBackground, ref tracker.BackgroundOverrideApplied);

        if (ForegroundModifier.GetProperty(fe) is { } foregroundProperty)
            ApplyInteractionBrush(fe, foregroundProperty, values?.Foreground,
                ref tracker.NormalForeground, ref tracker.ActiveForeground, ref tracker.ForegroundOverrideApplied);

        var borderProperty = GetBorderBrushProperty(fe);
        if (borderProperty is not null)
        {
            if (values?.BorderBrush is { } borderBrush)
            {
                if (!tracker.BorderBrushOverrideApplied || (tracker.PreserveUpdatedBorderBrush
                    && !ReferenceEquals(fe.ReadLocalValue(borderProperty), tracker.ActiveBorderBrush)))
                {
                    // Capture the latest normal value before applying an override.
                    tracker.NormalBorderBrush = (Brush?)fe.GetValue(borderProperty);
                    tracker.NormalBorderBrushHasLocalValue = fe.ReadLocalValue(borderProperty) != DependencyProperty.UnsetValue;
                    tracker.BorderBrushOverrideApplied = true;
                }
                tracker.ActiveBorderBrush = borderBrush;
                fe.SetValue(borderProperty, borderBrush);
            }
            else if (tracker.BorderBrushOverrideApplied)
            {
                // A Flyout can keep the tracker alive after its target writes a new normal brush.
                bool restore = !tracker.PreserveUpdatedBorderBrush
                    || ReferenceEquals(fe.ReadLocalValue(borderProperty), tracker.ActiveBorderBrush);
                if (restore)
                {
                    if (tracker.NormalBorderBrushHasLocalValue)
                        fe.SetValue(borderProperty, tracker.NormalBorderBrush);
                    else
                        fe.ClearValue(borderProperty);
                }
                tracker.ActiveBorderBrush = null;
                tracker.BorderBrushOverrideApplied = false;
                tracker.PreserveUpdatedBorderBrush = false;
            }
        }
    }

    /// <summary>
    /// Writes an interaction state's <paramref name="stateBrush"/> over <paramref name="dp"/>,
    /// or, once no state sets it, puts back what the property held when the state took it
    /// over: the captured local brush, or no local value at all. A property no state touched,
    /// or one another writer replaced the state's brush on, is left alone.
    /// </summary>
    private static void ApplyInteractionBrush(
        FrameworkElement fe, DependencyProperty dp, Brush? stateBrush,
        ref object? normalLocal, ref Brush? activeBrush, ref bool overrideApplied)
    {
        if (stateBrush is not null)
        {
            // Capture on the first override, and again if another writer replaced the state's
            // brush while a state was active (a re-render setting a new .Background): that value
            // is what the element now carries, so it is what leaving the states puts back.
            if (!overrideApplied || !ReferenceEquals(fe.ReadLocalValue(dp), activeBrush))
            {
                // A binding is kept as its Binding, read through the typed GetBindingExpression:
                // the raw local value does not reliably project as a BindingExpression under
                // NativeAOT, so testing it with `is` would lose the binding there.
                var local = fe.ReadLocalValue(dp);
                normalLocal = local is null or Brush || ReferenceEquals(local, DependencyProperty.UnsetValue)
                    ? local
                    : (object?)fe.GetBindingExpression(dp)?.ParentBinding ?? local;
                overrideApplied = true;
            }
            fe.SetValue(dp, stateBrush);
            activeBrush = stateBrush;
            return;
        }

        if (!overrideApplied)
            return;
        var normal = normalLocal;
        var active = activeBrush;
        normalLocal = null;
        activeBrush = null;
        overrideApplied = false;
        if (!ReferenceEquals(fe.ReadLocalValue(dp), active))
            return;
        // A local brush (or local null) is written back, and a binding the state wrote over is set
        // again from its Binding, so it keeps following its source. No local value clears the
        // property to what styles supply.
        if (normal is null or Brush)
            fe.SetValue(dp, normal);
        else if (normal is Microsoft.UI.Xaml.Data.BindingBase binding)
            fe.SetBinding(dp, binding);
        else
            fe.ClearValue(dp);
    }

    private static InteractionStateValues? GetStateValues(InteractionStatesConfig? config, InteractionState state) => state switch
    {
        InteractionState.PointerOver => config?.PointerOver,
        InteractionState.Pressed when config is not null => MergePressed(config.PointerOver, config.Pressed),
        InteractionState.Focused => config?.Focused,
        _ => null,
    };

    private static InteractionStateValues? MergePressed(InteractionStateValues? pointerOver, InteractionStateValues? pressed)
    {
        if (pressed is null) return pointerOver;
        if (pointerOver is null) return pressed;

        // Pressed inherits unoverridden values from PointerOver
        return new InteractionStateValues(
            Opacity: pressed.Opacity ?? pointerOver.Opacity,
            Scale: pressed.Scale ?? pointerOver.Scale,
            ScaleV: pressed.ScaleV ?? pointerOver.ScaleV,
            Translation: pressed.Translation ?? pointerOver.Translation,
            Rotation: pressed.Rotation ?? pointerOver.Rotation,
            Background: pressed.Background ?? pointerOver.Background,
            Foreground: pressed.Foreground ?? pointerOver.Foreground,
            BorderBrush: pressed.BorderBrush ?? pointerOver.BorderBrush);
    }
}
