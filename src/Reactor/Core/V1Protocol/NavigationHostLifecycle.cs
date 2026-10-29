using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Spec 047 §14 Phase 4 — V1-owned NavigationHost mount/update logic, relocated
// verbatim out of Reconciler.Mount.cs / Reconciler.Update.cs (Bucket 1 cleanup).
//
// NavigationHost is control-side mounted: the value returned to the engine is a
// host Grid, while the real page content (resolved from the route map) lives in
// that Grid's children and is swapped on route changes. Per-instance route /
// cache / transition state is tracked in the reconciler's _navigationHostNodes
// table (shared with the Dispose() teardown loop and CleanupNavigationHostNode,
// which the handler's Unmount calls). The navigation lifecycle-hook walkers
// (InvokeNavigatingFrom/To, CollectLifecycleHooks, InvokePostNavigationLifecycle)
// stay on the Reconciler because they read general reconciler state
// (_componentNodes); this class calls back into them.
internal static class NavigationHostLifecycle
{
    public static WinUI.Grid Mount(Reconciler reconciler, NavigationHostElement element, Action requestRerender)
    {
        var grid = new WinUI.Grid();
        var handle = (Navigation.INavigationHandle)element.NavigationHandle;
        var routeMap = element.RouteMap;
        var currentRoute = handle.CurrentRoute;

        // Resolve and mount the initial route's element
        var childElement = routeMap(currentRoute);
        var childControl = reconciler.Mount(childElement, requestRerender);
        if (childControl is not null)
            grid.Children.Add(childControl);

        // Track state for update/unmount
        var node = new Reconciler.NavigationHostNode
        {
            Handle = handle,
            LastRenderedRoute = currentRoute,
            CurrentChildElement = childElement,
            CurrentChildControl = childControl,
            RouteMap = routeMap,
            RequestRerender = requestRerender,
            HostTransition = element.Transition,
            CacheMode = element.CacheMode,
        };

        // Create page cache when caching is enabled
        if (element.CacheMode != Navigation.NavigationCacheMode.Disabled)
        {
            node.Cache = new Navigation.NavigationCache(
                element.CacheSize, evicted => Navigation.ConnectedNavigationOperation.UnmountPage(reconciler, evicted));
        }

        // Subscribe to route changes so NavigationHost updates even if an intermediate
        // component's ShouldUpdate blocks the re-render propagation.
        void onRouteChanged() => requestRerender();
        handle.RouteChanged += onRouteChanged;
        node.RouteChangedHandler = onRouteChanged;

        // Wire lifecycle guard: invokes onNavigatingFrom callbacks from the current
        // page's component tree before the stack mutation. Records the navigation mode
        // and previous route for post-swap onNavigatedTo/onNavigatedFrom invocation.
        handle.LifecycleGuard = ctx =>
        {
            reconciler.InvokeNavigatingFrom(node.CurrentChildControl, ctx);
            if (!ctx.IsCancelled)
            {
                node.PendingNavigationMode = ctx.Mode;
                node.PendingPreviousRoute = ctx.Route;
            }
        };

        reconciler._navigationHostNodes[grid] = node;
        // Issue #1291 — a navigation host mounted inside a boundary child that then fails is
        // discarded unattached; record it so the boundary's rollback releases its route
        // subscription.
        reconciler.NoteBoundaryMountRegistration(grid);
        return grid;
    }

