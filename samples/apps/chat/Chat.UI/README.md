# Chat Sample UI

This project contains provider-neutral chat UI components.

`Chat.UI` builds as a separate project so chat UI can be reused without source-linking files into the app shell.

## Responsibilities

- Renders generic chat surfaces such as the sidebar, timeline, input bar, session header, landing page, and status bar.
- Consumes `ChatSample.Chat.Model` types.
- Raises callbacks for app or provider actions without calling provider APIs directly.

## Dependency rules

- May depend on `ChatSample.Chat.Model` and Reactor/WinUI UI APIs.
- Must not reference `ChatSample.App` or provider-specific projects.
- Should remain reusable for other chat providers.
