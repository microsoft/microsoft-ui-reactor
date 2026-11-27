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

// Reconciler partial: ApplyModifiers and modifier target tracking.
public sealed partial class Reconciler
{
    internal void ApplyModifiers(FrameworkElement fe, ElementModifiers m, Action requestRerender, bool ownsControl = false)
        => ApplyModifiers(fe, null, m, requestRerender, ownsControl: ownsControl);

    private static void ApplyDragAttached(FrameworkElement fe, DragAttached? attached)
        => DragAttachedProperties.SetIsDragEnabled(fe, attached?.IsEnabled);

    private Element? GetModifierAnimationSource(FrameworkElement control)
    {
        if (_reconcilingElement?.AnimationConfig is not null) return _reconcilingElement;
        if (ReferenceEquals(UpdatingFlyout.Target, control)) return UpdatingFlyout.Decorator;
        return _reconcilingElement is { } element && IsFlyoutDecorator(element) ? element : null;
    }

    internal ModifierMountScope CaptureModifierMounts(Element parent) => new(this, parent);

    // A V1 decorator can mount several children but only the control it returns
    // can share its modifiers. Keep candidates on the reconciler until that return.
    internal readonly ref struct ModifierMountScope
    {
        private readonly Reconciler _owner;
        private readonly Element? _previousParent;
        private readonly int _start;

        internal ModifierMountScope(Reconciler owner, Element parent)
        {
            _owner = owner;
            _previousParent = owner._modifierMountParent;
            _start = owner._modifierMounts?.Count ?? 0;
            owner._modifierMountParent = parent;
        }

        internal void RecordReturnedChild(UIElement control)
        {
            if (_owner._modifierMounts is not { } mounts) return;
            for (int i = mounts.Count - 1; i >= _start; i--)
            {
                if (!ReferenceEquals(mounts[i].Control, control)) continue;
                _owner.RecordModifierTarget(control, _owner._modifierMountParent!, mounts[i].Target);
                break;
            }
        }

        public void Dispose()
        {
            if (_owner._modifierMounts is { } mounts && mounts.Count > _start)
                mounts.RemoveRange(_start, mounts.Count - _start);
            _owner._modifierMountParent = _previousParent;
        }
    }

