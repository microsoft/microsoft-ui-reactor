---
title: "Migration: Windowing evolution"
description: "Spec 054 intentionally removes a few legacy windowing fields in favor of more explicit shapes. The new APIs are additive except for the breaking changes below."
sidebar:
  label: "Windowing evolution"
---

| Removed / changed API | Replacement | Migration |
| --- | --- | --- |
| `IsResizable` (bool) | `ResizeMode` (`WindowResizeMode`) | `IsResizable = false` → `ResizeMode = WindowResizeMode.NoResize`. The old default `true` is the new default `CanResize`. |
| `IsShownInSwitchers` (bool) | `ShowInTaskbar` + `ShowInSwitcher` | `IsShownInSwitchers = false` → `ShowInTaskbar = false, ShowInSwitcher = false`. |
| `IsAlwaysOnTop` (bool) | `Level` (`WindowLevel`) | `IsAlwaysOnTop = true` → `Level = WindowLevel.AlwaysOnTop`. |
| `WindowStartPosition.RestoreFromPersistence` | `.WithPersistence(id)` or `PersistPlacement = true` | Old: `StartPosition = WindowStartPosition.RestoreFromPersistence, PersistenceId = "main"`. New: `new WindowSpec { Title = "Main" }.WithPersistence("main")`. |
| `ExtendsContentIntoTitleBar` (`bool`) | `ExtendsContentIntoTitleBar` (`bool?`) plus `TitleBar(...)` inference | If a `TitleBar(...)` element is present, drop `ExtendsContentIntoTitleBar = true` and let Reactor infer it. Keep explicit `true` only for content-extension without a `TitleBar(...)`; keep explicit `false` to force system chrome. |

## Examples [#examples]

```csharp
// Before
// new WindowSpec
// {
//     Title = "Tools",
//     IsResizable = false,
//     IsShownInSwitchers = false,
//     IsAlwaysOnTop = true,
// };

// After
public static WindowSpec Tools { get; } = new()
{
    Title = "Tools",
    ResizeMode = WindowResizeMode.NoResize,
    ShowInTaskbar = false,
    ShowInSwitcher = false,
    Level = WindowLevel.AlwaysOnTop,
};
```

```csharp
// Before
// new WindowSpec
// {
//     Title = "Main",
//     PersistenceId = "main",
//     StartPosition = WindowStartPosition.RestoreFromPersistence,
// };

// After
public static WindowSpec Main { get; } =
    new WindowSpec { Title = "Main" }.WithPersistence("main");
```

`WindowPlacementCodec` stores only monitor fingerprints and `WINDOWPLACEMENT`,
not `WindowStartPosition`, so no on-disk placement migration is required.

## Next Steps [#next-steps]

- **[Windows](/microsoft-ui-reactor/main/guides/windows)** — The post-054 windowing surface in full: sizing, placement, z-order, chrome, taskbar, displays.
- **[Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)** — Interop-heavy recipes that deliberately sit outside the `WindowSpec` contract.
- **[Persistence](/microsoft-ui-reactor/main/guides/persistence)** — Persisted scopes beyond window placement.
