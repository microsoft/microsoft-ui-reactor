# 046 — Docking Content Types & Reserved Document Area

| | |
|---|---|
| **Status** | Draft — 2026-05-23 |
| **Owner** | @codemonkeychris |
| **Related** | [045](045-docking-windows-design.md) (docking design — this spec is a P3 additive amendment per §6.4), feedback note `pix/winui-port/feedback/reactor-docking-center-targets-leftmost-group.md` |

## 1. Summary

Reactor's docking system today treats every `DockTabGroup` as interchangeable. `DockHostModel.Dock(content, DockTarget.Center)` routes into the **leftmost descendant tab group** of the root — regardless of what that group contains. In a Visual Studio-shaped layout (`[ left tool window | empty doc area | right tool window ]`), programmatically opening a document tabs it next to the left tool window, not into the empty middle group.

This spec introduces a small additive vocabulary on top of the existing `DockNode` algebra so that apps can express the **document-area-vs-tool-window-strip** distinction that every IDE-class docking framework supports (AvalonDock, Avalonia Dock, DevExpress, Telerik, Eclipse, IntelliJ, Qt). The shape:

- A `DockGroupRole` tag on `DockTabGroup` (`General` | `DocumentArea` | `ToolWindowStrip`).
- An `AllowedSides` mask on `ToolWindow` (Qt-style placement constraint).
- A routing rule that prefers role-matched groups for `Dock(Center)` and similar.
- Reserved-center semantics: `DocumentArea` groups survive empty without being culled.
- Drag-drop overlay filters incompatible drop targets.

Default `Role = General` and unconstrained `AllowedSides` preserve today's behavior for callers that don't opt in. No breaking change.

## 2. Motivation

### 2.1 The leftmost-descendant bug

`DockLayoutMutator.AddAsTab` in `src/Reactor/Docking/Native/DockLayoutMutator.cs:336-366` unconditionally recurses into `s.Children[0]` when the root is a `DockSplit`:

```csharp
case DockSplit s:
{
    if (s.Children.Count == 0) return pane;
    var newChildren = new DockNode[s.Children.Count];
    for (int i = 0; i < s.Children.Count; i++) newChildren[i] = s.Children[i];
    newChildren[0] = AddAsTab(s.Children[0], pane);
    return s with { Children = newChildren };
}
```

So `Dock(Center)` is really "leftmost descendant tab group". A VS-style layout

```
DockSplit Horizontal
├─ DockTabGroup [ ToolWindow "Gallery Items" ]   (leftmost)
├─ DockTabGroup [ ]                              (intended doc area)
└─ DockTabGroup [ ToolWindow "Configuration" ]
```

routes documents into the Gallery Items group every time.

### 2.2 Why apps can't work around it

`DockHostModel`'s public mutators are `Dock(target)`, `Float`, `Hide`, `Show`, `Close`, `Activate`, `PinToSide`. None lets the caller name a target `DockTabGroup`. `IDockLayoutStrategy.BeforeInsertDocument` gets the model handle but has the same `Dock(target)` available, so it can't route either.

Restructuring the layout doesn't fix this. To make `Dock(Center)` land in the doc area, the doc area's `DockTabGroup` must be the leftmost descendant of the root — conflicting with the natural "tool window on the left, documents in the middle" arrangement every IDE uses.

### 2.3 Empty document area collapses

A related issue: closing the last document in a tab group runs the post-close cull pass and removes the group entirely, collapsing the layout. In a VS-style shell, the document well should remain as a visible empty surface ready to accept the next document. Today this requires `ShowWhenEmpty = true` on the group, which is a flag the app has to remember to set and doesn't compose with the routing problem above.

## 3. Goals / non-goals

### 3.1 Goals

- **G1.** `Dock(content, Center)` lands in the right place when the layout has a designated document area, without the caller holding group references.
- **G2.** Document-area groups survive empty without per-group `ShowWhenEmpty` bookkeeping.
- **G3.** Apps can constrain where tool windows are allowed to dock (drag-drop and programmatic), Qt-style.
- **G4.** Drag-drop overlay greys out incompatible drop targets so users get visual feedback before releasing the drag.
- **G5.** Tagging round-trips through layout JSON; default-value handling preserves old layouts.
- **G6.** No breaking changes for callers that don't opt in. Default `Role = General` produces today's behavior.
- **G7.** Add the public group-targeting overload that already exists internally (`Dock(content, DockTabGroup, DockTarget)`), so strategies and advanced apps have a hatch.

