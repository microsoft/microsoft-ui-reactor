using System.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Visual taskbar progress for a top-level window. Backs
/// <see cref="ReactorWindow.Progress"/>. (spec 036 §11.1)
/// </summary>
/// <remarks>
/// All writes are best-effort. The shell is the source of truth — Reactor
/// keeps the last-applied state for round-trip queries and idempotent re-apply
/// only. A <see cref="TaskbarProgressState.None"/> assignment clears both
/// state and value; an <see cref="TaskbarProgressState.Indeterminate"/> state
/// ignores <see cref="Value"/>.
/// </remarks>
public sealed class TaskbarProgress
{
    private readonly nint _hwnd;
    private readonly Func<bool> _isDisposed;
    private TaskbarProgressState _state = TaskbarProgressState.None;
    private double _value;

    internal TaskbarProgress(nint hwnd, Func<bool> isDisposed)
    {
        _hwnd = hwnd;
        _isDisposed = isDisposed;
    }

    /// <summary>Last-applied progress state. (spec 036 §11.1)</summary>
    public TaskbarProgressState State
    {
        get => _state;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(State));
            _state = value;
            ApplyState(value);
        }
    }

    /// <summary>
    /// Progress fraction in <c>[0.0, 1.0]</c>. Ignored when
    /// <see cref="State"/> is <see cref="TaskbarProgressState.Indeterminate"/>
    /// or <see cref="TaskbarProgressState.None"/>. Out-of-range values throw
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(Value));
            if (!(value >= 0.0 && value <= 1.0))
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"TaskbarProgress.Value must be in [0.0, 1.0]; got {value}.");
            _value = value;
            // Implicit promote: setting Value while in None flips to Normal,
            // matching the user's intent ("show me 30%"). Indeterminate
            // remains indeterminate; explicit Paused / Error remain.
            if (_state == TaskbarProgressState.None)
            {
                _state = TaskbarProgressState.Normal;
                ApplyState(_state);
            }
            ApplyValue(value);
        }
    }

    /// <summary>Shorthand for <c>State = None</c> + <c>Value = 0</c>.</summary>
    public void Clear()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Clear));
        _value = 0;
        _state = TaskbarProgressState.None;
        ApplyState(TaskbarProgressState.None);
    }

    private void ApplyState(TaskbarProgressState state)
    {
        if (_isDisposed()) return;
        var taskbar = TaskbarComSingleton.TryGet();
        if (taskbar is null) return;
        try
        {
            var native = state switch
            {
                TaskbarProgressState.Indeterminate => NativeTaskbarProgressState.Indeterminate,
                TaskbarProgressState.Normal        => NativeTaskbarProgressState.Normal,
                TaskbarProgressState.Paused        => NativeTaskbarProgressState.Paused,
                TaskbarProgressState.Error         => NativeTaskbarProgressState.Error,
                _                                  => NativeTaskbarProgressState.NoProgress,
            };
            _ = taskbar.SetProgressState(_hwnd, native);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] TaskbarProgress.SetProgressState failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyValue(double value)
    {
        if (_isDisposed()) return;
        if (_state is TaskbarProgressState.Indeterminate or TaskbarProgressState.None) return;
        var taskbar = TaskbarComSingleton.TryGet();
        if (taskbar is null) return;
        try
        {
            // Quantize to 1000 units — matches WinUIEx convention and gives
            // smooth-looking shell progress without floating-point edge cases.
            const ulong total = 1000;
            ulong completed = (ulong)Math.Round(value * total);
            if (completed > total) completed = total;
            _ = taskbar.SetProgressValue(_hwnd, completed, total);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] TaskbarProgress.SetProgressValue failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// State of the taskbar progress indicator for a window. Wire-compatible with
/// the shell's <c>TBPF_*</c> values. (spec 036 §11.1)
/// </summary>
public enum TaskbarProgressState
{
    /// <summary>No progress shown (default).</summary>
    None,
    /// <summary>Marquee animation; <see cref="TaskbarProgress.Value"/> is ignored.</summary>
    Indeterminate,
    /// <summary>Standard determinate progress, green.</summary>
    Normal,
    /// <summary>Determinate progress, yellow (paused).</summary>
    Paused,
    /// <summary>Determinate progress, red (error).</summary>
    Error,
}
