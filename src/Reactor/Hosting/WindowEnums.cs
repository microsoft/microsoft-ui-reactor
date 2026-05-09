namespace Microsoft.UI.Reactor;

/// <summary>
/// Initial placement strategy for a <see cref="ReactorWindow"/> when it opens.
/// (spec 036 §3.2 / §4.1)
/// </summary>
public enum WindowStartPosition
{
    /// <summary>WinUI default placement — the OS picks based on prior windows.</summary>
    Default,

    /// <summary>Center the window on the primary monitor.</summary>
    CenterOnPrimary,

    /// <summary>Center the window on its <see cref="WindowSpec.Owner"/>'s monitor.</summary>
    CenterOnOwner,

    /// <summary>
    /// Restore the previous size/position from the registered persistence store
    /// keyed by <see cref="WindowSpec.PersistenceId"/>. Falls back to
    /// <see cref="Default"/> when no prior session is recorded or the saved
    /// monitor layout no longer matches.
    /// </summary>
    RestoreFromPersistence,

    /// <summary>Place at <see cref="WindowSpec.ManualPosition"/>. Both must be set together.</summary>
    Manual,
}

/// <summary>
/// Coarse classifier for the WinUI <see cref="Microsoft.UI.Windowing.AppWindowPresenterKind"/>
/// presenter applied to a window. (spec 036 §3.2 / §4.1)
/// </summary>
public enum PresenterKind
{
    /// <summary>Standard chrome with caption and a system menu.</summary>
    Overlapped,

    /// <summary>Borderless full-screen presentation.</summary>
    FullScreen,

    /// <summary>Compact-overlay (PIP-style) presentation.</summary>
    CompactOverlay,
}

/// <summary>
/// Lifecycle/state of a <see cref="ReactorWindow"/>. Exposed via
/// <see cref="ReactorWindow.State"/> and the <c>UseWindowState</c> hook.
/// (spec 036 §3.2)
/// </summary>
public enum WindowState
{
    /// <summary>Not minimized, not maximized — the default.</summary>
    Normal,

    /// <summary>Minimized to the taskbar.</summary>
    Minimized,

    /// <summary>Maximized to fill the work area.</summary>
    Maximized,

    /// <summary>Borderless full-screen presentation.</summary>
    FullScreen,

    /// <summary>Compact-overlay presentation.</summary>
    CompactOverlay,
}

/// <summary>
/// Why the window is closing. Carried on
/// <see cref="WindowClosingEventArgs.Reason"/>. (spec 036 §4.2)
/// </summary>
public enum WindowCloseReason
{
    /// <summary>The user closed the window via the system menu, caption button, or Alt+F4.</summary>
    UserClosed,

    /// <summary>App code called <see cref="ReactorWindow.Close"/> or <see cref="ReactorApp.Exit"/>.</summary>
    AppClosed,

    /// <summary>The window's owner closed and is cascading to its owned windows.</summary>
    OwnerClosed,
}

/// <summary>
/// Process-shutdown policy. Evaluated whenever a window or tray icon closes.
/// (spec 036 §6.2)
/// </summary>
public enum ShutdownPolicy
{
    /// <summary>Default. Closing the primary window exits the process.</summary>
    OnPrimaryWindowClosed,

    /// <summary>Exit when the last window AND the last tray icon have closed.</summary>
    OnLastSurfaceClosed,

    /// <summary>Surfaces close, but the process keeps running until <see cref="ReactorApp.Exit"/> is called.</summary>
    Explicit,
}
