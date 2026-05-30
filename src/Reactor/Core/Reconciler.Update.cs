using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using Windows.UI.WebUI;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: Reconciler.Update.cs — patches existing WinUI controls to match new Elements.
// Update() diffs old vs new Element and mutates the existing control in-place.
// Critical optimization: Element.ShallowEquals short-circuits when nothing changed.
// Returns null if existing control was patched; returns a new UIElement if the
// control type changed (caller must swap). Each UpdateXxx method mirrors its
// MountXxx counterpart but only touches properties that differ.

public sealed partial class Reconciler
{
    /// <summary>
    /// Diffs oldEl vs newEl and patches the existing control. Returns null if patched in-place,
    /// or a replacement UIElement if the control type changed at runtime.
    /// </summary>
    internal UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        DebugElementsDiffed++;
        // Unwrap all layers of ModifiedElement, accumulating modifiers.
        // Inner modifiers override outer ones (via Merge: other wins where non-null).
        ElementModifiers? oldModifiers = oldEl.Modifiers;
        ElementModifiers? modifiers = newEl.Modifiers;
        while (oldEl is ModifiedElement oldMod && newEl is ModifiedElement newMod)
        {
            oldModifiers = oldModifiers is not null
                ? oldModifiers.Merge(oldMod.WrappedModifiers)
                : oldMod.WrappedModifiers;
            modifiers = modifiers is not null
                ? modifiers.Merge(newMod.WrappedModifiers)
                : newMod.WrappedModifiers;
            oldEl = oldMod.Inner;
            newEl = newMod.Inner;
        }
        // Merge any modifiers from the final inner element
        if (oldEl.Modifiers is not null)
            oldModifiers = oldModifiers is not null ? oldModifiers.Merge(oldEl.Modifiers) : oldEl.Modifiers;
        if (newEl.Modifiers is not null)
            modifiers = modifiers is not null ? modifiers.Merge(newEl.Modifiers) : newEl.Modifiers;

        // Short-circuit: if old and new elements are structurally identical,
        // skip all WinUI property access. This is the critical optimization for
        // large grids where only a fraction of elements change each frame.
        // Exception: elements with ThemeBindings must always re-apply because
        // the resolved brush value depends on the control's effective theme,
        // which can change independently of the element tree (e.g., parent
        // RequestedTheme toggle).
        // ReferenceEquals would fail constantly because fluent chains like
        // .Width(200).Margin(10) produce a fresh ElementModifiers each render —
        // identical values, new instance. Use structural equality so we skip
        // when nothing actually changed.
        //
        // Callback-presence (oldEl.HasCallbacks == newEl.HasCallbacks) must
        // also match: ShallowEquals ignores delegate identity, so a null→non-null
        // OnClick transition would otherwise be skipped and the lazy-wire path
        // in UpdateXxx never gets to attach the WinRT event. If presence
        // changes, force Update so EnsureXxxWiring (poolable) or the diff-based
        // null→non-null checks (non-poolable) can subscribe.
        if (Element.ShallowEquals(oldEl, newEl)
            && Element.ModifiersEqual(oldModifiers, modifiers)
            && oldEl.HasCallbacks == newEl.HasCallbacks
            && !ForceRenderThroughWrapper(newEl)
            && !IsOnDirtyAncestorPath(control))
        {
            DebugElementsSkipped++;
            // Refresh Tag so the event trampoline dispatches into the new element's
            // closure on next click/value-change. Gated on HasCallbacks so we skip
            // the DependencyProperty write entirely for leaves with no handlers
            // (TextBlock, Image, Border, etc.) — which is most of them.
            if (newEl.HasCallbacks && control is FrameworkElement tagFeSE)
                SetElementTag(tagFeSE, newEl);
            if (newEl.ThemeBindings is not null && control is FrameworkElement thFeSE)
                ApplyThemeBindings(thFeSE, newEl.ThemeBindings);
            // Re-resolve ThemeRef-based resource overrides on theme change
            if (newEl.ResourceOverrides is { ThemeRefs.Count: > 0 } && control is FrameworkElement resFeSE)
                ApplyResourceOverrides(resFeSE, newEl.ResourceOverrides, newEl.ResourceOverrides);
            return null; // null = keep existing control as-is
        }
        DebugUIElementsModified++;

        // Push context values onto scope before processing children
        var ctxValues = newEl.ContextValues;
        int ctxCount = 0;
        if (ctxValues is { Count: > 0 })
        {
            _contextScope.Push(ctxValues);
            ctxCount = ctxValues.Count;
        }

