using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §4.2 / §4.3 — cross-window tab dock-in router.
//
//  WinUI's `TabView.CanDragTabs` drag pipeline is window-local: drag
//  events (`DragEnter` / `DragOver` / `Drop`) fire only on `AllowDrop`
//  elements inside the SAME XAML island as the source TabView. A drag
//  initiated in window A does NOT deliver events to AllowDrop elements
//  in window B — so the `DockDropTargetOverlay` mounted inside a floating
//  window never sees the drag and can't reveal its drop targets.
//
//  This router lets the SOURCE host (the window that owns the dragged
//  pane) detect at drop-completion time whether the cursor is over a
//  registered floating window, and if so, route the pane there as a
//  new tab (the "Add as tab" / Center target — spec §4.2 limits the
//  cross-window dock-in to Center to avoid the splits-in-titlebar
//  problem documented in §4.3).
//
//  Each `DockFloatingWindowComponent` registers itself on mount with a
//  closure that appends a pane to its local pane state, and unregisters
//  on unmount. The source host iterates the registry, checks the
//  cursor's screen position against each registered window's HWND rect,
//  and invokes the matching appender when a hit is found.
// ════════════════════════════════════════════════════════════════════════

internal static class DockFloatingPaneRouter
{
    // Lock around the dictionary — Register / Unregister are called
    // from the UI thread but TryAppendUnderCursor is called from
    // tab-drag-completed which is also UI thread; the lock guards
    // against future scenarios where a teardown races a hit-test.
    private static readonly object _gate = new();
    private static readonly Dictionary<ReactorWindow, Action<DockableContent>> _appenders = new();

    /// <summary>
    /// Registers an "append-as-tab" callback for the given floating window.
    /// Called by <see cref="DockFloatingWindowComponent"/> from its
    /// <c>UseEffect</c> mount handler.
    /// </summary>
    public static void Register(ReactorWindow window, Action<DockableContent> append)
    {
        lock (_gate) _appenders[window] = append;
    }

    /// <summary>
    /// Removes the registration. Called from the matching <c>UseEffect</c>
    /// cleanup so the dictionary doesn't leak when a floating window closes.
    /// </summary>
    public static void Unregister(ReactorWindow window)
    {
        lock (_gate) _appenders.Remove(window);
    }

    /// <summary>
    /// Hit-tests the current cursor screen position against every registered
    /// floating window's HWND rect. When a hit is found, invokes that
    /// window's appender with <paramref name="pane"/> and returns
    /// <c>true</c>. Returns <c>false</c> when the cursor is not over any
    /// registered floating window (caller should fall back to its normal
    /// tear-out / cancel path).
    /// </summary>
    public static bool TryAppendUnderCursor(DockableContent pane)
    {
        if (!NativeInterop.GetCursorPos(out var cursor))
            return false;

        // Copy the snapshot under lock so we can iterate without holding
        // it across the appender invocation (which mutates component
        // state and may re-enter).
        KeyValuePair<ReactorWindow, Action<DockableContent>>[] snapshot;
        lock (_gate)
        {
            if (_appenders.Count == 0) return false;
            snapshot = new KeyValuePair<ReactorWindow, Action<DockableContent>>[_appenders.Count];
            int i = 0;
            foreach (var kv in _appenders) snapshot[i++] = kv;
        }

        for (int i = 0; i < snapshot.Length; i++)
        {
            var win = snapshot[i].Key;
            global::Microsoft.UI.Xaml.Window? native;
            // Typed catches for the recoverable cases we expect when a
            // floating window is mid-close / disposed / WinRT proxy is
            // torn down. Anything else propagates so unknown failures
            // don't get silently swallowed.
            try { native = win.NativeWindow; }
            catch (COMException) { continue; }
            catch (ObjectDisposedException) { continue; }
            catch (InvalidOperationException) { continue; }
            if (native is null) continue;
            nint hwnd;
            try { hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native); }
            catch (COMException) { continue; }
            catch (ObjectDisposedException) { continue; }
            catch (InvalidOperationException) { continue; }
            if (hwnd == 0) continue;
            if (!NativeInterop.GetWindowRect(hwnd, out var rect)) continue;
            if (cursor.X < rect.Left || cursor.X >= rect.Right) continue;
            if (cursor.Y < rect.Top || cursor.Y >= rect.Bottom) continue;

            // Bring the window forward so the user has visible
            // confirmation that the pane landed there (matches the
            // perceived UX of "I dropped it on the floating window").
            // SetForegroundWindow is best-effort: Windows refuses the
            // foreground promotion under several documented conditions
            // (foreground-lock timer, no input focus on caller) — these
            // surface as a benign Win32 SetLastError, not an exception,
            // but be defensive about COM/disposed shutdowns regardless.
            try { NativeInterop.SetForegroundWindow(hwnd); }
            catch (COMException) { /* best-effort */ }
            catch (ObjectDisposedException) { /* best-effort */ }
            snapshot[i].Value(pane);
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when any floating window is currently registered. Lets the
    /// source host fast-skip the hit-test when no floating windows exist.
    /// </summary>
    public static bool HasRegisteredWindows
    {
        get { lock (_gate) return _appenders.Count > 0; }
    }

    // ── Win32 hit-test interop ────────────────────────────────────────
    private static class NativeInterop
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint hwnd);
    }
}
