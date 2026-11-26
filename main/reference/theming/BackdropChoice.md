---
title: "BackdropChoice"
seo:
  description: "Tagged value stored in ElementModifiers.Backdrop. Holds either a BackdropKind selector (the common case, materialized by the host into a built-in WinUI backdrop) or a custom factory delegate (escape hatch for tinted Mica or third-party SystemBackdrop subclasses) — never both, never neither. The \"exactly one set\" invariant is enforced by the private constructor; construct via BackdropChoice.Of or BackdropChoice.Of."
---

Tagged value stored in [`ElementModifiers.Backdrop`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers#backdrop). Holds either a [`BackdropKind`](/microsoft-ui-reactor/main/reference/theming/enums#backdropkind) selector (the common case, materialized by the host into a built-in WinUI backdrop) or a custom factory delegate (escape hatch for tinted Mica or third-party [`SystemBackdrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop) subclasses) — never both, never neither. The "exactly one set" invariant is enforced by the private constructor; construct via [`BackdropChoice.Of`](#ofbackdropkind) or [`BackdropChoice.Of`](#offuncsystembackdrop).

```csharp
public sealed record BackdropChoice
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## Remarks [#remarks]

Equality is value-based on [`BackdropChoice.Kind`](#kind) and reference-based on [`BackdropChoice.Factory`](#factory) — matching the change-detection in `BackdropApplier`, which uses `ReferenceEquals` on the factory. Two factory choices compare equal only if they hold the same delegate instance; callers who want stable identity across re-renders should hold the factory in a captured local or a hook state cell.

## Properties [#properties]

### `Factory` [#factory]

Custom factory; null when this choice carries a kind selector.

```csharp
public Func<SystemBackdrop?>? Factory  // get
```

### `Kind` [#kind]

The kind selector, or null when this choice carries a factory.

```csharp
public BackdropKind? Kind  // get
```

## Methods [#methods]

### `Of` [#of]

#### `Of(BackdropKind)` [#ofbackdropkind]

Construct from a kind selector.

```csharp
public static BackdropChoice Of(BackdropKind kind)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `kind` | [`BackdropKind`](/microsoft-ui-reactor/main/reference/theming/enums#backdropkind) |  |

#### `Of(Func<SystemBackdrop?>)` [#offuncsystembackdrop]

Construct from a factory delegate.

```csharp
public static BackdropChoice Of(Func<SystemBackdrop?> factory)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<SystemBackdrop?>` |  |
