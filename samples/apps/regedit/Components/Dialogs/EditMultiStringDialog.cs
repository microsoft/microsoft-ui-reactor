using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record EditMultiStringDialogProps(
    bool IsOpen,
    string ValueName,
    string ValueData,
    Action<string> OnValueDataChanged,
    Action OnSave,
    Action OnCancel
);

internal sealed class EditMultiStringDialog : Component<EditMultiStringDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Strings.EditMultiStringTitle,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.ValueName),
                    TextBox(Props.ValueName, _ => { })
                        .IsReadOnly()
                ),
                VStack(4,
                    TextBlock(Strings.ValueData),
                    TextBox(Props.ValueData, Props.OnValueDataChanged)
                        .Set(tb =>
                        {
                            tb.AcceptsReturn = true;
                            tb.TextWrapping = TextWrapping.Wrap;
                        })
                        .Height(200)
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
