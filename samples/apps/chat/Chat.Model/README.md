# Chat Sample Model

This project contains provider-neutral chat contracts and state.

## Responsibilities

- Defines chat thread, timeline, permission, and event models.
- Defines the data-provider contract that supplies threads, timelines, mutations, and provider notifications.
- Reduces chat events into `ChatTimelineState`.
- Avoids any dependency on provider-specific transports, WinUI, or app-specific services.

## Dependency rules

- May depend on base .NET libraries only.
- Must not reference `ChatSample.Chat.UI` or `ChatSample.App`.
- Should represent generic chat behavior that can be reused by another provider.
