using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Applies a Reactor <see cref="BackdropChoice"/> modifier to a hosting
/// <see cref="Window"/>. Mutates <see cref="Window.SystemBackdrop"/> only when the
/// effective backdrop kind has changed since the last application, so repeated
/// re-renders that carry the same backdrop modifier do not allocate or trigger
/// visual reflows.
/// </summary>
/// <remarks>
/// Spec 033 §6. Hosting boundaries (<see cref="ReactorHost"/>,
/// <see cref="ReactorHostControl"/>) own one instance of this helper for the
/// lifetime of the host. <see cref="ReactorHostControl"/> hosts that do not own
/// their window pass <c>null</c> for <c>window</c> and the applier no-ops with a
/// debug log.
/// </remarks>
internal sealed class BackdropApplier
{
    // Windows whose native surface has been torn down (Window.Closed has fired).
    // Writing Window.SystemBackdrop on such a window faults with an
    // ACCESS_VIOLATION (0xC0000005) deep in the WinUI backdrop interop — an
    // unmanaged fault no try/catch can trap — and the WinUI backdrop machinery
    // is global, so the corruption takes down later windows in the same process
    // too. A window can outlive its native surface in two ways that reach a
    // BackdropApplier: an app-driven Application.Exit() that tears down every
    // open window mid-process, and a test harness that reuses one Window object
    // across many hosts. Tracked process-wide (keyed weakly on the Window so a
    // genuinely collected window doesn't leak) and consulted before every
    // SystemBackdrop write. (issue #647)
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> s_closedWindows = new();
    private static readonly object s_closedMarker = new();

    /// <summary>
    /// Record that <paramref name="window"/>'s native surface has been torn down
    /// (its <c>Closed</c> event fired), so no <see cref="BackdropApplier"/> — including
    /// one created later for a reused window — writes <c>SystemBackdrop</c> on it
    /// again. Idempotent. (issue #647)
    /// </summary>
    internal static void MarkWindowClosed(Window? window)
    {
        if (window is null) return;
        s_closedWindows.AddOrUpdate(window, s_closedMarker);
    }

    private readonly Window? _window;

    // Last-applied state — used so the host's per-render apply pass is a no-op
    // when the modifier hasn't changed. We compare on kind/factory identity, not
    // on the materialized backdrop instance, because WinUI's setter triggers a
    // visual reflow even when the new value equals the old.
    private BackdropKind? _lastKind;
    private global::System.Func<SystemBackdrop?>? _lastFactory;
    private bool _hasApplied;

    // Spec 036 §3.3 — a window-level default seeded by WindowSpec.Backdrop.
    // The render-pass Apply consults this when the tree's modifier is null
    // so apps that declare backdrop on the spec don't have to also push a
    // Backdrop modifier through the root component.
    private BackdropChoice? _windowDefault;

    public BackdropApplier(Window? window)
    {
        _window = window;
    }

    // True once the owning window's native surface has been torn down — see
    // s_closedWindows. SystemBackdrop writes are skipped from here on. (issue #647)
    private bool WindowSurfaceGone => _window is not null && s_closedWindows.TryGetValue(_window, out _);

    /// <summary>
    /// Sets the window-level default backdrop. Used by
    /// <see cref="ReactorWindow"/> to apply <see cref="WindowSpec.Backdrop"/>
    /// before the first render so the user sees the right material from
    /// frame zero. Tree-level <c>BackdropChoice</c> modifiers still take
    /// precedence on subsequent renders.
    /// </summary>
    internal void SetWindowDefault(BackdropChoice? choice)
    {
        _windowDefault = choice;
    }

    /// <summary>True when this applier has a real owning window to mutate.</summary>
    public bool HasWindow => _window is not null;

