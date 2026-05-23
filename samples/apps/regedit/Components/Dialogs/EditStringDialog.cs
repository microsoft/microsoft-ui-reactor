using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record EditStringDialogProps(
    bool IsOpen,
    string Title,
    string ValueName,
    string ValueData,
    Action<string> OnValueDataChanged,
    Action OnSave,
    Action OnCancel
);

internal sealed class EditStringDialog : Component<EditStringDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Props.Title,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.ValueName),
                    TextBox(Props.ValueName, _ => { })
                        .IsReadOnly()
                ),
                VStack(4,
                    TextBlock(Strings.ValueData),
                    TextBox(Props.ValueData, Props.OnValueDataChanged)
                )
            ).Width(400),
            Strings.OK
        ) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = _ => Props.OnCancel(),
        };
    }
}
