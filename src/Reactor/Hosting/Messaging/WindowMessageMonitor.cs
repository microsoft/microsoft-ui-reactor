using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Messaging;

/// <summary>
/// Spec 036 §5.1 — minimal Win32 message-monitor for the lifted-XAML
/// <see cref="Microsoft.UI.Xaml.Window"/> HWND. Subclasses the HWND via
/// the COMCTL32 <c>SetWindowSubclass</c> API and surfaces the messages
/// Reactor cares about (DPI changes, min/max constraints, sizing edges,
/// show/hide).
/// </summary>
/// <remarks>
/// <para>Threading: <c>WndProc</c> runs on the lifted-XAML message pump's
/// UI thread. Subscribers' handlers run on that thread; do not marshal
/// heavy work into the WndProc — return quickly so paint / input continue.</para>
/// <para>AOT / trim safety: the WndProc is a <c>[UnmanagedCallersOnly]</c>
/// static; instance recovery uses the <c>dwRefData</c> slot of
/// <c>SetWindowSubclass</c> to round-trip a <see cref="GCHandle"/>.
/// No reflection, no dynamic delegates.</para>
/// </remarks>
internal sealed class WindowMessageMonitor : IDisposable
{
    /// <summary>WM_DPICHANGED — sent when the per-monitor DPI for the window changes.</summary>
    public const uint WM_DPICHANGED = 0x02E0;

    /// <summary>WM_GETMINMAXINFO — sent during resize so we can return DIP-correct min/max.</summary>
    public const uint WM_GETMINMAXINFO = 0x0024;

    /// <summary>WM_SHOWWINDOW — toggled when the window is being shown/hidden.</summary>
    public const uint WM_SHOWWINDOW = 0x0018;

    /// <summary>WM_SIZING — drag-resize feedback. Used by Phase 2 to mark <c>_userResized = true</c>.</summary>
    public const uint WM_SIZING = 0x0214;

    /// <summary>WM_ENTERSIZEMOVE — start of a modal resize/drag loop.</summary>
    public const uint WM_ENTERSIZEMOVE = 0x0231;

    /// <summary>WM_EXITSIZEMOVE — end of a modal resize/drag loop.</summary>
    public const uint WM_EXITSIZEMOVE = 0x0232;

    /// <summary>WM_COMMAND — menu and thumbnail-toolbar button clicks.</summary>
    public const uint WM_COMMAND = 0x0111;

    private readonly nint _hwnd;
    private GCHandle _selfHandle;
    private bool _subclassed;
    private readonly nuint _subclassId;
    private bool _disposed;

    // Per-process monotonic subclass id so concurrent monitors on different
    // windows never collide on COMCTL32's (HWND, SubclassProc, idSubclass) key.
    // Counter is unbounded; >2^31 monitors over a process lifetime would wrap
    // — implausible for a desktop session, but Debug.Assert below would catch
    // a regression that creates monitors in a tight loop without disposing.
    private const nuint SubclassIdMaxBeforeWarn = (nuint)int.MaxValue;
    private static nuint s_nextSubclassId = 1001;
    private static readonly object s_subclassIdLock = new();

    /// <summary>The HWND being monitored.</summary>
    public nint Hwnd => _hwnd;

    public WindowMessageMonitor(nint hwnd)
    {
        if (hwnd == 0) throw new ArgumentException("HWND must be non-zero.", nameof(hwnd));
        _hwnd = hwnd;
        lock (s_subclassIdLock)
        {
            _subclassId = s_nextSubclassId++;
            Debug.Assert(s_nextSubclassId < SubclassIdMaxBeforeWarn,
                "WindowMessageMonitor subclass-id counter is approaching int.MaxValue; check for monitor leaks.");
        }
    }

    /// <summary>
    /// Construct from a WinUI <see cref="Microsoft.UI.Xaml.Window"/> by extracting
    /// its HWND via <c>WindowNative.GetWindowHandle</c>.
    /// </summary>
    public static WindowMessageMonitor ForWindow(Microsoft.UI.Xaml.Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        return new WindowMessageMonitor(hwnd);
    }

    private event EventHandler<WindowMessageEventArgs>? _messageReceived;