    public static UIElement? Update(
        Reconciler reconciler,
        NavigationHostElement oldEl, NavigationHostElement newEl,
        WinUI.Grid grid, Action requestRerender)
    {
        if (!reconciler._navigationHostNodes.TryGetValue(grid, out var node))
        {
            // Lost tracking — remount from scratch
            return reconciler.Mount(newEl, requestRerender);
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
                reconciler.InvokeNavigatingFrom(node.CurrentChildControl, ctx);
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
            bool unmountedByUpdate = false;
            var replacement = node.CurrentChildControl is not null
                ? reconciler.UpdateChildTracked(node.CurrentChildElement, newChildElement, node.CurrentChildControl, requestRerender, out unmountedByUpdate)
                : reconciler.Mount(newChildElement, requestRerender);

            if (replacement is not null && node.CurrentChildControl is not null)
            {
                // Child control type changed — swap in grid
                var idx = grid.Children.IndexOf(node.CurrentChildControl);
                if (idx >= 0)
                    grid.Children[idx] = replacement;
                else
                    grid.Children.Add(replacement);
                if (!unmountedByUpdate) reconciler.UnmountChild(node.CurrentChildControl);
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
            var transitionGeneration = ++node.TransitionGeneration;
            // Disposal runs departure callbacks synchronously. Consume this
            // navigation's options first so a callback's redirect keeps its own
            // pending state for the next render pass.
            var pendingMode = node.PendingNavigationMode;
            var pendingPreviousRoute = node.PendingPreviousRoute;
            var transitionOverride = handle.PendingTransitionOverride;
            var connectedKey = handle.PendingConnectedAnimationKey;
            node.PendingNavigationMode = null;
            node.PendingPreviousRoute = null;
            handle.PendingTransitionOverride = null;
            handle.PendingConnectedAnimationKey = null;
            var interruptedOperation = node.ConnectedOperation;
            node.ConnectedOperation = null;
            interruptedOperation?.Dispose();
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

            // Collect lifecycle hooks from old page BEFORE detach/unmount
            var oldHooks = pendingMode is not null
                ? reconciler.CollectLifecycleHooks(oldChildControl)
                : null;

            // Resolve transition: per-navigation override > host default
            var transition = transitionOverride ?? node.HostTransition;
            // Connected(key) remains a shared-element-only transition. The
            // independent option composes with any ordinary page transition.
            if (transition is Navigation.ConnectedTransition connectedTransition)
            {
                connectedKey ??= connectedTransition.AnimationKey;
                transition = Navigation.NavigationTransition.None;
            }
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
                if (connectedKey is not null && newChildControl is { } cachedControl)
                {
                    // Route payloads can change while the page is cached (for
                    // example selecting a second device for the same detail route).
                    // Reconcile current props before looking up the shared key.
                    var refreshedElement = node.RouteMap(currentRoute);
                    var candidates = Navigation.ConnectedNavigationOperation.Collect(reconciler, cachedControl);
                    foreach (var candidate in candidates) reconciler.ClaimNavigationConnectedElement(candidate);
                    try
                    {
                        newChildControl = reconciler.Reconcile(newChildElement, refreshedElement, cachedControl, requestRerender);
                        newChildElement = refreshedElement;
                    }
                    finally
                    {
                        foreach (var candidate in candidates) reconciler.ReleaseNavigationConnectedElement(candidate);
                    }
                }
            }
            else
            {
                // Cache miss — mount fresh
                newChildElement = node.RouteMap(currentRoute);
                newChildControl = reconciler.Mount(newChildElement, requestRerender);
            }

            // Destination-side guard: invoke onNavigatingTo on the new page.
            // If cancelled, revert to old page.
            if (!reconciler.InvokeNavigatingTo(newChildControl, currentRoute, pendingPreviousRoute, mode))
            {
                if (!wasCacheHit && newChildControl is not null)
                    reconciler.UnmountChild(newChildControl);
                return null;
            }

            // Update node state immediately
            node.CurrentChildElement = newChildElement;
            node.CurrentChildControl = newChildControl;
            node.LastRenderedRoute = currentRoute;

            // Action to finalize the old page (cache or unmount)
            void FinalizeOldPage(UIElement? oldCtrl, Element? oldElem, object? oldRoute)
            {
                if (oldCtrl is null || ReferenceEquals(oldCtrl, node.CurrentChildControl)) return;
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
                    Navigation.ConnectedNavigationOperation.UnmountPage(reconciler, oldCtrl);
                }
            }

            // Determine whether to run an animated transition
            bool useAnimation = (connectedKey is not null || transition is not Navigation.SuppressTransition)
                && oldChildControl is not null
                && newChildControl is not null;

            if (useAnimation)
            {
                // Mount new content at Opacity 0 alongside old content
                var inVisual = ElementCompositionPreview.GetElementVisual(newChildControl!);
                ElementPool.MarkCompositorTainted(newChildControl!);
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

                void CompleteTransition()
                {
                    if (node.TransitionGeneration == transitionGeneration)
                    {
                        node.TransitionInProgress = false;
                        node.ConnectedOperation = null;
                    }
                    FinalizeOldPage(capturedOldControl, capturedOldElement, capturedOldRoute);

                    // Every departed page receives its completion callback,
                    // including when a newer navigation interrupts this edge.
                    // Only the latest destination receives OnNavigatedTo.
                    reconciler.InvokePostNavigationLifecycle(
                        node.TransitionGeneration == transitionGeneration ? capturedNewControl : null,
                        capturedOldHooks,
                        capturedCurrentRoute, capturedPreviousRoute, capturedMode);
                }

                if (connectedKey is not null)
                {
                    var operation = new Navigation.ConnectedNavigationOperation(
                        reconciler, capturedOldControl!, capturedNewControl, connectedKey,
                        transition, capturedMode, CompleteTransition);
                    node.ConnectedOperation = operation.IsCompleted ? null : operation;
                }
                else
                    Navigation.TransitionEngine.RunTransition(
                        capturedOldControl!, capturedNewControl, transition, capturedMode, CompleteTransition);
            }
            else
            {
                // Instant swap. When both pages exist this still goes through RunTransition,
                // even though there is nothing to animate: that is what claims the transition
                // ownership stamp and normalizes both visuals. Skipping it would let a suppress
                // navigation overlap an in-flight predecessor without revoking its claim, and
                // the predecessor's completion handler would later reset pages this swap has
                // already moved past.
                if (oldChildControl is not null && newChildControl is not null)
                {
                    grid.Children.Add(newChildControl);

                    var capturedOldControl = oldChildControl;
                    var capturedOldElement = oldChildElement;
                    var capturedOldRoute = previousRoute;
                    var capturedNewControl = newChildControl;
                    var capturedOldHooks = oldHooks;
                    var capturedMode = mode;
                    var capturedCurrentRoute = currentRoute;
                    var capturedPreviousRoute = pendingPreviousRoute;

                    Navigation.TransitionEngine.RunTransition(
                        capturedOldControl, capturedNewControl, transition, capturedMode,
                        onComplete: () =>
                        {
                            FinalizeOldPage(capturedOldControl, capturedOldElement, capturedOldRoute);

                            reconciler.InvokePostNavigationLifecycle(
                                capturedNewControl, capturedOldHooks,
                                capturedCurrentRoute, capturedPreviousRoute, capturedMode);
                        });
                }
                else
                {
                    // One side is missing — there is no pair to hand to the transition engine.
                    FinalizeOldPage(oldChildControl, oldChildElement, previousRoute);

                    if (newChildControl is not null)
                        grid.Children.Add(newChildControl);

                    reconciler.InvokePostNavigationLifecycle(
                        newChildControl, oldHooks,
                        currentRoute, pendingPreviousRoute, mode);
                }
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
                node.Cache = new Navigation.NavigationCache(newEl.CacheSize,
                    evicted => Navigation.ConnectedNavigationOperation.UnmountPage(reconciler, evicted));
            }
        }
        if (node.Cache is not null)
            node.Cache.MaxSize = newEl.CacheSize;

        return null; // Patched in place
    }
}
