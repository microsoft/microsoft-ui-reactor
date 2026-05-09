using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Taskbar overlay icon ("badge") for a top-level window. Backs
/// <see cref="ReactorWindow.Overlay"/>. (spec 036 §11.2)
/// </summary>
/// <remarks>
/// Setting <see cref="Icon"/> to null clears the overlay. The icon should be
/// 16×16 logical pixels — larger images are downscaled by the shell with
/// quality loss; we emit a one-shot <c>Debug.WriteLine</c> when a
/// <see cref="WindowIcon.FromPath(string)"/> source can't be loaded as an HICON.
///
/// Accessibility: <see cref="AccessibleDescription"/> flows through the
/// <c>pszDescription</c> parameter of <c>ITaskbarList3.SetOverlayIcon</c> —
/// without it the overlay is invisible to assistive tech (spec §0.6).
/// </remarks>
public sealed class TaskbarOverlay
{
    private readonly nint _hwnd;
    private readonly Func<bool> _isDisposed;
    private WindowIcon? _icon;
    private string? _accessibleDescription;
    private nint _currentHIcon;

    internal TaskbarOverlay(nint hwnd, Func<bool> isDisposed)
    {
        _hwnd = hwnd;
        _isDisposed = isDisposed;
    }

    /// <summary>The current overlay icon. Null clears.</summary>
    public WindowIcon? Icon
    {
        get => _icon;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(Icon));
            _icon = value;
            Apply();
        }
    }

    /// <summary>
    /// Accessible description handed to the shell's
    /// <c>SetOverlayIcon(pszDescription)</c> — required for assistive tech to
    /// announce the overlay. (spec 036 §0.6)
    /// </summary>
    public string? AccessibleDescription
    {
        get => _accessibleDescription;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(AccessibleDescription));
            _accessibleDescription = value;
            Apply();
        }
    }

    private void Apply()
    {
        if (_isDisposed()) return;
        var taskbar = TaskbarComSingleton.TryGet();
        if (taskbar is null) return;

        // Replace the cached HICON; the shell holds its own reference once
        // SetOverlayIcon returns, so we destroy ours after the call.
        nint newIcon = LoadIconFor(_icon);
        try
        {
            _ = taskbar.SetOverlayIcon(_hwnd, newIcon, _accessibleDescription);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] TaskbarOverlay.SetOverlayIcon failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Free the previous icon AFTER the new one has been swapped in to
        // avoid a frame where the shell might dereference it. The fresh
        // newIcon is also freed — SetOverlayIcon copies the HICON internally.
        if (_currentHIcon != 0)
        {
            try { _ = DestroyIcon(_currentHIcon); } catch { /* best effort */ }
        }
        _currentHIcon = 0; // SetOverlayIcon retains its own copy.
        if (newIcon != 0)
        {
            try { _ = DestroyIcon(newIcon); } catch { /* best effort */ }
        }
    }

    private static nint LoadIconFor(WindowIcon? icon)
    {
        if (icon is null || string.IsNullOrEmpty(icon.Source)) return 0;
        if (icon.IsResource) return 0; // ms-appx:/// resources require WinRT path; shell overlay needs HICON.

        try
        {
            // LR_LOADFROMFILE | LR_DEFAULTSIZE — let the shell pick 16×16
            // from the .ico. LR_SHARED is unsafe across windows.
            const uint IMAGE_ICON = 1;
            const uint LR_LOADFROMFILE = 0x10;
            const uint LR_DEFAULTSIZE = 0x40;
            return LoadImageW(0, icon.Source, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] TaskbarOverlay.LoadIcon failed for '{icon.Source}': {ex.Message}");
            return 0;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
