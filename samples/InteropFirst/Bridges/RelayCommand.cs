using System;
using System.Windows.Input;
using Microsoft.UI.Dispatching;

namespace InteropFirst.Bridges;

/// <summary>
/// Minimal MVVM <see cref="ICommand"/> implementation used by
/// <see cref="MainPageViewModel"/>. Avoids pulling in Community Toolkit MVVM
/// just for one type — keeps the sample's dependency surface to WinAppSDK +
/// Reactor only.
/// </summary>
/// <remarks>
/// Spec 033 §7. The <c>execute</c> delegate runs synchronously on the calling
/// thread (the dispatcher in normal use). We deliberately do not catch
/// exceptions inside <see cref="Execute"/> — letting them propagate to WinUI's
/// unhandled-exception handler (and your own UnhandledException hook) is the
/// whole point of having one. A swallowed exception in a command handler is a
/// silent bug.
/// </remarks>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    private readonly DispatcherQueue? _dispatcherQueue;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        // Capture the UI dispatcher at construction so RaiseCanExecuteChanged
        // can be safely called from any thread.
        try { _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); }
        catch { _dispatcherQueue = null; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _execute();
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Re-evaluate <see cref="CanExecute"/> in the UI. Marshals to the captured
    /// UI dispatcher when called from a background thread.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        _dispatcherQueue.TryEnqueue(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
