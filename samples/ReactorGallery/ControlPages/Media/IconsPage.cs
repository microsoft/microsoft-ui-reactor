using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class IconsPage : Component
{
    static Element Cell(string label, Element icon) =>
        VStack(6,
            Border(icon.Center()).Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
            Caption(label).Foreground(Theme.SecondaryText).Center());

    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("Icons", "Standalone Icon elements built from Symbol, Font glyph, or Path sources."),

            SampleCard("Symbol icons",
                HStack(16,
                    Cell("Home", Icon(Symbol.Home)),
                    Cell("Save", Icon(Symbol.Save)),
                    Cell("Delete", Icon(Symbol.Delete)),
                    Cell("Setting", Icon(Symbol.Setting))),
                sourceCode: @"
Icon(Symbol.Home)
Icon(Symbol.Save)
Icon(Symbol.Delete)
Icon(Symbol.Setting)
"),

            SampleCard("Font glyph and Path icons",
                HStack(16,
                    Cell("FontIcon", Icon(FontIcon("\uE790"))),
                    Cell("FontIcon", Icon(FontIcon("\uE734"))),
                    Cell("PathIcon", Icon(PathIcon("M 0,0 L 24,0 L 12,20 Z")))),
                sourceCode: @"
Icon(FontIcon(""\uE790""))              // Segoe Fluent Icons glyph
Icon(PathIcon(""M 0,0 L 24,0 L 12,20 Z""))  // vector path data
")
        ).Margin(36, 24, 36, 36));
    }
}
