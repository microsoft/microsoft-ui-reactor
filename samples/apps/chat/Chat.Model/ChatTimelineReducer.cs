namespace ChatSample.Chat.Model;

public static class ChatTimelineReducer
{
    public static ChatTimelineState Apply(ChatTimelineState state, ChatEvent evt)
    {
        return evt switch
        {
            ChatUserMessageEvent e => ApplyUserMessage(state, e),
            ChatThinkingEvent => state with { TurnActive = true },
            ChatReasoningEvent e => UpsertReasoning(BeginTurn(state), e.Text, replace: true),
            ChatReasoningDeltaEvent e => UpsertReasoning(BeginTurn(state), e.Text, replace: false),
            ChatMessageDeltaEvent e => UpsertAssistant(BeginTurn(state), e.Text, replace: false, streaming: true),
            ChatMessageEvent e => UpsertAssistant(BeginTurn(state), e.Text, replace: true, streaming: false),
            ChatTurnEndEvent => state with { TurnActive = false, ActiveAssistantId = null, ActiveReasoningId = null, PendingPermission = null },
            ChatIntentEvent e => state with { CurrentIntent = e.Intent },
            ChatToolStartEvent e => ApplyToolStart(state, e),
            ChatToolOutputEvent e => ApplyToolOutput(state, e),
            ChatToolErrorEvent e => ApplyToolError(state, e),
            ChatErrorEvent e => PushEntry(state, ChatTimelineItemKind.Status, e.Text, ChatTone.Error),
            ChatStatusEvent e => PushEntry(state, ChatTimelineItemKind.Status, e.Text, e.Tone),
            ChatRestoredEvent e => PushEntry(state, ChatTimelineItemKind.Status, e.Text, ChatTone.Info),
            ChatContextChangedEvent => state,
            ChatModelChangedEvent e => PushEntry(state, ChatTimelineItemKind.Status, $"Model -> {e.Model}", ChatTone.Success),
            ChatPermissionRequestEvent e => state with
            {
                PendingPermission = new ChatPermissionRequest(e.RequestId, e.PermissionKind, e.ToolName, e.Detail)
            },
            ChatRawEvent e => e.Text is { Length: > 0 } t ? PushEntry(state, ChatTimelineItemKind.Raw, t) : state,
            _ => state
        };
    }

    public static ChatTimelineState AddLocalUser(ChatTimelineState state, string text, string nonce)
    {
        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, ChatTimelineItemKind.User, text));
        state.LocalNonces.Add(nonce);
        return state with { NextId = state.NextId + 1, TurnActive = true };
    }

    public static ChatTimelineState AddSystem(ChatTimelineState state, string text, ChatTone tone = ChatTone.Info)
        => PushEntry(state, ChatTimelineItemKind.Status, text, tone);

    public static ChatTimelineState ClearPermission(ChatTimelineState state)
        => state with { PendingPermission = null };

    static ChatTimelineState ApplyUserMessage(ChatTimelineState state, ChatUserMessageEvent e)
    {
        if (e.Nonce is { } nonce && state.LocalNonces.Contains(nonce))
        {
            state.LocalNonces.Remove(nonce);
            return state;
        }

        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, ChatTimelineItemKind.User, e.Text));
        return state with { NextId = state.NextId + 1, TurnActive = true };
    }

    static ChatTimelineState ApplyToolStart(ChatTimelineState state, ChatToolStartEvent e)
    {
        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, ChatTimelineItemKind.ToolCall, e.Text,
            ToolName: e.ToolName, ToolResult: ChatToolCallStatus.InProgress,
            IntentSummary: e.Text, ToolArgs: e.ToolArgs));
        return state with { NextId = state.NextId + 1, ActiveToolCallId = id };
    }

    static ChatTimelineState ApplyToolOutput(ChatTimelineState state, ChatToolOutputEvent e)
    {
        if (state.ActiveToolCallId is { } tid)
        {
            var idx = state.Entries.FindIndex(en => en.Id == tid);
            if (idx >= 0)
                state.Entries[idx] = state.Entries[idx] with
                {
                    ToolResult = ChatToolCallStatus.Success,
                    ToolOutput = e.Text
                };
        }
        return state with { ActiveToolCallId = null, PendingPermission = null };
    }

    static ChatTimelineState ApplyToolError(ChatTimelineState state, ChatToolErrorEvent e)
    {
        if (state.ActiveToolCallId is { } tid)
        {
            var idx = state.Entries.FindIndex(en => en.Id == tid);
            if (idx >= 0)
                state.Entries[idx] = state.Entries[idx] with
                {
                    ToolResult = ChatToolCallStatus.Error,
                    ToolOutput = e.Text
                };
        }
        return state with { ActiveToolCallId = null, PendingPermission = null };
    }

    static ChatTimelineState UpsertAssistant(ChatTimelineState state, string text, bool replace, bool streaming)
    {
        if (state.ActiveAssistantId is { } aid)
        {
            var idx = state.Entries.FindIndex(e => e.Id == aid);
            if (idx >= 0)
            {
                var existing = state.Entries[idx];
                state.Entries[idx] = existing with
                {
                    Text = replace ? text : existing.Text + text,
                    IsStreaming = streaming
                };
                return state;
            }
        }

        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, ChatTimelineItemKind.Assistant, text, IsStreaming: streaming));
        return state with { NextId = state.NextId + 1, ActiveAssistantId = id };
    }

    static ChatTimelineState UpsertReasoning(ChatTimelineState state, string text, bool replace)
    {
        if (state.ActiveReasoningId is { } rid)
        {
            var idx = state.Entries.FindIndex(e => e.Id == rid);
            if (idx >= 0)
            {
                var existing = state.Entries[idx];
                state.Entries[idx] = existing with { Text = replace ? text : existing.Text + text };
                return state;
            }
        }

        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, ChatTimelineItemKind.Reasoning, text));
        return state with { NextId = state.NextId + 1, ActiveReasoningId = id };
    }

    static ChatTimelineState PushEntry(ChatTimelineState state, ChatTimelineItemKind kind, string text, ChatTone? tone = null)
    {
        var id = $"e{state.NextId}";
        state.Entries.Add(new(id, kind, text, Tone: tone));
        return state with { NextId = state.NextId + 1 };
    }

    static ChatTimelineState BeginTurn(ChatTimelineState state) =>
        state.TurnActive ? state : state with { TurnActive = true };
}
