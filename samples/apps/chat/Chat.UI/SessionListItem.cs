using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record SessionListItemProps(ChatThread Session, bool IsSelected, Action<string> OnSelect, Action<string> OnSuspend, Action<string> OnDelete);

/// <summary>
/// Renders one sidebar row for a chat, including selection state, status indicator, metadata, and context actions.
/// </summary>
public class SessionListItem : Component<SessionListItemProps>
{
    ChatThread S => Props.Session;

    string WorkspaceLabel => S.Workspace ?? S.Repository?.Split('/').LastOrDefault() ?? "";
    string TimeLabel => S.UpdatedAt.HasValue ? TimeFormat.Relative(S.UpdatedAt.Value) : "";
    string HostLabel => S.HostName is { Length: > 0 } h ? h : "";

    Element StatusIndicator => S.Activity switch
    {
        ChatActivity.Working => Border(Empty()).Size(8, 8).CornerRadius(4).Background("#10b981"),
        ChatActivity.AwaitingInput or ChatActivity.AwaitingPermission
            => Border(Empty()).Size(8, 8).CornerRadius(4).Background("#f59e0b"),
        _ when S.Status == ChatThreadStatus.Running
            => Border(Empty()).Size(8, 8).CornerRadius(4).Background("#3b82f6"),
        _ when S.Status == ChatThreadStatus.Suspended
            => Border(Empty()).Size(8, 8).CornerRadius(4).Background("#6b7280"),
        _ => Empty(),
    };

    public override Element Render()
    {
        var selectedBar = Props.IsSelected
            ? Border(Empty()).Width(3).Background(Accent).VAlign(VerticalAlignment.Stretch).HAlign(HorizontalAlignment.Left)
            : Empty();

        return Button(
            Grid(["*"], ["*"],
                VStack(2,
                    FlexRow(
                        StatusIndicator.VAlign(VerticalAlignment.Center),
                        TextBlock(S.DisplayTitle).SemiBold()
                            .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; })
                            .Flex(grow: 1)
                    ) with { ColumnGap = 8 },
                    (FlexRow(
                        Caption(TimeLabel).Foreground(TertiaryText),
                        WorkspaceLabel.Length > 0
                            ? Caption($"· {WorkspaceLabel}").Foreground(TertiaryText) : Empty(),
                        HostLabel.Length > 0
                            ? Caption($"· {HostLabel}").Foreground(TertiaryText) : Empty()
                    ) with { ColumnGap = 4 })
                ).Padding(20, 10, 20, 10).Grid(row: 0, column: 0),
                selectedBar.Grid(row: 0, column: 0)
            ),
            () => Props.OnSelect(S.Id)
        ).Set(b =>
        {
            b.BorderThickness = new Thickness(0);
            b.Padding = new Thickness(0);
            b.CornerRadius = new CornerRadius(0);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.ContextFlyout = BuildContextMenu();
        }).Resources(r => r
            .Set("ButtonBackground", Props.IsSelected ? Ref("SubtleFillColorTertiaryBrush") : Ref("SubtleFillColorTransparentBrush"))
            .Set("ButtonBackgroundPointerOver", Ref("SubtleFillColorSecondaryBrush"))
            .Set("ButtonBackgroundPressed", Ref("SubtleFillColorTertiaryBrush"))
            .Set("ButtonBorderBrush", Ref("SubtleFillColorTransparentBrush"))
            .Set("ButtonBorderBrushPointerOver", Ref("SubtleFillColorTransparentBrush"))
            .Set("ButtonBorderBrushPressed", Ref("SubtleFillColorTransparentBrush"))
        ).WithKey(S.Id);
    }

    Microsoft.UI.Xaml.Controls.MenuFlyout BuildContextMenu()
    {
        var suspendItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = S.Status == ChatThreadStatus.Suspended ? "Resume" : "Suspend",
        };
        suspendItem.Click += (_, _) => Props.OnSuspend(S.Id);

        var deleteItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Delete",
        };
        deleteItem.Click += (_, _) => Props.OnDelete(S.Id);

        return new Microsoft.UI.Xaml.Controls.MenuFlyout { Items = { suspendItem, deleteItem } };
    }
}
