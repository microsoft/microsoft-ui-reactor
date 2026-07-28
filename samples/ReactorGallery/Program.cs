using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

// No explicit width/height: the gallery is content-heavy and benefits from a
// window proportional to the display, which is what the OS default gives
// (~3/4 of the work area). This is also the sample that dogfoods the spec 036
// §4.1 "unset size defers to the OS" path.
ReactorApp.Run<WinUIGalleryReactor.GalleryShell>("WinUI Gallery (Reactor)",
    configure: host =>
    {
        XamlInterop.Register(host.Reconciler);
        // DockManager is constructed directly (no factory), so its handler must be
        // registered up front for the Docking gallery page to mount.
        Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler);
    });
