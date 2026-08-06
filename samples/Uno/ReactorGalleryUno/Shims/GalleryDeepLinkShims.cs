// Uno-side counterparts for the gallery's Windows-only deep-link plumbing.
//
// samples/ReactorGallery/DeepLink splits cleanly in two:
//   * GalleryRoutes.cs / GalleryActivationRouting.cs are pure routing logic and
//     are source-shared verbatim by this project;
//   * GalleryActivation.cs / GalleryProtocol.cs / GalleryPackageIdentity.cs are
//     Windows App SDK (Microsoft.Windows.AppLifecycle.AppInstance) and Win32
//     (LibraryImport + HKCU registry), which have no Uno equivalent.
//
// Rather than fork GalleryShell.cs and SettingsPage.cs to remove their calls,
// this file re-declares the same types with the same surface so ALL gallery
// source compiles unchanged. The semantics are honest, not fake: on Uno the app
// is never launched by a `reactor-gallery://` link, so there is no initial route,
// no activation event ever fires, and protocol registration reports "not
// registered" and refuses to register.

using System;
using System.Diagnostics.CodeAnalysis;

namespace WinUIGalleryReactor;

/// <summary>
/// Uno stand-in for the WinAppSDK single-instance activation handler.
/// Deep-link activation needs <c>AppInstance.FindOrRegisterForKey</c> /
/// <c>GetActivatedEventArgs</c>, which are Windows App SDK APIs; nothing
/// equivalent exists across the Uno heads, so this reports "no activation".
/// </summary>
public static class GalleryActivation
{
    /// <summary>Always <c>null</c> on Uno: the app is never protocol-activated.</summary>
    public static GalleryRoute? InitialRoute => null;

    /// <summary>
    /// Never raised on Uno. Declared so <c>GalleryShell</c>'s subscribe/unsubscribe
    /// effect compiles and runs unchanged.
    /// </summary>
#pragma warning disable CS0067 // part of the shared API surface; intentionally never raised here
    public static event Action<GalleryRoute>? RouteActivated;
#pragma warning restore CS0067

    /// <summary>Always <c>false</c> — there is never a pending warm-start route.</summary>
    public static bool TryTakePendingRoute([NotNullWhen(true)] out GalleryRoute? route)
    {
        route = null;
        return false;
    }

    /// <summary>
    /// Always <c>false</c> — no single-instance redirection, so startup continues
    /// normally and opens this instance's window.
    /// </summary>
    public static bool TryRedirectToRunningInstance() => false;
}

/// <summary>
/// Uno stand-in for the <c>reactor-gallery://</c> protocol registration.
/// The Windows build either declares the scheme in its MSIX manifest or writes
/// an HKCU registration via Win32; neither is available (or meaningful) here, so
/// registration is reported as unsupported rather than pretended.
/// </summary>
public static class GalleryProtocol
{
    /// <summary>Uno heads have no MSIX package identity.</summary>
    public static bool IsPackaged => false;

    /// <summary>Nothing manages the scheme on Uno, so the Settings page shows the toggle as off and inert.</summary>
    public static bool IsManagedByPackage => false;

    /// <summary>Always <c>false</c>: the scheme is never registered on Uno.</summary>
    public static bool IsRegistered => false;

    /// <summary>No-op; returns <c>false</c> to report "not registered".</summary>
    public static bool EnsureRegistered() => false;

    /// <summary>Unsupported on Uno; returns <c>false</c>.</summary>
    public static bool Register() => false;

    /// <summary>Unsupported on Uno; returns <c>false</c>.</summary>
    public static bool Unregister() => false;
}
