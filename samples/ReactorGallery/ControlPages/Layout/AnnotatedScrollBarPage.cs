using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

class AnnotatedScrollBarPage : Component
{
    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("AnnotatedScrollBar", "Shows a scrollbar surface that can display app-defined annotations."),
            SampleCard("Standalone control surface",
                VStack(8,
                    Border(AnnotatedScrollBar().Height(300).Width(40))
                        .Padding(12)
                        .Background(Theme.CardBackground)
                        .WithBorder(Theme.DividerStroke),
                    Caption("AnnotatedScrollBar is typically composed with a scrolling surface such as ItemsView or ScrollView to show section labels; full wiring is app-specific.")
                ),
                sourceCode: @"Border(AnnotatedScrollBar().Height(300).Width(40))
    .Padding(12)
    .Background(Theme.CardBackground)
    .WithBorder(Theme.DividerStroke)")
        ).Margin(36, 24, 36, 36));
    }
}