        UIElement? result;
        try
        {

        // Spec 047 §14 Phase 4 (§4.5) — dispatch is V1 registry → external
        // `_typeRegistry` → composition-primitive switch (legacy `UpdateXxx`
        // arms deleted; V1 is the production path).
        // V1 Update returns the UIElement to install in the parent's
        // slot. Standard handlers always return `control` unchanged
        // (§13 Q12 — no substitution on the public author surface);
        // decorator-style handlers (§14 Phase 3 completion) may return
        // a different instance (target-wrapping decorators whose
        // Target changed type). When the returned instance equals
        // `control`, set `result` to null so callers preserve identity.
        if (_v1Handlers.TryGet(newEl.GetType(), out var v1Entry))
        {
            var v1Result = v1Entry.Update(oldEl, newEl, control, requestRerender, this);
            result = ReferenceEquals(v1Result, control) ? null : v1Result;
        }
        // Registered types checked first
        else if (_typeRegistry.TryGetValue(newEl.GetType(), out var reg))
        {
            result = reg.Update(oldEl, newEl, control, requestRerender, this);
        }
        else
        {
        result = (oldEl, newEl, control) switch
        {
            // Typed, data-driven TreeView<T> — hand-coded escape-hatch path
            // (per-container hosting), so it stays on the switch. (#447)
            (TemplatedTreeViewElementBase o, TemplatedTreeViewElementBase n, WinUI.TreeView ttv)
                => UpdateTemplatedTreeView(o, n, ttv, requestRerender),
            (CommandHostElement o, CommandHostElement n, WinUI.Grid chGrid)
                => UpdateCommandHost(o, n, chGrid, requestRerender),
            (ErrorBoundaryElement oldEb, ErrorBoundaryElement newEb, Border)
                => UpdateErrorBoundary(oldEb, newEb, control, requestRerender),
            (FormFieldElement oldFf, FormFieldElement newFf, WinUI.StackPanel sp)
                => UpdateFormField(oldFf, newFf, sp, requestRerender),
            (ValidationVisualizerElement oldVv, ValidationVisualizerElement newVv, WinUI.StackPanel sp)
                => UpdateValidationVisualizer(oldVv, newVv, sp, requestRerender),
            (ValidationRuleElement, ValidationRuleElement n, WinUI.StackPanel)
                => UpdateValidationRule(n),
            (ComponentElement, ComponentElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            (FuncElement, FuncElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            (MemoElement, MemoElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            _ => Mount(newEl, requestRerender),
        };
        }

        // Apply inline modifiers after update. When old modifiers existed but new
        // modifiers are null, pass an empty instance so ApplyModifiers can clear
        // stale values (same principle as the flex attached-property fix).
        var target = result ?? control;

        // Record the control for highlight overlay only when the element's own
        // WinUI properties were actually updated (not just children recursed).
        // Containers whose only change is children references are excluded — the
        // individual children will be captured if they change.
        if (result is null && ReactorFeatureFlags.HighlightReconcileChanges
            && _highlightModified is not null
            && (!Element.OwnPropsEqual(oldEl, newEl) || !Element.ModifiersEqual(oldModifiers, modifiers)))
            _highlightModified.Add(control);
        if ((modifiers is not null || oldModifiers is not null) && target is FrameworkElement fe)
            ApplyModifiers(fe, oldModifiers, modifiers ?? new ElementModifiers(), requestRerender);

        // Re-apply the caption-derived default after modifiers have run so a
        // label change ("+ 1" → "+ 2") updates UIA Name when the author never
        // set an explicit name. No-ops when the author did.
        if (target is FrameworkElement captionFe)
            UpdateDefaultAutomationName(
                captionFe,
                ResolveCaptionForElement(oldEl),
                ResolveCaptionForElement(newEl));

        // Apply theme-resource bindings (ThemeRef → resolved Brush from WinUI resources)
        if (newEl.ThemeBindings is not null && target is FrameworkElement thFe)
            ApplyThemeBindings(thFe, newEl.ThemeBindings);

        // Apply per-control resource overrides (lightweight styling)
        if ((newEl.ResourceOverrides is not null || oldEl.ResourceOverrides is not null) && target is FrameworkElement resFe)
            ApplyResourceOverrides(resFe, oldEl.ResourceOverrides, newEl.ResourceOverrides);

        // Apply transitions after update (re-applies when transition config changes)
        if (newEl.ImplicitTransitions is not null || newEl.ThemeTransitions is not null)
            ApplyTransitions(target, newEl.ImplicitTransitions, newEl.ThemeTransitions);

        // Apply or clear Composition-layer layout animation
        if (newEl.LayoutAnimation is not null)
            ApplyLayoutAnimation(target, newEl.LayoutAnimation);
        else if (oldEl.LayoutAnimation is not null)
            ClearLayoutAnimation(target);

        // Apply or clear compositor property animation (.Animate() modifier)
        if (newEl.AnimationConfig is not null)
            ApplyPropertyAnimation(target, newEl.AnimationConfig, newEl.LayoutAnimation);
        else if (oldEl.AnimationConfig is not null)
            ClearPropertyAnimation(target, newEl.LayoutAnimation);

        // Apply or clear interaction states (.InteractionStates() modifier)
        if (newEl.InteractionStates is not null)
            ApplyInteractionStates(target, newEl.InteractionStates);
        else if (oldEl.InteractionStates is not null)
            ClearInteractionStates(target);

        // Apply keyframe animations (.Keyframes() modifier)
        if (newEl.KeyframeAnimations is not null)
            ApplyKeyframeAnimations(target, newEl.KeyframeAnimations);
        else if (oldEl.KeyframeAnimations is not null)
            ClearKeyframeAnimations(target, oldEl.KeyframeAnimations);

        // Apply or clear scroll-linked expression animations (.ScrollLinked() modifier)
        if (newEl.ScrollAnimation is not null)
            ApplyScrollAnimation(target, newEl.ScrollAnimation);
        else if (oldEl.ScrollAnimation is not null)
            ClearScrollAnimation(target, oldEl.ScrollAnimation);

        // Apply stagger delays to children (.Stagger() modifier)
        if (newEl.StaggerConfig is not null)
            ApplyStaggerDelays(target, newEl.StaggerConfig);

        }
        finally
        {
            if (ctxCount > 0)
                _contextScope.Pop(ctxCount);
        }

        return result;
    }

    private static Microsoft.UI.Xaml.Documents.Inline MountInline(RichTextInline inline)
    {
        switch (inline)
        {
            case RichTextRun run:
                var r = new Microsoft.UI.Xaml.Documents.Run { Text = run.Text };
                if (run.IsBold) r.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                if (run.IsItalic) r.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
                if (run.IsStrikethrough) r.TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough;
                if (run.FontSize.HasValue) r.FontSize = run.FontSize.Value;
                if (run.FontFamily is not null) r.FontFamily = WinRTCache.GetFontFamily(run.FontFamily);
                if (run.Foreground is not null) r.Foreground = run.Foreground;
                return r;
            case RichTextHyperlink link:
                var l = link?.NavigateUri ?? new Uri("about:blank");
                l = l.ToString().Length < 1 ? l = new Uri("about:blank") : l;
                var hl = new Microsoft.UI.Xaml.Documents.Hyperlink();
                try { hl.NavigateUri = l; } catch { hl.NavigateUri = new Uri("about:blank"); }
                hl.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = link?.Text ?? ""});
                return hl;
            case RichTextLineBreak:
                return new Microsoft.UI.Xaml.Documents.LineBreak();
            default:
                return new Microsoft.UI.Xaml.Documents.Run { Text = "" };
        }
    }

    private static Microsoft.UI.Xaml.Documents.Paragraph MountParagraph(RichTextParagraph para)
    {
        var p = new Microsoft.UI.Xaml.Documents.Paragraph();
        foreach (var inline in para.Inlines)
            p.Inlines.Add(MountInline(inline));
        return p;
    }

