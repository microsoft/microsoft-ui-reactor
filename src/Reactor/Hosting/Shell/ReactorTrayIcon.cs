using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Top-level system-tray icon — a peer of <see cref="ReactorWindow"/>.
/// Created via <see cref="ReactorApp.OpenTrayIcon"/>. Provides the icon
/// bitmap, tooltip, and click events; reconciles a Reactor
/// <see cref="Element"/> as its flyout via <see cref="ShowFlyout"/>.
/// (spec 036 §11.4)
/// </summary>
/// <remarks>
/// <para>Public mutators must run on the UI thread captured by
/// <see cref="ReactorApp.UIDispatcher"/>. Disposal is idempotent — a
/// second <see cref="Close"/> / <see cref="Dispose"/> call is a no-op.</para>
/// <para>The flyout-content reconciliation runs in a <see cref="RenderContext"/>
/// without an owning <see cref="ReactorWindow"/>; hooks like
/// <see cref="RenderContext.UseWindow"/> return their documented fallback
/// values (spec 036 §7.1).</para>
/// </remarks>
public sealed class ReactorTrayIcon : IDisposable
{
    private static int s_nextId;
    private static int s_nextShellIconId = 1;

    private readonly string _id;
    private readonly uint _shellIconId;
    private TrayIconSpec _spec;
    private nint _hIcon;
    private bool _disposed;
    private bool _registered;
    private TrayCallbackEntry? _callbacks;

    /// <summary>Stable id, e.g. <c>"tray-1"</c>. Allocated monotonically per process.</summary>
    public string Id => _id;

    /// <summary>Optional stable identity (from <see cref="TrayIconSpec.Key"/>).</summary>
    public WindowKey? Key => _spec.Key;

    /// <summary>Last-applied <see cref="TrayIconSpec"/> snapshot.</summary>
    public TrayIconSpec Spec => _spec;

