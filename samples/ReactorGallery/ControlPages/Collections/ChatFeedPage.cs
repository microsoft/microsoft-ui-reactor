using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Collections;

class ChatFeedPage : Component
{
    public override Element Render() => ScrollView(VStack(16,
        PageHeader("Chat feed", "A composition of ItemsView, AnnotatedScrollBar, TextBox, and SplitView. No chat-specific framework controls."),
        SampleCard("Conversation with history and a resizable pane",
            Component<ChatFeedDemo>(),
            """
            var (tailRequest, setTailRequest) = UseState(0);
            var (following, setFollowing) = UseState(true);
            var itemsRef = this.UseElementRef<Microsoft.UI.Xaml.Controls.ItemsView>();

            ItemsView(messages, message => message.Id,
                (message, _) => ItemContainer(TextBlock(message.Text).TextWrapping(TextWrapping.Wrap)))
                .Ref(itemsRef)
                .FollowTail(onFollowingChanged: setFollowing)
                .ScrollToTail(tailRequest)
                .ScrollIntoView(navigation.Key, navigation.Request);

            // OpenClaw's native tick template and hover preview, independent of pixel estimates.
            AnnotatedScrollBar()
                .ScrollRange(1)
                .Set(bar => OpenClawHistoryRail.Update(bar, historyEntries, itemsRef.Current, Navigate))
                .OnUnmount(OpenClawHistoryRail.Detach);

            var composer = OpenClawChatStyles.Composer(
                TextBox(draft, setDraft, "Message Assistant")
                    .AutomationName("Message")
                    .SubmitOnEnter(Send), // native text; Shift+Enter inserts a newline
                toolbar, viewportWidth);

            (SplitView(pane: details, content: conversation) with
                { DisplayMode = SplitViewDisplayMode.Inline, OpenPaneLength = paneWidth })
                .ResizablePane(minimum: 160, maximum: 360, onLengthChanged: setPaneWidth);
            """),
        Caption("Try: scroll up, append or stream a reply, then Jump to latest. Hover or click a history tick; use Up/Down/Home/End on the focused rail. Drag the pane divider or use Left/Right/Home/End. Shift+Enter adds a line; Enter sends.")
    ).Margin(36, 24, 36, 36));
}

class ChatFeedDemo : Component
{
    private sealed record Message(string Id, string Sender, string Text);
    private static readonly Message[] InitialMessages = Enumerable.Range(1, 24)
        .Select(index => new Message($"message-{index}", index % 2 == 0 ? "Assistant" : "You",
            index % 2 == 0
                ? $"Reply {index / 2}: These are native WinUI controls rendered by Reactor. Scroll back to read without being pulled away when another message arrives."
                : $"Message {(index + 1) / 2}: Show me how reusable scrolling and input behaviors work."))
        .ToArray();

