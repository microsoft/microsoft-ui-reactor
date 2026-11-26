# BackdropChoice

Tagged value stored in [`ElementModifiers.Backdrop`](../modifiers/ElementModifiers.md#backdrop). Holds either a [`BackdropKind`](enums.md#backdropkind) selector (the common case, materialized by the host into a built-in WinUI backdrop) or a custom factory delegate (escape hatch for tinted Mica or third-party [`SystemBackdrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop) subclasses) — never both, never neither. The "exactly one set" invariant is enforced by the private constructor; construct via [`BackdropChoice.Of`](#ofbackdropkind) or [`BackdropChoice.Of`](#offuncsystembackdrop).

```csharp
public sealed record BackdropChoice
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Styling and Theming](../../styling.md)

## Remarks

Equality is value-based on [`BackdropChoice.Kind`](#kind) and reference-based on [`BackdropChoice.Factory`](#factory) — matching the change-detection in `BackdropApplier`, which uses `ReferenceEquals` on the factory. Two factory choices compare equal only if they hold the same delegate instance; callers who want stable identity across re-renders should hold the factory in a captured local or a hook state cell.

## Properties

### `Factory`

Custom factory; null when this choice carries a kind selector.

```csharp
public Func<SystemBackdrop?>? Factory  // get
```

### `Kind`

The kind selector, or null when this choice carries a factory.

```csharp
public BackdropKind? Kind  // get
```

## Methods

### `Of`

#### `Of(BackdropKind)`

Construct from a kind selector.

```csharp
public static BackdropChoice Of(BackdropKind kind)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `kind` | [`BackdropKind`](enums.md#backdropkind) |  |

#### `Of(Func<SystemBackdrop?>)`

Construct from a factory delegate.

```csharp
public static BackdropChoice Of(Func<SystemBackdrop?> factory)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<SystemBackdrop?>` |  |
