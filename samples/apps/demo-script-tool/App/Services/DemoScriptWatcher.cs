using System;
using System.IO;
using System.Threading;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Watches <c>demo-script.md</c> for external edits and surfaces a single
/// debounced callback (spec §Filesystem Watcher). Generated step files are
/// not watched — the tool is the authoritative writer there.
/// </summary>
public sealed class DemoScriptWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly Action _onChanged;
    readonly Action _onDeleted;
    readonly SynchronizationContext? _ui;
    CancellationTokenSource? _debounceCts;
    bool _disposed;

    public DemoScriptWatcher(string projectRoot, Action onChanged, Action onDeleted)
    {
        _onChanged = onChanged;
        _onDeleted = onDeleted;
        _ui = SynchronizationContext.Current;

        _watcher = new FileSystemWatcher(projectRoot, DemoScriptStore.FileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        _watcher.Created += (_, _) => Schedule(_onChanged);
        _watcher.Changed += (_, _) => Schedule(_onChanged);
        _watcher.Renamed += (_, _) => Schedule(_onChanged);
        _watcher.Deleted += (_, _) => Schedule(_onDeleted);
    }

    void Schedule(Action target)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(100), token)
            .ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                if (_ui is not null)
                    _ui.Post(_ => target(), null);
                else
                    target();
            }, System.Threading.Tasks.TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
