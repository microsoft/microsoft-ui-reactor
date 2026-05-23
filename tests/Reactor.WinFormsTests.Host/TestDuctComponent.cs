using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace Microsoft.UI.Reactor.WinFormsTests.Host;

/// <summary>
/// A Reactor component designed for E2E testing inside a XAML Island.
/// Every interactive control has an AutomationId so Appium can find it.
/// </summary>
class TestReactorComponent : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (text, setText) = UseState("");

        return Grid([GridSize.Star()], [GridSize.Star()],
            VStack(
                TextBlock("Reactor Island Content")
                    .FontSize(16)
                    .AutomationId("Reactor_Title"),

                // Focusable text field — first Tab stop inside the island
                TextBox(text, setText, placeholder: "Type in island")
                    .Width(250)
                    .AutomationId("Reactor_TextField1")
                    .AutomationName("Island text field"),

                TextBlock($"Text: {text}")
                    .AutomationId("Reactor_TextDisplay"),

                // Focusable button — second Tab stop inside the island
                Button("Island Button", () => setCount(count + 1))
                    .AutomationId("Reactor_Button1")
                    .AutomationName("Island button"),

                TextBlock($"Count: {count}")
                    .AutomationId("Reactor_CountDisplay"),

                // A second text field — third Tab stop
                TextBox("", _ => { }, placeholder: "Second field")
                    .Width(250)
                    .AutomationId("Reactor_TextField2")
                    .AutomationName("Island second field"),

                // Accessibility test targets
                TextBlock("Status: Ready")
                    .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite)
                    .AutomationId("Reactor_LiveRegion"),

                TextBlock("Island rendered successfully")
                    .AutomationId("Reactor_RenderProof")

            ).Padding(16)
        ).Background(SolidBackground);
    }
}
