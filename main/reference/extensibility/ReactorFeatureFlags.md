---
title: "ReactorFeatureFlags"
description: "Process-wide feature flags that gate risky or in-progress behavior. Each flag is a plain mutable boolean so apps can flip it on startup; the defaults are chosen to preserve existing behavior."
---

```csharp
public static class ReactorFeatureFlags
```

Namespace: `Microsoft.UI.Reactor.Core`.

## Remarks [#remarks]

A flag lives here only while its feature is rolling out. Once a flag's default flips to `true` and has soaked for a release, the flag is removed along with the legacy code path it was gating — leaving permanent config knobs here would slow future refactors.

Flags are read lazily — changes after a feature has already initialized are not guaranteed to take effect. Tests that need to toggle a flag should save and restore the value around the scope that depends on it.

## Properties [#properties]

### `FocusRevalidation` [#focusrevalidation]

When true, stale `UseResource` entries refetch on window activation / app resume events. Per-resource opt-out is exposed through `ResourceOptions.RefetchOnWindowFocus` (default `false`).

```csharp
public static bool FocusRevalidation  // get, set
```

**Remarks**

Default: `false`. This mirrors TanStack Query's opt-in posture — window-focus revalidation is surprising in desktop apps where Alt-Tab is a common idiom.

### `HighlightReconcileChanges` [#highlightreconcilechanges]

When true, the reconciler records which `UIElement`s were mounted or modified during each pass and Reactor hosts ([`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) and [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl), via the shared `HighlightOverlayWiring`) draw semi-transparent overlays (red = new mount, yellow = property update) so you can visualize reconcile impact. The overlay is rendered via Composition visuals and is hit-test invisible.

```csharp
public static bool HighlightReconcileChanges  // get, set
```

**Remarks**

Default: `false`. Intended for interactive debugging sessions. When off, the reconciler skips all collection work so there is zero overhead.

### `UseHookBasedPaging` [#usehookbasedpaging]

When true, `DataGridComponent` reads paged data through `UseInfiniteResource` / `UseDataSource` instead of the legacy `DataPageCache`. Part of the Phase-3 migration in `docs/specs/020-async-resources-design.md` §11.

```csharp
public static bool UseHookBasedPaging  // get, set
```

**Remarks**

Default: `false`. The hook-based path is covered by `DataGridParityFixtures` but is not yet the default for consumers with custom `IDataSource` implementations.

### `WarnLayoutFootguns` [#warnlayoutfootguns]

When true, the reconciler emits a one-time diagnostic warning — at mount and on update — for layout "footguns" that silently collapse to `0×0` — most notably an `HStack`/`VStack` placed in a `Grid` [`GridSize.Auto`](/microsoft-ui-reactor/main/reference/layout/GridSize#auto) track with no explicit size and no explicitly-sized children (issue #345). Each warning is written to `Debug.WriteLine`.

```csharp
public static bool WarnLayoutFootguns  // get, set
```

**Remarks**

Default: `false`. In `DEBUG` builds the detection is always on regardless of this flag — the flag is the opt-in switch for `Release` builds. When off in `Release` the detection call site is gated by a single read of this flag per mounted/updated element (the check then no-ops for any non-`Grid` element), so it is effectively free.

Detection runs on both the mount and update reconcile paths, so a warning can appear after a state-driven layout change (e.g. a column flipped to `Auto` or an explicit size removed), not just on first render. Each distinct offending placement warns at most once per process.
