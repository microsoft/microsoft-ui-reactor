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

// Reconciler partial: Unmount, unmount-and-pool, and isolated teardown.
public sealed partial class Reconciler
{
    /// <summary>
    /// Unmounts a child control. Public so registered type handlers can unmount children.
    /// </summary>
    public void UnmountChild(UIElement control)
    {
        Unmount(control);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount
    // ════════════════════════════════════════════════════════════════════

    private void Unmount(UIElement control)
    {
        UnmountRecursive(control);
    }

    /// <summary>
    /// Spec 047 §14 Phase 4 (4.0.2) — tears down the per-instance navigation
    /// state (route subscription, cache, current child control) tracked for a
    /// <see cref="NavigationHostElement"/>'s host Grid. Owned by
    /// <c>NavigationHostHandler.Unmount</c> on the V1 path; also invoked by the
    /// V1-OFF fallback in <see cref="UnmountRecursive"/>. No-op if the control
    /// is not a tracked navigation host.
    /// </summary>
    internal void CleanupNavigationHostNode(UIElement control)
    {
        if (!_navigationHostNodes.TryGetValue(control, out var navNode))
            return;
        navNode.TransitionGeneration++;
        navNode.ConnectedOperation?.Dispose();
        navNode.ConnectedOperation = null;
        if (navNode.RouteChangedHandler is not null)
            navNode.Handle.RouteChanged -= navNode.RouteChangedHandler;
        navNode.Handle.Detach();
        navNode.Cache?.Clear();
        if (navNode.CurrentChildControl is not null)
            Navigation.ConnectedNavigationOperation.UnmountPage(this, navNode.CurrentChildControl);
        _navigationHostNodes.Remove(control);
    }

    private void UnmountRecursive(UIElement control)
    {
        if (SkipOrTrackIsolatedTeardown(control)) return;
        CountPerfChanges();
        NoteUnmountDuringSlotUpdate(control);
        EndExit(control);
        CancelPostLayoutSweep(control);
        // Before any early exit: an unmounted control an app still holds must not keep a
        // FlipView's mounted item elements alive.
        if (control is FrameworkElement snapshotFe) ClearRefreshSnapshots(snapshotFe);

        // Capture connected animation snapshot while element is still in the visual tree
        if (FindMountedElementValue(control, static element => element.ConnectedAnimationKey) is { } connectedKey)
        {
            PrepareConnectedAnimationSource(connectedKey, control);
        }

        // Each element in the decorator chain can own refs and animations.
        if (control is FrameworkElement cleanupFe)
            CleanupElementChainForUnmount(cleanupFe);

        // Issue #1262 — a control that carried an attached verdict takes it with it.
        // Nothing else does this: DetachReactorState runs only on a full detach, so a
        // conditionally rendered validated control (or a whole FormField) leaving the
        // tree otherwise left the context invalid over a field that no longer exists.
        // The binding retracts only while it still owns the slot, so an incoming control
        // that already replaced the verdict is not disturbed by the outgoing one.
        WithdrawAttachedValidation(control);

        // OnUnmountAction (.OnUnmount) — imperative teardown half of .OnMount.
        if (control is FrameworkElement umFe)
            RunUnmountAction(umFe);

        // Issue #917 — a TitleBar going away withdraws both its
        // ExtendsContentIntoTitleBar inference and its caption-height
        // contribution from the owning window, so a window that merely used to
        // host one is not left content-extended and tall for its lifetime.
        if (control is WinUI.TitleBar unmountingBar)
            WithdrawTitleBar(unmountingBar);

        if (_componentNodes.TryGetValue(control, out var node))
        {
            if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                    global::System.Diagnostics.Tracing.EventLevel.Informational,
                    Diagnostics.ReactorEventSource.Keywords.Lifecycle))
            {
                Diagnostics.ReactorEventSource.Log.ComponentUnmount(
                    Diagnostics.ComponentNames.For(node.Component, node.Element));
            }
            // The wrapper may be pooled and reused by an unrelated element; drop its id.
            if (node.DiagnosticId != 0)
                Diagnostics.ComponentRenderControls.Registry.Forget(node.DiagnosticId, control);
            RunUnmountCleanups(node);
            _componentNodes.Remove(control);
        }

        _errorBoundaryNodes.Remove(control);

