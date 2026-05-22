# Chat Sample App

This project is a native Windows chat sample built with Microsoft.UI.Reactor (Reactor).

## Responsibilities

- Starts the Reactor/WinUI application.
- Owns app-level composition, layout, notifications, logging, and window setup.
- Wires the chat UI to a local in-memory implementation of the shared data-provider contract.
- Hosts app-specific controls such as notifications, logging, and split-pane layout.

## Dependencies

- `ChatSample.Chat.Model` for provider-neutral chat state and events.
- `ChatSample.Chat.UI` for reusable chat UI components.
- `Microsoft.UI.Reactor` for the native UI framework.

The app shell should stay thin: reusable chat concepts belong in `Chat.Model`, reusable chat visuals belong in `Chat.UI`, and real providers can replace the local in-memory data provider.
