# Charting Accessibility — Design Spec

## Status

**Proposal** — pending review. Targets an **"A" grade** on charting accessibility: WCAG 2.2 AA
conformance for every built-in chart type out of the box, with keyboard, screen-reader, forced-colors,
and reduced-motion support on par with Highcharts / Power BI.

**Scope decisions (post-review):**
- **Sonification (was Layer 9) — deferred.** Nice-to-have ceiling feature; does not affect the
  "A" grade. Can be revisited if an app-team requests it.
- **"View as data table" — recast as app-level.** The framework does not ship a shadow
  `DataGrid` per chart. The real screen-reader win comes from Layer 1's UIA
  `IGridProvider` + `IValueProvider` exposure, which works without any visible table.
  What belongs in the framework is the **convention** (T key, focus save/restore, live-region
  announcement on toggle) — not the visible content. Apps plug in their own view via
  `.AlternateView(Element)`. See revised Layer 3.

Related specs: [006 Accessibility Design](006-accessibility-design.md),
[016 Native Chart Migration](016-native-chart-migration.md),
[024 AI-Agent DevTools](024-ai-agent-devtools.md).

---

## Problem Statement

Spec 016 migrated Microsoft.UI.Reactor's chart DSL to native element trees. That unlocked reconciliation,
theming, and animation — but accessibility was **explicitly left for a follow-up**. Today:

- `src/Reactor/Charting/` contains **zero** references to `AutomationPeer`, `AutomationProperties`,
  `IsTabStop`, `OnKeyDown`, or `LiveRegion`.
- Chart primitives (`D3Rect`, `D3LinePath`, `D3AreaPath`, `D3Pie`, `D3Circle`, `D3Path`) render as
  raw WinUI `Shape` instances with no accessible name, role, value, or description.
- The root chart elements (`ChartElement<T>`, `PieChartElement<T>`, `TreeChartElement<T>`,
  `ForceGraphElement`) expose no semantics — to a screen reader, a chart is a canvas-shaped void.
- No chart in the codebase supports keyboard navigation, tooltips-via-keyboard, data-table fallback,
  live-region updates, forced-colors palette remap, or reduced-motion.
- `AccessibilityScanner` has no chart-specific rules (A11Y_001–008 cover generic elements).
- `D3Charts.ChartForeground` / `ChartAxis` / `ChartGrid` brushes adapt to dark mode but **do not**
  listen to `AccessibilitySettings.HighContrast` / `UISettings.ColorValuesChanged`.

Meanwhile, Reactor's general accessibility infrastructure (spec 006) is already rich:

| Primitive | Location | Status |
|---|---|---|
| `ElementModifiers` (AutomationName, HelpText, Landmark, LiveRegion, PositionInSet, …) | `src/Reactor/Elements/ElementExtensions.cs:1024-1184` | Shipped |
| `SemanticPanel` + custom automation peer (`IRangeValueProvider`, `IValueProvider`) | `src/Reactor/Accessibility/SemanticPanel.cs:1` | Shipped |
| `AccessibilityScanner` (WCAG checks → AI-agent JSON) | `src/Reactor/Core/AccessibilityScanner.cs:1` | Shipped, no chart rules |
| `UseHighContrast` hook | spec 006 Layer 5 | Shipped |
| `UseReducedMotion` hook | spec 006 Layer 5 | Planned — not yet implemented |
| Keyboard modifiers (`IsTabStop`, `TabIndex`, `AccessKey`, `OnKeyDown`) | `ElementExtensions.cs:193-194,1024-1184` | Shipped |

So the gap is **charting-specific infrastructure**, not framework primitives. This spec designs
that infrastructure.

### The "A" Grade Bar

An "A" on charting accessibility means:

1. **Every built-in chart passes WCAG 2.2 AA** on the static-data-viz path with zero author work
   beyond `.AutomationName()` (or automatic derivation from title).
2. **Every interactive chart** (pan/zoom/filter/brush/legend-toggle) has a fully keyboard-operable
   equivalent and a debounced screen-reader narrative.
3. **AccessibilityScanner emits chart-specific rules** so authors (and their AI agents) get
   actionable feedback.
4. **Forced-colors and reduced-motion are honored by default** — no author opt-in required.
5. **On-demand "announce current view"** (S key) available, so users can replay the current
   live-region narrative regardless of debounce state.

---

## Goals

