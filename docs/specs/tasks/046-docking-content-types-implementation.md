# Docking Content Types & Reserved Document Area — Implementation Tasks

Derived from: [`docs/specs/046-docking-content-types-and-reserved-area.md`](../046-docking-content-types-and-reserved-area.md)

Scope reminder: an additive P3 amendment to [spec 045](../045-docking-windows-design.md) per §6.4 of that spec. Adds (a) `DockGroupRole` tag on `DockTabGroup`, (b) `DockSides` mask on `ToolWindow.AllowedSides`, (c) role-aware routing in `DockLayoutMutator`, (d) reserved-empty semantics for `DocumentArea` groups, (e) a public group-targeted `Dock` overload, (f) drag-drop drop-target filtering, (g) JSON round-trip for the new fields. No breaking change — default `Role = General` / `AllowedSides = All` preserves today's behavior.

Source references (read these before starting each phase):
- `src/Reactor/Docking/DockNode.cs` — `DockTabGroup`, `DockableContent`, `Document`, `ToolWindow` records.
- `src/Reactor/Docking/Native/DockLayoutMutator.cs` — `AddAsTab` (line ~336, the bug), `RemoveInner` (line ~242, the cull pass), `InsertPaneAtTarget` (line ~306, split/edge insertion), `WrapAsGroup` (line ~333, role-propagation gap), `MovePaneToGroupTarget` / `InsertPaneIntoGroup` (group-target primitives we're exposing).
- `src/Reactor/Docking/Native/DockHostNativeComponent.cs` — drag-drop overlay caller (~line 542), drop hit-testing.
- `src/Reactor/Docking/Native/DockDropTargetOverlayElement.cs` — drop target adornment rendering.
- `src/Reactor/Docking/Native/DockFloatingPaneRouter.cs` — cross-window dock-in path; role implications on tear-out / drop-back.
- `src/Reactor/Docking/Native/DockFloatingWindow.cs` — floating-window pane state; role of the internal group.
- `src/Reactor/Docking/Persistence/DockLayoutJson.cs` — JSON schema; role / allowedSides round-trip.
- `src/Reactor/Docking/Persistence/DockLayoutSerializer.cs` — read/write of new fields.
- `src/Reactor/Docking/IDockLayoutStrategy.cs` — strategy hooks; role-validation interaction.
- `src/Reactor/Docking/DockHostModel.cs` — public mutator surface; new `Dock(content, group, target)` overload.
- `src/Reactor/Docking/Persistence/DockLayoutMigrationRegistry.cs` — migration entries (none expected; verify).
- `src/Reactor/Docking/Diagnostics/DockOperationLog.cs` — diagnostic event types; new fallback event.
- Feedback that motivated this work: `C:\Users\andersonch\Code\pix\winui-port\feedback\reactor-docking-center-targets-leftmost-group.md`.

Conventions:
- Same as [spec 045 tasks](045-docking-windows-implementation.md) — DIPs on the public surface, UI-thread-affined mutations, zero-alloc on pointer-move paths, invariant-culture persistence.
- All public additions get XML docs with `<remarks>` linking to spec 046 § number.
- Tests:
  - Unit: `tests/Reactor.Tests/Docking/`. New file `RoleAwareRoutingTests.cs` for §6.3 routing matrix; extend `DockLayoutMutatorTests.cs` for the cull-pass changes and the public group-target overload.
  - Selftest: `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Docking046_*`. Visual fixtures for the VS-style layout, the empty-document-well, and the floating round-trip.
  - Allocation: extend the docking allocation harness if it exists for drag-over hot path; otherwise note as deferred.
  - JSON round-trip: extend `DockLayoutJsonTests.cs`.
- A task is **done** when:
  1. Code compiles under `Reactor.sln` warnings-as-errors.
  2. Tests cover happy path + every documented failure mode in this file's §Scenarios section.
  3. Public API has XML docs (no CS1591).
  4. No new analyzer warnings.
  5. Selftest fixture renders correctly under Light / Dark / NightSky at 100% / 200% scaling.
  6. CHANGELOG entry appended under `## [Unreleased]` → "Spec 046 — Docking content types".

Branch convention: `feat/046-docking-content-types`. One feature branch, can be one PR (small enough) or split phase-0+1 / phase-2-4 / phase-5-7 if review load demands.

---

## Phase 0 — Cross-cutting setup

### 0.1 Tracking & docs

- [ ] Create this tracking file at `docs/specs/tasks/046-docking-content-types-implementation.md` (this file). Update as tasks land.
- [ ] Add a `Spec 046 — Docking content types` entry under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] Cut branch `feat/046-docking-content-types` off current `main`.

