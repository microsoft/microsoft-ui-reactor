using System;
using System.Collections.Generic;

namespace WinUIGalleryReactor;

/// <summary>
/// The order in which an activation's candidate strings are tried when deciding which
/// page a launch should open.
/// </summary>
/// <remarks>
/// Kept apart from <c>GalleryActivation</c> — and free of any WinRT dependency —
/// so the priority rules can be unit-tested headlessly. <c>GalleryActivation</c> keeps
/// the part that genuinely needs the platform: pulling these strings back out of an
/// <c>AppActivationArguments</c>.
/// </remarks>
public static class GalleryActivationRouting
{
    /// <summary>
    /// Resolve the route an activation implies, or <c>null</c> when none of its
    /// candidates name a real page.
    /// </summary>
    /// <param name="protocolUri">
    /// The URI from a protocol activation, or <c>null</c> when the activation wasn't one.
    /// Highest priority: it is the only candidate the user explicitly aimed at this app.
    /// </param>
    /// <param name="launchArguments">
    /// The argument string from a launch activation — how a jump-list entry, tray command,
    /// or redirected launch carries a link.
    /// </param>
    /// <param name="commandLineArgs">
    /// Remaining raw command-line arguments (excluding the executable), or <c>null</c> to
    /// skip the fallback entirely. Covers
    /// <c>ReactorGallery.exe reactor-gallery:///item/button</c> typed by hand, and any
    /// shell that passes the URI through without the AppLifecycle marker. Callers pass
    /// <c>null</c> for a *redirected* activation, where this process's own argv describes
    /// the original launch rather than the incoming link.
    /// </param>
    public static GalleryRoute? Resolve(
        string? protocolUri,
        string? launchArguments,
        IReadOnlyList<string>? commandLineArgs)
    {
        if (GalleryRoutes.TryResolve(protocolUri, out var protocolRoute))
            return protocolRoute;

        if (GalleryRoutes.TryResolve(launchArguments, out var launchRoute))
            return launchRoute;

        if (commandLineArgs is not null)
        {
            for (int i = 0; i < commandLineArgs.Count; i++)
            {
                if (GalleryRoutes.TryResolve(commandLineArgs[i], out var argRoute))
                    return argRoute;
            }
        }

        return null;
    }
}
