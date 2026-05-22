# Chat Sample

This sample demonstrates a native Windows chat experience built with Microsoft.UI.Reactor (Reactor) and WinUI.

The app uses an in-memory data provider so the sample can run on its own. It does not require a server, API key, or external chat service. The reusable model and UI projects are intentionally separated so another app can replace the local provider with a service-backed implementation.

## Project layout

| Project | Purpose |
|---|---|
| `App\ChatSample.csproj` | Native app shell, window setup, in-memory data provider, notifications, and app-specific layout. |
| `Chat.Model\Chat.Model.csproj` | Provider-neutral chat state, timeline events, reducer, data snapshots, and provider contracts. |
| `Chat.UI\Chat.UI.csproj` | Reusable Reactor/WinUI chat components such as the sidebar, timeline, landing page, input bar, header, and status bar. |

## What it demonstrates

- A layered chat architecture with provider-neutral state and UI.
- A separate reusable chat UI project, not source-linked into the app.
- Seeded conversations rendered from `ChatTimelineState`.
- A local provider send-message flow that appends the user message, shows assistant activity, and returns a generated response.
- Status, model, timeline, recent chat, suspend/resume, and delete UI affordances.

## Build and run

From the repository root:

```powershell
dotnet build samples\apps\chat\App\ChatSample.csproj
```

Run the app:

```powershell
dotnet run --project samples\apps\chat\App\ChatSample.csproj
```

Run with Reactor devtools enabled:

```powershell
dotnet run --project samples\apps\chat\App\ChatSample.csproj -- --devtools run
```

## Reusing the UI

`Chat.UI` only depends on `Chat.Model` plus Reactor/WinUI UI APIs. It should stay free of app-specific services and provider-specific transports. To connect this UI to a real backend, keep the app shell thin and implement `IChatDataProvider` by adapting that backend into `ChatThread`, `ChatEvent`, and `ChatTimelineState`.
