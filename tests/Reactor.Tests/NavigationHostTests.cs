using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 2 tests: NavigationHost element, DSL, and node tracking.
///
/// Tests are split by infrastructure needs:
/// - Unit tests (this file): Element construction, DSL factory, subscription lifecycle,
///   NavigationHostNode state management. No WinUI controls needed.
/// - Self-host tests: NavigationHost mount/update/content swap via Reconciler.
///   These require WinUI Application context and run in Reactor.AppTests or self-test fixtures.
/// </summary>
public class NavigationHostTests
{
    private static readonly Action NoOp = () => { };

    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;
    private sealed record Profile(string Name) : Route;

    private static Element RouteToElement(Route route) => route switch
    {
        Home => TextBlock("Home Page"),
        Detail d => TextBlock($"Detail #{d.Id}"),
        Settings => TextBlock("Settings Page"),
        Profile p => TextBlock($"Profile: {p.Name}"),
        _ => TextBlock("Unknown"),
    };

    // ════════════════════════════════════════════════════════════════
    //  NavigationHostElement record
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigationHostElement_Stores_Handle_And_RouteMap()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        Func<object, Element> routeMap = r => RouteToElement((Route)r);

        var element = new NavigationHostElement(handle, routeMap);

        Assert.Same(handle, element.NavigationHandle);
        Assert.Same(routeMap, element.RouteMap);
    }

    [Fact]
    public void NavigationHostElement_Has_Sensible_Defaults()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = new NavigationHostElement(handle, _ => EmptyElement.Instance);

        Assert.Same(NavigationTransition.Default, element.Transition);
        Assert.Equal(NavigationCacheMode.Disabled, element.CacheMode);
        Assert.Equal(10, element.CacheSize);
    }

    [Fact]
    public void NavigationHostElement_With_Initializer_Overrides_Defaults()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = new NavigationHostElement(handle, _ => EmptyElement.Instance) with
        {
            Transition = NavigationTransition.None,
            CacheMode = NavigationCacheMode.Enabled,
            CacheSize = 5,
        };

        Assert.IsType<SuppressTransition>(element.Transition);
        Assert.Equal(NavigationCacheMode.Enabled, element.CacheMode);
        Assert.Equal(5, element.CacheSize);
    }

    [Fact]
    public void NavigationHostElement_RouteMap_Resolves_Routes_Correctly()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var element = new NavigationHostElement(handle, r => RouteToElement((Route)r));

        var homeResult = element.RouteMap(new Home());
        Assert.IsType<TextBlockElement>(homeResult);
        Assert.Equal("Home Page", ((TextBlockElement)homeResult).Content);

        var detailResult = element.RouteMap(new Detail(42));
        Assert.Equal("Detail #42", ((TextBlockElement)detailResult).Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  DSL factory: NavigationHost<TRoute>
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DSL_NavigationHost_Returns_NavigationHostElement()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = NavigationHost(handle, RouteToElement);

        Assert.IsType<NavigationHostElement>(element);
    }

    [Fact]
    public void DSL_NavigationHost_Provides_Context_For_Child_Mode()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = NavigationHost(handle, RouteToElement);

        Assert.NotNull(element.ContextValues);
        Assert.Single(element.ContextValues);
        Assert.True(element.ContextValues.ContainsKey(NavigationContext<Route>.Instance));
        Assert.Same(handle, element.ContextValues[NavigationContext<Route>.Instance]);
    }

    [Fact]
    public void DSL_NavigationHost_Type_Erases_RouteMap_Correctly()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = NavigationHost(handle, RouteToElement);

        // The type-erased routeMap should correctly handle boxed Route values
        var result = element.RouteMap(new Detail(7));
        Assert.IsType<TextBlockElement>(result);
        Assert.Equal("Detail #7", ((TextBlockElement)result).Content);
    }

    [Fact]
    public void DSL_NavigationHost_With_Initializer_Preserves_Context()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var element = NavigationHost(handle, RouteToElement) with
        {
            Transition = NavigationTransition.Fade(),
            CacheSize = 3,
        };

        // with { } should preserve ContextValues from .Provide()
        Assert.NotNull(element.ContextValues);
        Assert.True(element.ContextValues.ContainsKey(NavigationContext<Route>.Instance));
    }

    // ════════════════════════════════════════════════════════════════
    //  INavigationHandle interface (internal, for reconciler)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void INavigationHandle_CurrentRoute_Returns_Boxed_Route()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        Assert.IsType<Home>(iHandle.CurrentRoute);

        handle.Navigate(new Detail(5));
        Assert.Equal(new Detail(5), iHandle.CurrentRoute);
    }

    [Fact]
    public void INavigationHandle_RouteChanged_Fires_On_Navigation()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        int changeCount = 0;
        iHandle.RouteChanged += () => changeCount++;

        handle.Navigate(new Detail(1));
        Assert.Equal(1, changeCount);

        handle.GoBack();
        Assert.Equal(2, changeCount);

        handle.Navigate(new Settings());
        Assert.Equal(3, changeCount);

        handle.Replace(new Profile("Alice"));
        Assert.Equal(4, changeCount);

        handle.Reset(new Home());
        Assert.Equal(5, changeCount);
    }

    [Fact]
    public void INavigationHandle_RouteChanged_Does_Not_Fire_On_Failed_Navigation()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        int changeCount = 0;
        iHandle.RouteChanged += () => changeCount++;

        // GoBack on empty stack should fail and not fire
        handle.GoBack();
        Assert.Equal(0, changeCount);

        // GoForward on empty forward stack should fail and not fire
        handle.GoForward();
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void INavigationHandle_RouteChanged_Unsubscribe_Works()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        int changeCount = 0;
        void handler() => changeCount++;
        iHandle.RouteChanged += handler;

        handle.Navigate(new Detail(1));
        Assert.Equal(1, changeCount);

        iHandle.RouteChanged -= handler;

        handle.Navigate(new Settings());
        Assert.Equal(1, changeCount); // Should NOT have incremented
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationHostNode state management (internal)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigationHostNode_Tracks_Initial_State()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var node = new Reconciler.NavigationHostNode
        {
            Handle = (INavigationHandle)handle,
            LastRenderedRoute = handle.CurrentRoute,
            RouteMap = r => RouteToElement((Route)r),
        };

        Assert.IsType<Home>(node.LastRenderedRoute);
        Assert.Null(node.CurrentChildElement);
        Assert.Null(node.CurrentChildControl);
    }

    [Fact]
    public void NavigationHostNode_Detects_Route_Change()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var node = new Reconciler.NavigationHostNode
        {
            Handle = (INavigationHandle)handle,
            LastRenderedRoute = handle.CurrentRoute,
            RouteMap = r => RouteToElement((Route)r),
        };

        handle.Navigate(new Detail(1));

        // The node's LastRenderedRoute is stale — route has changed
        Assert.False(Equals(node.Handle.CurrentRoute, node.LastRenderedRoute));
    }

    [Fact]
    public void NavigationHostNode_RouteMap_Produces_Correct_Elements()
    {
        var node = new Reconciler.NavigationHostNode
        {
            Handle = null!,
            LastRenderedRoute = new Home(),
            RouteMap = r => RouteToElement((Route)r),
        };

        var homeElement = node.RouteMap(new Home());
        Assert.IsType<TextBlockElement>(homeElement);
        Assert.Equal("Home Page", ((TextBlockElement)homeElement).Content);

        var detailElement = node.RouteMap(new Detail(42));
        Assert.Equal("Detail #42", ((TextBlockElement)detailElement).Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Two independent stacks don't interfere (unit level)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Independent_Navigation_Stacks_Are_Isolated()
    {
        var ctx1 = new RenderContext();
        var ctx2 = new RenderContext();

        ctx1.BeginRender(() => { });
        var nav1 = ctx1.UseNavigation<Route>(new Home());

        ctx2.BeginRender(() => { });
        var nav2 = ctx2.UseNavigation<Route>(new Settings());

        // Navigate on stack 1
        nav1.Navigate(new Detail(1));

        // Stack 2 should be unaffected
        Assert.IsType<Detail>(nav1.CurrentRoute);
        Assert.IsType<Settings>(nav2.CurrentRoute);
    }

    [Fact]
    public void Child_Component_Can_Navigate_Parents_Stack()
    {
        // Simulate parent creating navigation, child using it
        var parentCtx = new RenderContext();
        parentCtx.BeginRender(() => { });
        var nav = parentCtx.UseNavigation<Route>(new Home());

        // Child receives same handle via context
        var childCtx = new RenderContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [NavigationContext<Route>.Instance] = nav });
        childCtx.BeginRender(() => { }, scope);
        var childNav = childCtx.UseNavigation<Route>();

        // Child navigates — should affect parent's stack
        childNav.Navigate(new Detail(99));
        Assert.Equal(new Detail(99), nav.CurrentRoute);
        Assert.True(nav.CanGoBack);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationContext<TRoute> static instance
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigationContext_Returns_Same_Instance_Per_Type()
    {
        var ctx1 = NavigationContext<Route>.Instance;
        var ctx2 = NavigationContext<Route>.Instance;
        Assert.Same(ctx1, ctx2);
    }

    [Fact]
    public void NavigationContext_Default_Value_Is_Null()
    {
        var ctx = NavigationContext<Route>.Instance;
        Assert.Null(ctx.DefaultValue);
    }

    [Fact]
    public void NavigationContext_Different_Route_Types_Get_Different_Instances()
    {
        var routeCtx = NavigationContext<Route>.Instance;
        var stringCtx = NavigationContext<string>.Instance;

        // Different generic type arguments produce different context instances
        Assert.NotSame((object)routeCtx, (object)stringCtx);
    }
}
