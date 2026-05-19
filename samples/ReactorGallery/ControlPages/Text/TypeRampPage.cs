using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Text;

/// <summary>
/// Phase 8.1 — exercises the spec-039 §17.6 type-ramp factories
/// (<see cref="Factories.Title"/>, <see cref="Factories.Subtitle"/>,
/// <see cref="Factories.Body"/>, <see cref="Factories.BodyStrong"/>,
/// <see cref="Factories.BodyLarge"/>). The type ramp lives only as
/// factories — never as fluents — per §17.6's "no two-ways trap" rule.
/// </summary>
class TypeRampPage : Component
{
    public override Element Render()
    {
        return PageContent("Type ramp",
            "Reactor exposes the WinUI 3 type ramp as named factory functions. " +
            "Use them instead of ApplyStyle(\"TitleTextBlockStyle\") for the common case.",

            SampleCard("Factories",
                VStack(8,
                    Title("Title — 28px Semibold"),
                    Subtitle("Subtitle — 20px Semibold"),
                    BodyLarge("BodyLarge — 18px regular"),
                    BodyStrong("BodyStrong — 14px Semibold"),
                    Body("Body — 14px regular body text")
                ),
                sourceCode: @"Title(""Title — 28px Semibold"")
Subtitle(""Subtitle — 20px Semibold"")
BodyLarge(""BodyLarge — 18px regular"")
BodyStrong(""BodyStrong — 14px Semibold"")
Body(""Body — 14px regular body text"")"),

            SampleCard("Composed example — article card",
                Card(
                    VStack(8,
                        Title("Release notes"),
                        Subtitle("Version 2.5"),
                        BodyStrong("New update available"),
                        Body("Version 2.5 ships performance improvements and several bug fixes."),
                        Body("Restart the app to apply.")
                    )
                ).Width(420),
                sourceCode: @"Card(VStack(8,
    Title(""Release notes""),
    Subtitle(""Version 2.5""),
    BodyStrong(""New update available""),
    Body(""…body…""),
    Body(""…body…"")
))")
        );
    }
}
