# Reactor Performance Experiments — Tracking Spec

## Status
**Baselines measured, priorities revised** — WinUI3 Direct/Bound and Reactor
current-state numbers collected. Several hypotheses invalidated by data.
EXP-9 cut (Reactor already wins). EXP-1 and EXP-3 merged. Phase 1 focus:
interactive pooling (EXP-6), GC pressure (EXP-10), property diffs (EXP-2).
Machine: ARM64 Release, 10s duration per run.

---

## Goal

Identify and validate performance optimizations for Microsoft.UI.Reactor's reconciler, element
creation, and render loop. Each experiment follows the same methodology:

1. **Baseline (vanilla WinUI3)** — Direct control manipulation or data binding
2. **Current (Reactor, unoptimized)** — Today's Reactor reconciler
3. **New (Reactor, with optimization)** — Reactor with the specific experiment applied

All measurements use the `PerfTracker` harness from `StressPerf.Shared` and
report: **Avg Update ms**, **Avg FPS**, **Peak Memory MB**.

Where experiments require new test scenarios beyond StressPerf's 4,800-cell grid,
new benchmark apps follow the same `--headless --percent P --duration D` CLI pattern.

### Machine
ARM64 builds, Release configuration. All numbers on the same hardware.

---

## Experiment Index

| # | Optimization | Target Metric | Section |
|---|---|---|---|
| 1 | Dirty Subtree Tracking | Wall clock, CPU | [EXP-1](#exp-1-dirty-subtree-tracking) |
| 2 | Property Diff Bitmasks | Wall clock (COM calls) | [EXP-2](#exp-2-property-diff-bitmasks) |
| 3 | Structural Sharing / Ref-Equality Bailout | Wall clock, memory | [EXP-3](#exp-3-structural-sharing) |
| 4 | Off-Thread Tree Building | UI thread blocking | [EXP-4](#exp-4-off-thread-tree-building) |
| 5 | Mutation Journaling | Enabler for #4, #7 | [EXP-5](#exp-5-mutation-journaling) |
| 6 | Interactive Control Pooling | Memory, mount cost | [EXP-6](#exp-6-interactive-control-pooling) |
| 7 | Time-Sliced Reconciliation | Interactivity (jank) | [EXP-7](#exp-7-time-sliced-reconciliation) |
| 8 | Prioritized State Updates (Lanes) | Perceived responsiveness | [EXP-8](#exp-8-prioritized-state-updates) |
| 9 | Lazy Off-Screen Mounting | Initial render time | [EXP-9](#exp-9-lazy-off-screen-mounting) |
| 10 | Arena Allocation for Element Trees | GC pressure, memory | [EXP-10](#exp-10-arena-allocation) |

---

## EXP-1: Dirty Subtree Tracking

### Problem
`RenderLoop` re-renders the full component tree from root on every state change.
A `SetState` in one leaf component triggers `Render()` on every component in the
tree, even those with identical props and no state changes.

### Optimization
Maintain an invalidation set. When `SetState` is called, mark that component's
`ComponentNode` (and ancestors up to root) as dirty. During reconcile, if a
component is not dirty and its element passes `ShallowEquals`, skip the entire
subtree — don't call `Render()` at all.

**Precedent:** React's `beginWork` bailout (`oldProps === newProps && !hasLanes`
skips entire subtrees). Compose's `$changed` bitmask skips composable bodies.
SwiftUI's AttributeGraph only re-evaluates dirty attributes.

### Test Scenario: `PerfBench.DirtyTracking`

Workload:
- **200 independent counter components** arranged in a 10×20 grid
- Each counter is a function component with its own `UseState<int>`
- A `DispatcherTimer` at 30 Hz increments **1 randomly chosen counter** per tick
- Measures: tree build ms + reconcile ms per frame

WinUI3 baselines:
- **Direct**: 200 TextBlocks in a Grid, update 1 TextBlock per tick
- **Bound**: 200 TextBlocks bound to 200 ViewModels, update 1 ViewModel per tick

### Hypothesis

| Variant | Avg Update (ms) | Rationale |
|---|---|---|
| WinUI3 Direct | **0.02** | Single TextBlock.Text set, trivial |
| WinUI3 Bound | **0.05** | Binding propagation for 1 property |
| Reactor Current | **1.5–3.0** | Renders all 200 components, reconciles all 200 elements even though 199 are unchanged |
| Reactor + Dirty Tracking | **0.05–0.15** | Renders only 1 dirty component, skips 199 subtrees via dirty check |

**Expected improvement:** 10–30× reduction in per-frame update cost. This is the
single highest-impact optimization because it converts O(total components) work
into O(dirty components) work.

### Actual Results

| Variant | Avg Update (ms) | Avg FPS | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **0.06** | **59.0** | **131** |
| WinUI3 Bound | **0.13** | **58.9** | **135** |
| Reactor Current | **0.04** | **59.8** | **140** |
| Reactor + Dirty Tracking | _pending_ | _pending_ | _pending_ |

**Analysis:** At 200 elements with only 1 update/tick, the workload is light enough that
all variants hit ~60 FPS. Reactor's update time is the lowest (0.04ms) because
`BeginUpdate/EndUpdate` only measures the `SetState` call, not the subsequent
reconcile (which is deferred to the render loop). The real cost shows up in GC
pressure: Reactor had 9 Gen0 collections vs 1 for Direct/Bound, confirming that
element tree allocation per frame is a measurable overhead even at this scale.
The dirty tracking optimization should eliminate 199/200 of the render work.

---

## EXP-2: Property Diff Bitmasks

### Problem
When `ShallowEquals` returns false (element changed), the reconciler sets *every*
property on the WinUI control, even if only one property actually differs. Each
property set is a COM interop call across the WinUI apartment boundary.

### Optimization
Generate per-element-type `DiffProps(old, new)` methods (via source generator or
manual code) that return a bitmask of which properties changed. `ApplyChanges`
only sets the changed properties.

Example for `TextElement`:
```
Bit 0 = Content changed
Bit 1 = FontSize changed
Bit 2 = FontWeight changed
Bit 3 = Foreground changed
...
```

**Precedent:** Vue 3's compiler emits patch flags per template node indicating
which bindings are dynamic. Lit only updates changed template "parts."

### Test Scenario: `PerfBench.PropertyDiff`

Workload:
- **4,800 TextBlocks** in the StressPerf grid layout
- 50% of cells updated per tick at 30 Hz
- Each update changes **only `Text` and `Foreground`** (2 of ~8 properties)
- Measures: reconcile phase ms per frame

WinUI3 baselines:
- **Direct**: Set only `.Text` and `.Foreground` on changed cells
- **Bound**: Bindings fire only for changed properties (inherently fine-grained)

### Hypothesis

| Variant | Avg Reconcile (ms) | Rationale |
|---|---|---|
| WinUI3 Direct | **1.5** | 2,400 cells × 2 property sets = 4,800 COM calls |
| WinUI3 Bound | **2.5** | Binding overhead per property, but only 2 properties fire |
| Reactor Current | **4.0–6.0** | 2,400 cells × ~8 property sets = ~19,200 COM calls (sets all props even if unchanged) |
| Reactor + Bitmask Diff | **2.0–2.5** | 2,400 cells × 2 property sets = 4,800 COM calls + bitmask comparison overhead |

**Expected improvement:** 2–3× reduction in reconcile time for partial-update
scenarios. Brings Reactor closer to parity with direct manipulation.

### Actual Results

| Variant | Avg Update (ms) | Avg FPS | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **9.75** | **12.6** | **376** |
| WinUI3 Bound | **33.21** | **10.1** | **468** |
| Reactor Current | **0.34** (state only) | **5.4** | **436** |
| Reactor + Bitmask Diff | **0.31** (state only) | **5.6** | **431** |

**Analysis:** At 4,800 cells with 50% update rate, this is the first scenario where
Reactor's overhead becomes clearly visible. Reactor achieves only 5.4 FPS vs 12.6 for
Direct — the reconciler is spending ~180ms per frame rebuilding and diffing the
entire 4,800-element tree and setting all properties on changed elements. Reactor's
"Avg Update" (0.34ms) is misleadingly low because it only measures the data
mutation, not the subsequent reconcile pass. GC pressure is severe: 94 Gen0
collections (vs 2 for Direct), confirming that per-frame element allocation is
a real bottleneck at this scale. Bound (33ms update, 10 FPS) shows binding
engine overhead is also significant. The bitmask diff optimization should reduce
COM calls from ~8 per cell to 2, bringing Reactor closer to Direct's throughput.

**EXP-2 Bitmask Result (3-run avg):** Bitmask ON achieved 5.6 FPS vs 5.4 FPS
baseline — a **~3-4% improvement**, within run-to-run noise. The optimization
successfully avoids COM *reads* for unchanged properties (comparing old vs new
Element C# fields instead), but the existing `UpdateText` already guards COM
*writes* with per-property checks (`if (tb.Text != n.Content)`). Since COM reads
are cheap relative to writes (no layout invalidation), the savings are marginal.
The dominant bottleneck at this scale is the full 4,800-element tree rebuild and
GC pressure (~146 Gen0 collections), not the property-setting phase. The bitmask
diff is architecturally correct but low-impact in isolation — EXP-3 (structural
sharing) and EXP-10 (GC reduction) target the actual bottlenecks.

---

## EXP-3: Structural Sharing

### Problem
Every `Render()` call allocates new `Element` record instances and new `Element[]`
arrays, even for subtrees that haven't changed. The reconciler can't bail out
early because `oldElement != newElement` (different object references) even when
the content is identical — it must walk into `ShallowEquals` and children.

### Optimization
Cache and reuse `Element` subtree references when inputs haven't changed.
- Components whose `Render()` output depends only on props and state can cache
  the previous `Element` result and return the same reference when inputs match.
- A `Memo(props, () => element)` helper returns the cached `Element` reference
  when `props` is unchanged.
- The reconciler adds a fast path: `if (ReferenceEquals(oldElement, newElement)) return;`
  — skips the entire subtree in O(1), no `ShallowEquals`, no child walk.

**Precedent:** React's primary bailout: `oldProps === newProps` (referential
equality) skips the entire subtree. React Native Fabric's shadow tree uses C++
pointer equality on `ShadowNode`. Persistent data structures share unchanged
subtrees automatically.

### Test Scenario: `PerfBench.StructuralSharing`

Workload:
- A **dashboard layout** with 5 panels, each containing 50 elements (250 total)
- A timer at 30 Hz updates **1 panel's** data (50 elements change)
- The other 4 panels (200 elements) are completely static per frame
- Measures: tree build ms + reconcile ms per frame

WinUI3 baselines:
- **Direct**: Update 50 TextBlocks in the changed panel only
- **Bound**: 250 bound TextBlocks, 50 VMs fire PropertyChanged

### Hypothesis

| Variant | Avg Update (ms) | Rationale |
|---|---|---|
| WinUI3 Direct | **0.1** | 50 property sets, trivial |
| WinUI3 Bound | **0.3** | 50 bindings fire, 200 are dormant |
| Reactor Current | **1.0–2.0** | Renders all 5 panels (250 elements), reconciles all 250 with ShallowEquals checks |
| Reactor + Structural Sharing | **0.3–0.5** | 4 panels return cached Element refs → O(1) ref-equality skip each. Only 1 panel (50 elements) reconciled |

**Expected improvement:** 2–4× for layouts with mostly-static regions. Combines
multiplicatively with EXP-1 (dirty tracking skips the render call; structural
sharing skips the reconcile walk).

### Actual Results

| Variant | Avg Update (ms) | Avg FPS | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **0.29** | **59.1** | **136** |
| WinUI3 Bound | **0.84** | **58.5** | **143** |
| Reactor Current | **0.04** | **59.6** | **147** |
| Reactor + Structural Sharing | _pending_ | _pending_ | _pending_ |

**Analysis:** With only 250 elements and 50 changing per tick, all variants comfortably
hit ~60 FPS. The workload is too light to differentiate — Reactor's reconciler handles
250 elements easily within a single frame. The structural sharing optimization will
show its value at larger scales where the 4 static panels represent significant
wasted work. GC pressure: Reactor had 5 Gen0 collections vs 1 for Direct, proportional
to the smaller tree size.

---

## EXP-4: Off-Thread Tree Building

### Problem
The entire render cycle — tree build, reconcile, effects — runs on the UI
thread via `DispatcherQueue.TryEnqueue`. The tree build phase
(`Component.Render()`) produces immutable `Element` records with no WinUI
affinity, yet it blocks the UI thread while running.

### Optimization
Move the tree build phase to a `ThreadPool` thread. The flow becomes:

```
UI Thread                          Thread Pool
──────────                         ───────────
RequestRender() ──dispatch──►      Build Element tree
                                   (Component.Render calls)
◄──────────────── post back ────   Element tree ready
Reconcile(old, new)
FlushEffects()
```

State reads during `Render()` need snapshot isolation (similar to Compose's
snapshot system) — take a copy of state values before dispatching to the
background thread, ensuring the tree build sees a consistent view.

**Precedent:** Compose's composition can run off-main-thread via snapshot
isolation. React Native Fabric builds ShadowNode trees in C++ on a background
thread.

### Test Scenario: `PerfBench.OffThread`

Workload:
- **1,000 elements** with moderately expensive `Render()` (each component reads
  2 state values, produces 3 child elements with computed properties)
- 30 Hz timer triggers full re-render
- **Primary metric:** UI thread blocked time per frame (excludes tree build)
- **Secondary metric:** Total wall clock per frame (tree build + reconcile)
- Also measures: input latency via a TextBox that accepts keystrokes during
  updates — measure time between keydown event and character appearing

WinUI3 baselines:
- **Direct**: 1,000 TextBlocks updated directly (all on UI thread, no tree build phase)
- **Bound**: 1,000 bindings firing (binding engine is inherently UI thread)

### Hypothesis

| Variant | UI Thread Blocked (ms) | Total Wall Clock (ms) | Input Latency (ms) |
|---|---|---|---|
| WinUI3 Direct | **2.0** | **2.0** | **< 1** |
| WinUI3 Bound | **3.0** | **3.0** | **< 1** |
| Reactor Current | **5.0–8.0** | **5.0–8.0** | **5–10** |
| Reactor + Off-Thread Build | **2.5–4.0** | **5.0–8.0** (same total) | **2–4** |

**Expected improvement:** 40–50% reduction in UI thread blocking. Total wall
clock stays the same (work is moved, not eliminated), but interactivity improves
because the UI thread is free to process input during tree build. Input latency
should roughly halve.

### Actual Results

| Variant | Avg UI Block (ms) | Avg FPS | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **1.91** | **29.9** | **266** |
| WinUI3 Bound | **6.46** | **29.6** | **308** |
| Reactor Current | **0.09** (state only) | **31.7** | **283** |
| Reactor + Off-Thread Build | _pending_ | _pending_ | _pending_ |

**Analysis:** At 1,000 elements with expensive computation (sin/cos/sqrt), the 30Hz
timer limits all variants to ~30 FPS. Reactor's measured UI block (0.09ms) is the
`SetState` call — the real UI thread work happens in the deferred render loop.
Direct's UI block (1.91ms) includes both computation and property sets. Bound's
(6.46ms) adds binding engine overhead. Reactor's GC pressure is notable: 62 Gen0
collections vs 3 for Direct. The off-thread optimization would move Reactor's tree
build to a ThreadPool thread, freeing the UI thread during reconcile.

---

## EXP-5: Mutation Journaling

### Problem
The reconciler directly mutates WinUI controls during the tree walk (sets
properties, adds/removes children). This couples the diff phase to the UI
thread and prevents batching or reordering of mutations.

### Optimization
The reconciler produces a flat `List<Mutation>` instead of touching controls:

```csharp
abstract record Mutation;
record Create(Type ControlType, int Id) : Mutation;
record SetProp(int Id, string Prop, object Value) : Mutation;
record AddChild(int ParentId, int ChildId, int Index) : Mutation;
record RemoveChild(int ParentId, int ChildId) : Mutation;
record Move(int ParentId, int ChildId, int NewIndex) : Mutation;
```

A `MutationApplier` replays the journal on the UI thread in a single batch.
This enables:
- The diff phase to run off-thread (since it no longer touches UIElements)
- Coalescing redundant mutations (e.g., two `SetProp` on the same property → keep last)
- Atomic commits (apply all changes in one shot)

**Precedent:** React Native Fabric's `Differentiator.cpp` produces mutation
instructions applied on the UI thread. Flutter's layer tree diffs produce a
mutation list for the raster thread.

### Test Scenario: `PerfBench.Journal`

Workload:
- **StressPerf grid** (4,800 cells) at 50% update rate, 30 Hz
- Compare: (a) current in-place reconcile vs (b) journal + batch apply
- Measure reconcile phase and apply phase separately
- Also measure: mutation coalescing effectiveness (how many mutations are
  eliminated by dedup)

WinUI3 baselines:
- **Direct**: Direct property sets (no journal overhead, optimal path)
- **Bound**: Binding engine batches internally within a single dispatcher tick

### Hypothesis

| Variant | Reconcile (ms) | Apply (ms) | Total (ms) | Mutations/frame |
|---|---|---|---|---|
| WinUI3 Direct | — | **1.5** | **1.5** | 4,800 |
| WinUI3 Bound | — | **2.5** | **2.5** | 4,800 |
| Reactor Current | **4.0** (interleaved) | (included) | **4.0** | ~19,200 (all props) |
| Reactor + Journal | **1.5** (no COM) | **2.5** (batch) | **4.0** | ~4,800 (after coalesce) |

**Expected improvement:** Total wall clock similar or slightly worse due to
journal overhead. The win is structural — this decouples diff from apply,
enabling EXP-4 (off-thread diff) and EXP-7 (time-slicing). Combined with
EXP-2 (bitmask diff), mutation count drops and apply phase shrinks.

### Actual Results

| Variant | Avg Update (ms) | Avg FPS | Peak Memory (MB) | Mutations/10s |
|---|---|---|---|---|
| WinUI3 Direct | **9.90** | **10.2** | **354** | **239,804** |
| WinUI3 Bound | **32.42** | **8.2** | **438** | **211,086** |
| Reactor Current | **0.96** | **5.2** | **396** | **119,898** |
| Reactor + Journal | _pending_ | _pending_ | _pending_ | _pending_ |

**Analysis:** Same 4,800-cell grid as EXP-2. Reactor at 5.2 FPS has roughly half the
throughput of Direct (10.2 FPS). Mutation counts confirm the overhead: Direct
produced 240K mutations (2 props × ~2400 cells × ~50 ticks) while Reactor produced
120K (fewer ticks due to lower FPS). The per-frame mutation density in Reactor
is higher since it sets all properties on every reconciled element, not just
the changed ones. The journal optimization decouples diff from apply, enabling
mutation coalescing and off-thread diffing.

---

## EXP-6: Interactive Control Pooling

### Problem
`ElementPool` only recycles non-interactive controls (TextBlock, StackPanel,
etc.). Interactive controls (Button, TextBox, ToggleSwitch, ComboBox) are
excluded because their event subscriptions and transient state make cleanup
complex. These are also the most expensive controls to create — Button template
instantiation involves XAML template parsing, visual state groups, and
ContentPresenter creation.

### Optimization
Add interactive controls to the pool with a `ResetInteractiveElement()` method:
- Unsubscribe all event handlers (Click, TextChanged, etc.)
- Clear transient visual state (IsPressed, Focus, selection)
- Reset Content/Text to defaults
- Call `GoToState("Normal")` to reset visual states

Since Reactor uses the Tag-based event pattern (wire once at mount, read element
from Tag), the reset only needs to detach the single event subscription.

**Precedent:** Android `RecyclerView` recycles all view types including
interactive controls. `ViewHolder.onRecycled()` clears transient state. iOS
`UICollectionView.prepareForReuse()` recycles any cell.

### Test Scenario: `PerfBench.InteractivePool`

Workload:
- A **form list** with 500 items, each containing: Button + TextBox + ToggleSwitch
  (1,500 interactive controls total)
- Virtualized via `LazyVStack` — only ~30 items visible at once
- User scrolls rapidly (programmatic scroll at 2000px/sec for 5 seconds)
- Measures: mount time per newly visible item, total memory, GC collections

WinUI3 baselines:
- **Direct**: `ItemsRepeater` with `DataTemplate` containing Button + TextBox +
  ToggleSwitch (WinUI's native recycling via `ElementFactory`)
- **Bound**: Same but with `{x:Bind}` bindings for each control's properties

### Hypothesis

| Variant | Avg Mount/item (ms) | Peak Memory (MB) | GC Gen0 Collections |
|---|---|---|---|
| WinUI3 Direct (recycle) | **0.5** | **45** | **low** |
| WinUI3 Bound (recycle) | **0.8** | **50** | **low** |
| Reactor Current (no pool) | **3.0–5.0** | **120** | **high** |
| Reactor + Interactive Pool | **0.8–1.5** | **55** | **low** |

**Expected improvement:** 3–5× reduction in per-item mount cost during scroll.
Memory usage should drop significantly as controls are recycled instead of
allocated/GC'd. Scroll smoothness should match native `ItemsRepeater`.

### Actual Results

All variants use ItemsRepeater-based virtualization (LazyVStack for Reactor).

| Variant | Total Mount (ms) | Per-item Mount (ms) | Peak Memory (MB) | GC Gen0 | Avg FPS | Min FPS |
|---|---|---|---|---|---|---|
| WinUI3 Direct | **458** | **0.92** | **257** | **1** | **53.4** | **27.8** |
| WinUI3 Bound | **427** | **0.85** | **261** | **1** | **53.0** | **26.6** |
| Reactor (no pool) | **305** | **0.61** | **243** | **2** | **58.2** | **43.9** |
| Reactor + Interactive Pool | **282** | **0.56** | **227** | **1** | **58.2** | **43.6** |

Reactor (no pool) uses `--optimization off` which disables `ElementPool.Enabled`,
so all controls are created fresh on each mount. Reactor + Pool uses `--optimization on`.

**Analysis:** With virtualization via LazyVStack/ItemsRepeater, Reactor already
outperforms both WinUI3 baselines even without interactive pooling. Interactive
pooling adds an incremental **8% mount improvement** (305→282ms) and **50%
fewer GC Gen0 collections** (2→1) by recycling Button, TextBox, and ToggleSwitch
controls instead of allocating new ones. Peak memory drops 7% (243→227 MB).

The larger story is that Reactor's reconciler + ItemsRepeater is faster than raw
WinUI3 Direct (282 vs 458ms) and Bound (282 vs 427ms) because Reactor's element
creation is lighter weight than full XAML template instantiation when virtualized.
FPS is also superior: 58.2 avg / 43.6 min vs Direct's 53.4/27.8.

---

## EXP-7: Time-Sliced Reconciliation

### Problem
A large reconcile pass (1000+ elements) can exceed 16ms, causing a missed frame
and visible jank. The entire reconcile runs synchronously — no yield points.

### Optimization
Break reconcile into interruptible units of work. After processing N element
pairs (e.g., 50), check elapsed time. If > 5ms, yield back to the
DispatcherQueue and resume next frame. Requires EXP-5 (mutation journal) so
that partial work can be accumulated and applied atomically when complete.

Two-phase commit:
1. **Reconcile phase (interruptible):** Walk element tree, produce mutations.
   Can pause and resume across frames.
2. **Apply phase (atomic):** Replay accumulated mutations in one batch. Must
   complete within a single frame.

**Precedent:** React Fiber's core design — each fiber node is a unit of work,
checked against a 5ms deadline via `shouldYield()`. Render phase is
interruptible; commit phase is atomic and synchronous.

### Test Scenario: `PerfBench.TimeSlice`

Workload:
- **Initial mount of 2,000 elements** (simulates navigating to a complex page)
- During mount, a 60fps animation runs on a separate `Canvas` (a bouncing ball
  via `CompositionTarget.Rendering`)
- Measure: animation frame drops during mount, total mount wall clock,
  longest single-frame block

WinUI3 baselines:
- **Direct**: Create 2,000 TextBlocks in a loop (synchronous, single frame block)
- **Bound**: Same with bindings (also synchronous)

### Hypothesis

| Variant | Longest Frame Block (ms) | Animation Drops | Total Mount (ms) |
|---|---|---|---|
| WinUI3 Direct | **80–120** | **5–7** | **80–120** |
| WinUI3 Bound | **100–150** | **6–9** | **100–150** |
| Reactor Current | **100–160** | **6–10** | **100–160** |
| Reactor + Time-Sliced | **5–8** | **0–1** | **150–250** |

**Expected improvement:** Longest frame block drops from >100ms to <8ms. Total
mount time *increases* (overhead of yielding + resuming, plus mutations are
batched), but perceived smoothness is dramatically better — animations stay
fluid during mount. Trade-off: the UI appears incrementally over several frames
instead of all-at-once.

### Actual Results

| Variant | Longest Frame Block (ms) | Animation Drops | Avg Mount (ms) |
|---|---|---|---|
| WinUI3 Direct | **173** | **5** | **15** |
| WinUI3 Bound | **182** | **7** | **105** |
| Reactor Current | **189** | **19** | **0.5** (deferred) |
| Reactor + Time-Sliced | _pending_ | _pending_ | _pending_ |

**Analysis:** All variants show significant frame blocking during the 2,000-element
mount (~170-190ms longest block). Reactor's measured mount time (0.5ms) is
misleadingly low — the `SetState(true)` call is near-instant, but the
actual reconcile/mount of 2,000 elements happens in the render loop and
causes the 189ms frame block. Reactor has the most animation drops (19 vs 5
for Direct), suggesting the reconcile pass introduces additional jank
beyond the raw control creation cost. The time-slicing optimization would
break this into 5-8ms chunks, keeping the animation smooth.
GC: Reactor had 171 Gen0 collections vs 2 for Direct — 2,000 elements worth of
per-frame allocation pressure during the mount renders.

---

## EXP-8: Prioritized State Updates

### Problem
All state updates are treated equally. A keystroke in a TextBox and a large list
filter update are processed in the same render pass. If the list filter is
expensive, it delays the keystroke feedback.

### Optimization
Implement a 2-tier priority system:

- **Sync** (default for `UseState` setters called from input event handlers):
  Processed immediately in the next `DispatcherQueue` tick.
- **Transition** (opt-in via `StartTransition(() => setFilteredItems(...))`):
  Deferred. Can be interrupted if a Sync update arrives. Shows stale UI during
  processing (the old list stays visible while the new one is computed).

Implementation: `RequestRender` tags each pending update with a priority.
`RenderLoop` processes Sync updates first. If a Transition render is in
progress and a Sync update arrives, abandon the Transition work and process
the Sync update.

**Precedent:** React 18's lanes system with `startTransition()`. Compose's
snapshot system coalesces low-priority updates.

### Test Scenario: `PerfBench.Priorities`

Workload:
- A **TextBox** (search input) + a **list of 5,000 items** filtered by the
  search text
- User types "abcdef" at ~10 chars/sec (100ms between keystrokes)
- Each keystroke triggers: (a) TextBox display update (Sync), (b) filter +
  re-render 5,000 items (Transition)
- Measure: keystroke-to-display latency, list update latency, dropped frames

WinUI3 baselines:
- **Direct**: TextBox.TextChanged filters an `ObservableCollection`, updates
  `ListView.ItemsSource` — all synchronous on UI thread
- **Bound**: TextBox bound to ViewModel, filter runs in setter, updates
  ObservableCollection — all synchronous

### Hypothesis

| Variant | Keystroke Latency (ms) | List Update Latency (ms) | Dropped Frames |
|---|---|---|---|
| WinUI3 Direct | **30–80** (blocked by filter) | **30–80** | **2–4 per keystroke** |
| WinUI3 Bound | **30–80** (blocked by filter) | **30–80** | **2–4 per keystroke** |
| Reactor Current | **40–100** (blocked by filter + reconcile) | **40–100** | **3–6 per keystroke** |
| Reactor + Priorities | **2–5** (immediate) | **100–200** (deferred) | **0** |

**Expected improvement:** Keystroke feedback becomes instant. List updates are
visibly delayed (stale content shown briefly), but the UI never janks. This is a
trade-off: total work is the same or slightly more, but perceived responsiveness
is dramatically better. Note that even WinUI3 Direct suffers here because the
filter is synchronous — Reactor with priorities would actually *beat* vanilla WinUI3
on keystroke responsiveness.

### Actual Results

| Variant | Avg Input Latency (ms) | Avg Update (ms) | Avg FPS |
|---|---|---|---|
| WinUI3 Direct | **22** | **21.1** | **54.2** |
| WinUI3 Bound | — (crashed) | — | — |
| Reactor Current | **46** | **0.01** | **54.6** |
| Reactor + Priorities | _pending_ | _pending_ | _pending_ |

**Analysis:** With 5,000 items and search-as-you-type, Reactor's input latency (46ms)
is **2× worse** than Direct (22ms). Both achieve similar FPS (~54), but
Reactor's keystroke feedback is delayed because each key triggers a full
reconcile of the filtered list. Direct's 22ms includes synchronous filter +
rebuild of the StackPanel. Reactor's update time (0.01ms) only measures the
`SetState` call — the actual filter + reconcile happens in the render loop.
The priorities optimization would process the TextBox update immediately
(Sync) and defer the expensive list filter to a Transition, giving
near-instant keystroke feedback.

---

## EXP-9: Lazy Off-Screen Mounting

### Problem
When a complex view loads (e.g., a tabbed settings page), all tabs' content is
mounted immediately even though only the active tab is visible. Components in
collapsed Expanders, hidden Pivots, and below-fold scroll content all pay full
mount cost upfront.

### Optimization
Introduce a `Deferred(element, estimatedSize?)` wrapper element. The reconciler
mounts a lightweight placeholder (empty Border with estimated size) instead of
the real element tree. When the placeholder becomes visible (enters viewport or
parent becomes visible), mount the real content.

Visibility detection:
- `EffectiveViewportChanged` on the placeholder for scroll-based visibility
- `Visibility` / `IsLoaded` tracking for tab/expander scenarios

**Precedent:** CSS `content-visibility: auto` defers layout/paint for off-screen
content. SwiftUI's `LazyVStack` defers view body evaluation until visible.
React's `<Suspense>` + `lazy()` defers component tree construction.

### Test Scenario: `PerfBench.DeferredMount`

Workload:
- A **Pivot** (tab control) with **5 tabs**, each containing 200 elements
  (1,000 total elements across all tabs)
- Only tab 0 is visible initially
- Measure: time from app launch to first interactive frame
- Then: measure time to switch to tab 1 (first visit, must mount 200 elements)

WinUI3 baselines:
- **Direct**: 5 panels with 200 TextBlocks each, all created at startup.
  Only the active panel is visible.
- **Bound**: Same with bindings, all created at startup.

### Hypothesis

| Variant | Initial Render (ms) | Tab Switch (ms) | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **80–100** (all 1000 created) | **< 1** (already exists) | **60** |
| WinUI3 Bound | **100–130** (all 1000 created) | **< 1** (already exists) | **65** |
| Reactor Current | **100–140** (all 1000 rendered + reconciled) | **< 1** (already mounted) | **70** |
| Reactor + Deferred | **25–35** (only 200 for active tab) | **25–35** (mount on demand) | **30** (grows as tabs visited) |

**Expected improvement:** 3–5× faster initial render. Memory proportional to
visible content only. Trade-off: first visit to each tab has a mount cost.
This matches the web/mobile pattern where faster initial load + lazy tab
content is preferred over slower initial load + instant tabs.

### Actual Results

| Variant | Avg Mount (ms) | Avg Tab Switch (ms) | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **9.5** | **0.45** | **218** |
| WinUI3 Bound | **54.7** | **0.38** | **226** |
| Reactor Current | **1.4** (per switch) | **0.16** | **139** |
| Reactor + Deferred | _pending_ | _pending_ | _pending_ |

**Analysis:** Direct pre-creates all 1,000 elements at startup (9.5ms mount), then
tab switches are trivial (toggling Visibility). Bound does the same but
with binding setup overhead (54.7ms). Reactor only renders the active tab's
200 elements, so initial mount is fast (1.4ms), and each tab switch triggers
a re-render of 200 elements. Reactor's peak memory (139 MB) is significantly
lower than Direct (218 MB) because only the active tab's controls exist.
This is actually favorable for Reactor's current architecture — the deferred
mounting optimization would formalize this lazy pattern for other
hidden-content scenarios (Expanders, below-fold scroll content).

---

## EXP-10: Arena Allocation for Element Trees

### Problem
Every render cycle allocates a new tree of `Element` record instances and
`Element[]` arrays. These are short-lived (only needed until reconciliation
compares them with the mounted state) and create Gen0 GC pressure proportional
to tree size on every frame.

### Optimization
Use an `ArrayPool<T>`-backed arena for element arrays, and explore `struct`
element variants for leaf nodes (TextElement, ImageElement) to avoid heap
allocation entirely.

Two sub-experiments:
- **10a: Array pooling** — `Element[]` children arrays rent from `ArrayPool`
  and return after reconcile. Requires a `RenderScope` that tracks rentals.
- **10b: Struct leaf elements** — Convert frequently-used leaf element types
  to `readonly record struct`. Requires boxing when stored in `Element[]` but
  avoids heap allocation for the element itself. Alternative: use a
  discriminated-union-style `LeafElement` struct with a type tag.

**C# constraint (10b):** Leaf elements inherit from `abstract record Element`,
so they cannot literally become `readonly record struct` (structs cannot inherit
from classes). The practical C# equivalent is **direct construction with cached
modifiers** — a `TextDirect()` factory that builds the final TextElement via
object initializer (1 heap allocation) instead of the fluent `with`-copy chain
`Text().FontSize().Foreground().Grid()` (7 heap allocations: 4 intermediate
TextElement copies + ElementModifiers + GridAttached + Dictionary). Immutable
modifier and attached-property objects are cached across frames so only the
TextElement record itself is allocated per cell per frame.

**Precedent:** Compose's slot table uses flat `int[]` and `Any?[]` arrays
(gap buffer) instead of heap-allocated tree nodes. Game engines use bump/arena
allocators for per-frame scratch data. ECS frameworks use slab allocation.

### Test Scenario: `PerfBench.Allocation`

Workload:
- **StressPerf grid** (4,800 cells) at 100% update rate, 30 Hz (worst case
  allocation pressure — every element reallocated every frame)
- Measure: GC Gen0 collections over 10 seconds, GC pause time, peak working set
- Use `GC.CollectionCount(0)` and `GC.GetGCMemoryInfo()` for precise tracking

WinUI3 baselines:
- **Direct**: No per-frame allocation (mutates existing controls in place)
- **Bound**: ViewModels are long-lived, minimal per-frame allocation (just
  property change events)

### Hypothesis

| Variant | Gen0 Collections / 10s | Avg GC Pause (ms) | Peak Memory (MB) |
|---|---|---|---|
| WinUI3 Direct | **< 5** | **< 0.5** | **140** |
| WinUI3 Bound | **< 10** | **< 0.5** | **150** |
| Reactor Current | **50–100** | **1.0–2.0** | **170** |
| Reactor + Array Pool | **20–40** | **0.5–1.0** | **155** |
| Reactor + Struct Leaves | **10–20** | **< 0.5** | **145** |

**Expected improvement:** Moderate. .NET's Gen0 GC is already very fast, so
the wall-clock impact may be small (< 1ms per frame). The real benefit is
reducing GC pauses that occasionally cause frame drops. This experiment has
the lowest expected impact and highest implementation complexity — pursue only
if GC is measured as a bottleneck in real workloads.

**What actually happened:** 10a (array pooling) had zero impact because
children arrays are a tiny fraction of total allocation. 10b (direct
construction with cached modifiers) showed meaningful improvement: 33%
fewer Gen0 collections and 26 MB less peak memory. The key insight is that
the fluent `with`-copy chain is the allocation multiplier (7× per cell),
not the container arrays. See Actual Results below.

### Actual Results

| Variant | Avg Update (ms) | Avg FPS | Peak Memory (MB) | GC Gen0 / 10s |
|---|---|---|---|---|
| WinUI3 Direct | **30.86** | **2.8** | **284** | **0** |
| WinUI3 Bound | **47.73** | **2.6** | **319** | **0** |
| Reactor Current | **0.21** (state only) | **3.1** | **339** | **51** |
| Reactor + Array Pool (10a) | **0.18** | **3.2** | **338** | **52** |
| Reactor + Direct Alloc (10b) | **0.17** | **3.3** | **313** | **34** |

**Analysis (baseline):** At 100% update rate (all 4,800 cells every tick),
this is the worst-case scenario for all variants. All achieve only ~3 FPS —
the sheer volume of 4,800 TextBlock property sets per frame overwhelms the UI
thread. Reactor's GC pressure is dramatic: **51 Gen0 collections** in 10 seconds
(vs 0 for Direct/Bound). Each frame allocates 4,800 new Element records plus
arrays — at 3 FPS that's ~14,400 allocations/second. Peak memory is highest
for Reactor (339 MB vs 284 MB for Direct), confirming the allocation overhead.

**Analysis (10a — array pooling):** `RenderArena` rents `Element[]` children
arrays from `ArrayPool<Element>.Shared` and returns them after each render
cycle. Result: **no measurable impact** — Gen0 count is within noise (52 vs
51). The children arrays (one per Grid/VStack call) are a negligible fraction
of total allocation pressure. The dominant cost is the ~33,600 individual
Element record + modifier objects allocated per frame, not the handful of
container arrays. 10a is architecturally correct but solves the wrong
bottleneck.

**Analysis (10b — direct construction with cached modifiers):** The fluent
chain `Text().FontSize().Foreground().Grid()` creates **7 heap objects per
cell** (4 intermediate TextElement `with`-copies, 1 ElementModifiers, 1
GridAttached, 1 Dictionary). `TextDirect()` replaces this with a single
object-initializer construction, caching the immutable ElementModifiers (per
color) and GridAttached dictionary (per grid position) across frames. Result:
**Gen0 collections dropped 33%** (51 → 34), **peak memory dropped 26 MB**
(339 → 313 MB), and avg update time improved 19% (0.21 → 0.17 ms). The
remaining 34 Gen0 collections come from per-frame string allocations
(`.ToString()`) and the 4,800 TextElement records themselves — to eliminate
those, Reactor would need a fundamentally different element representation
(e.g., Compose-style slot tables or arena-allocated flat buffers).

---

## Implementation Priority (Revised — based on measured data)

Original priorities were based on hypothesis. The actual baselines changed the
picture significantly. Key surprises:

- **GC pressure is the #1 cross-cutting problem** — Reactor shows 10–100× more Gen0
  collections than Direct in every heavy scenario. Originally ranked last, now
  elevated to Phase 1.
- **Interactive control mount is 20× slower** — clearest, most unambiguous gap.
- **EXP-9 (Deferred Mount) is already solved** — Reactor's architecture naturally
  defers unmounted content. No optimization needed.
- **EXP-1 and EXP-3 overlap** — both are "skip unchanged subtrees" at different
  layers. Merge into one effort, retest at a heavier scale.
- **EXP-4 (Off-Thread) instrumentation is broken** — BeginUpdate/EndUpdate only
  captures SetState, not the render loop. Can't draw conclusions yet.

```
Phase 1 — Biggest measured gaps (pursue immediately)
  EXP-6  Interactive Control Pooling     ██████████  20× mount gap — blocks real-world scrolling
  EXP-10 Arena Allocation (array pool)   █████████   GC is the common bottleneck in 7/10 experiments
  EXP-2  Property Diff Bitmasks          ████████    2.3× FPS gap at 4,800 cells, compounds with GC

Phase 2 — Retest with heavier workload, then implement
  EXP-1+3  Dirty Tracking + Structural   ███████     Merge into one "skip unchanged subtrees" opt.
           Sharing (combined)                        Current 200-element test is too light — scale to 4,800

Phase 3 — Architectural (only if Phase 1–2 aren't sufficient)
  EXP-5  Mutation Journaling             █████       Enabler for EXP-7; not a perf win on its own
  EXP-7  Time-Sliced Reconciliation      █████       19 anim drops vs 5 — real but requires EXP-5 first

Deprioritized — pursue late or revisit
  EXP-8  Prioritized State Updates       ███         46ms vs 22ms gap is real but modest; complex impl
  EXP-4  Off-Thread Tree Building        ██          Instrumentation doesn't capture real bottleneck;
                                                     revisit after journaling enables proper measurement

Cut
  EXP-9  Lazy Off-Screen Mounting        —           Reactor already wins (1.4ms mount, 139MB vs 218MB).
                                                     Architecture provides this for free.
  EXP-3  (as standalone)                 —           Folded into EXP-1 combined effort.
```

### Key insight from baselines

Reactor's `BeginUpdate/EndUpdate` instrumentation only measures the `SetState` call,
not the subsequent reconcile pass in the render loop. This makes Reactor's "Avg Update"
numbers misleadingly low (0.04–0.96ms) when the real per-frame cost is 50–185ms.
**Before implementing optimizations, fix the instrumentation** to measure the full
render loop cost (tree build + reconcile + apply) so we can accurately measure
improvement.

---

## Test Infrastructure Requirements

Each experiment produces a standalone benchmark app following the StressPerf
pattern:

```
tests/perf_bench/
├── PerfBench.Shared/           # Extended PerfTracker (GC metrics, input latency)
├── PerfBench.DirtyTracking/
│   ├── DirtyTracking.Direct/   # WinUI3 baseline
│   ├── DirtyTracking.Bound/    # WinUI3 + data binding baseline
│   └── DirtyTracking.Reactor/     # Reactor variant (toggle optimization on/off via flag)
├── PerfBench.PropertyDiff/
│   └── ...
└── ...
```

### Extended PerfTracker Metrics

Beyond the existing FPS / Update ms / Memory metrics, experiments need:

| Metric | How | Used By |
|---|---|---|
| UI thread blocked ms | `Stopwatch` around reconcile-only portion | EXP-4 |
| Input latency ms | Timestamp in KeyDown → timestamp when TextBlock updates | EXP-4, EXP-8 |
| GC Gen0/Gen1 collections | `GC.CollectionCount(n)` delta per interval | EXP-10 |
| GC pause time | `GC.RegisterForFullGCNotification` or ETW | EXP-10 |
| Mutation count | Counter in journal | EXP-5 |
| Longest frame block | `CompositionTarget.Rendering` delta max | EXP-7 |
| Animation frame drops | Count frames where delta > 20ms | EXP-7 |

### CLI Flag for A/B Toggle

Reactor benchmark apps accept `--optimization on|off` to enable/disable the
specific experiment, allowing A/B comparison in a single binary:

```bash
# Current behavior (optimization off)
PerfBench.DirtyTracking.Reactor.exe --headless --optimization off --duration 10

# With optimization
PerfBench.DirtyTracking.Reactor.exe --headless --optimization on --duration 10
```
