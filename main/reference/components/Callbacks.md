---
title: "Callbacks<T>"
description: "An opt-in, always-equal wrapper for the delegate (callback) portion of a component's props, so callback identity never forces a re-render."
sidebar:
  label: "Callbacks"
---

```csharp
public sealed record Callbacks<T>(T Value)
```

Namespace: `Microsoft.UI.Reactor.Core`.

## Remarks [#remarks]

[`Component<TProps>`](/microsoft-ui-reactor/main/reference/components/Component-1) memoizes against `!Equals(oldProps, newProps)` in its `ShouldUpdate` method. For `record` props that is a field-by-field compare, and `Action`/`Func` delegate fields compare by *reference*. A parent typically passes a freshly-allocated delegate (a lambda or local function) on every render, so a child's props compare unequal and the child re-renders even though no observable data changed.

Historically apps worked around this by hand-writing `Equals`/`GetHashCode` on every props record, listing only the data fields — ~10 lines each, with a silent stale-UI bug if you forgot a field. Wrap the callbacks in [`Callbacks<T>`](/microsoft-ui-reactor/main/reference/components/Callbacks) instead: its [`Callbacks.Equals`](/microsoft-ui-reactor/main/reference/components/Callbacks) is constant `true` and [`Callbacks.GetHashCode`](/microsoft-ui-reactor/main/reference/components/Callbacks) is constant `0`, so the outer record's auto-generated equality treats the callbacks slot as always-equal — data fields still drive the memo decision, callbacks never do.

```csharp
public sealed record StepCardCallbacks(
    Action<int, string> OnPromptChanged,
    Action<StepModel> OnRun);

public sealed record StepCardProps(
    StepModel Step,
    bool IsGenerating,
    Callbacks<StepCardCallbacks> Cb);   // ← no manual Equals needed
```

**Stale-delegate guarantee.** "Always-equal for diffing" must not mean "stale delegate at invoke". When the reconciler skips a child's re-render because the data is unchanged, it still refreshes the child's [`Component.Props`](/microsoft-ui-reactor/main/reference/components/Component-1#props) with the latest props (see `Reconciler.ReconcileComponent`). So a handler that reads `Props.Cb.Value.OnRun` *live* at event time always invokes the *current* delegate, never the memoized one. Read callbacks off `Props` at dispatch time — do not capture `Props.Cb.Value.OnRun` into a local at render time.

## Type parameters [#type-parameters]

| Name | Description |
| --- | --- |
| `T` | The payload carrying the callbacks. Usually a small record (or a single delegate) holding only `Action`/`Func` delegate members. |

## Constructors [#constructors]

### `Callbacks` [#callbacks]

An opt-in, always-equal wrapper for the delegate (callback) portion of a component's props, so callback identity never forces a re-render.

```csharp
public Callbacks(T Value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | The wrapped callbacks payload. Read it live at dispatch time. |

**Type parameters**

| Name | Description |
| --- | --- |
| `T` | The payload carrying the callbacks. Usually a small record (or a single delegate) holding only `Action`/`Func` delegate members. |

**Remarks**

[`Component<TProps>`](/microsoft-ui-reactor/main/reference/components/Component-1) memoizes against `!Equals(oldProps, newProps)` in its `ShouldUpdate` method. For `record` props that is a field-by-field compare, and `Action`/`Func` delegate fields compare by *reference*. A parent typically passes a freshly-allocated delegate (a lambda or local function) on every render, so a child's props compare unequal and the child re-renders even though no observable data changed.

Historically apps worked around this by hand-writing `Equals`/`GetHashCode` on every props record, listing only the data fields — ~10 lines each, with a silent stale-UI bug if you forgot a field. Wrap the callbacks in [`Callbacks<T>`](/microsoft-ui-reactor/main/reference/components/Callbacks) instead: its [`Callbacks.Equals`](/microsoft-ui-reactor/main/reference/components/Callbacks) is constant `true` and [`Callbacks.GetHashCode`](/microsoft-ui-reactor/main/reference/components/Callbacks) is constant `0`, so the outer record's auto-generated equality treats the callbacks slot as always-equal — data fields still drive the memo decision, callbacks never do.

```csharp
public sealed record StepCardCallbacks(
    Action<int, string> OnPromptChanged,
    Action<StepModel> OnRun);

public sealed record StepCardProps(
    StepModel Step,
    bool IsGenerating,
    Callbacks<StepCardCallbacks> Cb);   // ← no manual Equals needed
```

**Stale-delegate guarantee.** "Always-equal for diffing" must not mean "stale delegate at invoke". When the reconciler skips a child's re-render because the data is unchanged, it still refreshes the child's [`Component.Props`](/microsoft-ui-reactor/main/reference/components/Component-1#props) with the latest props (see `Reconciler.ReconcileComponent`). So a handler that reads `Props.Cb.Value.OnRun` *live* at event time always invokes the *current* delegate, never the memoized one. Read callbacks off `Props` at dispatch time — do not capture `Props.Cb.Value.OnRun` into a local at render time.

## Properties [#properties]

### `Value` [#value]

The wrapped callbacks payload. Read it live at dispatch time.

```csharp
public T Value  // get, init
```

## Operators [#operators]

### `op_Implicit` [#op_implicit]

Wraps a callbacks payload so it can be assigned directly to a [`Callbacks<T>`](/microsoft-ui-reactor/main/reference/components/Callbacks) props field without calling the constructor — the payload type is inferred from the target field, so authors just write `new StepProps(step, new StepCallbacks(...))`.

```csharp
public static implicit operator Callbacks<T>(T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `T` |  |
