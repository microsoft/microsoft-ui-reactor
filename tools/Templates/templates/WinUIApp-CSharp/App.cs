using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;   // FlexDirection, FlexJustify, FlexAlign
using Microsoft.UI.Xaml;             // Thickness, HorizontalAlignment, VerticalAlignment
using Microsoft.UI.Xaml.Controls;    // Orientation, InfoBarSeverity, etc.
using static Microsoft.UI.Reactor.Factories;

#if (csharpFeature_TopLevelProgram)
ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600);

#else
namespace Company.ReactorApp1;

class Program
{
    static void Main(string[] args)
    {
        ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600);
    }
}

#endif
class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        return VStack(
            Heading($"Hello, {name}!"),
            TextField(name, setName, placeholder: "Your name").AutomationName("NameInput")
        );
    }
}