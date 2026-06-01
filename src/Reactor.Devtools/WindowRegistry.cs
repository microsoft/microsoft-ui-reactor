using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Snapshot of a registered window as surfaced by <c>reactor.windows</c>.
/// </summary>
internal sealed record WindowInfo(
    string Id,
    string Title,
    long Hwnd,
    WindowBounds Bounds,
    bool IsMain,
    string BuildTag,
    string? Key,
    double WidthDip,
    double HeightDip,
    uint Dpi,
    string State);

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Tracks live WinUI windows and their stable MCP ids. Id assignment happens at
/// <see cref="Window.Activated"/> time; ids are reserved forever so a reopened
/// window with the same title takes a suffix, not the original id.
/// </summary>
internal sealed class WindowRegistry
{
    private readonly object _lock = new();
    private readonly WindowIdAllocator _allocator = new();
    private readonly List<Entry> _entries = new();
    private readonly string _buildTag;

    public WindowRegistry(string buildTag) => _buildTag = buildTag;

    /// <summary>
    /// Attaches to a window so it will appear in the registry on activation.
    /// Safe to call before or after Activated has fired — we also register the
    /// current state eagerly so tests don't need to pump the dispatcher.
    /// <paramref name="stableId"/> forces a specific id (e.g. <c>"main"</c> for
    /// the primary devtools window) so the handle stays the same even as the
    /// window's title changes on <c>switchComponent</c>.
    /// </summary>
    public void Attach(Window window, bool isMain = false, string? stableId = null)
    {
        RegisterCore(window, reactorWindow: null, isMain, stableId);
        // The Activated re-register subscription that used to live here was
        // dead code (RegisterCore early-exits when the entry is already
        // present) and racy on close (after Forget removes the entry, a
        // late Activated would hit RegisterCore's window.Title getter on
        // a window mid-teardown and COMException). Drop it.
        window.Closed += (_, _) => Forget(window);
    }

    /// <summary>
    /// Variant that retains a back-reference to the owning <see cref="ReactorWindow"/>.
    /// The MCP <c>windows.*</c> tools (spec 036 §10) use the back-ref to expose
    /// per-window DPI / state / key without re-walking <see cref="ReactorApp.Windows"/>.
    /// </summary>
    public void Attach(ReactorWindow window, bool isMain = false, string? stableId = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        RegisterCore(window.NativeWindow, window, isMain, stableId);
        // Forget on close. Note: we do NOT re-register on Activated. The
        // initial RegisterCore covers the entry's lifetime, and re-firing
        // RegisterCore from a late Activated event after Forget already ran
        // hits a COMException reading window.Title on a window the OS is
        // tearing down. The early-exit on line 87 of RegisterCore makes a
        // re-register a no-op anyway, so the Activated subscription was
        // dead code before this fix and racy after Close.
        window.NativeWindow.Closed += (_, _) => Forget(window.NativeWindow);
    }

    /// <summary>
    /// Explicit detach. Idempotent. The <see cref="Window.Closed"/> subscription
    /// installed by <see cref="Attach(Window, bool, string?)"/> already calls
    /// this on close — exposed so callers driving the registry from
    /// <see cref="ReactorApp.WindowClosed"/> can detach without waiting on the
    /// native event.
    /// </summary>
    public void Detach(ReactorWindow window)
    {
        if (window is null) return;
        Forget(window.NativeWindow);
    }