### 0.2 Cross-reference from spec 045

- [ ] Edit `docs/specs/045-docking-windows-design.md` — add a "see also" reference in §5.3.1 (Document/ToolWindow split) and §6.4 (algebra extensions) pointing to spec 046.

---

## Phase 1 — Types

### 1.1 `DockGroupRole` enum

- [ ] Add `DockGroupRole` enum to `src/Reactor/Docking/Enums.cs` with values `General`, `DocumentArea`, `ToolWindowStrip`. XML doc per spec 046 §6.1.
- [ ] Add `Role` property (default `General`) to `DockTabGroup` record in `src/Reactor/Docking/DockNode.cs`. Positional parameter at the end of the constructor to preserve source compat for callers using positional syntax for earlier params.

**Tests:**

- [ ] `DockGroupRoleTests.cs` — record equality unaffected by default vs. explicit `General`; record `with`-expression preserves role.
- [ ] `DockTabGroup` source-compat: existing constructors that omit `Role` still compile and default to `General`.

### 1.2 `DockSides` flags enum

- [ ] Add `[Flags] DockSides` enum to `src/Reactor/Docking/Enums.cs` with `None`, `Left`, `Top`, `Right`, `Bottom`, `All`. XML doc per spec 046 §6.2.
- [ ] Add `AllowedSides` property (default `DockSides.All`) to `ToolWindow` record. Init-only.
- [ ] Add `ToFlag` extension: `DockSide → DockSides` for use in §6.6 drop filter.

**Tests:**

- [ ] `DockSidesTests.cs` — flag combinations (`Left | Right`), `HasFlag` semantics, `DockSide.ToFlag()` round-trip for all four sides.
- [ ] `ToolWindow` source-compat: existing `ToolWindow` constructions still compile; default `AllowedSides == DockSides.All`.

### 1.3 Build clean

- [ ] `dotnet build` clean with no warnings.
- [ ] Run existing docking unit tests — all green (no behavior change yet).

---

## Phase 2 — Routing

### 2.1 Category detection

- [ ] Add private helper in `DockLayoutMutator`: `CategoryOf(DockableContent) → enum DockContentCategory { Document, ToolWindow, Untyped }`. Uses pattern match on subclass; base `DockableContent` → `Untyped`.
- [ ] Add private helper `AcceptsCategory(DockGroupRole, DockContentCategory) → bool` implementing the acceptance table from spec 046 §6.3.
- [ ] Add private helper `PreferredFor(DockGroupRole, DockContentCategory) → bool` for first-pass preference (Document↔DocumentArea, ToolWindow↔ToolWindowStrip).

**Tests:**

- [ ] `CategoryOfTests` — `Document` → `Document`, `ToolWindow` → `ToolWindow`, raw `DockableContent` → `Untyped`. Subclasses of `Document` (if any) → `Document`.
- [ ] `AcceptsCategoryTests` — full 3×3 matrix with table from spec §6.3.

### 2.2 Rewrite `AddAsTab` (the leftmost-descendant bug)

- [ ] Replace `AddAsTab` (current line ~336) with the two-pass search from spec §6.3:
  1. First pass: find descendant group where `PreferredFor(group.Role, category) == true`. If found, insert.
  2. Second pass: find first descendant group where `AcceptsCategory(group.Role, category) == true`. If found, insert.
  3. Fallback: today's "first child of root" behavior, with a `DockOperationLog` diagnostic emitted.
