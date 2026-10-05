using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Compiled-XAML panel declaring <c>&lt;reactor:ReactorHostControl ComponentType="local:XamlDeclaredHostComponent"/&gt;</c>,
/// so <see cref="HostingCoverageFixtures.HostControlXamlComponentType"/> exercises the real
/// markup path: the XAML parser sets <see cref="ReactorHostControl.ComponentType"/>, and the
/// host creates the root through this app's generated XAML type information.
/// </summary>
public sealed partial class XamlDeclaredHostPanel : UserControl
{
    public XamlDeclaredHostPanel() => InitializeComponent();

    internal ReactorHostControl HostControl => Host;
}

public sealed class XamlDeclaredHostComponent : Component<string>
{
    public override Element Render()
    {
        var (clicks, setClicks) = UseState(0);
        return VStack(
            TextBlock($"XamlDeclared:{Props}:{clicks}"),
            Button("XamlDeclaredInc", () => setClicks(clicks + 1)));
    }
}
