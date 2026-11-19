using Microsoft.UI.Reactor.Core;
using WctControls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class CameraPreviewLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupQueue_OnlyBlocksReentryWhenReleaseFails(bool failCleanup)
    {
        var queue = new CameraPreviewCleanupQueue();
        var reports = new List<string>();
        var previous = new CameraPreviewLifetime(Task.CompletedTask,
            () => Task.FromException(new InvalidOperationException("start")),
            () => { },
            () => failCleanup
                ? Task.FromException(new InvalidOperationException("cleanup"))
                : Task.CompletedTask,
            (operation, _) => reports.Add(operation));
        previous.Start();
        queue.Retire(previous);
        await previous.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(previous.HasErrors);
        Assert.Equal(!failCleanup, previous.ReleaseSucceeded);

        int starts = 0;
        var next = new CameraPreviewLifetime(queue.WaitAsync(),
            () => { starts++; return Task.CompletedTask; },
            () => { },
            () => Task.CompletedTask,
            (operation, _) => reports.Add(operation));
        next.Start();
        if (failCleanup)
        {
            await next.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("previous cleanup", reports);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queue.WaitAsync());
        }
        else
        {
            await next.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("previous cleanup", reports);
        }
        Assert.Equal(failCleanup ? 0 : 1, starts);
    }

    [Fact]
    public async Task QueuedSwitchContinuation_SettlesBeforeFinalStopAndUnsubscribe()
    {
        var events = new List<string>();
        bool playing = false;
        var started = new TaskCompletionSource();
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () => { playing = true; started.SetResult(); return Task.CompletedTask; },
            () => { playing = false; events.Add("stop"); },
            () => { events.Add("cleanup"); return Task.CompletedTask; },
            (operation, _) => events.Add(operation),
            () => events.Add("unsubscribe"),
            () => { playing = true; events.Add("switch"); return Task.CompletedTask; });
        lifetime.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["stop", "cleanup", "switch", "stop", "unsubscribe"], events);
        Assert.False(playing);
        Assert.True(lifetime.ReleaseSucceeded);
    }

    [Fact]
    public async Task SettleFailure_KeepsListenerAndBlocksReentry()
    {
        var queue = new CameraPreviewCleanupQueue();
        var events = new List<string>();
        var started = new TaskCompletionSource();
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () => { started.SetResult(); return Task.CompletedTask; },
            () => events.Add("stop"),
            () => Task.CompletedTask,
            (operation, _) => events.Add(operation),
            () => events.Add("unsubscribe"),
            () => Task.FromException(new InvalidOperationException("settle")));
        queue.Track(lifetime);
        lifetime.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.Retire(lifetime);
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["stop", "settle", "stop"], events);
        Assert.True(lifetime.HasErrors);
        Assert.False(lifetime.ReleaseSucceeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.WaitAsync());
    }

    [Fact]
    public async Task ReplacementMountedBeforePreviousRetires_WaitsForPreviousCleanup()
    {
        var queue = new CameraPreviewCleanupQueue();
        var events = new List<string>();
        var previousStarted = new TaskCompletionSource();
        var previous = new CameraPreviewLifetime(queue.WaitAsync(),
            () => { events.Add("previous start"); previousStarted.SetResult(); return Task.CompletedTask; },
            () => { },
            () => { events.Add("previous cleanup"); return Task.CompletedTask; },
            (operation, _) => events.Add(operation));
        queue.Track(previous);
        previous.Start();
        await previousStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var nextStarted = new TaskCompletionSource();
        var next = new CameraPreviewLifetime(queue.WaitAsync(),
            () => { events.Add("next start"); nextStarted.SetResult(); return Task.CompletedTask; },
            () => { },
            () => Task.CompletedTask,
            (operation, _) => events.Add(operation));
        queue.Track(next);
        next.Start();
        Assert.False(nextStarted.Task.IsCompleted);

        queue.Retire(previous);
        await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["previous start", "previous cleanup", "next start"], events);
        queue.Retire(next);
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateErrors_AreHandedToTheNextPreviewOnce(bool failCleanup)
    {
        var queue = new CameraPreviewCleanupQueue();
        var started = new TaskCompletionSource();
        var previous = new CameraPreviewLifetime(Task.CompletedTask,
            () => { started.SetResult(); return Task.CompletedTask; },
            () => { },
            () => failCleanup
                ? Task.FromException(new InvalidOperationException("cleanup"))
                : Task.CompletedTask,
            (operation, error) => queue.RecordLate($"{operation}: {error.Message}"));
        queue.Track(previous);
        previous.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.Retire(previous);
        queue.RecordLate("preview: NoFrameSourceGroupAvailable");
        await previous.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        if (failCleanup)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => queue.WaitAsync());
            Assert.Contains("Restart the sample", error.Message);
            Assert.Contains("Earlier errors:", error.Message);
            Assert.Contains("preview: NoFrameSourceGroupAvailable", error.Message);
            Assert.Contains("cleanup: cleanup", error.Message);
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => queue.WaitAsync());
            Assert.DoesNotContain("Earlier errors", again.Message);
        }
        else
        {
            Assert.Equal(["preview: NoFrameSourceGroupAvailable"], await queue.WaitAsync());
            Assert.Empty(await queue.WaitAsync());
        }
    }

    [Fact]
    public async Task UnsubscribeFailureAlone_BlocksReentry()
    {
        var queue = new CameraPreviewCleanupQueue();
        var reports = new List<string>();
        var started = new TaskCompletionSource();
        var previous = new CameraPreviewLifetime(Task.CompletedTask,
            () => { started.SetResult(); return Task.CompletedTask; },
            () => { },
            () => Task.CompletedTask,
            (operation, _) => reports.Add(operation),
            () => throw new InvalidOperationException("unsubscribe"));
        previous.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.Retire(previous);
        await previous.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["unsubscribe"], reports);
        Assert.False(previous.ReleaseSucceeded);

        int starts = 0;
        var next = new CameraPreviewLifetime(queue.WaitAsync(),
            () => { starts++; return Task.CompletedTask; },
            () => { },
            () => Task.CompletedTask,
            (operation, _) => reports.Add(operation));
        next.Start();
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, starts);
        Assert.Contains("previous cleanup", reports);
    }

    [Fact]
    public async Task FinalStopFailureAlone_BlocksReentry()
    {
        var queue = new CameraPreviewCleanupQueue();
        var reports = new List<string>();
        int stops = 0;
        var started = new TaskCompletionSource();
        var previous = new CameraPreviewLifetime(Task.CompletedTask,
            () => { started.SetResult(); return Task.CompletedTask; },
            () => { if (++stops == 2) throw new InvalidOperationException("final stop"); },
            () => Task.CompletedTask,
            (operation, _) => reports.Add(operation));
        previous.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        queue.Retire(previous);
        await previous.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, stops);
        Assert.Equal(["stop after cleanup"], reports);
        Assert.False(previous.ReleaseSucceeded);

        int starts = 0;
        var next = new CameraPreviewLifetime(queue.WaitAsync(),
            () => { starts++; return Task.CompletedTask; },
            () => { },
            () => Task.CompletedTask,
            (operation, _) => reports.Add(operation));
        next.Start();
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, starts);
        Assert.Contains("previous cleanup", reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageRetirement_StopsMountedPreviewOnceAndGivesReplacementANewKey(bool hotReload)
    {
        var context = new RenderContext();
        var errors = new List<string?>();
        var stopped = new List<object>();
        CameraPreviewPageLifetime<object> Render()
        {
            context.BeginRender(() => { });
            var lifetime = context.UseMemo(() => new CameraPreviewPageLifetime<object>(stopped.Add), Array.Empty<object>());
            context.UseEffect(() => lifetime.Retire, Array.Empty<object>());
            context.FlushEffects();
            return lifetime;
        }

        var first = Render();
        var control = new object();
        first.Attach(control);
        first.Report("live", errors.Add);
        Assert.Same(first, Render());
        Assert.Empty(stopped);
        if (hotReload)
            context.ResetForHotReload();
        else
        {
            context.RunCleanups();
            context = new RenderContext();
        }

        Assert.Equal([control], stopped);
        first.Retire();
        Assert.Single(stopped);
        var late = new object();
        first.Attach(late);
        Assert.Equal([control, late], stopped);

        var replacement = Render();
        Assert.NotEqual(first.Key, replacement.Key);
        first.Report("late", errors.Add);
        replacement.Report("replacement", errors.Add);
        Assert.Equal(["live", "replacement"], errors);
        context.RunCleanups();
    }

    [Fact]
    public async Task UnmountDuringStart_KeepsFailureListenerThroughCleanup()
    {
        var started = new TaskCompletionSource();
        var finishStart = new TaskCompletionSource();
        var cleaning = new TaskCompletionSource();
        var finishCleanup = new TaskCompletionSource();
        var errors = new List<string>();
        Action? failureEvent = null;
        Action? failureHandler = null;
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            async () => { started.SetResult(); await finishStart.Task; },
            () => { },
            async () => { cleaning.SetResult(); await finishCleanup.Task; },
            (_, error) => Assert.Fail(error.ToString()),
            () => failureEvent -= failureHandler);
        failureHandler = () => { errors.Add("CameraAccessDenied"); lifetime.RecordFailure(); };
        failureEvent += failureHandler;

        lifetime.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var completion = lifetime.StopAsync();
        Assert.False(completion.IsCompleted);
        failureEvent?.Invoke();
        Assert.Equal(["CameraAccessDenied"], errors);
        Assert.True(lifetime.HasErrors);

        finishStart.SetResult();
        await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        failureEvent?.Invoke();
        Assert.Equal(2, errors.Count);
        finishCleanup.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(failureEvent);
    }

    [Fact]
    public async Task UnmountDuringStart_WaitsThenCleansUpOnce()
    {
        var calls = new List<string>();
        var started = new TaskCompletionSource();
        var finishStart = new TaskCompletionSource();
        var finishCleanup = new TaskCompletionSource();
        var cleaning = new TaskCompletionSource();
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            async () =>
            {
                calls.Add("start");
                started.SetResult();
                await finishStart.Task;
                calls.Add("started");
            },
            () => calls.Add("stop"),
            async () =>
            {
                calls.Add("cleanup");
                cleaning.SetResult();
                await finishCleanup.Task;
            },
            (_, error) => Assert.Fail(error.ToString()));

        lifetime.Start();
        lifetime.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var completion = lifetime.StopAsync();
        Assert.Same(completion, lifetime.StopAsync());
        Assert.False(completion.IsCompleted);
        Assert.Equal(["start"], calls);

        finishStart.SetResult();
        await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["start", "started", "stop", "cleanup"], calls);
        Assert.False(completion.IsCompleted);

        finishCleanup.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["start", "started", "stop", "cleanup", "stop"], calls);
        lifetime.Start();
        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(5, calls.Count);
        Assert.False(lifetime.HasErrors);
    }

    [Fact]
    public async Task UnmountBeforeLoaded_NeverAcquiresCamera()
    {
        var calls = new List<string>();
        var lifetime = Create(calls);

        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        lifetime.Start();

        Assert.Empty(calls);
    }

    [Fact]
    public async Task NextPreview_WaitsForPreviousCleanup()
    {
        var calls = new List<string>();
        var previousCleanup = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var lifetime = new CameraPreviewLifetime(previousCleanup.Task,
            () => { calls.Add("start"); started.SetResult(); return Task.CompletedTask; },
            () => calls.Add("stop"),
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            (_, error) => Assert.Fail(error.ToString()));

        lifetime.Start();
        Assert.Empty(calls);
        previousCleanup.SetResult();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(["start", "stop", "cleanup", "stop"], calls);
    }

    [Fact]
    public async Task UnmountWhileWaitingForPreviousCleanup_SkipsStartButPreservesReleaseBarrier()
    {
        var calls = new List<string>();
        var previousCleanup = new TaskCompletionSource();
        var lifetime = Create(calls, previousCleanup.Task);

        lifetime.Start();
        var completion = lifetime.StopAsync();
        Assert.False(completion.IsCompleted);
        previousCleanup.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task StartFailure_IsReportedAndStillCleanedUpOnUnmount()
    {
        var calls = new List<string>();
        var expected = new InvalidOperationException("start failed");
        var errors = new List<(string, Exception)>();
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () => throw expected,
            () => calls.Add("stop"),
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            (operation, error) => errors.Add((operation, error)));

        lifetime.Start();

        Assert.Equal([("start", expected)], errors);
        Assert.True(lifetime.HasErrors);
        Assert.False(lifetime.Completion.IsCompleted);
        Assert.Empty(calls);

        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["stop", "cleanup", "stop"], calls);
    }

    [Fact]
    public async Task StopAndCleanupFailures_AreBothReportedAndFinalStopStillRuns()
    {
        var stopError = new InvalidOperationException("stop failed");
        var cleanupError = new InvalidOperationException("cleanup failed");
        var errors = new List<(string, Exception)>();
        var started = new TaskCompletionSource();
        var stops = 0;
        var cleanups = 0;
        var lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () => { started.SetResult(); return Task.CompletedTask; },
            () => { stops++; throw stopError; },
            () => { cleanups++; return Task.FromException(cleanupError); },
            (operation, error) => errors.Add((operation, error)));

        lifetime.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, stops);
        Assert.Equal(1, cleanups);
        Assert.True(lifetime.HasErrors);
        Assert.Equal([("stop", stopError), ("cleanup", cleanupError), ("stop after cleanup", stopError)], errors);
    }

    [Fact]
    public async Task ToolkitFailureThenNativeRetry_RemainsOwnedUntilUnmount()
    {
        var calls = new List<string>();
        bool captureActive = false;
        CameraPreviewLifetime? lifetime = null;
        lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () =>
            {
                calls.Add("start");
                lifetime!.RecordFailure();
                Assert.Equal(["start"], calls);
                return Task.CompletedTask;
            },
            () => calls.Add("stop"),
            () =>
            {
                calls.Add("cleanup");
                captureActive = false;
                return Task.CompletedTask;
            },
            (_, error) => Assert.Fail(error.ToString()));

        lifetime.Start();
        Assert.True(lifetime.HasErrors);
        Assert.False(lifetime.Completion.IsCompleted);
        Assert.Equal(["start"], calls);

        // Toolkit's switch button may initialize the same helper again after failure.
        captureActive = true;
        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(captureActive);
        Assert.Equal(["start", "stop", "cleanup", "stop"], calls);
    }

    [Fact]
    public async Task ReentrantUnmountFromStartCallback_DoesNotCleanUpUntilStartReturns()
    {
        var calls = new List<string>();
        CameraPreviewLifetime? lifetime = null;
        lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () =>
            {
                calls.Add("start");
                _ = lifetime!.StopAsync();
                Assert.Equal(["start"], calls);
                calls.Add("started");
                return Task.CompletedTask;
            },
            () => calls.Add("stop"),
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            (_, error) => Assert.Fail(error.ToString()));

        lifetime.Start();
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(["start", "started", "stop", "cleanup", "stop"], calls);
    }

    private static CameraPreviewLifetime Create(List<string> calls, Task? previousCleanup = null) =>
        new(previousCleanup ?? Task.CompletedTask,
            () => { calls.Add("start"); return Task.CompletedTask; },
            () => calls.Add("stop"),
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            (_, error) => Assert.Fail(error.ToString()));
}
