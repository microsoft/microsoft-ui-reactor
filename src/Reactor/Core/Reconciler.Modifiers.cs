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
        // A helper listed in ModifierEmitter.GeneratedHelpers (spec 067 layer 3) lives in
        // Reconciler.Modifiers.Generated.cs: add the field's ModifierSchema slot and run
        // tools/Reactor.ModifierGen instead of editing it.
        ApplyRequestedThemeModifier(fe, oldM, m);
        ApplyMarginModifiers(fe, oldM, m, ownsControl);
        ApplyPaddingModifiers(fe, oldM, m, ownsControl);
        ApplySizeAndAlignmentModifiers(fe, oldM, m);
        ApplyOpacityAndTransformModifiers(fe, oldM, m);
        ApplyVisibilityModifier(fe, oldM, m);
        ApplyToolTipModifiers(fe, oldM, m, requestRerender);
        ApplyToolTipPlacementModifier(fe, oldM, m);
        ApplyFlyoutModifiers(fe, oldM, m, requestRerender);
        ApplyIsEnabledAndCornerRadiusModifiers(fe, oldM, m);
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
        // inline edge changed) and wrote `x` back, and the release never ran (#1749). The release
        // in ApplyResolvedPaddingModifier tests the bag, as Margin's reset does.
        var resolvedPadding = m.Padding;
        InlineOverlay? prevPadOverlay = null;
        if (m.PaddingInlineStart.HasValue || m.PaddingInlineEnd.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            if (ownsControl && oldM is { } oldPads && (oldPads.PaddingInlineStart.HasValue || oldPads.PaddingInlineEnd.HasValue))
                prevPadOverlay = GetInlineOverlays(fe)?.Padding;
            var basePad = resolvedPadding ?? oldM?.Padding ?? UnoverlaidBase(ReadPadding(fe), prevPadOverlay);
            var left = isRtl ? (m.PaddingInlineEnd ?? basePad.Left) : (m.PaddingInlineStart ?? basePad.Left);
            var right = isRtl ? (m.PaddingInlineStart ?? basePad.Right) : (m.PaddingInlineEnd ?? basePad.Right);
            resolvedPadding = new Thickness(left, basePad.Top, right, basePad.Bottom);
            var padOverlay = new InlineOverlay(basePad, resolvedPadding.Value);
            if (ownsControl && prevPadOverlay != padOverlay)
                GetOrCreateInlineOverlays(fe).Padding = padOverlay;
        }
        // Same inline-delta rule as Margin (ApplyMarginModifiers): the generated write also fires
        // when an inline edge changed or the previous overlay wrote something else.
        ApplyResolvedPaddingModifier(fe, oldM, m, resolvedPadding, prevPadOverlay is { } pp && pp.Written != resolvedPadding);
    }

    /// <summary>Opacity and the facade transforms (Scale, Rotation, Translation, TransformOrigin, CenterPoint).</summary>
    private void ApplyOpacityAndTransformModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
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
    }

    /// <summary>ToolTip and RichToolTip.</summary>
    private void ApplyToolTipModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, Action requestRerender)
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
    }

    /// <summary>AttachedFlyout and ContextFlyout.</summary>
    private void ApplyFlyoutModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, Action requestRerender)
    {
        if (m.AttachedFlyout is not null)
            ApplyFlyoutAttachment(fe, oldM?.AttachedFlyout, m.AttachedFlyout, requestRerender);
        else if (oldM?.AttachedFlyout is not null && UnmountFlyoutAttachmentContent(fe, out var displaced))
        {
            fe.ClearValue(FlyoutSlotProperty(fe));
            // A button's own flyout: argument the modifier displaced takes the slot back.
            if (displaced is not null) SetFlyoutOnControl(fe, displaced);
        }

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

    /// <summary>BorderBrush and BorderThickness with its BiDi-aware inline overlay (BorderInlineStart).</summary>
    private void ApplyBorderModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool ownsControl)
    {
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
            var baseBorder = resolvedBorder ?? oldM?.BorderThickness ?? UnoverlaidBase(ReadBorderThickness(fe), prevBorderOverlay);
            var inlineStartThickness = m.BorderInlineStart.Value;
            if (isRtl)
                resolvedBorder = new Thickness(baseBorder.Left, baseBorder.Top, inlineStartThickness.Left, baseBorder.Bottom);
            else
                resolvedBorder = new Thickness(inlineStartThickness.Left, baseBorder.Top, baseBorder.Right, baseBorder.Bottom);
            var borderOverlay = new InlineOverlay(baseBorder, resolvedBorder.Value);
            if (ownsControl && prevBorderOverlay != borderOverlay)
                GetOrCreateInlineOverlays(fe).Border = borderOverlay;
        }
        // Same inline-delta rule as Margin/Padding: the generated write also fires when the inline
        // edge changed or the previous overlay wrote something else, and the release fires when
        // the old bag carried only an inline border too, or that edge would never be released.
        ApplyResolvedBorderThicknessModifier(fe, oldM, m, resolvedBorder, prevBorderOverlay is { } pb2 && pb2.Written != resolvedBorder);
    }

    /// <summary>Background and Foreground.</summary>
    private void ApplyBackgroundAndForegroundModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, bool keepRemovedBackground,
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

        ForegroundModifier.Apply(fe, oldM, m, foregroundUpdate, this);
    }

    internal InteractionStatesConfig? FindUpdatingInteractionStates(FrameworkElement fe)
    {
        if (ReferenceEquals(ModifierUpdate.Control, fe) && ModifierUpdate.ClearsInteractionStates) return null;
        // Use the new shared-target chain so removing interaction states still writes the
        // normal foreground. A resource-refreshed Memo exposes its rebuilt Flyout during recursion.
        var source = ReferenceEquals(ModifierUpdate.Control, fe) ? ModifierUpdate.Root : _reconcilingElement;
        var interactionStates = FindMountedElementValue(fe, static e => e.InteractionStates, source)
            ?? (ReferenceEquals(UpdatingFlyout.Target, fe)
                ? FindMountedElementValue(fe, static e => e.InteractionStates, UpdatingFlyout.Decorator) : null);
        // A keyed Memo has no declarative target; its inner update has already installed it.
        if (interactionStates is null && _reconcilingElement is KeyedMemoElement)
            interactionStates = FindMountedElementValue(fe, static e => e.InteractionStates);
        return interactionStates;
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
