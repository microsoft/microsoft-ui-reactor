using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorRegedit.Models;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record EditDwordDialogProps(
    bool IsOpen,
    string ValueName,
    string ValueData,
    NumberBase NumberBase,
    Action<string> OnValueDataChanged,
    Action<NumberBase> OnBaseChanged,
    Action OnSave,
    Action OnCancel
);

internal sealed class EditDwordDialog : Component<EditDwordDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Strings.EditDwordTitle,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.ValueName),
                    TextBox(Props.ValueName, _ => { })
                        .IsReadOnly()
                ),
                VStack(4,
                    TextBlock(Strings.ValueData),
                    TextBox(Props.ValueData, Props.OnValueDataChanged)
                ),
                VStack(4,
                    TextBlock(Strings.Base),
                    RadioButton(Strings.Hexadecimal,
                        Props.NumberBase == NumberBase.Hexadecimal,
                        _ => Props.OnBaseChanged(NumberBase.Hexadecimal),
                        "dwordBase"),
                    RadioButton(Strings.Decimal,
                        Props.NumberBase == NumberBase.Decimal,
                        _ => Props.OnBaseChanged(NumberBase.Decimal),
                        "dwordBase")
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
