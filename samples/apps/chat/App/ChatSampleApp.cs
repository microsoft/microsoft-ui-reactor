using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.App;

/// <summary>
/// Composes the sample app and wires reusable chat UI callbacks to an interchangeable chat data provider.
/// </summary>
class ChatSampleApp : Component
{
    readonly IChatDataProvider _provider = new InMemoryChatDataProvider();

    string? _selectedId;
    Action<string?> _setSelectedId = null!;
    ChatThread[] _threads = [];
    Action<ChatThread[]> _setThreads = null!;
    IReadOnlyDictionary<string, ChatTimelineState> _timelines = new Dictionary<string, ChatTimelineState>();
    Action<IReadOnlyDictionary<string, ChatTimelineState>> _setTimelines = null!;
    string? _connectionStatus;
    Action<string?> _setConnectionStatus = null!;
    string[] _availableModels = [];
    Action<string[]> _setAvailableModels = null!;
    (string Message, InfoBarSeverity Severity)? _notification;
    Action<(string Message, InfoBarSeverity Severity)?> _setNotification = null!;

    string? SelectedId { get => _selectedId; set => _setSelectedId(value); }
    ChatThread[] Threads { get => _threads; set => _setThreads(value); }
    IReadOnlyDictionary<string, ChatTimelineState> Timelines { get => _timelines; set => _setTimelines(value); }
    string? ConnectionStatus { get => _connectionStatus; set => _setConnectionStatus(value); }
    string[] AvailableModels { get => _availableModels; set => _setAvailableModels(value); }

