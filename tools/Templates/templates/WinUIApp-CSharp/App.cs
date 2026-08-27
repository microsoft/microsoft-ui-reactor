using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;         // BackdropKind
using Microsoft.UI.Reactor.Layout;        // FlexDirection, FlexJustify, FlexAlign
using Microsoft.UI.Xaml;                  // Thickness, HorizontalAlignment, VerticalAlignment
using Microsoft.UI.Xaml.Controls;         // Orientation, InfoBarSeverity, etc.
using static Microsoft.UI.Reactor.Factories;

#if (csharpFeature_TopLevelProgram)
ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600,
    icon: WindowIcon.FromPath("Assets/AppIcon.ico"));

#else
namespace Company.ReactorApp1;

class Program
{
    static void Main(string[] args)
    {
        ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600,
            icon: WindowIcon.FromPath("Assets/AppIcon.ico"));
    }
}

#endif
class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        // App-mark icon drawn *inside* the title bar. This is separate from the
        // window icon set via ReactorApp.Run(icon:) above, which is what Windows
        // shows in the taskbar, Alt-Tab, and Task Manager. The two may differ --
        // a monochrome mark here, a full-colour .ico there. This placeholder is a
        // Segoe Fluent Icons glyph (U+EA3A); swap it for a bundled asset, e.g.
        // .Icon("ms-appx:///Assets/AppIcon.ico").
        var titleBar = TitleBar("Company.ReactorApp1")
            .Icon(FontIcon("\uEA3A", "Segoe Fluent Icons"))
            .Flex(shrink: 0);

        var body = Border(
            FlexColumn(
                Heading($"Hello, {name}!"),
                TextBox(name, setName, placeholderText: "Your name")
                    .AutomationName("NameInput")
            ) with { RowGap = 16 }
        ).Padding(24).Flex(grow: 1, basis: 0);

        return FlexColumn(titleBar, body)
            .Backdrop(BackdropKind.Mica);
    }
}
