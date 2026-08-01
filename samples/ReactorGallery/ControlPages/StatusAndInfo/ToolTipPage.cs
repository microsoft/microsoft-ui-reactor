using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.StatusAndInfo;

class ToolTipPage : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(16,
                PageHeader("ToolTip",
                    "A popup that displays helpful text when hovering over an element."),

                SampleCard("Text ToolTip",
                    HStack(16,
                        Button("Hover Me").ToolTip("This is a simple tooltip"),
                        Button("Save").ToolTip("Save the current document (Ctrl+S)")
                    ),
                    @"Button(""Hover Me"").ToolTip(""This is a simple tooltip"")
Button(""Save"").ToolTip(""Save document (Ctrl+S)"")"),

                SampleCard("ToolTip on Various Controls",
                    HStack(16,
                        TextBlock("Hover this text").Foreground(Theme.AccentText)
                            .ToolTip("Text elements can have tooltips too"),
                        CheckBox(false, label: "Enable").ToolTip("Enable the feature"),
                        ToggleSwitch(false).AutomationName("Dark mode")
                            .ToolTip("Toggle dark mode")
                    ),
                    @"TextBlock(""Hover this text"").Foreground(Theme.AccentText)
    .ToolTip(""Text elements can have tooltips too"")
CheckBox(false, label: ""Enable"").ToolTip(""Enable the feature"")
ToggleSwitch(false).AutomationName(""Dark mode"").ToolTip(""Toggle dark mode"")"),

                SampleCard("Rich ToolTip",
                    Button("Rich Tooltip").WithToolTip(
                        VStack(4,
                            TextBlock("Detailed Info").Bold(),
                            TextBlock("This tooltip contains multiple lines of content.")
                                .Foreground(Theme.SecondaryText).FontSize(12)
                        ).Padding(4)),
                    @"Button(""Rich Tooltip"").WithToolTip(
    VStack(4,
        TextBlock(""Title"").Bold(),
        TextBlock(""Description"").FontSize(12)))"),

                SampleCard("ToolTip Placement",
                    HStack(16,
                        Button("Left").ToolTip("Opens to the left", PlacementMode.Left),
                        Button("Right").ToolTip("Opens to the right", PlacementMode.Right),
                        Button("Bottom").ToolTip("Opens below", PlacementMode.Bottom),
                        Button("Follows Mouse").ToolTip("Tracks the pointer", PlacementMode.Mouse)
                    ),
                    @"Button(""Left"").ToolTip(""Opens to the left"", PlacementMode.Left)
Button(""Follows Mouse"").ToolTip(""Tracks the pointer"", PlacementMode.Mouse)

// Placement also composes with a rich tooltip:
Button(""Info"")
    .WithToolTip(VStack(TextBlock(""Title"").Bold()), PlacementMode.Right)")
            ).Margin(36, 24, 36, 36)
        );
    }
}