    /// <summary>
    /// Applies <paramref name="choice"/> to the window. Pass <c>null</c> to clear
    /// the backdrop (returns the window to its WinUI default — usually
    /// system-themed solid background).
    /// </summary>
    /// <returns>
    /// True if the backdrop on the window changed as a result of this call.
    /// </returns>
    public bool Apply(BackdropChoice? choice)
    {
        // Tree modifier wins; fall back to the window-level default seeded
        // from WindowSpec.Backdrop. (spec 036 §3.3)
        choice ??= _windowDefault;
        if (_window is null)
        {
            // Spec: no-op + debug log when the host does not own a Window.
            // Only log on first encounter so re-renders aren't noisy.
            if (!_hasApplied && choice is not null)
            {
                Debug.WriteLine("[Reactor] Backdrop modifier ignored: host does not own a Window.");
                _hasApplied = true;
            }
            return false;
        }

        // The window's native surface has been torn down (e.g. Application.Exit
        // closed it, or the harness is reusing a closed Window). Writing
        // SystemBackdrop now AVs (0xC0000005) — skip it. (issue #647)
        if (WindowSurfaceGone)
            return false;

        var nextKind = choice?.Kind;
        var nextFactory = choice?.Factory;

        // No change since last apply — bail out before touching WinUI.
        if (_hasApplied && nextKind == _lastKind && ReferenceEquals(nextFactory, _lastFactory))
            return false;

        SystemBackdrop? backdrop = null;
        try
        {
            backdrop = nextFactory is not null
                ? nextFactory()
                : Materialize(nextKind);
        }
        catch (global::System.Exception ex)
        {
            // Backdrop instantiation can fail on Win10 or under restricted hosting
            // models. Spec §6 delegates to WinUI's behavior for unsupported builds,
            // but a constructor throw would otherwise propagate up the render loop;
            // catch and fall back to "no backdrop" with a diagnostic log.
            Debug.WriteLine($"[Reactor] Backdrop materialization failed for kind={nextKind}: {ex.GetType().Name}: {ex.Message}");
            if (nextKind == BackdropKind.Transparent)
                Core.Diagnostics.DiagnosticLog.Warning(Core.Diagnostics.LogCategory.Hosting, "Backdrop.Transparent", "TransparentBackdrop is unsupported; falling back to no backdrop.");
            backdrop = null;
        }

        try
        {
            _window.SystemBackdrop = backdrop;
        }
        catch (global::System.Exception ex)
        {
            Debug.WriteLine($"[Reactor] Window.SystemBackdrop setter threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        Debug.WriteLine(
            $"[Reactor] Backdrop set on window {_window.GetHashCode():X8}: kind={(nextKind?.ToString() ?? (nextFactory is not null ? "factory" : "None"))}");

        _lastKind = nextKind;
        _lastFactory = nextFactory;
        _hasApplied = true;
        return true;
    }

    /// <summary>
    /// Clears the backdrop and resets internal state. Called by the host on dispose
    /// so subsequent non-Reactor hosts on the same window see a clean slate.
    /// </summary>
    /// <param name="windowClosed">
    /// When <c>true</c> the owning window has already been closed/destroyed. The
    /// <c>Window.SystemBackdrop</c> write is then skipped (in addition to the
    /// process-wide closed-window guard): touching <c>set_SystemBackdrop</c> on a
    /// torn-down window faults with an <c>ACCESS_VIOLATION</c> (0xC0000005) — an
    /// unmanaged fault the surrounding <c>try/catch</c> cannot trap — and corrupts
    /// the WinUI backdrop interop for windows opened later in the same process.
    /// Clearing the backdrop on a window that is going away is pointless anyway.
    /// The internal last-applied state is still reset so a reused applier starts
    /// clean. (issue #647)
    /// </param>
    public void Reset(bool windowClosed = false)
    {
        if (_window is null)
        {
            _hasApplied = false;
            return;
        }
        if (!windowClosed && !WindowSurfaceGone)
        {
            try { _window.SystemBackdrop = null; }
            catch (global::System.Exception ex)
                when (ex is global::System.ObjectDisposedException
                    or global::System.InvalidOperationException
                    or global::System.ArgumentException)
            {
                Debug.WriteLine($"[Reactor] Backdrop reset failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        _lastKind = null;
        _lastFactory = null;
        _hasApplied = false;
    }

    /// <summary>
    /// Materializes a built-in backdrop instance for the given kind. Returns null
    /// for <see cref="BackdropKind.None"/> (clears the host window).
    /// </summary>
    /// <remarks>
    /// Visible for tests in the same assembly so the materialization mapping can
    /// be exercised without standing up a full host.
    /// </remarks>
    internal static SystemBackdrop? Materialize(BackdropKind? kind) => kind switch
    {
        null or BackdropKind.None => null,
        BackdropKind.Mica => new MicaBackdrop(),
        BackdropKind.MicaAlt => new MicaBackdrop
        {
            Kind = global::Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt,
        },
        BackdropKind.DesktopAcrylic => new DesktopAcrylicBackdrop(),
        // AcrylicThin: WinAppSDK 2.0 preview's DesktopAcrylicBackdrop does not
        // expose a Kind selector, so we materialize the same base type. When the
        // SDK ships the variant we'll switch to it without an API change here.
        BackdropKind.AcrylicThin => new DesktopAcrylicBackdrop(),
        BackdropKind.Transparent => TransparentUnsupported(),
        _ => null,
    };

    private static SystemBackdrop? TransparentUnsupported()
    {
        Core.Diagnostics.DiagnosticLog.Warning(
            Core.Diagnostics.LogCategory.Hosting,
            "Backdrop.Transparent",
            "TransparentBackdrop is unavailable in the referenced Windows App SDK; falling back to no backdrop.");
        return null;
    }
}
