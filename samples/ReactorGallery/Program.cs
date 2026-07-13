using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<WinUIGalleryReactor.GalleryShell>("WinUI Gallery (Reactor)", width: 1400, height: 900,
    configure: host =>
    {
        XamlInterop.Register(host.Reconciler);
        // DockManager is constructed directly (no factory), so its handler must be
        // registered up front for the Docking gallery page to mount.
        Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler);
    });
