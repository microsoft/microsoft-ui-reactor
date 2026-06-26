using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

#pragma warning disable xUnit1031 // These tests deliberately use blocking (.Result/.Wait/WaitAll)
                                  // to drive UI-thread + background-thread interaction patterns.

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Cross-thread tests for <see cref="NavigationHandle{TRoute}"/> mutators (issue #234).
///
/// Mirrors the <c>UseState</c>/<c>UseReducer</c> cross-thread tests added for #212:
/// the navigation hook is now thread-safe by default. On the UI thread the mutators
/// behave exactly as before; off-thread they auto-marshal onto the captured UI
/// dispatcher, and when no dispatcher is available (this headless unit-test context)
/// they throw a loud, actionable exception INSTEAD of corrupting the back/forward
/// stacks or silently dropping the navigation.
/// </summary>
public class ThreadSafeNavigationTests
{
    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;

    private static NavigationHandle<Route> MakeHandle(Route initial)
    {
        // Build the handle through the real hook so its captured UI thread id is the
        // render thread — exactly how a component obtains it via UseNavigation.
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx.UseNavigation<Route>(initial);
    }

    private static InvalidOperationException AssertOffThreadThrows(Action call)
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Run(call, TestContext.Current.CancellationToken);
        }).Result;
        // The message names the operation and the missing-dispatcher remedy.
        Assert.Contains("NavigationHandle.", ex.Message);
        Assert.Contains("no UI dispatcher", ex.Message);
        return ex;
    }

    // ════════════════════════════════════════════════════════════════
    //  Off-thread, no dispatcher: each mutator throws loudly (#234)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Navigate_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());

        AssertOffThreadThrows(() => nav.Navigate(new Detail(1)));

        // No partial mutation: the stack is exactly as it started.
        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.Equal(1, nav.Depth);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void GoBack_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());
        nav.Navigate(new Detail(1)); // on UI thread — sets up a back entry
        Assert.True(nav.CanGoBack);

        AssertOffThreadThrows(() => nav.GoBack());

        Assert.IsType<Detail>(nav.CurrentRoute);
        Assert.True(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
        Assert.Equal(2, nav.Depth);
    }

    [Fact]
    public void GoForward_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());
        nav.Navigate(new Detail(1));
        nav.GoBack(); // now CanGoForward
        Assert.True(nav.CanGoForward);

        AssertOffThreadThrows(() => nav.GoForward());

        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.True(nav.CanGoForward);
    }

    [Fact]
    public void Replace_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());

        AssertOffThreadThrows(() => nav.Replace(new Settings()));

        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.Equal(1, nav.Depth);
    }

    [Fact]
    public void Reset_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());
        nav.Navigate(new Detail(1));
        nav.Navigate(new Detail(2));

        AssertOffThreadThrows(() => nav.Reset(new Settings()));

        Assert.IsType<Detail>(nav.CurrentRoute);
        Assert.Equal(new Detail(2), nav.CurrentRoute);
        Assert.Equal(3, nav.Depth);
    }

    [Fact]
    public void PopTo_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());
        nav.Navigate(new Detail(1));
        nav.Navigate(new Detail(2));

        AssertOffThreadThrows(() => nav.PopTo(r => r is Home));

        Assert.Equal(new Detail(2), nav.CurrentRoute);
        Assert.Equal(3, nav.Depth);
    }

    [Fact]
    public void SetState_OffThread_NoDispatcher_Throws_And_Leaves_Stack_Untouched()
    {
        var nav = MakeHandle(new Home());
        var snapshot = new NavigationState<Route>(
            BackStack: new Route[] { new Home(), new Detail(1) },
            Current: new Detail(2),
            ForwardStack: Array.Empty<Route>());

        AssertOffThreadThrows(() => nav.SetState(snapshot));

        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.Equal(1, nav.Depth);
    }

    // ════════════════════════════════════════════════════════════════
    //  UI thread: behavior is unchanged (the gate is a no-op fast path)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mutators_On_UI_Thread_Behave_Normally()
    {
        var nav = MakeHandle(new Home());

        Assert.True(nav.Navigate(new Detail(1)));
        Assert.Equal(new Detail(1), nav.CurrentRoute);

        Assert.True(nav.Replace(new Detail(2)));
        Assert.Equal(new Detail(2), nav.CurrentRoute);

        Assert.True(nav.GoBack());
        Assert.IsType<Home>(nav.CurrentRoute);

        Assert.True(nav.GoForward());
        Assert.Equal(new Detail(2), nav.CurrentRoute);

        Assert.True(nav.Reset(new Settings()));
        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void Navigate_On_UI_Thread_Fires_Rerender()
    {
        var ctx = new RenderContext();
        int rerenders = 0;
        ctx.BeginRender(() => rerenders++);
        var nav = ctx.UseNavigation<Route>(new Home());

        nav.Navigate(new Detail(1));

        Assert.True(rerenders >= 1);
        Assert.Equal(new Detail(1), nav.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  Concurrency: many off-thread writers cannot corrupt the stack
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Concurrent_OffThread_Writers_All_Rejected_No_Corruption()
    {
        var nav = MakeHandle(new Home());

        // 32 background writers all attempt to mutate concurrently. With no
        // dispatcher every attempt must throw at the gate BEFORE touching the
        // stack — so none of them can interleave a List<T> mutation.
        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                switch (i % 4)
                {
                    case 0: nav.Navigate(new Detail(i)); break;
                    case 1: nav.Replace(new Detail(i)); break;
                    case 2: nav.Reset(new Detail(i)); break;
                    default: nav.GoBack(); break;
                }
            });
        }, TestContext.Current.CancellationToken)).ToArray();

        Task.WaitAll(tasks, TestContext.Current.CancellationToken);

        // The stack is pristine — exactly the initial single-entry Home stack.
        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.Equal(1, nav.Depth);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    // ════════════════════════════════════════════════════════════════
    //  SetState validates the snapshot shape up front (M7 fail-fast)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetState_OffThread_NullCurrent_Throws_ArgumentException_Synchronously()
    {
        var nav = MakeHandle(new Home());
        var bad = new NavigationState<Route>(
            BackStack: Array.Empty<Route>(),
            Current: null!,
            ForwardStack: Array.Empty<Route>());

        // The Current-null check runs BEFORE the marshal gate, so an off-thread caller
        // gets an ArgumentException at the call site — NOT an InvalidOperationException
        // raised later on the dispatcher (which the caller could never observe), and
        // NOT a swallowed marshal. ArgumentException must win the race against the gate.
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await Task.Run(() => nav.SetState(bad), TestContext.Current.CancellationToken);
        }).Result;
        Assert.IsNotType<InvalidOperationException>(ex);
        Assert.Equal("state", ex.ParamName);
    }

    [Fact]
    public void SetState_OffThread_NullBackStack_Throws_ArgumentException_Synchronously()
    {
        var nav = MakeHandle(new Home());
        var bad = new NavigationState<Route>(
            BackStack: null!,
            Current: new Home(),
            ForwardStack: Array.Empty<Route>());

        // Like the Current check, BackStack is validated BEFORE the marshal gate so a null
        // list fails fast at the call site rather than throwing later inside RestoreState's
        // AddRange on the UI dispatcher, where an off-thread caller could never observe it.
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await Task.Run(() => nav.SetState(bad), TestContext.Current.CancellationToken);
        }).Result;
        Assert.IsNotType<InvalidOperationException>(ex);
        Assert.Equal("state", ex.ParamName);
    }

    [Fact]
    public void SetState_OffThread_NullForwardStack_Throws_ArgumentException_Synchronously()
    {
        var nav = MakeHandle(new Home());
        var bad = new NavigationState<Route>(
            BackStack: Array.Empty<Route>(),
            Current: new Home(),
            ForwardStack: null!);

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await Task.Run(() => nav.SetState(bad), TestContext.Current.CancellationToken);
        }).Result;
        Assert.IsNotType<InvalidOperationException>(ex);
        Assert.Equal("state", ex.ParamName);
    }

    [Fact]
    public void EnqueueOrThrow_NullDispatcher_Throws_NoDispatcher_Message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            UIThreadMarshal.EnqueueOrThrow(
                tryEnqueue: null,
                work: () => { },
                onNoDispatcher: () => "NO-DISPATCHER",
                onRefused: () => "REFUSED"));

        Assert.Equal("NO-DISPATCHER", ex.Message);
    }

    [Fact]
    public void EnqueueOrThrow_DispatcherRefuses_Throws_Refused_Message()
    {
        // tryEnqueue returns false — the dispatcher-shutting-down branch that was
        // previously unreachable from a unit test (only the null-dispatcher branch
        // was hit). This covers the TryEnqueue == false failure mode end-to-end.
        bool enqueueCalled = false;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            UIThreadMarshal.EnqueueOrThrow(
                tryEnqueue: _ => { enqueueCalled = true; return false; },
                work: () => { },
                onNoDispatcher: () => "NO-DISPATCHER",
                onRefused: () => "REFUSED"));

        Assert.True(enqueueCalled);
        Assert.Equal("REFUSED", ex.Message);
    }

    [Fact]
    public void EnqueueOrThrow_DispatcherAccepts_Returns_True_And_Posts_Work()
    {
        Action? posted = null;
        var work = new Action(() => { });

        bool result = UIThreadMarshal.EnqueueOrThrow(
            tryEnqueue: w => { posted = w; return true; },
            work: work,
            onNoDispatcher: () => "NO-DISPATCHER",
            onRefused: () => "REFUSED");

        Assert.True(result);
        Assert.Same(work, posted); // the exact work delegate is handed to the dispatcher
    }
}