### 3.2 Non-goals

- **N1.** A predicate / `Accepts` function on `DockTabGroup` (option 3 from §5). Punted to a future spec if v1 enums prove insufficient.
- **N2.** Named-region / template-style layouts (option 2 from §5). Larger redesign; not motivated by current scenarios.
- **N3.** First-party `VisualStudioLayoutStrategy` (option 4 from §5). The role+mask vocabulary makes most VS-style behavior the default; a starter strategy can ship later if needed.
- **N4.** Restructuring `Document` / `ToolWindow` into separate node types (AvalonDock's `LayoutDocumentPane` vs `LayoutAnchorablePane` shape). Spec 045 §5.3.1 already chose the "category-on-content + tag-on-group" model; this spec follows through on it rather than reopening it.
- **N5.** Drag-drop changes that affect the drag *session* (preview, snap-back, threshold). Only the **drop-target filter** changes.

## 4. Prior art

Surveyed in conversation prior to drafting; summarized here for spec self-containedness.

### 4.1 Typed containers (this spec's lineage)

- **AvalonDock (WPF)** — `LayoutDocumentPane` vs `LayoutAnchorablePane`. Document panes survive empty. Routing by type.
- **Avalonia Dock** — `DocumentDock` vs `ToolDock`. `DocumentDock.CanCreateDocument` lets the host construct new docs in place.
- **DevExpress DockManager** — `DocumentGroup` vs `LayoutGroup`. `DocumentGroup` is the reserved center; never collapses.
- **Telerik RadDocking** — `RadDocumentHost` is the reserved document well.
- **Syncfusion DockingManager** — `DocumentContainer = true` flag on a child.

The common pattern: **container kind IS the policy.** Type enforcement is structural, default placement is "find the container of matching type," reserved space is "DocumentDock survives empty."

### 4.2 Allowed-areas mask on content

- **Qt `QDockWidget.setAllowedAreas(...)`** — content declares where it can land; host enforces.
- **WinForms DockPanel Suite** — `DockContent.DockAreas` flags + `ShowHint = DockState.Document` for default placement.
- **IntelliJ Platform** — `ToolWindowAnchor` declared at registration; editor area is a separate subsystem entirely.

This composes cleanly with §4.1 and is worth adopting alongside typed groups regardless.

### 4.3 Other shapes we considered but did not adopt

- **Named slots / part-placeholders** (Eclipse e4, VS Code view containers, NetBeans `Mode`) — most expressive, but a bigger redesign than the current pain warrants.
- **Predicate-based drop targeting** (GoldenLayout, FlexLayout `onTabDrag`) — useful escape hatch, but rarely the primary mechanism. Function-valued props don't serialize cleanly.
- **Strategy + low-level primitives only** — fine for power users; makes the common case feel like a chore.

See §5 for the full decision matrix.

## 5. Considered alternatives

Four options were sketched before settling on the recommendation. Recorded here so future readers see what was rejected and why.

### 5.1 Option A — Role-tagged groups + content categories *(chosen)*

`DockGroupRole` enum on `DockTabGroup`; existing `Document` / `ToolWindow` subclasses supply the category. Router prefers role-matched groups for `Dock(Center)`. `Role.DocumentArea` implies reserved.

- **Pro:** smallest mental-model jump, persistable as enum, matches what porters from AvalonDock expect.
- **Con:** closed enum forces VS-shaped vocabulary. `Custom` + `RoleTag` would be the escape hatch if needed; punted to a follow-up.

### 5.2 Option B — Named regions

Layout declares typed, named regions (`new DockRegion("documents", Accepts: Docs, IsDefault: true, …)`). Content references regions by name.

- **Pro:** first-class document-area concept, deterministic Reset Layout (skeleton is data).
- **Con:** introduces a new node type alongside `DockTabGroup`; two ways to author layouts. Bigger redesign than the current pain warrants.

### 5.3 Option C — Acceptance predicates + placement scoring

`Func<DockableContent, bool>? Accepts` + `Func<DockableContent, int>? PlacementScore` on `DockTabGroup`.

- **Pro:** maximum expressivity with a tiny vocabulary.
- **Con:** function-valued props don't serialize — must be re-attached on each mount. Wrong primitive to lead with; right primitive to add later as an escape hatch.

### 5.4 Option D — Strategy-only + public targeting primitives

Expose `Dock(content, DockTabGroup, DockTarget)` publicly; ship a `VisualStudioLayoutStrategy` that wires VS behavior on top.

- **Pro:** keeps platform concept count low.
- **Con:** drag-drop enforcement still needs a platform hook (a strategy can't intercept overlay targeting). Most apps want the same policy; making it opt-in feels like exporting a chore.

### 5.5 Decision matrix

| | Default placement | Reserved space | Type enforcement | JSON round-trip |
|---|---|---|---|---|
| **A** Roles+Categories | Role↔Category match | `Role.DocumentArea` implies | Role/category matrix | ✓ |
| **B** Named Regions | `IsDefault` per region | Region `Reserved` | Region `Accepts` | ✓ |
| **C** Predicates | `PlacementScore` | `Reserved` flag | `Accepts` func | partial |
| **D** Strategy + primitives | App's strategy | Strategy + `ShowWhenEmpty` | Strategy + new drag hook | n/a |

Option A wins on the bottom row (JSON round-trip) and the "no app code required for the common case" axis. The targeting primitive from D is included alongside A as the explicit-control hatch (§6.4).

## 6. Recommended design

### 6.1 `DockGroupRole`

```csharp
namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Categorizes a <see cref="DockTabGroup"/> for routing and reserved-empty
/// behavior. Default <see cref="General"/> preserves pre-046 semantics.
/// </summary>
public enum DockGroupRole
{
    /// <summary>Untyped group. Today's behavior — accepts any content,
    /// removed from the layout when empty.</summary>
    General,

    /// <summary>The document well. Preferred target for
    /// <see cref="Document"/> inserts via <c>Dock(Center)</c>; rejects
    /// <see cref="ToolWindow"/> drops by default. Survives empty
    /// (implicit <c>ShowWhenEmpty = true</c>; exempt from cull).</summary>
    DocumentArea,

    /// <summary>An edge strip of tool windows. Rejects <see cref="Document"/>
    /// drops by default; routes tool windows here when their
    /// <see cref="ToolWindow.AllowedSides"/> matches the host's resolved
    /// side.</summary>
    ToolWindowStrip,
}
```

`DockTabGroup` gains:

```csharp
public sealed record DockTabGroup(
    IReadOnlyList<DockableContent> Documents,
    TabPosition TabPosition = TabPosition.Top,
    bool CompactTabs = false,
    bool ShowWhenEmpty = false,
    int SelectedIndex = -1,
    double? Width = null,
    double? Height = null,
    TabChrome TabChrome = TabChrome.Win11,
    DockGroupRole Role = DockGroupRole.General) : DockNode;
```

### 6.2 `ToolWindow.AllowedSides`

```csharp
[Flags]
public enum DockSides
{
    None   = 0,
    Left   = 1 << 0,
    Top    = 1 << 1,
    Right  = 1 << 2,
    Bottom = 1 << 3,
    All    = Left | Top | Right | Bottom,
}

public sealed record ToolWindow : DockableContent
{
    // … existing fields …

    /// <summary>
    /// Edges this tool window may dock to. Affects drag-drop drop-target
    /// eligibility and programmatic <see cref="DockHostModel.PinToSide"/>.
    /// Default <see cref="DockSides.All"/> preserves today's behavior.
    /// </summary>
    public DockSides AllowedSides { get; init; } = DockSides.All;
}
```

Documents are not edge-constrained, so the mask only lives on `ToolWindow`.

### 6.3 Routing rule

Replace `DockLayoutMutator.AddAsTab`'s "always recurse into `Children[0]`" with a role-aware search:

```pseudocode
AddAsTab(root, pane):
  category = CategoryOf(pane)   // Document | ToolWindow | DockableContent
  return InsertInto(root, pane, category)

InsertInto(node, pane, category):
  match node:
    case DockTabGroup g:
      if AcceptsCategory(g.Role, category): append pane to g
      else: return null         // signal "not me"
    case DockSplit s:
      // First pass: prefer role-matched group anywhere in the subtree
      for child in s.Children:
        if PreferredFor(child, category):
          recurse → return updated s with child replaced
      // Second pass: first group that accepts the category
      for child in s.Children:
        result = InsertInto(child, pane, category)
        if result != null: return updated s
      return null
    case DockableContent leaf:
      wrap leaf+pane as new DockTabGroup
```

Acceptance table:

| Pane category    | `General` | `DocumentArea` | `ToolWindowStrip` |
|------------------|-----------|----------------|--------------------|
| `Document`       | ✓         | ✓ (preferred)  | ✗                  |
| `ToolWindow`     | ✓         | ✗              | ✓ (preferred)      |
| Base `DockableContent` | ✓   | ✓              | ✓                  |

Untyped `DockableContent` accepts everywhere for back-compat (P1 callers).

If no group accepts (e.g., document insert into a layout with only `ToolWindowStrip` groups), fall back to today's behavior: append to the leftmost group. Emit a `DockOperationLog` diagnostic noting the fallback.

### 6.4 Group-targeted `Dock` overload

Expose what's already internal:

```csharp
public partial class DockHostModel
{
    /// <summary>
    /// Insert <paramref name="content"/> into <paramref name="targetGroup"/>
    /// at the given target. The group reference must come from the current
    /// layout snapshot (e.g. captured at layout-build time or resolved
    /// from <see cref="Layout"/>).
    /// </summary>
    public void Dock(DockableContent content, DockTabGroup targetGroup,
                     DockTarget target = DockTarget.Center) { … }
}
```

Implementation queues a new `PendingMutation.DockToGroupOp` that dispatches to the existing `DockLayoutMutator.MovePaneToGroupTarget` / `InsertPaneIntoGroup`. Same identity protocol as the drag-drop path.

This is the explicit-control escape hatch for cases the role/category routing can't express.

### 6.5 Reserved-empty semantics

`Role.DocumentArea` implies the existing `ShowWhenEmpty = true` behavior *and* exempts the group from the post-close cull pass — but only enough exemption to guarantee that **at most one** DocumentArea group always remains in the tree as the reserved well. Two cases:

1. **Author sets `ShowWhenEmpty = true` explicitly.** Unchanged today's behavior; group survives empty.
2. **Author sets `Role = DocumentArea` but leaves `ShowWhenEmpty = false`.** Effective `ShowWhenEmpty` is treated as `true` for cull purposes. Persisted JSON reflects what the author wrote (no normalization at serialize time).

The cull pass (DockLayoutMutator's "remove empty groups" sweep — single call site to identify during implementation) gets one additional check: `if (group.Role == DockGroupRole.DocumentArea) skip`. After the recursive remove, a post-pass `PruneRedundantEmptyDocumentAreas` walks the tree once: if more than one empty `DocumentArea` group exists, the first (tree-order) is preserved and the rest cull.

**Why the "first wins" prune rule.** Split-on-drag (§2.3) creates new sibling `DocumentArea` groups when the user drags a `Document` to a Split* edge of an existing `DocumentArea`. Without the prune pass, closing all documents in BOTH sibling groups would leave two empty wells stacked next to each other — visually noisy, structurally meaningless. The prune rule is local to "empty DocumentArea groups that are redundant"; non-empty wells are never culled, regardless of count. The "first" preference matches reading order so the user's mental model of "the well I had first" survives.

This is a refinement of the literal spec text — discovered during implementation when the unrefined rule produced empty-well clutter in the dock-showcase Scene J. Apps that need every DocumentArea preserved unconditionally should set `ShowWhenEmpty = true` explicitly (case 1 above) — explicit `ShowWhenEmpty` bypasses the prune pass.

### 6.6 Drag-drop overlay filtering

Drop-target rendering (`DockDropTargetOverlayElement` + caller sites in `DockHostNativeComponent`) gets a single filter point:

```csharp
bool CanDropInto(DockTabGroup target, DockableContent payload)
{
    var category = CategoryOf(payload);
    if (!AcceptsCategory(target.Role, category)) return false;
    if (payload is ToolWindow tw && target.ResolvedSide is DockSide s
        && !tw.AllowedSides.HasFlag(s.ToFlag())) return false;
    return true;
}
```

Filtered drop targets render with the existing disabled/dimmed adornment style — no new visual treatment needed. Hit-testing skips them.

### 6.7 Persistence

`DockLayoutJson` schema additions:

- `DockTabGroup.role` — string, one of `"general" | "documentArea" | "toolWindowStrip"`. Omitted from JSON when `General` (the default).
- `ToolWindow.allowedSides` — array of strings, e.g. `["left", "right"]`. Omitted when `All`.

Old layouts deserialize unchanged (omitted fields → defaults). No migration entry needed in `DockLayoutMigrationRegistry`; this is a purely additive read-side change.

### 6.8 Authoring example

```csharp
var layout = new DockSplit(
    Orientation.Horizontal,
    new DockNode[]
    {
        new DockTabGroup(
            new[] { galleryItemsToolWindow },
            Width: 260,
            Role: DockGroupRole.ToolWindowStrip),
        new DockTabGroup(
            Array.Empty<DockableContent>(),
            Role: DockGroupRole.DocumentArea),  // implies ShowWhenEmpty
        new DockTabGroup(
            new[] { configurationToolWindow },
            Width: 320,
            Role: DockGroupRole.ToolWindowStrip),
    });

// Programmatic open lands in the document area:
model.Dock(new Document { Title = "Mesh Viewer", Key = "doc:meshviewer" },
           DockTarget.Center);
```

A tool window with `AllowedSides = DockSides.Bottom`:

```csharp
var errors = new ToolWindow {
    Title = "Errors", Key = "tool:errors",
    AllowedSides = DockSides.Bottom
};
```

— users can only drag it to the bottom strip; other drop targets dim during drag.

## 7. Migration & back-compat

- **Default behavior unchanged.** `Role = General` and `AllowedSides = All` mean callers who don't opt in see today's routing.
- **`Document` / `ToolWindow` subclasses already exist** (spec 045 §5.3.1). Category detection in the router uses `pane is Document` / `pane is ToolWindow`; base `DockableContent` is treated as unconstrained.
- **No JSON migration.** Old layout JSON deserializes with default values for the new fields.
- **No public API removal.** All additions are additive.
- **Selftest impact.** The dock host selftest fixture should be re-evaluated to confirm no fixture depends on the leftmost-descendant routing accidentally. If a fixture does, fix it to use the explicit group-target overload.

## 8. Implementation plan

Estimated total: **5–7 days of focused work**.

### Phase 0 — types (½ day)

- [ ] Add `DockGroupRole` enum (`src/Reactor/Docking/Enums.cs` or co-locate with `DockNode.cs`).
- [ ] Add `DockSides` `[Flags]` enum.
- [ ] Add `Role` to `DockTabGroup` record.
- [ ] Add `AllowedSides` to `ToolWindow` record.
- [ ] Build clean. No behavior change yet.

### Phase 1 — routing (½ day)

- [ ] Rewrite `DockLayoutMutator.AddAsTab` per §6.3.
- [ ] Add `CategoryOf(DockableContent)` and `AcceptsCategory(Role, category)` private helpers.
- [ ] Add diagnostic log on the no-acceptor fallback path.

### Phase 2 — reserved-empty (½ day)

- [ ] Locate the post-close cull pass (grep for empty-group removal in `DockLayoutMutator` / `DockHostModel`).
- [ ] Skip cull when `Role == DocumentArea`.
- [ ] Confirm `ShowWhenEmpty` rendering already handles the visual case; no renderer change should be needed.

### Phase 3 — public group-target overload (½ day)

- [ ] Add `DockHostModel.Dock(content, DockTabGroup, DockTarget)`.
- [ ] Plumb to a new `PendingMutation.DockToGroupOp`.
- [ ] Reconciler dispatches to existing `MovePaneToGroupTarget` / `InsertPaneIntoGroup`.

### Phase 4 — drag-drop overlay filter (1–2 days, trickiest)

- [ ] Add `CanDropInto(group, payload)` helper (§6.6).
- [ ] Call from `DockDropTargetOverlayElement` adornment rendering — filtered targets use the existing disabled style.
- [ ] Call from `DockHostNativeComponent` drop hit-testing — filtered targets ignore the release.
- [ ] Manual verify on the gallery sample (drag a tool window across a `DocumentArea` group; drag a document across a `ToolWindowStrip`; drag a `Bottom`-only tool window across a `Left` strip).

### Phase 5 — persistence (½ day)

- [ ] Add `role` field to `DockLayoutJson` writer (omit when `General`).
- [ ] Add `allowedSides` array to `ToolWindow` JSON shape (omit when `All`).
- [ ] Reader populates defaults when fields absent.
- [ ] Round-trip test: layout → JSON → layout, deep equal.

### Phase 6 — tests (1 day)

- [ ] Unit: extend `DockLayoutMutator` tests with role-aware routing cases (the leftmost-descendant repro from §2.1, plus the inverse with tool-window-only strips, plus the fallback path).
- [ ] Unit: `AllowedSides` enforcement (PinToSide reject + drop-target filter).
- [ ] Unit: cull-pass skip for `DocumentArea`.
- [ ] Unit: JSON round-trip with new fields populated and omitted.
- [ ] Selftest: visual fixture for the VS-style layout from §6.8 with a "close last document" sequence proving the center stays visible.

### Phase 7 — docs (½ day)

- [ ] Edit `docs/_pipeline/templates/docking.md.dt` (or whatever the docking topic file is — verify path before editing). **Do not hand-edit `docs/guide/`** — see `feedback_docs_pipeline.md` memory.
- [ ] Add a "Shaping a document well" subsection with the §6.8 example.
- [ ] Add a callout for `AllowedSides` in the tool-window section.
- [ ] Run `mur docs compile` to regenerate the published guide.
- [ ] Update spec 045 to cross-reference this spec from §5.3.1 (Document/ToolWindow split discussion) and §6.4 (algebra extensions).

### Implementation task list

A detailed task file lives at `docs/specs/tasks/046-docking-content-types-implementation.md` (created alongside the implementation branch; not authored as part of this spec draft).

## 9. Open questions

- **Q1.** Should `Role.ToolWindowStrip` *reject* documents, or just deprioritize them? Current §6.3 rejects. Rejection makes drag-drop feedback clearer ("you can't drop a doc here"), but means a layout with no `General` or `DocumentArea` group can't accept programmatic documents at all — they'd hit the fallback path and log. **Tentative answer: reject, with a fallback that emits a clear diagnostic.** Revisit if the diagnostic becomes a frequent customer complaint.

- **Q2.** Should we add `Role.Custom` + `RoleTag : object?` now, or wait? Adding now costs a property and a default-value branch in the router; not adding it means apps with non-VS taxonomies have to use the group-target overload (§6.4). **Tentative answer: wait.** The escape hatch exists; add `Custom` if a real scenario asks for it.

- **Q3.** When a layout strategy returns `true` from `BeforeInsertDocument` (signaling it handled placement), should we still validate against role compatibility, or trust the strategy? **Tentative answer: trust.** A strategy that opts in is taking responsibility for its choice; second-guessing it defeats the hook.

- **Q4.** `AllowedSides` on tool windows — does it apply to *programmatic* `PinToSide` calls too, or only drag-drop? **Tentative answer: both,** with `PinToSide` throwing on an invalid combination. Strategies that need to bypass can clear the mask before calling.

- **Q5.** Should `DockGroupRole.DocumentArea` *also* set a default `MinWidth` so the empty document well has a sensible visible footprint? Or leave sizing to the author? **Tentative answer: leave to the author.** Adding implicit sizing surprises authors; the explicit `Width` / `MinWidth` props on `DockTabGroup` are the right place.

## 10. Out of scope

- **OS1.** A predicate `Accepts` on `DockTabGroup`. May land in a future spec if v1 enums prove insufficient.
- **OS2.** Named-region authoring (Eclipse e4-style). Larger redesign; not motivated by current scenarios.
- **OS3.** First-party `VisualStudioLayoutStrategy` class. The role+mask vocabulary handles the common case; a starter strategy can ship later.
- **OS4.** Restructuring `Document` / `ToolWindow` into separate `DockNode` types. Spec 045 chose category-on-content; this spec extends that choice rather than reopening it.
- **OS5.** Cross-app drag-drop policy. Spec 045 N3 already excludes cross-process docking.
- **OS6.** New devtools introspection for role/category. Static layout introspection (spec 045 §8.2) already exposes the full tree; the new fields will appear there automatically once serialized.
