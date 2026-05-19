using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Internal state for a window's thumbnail toolbar. Hidden behind
/// <see cref="ReactorWindow.SetThumbnailToolbar(IReadOnlyList{ThumbnailToolbarButton})"/>
/// / <see cref="ReactorWindow.ClearThumbnailToolbar"/>.
/// </summary>
internal sealed class ThumbnailToolbarState
{
    /// <summary>Hard cap from the shell — <c>ThumbBarAddButtons</c> rejects > 7.</summary>
    internal const int MaxButtons = 7;

    private readonly nint _hwnd;
    private bool _initialized;
    // Map index → managed button so WM_COMMAND can find the click delegate
    // (the shell sends back the iId we assigned, which is the slot index).
    private readonly List<Slot> _slots = new(MaxButtons);
    private readonly List<nint> _hIcons = new(MaxButtons); // owned, destroyed on Replace / Dispose.
    // Stable ids → slot mapping so app-supplied Id round-trips to the shell's iId.
    private readonly Dictionary<string, int> _idToSlot = new(StringComparer.Ordinal);

    public ThumbnailToolbarState(nint hwnd) => _hwnd = hwnd;

    private sealed record Slot(string Id, ThumbnailToolbarButton Button);

    public void Replace(IReadOnlyList<ThumbnailToolbarButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count > MaxButtons)
            throw new ArgumentException(
                $"Thumbnail toolbar supports at most {MaxButtons} buttons; got {buttons.Count}. (spec 036 §11.5)",
                nameof(buttons));

        // Validate uniqueness of ids — the shell tolerates duplicates but the
        // managed-side click dispatch would clobber.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in buttons)
        {
            ArgumentNullException.ThrowIfNull(b);
            if (string.IsNullOrEmpty(b.Id))
                throw new ArgumentException("ThumbnailToolbarButton.Id must be non-empty.", nameof(buttons));
            if (!seen.Add(b.Id))
                throw new ArgumentException(
                    $"ThumbnailToolbarButton.Id '{b.Id}' is duplicated.", nameof(buttons));
            if (b.OnClick is null)
                throw new ArgumentException(
                    $"ThumbnailToolbarButton '{b.Id}' must supply OnClick.", nameof(buttons));
        }

        var taskbar = TaskbarComSingleton.TryGet();
        if (taskbar is null) return;

        // Build the THUMBBUTTON wire array. The shell wants a fixed 7-slot
        // contract: any slot we don't fill must be marked Hidden.
        var native = new THUMBBUTTON[MaxButtons];
        var newIcons = new List<nint>(MaxButtons);
        for (int i = 0; i < MaxButtons; i++)
        {
            if (i < buttons.Count)
            {
                var b = buttons[i];
                var hIcon = LoadIconFor(b.Icon);
                if (hIcon != 0) newIcons.Add(hIcon);
                native[i] = new THUMBBUTTON
                {
                    dwMask = ThumbButtonMask.ICON | ThumbButtonMask.TOOLTIP | ThumbButtonMask.THBF_FLAGS,
                    iId = (uint)i,
                    iBitmap = 0,
                    hIcon = hIcon,
                    szTip = Truncate(b.Tooltip, 259),
                    dwFlags = (b.IsEnabled ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled)
                              | (b.IsVisible ? 0 : ThumbButtonFlags.Hidden)
                              | (b.DismissOnClick ? ThumbButtonFlags.DismissOnClick : 0),
                };
            }
            else
            {
                native[i] = new THUMBBUTTON
                {
                    dwMask = ThumbButtonMask.THBF_FLAGS,
                    iId = (uint)i,
                    szTip = string.Empty,
                    dwFlags = ThumbButtonFlags.Hidden | ThumbButtonFlags.NonInteractive,
                };
            }
        }

        try
        {
            int hr = _initialized
                ? taskbar.ThumbBarUpdateButtons(_hwnd, MaxButtons, native)
                : taskbar.ThumbBarAddButtons(_hwnd, MaxButtons, native);
            if (hr < 0)
            {
                DiagnosticLog.HResultFailed(LogCategory.Shell,
                    _initialized ? "ThumbnailToolbar.UpdateButtons" : "ThumbnailToolbar.AddButtons",
                    hr);
                FreeIcons(newIcons);
                return;
            }
            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] ThumbnailToolbar.Replace failed: {ex.GetType().Name}: {ex.Message}");
            FreeIcons(newIcons);
            return;
        }

        // Swap state only after a successful native call so a failure leaves
        // us with the previous map intact for click dispatch.
        FreeIcons(_hIcons);
        _hIcons.Clear();
        _hIcons.AddRange(newIcons);

        _slots.Clear();
        _idToSlot.Clear();
        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            _slots.Add(new Slot(b.Id, b));
            _idToSlot[b.Id] = i;
        }
    }

    /// <summary>WM_COMMAND dispatch — returns true if the id matched a slot.</summary>
    public bool TryDispatchClick(uint slot)
    {
        if (slot >= _slots.Count) return false;
        var b = _slots[(int)slot].Button;
        try { b.OnClick?.Invoke(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] ThumbnailToolbarButton[{b.Id}] click handler threw: {ex.Message}");
        }
        return true;
    }

    public void Dispose()
    {
        FreeIcons(_hIcons);
        _hIcons.Clear();
        _slots.Clear();
        _idToSlot.Clear();
    }

    private static void FreeIcons(IEnumerable<nint> icons)
    {
        foreach (var h in icons)
        {
            if (h == 0) continue;
            try { _ = DestroyIcon(h); } catch { /* best effort */ }
        }
    }

    private static nint LoadIconFor(WindowIcon icon)
    {
        if (icon is null || string.IsNullOrEmpty(icon.Source) || icon.IsResource) return 0;
        try
        {
            const uint IMAGE_ICON = 1;
            const uint LR_LOADFROMFILE = 0x10;
            const uint LR_DEFAULTSIZE = 0x40;
            return LoadImageW(0, icon.Source, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] ThumbnailToolbar.LoadIcon failed for '{icon.Source}': {ex.Message}");
            return 0;
        }
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
