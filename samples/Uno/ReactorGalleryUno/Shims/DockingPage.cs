// Uno stand-in for the gallery's docking page.
//
// The docking subsystem (dock manager, tab tear-off, floating windows) is built
// on Win32 native windowing and is excluded from the Reactor.Uno port entirely,
// so ControlPages/Layout/DockingPage.cs cannot compile here. PageRouter is shared
// source and still routes the "docking" tag, so this page takes its place and
// says so plainly rather than 404-ing or silently rendering nothing.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor.ControlPages.Layout;

class DockingPage : Component
{
    public override Element Render() =>
        VStack(12,
            Heading("Docking"),
            Body("Not available on Uno Platform."),
            Body(
                "The dock manager, tab tear-off and floating windows are implemented "
                + "against Win32 native windowing, so the docking subsystem is excluded "
                + "from the Uno port. Every other page in this gallery is the exact same "
                + "source as the Windows gallery.")
        ).Padding(24);
}
