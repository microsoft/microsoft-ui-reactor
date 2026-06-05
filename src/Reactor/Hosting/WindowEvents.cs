using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Event payload for <see cref="ReactorWindow.SizeChanged"/>. Carries the new
/// DIP size and the underlying WinUI args for escape hatches. (spec 036 §4.2)
/// </summary>
public sealed class WindowDipSizeChangedEventArgs : EventArgs
{
    /// <summary>New size of the window's content region in DIPs.</summary>
    public (double Width, double Height) Size { get; }

    /// <summary>Underlying WinUI args. Use only when the DIP-shaped <see cref="Size"/> is insufficient.</summary>
    public WindowSizeChangedEventArgs? Raw { get; }

    /// <summary>Construct from a DIP size and the originating WinUI args.</summary>
    public WindowDipSizeChangedEventArgs((double Width, double Height) size, WindowSizeChangedEventArgs? raw)
    {
        Size = size;
        Raw = raw;
    }
}

/// <summary>Event payload for <see cref="ReactorWindow.PositionChanged"/>.</summary>
public sealed class WindowDipPositionChangedEventArgs : EventArgs
{
    /// <summary>New DIP top-left position of the window.</summary>
    public (double X, double Y) Position { get; }

    /// <summary>Construct from a DIP top-left position.</summary>
    public WindowDipPositionChangedEventArgs((double X, double Y) position)
    {
        Position = position;
    }
}

/// <summary>Event payload for <see cref="ReactorWindow.ZOrderChanged"/>.</summary>
public sealed class WindowZOrderChangedEventArgs : EventArgs
{
    /// <summary>
    /// True when Win32 reported this window was inserted behind another HWND.
    /// This is a covered hint, not a pixel-accurate occlusion guarantee.
    /// </summary>
    public bool IsCovered { get; }

    /// <summary>True when Win32 reported this window moved to the top of its z-order tier.</summary>
    public bool MovedToTop { get; }

    /// <summary>Construct from z-order hint flags.</summary>
    public WindowZOrderChangedEventArgs(bool isCovered, bool movedToTop)
    {
        IsCovered = isCovered;
        MovedToTop = movedToTop;
    }
}

/// <summary>
/// Event payload for <see cref="ReactorWindow.Closing"/>. Set
/// <see cref="Cancel"/> to <c>true</c> to abort the close. (spec 036 §4.2)
/// </summary>
public sealed class WindowClosingEventArgs : EventArgs
{
    /// <summary>Why the window is closing.</summary>
    public WindowCloseReason Reason { get; }

    /// <summary>
    /// Set to <c>true</c> to keep the window open. Multiple subscribers may
    /// stack — once any handler sets this to true, the close is cancelled.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>Construct with the originating reason.</summary>
    public WindowClosingEventArgs(WindowCloseReason reason)
    {
        Reason = reason;
    }
}
