using Windows.System;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// A key press together with the modifier state that was live at the moment it was captured
/// (issue #987).
/// </summary>
/// <remarks>
/// <para>
/// The DataGrid's KeyDown handler defers its work through <c>DispatcherQueue.TryEnqueue</c>, so the
/// modifiers cannot be read where the key is handled — by then the keyboard has been re-sampled one
/// or more frames later and the user may already have released Shift. This type is the snapshot
/// that crosses that gap: build it synchronously inside the routed handler, next to the captured
/// key, and pass the whole chord to the deferred work.
/// </para>
/// <para>
/// <see cref="Ctrl"/> is captured even though nothing reads it yet. It is the seat for the
/// <c>Ctrl+Home</c> / <c>Ctrl+End</c> "first/last row" navigation spec 017 §6.8 specifies and the
/// grid does not implement; capturing it now means that arm needs no change to the dispatch
/// pipeline, only a new switch case.
/// </para>
/// </remarks>
/// <param name="Key">The key that was pressed.</param>
/// <param name="Shift">Whether either Shift key was down when the press was captured.</param>
/// <param name="Ctrl">Whether either Ctrl key was down when the press was captured.</param>
internal readonly record struct KeyChord(VirtualKey Key, bool Shift, bool Ctrl)
{
    /// <summary>
    /// Snapshots <paramref name="key"/> together with the live Shift / Ctrl state.
    /// </summary>
    /// <remarks>
    /// Must be called synchronously from the routed KeyDown handler. Calling it from deferred work
    /// re-reads the keyboard at that later time, which is the bug this type exists to prevent.
    /// </remarks>
    internal static KeyChord Capture(VirtualKey key) => Capture(key, IsDownNow);

    /// <summary>
    /// <see cref="Capture(VirtualKey)"/> with the modifier probe injected, so the mapping from
    /// probe answers to <see cref="Shift"/> / <see cref="Ctrl"/> is testable headlessly — the real
    /// probe is a WinRT call that a headless run cannot make.
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="isDown">Answers whether the given modifier key is currently held down.</param>
    internal static KeyChord Capture(VirtualKey key, Func<VirtualKey, bool> isDown)
    {
        ArgumentNullException.ThrowIfNull(isDown);

        // VirtualKey.Shift / .Control are the aggregate ("either side") keys — probing LeftShift
        // alone would miss a right-hand Shift+Tab.
        return new KeyChord(key, isDown(VirtualKey.Shift), isDown(VirtualKey.Control));
    }

    private static bool IsDownNow(VirtualKey key)
        => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}
