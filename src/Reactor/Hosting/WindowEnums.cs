namespace Microsoft.UI.Reactor;

/// <summary>
/// Initial placement strategy for a <see cref="ReactorWindow"/> when it opens.
/// (spec 036 §3.2 / §4.1)
/// </summary>
public enum WindowStartPosition
{
    /// <summary>WinUI default placement — the OS picks based on prior windows.</summary>
    Default = 0,

    /// <summary>Center the window on the primary monitor.</summary>
    CenterOnPrimary = 1,

    /// <summary>Center the window on its <see cref="WindowSpec.Owner"/>'s monitor.</summary>
    CenterOnOwner = 2,

    /// <summary>Place at <see cref="WindowSpec.ManualPosition"/>. Both must be set together.</summary>
    Manual = 4,

    /// <summary>Center on the monitor nearest the current cursor position.</summary>
    CenterOnCurrent = 5,
}

/// <summary>
/// Coarse classifier for the WinUI <see cref="Microsoft.UI.Windowing.AppWindowPresenterKind"/>
/// presenter applied to a window. (spec 036 §3.2 / §4.1)
/// </summary>
public enum WindowStyle
{
    /// <summary>Standard overlapped window chrome.</summary>
    Default,

    /// <summary>No border, caption, or system menu.</summary>
    None,

    /// <summary>Tool-window chrome; hidden from the taskbar unless explicitly overridden.</summary>
    ToolWindow,
}

public enum WindowCornerStyle
{
    /// <summary>Let the OS choose the default corner style.</summary>
    Default,

    /// <summary>Request square window corners.</summary>
    Square,

    /// <summary>Request rounded window corners.</summary>
    Rounded,

    /// <summary>Request small rounded window corners.</summary>
    RoundedSmall,
}

public enum WindowLevel
{
    /// <summary>Normal z-order behavior.</summary>
    Normal,

    /// <summary>Keep above other Reactor app windows when they activate.</summary>
    Floating,

    /// <summary>Use the Win32 topmost tier.</summary>
    AlwaysOnTop,
}

public enum WindowSizeToContent
{
    /// <summary>Window size is controlled manually or by the OS default.</summary>
    Manual,

    /// <summary>Window width tracks content desired width; height stays unchanged.</summary>
    Width,

    /// <summary>Window height tracks content desired height; width stays unchanged.</summary>
    Height,

    /// <summary>Window width and height both track content desired size.</summary>
    WidthAndHeight,
}

public enum WindowResizeMode
{
    /// <summary>User can drag borders; minimize and maximize buttons are enabled subject to the spec masks.</summary>
    CanResize,

    /// <summary>User cannot drag borders; minimize and maximize buttons are disabled subject to the spec masks.</summary>
    NoResize,

    /// <summary>User cannot drag borders, but the minimize button stays enabled subject to the spec mask.</summary>
    CanMinimize,
}

/// <summary>
/// Whether <see cref="WindowSpec.AspectRatio"/> applies to the outer window
/// rectangle (caption + borders included) or to the client (content) area.
/// </summary>
/// <remarks>
/// <para><see cref="Window"/> is the simpler / cheaper mode and matches the
/// raw shape of the OS <c>WM_SIZING</c> contract — the framework just
/// enforces a ratio on the rect the OS hands us.</para>
/// <para><see cref="Client"/> is what most media/game/canvas apps want
/// (a 1:1 client area for a square video player, a 16:9 client area for a
/// game viewport). The framework computes the chrome inset via
/// <c>AdjustWindowRectExForDpi</c> at the current window style + DPI, so
/// the ratio stays accurate across DPI changes and across
/// <see cref="WindowStyle"/> flips. Has no effect when
/// <see cref="WindowSpec.AspectRatio"/> is null.</para>
/// </remarks>
public enum AspectRatioBasis
{
    /// <summary>Ratio applies to the outer window rect, including caption and borders. Default.</summary>
    Window,

    /// <summary>Ratio applies to the client area. The framework auto-accounts for chrome.</summary>
    Client,
}

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
