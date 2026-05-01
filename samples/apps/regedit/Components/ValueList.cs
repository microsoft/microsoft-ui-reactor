using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorRegedit.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components;

internal sealed record ValueListProps(
    RegistryValueEntry[] Values,
    int SelectedIndex,
    Action<int> OnSelectionChanged,
    Action<RegistryValueEntry> OnModify,
    Action<RegistryValueEntry> OnModifyBinary,
    Action<RegistryValueEntry> OnDelete,
    Action<RegistryValueEntry> OnRename
);

internal sealed class ValueList : Component<ValueListProps>
{
    // Segoe MDL2 Assets glyph codes
    private const string StringIcon = "\uE8A5";   // document icon for string values
    private const string BinaryIcon = "\uE9F5";   // memory/chip icon for binary values

    public override Element Render()
    {
        var values = Props.Values;

        // Column header
        var header = Grid(
            [GridSize.Star(2), GridSize.Star(), GridSize.Star(3)],
            [GridSize.Px(32)],
            TextBlock(Strings.ColumnName).SemiBold().VAlign(VerticalAlignment.Center).Grid(row: 0, column: 0),
            TextBlock(Strings.ColumnType).SemiBold().VAlign(VerticalAlignment.Center).Grid(row: 0, column: 1),
            TextBlock(Strings.ColumnData).SemiBold().VAlign(VerticalAlignment.Center).Grid(row: 0, column: 2)
        ).Set(g =>
        {
            g.Padding = new Thickness(8, 0, 8, 0);
            g.BorderThickness = new Thickness(0, 0, 0, 1);
            g.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        });

        var list = LazyVStack<RegistryValueEntry>(
            values,
            v => v.Name + v.Kind,
            (value, index) => RenderValueRow(value, index)
        ) with { EstimatedItemSize = 28, Spacing = 0 };

        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Star()],
            header.Grid(row: 0, column: 0),
            list.Grid(row: 1, column: 0)
        );
    }

    private Element RenderValueRow(RegistryValueEntry value, int index)
    {
        var icon = TextBlock(GetValueIcon(value.Kind))
            .Set(tb =>
            {
                tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
                tb.FontSize = 14;
            });

        var row = Grid(
            [GridSize.Star(2), GridSize.Star(), GridSize.Star(3)],
            [GridSize.Px(28)],
            HStack(6,
                icon.VAlign(VerticalAlignment.Center),
                TextBlock(value.DisplayName).VAlign(VerticalAlignment.Center)
            ).Grid(row: 0, column: 0),
            TextBlock(value.TypeName)
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 0, column: 1),
            TextBlock(value.DisplayData)
                .Set(tb =>
                {
                    tb.TextTrimming = TextTrimming.CharacterEllipsis;
                    tb.TextWrapping = TextWrapping.NoWrap;
                })
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 0, column: 2)
        ).Padding(8, 0, 8, 0)
         .OnPointerEntered((s, _) =>
             ((Microsoft.UI.Xaml.Controls.Grid)s).Background =
                 (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"])
         .OnPointerExited((s, _) =>
             ((Microsoft.UI.Xaml.Controls.Grid)s).Background = null)
         .OnTapped((_, _) => Props.OnSelectionChanged(index))
         .OnDoubleTapped((_, _) => Props.OnModify(value));

        // Context flyout for right-click
        return MenuFlyout(
            row,
            [
                MenuItem(Strings.Modify, () => Props.OnModify(value)),
                MenuItem(Strings.ModifyBinaryData, () => Props.OnModifyBinary(value)),
                MenuSeparator(),
                MenuItem(Strings.Delete, () => Props.OnDelete(value)),
                MenuItem(Strings.Rename, () => Props.OnRename(value)),
            ]
        );
    }

    private static string GetValueIcon(Microsoft.Win32.RegistryValueKind kind) => kind switch
    {
        Microsoft.Win32.RegistryValueKind.Binary => BinaryIcon,
        Microsoft.Win32.RegistryValueKind.DWord => BinaryIcon,
        Microsoft.Win32.RegistryValueKind.QWord => BinaryIcon,
        _ => StringIcon
    };
}
