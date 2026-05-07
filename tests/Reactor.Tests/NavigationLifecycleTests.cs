using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 3 tests: Navigation lifecycle hooks.
///
/// Unit tests covering:
/// - UseNavigationLifecycle hook state management
/// - onNavigatedTo / onNavigatingFrom / onNavigatedFrom callback invocation
/// - onNavigatingFrom cancellation
/// - Lifecycle context values (Route, PreviousRoute, TargetRoute, Mode)
/// - Multiple components with lifecycle hooks
/// - Lifecycle hooks coexisting with other hooks
/// - Guard + lifecycle guard ordering
/// </summary>
public class NavigationLifecycleTests
{
    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;
    private sealed record Profile(string Name) : Route;

    // ════════════════════════════════════════════════════════════════
    //  NavigatedToContext / NavigatedFromContext construction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigatedToContext_Exposes_All_Properties()
    {
        var ctx = new NavigatedToContext(new Detail(1), new Home(), NavigationMode.Push);

        Assert.Equal(new Detail(1), ctx.Route);
        Assert.Equal(new Home(), ctx.PreviousRoute);
        Assert.Equal(NavigationMode.Push, ctx.Mode);
    }

    [Fact]
    public void NavigatedToContext_Allows_Null_PreviousRoute()
    {
        var ctx = new NavigatedToContext(new Home(), null, NavigationMode.Reset);

        Assert.Equal(new Home(), ctx.Route);
        Assert.Null(ctx.PreviousRoute);
    }

    [Fact]
    public void NavigatedFromContext_Exposes_All_Properties()
    {
        var ctx = new NavigatedFromContext(new Home(), new Detail(1), NavigationMode.Push);

        Assert.Equal(new Home(), ctx.Route);
        Assert.Equal(new Detail(1), ctx.TargetRoute);
        Assert.Equal(NavigationMode.Push, ctx.Mode);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseNavigationLifecycle hook state
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseNavigationLifecycle_Registers_Hook_State()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        ctx.UseNavigationLifecycle(
            onNavigatedTo: _ => { },
            onNavigatingFrom: _ => { },
            onNavigatedFrom: _ => { });

        var hook = ctx.GetNavigationLifecycleHook();
        Assert.NotNull(hook);
        Assert.NotNull(hook.OnNavigatedTo);
        Assert.NotNull(hook.OnNavigatingFrom);
        Assert.NotNull(hook.OnNavigatedFrom);
    }

    [Fact]
    public void UseNavigationLifecycle_With_Null_Callbacks()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        ctx.UseNavigationLifecycle(); // All null

