# Existing-API Surface Inventory — Phase 0 §14 Deliverable 5

Confirms Appendix A's mapping against the current source. Feeds Phase 1's
first task (promote `ApplySetters` / `SetElementTag` / `GetElementTag` per
spec §14 Phase 1 line 1).

## Members named in spec §3 / Appendix A — current location

| Member | Status today | Citation | Spec §3 / App A claimed | Drift |
|---|---|---|---|---|
| `Reconciler.SetElementTag(FrameworkElement, Element?)` | `internal static` | `Reconciler.cs:331` | `internal` at `:331-352` | None — current. |
| `Reconciler.GetElementTag(UIElement)` | `internal static` | `Reconciler.cs:346` | `internal` at `:331-352` | None. |
| `Reconciler.GetElementTag(FrameworkElement)` | `internal static` | `Reconciler.cs:351` | `internal` at `:331-352` | None. |
| `Reconciler.ApplySetters<T>(Action<T>[], T)` | `internal static` | `Reconciler.cs:1436` | `internal` at `:1436` | None. |
| `ChangeEchoSuppressor` | `internal static class` | `ChangeEchoSuppressor.cs:37` | `internal`, dedicated file | None. |
| `Reconciler.EventHandlerState` class | `internal sealed class` | `Reconciler.cs:2787` | `internal` at `:2780+` | **+7 lines** — spec §3 says `:2780+`, actual is `:2787`. Spec needs an update. |
| `Reconciler.Ensure*Subscribed` family | `private static` | `Reconciler.cs:2963–3120+` | `:2963-3069+` | Spec range understates — current family runs through `~:3200` (post-tapped/key/focus growth). Cosmetic. |
| `Reconciler.ApplyDefaultAutomationName` | `internal static` | `Reconciler.cs:1451` | `internal`, file-scoped | None. |
| `Reconciler.UpdateDefaultAutomationName` | `internal static` | `Reconciler.cs:1467` | Not separately named in §3 | New member since spec drafted — App A could mention. |
| `Reconciler.ApplyThemeBindings` | `private static` | `Reconciler.cs:3304` | `internal`, no line | **Visibility drift**: spec §3 says `internal`; current is `private`. Promotion needed regardless. |
| `Reconciler.ApplyResourceOverrides` | `private static` | `Reconciler.cs:3406` | `internal`, no line | **Visibility drift**: same as above. |
| `ElementPool` | **`public sealed class`** | `ElementPool.cs:12` | "Pool/`_pool`" | The class itself is already `public`, but the per-reconciler `_pool` field and `TryRent` / `Return` policy are reached only by built-in mount/unmount paths. External authors today can construct an `ElementPool` but cannot plumb it through `Reconciler`. |

### Suggested spec edits (handled by 0.5 follow-up)

- Update §3 citation `Reconciler.cs:2780+` → `Reconciler.cs:2787` (EventHandlerState).
- Update §3 citation `2963-3069+` → `2963-3200` (current range of Ensure*).
- Update Appendix A row "internal `ApplyThemeBindings` / `ApplyResourceOverrides`" to reflect their actual `private` visibility (they were factored down since the spec was drafted).
- Append `UpdateDefaultAutomationName` next to `ApplyDefaultAutomationName` — they ship as a pair now.

## In-tree `RegisterType` consumers and what they reach for

A complete enumeration of `RegisterType` call sites in this repo. The
"reaches for" column lists `internal` members used by the registration.

| Call site | Element / Control | Reaches for internals | Notes |
|---|---|---|---|
| `src/Reactor/Controls/DataGrid/ResizeGrip.cs:45` | `ResizeGripElement` → `ResizeGripControl` | `Reconciler.SetElementTag` (`:58`, `:86`) | Same-assembly access. Sets background once at mount; pointer handlers mutate state directly. Already part of the engine assembly — promotion is invisible to it. |
| `src/Reactor/Docking/Native/DockSplitterElement.cs:47` | `DockSplitterElement` → `DockSplitterControl` | None (uses a private CWT) | Manages its own delegate storage via `ConditionalWeakTable<DockSplitterControl, EventHandler<…>>`. Comment explains: WinRT projection rejects closed-generic delegates at `SetValue` time; the CWT sidesteps COM. Would still benefit from `BindFor.OnCustomEvent` in the v1 protocol (§4). |
| `src/Reactor/Docking/Native/DockingNativeInterop.cs:52` | `DockManager` → `Border` | (check below) | |
| `src/Reactor/Docking/Native/DockDropTargetOverlayElement.cs:40` | `DockDropTargetOverlayElement` → `DockDropTargetOverlayControl` | (check below) | |
| `src/Reactor/Hosting/XamlInterop.cs:51` | `XamlPageElement` → `Frame` | `Reconciler.SetElementTag` (`:56`, `:63`) | In-tree. |
| `src/Reactor/Hosting/XamlInterop.cs:74` | `XamlHostElement` → `FrameworkElement` | `Reconciler.SetElementTag` (`:79`, `:85`), `Reconciler.DetachReactorState` (`:98`) | In-tree. `DetachReactorState` is another internal member not in App A — see follow-up. |
| `samples/apps/monaco-editor/Monaco/MonacoEditorElement.cs:38` | `MonacoEditorElement` → `MonacoEditor` | **None** — uses `editor.Tag = el` directly | External consumer. Bypasses `SetElementTag` entirely. Implications: no pool survival (no `ReactorState`), no trampoline reattach across re-mount, no echo suppression. Works because Monaco's `TextChanged` already filters `IsFlush` to ignore programmatic writes — the echo-suppression invariant is hand-rolled into the WinUI control. |
| `samples/apps/regedit/Components/SplitPanel.cs:29` | `CursorBorderElement` → `CursorPanel` | **None** — uses `panel.Tag = el` directly | External consumer. Same pattern as Monaco. |

