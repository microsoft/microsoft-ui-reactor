namespace WctControls;

// UI-thread lifecycle ordering, kept independent of WinUI so overlap is testable.
internal sealed class CameraPreviewLifetime
{
    private readonly TaskCompletionSource _loaded = new();
    private readonly TaskCompletionSource _stopRequested = new();
    private readonly Func<Task> _start;
    private readonly Action _stop;
    private readonly Func<Task> _cleanup;
    private readonly Action<string, Exception> _report;
    private readonly Action? _unsubscribe;
    private readonly Func<Task>? _settle;

    public CameraPreviewLifetime(Task previousCleanup, Func<Task> start, Action stop,
        Func<Task> cleanup, Action<string, Exception> report, Action? unsubscribe = null,
        Func<Task>? settle = null)
    {
        _start = start;
        _stop = stop;
        _cleanup = cleanup;
        _report = report;
        _unsubscribe = unsubscribe;
        _settle = settle;
        Completion = RunAndUnsubscribeAsync(previousCleanup);
    }

    public Task Completion { get; }
    public bool HasErrors { get; private set; }
    public bool ReleaseSucceeded { get; private set; } = true;

    public void Start() => _loaded.TrySetResult();

    public void RecordFailure() => HasErrors = true;

    public Task StopAsync()
    {
        _stopRequested.TrySetResult();
        _loaded.TrySetResult();
        return Completion;
    }

    private async Task RunAndUnsubscribeAsync(Task previousCleanup)
    {
        try
        {
            await RunAsync(previousCleanup);
        }
        finally
        {
            try
            {
                _unsubscribe?.Invoke();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                ReleaseSucceeded = false;
                Report("unsubscribe", error);
            }
        }
    }

    private async Task RunAsync(Task previousCleanup)
    {
        await _loaded.Task;
        try
        {
            await previousCleanup;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Report("previous cleanup", error);
            return;
        }
        if (_stopRequested.Task.IsCompleted)
            return;

        ReleaseSucceeded = false;
        try
        {
            try
            {
                await _start();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                Report("start", error);
            }

            await _stopRequested.Task;
        }
        finally
        {
            bool cleanupSucceeded = false;
            try
            {
                StopPlayback("stop");
            }
            finally
            {
                try
                {
                    await _cleanup();
                    cleanupSucceeded = true;
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                {
                    Report("cleanup", error);
                }
                finally
                {
                    // A Toolkit camera-switch continuation can restore playback
                    // while helper cleanup waits for its capture semaphore. It is
                    // queued before cleanup resumes, so settle lets it run first.
                    await SettleAsync();
                    bool stopped = StopPlayback("stop after cleanup");
                    ReleaseSucceeded = cleanupSucceeded && stopped;
                }
            }
        }
    }

    private async Task SettleAsync()
    {
        if (_settle is null)
            return;
        try
        {
            await _settle();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Report("settle", error);
        }
    }

    private bool StopPlayback(string operation)
    {
        try
        {
            _stop();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Report(operation, error);
            return false;
        }
    }

    private void Report(string operation, Exception error)
    {
        HasErrors = true;
        _report(operation, error);
    }
}

internal sealed class CameraPreviewCleanupQueue
{
    private Task _pending = Task.CompletedTask;
    private readonly List<CameraPreviewLifetime> _failed = new();
    private readonly HashSet<CameraPreviewLifetime> _tracked = new();

    // Track from creation, so a later preview waits for this one's release even
    // when the replacement mounts before this one unmounts.
    public void Track(CameraPreviewLifetime lifetime)
    {
        if (_tracked.Add(lifetime))
            _pending = Task.WhenAll(_pending, ObserveAsync(lifetime));
    }

    public void Retire(CameraPreviewLifetime lifetime)
    {
        Track(lifetime);
        _ = lifetime.StopAsync();
    }

    public async Task WaitAsync()
    {
        await _pending;
        if (_failed.Count != 0)
            throw new InvalidOperationException(
                "A previous camera preview did not release its resources. Restart the sample before opening another preview.");
    }

    private async Task ObserveAsync(CameraPreviewLifetime lifetime)
    {
        try
        {
            await lifetime.Completion;
        }
        finally
        {
            _tracked.Remove(lifetime);
            // The delegates retain the native owner and helper when release fails.
            if (!lifetime.ReleaseSucceeded)
                _failed.Add(lifetime);
        }
    }
}

// Page-owned link between the mounted native preview and host-disposal cleanup.
internal sealed class CameraPreviewPageLifetime<TControl>(Action<TControl> stop) where TControl : class
{
    private bool _inactive;
    private TControl? _control;

    public string Key { get; } = Guid.NewGuid().ToString("N");

    public void Attach(TControl control)
    {
        if (_inactive)
            stop(control);
        else
            _control = control;
    }

    public void Retire()
    {
        _inactive = true;
        if (_control is { } control)
        {
            _control = null;
            stop(control);
        }
    }

    public void Report(string? error, Action<string?> onError)
    {
        if (!_inactive)
            onError(error);
    }
}
