# UseAnnounce

Creates an [`AnnounceHandle`](hook-types.md#announcehandle) for making screen reader announcements. The handle persists across re-renders.

You must include [`AnnounceHandle.Region`](hook-types.md#region) in your rendered tree:

```csharp
var announce = UseAnnounce();
return VStack(
    announce.Region,
    Button("Save", () => { Save(); announce.Announce("Document saved"); }),
);
```

```csharp
public static AnnounceHandle UseAnnounce(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseAnnounceExtensions`.

**Learn more:** [Accessibility](../../accessibility.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected AnnounceHandle UseAnnounce()
public static AnnounceHandle UseAnnounce(this Component component)
```
