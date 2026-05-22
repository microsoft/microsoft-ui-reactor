# Docking — Overview

> Source authoring notes for the docking topic. Per the auto-memory
> `feedback_docs_pipeline.md`, the rendered `docs/guide/docking.md`
> is **generated output** — never hand-edit it. Edit this file (and
> `api.md` alongside) and the future `docs/_pipeline/templates/
> docking.md.dt`, then re-run the pipeline.

## What docking is

Reactor's docking system lets a single Reactor shell host multiple,
user-rearrangeable surfaces — the Visual Studio / VS Code / Photoshop
/ Figma layout idiom. Users drag tabs between groups, split panes,
pin tool windows to a side, and tear panes out into floating
sub-windows; layouts persist across sessions; per-pane state composes
with [`WindowPersistedScope`](persistence.md).

The first-class element is `DockManager` (Phase 1) / `DockHost`
(Phase 3 rename). It carries an immutable [`DockNode`](api.md#docknode)
tree describing the desired layout. The reconciler turns that tree
into:

- **Phase 1 (historical):** a single
  [vendored WinUI.Dock](https://github.com/qian-o/WinUI.Dock) XAML
  control wrapped by `src/Reactor.Docking.Xaml/`. The wrapper +
  vendored control were retired at the §2.29 review gate; both source
  trees are removed in this branch.
- **Phase 2 (this release):** a Reactor-native pane stack shipping in
  the core `Microsoft.UI.Reactor` package. Public API stays identical
  to Phase 1 (same `DockManager`/`DockNode`/`DockableContent`); the
  underlying renderer is `DockHostNativeComponent` composing WinUI
  primitives directly.
- **Phase 3:** any `ReactorWindow` becomes adoptable into a
  `DockHost`. Tear-outs are real top-level Reactor windows.
- **Phase 4:** floating windows use the WinUI 11 `TitleBar` control
  for the tabs-in-titlebar Edge / Files / Terminal pattern.

The four phases share **one public surface** (committed at the
[Phase 1 exit gate](../../../specs/045-docking-windows-design.md#47-phase-1-human-review-gate)).
Each phase is gated by a human-in-the-loop interactivity review —
crashes, snap-back glitches, or hover-state lag are merge blockers.

## Phase 1 capabilities (this release)

What works today against the wrapper:

- **Documents in tab groups.** `DockTabGroup` holds N `DockableContent`
  leaves. Tabs reorder by drag; the active tab is reported via
  `SelectedIndex`. Tab strip position via `TabPosition.{Top,Bottom}`.
- **Recursive splits.** `DockSplit(Orientation, …)` nests arbitrarily.
  Splitters drag-resize with min/max clamping. Sizes round-trip
  through `SaveLayout`/`LoadLayout`.
- **Side pins (auto-hide).** `LeftSide`/`TopSide`/`RightSide`/
  `BottomSide` on `DockManager` carry pinned tool windows. Click the
  side icon → SidePopup expands; click out → collapse.
- **Floating tear-out.** Drag a tab title into open space → a
  FloatingWindow appears at the pointer with a custom title bar from
  `IDockAdapter.GetFloatingWindowTitleBar(…)`. Drop back into any tab
  group to re-dock.
- **Programmatic dock.** Mutate the `DockManager.Layout` tree to
  move panes via code. Phase 2 also exposes the `DockHostModel`
  surface (`Dock`/`Float`/`Hide`/`Show`/`Close`/`Activate`/`PinToSide`)
  for app-driven mutations that route through the same lifecycle
  event pipeline as user-driven drags.
- **Persistence.** `manager.SaveLayout()` → JSON; `LoadLayout(json)`
  restores. The wrapper auto-routes through
  `WindowPersistedScope["docking:<PersistenceId>"]` when
  `PersistenceId` is set.
- **Compact + bottom tabs.** `DockTabGroup(…, TabPosition: Bottom,
  CompactTabs: true)` mirrors Office's tool-pane shape.

## Phase 1 — known limitations

These motivate the Phase 2 / 3 / 4 work:

- **No cross-`DockManager` drag.** Phase 1 restricts drag-out to
  within a single manager. Cross-manager → Phase 3.
- **Single role `DockableContent`.** Visual Studio's document /
  tool-window distinction is collapsed in Phase 1. Phase 2 introduces
  `Document` and `ToolWindow` subclasses.
- **No per-pane state slot.** `PersistenceState` is a plain string
  (typed `Document<TState>` lands in Phase 2).
- **A11y baseline only.** Drop-target overlay isn't keyboard-driven
  yet. Phase 2 ships full keyboard nav (Ctrl+Tab, Ctrl+F4,
  Ctrl+Shift+M) and live-region announcements.
- **No cancellable events.** Phase 1 has `IDockBehavior.OnDocked` /
  `OnFloating` — informational only. Phase 2's `OnDocking` /
  `OnContentDocking` etc. are cancellable.

## Sample

See [`samples/apps/dock-showcase/`](../../../samples/) (lands with
the showcase commit). The six scenes mirror the
[Phase 1 review script](../../../specs/045-docking-windows-design.md#47-phase-1-human-review-gate):

| Scene | What it exercises |
|---|---|
| A — IDE | Solution Explorer (left tool) + center editor tabs + Properties (right tool) + Error List / Terminal (bottom tabs). |
| B — Floating | Tear-out, drop-back, custom title bar. |
| C — Side pin | Pin → SidePopup → close from popup. |
| D — Compact / Bottom | `CompactTabs=true` + `TabPosition=Bottom`. |
| E — Persistence | Save / Load via file menu. |
| F — Programmatic | "Open Properties" button issues `DockTo(…)`. |

## Registration

The native docking host isn't auto-registered with Reactor's
reconciler — apps opt in by calling
`DockingNativeInterop.Register(host.Reconciler)` at host construction
time (same pattern as `XamlInterop.Register`):

```csharp
public class App : ReactorApplication
{
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new ReactorWindow();
        var host = window.Host;
        DockingNativeInterop.Register(host.Reconciler);
        host.Mount(_ => new MyShell());
        window.Activate();
    }
}
```

After registration, any `DockManager` element in the tree is
recognized.

## See also

- [API surface](api.md) — every record / interface / enum with
  example invocation.
- [Spec 045](../../../specs/045-docking-windows-design.md) — the
  full design, including non-goals, prior-art matrix, and the
  four-phase plan.