1. Zero-effort baseline: any chart using the native DSL is WCAG 2.2 AA capable with one line of
   author metadata (the chart's title/name).
2. Uniform surface across `ChartElement<T>`, `PieChartElement<T>`, `TreeChartElement<T>`,
   `ForceGraphElement`, and any future chart type.
3. UIA mapping aligned with the most powerful screen-reader pattern available: `IGridProvider` +
   `ITableProvider` + `IValueProvider`. Narrator/JAWS table commands "just work."
4. Keyboard conventions match Highcharts/Power BI so users with established muscle memory aren't
   retrained.
5. All chart infrastructure auto-reacts to `AccessibilitySettings.HighContrast`,
   `UISettings.AnimationsEnabled`, and Windows contrast themes without author opt-in.
6. AI-agent-friendly diagnostics (continuing spec 024 / scanner pattern) so an agent can auto-fix
   missing chart semantics.

## Non-Goals

- Replacing the chart DSL. This spec adds accessibility layers on top of the DSL introduced in
  spec 016; no data-model changes.
- Re-implementing WinUI `DataGrid`. The "view as table" fallback leverages existing Reactor grid
  primitives (spec 004 PropertyGrid / data system).
- Building a full screen-reader experience for the `ForceGraph` physics simulation — treat the
  live layout as a decorative view with a graph-structure table fallback.
- Supporting non-UIA assistive technology (AT-SPI, macOS). WinUI is the only surface today.

---

## Design Philosophy

- **Accessibility by default, author override when needed.** Defaults for name, description,
  per-point labels, keyboard bindings, focus indicator, live region debounce, and palette are
  all automatic. Authors only touch modifiers when domain knowledge matters (units, custom
  summary copy).
- **Accessors do double duty.** The same `Func<T, TX>` and `Func<T, TY>` that drive geometry also
  drive per-point screen-reader labels — no duplicate metadata to maintain.
- **Virtual focus, not per-point XAML elements.** A chart with 10,000 points cannot instantiate
  10,000 focusable `Rectangle` controls. A single `ChartKeyboardNavigator` overlays a virtual
  focus cursor and exposes the focused point via a single peer; UIA sees a grid of 10,000
  `IGridItemProvider` entries without paying the XAML cost.
- **One polite live region per chart, debounced.** Pan/zoom/filter/animate produce at most one
  announcement per settled state. Assertive is reserved for errors.
- **Scanner rules are structured and fix-suggesting.** Each chart-specific rule emits the same
  JSON shape as A11Y_001–008 so an AI agent can patch it.

---

## Architecture Overview

Eight layers. Each is independently shippable. Later layers depend on earlier ones.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Layer 8: Scanner rules (A11Y_CHART_001–007, 009–012)              │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 7: Forced-colors + reduced-motion integration               │
│  → palette remap, shape/dash double-encoding, animation snap       │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 6: Debounced live-region helper + on-demand "announce" (S)  │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 5: Viewport & overlay focus context                         │
│  → pan/zoom UIA, embedded-control tab order, focus save/restore    │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 4: ChartKeyboardNavigator — virtual focus + standard keys   │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 3: Alternate-view toggle convention (T key, app-supplied)   │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 2: Auto-generated chart summary + accessor-driven labels    │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 1: ChartAutomationPeer — UIA grid/table mapping             │
├─────────────────────────────────────────────────────────────────────┤
│  Layer 0: Existing Reactor accessibility infra (spec 006)          │
└─────────────────────────────────────────────────────────────────────┘
```

Sonification is out of scope for this spec. See Status.

---

## Layer 1: `ChartAutomationPeer`

One shared peer drives every chart. Subclass `FrameworkElementAutomationPeer`, attach to the root
`Canvas` (or `XamlHostElement` for `ForceGraph`), and expose:

| UIA Pattern | Provider | Role |
|---|---|---|
| `Group` (control type) | root | Chart container. Name = `.AutomationName()` (or auto from `.Title()`). HelpText = `.Description()`. FullDescription = auto-generated summary (Layer 2). |
| `IGridProvider` | root | `RowCount` = series count, `ColumnCount` = max points per series. |
| `ITableProvider` | root | Row headers = series names; column headers = x-axis categories/ticks. `RowOrColumnMajor` = `RowMajor`. |
| `IGridItemProvider` | each point | `Row` = series index, `Column` = point index. |
| `ITableItemProvider` | each point | Exposes row/column headers back to Narrator. |
| `IValueProvider` | each point | `Value` = human-readable (e.g., `"$42,300 on March 14"`); `IsReadOnly = true`. |
| `ISelectionItemProvider` | each point (interactive charts only) | Focus = selected. |
| `IInvokeProvider` | each point (interactive charts only) | Drill-down handler. |
| `IRangeValueProvider` | each axis | Exposes current min/max, small/large change. Drives pan/zoom via automation. |
| `IScrollProvider` | plot area (interactive only) | `HorizontalScrollPercent`, `HorizontalViewSize`. |
| `ISelectionProvider` | legend | Multi-select of visible series. |

Peer is driven from the **virtual element tree** the chart DSL produces, not from post-render
`Canvas.Children`. This keeps it in lockstep with reconciliation.

**Events fired:**

- `AutomationEvents.PropertyChanged` on data, viewport, or filter change.
- `AutomationEvents.LiveRegionChanged` (Polite) on debounced view-state transitions (Layer 6).
- `AutomationEvents.SelectionItemPatternOnElementSelected` on focus change (interactive charts).

**File layout:**

```
src/Reactor/Charting/Accessibility/
  ChartAutomationPeer.cs         — root peer, grid/table providers
  ChartPointProvider.cs          — per-point provider (value, grid-item)
  ChartAxisProvider.cs           — axis range provider
  ChartLegendProvider.cs         — legend selection provider
  IChartAccessibilityData.cs     — interface the DSL implements to feed the peer
```

Each chart element (`ChartElement<T>` etc.) implements `IChartAccessibilityData`:

```csharp
internal interface IChartAccessibilityData
{
    string? Name { get; }
    string? Description { get; }
    IReadOnlyList<ChartSeriesDescriptor> Series { get; }
    IReadOnlyList<ChartAxisDescriptor> Axes { get; }
    ChartViewport? Viewport { get; }       // null for non-interactive
}
```

---

## Layer 2: Accessor-driven labels + auto-summary

### 2.1 New chart modifiers

Add to `ChartElement<T>`, `PieChartElement<T>`, `TreeChartElement<T>`:

```csharp
public ChartElement<T> Title(string title);                       // visible + accessible name
public ChartElement<T> Description(string fullDescription);       // overrides auto-summary
public ChartElement<T> SeriesName(string name);                   // single-series shorthand
public ChartElement<T> SeriesNames(params string[] names);        // multi-series
public ChartElement<T> DataLabel(Func<T, int, string> labeller);  // per-point override
public ChartElement<T> Units(string xUnits, string yUnits);       // "months", "US dollars"
public ChartElement<T> AxisLabel(ChartAxis axis, string label);   // x/y axis names
```

Default per-point label (when `.DataLabel()` not set):

```
"{seriesName}, {xLabel}: {yValue}{yUnits}, point {i} of {n}"
```

Example:

```csharp
AreaChart(revenue, d => d.Month, d => d.Revenue)
    .Title("Monthly revenue, 2025")
    .SeriesName("Revenue")
    .Units(xUnits: "month", yUnits: "US dollars");
// Each point announces: "Revenue, March: 42,300 US dollars, point 3 of 12"
```

### 2.2 `ChartSummarizer`

Utility that, given `IEnumerable<T>` + accessors + descriptors, returns a structured summary:

```csharp
public record ChartSummary(
    string Overview,       // "Line chart, 2 series, 12 points each."
    string AxisRanges,     // "X from January to December. Y from 12,000 to 187,000 USD."
    string[] SeriesStats,  // ["Revenue: min 12,000 Jan, max 187,000 Nov, generally increasing."]
    string[] Outliers,     // ["November revenue 187,000 is 2.3× the mean."]
    string TrendVerdict);  // "Generally increasing; seasonal peak in Q4."
```

Wired to `AutomationProperties.FullDescription` by default. Trend detection uses a simple
Mann-Kendall test + autocorrelation for seasonality; good enough for screen-reader summary.

### 2.3 Auto-name derivation

If `.AutomationName()` / `.Title()` not set, derive from:

1. Parent `Section(title: ...)` if any.
2. Preceding `Heading()` in the same Stack.
3. Fallback: `"{ChartType} chart with {seriesCount} series"`.

Matches spec 006's derivation rules for form fields.

---

## Layer 3: Alternate-view toggle convention

A sighted "view as table" is inherently domain-specific (which columns, what formatting,
whether to allow sort/filter, how to show computed fields). The framework does **not** ship a
one-size-fits-all `DataGrid` view. Instead it provides the **contract** — keyboard binding,
focus behavior, live-region announcement, UIA wiring — so any app-supplied element plugs in
consistently.

The screen-reader accessibility of the chart data does **not** depend on this feature. Layer 1's
`IGridProvider` + `IValueProvider` already exposes per-point data structurally; Narrator/JAWS
table commands work without any visible table. Layer 3 is about giving sighted users (including
low-vision and cognitive-load users who benefit from tabular data) a consistent way to ask for
an alternate view.

### 3.1 API

```csharp
AreaChart(...)
    .AlternateView(DataGrid(revenue)
                     .Column("Month", d => d.Month)
                     .Column("Revenue", d => d.Revenue, format: "C0"));
```

`.AlternateView()` accepts any `Element`. Common choices: a `DataGrid`, a `SummaryCard`, a
`SparklineGrid`, or a hand-rolled layout. The framework is indifferent to content.

### 3.2 Behavior (when `.AlternateView()` is set)

- **T** key (and `Alt+Shift+F11` for Power BI parity) toggles between chart view and alternate
  view. Keyboard binding registered via the `ChartKeyboardNavigator` (Layer 4).
- Toggle preserves focus context: if focus was on a data point, re-entering chart view restores
  it (Layer 5 `ChartFocusContext`).
- Toggle announces state via the chart's live region (Layer 6): `"Showing data table"` /
  `"Showing chart"`.
- When alternate view is active, the chart is pruned from the UIA tree via
  `AutomationProperties.AccessibilityView = Raw` (Reactor's `.AccessibilityHidden()` modifier,
  see spec 006) to avoid double-announcement. Visual XAML `Visibility` is unchanged.
- If `.AlternateView()` is **not** set, the T key is a no-op (not an error). Nothing is
  synthesized.

### 3.3 What the framework does not own

- No default table template, column picking, sorting, filtering, or formatting.
- No coupling between the chart's filter/sort/viewport state and the alternate view's state —
  apps decide whether to mirror (Power BI pattern) or expose raw data (Excel pattern).
- No CSV/Excel export. Apps can wire `.AlternateView(DataGrid(...).Exportable())` themselves.

### 3.4 Guidance to app authors

Docs / samples should show the canonical pattern: an app-supplied `DataGrid` whose bindings
mirror the chart's accessors, wrapped in `.AlternateView()`. The AI-agent-friendly part of
the story lives in the scanner (Layer 8) and docs, not the framework runtime.

---

## Layer 4: `ChartKeyboardNavigator`

One reusable controller attached to every interactive chart.

### 4.1 Virtual focus

- Chart root is a single focusable `Canvas` (`.IsTabStop(true)`, `.TabIndex(...)` assigned by
  natural document order).
- When focus enters, a virtual focus cursor renders as a `D3Rect` overlay (or ring for pie slices,
  circle for scatter points) positioned over the current point.
- Navigator holds `{seriesIndex, pointIndex}` state; no per-point XAML elements.
- Focus indicator meets **WCAG 2.4.13**: double-ring (light + dark stroke) so contrast ≥ 3:1
  against any background; perimeter ≥ 2 px.

### 4.2 Standard key bindings

| Key | Action |
|---|---|
| Tab / Shift+Tab | Enter / leave chart. Inside: toolbar → legend → plot → overlays (Layer 5). |
| ← / → | Previous / next point in current series |
| ↑ / ↓ | Switch to adjacent series (snap to nearest x-index) |
| Home / End | First / last point in current series |
| Ctrl+Home / Ctrl+End | First / last point across all series |
| Enter / Space | Invoke (drill-down, open tooltip explicitly) |
| Shift+← / → | Extend brush selection |
| + / − or Ctrl+= / Ctrl+− | Zoom in / out, centered on focused point |
| Ctrl+0 | Reset zoom |
| Alt+← / → / ↑ / ↓ | Pan |
| L | Focus legend |
| Space (on legend item) | Toggle series visibility |
| T or Alt+Shift+F11 | Toggle alternate view (no-op if `.AlternateView()` not set — Layer 3) |
| S | Speak summary / replay current view announcement (Layer 6) |
| Shift+? / F1 | Open keyboard help dialog |
| Esc | Leave current mode (exit pan, close tooltip); second Esc leaves chart |

### 4.3 API

```csharp
LineChart(...)
    .Interactive()                         // turns on the navigator (default off for static charts)
    .OnPointInvoke((d, i) => ShowDrill(d)) // Enter/Space + click
    .OnBrushChanged(range => filter = range);
```

`.Interactive()` is implicit when any of `.Pan()`, `.Zoom()`, `.Brush()`, `.OnPointInvoke()`,
or `.IsTextSelectionEnabled()` is used.

---

## Layer 5: Viewport + overlay focus context

### 5.1 Viewport semantics

Plot area gets:

- `AutomationProperties.Name = "Plot area"` (localizable).
- `AutomationLiveSetting` bound to the Layer 6 announcer.
- UIA `IScrollProvider` (if pan-enabled) + `IRangeValueProvider` on each axis.
- `AutomationProperties.ItemStatus` bound to current filter summary
  (e.g., `"Filtered: March 1 to March 14, 12 of 365 points"`).

### 5.2 Embedded-control tab order

When overlays (legend toggles, slider, brush handles, annotations) exist inside the chart:

| Tier | Elements | Notes |
|---|---|---|
| 1 | Title, toolbar | Skip-links to `.AutomationLandmark()`ed regions. |
| 2 | Legend | `role="group"` with `AutomationName("Legend")`. Announces "Legend, 4 items" on entry. Items are `role="switch"`, use `ISelectionItemProvider`. |
| 3 | Plot area | Single tab stop; arrow-key navigation internal (Layer 4). |
| 4 | Overlaid controls | Real focusable elements with their own name/role/value. Slider uses `IRangeValueProvider` + `aria-valuetext`-equivalent (`UIA.ValuePattern.Value`) with units. |

Framework provides `ChartFocusContext` that:

- Saves `{seriesIndex, pointIndex}` when Tab leaves the plot area into an overlay.
- On **Esc**, returns focus to the saved point and re-announces it.
- On data/filter change, if the saved point is filtered out, moves to nearest surviving point in
  same series and emits a polite-announcement: `"Point filtered out; focus moved to March 10."`

### 5.3 Decoration pruning

All `D3Charts` decoration primitives (grid lines, tick marks, minor axes, background) auto-set
`AccessibilityView = Raw` unless they carry meaningful data. Keeps the UIA tree clean.

---

## Layer 6: Debounced live region + on-demand announce

### 6.1 `ChartLiveAnnouncer`

One polite live region per chart. Trailing debounce 400 ms. Collapses bursts to one message.
Messages are generated by the same `ChartSummarizer` used for the static summary, but
parameterized by *what changed*:

| Event | Message template |
|---|---|
| Zoom | `"Zoomed. Showing {m} of {n} points, {xStart} to {xEnd}."` |
| Pan | `"Panned. Showing {xStart} to {xEnd}."` |
| Brush | `"Selected {m} points, {xStart} to {xEnd}."` |
| Filter | `"Filter applied: {filterDescription}. {m} of {n} points visible."` |
| Data update | `"Data updated. {newN} points."` |
| Series toggle | `"{seriesName} {hidden|shown}. {visibleCount} series visible."` |
| Cross-chart | `"Filtered by selection from {sourceChartName}: {m} of {n} visible."` |

Assertive is reserved for errors: `"No data in selected range."`

### 6.2 "Announce current view" (S)

Escape hatch for users who missed or silenced the auto-announcement. Re-speaks the full current
view summary regardless of debounce state. Does not interrupt an in-progress announcement
(queues instead).

### 6.3 Suppression during animation

While an animation is in flight, intermediate transitions don't announce. Only the settled state
does. Combined with reduced-motion (Layer 7), this means reduced-motion users hear the state
immediately and full-motion users hear it ~200ms later after the tween completes.

---

## Layer 7: Forced-colors + reduced-motion + double-encoding

### 7.1 Forced-colors palette

Extend `D3Charts` brushes. `IsForcedColors` follows the same per-render `[ThreadStatic]` pattern
as the existing `IsDarkTheme` flag (`src/Reactor/Charting/D3Charts.cs:52`) so multi-window hosts
can render concurrently with different accessibility settings without cross-talk:

```csharp
// src/Reactor/Charting/D3Charts.cs
[ThreadStatic] private static bool _isForcedColors;
public static bool IsForcedColors
{
    get => _isForcedColors;
    set => _isForcedColors = value;
}

public static SolidColorBrush ChartSeries(int seriesIndex);
public static DashStyle   ChartSeriesDash(int seriesIndex);
public static MarkerShape ChartSeriesMarker(int seriesIndex);
```

Host applications set `IsForcedColors` once per render pass, matching the existing
`IsDarkTheme` pattern. A `RenderContext` hook reads `AccessibilitySettings.HighContrast` and
propagates it automatically.

When `IsForcedColors`:

- Series 1 → `CanvasText`, Series 2 → `Highlight`, Series 3 → `LinkText`, Series 4 → `GrayText`.
- Clip palette to 4 colors; collisions beyond that force shape/dash to carry the signal.
- Axes/labels → `CanvasText`. Selection → `Highlight` / `HighlightText`. Disabled → `GrayText`.

Listen for `AccessibilitySettings.HighContrastChanged` and
`UISettings.ColorValuesChanged` at startup; re-theme live on change.

### 7.2 Double-encoding always on

Every series carries **three** distinguishing signals: color + marker shape + line dash pattern.
Authors opt **out** of this (e.g., minimalist dashboard) via `.ColorOnly()`, which triggers a
scanner warning.

Default shape cycle: circle, square, triangle, diamond, plus, cross, star, hexagon.
Default dash cycle: solid, `4-2`, `2-2`, `6-2-2-2`, `8-4`, `1-1`.

Default palette: **Okabe-Ito** (colorblind-safe 8-color set).

### 7.3 Reduced motion

`ChartAnimator` wraps every chart transition (data entry/exit, zoom tween, pan inertia,
force-graph simulation). On `UISettings.AnimationsEnabled == false` or
`SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) == false`:

- Skip entrance/exit animations; snap to final.
- Disable inertia on pan; snap on release.
- Terminate force-graph simulation at cooled state immediately; no iterative render.
- Keep ≤ 150 ms opacity fades only (WCAG 2.3.3 tolerance).

### 7.4 Focus indicator contrast

`ChartKeyboardNavigator` focus ring uses a double-ring (1px dark + 1px light) that guarantees
3:1 contrast against any chart background, including overlapping series. Ring geometry: 2 px
perimeter + 2 px gap minimum.

### 7.5 Hit target expansion

Point markers smaller than 24×24 px get a transparent `D3Rect` hit shape sized 24×24, centered
on the marker, when the chart is `.Interactive()`. WCAG 2.2 §2.5.8.

### 7.6 Palette customization

Developer customization is a first-class requirement (brand identity, design systems), but the
"A" grade pledge — 3:1 pairwise contrast, 3:1 against background, colorblind-safe, forced-colors
remap, shape/dash double-encoding — is non-negotiable. Resolution: separate **palette definition**
from **palette use**, run all validation at the definition step, and make the safe paths the
path of least resistance.

#### 7.6.1 Tier 1 — Curated palettes (recommended default)

Ship a set of pre-vetted palettes as `ChartPalette` constants. Each is verified offline for
pairwise contrast (≥ 3:1 WCAG non-text contrast), colorblind-safe separation under
deuteranopia/protanopia/tritanopia simulation (ΔE ≥ 10 between any two series), and contrast
≥ 3:1 against both `ChartBackground` variants (light + dark theme).

```csharp
public sealed class ChartPalette
{
    private ChartPalette(Color[] colors, string name) { /* ... */ }

