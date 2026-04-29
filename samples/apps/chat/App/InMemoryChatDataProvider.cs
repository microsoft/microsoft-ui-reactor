namespace ChatSample.App;

/// <summary>
/// Provides the sample app with local seeded conversations and generated responses without any external service.
/// </summary>
sealed class InMemoryChatDataProvider : IChatDataProvider
{
    static readonly string[] s_availableModels = ["Sample assistant"];

    readonly object _gate = new();
    readonly Dictionary<string, ChatThread> _threadMap = new();
    readonly Dictionary<string, ChatTimelineState> _timelines = new();
    readonly Dictionary<string, CancellationTokenSource> _responses = new();
    int _nextThreadId = 3;
    bool _initialized;
    bool _disposed;

    public string DisplayName => "Local sample";

    public event EventHandler<ChatDataChangedEventArgs>? Changed;
    public event EventHandler<ChatProviderNotificationEventArgs>? NotificationRequested;

    public Task<ChatDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        return Task.FromResult(CreateSnapshot());
    }

    public async Task<ChatThread> CreateThreadAsync(string? initialMessage = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var title = string.IsNullOrWhiteSpace(initialMessage) ? "New chat" : CreateTitle(initialMessage);
        var id = $"sample-{Interlocked.Increment(ref _nextThreadId)}";
        var now = DateTimeOffset.Now;
        var thread = new ChatThread
        {
            Id = id,
            Title = title,
            Status = ChatThreadStatus.Running,
            Activity = string.IsNullOrWhiteSpace(initialMessage) ? ChatActivity.Idle : ChatActivity.Working,
            Workspace = "Sample workspace",
            HostName = DisplayName,
            ProfileName = "Demo",
            Model = s_availableModels[0],
            CreatedAt = now,
            UpdatedAt = now,
        };

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            _threadMap[id] = thread;
            _timelines[id] = ChatTimelineState.Initial() with { HistoryLoaded = true };
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);

        if (!string.IsNullOrWhiteSpace(initialMessage))
        {
            await SendMessageAsync(id, initialMessage, cancellationToken);
            lock (_gate)
                thread = _threadMap[id];
        }

        return thread;
    }

    public Task SendMessageAsync(string threadId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var trimmed = message.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Message cannot be empty.", nameof(message));

        CancelResponse(threadId);

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.TryGetValue(threadId, out var thread))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");
            if (thread.Status == ChatThreadStatus.Suspended)
                throw new InvalidOperationException("Resume the chat before sending a message.");

            var nonce = Guid.NewGuid().ToString("N");
            var timeline = _timelines.TryGetValue(threadId, out var current)
                ? current
                : ChatTimelineState.Initial() with { HistoryLoaded = true };
            _timelines[threadId] = ChatTimelineReducer.AddLocalUser(timeline, trimmed, nonce) with { HistoryLoaded = true };

            _threadMap[threadId] = thread with
            {
                Activity = ChatActivity.Working,
                UpdatedAt = DateTimeOffset.Now,
                Title = thread.Title.StartsWith("New chat", StringComparison.OrdinalIgnoreCase) ? CreateTitle(trimmed) : thread.Title,
            };

            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        StartSampleResponse(threadId, trimmed);
        return Task.CompletedTask;
    }

    public Task StopResponseAsync(string threadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        CancelResponse(threadId);
        return Task.CompletedTask;
    }

    public Task SetThreadSuspendedAsync(string threadId, bool suspended, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (suspended)
            CancelResponse(threadId);

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.TryGetValue(threadId, out var thread))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");

            _threadMap[threadId] = thread with
            {
                Status = suspended ? ChatThreadStatus.Suspended : ChatThreadStatus.Running,
                Activity = ChatActivity.Idle,
                UpdatedAt = DateTimeOffset.Now,
            };
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        CancelResponse(threadId);

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.Remove(threadId))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");

            _timelines.Remove(threadId);
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public Task SetModelAsync(string threadId, string model, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        if (!s_availableModels.Contains(model))
            throw new ArgumentException($"Unknown model '{model}'.", nameof(model));

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.TryGetValue(threadId, out var thread))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");

            _threadMap[threadId] = thread with { Model = model, UpdatedAt = DateTimeOffset.Now };
            ApplyEventCore(threadId, new ChatModelChangedEvent(model));
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public Task SetPermissionModeAsync(string threadId, bool allowAll, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.ContainsKey(threadId))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");

            ApplyEventCore(threadId, new ChatStatusEvent(
                allowAll ? "Sample permissions set to auto-approve." : "Sample permissions set to prompt.",
                ChatTone.Info));
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public Task RespondToPermissionAsync(string threadId, string requestId, bool allow, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_threadMap.ContainsKey(threadId))
                throw new KeyNotFoundException($"Chat thread '{threadId}' was not found.");

            ApplyEventCore(threadId, new ChatStatusEvent(
                $"Permission {requestId} {(allow ? "allowed" : "denied")}.",
                allow ? ChatTone.Success : ChatTone.Warning));
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        CancellationTokenSource[] responses;
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            responses = _responses.Values.ToArray();
            _responses.Clear();
        }

        foreach (var cts in responses)
        {
            cts.Cancel();
            cts.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    void EnsureInitialized()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_initialized)
                return;

            var now = DateTimeOffset.Now;

            var first = new ChatThread
            {
                Id = "sample-1",
                Title = "Explore Reactor chat UI",
                Status = ChatThreadStatus.Running,
                Activity = ChatActivity.Idle,
                Workspace = "Sample workspace",
                HostName = DisplayName,
                ProfileName = "Demo",
                Model = s_availableModels[0],
                CreatedAt = now.AddMinutes(-45),
                UpdatedAt = now.AddMinutes(-3),
            };

            var second = new ChatThread
            {
                Id = "sample-2",
                Title = "Review component boundaries",
                Status = ChatThreadStatus.Running,
                Activity = ChatActivity.Idle,
                Workspace = "Chat UI",
                HostName = DisplayName,
                ProfileName = "Demo",
                Model = s_availableModels[0],
                CreatedAt = now.AddMinutes(-25),
                UpdatedAt = now.AddMinutes(-12),
            };

            _threadMap[first.Id] = first;
            _threadMap[second.Id] = second;

            _timelines[first.Id] = BuildTimeline(
                new ChatUserMessageEvent("How is this sample structured?"),
                new ChatMessageEvent("The sample is split into a thin app shell, provider-neutral chat model, and reusable chat UI project."),
                new ChatTurnEndEvent(),
                new ChatStatusEvent("Tip: try sending a message to see local streaming state.", ChatTone.Info));

            _timelines[second.Id] = BuildTimeline(
                new ChatUserMessageEvent("Can another provider reuse this UI?"),
                new ChatMessageEvent("Yes. The UI consumes ChatThread and ChatTimelineState, then raises callbacks for provider-specific actions."),
                new ChatTurnEndEvent());

            _initialized = true;
        }
    }

    static ChatTimelineState BuildTimeline(params ChatEvent[] events)
    {
        var state = ChatTimelineState.Initial() with { HistoryLoaded = true };
        foreach (var evt in events)
            state = ChatTimelineReducer.Apply(state, evt);

        return state with { HistoryLoaded = true };
    }

    ChatDataSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return CreateSnapshotCore();
        }
    }

    ChatDataSnapshot CreateSnapshotCore()
    {
        var threads = _threadMap.Values
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();

        return new(
            threads,
            _timelines.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            threads.FirstOrDefault()?.Id,
            $"Connected - {_threadMap.Count} chat(s)",
            [.. s_availableModels]);
    }

    void ApplyEvent(string threadId, ChatEvent evt)
    {
        ChatDataSnapshot snapshot;
        ChatProviderNotification? notification = null;

        lock (_gate)
        {
            if (_disposed)
                return;

            ApplyEventCore(threadId, evt);
            snapshot = CreateSnapshotCore();

            if (evt is ChatPermissionRequestEvent permission && _threadMap.TryGetValue(threadId, out var thread))
            {
                notification = new(
                    ChatProviderNotificationKind.PermissionRequested,
                    threadId,
                    thread.DisplayTitle,
                    permission.Detail,
                    permission.ToolName);
            }
        }

        Publish(snapshot);
        if (notification is not null)
            PublishNotification(notification);
    }

    void ApplyEventCore(string threadId, ChatEvent evt)
    {
        if (!_timelines.TryGetValue(threadId, out var current))
            current = ChatTimelineState.Initial() with { HistoryLoaded = true };

        _timelines[threadId] = ChatTimelineReducer.Apply(current, evt) with { HistoryLoaded = true };
    }

    void UpdateThread(string threadId, Func<ChatThread, ChatThread> update)
    {
        ChatDataSnapshot snapshot;
        lock (_gate)
        {
            if (_disposed || !_threadMap.TryGetValue(threadId, out var thread))
                return;

            _threadMap[threadId] = update(thread);
            snapshot = CreateSnapshotCore();
        }

        Publish(snapshot);
    }

    bool CancelResponse(string threadId)
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_responses.Remove(threadId, out var existing))
                cts = existing;
        }

        if (cts is null)
            return false;

        cts.Cancel();
        cts.Dispose();
        return true;
    }

    void StartSampleResponse(string threadId, string message)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed)
            {
                cts.Dispose();
                return;
            }

            _responses[threadId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token);
                ApplyEvent(threadId, new ChatThinkingEvent("Thinking"));

                await Task.Delay(450, cts.Token);
                ApplyEvent(threadId, new ChatReasoningEvent("This is a local demo response generated without any external service."));

                await Task.Delay(550, cts.Token);
                ApplyEvent(threadId, new ChatMessageEvent(CreateSampleResponse(message)));

                await Task.Delay(150, cts.Token);
                ApplyEvent(threadId, new ChatTurnEndEvent());
                UpdateThread(threadId, t => t with { Activity = ChatActivity.Idle, UpdatedAt = DateTimeOffset.Now });
                PublishTurnComplete(threadId);
            }
            catch (OperationCanceledException)
            {
                ApplyEvent(threadId, new ChatStatusEvent("Response stopped.", ChatTone.Warning));
                ApplyEvent(threadId, new ChatTurnEndEvent());
                UpdateThread(threadId, t => t with { Activity = ChatActivity.Idle, UpdatedAt = DateTimeOffset.Now });
            }
            finally
            {
                var dispose = false;
                lock (_gate)
                {
                    if (_responses.TryGetValue(threadId, out var currentCts) && ReferenceEquals(currentCts, cts))
                    {
                        _responses.Remove(threadId);
                        dispose = true;
                    }
                }

                if (dispose)
                    cts.Dispose();
            }
        });
    }

    void PublishTurnComplete(string threadId)
    {
        ChatProviderNotification? notification = null;
        lock (_gate)
        {
            if (!_disposed && _threadMap.TryGetValue(threadId, out var thread))
            {
                notification = new(
                    ChatProviderNotificationKind.TurnComplete,
                    threadId,
                    thread.DisplayTitle);
            }
        }

        if (notification is not null)
            PublishNotification(notification);
    }

    void Publish(ChatDataSnapshot snapshot) =>
        Changed?.Invoke(this, new ChatDataChangedEventArgs(snapshot));

    void PublishNotification(ChatProviderNotification notification) =>
        NotificationRequested?.Invoke(this, new ChatProviderNotificationEventArgs(notification));

    void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InMemoryChatDataProvider));
    }

    static string CreateTitle(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Length == 0)
            return "New chat";

        return trimmed.Length <= 48 ? trimmed : trimmed[..45] + "...";
    }

    static string CreateSampleResponse(string message) =>
        $"You said: \"{message}\".\n\nThis sample keeps chat data in an in-memory provider and uses the shared chat model plus reusable chat UI project. Swap the provider implementation to connect it to a real service.";
}
