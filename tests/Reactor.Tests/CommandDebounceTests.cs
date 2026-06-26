using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the leading-edge <see cref="Command.DebounceMs"/> debounce realized by
/// <see cref="RenderContext.UseCommand(Command)"/> (issue #136). Timing is driven by an
/// injected <see cref="FakeTimeProvider"/> so the window assertions aren't wall-clock-flaky.
/// </summary>
[Collection("UnobservedTaskException")]
public class CommandDebounceTests
{
    private static RenderContext CreateContext(FakeTimeProvider time)
    {
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => { });
        return ctx;
    }

    private static void Rerender(RenderContext ctx)
    {
        ctx.BeginRender(() => { });
    }

    // ════════════════════════════════════════════════════════════════
    //  (a) second invoke within the window is dropped
    //  (b) an invoke after the window elapses is accepted
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Second_Invoke_Within_Window_Is_Dropped_Then_Accepted_After()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        int fires = 0;
        var cmd = new Command { Label = "Run", Execute = () => fires++, DebounceMs = 1500 };

        var result = ctx.UseCommand(cmd);

        result.Execute!();                 // accepted
        Assert.Equal(1, fires);

        result.Execute!();                 // within window → dropped
        time.Advance(TimeSpan.FromMilliseconds(500));
        result.Execute!();                 // still within window → dropped
        Assert.Equal(1, fires);

        time.Advance(TimeSpan.FromMilliseconds(1000)); // window (1500ms) elapses → timer clears it
        result.Execute!();                 // accepted again
        Assert.Equal(2, fires);
    }

    // ════════════════════════════════════════════════════════════════
    //  (c) IsEnabled is false during the window and true after
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsEnabled_Is_False_During_Window_And_True_After()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        var cmd = new Command { Label = "Run", Execute = () => { }, DebounceMs = 1000 };

        var result = ctx.UseCommand(cmd);
        Assert.True(result.IsEnabled);
        Assert.False(result.IsDebouncing);

        result.Execute!();

        // Re-render to observe the debouncing state flowing through.
        Rerender(ctx);
        var during = ctx.UseCommand(cmd);
        Assert.True(during.IsDebouncing);
        Assert.False(during.IsEnabled);

        // Not yet elapsed — still disabled.
        time.Advance(TimeSpan.FromMilliseconds(999));
        Rerender(ctx);
        var stillDuring = ctx.UseCommand(cmd);
        Assert.False(stillDuring.IsEnabled);

        // Window elapses → timer fires → re-enabled.
        time.Advance(TimeSpan.FromMilliseconds(1));
        Rerender(ctx);
        var after = ctx.UseCommand(cmd);
        Assert.False(after.IsDebouncing);
        Assert.True(after.IsEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  (d) async commands keep DebounceMs extending the window past lambda return
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Async_Command_DebounceMs_Extends_Window_Past_Lambda_Return()
    {
        var time = new FakeTimeProvider();
        using var stateChanged = new SemaphoreSlim(0);
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => stateChanged.Release());

        var cmd = new Command
        {
            Label = "Re-gen",
            ExecuteAsync = () => Task.CompletedTask, // returns immediately
            DebounceMs = 250,
        };

        var result = ctx.UseCommand(cmd);
        result.Execute!();

        // The synchronous part sets IsDebouncing=true (1st release) and IsExecuting=true (2nd),
        // and the immediately-completing task resets IsExecuting=false (3rd release).
        for (int i = 0; i < 3; i++)
            await stateChanged.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Lambda already returned (IsExecuting back to false) but the debounce window holds
        // the command disabled.
        ctx.BeginRender(() => stateChanged.Release());
        var during = ctx.UseCommand(cmd);
        Assert.False(during.IsExecuting);
        Assert.True(during.IsDebouncing);
        Assert.False(during.IsEnabled);

        // Elapse the debounce window → re-enabled.
        time.Advance(TimeSpan.FromMilliseconds(250));
        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Rerender(ctx);
        var after = ctx.UseCommand(cmd);
        Assert.False(after.IsDebouncing);
        Assert.True(after.IsEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  (e) DebounceMs = 0 preserves today's behavior exactly
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DebounceMs_Zero_Sync_Command_Passes_Through_Unchanged()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        var original = new Command { Label = "Cut", Execute = () => { } }; // DebounceMs defaults to 0

        var result = ctx.UseCommand(original);

        Assert.Same(original, result);
    }

    [Fact]
    public void DebounceMs_Zero_Sync_Command_Never_Disables()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        int fires = 0;
        var cmd = new Command { Label = "Cut", Execute = () => fires++, DebounceMs = 0 };

        var result = ctx.UseCommand(cmd);

        result.Execute!();
        result.Execute!();
        result.Execute!();
        Assert.Equal(3, fires);          // no fire is dropped
        Assert.True(result.IsEnabled);   // never disables
    }

    [Fact]
    public void Default_DebounceMs_Is_Zero()
    {
        var cmd = new Command { Label = "x", Execute = () => { } };
        Assert.Equal(0, cmd.DebounceMs);
        Assert.False(cmd.IsDebouncing);
    }

    // ════════════════════════════════════════════════════════════════
    //  Parameterized command debounce
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parameterized_Sync_Command_Debounces()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        var args = new List<string>();
        var cmd = new Command<string> { Label = "Delete", Execute = args.Add, DebounceMs = 500 };

        var result = ctx.UseCommand(cmd);

        result.Execute!("a");            // accepted
        result.Execute!("b");            // dropped (within window)
        Assert.Equal(new[] { "a" }, args);

        time.Advance(TimeSpan.FromMilliseconds(500));
        result.Execute!("c");            // accepted
        Assert.Equal(new[] { "a", "c" }, args);
    }

    // ════════════════════════════════════════════════════════════════
    //  Parameterized async + debounce (L3)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Parameterized_Async_Command_Debounces_And_Forwards_Arg()
    {
        var time = new FakeTimeProvider();
        using var stateChanged = new SemaphoreSlim(0);
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => stateChanged.Release());

        var seen = new List<string>();
        var cmd = new Command<string>
        {
            Label = "Delete",
            ExecuteAsync = arg => { lock (seen) seen.Add(arg); return Task.CompletedTask; },
            DebounceMs = 300,
        };

        var result = ctx.UseCommand(cmd);

        result.Execute!("a");   // accepted, forwards "a"
        // Releases: IsDebouncing=true, IsExecuting=true, then IsExecuting=false (task completes).
        for (int i = 0; i < 3; i++)
            await stateChanged.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        result.Execute!("b");   // within window → dropped (guard already cleared, window holds)
        lock (seen) Assert.Equal(new[] { "a" }, seen);

        ctx.BeginRender(() => stateChanged.Release());
        var during = ctx.UseCommand(cmd);
        Assert.True(during.IsDebouncing);
        Assert.False(during.IsEnabled);

        time.Advance(TimeSpan.FromMilliseconds(300));   // window elapses → IsDebouncing=false
        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        result.Execute!("c");   // accepted, forwards "c"
        for (int i = 0; i < 3; i++)
            await stateChanged.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        lock (seen) Assert.Equal(new[] { "a", "c" }, seen);
    }

    // ════════════════════════════════════════════════════════════════
    //  Long-running async: window elapses but the lambda is still running, so a
    //  second fire is dropped by the re-entrance guard (M5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LongRunning_Async_Second_Fire_After_Window_Is_Dropped_By_Guard()
    {
        var time = new FakeTimeProvider();
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => { });

        int runs = 0;
        using var started = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        var cmd = new Command
        {
            Label = "x",
            ExecuteAsync = async () => { Interlocked.Increment(ref runs); started.Release(); await release.Task; },
            DebounceMs = 100,
        };

        var result = ctx.UseCommand(cmd);

        result.Execute!();
        await started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref runs));

        // Debounce window elapses, but the async lambda is still running → the command stays
        // disabled via the re-entrance guard and a fresh fire is dropped, not re-armed.
        time.Advance(TimeSpan.FromMilliseconds(100));
        result.Execute!();
        Assert.Equal(1, Volatile.Read(ref runs));

        // Let the lambda finish; the guard clears and a subsequent fire is accepted.
        release.SetResult();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (Volatile.Read(ref runs) >= 2) break;
            result.Execute!();
            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, Volatile.Read(ref runs));
    }

    // ════════════════════════════════════════════════════════════════
    //  Stable hook shape (H1): a command at one call site can flip
    //  sync↔async and DebounceMs 0↔N across renders without reordering hooks
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseCommand_Consumes_Stable_Hook_Shape_Across_Command_Shape_Changes()
    {
        var time = new FakeTimeProvider();
        var ctx = new RenderContext { TimeProvider = time };

        // Render 1: pure sync, no debounce. A UseState AFTER UseCommand lands at some slot.
        ctx.BeginRender(() => { });
        ctx.UseCommand(new Command { Label = "x", Execute = () => { } });
        var (v1, set1) = ctx.UseState(42);
        Assert.Equal(42, v1);
        set1(99);

        // Render 2: async + debounce at the SAME call site. If UseCommand consumed a different
        // number of hook slots, the trailing UseState would shift and throw or misbind.
        ctx.BeginRender(() => { });
        ctx.UseCommand(new Command { Label = "x", ExecuteAsync = () => Task.CompletedTask, DebounceMs = 250 });
        var (v2, _) = ctx.UseState(42);
        Assert.Equal(99, v2);   // state preserved → slots didn't move

        // Render 3: back to pure sync, no debounce.
        ctx.BeginRender(() => { });
        ctx.UseCommand(new Command { Label = "x", Execute = () => { } });
        var (v3, _) = ctx.UseState(42);
        Assert.Equal(99, v3);
    }

    // ════════════════════════════════════════════════════════════════
    //  Timer lifecycle (M7 / M8): the re-enable timer is disposed on unmount
    //  and re-arming a window never leaks timers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Debounce_Timer_Is_Disposed_On_Unmount()
    {
        var time = new FakeTimeProvider();
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => { });
        var cmd = new Command { Label = "x", Execute = () => { }, DebounceMs = 1000 };

        var result = ctx.UseCommand(cmd);
        ctx.FlushEffects();          // register the unmount cleanup effect

        result.Execute!();           // arms a window → one live timer
        Assert.Equal(1, time.ActiveTimerCount);

        ctx.RunCleanups();           // unmount → cleanup disposes the live timer
        Assert.Equal(0, time.ActiveTimerCount);
    }

    [Fact]
    public void Reentering_Window_Does_Not_Leak_Timers()
    {
        var time = new FakeTimeProvider();
        var ctx = new RenderContext { TimeProvider = time };
        ctx.BeginRender(() => { });
        var cmd = new Command { Label = "x", Execute = () => { }, DebounceMs = 100 };

        var result = ctx.UseCommand(cmd);
        ctx.FlushEffects();

        for (int i = 0; i < 5; i++)
        {
            result.Execute!();                              // arm window i
            Assert.Equal(1, time.ActiveTimerCount);         // exactly one live timer
            time.Advance(TimeSpan.FromMilliseconds(100));   // window elapses → timer fires
            Assert.Equal(0, time.ActiveTimerCount);         // fired timer disposes itself, not retained
        }
        Assert.Equal(0, time.ActiveTimerCount);             // never accumulates

        ctx.RunCleanups();
        Assert.Equal(0, time.ActiveTimerCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  Time-based acceptance: a fire after DebounceMs has elapsed is accepted
    //  even if the re-enable timer callback is delayed (threadpool starvation),
    //  honoring the fixed-duration semantics instead of dropping until the
    //  callback runs.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Fire_After_Deadline_Is_Accepted_Even_If_Timer_Callback_Is_Delayed()
    {
        var time = new FakeTimeProvider();
        var ctx = CreateContext(time);
        int fires = 0;
        var cmd = new Command { Label = "Run", Execute = () => fires++, DebounceMs = 1000 };

        var result = ctx.UseCommand(cmd);

        result.Execute!();                 // accepted, window armed for 1000ms
        Assert.Equal(1, fires);

        // Advance the clock PAST the deadline but DON'T let the re-enable timer fire (simulates the
        // callback being delayed under load). The window flag is still set, but it has logically
        // expired — a fire now must be accepted, not dropped.
        time.AdvanceWithoutFiring(TimeSpan.FromMilliseconds(1001));
        result.Execute!();
        Assert.Equal(2, fires);

        // And a fire still inside the freshly re-armed window is dropped as usual.
        time.AdvanceWithoutFiring(TimeSpan.FromMilliseconds(500));
        result.Execute!();
        Assert.Equal(2, fires);
    }
}
