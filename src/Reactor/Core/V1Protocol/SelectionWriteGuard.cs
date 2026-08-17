namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Issue #1090 — the reachability rule for suppressed selected-index writes.
///
/// <para><b>Why this exists.</b> Echo suppression is armed per <em>expected
/// event</em>: <see cref="ChangeEchoSuppressor.BeginSuppress"/> (or
/// <c>ReactorBinding.WriteSuppressed</c>) promises that the write about to
/// happen will raise the control's change event, and
/// <see cref="ChangeEchoSuppressor.ShouldSuppress(Reconciler.ReactorState)"/>
/// consumes that promise when the event arrives. A write that the control
/// silently refuses breaks the promise in a way the counter cannot detect: the
/// token outlives the write and is spent on the user's next <em>genuine</em>
/// interaction, which is then dropped.</para>
///
/// <para>Selection is the case where this bites. WinUI will not honor a
/// <c>SelectedIndex</c> past the end of its <c>ItemsSource</c> — the property
/// stays where it was, no <c>SelectionChanged</c> is raised, and on some
/// item-count/index combinations the setter throws outright. The usual trigger
/// is entirely idiomatic: a list whose items array is still empty on mount while
/// its data loads (<c>UseState&lt;T[]&gt;([])</c> plus a fetch), with
/// <c>SelectedIndex</c> controlled at 0.</para>
///
/// <para>A drift gate (<c>control != requested</c>) is necessary but not
/// sufficient — it only proves the write is not a no-op, not that it can land.
/// Callers must gate on both.</para>
/// </summary>
internal static class SelectionWriteGuard
{
    /// <summary>
    /// Can a controlled selected-index write of <paramref name="index"/> land on
    /// a source of <paramref name="itemCount"/> items — and therefore be relied
    /// on to raise the change event that echo suppression is armed for?
    ///
    /// <para><c>-1</c> (and any negative sentinel) is always reachable: it is
    /// spec-050's explicit force-clear value, and clearing a selection is
    /// meaningful against a source of any size. The caller's drift gate has
    /// already established the control is not already there, so the write lands
    /// and echoes.</para>
    ///
    /// <para>When this returns <c>false</c> the caller must skip the write
    /// entirely — <em>not</em> write without arming. Writing bare would let the
    /// control's own event (if the platform ever did raise one) leak into the
    /// user callback, which is the issue #495 render storm.</para>
    /// </summary>
    /// <param name="index">The controlled index the element is requesting.</param>
    /// <param name="itemCount">Item count of the source the write will target.</param>
    internal static bool CanLand(int index, int itemCount)
        => index < 0 || index < itemCount;
}
