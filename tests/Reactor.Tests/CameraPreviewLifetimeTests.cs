using WctControls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class CameraPreviewLifetimeTests
{
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
    public async Task StartFailure_IsReportedAndCleanedUpWithoutWaitingForUnmount()
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
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal([("start", expected)], errors);
        Assert.True(lifetime.HasErrors);
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
    public async Task ToolkitFailureResult_MarksFailureAndReleasesWithoutAnException()
    {
        var calls = new List<string>();
        CameraPreviewLifetime? lifetime = null;
        lifetime = new CameraPreviewLifetime(Task.CompletedTask,
            () =>
            {
                calls.Add("start");
                _ = lifetime!.FailAsync();
                Assert.Equal(["start"], calls);
                return Task.CompletedTask;
            },
            () => calls.Add("stop"),
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            (_, error) => Assert.Fail(error.ToString()));

        lifetime.Start();
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(lifetime.HasErrors);
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
