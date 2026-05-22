# Specialized Control Scrub — Reactor-original controls

## Status

**Draft.** This spec runs the same three-checks-per-control audit as
[spec 039](039-property-and-event-scrub.md) against the Microsoft.UI.Reactor (Reactor)-original
controls under `src/Reactor/Controls/**`. Spec 039 deliberately scoped
these out (see 039 §13) and scheduled this follow-up; spec 039 Phase 7.1
created this draft. Outline only — no answers yet. A companion task list
(`docs/specs/tasks/040-specialized-control-scrub-implementation.md`) will
be created once the audit below is filled in; it does not yet exist.

## Goals

For every Reactor-original specialized control surface, answer the same
three questions as spec 039:

1. **Event parity.** Every callback property (constructor positional /
   init-only `Action<T>?`) should also be reachable via a fluent extension.
2. **Property coverage.** Commonly-used properties of the underlying
   conceptual control should be first-class on the Reactor element (init
   property or fluent extension), so the default scenario never requires
   `.Set(c => c.Foo = bar)`.
3. **Naming alignment.** Property / factory / element-record names should
   be internally consistent (no equivalent WinUI control exists, so the
   bar is self-consistency rather than WinUI parity). Where a folder /
   type / factory disagree (e.g. `MaskedTextBox` folder vs
   `MaskedTextField` type — see 039 §3.1 / §16.3), call it out.

## Methodology

Sources surveyed:

- `src/Reactor/Controls/AutoSuggest/**` — `AutoSuggestElement<T>`.
- `src/Reactor/Controls/DataGrid/**` — `DataGridElement<T>` and related
  template-context records (`CellContext<T>`, `RowContext<T>`,
  `HeaderContext`, `FieldDescriptor`).
- `src/Reactor/Controls/MaskedTextBox/**` — `MaskedTextFieldElement`.
- `src/Reactor/Controls/PropertyGrid/**` — `PropertyGridElement`.
- `src/Reactor/Controls/Virtualization/**` — `VirtualListElement`.

Per-control audit columns mirror spec 039 §0:

| Field | Meaning |
| --- | --- |
| **Factory** | Public DSL entry point in `Factories` |
| **Element** | Backing record |
| **Conceptual peer** | Closest WinForms / WinUI / web equivalent (or "(custom)") |
| **Events on element** | All `Action`/`Action<T>` callbacks |
| **Events with fluent** | Which of those events also have a fluent extension |
| **Missing common properties** | Properties of the conceptual peer (or expected by callers) that are not surfaced as init property or fluent |
| **Naming notes** | Internal-consistency observations |

---

## §1 `AutoSuggestElement<T>`

File: `src/Reactor/Controls/AutoSuggest/AutoSuggestElement.cs`.

- [ ] **Event parity.** Inventory every callback on `AutoSuggestElement<T>`
      and confirm each has a fluent extension in
      `src/Reactor/Elements/ElementExtensions.Events.cs`.
- [ ] **Property coverage.** Compare against `AutoSuggestBoxElement`
      (spec 039 §3.4) — the typed peer should expose at minimum the same
      common properties as its untyped sibling, plus a typed item
      template / display selector surface.
- [ ] **Naming alignment.** Confirm factory name, element-record name,
      and namespace are self-consistent. The Reactor untyped sibling is
      `AutoSuggestBoxElement` — call out the `Box` suffix delta.

## §2 `DataGridElement<T>`

File: `src/Reactor/Controls/DataGrid/DataGridElement.cs` + sibling files
in the same folder.

- [ ] **Event parity.** Inventory every callback on `DataGridElement<T>`
      (`OnSelectionChanged`, `OnRowChanged`, …) and confirm each has a
      fluent extension. Note: `OnRowChanged` is `Func<RowKey, T, Task>?`
      (not `Action`), so spec 040 decides whether `Func`-shaped callbacks
      should also get fluents (spec 039 only covered `Action` / `Action<T>`).
- [ ] **Property coverage.** Audit against
      `Microsoft.UI.Xaml.Controls.DataGrid` (Community Toolkit) and
      WinForms `DataGridView`. Likely gaps: sort/group state, column
      auto-sizing, frozen columns, alternating row brushes,
      cell-validation surface, copy/paste keymap.
- [ ] **Naming alignment.** Confirm `DataGridElement<T>` vs
      `Microsoft.UI.Xaml.Controls.DataGrid` (CT) vs `DataGridView`
      (WinForms) — the Reactor name is fine; document the chosen
      reference peer for callers.

## §3 `MaskedTextFieldElement`

File: `src/Reactor/Controls/MaskedTextBox/MaskedTextFieldElement.cs`.

- [ ] **Event parity.** Inventory every callback (currently `OnChanged`)
      and confirm fluent coverage.
