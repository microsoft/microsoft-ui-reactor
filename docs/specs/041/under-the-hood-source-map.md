# Under-the-hood Source Map

Phase-3.5 authoring aid. For each of the 14 Section-9 pages, lists the
`src/Reactor/` directories whose code the page will reference via
`snippet="source:..."` directives. The Phase-3.5 author studies these
areas before writing.

Verified against the actual `src/Reactor/` tree as of 2026-05-16.

---

## 1. `architecture-overview.md` (Comprehensive)

**Theme:** declarative shell → element records → reconciler → WinUI tree.

**Source areas:**
- `src/Reactor/Core/Component.cs` — component base, render loop entry
- `src/Reactor/Core/Element.cs` + `ElementFactory.cs` — element records
- `src/Reactor/Core/Reconciler.cs` (+ partial files
  `Reconciler.Mount.cs`, `Reconciler.Update.cs`,
  `Reconciler.Gestures.cs`, `Reconciler.DragDrop.cs`) — reconciler
  phases
- `src/Reactor/Core/RenderContext.cs` — render-time ambient state
- `src/Reactor/Hosting/` — app + window hosting glue

## 2. `reactivity-model.md` (Comprehensive)

**Theme:** state-setter → re-render. Why hooks, not INotifyPropertyChanged.

**Source areas:**
- `src/Reactor/Core/Component.cs` — `SetState` path, re-render queue
- `src/Reactor/Hooks/UseState.cs` (and other `Use*`)
- `src/Reactor/Core/ShouldUpdate*` (if present) — bail-out logic
- `src/Reactor/Core/Observable.cs` + `ObservableTreeTracker.cs` —
  observable bridge

## 3. `reactor-vs-xaml.md` (Comprehensive)

**Theme:** DependencyProperty / Binding / DataTemplate / Style /
VisualStateManager / Storyboard / INotifyPropertyChanged mapped to
Reactor equivalents.

**Source areas (illustrative, not deep):**
- `src/Reactor/Elements/ElementExtensions.cs` — modifier composition
- `src/Reactor/Core/Theme.cs` + `ThemeRef`-related — Style/resource
  parallel
- `src/Reactor/Core/Observable.cs` — INPC bridge
- `src/Reactor/Hooks/UseState.cs` — state vs DependencyProperty
- Animation: `src/Reactor/Animation/` (Storyboard parallel)

## 4. `reconciliation.md` (Comprehensive — promotes `docs/reference/reconciliation.md`)

**Source areas:**
- `src/Reactor/Core/Reconciler.cs`
- `src/Reactor/Core/Reconciler.Mount.cs`
- `src/Reactor/Core/Reconciler.Update.cs`
- `src/Reactor/Core/ChildReconciler.cs`
- `src/Reactor/Core/ChildCollection.cs`
- `src/Reactor/Core/Element.cs` — identity / WithKey

## 5. `hooks-internals.md` (Comprehensive — promotes `docs/reference/state-and-hooks.md`)

**Source areas:**
- `src/Reactor/Hooks/UseState.cs` (note: actual file is in
  `src/Reactor/Core/` per audit — verify path; the public hook
  surface dispatches through `src/Reactor/Hooks/`)
- `src/Reactor/Hooks/UseElementRef.cs`
- `src/Reactor/Hooks/UseMemoCells.cs`
- `src/Reactor/Hooks/UseMutation.cs`
- `src/Reactor/Hooks/UseResource.cs`
- `src/Reactor/Core/Component.cs` — hook slot table

## 6. `effects-scheduling.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Hooks/` (UseEffect — check exact file)
- `src/Reactor/Hooks/UseResource.cs` + `UseInfiniteResource.cs`
- `src/Reactor/Hooks/Pending.cs` + `PendingScope.cs`
- `src/Reactor/Core/QueryCache.cs` — query/cache plumbing

