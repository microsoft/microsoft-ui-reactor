using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class RelativePanelPage : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(16,
                PageHeader("RelativePanel", "Positions children relative to each other or the panel edges."),

                SampleCard("Basic RelativePanel",
                    Border(
                        RelativePanel(
                            Border(TextBlock("Top-Left").Center().Foreground("#FFFFFF").Padding(8))
                                .Background("#E74C3C").CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                                .RelativePanel(name: "topLeft", alignLeftWithPanel: true, alignTopWithPanel: true),

                            Border(TextBlock("Right of Red").Center().Foreground("#FFFFFF").Padding(8))
                                .Background("#3498DB").CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                                .RelativePanel(name: "rightBlock", rightOf: "topLeft")
                                .Margin(8, 0, 0, 0),

                            Border(TextBlock("Below Red").Center().Foreground("#FFFFFF").Padding(8))
                                .Background("#2ECC71").CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                                .RelativePanel(name: "belowBlock", below: "topLeft")
                                .Margin(0, 8, 0, 0)
                        )
                    ).Size(400, 200).Background(Theme.SubtleFill).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    """
                    RelativePanel(
                        Border("Top-Left").RelativePanel(name: "topLeft",
                            alignLeftWithPanel: true, alignTopWithPanel: true),
                        Border("Right").RelativePanel(name: "r", rightOf: "topLeft")
                    )
                    """),

                SampleCard("Panel Alignment",
                    Border(
                        RelativePanel(
                            Border(TextBlock("Center").Center().Foreground("#FFFFFF").Padding(8))
                                .Background("#9B59B6").CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                                .RelativePanel(name: "center",
                                    alignHorizontalCenterWithPanel: true,
                                    alignVerticalCenterWithPanel: true),

                            Border(TextBlock("Bottom-Right").Center().Foreground("#FFFFFF").Padding(8))
                                .Background("#E67E22").CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                                .RelativePanel(name: "bottomRight",
                                    alignRightWithPanel: true,
                                    alignBottomWithPanel: true)
                        )
                    ).Size(400, 200).Background(Theme.SubtleFill).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    """
                    Border("Center").RelativePanel(name: "center",
                        alignHorizontalCenterWithPanel: true,
                        alignVerticalCenterWithPanel: true)
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
