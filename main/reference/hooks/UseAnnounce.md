---
title: "UseAnnounce"
seo:
  description: "Creates an AnnounceHandle for making screen reader announcements. The handle persists across re-renders."
---

Creates an [`AnnounceHandle`](/microsoft-ui-reactor/main/reference/hooks/hook-types#announcehandle) for making screen reader announcements. The handle persists across re-renders.

You must include [`AnnounceHandle.Region`](/microsoft-ui-reactor/main/reference/hooks/hook-types#region) in your rendered tree:

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

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseAnnounceExtensions`.

**Learn more:** [Accessibility](/microsoft-ui-reactor/main/guides/accessibility), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected AnnounceHandle UseAnnounce()
public static AnnounceHandle UseAnnounce(this Component component)
```