    private Element? RecordModifierTarget(UIElement control, Element parent, Element target)
    {
        var state = _hasModifierTargets ? control.GetValue(ReactorAttached.StateProperty) as ReactorState : null;
        var targets = state?.ModifierTargets;
        if (targets is not null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (!ReferenceEquals(targets[i].Parent, parent)) continue;
                var previous = targets[i].Target;
                targets[i] = (parent, target);
                return previous;
            }
        }
        // Keep the shared edge if a modifier on the parent can be handed to keyframes.
        // Its otherwise plain target may introduce a keyframe trigger on a later update.
        if (!HasRestorableModifier(target, targets)
            && (parent.KeyframeAnimations is null || !HasRestorableModifier(parent, null))) return null;
        targets ??= (state ?? GetOrCreateReactorState(control)).ModifierTargets ??= [];
        targets.Add((parent, target));
        _hasModifierTargets = true;
        return null;
    }

    private static bool HasRestorableModifier(Element target, List<(Element Parent, Element Target)>? targets)
    {
        static bool Declares(ElementModifiers? modifiers) => modifiers?.Visual is { } v
            && (v.Opacity.HasValue || v.Scale.HasValue || v.Rotation.HasValue
                || v.Translation.HasValue || v.CenterPoint.HasValue || v.TransformOrigin.HasValue);

        while (target is ModifiedElement wrapped)
        {
            if (Declares(wrapped.Modifiers) || Declares(wrapped.WrappedModifiers)) return true;
            target = wrapped.Inner;
        }
        if (Declares(target.Modifiers)) return true;
        if (targets is not null)
            foreach (var entry in targets)
                if (ReferenceEquals(entry.Parent, target)) return true;
        return false;
    }

    private bool RefreshModifierTarget(UIElement control, Element oldElement, Element newElement)
    {
        if (!_hasModifierTargets || !(newElement is KeyedMemoElement
            || (_v1Handlers.TryGet(newElement.GetType(), out var entry) && entry is IV1SourceTargetResolver))) return false;
        if (control.GetValue(ReactorAttached.StateProperty) is not ReactorState { ModifierTargets: { } targets }) return false;
        // A Memo resource refresh passes a fresh element as both old and new.
        // Rebind using the previously recorded target, including through legacy wrappers.
        while (oldElement is ModifiedElement wrapped) oldElement = wrapped.Inner;
        for (int i = 0; i < targets.Count; i++)
        {
            if (!ReferenceEquals(targets[i].Parent, oldElement)) continue;
            targets[i] = (newElement, targets[i].Target);
            return true;
        }
        return false;
    }

    internal (Element Source, ElementModifiers Modifiers)? FindRemainingModifierOwner(Func<ElementModifiers, bool> declares)
    {
        // During target recursion the mounted decorator still describes the old tree.
        // Resolve one property: outer decorators win; inner legacy declarations override outer ones.
        var root = ModifierUpdate.Root;
        for (var depth = 0; root is not null && depth <= ReactorSourceMap.MaxDecoratorDepth; depth++)
        {
            var modifiers = ReferenceEquals(root, ModifierUpdate.Root) ? ModifierUpdate.Modifiers : null;
            if (root is ModifiedElement && modifiers is null) modifiers = root.Modifiers;
            while (root is ModifiedElement wrapped && depth++ <= ReactorSourceMap.MaxDecoratorDepth)
            {
                if (declares(wrapped.WrappedModifiers)) modifiers = wrapped.WrappedModifiers;
                root = wrapped.Inner;
            }
            if (root.Modifiers is { } inner && declares(inner)) return (root, inner);
            if (modifiers is not null && declares(modifiers)) return (root, modifiers);
            // Match Mount and Update dispatch: a per-host handler can shadow a global decorator.
            var type = root.GetType();
            Element? next = null;
            if (_v1Handlers.TryGet(type, out var handler))
                next = (handler as IV1SourceTargetResolver)?.GetSourceTarget(root);
            else if (!_typeRegistry.ContainsKey(type))
                next = ReactorSourceMap.DecoratorTarget(root);
            if (next is null && ModifierUpdate.Control?.GetValue(ReactorAttached.StateProperty)
                is ReactorState { ModifierTargets: { } targets })
                foreach (var target in targets)
                    if (ReferenceEquals(target.Parent, root)) { next = target.Target; break; }
            root = next;
        }
        return null;
    }

    private Func<UIElement, AnimateProperty, AnimationConfig?>? _channelAnimationLookup;

    // Passed to AnimationHelper so a modifier write resolves its channel's .Animate() declaration
    // over the whole shared-target chain (spec 066 §5.3, §6.1).
    private Func<UIElement, AnimateProperty, AnimationConfig?> ChannelAnimationLookup => _channelAnimationLookup ??= FindChannelAnimation;

    /// <summary>
    /// The outermost <c>.Animate()</c> declaration in the shared-target chain updating
    /// <paramref name="control"/> whose properties include <paramref name="channel"/>, walked the way
    /// <see cref="FindRemainingModifierOwner"/> walks it: legacy wrappers, a handler's source target,
    /// a decorator target, then the recorded shared-control edges. Null outside that update.
    /// </summary>
    private AnimationConfig? FindChannelAnimation(UIElement control, AnimateProperty channel)
    {
        if (!ReferenceEquals(ModifierUpdate.Control, control)) return null;
        var root = ModifierUpdate.Root;
        for (var depth = 0; root is not null && depth <= ReactorSourceMap.MaxDecoratorDepth; depth++)
        {
            while (true)
            {
                if (root.AnimationConfig is { } config && (config.Properties & channel) != 0) return config;
                if (root is not ModifiedElement wrapped || depth++ > ReactorSourceMap.MaxDecoratorDepth) break;
                root = wrapped.Inner;
            }
            var type = root.GetType();
            Element? next = null;
            if (_v1Handlers.TryGet(type, out var handler))
                next = (handler as IV1SourceTargetResolver)?.GetSourceTarget(root);
            else if (!_typeRegistry.ContainsKey(type))
                next = ReactorSourceMap.DecoratorTarget(root);
            if (next is null && control.GetValue(ReactorAttached.StateProperty) is ReactorState { ModifierTargets: { } targets })
                foreach (var target in targets)
                    if (ReferenceEquals(target.Parent, root)) { next = target.Target; break; }
            root = next;
        }
        return null;
    }

    private Element? GetRetainedModifierAnimationSource(FrameworkElement control, Element owner)
    {
        if (FindMountedElementValue(control, static e => e.AnimationConfig, owner) is not null) return owner;
        return GetModifierAnimationSource(control) ?? ModifierUpdate.Root;
    }

    private void ClearRemovedTransformModifiers(FrameworkElement control, ElementModifiers? old, ElementModifiers? current)
    {
        // Release departing facade values before setters or nested targets can
        // start another animation. Remaining declarations keep their own values.
        if (old?.Scale.HasValue == true && current?.Scale.HasValue != true
            && FindRemainingModifierOwner(static m => m.Scale.HasValue) is null)
            AnimationHelper.ResetCompositionModifier(control, "Scale");
        if (old?.Rotation.HasValue == true && current?.Rotation.HasValue != true
            && FindRemainingModifierOwner(static m => m.Rotation.HasValue) is null)
            AnimationHelper.ResetCompositionModifier(control, "Rotation");
        if (old?.Translation.HasValue == true && current?.Translation.HasValue != true
            && FindRemainingModifierOwner(static m => m.Translation.HasValue) is null)
            AnimationHelper.ResetCompositionModifier(control, "Translation");
        if (old?.CenterPoint.HasValue == true && current?.CenterPoint.HasValue != true
            && FindRemainingModifierOwner(static m => m.CenterPoint.HasValue) is null)
            AnimationHelper.ResetCompositionModifier(control, "CenterPoint");
        // After CenterPoint, so the restored static pivot is the remaining offset.
        if (old?.TransformOrigin.HasValue == true && current?.TransformOrigin.HasValue != true
            && FindRemainingModifierOwner(static m => m.TransformOrigin.HasValue) is null)
            TransformOriginBinding.Deactivate(control);
    }

    private void ApplyDeclaredOpacity(FrameworkElement element, float value, Element? source)
    {
        var state = GetOrCreateReactorState(element);
        bool defer = ShouldDeferOpacityWrite(element, state);
        var previous = state.DeclaredOpacity;
        state.DeclaredOpacity = (value, defer && (previous.Deferred || previous.Value != value));
        if (state.Composition is { } composition) composition.DeferredModifierResets &= ~AnimateProperty.Opacity;

        // XAML pushes a changed local value to the visual on its next frame (#1669).
        // Hold the declaration until the keyframe releases opacity to preserve its controller.
        if (!defer)
            AnimationHelper.SetOrAnimate(element, "Opacity", value, source, ChannelAnimationLookup);
    }

    private static bool ShouldDeferOpacityWrite(UIElement element, ReactorState state)
    {
        if (state.OpacityKeyframe is not { ReplayedWithoutOpacity: false } playback) return false;
        var current = ElementCompositionPreview.GetElementVisual(element).TryGetAnimationController("Opacity");
        return Equals(current, playback.Controller)
            || (current is null && Equals(state.Composition?.OpacityRestart?.Controller, playback.Controller));
    }

    // ownsControl: true when `fe` was created for the element `m` belongs to, as opposed to a
    // flyout decorator or keyed memo layered over another element's control. Only the owner's
    // inline overlays are recorded (issue #965).
    internal void ApplyModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, Action requestRerender,
        bool keepRemovedBackground = false, bool ownsControl = false,
        ForegroundModifier.UpdateAction foregroundUpdate = ForegroundModifier.UpdateAction.None)
    {
        // Guard each property: only call into WinUI when the value actually changed.
        // Each WinUI property set is a managed→native interop call, so avoiding
        // unnecessary sets is critical for large element counts.
        //
        // Each family helper below owns a set/unset arm pair per modifier, and the calls keep
        // the statement order of the pre-split body. Keep it unless you have a reason: at least
        // RequestedTheme must come before anything theme-resolved, and the OnMount/OnUpdate
        // callbacks must run after the property writes they may override. Add a new modifier's
        // arms to the helper for its family, or add a new `Apply…Modifier(s)(fe, oldM, m, …)`
        // helper at the point in the sequence where its writes belong. The source gates in
        // Reactor.Tests scan every helper with that name and signature (ReconcilerSource.ApplyModifiersFamily).
        ApplyRequestedThemeModifier(fe, oldM, m);
        ApplyMarginModifiers(fe, oldM, m, ownsControl);
        ApplyPaddingModifiers(fe, oldM, m, ownsControl);
        ApplySizeAndAlignmentModifiers(fe, oldM, m);
        ApplyOpacityTransformAndVisibilityModifiers(fe, oldM, m);
        ApplyToolTipAndFlyoutModifiers(fe, oldM, m, requestRerender);
        ApplyIsEnabledModifier(fe, oldM, m);
        ApplyBorderModifiers(fe, oldM, m, ownsControl);
        ApplyBackgroundAndForegroundModifiers(fe, oldM, m, keepRemovedBackground, foregroundUpdate);
        ApplyAutomationAndFocusModifiers(fe, oldM, m);

        // ── Accessibility — Tier 2/3 (lazy sub-record) ─────────────
        var a11y = m.Accessibility;
        var oldA11y = oldM?.Accessibility;
        if (a11y is not null || oldA11y is not null)
            AccessibilityApplier.ApplyAccessibilityModifiers(fe, oldA11y, a11y);

        ApplyTypographyModifiers(fe, oldM, m);

        // ── Declarative event handlers ────────────────────────────
        // Detach previous handler (if any) before attaching new one.
        // Handlers are stored in Tag via a wrapper so we can find them for detach.
        ModifierEvents.ApplyEventHandlers(fe, oldM, m);

        // Gesture recognizers (.OnPan / .OnPinch / .OnRotate)
        GestureDispatch.ApplyGestureHandlers(fe, oldM, m);

        // Drag-and-drop (.OnDragStart / .OnDrop / .OnDragEnter / .OnDragOver / .OnDragLeave)
        DragDropDispatch.ApplyDragDropHandlers(fe, oldM, m);

        ApplyLifecycleCallbackModifiers(fe, oldM, m);
        ApplyRefModifier(fe, oldM, m);

        ReferenceEdgeWiring.ApplyModifierReferenceEdges(fe, oldM, m);
    }

    /// <summary>RequestedTheme.</summary>
    private static void ApplyRequestedThemeModifier(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // RequestedTheme must be set BEFORE ApplyThemeBindings so that ThemeRef
        // bindings resolve against the correct theme variant.
        if (m.RequestedTheme.HasValue && m.RequestedTheme != oldM?.RequestedTheme)
            fe.RequestedTheme = m.RequestedTheme.Value;
        else if (!m.RequestedTheme.HasValue && oldM?.RequestedTheme.HasValue == true)
            fe.ClearValue(FrameworkElement.RequestedThemeProperty);
    }

    /// <summary>Margin with its BiDi-aware inline overlay (MarginInlineStart/End).</summary>
    private static void ApplyMarginModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool ownsControl)
    {
        // Apply physical margin, then overlay logical (BiDi-aware) inline margin.
        // The previous render's physical margin is only carried forward as the *base*
        // for that overlay — folding it into resolvedMargin unconditionally would make
        // the value non-null whenever the last render had a margin, which left the unset
        // arm below unreachable and meant `.Margin(x)` → no margin reset nothing (#952).
        var resolvedMargin = m.Margin;
        // Issue #965: the owner's previous overlay, consulted only when the previous render
        // wrote one.
        InlineOverlay? prevMarginOverlay = null;
        if (m.MarginInlineStart.HasValue || m.MarginInlineEnd.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            if (ownsControl && oldM is { } oldMargins && (oldMargins.MarginInlineStart.HasValue || oldMargins.MarginInlineEnd.HasValue))
                prevMarginOverlay = GetInlineOverlays(fe)?.Margin;
            var baseMargin = resolvedMargin ?? oldM?.Margin ?? UnoverlaidBase(fe.Margin, prevMarginOverlay);
            var left = isRtl ? (m.MarginInlineEnd ?? baseMargin.Left) : (m.MarginInlineStart ?? baseMargin.Left);
            var right = isRtl ? (m.MarginInlineStart ?? baseMargin.Right) : (m.MarginInlineEnd ?? baseMargin.Right);
            resolvedMargin = new Thickness(left, baseMargin.Top, right, baseMargin.Bottom);
            var marginOverlay = new InlineOverlay(baseMargin, resolvedMargin.Value);
            if (ownsControl && prevMarginOverlay != marginOverlay)
                GetOrCreateInlineOverlays(fe).Margin = marginOverlay;
        }
        // Unset goes through ClearValue, never through "write the DP default". A local
        // value outranks every Style setter in WinUI's precedence order, so assigning the
        // default would not restore the styled value — it would permanently override it
        // (issue #952). Same rule for every reset arm in the helpers that follow.
        // The value guard compares against the previous *physical* margin, so dropping or
        // changing an inline edge while the physical margin stays put would resolve back to
        // the old value and skip the write, stranding the previous render's inline edge on
        // the control. An inline delta is a change in its own right — and so is a resolved value
        // that differs from what the previous overlay wrote (a FlowDirection flip under unchanged
        // modifiers can resolve back to the physical value).
        var marginInlineChanged = m.MarginInlineStart != oldM?.MarginInlineStart
            || m.MarginInlineEnd != oldM?.MarginInlineEnd
            || (prevMarginOverlay is { } pm && pm.Written != resolvedMargin);
        if (resolvedMargin.HasValue && (resolvedMargin != oldM?.Margin || marginInlineChanged)) fe.Margin = resolvedMargin.Value;
        else if (!resolvedMargin.HasValue && (oldM?.Margin.HasValue == true || oldM?.MarginInlineStart.HasValue == true || oldM?.MarginInlineEnd.HasValue == true))
            fe.ClearValue(FrameworkElement.MarginProperty);
    }

    /// <summary>Padding with its BiDi-aware inline overlay (PaddingInlineStart/End).</summary>
    private static void ApplyPaddingModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool ownsControl)
    {
        // Apply physical padding, then overlay logical (BiDi-aware) inline padding.
        //
        // Same shape as Margin: resolve from the new bag only, and carry the previous render's
        // physical padding forward solely as the inline overlay's *base*. Folding it into
        // `resolvedPadding` made the value non-null whenever the last render had padding, so a
        // render that dropped `.Padding(x)` and an inline edge together took the write arm (the
        // inline edge changed) and wrote `x` back, and the reset below never ran (#1749).
        // `!wantsPadding && hadPadding` is the same reset guard Margin spells out inline.
        var wantsPadding = m.Padding.HasValue || m.PaddingInlineStart.HasValue || m.PaddingInlineEnd.HasValue;
        var hadPadding = oldM is not null && (oldM.Padding.HasValue || oldM.PaddingInlineStart.HasValue || oldM.PaddingInlineEnd.HasValue);
        var resolvedPadding = m.Padding;
        InlineOverlay? prevPadOverlay = null;
        if (m.PaddingInlineStart.HasValue || m.PaddingInlineEnd.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            if (ownsControl && oldM is { } oldPads && (oldPads.PaddingInlineStart.HasValue || oldPads.PaddingInlineEnd.HasValue))
                prevPadOverlay = GetInlineOverlays(fe)?.Padding;
            var basePad = resolvedPadding ?? oldM?.Padding ?? UnoverlaidBase(fe switch
            {
                WinUI.Control pc => pc.Padding,
                WinUI.Border pb => pb.Padding,
                WinUI.StackPanel psp => psp.Padding,
                WinUI.Grid pg => pg.Padding,
                WinUI.RelativePanel prp => prp.Padding,
                WinUI.TextBlock ptb => ptb.Padding,
                _ => new Thickness(),
            }, prevPadOverlay);
            var left = isRtl ? (m.PaddingInlineEnd ?? basePad.Left) : (m.PaddingInlineStart ?? basePad.Left);
            var right = isRtl ? (m.PaddingInlineStart ?? basePad.Right) : (m.PaddingInlineEnd ?? basePad.Right);
            resolvedPadding = new Thickness(left, basePad.Top, right, basePad.Bottom);
            var padOverlay = new InlineOverlay(basePad, resolvedPadding.Value);
            if (ownsControl && prevPadOverlay != padOverlay)
                GetOrCreateInlineOverlays(fe).Padding = padOverlay;
        }
        // Same inline-delta rule as Margin (ApplyMarginModifiers).
        var paddingInlineChanged = m.PaddingInlineStart != oldM?.PaddingInlineStart
            || m.PaddingInlineEnd != oldM?.PaddingInlineEnd
            || (prevPadOverlay is { } pp && pp.Written != resolvedPadding);
        if (resolvedPadding.HasValue && (resolvedPadding != oldM?.Padding || paddingInlineChanged))
        {
            if (fe is WinUI.Control padCtrl) padCtrl.Padding = resolvedPadding.Value;
            else if (fe is WinUI.Border padBdr) padBdr.Padding = resolvedPadding.Value;
            else if (fe is WinUI.StackPanel padSp) padSp.Padding = resolvedPadding.Value;
            else if (fe is WinUI.Grid padGrid) padGrid.Padding = resolvedPadding.Value;
            else if (fe is WinUI.RelativePanel padRelative) padRelative.Padding = resolvedPadding.Value;
            else if (fe is WinUI.TextBlock padTb) padTb.Padding = resolvedPadding.Value;
        }
        else if (!wantsPadding && hadPadding)
        {
            // Clear rather than write a local `new Thickness(0)`: a zero local wins over the
            // control's style/template, so a Button that dropped `.Padding(...)` would flush to
            // nothing instead of returning to its themed padding. This is the `Padding` half of
            // issue #952; the sizing/alignment arms (ApplySizeAndAlignmentModifiers) follow the same rule.
            if (fe is WinUI.Control padCtrl) padCtrl.ClearValue(WinUI.Control.PaddingProperty);
            else if (fe is WinUI.Border padBdr) padBdr.ClearValue(WinUI.Border.PaddingProperty);
            else if (fe is WinUI.StackPanel padSp) padSp.ClearValue(WinUI.StackPanel.PaddingProperty);
            else if (fe is WinUI.Grid padGrid) padGrid.ClearValue(WinUI.Grid.PaddingProperty);
            else if (fe is WinUI.RelativePanel padRelative) padRelative.ClearValue(WinUI.RelativePanel.PaddingProperty);
            else if (fe is WinUI.TextBlock padTb) padTb.ClearValue(WinUI.TextBlock.PaddingProperty);
        }
    }

    /// <summary>Width/Height, Min/Max sizes, and the (content) alignments.</summary>
    private static void ApplySizeAndAlignmentModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        if (m.Width.HasValue && m.Width != oldM?.Width) fe.Width = m.Width.Value;
        else if (!m.Width.HasValue && oldM?.Width.HasValue == true) fe.ClearValue(FrameworkElement.WidthProperty);
        if (m.Height.HasValue && m.Height != oldM?.Height) fe.Height = m.Height.Value;
        else if (!m.Height.HasValue && oldM?.Height.HasValue == true) fe.ClearValue(FrameworkElement.HeightProperty);
        if (m.MinWidth.HasValue && m.MinWidth != oldM?.MinWidth) fe.MinWidth = m.MinWidth.Value;
        else if (!m.MinWidth.HasValue && oldM?.MinWidth.HasValue == true) fe.ClearValue(FrameworkElement.MinWidthProperty);
        if (m.MinHeight.HasValue && m.MinHeight != oldM?.MinHeight) fe.MinHeight = m.MinHeight.Value;
        else if (!m.MinHeight.HasValue && oldM?.MinHeight.HasValue == true) fe.ClearValue(FrameworkElement.MinHeightProperty);
        if (m.MaxWidth.HasValue && m.MaxWidth != oldM?.MaxWidth) fe.MaxWidth = m.MaxWidth.Value;
        else if (!m.MaxWidth.HasValue && oldM?.MaxWidth.HasValue == true) fe.ClearValue(FrameworkElement.MaxWidthProperty);
        if (m.MaxHeight.HasValue && m.MaxHeight != oldM?.MaxHeight) fe.MaxHeight = m.MaxHeight.Value;
        else if (!m.MaxHeight.HasValue && oldM?.MaxHeight.HasValue == true) fe.ClearValue(FrameworkElement.MaxHeightProperty);
        if (m.HorizontalAlignment.HasValue && m.HorizontalAlignment != oldM?.HorizontalAlignment) fe.HorizontalAlignment = m.HorizontalAlignment.Value;
        else if (!m.HorizontalAlignment.HasValue && oldM?.HorizontalAlignment.HasValue == true) fe.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
        if (m.VerticalAlignment.HasValue && m.VerticalAlignment != oldM?.VerticalAlignment) fe.VerticalAlignment = m.VerticalAlignment.Value;
        else if (!m.VerticalAlignment.HasValue && oldM?.VerticalAlignment.HasValue == true) fe.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        if (fe is WinUI.Control contentAlignmentControl)
        {
            if (m.HorizontalContentAlignment.HasValue && m.HorizontalContentAlignment != oldM?.HorizontalContentAlignment)
                contentAlignmentControl.HorizontalContentAlignment = m.HorizontalContentAlignment.Value;
            else if (!m.HorizontalContentAlignment.HasValue && oldM?.HorizontalContentAlignment.HasValue == true)
                contentAlignmentControl.ClearValue(WinUI.Control.HorizontalContentAlignmentProperty);

            if (m.VerticalContentAlignment.HasValue && m.VerticalContentAlignment != oldM?.VerticalContentAlignment)
                contentAlignmentControl.VerticalContentAlignment = m.VerticalContentAlignment.Value;
            else if (!m.VerticalContentAlignment.HasValue && oldM?.VerticalContentAlignment.HasValue == true)
                contentAlignmentControl.ClearValue(WinUI.Control.VerticalContentAlignmentProperty);
        }
    }

    /// <summary>Opacity, the facade transforms (Scale, Rotation, Translation, TransformOrigin, CenterPoint), and IsVisible.</summary>
    private void ApplyOpacityTransformAndVisibilityModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        if (m.Opacity.HasValue && m.Opacity != oldM?.Opacity)
            ApplyDeclaredOpacity(fe, (float)m.Opacity.Value, GetModifierAnimationSource(fe));
        else if (!m.Opacity.HasValue && oldM?.Opacity.HasValue == true)
        {
            if (FindRemainingModifierOwner(static m => m.Opacity.HasValue) is { } owner)
                ApplyDeclaredOpacity(fe, (float)owner.Modifiers.Opacity!.Value, GetRetainedModifierAnimationSource(fe, owner.Source));
            else
            {
                fe.ClearValue(UIElement.OpacityProperty);
                AnimationHelper.ResetCompositionModifier(fe, "Opacity", removeOpacityDeclaration: true);
                StopPendingEnterFade(fe);
            }
        }
        // These facade properties have no public dependency-property identifiers.
        // Update already released final removals before handlers ran. Here only
        // restore surviving declarations; a second reset would overwrite new setters.
        if (m.Scale.HasValue && m.Scale != oldM?.Scale)
            AnimationHelper.SetOrAnimateVector3(fe, "Scale", m.Scale.Value, GetModifierAnimationSource(fe), ChannelAnimationLookup);
        else if (!m.Scale.HasValue && oldM?.Scale.HasValue == true)
        {
            if (FindRemainingModifierOwner(static m => m.Scale.HasValue) is { } owner)
                AnimationHelper.SetOrAnimateVector3(fe, "Scale", owner.Modifiers.Scale!.Value, GetRetainedModifierAnimationSource(fe, owner.Source), ChannelAnimationLookup);
        }
        if (m.Rotation.HasValue && m.Rotation != oldM?.Rotation)
            AnimationHelper.SetOrAnimate(fe, "Rotation", m.Rotation.Value, GetModifierAnimationSource(fe), ChannelAnimationLookup);
        else if (!m.Rotation.HasValue && oldM?.Rotation.HasValue == true)
        {
            if (FindRemainingModifierOwner(static m => m.Rotation.HasValue) is { } owner)
                AnimationHelper.SetOrAnimate(fe, "Rotation", owner.Modifiers.Rotation!.Value, GetRetainedModifierAnimationSource(fe, owner.Source), ChannelAnimationLookup);
        }
        if (m.Translation.HasValue && m.Translation != oldM?.Translation)
            AnimationHelper.SetOrAnimateVector3(fe, "Translation", m.Translation.Value, GetModifierAnimationSource(fe), ChannelAnimationLookup);
        else if (!m.Translation.HasValue && oldM?.Translation.HasValue == true)
        {
            if (FindRemainingModifierOwner(static m => m.Translation.HasValue) is { } owner)
                AnimationHelper.SetOrAnimateVector3(fe, "Translation", owner.Modifiers.Translation!.Value, GetRetainedModifierAnimationSource(fe, owner.Source), ChannelAnimationLookup);
        }
        // Before CenterPoint (spec 066 §8.2): an origin that starts here takes the current offset,
        // and a CenterPoint change in the same render then writes or animates that offset, so a
        // curve isn't cancelled by the expression starting after it.
        if (m.TransformOrigin.HasValue && m.TransformOrigin != oldM?.TransformOrigin)
            TransformOriginBinding.Apply(fe, m.TransformOrigin.Value);
        else if (!m.TransformOrigin.HasValue && oldM?.TransformOrigin.HasValue == true
            && FindRemainingModifierOwner(static m => m.TransformOrigin.HasValue) is { } originOwner)
            TransformOriginBinding.Apply(fe, originOwner.Modifiers.TransformOrigin!.Value);
        if (m.CenterPoint.HasValue && m.CenterPoint != oldM?.CenterPoint)
            AnimationHelper.SetOrAnimateVector3(fe, "CenterPoint", m.CenterPoint.Value, GetModifierAnimationSource(fe), ChannelAnimationLookup);
        else if (!m.CenterPoint.HasValue && oldM?.CenterPoint.HasValue == true)
        {
            if (FindRemainingModifierOwner(static m => m.CenterPoint.HasValue) is { } owner)
                AnimationHelper.SetOrAnimateVector3(fe, "CenterPoint", owner.Modifiers.CenterPoint!.Value, GetRetainedModifierAnimationSource(fe, owner.Source), ChannelAnimationLookup);
        }
        if (m.IsVisible.HasValue && m.IsVisible != oldM?.IsVisible)
            fe.Visibility = m.IsVisible.Value ? Visibility.Visible : Visibility.Collapsed;
        else if (!m.IsVisible.HasValue && oldM?.IsVisible.HasValue == true)
            fe.ClearValue(UIElement.VisibilityProperty);
    }

    /// <summary>ToolTip/RichToolTip, ToolTipPlacement, AttachedFlyout, and ContextFlyout.</summary>
    private void ApplyToolTipAndFlyoutModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, Action requestRerender)
    {
        if (m.RichToolTip is not null)
        {
            var oldTipEl = oldM?.RichToolTip;
            var existingTip = WinUI.ToolTipService.GetToolTip(fe) as UIElement;
            if (oldTipEl is not null && existingTip is not null && CanUpdate(oldTipEl, m.RichToolTip))
            {
                var replacement = UpdateHostedChild(oldTipEl, m.RichToolTip, existingTip, requestRerender);
                if (replacement is not null)
                    WinUI.ToolTipService.SetToolTip(fe, replacement);
            }
            else
            {
                WinUI.ToolTipService.SetToolTip(fe, Mount(m.RichToolTip, requestRerender));
            }
        }
        else if (m.ToolTip is not null && m.ToolTip != oldM?.ToolTip)
            WinUI.ToolTipService.SetToolTip(fe, m.ToolTip);
        else if (m.RichToolTip is null && m.ToolTip is null && (oldM?.RichToolTip is not null || oldM?.ToolTip is not null))
            fe.ClearValue(WinUI.ToolTipService.ToolTipProperty);

        if (m.ToolTipPlacement.HasValue && m.ToolTipPlacement != oldM?.ToolTipPlacement)
            WinUI.ToolTipService.SetPlacement(fe, m.ToolTipPlacement.Value);
        else if (!m.ToolTipPlacement.HasValue && oldM?.ToolTipPlacement.HasValue == true)
            fe.ClearValue(WinUI.ToolTipService.PlacementProperty);

        if (m.AttachedFlyout is not null)
            ApplyFlyoutAttachment(fe, oldM?.AttachedFlyout, m.AttachedFlyout, requestRerender);

        if (m.ContextFlyout is not null)
        {
            var oldContextEl = oldM?.ContextFlyout;
            if (oldContextEl is not null && fe.ContextFlyout is WinPrim.FlyoutBase existingCtx)
                UpdateFlyoutInPlace(existingCtx, oldContextEl, m.ContextFlyout, requestRerender);
            else
                fe.ContextFlyout = CreateFlyoutFromElement(m.ContextFlyout, requestRerender);
        }
        else if (oldM?.ContextFlyout is not null)
            fe.ClearValue(UIElement.ContextFlyoutProperty);
    }

    /// <summary>IsEnabled.</summary>
    private static void ApplyIsEnabledModifier(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // IsEnabled (on Control)
        if (m.IsEnabled.HasValue && m.IsEnabled != oldM?.IsEnabled && fe is WinUI.Control enCtrl)
            enCtrl.IsEnabled = m.IsEnabled.Value;
        else if (!m.IsEnabled.HasValue && oldM?.IsEnabled.HasValue == true && fe is WinUI.Control enCtrl2)
            enCtrl2.ClearValue(WinUI.Control.IsEnabledProperty);
    }

    /// <summary>CornerRadius, BorderBrush, and BorderThickness with its BiDi-aware inline overlay (BorderInlineStart).</summary>
    private void ApplyBorderModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool ownsControl)
    {
        // CornerRadius (on Control, Border, Grid, StackPanel, and RelativePanel)
        if (m.CornerRadius.HasValue && m.CornerRadius != oldM?.CornerRadius)
        {
            if (fe is WinUI.Control crCtrl) crCtrl.CornerRadius = m.CornerRadius.Value;
            else if (fe is WinUI.Border crBdr) crBdr.CornerRadius = m.CornerRadius.Value;
            else if (fe is WinUI.Grid crGrid) crGrid.CornerRadius = m.CornerRadius.Value;
            else if (fe is WinUI.StackPanel crStack) crStack.CornerRadius = m.CornerRadius.Value;
            else if (fe is WinUI.RelativePanel crRelative) crRelative.CornerRadius = m.CornerRadius.Value;
        }
        else if (!m.CornerRadius.HasValue && oldM?.CornerRadius.HasValue == true)
        {
            if (fe is WinUI.Control crCtrl) crCtrl.ClearValue(WinUI.Control.CornerRadiusProperty);
            else if (fe is WinUI.Border crBdr) crBdr.ClearValue(WinUI.Border.CornerRadiusProperty);
            else if (fe is WinUI.Grid crGrid) crGrid.ClearValue(WinUI.Grid.CornerRadiusProperty);
            else if (fe is WinUI.StackPanel crStack) crStack.ClearValue(WinUI.StackPanel.CornerRadiusProperty);
            else if (fe is WinUI.RelativePanel crRelative) crRelative.ClearValue(WinUI.RelativePanel.CornerRadiusProperty);
        }

        // BorderBrush / BorderThickness (Control, Border, and concrete panels)
        if (!ReferenceEquals(m.BorderBrush, oldM?.BorderBrush)
            && _reconcilingElement?.InteractionStates is not null
            && GetCompositionState(fe)?.InteractionTracker is { } borderTracker
            && borderTracker.BorderBrushOverrideApplied)
        {
            // Keep the active interaction brush while updating what hover-out restores.
            borderTracker.NormalBorderBrush = m.BorderBrush;
            borderTracker.NormalBorderBrushHasLocalValue = m.BorderBrush is not null;
        }
        else if (m.BorderBrush is not null && !ReferenceEquals(m.BorderBrush, oldM?.BorderBrush))
        {
            if (fe is WinUI.Control bbCtrl) bbCtrl.BorderBrush = m.BorderBrush;
            else if (fe is WinUI.Border bbBdr) bbBdr.BorderBrush = m.BorderBrush;
            else if (fe is WinUI.Grid bbGrid) bbGrid.BorderBrush = m.BorderBrush;
            else if (fe is WinUI.StackPanel bbStack) bbStack.BorderBrush = m.BorderBrush;
            else if (fe is WinUI.RelativePanel bbRelative) bbRelative.BorderBrush = m.BorderBrush;
        }
        else if (m.BorderBrush is null && oldM?.BorderBrush is not null)
        {
            if (fe is WinUI.Control bbCtrl) bbCtrl.ClearValue(WinUI.Control.BorderBrushProperty);
            else if (fe is WinUI.Border bbBdr) bbBdr.ClearValue(WinUI.Border.BorderBrushProperty);
            else if (fe is WinUI.Grid bbGrid) bbGrid.ClearValue(WinUI.Grid.BorderBrushProperty);
            else if (fe is WinUI.StackPanel bbStack) bbStack.ClearValue(WinUI.StackPanel.BorderBrushProperty);
            else if (fe is WinUI.RelativePanel bbRelative) bbRelative.ClearValue(WinUI.RelativePanel.BorderBrushProperty);
        }
        // Apply physical border thickness, then overlay logical (BiDi-aware) inline border
        var resolvedBorder = m.BorderThickness;
        InlineOverlay? prevBorderOverlay = null;
        if (m.BorderInlineStart.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            if (ownsControl && oldM?.BorderInlineStart is not null)
                prevBorderOverlay = GetInlineOverlays(fe)?.Border;
            // Same base rule as Margin/Padding: the previous render's physical thickness, else the
            // control's value minus the previous inline overlay (issue #965).
            var baseBorder = resolvedBorder ?? oldM?.BorderThickness ?? UnoverlaidBase(fe switch
            {
                WinUI.Control c => c.BorderThickness,
                WinUI.Border b => b.BorderThickness,
                WinUI.Grid g => g.BorderThickness,
                WinUI.StackPanel s => s.BorderThickness,
                WinUI.RelativePanel r => r.BorderThickness,
                _ => new Thickness(),
            }, prevBorderOverlay);
            var inlineStartThickness = m.BorderInlineStart.Value;
            if (isRtl)
                resolvedBorder = new Thickness(baseBorder.Left, baseBorder.Top, inlineStartThickness.Left, baseBorder.Bottom);
            else
                resolvedBorder = new Thickness(inlineStartThickness.Left, baseBorder.Top, baseBorder.Right, baseBorder.Bottom);
            var borderOverlay = new InlineOverlay(baseBorder, resolvedBorder.Value);
            if (ownsControl && prevBorderOverlay != borderOverlay)
                GetOrCreateInlineOverlays(fe).Border = borderOverlay;
        }
        // Same inline-delta rule as Margin/Padding; the reset arm likewise has to fire
        // when the old bag carried only an inline border, or that edge is never released.
        var borderInlineChanged = m.BorderInlineStart != oldM?.BorderInlineStart
            || (prevBorderOverlay is { } pb2 && pb2.Written != resolvedBorder);
        if (resolvedBorder.HasValue && (resolvedBorder != oldM?.BorderThickness || borderInlineChanged))
        {
            if (fe is WinUI.Control btCtrl) btCtrl.BorderThickness = resolvedBorder.Value;
            else if (fe is WinUI.Border btBdr) btBdr.BorderThickness = resolvedBorder.Value;
            else if (fe is WinUI.Grid btGrid) btGrid.BorderThickness = resolvedBorder.Value;
            else if (fe is WinUI.StackPanel btStack) btStack.BorderThickness = resolvedBorder.Value;
            else if (fe is WinUI.RelativePanel btRelative) btRelative.BorderThickness = resolvedBorder.Value;
        }
        else if (!resolvedBorder.HasValue && (oldM?.BorderThickness.HasValue == true || oldM?.BorderInlineStart.HasValue == true))
        {
            if (fe is WinUI.Control btCtrl) btCtrl.ClearValue(WinUI.Control.BorderThicknessProperty);
            else if (fe is WinUI.Border btBdr) btBdr.ClearValue(WinUI.Border.BorderThicknessProperty);
            else if (fe is WinUI.Grid btGrid) btGrid.ClearValue(WinUI.Grid.BorderThicknessProperty);
            else if (fe is WinUI.StackPanel btStack) btStack.ClearValue(WinUI.StackPanel.BorderThicknessProperty);
            else if (fe is WinUI.RelativePanel btRelative) btRelative.ClearValue(WinUI.RelativePanel.BorderThicknessProperty);
        }
    }

    /// <summary>Background and Foreground.</summary>
    private static void ApplyBackgroundAndForegroundModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool keepRemovedBackground,
        ForegroundModifier.UpdateAction foregroundUpdate)
    {
        // Background (Panel, Control, or Border)
        if (m.Background is not null && !ReferenceEquals(m.Background, oldM?.Background))
        {
            if (fe is WinUI.Panel panel) panel.Background = m.Background;
            else if (fe is WinUI.Control ctrl2) ctrl2.Background = m.Background;
            else if (fe is WinUI.Border bdr) bdr.Background = m.Background;
        }
        else if (m.Background is null && oldM?.Background is not null && !keepRemovedBackground)
        {
            if (fe is WinUI.Panel panel) panel.ClearValue(WinUI.Panel.BackgroundProperty);
            else if (fe is WinUI.Control ctrl2) ctrl2.ClearValue(WinUI.Control.BackgroundProperty);
            else if (fe is WinUI.Border bdr) bdr.ClearValue(WinUI.Border.BackgroundProperty);
        }

        ForegroundModifier.Apply(fe, oldM, m, foregroundUpdate);
    }

    /// <summary>AutomationName/Id, IsDragRegion, ElementSoundMode, and the Tier 1 accessibility and focus properties.</summary>
    private static void ApplyAutomationAndFocusModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // AutomationProperties.Name
        if (m.AutomationName is not null && m.AutomationName != oldM?.AutomationName)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, m.AutomationName);
        else if (m.AutomationName is null && oldM?.AutomationName is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty);

        // AutomationProperties.AutomationId
        if (m.AutomationId is not null && m.AutomationId != oldM?.AutomationId)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(fe, m.AutomationId);
        else if (m.AutomationId is null && oldM?.AutomationId is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty);

        // TitleBar.IsDragRegion (attached; inert outside a TitleBar). WinApp SDK ≥ 2.1.3.
        if (m.IsDragRegion.HasValue && m.IsDragRegion != oldM?.IsDragRegion)
            WinUI.TitleBar.SetIsDragRegion(fe, m.IsDragRegion.Value);
        else if (!m.IsDragRegion.HasValue && oldM?.IsDragRegion.HasValue == true)
            fe.ClearValue(WinUI.TitleBar.IsDragRegionProperty);

        // ElementSoundMode (on Control, not FrameworkElement)
        // The reset arm re-tests `fe is Control` under its own pattern variable rather than
        // reusing `ctrl`: reaching the `else` only proves the whole set condition was false,
        // which a non-Control satisfies while still having the modifier set. Without the
        // re-test a Border that carries `.SoundMode(...)` would take the reset branch and
        // clear a property it never had. Same shape as IsEnabled (ApplyIsEnabledModifier, issue #986).
        if (m.ElementSoundMode.HasValue && m.ElementSoundMode != oldM?.ElementSoundMode && fe is WinUI.Control ctrl)
            ctrl.ElementSoundMode = m.ElementSoundMode.Value;
        else if (!m.ElementSoundMode.HasValue && oldM?.ElementSoundMode.HasValue == true && fe is WinUI.Control ctrlSnd)
            ctrlSnd.ClearValue(WinUI.Control.ElementSoundModeProperty);

        // ── Accessibility — Tier 1 (inline properties) ──────────────
        if (m.HeadingLevel.HasValue && m.HeadingLevel != oldM?.HeadingLevel)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(fe, m.HeadingLevel.Value);
        else if (!m.HeadingLevel.HasValue && oldM?.HeadingLevel.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HeadingLevelProperty);

        if (m.IsTabStop.HasValue && m.IsTabStop != oldM?.IsTabStop)
            fe.IsTabStop = m.IsTabStop.Value;
        else if (!m.IsTabStop.HasValue && oldM?.IsTabStop.HasValue == true)
            fe.ClearValue(UIElement.IsTabStopProperty);

        if (m.IsHitTestVisible.HasValue && m.IsHitTestVisible != oldM?.IsHitTestVisible)
            fe.IsHitTestVisible = m.IsHitTestVisible.Value;
        else if (!m.IsHitTestVisible.HasValue && oldM?.IsHitTestVisible.HasValue == true)
            fe.ClearValue(UIElement.IsHitTestVisibleProperty);

        if (m.TabIndex.HasValue && m.TabIndex != oldM?.TabIndex && fe is WinUI.Control tabIdxCtrl)
            tabIdxCtrl.TabIndex = m.TabIndex.Value;
        else if (!m.TabIndex.HasValue && oldM?.TabIndex.HasValue == true && fe is WinUI.Control tabIdxCtrl2)
            tabIdxCtrl2.ClearValue(WinUI.Control.TabIndexProperty);

        if (m.AccessKey is not null && m.AccessKey != oldM?.AccessKey)
            fe.AccessKey = m.AccessKey;
        else if (m.AccessKey is null && oldM?.AccessKey is not null)
            fe.ClearValue(UIElement.AccessKeyProperty);

        if (m.XYFocusKeyboardNavigation.HasValue && m.XYFocusKeyboardNavigation != oldM?.XYFocusKeyboardNavigation)
            fe.XYFocusKeyboardNavigation = m.XYFocusKeyboardNavigation.Value;
        else if (!m.XYFocusKeyboardNavigation.HasValue && oldM?.XYFocusKeyboardNavigation.HasValue == true)
            fe.ClearValue(UIElement.XYFocusKeyboardNavigationProperty);
    }

    /// <summary>FontFamily, FontSize, and FontWeight.</summary>
    private static void ApplyTypographyModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // ── Typography (FontFamily, FontSize, FontWeight) ──────────
        if (m.FontFamily is not null && !ReferenceEquals(m.FontFamily, oldM?.FontFamily))
        {
            if (fe is WinUI.Control ffCtrl) ffCtrl.FontFamily = m.FontFamily;
            else if (fe is TextBlock ffTb) ffTb.FontFamily = m.FontFamily;
        }
        else if (m.FontFamily is null && oldM?.FontFamily is not null)
        {
            if (fe is WinUI.Control ffCtrl) ffCtrl.ClearValue(WinUI.Control.FontFamilyProperty);
            else if (fe is TextBlock ffTb) ffTb.ClearValue(TextBlock.FontFamilyProperty);
        }
        if (m.FontSize.HasValue && m.FontSize != oldM?.FontSize)
        {
            if (fe is WinUI.Control fsCtrl) fsCtrl.FontSize = m.FontSize.Value;
            else if (fe is TextBlock fsTb) fsTb.FontSize = m.FontSize.Value;
        }
        else if (!m.FontSize.HasValue && oldM?.FontSize.HasValue == true)
        {
            if (fe is WinUI.Control fsCtrl) fsCtrl.ClearValue(WinUI.Control.FontSizeProperty);
            else if (fe is TextBlock fsTb) fsTb.ClearValue(TextBlock.FontSizeProperty);
        }
        if (m.FontWeight.HasValue && m.FontWeight != oldM?.FontWeight)
        {
            if (fe is WinUI.Control fwCtrl) fwCtrl.FontWeight = m.FontWeight.Value;
            else if (fe is TextBlock fwTb) fwTb.FontWeight = m.FontWeight.Value;
        }
        else if (!m.FontWeight.HasValue && oldM?.FontWeight.HasValue == true)
        {
            if (fe is WinUI.Control fwCtrl) fwCtrl.ClearValue(WinUI.Control.FontWeightProperty);
            else if (fe is TextBlock fwTb) fwTb.ClearValue(TextBlock.FontWeightProperty);
        }
    }

    /// <summary>OnMount, OnUpdate, and OnUnmount callbacks.</summary>
    private void ApplyLifecycleCallbackModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // OnMountAction — only run on initial mount (oldM is null). Spec 066 §9 row 1: name the
        // modifier when WinUI rejects a facade write the callback makes after the handoff; the
        // filter returns false, so the exception is unchanged.
        if (m.OnMountAction is not null && oldM is null)
        {
            var opacityProbe = new OpacityRestartProbe(fe);
            try { m.OnMountAction(fe); }
            catch (UnauthorizedAccessException) when (FacadeConflictDiagnostic.ReportRejectedSetter(fe, "'.OnMount(...)' callback")) { throw; }
            opacityProbe.Complete();
        }

        // OnUpdateAction — the update-time counterpart, run on every in-place
        // update (oldM is not null) but never at mount. Runs after child
        // reconciliation (ApplyModifiers is invoked post-dispatch in the update
        // path), so descendants realized during this update are already in the
        // visual tree. Chart label a11y uses it to re-hide newly realized
        // focusable descendants that OnMountAction (mount-only) would miss (#162).
        if (m.OnUpdateAction is not null && oldM is not null)
        {
            var opacityProbe = new OpacityRestartProbe(fe);
            m.OnUpdateAction(fe);
            opacityProbe.Complete();
        }

        // OnUnmountAction — captured here (authoritative modifier set) so it fires
        // reliably at teardown regardless of the tagged element's identity. Keep the
        // latest action across re-renders; don't clear an inner target's registration
        // when a shared-control wrapper drops its own callback. Owner identity prevents
        // a retained control from firing a disposed reconciler's callback in a new host.
        // Known limitation: a shared control has one slot, so a wrapper's callback
        // replaces the target's. If the wrapper later drops it while the target update is
        // shallow-skipped, nothing re-registers the target's callback and it is lost.
        if (m.OnUnmountAction is not null)
        {
            var callbackState = GetOrCreateReactorState(fe);
            callbackState.UnmountAction = m.OnUnmountAction;
            callbackState.UnmountActionOwner = _unmountActionOwner;
            _boundaryMountJournal?.Add(fe);
        }
        else if (oldM?.OnUnmountAction is not null
            && TryGetReactorState(fe, out var unmountState)
            && ReferenceEquals(unmountState.UnmountActionOwner, _unmountActionOwner)
            && ReferenceEquals(unmountState.UnmountAction, oldM.OnUnmountAction))
        {
            unmountState.UnmountAction = null;
            unmountState.UnmountActionOwner = null;
        }
    }

    /// <summary>The element ref (.Ref).</summary>
    private static void ApplyRefModifier(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // Element ref — populate on mount/update so imperative APIs (FocusManager.Focus)
        // can target the mounted control. Writing on every update is cheap (single field
        // write) and keeps the ref fresh when the pool recycles elements. When the ref is
        // swapped or removed (oldM.Ref differs from m.Ref), clear the old cell first so it
        // doesn't dangle pointing at a control it no longer represents (CR-001).
        var oldRef = oldM?.Ref;
        var newRef = m.Ref;
        if (oldRef is not null && !ReferenceEquals(oldRef, newRef))
        {
            oldRef.SetCurrent(null);
            UntrackProducerRef(fe, oldRef);
        }
        if (newRef is not null)
        {
            // Every write that points a ref at this control is recorded, so a ref already
            // pointing here is already tracked and the steady-state update skips the DP read.
            // Record it before SetCurrent, whose CurrentChanged dispatch can throw.
            if (!ReferenceEquals(newRef.Current, fe)) TrackProducerRef(fe, newRef);
            newRef.SetCurrent(fe);
            ReferenceEdgeWiring.AssertTypedRefMatch(newRef, fe);
        }
    }
}
