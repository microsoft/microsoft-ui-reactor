// Recipe: themed card surface following Win11 design rules.
//
// Pattern: the `Card(child)` factory bakes in Theme.CardBackground,
// Theme.CardStroke (1px), 8px corner radius, and 16px padding — re-themes
// on light/dark/contrast switches. Headings via Subtitle()/Body()/Caption()
// from the WinUI 3 type ramp, not raw FontSize. Override any preset by
// chaining a fluent on the returned border (e.g. .Padding(24)).
// Never hardcode hex on themed surfaces — agents/reviewers will reject it.

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
                    Tile("Storage",   "12% used",         "View details"),
                    Tile("Updates",   "Up to date",       "Check now"),
                    Tile("Bluetooth", "2 devices paired", "Manage")
                ).FlexPadding(16, 16)
            ).Flex(grow: 1)
        ).Backdrop(BackdropKind.Mica);

    // Card(...) factory bakes in background, stroke, corner radius, padding.
    // Bottom margin separates stacked cards — chain any fluent to override.
    static Element Tile(string title, string status, string action) =>
        Card(
            FlexColumn(
                Subtitle(title),
                Caption(status).Foreground(SecondaryText),
                HyperlinkButton(action).Margin(0, 8, 0, 0)))
        .Margin(0, 0, 0, 12);
}
