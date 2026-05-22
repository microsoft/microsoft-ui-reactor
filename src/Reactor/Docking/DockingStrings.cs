namespace Microsoft.UI.Reactor.Docking;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.21 / §8.6 — docking string surface.
//
//  Every user-facing string the docking subsystem renders flows through
//  `DockingStrings.Get(key)`. The default implementation returns the
//  English string from `DefaultEnglish` below; apps wire up localization
//  by assigning `DockingStrings.Resolver` to a delegate that consults
//  their `IntlAccessor` (typically obtained via `UseIntl()` and
//  captured into a static at app startup).
//
//  Why a delegate rather than direct `IntlAccessor` use?
//   • The drop-target overlay + side-strip controls are realized
//     WinUI `UIElement`s, not Reactor `Component`s. They don't have
//     `UseIntl()` in scope.
//   • The same docking subsystem is consumed by tests + tooling
//     contexts where no `LocaleContext` exists.
//   • The native docking host has no built-in `.resw` resource — the
//     Phase-1 `src/Reactor.Docking.Xaml/Resources/Reactor.Docking.resw`
//     file was retired with the wrapper assembly at the §2.29 review
//     gate. The delegate is the only localization route today; apps
//     plug their `.resw` / catalog into the resolver.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// String keys for the docking subsystem. The English defaults below
/// are the source of truth — apps localize by wiring
/// <see cref="DockingStrings.Resolver"/> to consult their own catalog
/// against these key strings.
/// </summary>
public static class DockingStringKeys
{
    // Host landmark (§2.22).
    public const string DockHostLandmark = "Docking.DockHost.Landmark";

    // Drop-target overlay (§2.3).
    public const string DropTargetCenter = "Docking.DropTarget.Center";
    public const string DropTargetSplitLeft = "Docking.DropTarget.SplitLeft";
    public const string DropTargetSplitTop = "Docking.DropTarget.SplitTop";
    public const string DropTargetSplitRight = "Docking.DropTarget.SplitRight";
    public const string DropTargetSplitBottom = "Docking.DropTarget.SplitBottom";
    public const string DropTargetDockLeft = "Docking.DropTarget.DockLeft";
    public const string DropTargetDockTop = "Docking.DropTarget.DockTop";
    public const string DropTargetDockRight = "Docking.DropTarget.DockRight";
    public const string DropTargetDockBottom = "Docking.DropTarget.DockBottom";
    public const string DropTargetHostLandmark = "Docking.DropTarget.HostLandmark";

    // Floating window (§2.6).
    public const string FloatingWindowDefaultTitle = "Docking.FloatingWindow.DefaultTitle";

    // Side pin (§2.5).
    public const string SidePinTooltipPrefix = "Docking.SidePin.Tooltip";

    // Navigator (§2.10, deferred — keys reserved).
    public const string NavigatorHeadingDocuments = "Docking.Navigator.Documents";
    public const string NavigatorHeadingToolWindows = "Docking.Navigator.ToolWindows";
    public const string NavigatorHeadingActive = "Docking.Navigator.Active";

    // Per-pane context menu (§2.21).
    public const string MenuClose = "Docking.Menu.Close";
    public const string MenuHide = "Docking.Menu.Hide";
    public const string MenuFloat = "Docking.Menu.Float";
    public const string MenuPinToSide = "Docking.Menu.PinToSide";
    public const string MenuAutoHide = "Docking.Menu.AutoHide";
    public const string MenuMoveToNextGroup = "Docking.Menu.MoveToNextGroup";

    // Error / fallback strings (§2.21).
    public const string LayoutRestoreFailed = "Docking.Error.LayoutRestoreFailed";

    // UIA live-region announcements (§2.10, §2.22). Each template carries
    // the `{paneTitle}` placeholder substituted at announcement time.
    public const string LiveDocked  = "Docking.LiveRegion.Docked";
    public const string LiveFloated = "Docking.LiveRegion.Floated";
    public const string LivePinned  = "Docking.LiveRegion.Pinned";
    public const string LiveClosed  = "Docking.LiveRegion.Closed";
    public const string LiveHidden  = "Docking.LiveRegion.Hidden";
    public const string LiveShown   = "Docking.LiveRegion.Shown";
}

/// <summary>
/// Localization router for docking strings. Apps wire this once at
/// startup (typically in their app-startup code that has access to
/// the active <c>IntlAccessor</c> via context). When no resolver is
/// installed, callers get the English default — adequate for
/// development, English-only deployments, and headless test contexts.
/// </summary>
/// <remarks>
/// Spec 045 §2.21. The contract:
/// <list type="bullet">
///   <item><description><c>Resolver</c> is invoked with a key
///   (e.g. <c>Docking.DropTarget.Center</c>) and the English default.
///   It returns the localized string, or null to fall back to the
///   default.</description></item>
///   <item><description>The resolver is called from the UI thread
///   during render / hit-test paths — keep it allocation-light and
///   side-effect-free.</description></item>
///   <item><description>Apps wiring a resolver supply a stable
///   delegate (not a fresh allocation per render); a typical pattern
///   captures the <c>IntlAccessor</c> once and calls
///   <c>accessor.Message(MessageKey)</c> inside.</description></item>
/// </list>
/// </remarks>
public static class DockingStrings
{
    /// <summary>
    /// Optional resolver invoked for every docking string. Returns the
    /// localized string for the given key, or null to fall back to the
    /// English default.
    /// </summary>
    public static Func<string, string?>? Resolver { get; set; }

