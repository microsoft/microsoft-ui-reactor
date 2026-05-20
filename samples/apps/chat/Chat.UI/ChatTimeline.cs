using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record ChatTimelineProps(
    string? SessionId,
    IReadOnlyList<ChatTimelineItem> Entries,
    bool HasMoreHistory,
    Action? OnLoadMoreHistory);

/// <summary>
/// Renders the scrollable chat transcript, including user messages, assistant Markdown, tool/status rows, and history loading.
/// </summary>
public class ChatTimeline : Component<ChatTimelineProps>
{
    const double FollowThreshold = 60;

    static readonly Microsoft.UI.Reactor.Markdown.MarkdownOptions _markdownOptions = new()
    {
        CodeFontFamily = "Cascadia Code, Cascadia Mono, Consolas",
        CodeBlock = (code, lang) =>
        {
            var header = lang is { Length: > 0 }
                ? (Element)Caption(lang).Foreground(Theme.TertiaryText).Padding(12, 6, 12, 0)
                : Empty();
            return Border(
                VStack(0,
                    header,
                    TextBlock(code)
                        .Set(t =>
                        {
                            t.FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas");
                            t.FontSize = 13;
                            t.TextWrapping = TextWrapping.Wrap;
                            t.IsTextSelectionEnabled = true;
                        })
                        .Foreground(Theme.PrimaryText)
                        .Padding(12, 8, 12, 12)
                )
            ).Background(Theme.Ref("CardBackgroundFillColorDefaultBrush"))
             .WithBorder(Theme.DividerStroke, 1)
             .CornerRadius(8).Margin(0, 4, 0, 4);
        },
        Table = (rows, aligns) =>
        {
            // Simple bordered table
            return Border(
                VStack(0, rows)
            ).WithBorder(Theme.DividerStroke, 1)
             .CornerRadius(4).Margin(0, 4, 0, 4);
        },
    };

    static string FormatToolLabel(ChatTimelineItem e)
    {
        var text = e.Text ?? "";
        return e.ToolName switch
        {
            "bash" or "powershell" => $"$ {text}",
            "read" or "view" => text,
            "edit" or "create" => text,
            "grep" => $"🔍 {text}",
            "glob" => $"📂 {text}",
            "web_fetch" => $"🌐 {text}",
            "web_search" => $"🔎 {text}",
            "task" => text,
            "report_intent" => text,
            _ => text == e.ToolName || string.IsNullOrEmpty(text) ? e.ToolName ?? "tool" : $"{e.ToolName}: {text}"
        };
    }

