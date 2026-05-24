using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class ComboBoxPage: Component
{
    public override Element Render()
    {
        var colors = new[] { "Red", "Green", "Blue", "Yellow" };
        var (selectedIndex, setSelectedIndex) = UseState(0);
        var (editableIndex, setEditableIndex) = UseState(-1);

        return ScrollView(VStack(16,
            PageHeader("ComboBox", "A drop-down list of items a user can select from."),

            SampleCard("Basic ComboBox",
                VStack(8,
                    ComboBox(colors, selectedIndex, i => setSelectedIndex(i)),
                    TextBlock($"Selected: {colors[selectedIndex]}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
ComboBox(colors, selectedIndex, i => setSelectedIndex(i))
"),

            SampleCard("ComboBox with Placeholder",
                ComboBox(colors).PlaceholderText("Pick a color"),
                sourceCode: @"
ComboBox(colors).PlaceholderText(""Pick a color"")
"),

            SampleCard("Editable ComboBox",
                ComboBox(colors, editableIndex, i => setEditableIndex(i)).IsEditable(),
                sourceCode: @"
ComboBox(colors, editableIndex, i => setEditableIndex(i)).IsEditable()
")
        ).Margin(36, 24, 36, 36));
    }
}