    public static ChartPalette OkabeIto      { get; }  // 8 colors, colorblind-safe
    public static ChartPalette IBM           { get; }  // 5 colors, colorblind-safe
    public static ChartPalette Viridis       { get; }  // sequential, perceptually uniform
    public static ChartPalette Cividis       { get; }  // sequential, colorblind-friendly
    public static ChartPalette FluentDefault { get; }  // matches Reactor theme tokens

    public static HardenResult Harden(Color[] input, HardenOptions? options = null);
}

LineChart(...).Palette(ChartPalette.OkabeIto);
```

`ChartPalette` is a sealed class with a private constructor, so the only way to obtain one is
the curated static set (Tier 1) or as the output of `Harden()`. `ChartPalette.Harden(...)`
lives on the same type to keep the one-import story clean.

Default when no `.Palette()` is set: `ChartPalette.OkabeIto`.

#### 7.6.2 Tier 3 — Raw colors, scanner-validated

Developer supplies explicit series colors. Framework accepts and renders them, but the scanner
runs every check and emits violations with specific, actionable fixes.

```csharp
LineChart(...).SeriesColors(
    Color.FromHex("#4A90E2"),
    Color.FromHex("#5AA0E8"),
    Color.FromHex("#E85D75"));
```

Checks run:

- Pairwise WCAG non-text contrast (every series vs. every other) ≥ 3:1.
- Each series vs. `ChartBackground` (both light and dark) ≥ 3:1.
- Pairwise ΔE ≥ 10 under deuteranopia, protanopia, tritanopia simulation.

Violations emit scanner rules `A11Y_CHART_009`–`011` (see Layer 8) with the offending hex
values, the specific check failed, and a nearest-safe-alternative hex value the agent can patch
in. Example diagnostic:

> `A11Y_CHART_010`: Series 1 (#4A90E2) and Series 2 (#5AA0E8) have ΔE 3.2 under deuteranopia
> — indistinguishable to ~5% of male users. Nearest safe alternative for Series 2: #2E5F8F
> (ΔE 18.7). Apply via `.SeriesColors(...)` or call `ChartPalette.Harden(...)`.

#### 7.6.3 Tier 4 — `.RawColors()` escape hatch

For prototypes, designer-review builds, and cases where the developer has out-of-band assurance
that colors are acceptable. Scanner runs but logs a single aggregate warning (`A11Y_CHART_012`)
rather than per-series violations, so CI doesn't fail but the decision is recorded.

```csharp
LineChart(...).RawColors(red, blue, green);  // no per-check validation; one aggregate warning
```

Exists because *some* teams will override checks anyway; better to give them a named,
auditable escape hatch than to watch them disable the scanner globally.

#### 7.6.4 Non-negotiables regardless of tier

- **Forced-colors always wins.** When `AccessibilitySettings.HighContrast` is on, Tier 1/3/4
  colors are all ignored and §7.1's system-color mapping applies. Not configurable.
- **Double-encoding stays.** Shape + dash cycle independently of color selection. Customizing
  colors does not disable §7.2. `.ColorOnly()` remains the only opt-out and still emits
  `A11Y_CHART_004`.
- **Background contrast is enforced even when series-pairwise passes.** A custom palette that
  passes pairwise 3:1 but fails against the chart background is still a violation.

#### 7.6.5 The `Harden` utility

Single utility exposed to both runtime code and the AI-agent devtools path
(spec 024). Given any `Color[]` or `ChartPalette`, returns the nearest palette that passes all
checks, plus a structured diff.

```csharp
// (method lives on the sealed ChartPalette class defined in §7.6.1)
public sealed class ChartPalette
{
    public static HardenResult Harden(
        Color[] input,
        HardenOptions? options = null);    // target contrast ratio, background, etc.
}

