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
    /// A chord for <paramref name="key"/> with no modifiers, built WITHOUT touching the keyboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a cheap stand-in for <see cref="Capture(VirtualKey)"/> — it reports no modifiers
    /// whether or not any are held. Use it only where the answer provably cannot depend on modifier
    /// state: the grid's KeyDown handler settles its modifier-blind claim
    /// (<c>DataGridComponent&lt;T&gt;.ShouldHandleKey</c>) with one of these, so that the keyboard is
    /// probed only for keys the grid actually owns and never for ordinary typing into an editor.
    /// </para>
    /// <para>
    /// <b>Never pass one of these to <c>DataGridComponent&lt;T&gt;.HandleKeyDown</c>.</b> That
    /// overload takes a <see cref="KeyChord"/>, so a conflict resolution that loses the captured
    /// local raises <c>CS0103</c> at the dispatch — and this method is the nearest expression that
    /// compiles, sitting a few lines above in the same handler with wording a hurried reader can
    /// take as permission. The repair builds, keeps <see cref="Capture(VirtualKey)"/> in its correct
    /// position, and silently restores issue #987 in full: the grid is modifier-blind on every
    /// keystroke and Shift+Tab moves forward every time. Nothing else catches it — not the compiler,
    /// which is satisfied; not an unused-variable warning, because the cell-edit guard still reads
    /// the capture. Only
    /// <c>DataGridCaptureSiteTests.DeferredDispatch_ReceivesTheCapturedChord</c> fails. Pass the
    /// captured chord into the deferred work instead of rebuilding one at the dispatch.
    /// </para>
    /// </remarks>
    internal static KeyChord Unmodified(VirtualKey key) => new(key, Shift: false, Ctrl: false);

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
