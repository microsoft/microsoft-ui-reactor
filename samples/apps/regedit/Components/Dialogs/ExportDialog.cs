using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record ExportDialogProps(
    bool IsOpen,
    bool ExportAll,
    string SelectedBranch,
    Action<bool> OnExportAllChanged,
    Action OnExport,
    Action OnCancel
);

internal sealed class ExportDialog : Component<ExportDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Strings.ExportTitle,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.ExportRange),
                    RadioButton(Strings.All,
                        Props.ExportAll,
                        _ => Props.OnExportAllChanged(true),
                        "exportRange"),
                    RadioButton(Strings.SelectedBranch,
                        !Props.ExportAll,
                        _ => Props.OnExportAllChanged(false),
                        "exportRange")
                ),
                When(!Props.ExportAll, () =>
                    TextBox(Props.SelectedBranch, _ => { })
                        .IsReadOnly()
                )
            ).Width(400),
            Strings.Export
        ) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = _ => Props.OnCancel(),
        };
    }
}
