using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Per-process WinUI window that hosts the reconciled content of a tray icon's
/// flyout. Borderless, non-resizable, hidden from the taskbar / Alt-Tab; auto-
/// dismisses on deactivation. (spec 036 §11.4)
/// </summary>
/// <remarks>
/// <para>One instance per process — only one tray flyout can be open at a
/// time, so a single host window is reused across every
/// <see cref="ReactorTrayIcon"/>. Lazily created on the first
/// <see cref="ReactorTrayIcon.ShowFlyout"/> call. Disposed on
/// <see cref="ReactorApp.Exit"/> via the static <see cref="ResetForTests"/>
/// path or on process teardown.</para>
/// <para>The host owns a transient <see cref="ReactorHost"/> for each show:
/// the previous host is disposed before the next mount so the flyout content
/// gets a fresh hook state per invocation. Components inside the flyout's
/// element tree still get their own <c>UseState</c> / <c>UseEffect</c>
/// lifetimes scoped to that single show.</para>
/// </remarks>
internal sealed class TrayFlyoutHostWindow : IDisposable
{
    private static TrayFlyoutHostWindow? s_instance;
    private static readonly object s_lock = new();

    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private ReactorHost? _host;
    private bool _disposed;
    private bool _isShowing;

    /// <summary>Acquire the per-process host, creating it on first call. UI-thread only.</summary>
    public static TrayFlyoutHostWindow GetOrCreate()
    {
        var existing = Volatile.Read(ref s_instance);
        if (existing is not null) return existing;
        lock (s_lock)
        {
            if (s_instance is not null) return s_instance;
            var instance = new TrayFlyoutHostWindow();
            Volatile.Write(ref s_instance, instance);
            return instance;
        }
    }

    /// <summary>Dismiss the currently visible flyout, if any. UI-thread only. Idempotent.</summary>
    public static void HideIfShown()
    {
        var existing = Volatile.Read(ref s_instance);
        existing?.Hide();
    }

    /// <summary>Test hook — destroy the singleton between fixtures.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_instance?.Dispose();
            s_instance = null;
        }
    }

    private TrayFlyoutHostWindow()
    {
        _window = new Window { Title = "ReactorTrayFlyout" };
        _appWindow = _window.AppWindow;

        try { _appWindow.IsShownInSwitchers = false; }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] TrayFlyout IsShownInSwitchers failed: {ex.Message}"); }

        try
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_appWindow.Presenter is OverlappedPresenter op)
            {
                op.IsResizable = false;
                op.IsMinimizable = false;
                op.IsMaximizable = false;
                op.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
                op.IsAlwaysOnTop = true;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] TrayFlyout presenter setup failed: {ex.Message}"); }

        _window.Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Light-dismiss on focus loss — clicking outside the flyout, pressing
        // Esc, or invoking another window all deactivate this one.
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isShowing)
            Hide();
    }

    /// <summary>
    /// Mount the flyout content via a fresh <see cref="ReactorHost"/>, position
    /// the window at the current cursor, and activate it. UI-thread only.
    /// </summary>
    public void Show(Element flyoutContent)
    {
        if (_disposed) return;
        ArgumentNullException.ThrowIfNull(flyoutContent);

        // Tear down the previous mount before re-mounting so the flyout gets
        // a fresh hook state per invocation.
        try { _host?.Dispose(); } catch { /* best effort */ }
        _host = new ReactorHost(_window);
        _host.Mount(_ => flyoutContent);

        // Position at cursor. Default size is intentionally compact — the
        // content's own measure-arrange will lay out within these bounds; apps
        // can size their root container if they need more space. We don't
        // auto-fit because UpdateLayout-then-resize incurs an extra layout
        // pass and a visible flicker.
        try
        {
            if (TrayIconComInterop.GetCursorPos(out var pt))
            {
                _appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(pt.x, pt.y, 280, 360));
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] TrayFlyout position failed: {ex.Message}"); }

        _isShowing = true;
        try { _window.Activate(); }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] TrayFlyout Activate failed: {ex.Message}"); }
    }

    /// <summary>Hide the flyout without disposing the cached host window. Idempotent.</summary>
    public void Hide()
    {
        if (!_isShowing) return;
        _isShowing = false;
        try { _appWindow.Hide(); }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] TrayFlyout Hide failed: {ex.Message}"); }

        // Dispose the per-show host so its hook-state cleanups run promptly;
        // the next Show creates a new host on the same window.
        try { _host?.Dispose(); } catch { /* best effort */ }
        _host = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _window.Activated -= OnActivated; } catch { /* best effort */ }
        try { _host?.Dispose(); } catch { /* best effort */ }
        _host = null;
        try { _window.Close(); } catch { /* best effort */ }
    }
}
