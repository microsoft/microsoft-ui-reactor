// Uno stand-in for the gallery's WebView2 page.
//
// This is a *compilation* limitation, not a capability one. On Uno the name
// `WebView2` resolves to a namespace, which shadows Reactor's `WebView2(...)`
// element factory that the shared page calls — and a namespace shadowing a
// method group is not something a `global using` alias can disambiguate. The
// page is therefore excluded and replaced here, rather than forking it.
//
// Uno does implement a WebView2 control, so a fully-qualified variant of the
// shared page would work; that is worth revisiting upstream so both galleries
// can keep using one file.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor.ControlPages.Media;

class WebView2Page : Component
{
    public override Element Render() =>
        VStack(12,
            Heading("WebView2"),
            Body("Not shown in the Uno gallery."),
            Body(
                "Reactor's WebView2(...) factory is shadowed by a namespace of the "
                + "same name on Uno, so the shared gallery page does not compile here. "
                + "Uno itself does implement WebView2 — this is a naming collision in "
                + "the sample, not a missing platform feature.")
        ).Padding(24);
}
