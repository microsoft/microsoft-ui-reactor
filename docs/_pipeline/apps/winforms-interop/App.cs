using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Interop.WinForms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using SWF = System.Windows.Forms;

// <snippet:bootstrap>
XamlIslandBootstrap.Run(() =>
{
    var form = new SWF.Form
    {
        Text = "My WinForms + Reactor App",
        Width = 800,
        Height = 500
    };

    var island = new XamlIslandControl
    {
        ComponentType = typeof(WinFormsHostDemo),
        Dock = SWF.DockStyle.Fill
    };

    form.Controls.Add(island);
    form.Show();
});
// </snippet:bootstrap>

// <snippet:island-control>
class WinFormsHostDemo : Component
{
    public override Element Render()
    {
        // This component is hosted via XamlIslandControl.ComponentType
        var (count, setCount) = UseState(0);

        return VStack(12,
            Heading("Reactor in WinForms"),
            TextBlock($"Count: {count}").FontSize(24),
            Button("+1", () => setCount(count + 1))
        ).Padding(24).Background(SolidBackground);
    }
}
// </snippet:island-control>

// <snippet:designer>
// In the form's Designer.cs file:
//
// this.reactorIsland = new XamlIslandControl();
// this.reactorIsland.ComponentType = typeof(DashboardComponent);
// this.reactorIsland.Dock = DockStyle.Fill;
// this.panel1.Controls.Add(this.reactorIsland);
//
// The Properties grid shows a dropdown of all Component subclasses
// with parameterless constructors. Select your component and the
// designer serializes it as typeof(DashboardComponent).
// </snippet:designer>

// <snippet:keyboard>
class KeyboardDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");

        // Tab moves focus into this Reactor tree from WinForms controls.
        // Tab/Shift+Tab cycles through Reactor controls normally.
        // Tab out of the last Reactor control returns focus to WinForms.
        return VStack(12,
            TextBox(text, setText, placeholder: "Type here...")
                .TabIndex(0),
            Button("Submit", () => { })
                .TabIndex(1)
                .AccessKey("S")
        ).Padding(24).Background(SolidBackground);
    }
}
// </snippet:keyboard>

// <snippet:accessibility>
class AccessibleIslandComponent : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");

        // All accessibility modifiers work inside XAML Islands
        return VStack(12,
            Heading("Registration")
                .HeadingLevel(AutomationHeadingLevel.Level1),
            TextBox(name, setName, header: "Full Name")
                .AutomationName("Full name")
                .Required()
                .TabIndex(0),
            Button("Register", () => { })
                .AutomationName("Submit registration")
                .TabIndex(1)
        ).Padding(24)
         .Landmark(AutomationLandmarkType.Form)
         .Background(SolidBackground);
    }
}
// </snippet:accessibility>

// <snippet:background>
class BackgroundDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        // Always set an explicit background on root content.
        // XAML Islands have no default background — without this,
        // content renders on a transparent surface.
        return VStack(12,
            TextBlock("Theme-aware background").Bold(),
            TextBlock($"Count: {count}"),
            Button("Increment", () => setCount(count + 1))
        ).Padding(24).Background(SolidBackground);
    }
}
// </snippet:background>

// <snippet:content-factory>
// Use ContentFactory for components needing parameters:
//
// var island = new XamlIslandControl
// {
//     ContentFactory = () =>
//     {
//         var host = new ReactorHostControl();
//         host.SetComponent<ConfigurableComponent>();
//         return host;
//     }
// };

class ConfigurableComponent : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(12,
            Heading("Dashboard"),
            TextBlock($"Value: {count}"),
            Button("+1", () => setCount(count + 1))
        ).Padding(24).Background(SolidBackground);
    }
}
// </snippet:content-factory>
