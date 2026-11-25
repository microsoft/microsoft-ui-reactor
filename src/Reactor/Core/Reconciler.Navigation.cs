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

// Reconciler partial: NavigationHost node state and navigation lifecycle hook traversal.
public sealed partial class Reconciler
{
    /// <summary>
    /// Tracks the state of a mounted NavigationHost in the tree.
    /// Stores the current child control/element and the subscription to route changes
    /// so content can be swapped when navigation occurs.
    /// </summary>
    internal class NavigationHostNode
    {
        public Navigation.ConnectedNavigationOperation? ConnectedOperation { get; set; }
        public long TransitionGeneration { get; set; }
        /// <summary>The type-erased navigation handle (implements INavigationHandle internally).</summary>
        public Navigation.INavigationHandle Handle { get; set; } = null!;
        /// <summary>The route that was last rendered.</summary>
        public object LastRenderedRoute { get; set; } = null!;
        /// <summary>The element returned by routeMap for the current route.</summary>
        public Element? CurrentChildElement { get; set; }
        /// <summary>The mounted WinUI control for the current route.</summary>
        public UIElement? CurrentChildControl { get; set; }
        /// <summary>The route-mapping function (type-erased).</summary>
        public Func<object, Element> RouteMap { get; set; } = null!;
        /// <summary>The rerender callback for triggering content swap.</summary>
        public Action? RequestRerender { get; set; }
        /// <summary>Handler attached to INavigationHandle.RouteChanged for cleanup.</summary>
        public Action? RouteChangedHandler { get; set; }
        /// <summary>Navigation mode recorded by the lifecycle guard before stack mutation.</summary>
        public Navigation.NavigationMode? PendingNavigationMode { get; set; }
        /// <summary>Previous route recorded by the lifecycle guard before stack mutation.</summary>
        public object? PendingPreviousRoute { get; set; }
        /// <summary>The host-level default transition.</summary>
        public Navigation.NavigationTransition HostTransition { get; set; } = Navigation.NavigationTransition.Default;
        /// <summary>True if a transition animation is currently running.</summary>
        public bool TransitionInProgress { get; set; }
        /// <summary>The host-level cache mode.</summary>
        public Navigation.NavigationCacheMode CacheMode { get; set; } = Navigation.NavigationCacheMode.Disabled;
        /// <summary>Page cache for this NavigationHost (null when CacheMode is Disabled).</summary>
        public Navigation.NavigationCache? Cache { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Navigation lifecycle hook traversal
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects all <see cref="RenderContext.NavigationLifecycleHookState"/> instances from
    /// the component subtree rooted at <paramref name="root"/>.
    /// </summary>
    internal List<RenderContext.NavigationLifecycleHookState> CollectLifecycleHooks(UIElement? root)
    {
        var hooks = new List<RenderContext.NavigationLifecycleHookState>();
        CollectLifecycleHooksRecursive(root, hooks);
        return hooks;
    }

    private void CollectLifecycleHooksRecursive(UIElement? control, List<RenderContext.NavigationLifecycleHookState> hooks)
    {
        // A nested host owns its pages' lifecycle. Its route contexts and guards
        // belong to that stack, not the enclosing page's navigation operation.
        if (control is null || _navigationHostNodes.ContainsKey(control)) return;

        if (_componentNodes.TryGetValue(control, out var node))
        {
            var ctx = node.Component?.Context ?? node.Context;
            var hook = ctx?.GetNavigationLifecycleHook();
            if (hook is not null)
                hooks.Add(hook);
        }

        ForEachReactorChildControl(control, child => CollectLifecycleHooksRecursive(child, hooks));
    }

    /// <summary>
    /// Invokes post-navigation lifecycle callbacks: onNavigatedTo on the new page,
    /// then onNavigatedFrom on the old page (using pre-collected hooks).
    /// </summary>
    internal void InvokePostNavigationLifecycle(
        UIElement? newChildControl,
        List<RenderContext.NavigationLifecycleHookState>? oldHooks,
        object currentRoute, object? previousRoute, Navigation.NavigationMode mode)
    {
        // onNavigatedTo on the new page's component tree
        var newHooks = CollectLifecycleHooks(newChildControl);
        var navigatedToCtx = new Navigation.NavigatedToContext(currentRoute, previousRoute, mode);
        foreach (var hook in newHooks)
        {
            hook.OnNavigatedTo?.Invoke(navigatedToCtx);
        }

        // onNavigatedFrom on the old page (callbacks captured at last render)
        if (oldHooks is not null)
        {
            var navigatedFromCtx = new Navigation.NavigatedFromContext(
                previousRoute!, currentRoute, mode);
            foreach (var hook in oldHooks)
            {
                hook.OnNavigatedFrom?.Invoke(navigatedFromCtx);
            }
        }
    }

    /// <summary>
    /// Invokes <c>onNavigatingTo</c> (destination-side guard) on all lifecycle hooks
    /// in the new page's subtree. Returns true if navigation should proceed.
    /// </summary>
    internal bool InvokeNavigatingTo(
        UIElement? newChildControl,
        object currentRoute, object? previousRoute, Navigation.NavigationMode mode)
    {
        var hooks = CollectLifecycleHooks(newChildControl);
        var ctx = new Navigation.NavigatingToContext(currentRoute, previousRoute, mode);
        foreach (var hook in hooks)
        {
            hook.OnNavigatingTo?.Invoke(ctx);
            if (ctx.IsCancelled)
            {
                Navigation.NavigationDiagnostics.OnNavigationCancelled(
                    previousRoute ?? currentRoute, currentRoute, mode, "destination guard");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Invokes <c>onNavigatingFrom</c> on all lifecycle hooks in the subtree.
    /// Sets <see cref="Navigation.NavigatingFromContext.IsCancelled"/> if any callback cancels.
    /// </summary>
    internal void InvokeNavigatingFrom(UIElement? root, Navigation.NavigatingFromContext ctx)
    {
        if (root is null || _navigationHostNodes.ContainsKey(root)) return;

        if (_componentNodes.TryGetValue(root, out var node))
        {
            var renderCtx = node.Component?.Context ?? node.Context;
            var hook = renderCtx?.GetNavigationLifecycleHook();
            hook?.OnNavigatingFrom?.Invoke(ctx);
            if (ctx.IsCancelled) return;
        }

        ForEachReactorChildControl(root, child =>
        {
            InvokeNavigatingFrom(child, ctx);
            return !ctx.IsCancelled;
        });
    }
}
