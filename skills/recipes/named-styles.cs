// Recipe: named-style fluents for buttons, hyperlinks, and InfoBars.
//
// Pattern: named-style fluents are zero-arg modifiers that apply the
// matching WinUI theme resource (AccentButtonStyle, SubtleButtonStyle,
// TextBlockButtonStyle for .TextLink() on Button / HyperlinkButton,
// InfoBarSeverity.*). They re-theme on light / dark / contrast switches
// and stack with other fluents (.Padding, .Click, etc.).

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

ReactorApp.Run<App>("Named styles", width: 560, height: 520);

class App : Component
{
    public override Element Render() =>
        ScrollView(
            VStack(20,
                Subtitle("Buttons"),
                HStack(8,
                    Button("Default", () => { }),
                    // Accent — primary call-to-action color.
                    Button("Accent", () => { }).AccentButton(),
                    // Subtle — transparent until hover; for toolbar / chrome.
                    Button("Subtle", () => { }).SubtleButton(),
                    // TextLink — borderless, hyperlink-style; flips between
                    // Button and HyperlinkButton with the same fluent name.
                    Button("Text link", () => { }).TextLink()),

                Subtitle("Hyperlinks"),
                HyperlinkButton("Open docs").TextLink(),

                Subtitle("InfoBars"),
                // Severity fluents — short aliases for .Severity(Info|...).
                InfoBar("Tip", "You can drag the divider.").Informational(),
                InfoBar("Saved", "Changes written to disk.").Success(),
                InfoBar("Heads up", "Unsaved changes will be discarded.").Warning(),
                InfoBar("Failed", "Couldn't reach the server.").Error()
            ).Padding(24));
}
