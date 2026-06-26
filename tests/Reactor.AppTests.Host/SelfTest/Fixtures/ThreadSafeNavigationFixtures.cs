using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost tests for issue #234: <c>NavigationHandle&lt;TRoute&gt;</c> mutators
/// invoked off the UI thread must auto-marshal onto the captured dispatcher and
/// apply correctly. The unit tests in <c>ThreadSafeNavigationTests</c> only prove
/// off-thread <em>rejection</em> when no dispatcher exists; these fixtures drive a
/// real pumped WinUI <c>DispatcherQueue</c> and assert the happy path end-to-end —
/// the store ends in the right state and the component actually re-renders.
/// </summary>
internal static class ThreadSafeNavigationFixtures
{
    private enum NavRoute { Home, Detail, Settings }

    /// <summary>
    /// Mounts a component with <c>UseNavigation</c>, then calls <c>Navigate</c> and
    /// <c>Replace</c> from a background <c>Task.Run</c>. Verifies the navigation
    /// marshals onto the UI thread, the back/forward stacks end in the right state,
    /// and the bound component re-renders to reflect the new route.
    /// </summary>
    internal class NavigateOffThreadMarshals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            NavigationHandle<NavRoute>? nav = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var handle = ctx.UseNavigation(NavRoute.Home);
                nav = handle;
                return TextBlock($"Route: {handle.CurrentRoute}");
            });

            await Harness.Render();
            H.Check("NavMarshal_Initial", H.FindText("Route: Home") is not null);

            // Navigate from a background thread — the exact #234 scenario that used
            // to mutate the List<T> backing store off-thread with no protection.
            var done = new TaskCompletionSource();
            _ = Task.Run(() =>
            {
                try
                {
                    nav!.Navigate(NavRoute.Detail);
                    done.TrySetResult();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Forward ANY recoverable failure (not only the marshal's InvalidOperationException)
                    // so an unexpected exception surfaces on `await done.Task` with its real stack trace
                    // instead of hanging the fixture until the 10s timeout. The filter excludes only
                    // process-fatal exceptions, matching the house style in src/Reactor.
                    done.TrySetException(ex);
                }
            });

            var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            H.Check("NavMarshal_NavigateCompleted", winner == done.Task);
            if (winner == done.Task) await done.Task; // surface any captured exception

            // Drain the marshaled mutation + the rerender it requests.
            for (int i = 0; i < 4; i++) await Harness.Render();

            // The store ended in the right state: current advanced, Home pushed.
            H.Check("NavMarshal_NavigateCurrent", nav!.CurrentRoute.Equals(NavRoute.Detail));
            H.Check("NavMarshal_NavigateBackStack",
                nav!.BackStack.Count == 1 && nav.BackStack[0].Equals(NavRoute.Home));
            // ...and the rerender actually fired (not just the store mutated).
            H.Check("NavMarshal_NavigateRerendered", H.FindText("Route: Detail") is not null);

            // Replace from a background thread — a second mutator end-to-end.
            var done2 = new TaskCompletionSource();
            _ = Task.Run(() =>
            {
                try
                {
                    nav!.Replace(NavRoute.Settings);
                    done2.TrySetResult();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Surface any recoverable failure immediately (see note above).
                    done2.TrySetException(ex);
                }
            });

            var winner2 = await Task.WhenAny(done2.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            H.Check("NavMarshal_ReplaceCompleted", winner2 == done2.Task);
            if (winner2 == done2.Task) await done2.Task;

            for (int i = 0; i < 4; i++) await Harness.Render();

            // Replace swaps current without growing the back stack.
            H.Check("NavMarshal_ReplaceCurrent", nav!.CurrentRoute.Equals(NavRoute.Settings));
            H.Check("NavMarshal_ReplaceBackStack",
                nav!.BackStack.Count == 1 && nav.BackStack[0].Equals(NavRoute.Home));
            H.Check("NavMarshal_ReplaceRerendered", H.FindText("Route: Settings") is not null);
        }
    }

    /// <summary>
    /// Companion to <see cref="NavigateOffThreadMarshals"/> that drives the <em>remaining</em>
    /// thread-safe mutators &#8212; <c>GoBack</c>, <c>GoForward</c>, <c>PopTo</c>, <c>Reset</c>,
    /// and <c>SetState</c> &#8212; off the UI thread under a real pumped <c>DispatcherQueue</c>.
    /// Each one's marshal gate is mechanically identical, but proving the happy path
    /// end-to-end (store mutates + component re-renders) for every mutator closes the
    /// coverage gap left by only exercising Navigate/Replace.
    /// </summary>
    internal class MutatorsOffThreadMarshal(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            NavigationHandle<NavRoute>? nav = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var handle = ctx.UseNavigation(NavRoute.Home);
                nav = handle;
                return TextBlock($"Route: {handle.CurrentRoute}");
            });

            await Harness.Render();

            // Build a back stack on the UI thread: Home -> Detail -> Settings.
            nav!.Navigate(NavRoute.Detail);
            nav!.Navigate(NavRoute.Settings);
            for (int i = 0; i < 2; i++) await Harness.Render();
            H.Check("NavMutators_Setup",
                nav!.CurrentRoute.Equals(NavRoute.Settings) && nav.BackStack.Count == 2);

            // GoBack off-thread: current rewinds to Detail, Settings moves to forward stack.
            await RunOffThread("GoBack", () => nav!.GoBack());
            H.Check("NavMutators_GoBackCurrent", nav!.CurrentRoute.Equals(NavRoute.Detail));
            H.Check("NavMutators_GoBackForward", nav!.CanGoForward);
            H.Check("NavMutators_GoBackRerendered", H.FindText("Route: Detail") is not null);

            // GoForward off-thread: current advances back to Settings.
            await RunOffThread("GoForward", () => nav!.GoForward());
            H.Check("NavMutators_GoForwardCurrent", nav!.CurrentRoute.Equals(NavRoute.Settings));
            H.Check("NavMutators_GoForwardRerendered", H.FindText("Route: Settings") is not null);

            // PopTo off-thread: pop back to Home, draining the back stack.
            await RunOffThread("PopTo", () => nav!.PopTo(r => r == NavRoute.Home));
            H.Check("NavMutators_PopToCurrent", nav!.CurrentRoute.Equals(NavRoute.Home));
            H.Check("NavMutators_PopToBackEmpty", nav!.BackStack.Count == 0);
            H.Check("NavMutators_PopToRerendered", H.FindText("Route: Home") is not null);

            // Rebuild a back entry, then Reset off-thread: single root, both stacks cleared.
            nav!.Navigate(NavRoute.Detail);
            await Harness.Render();
            await RunOffThread("Reset", () => nav!.Reset(NavRoute.Settings));
            H.Check("NavMutators_ResetCurrent", nav!.CurrentRoute.Equals(NavRoute.Settings));
            H.Check("NavMutators_ResetCleared", nav!.BackStack.Count == 0 && !nav.CanGoForward);
            H.Check("NavMutators_ResetRerendered", H.FindText("Route: Settings") is not null);

            // SetState off-thread: restore a captured snapshot wholesale.
            var snapshot = new NavigationState<NavRoute>(
                BackStack: new[] { NavRoute.Home },
                Current: NavRoute.Detail,
                ForwardStack: new[] { NavRoute.Settings });
            await RunOffThread("SetState", () => nav!.SetState(snapshot));
            H.Check("NavMutators_SetStateCurrent", nav!.CurrentRoute.Equals(NavRoute.Detail));
            H.Check("NavMutators_SetStateBackStack",
                nav!.BackStack.Count == 1 && nav.BackStack[0].Equals(NavRoute.Home));
            H.Check("NavMutators_SetStateForwardStack",
                nav!.ForwardStack.Count == 1 && nav.ForwardStack[0].Equals(NavRoute.Settings));
            H.Check("NavMutators_SetStateRerendered", H.FindText("Route: Detail") is not null);
        }

        // Runs a mutator from a background Task.Run, waits for it to be accepted (the
        // off-thread call returns once the work is scheduled), then pumps the dispatcher
        // so the marshaled mutation + the rerender it requests actually apply.
        private async Task RunOffThread(string label, Action mutate)
        {
            var done = new TaskCompletionSource();
            _ = Task.Run(() =>
            {
                try
                {
                    mutate();
                    done.TrySetResult();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Forward ANY recoverable failure so an unexpected exception (e.g. a setup
                    // regression) surfaces on `await done.Task` instead of hanging to the 10s
                    // timeout. The filter excludes only process-fatal exceptions (house style).
                    done.TrySetException(ex);
                }
            });

            var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            H.Check($"NavMutators_{label}Completed", winner == done.Task);
            if (winner == done.Task) await done.Task; // surface any captured exception

            // Drain the marshaled mutation + the rerender it requests.
            for (int i = 0; i < 4; i++) await Harness.Render();
        }
    }

    /// <summary>
    /// Regression for the off-thread <c>SetState</c> snapshot-aliasing race (issue #234
    /// review): <c>NavigationState&lt;TRoute&gt;</c> accepts arbitrary <c>IReadOnlyList</c>
    /// stacks, so a caller can hand in a live <c>List&lt;T&gt;</c>. The off-thread call
    /// marshals the restore onto the dispatcher and returns immediately; if the caller then
    /// mutates that original list before the dispatcher runs, the applied history must still
    /// match the snapshot validated at call time — <c>SetState</c> freezes the stacks into
    /// arrays before the hop. This fixture corrupts the caller's lists inside the marshal
    /// window and asserts the restored state is the call-time snapshot, not the corruption.
    /// </summary>
    internal class SetStateOffThreadFreezesSnapshot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            NavigationHandle<NavRoute>? nav = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var handle = ctx.UseNavigation(NavRoute.Home);
                nav = handle;
                return TextBlock($"Route: {handle.CurrentRoute}");
            });

            await Harness.Render();

            // Caller-owned MUTABLE lists handed to SetState as the snapshot's stacks.
            var backStack = new List<NavRoute> { NavRoute.Home };
            var forwardStack = new List<NavRoute> { NavRoute.Settings };
            var snapshot = new NavigationState<NavRoute>(
                BackStack: backStack, Current: NavRoute.Detail, ForwardStack: forwardStack);

            // Off the UI thread: call SetState (which marshals the restore), then immediately
            // corrupt the caller's lists BEFORE the dispatcher applies it. The freeze happens
            // synchronously inside SetState, so these mutations must not leak into the restore.
            var done = new TaskCompletionSource();
            _ = Task.Run(() =>
            {
                try
                {
                    nav!.SetState(snapshot);
                    backStack.Clear();
                    backStack.Add(NavRoute.Settings);
                    forwardStack.Clear();
                    done.TrySetResult();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    done.TrySetException(ex);
                }
            });

            var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            H.Check("NavFreeze_Completed", winner == done.Task);
            if (winner == done.Task) await done.Task;

            // Drain the marshaled restore + the rerender it requests.
            for (int i = 0; i < 4; i++) await Harness.Render();

            // The applied history matches the call-time snapshot, not the corrupted lists.
            H.Check("NavFreeze_Current", nav!.CurrentRoute.Equals(NavRoute.Detail));
            H.Check("NavFreeze_BackStack",
                nav!.BackStack.Count == 1 && nav.BackStack[0].Equals(NavRoute.Home));
            H.Check("NavFreeze_ForwardStack",
                nav!.ForwardStack.Count == 1 && nav.ForwardStack[0].Equals(NavRoute.Settings));
            H.Check("NavFreeze_Rerendered", H.FindText("Route: Detail") is not null);
        }
    }
}
