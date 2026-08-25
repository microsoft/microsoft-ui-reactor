# Announce

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.AnnounceHandle.Announce(System.String)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`Announce(string)`](#announcestring)
- [`Announce(string, bool)`](#announcestring-bool)

## `Announce(string)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.AnnounceHandle.Announce(System.String)`

### Summary

Announces a message to screen readers (polite — queued after current speech).

## `Announce(string, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.AnnounceHandle.Announce(System.String,System.Boolean)`

### Summary

Announces a message to screen readers.

### Parameters

- **message** — The text to announce.
- **assertive** — If true, interrupts current speech immediately.
If false (default), queued after current speech finishes.

### Discussion

Thread-safe. When called on the UI thread the announcement is raised
synchronously. When called from any other thread it is marshalled to the
captured UI `DispatcherQueue` and runs asynchronously
(fire-and-forget) — it may complete after this method returns. Calls made
before the live-region [Region](Region.md) ([guide](../../hooks.md)) has mounted are ignored.


