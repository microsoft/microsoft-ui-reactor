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

    public CameraPreviewLifetime(Task previousCleanup, Func<Task> start, Action stop,
        Func<Task> cleanup, Action<string, Exception> report, Action? unsubscribe = null)
    {
        _start = start;
        _stop = stop;
        _cleanup = cleanup;
        _report = report;
        _unsubscribe = unsubscribe;
        Completion = RunAndUnsubscribeAsync(previousCleanup);
    }

    public Task Completion { get; }
    public bool HasErrors { get; private set; }

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
            catch (Exception error)
            {
                Report("unsubscribe", error);
            }
        }
    }

    private async Task RunAsync(Task previousCleanup)
    {
        await _loaded.Task;
        await previousCleanup;
        if (_stopRequested.Task.IsCompleted)
            return;

        try
        {
            try
            {
                await _start();
            }
            catch (Exception error)
            {
                Report("start", error);
            }

            await _stopRequested.Task;
        }
        finally
        {
            try
            {
                StopPlayback("stop");
            }
            finally
            {
                try
                {
                    await _cleanup();
                }
                catch (Exception error)
                {
                    Report("cleanup", error);
                }
                finally
                {
                    // A Toolkit camera-switch continuation can restore playback
                    // while helper cleanup waits for its capture semaphore.
                    StopPlayback("stop after cleanup");
                }
            }
        }
    }

    private void StopPlayback(string operation)
    {
        try
        {
            _stop();
        }
        catch (Exception error)
        {
            Report(operation, error);
        }
    }

    private void Report(string operation, Exception error)
    {
        HasErrors = true;
        _report(operation, error);
    }
}

internal sealed class CameraPreviewPageLifetime
{
    private bool _inactive;

    public string Key { get; } = Guid.NewGuid().ToString("N");

    public void Deactivate() => _inactive = true;

    public void Report(string? error, Action<string?> onError)
    {
        if (!_inactive)
            onError(error);
    }
}