    void ShowNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        _setNotification((message, severity));
        _ = Task.Delay(3000).ContinueWith(_ => _setNotification(null));
    }

    async Task LoadProviderAsync()
    {
        try
        {
            ApplySnapshot(await _provider.LoadAsync(), selectDefaultWhenEmpty: true);
        }
        catch (Exception ex)
        {
            ShowProviderError("Unable to load chat data.", ex);
        }
    }

    void RunProviderOperation(Func<CancellationToken, Task> operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await operation(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ShowProviderError("Chat provider operation failed.", ex);
            }
        });
    }

    void ShowProviderError(string message, Exception ex)
    {
        System.Diagnostics.Trace.WriteLine($"[provider] {message} {ex}");
        ShowNotification($"{message} {ex.Message}", InfoBarSeverity.Error);
    }

    void OnProviderChanged(object? sender, ChatDataChangedEventArgs e) =>
        ApplySnapshot(e.Snapshot, selectDefaultWhenEmpty: false);

    void OnProviderNotification(object? sender, ChatProviderNotificationEventArgs e)
    {
        var notification = e.Notification;
        if (notification.ThreadId != SelectedId)
            return;

        switch (notification.Kind)
        {
            case ChatProviderNotificationKind.TurnComplete:
                Notifications.ShowTurnComplete(notification.ThreadId, notification.Title);
                break;
            case ChatProviderNotificationKind.PermissionRequested:
                if (notification.ToolName is { } toolName && notification.Message is { } detail)
                    Notifications.ShowPermissionRequest(notification.ThreadId, toolName, detail);
                break;
            case ChatProviderNotificationKind.Error:
                if (notification.Message is { } message)
                    Notifications.ShowError(notification.ThreadId, message);
                break;
        }
    }

    void ApplySnapshot(ChatDataSnapshot snapshot, bool selectDefaultWhenEmpty)
    {
        Threads = snapshot.Threads;
        Timelines = snapshot.Timelines;
        ConnectionStatus = snapshot.ConnectionStatus;
        AvailableModels = snapshot.AvailableModels;

        var selected = SelectedId;
        if (selected is not null && snapshot.Threads.All(t => t.Id != selected))
            SelectedId = snapshot.DefaultThreadId;
        else if (selectDefaultWhenEmpty && selected is null)
            SelectedId = snapshot.DefaultThreadId;
    }

    void OnSelectThread(string id)
    {
        if (id == SelectedId) return;
        SelectedId = id;
    }

    void OnNewThread() => SelectedId = null;

    void OnCreateWithMessage(string message, string? _) =>
        RunProviderOperation(async ct =>
        {
            var thread = await _provider.CreateThreadAsync(message, ct);
            SelectedId = thread.Id;
        });

    void OnSuspendThread(string id)
    {
        var thread = Threads.FirstOrDefault(t => t.Id == id);
        if (thread is null)
            return;

        var suspended = thread.Status != ChatThreadStatus.Suspended;
        RunProviderOperation(ct => _provider.SetThreadSuspendedAsync(id, suspended, ct));
    }

    void OnDeleteThread(string id)
    {
        if (SelectedId == id)
        {
            SelectedId = Threads
                .Where(t => t.Id != id)
                .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault()?.Id;
        }

        RunProviderOperation(async ct =>
        {
            await _provider.DeleteThreadAsync(id, ct);
            ShowNotification("Chat deleted");
        });
    }

    void OnPermissionResponse(string requestId, bool allow)
    {
        if (SelectedId is not { } id)
            return;

        RunProviderOperation(ct => _provider.RespondToPermissionAsync(id, requestId, allow, ct));
    }

    void OnSendMessage(string message)
    {
        if (SelectedId is not { } id)
            return;

        var thread = Threads.FirstOrDefault(t => t.Id == id);
        if (thread?.Status == ChatThreadStatus.Suspended)
        {
            ShowNotification("Resume the chat before sending a message.", InfoBarSeverity.Warning);
            return;
        }

        RunProviderOperation(ct => _provider.SendMessageAsync(id, message, ct));
    }

    void OnStop()
    {
        if (SelectedId is { } id)
            RunProviderOperation(ct => _provider.StopResponseAsync(id, ct));
    }

    public override Element Render()
    {
        (_selectedId, _setSelectedId) = UseState<string?>(null, threadSafe: true);
        (_threads, _setThreads) = UseState<ChatThread[]>([], threadSafe: true);
        (_timelines, _setTimelines) = UseState<IReadOnlyDictionary<string, ChatTimelineState>>(new Dictionary<string, ChatTimelineState>(), threadSafe: true);
        (_connectionStatus, _setConnectionStatus) = UseState<string?>(null, threadSafe: true);
        (_availableModels, _setAvailableModels) = UseState<string[]>([], threadSafe: true);
        (_notification, _setNotification) = UseState<(string Message, InfoBarSeverity Severity)?>(null, threadSafe: true);

        UseEffect((Func<Action>)(() =>
        {
            _provider.Changed += OnProviderChanged;
            _provider.NotificationRequested += OnProviderNotification;
            _ = LoadProviderAsync();

            return () =>
            {
                _provider.Changed -= OnProviderChanged;
                _provider.NotificationRequested -= OnProviderNotification;
                _ = Task.Run(async () => await _provider.DisposeAsync());
            };
        }));

        var selectedThread = SelectedId is { } selectedId ? Threads.FirstOrDefault(s => s.Id == selectedId) : null;
        var hostConnected = ConnectionStatus?.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) == true;
        var isConnected = hostConnected && selectedThread?.Status == ChatThreadStatus.Running;
        var tl = selectedThread is not null && Timelines.TryGetValue(selectedThread.Id, out var selectedTimeline)
            ? selectedTimeline
            : ChatTimelineState.Initial();
        var entries = selectedThread is not null ? tl.Entries.AsReadOnly() : (IReadOnlyList<ChatTimelineItem>)[];

        Element sidebar = Component<Sidebar, SidebarProps>(
            new(Threads, SelectedId, ConnectionStatus, OnSelectThread, OnNewThread, OnSuspendThread, OnDeleteThread,
                [], 0, _ => { }));

        var header = Component<SessionHeader, SessionHeaderProps>(new(selectedThread, tl));

        Element timelineOrLanding = SelectedId is null
            ? Component<LandingPage, LandingPageProps>(new(Threads, null, OnCreateWithMessage, OnSelectThread))
            : Component<ChatTimeline, ChatTimelineProps>(new(SelectedId, entries, false, null));

        Element notificationBar = _notification is { } n
            ? InfoBar(n.Message).Severity(n.Severity).Closable()
                .Set(ib => { ib.IsOpen = true; ib.Closed += (_, _) => _setNotification(null); })
            : Empty();

        Element statusBar = selectedThread is { } statusThread
            ? Component<StatusBar, StatusBarProps>(new(statusThread, AvailableModels,
                model => RunProviderOperation(ct => _provider.SetModelAsync(statusThread.Id, model, ct)),
                allowAll => RunProviderOperation(ct => _provider.SetPermissionModeAsync(statusThread.Id, allowAll, ct))))
            : Empty();

        var connState = isConnected ? "connected" : selectedThread is null ? "disconnected" : "connecting";
        var inputBar = selectedThread is not null
            ? Component<InputBar, InputBarProps>(new(connState, tl.TurnActive, tl.PendingPermission,
                OnSendMessage, OnStop, OnPermissionResponse))
            : Empty();

        var divider = Border(Empty()).Height(1).Background(DividerStroke);
        var subtitle = selectedThread?.DisplayTitle ?? "";
        var titleBar = TitleBar("Chat Sample").Subtitle(subtitle);

        var newThreadCmd = new Command
        {
            Label = "New chat",
            Execute = OnNewThread,
            Icon = SymbolIcon("Add"),
            Accelerator = Accelerator(global::Windows.System.VirtualKey.N, global::Windows.System.VirtualKeyModifiers.Control),
        };
        var closeThreadCmd = new Command
        {
            Label = "Close chat",
            Execute = () => { if (SelectedId is { } id) OnSuspendThread(id); },
            CanExecute = SelectedId is not null,
            Accelerator = Accelerator(global::Windows.System.VirtualKey.W, global::Windows.System.VirtualKeyModifiers.Control),
        };
        var escapeCmd = new Command
        {
            Label = "Back",
            Execute = OnNewThread,
            Accelerator = Accelerator(global::Windows.System.VirtualKey.Escape),
        };

        var switchCmds = new List<Command>();
        for (int i = 0; i < Math.Min(Threads.Length, 3); i++)
        {
            var idx = i;
            var thread = Threads[idx];
            var key = idx switch
            {
                0 => global::Windows.System.VirtualKey.Number1,
                1 => global::Windows.System.VirtualKey.Number2,
                _ => global::Windows.System.VirtualKey.Number3
            };
            switchCmds.Add(new Command
            {
                Label = $"Chat {idx + 1}",
                Execute = () => OnSelectThread(thread.Id),
                Accelerator = Accelerator(key, global::Windows.System.VirtualKeyModifiers.Control),
            });
        }

        var allCommands = new List<Command> { newThreadCmd, closeThreadCmd, escapeCmd };
        allCommands.AddRange(switchCmds);

        Element chatArea = Grid([GridSize.Star()], [GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto],
            notificationBar.Grid(row: 0, column: 0),
            header.Grid(row: 1, column: 0),
            divider.Grid(row: 2, column: 0),
            timelineOrLanding.Grid(row: 3, column: 0),
            inputBar.Grid(row: 4, column: 0),
            statusBar.Grid(row: 5, column: 0)
        );

        return Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star()],
            titleBar.Grid(row: 0, column: 0),
            Component<SplitPanel, SplitPanelProps>(new(
                Left: sidebar, Right: chatArea, InitialWidth: 280, MinWidth: 200))
                .Grid(row: 1, column: 0)
        ).OnMount(el =>
        {
            var grid = (Microsoft.UI.Xaml.Controls.Grid)el;
            grid.KeyboardAccelerators.Clear();
            foreach (var cmd in allCommands)
            {
                if (cmd.Accelerator is null) continue;
                var command = cmd;
                var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
                {
                    Key = command.Accelerator.Key,
                    Modifiers = command.Accelerator.Modifiers,
                };
                accelerator.Invoked += (_, e) =>
                {
                    e.Handled = true;
                    if (command.IsEnabled)
                        command.Execute?.Invoke();
                };
                grid.KeyboardAccelerators.Add(accelerator);
            }
        });
    }
}
