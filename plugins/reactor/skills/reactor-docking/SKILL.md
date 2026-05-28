---
name: reactor-docking
description: "Reactor docking windows — `DockManager`, `DockSplit`/`DockTabGroup`/`Document`/`ToolWindow`, drag-to-float/redock, roles (`DocumentArea`/`ToolWindowStrip`), persistence. Use when building IDE-/Office-shaped layouts with dockable, tear-out tool windows and document wells. READ THIS BEFORE wiring `DockManager` — the content-vs-shape ownership model is the #1 thing people get wrong."
---

# Docking in Reactor

`DockManager` hosts a tree of dockable panes that the user can drag, split,
tear out into floating windows, re-dock, pin to a side, and persist. It is the
foundation for IDE-/Office-shaped apps (solution explorer + editor well +
tool panes).

```csharp
configure: host => DockingNativeInterop.Register(host.Reconciler)  // required once at startup
```

## The ownership model — READ THIS FIRST

This is the single most important rule, and the easiest to get wrong:

> **The app owns CONTENT. The host owns SHAPE.**

- **Content** = which panes exist, their `Title`/`Content`/`Key`, which document
  is active. The app declares this via `manager.Layout`, fresh every render,
  derived from its own `UseState`.
- **Shape** = the user's drag-modified arrangement: split orientations/ratios,
  which group a tab lives in, floating windows. The **host owns this
  internally** (spec 045 §2.30) and resolves the effective layout each render
  by matching your content to its shape *by `Key`*.

### ❌ The anti-pattern that breaks everything

Do **NOT** round-trip the host's live layout back into your own state:

```csharp
// ❌ WRONG — feeds the host's shape back into the content prop.
var (layout, setLayout) = UseState<DockNode?>(BuildLayout());
new DockManager {
    Layout = layout,
    OnLiveLayoutChanged = next => setLayout(next),   // ⛔ double-owns the shape
};
```

This double-owns the shape. Symptoms (all of these at once):
- **Drag-out-to-float works, but you can't drag back to re-dock.**
- **Clicking tabs doesn't switch** / selection resets.
- Splitter drags snap back.

`OnLiveLayoutChanged` is for **observation only** (e.g. a layout inspector). It
is never a setter for `manager.Layout`.

### ✅ The correct pattern

The app holds *which documents are open* (content), not the live tree. Opening
or closing a pane changes the **set of `Key`s** in `manager.Layout`; the host
detects the key-set change and merges it into its shape. Reset is a
`.WithKey(...)` remount.

```csharp
var (openDocs, setOpenDocs) = UseState(ImmutableList.Create(File.AppCs));
var (activeKey, setActiveKey) = UseState<object?>(File.AppCs.Key);
var (epoch, bumpEpoch) = UseReducer(0);

void Open(ProjectFile f) {
    if (!openDocs.Any(d => Equals(d.Key, f.Key))) setOpenDocs(openDocs.Add(f));
    setActiveKey(f.Key);                       // focus it (drives SelectedIndex)
}

var editorWell = new DockTabGroup(
    openDocs.Select(MakeDoc).ToArray(),
    SelectedIndex: openDocs.FindIndex(d => Equals(d.Key, activeKey)),
    ShowWhenEmpty: true,
    Role: DockGroupRole.DocumentArea);

new DockManager {
    Layout = BuildLayout(editorWell),
    // Sync state when the host closes a tab via its X button. The host already
    // removed it from its shape; drop it from openDocs so the key sets converge.
    OnDocumentClosing = args =>
        setOpenDocs(openDocs.RemoveAll(d => Equals(d.Key, args.Document.Key))),
}.WithKey($"dock-{epoch}");   // View ▸ Reset Layout: bumpEpoch + reset openDocs
```

Rules of thumb:
- Derive `manager.Layout` from app state every render. Never store the host's tree.
- Add/remove a pane ⇒ the `Key` set changes ⇒ the host picks it up.
- Model-mutator additions (drag, `DockHostModel.Dock`) keep the same app key set,
  so the host preserves them. That's why round-tripping is both unnecessary and harmful.