    public override Element Render()
    {
        var (messages, updateMessages) = UseReducer(InitialMessages);
        var (draft, setDraft) = UseState("");
        var (following, setFollowing) = UseState(true);
        var (tailRequest, setTailRequest) = UseState(0);
        var (navigation, setNavigation) = UseState((Key: (string?)null, Request: 0));
        var (paneOpen, setPaneOpen) = UseState(true);
        var (paneWidth, setPaneWidth) = UseState(320.0);
        var (streaming, setStreaming) = UseState(false);
        var (viewportWidth, setViewportWidth) = UseState(800.0);
        var (availableWidth, setAvailableWidth) = UseState(1100.0);
        var (hovered, setHovered) = UseState<string?>(null);
        var (focused, setFocused) = UseState<string?>(null);
        var (attachment, setAttachment) = UseState(false);
        var (model, setModel) = UseState("Default model");
        var itemsRef = this.UseElementRef<Microsoft.UI.Xaml.Controls.ItemsView>();
        var streamId = UseRef<string?>(null);

        UseEffect(() =>
        {
            if (!streaming) return () => { };
            var ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            timer.Tick += (_, _) =>
            {
                updateMessages(current => current.Select(message => message.Id == streamId.Current
                    ? message with { Text = message.Text + " Growing content follows the tail without moving a reader in history." }
                    : message).ToArray());
                if (++ticks >= 12) setStreaming(false);
            };
            timer.Start();
            return () => timer.Stop();
        }, streaming);

        void JumpToLatest() => setTailRequest(tailRequest + 1);
        void Send(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            updateMessages(current => [.. current, new(Guid.NewGuid().ToString("N"), "You", value)]);
            setDraft("");
            JumpToLatest();
        }
        void Append() => updateMessages(current =>
            [.. current, new(Guid.NewGuid().ToString("N"), "Assistant", "A new reply arrived. Reading older messages should keep your position.")]);
        void StartStream()
        {
            if (streaming) return;
            var id = Guid.NewGuid().ToString("N");
            streamId.Current = id;
            updateMessages(current => [.. current, new(id, "Assistant", "Streaming reply:")]);
            setStreaming(true);
        }
        void Navigate(string key) => setNavigation((key, navigation.Request + 1));
        void Copy(string text)
        {
            var data = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(text);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        }

        Element MessageBody(Message message)
        {
            var footer = HStack(8,
                TextBlock(message.Sender == "You" ? "Just now" : "Assistant").FontSize(12).LineHeight(18)
                    .Foreground(Theme.SecondaryText),
                OpenClawChatStyles.ToolbarButton("\uE8C8", "Copy message", () => Copy(message.Text))
                    .Padding(4).CornerRadius(4).Foreground(Theme.SecondaryText))
                .Opacity(hovered == message.Id || focused == message.Id ? 1 : 0)
                .IsHitTestVisible(hovered == message.Id || focused == message.Id);
            if (message.Sender == "You")
                return VStack(8,
                    Border(OpenClawChatStyles.Prose(message.Text, user: true))
                        .Set(OpenClawChatStyles.UserBubble)
                        .CornerRadius(12).Padding(16, 12).MaxWidth(720).HAlign(HorizontalAlignment.Right),
                    footer.Margin(16, 2, 4, 0).HAlign(HorizontalAlignment.Right))
                    .Margin(32, 4, 0, 4);

            var avatarSlot = viewportWidth >= 960 ? 46 : 0;
            return Grid(columns: [GridSize.Px(avatarSlot), GridSize.Star(), GridSize.Px(avatarSlot)],
                rows: [GridSize.Auto], children:
                [
                    (avatarSlot > 0 ? PersonPicture().Set(p => p.Initials = "AI").Size(36, 36)
                        .Margin(0, 0, 10, 0).VAlign(VerticalAlignment.Top).AutomationName("Assistant") : Empty()).Grid(column: 0),
                    VStack(4, OpenClawChatStyles.Prose(message.Text), footer).Grid(column: 1),
                ]).Margin(0, 4);
        }

        var historyEntries = messages.Select(message => new HistoryEntry(message.Id, message.Sender, message.Text)).ToArray();
        var gutter = OpenClawChatStyles.Gutter(viewportWidth);
        var feed = (ItemsView(messages, message => message.Id,
            (message, _) => ItemContainer(
                Border(MessageBody(message))
                    .MaxWidth(OpenClawChatStyles.ReadingWidth - 32 + (message.Sender != "You" && viewportWidth >= 960 ? 92 : 0))
                    .Margin(gutter + 16 - (message.Sender != "You" && viewportWidth >= 960 ? 46 : 0), 0,
                        gutter + 16 - (message.Sender != "You" && viewportWidth >= 960 ? 46 : 0),
                        message.Id == messages[^1].Id ? OpenClawChatStyles.TranscriptBottomInset : 0)
                    .OnPointerEntered((_, _) => setHovered(message.Id))
                    .OnPointerExited((_, _) => { if (hovered == message.Id) setHovered(null); })
                    .OnGotFocus((_, _) => setFocused(message.Id))
                    .OnLostFocus((_, _) => { if (focused == message.Id) setFocused(null); })
            ).Resources(OpenClawChatStyles.ItemResources).IsTabStop(false)
                .Set(container => container.HorizontalContentAlignment = HorizontalAlignment.Stretch)
            ) with { SelectionMode = ItemsViewSelectionMode.None })
            .FollowTail(onFollowingChanged: setFollowing)
            .ScrollToTail(tailRequest)
            .ScrollIntoView(navigation.Key, navigation.Request)
            .Ref(itemsRef).Set(OpenClawChatStyles.Feed).OnUnmount(OpenClawChatStyles.ReleaseFeed)
            .AutomationName("Conversation")
            .AutomationId("ChatFeedItems");
        var rail = AnnotatedScrollBar()
            .ScrollRange(1)
            .Set(bar => OpenClawHistoryRail.Update(bar, historyEntries, itemsRef.Current, Navigate))
            .OnUnmount(OpenClawHistoryRail.Detach)
            .AutomationName("Conversation history")
            .HelpText("Up and Down navigate messages. Home and End go to the first and latest message.")
            .AutomationId("ChatHistoryRail")
            .IsTabStop(true).MinWidth(0).Width(OpenClawChatStyles.RailWidth(viewportWidth))
            .MaxHeight(Math.Max(1, messages.Length - 1) * 10 + 16)
            .HAlign(HorizontalAlignment.Right);

        var sendEnabled = streaming || !string.IsNullOrWhiteSpace(draft);
        var primary = Button(
                Border(TextBlock(streaming ? "\uE71A" : "\uE74A")
                    .FontFamily("Segoe Fluent Icons").FontSize(streaming ? 10 : 12)
                    .Foreground(sendEnabled ? Theme.Ref("TextOnAccentFillColorPrimaryBrush") : Theme.TertiaryText)
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                    .Set(text => text.IsTextScaleFactorEnabled = false))
                    .Size(24, 24).CornerRadius(12).Background(sendEnabled ? Theme.Accent : Theme.SubtleFill),
                () => { if (streaming) setStreaming(false); else Send(draft); })
            .Size(32, 32).MinWidth(32).MinHeight(32).Padding(0).BorderThickness(0).CornerRadius(16)
            .Resources(OpenClawChatStyles.ToolbarResources).IsEnabled(sendEnabled)
            .AutomationName(streaming ? "Stop" : "Send").ToolTip(streaming ? "Stop" : "Send")
            .AutomationId("ChatComposerPrimaryAction");
        var toolbar = Grid(columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto], rows: [GridSize.Auto], children:
        [
            OpenClawChatStyles.ToolbarButton("\uE710", "Add sample attachment", () => setAttachment(!attachment)).Grid(column: 0),
            Button(TextBlock(model).FontSize(12), () => setModel(model == "Default model" ? "Fast model" : "Default model"))
                .Resources(OpenClawChatStyles.ToolbarResources).BorderThickness(0).Padding(8, 4)
                .MinWidth(0).MinHeight(32).HAlign(HorizontalAlignment.Right)
                .AutomationName("Choose a sample model").Grid(column: 1),
            HStack(4,
                OpenClawChatStyles.ToolbarButton("\uE720", "Voice input is unavailable in this sample", () => { }).IsEnabled(false),
                primary).Grid(column: 2),
        ]).Margin(-8, 0);
        var composer = OpenClawChatStyles.Composer(
            TextBox(draft, setDraft, "Message Assistant")
                .AutomationName("Message").AutomationId("ChatComposerInput")
                .HelpText("Enter to send. Shift+Enter for a new line.").SubmitOnEnter(Send),
            toolbar, viewportWidth,
            attachment ? Button("notes.txt  \u00D7", () => setAttachment(false))
                .FontSize(12).Height(28).MinHeight(0).Padding(8, 4).CornerRadius(8)
                .AutomationName("Remove sample attachment").HAlign(HorizontalAlignment.Left) : null);

