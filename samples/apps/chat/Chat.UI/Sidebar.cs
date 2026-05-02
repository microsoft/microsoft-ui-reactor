using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record SidebarProps(
    ChatThread[] Sessions, string? SelectedId, string? ConnectionStatus,
    Action<string> OnSelect, Action OnNewSession, Action<string> OnSuspend, Action<string> OnDelete,
    string[] ProfileNames, int SelectedProfileIndex, Action<int> OnProfileChanged);

/// <summary>
/// Renders the left navigation rail with connection status, optional profile picker, new-chat action, and recent chat list.
/// </summary>
public class Sidebar : Component<SidebarProps>
{
    bool HostConnected => Props.ConnectionStatus?.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) == true;

    string HostStatusTitle
    {
        get
        {
            if (HostConnected) return "Connected";
            if (Props.ConnectionStatus is null) return "Connecting";
            if (Props.ConnectionStatus.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase)) return "Connecting";
            if (Props.ConnectionStatus.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase)
                || Props.ConnectionStatus.Contains("retrying", StringComparison.OrdinalIgnoreCase))
                return "Reconnecting";
            return "Disconnected";
        }
    }

    string HostStatusDetail => HostConnected
        ? $"{Props.Sessions.Length} session(s)"
        : Props.ConnectionStatus ?? "Connecting…";

    Element ConnectionDot => HostConnected
        ? Border(Empty()).Size(8, 8).CornerRadius(4).Background(Ref("SystemFillColorSuccessBrush")).VAlign(VerticalAlignment.Center)
        : Border(Empty()).Size(8, 8).CornerRadius(4).Background(Ref("SystemFillColorCautionBrush")).VAlign(VerticalAlignment.Center);

    public override Element Render() => Border(
        Grid([GridSize.Star()], [GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
            // Connection status + new session button
            VStack(12,
                (FlexRow(
                     ConnectionDot,
                     VStack(0,
                        TextBlock(HostStatusTitle).SemiBold(),
                        Caption(HostStatusDetail).Foreground(TertiaryText)
                     ).Flex(grow: 1)
                 ) with { ColumnGap = 8 }),

                // Profile picker — only show if multiple profiles
                Props.ProfileNames.Length > 1
                    ? VStack(4,
                        Caption("Profile").Foreground(TertiaryText),
                        ComboBox(Props.ProfileNames, Props.SelectedProfileIndex, Props.OnProfileChanged)
                            .Set(cb =>
                            {
                                cb.HorizontalAlignment = HorizontalAlignment.Stretch;
                                cb.CornerRadius = new CornerRadius(6);
                            })
                    )
                    : Props.ProfileNames.Length == 1
                        ? Caption($"Profile: {Props.ProfileNames[0]}").Foreground(TertiaryText)
                        : (Element)Empty(),

                Button("+ New session", Props.OnNewSession)
                    .Set(b =>
                    {
                        b.HorizontalAlignment = HorizontalAlignment.Stretch;
                        b.HorizontalContentAlignment = HorizontalAlignment.Center;
                        b.Padding = new Thickness(0, 8, 0, 8);
                        b.CornerRadius = new CornerRadius(6);
                    })
            ).Padding(0, 0, 0, 12).Grid(row: 0, column: 0),

            // "Recent" label
            Caption("Recent").Foreground(TertiaryText).SemiBold().Padding(0, 4, 0, 8).Grid(row: 1, column: 0),

            // Divider
            Border(Empty()).Height(1).Background(DividerStroke).Margin(-20, 0, -20, 0).Grid(row: 2, column: 0),

            // Profile indicator when single profile
            Empty().Grid(row: 3, column: 0),

            // Session list
            ScrollView(
                VStack(0,
                    Props.Sessions.Select(s =>
                        (Element)Component<SessionListItem, SessionListItemProps>(
                            new(s, s.Id == Props.SelectedId, Props.OnSelect, Props.OnSuspend, Props.OnDelete))
                    ).ToArray()
                ).Margin(-20, 0, -20, 0)
            ).Grid(row: 4, column: 0),

            // Footer
            (FlexRow(
                Caption(Props.ConnectionStatus ?? "Connecting…").Foreground(TertiaryText).Flex(grow: 1),
                Caption("\uE713").Foreground(TertiaryText)
                    .Set(t => t.FontFamily = new FontFamily("Segoe MDL2 Assets"))
            ) with { ColumnGap = 8 }).Padding(0, 8, 0, 0).Grid(row: 5, column: 0)
        )
    ).Padding(20, 16, 20, 12).Background(LayerFill);
}
