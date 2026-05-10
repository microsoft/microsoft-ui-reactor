// Recipe: themed card surface following Win11 design rules.
//
// Pattern: Border + Theme.CardBackground for fill, Theme.CardStroke for the
// 1px hairline, .CornerRadius(8), .Padding aligned to the 4px grid. Headings
// via Heading()/SubHeading() not raw FontSize. Never hardcode hex on themed
// surfaces — agents/reviewers will reject it.

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<App>("Card demo", width: 560, height: 480);

class App : Component
{
    public override Element Render() =>
        FlexColumn(
            (TitleBar("Cards") with { Subtitle = "Win11 design tokens" }).Flex(shrink: 0),
            ScrollView(
                FlexColumn(
                    Card("Storage",   "12% used",        "View details"),
                    Card("Updates",   "Up to date",      "Check now"),
                    Card("Bluetooth", "2 devices paired", "Manage")
                ).FlexPadding(16, 16)
            ).Flex(grow: 1)
        ).Backdrop(BackdropKind.Mica);

    static Element Card(string title, string status, string action) =>
        Border(
            FlexColumn(
                SubHeading(title),
                Caption(status).Foreground(SecondaryText),
                HyperlinkButton(action).Margin(0, 8, 0, 0)
            ).FlexPadding(16))
        .Background(CardBackground)
        .WithBorder(CardStroke, 1)
        .CornerRadius(8)
        .Margin(0, 0, 0, 12);
}
