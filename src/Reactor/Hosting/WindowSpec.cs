using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

public enum WindowEmbedStyle
{
    Child,
    Owner,
}

public sealed record EmbedRequest(WindowEmbedStyle Style, int HostPid, bool InitialVisibility);

/// <summary>
/// Immutable (init-only) declarative description of a top-level Reactor window. Hand to
/// <see cref="ReactorApp.OpenWindow(WindowSpec, Func{Component}, Action{Microsoft.UI.Reactor.Hosting.ReactorHost})"/>
/// to open a window; hand to <see cref="ReactorWindow.Update"/> to diff
/// against the current spec and apply only changed fields.
/// </summary>
/// <remarks>
/// <para>All sizes and positions are <b>DIPs</b> (device-independent pixels) —
/// no Win32 / no <c>SizeInt32</c> in user code. (spec 036 §4.1)</para>
/// <para>Validation runs in the primary constructor: <c>Width</c>/<c>Height</c>
/// must be positive, max ≥ min when both are set, and
/// <see cref="ManualPosition"/> is required iff
/// <see cref="StartPosition"/> is <see cref="WindowStartPosition.Manual"/>.</para>
/// </remarks>
public sealed record WindowSpec
{
    /// <summary>Window caption text. Defaults to <c>"Reactor App"</c>.</summary>
    public string Title { get; init; } = "Reactor App";

    /// <summary>Initial DIP width. Must be positive.</summary>
    public double Width { get; init; } = 1024;

    /// <summary>Initial DIP height. Must be positive.</summary>
    public double Height { get; init; } = 768;

    /// <summary>Optional minimum DIP width.</summary>
    public double? MinWidth { get; init; }

    /// <summary>Optional minimum DIP height.</summary>
    public double? MinHeight { get; init; }

    /// <summary>Optional maximum DIP width.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Optional maximum DIP height.</summary>
    public double? MaxHeight { get; init; }

    /// <summary>Initial placement strategy.</summary>
    public WindowStartPosition StartPosition { get; init; } = WindowStartPosition.Default;

    /// <summary>
    /// DIP top-left position. Set together with
    /// <see cref="WindowStartPosition.Manual"/>; otherwise <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The DIP→physical conversion uses the window's <b>initial</b> DPI (the
    /// monitor it opens on). Coordinates that target a different monitor on a
    /// mixed-DPI desktop may land at a slightly different physical position
    /// because Windows virtual-screen coordinates are physical pixels with no
    /// global DIP coordinate space. For absolute placement on a specific
    /// non-primary monitor, prefer <see cref="WindowStartPosition.CenterOnOwner"/>
    /// (with an owner already on that monitor) or call
    /// <see cref="ReactorWindow.SetPosition"/> after the window opens.
    /// </remarks>
    public (double X, double Y)? ManualPosition { get; init; }

    /// <summary>Coarse presenter selection.</summary>
    public PresenterKind Presenter { get; init; } = PresenterKind.Overlapped;

    /// <summary>Window chrome style for overlapped windows.</summary>
    public WindowStyle Style { get; init; } = WindowStyle.Default;

    /// <summary>
    /// DWM corner preference. On Windows 10 the platform ignores this setting.
    /// </summary>
    public WindowCornerStyle CornerStyle { get; init; } = WindowCornerStyle.Default;

    /// <summary>Z-order tier for this window.</summary>
    public WindowLevel Level { get; init; } = WindowLevel.Normal;

    /// <summary>
    /// Content-driven sizing mode. Size changes are applied after the first layout pass,
    /// so apps may see one frame at the initial Width/Height before content settles.
    /// </summary>
    public WindowSizeToContent SizeToContent { get; init; } = WindowSizeToContent.Manual;

    /// <summary>Resize/chrome policy for overlapped windows.</summary>
    public WindowResizeMode ResizeMode { get; init; } = WindowResizeMode.CanResize;

    /// <summary>Optional width / height aspect lock applied during interactive resize.</summary>
    public double? AspectRatio { get; init; }

    /// <summary>
    /// Whether <see cref="AspectRatio"/> constrains the outer window rect or
    /// the client (content) area. Default is
    /// <see cref="AspectRatioBasis.Window"/>; switch to
    /// <see cref="AspectRatioBasis.Client"/> when the ratio describes the
    /// content (e.g. 1:1 for a square video, 16:9 for a game viewport) and
    /// the framework should auto-account for chrome.
    /// </summary>
    public AspectRatioBasis AspectRatioBasis { get; init; } = AspectRatioBasis.Window;

    /// <summary>Whether pressing non-interactive background starts an OS window drag.</summary>
    public bool IsMovableByBackground { get; init; }