        var conversation = Grid(
            rows: [GridSize.Star(), GridSize.Auto],
            columns: [GridSize.Star()],
            children:
            [
                Grid(columns: [GridSize.Star()], rows: [GridSize.Star()], children:
                [
                    OpenClawTranscriptFade.Wrap(feed).Margin(0, 8, 0, 0),
                    rail,
                    Button(TextBlock("\uE74B").FontFamily("Segoe Fluent Icons").FontSize(16), JumpToLatest)
                        .Size(32, 32).MinWidth(32).MinHeight(32).Padding(0).CornerRadius(16)
                        .Foreground(Theme.SecondaryText).WithBorder(Theme.ControlStroke)
                        .Resources(r => r.Set("ButtonBackground", Theme.LayerFill))
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Bottom).Margin(0, 0, 0, 8)
                        .Opacity(following ? 0 : 1).IsEnabled(!following).IsTabStop(!following)
                        .IsHitTestVisible(!following).AutomationName("Jump to latest").ToolTip("Jump to latest"),
                ]).Grid(row: 0),
                composer.Grid(row: 1),
            ]).OnSizeChanged((_, args) => setViewportWidth(args.NewSize.Width));

        var details = TabView(Tab("Details", ScrollView(VStack(12,
            TextBlock($"{messages.Length} messages").TextWrapping(TextWrapping.Wrap),
            TextBlock("Resize this pane by dragging its inner edge. The handle is also keyboard accessible.")
                .TextWrapping(TextWrapping.Wrap),
            Button("Prepend history", () => updateMessages(current =>
                [new(Guid.NewGuid().ToString("N"), "You", "An earlier message loaded without replacing the existing row keys."), .. current])),
            TextBlock($"Pane width: {paneWidth:0}").Foreground(Theme.SecondaryText)
        ).Padding(12)))) with { OnTabCloseRequested = _ => setPaneOpen(false) };

        return VStack(8,
            HStack(8,
                Button("Append reply", Append),
                Button(streaming ? "Streaming..." : "Stream reply", StartStream).IsEnabled(!streaming),
                Button("Jump to latest", JumpToLatest),
                Button(paneOpen ? "Hide details" : "Show details", () => setPaneOpen(!paneOpen))),
            TextBlock(following ? "Following latest" : "Reading history").FontSize(12).Foreground(Theme.SecondaryText),
            (SplitView(pane: details, content: conversation) with
                {
                    DisplayMode = availableWidth < 652 ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline,
                    PanePlacement = SplitViewPanePlacement.Right, IsPaneOpen = paneOpen, OpenPaneLength = paneWidth,
                })
                .PaneOpenChanged(setPaneOpen)
                .ResizablePane(280, Math.Clamp(availableWidth - 372, 280, 800), setPaneWidth)
                .Set(OpenClawChatStyles.ResizePane).Background(Theme.LayerFill)
                .OnSizeChanged((_, args) => setAvailableWidth(args.NewSize.Width)).Height(640));
    }
}
