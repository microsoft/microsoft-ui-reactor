namespace Microsoft.UI.Reactor;

/// <summary>
/// Immutable (init-only) declarative description of a system-tray icon. Hand to
/// <see cref="ReactorApp.OpenTrayIcon"/> to register; hand to
/// <see cref="ReactorTrayIcon.Update"/> to diff and re-apply only changed
/// fields. (spec 036 §11.4)
/// </summary>
/// <param name="Icon">
/// The icon to display in the notification area. Filesystem paths
/// (<see cref="WindowIcon.FromPath"/>) and packaged resources
/// (<see cref="WindowIcon.FromResource"/>) are both supported.
/// </param>
/// <param name="Tooltip">
/// User-visible tooltip that surfaces as the icon's accessible name to
/// Narrator / UIA. (spec 036 §0.6)
/// </param>
/// <param name="Key">
/// Optional stable identity for <c>UseTrayIcon</c> reuse and shell lookups.
/// </param>
/// <param name="IsVisible">
/// Whether the icon is currently shown. Hidden icons are still registered
/// with the shell but suppressed via <c>NIS_HIDDEN</c>.
/// </param>
public sealed record TrayIconSpec(
    WindowIcon Icon,
    string Tooltip,
    WindowKey? Key = null,
    bool IsVisible = true);