    // Spec 047 §14 Phase 3-final Batch B — widened to internal static so the
    // legacy MountRichTextBlock arm AND RichTextBlockDescriptor's .OneWay set
    // lambda call the same rebuild path.
    internal static void RebuildRichTextBlocks(RichTextBlockElement n, WinUI.RichTextBlock rtb)
    {
        rtb.Blocks.Clear();
        if (n.Paragraphs is not null)
        {
            foreach (var para in n.Paragraphs)
                rtb.Blocks.Add(MountParagraph(para));
        }
        else
        {
            var p = new Microsoft.UI.Xaml.Documents.Paragraph();
            p.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = n.Text });
            rtb.Blocks.Add(p);
        }
    }





    private static bool CanSynchronizeNumberBoxImmediateValueWithoutReformat(NumberBoxElement el, string text, double value)
    {
        if (el.NumberFormatter is not null) return false;
        var canonical = value.ToString("G", global::System.Globalization.CultureInfo.CurrentCulture);
        return string.Equals(text, canonical, StringComparison.Ordinal);
    }

    private static bool AreNumberBoxValuesEquivalent(double left, double right)
    {
        var tolerance = 1e-12 * global::System.Math.Max(
            1.0,
            global::System.Math.Max(global::System.Math.Abs(left), global::System.Math.Abs(right)));
        return global::System.Math.Abs(left - right) <= tolerance;
    }


    internal UIElement? UpdateCheckBox(CheckBoxElement o, CheckBoxElement n, WinUI.CheckBox cb)
    {
        SetElementTag(cb, n);
        bool oldWired = o.OnIsCheckedChanged is not null || o.OnCheckedStateChanged is not null;
        bool newWired = n.OnIsCheckedChanged is not null || n.OnCheckedStateChanged is not null;
        if (!oldWired && newWired)
        {
            cb.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(true);
                el?.OnCheckedStateChanged?.Invoke(true);
            };
            cb.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(false);
                el?.OnCheckedStateChanged?.Invoke(false);
            };
            cb.Indeterminate += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnCheckedStateChanged?.Invoke(null);
            };
        }
        cb.Content = n.Label;
        cb.IsThreeState = n.IsThreeState;
        var target = n.IsThreeState ? n.CheckedState : n.IsChecked;
        if (cb.IsChecked != target)
        {
            ChangeEchoSuppressor.BeginSuppress(cb);
            cb.IsChecked = target;
        }
        ApplySetters(n.Setters, cb);
        return null;
    }

    internal UIElement? UpdateExpander(ExpanderElement o, ExpanderElement n, WinUI.Expander exp, Action requestRerender)
    {
        exp.IsExpanded = n.IsExpanded;
        exp.ExpandDirection = n.ExpandDirection;

        // Element header wins over the string slot. Reconcile via ReconcileChild
        // when both old and new use HeaderTemplate; otherwise swap modes.
        if (n.HeaderTemplate is not null)
        {
            ReconcileChild(o.HeaderTemplate, n.HeaderTemplate,
                () => exp.Header as UIElement,
                c => exp.Header = c,
                () => exp.Header = n.Header,
                requestRerender);
        }
        else
        {
            if (exp.Header is UIElement headerCtrl) Unmount(headerCtrl);
            exp.Header = n.Header;
        }

        if (!ReferenceEquals(o.ContentTransitions, n.ContentTransitions))
            exp.ContentTransitions = n.ContentTransitions;

        // Reconcile content child
        if (exp.Content is UIElement existingContent && CanUpdate(o.Content, n.Content))
        {
            var replacement = Update(o.Content, n.Content, existingContent, requestRerender);
            if (replacement is not null && !ReferenceEquals(exp.Content, replacement))
                exp.Content = replacement;
        }
        else
        {
            if (exp.Content is UIElement oldContent)
                Unmount(oldContent);
            exp.Content = Mount(n.Content, requestRerender);
        }

        SetElementTag(exp, n);
        if (o.OnIsExpandedChanged is null && n.OnIsExpandedChanged is not null)
        {
            exp.Expanding += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
            exp.Collapsed += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        }
        ApplySetters(n.Setters, exp);
        return null;
    }

    internal UIElement? UpdateNavigationHost(
        NavigationHostElement oldEl, NavigationHostElement newEl,
        WinUI.Grid grid, Action requestRerender)
    {
        if (!_navigationHostNodes.TryGetValue(grid, out var node))
        {
            // Lost tracking — remount from scratch
            return Mount(newEl, requestRerender);
        }

        var handle = (Navigation.INavigationHandle)newEl.NavigationHandle;
        var currentRoute = handle.CurrentRoute;

        // Update the RouteMap if the delegate reference changed (rare but possible if
        // the parent component recreates the lambda every render).
        node.RouteMap = newEl.RouteMap;

        // If the handle changed (different navigation stack wired up), re-subscribe
        if (!ReferenceEquals(node.Handle, handle))
        {
            if (node.RouteChangedHandler is not null)
                node.Handle.RouteChanged -= node.RouteChangedHandler;
            node.Handle.Detach();

            node.Handle = handle;
            void onRouteChanged() => requestRerender();
            handle.RouteChanged += onRouteChanged;
            node.RouteChangedHandler = onRouteChanged;

            // Re-wire lifecycle guard for the new handle
            handle.LifecycleGuard = ctx =>
            {
                InvokeNavigatingFrom(node.CurrentChildControl, ctx);
                if (!ctx.IsCancelled)
                {
                    node.PendingNavigationMode = ctx.Mode;
                    node.PendingPreviousRoute = ctx.Route;
                }
            };
        }

        node.RequestRerender = requestRerender;

        if (Equals(currentRoute, node.LastRenderedRoute) && node.CurrentChildElement is not null)
        {
            // Route unchanged — reconcile the existing child element in place
            var newChildElement = node.RouteMap(currentRoute);
            var replacement = node.CurrentChildControl is not null
                ? Update(node.CurrentChildElement, newChildElement, node.CurrentChildControl, requestRerender)
                : Mount(newChildElement, requestRerender);

            if (replacement is not null && node.CurrentChildControl is not null)
            {
                // Child control type changed — swap in grid
                var idx = grid.Children.IndexOf(node.CurrentChildControl);
                if (idx >= 0)
                    grid.Children[idx] = replacement;
                else
                    grid.Children.Add(replacement);
                Unmount(node.CurrentChildControl);
                node.CurrentChildControl = replacement;
            }
            else if (replacement is not null)
            {
                grid.Children.Add(replacement);
                node.CurrentChildControl = replacement;
            }

            node.CurrentChildElement = newChildElement;
        }
        else
        {
            // Route changed — transition from old page to new page.
            // Lifecycle sequence per spec:
            //   1. onNavigatingFrom (already done by LifecycleGuard before stack mutation)
            //   2-3. Stack mutation (already done)
            //   4-5. Resolve + mount new element (or restore from cache)
            //   6. Run transition animation
            //   7. onNavigatedTo (new page)
            //   8. onNavigatedFrom (old page)
            //   9. Unmount or cache old element

            var oldChildControl = node.CurrentChildControl;
            var oldChildElement = node.CurrentChildElement;
            var previousRoute = node.LastRenderedRoute;
            var pendingMode = node.PendingNavigationMode;
            var pendingPreviousRoute = node.PendingPreviousRoute;
            node.PendingNavigationMode = null;
            node.PendingPreviousRoute = null;

            // Collect lifecycle hooks from old page BEFORE detach/unmount
            var oldHooks = pendingMode is not null
                ? CollectLifecycleHooks(oldChildControl)
                : null;

            // Resolve transition: per-navigation override > host default
            var transitionOverride = handle.PendingTransitionOverride;
            handle.PendingTransitionOverride = null;
            var transition = transitionOverride ?? node.HostTransition;
            var mode = pendingMode ?? Navigation.NavigationMode.Push;

            // Resolve new child: check cache first, then mount fresh
            UIElement? newChildControl;
            Element? newChildElement;

            bool wasCacheHit = false;
            if (node.Cache is not null && node.Cache.TryGet(currentRoute, out var cached))
            {
                // Cache hit — restore the mounted control
                newChildControl = cached.MountedControl;
                newChildElement = cached.LastElement;
                node.Cache.Remove(currentRoute);
                wasCacheHit = true;
            }
            else
            {
                // Cache miss — mount fresh
                newChildElement = node.RouteMap(currentRoute);
                newChildControl = Mount(newChildElement, requestRerender);
            }

            // Destination-side guard: invoke onNavigatingTo on the new page.
            // If cancelled, revert to old page.
            if (!InvokeNavigatingTo(newChildControl, currentRoute, pendingPreviousRoute, mode))
            {
                if (!wasCacheHit && newChildControl is not null)
                    Unmount(newChildControl);
                return null;
            }

            // Update node state immediately
            node.CurrentChildElement = newChildElement;
            node.CurrentChildControl = newChildControl;
            node.LastRenderedRoute = currentRoute;

            // Action to finalize the old page (cache or unmount)
            void FinalizeOldPage(UIElement? oldCtrl, Element? oldElem, object? oldRoute)
            {
                if (oldCtrl is null) return;
                grid.Children.Remove(oldCtrl);

                if (node.Cache is not null && node.CacheMode != Navigation.NavigationCacheMode.Disabled
                    && oldRoute is not null)
                {
                    // Store in cache instead of unmounting
                    node.Cache.Add(oldRoute, new Navigation.CachedPage
                    {
                        MountedControl = oldCtrl,
                        LastElement = oldElem,
                        LastAccessed = DateTime.UtcNow,
                        CacheMode = node.CacheMode,
                    });
                }
                else
                {
                    Unmount(oldCtrl);
                }
            }

            // Determine whether to run an animated transition
            bool useAnimation = transition is not Navigation.SuppressTransition
                && oldChildControl is not null
                && newChildControl is not null;

            if (useAnimation)
            {
                // Mount new content at Opacity 0 alongside old content
                var inVisual = ElementCompositionPreview.GetElementVisual(newChildControl!);
                inVisual.Opacity = 0;
                grid.Children.Add(newChildControl!);
                node.TransitionInProgress = true;

                // Capture references for the completion callback
                var capturedOldControl = oldChildControl;
                var capturedOldElement = oldChildElement;
                var capturedOldRoute = previousRoute;
                var capturedNewControl = newChildControl!;
                var capturedMode = mode;
                var capturedCurrentRoute = currentRoute;
                var capturedPreviousRoute = pendingPreviousRoute;
                var capturedOldHooks = oldHooks;

                Navigation.TransitionEngine.RunTransition(
                    capturedOldControl!, capturedNewControl, transition, capturedMode,
                    onComplete: () =>
                    {
                        node.TransitionInProgress = false;
                        FinalizeOldPage(capturedOldControl, capturedOldElement, capturedOldRoute);

                        InvokePostNavigationLifecycle(
                            capturedNewControl, capturedOldHooks,
                            capturedCurrentRoute, capturedPreviousRoute, capturedMode);
                    });
            }
            else
            {
                // Instant swap (SuppressTransition or missing controls)
                FinalizeOldPage(oldChildControl, oldChildElement, previousRoute);

                if (newChildControl is not null)
                    grid.Children.Add(newChildControl);

                InvokePostNavigationLifecycle(
                    newChildControl, oldHooks,
                    currentRoute, pendingPreviousRoute, mode);
            }
        }

        // Update host properties if changed
        node.HostTransition = newEl.Transition;
        if (node.CacheMode != newEl.CacheMode)
        {
            node.CacheMode = newEl.CacheMode;
            if (newEl.CacheMode == Navigation.NavigationCacheMode.Disabled && node.Cache is not null)
            {
                node.Cache.Clear();
                node.Cache = null;
            }
            else if (newEl.CacheMode != Navigation.NavigationCacheMode.Disabled && node.Cache is null)
            {
                node.Cache = new Navigation.NavigationCache(newEl.CacheSize, evicted => Unmount(evicted));
            }
        }
        if (node.Cache is not null)
            node.Cache.MaxSize = newEl.CacheSize;

        return null; // Patched in place
    }

    private static object? FindNavItemByTag(global::System.Collections.IEnumerable items, string? selectedTag)
    {
        if (selectedTag is null) return null;
        foreach (var item in items)
        {
            if (item is WinUI.NavigationViewItem nvi)
            {
                if ((nvi.Tag as string) == selectedTag) return nvi;
                var child = FindNavItemByTag(nvi.MenuItems, selectedTag);
                if (child is not null) return child;
            }
        }
        return null;
    }


    internal void ReconcileChild(Element? oldChild, Element? newChild,
        Func<UIElement?> getControl, Action<UIElement> setControl, Action clearControl,
        Action requestRerender)
    {
        if (newChild is not null && oldChild is not null
            && getControl() is UIElement existing && CanUpdate(oldChild, newChild))
        {
            var replacement = Update(oldChild, newChild, existing, requestRerender);
            if (replacement is not null) setControl(replacement);
        }
        else if (newChild is not null)
        {
            if (getControl() is UIElement old) Unmount(old);
            var mounted = Mount(newChild, requestRerender);
            if (mounted is not null) setControl(mounted);
        }
        else if (newChild is null && getControl() is UIElement stale)
        {
            Unmount(stale);
            clearControl();
        }
    }


    // Internal visibility — see comment on MountListView (1.15 Path B).
    internal UIElement? UpdateListView(ListViewElement o, ListViewElement n, WinUI.ListView lv, Action requestRerender)
    {
        lv.SelectionMode = n.SelectionMode;
        lv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) lv.Header = n.Header;
        if (lv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            lv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            lv.ItemContainerStyle = n.ItemContainerStyle;

        // Update ItemsSource — ContainerContentChanging re-mounts visible items via Tag.
        // Always set a new list when items differ (even same count) so WinUI re-realizes containers.
        if (!ReferenceEquals(o.Items, n.Items))
            lv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();

        SetElementTag(lv, n);

        // Mount subscribes SelectionChanged unconditionally and reads handlers
        // via GetElementTag, so no lazy wire here — the tag refresh above
        // makes a newly-attached OnSelectedIndexChanged / OnSelectionChanged
        // pick up on the very next selection.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            lv.ItemClick += (s, args) =>
            {
                var l = (WinUI.ListView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
            };

        if (n.SelectedIndex >= 0) lv.SelectedIndex = n.SelectedIndex;
        ApplySetters(n.Setters, lv);
        return null;
    }

    internal UIElement? UpdateGridView(GridViewElement o, GridViewElement n, WinUI.GridView gv, Action requestRerender)
    {
        gv.SelectionMode = n.SelectionMode;
        gv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) gv.Header = n.Header;
        if (gv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            gv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            gv.ItemContainerStyle = n.ItemContainerStyle;

        if (!ReferenceEquals(o.Items, n.Items))
            gv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();

        SetElementTag(gv, n);

        // SelectionChanged is wired unconditionally in Mount (see comment in
        // UpdateListView). Tag refresh suffices to pick up a later-attached
        // OnSelectedIndexChanged / OnSelectionChanged.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            gv.ItemClick += (s, args) =>
            {
                var g = (WinUI.GridView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(g) as GridViewElement)?.OnItemClick?.Invoke(idx);
            };

        if (n.SelectedIndex >= 0) gv.SelectedIndex = n.SelectedIndex;
        ApplySetters(n.Setters, gv);
        return null;
    }

    /// <summary>
    /// Walks visible (realized) containers and reconciles each item's Element
    /// using the stored reactor element (attached DP) as the old element.
    /// Iterates the realized panel children directly rather than calling
    /// ContainerFromIndex(i) for every i in 0..ItemCount — on a virtualized
    /// list with thousands of items but a small viewport, that loop would do
    /// thousands of cross-WinRT lookups per parent re-render and discard
    /// most as null. Children of the realized panel IS the realized set, so
    /// iterating it is O(realized) instead of O(total).
    /// </summary>
    private void RefreshRealizedContainers(WinUI.ListViewBase listViewBase, Internal.IItemViewSource viewSource, Action requestRerender)
    {
        var panel = listViewBase.ItemsPanelRoot;
        if (panel is null) return;

        // Snapshot first — Update may indirectly mount new controls and modifying
        // Children during enumeration throws (WinUI's UIElementCollection enforces
        // this). Counts are small (one viewport's worth) so the copy is cheap.
        var realized = new List<UIElement>(panel.Children.Count);
        foreach (var child in panel.Children) realized.Add(child);

        foreach (var child in realized)
        {
            // Cast to SelectorItem so both ListView (ListViewItem) and GridView
            // (GridViewItem) containers are handled — both derive from SelectorItem
            // and share the same ContentTemplateRoot pattern.
            if (child is not Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container) continue;
            if (container.ContentTemplateRoot is not ContentControl cc) continue;

            var index = listViewBase.IndexFromContainer(container);
            if (index < 0 || index >= viewSource.ItemCount) continue;

            var oldItemElement = GetElementTag(cc);
            var newItemElement = viewSource.BuildItemView(index);

            if (oldItemElement is not null && cc.Content is UIElement existingCtrl && CanUpdate(oldItemElement, newItemElement))
            {
                var replacement = Update(oldItemElement, newItemElement, existingCtrl, requestRerender);
                if (replacement is not null && !ReferenceEquals(cc.Content, replacement))
                    cc.Content = replacement;
            }
            else
            {
                if (cc.Content is UIElement oldCtrl)
                    Unmount(oldCtrl);
                cc.Content = Mount(newItemElement, requestRerender);
            }
            SetElementTag(cc, newItemElement);
        }
    }

    internal UIElement? UpdateTemplatedListView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.ListView lv, Action requestRerender)
    {
        lv.SelectionMode = n.GetSelectionMode();
        lv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) lv.Header = header;

        // SetElementTag *before* the diff so HandleTemplatedContainerContentChanging
        // — which fires synchronously inside the OC insert/move ops — reads the
        // new element when materializing freshly-inserted containers.
        SetElementTag(lv, n);

        // Spec 042 Phase 1: feed structural changes through the
        // keyed-diff pipeline so WinUI animates only the affected
        // containers. RefreshRealizedContainers still runs in every case
        // because the viewBuilder is a closure that may produce
        // different content even when the key set is unchanged.
        ApplyKeyedDiffOrFallback(lv, n);
        RefreshRealizedContainers(lv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        if (selectedIndex >= 0) lv.SelectedIndex = selectedIndex;
        n.ApplyControlSetters(lv);
        return null;
    }

    internal UIElement? UpdateTemplatedGridView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.GridView gv, Action requestRerender)
    {
        gv.SelectionMode = n.GetSelectionMode();
        gv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) gv.Header = header;

        SetElementTag(gv, n);
        ApplyKeyedDiffOrFallback(gv, n);
        RefreshRealizedContainers(gv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        if (selectedIndex >= 0) gv.SelectedIndex = selectedIndex;
        n.ApplyControlSetters(gv);
        return null;
    }

    /// <summary>
    /// Spec 042 §4 — apply the keyed diff to the control's attached
    /// <see cref="ReactorListState"/>. If the state is missing (control
    /// mounted before Phase 1, or the diff bailed out and the legacy
    /// ItemsSource was swapped in) we transparently rebuild a fresh
    /// state and re-bind ItemsSource. The result is correctness either
    /// way; bailout only degrades the animation.
    /// </summary>
    private void ApplyKeyedDiffOrFallback(WinUI.ListViewBase lvb, TemplatedListElementBase n)
    {
        var state = GetListState(lvb);
        if (state is null || !ReferenceEquals(lvb.ItemsSource, state.Source))
        {
            // No state, or another path replaced ItemsSource (bailout from a
            // prior render). Rebuild and rebind in one step.
            var fresh = BuildListStateFromElement(n);
            SetListState(lvb, fresh);
            lvb.ItemsSource = fresh.Source;
            return;
        }

        // Project new keys through the typed peer. Pass the active ambient
        // so newly-inserted ReactorRows are tagged with the kind and the
        // ContainerContentChanging handler can attach a per-container
        // enter animation as those containers realize. (spec 042 §6.)
        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new TemplatedKeyAdapter(n),
            static (item, _) => item.Key,
            _logger,
            lvb.GetType().Name,
            ambient,
            controlInstance: lvb);

        // Drive per-container offset animations for survivors that moved.
        // Insert / Remove animations attach through the realize/recycle
        // path so they survive virtualization; Move requires the live
        // container handle here because the row instance was already
        // realized before the move op.
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimations(lvb, movedRows, ambient.Kind);

        if (stats.Bailout)
        {
            // Reset replaced state.Source's contents in bulk; ItemsSource
            // still references the same OC object so WinUI sees a single
            // Reset action and re-realizes. Acceptable per spec §4.3.
            // No additional binding refresh needed.
        }
    }

    /// <summary>
    /// Adapter so the generic <see cref="KeyedListDiff.Apply{T}"/> can run
    /// against the abstract non-generic <see cref="TemplatedListElementBase"/>
    /// without an extra allocation per item.
    /// </summary>
    private readonly struct TemplatedKeyAdapter : IReadOnlyList<TemplatedKeyAdapter.KeyOnly>
    {
        private readonly TemplatedListElementBase _el;
        public TemplatedKeyAdapter(TemplatedListElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static ReactorListState BuildListStateFromElement(TemplatedListElementBase el)
    {
        var state = new ReactorListState();
        int n = el.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, el.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    internal UIElement? UpdateTemplatedFlipView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.FlipView fv, Action requestRerender)
    {
        // FlipView items are pre-mounted directly (no ContainerContentChanging).
        // Build old element array from o, then reconcile like regular items.
        int oldCount = o.ItemCount;
        int newCount = n.ItemCount;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            var oldItemElement = o.BuildItemView(i);
            var newItemElement = n.BuildItemView(i);
            if (fv.Items[i] is UIElement existingCtrl && CanUpdate(oldItemElement, newItemElement))
            {
                var replacement = Update(oldItemElement, newItemElement, existingCtrl, requestRerender);
                if (replacement is not null && replacement != existingCtrl)
                    fv.Items[i] = replacement;
            }
            else
            {
                if (fv.Items[i] is UIElement oldCtrl) Unmount(oldCtrl);
                fv.Items[i] = Mount(newItemElement, requestRerender)!;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
        {
            if (fv.Items[i] is UIElement oldCtrl) Unmount(oldCtrl);
            fv.Items.RemoveAt(i);
        }

        // Add new
        for (int i = shared; i < newCount; i++)
        {
            var ctrl = Mount(n.BuildItemView(i), requestRerender);
            if (ctrl is not null) fv.Items.Add(ctrl);
        }

        fv.SelectedIndex = n.GetSelectedIndex();
        SetElementTag(fv, n);
        n.ApplyControlSetters(fv);
        return null;
    }

    private readonly struct ItemsRepeaterKeyAdapter : IReadOnlyList<ItemsRepeaterKeyAdapter.KeyOnly>
    {
        private readonly ItemsRepeaterElementBase _el;
        public ItemsRepeaterKeyAdapter(ItemsRepeaterElementBase el) => _el = el;
        public KeyOnly this[int index] => new(((Internal.IKeyedItemSource)_el).GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    internal UIElement? UpdateLazyStack(LazyStackElementBase n, WinUI.ScrollViewer sv, Action requestRerender)
    {
        if (sv.Content is WinUI.ItemsRepeater repeater)
        {
            // Try to update the existing factory in place. This avoids
            // replacing ItemTemplate, which would cause ItemsRepeater to
            // re-realize all items (modifying Children during layout →
            // "Cannot run layout in the middle of a collection change").
            // The factory keeps its identity; existing realized items
            // stay mounted. On next scroll or layout, IElementFactory.GetElement
            // uses the updated viewBuilder to produce new content.
            if (repeater.ItemTemplate is IElementFactory existingFactory && n.TryUpdateFactory(existingFactory))
            {
                // Spec 042 Phase 1: route Items changes through the keyed
                // diff into the internally-owned OC<ReactorRow>. WinUI
                // sees incremental Insert/Move/RemoveAt events and only
                // animates affected containers; the steady-state
                // RefreshRealizedItems below still runs for per-row
                // content updates.
                ApplyLazyKeyedDiffOrFallback(repeater, n, existingFactory);
                n.RefreshRealizedItems(existingFactory, repeater);
            }
            else
            {
                // First mount or type mismatch — full replacement using the
                // Phase 1 OC<ReactorRow> binding shape.
                var fresh = BuildListStateFromLazy(n);
                SetListState(repeater, fresh);
                repeater.ItemsSource = fresh.Source;
                var factory = n.CreateFactory(this, requestRerender, _pool);
                n.AttachListStateToFactory(factory, fresh);
                repeater.ItemTemplate = factory;
            }
            if (repeater.Layout is WinUI.StackLayout layout)
                layout.Spacing = n.Spacing;
            SetElementTag(repeater, n);
            ApplySetters(n.RepeaterSetters, repeater);
        }
        SetElementTag(sv, n);
        ApplySetters(n.ScrollViewerSetters, sv);
        return null;
    }

    // ── ItemContainer ───────────────────────────────────────────────────


    // ── ItemsView ───────────────────────────────────────────────────────



    private readonly struct ItemsViewKeyAdapter : IReadOnlyList<ItemsViewKeyAdapter.KeyOnly>
    {
        private readonly ItemsViewElementBase _el;
        public ItemsViewKeyAdapter(ItemsViewElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }


    private void ApplyLazyKeyedDiffOrFallback(WinUI.ItemsRepeater repeater, LazyStackElementBase n, IElementFactory factory)
    {
        var state = GetListState(repeater);
        if (state is null || !ReferenceEquals(repeater.ItemsSource, state.Source))
        {
            var fresh = BuildListStateFromLazy(n);
            SetListState(repeater, fresh);
            repeater.ItemsSource = fresh.Source;
            n.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new LazyKeyAdapter(n),
            static (item, _) => item.Key,
            _logger,
            repeater.GetType().Name,
            ambient,
            controlInstance: repeater);

        // ItemsRepeater realizes containers through ElementFactory, so the
        // enter animation runs from there. Moves on already-realized
        // elements need the same handle-based offset animation as the
        // templated list path. (spec 042 §6.)
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimationsRepeater(repeater, movedRows, ambient.Kind);
        // Bailout reset still mutates state.Source in place, so the
        // existing ItemsSource binding remains valid.
    }

    private readonly struct LazyKeyAdapter : IReadOnlyList<LazyKeyAdapter.KeyOnly>
    {
        private readonly LazyStackElementBase _el;
        public LazyKeyAdapter(LazyStackElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static ReactorListState BuildListStateFromLazy(LazyStackElementBase lazy)
    {
        var state = new ReactorListState();
        int n = lazy.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, lazy.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    /// <summary>
    /// Spec 042 §6 — per-container offset animation for ListView/GridView
    /// survivors that moved index inside an active <see cref="Animations.Animate"/>
    /// transaction. WinUI's <c>ListViewBase.ContainerFromItem</c>
    /// returns the live container for a realized row (null for virtualized
    /// ones, which is fine — the realize path attaches the animation when
    /// they come back into view).
    /// </summary>
    private void ApplyMoveAnimations(WinUI.ListViewBase lvb, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;

        // WinUI's container realignment for OC.Move events runs on the
        // next layout pass — calling ContainerFromIndex synchronously
        // here returns null even for items whose containers are realized,
        // because the lookup is keyed on the pre-move position the
        // ListView hasn't reconciled yet. Defer to the next dispatcher
        // turn so the lookup runs after layout. Implicit-Offset attached
        // *after* the position change still animates subsequent layout
        // shifts on the same container, which is the right shape for
        // continued reordering inside one Animate block.
        var dq = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Action attach = () =>
        {
            for (int i = 0; i < moved.Count; i++)
            {
                var row = moved[i];
                try
                {
                    var container = lvb.ContainerFromIndex(row.Index) as UIElement
                                  ?? lvb.ContainerFromItem(row) as UIElement;
                    if (container is not null)
                        StartMoveOffsetAnimation(container, curve);
                }
                catch { /* best-effort */ }
            }
        };
        if (dq is not null) dq.TryEnqueue(global::Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => attach());
        else attach();
    }

    /// <summary>
    /// Spec 042 §6 — same as <see cref="ApplyMoveAnimations"/> but routed
    /// through <see cref="WinUI.ItemsRepeater.TryGetElement"/> because
    /// ItemsRepeater doesn't expose <c>ContainerFromItem</c>. Row.Index is
    /// the post-move target position, which is what TryGetElement keys on.
    /// </summary>
    private void ApplyMoveAnimationsRepeater(WinUI.ItemsRepeater repeater, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;
        for (int i = 0; i < moved.Count; i++)
        {
            try
            {
                var container = repeater.TryGetElement(moved[i].Index);
                if (container is not null)
                    StartMoveOffsetAnimation(container, curve);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// One-shot Composition offset animation: snap the visual to its
    /// previous offset and animate to zero so the row visibly slides into
    /// its new layout slot. The expression keyframe form is required so
    /// the spring/ease curve picks the *current* visual.Offset as the
    /// starting value — WinUI has already moved the layout slot under us
    /// by the time we attach. (spec 042 §6, Q4 — per-container.)
    /// </summary>
    private static void StartMoveOffsetAnimation(UIElement container, Curve curve)
    {
        var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(container);
        var compositor = visual.Compositor;
        var anim = AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve);
        // Implicit animations fire automatically when WinUI assigns the
        // new Offset on layout; attaching here means the next layout pass
        // animates instead of snapping. We deliberately don't pre-set
        // Offset — letting the implicit animation observe WinUI's own
        // assignment is what makes the move read correctly without us
        // racing the layout pass.
        var coll = compositor.CreateImplicitAnimationCollection();
        coll["Offset"] = anim;
        visual.ImplicitAnimations = coll;
    }


    private UIElement? UpdateCommandHost(CommandHostElement o, CommandHostElement n, WinUI.Grid host, Action requestRerender)
    {
        // Update child element
        if (host.Children.Count > 0 && host.Children[0] is UIElement existingChild)
        {
            var replacement = UpdateChild(o.Child, n.Child, existingChild, requestRerender);
            if (replacement is not null)
            {
                UnmountChild(existingChild);
                host.Children[0] = replacement;
            }
        }
        else
        {
            var child = Mount(n.Child, requestRerender);
            if (child is not null) host.Children.Add(child);
        }

        // Rebuild accelerators — clear and re-add (commands may have changed enabled state or handlers)
        host.KeyboardAccelerators.Clear();
        AddCommandHostAccelerators(host, n.Commands);

        SetElementTag(host, n);
        return null;
    }




    internal static void ApplyFlexAttached(Element child, Microsoft.UI.Xaml.UIElement ctrl)
    {
        var fa = child.GetAttached<FlexAttached>();
        // Always apply — reset to defaults when no FlexAttached, so stale values
        // from pool-rented or reconciler-reused controls are cleared.
        Layout.FlexPanel.SetGrow(ctrl, fa?.Grow ?? 0);
        Layout.FlexPanel.SetShrink(ctrl, fa?.Shrink ?? 1);
        if (fa is { Basis: { } basis }) Layout.FlexPanel.SetBasis(ctrl, basis);
        else ctrl.ClearValue(Layout.FlexPanel.BasisProperty);
        if (fa is { MinWidth: { } minWidth }) Layout.FlexPanel.SetMinWidth(ctrl, minWidth);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinWidthProperty);
        if (fa is { MinHeight: { } minHeight }) Layout.FlexPanel.SetMinHeight(ctrl, minHeight);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinHeightProperty);
        if (fa is { AlignSelf: { } alignSelf }) Layout.FlexPanel.SetAlignSelf(ctrl, alignSelf);
        else ctrl.ClearValue(Layout.FlexPanel.AlignSelfProperty);
        Layout.FlexPanel.SetPosition(ctrl, fa?.Position ?? Layout.FlexPositionType.Relative);
        if (fa is { Left: { } left }) Layout.FlexPanel.SetLeft(ctrl, left);
        else ctrl.ClearValue(Layout.FlexPanel.LeftProperty);
        if (fa is { Top: { } top }) Layout.FlexPanel.SetTop(ctrl, top);
        else ctrl.ClearValue(Layout.FlexPanel.TopProperty);
        if (fa is { Right: { } right }) Layout.FlexPanel.SetRight(ctrl, right);
        else ctrl.ClearValue(Layout.FlexPanel.RightProperty);
        if (fa is { Bottom: { } bottom }) Layout.FlexPanel.SetBottom(ctrl, bottom);
        else ctrl.ClearValue(Layout.FlexPanel.BottomProperty);
    }

    /// <summary>
    /// Recursively diff TreeViewNode lists, reusing existing nodes where Content matches.
    /// Only adds/removes/updates nodes that actually changed, minimizing COM interop calls.
    /// Also reconciles ContentElement changes on existing nodes.
    ///
    /// Algorithm: Snapshot existing live nodes into a Content→node map. Clear the live list,
    /// then rebuild it in new order — reusing matched nodes and creating fresh ones.
    /// </summary>
    private void DiffTreeViewNodes(
        IList<WinUI.TreeViewNode> liveNodes,
        TreeViewNodeData[] oldData,
        TreeViewNodeData[] newData,
        Action requestRerender)
    {
        // Snapshot: map old Content → (live node, old data index).
        // Use the old data array for indexing since liveNodes mirrors it 1:1.
        var liveByContent = new Dictionary<string, (WinUI.TreeViewNode Node, int OldIdx)>(oldData.Length);
        for (int i = 0; i < oldData.Length && i < liveNodes.Count; i++)
            liveByContent.TryAdd(oldData[i].Content, (liveNodes[i], i));

        // Detach all live nodes so we can re-insert in new order
        liveNodes.Clear();

        for (int i = 0; i < newData.Length; i++)
        {
            var nd = newData[i];

            if (liveByContent.Remove(nd.Content, out var match))
            {
                var liveNode = match.Node;
                var oldNodeData = oldData[match.OldIdx];

                if (liveNode.IsExpanded != nd.IsExpanded)
                    liveNode.IsExpanded = nd.IsExpanded;

                ReconcileTreeNodeContent(liveNode, oldNodeData, nd, requestRerender);

                // Diff children
                var oldChildren = oldNodeData.Children;
                var newChildren = nd.Children;

                if (!ReferenceEquals(oldChildren, newChildren))
                {
                    if (newChildren is null)
                        liveNode.Children.Clear();
                    else if (oldChildren is null)
                    {
                        liveNode.Children.Clear();
                        foreach (var child in newChildren)
                            liveNode.Children.Add(CreateTreeNode(child));
                    }
                    else
                        DiffTreeViewNodes(liveNode.Children, oldChildren, newChildren, requestRerender);
                }

                liveNodes.Add(liveNode);
            }
            else
            {
                // New node
                liveNodes.Add(CreateTreeNode(nd));
            }
        }
        // Unmatched old nodes are simply not re-added — they're dropped.
    }

    /// <summary>
    /// Reconciles ContentElement changes on a TreeViewNode.
    /// When ContentElement is used, node.Content holds a mounted UIElement.
    /// </summary>
#pragma warning disable CS0618 // legacy TreeViewNodeData.ContentElement path (see issue #447)
    private void ReconcileTreeNodeContent(
        WinUI.TreeViewNode liveNode,
        TreeViewNodeData? oldData,
        TreeViewNodeData newData,
        Action requestRerender)
    {
        var oldContentEl = oldData?.ContentElement;
        var newContentEl = newData.ContentElement;

        if (newContentEl is null && oldContentEl is null) return; // Both text-only, no change needed

        if (newContentEl is not null && oldContentEl is not null
            && liveNode.Content is UIElement existingCtrl
            && CanUpdate(oldContentEl, newContentEl))
        {
            // Reconcile in place
            var replacement = Update(oldContentEl, newContentEl, existingCtrl, requestRerender);
            if (replacement is not null && !ReferenceEquals(liveNode.Content, replacement))
                liveNode.Content = replacement;
        }
        else if (newContentEl is not null)
        {
            // Mount new content element
            if (liveNode.Content is UIElement oldCtrl)
                Unmount(oldCtrl);
            liveNode.Content = Mount(newContentEl, requestRerender);
        }
        else
        {
            // ContentElement removed, revert to data
            if (liveNode.Content is UIElement oldCtrl2)
                Unmount(oldCtrl2);
            liveNode.Content = newData;
        }
    }
#pragma warning restore CS0618

    // ── Typed, data-driven TreeView<T> ───────────────────────────────────

    /// <summary>
    /// Updates a typed <see cref="TemplatedTreeViewElementBase"/> in place:
    /// keyed-diffs the node hierarchy and writes back the parent-control props.
    /// The ItemInvoked / Expanding trampolines resolve the live element via
    /// <c>GetElementTag</c>, so refreshing the element tag is all that's needed
    /// to pick up new callbacks — no re-subscription.
    /// </summary>
    private UIElement? UpdateTemplatedTreeView(TemplatedTreeViewElementBase o, TemplatedTreeViewElementBase n, WinUI.TreeView tv, Action requestRerender)
    {
        // Diff the node hierarchy (structure + each node's data item). The view
        // reconcile is a separate flat pass over the realized containers below —
        // keeping the two concerns decoupled, and using the index-based container
        // lookup that works under NativeAOT (ContainerFromItem does not resolve
        // there, and a freshly-realized container can still be the base
        // ListViewItem rather than TreeViewItem).
        DiffTemplatedTreeNodes(tv.RootNodes, o, o.GetRoots(), n, n.GetRoots());

        tv.SelectionMode = n.GetSelectionMode();
        tv.CanDragItems = n.GetCanDragItems();
        tv.AllowDrop = n.GetAllowDrop();
        tv.CanReorderItems = n.GetCanReorderItems();

        SetElementTag(tv, n);
        n.ApplyControlSetters(tv);

        // Reconcile the view of every currently-realized container against its
        // node's (now-updated) data. Unrealized nodes need no work — their view
        // is (re)built fresh from node.Content when they next realize via CCC.
        RefreshRealizedTreeContainers(tv, FindTypedTreeListControl(tv), n, requestRerender);
        return null;
    }

    /// <summary>
    /// Reconciles the hosted view of every realized container against its node's
    /// current <c>node.Content</c> data. Iterates the flattened
    /// <see cref="WinUI.ListView.Items"/> via index (the AOT-robust lookup), so
    /// it covers visible nodes at every depth in one pass.
    /// </summary>
    private void RefreshRealizedTreeContainers(WinUI.TreeView tv, WinUI.ListView? list, TemplatedTreeViewElementBase n, Action requestRerender)
    {
        if (list is null) return;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ContentControl container) continue;
            if (container.ContentTemplateRoot is not ContentControl cc) continue;
            if (list.Items[i] is not WinUI.TreeViewNode node || node.Content is not { } data) continue;

            var newView = n.BuildView(data);
            if (cc.Content is UIElement existing && GetElementTag(cc) is Element oldView && CanUpdate(oldView, newView))
            {
                var replacement = Update(oldView, newView, existing, requestRerender);
                if (replacement is not null && !ReferenceEquals(cc.Content, replacement))
                    cc.Content = replacement;
            }
            else
            {
                if (cc.Content is UIElement old) UnmountChild(old);
                cc.Content = Mount(newView, requestRerender);
            }
            SetElementTag(cc, newView);
        }
    }

    private static readonly object[] s_emptyTreeItems = [];

    /// <summary>
    /// Keyed, in-place hierarchical reconcile of a typed TreeView node list.
    /// Keys on the element's <c>KeySelector</c>. Matched nodes are reused (their
    /// data item and any realized container view reconciled in place); the live
    /// <see cref="WinUI.TreeViewNode"/> collection is then reordered with minimal
    /// insert/move/remove ops so that <b>unchanged-order updates touch the
    /// collection not at all</b> — avoiding the container churn that a
    /// clear-and-rebuild would force.
    /// </summary>
    private void DiffTemplatedTreeNodes(
        IList<WinUI.TreeViewNode> liveNodes,
        TemplatedTreeViewElementBase oldEl, IReadOnlyList<object> oldItems,
        TemplatedTreeViewElementBase newEl, IReadOnlyList<object> newItems)
    {
        // Snapshot: map old key → (live node, old item). Live nodes correspond
        // 1:1 to oldItems in order.
        var oldByKey = new Dictionary<string, (WinUI.TreeViewNode Node, object OldItem)>(oldItems.Count);
        for (int i = 0; i < oldItems.Count && i < liveNodes.Count; i++)
            oldByKey.TryAdd(oldEl.GetKey(oldItems[i]), (liveNodes[i], oldItems[i]));

        // Resolve the target node sequence: reuse-and-reconcile matched nodes,
        // build fresh ones for new keys.
        var target = new List<WinUI.TreeViewNode>(newItems.Count);
        for (int i = 0; i < newItems.Count; i++)
        {
            var newItem = newItems[i];
            if (oldByKey.Remove(newEl.GetKey(newItem), out var match))
            {
                var node = match.Node;
                // node.Content is the data item; refresh it so the trampolines
                // hand back the current T (value-type T is boxed fresh).
                node.Content = newItem;

                bool expanded = newEl.GetIsExpanded(newItem);
                if (node.IsExpanded != expanded) node.IsExpanded = expanded;

                DiffTemplatedTreeNodes(
                    node.Children,
                    oldEl, oldEl.GetChildren(match.OldItem) ?? s_emptyTreeItems,
                    newEl, newEl.GetChildren(newItem) ?? s_emptyTreeItems);

                target.Add(node);
            }
            else
            {
                target.Add(BuildTemplatedTreeNode(newEl, newItem));
            }
        }

        // Removed nodes (unmatched old keys): drop them. Their realized
        // containers recycle → the ContainerContentChanging recycle path tears
        // their views down.
        foreach (var leftover in oldByKey.Values)
            liveNodes.Remove(leftover.Node);

        // Reorder/insert so liveNodes matches `target`. Reused nodes already in
        // the right slot mean zero collection mutations (the common case).
        for (int j = 0; j < target.Count; j++)
        {
            if (j < liveNodes.Count && ReferenceEquals(liveNodes[j], target[j])) continue;

            int current = IndexOfNode(liveNodes, target[j], j);
            if (current >= 0) liveNodes.RemoveAt(current);
            liveNodes.Insert(j, target[j]);
        }
        // Trim any stragglers beyond the target length (defensive — removals
        // above should already have handled these).
        while (liveNodes.Count > target.Count)
            liveNodes.RemoveAt(liveNodes.Count - 1);
    }

    private static int IndexOfNode(IList<WinUI.TreeViewNode> nodes, WinUI.TreeViewNode target, int startAt)
    {
        for (int i = startAt; i < nodes.Count; i++)
            if (ReferenceEquals(nodes[i], target)) return i;
        return -1;
    }

    private UIElement? UpdateFormField(
        FormFieldElement oldFf, FormFieldElement newFf,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // Fixed 3-child layout: [0] label, [1] content, [2] description/error
        if (panel.Children.Count != 3)
            return Mount(newFf, requestRerender);

        var fieldName = FormFieldHelpers.ResolveFieldName(newFf.FieldName, newFf.Content);

        // Auto-validate
        var attached = newFf.Content.GetAttached<ValidationAttached>();
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null && attached is not null && attached.Validators.Length > 0)
        {
            ValidationReconciler.ValidateAttached(valCtx, attached, attached.Value);
        }

        // [0] Update label
        if (panel.Children[0] is TextBlock labelTb)
        {
            var displayLabel = FormFieldHelpers.GetDisplayLabel(newFf.Label, newFf.Required);
            labelTb.Text = displayLabel;
            labelTb.Visibility = displayLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // [1] Patch content in-place (preserves caret position and focus)
        var existingContent = panel.Children[1];
        if (CanUpdate(oldFf.Content, newFf.Content))
        {
            var replacement = Update(oldFf.Content, newFf.Content, existingContent, requestRerender);
            if (replacement is not null)
            {
                // WinUI indexer assignment doesn't fully disconnect the old element's
                // parent state — use RemoveAt+Insert (see ChildCollection.Replace).
                Unmount(existingContent);
                panel.Children.RemoveAt(1);
                panel.Children.Insert(1, replacement);
                existingContent = replacement;
            }
        }
        else
        {
            // Content element type changed — must remount
            Unmount(existingContent);
            panel.Children.RemoveAt(1);
            var newContent = Mount(newFf.Content, requestRerender)
                ?? new WinUI.StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Insert(1, newContent);
            existingContent = newContent;
        }

        ApplyFormFieldAutomation(existingContent, newFf.Label);
        ApplyFormFieldErrorStyling(existingContent, valCtx, fieldName, newFf.ShowWhen);

        // [2] Update description/error text
        if (panel.Children[2] is TextBlock descTb)
        {
            ApplyFormFieldDescription(descTb, valCtx, fieldName, newFf.Description, newFf.ShowWhen);
        }

        SetElementTag(panel, newFf);
        return null; // patched in-place
    }

    private UIElement? UpdateValidationVisualizer(
        ValidationVisualizerElement oldVv, ValidationVisualizerElement newVv,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // The visualizer layout varies by style and message state, so a full in-place
        // patch is complex. However, we can at least reconcile the content child when
        // styles match and the content element is updatable.
        if (oldVv.Style != newVv.Style)
            return Mount(newVv, requestRerender);

        // Find the content child — it's the form control, not the error display chrome.
        // In MountValidationVisualizer, content is added after style-specific elements,
        // except for Inline where error text comes after content.
        // For simplicity and correctness, remount the visualizer but reconcile the
        // content subtree to preserve control state.
        return Mount(newVv, requestRerender);
    }

    private UIElement? UpdateValidationRule(ValidationRuleElement rule)
    {
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null)
            rule.Evaluate(valCtx);
        return null; // keep existing collapsed placeholder
    }

    private UIElement? UpdateErrorBoundary(
        ErrorBoundaryElement oldEb, ErrorBoundaryElement newEb,
        UIElement control, Action requestRerender)
    {
        if (!_errorBoundaryNodes.TryGetValue(control, out var node))
            return Mount(newEb, requestRerender);

        var wrapper = (Border)control;
        var existingChild = wrapper.Child;

        // Always retry the child on re-render (error recovery).
        Element newRendered;
        Exception? caughtEx = null;

        _errorBoundaryDepth++;
        try
        {
            newRendered = newEb.Child;
            var newControl = Reconcile(node.RenderedElement, newEb.Child, existingChild, requestRerender);
            if (newControl != existingChild)
                wrapper.Child = newControl;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ErrorBoundary caught render error during update");
            caughtEx = ex;
            if (existingChild is not null)
                Unmount(existingChild);
            newRendered = newEb.Fallback(ex);
            wrapper.Child = Mount(newRendered, requestRerender);
        }
        finally
        {
            _errorBoundaryDepth--;
        }

        node.ChildElement = newEb.Child;
        node.RenderedElement = newRendered;
        node.CaughtException = caughtEx;
        node.Fallback = newEb.Fallback;

        return null;
    }

    private UIElement? UpdateComponent(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        ReconcileComponent(oldEl, newEl, control, requestRerender);
        return null;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

    internal void UpdateMenuFlyoutItems(
        global::System.Collections.Generic.IList<WinUI.MenuFlyoutItemBase> target,
        MenuFlyoutItemBase[] oldSource,
        MenuFlyoutItemBase[] newSource)
    {
        int oldCount = oldSource.Length;
        int newCount = newSource.Length;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            switch (newSource[i])
            {
                case MenuFlyoutItemData mfi when target[i] is WinUI.MenuFlyoutItem existing:
                    existing.Text = mfi.Text;
                    existing.IsEnabled = mfi.IsEnabled;
                    existing.Icon = ResolveIcon(mfi.IconElement, mfi.Icon);
                    if (mfi.AccessKey is not null) existing.AccessKey = mfi.AccessKey;
                    if (mfi.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(existing, mfi.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(existing, mfi.Description);
                    }
                    existing.Tag = mfi;
                    break;

                case ToggleMenuFlyoutItemData toggle when target[i] is WinUI.ToggleMenuFlyoutItem toggleItem:
                    toggleItem.Text = toggle.Text;
                    toggleItem.IsChecked = toggle.IsChecked;
                    toggleItem.Icon = ResolveIcon(toggle.IconElement, toggle.Icon);
                    toggleItem.Tag = toggle;
                    break;

                case RadioMenuFlyoutItemData radio when target[i] is WinUI.RadioMenuFlyoutItem radioItem:
                    radioItem.Text = radio.Text;
                    radioItem.IsChecked = radio.IsChecked;
                    radioItem.Tag = radio;
                    break;

                case MenuFlyoutSeparatorData when target[i] is WinUI.MenuFlyoutSeparator:
                    break; // nothing to update

                case MenuFlyoutSubItemData sub when target[i] is WinUI.MenuFlyoutSubItem subItem:
                    subItem.Text = sub.Text;
                    subItem.Icon = ResolveIcon(sub.IconElement, sub.Icon);
                    // Recursively patch sub-items
                    var oldSub = oldSource[i] is MenuFlyoutSubItemData oldSubData ? oldSubData.Items : [];
                    UpdateMenuFlyoutItems(subItem.Items, oldSub, sub.Items);
                    break;

                default:
                    // Type mismatch — replace the item
                    target[i] = CreateMenuFlyoutItem(newSource[i]);
                    break;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        for (int i = shared; i < newCount; i++)
            target.Add(CreateMenuFlyoutItem(newSource[i]));
    }

    internal static void UpdateAppBarItems(
        global::System.Collections.Generic.IList<WinUI.ICommandBarElement> target,
        AppBarItemBase[]? source)
    {
        int newCount = source?.Length ?? 0;
        int oldCount = target.Count;

        // Update shared range (only update if types match, otherwise replace)
        int shared = Math.Min(oldCount, newCount);
        for (int i = 0; i < shared; i++)
        {
            if (source is null) continue;
            switch (source[i])
            {
                case AppBarButtonData cmd when target[i] is WinUI.AppBarButton abb:
                    abb.Label = cmd.Label;
                    abb.IsEnabled = cmd.IsEnabled;
                    abb.Icon = ResolveIcon(cmd.IconElement, cmd.Icon);
                    if (cmd.AccessKey is not null) abb.AccessKey = cmd.AccessKey;
                    if (cmd.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(abb, cmd.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(abb, cmd.Description);
                    }
                    abb.Tag = cmd;
                    break;
                case AppBarToggleButtonData toggle when target[i] is WinUI.AppBarToggleButton atb:
                    atb.Label = toggle.Label;
                    atb.IsChecked = toggle.IsChecked;
                    if (toggle.Icon is not null) atb.Icon = new WinUI.SymbolIcon(ParseSymbol(toggle.Icon));
                    atb.Tag = toggle;
                    break;
                case AppBarSeparatorData when target[i] is WinUI.AppBarSeparator:
                    break; // nothing to update
                default:
                    // Type mismatch — replace
                    target[i] = CreateAppBarItem(source[i]);
                    break;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        if (source is not null)
            for (int i = shared; i < newCount; i++)
                target.Add(CreateAppBarItem(source[i]));
    }
}
