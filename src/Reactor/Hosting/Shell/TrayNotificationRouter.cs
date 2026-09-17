namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// The logical tray interaction a <c>Shell_NotifyIcon</c> callback
/// notification maps to. (spec 036 §11.4)
/// </summary>
internal enum TrayCallbackKind
{
    /// <summary>The notification carries no user interaction we surface.</summary>
    None,

    /// <summary>A left click or keyboard activation of the icon.</summary>
    Click,

    /// <summary>A left double click on the icon.</summary>
    DoubleClick,

    /// <summary>A right click, or the keyboard context-menu gesture.</summary>
    RightClick,
}

/// <summary>
/// Pure routing table for the <c>WM_APP + 1</c> shell callback that
/// <see cref="TrayHiddenWindow"/> receives. Split out of
/// <c>TrayHiddenWindow.DispatchCallback</c> so the mapping is reachable from
/// the headless test tier — the hidden window itself owns a real HWND and a
/// <c>DispatcherQueue</c>. (spec 036 §11.4)
/// </summary>
/// <remarks>
/// <para><b>NOTIFYICON_VERSION_4 wire protocol (issue #1180).</b> Explorer
/// forwards <em>both</em> the legacy mouse message and the version-4 semantic
/// notification for a single physical interaction: a left click arrives as
/// <c>WM_LBUTTONUP</c> <em>and</em> <c>NIN_SELECT</c>, a right click as
/// <c>WM_RBUTTONUP</c> <em>and</em> <c>WM_CONTEXTMENU</c>. Routing both arms
/// of each pair fires <c>Click</c> / <c>RightClick</c> twice per interaction,
/// so only the v4 semantic notifications are routed.</para>
/// <para><c>WM_LBUTTONDBLCLK</c> has no v4 counterpart and stays. It is not a
/// duplicate of <c>NIN_SELECT</c>: a physical double click legitimately
/// raises <c>Click</c> once (from the first click's <c>NIN_SELECT</c>) and
/// then <c>DoubleClick</c> once.</para>
/// </remarks>
internal static class TrayNotificationRouter
{
    /// <summary>
    /// Map a shell callback notification id to the interaction it represents.
    /// Returns <see cref="TrayCallbackKind.None"/> for anything we do not
    /// surface — balloon notifications, popup hover, and the legacy mouse
    /// messages that duplicate a v4 notification.
    /// </summary>
    internal static TrayCallbackKind Classify(uint notification) => notification switch
    {
        TrayIconComInterop.NIN_SELECT or TrayIconComInterop.NIN_KEYSELECT => TrayCallbackKind.Click,
        TrayIconComInterop.WM_LBUTTONDBLCLK => TrayCallbackKind.DoubleClick,
        TrayIconComInterop.WM_CONTEXTMENU => TrayCallbackKind.RightClick,
        _ => TrayCallbackKind.None,
    };

    /// <summary>
    /// Invoke the callback on <paramref name="entry"/> that corresponds to
    /// <paramref name="kind"/>. A <see cref="TrayCallbackKind.None"/> kind, or
    /// an unset handler, is a no-op.
    /// </summary>
    internal static void Dispatch(TrayCallbackEntry entry, TrayCallbackKind kind)
    {
        ArgumentNullException.ThrowIfNull(entry);
        switch (kind)
        {
            case TrayCallbackKind.Click:
                entry.OnClick?.Invoke();
                break;
            case TrayCallbackKind.DoubleClick:
                entry.OnDoubleClick?.Invoke();
                break;
            case TrayCallbackKind.RightClick:
                entry.OnRightClick?.Invoke();
                break;
        }
    }
}