public record HardenResult(
    Color[] Palette,                      // the safe output
    IReadOnlyList<ColorAdjustment> Diffs, // per-color: original, adjusted, reason, ΔE delta
    bool PassedWithoutChanges);
```

Algorithm: operates in LCH color space. For each failing pair, push the lightness of the
lower-priority series away from its neighbor until pairwise 3:1 and colorblind-safe ΔE are
both satisfied, preserving hue and chroma where possible. Background-contrast failures adjust
lightness toward the opposite end from the background. Bounded iterations (max 8 passes) with
a deterministic output.

Usage patterns:

```csharp
// Runtime — developer self-hardens before supplying to chart:
var safe = ChartPalette.Harden(brandColors).Palette;
LineChart(...).SeriesColors(safe);

// AI agent — consumes scanner JSON, calls Harden via devtools action,
// emits a patch that replaces the literal hex array with the safe alternative.
```

Also exposed as a CLI / devtools command (spec 025 parity) so agents and humans alike can
run `reactor charts harden "#4A90E2,#5AA0E8,#E85D75"` and get back the hardened palette plus
the per-color rationale. Scanner violations under `A11Y_CHART_009`–`011` embed the
`Harden` result directly in the fix-suggestion JSON, so applying the fix is a single action
— no separate lookup needed.

---

## Layer 8: AccessibilityScanner chart rules

Extend `src/Reactor/Core/AccessibilityScanner.cs` with chart-specific rules, emitted in the same
JSON shape as existing A11Y_001–008:

| Rule | Check | Suggested fix |
|---|---|---|
| `A11Y_CHART_001` | Chart has no `Title`/`AutomationName` and no derivable name | Add `.Title("...")` or `.AutomationName("...")`. |
| `A11Y_CHART_002` | Chart has no `Description` and `ChartSummarizer` produced empty | Add `.Description("...")` or provide accessors with labels. |
| `A11Y_CHART_003` | Chart is `.Interactive()` but `ChartKeyboardNavigator` disabled | Remove `.DisableKeyboard()` or move interactivity out. |
| `A11Y_CHART_004` | `.ColorOnly()` used — color is sole series encoding | Remove `.ColorOnly()` or provide `.SeriesShapes(...)`. |
| `A11Y_CHART_005` | `.TightHitTest()` disabled the automatic 24×24 hit-target expansion on a marker < 24 px | Remove `.TightHitTest()` (expansion is automatic per §7.5), or use `.MarkerSize(24)` if tight hit-testing is genuinely required. |
| `A11Y_CHART_006` | Focus indicator contrast < 3:1 against computed chart background | Use default focus ring; do not override with `.FocusRing(...)` in low-contrast contexts. |
| `A11Y_CHART_007` | `.AnnounceEveryFrame()` used — floods live region | Remove; defaults are debounced for a reason. |
| `A11Y_CHART_009` | Custom palette fails pairwise WCAG 3:1 non-text contrast | Run `ChartPalette.Harden(...)` — fix suggestion embeds hardened hex values. |
| `A11Y_CHART_010` | Custom palette fails colorblind simulation (ΔE < 10 under deut/prot/trit) | Same: hardened alternative provided inline. |
| `A11Y_CHART_011` | Custom palette fails 3:1 contrast against `ChartBackground` (light or dark) | Hardened alternative adjusts lightness away from background. |
| `A11Y_CHART_012` | `.RawColors()` escape hatch used | Informational; no blocking. Recorded for audit. Consider moving to `.Palette()` or `.SeriesColors()`. |

*(A11Y_CHART_008 for a missing data-table fallback is intentionally absent: the fallback is
app-level — see Layer 3.)*

Scanner emits element path + suggested modifier diff so an AI agent can auto-apply.

---

## Scenarios

### Scenario 1 — Static data visualization (90% of charts)

Layers required: 1, 2, 7, 8. Layer 3 optional (only if app provides an `.AlternateView()`).

Author work: `.Title("Monthly revenue, 2025")`. That's it. Everything else is defaults.
Grade: **A** out of the box.

### Scenario 2 — Chart with embedded interactive controls

Layers required: 1, 2, 4, 5, 6, 7, 8. Layer 3 optional.

Example: timeline chart with legend toggles, a date-range slider overlay, and brush-to-filter.

```csharp
TimelineChart(events, d => d.When, d => d.Severity)
    .Title("Production incidents, last 90 days")
    .SeriesNames("P0", "P1", "P2")
    .LegendInteractive()        // toggles series visibility
    .DateRangeSlider()          // overlay slider
    .Brush(range => SetFilter(range))
    .AlternateView(IncidentTable(events));   // app-supplied; framework wires T-key + focus