    /// <summary>
    /// Fires for every Windows message routed through this window. Set
    /// <see cref="WindowMessageEventArgs.Handled"/> to <c>true</c> to short-circuit
    /// <c>DefSubclassProc</c>.
    /// </summary>
    public event EventHandler<WindowMessageEventArgs> MessageReceived
    {
        add
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowMessageMonitor));
            EnsureSubclassed();
            _messageReceived += value;
        }
        remove
        {
            _messageReceived -= value;
        }
    }

    private unsafe void EnsureSubclassed()
    {
        if (_subclassed) return;
        if (_disposed) return;

        // Strong handle: COMCTL32's dwRefData slot is the *only* reference
        // SubclassProcStatic dereferences on every WM_*. A weak handle would
        // let the runtime recycle the same numeric handle id to an unrelated
        // object after GC, at which point the static WndProc would resolve
        // dwRefData to whatever now lives at that slot (the `is WindowMessageMonitor`
        // cast catches type mismatches but not WMM-to-WMM recycling).
        // Invariant: the strong handle is freed *only after* RemoveWindowSubclass
        // returns successfully (see Dispose). The finalizer below relies on
        // this — a strong root keeps the WMM rooted, so the finalizer can only
        // run once Dispose has already cleared `_subclassed`.
        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        // SetWindowSubclass takes a function pointer; we publish the static
        // [UnmanagedCallersOnly] WndProc via &SubclassProcStatic.
        var ok = SetWindowSubclass(_hwnd, &SubclassProcStatic, _subclassId, (nuint)(nint)GCHandle.ToIntPtr(_selfHandle));
        if (!ok)
        {
            // Comctl32 missing or HWND no longer valid — leave subclass off,
            // monitor degrades to a no-op (no events ever fire). Free the
            // strong handle so we don't leak the WMM permanently.
            Debug.WriteLine($"[Reactor] WindowMessageMonitor: SetWindowSubclass failed for HWND {_hwnd:X}.");
            _selfHandle.Free();
            return;
        }
        _subclassed = true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint SubclassProcStatic(nint hwnd, uint msg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        // Resolve the monitor instance from the dwRefData slot. If the GCHandle
        // is no longer alive we fall through to DefSubclassProc — this can
        // happen during Dispose between Free() and RemoveWindowSubclass().
        try
        {
            var handlePtr = (nint)dwRefData;
            if (handlePtr != 0)
            {
                var gch = GCHandle.FromIntPtr(handlePtr);
                if (gch.IsAllocated && gch.Target is WindowMessageMonitor monitor)
                {
                    var handler = monitor._messageReceived;
                    if (handler is not null)
                    {
                        var args = new WindowMessageEventArgs(hwnd, msg, wParam, lParam);
                        try { handler.Invoke(monitor, args); }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Reactor] WindowMessageMonitor handler threw: {ex.Message}");
                        }
                        if (args.Handled)
                            return args.Result;
                    }
                }
            }
        }
        catch
        {
            // Swallow — DefSubclassProc must always be called for non-handled cases.
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Order matters: RemoveWindowSubclass *must* return before we Free the
        // strong handle, otherwise COMCTL32 could dispatch one more WM_* through
        // a stale dwRefData. SetWindowSubclass / RemoveWindowSubclass / WndProc
        // all run on the same UI thread, so as long as Dispose itself is called
        // on the UI thread, no concurrent dispatch races this teardown.
        unsafe
        {
            if (_subclassed)
            {
                try { RemoveWindowSubclass(_hwnd, &SubclassProcStatic, _subclassId); }
                catch { /* best effort — HWND may already be destroyed */ }
                _subclassed = false;
            }
        }
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        GC.SuppressFinalize(this);
    }

    ~WindowMessageMonitor()
    {
        // With a strong GCHandle, the only way the finalizer can run is when
        // Dispose has already freed the handle (which clears the strong root).
        // That means by the time we get here, _subclassed is already false and
        // the native subclass is gone. We deliberately do NOT call
        // RemoveWindowSubclass here: the finalizer runs on the GC thread, but
        // SetWindowSubclass / RemoveWindowSubclass are documented to be called
        // on the thread that owns the HWND (the UI thread). A cross-thread
        // RemoveWindowSubclass races against any in-flight WM_* dispatch on
        // the UI thread.
        // If you see this assertion fire, somebody constructed a WindowMessageMonitor,
        // attached a subscriber, and never disposed it — fix that, don't suppress.
        Debug.Assert(!_subclassed,
            "WindowMessageMonitor finalized while still subclassed; Dispose() was missed.");
    }

    // ── COMCTL32 PInvokes ──────────────────────────────────────────────

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool SetWindowSubclass(
        nint hWnd,
        delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool RemoveWindowSubclass(
        nint hWnd,
        delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass);

    [DllImport("Comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);
}

/// <summary>
/// Carries one Windows message routed through <see cref="WindowMessageMonitor"/>.
/// Set <see cref="Handled"/> + <see cref="Result"/> to short-circuit
/// <c>DefSubclassProc</c>; otherwise the default proc runs.
/// </summary>
internal sealed class WindowMessageEventArgs : EventArgs
{
    public WindowMessageEventArgs(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        Hwnd = hwnd;
        Msg = msg;
        WParam = wParam;
        LParam = lParam;
    }

    public nint Hwnd { get; }
    public uint Msg { get; }
    public nuint WParam { get; }
    public nint LParam { get; }

    /// <summary>Set by handlers that want to short-circuit <c>DefSubclassProc</c>.</summary>
    public bool Handled { get; set; }

    /// <summary>Return value when <see cref="Handled"/> is true.</summary>
    public nint Result { get; set; }
}