    static bool ContainsEntryId(IReadOnlyList<ChatTimelineItem> entries, string id)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Id == id)
                return true;
        }

        return false;
    }

    static double ClampOffset(double offset, double max) =>
        Math.Max(0, Math.Min(offset, max));

    public override Element Render()
    {
        var scrollViewRef = UseRef<Microsoft.UI.Xaml.Controls.ScrollViewer?>(null);
        var isFollowingRef = UseRef(true);
        var contentRef = UseRef<Microsoft.UI.Xaml.Controls.StackPanel?>(null);
        var prevEntryCountRef = UseRef(0);
        var prevSessionIdRef = UseRef<string?>(null);
        var prevFirstEntryIdRef = UseRef<string?>(null);
        var prevLastEntryIdRef = UseRef<string?>(null);
        var lastVerticalOffsetRef = UseRef(0.0);
        var lastScrollableHeightRef = UseRef(0.0);
        var suppressAutoFollowRef = UseRef(false);
        var sessionOffsetsRef = UseRef<Dictionary<string, double>>(new());
        var hasMoreHistoryRef = UseRef(Props.HasMoreHistory);
        var loadMoreHistoryRef = UseRef<Action?>(Props.OnLoadMoreHistory);
        var loadMoreRequestedForCountRef = UseRef(-1);

        hasMoreHistoryRef.Current = Props.HasMoreHistory;
        loadMoreHistoryRef.Current = Props.OnLoadMoreHistory;

        var entryCount = Props.Entries.Count;
        var firstEntryId = entryCount > 0 ? Props.Entries[0].Id : null;
        var lastEntryId = entryCount > 0 ? Props.Entries[entryCount - 1].Id : null;
        var previousSessionId = prevSessionIdRef.Current;
        var previousEntryCount = prevEntryCountRef.Current;
        var previousFirstEntryId = prevFirstEntryIdRef.Current;
        var previousLastEntryId = prevLastEntryIdRef.Current;
        var sessionChanged = Props.SessionId != previousSessionId;
        var initialLoad = !sessionChanged && previousEntryCount == 0 && entryCount > 0;
        var prependedHistory = !sessionChanged
            && previousEntryCount > 0
            && entryCount > previousEntryCount
            && previousFirstEntryId is not null
            && firstEntryId != previousFirstEntryId
            && lastEntryId == previousLastEntryId
            && ContainsEntryId(Props.Entries, previousFirstEntryId);
        var appendedEntries = !sessionChanged
            && entryCount > previousEntryCount
            && !prependedHistory;

        void StoreSessionOffset(string? sessionId, double offset)
        {
            if (sessionId is { Length: > 0 })
                sessionOffsetsRef.Current[sessionId] = offset;
        }

        void UpdateScrollMetrics(Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            lastVerticalOffsetRef.Current = sv.VerticalOffset;
            lastScrollableHeightRef.Current = sv.ScrollableHeight;
            isFollowingRef.Current = sv.ScrollableHeight - sv.VerticalOffset <= FollowThreshold;
            StoreSessionOffset(prevSessionIdRef.Current, sv.VerticalOffset);
        }

        void QueueScrollToOffset(Microsoft.UI.Xaml.Controls.ScrollViewer sv, string? sessionId, double targetOffset, bool disableAnimation, bool suppressAutoFollow)
        {
            suppressAutoFollowRef.Current = suppressAutoFollow;
            sv.DispatcherQueue.TryEnqueue(() =>
            {
                var target = ClampOffset(targetOffset, sv.ScrollableHeight);
                sv.ChangeView(null, target, null, disableAnimation);
                lastVerticalOffsetRef.Current = target;
                lastScrollableHeightRef.Current = sv.ScrollableHeight;
                isFollowingRef.Current = sv.ScrollableHeight - target <= FollowThreshold;
                StoreSessionOffset(sessionId, target);

                if (suppressAutoFollow)
                    sv.DispatcherQueue.TryEnqueue(() => suppressAutoFollowRef.Current = false);
            });
        }

        void QueueScrollToBottom(Microsoft.UI.Xaml.Controls.ScrollViewer sv, string? sessionId, bool disableAnimation)
        {
            isFollowingRef.Current = true;
            sv.DispatcherQueue.TryEnqueue(() =>
            {
                var bottom = sv.ScrollableHeight;
                sv.ChangeView(null, bottom, null, disableAnimation);
                lastVerticalOffsetRef.Current = bottom;
                lastScrollableHeightRef.Current = sv.ScrollableHeight;
                isFollowingRef.Current = true;
                StoreSessionOffset(sessionId, bottom);
            });
        }

        void QueuePreservePrependOffset(Microsoft.UI.Xaml.Controls.ScrollViewer sv, string? sessionId, double oldOffset, double oldScrollableHeight)
        {
            suppressAutoFollowRef.Current = true;
            sv.DispatcherQueue.TryEnqueue(() =>
            {
                var delta = sv.ScrollableHeight - oldScrollableHeight;
                var target = ClampOffset(oldOffset + delta, sv.ScrollableHeight);
                sv.ChangeView(null, target, null, disableAnimation: true);
                lastVerticalOffsetRef.Current = target;
                lastScrollableHeightRef.Current = sv.ScrollableHeight;
                isFollowingRef.Current = sv.ScrollableHeight - target <= FollowThreshold;
                StoreSessionOffset(sessionId, target);
                sv.DispatcherQueue.TryEnqueue(() => suppressAutoFollowRef.Current = false);
            });
        }

        // Load more button — outside the repeated items
        var loadMoreButton = Props.HasMoreHistory
            ? Button("Load earlier messages", () => Props.OnLoadMoreHistory?.Invoke())
                .HAlign(HorizontalAlignment.Center)
                .Set(b => { b.Padding = new Thickness(16, 8, 16, 8); b.CornerRadius = new CornerRadius(4); })
                .Resources(r => r
                    .Set("ButtonBackground", Ref("SubtleFillColorTransparentBrush"))
                    .Set("ButtonBackgroundPointerOver", Ref("SubtleFillColorSecondaryBrush"))
                    .Set("ButtonBackgroundPressed", Ref("SubtleFillColorTertiaryBrush"))
                    .Set("ButtonBorderBrush", Ref("SubtleFillColorTransparentBrush")))
                .Margin(0, 8, 0, 8)
            : (Element)Empty();

        static Element TimelineInset(Element child, double top = 2, double bottom = 2) =>
            Border(child).Padding(36, top, 24, bottom);

        Element RenderEntry(ChatTimelineItem entry) => entry.Kind switch
        {
            // User messages — muted background, full width, collapsible-style
            ChatTimelineItemKind.User => Border(
                TextBlock(entry.Text)
                    .Set(t => { t.TextWrapping = TextWrapping.Wrap; t.IsTextSelectionEnabled = true; t.FontSize = 14; })
                    .Padding(12, 8, 12, 8)
            ).Background(Ref("SubtleFillColorSecondaryBrush"))
             .CornerRadius(8).Margin(24, 8, 24, 4),

            // Assistant — markdown in a padded, width-constraining surface
            ChatTimelineItemKind.Assistant => Border(
                Grid([GridSize.Star()], [GridSize.Auto],
                    Markdown(entry.Text ?? "", _markdownOptions).Grid(row: 0, column: 0)
                )
            ).Padding(24, 4, 24, 4)
             .AutomationName(entry.Text ?? ""),

            // Tool calls — compact row, hover highlight
            ChatTimelineItemKind.ToolCall => TimelineInset(
                (FlexRow(
                    Caption(entry.ToolResult switch
                    {
                        ChatToolCallStatus.Success => "✓",
                        ChatToolCallStatus.Error => "✗",
                        _ => "⋯"
                    }).Foreground(entry.ToolResult switch
                    {
                        ChatToolCallStatus.Success => SecondaryText,
                        ChatToolCallStatus.Error => Ref("SystemFillColorCriticalBrush"),
                        _ => TertiaryText
                    }).VAlign(VerticalAlignment.Center).Set(t => t.FontSize = 12),
                    Caption(entry.ToolName ?? "tool").Foreground(SecondaryText)
                        .Set(t => { t.FontFamily = new FontFamily("Cascadia Code, Consolas"); t.FontSize = 12; })
                        .VAlign(VerticalAlignment.Center),
                    When(entry.Text is { Length: > 0 } && entry.Text != entry.ToolName,
                        () => Caption(FormatToolLabel(entry)).Foreground(TertiaryText)
                            .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; t.IsTextSelectionEnabled = true; t.FontSize = 12; })
                            .VAlign(VerticalAlignment.Center).Flex(grow: 1))
                ) with { ColumnGap = 6 })),

            // Reasoning
            ChatTimelineItemKind.Reasoning => TimelineInset(
                Caption("thinking…").Foreground(TertiaryText)
                    .Set(t => { t.FontStyle = global::Windows.UI.Text.FontStyle.Italic; t.FontSize = 12; })),

            // Filtered status
            ChatTimelineItemKind.Status when entry.Text.Contains("Restored") || entry.Text.Contains("Connecting to") || entry.Text.Contains("Connected") || entry.Text.Contains("Resuming") => Empty(),

            ChatTimelineItemKind.Status when entry.Tone == ChatTone.Error =>
                TimelineInset(
                    Caption(entry.Text).Foreground(Ref("SystemFillColorCriticalBrush"))
                        .Set(t => { t.TextWrapping = TextWrapping.Wrap; t.FontSize = 12; }),
                    top: 4,
                    bottom: 4),

            ChatTimelineItemKind.Status => TimelineInset(
                Caption(entry.Text).Foreground(TertiaryText).Set(t => t.FontSize = 12)),

             _ => Empty()
        };

        // Render entries — matches web client patterns
        var renderedEntries = Props.Entries
            .Select(entry => RenderEntry(entry).WithKey(entry.Id))
            .ToArray();

        return Grid([GridSize.Star()], [GridSize.Star()],
            ScrollViewer(
                Grid([GridSize.Star()], [GridSize.Auto, GridSize.Auto, GridSize.Auto],
                    loadMoreButton.Grid(row: 0, column: 0),
                    VStack(4, renderedEntries).Set(sp =>
                    {
                        if (contentRef.Current != sp)
                        {
                            contentRef.Current = (Microsoft.UI.Xaml.Controls.StackPanel)sp;
                            sp.SizeChanged += (_, _) =>
                            {
                                if (!suppressAutoFollowRef.Current && isFollowingRef.Current && scrollViewRef.Current is { } sv)
                                    QueueScrollToBottom(sv, prevSessionIdRef.Current, disableAnimation: true);
                            };
                        }
                    }).Grid(row: 1, column: 0),
                    Border(Empty()).Height(24).Grid(row: 2, column: 0)
                )
            ).Set(sv =>
            {
                sv.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
                sv.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
                sv.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                if (scrollViewRef.Current != sv)
                {
                    scrollViewRef.Current = sv;
                    sv.ViewChanged += (_, _) =>
                    {
                        UpdateScrollMetrics(sv);

                        if (sv.ScrollableHeight > 0
                            && sv.VerticalOffset <= FollowThreshold
                            && hasMoreHistoryRef.Current
                            && loadMoreRequestedForCountRef.Current != prevEntryCountRef.Current)
                        {
                            loadMoreRequestedForCountRef.Current = prevEntryCountRef.Current;
                            loadMoreHistoryRef.Current?.Invoke();
                        }
                    };
                }

                if (entryCount != previousEntryCount)
                    loadMoreRequestedForCountRef.Current = -1;

                if (sessionChanged)
                {
                    StoreSessionOffset(previousSessionId, lastVerticalOffsetRef.Current);

                    if (entryCount > 0)
                    {
                        if (Props.SessionId is not null && sessionOffsetsRef.Current.TryGetValue(Props.SessionId, out var savedOffset))
                            QueueScrollToOffset(sv, Props.SessionId, savedOffset, disableAnimation: true, suppressAutoFollow: true);
                        else
                            QueueScrollToBottom(sv, Props.SessionId, disableAnimation: true);
                    }
                }
                else if (prependedHistory)
                {
                    QueuePreservePrependOffset(sv, Props.SessionId, lastVerticalOffsetRef.Current, lastScrollableHeightRef.Current);
                }
                else if (initialLoad)
                {
                    if (Props.SessionId is not null && sessionOffsetsRef.Current.TryGetValue(Props.SessionId, out var savedOffset))
                        QueueScrollToOffset(sv, Props.SessionId, savedOffset, disableAnimation: true, suppressAutoFollow: true);
                    else
                        QueueScrollToBottom(sv, Props.SessionId, disableAnimation: true);
                }
                else if (appendedEntries && isFollowingRef.Current)
                {
                    QueueScrollToBottom(sv, Props.SessionId, disableAnimation: false);
                }

                prevSessionIdRef.Current = Props.SessionId;
                prevFirstEntryIdRef.Current = firstEntryId;
                prevLastEntryIdRef.Current = lastEntryId;
                prevEntryCountRef.Current = entryCount;
            }).Grid(row: 0, column: 0)
        );
    }
}
