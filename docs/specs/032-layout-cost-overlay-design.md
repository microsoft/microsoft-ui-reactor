# Layout Cost Overlay — Design Spec

## Status

**Draft** — 2026-04-24.

---

## Overview

A new devtool overlay that shows, for each mounted Reactor `Component`
subtree, three live numbers: **layout time**, **authored element count**,
and **rendered element count**. Data comes from the existing
`Microsoft-Windows-XAML` ETW provider — no WinUI source changes required.
Rendering uses the Composition visual layer (same technique as the
reconcile-highlight overlay in #88) so the overlay does not participate in
reconcile or layout.

Primary anchor is the Reactor Component boundary, with secondary modes for
on-demand drill-down and auto-surfaced hotspots. The overlay is gated
behind a new feature flag and surfaced as a new `DevtoolsMenu` toggle; it
is fully independent from the reconcile-highlight overlay.

The feature's long-term home is WinUI itself. This spec ships the capability
in Reactor first because Reactor has the Component boundary that makes the
numbers meaningful; the ETW + rendering pieces are designed to be lifted
upstream unchanged.

---

## Motivation

WinUI developers have no visibility into the gap between "elements I
authored" and "elements WinUI actually materialized." A single `Button`
expands through its `ControlTemplate` into ~10 elements
(`Border`, `Grid`, `ContentPresenter`, `TextBlock`, …). A screen with 50
authored elements can render 800. The developer never sees this, and never
sees which of their components is paying the cost of that expansion.

Existing signals:

- **`DebugSettings.EnableFrameRateCounter`** — one global FPS number; no
  attribution.
- **Live Visual Tree descendant count** — count is available but is a
  design-time inspection tool, not a live runtime signal; no timing.
- **ETW / WPA XAML plugin** — has everything we need but requires an ETL
  capture, an admin-elevated session, and manual analysis.
- **Reactor's #88 reconcile-highlight overlay** — shows *which* elements
  Reactor touched, not what layout cost them downstream.

The dev-loop friction on all of the above is high enough that most devs
ship without ever looking. The layout cost overlay collapses the loop to
"toggle a switch, read a number on the element that's slow."

---

## Goals

1. **Per-Component layout attribution.** Surface layout time (measure + arrange,
   EMA) rolled up to the Component subtree. A developer should be able to point
   at a badge and know exactly which of their Components it belongs to.
2. **Template-inflation visibility.** Show authored vs rendered element count
   side by side. The ratio is the "wait, really?" moment we want to create.
3. **Zero WinUI source changes.** The ETW provider already emits
   `MeasureElementBegin`/`End` and `ArrangeElementBegin`/`End` in
   `dxaml/xcp/plat/win/desktop/Microsoft-Windows-XAML-ETW.man` with ElementId
   and bounds. Consume those; don't add new events.
4. **User-mode, no admin.** Start the ETW session from inside the app.
   Target user-mode `Performance Log Users` membership at worst; target no
   elevation requirement for the common dev-box case.
5. **Zero cost when off.** Feature flag gates the ETW session, the
   per-frame attribution loop, and the Composition overlay. When the flag
   is false, no session is started and no events flow.
6. **No self-measurement.** The overlay's own visuals must not appear in
   its own data. Composition-layer rendering with no `TextBlock`s keeps
   it entirely out of the XAML layout pipeline.
7. **Designed to upstream.** The attribution logic, ETW consumer, and
   overlay visual primitives are scoped so they can move into WinUI
   without API churn.

## Non-goals

- **Not** per-UIElement badges. "A badge on every TextBlock" is too noisy
  to be useful and would dominate the scene. Per-Component + drill-down
  covers the actionable surface.
- **Not** a full profiler UX. No history, no diffing, no flame graphs, no
  export. Live numbers only. If that's not enough, the user falls back to
  WPA.
- **Not** a new ETW provider. We consume the existing
  `Microsoft-Windows-XAML` provider only.
- **Not** bindings / data-template / styles attribution. This spec is
  measure + arrange only. Other cost axes are covered by
  `023-perf-insight-tools.md`.
- **Not** a fork of the reconcile-highlight overlay. New flag, new
  Composition container, new menu toggle. They share the wrapper Canvas
  infrastructure from `HighlightOverlayWiring.cs` but are otherwise
  independent.
- **Not** a design-time tool. Runtime overlay only.
- **Not** a replacement for `DebugSettings.EnableFrameRateCounter`. That
  stays as the global readout; this is per-subtree.

---

## Background: the ETW events we rely on

From `Microsoft-Windows-XAML-ETW.man`:

| Symbol                | Task            | Value | Keyword   | Level     | Payload                                                             |
|-----------------------|-----------------|-------|-----------|-----------|---------------------------------------------------------------------|
| `MeasureElementBegin` | `MeasureElement`| 47    | `Detailed`| `Verbose` | `ElementId` (UInt64), `AvailableWidth` (float), `AvailableHeight` (float) |
| `MeasureElementEnd`   | `MeasureElement`| 48    | `Detailed`| `Verbose` | `ElementId` (UInt64), `DesiredWidth` (float), `DesiredHeight` (float) |
| `ArrangeElementBegin` | `ArrangeElement`| 49    | `Detailed`| `Verbose` | `ElementId` (UInt64), final rect                                    |
| `ArrangeElementEnd`   | `ArrangeElement`| 50    | `Detailed`| `Verbose` | `ElementId` (UInt64), final rect                                    |

Emitted from `dxaml/xcp/core/core/elements/uielement.cpp` around lines
4100 / 4124 / 4420 / 4448.

Provider GUID: `{531A35AB-63CE-4BCF-AA98-F88C7A89E455}`
(`Microsoft-Windows-XAML`). The events fire only when a consuming session
enables the provider at the `Detailed` keyword / `Verbose` level. No
session, no events.

`ElementId` is the `CUIElement*` native pointer cast to UInt64 —
stable for the element's lifetime, reused after free. The `Arrange*` events
carry final bounds in root-relative coordinates, which is how we attribute
events to Components by spatial rollup (§ Attribution strategy).

---

## Design

### Feature flag and menu toggle

Add `ReactorFeatureFlags.ShowLayoutCost : bool` (default `false`) in
`src/Reactor/Core/ReactorFeatureFlags.cs`, alongside
`HighlightReconcileChanges`. Semantics match the existing flag:

- Read at host startup. Changes after host initialization require a
  teardown/restart of the host to take effect. Tests save and restore.
- Flag controls three things: ETW session lifetime, attribution loop, and
  overlay rendering. All-or-nothing.

Add a menu item to the built-in `DevtoolsMenu`:
`"Show layout cost overlay"`, as a sibling of the
`"Highlight reconcile changes"` toggle added in #83. The two toggles are
independent — enabling one does not affect the other.

### Anchor model

Three modes, cycled through the menu item or via a `Ctrl+Shift+L` keybind
when the flag is on:

1. **Components (default).** One badge per mounted Component instance,
   anchored at the top-right corner of the Component's subtree bounding
   rect. A Component's badge subsumes inner Components unless an inner
   Component exceeds a *surface-through* threshold (see below). This keeps
   the root `App` Component's badge from visually smothering everything.

2. **Heatmap.** No Component-boundary bias. Auto-select the top *N*
   (default 10) subtrees by either layout ms or inflation ratio exceeding
   a threshold, regardless of depth. Badges on those only. Useful for
   "where is the problem right now" when the dev has no specific
   hypothesis.

3. **Inspect-only.** No badges drawn by default. Cursor hover walks up to
   the nearest Component boundary and shows that Component's badge; click
   to pin, click again to unpin. Useful for exploration in dense scenes.

Mode defaults to Components when the flag is first enabled in a session.
Mode is not persisted across sessions — devtools state is ephemeral.

#### Surface-through rule (Components mode)

A parent Component's badge hides all descendant-Component badges *unless*
a descendant's numbers exceed either:

- Layout ms > 50% of parent's layout ms, or
- Rendered count > 50% of parent's rendered count, or
- Inflation ratio > 2× parent's ratio.

A surfaced descendant is drawn at its own subtree's corner, in addition
to the parent's badge. This collapses uninteresting nesting (a 200-element
Component that's 99% of a 202-element parent is drawn once) while letting
hotspots punch through the default rollup.

Thresholds are constants for v1. Making them user-configurable is a v2
consideration — see [Open questions](#open-questions).

### Badge content — meter, not text

The badge is a small fixed-size box with two horizontal bars inside. No
text in the idle view. Text appears only on hover / pin (§ Detailed-view
pin), where it's rendered lazily and excluded from attribution.

```
 ┌──────────────┐
 │ ███░░░░░░░░░ │  ← layout ms  (fill length = time, color = ms ramp)
 │ ██▓▓▓▓▓▓░░░░ │  ← element count (gray = authored, color = inflation tail)
 └──────────────┘
      32 × 14 px
```

Dropping text is the whole point: no `TextBlock` means no layout pass for
the overlay's own chrome, no `DirectWrite`/Win2D interop research, and no
`IsOverlayChrome` exclusion bookkeeping. The meter is pure
`ShapeVisual` / `SpriteVisual` rectangles.

#### The two bars

**Top bar — layout ms.** Fill length maps the subtree's EMA
(measure + arrange) time linearly from 0 to a 33 ms ceiling (one 30 Hz
frame). Values above 33 ms clamp to full. Fill color is chosen from the
ms ramp:

- green ≤ 2 ms
- yellow ≤ 8 ms
- orange ≤ 16 ms
- red > 16 ms

**Bottom bar — element count, bicolored.** Total fill length maps
`rendered` on a log₁₀ scale, from 1 to 10,000 elements (`log(n+1) /
log(10001)`, clamped). The bar is split in two:

- Left portion, length proportional to `log(authored+1)` — drawn gray.
  This is "what you wrote."
- Right portion, length proportional to `log(rendered+1) − log(authored+1)`
  — drawn in the inflation-ramp color. This is "what WinUI added
  through template expansion."

The visual length of the colored tail *is* the inflation story. A dev
glancing at a row of meters can see at a glance which Components are
paying for template expansion and which aren't.

Inflation color ramp (applied to the tail only):

- green ≤ 3×
- yellow ≤ 8×
- orange ≤ 20×
- red > 20×

When `authored == rendered`, the tail has zero length and the bar is
fully gray — no inflation.

#### Box chrome

- Fixed 32 × 14 px. Bars are each 5 px tall with 1 px padding top/bottom
  and 1 px gutter between.
- Box background: `Color.FromArgb(200, 30, 30, 30)` — dark, semi-transparent,
  readable over any content.
- Box border: 1 px `Color.FromArgb(255, 80, 80, 80)` rounded rect, 2 px
  corner radius.
- Anchored at the Component subtree's top-right corner, offset −4 px
  inward. If the subtree rect is smaller than ~40 px wide or tall, the
  meter is suppressed — no room.
- Drawn as two Composition `SpriteVisual` bar fills (each sized by
  `Size` animation on flush) layered over a single `ShapeVisual`
  rounded-rect background. Six visuals per meter at most.

#### Data → visual mapping (reference)

```
msBar.Width      = boxInnerWidth * min(layoutMs / 33, 1)
msBar.Color      = ramp(layoutMs, msThresholds)

authoredFrac     = log(authored + 1) / log(10001)
renderedFrac     = log(rendered + 1) / log(10001)
authoredBar.Width = boxInnerWidth * authoredFrac
authoredBar.Color = gray
tailBar.X        = boxInnerWidth * authoredFrac
tailBar.Width    = boxInnerWidth * max(renderedFrac − authoredFrac, 0)
tailBar.Color    = ramp(rendered / max(authored, 1), inflationThresholds)
```

#### Detailed-view pin

Hover or click-to-pin reveals a textual readout next to the meter. Text
in this detailed view *is* a `TextBlock`, wrapped in a single
`Panel` tagged `LayoutCostOverlay.IsOverlayChrome = true`. The
attribution loop filters events from elements in any `IsOverlayChrome`
subtree, so the pin's own layout cost does not show up in its own
numbers. The pin is transient (one or zero visible at a time), so the
cost and the filter complexity are both bounded.

Readout:

```
MyStockGrid
  measure   2.1 ms
  arrange   1.1 ms
  authored  50
  rendered  847  (17.0×)
  frame     #12847
```

A 1 px outline traces the pinned subtree's bounds. Click outside to
unpin.

#### Legend

The `DevtoolsMenu` item has a flyout that explains the meter once:

```
  ▮▮▮░░   top bar     layout time  (0 → 33 ms)
  ▮▮▓▓░   bottom bar  elements     (gray = authored, color = inflation)
```

Shown on first enable per session. Dismissible.

### Data pipeline

```
  ETW (Microsoft-Windows-XAML)
       │
       ▼
  TraceEventSession (in-proc, user-mode)
       │  Measure/ArrangeElementBegin/End events
       ▼
  EventPairing (per-thread, per-ElementId)
       │  (ElementId, measureUs, arrangeUs, rect)
       ▼
  Attribution (spatial point-in-rect or pointer map)
       │  per-Component per-frame totals
       ▼
  EMA store (α=0.2)
       │
       ▼
  LayoutCostOverlay (Composition layer, post-layout flush)
```

#### ETW consumer

Use `Microsoft.Diagnostics.Tracing.TraceEvent` (the
`TraceEventSession` + `DynamicTraceEventParser` classes). In-process
session on the `Microsoft-Windows-XAML` provider at the `Detailed`
keyword, `Verbose` level.

Session lifecycle:

- **Start:** when `ShowLayoutCost` is flipped to true, in `ReactorHost`
  after the main window is shown. Session is private to the process
  (real-time, not ETL-backed).
- **Stop:** on flag flip-to-false, on host dispose, or on process exit
  (registered via `AppDomain.ProcessExit` as a last-resort cleanup).
- **Leak guard:** session name includes the current PID
  (`Reactor.LayoutCost.{pid}`). On startup, if a session with a matching
  *base* name (different pid, not ours) is detected, close it first. This
  matters for crashed dev processes leaving orphan sessions — ETW sessions
  outlive the process that started them.

Running as a non-admin user typically requires the account to be a member
of `Performance Log Users`. On most dev boxes the user is already a local
admin, so this works out of the box. On locked-down CI or pairing boxes
the session start will fail; we detect the failure, log a single warning
line via `Debug.WriteLine`, disable the overlay for the session, and show
a one-line "ETW session unavailable — overlay disabled" message in the
DevtoolsMenu. The flag stays on; the overlay just has nothing to show.

#### Event pairing

`Measure*` and `Arrange*` events are `opcode="win:Start"` / `win:Stop`
pairs in the manifest, but ETW does not auto-pair them — the consumer
must. Because measure/arrange are recursive (a parent's measure spans
its children's measures), we pair using a per-thread stack keyed on
`ElementId`:

```
onMeasureBegin(ElementId e, Timestamp t): push (e, t) onto measureStack[threadId]
onMeasureEnd  (ElementId e, Timestamp t): pop top; assert top.ElementId == e;
                                          emit (e, t - top.t) as measureUs[e]
```

Same pattern for Arrange. Self-time (excluding children) is
`totalTime − sum(childrenTime)` and is tracked alongside inclusive time
for the detailed-view breakdown.

The UI thread is the only thread that runs layout for a given window, so
`measureStack` is single-consumer per window. We don't need cross-thread
synchronization on the stack itself, but the handing-off from the ETW
callback thread (which *is* a background thread) to the per-window state
does need one lock per window. Keep it cheap: per-frame batch the raw
event tuples on the ETW thread into a ring buffer, drain it on the UI
thread once per render pass.

#### Event volume

Stress worst case (`StressPerf.Reactor` at 100%): 4,800 cells × 60 Hz ×
4 events = ~1.15M events/sec. `TraceEvent` has been measured to handle
~3–5M events/sec on modern hardware. Budget:

- ETW callback thread: ≤ 5% CPU of one core.
- UI thread per-frame drain + attribution: ≤ 0.5 ms/frame at 4,800
  elements.

If we miss the budget, the mitigation is to cap the ring buffer size
and drop the oldest events — the overlay degrades to "mostly right" under
overload, which is consistent with the EMA presentation. Do not drop
newest, because that biases the overlay to stale data.

### Attribution strategy

We need to map each `ElementId` back to a Reactor Component. Two
mechanisms, primary + fallback.

**Primary: pointer map.** For every `UIElement` Reactor creates, cache the
native `CUIElement*` via WinRT interop (`IUIElement7`'s
`ICoreObjectReference` → `get_NativePointer`, or the equivalent on
whatever interface is publicly available on lifted WinUI; final interop
path is a v1 research item). This gives Reactor a direct
`ElementId → UIElement → Component` lookup for elements it authored.

Template-expanded descendants (the Border / Grid / ContentPresenter
children of a lifted `Button`) are *not* in Reactor's tree, so the
pointer map misses them. That's what the spatial fallback covers.

**Fallback: spatial rollup.** On every `ArrangeElementEnd`, stash the
final rect (root-relative coords from the event payload) in an
`ElementId → Rect` map. Per-frame, we also maintain the bounding rect of
each mounted Component (derived from its authored UIElements'
`TransformToVisual(root).TransformBounds(new Rect(0,0,ActualWidth,ActualHeight))`).
Events whose `ElementId` is not in the pointer map are attributed to the
innermost Component whose bounding rect contains the event's rect center.

Pros: robust to template expansion, works for any element WinUI
materialized regardless of whether Reactor knows about it.
Cons: wrong for overflowing / clipped children (a popup opening outside
its anchor's bounds is attributed to whatever Component sits under its
screen position). Documented limitation for v1.

**Third case: events with no match.** An event whose `ElementId` is not
in the pointer map *and* whose rect doesn't sit inside any Component's
bounds is bucketed into a synthetic "Chrome" Component at the root. Its
badge is shown only in Heatmap mode, labeled `<chrome>`. This covers
system decorations, popup surfaces anchored off-screen, etc. Zero
attribution silently would hide real cost; a visible bucket is honest.

#### Authored count

Reactor's element tree (`Element.cs`, `Reconciler.Mount.cs`) already
tracks per-component authored elements. Add a property on the
per-Component reconciler state:
`AuthoredElementCount : int` — incremented on every `UIElement` mount
attributed to that Component, decremented on unmount. Cheap,
already-walked path; no new traversal.

### Rendering

Mirror the pattern from
`src/Reactor/Hosting/HighlightOverlayWiring.cs`:

- Reuse the same wrapper Grid + overlay Canvas setup. A second
  hit-test-invisible Canvas is added to the wrapper, sibling to the
  reconcile-highlight Canvas.
- A new `LayoutCostOverlay` class (analogous to
  `ReconcileHighlightOverlay`) owns a `ContainerVisual` inside the Canvas
  and manages one meter per visible Component.
- Each meter is 6 Composition visuals total: 1 `ShapeVisual` for the
  rounded-rect box background + border, 1 `SpriteVisual` for the layout-ms
  bar, 2 `SpriteVisual`s for the authored + inflation-tail bars, and 2
  reserved for the hover-outline / pin indicator. No `TextBlock`, no
  `DrawingSurface`, no `DirectWrite` — all fills are solid-color brushes.
- Flush updates the `Size` / `Offset` / `Brush.Color` of existing visuals
  in place where possible, rather than recreating them per frame.
- The hover / pin detailed readout is *not* a meter — it's a single
  `TextBlock` inside a `Border` that mounts lazily when a pin is
  requested and unmounts when dismissed. Wrapped in a `Panel` tagged
  `LayoutCostOverlay.IsOverlayChrome = true`; the attribution loop
  filters events whose `ElementId` belongs to any `IsOverlayChrome`
  subtree. At most one pin is visible at a time, so the filter set is
  small and the overhead is bounded.

#### Flush cadence

Post-layout, on each render pass, at `DispatcherQueuePriority.Low`. Same
throttle as `HighlightOverlayWiring` (80 ms minimum between flushes)
applies — the overlay is an "eventually fresh" view, not a
frame-accurate one.

#### Update cost budget

At 60 Hz with 200 mounted Components:

- Attribution drain: ≤ 0.5 ms/frame at steady state.
- Overlay rebuild: only badges whose numbers changed beyond a
  presentation-epsilon (e.g. ΔLayoutMs > 0.1, Δcount > 0) are re-rendered.
  No change = no Composition work.

### Interaction with the reconcile-highlight overlay

Both overlays coexist. When both flags are on, the reconcile-highlight
paints its striped rectangles as before, and the layout-cost overlay
paints badges on top. Badges are drawn at the Component's top-right
corner; reconcile stripes are subtree-wide. They do not compete for the
same pixels.

The two overlays' Canvases are both inside the wrapper Grid; `ZIndex`
orders them reconcile-below, cost-above so the cost badges are readable
over the stripes.

---

## Integration points

| Area                                          | Change                                                                                  |
|-----------------------------------------------|-----------------------------------------------------------------------------------------|
| `src/Reactor/Core/ReactorFeatureFlags.cs`     | Add `ShowLayoutCost` flag with doc comment.                                              |
| `src/Reactor/Hosting/HighlightOverlayWiring.cs` | Promote the wrapper-Grid / overlay-Canvas infrastructure to a shared `OverlayWiring` that owns two Canvases (reconcile + cost), or split into two siblings if the refactor is risky. |
| `src/Reactor/Hosting/LayoutCostOverlay.cs`    | New — Composition visuals, badge management, color ramp.                                |
| `src/Reactor/Hosting/Etw/LayoutEtwConsumer.cs`| New — ETW session, event pairing, ring buffer.                                          |
| `src/Reactor/Hosting/LayoutAttribution.cs`    | New — pointer map, spatial rollup, per-Component rollups, EMAs.                         |
| `src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs` | New menu item `"Show layout cost overlay"` (+ mode cycle submenu).              |
| `src/Reactor/Core/Reconciler.Mount.cs`        | Increment per-Component `AuthoredElementCount` on mount, decrement on unmount.          |
| `src/Reactor/Core/Element.cs`                 | Thread the owning Component identifier down to mount events (already partially present). |
| `samples/Reactor.TestApp/App.cs`              | Expose the overlay toggle via the sample's `DevtoolsMenu`.                              |

Binary dependency: `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet.
Already a small, well-known package (~1.5 MB managed); no native
dependencies. Added to `src/Reactor/Reactor.csproj` with
`<IncludeAssets>all</IncludeAssets>`.

---

## Performance

- **Flag off:** zero cost. No ETW session, no consumer, no overlay, no
  authored-count bookkeeping. The `AuthoredElementCount` increment is
  behind a `if (ReactorFeatureFlags.ShowLayoutCost)` check in the mount
  path; the reconciler's hot path gains one boolean check per mount.

- **Flag on, idle app:** ~0 events/sec from the provider (no layout
  activity), <0.1% CPU.

- **Flag on, StressPerf.Reactor at 100%:** worst case measured separately
  (v1 acceptance gate is "≤ 1 FPS regression vs flag-off on the Reactor
  100% scenario at 4,800 cells on an ARM64 dev box"). If we miss the
  gate, we ship with a documented recommended cell cap for the overlay.

- **Memory:** per-`ElementId` state is ~64 bytes (rect + EMA + type tag).
  At 10,000 rendered elements that's ~640 KB — fine. The pointer map
  grows with Reactor's element count and shrinks on unmount; no
  unbounded growth.

---

## Testing

Fixtures live in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`
following the pattern from `ReconcileHighlightTests.cs` (#83).

- `LayoutCostOverlay_Components_ShowsBadgePerComponent` — mount a scene
  with 3 nested Components, assert 3 badges in the overlay container,
  positioned at expected corners.
- `LayoutCostOverlay_Heatmap_LimitsBadgeCount` — mount 50 Components,
  assert ≤ 10 badges in Heatmap mode.
- `LayoutCostOverlay_Inspect_HoverRevealsBadge` — assert no badges
  default, hover event reveals the correct Component's badge.
- `LayoutCostOverlay_AuthoredVsRendered` — mount a single `Button`,
  assert authored = 1, rendered ≥ 5 (template expansion).
- `LayoutCostOverlay_Unmount_ClearsBadge` — mount/unmount, assert the
  badge is removed and per-Component state is freed.
- `LayoutCostOverlay_FlagOff_ZeroVisuals` — assert the overlay Canvas
  has zero children when the flag is false.
- `LayoutCostOverlay_SessionFailure_Graceful` — simulate
  `TraceEventSession` start failure, assert the overlay draws no badges
  and the menu shows the "unavailable" state.

Real ETW session tests run only on dev-box CI where the test account is
in `Performance Log Users`; gate them behind a
`[LayoutCostEtwFact]` attribute that skips when the session can't be
created. Mock ETW events are used for the attribution-logic tests.

---

## Open questions

1. **Native pointer interop path.** The WinRT surface for
   `UIElement → CUIElement*` on lifted WinUI is not documented in public
   headers. Confirm via `ABI` headers or the `unsealed` internal surface
   of the lift, or fall back entirely to spatial attribution (which works
   but is less accurate for clipped children).

2. **Color-ramp thresholds.** The green/yellow/orange/red cutoffs on ms
   and inflation ratio are opinionated guesses. Real thresholds should
   come from workload studies once the overlay is in devs' hands. For
   v1 they're compile-time constants in `LayoutCostOverlay.cs`;
   promote to `ReactorFeatureFlags` if tuning becomes frequent.

3. **Log-scale ceiling for the count bar.** 10,000 is the picked ceiling.
   Apps with genuinely enormous element counts (a million-cell virtualized
   grid) will pin the bar and lose resolution. The meter is a "glance"
   tool; for those cases the detailed-view pin (textual) is the fallback.
   Consider making the ceiling adaptive to the largest visible rendered
   count in v2.

4. **Surface-through thresholds.** The 50% / 2× thresholds for
   descendant-Component surfacing are first-principles, not measured.
   Expect to revise.

5. **Self-time vs inclusive time.** Meters show inclusive
   (measure + arrange) ms. The detailed-view pin could break out
   measure/arrange + self-vs-inclusive, but v1 leans no — the
   Component-rollup already approximates what a dev wants ("my
   Component, not its children"), and the meter has no room for a
   fourth bar.

6. **Multi-window apps.** One ETW session per process. Attribution needs
   per-`XamlRoot` Component roots to avoid cross-window bleed.
   Bookkeeping is straightforward but untested in v1 — add a
   multi-window fixture when that scenario lands.

7. **Authored count for Fragments.** Reactor's `Fragment` / array-return
   patterns don't produce a `UIElement` of their own. A Fragment child
   of a Component contributes zero to authored count but its descendants
   do. Verify this is the intuitive behavior (think: a Fragment is
   transparent).

8. **Orphaned ETW sessions on process crash.** ETW sessions outlive
   their starting process. The leak guard on startup mitigates but does
   not fully solve this. Worst case: an orphan session slowly accumulates
   events no one consumes, burning CPU. `logman query -ets | grep
   Reactor.LayoutCost` is a dev-side cleanup workaround; accept for v1.

9. **Color-blind friendliness.** The inflation-tail ramp relies on
   green / yellow / orange / red distinctions. Consider adding a
   hatch-pattern or stripe overlay on the tail at higher severity (the
   reconcile-highlight overlay already uses diagonal stripes for a
   similar reason). v1 ships color-only; revisit if dev feedback
   surfaces a problem.

---

## Upstream path

When this overlay moves into WinUI, the following parts lift cleanly:

- **Attribution-by-rect** generalizes to any logical subtree boundary,
  not just Reactor Components. A WinUI-native version anchors on any
  `FrameworkElement` whose `x:Name` is set, on the subtree under a
  `UserControl`, or on a dev-annotated element.
- **Composition-layer meter rendering** is framework-agnostic. The
  text-free design lifts unchanged — no DirectWrite / Win2D dependency
  to port.
- **ETW consumer + event pairing** is framework-agnostic.
- **Authored vs rendered count** requires a WinUI-side notion of
  "authored" — the distinction is less crisp for plain XAML (every
  element is authored; template expansion is what happens below). The
  upstream version likely replaces the bicolored bottom bar with a
  single log-scaled "subtree element count" bar, colored by absolute
  count thresholds. That's still valuable.

The Reactor-specific piece that does *not* upstream is the
Component-boundary anchor. WinUI has no equivalent logical primitive;
`UserControl` is the closest but too heavy. This is why the overlay
ships in Reactor first: the thing that makes the numbers meaningful
is Reactor-side.

---

## Implementation phases

1. **Phase 1 — Data pipeline.** ETW session start/stop, event pairing,
   ring buffer drain on UI thread. No overlay; just log totals to
   `Debug.WriteLine` on a timer. Validates event volume and session
   lifecycle.
2. **Phase 2 — Attribution.** Pointer map + spatial rollup, per-Component
   rollups, EMAs. Still no overlay; expose totals through an
   `ILayoutCostReporter` test interface.
3. **Phase 3 — Overlay (Components mode).** Composition visuals,
   meter rendering (2 bars per box, all solid-color brushes, no text),
   surface-through rule. No text-rendering research blocks this phase.
4. **Phase 4 — Modes (Heatmap, Inspect).** Mode cycling via menu item
   and keybind.
5. **Phase 5 — Polish.** Detailed-view pin, color-ramp tuning, menu
   telemetry, sample-app integration.

Phases 1–3 are the v1 scope. 4 and 5 are fast-follow.