        // Spec 047 §14 Phase 1 (1.1) — V1 handler unmount dispatch. Standard
        // handlers return CollectSelf (terminate the traversal — children
        // already torn down by the handler; e.g. NavigationHostHandler.Unmount
        // runs CleanupNavigationHostNode). Phase 3 completion decorator handlers
        // may return ContinueDefaultTraversal. Target-wrapping flyouts then yield
        // to the target's own handler on the same control before child traversal.
        ReactorState? unmountState = null;
        if (control is FrameworkElement unmountFe)
            TryGetReactorState(unmountFe, out unmountState);

        var unmountElement = unmountState?.DecoratorElement ?? unmountState?.Element;
        // Issue #1753: a .WithFlyout(...) flyout's content is unmounted with its host.
        if (unmountState?.ModifierFlyout is not null) ReleaseFlyoutAttachment((FrameworkElement)control);
        // Finish the saved flyout decorator before the existing target dispatch.
        if (unmountState?.DecoratorElement is { } decoratorElement
            && _v1Handlers.TryGet(decoratorElement.GetType(), out var decoratorEntry)
            && decoratorEntry.PreservesTargetTag
            && decoratorEntry.HasUnmount)
        {
            V1Protocol.V1UnmountDisposition decoratorDisposition;
            if (!IsolatesUnmountCleanupFailures)
                decoratorDisposition = decoratorEntry.Unmount(control, this);
            else if (!RunIsolatedTeardown(control, decoratorEntry, static (entry, c, r) => entry.Unmount(c, r), out decoratorDisposition))
                return;

            if (decoratorDisposition != V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal)
                return;
            unmountElement = null;
            if (ReferenceEquals(unmountState.DecoratorElement, decoratorElement))
            {
                unmountState.DecoratorElement = null;
                if (!ReferenceEquals(unmountState.Element, decoratorElement))
                    unmountElement = unmountState.Element;
            }
        }

        if (control is FrameworkElement v1Fe && unmountElement is Element v1TagEl
            && _v1Handlers.TryGet(v1TagEl.GetType(), out var v1Entry) && v1Entry.HasUnmount)
        {
            V1Protocol.V1UnmountDisposition v1Disposition;
            if (!IsolatesUnmountCleanupFailures)
                v1Disposition = v1Entry.Unmount(control, this);
            else if (!RunIsolatedTeardown(control, v1Entry, static (entry, c, r) => entry.Unmount(c, r), out v1Disposition))
                return;
            if (v1Disposition != V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal)
                return;
        }

        // Check registered type unmount handlers via the attached element
        if (control is FrameworkElement fe && unmountState is { } regState
            && (regState.DecoratorElement ?? regState.Element) is Element tagEl
            && _typeRegistry.TryGetValue(tagEl.GetType(), out var reg) && reg.HasUnmount
            && TryBeginRegisteredUnmount(regState))
        {
            RunRegisteredUnmount(reg, control, regState);
            return;
        }

        if (control is WinUI.RichTextBlock rtb)
        {
            // Issue #480 — RichTextBlock is neither a Panel nor a ContentControl,
            // so ForEachReactorChildControl doesn't recurse into its flow
            // document. Route A (Reactor element) inline UI children must be torn
            // down here or their hook cleanups + pooled descendants leak when the
            // block goes away.
            UnmountInlineUIChildren(rtb);
        }