- [ ] **Property coverage.** Audit against WinForms `MaskedTextBox`.
      Likely gaps: `PromptChar`, `HidePromptOnLeave`, `BeepOnError`,
      `RejectInputOnFirstFailure`, `CutCopyMaskFormat`,
      `MaskCompleted` / `MaskFull` state surface.
- [ ] **Naming alignment.** The folder is `MaskedTextBox/` while the type
      is `MaskedTextFieldElement` and the factory is `MaskedTextField`.
      Spec 039 §16.3 already flagged this — if/when we rename
      `TextField` → `TextBox` we bundle this rename in the same change.
      Spec 040 either re-confirms keep-as-is or proposes the rename.

## §4 `PropertyGridElement`

File: `src/Reactor/Controls/PropertyGrid/PropertyGridElement.cs` + sibling
files (`PropertyGridDefaults`, `TypeRegistry`, `TypeMetadata`,
`Attributes`, `ReflectionTypeMetadataProvider`).

- [ ] **Event parity.** Inventory every callback (currently
      `OnRootChanged`) and confirm fluent coverage. Decide whether
      per-property change events should also surface.
- [ ] **Property coverage.** Audit against WinForms `PropertyGrid`.
      Likely gaps: `BrowsableAttributes` / categorization toggle,
      `HelpVisible`, `ToolbarVisible`, sort mode (categorized vs
      alphabetic), `SelectedGridItemChanged`,
      `PropertySort` enum surface.
- [ ] **Naming alignment.** Confirm element-record / factory /
      attribute namespace are self-consistent. `Controls.Attributes`
      currently exports `[BrowsableInPropertyGrid]` etc. — call out the
      pattern.

## §5 `VirtualListElement`

File: `src/Reactor/Controls/Virtualization/VirtualListElement.cs`.

- [ ] **Event parity.** Inventory every callback (currently
      `OnVisibleRangeChanged`) and confirm fluent coverage. Decide
      whether scroll-position / mount/unmount-per-item events should
      surface (these tend to be hot paths — be deliberate).
- [ ] **Property coverage.** Audit against WinUI `ItemsRepeater` and
      `ListView` virtualization surface. Likely gaps: `Orientation`,
      `EstimatedItemSize` vs `FixedItemSize` (currently only fixed),
      anchor-based scrolling, `IncrementalLoadingTrigger`,
      bidirectional incremental loading.
- [ ] **Naming alignment.** Reactor's `VirtualListElement` is generic
      virtualization (vs `LazyVStack<T>` / `LazyHStack<T>` which are
      typed `ItemsRepeater` wrappers — spec 039 §0.3). Document the
      relationship between the three.

---

## §6 Summary — recommended remediation order

To be filled out once §1–§5 are audited. Likely to mirror spec 039 §14:

1. Event-fluent parity (most controls already covered by spec 039
   Phase 7.2; expect this section to be mostly a no-op).
2. Property-coverage gaps with the most user-visible impact (DataGrid +
   PropertyGrid are likely to dominate).
3. Naming-alignment decisions (the `MaskedTextField` vs `MaskedTextBox`
   question carries forward from spec 039 §16.3).

## §6.1 Spec 039 Phase 3.7 carry-overs (niche WinUI events)

Spec 039 Phase 3.7 deferred five niche WinUI event surfaces to this spec.
None require a new Reactor element record beyond what already exists, so
they slot in as follow-ons to the §1–§5 audit above. Per-item rationale
lives in the 039 implementation task list; what's recorded here is the
ownership transfer.

- [ ] `MenuFlyout.Opening` / `MenuFlyout.Closing` — needs a Reactor-shaped
      cancellation pattern. Open question: how do we model cancellable
      args without leaking `CancelEventArgs` into user callbacks?
- [ ] `CommandBar.IsOpenChanged` — no existing consumer; bundle with any
      future `CommandBar`-using sample.
- [ ] `SemanticZoom.ViewChangeStarted` / `SemanticZoom.ViewChangeCompleted`
      — `SemanticZoom` is not currently modelled as a Reactor element;
      modelling the control is a prerequisite.
- [ ] `AnnotatedScrollBar.DetailLabelRequested` — `AnnotatedScrollBar`
      (WinUI 3 1.5+) is not yet exposed as a Reactor element.
- [ ] `MapControl.ViewChanged` — depends on the Windows Maps SDK which is
      not currently a Reactor dependency. Likely stays deferred until a
      consumer pulls the SDK in.

## §7 Open questions

To be filled out during audit. Likely candidates:

1. Should `Func<…, Task>?` callbacks (e.g. `DataGridElement<T>.OnRowChanged`)
   get fluents the same way `Action`-shaped callbacks do? Spec 039 §0.1
   only covered `Action` / `Action<T>`.
2. For `PropertyGrid` and `DataGrid`, do we model per-cell/per-property
   change events or rely on the existing whole-root / whole-row events?
3. For `VirtualListElement`, do we expose `MountItem` / `UnmountItem`
   lifecycle events — and at what cost on hot scroll paths?