    private void RegisterCore(Window window, ReactorWindow? reactorWindow, bool isMain, string? stableId)
    {
        lock (_lock)
        {
            // Update the back-reference if we already know about this window
            // and the caller has supplied a fresher one.
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].Window.Target, window))
                {
                    if (reactorWindow is not null && _entries[i].ReactorWindow?.Target is not ReactorWindow)
                        _entries[i] = _entries[i] with { ReactorWindow = new WeakReference(reactorWindow) };
                    return;
                }
            }

            // The devtools main window pins to "main" so the id survives
            // switchComponent (which updates the window title). Secondary
            // windows fall through to the title-based allocator.
            // window.Title can throw COMException when the window's native
            // peer is mid-teardown — fall back to a generic seed so a late
            // registration doesn't crash the close path.
            string title;
            try { title = window.Title ?? string.Empty; }
            catch (Exception ex)
            {
                global::System.Diagnostics.Debug.WriteLine($"[Reactor] WindowRegistry.RegisterCore Title getter threw: {ex.Message}");
                title = "(unknown)";
            }
            var id = stableId is not null
                ? _allocator.Reserve(stableId)
                : _allocator.Allocate(title);
            _entries.Add(new Entry(
                id,
                new WeakReference(window),
                reactorWindow is null ? null : new WeakReference(reactorWindow),
                isMain));
        }
    }

    private void Forget(Window window)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => ReferenceEquals(e.Window.Target, window));
        }
    }

    /// <summary>Returns a snapshot of active windows for the <c>windows</c> tool.</summary>
    public IReadOnlyList<WindowInfo> Snapshot()
    {
        lock (_lock)
        {
            var result = new List<WindowInfo>(_entries.Count);
            foreach (var entry in _entries)
            {
                if (entry.Window.Target is not Window w) continue;
                var bounds = ReadBounds(w);
                var rw = entry.ReactorWindow?.Target as ReactorWindow;
                result.Add(new WindowInfo(
                    Id: entry.Id,
                    Title: w.Title ?? "",
                    Hwnd: TryGetHwnd(w),
                    Bounds: bounds,
                    IsMain: entry.IsMain,
                    BuildTag: _buildTag,
                    Key: rw?.Key?.ToString(),
                    WidthDip: rw is null ? 0 : ToDip(bounds.Width, rw.Dpi),
                    HeightDip: rw is null ? 0 : ToDip(bounds.Height, rw.Dpi),
                    Dpi: rw?.Dpi ?? 96,
                    State: rw?.State.ToString() ?? "Normal"));
            }
            return result;
        }
    }

    private static double ToDip(int physical, uint dpi)
    {
        var d = dpi == 0 ? 96 : dpi;
        return physical * 96.0 / d;
    }

    /// <summary>Resolves an id to its Window. Returns null if not registered or disposed.</summary>
    public Window? Resolve(string id)
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Id == id && entry.Window.Target is Window w)
                    return w;
            }
            return null;
        }
    }

    /// <summary>
    /// Resolves an id to its <see cref="ReactorWindow"/> back-reference, or
    /// null if the entry was attached via the legacy <see cref="Attach(Window, bool, string?)"/>
    /// overload (devtools-only test paths).
    /// </summary>
    public ReactorWindow? ResolveReactorWindow(string id)
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Id == id && entry.ReactorWindow?.Target is ReactorWindow rw)
                    return rw;
            }
            return null;
        }
    }

    /// <summary>
    /// When exactly one window is registered, returns it; when multiple are
    /// registered, returns null so callers can error with the available ids.
    /// </summary>
    public Window? TryDefault(out IReadOnlyList<string> activeIds)
    {
        lock (_lock)
        {
            activeIds = _entries
                .Where(e => e.Window.Target is Window)
                .Select(e => e.Id)
                .ToArray();

            if (activeIds.Count == 1)
                return _entries.First(e => e.Window.Target is Window).Window.Target as Window;
            return null;
        }
    }

    private static long TryGetHwnd(Window w)
    {
        try
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(w).ToInt64();
        }
        catch { return 0; }
    }

    private static WindowBounds ReadBounds(Window w)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
            if (GetWindowRect(hwnd, out var r))
                return new WindowBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        catch { }
        return new WindowBounds(0, 0, 0, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    // Unit-test hooks ------------------------------------------------------------

    internal WindowIdAllocator AllocatorForTests => _allocator;

    private sealed record Entry(string Id, WeakReference Window, WeakReference? ReactorWindow, bool IsMain);
}