        ForEachReactorChildControl(control, UnmountRecursive);
    }

    private static void CleanupElementChainForUnmount(FrameworkElement control)
    {
        ElementPool.CancelOpacitySync(control);
        // Whichever layer started a keyframe (a keyed Memo's isn't on this element chain), an
        // unmounted control's pending opacity restart must go with it (#1669).
        CancelOpacityRestart(control);
        // Before the reference-edge teardown below, which runs user setters that can throw.
        ClearTrackedProducerRefs(control);
        var current = GetMountedElement(control);
        CleanupElementStateForUnmount(control, current);
        // Reference edges belong to the control, even when it has no element tag.
        ReferenceEdgeWiring.TeardownReferenceEdges(control);
        for (var depth = 0; current is not null && depth < ReactorSourceMap.MaxDecoratorDepth; depth++)
        {
            current = ReactorSourceMap.DecoratorTarget(current);
            if (current is null) break;
            CleanupElementStateForUnmount(control, current);
        }
    }

    private static void TrackProducerRef(FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef producerRef)
    {
        var state = GetOrCreateReactorState(control);
        switch (state.ProducerRefs)
        {
            case null:
                state.ProducerRefs = producerRef;
                break;
            case Microsoft.UI.Reactor.Input.ElementRef single when !ReferenceEquals(single, producerRef):
                state.ProducerRefs = new List<Microsoft.UI.Reactor.Input.ElementRef>(2) { single, producerRef };
                break;
            case List<Microsoft.UI.Reactor.Input.ElementRef> list when !ContainsReference(list, producerRef):
                list.Add(producerRef);
                break;
        }
    }

    private static void UntrackProducerRef(FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef producerRef)
    {
        if (!TryGetReactorState(control, out var state)) return;
        if (ReferenceEquals(state.ProducerRefs, producerRef))
            state.ProducerRefs = null;
        else if (state.ProducerRefs is List<Microsoft.UI.Reactor.Input.ElementRef> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], producerRef)) continue;
                list.RemoveAt(i);
                break;
            }
        }
    }

    private static bool ContainsReference(List<Microsoft.UI.Reactor.Input.ElementRef> list, Microsoft.UI.Reactor.Input.ElementRef producerRef)
    {
        foreach (var entry in list)
            if (ReferenceEquals(entry, producerRef)) return true;
        return false;
    }

    // Clear a ref only while it still names this control. When an update replaces a control,
    // ApplyModifiers points the ref at the replacement before the caller unmounts the old
    // control, and the ref must keep it.
    private static void ClearTrackedProducerRefs(FrameworkElement control)
    {
        if (!TryGetReactorState(control, out var state) || state.ProducerRefs is not { } refs) return;
        state.ProducerRefs = null;
        if (refs is Microsoft.UI.Reactor.Input.ElementRef single)
        {
            ClearProducerRefIfCurrent(control, single);
            return;
        }
        // Outside a Reconcile commit, clearing a ref dispatches CurrentChanged synchronously and
        // can throw. Clear the rest first, so one throwing subscriber can't strand another ref.
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        foreach (var producerRef in (List<Microsoft.UI.Reactor.Input.ElementRef>)refs)
        {
            try { ClearProducerRefIfCurrent(control, producerRef); }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                failure ??= global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
        }
        failure?.Throw();
    }

    private static void ClearProducerRefIfCurrent(FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef producerRef)
    {
        if (producerRef.Current is { } current && IsSameNativeElement(current, control))
            producerRef.SetCurrent(null);
    }

    private static void CleanupElementStateForUnmount(FrameworkElement control, Element? element)
    {
        // A control kept out of the pool isn't reused, and a connected-animation source keeps its
        // hover brushes for the snapshot WinUI takes on the next frame.
        if (element?.InteractionStates is not null)
            InteractionStateApplier.ClearInteractionStates(control,
                restoreBrushes: control.GetValue(ReactorAttached.StateProperty) is not ReactorState { PoolExcluded: true });
        if (element?.KeyframeAnimations is not null)
            ClearKeyframeAnimations(control, element.KeyframeAnimations);
        if (element?.ScrollAnimation is not null)
            ClearScrollAnimation(control, element.ScrollAnimation);
    }

    // Two managed wrappers can front one native element (see ReactorAttached.StateProperty), so
    // identity falls back to the ReactorState that lives on the native object. When b carries no
    // ReactorState there is nothing native to compare, and only the same wrapper matches.
    private static bool IsSameNativeElement(FrameworkElement a, FrameworkElement b) =>
        ReferenceEquals(a, b)
        || (b.GetValue(ReactorAttached.StateProperty) is ReactorState state
            && ReferenceEquals(a.GetValue(ReactorAttached.StateProperty), state));

    // True when an update result is the control it was handed, possibly through a second managed
    // wrapper, which means the update patched it in place. TypeRegistration.Update and
    // ReconcileV1Child both decide this here. Every wrapper for a native object reads the same
    // ReactorState, so the handed-in control gets one if it has none and the result is checked
    // for it. That allocation happens only when the result is a different wrapper, which is
    // almost always a genuine replacement, and it lands on the control being replaced.
    private static bool IsSameControl(UIElement result, UIElement control)
    {
        if (ReferenceEquals(result, control)) return true;
        if (result is not FrameworkElement resultFe || control is not FrameworkElement controlFe)
            return false;
        var state = GetOrCreateReactorState(controlFe);
        return ReferenceEquals(resultFe.GetValue(ReactorAttached.StateProperty), state);
    }

    // Controls whose RegisterType unmount callback is running, by the ReactorState on the native
    // control so a second managed wrapper for it is recognized too. A callback that tears its own
    // control down through the reconciler (UnmountChild on the control it was handed, to reach
    // the children it mounted) re-enters the unmount path for that control; the re-entrant call
    // takes the default walk over the children instead of calling the callback again.
    private HashSet<ReactorState>? _registeredUnmountsRunning;

    private bool TryBeginRegisteredUnmount(ReactorState state) =>
        (_registeredUnmountsRunning ??= new HashSet<ReactorState>(ReferenceEqualityComparer.Instance)).Add(state);

    private void EndRegisteredUnmount(ReactorState state) => _registeredUnmountsRunning?.Remove(state);

    // While a control handler's or RegisterType callback's teardown runs under
    // IsolateUnmountCleanupFailures: the controls it has torn down. If it throws part-way, the
    // recovery walk over its children (_isolatedTeardownSkip) skips those, so no child
    // teardown runs twice.
    private HashSet<UIElement>? _isolatedTeardownTrack;
    private HashSet<UIElement>? _isolatedTeardownSkip;

    // Entry of both unmount walks: true to skip a control already torn down by the failed
    // teardown being recovered; otherwise records the control for the running teardown.
    private bool SkipOrTrackIsolatedTeardown(UIElement control)
    {
        if (_isolatedTeardownSkip is { } skip && skip.Contains(control)) return true;
        _isolatedTeardownTrack?.Add(control);
        return false;
    }

    /// <summary>
    /// Runs a control's teardown (a V1 handler's or a <c>RegisterType</c> unmount) inside an
    /// isolated teardown (<see cref="IsolateUnmountCleanupFailures"/>). A throw is collected, and
    /// the Reactor children the teardown did not reach are torn down, each once; returns false
    /// then, and the caller must not pool the half-torn-down control.
    /// </summary>
    private bool RunIsolatedTeardown<TState>(UIElement control, TState state,
        Func<TState, UIElement, Reconciler, V1Protocol.V1UnmountDisposition> teardown,
        out V1Protocol.V1UnmountDisposition disposition)
    {
        disposition = default;
        var outerTrack = _isolatedTeardownTrack;
        var track = _isolatedTeardownTrack = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        bool failed = false;
        try { disposition = teardown(state, control, this); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // A handler that reported its failures itself (in order) only signals the failure.
            if (ex is not V1Protocol.ReportedUnmountFailureException)
                ReportIsolatedUnmountFailure(ex);
            failed = true;
        }
        finally { _isolatedTeardownTrack = outerTrack; }
        if (!failed) return true;

        // An enclosing recovery's skips still apply: what an outer teardown tore down inside
        // this control must not be torn down again by this control's recovery.
        var outerSkip = _isolatedTeardownSkip;
        if (outerSkip is not null) track.UnionWith(outerSkip);
        _isolatedTeardownSkip = track;
        try { ForEachReactorChildControl(control, UnmountRecursive); }
        finally { _isolatedTeardownSkip = outerSkip; }
        return false;
    }

    /// <summary>
    /// Runs <paramref name="teardown"/> (a handler's own child teardown after its unmount hook
    /// failed, inside <see cref="RunIsolatedTeardown"/>) skipping the controls the running
    /// teardown already tore down, so none is torn down twice.
    /// </summary>
    internal void SkippingIsolatedTornDown<TState>(TState state, Action<TState> teardown)
    {
        var outerSkip = _isolatedTeardownSkip;
        var skip = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        if (_isolatedTeardownTrack is { } track) skip.UnionWith(track);
        if (outerSkip is not null) skip.UnionWith(outerSkip);
        _isolatedTeardownSkip = skip;
        try { teardown(state); }
        finally { _isolatedTeardownSkip = outerSkip; }
    }

    /// <summary>
    /// Runs a <c>RegisterType</c> unmount callback; inside an isolated teardown, through
    /// <see cref="RunIsolatedTeardown"/>. Returns false when it failed part-way.
    /// </summary>
    private bool RunRegisteredUnmount(ITypeRegistration reg, UIElement control, ReactorState regState)
    {
        try
        {
            if (!IsolatesUnmountCleanupFailures)
            {
                reg.Unmount(control, this);
                return true;
            }
            return RunIsolatedTeardown(control, reg, static (registration, c, r) =>
            {
                registration.Unmount(c, r);
                return V1Protocol.V1UnmountDisposition.CollectSelf;
            }, out _);
        }
        finally { EndRegisteredUnmount(regState); }
    }

    // Mounted content can exist before a control template attaches it to the
    // visual tree. Navigation must see those slots without forcing template/layout.
    internal void VisitMountedChildControls(UIElement control, Action<UIElement> visit)
    {
        ForEachReactorChildControl(control, visit);
        if (GetMountedElement(control) is { } element
            && global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.UnwrapDecorators(element) is { } hostElement
            && _v1Handlers.TryGet(hostElement.GetType(), out var entry)
            && entry is IV1ChildEnumerator children)
            children.VisitLiveChildren(control, visit);
    }

    private static void ForEachReactorChildControl(UIElement control, Action<UIElement> visit)
        => ForEachReactorChildControl(control, child => { visit(child); return true; });

    private static void ForEachReactorChildControl(UIElement control, Func<UIElement, bool> visit)
    {
        if (control is WinUI.Panel panel)
        {
            foreach (var child in panel.Children)
                if (!visit(child)) return;
        }
        else if (control is WinUI.ItemsRepeater repeater)
        {
            // ItemsRepeater does not expose a Children collection; realized and
            // recycled row hosts live in its visual subtree.
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(repeater);
            for (int i = 0; i < count; i++)
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(repeater, i) is UIElement child && !visit(child))
                    return;
            }
        }
        else if (control is WinUI.Border border && border.Child is not null)
        {
            visit(border.Child);
        }
        else if (control is WinUI.ScrollViewer sv && sv.Content is UIElement svChild)
        {
            visit(svChild);
        }
        else if (control is WinUI.UserControl uc && uc.Content is UIElement ucChild)
        {
            visit(ucChild);
        }
        // Named-slot / single-content hosts that are NOT Panel/Border/ScrollViewer/
        // UserControl/ContentControl still host reactor children through their own
        // WinUI child properties, so the subtree walks built on this helper — notably
        // HasConsumedContextChangedInSubtree for the issue #811 context re-render fix —
        // must descend into them or a consumer under one goes stale behind a
        // reference-stable skip. SplitView is checked before the ContentControl arm
        // because it is a plain Control (not a ContentControl) with two slots.
        // The authoritative child slots live on each control's V1 children strategy
        // (SingleContent.GetCurrentChild / NamedSlots); a fully strategy-driven walk is
        // the principled generalization for the long tail (NavigationView, TitleBar,
        // TeachingTip, SemanticZoom, …) and is tracked as a follow-up.
        else if (control is WinUI.SplitView splitView)
        {
            if (splitView.Content is UIElement splitContent && !visit(splitContent)) return;
            if (splitView.Pane is UIElement splitPane && !visit(splitPane)) return;
        }
        else if (control is WinUI.Viewbox viewbox && viewbox.Child is UIElement viewboxChild)
        {
            visit(viewboxChild);
        }
        else if (control is WinUI.ContentControl cc && cc.Content is UIElement ccChild)
        {
            visit(ccChild);
        }
    }

    /// <summary>
    /// Walks a typed TreeView's visual subtree and unmounts every per-node view
    /// hosted in a tagged container ContentControl. Used on full-tree unmount
    /// (recycle of individual containers is handled by the
    /// ContainerContentChanging recycle path).
    /// </summary>
    internal void UnmountTypedTreeViewContainers(WinUI.TreeView treeView)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(treeView);
        for (int i = 0; i < count; i++)
            UnmountTypedTreeViewContainersRecursive(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(treeView, i));
    }

    internal void UnmountTypedTreeViewContainersRecursive(DependencyObject node)
    {
        // Our node-view hosts are ContentControls tagged with the view Element.
        if (node is WinUI.ContentControl cc && cc.Content is UIElement view && GetMountedElement(cc) is not null)
        {
            UnmountChild(view);
            cc.Content = null;
            ClearElementTag(cc);
            return; // the view's own subtree is handled by UnmountChild
        }
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            UnmountTypedTreeViewContainersRecursive(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
    }

    /// <summary>
    /// Finds the typed TreeView's internal <c>TreeViewList</c> (template part
    /// "ListControl", a public <see cref="WinUI.ListView"/> subclass). Returns
    /// the cached instance when hosting is already attached; otherwise walks the
    /// visual subtree without caching — only <c>AttachTypedTreeHosting</c> writes
    /// <see cref="_typedTreeListControls"/> (its presence marks "subscribed", so
    /// a read-side cache write here would suppress the real subscription).
    /// </summary>
    internal WinUI.ListView? FindTypedTreeListControl(WinUI.TreeView treeView) =>
        _typedTreeListControls.TryGetValue(treeView, out var cached)
            ? cached
            : FindDescendantListView(treeView);

    private static WinUI.ListView? FindDescendantListView(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is WinUI.ListView lv) return lv;
            if (FindDescendantListView(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// Unmounts and returns all descendants + root to the pool.
    /// Call AFTER the root has been detached from the visual tree.
    /// Collects all controls first, then pools bottom-up so DetachFromParent
    /// removes children before parents clear their collections.
    /// </summary>
    internal void UnmountAndPool(UIElement control)
    {
        var toPool = new List<FrameworkElement>();
        UnmountAndCollect(control, toPool);

        // Pool top-down: parent's CleanElement calls Children.Clear() which
        // detaches children, so by the time children are pooled they're parentless.
        for (int i = 0; i < toPool.Count; i++)
            _pool.Return(toPool[i]);
    }

    private void UnmountAndCollect(UIElement control, List<FrameworkElement> toPool)
    {
        if (SkipOrTrackIsolatedTeardown(control)) return;
        CountPerfChanges();
        NoteUnmountDuringSlotUpdate(control);
        // A control torn down with an ancestor while still fading out is no longer exiting.
        EndExit(control);
        CancelPostLayoutSweep(control);
        // Before any early exit: an unmounted control an app still holds must not keep a
        // FlipView's mounted item elements alive.
        if (control is FrameworkElement snapshotFe) ClearRefreshSnapshots(snapshotFe);

        // Capture connected animation snapshot while element is still in the visual tree
        if (FindMountedElementValue(control, static element => element.ConnectedAnimationKey) is { } connectedKey)
        {
            PrepareConnectedAnimationSource(connectedKey, control);
        }

        // Mirrors UnmountRecursive: release state throughout the decorator chain.
        if (control is FrameworkElement cleanupFe)
            CleanupElementChainForUnmount(cleanupFe);

        WithdrawAttachedValidation(control);

        // OnUnmountAction (.OnUnmount) — imperative teardown half of .OnMount.
        if (control is FrameworkElement umFe)
            RunUnmountAction(umFe);

        // Issue #917 — mirrors UnmountRecursive: a TitleBar reached through the
        // pooling traversal (nested in a poolable container) must also withdraw
        // its inference and caption-height contribution.
        if (control is WinUI.TitleBar pooledUnmountingBar)
            WithdrawTitleBar(pooledUnmountingBar);

        // Run cleanup logic (component teardown, etc.)
        if (_componentNodes.TryGetValue(control, out var node))
        {
            if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                    global::System.Diagnostics.Tracing.EventLevel.Informational,
                    Diagnostics.ReactorEventSource.Keywords.Lifecycle))
            {
                Diagnostics.ReactorEventSource.Log.ComponentUnmount(
                    Diagnostics.ComponentNames.For(node.Component, node.Element));
            }
            // The wrapper may be pooled and reused by an unrelated element; drop its id.
            if (node.DiagnosticId != 0)
                Diagnostics.ComponentRenderControls.Registry.Forget(node.DiagnosticId, control);
            RunUnmountCleanups(node);
            _componentNodes.Remove(control);
        }

        // Mirrors UnmountRecursive: the wrapper may be pooled and reused by an unrelated element.
        _errorBoundaryNodes.Remove(control);

        // Spec 047 §14 Phase 1 (1.1) — V1 handler unmount on the
        // UnmountAndCollect path. Standard handlers return CollectSelf
        // (mirrors the legacy registry behavior: pool the control, no
        // recursion). Phase 3 completion decorator handlers may opt out
        // of pooling (SkipPool — handler-managed teardown) or fall back
        // to the target's handler and default child traversal
        // (ContinueDefaultTraversal). The decorator and target share a control.
        ReactorState? unmountState = null;
        if (control is FrameworkElement unmountFe)
            TryGetReactorState(unmountFe, out unmountState);
        var unmountElement = unmountState?.DecoratorElement ?? unmountState?.Element;
        // Issue #1753: a .WithFlyout(...) flyout's content is unmounted with its host.
        if (unmountState?.ModifierFlyout is not null) ReleaseFlyoutAttachment((FrameworkElement)control);
        // Finish the saved flyout decorator before the existing target dispatch.
        if (unmountState?.DecoratorElement is { } decoratorElement
            && _v1Handlers.TryGet(decoratorElement.GetType(), out var decoratorEntry)
            && decoratorEntry.PreservesTargetTag && decoratorEntry.HasUnmount)
        {
            V1Protocol.V1UnmountDisposition decoratorDisposition;
            if (!IsolatesUnmountCleanupFailures)
                decoratorDisposition = decoratorEntry.Unmount(control, this);
            else if (!RunIsolatedTeardown(control, decoratorEntry, static (entry, c, r) => entry.Unmount(c, r), out decoratorDisposition))
                return;
            if (decoratorDisposition != V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal)
            {
                if (decoratorDisposition == V1Protocol.V1UnmountDisposition.CollectSelf
                    && control is FrameworkElement decoratorPool)
                    toPool.Add(decoratorPool);
                return;
            }
            unmountElement = null;
            if (ReferenceEquals(unmountState.DecoratorElement, decoratorElement))
            {
                unmountState.DecoratorElement = null;
                if (!ReferenceEquals(unmountState.Element, decoratorElement))
                    unmountElement = unmountState.Element;
            }
        }

        if (control is FrameworkElement v1Fe && unmountElement is Element v1TagEl
            && _v1Handlers.TryGet(v1TagEl.GetType(), out var v1Entry) && v1Entry.HasUnmount)
        {
            V1Protocol.V1UnmountDisposition v1Disposition;
            if (!IsolatesUnmountCleanupFailures)
                v1Disposition = v1Entry.Unmount(control, this);
            // A teardown that failed part-way leaves a half-torn-down control: never pool it.
            else if (!RunIsolatedTeardown(control, v1Entry, static (entry, c, r) => entry.Unmount(c, r), out v1Disposition))
                return;
            switch (v1Disposition)
            {
                case V1Protocol.V1UnmountDisposition.CollectSelf:
                    if (control is FrameworkElement v1Pool)
                        toPool.Add(v1Pool);
                    return;
                case V1Protocol.V1UnmountDisposition.SkipPool:
                    return;
                case V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal:
                    break;
            }
        }

        if (control is FrameworkElement fe && unmountState is { } regState
            && (regState.DecoratorElement ?? regState.Element) is Element tagEl
            && _typeRegistry.TryGetValue(tagEl.GetType(), out var reg) && reg.HasUnmount
            && TryBeginRegisteredUnmount(regState))
        {
            // A callback that failed part-way leaves a half-torn-down control: never pool it.
            if (!RunRegisteredUnmount(reg, control, regState))
                return;
            // Collect this control for pooling, but do NOT recurse into children —
            // they were created outside Reactor's tree and must not be pooled.
            // (Mirrors UnmountRecursive which returns early in this case.)
            if (control is FrameworkElement poolCandidate2)
                toPool.Add(poolCandidate2);
            return;
        }

        // Recurse into children. The shared child walk includes ItemsRepeater,
        // so pooled removals now tear down realized LazyStack rows too.
        ForEachReactorChildControl(control, child => UnmountAndCollect(child, toPool));
        if (control is WinUI.ContentControl cc && cc.Content is UIElement)
        {
            cc.Content = null; // Detach so pooled child has no parent
        }
        else if (control is WinUI.RichTextBlock rtb)
        {
            // Issue #480 — mirror UnmountRecursive's RichTextBlock arm so
            // Route A (Reactor element) inline UI children are torn down
            // before the block is recycled to the pool. ElementPool.Reset
            // calls rtb.Blocks.Clear() which drops the InlineUIContainer
            // references; without this, descendant hooks / event trampolines
            // would leak.
            UnmountInlineUIChildren(rtb);
        }

        if (control is FrameworkElement poolCandidate)
            toPool.Add(poolCandidate);
    }
}