```

All tab-order, live-region, focus-save/restore, and overlay grouping handled by Layers 4–6.

### Scenario 3 — Interactive chart with pan/zoom/filter/animate

Layers required: 1, 2, 4, 5, 6, 7, 8. Layer 3 optional.

```csharp
ScatterPlot(data, d => d.Latency, d => d.Throughput)
    .Title("API latency vs throughput")
    .Pan().Zoom()
    .OnPointInvoke(d => Drill(d));
```

Viewport exposed via `IScrollProvider` + axis `IRangeValueProvider`. Pan/zoom via Ctrl+= /
Alt+arrow. Live region announces settled state only.

### Scenario 4 — Force graph

Force graph physics is decorative; accessibility ships as structure, not motion.

- UIA: `IGridProvider` exposes edges as `{source, target, weight}` table rows. Screen-reader
  users get full table navigation without any visible table.
- Apps that want a sighted table view can wrap the graph with `.AlternateView(AdjacencyList(...))`.
- Keyboard nav walks adjacency: ← / → = next/prev node in sort; ↑ / ↓ = next neighbor of current
  node; Enter = focus neighbor as current.
- Simulation settles to final state immediately under reduced-motion.

---

## API Summary (additions)

```csharp
// Chart-level (all chart types)
.Title(string)
.Description(string)
.SeriesName(string) / .SeriesNames(params string[])
.DataLabel(Func<T,int,string>)
.Units(string xUnits, string yUnits)
.AxisLabel(ChartAxis, string)
.AlternateView(Element)   // app-supplied; enables T / Alt+Shift+F11 toggle

