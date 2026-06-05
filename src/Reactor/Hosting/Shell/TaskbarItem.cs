using System.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Grouping facade for the taskbar features associated with a <see cref="ReactorWindow"/>.
/// Existing <see cref="ReactorWindow.Progress"/>, <see cref="ReactorWindow.Overlay"/>,
/// <see cref="ReactorWindow.SetThumbnailToolbar"/>, and
/// <see cref="ReactorWindow.ClearThumbnailToolbar"/> shortcuts remain available.
/// </summary>
public sealed class TaskbarItem
{
    private readonly ReactorWindow _owner;
    private readonly nint _hwnd;
    private string? _description;

    internal TaskbarItem(ReactorWindow owner, nint hwnd)
    {
        _owner = owner;
        _hwnd = hwnd;
    }

    /// <summary>Taskbar progress indicator for the owning window.</summary>
    public TaskbarProgress Progress => _owner.Progress;

    /// <summary>Taskbar overlay icon for the owning window.</summary>
    public TaskbarOverlay Overlay => _owner.Overlay;

    /// <summary>
    /// Shell thumbnail tooltip / description for the taskbar button. The property
    /// keeps the last value assigned; native application is best-effort.
    /// </summary>
    public string? Description
    {
        get => _description;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(Description));
            _description = value;
            var taskbar = TaskbarComSingleton.TryGet();
            if (taskbar is null) return;
            try { _ = taskbar.SetThumbnailTooltip(_hwnd, value); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor] TaskbarItem.SetThumbnailTooltip failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Replace the thumbnail toolbar buttons for the owning window.</summary>
    public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons)
        => _owner.SetThumbnailToolbar(buttons);

    /// <summary>Hide all thumbnail toolbar buttons for the owning window.</summary>
    public void ClearThumbnailToolbar()
        => _owner.ClearThumbnailToolbar();
}