    /// <summary>Whether the minimize caption button is enabled (Overlapped only).</summary>
    public bool IsMinimizable { get; init; } = true;

    /// <summary>Whether the maximize caption button is enabled (Overlapped only).</summary>
    public bool IsMaximizable { get; init; } = true;

    private readonly bool _showInTaskbar = true;
    private readonly bool _showInTaskbarExplicit;

    /// <summary>Whether the window appears in the taskbar.</summary>
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        init
        {
            _showInTaskbar = value;
            _showInTaskbarExplicit = true;
        }
    }

    internal bool ShowInTaskbarExplicit => _showInTaskbarExplicit;

    /// <summary>Whether the window appears in Alt-Tab / shell switchers.</summary>
    public bool ShowInSwitcher { get; init; } = true;

    /// <summary>
    /// Whether app content extends into the title-bar region. When <c>null</c>,
    /// a mounted <c>TitleBar(...)</c> element infers <c>true</c>; otherwise the
    /// explicit value wins. Hot reload cannot migrate the previous bool field
    /// shape across this type change; restart after editing this field during HR.
    /// </summary>
    public bool? ExtendsContentIntoTitleBar { get; init; }

    /// <summary>Optional declarative backdrop. Seeds the per-host modifier.</summary>
    public BackdropChoice? Backdrop { get; init; }

    /// <summary>Optional window icon.</summary>
    public WindowIcon? Icon { get; init; }

    /// <summary>Optional persistence id for placement and future per-window persistence subsystems.</summary>
    public string? PersistenceId { get; init; }

    /// <summary>
    /// Restore window placement from the registered store on open and save on close.
    /// Ignored when <see cref="PersistenceId"/> is null.
    /// </summary>
    public bool PersistPlacement { get; init; }

    /// <summary>Initial placement used when persistence is enabled but no placement exists yet.</summary>
    public WindowStartPosition PersistenceFallback { get; init; } = WindowStartPosition.Default;

    /// <summary>
    /// Configure placement persistence in one call.
    /// </summary>
    public WindowSpec WithPersistence(string id, WindowStartPosition fallback = WindowStartPosition.Default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Persistence id must be non-empty.", nameof(id));
        return this with { PersistenceId = id, PersistPlacement = true, PersistenceFallback = fallback };
    }

    /// <summary>Optional stable identity for <c>UseOpenWindow</c> reuse and shell lookups.</summary>
    public WindowKey? Key { get; init; }

    /// <summary>Optional parent window for owned-window semantics.</summary>
    public ReactorWindow? Owner { get; init; }

    /// <summary>Optional request to embed this window into an external HWND host.</summary>
    public EmbedRequest? Embed { get; init; }

    /// <summary>Whether the window auto-activates after content mounts. Default: true.</summary>
    public bool ActivateOnOpen { get; init; } = true;

    /// <summary>
    /// Window-wide alpha in [0..1]. 1.0 = fully opaque (default, no layering
    /// overhead). Values below 1.0 are applied via Win32
    /// <c>SetLayeredWindowAttributes</c> on the underlying HWND. Used by the
    /// docking subsystem to render an in-flight tear-off floating window at
    /// reduced opacity (spec 045 §2.6 tear-off follow-up); apps may also set
    /// it for HUD overlays etc.
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// Apply the Win32 <c>WS_EX_NOACTIVATE</c> extended style so the window
    /// appears without stealing foreground activation. Matches VS tool-window
    /// / drag-preview behavior. Default false. The flag is applied during
    /// chrome setup (before Activate) so the first show observes it.
    /// </summary>
    public bool NoActivate { get; init; }

    /// <summary>
    /// Apply the Win32 <c>WS_EX_TRANSPARENT</c> extended style so mouse events
    /// pass THROUGH the window to whatever's underneath. Default false.
    /// Used by spec 045 §2.6 tear-off so the drag preview doesn't block
    /// clicks on drop-target overlays below it.
    /// </summary>
    /// <remarks>
    /// <para>Requires <see cref="Opacity"/> &lt; 1.0 — the OS only honors
    /// <c>WS_EX_TRANSPARENT</c> on layered windows. <see cref="Validate"/>
    /// throws when this field is true with <c>Opacity == 1.0</c>; the
    /// runtime mutator <c>ReactorWindow.SetIgnorePointerInput(true)</c>
    /// also throws if the live window is not layered.</para>
    /// </remarks>
    public bool IgnorePointerInput { get; init; }

    /// <summary>
    /// Construct with explicit field values. Validation runs after the record
    /// auto-init.
    /// </summary>
    public WindowSpec()
    {
        // Empty - validation deferred until field setters complete in the
        // record's init phase. The Validate() method is called explicitly
        // by the hosting layer (ReactorWindow.Apply) before use.
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when any cross-field invariant is
    /// violated. Called by <see cref="ReactorWindow"/> before the spec is
    /// applied; tests may also call this directly to verify a spec.
    /// </summary>
    public void Validate()
    {
        if (!(Width > 0))
            throw new ArgumentException("WindowSpec.Width must be positive.", nameof(Width));
        if (!(Height > 0))
            throw new ArgumentException("WindowSpec.Height must be positive.", nameof(Height));

        if (MinWidth is { } minW && !(minW > 0))
            throw new ArgumentException("WindowSpec.MinWidth must be positive when set.", nameof(MinWidth));
        if (MinHeight is { } minH && !(minH > 0))
            throw new ArgumentException("WindowSpec.MinHeight must be positive when set.", nameof(MinHeight));
        if (MaxWidth is { } maxW && !(maxW > 0))
            throw new ArgumentException("WindowSpec.MaxWidth must be positive when set.", nameof(MaxWidth));
        if (MaxHeight is { } maxH && !(maxH > 0))
            throw new ArgumentException("WindowSpec.MaxHeight must be positive when set.", nameof(MaxHeight));

        if (MinWidth is { } a && MaxWidth is { } b && a > b)
            throw new ArgumentException(
                $"WindowSpec.MaxWidth ({b}) must be >= MinWidth ({a}).", nameof(MaxWidth));
        if (MinHeight is { } c && MaxHeight is { } d && c > d)
            throw new ArgumentException(
                $"WindowSpec.MaxHeight ({d}) must be >= MinHeight ({c}).", nameof(MaxHeight));

        if (StartPosition == WindowStartPosition.Manual && ManualPosition is null)
            throw new ArgumentException(
                "WindowSpec.ManualPosition must be set when StartPosition == Manual.", nameof(ManualPosition));
        if (StartPosition != WindowStartPosition.Manual && ManualPosition is not null)
            throw new ArgumentException(
                "WindowSpec.ManualPosition must be null unless StartPosition == Manual.", nameof(ManualPosition));

        if (AspectRatio is { } ratio)
        {
            if (!(ratio > 0.0) || !double.IsFinite(ratio))
                throw new ArgumentException("WindowSpec.AspectRatio must be finite and greater than 0.", nameof(AspectRatio));
            if (ResizeMode == WindowResizeMode.NoResize)
                throw new ArgumentException("WindowSpec.AspectRatio cannot be combined with ResizeMode.NoResize.", nameof(AspectRatio));
            if (SizeToContent != WindowSizeToContent.Manual)
                throw new ArgumentException("WindowSpec.AspectRatio cannot be combined with SizeToContent.", nameof(SizeToContent));
        }

        if (PersistPlacement && string.IsNullOrWhiteSpace(PersistenceId))
            throw new ArgumentException("WindowSpec.PersistenceId must be non-empty (and not whitespace-only) when PersistPlacement is true.", nameof(PersistenceId));

        if (Embed is { } embed)
        {
            if (embed.HostPid <= 0)
                throw new ArgumentException("WindowSpec.Embed.HostPid must be positive.", nameof(Embed));
            if (embed.Style == WindowEmbedStyle.Child && Owner is not null)
                throw new ArgumentException("WindowSpec.Owner must be null when Embed.Style is Child.", nameof(Owner));
            if (PersistPlacement)
                throw new ArgumentException("WindowSpec.PersistPlacement must be false when Embed is set.", nameof(PersistPlacement));
        }

        if (Style == WindowStyle.None && !IsMovableByBackground)
            Core.Diagnostics.DiagnosticLog.Warning(
                Core.Diagnostics.LogCategory.Hosting,
                "WindowSpec.Validate",
                "WindowStyle.None without IsMovableByBackground can leave the window without a drag affordance.");

        if (!(Opacity >= 0.0 && Opacity <= 1.0) || double.IsNaN(Opacity))
            throw new ArgumentException(
                $"WindowSpec.Opacity ({Opacity}) must be in [0, 1].", nameof(Opacity));

        // WS_EX_TRANSPARENT is only honored by the OS when WS_EX_LAYERED is
        // also set; a non-layered window with the transparent style is a
        // silent no-op for click-through. Reject up front rather than ship
        // a misleading contract.
        if (IgnorePointerInput && Opacity >= 1.0)
            throw new ArgumentException(
                "WindowSpec.IgnorePointerInput requires Opacity < 1.0 (click-through is only effective on layered windows).",
                nameof(IgnorePointerInput));
    }
}
