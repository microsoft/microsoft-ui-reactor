using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record InputBarProps(
    string ConnectionState, // "connected", "connecting", "disconnected"
    bool TurnActive,
    ChatPermissionRequest? PendingPermission,
    Action<string> OnSend,
    Action OnStop,
    Action<string, bool> OnPermissionResponse);

/// <summary>
/// Renders the message composer, send/stop action, working indicator, and permission prompt for the active chat.
/// </summary>
public class InputBar : Component<InputBarProps>
{
    public override Element Render()
    {
        var inputState = UseState("", threadSafe: true);

        var sendAction = () =>
        {
            var msg = inputState.Value?.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            Props.OnSend(msg);
            inputState.Set("");
        };
        var sendActionRef = UseRef<Action>(sendAction);
        sendActionRef.Current = sendAction;

        var isConnected = Props.ConnectionState == "connected";
        var placeholder = Props.ConnectionState switch
        {
            "connected" => "Type a message...",
            "connecting" => "Connecting…",
            _ => "Not connected"
        };

        var inputField = Border(
            Grid([GridSize.Auto, GridSize.Star(), GridSize.Auto], [GridSize.Star()],
                Button(
                    TextBlock("+").FontSize(16),
                    () => { /* Future: attach files */ }
                ).Set(b =>
                {
                    b.Background = Res.Get("SubtleFillColorTransparentBrush");
                    b.BorderThickness = new Thickness(0);
                    b.Padding = new Thickness(8, 4, 8, 4);
                    b.MinWidth = 0; b.MinHeight = 0;
                    b.CornerRadius = new CornerRadius(4);
                }).VAlign(VerticalAlignment.Bottom).Grid(row: 0, column: 0),
                TextBox(inputState.Value, v => inputState.Set(v))
                    .Set(tb =>
                    {
                        tb.PlaceholderText = placeholder;
                        tb.AcceptsReturn = false;
                        tb.IsEnabled = isConnected;
                    })
                    .OnMount(fe =>
                    {
                        var textBox = (Microsoft.UI.Xaml.Controls.TextBox)fe;
                        textBox.KeyDown += (s, e) =>
                        {
                            if (e.Key == global::Windows.System.VirtualKey.Enter)
                            {
                                e.Handled = true;
                                sendActionRef.Current();
                            }
                        };
                    }).Margin(8, 0, 8, 0).Grid(row: 0, column: 1),
                // Send or Stop button
                Props.TurnActive
                    ? Button(
                        TextBlock("■").FontSize(14),
                        Props.OnStop
                    ).Set(b =>
                    {
                        b.CornerRadius = new CornerRadius(20);
                        b.Padding = new Thickness(8, 4, 8, 4);
                        b.MinWidth = 32; b.MinHeight = 32;
                        b.Background = Res.Get("SystemFillColorCriticalBrush");
                    }).VAlign(VerticalAlignment.Bottom).Grid(row: 0, column: 2)
                    .AutomationName("Stop response")
                    : Button(
                        TextBlock("↑").FontSize(16),
                        sendAction
                    ).Set(b =>
                    {
                        b.CornerRadius = new CornerRadius(20);
                        b.Padding = new Thickness(8, 4, 8, 4);
                        b.MinWidth = 32; b.MinHeight = 32;
                        b.IsEnabled = isConnected && !string.IsNullOrWhiteSpace(inputState.Value);
                        b.Background = isConnected && !string.IsNullOrWhiteSpace(inputState.Value)
                            ? Res.Get("AccentFillColorDefaultBrush") : Res.Get("SubtleFillColorTransparentBrush");
                    }).VAlign(VerticalAlignment.Bottom).Grid(row: 0, column: 2)
                    .AutomationName("Send message")
            )
        ).Padding(16, 12, 16, 16);

        return VStack(0,
            // Working indicator (spec 033 §5 — Expr keeps the row local to the branch)
            Expr(() => Props.TurnActive
                ? (FlexRow(
                    ProgressRing().Size(16, 16),
                    Caption("Assistant is working…").Foreground(SecondaryText)
                  ) with { ColumnGap = 8 }).Padding(16, 8, 16, 0)
                : Empty()),

            // Permission banner — Expr scopes the destructured `perm` to its consumers.
            Expr(() => Props.PendingPermission is { } perm
                ? Border(
                    HStack(8,
                        TextBlock($"⚠ {perm.ToolName}: {perm.Detail}")
                            .Set(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.CharacterEllipsis; })
                            .HAlign(HorizontalAlignment.Stretch),
                        Button("Allow", () => Props.OnPermissionResponse(perm.RequestId, true))
                            .Background(Accent).Set(b => { b.CornerRadius = new CornerRadius(4); b.Padding = new Thickness(12, 4, 12, 4); b.MinWidth = 0; b.MinHeight = 0; }),
                        Button("Deny", () => Props.OnPermissionResponse(perm.RequestId, false))
                            .Set(b => { b.CornerRadius = new CornerRadius(4); b.Padding = new Thickness(12, 4, 12, 4); b.MinWidth = 0; b.MinHeight = 0; })
                    ).Padding(12, 8, 12, 8)
                  ).Background(SubtleFill).CornerRadius(8).WithBorder(DividerStroke, 1).Margin(12, 4, 12, 4)
                : Empty()),

            inputField
        );
    }
}