## 7. `modifier-system.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Elements/ElementExtensions.cs`
- `src/Reactor/Elements/ElementExtensions.Events.cs`
- `src/Reactor/Elements/ElementExtensions.NamedStyles.cs`
- `src/Reactor/Elements/FlexExtensions.cs`
- `src/Reactor/Elements/GridExtensions.cs`
- `src/Reactor/Elements/RelativePanelExtensions.cs`
- `src/Reactor/Elements/CanvasExtensions.cs`
- `src/Reactor/Elements/BackdropExtensions.cs`

## 8. `threading-and-dispatch.md` (Solid)

**Source areas:**
- `src/Reactor/Core/Component.cs` — UI-thread invariants in SetState
- `src/Reactor/Hosting/` — dispatcher trampoline
- `src/Reactor/Core/ChangeEchoSuppressor.cs` — coalescing

## 9. `element-pool.md` (Solid)

**Source areas:**
- `src/Reactor/Core/ElementPool.cs`
- `src/Reactor/Core/Element.cs` (rental shape)

## 10. `source-mapping.md` (Solid)

**Source areas:**
- `src/Reactor/Core/Diagnostics/` — stack-trace attribution
- `src/Reactor/Core/Internal/` — source-map related
- (Spec 010 — link to spec rather than dump source)

## 11. `analyzer-architecture.md` (Comprehensive)

**Source areas (in `src/Reactor.Analyzers/`):**
- `HookRulesAnalyzer.cs` — REACTOR_HOOKS_001/004/005/006/007
- `AccessibilityAnalyzers.cs` — REACTOR_A11Y_001..003
- `UseThemeRefAnalyzer.cs` — REACTOR_THEME_001
- `UseLightweightStylingAnalyzer.cs` — REACTOR_THEME_002
- `RequestedThemeSetAnalyzer.cs` — REACTOR_THEME_003
- `MissingWithKeyAnalyzer.cs` — REACTOR_DSL_001
- `UseMemoCellsAnalyzer.cs` — REACTOR_HOOKS_007
- (After Phase 1.8: `XmlDocSummaryAnalyzer.cs`, `XmlDocCrefAnalyzer.cs`
  for REACTOR_DOC_001 / 002)

## 12. `animation-pipeline.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Animation/` (top level + subdirs)
- (Reference spec 015 implicit animations + spec 037 keyframes)

## 13. `focus-and-input-internals.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Input/` — pointer / gesture event flow
- `src/Reactor/Hooks/UseFocus.cs`
- `src/Reactor/Hooks/UseFocusTrap.cs`
- `src/Reactor/Hooks/UseElementFocus.cs`
- `src/Reactor/Core/FocusRevalidationService.cs`
- `src/Reactor/Hooks/UseAnnounce.cs`

## 14. `devtools-internals.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Hooks/UseDevtools.cs`
- `src/Reactor/Core/Diagnostics/` (overlay + ETW sources)
- `src/Reactor.Cli/Devtools/` (MCP server + IDE protocol)

## 15. `perf-instrumentation.md` (Comprehensive)

**Source areas:**
- `src/Reactor/Core/Diagnostics/` — ETW emission
- (Spec 031 frame-aligned sampling, spec 032 layout-cost attribution —
  link rather than dump)

---

## Notes for the Phase-3.5 author

- **Verify file paths before adding `snippet="source:..."` directives.**
  This map was assembled from a directory listing on 2026-05-16. The
  source tree moves; mismatch is a build error (REACTOR_DOC_SNIPPET_*).
- **Use `// <snippet:region>...// </snippet:region>` markers** in
  source. Add them in a separate PR if the region you want to extract
  isn't already marked — that keeps doc churn out of feature commits.
- **Prefer small extracts over multi-page dumps.** A reader who has
  read the prose only needs to see the 10-20 lines that prove the
  prose claim. Full files are noise.
- **`docs/reference/async-system.md` stays as the deeper reference
  for now** per spec §7.1.1; `effects-scheduling.md` links to it but
  does not duplicate it.
- **`docs/reference/reconciliation.md` and
  `docs/reference/state-and-hooks.md` are absorbed and deleted** by
  Phase 3.5.4 and 3.5.5 respectively.
