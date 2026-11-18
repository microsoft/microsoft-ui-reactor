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

    public CameraPreviewLifetime(Task previousCleanup, Func<Task> start, Action stop,
        Func<Task> cleanup, Action<string, Exception> report)
    {
        _start = start;
        _stop = stop;
        _cleanup = cleanup;
        _report = report;
        Completion = RunAsync(previousCleanup);
    }

    public Task Completion { get; }
    public bool HasErrors { get; private set; }

    public void Start() => _loaded.TrySetResult();

    public Task FailAsync()
    {
        HasErrors = true;
        return StopAsync();
    }

    public Task StopAsync()
    {
        _stopRequested.TrySetResult();
        _loaded.TrySetResult();
        return Completion;
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
                _stopRequested.TrySetResult();
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