- Reset / discard drag state ⇒ `.WithKey($"dock-{epoch}")` bump remounts the host.
- Layout persistence across launches ⇒ `PersistenceId`, not `OnLiveLayoutChanged`.

## Building a layout

```csharp
new DockSplit(Orientation.Vertical, new DockNode[] {
    new DockSplit(Orientation.Horizontal, new DockNode[] {
        new DockTabGroup(new DockableContent[]{ solutionTool }, Width: 260,
            Role: DockGroupRole.ToolWindowStrip),
        editorWell,                                              // DocumentArea
        new DockTabGroup(new DockableContent[]{ propsTool, gitTool }, Width: 300,
            TabPosition: TabPosition.Bottom, CompactTabs: true,
            Role: DockGroupRole.ToolWindowStrip),
    }),
    new DockTabGroup(new DockableContent[]{ output, terminal, errors },
        Height: 220, TabPosition: TabPosition.Bottom, CompactTabs: true,
        Role: DockGroupRole.ToolWindowStrip),
});
```

- `Document` (closable, not pinnable) vs `ToolWindow` (hideable, side-pinnable,
  `AllowedSides` mask). Both reconcile through `DockableContent`.
- **Roles** (spec 046): `DocumentArea` is the preferred `Dock(Center)` target and
  **survives empty** (the well stays a visible drop target). `ToolWindowStrip` is
  the preferred target for tool-window drops. Use object-initializer syntax for
  `Document`/`ToolWindow` so you opt into permission flags additively.
- Every pane needs a **stable, equatable `Key`** — it's how the host preserves
  pane state across reorders, tear-out, and re-dock. No fallback to title-keying.

## Pane content gotchas

### Make pane bodies fill the pane

A docked pane body is **content-sized** by default — a plain `Border`/`TextBox`
collapses to its desired height at the top of the pane. To fill, put it in a
flex container with `grow`:

```csharp
FlexColumn(
    editorTextBox.Flex(grow: 1, basis: 0)
).Flex(grow: 1);
```

### Multi-line TextBox: `AcceptsReturn` must be an element prop, not `.Set`

The `TextBox` descriptor applies `AcceptsReturn`/`TextWrapping` **before** `Text`
on purpose — single-line mode truncates `Text` at the first newline. A
`.Set(tb => tb.AcceptsReturn = true)` lambda runs *after* `Text` is assigned, so
the body collapses to one line. Use the first-class modifiers:

```csharp
// ✅ ordered before Text by the descriptor
TextBox(text, setText).AcceptsReturn().TextWrapping(TextWrapping.NoWrap)

// ❌ runs after Text → multi-line content truncated to the first line
TextBox(text, setText).Set(tb => tb.AcceptsReturn = true)
```

Generally: any prop whose *ordering relative to another prop matters* belongs on
the element (`.AcceptsReturn()`, `.TextWrapping()`), not in a `.Set(...)` escape
hatch — `.Set` always runs last.

## Key APIs

| API | Purpose |
|-----|---------|
| `DockManager { Layout, PersistenceId, ... }` | The host element |
| `DockSplit(Orientation, children)` | Resizable split (ratios owned by host) |
| `DockTabGroup(docs, TabPosition, CompactTabs, SelectedIndex, Width/Height, Role)` | A tab group |
| `Document { Title, Key, Content, CanClose }` | Closable document pane |
| `ToolWindow { Title, Key, Content, CanPin, AllowedSides, ... }` | Hideable/pinnable tool pane |
| `.WithKey($"dock-{epoch}")` | Remount to reset/discard drag shape |
| `OnDocumentClosing` | Sync app state when the host closes a tab |
| `OnLiveLayoutChanged` | **Observe** the resolved layout (never feed back to `Layout`) |
| `DockingNativeInterop.Register(host.Reconciler)` | One-time startup registration |

See `samples/apps/reactor-ide` for a complete IDE-shaped app exercising all of this.
