using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorRegedit.Models;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components.Dialogs;

internal sealed record FindDialogProps(
    bool IsOpen,
    string SearchText,
    FindFlags Flags,
    Action<string> OnSearchTextChanged,
    Action<FindFlags> OnFlagsChanged,
    Action OnFind,
    Action OnCancel
);

internal sealed class FindDialog : Component<FindDialogProps>
{
    public override Element Render()
    {
        return ContentDialog(
            Strings.FindTitle,
            VStack(12,
                VStack(4,
                    TextBlock(Strings.FindWhat),
                    TextBox(Props.SearchText, Props.OnSearchTextChanged)
                ),
                VStack(4,
                    TextBlock(Strings.LookAt),
                    CheckBox(
                        Props.Flags.HasFlag(FindFlags.Keys),
                        v => ToggleFlag(FindFlags.Keys, v),
                        Strings.FindKeys
                    ),
                    CheckBox(
                        Props.Flags.HasFlag(FindFlags.Values),
                        v => ToggleFlag(FindFlags.Values, v),
                        Strings.FindValues
                    ),
                    CheckBox(
                        Props.Flags.HasFlag(FindFlags.Data),
                        v => ToggleFlag(FindFlags.Data, v),
                        Strings.FindData
                    )
                ),
                CheckBox(
                    Props.Flags.HasFlag(FindFlags.WholeStringOnly),
                    v => ToggleFlag(FindFlags.WholeStringOnly, v),
                    Strings.MatchWholeStringOnly
                )
            ).Width(400),
            Strings.FindNext
        ) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = _ => Props.OnCancel(),
        };
    }

    private void ToggleFlag(FindFlags flag, bool enabled)
    {
        var newFlags = enabled
            ? Props.Flags | flag
            : Props.Flags & ~flag;
        Props.OnFlagsChanged(newFlags);
    }
}
