using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using WinUIGalleryReactor;
using static Microsoft.UI.Reactor.Factories;

// Deep linking, step 1: if a `reactor-gallery://` link launched us while a gallery
// is already running, hand the activation over and exit instead of opening a second
// window. Must happen before WinUI bootstraps.
if (GalleryActivation.TryRedirectToRunningInstance())
    return;

// Deep linking, step 2: make `reactor-gallery://` clickable. Unpackaged builds have
// to register themselves under HKCU; the MSIX flavour declares the scheme in
// Package.appxmanifest instead, so this call no-ops there.
GalleryProtocol.EnsureRegistered();

// No explicit width/height: the gallery is content-heavy and benefits from a
// window proportional to the display, which is what the OS default gives
// (~3/4 of the work area). This is also the sample that dogfoods the spec 036
// §4.1 "unset size defers to the OS" path.
//
// The icon is declared once, here: it drives the caption/Alt-Tab icon, and the
// shell's TitleBar inherits it rather than restating the path.
ReactorApp.Run<WinUIGalleryReactor.GalleryShell>("WinUI Gallery (Reactor)",
    icon: WindowIcon.FromPath("Assets/GalleryIcon.ico"),
    configure: host =>
    {
        XamlInterop.Register(host.Reconciler);
        // DockManager is constructed directly (no factory), so its handler must be
        // registered up front for the Docking gallery page to mount.
        Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler);
    });