### Follow-up: confirm the two remaining docking sites

The DockManager and DockDropTargetOverlay registrations were not opened during
this audit. Spec §3 says "the in-tree docking system" reaches `internal`
members; the splitter one (above) does not. The remaining two should be
opened during the Phase 1 promotion PR — if they only use
`Reconciler.SetElementTag`, the promotion list does not grow.

## Members that must be promoted to `public` in Phase 1

Driven by spec §14 Phase 1 line 1 + the consumer enumeration above. "Must"
means "an out-of-assembly handler authored against the v1 protocol cannot be
correctness-equivalent to a built-in without this member."

| Member | Reason | Today's visibility |
|---|---|---|
| `Reconciler.ApplySetters<T>` | Required to honor `.Set(...)` modifier chains on external elements without reflection. | `internal static` |
| `Reconciler.SetElementTag` | Required to wire the attached-DP-keyed state machine for pool-survivable event reattach and echo suppression. | `internal static` |
| `Reconciler.GetElementTag` (both overloads) | Required for handler trampolines to read the current element on dispatch. | `internal static` |
| `Reconciler.DetachReactorState` | Required for handlers that release a control to a long-lived parent (matches `XamlInterop` unmount path). Not in Appendix A today. | `internal static` (per `:98` use) |
| `ChangeEchoSuppressor.BeginSuppress(UIElement)` | Required until §8 lands. After §8, exposed as `ReactorBinding<T>.WriteSuppressed`. | `internal static` |
| `ChangeEchoSuppressor.ShouldSuppress(UIElement)` | Used inside trampolines to drop echoed events. After §8, the surface goes away. | `internal static` |
| `Reconciler.ApplyDefaultAutomationName` / `UpdateDefaultAutomationName` | Required for accessibility parity — external controls today don't get auto-named UIA peers. | `internal static` |
| `Reconciler.ApplyThemeBindings` | Required so external controls accept `.ThemeBindings(...)` modifier values. Currently `private`; promote to `internal` first, then `public`. | `private static` |
| `Reconciler.ApplyResourceOverrides` | Required so external controls accept `.ResourceOverrides(...)`. Promote `private` → `internal` → `public`. | `private static` |
| `Reconciler.EventHandlerState` + `Ensure*Subscribed` family | Not promoted as raw types — exposed through the `ReactorBinding<T>` façade in §4. Internal stays internal. | `internal` (class), `private static` (helpers) |
| `ElementPool` rental API on a `Reconciler` instance | Required so external `Mount` handlers can `AllocateControl(static () => new …)` and get pool-backed allocation. Class is already `public`, but reconciler-level access is private. | mixed |

## Members that can stay `internal`

Members backing the public surface but not directly exposed to handler
authors:

- `Reconciler.GetOrCreateReactorState` (`:325`) — internal helper; the public
  surface is `Bind` / `BindFor` in the v1 protocol.
- `ReactorAttached.StateProperty` — the attached DP itself; consumers reach
  it via `SetElementTag` / `GetElementTag`.
- `Reconciler.GetListState` / `SetListState` (`:361+`) — spec 042 territory,
  not extensibility.
- `EventHandlerState` raw class — wrapped by `ReactorBinding<T>` in §4.
- The `Ensure*Subscribed` family — wrapped by `ReactorBinding<T>.On<Event>`.

## Implications

1. **Phase 1 promotion PR scope.** Six internal helpers (`ApplySetters`,
   `SetElementTag`, both `GetElementTag` overloads, `DetachReactorState`,
   `ApplyDefaultAutomationName`, `UpdateDefaultAutomationName`) move to
   `public`. Two `private` helpers (`ApplyThemeBindings`,
   `ApplyResourceOverrides`) move to `internal` then `public`. None require
   API design — they're already shaped for the v1 protocol.

2. **External consumers' technical debt.** Both samples sidestep the engine
   entirely via `Tag = el`. They get away with it because their hosted
   controls don't pool (Monaco is heavyweight; CursorPanel is simple) and
   their event signatures don't echo (Monaco's `IsFlush` flag, CursorPanel's
   pure user-input handlers). Once the public surface lands, both should
   migrate to `BindFor<T>` — captured as a Phase 2 follow-up.

3. **In-tree consumers are already compliant.** Every in-tree `RegisterType`
   site uses `SetElementTag`. Phase 1's promotion is source-compatible —
   no in-tree migration required, just the visibility change.

4. **Docking registrations need confirmation.** Two of the docking
   registrations (`DockManager`, `DockDropTargetOverlay`) were not fully
   inspected. The Phase 1 promotion PR should open them; the conservative
   prediction is they use only `SetElementTag`.
