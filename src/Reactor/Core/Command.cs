namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Immutable (init-only) command descriptor that bundles an action with its metadata (label, icon,
/// keyboard accelerator, enabled state). Define once, use in any surface:
///   var save = new Command { Label = "Save", Execute = () => Save(), Icon = SymbolIcon("Save") };
///   AppBarButton(save)   // toolbar
///   MenuItem(save)       // menu
///   Button(save)         // inline
/// </summary>
public sealed record Command
{
    /// <summary>Human-readable label shown on buttons, menu items, tooltips, etc.</summary>
    public required string Label { get; init; }

    /// <summary>Synchronous action. Mutually exclusive with <see cref="ExecuteAsync"/>.</summary>
    public Action? Execute { get; init; }

    /// <summary>Asynchronous action. Use with <see cref="RenderContext.UseCommand"/> to get
    /// automatic IsExecuting tracking and re-entrance guards.</summary>
    public Func<Task>? ExecuteAsync { get; init; }

    /// <summary>Whether the command's action can be invoked. Defaults to true.</summary>
    public bool CanExecute { get; init; } = true;

    /// <summary>Whether the command is currently executing an async operation.
    /// Managed by <see cref="RenderContext.UseCommand"/>.</summary>
    public bool IsExecuting { get; init; }

    /// <summary>
    /// Leading-edge debounce window, in milliseconds. <c>0</c> (the default) disables
    /// debouncing and preserves the un-debounced behavior exactly.
    /// <para>
    /// When &gt; 0 and the command is processed through <see cref="RenderContext.UseCommand"/>,
    /// the first fire is accepted and any subsequent fire within <c>DebounceMs</c> of it is
    /// dropped (a no-op). For the duration of the window <see cref="IsDebouncing"/> is true,
    /// so <see cref="IsEnabled"/> reports false and the bound control visibly disables, then
    /// re-enables when the window elapses. This is the framework-owned replacement for the
    /// "wrap a sync action in <c>Task.Delay</c> to absorb double-clicks" pattern (issue #136).
    /// </para>
    /// <para>
    /// For async commands, <see cref="IsExecuting"/> already tracks the lambda's lifetime;
    /// <c>DebounceMs</c> is the time-based generalization that keeps the disabled window open
    /// past the lambda's return (the effective disabled window is the longer of the two).
    /// </para>
    /// <para>
    /// <b>DebounceMs only works through <see cref="RenderContext.UseCommand"/>.</b> The debounce
    /// state (last-fire window + re-enable timer) lives in the <c>UseCommand</c> hook's backing
    /// store, which is the only place that persists across renders (the <see cref="Command"/>
    /// record itself is reconstructed every render). A raw <c>new Command { DebounceMs = … }</c>
    /// bound directly to a control — without being passed through <c>UseCommand</c> — has nowhere
    /// to persist that state and is therefore <b>inert: it does NOT debounce</b>. Always wrap a
    /// debounced command with <c>UseCommand</c>:
    /// <code>var run = UseCommand(new Command { Label = "Run", Execute = Run, DebounceMs = 1500 });</code>
    /// </para>
    /// </summary>
    public int DebounceMs { get; init; }

    /// <summary>Whether the command is currently inside its leading-edge debounce window.
    /// This is framework-managed state set by <see cref="RenderContext.UseCommand"/> when
    /// <see cref="DebounceMs"/> &gt; 0; it is read-only to app authors (internal init).</summary>
    public bool IsDebouncing { get; internal init; }

    /// <summary>Internal marker: set when <see cref="RenderContext.UseCommand"/> has already
    /// wrapped this command's execution (debounce + IsExecuting tracking). Re-passing an
    /// already-handled command through <c>UseCommand</c> is a passthrough, so debounce is never
    /// applied twice. Not part of the public surface.</summary>
    internal bool DebounceHandled { get; init; }

    /// <summary>Icon to display alongside the command.</summary>
    public IconData? Icon { get; init; }

    /// <summary>Tooltip / accessibility description.</summary>
    public string? Description { get; init; }

    /// <summary>Keyboard shortcut for this command.</summary>
    public KeyboardAcceleratorData? Accelerator { get; init; }

    /// <summary>Access key (Alt+key) for this command.</summary>
    public string? AccessKey { get; init; }

    /// <summary>Computed: the command is enabled only when it can execute, is not currently
    /// executing, and is not inside its leading-edge debounce window.</summary>
    public bool IsEnabled => CanExecute && !IsExecuting && !IsDebouncing;
}

/// <summary>
/// Immutable (init-only) parameterized command descriptor. The action receives an argument of type <typeparamref name="T"/>,
/// enabling a single command definition to operate on different targets:
///   var delete = new Command&lt;Item&gt; { Label = "Delete", Execute = item => Remove(item) };
///   MenuItem(delete, selectedItem)
/// </summary>
public sealed record Command<T>
{
    /// <summary>Human-readable label shown on buttons, menu items, tooltips, etc.</summary>
    public required string Label { get; init; }

    /// <summary>Synchronous action that receives a parameter.</summary>
    public Action<T>? Execute { get; init; }

    /// <summary>Asynchronous action that receives a parameter. Use with
    /// <see cref="RenderContext.UseCommand{T}"/> for lifecycle tracking.</summary>
    public Func<T, Task>? ExecuteAsync { get; init; }

    /// <summary>Whether the command's action can be invoked. Defaults to true.</summary>
    public bool CanExecute { get; init; } = true;

    /// <summary>Whether the command is currently executing an async operation.</summary>
    public bool IsExecuting { get; init; }

    /// <summary>
    /// Leading-edge debounce window, in milliseconds. <c>0</c> (the default) disables
    /// debouncing. When &gt; 0 and the command is processed through
    /// <see cref="RenderContext.UseCommand{T}"/>, the first fire is accepted and any
    /// subsequent fire within the window is dropped, with <see cref="IsDebouncing"/> (and
    /// therefore <see cref="IsEnabled"/>=false) reflecting the window so the bound control
    /// disables. <b>Like <see cref="Command.DebounceMs"/>, this only works through
    /// <see cref="RenderContext.UseCommand{T}"/></b> — a raw <c>new Command&lt;T&gt; { DebounceMs = … }</c>
    /// bound directly is inert and does not debounce. See <see cref="Command.DebounceMs"/> for
    /// the full contract (issue #136).
    /// </summary>
    public int DebounceMs { get; init; }

    /// <summary>Whether the command is currently inside its leading-edge debounce window.
    /// Framework-managed state set by <see cref="RenderContext.UseCommand{T}"/> when
    /// <see cref="DebounceMs"/> &gt; 0; read-only to app authors (internal init).</summary>
    public bool IsDebouncing { get; internal init; }

    /// <summary>Internal marker: set when <see cref="RenderContext.UseCommand{T}"/> has already
    /// wrapped this command's execution, so debounce is never applied twice on a re-passed
    /// command. Not part of the public surface.</summary>
    internal bool DebounceHandled { get; init; }

    /// <summary>Icon to display alongside the command.</summary>
    public IconData? Icon { get; init; }

    /// <summary>Tooltip / accessibility description.</summary>
    public string? Description { get; init; }

    /// <summary>Keyboard shortcut for this command.</summary>
    public KeyboardAcceleratorData? Accelerator { get; init; }

    /// <summary>Access key (Alt+key) for this command.</summary>
    public string? AccessKey { get; init; }

    /// <summary>Computed: the command is enabled only when it can execute, is not currently
    /// executing, and is not inside its leading-edge debounce window.</summary>
    public bool IsEnabled => CanExecute && !IsExecuting && !IsDebouncing;
}