    /// <summary>The icon bitmap source. Mutating triggers a shell re-apply.</summary>
    public WindowIcon Icon
    {
        get => _spec.Icon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(Icon));
            if (ReferenceEquals(_spec.Icon, value)) return;
            _spec = _spec with { Icon = value };
            // Drop the cached HICON so ApplyToShell reloads from the new
            // source. ApplyToShell only refreshes when _hIcon == 0 or on
            // NIM_ADD, so without this the tray would keep showing the old
            // bitmap on a NIM_MODIFY.
            if (_hIcon != 0)
            {
                try { TrayIconComInterop.DestroyIcon(_hIcon); } catch { }
                _hIcon = 0;
            }
            ApplyToShell(TrayIconComInterop.NIM_MODIFY);
        }
    }

    /// <summary>Tooltip text. Mutating triggers a shell re-apply.</summary>
    public string Tooltip
    {
        get => _spec.Tooltip;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(Tooltip));
            _spec = _spec with { Tooltip = value };
            ApplyToShell(TrayIconComInterop.NIM_MODIFY);
        }
    }

    /// <summary>Whether the icon is currently shown. Mutating triggers a shell re-apply.</summary>
    public bool IsVisible
    {
        get => _spec.IsVisible;
        set
        {
            ThreadAffinity.ThrowIfNotOnUIThread(nameof(IsVisible));
            _spec = _spec with { IsVisible = value };
            ApplyToShell(TrayIconComInterop.NIM_MODIFY);
        }
    }

    /// <summary>
    /// Fires on the UI thread when the user left-clicks the icon.
    /// <para><b>Security:</b> see <see cref="DoubleClick"/> remarks. Treat
    /// click handlers as triggers for reversible UI actions only.</para>
    /// </summary>
    public event EventHandler? Click;

    /// <summary>
    /// Fires on the UI thread when the user double-clicks the icon.
    /// <para><b>Security — clicks are not authenticated:</b> the tray click
    /// callback arrives as a Win32 <c>WM_APP+1</c> message that any process at
    /// the same Integrity Level can synthesise via <c>PostMessage</c> to the
    /// hidden tray window. Use click handlers for reversible UI actions only
    /// (open a window, toggle state, show a flyout); gate any
    /// privileged or destructive operation behind a deliberate in-app
    /// confirmation step. (W-6, threat model 2026-05-08.)</para>
    /// </summary>
    public event EventHandler? DoubleClick;

    /// <summary>
    /// Fires on the UI thread when the user right-clicks the icon.
    /// <para><b>Security:</b> see <see cref="DoubleClick"/> remarks.</para>
    /// </summary>
    public event EventHandler? RightClick;

    internal ReactorTrayIcon(TrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
        _id = $"tray-{Interlocked.Increment(ref s_nextId)}";
        _shellIconId = (uint)Interlocked.Increment(ref s_nextShellIconId);
    }

    /// <summary>
    /// Register the icon with the shell. UI-thread only. Called once by
    /// <see cref="ReactorApp.OpenTrayIcon"/>; subsequent state changes flow
    /// through <see cref="Update"/> or the property setters.
    /// </summary>
    internal void RegisterWithShell()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(RegisterWithShell));
        if (_registered) return;

        var hidden = TrayHiddenWindow.GetOrCreate();
        _callbacks = new TrayCallbackEntry
        {
            OnClick = () => Click?.Invoke(this, EventArgs.Empty),
            OnDoubleClick = () => DoubleClick?.Invoke(this, EventArgs.Empty),
            OnRightClick = () => RightClick?.Invoke(this, EventArgs.Empty),
            OnReapply = ReapplyAfterShellRestart,
        };
        hidden.Register(_shellIconId, _callbacks);

        ApplyShellAdd(hidden.Hwnd);

        _registered = true;
    }

    private void ApplyShellAdd(nint hiddenHwnd)
    {
        ApplyToShell(TrayIconComInterop.NIM_ADD);

        // Switch to v4 semantics so the wParam/lParam shape matches what
        // TrayHiddenWindow's WndProc expects.
        try
        {
            var data = BuildNotifyIconData(_spec, _hIcon, hiddenHwnd, _shellIconId);
            data.uVersionOrTimeout = TrayIconComInterop.NOTIFYICON_VERSION_4;
            _ = TrayIconComInterop.Shell_NotifyIconW(TrayIconComInterop.NIM_SETVERSION, ref data);
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] NIM_SETVERSION failed: {ex.Message}"); }
    }

    /// <summary>
    /// Re-add this icon to the shell after Explorer broadcasts
    /// <c>TaskbarCreated</c> (Explorer restart, sign-out transition). Runs on
    /// the UI dispatcher; <see cref="TrayHiddenWindow"/> dispatches us here.
    /// </summary>
    private void ReapplyAfterShellRestart()
    {
        if (_disposed || !_registered) return;
        // Drop the cached HICON so we reload from the current icon source —
        // some shell-restart transitions invalidate cached handles, and a
        // fresh load is cheap. (spec 036 §11.4)
        if (_hIcon != 0)
        {
            try { TrayIconComInterop.DestroyIcon(_hIcon); } catch { }
            _hIcon = 0;
        }
        try { ApplyShellAdd(TrayHiddenWindow.GetOrCreate().Hwnd); }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] Tray reapply after TaskbarCreated failed: {ex.Message}"); }
    }

    private void ApplyToShell(uint message)
    {
        if (_disposed) return;
        try
        {
            var hidden = TrayHiddenWindow.GetOrCreate();
            // Refresh the HICON if the path/source has changed and we haven't
            // already loaded one.
            if (message == TrayIconComInterop.NIM_ADD || _hIcon == 0)
                _hIcon = LoadHIcon(_spec.Icon);

            var data = BuildNotifyIconData(_spec, _hIcon, hidden.Hwnd, _shellIconId);
            if (!TrayIconComInterop.Shell_NotifyIconW(message, ref data))
            {
                var err = global::System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Debug.WriteLine($"[Reactor] Shell_NotifyIcon (msg=0x{message:X}) failed: 0x{err:X8}");
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] ApplyToShell failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static TrayIconComInterop.NOTIFYICONDATAW BuildNotifyIconData(
        TrayIconSpec spec, nint hIcon, nint hWnd, uint id)
    {
        return new TrayIconComInterop.NOTIFYICONDATAW
        {
            cbSize = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<TrayIconComInterop.NOTIFYICONDATAW>(),
            hWnd = hWnd,
            uID = id,
            uFlags = TrayIconComInterop.NIF_MESSAGE
                   | TrayIconComInterop.NIF_ICON
                   | TrayIconComInterop.NIF_TIP
                   | TrayIconComInterop.NIF_SHOWTIP
                   | TrayIconComInterop.NIF_STATE,
            uCallbackMessage = TrayHiddenWindow.TrayCallbackMessage,
            hIcon = hIcon,
            szTip = TruncateForTip(spec.Tooltip),
            dwState = spec.IsVisible ? 0u : TrayIconComInterop.NIS_HIDDEN,
            dwStateMask = TrayIconComInterop.NIS_HIDDEN,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private static string TruncateForTip(string tip)
    {
        // szTip is a fixed 128 char buffer; truncate to fit so the WinRT
        // marshaller doesn't reject the call.
        if (string.IsNullOrEmpty(tip)) return string.Empty;
        return tip.Length <= 127 ? tip : tip.Substring(0, 127);
    }

    private static nint LoadHIcon(WindowIcon icon)
    {
        try
        {
            // Resource icons (ms-appx:///) aren't directly loadable via
            // LoadImageW — apps that need a tray icon for a packaged app
            // should ship a sidecar .ico and use FromPath. Silently skip.
            if (icon.IsResource)
            {
                Debug.WriteLine($"[Reactor] Tray icon: ms-appx resources cannot be loaded as HICON; ship a sidecar .ico file.");
                return 0;
            }

            // Tray icons live on the taskbar's notification area, which is
            // sized in the system DPI — so prefer GetSystemMetricsForDpi at
            // GetDpiForSystem() over the system-DPI-locked GetSystemMetrics.
            // Apps should still ship a multi-size .ico (16, 20, 24, 32, 40 px
            // assets) so LoadImageW can pick the closest in-file frame.
            uint dpi = 96;
            try { dpi = TrayIconComInterop.GetDpiForSystem(); }
            catch { /* fallback below */ }
            if (dpi == 0) dpi = 96;

            int cx, cy;
            try
            {
                cx = TrayIconComInterop.GetSystemMetricsForDpi(TrayIconComInterop.SM_CXSMICON, dpi);
                cy = TrayIconComInterop.GetSystemMetricsForDpi(TrayIconComInterop.SM_CYSMICON, dpi);
            }
            catch
            {
                cx = TrayIconComInterop.GetSystemMetrics(TrayIconComInterop.SM_CXSMICON);
                cy = TrayIconComInterop.GetSystemMetrics(TrayIconComInterop.SM_CYSMICON);
            }
            if (cx <= 0) cx = 16;
            if (cy <= 0) cy = 16;

            // LR_DEFAULTSIZE is intentionally omitted — it would override the
            // explicit size we just resolved when cx/cy are zero. Here cx/cy
            // are the DPI-aware target so LoadImage selects the closest frame.
            return TrayIconComInterop.LoadImageW(
                0,
                icon.Source,
                TrayIconComInterop.IMAGE_ICON,
                cx, cy,
                TrayIconComInterop.LR_LOADFROMFILE);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] LoadHIcon failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Show a Reactor <see cref="Element"/> as the tray icon's flyout. UI-thread
    /// only. The flyout reconciles in a context without an owning window
    /// (<see cref="RenderContext.UseWindow"/> returns null inside).
    /// Light-dismisses automatically when the user clicks outside it or
    /// presses Escape.
    /// (spec 036 §7.1, §11.4)
    /// </summary>
    public void ShowFlyout(Element flyoutContent)
    {
        ArgumentNullException.ThrowIfNull(flyoutContent);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(ShowFlyout));
        if (_disposed) return;

        var host = TrayFlyoutHostWindow.GetOrCreate();
        host.Show(flyoutContent);
    }

    /// <summary>Dismiss the tray flyout. UI-thread only. Idempotent.</summary>
    public void HideFlyout()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(HideFlyout));
        TrayFlyoutHostWindow.HideIfShown();
    }

    /// <summary>
    /// Diff <paramref name="next"/> against the current spec and re-apply
    /// only changed fields. UI-thread only.
    /// </summary>
    public void Update(TrayIconSpec next)
    {
        ArgumentNullException.ThrowIfNull(next);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Update));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorTrayIcon));

        var prev = _spec;
        _spec = next;
        if (!Equals(prev, next))
        {
            // Reload the HICON if the icon source changed.
            if (!ReferenceEquals(prev.Icon, next.Icon))
            {
                if (_hIcon != 0)
                {
                    try { TrayIconComInterop.DestroyIcon(_hIcon); } catch { }
                    _hIcon = 0;
                }
            }
            ApplyToShell(TrayIconComInterop.NIM_MODIFY);
        }
    }

    /// <summary>
    /// Remove the icon from the tray and dispose. UI-thread only. Idempotent.
    /// </summary>
    public void Close() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Dispose));
        _disposed = true;

        if (_registered)
        {
            try
            {
                var hidden = TrayHiddenWindow.GetOrCreate();
                var data = BuildNotifyIconData(_spec, _hIcon, hidden.Hwnd, _shellIconId);
                _ = TrayIconComInterop.Shell_NotifyIconW(TrayIconComInterop.NIM_DELETE, ref data);
                hidden.Unregister(_shellIconId);
            }
            catch (Exception ex) { Debug.WriteLine($"[Reactor] NIM_DELETE failed: {ex.Message}"); }
            _registered = false;
        }

        if (_hIcon != 0)
        {
            try { TrayIconComInterop.DestroyIcon(_hIcon); } catch { }
            _hIcon = 0;
        }

        _callbacks = null;

        ReactorApp.UnregisterTrayIcon(this);
    }
}
