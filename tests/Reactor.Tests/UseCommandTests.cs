using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for UseCommand hook — sync passthrough, async lifecycle, re-entrance guards.
/// </summary>
[Collection("UnobservedTaskException")]
public class UseCommandTests
{
    private static RenderContext CreateContext()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    private static void Rerender(RenderContext ctx)
    {
        ctx.BeginRender(() => { });
    }

    // ════════════════════════════════════════════════════════════════
    //  Sync passthrough
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Sync_Command_Passes_Through_Unchanged()
    {
        var ctx = CreateContext();
        var original = new Command { Label = "Cut", Execute = () => { } };

        var result = ctx.UseCommand(original);

        Assert.Same(original, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  Async command wrapping
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Async_Command_Returns_With_Execute_Set_And_ExecuteAsync_Null()
    {
        var ctx = CreateContext();
        var cmd = new Command { Label = "Save", ExecuteAsync = () => Task.CompletedTask };

        var result = ctx.UseCommand(cmd);

        Assert.NotNull(result.Execute);
        Assert.Null(result.ExecuteAsync);
    }

    [Fact]
    public void IsExecuting_Is_False_Initially()
    {
        var ctx = CreateContext();
        var cmd = new Command { Label = "Save", ExecuteAsync = () => Task.CompletedTask };

        var result = ctx.UseCommand(cmd);

        Assert.False(result.IsExecuting);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public async Task IsExecuting_Becomes_True_During_Execution()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateChanged = new SemaphoreSlim(0);
        var ctx = new RenderContext();
        ctx.BeginRender(() => stateChanged.Release());
        var cmd = new Command
        {
            Label = "Save",
            ExecuteAsync = async () =>
            {
                started.SetResult();
                await tcs.Task;
            }
        };

        var result = ctx.UseCommand(cmd);
        result.Execute!();
        // setIsExecuting(true) fires synchronously → 1st release

        await started.Task;
        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5)); // drain 1st release

        // Re-render to observe state (preserve callback for the next release)
        ctx.BeginRender(() => stateChanged.Release());
        var result2 = ctx.UseCommand(cmd);
        Assert.True(result2.IsExecuting);
        Assert.False(result2.IsEnabled);

        // Complete the task; finally block calls setIsExecuting(false) → 2nd release
        tcs.SetResult();
        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5));

        // Re-render to observe completion
        Rerender(ctx);
        var result3 = ctx.UseCommand(cmd);
        Assert.False(result3.IsExecuting);
        Assert.True(result3.IsEnabled);
    }

    [Fact]
    [Trait("Category", "Threading")]
    public async Task Error_In_ExecuteAsync_Still_Resets_IsExecuting()
    {
        // Use a semaphore to observe re-render requests from setIsExecuting.
        // Task.Yield() doesn't reliably interleave with thread pool work items
        // under xUnit's synchronization context.
        int unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception.InnerExceptions.Any(ex =>
                ex is InvalidOperationException { Message: "test error" }))
            {
                Interlocked.Increment(ref unobserved);
                e.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += handler;
        var stateChanged = new SemaphoreSlim(0);
        try
        {
            var ctx = new RenderContext();
            ctx.BeginRender(() => stateChanged.Release());
            var cmd = new Command
            {
                Label = "Save",
                ExecuteAsync = () => throw new InvalidOperationException("test error")
            };

            var result = ctx.UseCommand(cmd);
            result.Execute!();

            // Execute! synchronously sets IsExecuting=true (1st release), then Task.Run
            // lets the error fault the background task while still resetting
            // IsExecuting=false in finally (2nd release).
            await stateChanged.WaitAsync(TimeSpan.FromSeconds(5));
            await stateChanged.WaitAsync(TimeSpan.FromSeconds(5));

            Rerender(ctx);
            var result2 = ctx.UseCommand(cmd);
            Assert.False(result2.IsExecuting);

            for (int i = 0; i < 10 && Volatile.Read(ref unobserved) == 0; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10);
            }
            Assert.Equal(1, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
            stateChanged.Dispose();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Parameterized UseCommand<T>
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Sync_Parameterized_Command_Passes_Through()
    {
        var ctx = CreateContext();
        var original = new Command<string> { Label = "Delete", Execute = _ => { } };

        var result = ctx.UseCommand(original);

        Assert.Same(original, result);
    }

    [Fact]
    public async Task Parameterized_Async_Passes_Argument_Through()
    {
        string? received = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateChanged = new SemaphoreSlim(0);
        var ctx = new RenderContext();
        ctx.BeginRender(() => stateChanged.Release());
        var cmd = new Command<string>
        {
            Label = "Delete",
            ExecuteAsync = async arg =>
            {
                received = arg;
                started.SetResult();
                await tcs.Task;
            }
        };

        var result = ctx.UseCommand(cmd);
        result.Execute!("item-42");
        // setIsExecuting(true) fires synchronously → 1st release

        await started.Task;
        Assert.Equal("item-42", received);

        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5)); // drain 1st release

        // Re-render to observe IsExecuting=true (preserve callback for the next release)
        ctx.BeginRender(() => stateChanged.Release());
        var result2 = ctx.UseCommand(cmd);
        Assert.True(result2.IsExecuting);

        // Complete the task; finally block calls setIsExecuting(false) → 2nd release
        tcs.SetResult();
        await stateChanged.WaitAsync(TimeSpan.FromSeconds(5));

        Rerender(ctx);
        var result3 = ctx.UseCommand(cmd);
        Assert.False(result3.IsExecuting);
    }
}
