using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class ButtonPage: Component
{
    public override Element Render()
    {
        var (basicOutput, setBasicOutput) = UseState("Ready");
        var (accentOutput, setAccentOutput) = UseState("Ready");
        var (subtleOutput, setSubtleOutput) = UseState("Ready");
        var (linkOutput, setLinkOutput) = UseState("Ready");

        return PageContent("Button",
            "A button that responds to user clicks and pointer input.",

            // Phase 8.1 — .Click fluent (drops the leading "On" per Phase 1's naming convention).
            SampleCard("Basic Button — .Click(handler)",
                VStack(8,
                    Button("Save").Click(() => setBasicOutput("Saved!")),
                    TextBlock(basicOutput).Foreground(Theme.SecondaryText)),
                sourceCode: @"Button(""Save"").Click(() => setOutput(""Saved!""))"),

            SampleCard("Disabled Button",
                Button("Can't Click").Disabled(),
                sourceCode: @"Button(""Can't Click"").Disabled()"),

            // Phase 8.1 — .AccentButton() named-style fluent (spec 039 §17.1).
            SampleCard("Accent style — .AccentButton()",
                VStack(8,
                    Button("Confirm").Click(() => setAccentOutput("Confirmed!")).AccentButton(),
                    TextBlock(accentOutput).Foreground(Theme.SecondaryText)),
                sourceCode: @"Button(""Confirm"").Click(() => setOutput(""Confirmed!"")).AccentButton()"),

            // Phase 8.1 — .SubtleButton() named-style fluent (spec 039 §17.1).
            SampleCard("Subtle style — .SubtleButton()",
                VStack(8,
                    Button("Cancel").Click(() => setSubtleOutput("Cancelled.")).SubtleButton(),
                    TextBlock(subtleOutput).Foreground(Theme.SecondaryText)),
                sourceCode: @"Button(""Cancel"").Click(() => setOutput(""Cancelled."")).SubtleButton()"),

            // Phase 8.1 / 2.2 — .TextLink() for inline "Learn more" pattern (spec 039 §17.2).
            SampleCard("Text-link style — .TextLink()",
                VStack(8,
                    HStack(4,
                        TextBlock("Need help?").Foreground(Theme.SecondaryText),
                        Button("Learn more").Click(() => setLinkOutput("Link clicked.")).TextLink()),
                    TextBlock(linkOutput).Foreground(Theme.SecondaryText)),
                sourceCode: @"Button(""Learn more"").Click(...).TextLink()")
        );
    }
}