- [ ] When the recursive insert returns `null` (no acceptor in subtree), propagate `null` so the caller can try a sibling.
- [ ] When wrapping a bare `DockableContent` leaf into a new tab group on insert, the new wrapper group's role should be inferred from the leaf's category: a lone `Document` → `DocumentArea`, a lone `ToolWindow` → `General` (don't auto-create strips). Rationale: surprising auto-promotion to `ToolWindowStrip` would change layout behavior; auto-promoting to `DocumentArea` only fires when the user is already adding to a doc-only context.

**Tests** in new `tests/Reactor.Tests/Docking/RoleAwareRoutingTests.cs`:

- [ ] **Repro of feedback bug.** Layout = `Split[ Group(Tool "Gallery"), Group(empty), Group(Tool "Config") ]`. `InsertPaneAtTarget(layout, Document, Center)` lands in the middle group, NOT the first.
- [ ] **Tool window into VS layout.** Same layout, insert a `ToolWindow` with `Center` → lands in the first `General` group (the empty middle), since no `ToolWindowStrip` exists. Diagnostic logged for fallback.
- [ ] **DocumentArea preferred over General.** Layout = `Split[ Group(empty, role=General), Group(empty, role=DocumentArea), Group(empty, role=General) ]`. Insert Document → middle group.
- [ ] **First DocumentArea wins.** Two DocumentArea groups in the layout — insert lands in the first (tree order).
- [ ] **ToolWindowStrip rejects documents.** Layout = `Split[ Group(role=ToolWindowStrip), Group(role=DocumentArea) ]`. Insert Document → DocumentArea (preferred). Insert Document when only `ToolWindowStrip` exists → fallback to first group + diagnostic.
- [ ] **DocumentArea rejects tool windows.** Insert ToolWindow when only `DocumentArea` exists → fallback to first group + diagnostic.
- [ ] **Nested splits.** `Split[ Group(role=ToolWindowStrip), Split[ Group(role=DocumentArea), Group(role=General) ] ]`. Insert Document → nested DocumentArea.
- [ ] **Untyped DockableContent.** Raw `DockableContent` (not `Document` / `ToolWindow`) goes to first accepting group — back-compat for P1 callers.
- [ ] **Empty root.** `InsertPaneAtTarget(null, …)` wraps to a tab group; role inferred per §2.2 wrapping rule.
- [ ] **Single DockableContent leaf root.** Wraps leaf+new pane into a tab group; role derives from leaf category.

### 2.3 Split / edge target role propagation

- [ ] `InsertPaneAtTarget`'s `SplitLeft/Right/Top/Bottom` and `DockLeft/Right/Top/Bottom` cases call `WrapAsGroup`. Add an overload `WrapAsGroup(pane, role)` and pass an inferred role: a `Document` payload → `DocumentArea`, a `ToolWindow` → `ToolWindowStrip` (for `Dock*` edges) or `General` (for `Split*` within a doc area), Untyped → `General`.
- [ ] **Critical case:** splitting a Document inside a DocumentArea must produce a new `DocumentArea` group, not a `General` group. The hit-test path needs to know the *target* group's role and propagate it to the new sibling.

**Tests:**

- [ ] **Splitting a Document inside DocumentArea.** Layout has a `DocumentArea` group with two docs; user drags one to the right edge → the new sibling group is also `DocumentArea`. Subsequent `Dock(doc, Center)` still routes correctly.
- [ ] **Splitting a Document at the root.** Root is a DocumentArea group; `InsertPaneAtTarget(root, doc2, SplitRight)` produces `Split[ DocumentArea, DocumentArea ]`.
- [ ] **Dropping a ToolWindow on the bottom edge** of a layout creates a new `ToolWindowStrip` group, not `General`. (Verifies the user-requested "create new tool areas when there are none" case.)
- [ ] **Mixed split.** Splitting a Document via SplitLeft against a DocumentArea root creates `Split[ DocumentArea, DocumentArea ]`. Splitting a ToolWindow via DockBottom against the same root creates `Split[ DocumentArea, ToolWindowStrip ]`.

---

## Phase 3 — Reserved-empty semantics

### 3.1 Exempt DocumentArea from cull

- [ ] In `RemoveInner` (line ~242), `DockTabGroup` case where `docs.Count == 1` and the removed pane was the last: return `(node with { Documents = empty }, true)` instead of `(null, true)` when `group.Role == DockGroupRole.DocumentArea`.
- [ ] In `RemoveInner` `DockSplit` case, when collapsing zero-children → null, skip if the only emptied child was a DocumentArea group (which is now retained as an empty node).
- [ ] When `keep == 1` collapses a split to its lone child, the parent does its normal collapse; DocumentArea simply means *this group survives empty*, not *this group's split survives degenerate*.

**Tests** in `DockLayoutMutatorTests.cs`:

- [ ] **Close last document in DocumentArea.** `Split[ TWStrip(tool), DocumentArea(doc1) ]` → close doc1 → `Split[ TWStrip(tool), DocumentArea(empty) ]`. NOT collapsed to `TWStrip(tool)`.
- [ ] **Close last document in General group.** `Split[ TWStrip(tool), General(doc1) ]` → close doc1 → `TWStrip(tool)` (collapsed as today).
- [ ] **Close last document in DocumentArea, then reopen.** After the close, `Dock(doc2, Center)` lands in the surviving DocumentArea — round-trip works.
- [ ] **Nested cull.** `Split[ Split[ DocumentArea(empty), General(doc) ], TWStrip(tool) ]` — closing the General doc collapses the inner split to `DocumentArea(empty)`, outer split becomes `Split[ DocumentArea(empty), TWStrip(tool) ]`. DocumentArea remains.

### 3.2 Renderer behavior for empty DocumentArea

- [ ] Verify `DockTabGroupRenderer` already handles `Documents.Count == 0` rendering when `ShowWhenEmpty = true`. If it does, ensure `Role == DocumentArea` triggers the same path even when `ShowWhenEmpty = false` literally.
- [ ] Empty DocumentArea should render a visible neutral surface (background fill, no tab strip flicker). Match what `ShowWhenEmpty = true` already does — no new visual treatment.

**Tests:**

- [ ] Selftest `Docking046_EmptyDocumentArea.cs` — VS layout with the doc area emptied, screenshot baseline confirms a visible surface (not collapsed).
- [ ] Unit: render output for `DockTabGroup { Documents = [], Role = DocumentArea, ShowWhenEmpty = false }` matches `{ Documents = [], ShowWhenEmpty = true, Role = General }` (i.e., role-implies-visible).

---

## Phase 4 — Public group-target overload

### 4.1 API addition

- [ ] Add to `DockHostModel`: `public void Dock(DockableContent content, DockTabGroup targetGroup, DockTarget target = DockTarget.Center)`. XML doc with §6.4 link.
- [ ] Add corresponding `PendingMutation.DockToGroupOp` (mirror existing op records).
- [ ] Reconciler dispatch routes to existing `DockLayoutMutator.MovePaneToGroupTarget` / `InsertPaneIntoGroup`.
- [ ] Group resolution: caller passes the group from the current `Layout`; resolver matches by `ReferenceEquals` first, then by structural equality (record equality) as a fallback. If unresolved, log a `DockOperationLog` warning and no-op (don't throw — matches the model's current "best-effort" feel).

### 4.2 Strategy hook interaction

- [ ] `IDockLayoutStrategy.BeforeInsertDocument` / `BeforeInsertToolWindow` — when the strategy uses the new overload to place into a specific group, the strategy returns `true` and the default routing skips. Verify no double-insert.
- [ ] Document in §6.4 XML remarks: strategies that target a group via this overload are *trusted* — no role compatibility re-check fires. (Per spec §9 Q3.)

**Tests:**

- [ ] **Group resolution by reference.** Capture a `DockTabGroup` from `model.Layout`, mutate via other paths so the model's effective layout reuses the same record — `Dock(content, group, Center)` resolves correctly.
- [ ] **Group resolution by structural equality.** Build a fresh `DockTabGroup` record with the same shape as one in the layout (matching keys / position) — verify resolution still works.
- [ ] **Unresolvable group.** Pass a group that isn't in the layout — operation no-ops, diagnostic logged. No throw.
- [ ] **Trust the strategy.** Strategy places a Document into a `ToolWindowStrip` via the overload and returns `true` — no role check, no warning, document lands as requested.
- [ ] **Round-trip: capture → close → re-Dock.** Capture group ref before closing all docs in it (`Role = DocumentArea` keeps it alive), then `Dock(doc, group, Center)` succeeds.

---

## Phase 5 — Drag-drop overlay filtering

### 5.1 `CanDropInto` filter

- [ ] Add internal helper `DockLayoutMutator.CanDropInto(DockTabGroup target, DockableContent payload, DockSide? targetSide)`:
  - If `!AcceptsCategory(target.Role, CategoryOf(payload))` → `false`.
  - If `payload is ToolWindow tw && targetSide is DockSide s && !tw.AllowedSides.HasFlag(s.ToFlag())` → `false`.
  - Else → `true`.
- [ ] Plumb `targetSide` from the drag pipeline (overlay knows which edge it's painting for; pass through).

### 5.2 Drop-target adornment filtering

- [ ] `DockDropTargetOverlayElement` — before rendering each drop-target button, call `CanDropInto`. If `false`, render with the existing disabled/dimmed style; hit-test ignores the disabled targets.
- [ ] `DockHostNativeComponent` drop hit-testing (~line 542) — same filter applies. A drop on a filtered target is a no-op (snap-back, like dropping outside any target).

### 5.3 Drop on empty DocumentArea

- [ ] **Special case:** dropping a `Document` on an empty `DocumentArea` (no existing docs to overlay onto) — the group's Center target must remain reachable. Verify the overlay renders Center even when the group is empty.
- [ ] **Special case:** dropping a `ToolWindow` on an empty `DocumentArea` — Center target is filtered out (rejected). Edge targets (SplitLeft/etc.) on the *outer* layout remain available so the user can still pin the tool to the side via the layout-root overlay.

**Tests** in `DockDragDropFilterTests.cs` (new):

- [ ] **Document over ToolWindowStrip — Center filtered.** Drop is no-op, drag returns to source.
- [ ] **Document over DocumentArea — Center allowed.** Drop lands.
- [ ] **ToolWindow with AllowedSides=Bottom over Left strip — filtered.** Drop is no-op.
- [ ] **ToolWindow with AllowedSides=All over any strip — allowed.**
- [ ] **Document over empty DocumentArea — Center allowed, lands cleanly** (no overlay flicker on empty group).
- [ ] **ToolWindow over empty DocumentArea — Center filtered, layout-root edge targets still allowed.**
- [ ] **Mixed payload (untyped DockableContent) — accepted anywhere** (back-compat).
- [ ] **AllowedSides interaction with RTL.** A tool window pinned to `AllowedSides=Left` in RTL mode — the visual right edge is the logical Left. Drop filter uses logical sides (per spec 045 §8.8); test asserts logical mapping, not visual.

### 5.4 Programmatic `PinToSide` validation

- [ ] `DockHostModel.PinToSide(ToolWindow tw, DockSide side)` — throw `InvalidOperationException` if `!tw.AllowedSides.HasFlag(side.ToFlag())`. (Per spec §9 Q4.)
- [ ] Message includes the tool window's title/key and the mask, for diagnostics.
- [ ] Strategies that need to bypass clear the mask (mutate `AllowedSides`) before calling — document in XML remarks.

**Tests:**

- [ ] **PinToSide honors mask.** ToolWindow with `AllowedSides=Bottom` — `PinToSide(tw, DockSide.Left)` throws; `PinToSide(tw, DockSide.Bottom)` succeeds.
- [ ] **PinToSide default unconstrained.** Default `AllowedSides=All` — every side pins.
- [ ] **PinToSide creates a new strip if none exists.** Verify the new strip group has `Role = ToolWindowStrip`.

### 5.5 Allocation harness

- [ ] If a drag-over allocation harness exists for spec 045, extend it to cover the filter call path. Pointer-move during drag must remain zero-alloc. If no harness exists, file a follow-up tracking issue, do not block the spec.

---

## Phase 6 — Floating windows

This phase deserves its own attention because tear-out and drop-back interact with roles in non-obvious ways.

### 6.1 Tear-out — what role does the floating-window's internal group get?

- [ ] When `DockFloatingWindow` opens with a torn-out pane, the floating-window's internal `DockTabGroup` inherits a role from the source pane's category: `Document` → `DocumentArea`, `ToolWindow` → `General` (tools floating don't need strip semantics), Untyped → `General`.
- [ ] Floating window's internal group is *not* `ToolWindowStrip` — strips imply edge attachment, which floating windows don't have.

**Tests** in `DockFloatingWindowRoleTests.cs` (new):

- [ ] **Tear out a Document from DocumentArea.** Floating window's internal group has `Role = DocumentArea`. Closing the doc inside the floating window closes the floating window (DocumentArea reserved-empty does NOT apply to floating windows — they collapse when empty).
- [ ] **Tear out a ToolWindow.** Floating window's internal group has `Role = General`.
- [ ] **Floating-window reserved-empty exemption.** When a floating window's only doc closes, the window closes — the reserved-empty rule applies to in-layout DocumentArea groups, not to floating ones. (Confirm this is the desired behavior; alternative is "keep floating window alive with empty doc area", which we explicitly reject.)

### 6.2 Drop-back — cross-window dock-in respects role

- [ ] `DockFloatingPaneRouter.TryAppendUnderCursor` — currently does an unconditional "append as tab" to the host. Extend to call `InsertPaneAtTarget(hostRoot, pane, DockTarget.Center)` so role-aware routing applies. A floating Document dropped on a host with a DocumentArea lands in the DocumentArea, not the leftmost group.
- [ ] **Special case:** if the dropped pane is a `ToolWindow` and the host has no compatible `ToolWindowStrip`, the cross-window router should *not* create a new strip silently — that's a layout structural change the user didn't see telegraphed in the overlay. Instead, fall back to first-accepting-group + diagnostic, same as the in-window routing fallback.

**Tests:**

- [ ] **Float a Document, drag back into a VS-layout host.** Lands in DocumentArea, not leftmost tool group.
- [ ] **Float a ToolWindow, drag back into a host with no strips.** Lands in first General group with diagnostic; does NOT silently synthesize a new strip.
- [ ] **Float a ToolWindow with AllowedSides=Bottom, drag back into a host with only a Left strip.** Drop is filtered out (no valid landing); pane stays floating. Confirm the overlay had greyed the Left strip's Center target during the drag.
- [ ] **Tear out then drop on edge.** Float a Document, drag back to the host's right edge → new DocumentArea split arm created (role propagated per §2.3).

### 6.3 Persistence of floating windows

- [ ] Floating window's internal group role round-trips through layout JSON if the layout persists floating-window contents. (Verify: does spec 045 persist floating windows? Read `DockLayoutJson` schema. If yes, add role. If no, skip.)

**Tests:**

- [ ] If floating persistence exists: float a Document (DocumentArea-roled internal group), save, reload, verify role preserved.

---

## Phase 7 — Persistence

### 7.1 JSON schema additions

- [ ] `DockTabGroup` JSON: add `"role"` field. Omit when value is `General`. Values: `"general" | "documentArea" | "toolWindowStrip"`.
- [ ] `ToolWindow` JSON: add `"allowedSides"` array of strings. Omit when value is `All`. Values: `["left", "top", "right", "bottom"]` subset.
- [ ] All string values invariant-culture lowercase, matching existing convention.

### 7.2 Reader & writer

- [ ] `DockLayoutSerializer.WriteGroup` — emit `role` when not `General`.
- [ ] `DockLayoutSerializer.WriteToolWindow` — emit `allowedSides` when not `All`.
- [ ] `DockLayoutSerializer.ReadGroup` — parse `role`, default `General` if absent.
- [ ] `DockLayoutSerializer.ReadToolWindow` — parse `allowedSides`, default `All` if absent.
- [ ] Unknown role string → log warning, default to `General` (forward-compat).
- [ ] Unknown side string → log warning, ignore that entry (forward-compat).

### 7.3 Migration

- [ ] No `DockLayoutMigrationRegistry` entry expected — additive read-side change. Verify by inspection.

**Tests** in `DockLayoutJsonTests.cs` extension:

- [ ] **Round-trip: role.** Layout with all three roles → JSON → layout. Records equal.
- [ ] **Round-trip: AllowedSides.** Tool window with `Left | Bottom` → JSON → tool window. Mask equal.
- [ ] **Omitted defaults.** Layout with all defaults → JSON contains no `role` / `allowedSides` keys. Re-read produces equal layout.
- [ ] **Forward-compat: unknown role.** JSON with `"role": "futureRole"` → reads as `General`, warning logged.
- [ ] **Forward-compat: unknown side.** JSON with `"allowedSides": ["left", "diagonal"]` → reads `Left`, warning logged.
- [ ] **Old layout compat.** Existing layout JSON fixtures from spec 045 (look in `tests/Reactor.Tests/Docking/Fixtures/` if present) deserialize unchanged — defaults applied silently.

---

## Phase 8 — Tests: cross-cutting scenarios

The phase-specific test sections cover unit behavior. This section adds end-to-end and integration scenarios that span multiple phases.

### 8.1 The VS-shaped layout — happy path

- [ ] Selftest `Docking046_VSLayoutBasics.cs`:
  - Build `[ ToolWindowStrip(GalleryItems, Width=260), DocumentArea(empty), ToolWindowStrip(Config, Width=320) ]`.
  - `model.Dock(doc1, Center)` — lands in center.
  - `model.Dock(doc2, Center)` — joins center as second tab.
  - `model.Dock(doc3, Center)` — joins as third tab.
  - Close doc1, doc2, doc3 — DocumentArea survives empty.
  - `model.Dock(doc4, Center)` — lands cleanly in the surviving empty DocumentArea.
  - Pin tool windows to opposite sides; verify mask honored.

### 8.2 Multiple documents — split inside DocumentArea

- [ ] Selftest `Docking046_DocumentSplit.cs`:
  - VS layout with three docs already in DocumentArea.
  - Drag tab 3 to the right edge of the DocumentArea → new sibling DocumentArea group created (NOT General).
  - `model.Dock(doc4, Center)` — lands in the first DocumentArea (tree order), per spec §6.3 "first preferred wins".
  - Drag a doc between the two DocumentArea groups via tab-drag — both retain DocumentArea role.

### 8.3 Creating new tool areas when none exist

- [ ] Selftest `Docking046_SynthesizeToolStrip.cs`:
  - Layout = single `DocumentArea` group, no tool strips.
  - Drag a `ToolWindow` from … (where? — easiest: open a sample-app-provided ToolWindow via menu, which would call `model.PinToSide(tw, DockSide.Left)`).
  - Verify: a new `ToolWindowStrip`-roled group is created via `PinToSide` (which always synthesizes a strip when none exists — confirm this is the documented behavior; if not, add it).
  - Verify: subsequent `model.Dock(doc, Center)` still lands in the original DocumentArea (split structure preserved).
  - Drag a second tool window in via drag-drop: lands in the existing ToolWindowStrip, NOT a new one (drop on Center of the strip).
  - Drop a second tool window on the *edge* of the layout (DockBottom) — new ToolWindowStrip created at the bottom.

### 8.4 Floating round-trip

- [ ] Selftest `Docking046_FloatingRoundTrip.cs`:
  - VS layout with two docs in DocumentArea.
  - Tear out doc1 → floating window opens with internal `DocumentArea`-roled group.
  - Close doc1 inside the floating window → floating window closes (reserved-empty doesn't apply to floating).
  - Reopen via app's "open Mesh Viewer" → `Dock(doc, Center)` lands in the original DocumentArea (not regenerated leftmost).
  - Tear out doc2, drag back to right edge of host → new DocumentArea sibling created (split, both DocumentArea).

### 8.5 Reset Layout

- [ ] Selftest `Docking046_ResetLayout.cs`:
  - Build VS layout. User opens 3 docs, closes 2, drags tools to swap sides.
  - Save layout to JSON.
  - Call `DockManager.ResetLayout()` (or whatever the host's reset API is — confirm name; spec 045 §2.5 / §2.15 PreviousContainer may be relevant).
  - Verify: reset produces the original declared layout shape; roles preserved.
  - Verify: saved JSON deserialize → same effective layout.

### 8.6 Strategy interaction

- [ ] `RoleAwareStrategyTests.cs`:
  - Strategy implementing `BeforeInsertDocument` that uses the new `Dock(content, group, target)` overload to force a specific group.
  - Strategy returns `true` → default routing skipped; document lands where strategy said.
  - Strategy returns `false` → role-aware default routing fires.
  - Strategy mutates `tw.AllowedSides` before `PinToSide` to bypass the mask check; verify pin succeeds.

### 8.7 Diagnostics

- [ ] `DockOperationLogTests.cs` extension:
  - Fallback path (no acceptor for category) emits a typed event with the layout shape + payload category + chosen group.
  - Group-target overload's unresolvable-group path emits a warning event.
  - `PinToSide` mask violation throws — message contains tool key + mask + side.

### 8.8 Devtools introspection

- [ ] If devtools / MCP layout-dump exposes `DockTabGroup` (spec 045 §8.2), confirm role + AllowedSides surface in the dump. Extend the dump test fixture.
- [ ] If not, no action — additive new properties are visible via reflection-based dumps automatically.

### 8.9 Accessibility

- [ ] Drop-target adornments in filtered/disabled state must announce as such (UIA name suffix like "(not allowed)") — extend the existing UIA test for the dock overlay.
- [ ] Reserved empty DocumentArea must have a UIA name ("Document area, empty") so screen readers don't see a nameless region.

### 8.10 Edge cases

- [ ] **Empty layout.** Host with `Layout = null` or `Layout = empty split`. `Dock(doc, Center)` wraps to a new DocumentArea group (per §2.2 wrapping rule). Subsequent docs join it.
- [ ] **All-General layout (back-compat).** Existing app code that doesn't opt in — all groups default `General`, all routing falls back to today's "first child" behavior. No diagnostic spam (only the *no acceptor at all* fallback logs; "general accepts everything" is silent).
- [ ] **Single DocumentArea, no other groups.** `Dock(tool, Center)` — falls back to the DocumentArea (with diagnostic) since no strip exists. Document does the same without diagnostic.
- [ ] **Two DocumentArea groups.** `Dock(doc, Center)` lands in first (tree order). Future spec may add a "default" marker if this becomes confusing.
- [ ] **AllowedSides = None.** Currently allowed by the type system. Should we reject construction? **Decision: allow it; means the tool window can't be pinned, must be floating.** Verify `PinToSide` throws on every side.
- [ ] **Hide / Show.** `model.Hide(doc)` then `model.Show(doc)` — does Show restore to original DocumentArea via the PreviousContainer tracker (spec 045 §2.15), or run Dock(Center) routing? Confirm and test.
- [ ] **Activate when content is wrong category for current group.** E.g., `Activate(doc)` where the doc was force-placed in a `ToolWindowStrip` via the group-target overload. No role enforcement at activate time — just selects the tab. Confirm.

### 8.11 Performance

- [ ] If spec 045 has a routing/cull benchmark, extend it to include role-aware insert into a deep (10-level) split tree. Routing must remain O(n) in tree size, no quadratic blowup.
- [ ] Allocation harness for the drag-over hot path (see §5.5) — pointer-move that crosses filtered targets remains zero-alloc.

---

## Phase 9 — Docs

### 9.1 Guide updates

- [ ] **Edit `docs/_pipeline/templates/<docking-topic>.md.dt`** — verify the exact template file by inspecting `docs/_pipeline/templates/` before editing. **Do NOT hand-edit `docs/guide/*.md`** — it's generated output.
- [ ] Add subsection: "Shaping a document well" with the §6.8 authoring example from the spec.
- [ ] Add subsection: "Constraining tool window placement" covering `AllowedSides`.
- [ ] Add a callout near `Dock(target)`: "Programmatic `Dock(content, Center)` routes to the first `DockGroupRole.DocumentArea` group, falling back to the first compatible group. To target a specific group, use the `Dock(content, DockTabGroup, target)` overload."
- [ ] Run `mur docs compile` to regenerate `docs/guide/`.

### 9.2 Spec cross-reference

- [ ] Add a "See also: spec 046" line to spec 045 §5.3.1 and §6.4 (per Phase 0.2 above; restated here so it's not forgotten).

### 9.3 CHANGELOG entry

- [ ] Under `## [Unreleased]` → "Spec 046 — Docking content types":
  - Added: `DockGroupRole`, `DockSides`, `DockTabGroup.Role`, `ToolWindow.AllowedSides`, `DockHostModel.Dock(content, group, target)`.
  - Changed: `Dock(target=Center)` now prefers `DocumentArea` groups; documents lands in the document well by default in VS-style layouts. Default `Role=General` preserves prior behavior for layouts that don't opt in.

---

## Phase exit checklist

Before marking spec 046 complete:

- [ ] All Phase 0–9 task boxes checked.
- [ ] `RoleAwareRoutingTests`, `DockSidesTests`, `DockDragDropFilterTests`, `DockFloatingWindowRoleTests`, `RoleAwareStrategyTests` exist and pass.
- [ ] All Phase 8 selftest fixtures render correctly under Light/Dark/NightSky at 100%/200%.
- [ ] No new `Debug.WriteLine` calls (route diagnostics through `DockOperationLog` per spec 044 rule).
- [ ] No new analyzer warnings; `dotnet build` clean under warnings-as-errors.
- [ ] Manual smoke: open the dock gallery sample, exercise the §8.1–§8.4 scenarios by hand. Note any UX surprises in the PR description.
- [ ] Feedback file `pix/winui-port/feedback/reactor-docking-center-targets-leftmost-group.md` can be marked resolved (link this PR from there).
- [ ] PR description summarizes the spec, links to it, and calls out the back-compat story.