    /// <summary>
    /// Resolves <paramref name="key"/> via <see cref="Resolver"/> when
    /// installed, else returns the English default associated with
    /// the key.
    /// </summary>
    /// <param name="key">A constant from <see cref="DockingStringKeys"/>.</param>
    /// <remarks>
    /// A thrown resolver is swallowed and falls back to the English
    /// default. The docking subsystem calls <see cref="Get"/> from
    /// hot paths (drop-target overlay hit-test, side-strip tooltip,
    /// live-region announcer) where an unhandled exception would crash
    /// the host. Resolver authors that need failure observability
    /// should log inside their resolver before throwing — matches
    /// <c>ReactorTrace</c>'s subscriber-isolation posture.
    /// </remarks>
    public static string Get(string key)
    {
        string? resolved = null;
        if (Resolver is { } r)
        {
            try { resolved = r.Invoke(key); }
            catch { /* see remarks — never propagate a buggy resolver into the host. */ }
        }
        if (!string.IsNullOrEmpty(resolved)) return resolved;
        return DefaultEnglish(key);
    }

    /// <summary>
    /// Returns the localized side-pin tooltip ("Show {paneTitle}").
    /// Routes through <see cref="Resolver"/> with the
    /// <see cref="DockingStringKeys.SidePinTooltipPrefix"/> key when
    /// installed; otherwise produces the English default.
    /// </summary>
    /// <remarks>
    /// Resolvers that wire through ICU MessageFormat should expect
    /// the placeholder text containing <c>{paneTitle}</c>; this
    /// helper performs the substitution after resolving.
    /// </remarks>
    public static string SidePinTooltip(string paneTitle)
    {
        var template = Get(DockingStringKeys.SidePinTooltipPrefix);
        // The English default and the .resw entry both use the
        // placeholder `{paneTitle}`. We perform a simple substitution
        // — translators may rearrange the surrounding text but the
        // placeholder stays the same.
        return template.Replace("{paneTitle}", paneTitle ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a live-region announcement template via <see cref="Resolver"/>
    /// and substitutes the `{paneTitle}` placeholder. Returns empty when
    /// the key is unknown so callers can no-op silently.
    /// </summary>
    /// <remarks>
    /// Spec 045 §2.10. Keys are `Docking.LiveRegion.*` constants on
    /// <see cref="DockingStringKeys"/>.
    /// </remarks>
    public static string LiveAnnouncement(string key, string? paneTitle)
    {
        var template = Get(key);
        if (string.IsNullOrEmpty(template) || ReferenceEquals(template, key)) return string.Empty;
        return template.Replace("{paneTitle}", paneTitle ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// English default for each known key. Source of truth — apps
    /// localize by routing the same key strings through
    /// <see cref="Resolver"/>. No built-in `.resw` ships with the
    /// docking subsystem today.
    /// </summary>
    private static string DefaultEnglish(string key) => key switch
    {
        DockingStringKeys.DockHostLandmark      => "Docking area",
        DockingStringKeys.DropTargetCenter      => "Add as tab",
        DockingStringKeys.DropTargetSplitLeft   => "Split left",
        DockingStringKeys.DropTargetSplitTop    => "Split top",
        DockingStringKeys.DropTargetSplitRight  => "Split right",
        DockingStringKeys.DropTargetSplitBottom => "Split bottom",
        DockingStringKeys.DropTargetDockLeft    => "Dock left",
        DockingStringKeys.DropTargetDockTop     => "Dock top",
        DockingStringKeys.DropTargetDockRight   => "Dock right",
        DockingStringKeys.DropTargetDockBottom  => "Dock bottom",
        DockingStringKeys.DropTargetHostLandmark => "Dock targets",
        DockingStringKeys.FloatingWindowDefaultTitle => "Floating Window",
        DockingStringKeys.SidePinTooltipPrefix  => "Show {paneTitle}",
        DockingStringKeys.NavigatorHeadingDocuments => "Documents",
        DockingStringKeys.NavigatorHeadingToolWindows => "Tool Windows",
        DockingStringKeys.NavigatorHeadingActive => "Active",
        DockingStringKeys.MenuClose             => "Close",
        DockingStringKeys.MenuHide              => "Hide",
        DockingStringKeys.MenuFloat             => "Float",
        DockingStringKeys.MenuPinToSide         => "Pin to side",
        DockingStringKeys.MenuAutoHide          => "Auto-hide",
        DockingStringKeys.MenuMoveToNextGroup   => "Move to next group",
        DockingStringKeys.LayoutRestoreFailed   => "Could not restore the saved layout. Default layout applied.",
        DockingStringKeys.LiveDocked            => "{paneTitle} docked",
        DockingStringKeys.LiveFloated           => "{paneTitle} torn out",
        DockingStringKeys.LivePinned            => "{paneTitle} pinned",
        DockingStringKeys.LiveClosed            => "{paneTitle} closed",
        DockingStringKeys.LiveHidden            => "{paneTitle} hidden",
        DockingStringKeys.LiveShown             => "{paneTitle} shown",
        _ => key,
    };
}