        var hook = ctx.GetNavigationLifecycleHook();
        Assert.NotNull(hook);
        Assert.Null(hook.OnNavigatedTo);
        Assert.Null(hook.OnNavigatingFrom);
        Assert.Null(hook.OnNavigatedFrom);
    }

    [Fact]
    public void UseNavigationLifecycle_Updates_Callbacks_On_Rerender()
    {
        var ctx = new RenderContext();
        int callCount1 = 0, callCount2 = 0;

        // First render
        ctx.BeginRender(() => { });
        ctx.UseNavigationLifecycle(onNavigatedTo: _ => callCount1++);

        // Second render with different callback
        ctx.BeginRender(() => { });
        ctx.UseNavigationLifecycle(onNavigatedTo: _ => callCount2++);

        var hook = ctx.GetNavigationLifecycleHook();
        Assert.NotNull(hook);

        // Should use latest callback
        hook.OnNavigatedTo!(new NavigatedToContext(new Home(), null, NavigationMode.Push));
        Assert.Equal(0, callCount1);
        Assert.Equal(1, callCount2);
    }

    [Fact]
    public void GetNavigationLifecycleHook_Returns_Null_When_No_Hook()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseState(0); // Register a different hook type

        Assert.Null(ctx.GetNavigationLifecycleHook());
    }

    [Fact]
    public void UseNavigationLifecycle_Coexists_With_UseState()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (count, setCount) = ctx.UseState(0);
        ctx.UseNavigationLifecycle(onNavigatedTo: _ => { });
        var (name, setName) = ctx.UseState("test");

        Assert.Equal(0, count);
        Assert.Equal("test", name);
        Assert.NotNull(ctx.GetNavigationLifecycleHook());
    }

    [Fact]
    public void UseNavigationLifecycle_Throws_If_Hook_Order_Changes()
    {
        var ctx = new RenderContext();

        // First render: UseState then UseNavigationLifecycle
        ctx.BeginRender(() => { });
        ctx.UseState(0);
        ctx.UseNavigationLifecycle();

        // Second render: try UseNavigationLifecycle at UseState's index
        ctx.BeginRender(() => { });
        Assert.Throws<HookOrderException>(() => ctx.UseNavigationLifecycle());
    }

    // ════════════════════════════════════════════════════════════════
    //  LifecycleGuard on NavigationStack
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LifecycleGuard_Fires_Before_Programmatic_Guard()
    {
        var stack = new NavigationStack<Route>(new Home());
        var order = new List<string>();

        stack.LifecycleGuard = ctx => order.Add("lifecycle");
        stack.Guard = ctx => { order.Add("guard"); };

        stack.Push(new Detail(1));

        Assert.Equal(new[] { "lifecycle", "guard" }, order);
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Programmatic_Guard()
    {
        var stack = new NavigationStack<Route>(new Home());
        bool guardCalled = false;

        stack.LifecycleGuard = ctx => ctx.Cancel();
        stack.Guard = ctx => { guardCalled = true; };

        var result = stack.Push(new Detail(1));

        Assert.False(result);
        Assert.False(guardCalled);
        Assert.IsType<Home>(stack.Current); // Stack unchanged
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Push()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.LifecycleGuard = ctx => ctx.Cancel();

        Assert.False(stack.Push(new Detail(1)));
        Assert.IsType<Home>(stack.Current);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Pop()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.Push(new Detail(1));

        stack.LifecycleGuard = ctx => ctx.Cancel();

        Assert.False(stack.Pop());
        Assert.IsType<Detail>(stack.Current);
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Replace()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.LifecycleGuard = ctx => ctx.Cancel();

        Assert.False(stack.Replace(new Settings()));
        Assert.IsType<Home>(stack.Current);
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Reset()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.Push(new Detail(1));
        stack.LifecycleGuard = ctx => ctx.Cancel();

        Assert.False(stack.Reset(new Settings()));
        Assert.IsType<Detail>(stack.Current);
        Assert.True(stack.CanGoBack);
    }

    [Fact]
    public void LifecycleGuard_Cancellation_Prevents_Forward()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.Push(new Detail(1));
        stack.Pop();
        // Now at Home with Detail in forward stack

        stack.LifecycleGuard = ctx => ctx.Cancel();

        Assert.False(stack.Forward());
        Assert.IsType<Home>(stack.Current);
        Assert.True(stack.CanGoForward);
    }

    [Fact]
    public void LifecycleGuard_Receives_Correct_Context_On_Push()
    {
        var stack = new NavigationStack<Route>(new Home());
        NavigatingFromContext? captured = null;
        stack.LifecycleGuard = ctx => captured = ctx;

        stack.Push(new Detail(42));

        Assert.NotNull(captured);
        Assert.Equal(new Home(), captured!.Route);
        Assert.Equal(new Detail(42), captured.TargetRoute);
        Assert.Equal(NavigationMode.Push, captured.Mode);
    }

    [Fact]
    public void LifecycleGuard_Receives_Correct_Context_On_Pop()
    {
        var stack = new NavigationStack<Route>(new Home());
        stack.Push(new Detail(1));

        NavigatingFromContext? captured = null;
        stack.LifecycleGuard = ctx => captured = ctx;

        stack.Pop();

        Assert.NotNull(captured);
        Assert.Equal(new Detail(1), captured!.Route);
        Assert.Equal(new Home(), captured.TargetRoute);
        Assert.Equal(NavigationMode.Pop, captured.Mode);
    }

    [Fact]
    public void LifecycleGuard_Not_Called_When_Null()
    {
        var stack = new NavigationStack<Route>(new Home());
        // No lifecycle guard set — just verify no exception
        Assert.True(stack.Push(new Detail(1)));
    }

    // ════════════════════════════════════════════════════════════════
    //  INavigationHandle.LifecycleGuard
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void INavigationHandle_LifecycleGuard_Delegates_To_Stack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        bool called = false;
        iHandle.LifecycleGuard = ctx => called = true;

        handle.Navigate(new Detail(1));
        Assert.True(called);
    }

    [Fact]
    public void INavigationHandle_LifecycleGuard_Cancellation_Prevents_Navigate()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        iHandle.LifecycleGuard = ctx => ctx.Cancel();

        bool result = handle.Navigate(new Detail(1));
        Assert.False(result);
        Assert.IsType<Home>(handle.CurrentRoute);
    }

    [Fact]
    public void INavigationHandle_LifecycleGuard_Can_Be_Cleared()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        iHandle.LifecycleGuard = ctx => ctx.Cancel();
        iHandle.LifecycleGuard = null;

        // Should now succeed since guard was cleared
        Assert.True(handle.Navigate(new Detail(1)));
        Assert.IsType<Detail>(handle.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationHandle Navigated event still fires correctly with lifecycle guard
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Navigated_Event_Fires_After_Successful_Navigation_With_LifecycleGuard()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        // Set a non-cancelling lifecycle guard
        iHandle.LifecycleGuard = ctx => { /* allow */ };

        NavigationEventArgs<Route>? eventArgs = null;
        handle.Navigated += args => eventArgs = args;

        handle.Navigate(new Detail(1));

        Assert.NotNull(eventArgs);
        Assert.Equal(new Detail(1), eventArgs!.Route);
        Assert.Equal(new Home(), eventArgs.PreviousRoute);
    }

    [Fact]
    public void Navigated_Event_Does_Not_Fire_When_LifecycleGuard_Cancels()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        iHandle.LifecycleGuard = ctx => ctx.Cancel();

        bool eventFired = false;
        handle.Navigated += _ => eventFired = true;

        handle.Navigate(new Detail(1));

        Assert.False(eventFired);
    }

    // ════════════════════════════════════════════════════════════════
    //  Full lifecycle sequence (unit-level, no reconciler)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Lifecycle_Callbacks_Capture_Current_State_Via_Closures()
    {
        // Verifies that lifecycle callbacks can close over component state
        var ctx = new RenderContext();
        string capturedValue = "";

        ctx.BeginRender(() => { });
        var (value, _) = ctx.UseState("hello");
        ctx.UseNavigationLifecycle(
            onNavigatedTo: navCtx => capturedValue = value);

        var hook = ctx.GetNavigationLifecycleHook();
        hook!.OnNavigatedTo!(new NavigatedToContext(new Home(), null, NavigationMode.Push));

        Assert.Equal("hello", capturedValue);
    }

    [Fact]
    public void Multiple_Navigations_Each_Get_Correct_Context()
    {
        var stack = new NavigationStack<Route>(new Home());
        var contexts = new List<(object Route, object Target, NavigationMode Mode)>();

        stack.LifecycleGuard = ctx =>
            contexts.Add((ctx.Route, ctx.TargetRoute, ctx.Mode));

        stack.Push(new Detail(1));
        stack.Push(new Detail(2));
        stack.Pop();
        stack.Replace(new Settings());

        Assert.Equal(4, contexts.Count);

        Assert.Equal((new Home(), new Detail(1), NavigationMode.Push), contexts[0]);
        Assert.Equal((new Detail(1), new Detail(2), NavigationMode.Push), contexts[1]);
        Assert.Equal((new Detail(2), new Detail(1), NavigationMode.Pop), contexts[2]);
        Assert.Equal((new Detail(1), new Settings(), NavigationMode.Replace), contexts[3]);
    }

    [Fact]
    public void Conditional_Cancellation_Based_On_Target_Route()
    {
        var stack = new NavigationStack<Route>(new Home());

        // Only block navigation to Settings
        stack.LifecycleGuard = ctx =>
        {
            if (ctx.TargetRoute is Settings)
                ctx.Cancel();
        };

        Assert.True(stack.Push(new Detail(1)));
        Assert.IsType<Detail>(stack.Current);

        Assert.False(stack.Push(new Settings()));
        Assert.IsType<Detail>(stack.Current); // Blocked

        Assert.True(stack.Replace(new Profile("Alice")));
        Assert.IsType<Profile>(stack.Current);
    }
}
