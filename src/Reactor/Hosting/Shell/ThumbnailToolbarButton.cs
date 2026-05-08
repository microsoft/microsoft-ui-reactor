namespace Microsoft.UI.Reactor;

/// <summary>
/// One button in a window's thumbnail toolbar (taskbar live-preview UI).
/// Constructed declaratively and handed to
/// <see cref="ReactorWindow.SetThumbnailToolbar(IReadOnlyList{ThumbnailToolbarButton})"/>.
/// (spec 036 §11.5)
/// </summary>
/// <param name="Id">
/// Stable per-window identifier; used to diff against the previous call so
/// per-render <c>SetThumbnailToolbar</c> updates only the changed buttons.
/// Must be unique within a single set.
/// </param>
/// <param name="Icon">
/// Filesystem-backed icon. <see cref="WindowIcon.FromResource"/> sources are
/// not supported on the thumbnail surface (HICON only) and are silently
/// skipped — the button still renders without an icon.
/// </param>
/// <param name="Tooltip">Hover tooltip; truncated to 259 characters.</param>
/// <param name="OnClick">Click delegate. Invoked on the UI thread when the button is pressed.</param>
/// <param name="IsEnabled">Whether the button accepts input.</param>
/// <param name="IsVisible">Whether the button is rendered at all.</param>
/// <param name="DismissOnClick">When true, clicking the button hides the live-preview popup.</param>
public sealed record ThumbnailToolbarButton(
    string Id,
    WindowIcon Icon,
    string Tooltip,
    Action OnClick,
    bool IsEnabled = true,
    bool IsVisible = true,
    bool DismissOnClick = false);