// Interactive
.Interactive()
.Pan(bool = true)
.Zoom(bool = true)
.Brush(Action<ChartRange>)
.OnPointInvoke(Action<T,int>)
.LegendInteractive()

// Visual encoding
.ColorOnly()                            // opts out of shape/dash; triggers scanner warning
.SeriesShapes(params MarkerShape[])
.SeriesDashes(params DashStyle[])

// Palette customization (Layer 7.6)
.Palette(ChartPalette)                  // Tier 1 — curated, pre-vetted (default: OkabeIto)
.SeriesColors(params Color[])           // Tier 3 — scanner-validated raw colors
.RawColors(params Color[])              // Tier 4 — unchecked escape hatch (A11Y_CHART_012)
ChartPalette.Harden(Color[])            // utility — returns nearest safe palette + diffs

// Escape hatches (scanner warns on each)
.DisableKeyboard()
.AnnounceEveryFrame()
.TightHitTest()
.FocusRing(Brush)
```

All modifiers are optional. Their defaults deliver the "A" grade without author intervention.

---

## UIA Pattern Mapping (reference)

| UIA Pattern | Chart element |
|---|---|
| `Group` (control type) | Chart root |
| `IGridProvider` + `ITableProvider` | Chart root |
| `IGridItemProvider` + `ITableItemProvider` | Each data point |
| `IValueProvider` (read-only) | Each data point |
| `IInvokeProvider` | Data point (if drill-down) |
| `ISelectionItemProvider` | Data point (interactive) |
| `IRangeValueProvider` | Each axis |
| `IScrollProvider` | Plot area (if pan) |
| `ISelectionProvider` | Legend (multi-select series) |
| `IToggleProvider` | Legend item (series show/hide) |
| `LiveRegionChanged` (Polite) | Chart root (Layer 6) |

---

## Phasing

### Phase 1 — "A on the static case" (target: 4 weeks)

Layers 1, 2, 7, 8.

Ships: `ChartAutomationPeer`, `ChartSummarizer`, forced-colors + reduced-motion integration,
scanner rules 001, 002, 004, 006. Every existing chart in the repo and in
`docs/_pipeline/apps/charting/` goes from "inaccessible" to "A-grade static" by adding
`.Title()`. One PR per layer.

### Phase 2 — "Interactive keyboard + live region" (target: +4 weeks)

Layers 3, 4, 5, 6.

Ships: `.AlternateView()` toggle convention (T / Alt+Shift+F11), `ChartKeyboardNavigator`,
focus save/restore, overlay grouping, debounced announcer, on-demand announce. Viewport UIA
(`IScrollProvider`, axis `IRangeValueProvider`). Scanner rules 003, 005, 007.

### Phase 3 — "A+ ceiling" (target: +2 weeks)

Keyboard help dialog. Cross-chart announcement routing for dashboard scenarios. Canonical
`DataGrid`-based alternate view sample in `docs/_pipeline/apps/charting/`.

### Deferred (not scheduled)

Sonification / audio graph. Nice-to-have ceiling feature matching Highcharts and Apple Audio
Graphs. Does not affect the "A" grade. Revisit on request.

---

## Grading Rubric — What "A" Looks Like

A chart ships at **A** grade when it:

- [ ] Has a non-empty accessible name (automatic or `.Title()`).
- [ ] Exposes `IGridProvider` + per-point `IValueProvider` (screen-reader table navigation
      works without any visible table).
- [ ] Passes every interactive operation via keyboard alone (if interactive).
- [ ] Announces settled state changes via debounced polite live region.
- [ ] Honors `AccessibilitySettings.HighContrast` (system high-contrast toggle).
- [ ] Honors `UISettings.AnimationsEnabled` / `SPI_GETCLIENTAREAANIMATION`.
- [ ] Double-encodes series (color + shape + dash) unless explicit `.ColorOnly()`.
- [ ] Focus indicator meets WCAG 2.4.13 (3:1 contrast, ≥ 2 px perimeter).
- [ ] Interactive markers meet WCAG 2.5.8 (≥ 24×24 hit target).
- [ ] Emits no `AccessibilityScanner` chart rule violations at runtime.

A visible alternate view (via `.AlternateView()`) is encouraged for data-dense charts but is
**app-level** and not required for the "A" grade.

**A+** adds cross-chart live-region routing and (deferred) sonification.

---

## Open Questions

1. **`ForceGraph` keyboard model** — adjacency walk is proposed, but for dense graphs
   (> 200 nodes) is a separate "graph explorer" dialog more usable? Precedent: Apple's
   VoiceOver rotor for chart points.
2. **Should `.ColorOnly()` be a scanner warning or an error?** Currently warning; error would
   block the escape hatch entirely. Warning is the spec-006 default for override modifiers.
3. **Chart-in-chart composition** — if a small-multiples grid nests charts, do child peers
   roll up to a single parent peer, or expose individually? Roll-up is less chatty; individual
   preserves Power BI's "navigate between visuals" keyboard model.
4. **Minimum chart size for "accessible by default"** — sparklines (e.g., inline 20×8 px
   trend indicators) probably don't warrant full UIA grid exposure. Threshold? Proposal:
   charts under 80×40 default to `role="img"` + alt text only.
5. **Canonical alternate-view sample** — should the framework ship a `ChartDataGrid(chart)`
   helper that auto-builds a `DataGrid` from the chart's accessors, as a recommended default
   app-level pattern? Keeps the framework lean but still gives authors a one-liner.

---

## References

- WCAG 2.2 — https://www.w3.org/TR/WCAG22/
- WAI-ARIA Graphics Module (graphics-aam 1.0) — https://www.w3.org/TR/graphics-aam-1.0/
- Microsoft Learn, Custom automation peers — https://learn.microsoft.com/en-us/windows/apps/design/accessibility/custom-automation-peers
- Microsoft Learn, Contrast themes — https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes
- Highcharts accessibility module — https://www.highcharts.com/docs/accessibility/accessibility-module-feature-overview
- Power BI accessible report design — https://learn.microsoft.com/en-us/power-bi/create-reports/desktop-accessibility-creating-reports
- Okabe-Ito colorblind-safe palette — https://jfly.uni-koeln.de/color/
- Spec 006 — Reactor Accessibility System
- Spec 016 — Native Chart Migration
- Spec 024 — AI-Agent DevTools
