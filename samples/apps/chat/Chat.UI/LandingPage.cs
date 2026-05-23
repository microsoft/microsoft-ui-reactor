using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record LandingPageProps(
    ChatThread[] Sessions,
    Func<Action<string>, Action, Element>? WorkspacePicker,
    Action<string, string?> OnCreateWithMessage, // (message, workspace?)
    Action<string> OnSelectSession);

/// <summary>
/// Shows the empty-selection landing page with a starter prompt and recent chat shortcuts.
/// </summary>
public class LandingPage : Component<LandingPageProps>
{
    public override Element Render()
    {
        var inputState = UseState("", threadSafe: true);
        var selectedWorkspace = UseState<string?>(null, threadSafe: true);
        var showWorkspacePicker = UseState(false, threadSafe: true);

        var createAction = () =>
        {
            var msg = inputState.Value?.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            Props.OnCreateWithMessage(msg, selectedWorkspace.Value);
            inputState.Set("");
        };
        var createActionRef = UseRef<Action>(createAction);
        createActionRef.Current = createAction;

        var wsLabel = selectedWorkspace.Value is { } ws ? $"📁 {ws}" : "📁 Workspace (optional)";

        if (showWorkspacePicker.Value && Props.WorkspacePicker is not null)
            return Props.WorkspacePicker(
                ws => { selectedWorkspace.Set(ws); showWorkspacePicker.Set(false); },
                () => showWorkspacePicker.Set(false));

        // Recent session cards
        var recentCards = Props.Sessions.Take(3).Select(s =>
        {
            var sid = s.Id;
            var timeStr = (s.UpdatedAt ?? s.CreatedAt) is { } dt ? TimeFormat.Relative(dt) : "";
            var wsStr = s.Workspace ?? s.Cwd ?? "";
            return (Element)Button(
                VStack(2,
                    TextBlock(s.DisplayTitle).SemiBold()
                        .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; }),
                    Caption($"{timeStr} · {wsStr}")
                        .Foreground(TertiaryText)
                        .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; })
                ),
                () => Props.OnSelectSession(sid)
            ).Set(b =>
            {
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Padding = new Thickness(16, 12, 16, 12);
                b.CornerRadius = new CornerRadius(8);
                b.MinWidth = 160;
            }).Resources(r => r
                .Set("ButtonBackground", SubtleFill)
                .Set("ButtonBackgroundPointerOver", Ref("SubtleFillColorSecondaryBrush"))
                .Set("ButtonBackgroundPressed", Ref("SubtleFillColorTertiaryBrush"))
                .Set("ButtonBorderBrush", DividerStroke)
            ).Flex(grow: 1, basis: 0);
        }).ToArray();

        return VStack(24,
            // Heading
            SubHeading("What would you like to explore?").HAlign(HorizontalAlignment.Center),

            // Input bar
            Border(
                Grid([GridSize.Auto, GridSize.Star(), GridSize.Auto], [GridSize.Star()],
                    Caption("+").Foreground(SecondaryText).VAlign(VerticalAlignment.Center)
                        .Padding(8, 0, 0, 0).Grid(row: 0, column: 0),
                    TextBox(inputState.Value, v => inputState.Set(v))
                        .Set(tb =>
                        {
                            tb.PlaceholderText = "Ask the sample assistant...";
                            tb.AcceptsReturn = false;
                        })
                        .OnMount(fe =>
                        {
                            var textBox = (Microsoft.UI.Xaml.Controls.TextBox)fe;
                            textBox.KeyDown += (s, e) =>
                            {
                                if (e.Key == global::Windows.System.VirtualKey.Enter)
                                {
                                    e.Handled = true;
                                    createActionRef.Current();
                                }
                            };
                        }).Margin(4, 0, 4, 0).Grid(row: 0, column: 1),
                    Button(
                        TextBlock("↑").FontSize(16),
                        createAction
                    ).Set(b =>
                    {
                        b.CornerRadius = new CornerRadius(20);
                        b.Padding = new Thickness(8, 4, 8, 4);
                        b.MinWidth = 32; b.MinHeight = 32;
                        b.IsEnabled = !string.IsNullOrWhiteSpace(inputState.Value);
                        b.Background = !string.IsNullOrWhiteSpace(inputState.Value)
                            ? Res.Get("AccentFillColorDefaultBrush") : Res.Get("SubtleFillColorTransparentBrush");
                    }).VAlign(VerticalAlignment.Center).Grid(row: 0, column: 2)
                )
            ).WithBorder(DividerStroke, 1).CornerRadius(8).Padding(4, 4, 4, 4),

            // Workspace pill
            Props.WorkspacePicker is not null
                ? Button(wsLabel, () => showWorkspacePicker.Set(true))
                    .Set(b =>
                    {
                        b.Padding = new Thickness(12, 6, 12, 6);
                        b.CornerRadius = new CornerRadius(16);
                    }).Resources(r => r
                        .Set("ButtonBackground", SubtleFill)
                        .Set("ButtonBackgroundPointerOver", Ref("SubtleFillColorSecondaryBrush"))
                        .Set("ButtonBackgroundPressed", Ref("SubtleFillColorTertiaryBrush"))
                        .Set("ButtonBorderBrush", DividerStroke))
                    .HAlign(HorizontalAlignment.Left)
                : Empty(),

            // "or continue" divider + recent cards
            When(recentCards.Length > 0, () =>
                VStack(16,
                    (FlexRow(
                        Border(Empty()).Height(1).Background(DividerStroke).Flex(grow: 1),
                        Caption("or continue").Foreground(TertiaryText),
                        Border(Empty()).Height(1).Background(DividerStroke).Flex(grow: 1)
                    ) with { ColumnGap = 12 }),
                    (FlexRow([.. recentCards]) with { ColumnGap = 8, Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap })
                ))
        ).MaxWidth(600).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
         .Padding(40, 0, 40, 0);
    }
}
