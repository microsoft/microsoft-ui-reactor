using Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal sealed class ItemsHostGestureFixture : Component
{
    public override Element Render()
    {
        var (version, setVersion) = UseState(1);
        var (result, setResult) = UseState("pan:none");
        // The same label and visual properties keep the item skip-eligible.
        // Only the gesture closure changes when the parent advances its version.
        // OnTapped participates in equality and would take the ordinary update
        // path. Pan is intentionally excluded and exercises gesture refresh on skip.
        var item = Button("Pan target").Width(240).Height(100)
            .OnPan(onChanged: _ => setResult($"pan:{version}"), minimumDistance: 4)
            .AutomationId("ItemsHostTapTarget").WithKey("target");
        return VStack(
            Button("Refresh callback", () => setVersion(2)).AutomationId("ItemsHostRefresh"),
            TextBlock($"version:{version}").AutomationId("ItemsHostVersion"),
            TextBlock(result).AutomationId("ItemsHostTapResult"),
            ListBoxWrapperElement.ListBox(items: [item]));
    }
}
