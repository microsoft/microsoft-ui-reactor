# UseAnnounce

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseAnnounceExtensions.UseAnnounce(Microsoft.UI.Reactor.Core.Component)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseAnnounce(Component)`](#useannouncecomponent)
- [`UseAnnounce(RenderContext)`](#useannouncerendercontext)

## `UseAnnounce(Component)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseAnnounceExtensions.UseAnnounce(Microsoft.UI.Reactor.Core.Component)`

### Summary

Creates an [AnnounceHandle](AnnounceHandle.md) ([guide](../../hooks.md)) for making screen reader announcements.

## `UseAnnounce(RenderContext)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseAnnounceExtensions.UseAnnounce(Microsoft.UI.Reactor.Core.RenderContext)`

### Summary

Creates an [AnnounceHandle](AnnounceHandle.md) ([guide](../../hooks.md)) for making screen reader announcements.
The handle persists across re-renders.

You must include [Region](Region.md) ([guide](../../hooks.md)) in your rendered tree:
```csharp
var announce = UseAnnounce();
return VStack(
announce.Region,
Button("Save", () => { Save(); announce.Announce("Document saved"); }),
);
```


