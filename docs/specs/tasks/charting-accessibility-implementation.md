# Charting Accessibility — Implementation Plan

Execution plan for the 8-layer charting accessibility design defined in
[`docs/specs/026-charting-accessibility-design.md`](../026-charting-accessibility-design.md).

Phases follow the spec's dependency order (Layers 1–2–7–8 first, then 3–4–5–6).
Each task is independently checkable so work can pause and resume at any point.
Testing is woven into every phase — most tests are selftest fixtures in
`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`, with E2E Appium/UIA validation
in `tests/Reactor.AppTests/Tests/`.

**Test locations (per CONTRIBUTING.md):**

| Suite | Location | When to use |
|-------|----------|-------------|
| Unit (xUnit) | `tests/Reactor.Tests/` | Pure functions, record equality, D3 math — no WinUI window |
| Selftest (TAP) | `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` | Live WinUI controls, reconciler, VisualTreeHelper assertions |
| E2E (Appium) | `tests/Reactor.AppTests/Tests/` | Cross-process UIA validation, real assistive-tech property checks |

---

## Phase 1: Layer 1 — `ChartAutomationPeer` (UIA grid/table mapping)

The foundation. One shared automation peer that gives every chart a structured
UIA representation — screen readers see a navigable grid of data points with
values, series headers, and axis labels without any visible table.

### 1.1 — `IChartAccessibilityData` interface

- [x] Create `src/Reactor/Charting/Accessibility/IChartAccessibilityData.cs`
- [x] Define the interface:
  ```csharp
  internal interface IChartAccessibilityData
  {
      string? Name { get; }
      string? Description { get; }
      IReadOnlyList<ChartSeriesDescriptor> Series { get; }
      IReadOnlyList<ChartAxisDescriptor> Axes { get; }
      ChartViewport? Viewport { get; }
  }
  ```
- [x] Define supporting records `ChartSeriesDescriptor`, `ChartAxisDescriptor`,
      `ChartViewport` in the same namespace
- [x] `ChartSeriesDescriptor` holds: `Name`, `Points` (list of
      `ChartPointDescriptor` with `XLabel`, `YValue`, `FormattedLabel`)
- [x] Unit test: record equality and `with` expression correctness for all
      descriptor records (`tests/Reactor.Tests/D3/ChartAccessibilityDataTests.cs`)

### 1.2 — `ChartAutomationPeer` root peer

- [x] Create `src/Reactor/Charting/Accessibility/ChartAutomationPeer.cs`
- [x] Subclass `FrameworkElementAutomationPeer`, attach to chart root `Canvas`
- [x] Implement `IGridProvider`: `RowCount` = series count,
      `ColumnCount` = max points per series
- [x] Implement `ITableProvider`: row headers = series names, column headers =
      x-axis categories/ticks, `RowOrColumnMajor = RowMajor`
- [x] `GetAutomationControlType()` returns `Group`
- [x] `GetNameCore()` returns `.AutomationName()` / `.Title()` / auto-derived name
- [x] `GetFullDescriptionCore()` returns auto-summary (wired in Phase 2)
- [x] Fire `AutomationEvents.PropertyChanged` on data/viewport/filter change

### 1.3 — `ChartPointProvider` per-point provider

- [x] Create `src/Reactor/Charting/Accessibility/ChartPointProvider.cs`
- [x] Implement `IGridItemProvider`: `Row` = series index, `Column` = point index
- [x] Implement `ITableItemProvider`: expose row/column headers back to Narrator
- [x] Implement `IValueProvider`: `Value` = human-readable string (e.g.,
      `"$42,300 on March 14"`), `IsReadOnly = true`
- [x] Virtual children — peer creates child providers from `IChartAccessibilityData`
      without per-point XAML elements

### 1.4 — `ChartAxisProvider`

- [x] Create `src/Reactor/Charting/Accessibility/ChartAxisProvider.cs`
- [x] Implement `IRangeValueProvider`: expose current min/max, small/large change
- [x] Wire to axis descriptors from `IChartAccessibilityData`

### 1.5 — Integrate peer with chart elements

- [x] Implement `IChartAccessibilityData` on `ChartElement<T>` (line, bar, area,
      scatter)
- [x] Implement `IChartAccessibilityData` on `PieChartElement<T>`
- [x] Implement `IChartAccessibilityData` on `TreeChartElement<T>`
- [x] Implement `IChartAccessibilityData` on `ForceGraphElement` (edges as
      `{source, target, weight}` rows)
- [x] Wire `ChartAutomationPeer` creation in the reconciler mount path for
      all chart element types
- [x] Ensure peer is driven from the virtual element tree (not post-render
      `Canvas.Children`) so it stays in lockstep with reconciliation

### 1.6 — Selftest fixtures: peer wiring

- [x] Create `tests/Reactor.AppTests.Host/SelfTest/Fixtures/ChartAccessibilityFixtures.cs`
- [x] Fixture `ChartA11y_PeerAttachment`: mount a `LineChart`, assert
      `FrameworkElementAutomationPeer.FromElement()` returns a `ChartAutomationPeer`
- [x] Fixture `ChartA11y_GridProvider`: mount a 2-series, 5-point `BarChart`,
      assert `IGridProvider.RowCount == 2`, `ColumnCount == 5`
- [x] Fixture `ChartA11y_PointValue`: mount a `LineChart` with known data,
      assert child peers expose correct `IValueProvider.Value` strings
- [x] Fixture `ChartA11y_TableHeaders`: assert `ITableProvider` row headers match
      series names and column headers match x-axis tick labels
- [x] Fixture `ChartA11y_PieChart`: mount a `PieChart`, verify peer exposes
      slices as grid items with correct value strings
- [x] Fixture `ChartA11y_ForceGraph`: mount a `ForceGraph`, verify edges exposed
      as `{source, target, weight}` grid rows
- [x] Register all fixtures in `SelfTestFixtureRegistry.AllFixtures` and the
      `Create()` switch, using prefix `ChartA11y_`

### 1.7 — Unit tests: provider logic

- [x] Create `tests/Reactor.Tests/D3/ChartAutomationPeerTests.cs`
- [x] Test `IGridProvider` row/column counts for edge cases: empty data, single
      point, jagged series (different point counts)
- [x] Test `IValueProvider.Value` formatting with various data types (int, double,
      DateTime, string categories)
- [x] Test `ITableProvider` header generation with and without explicit axis labels
- [x] Test `IRangeValueProvider` min/max derivation from axis descriptors

---

## Phase 2: Layer 2 — Accessor-driven labels + auto-summary

### 2.1 — Chart modifier API additions

- [x] Add `.Title(string)` modifier to `ChartElement<T>`, `PieChartElement<T>`,
      `TreeChartElement<T>` — sets visible title + accessible name
- [x] Add `.Description(string)` — overrides auto-generated summary
- [x] Add `.SeriesName(string)` / `.SeriesNames(params string[])` — series labels
- [x] Add `.DataLabel(Func<T, int, string>)` — per-point label override
- [x] Add `.Units(string xUnits, string yUnits)` — axis unit annotations
- [x] Add `.AxisLabel(ChartAxis, string)` — explicit axis names
- [x] All modifiers are immutable `with`-expression based (record pattern)

### 2.2 — Default per-point label generation

- [x] Implement default label format:
      `"{seriesName}, {xLabel}: {yValue}{yUnits}, point {i} of {n}"`
- [x] When `.DataLabel()` is set, use the custom labeller instead
- [x] Wire default labels into `ChartPointProvider.Value`

### 2.3 — `ChartSummarizer`

- [x] Create `src/Reactor/Charting/Accessibility/ChartSummarizer.cs`
- [x] Implement `ChartSummary` record: `Overview`, `AxisRanges`, `SeriesStats[]`,
      `Outliers[]`, `TrendVerdict`
- [x] `Overview`: `"{chartType} chart, {seriesCount} series, {pointCount} points each."`
- [x] `AxisRanges`: derive from axis descriptors + units
- [x] `SeriesStats`: min, max, trend direction per series
- [x] Trend detection: implement simple Mann-Kendall test for monotonic trend
- [x] Outlier detection: flag points > 2σ from series mean
- [x] Wire summary to `AutomationProperties.FullDescription` via the peer

### 2.4 — Auto-name derivation

- [x] If `.AutomationName()` / `.Title()` not set, derive from:
      1. Parent `Section(title: ...)` if any
      2. Preceding `Heading()` in the same Stack
      3. Fallback: `"{ChartType} chart with {seriesCount} series"`
- [x] Implement derivation logic in the reconciler mount path, matching spec 006's
      form-field derivation rules

### 2.5 — Selftest fixtures: labels and summary

- [x] Fixture `ChartA11y_DefaultPointLabels`: mount a `LineChart` with known data
      and `.SeriesName()` / `.Units()`, assert per-point `IValueProvider.Value`
      matches expected format
- [x] Fixture `ChartA11y_CustomDataLabel`: use `.DataLabel(...)` override, assert
      custom labels appear in provider values
- [x] Fixture `ChartA11y_AutoSummary`: mount a chart with clear trend data, assert
      `FullDescription` contains trend keywords ("increasing", "decreasing")
- [x] Fixture `ChartA11y_AutoNameFromTitle`: mount chart with `.Title("Revenue")`,
      assert peer name = "Revenue"
- [x] Fixture `ChartA11y_AutoNameFallback`: mount chart without title, assert
      fallback name contains chart type
- [x] Register all in `SelfTestFixtureRegistry`

### 2.6 — Unit tests: summarizer logic

- [x] Create `tests/Reactor.Tests/D3/ChartSummarizerTests.cs`
- [x] Test `Overview` generation for each chart type (line, bar, area, pie, tree)
- [x] Test `AxisRanges` with numeric and DateTime axes
- [x] Test `SeriesStats` min/max calculation
- [x] Test Mann-Kendall trend detection: monotonic increasing, decreasing, flat,
      seasonal (expect "no clear trend")
- [x] Test outlier detection: clear outlier flagged, no false positives on normal
      distribution
- [x] Test default label formatting with various unit strings
- [x] Test label formatting edge cases: null series name, empty units, zero points

---

## Phase 3: Layer 7 — Forced-colors + reduced-motion + double-encoding

### 3.1 — `IsForcedColors` flag on `D3Charts`

- [x] Add `[ThreadStatic] private static bool _isForcedColors` to `D3Charts.cs`,
      following the existing `IsDarkTheme` pattern
- [x] Add public property `IsForcedColors { get; set; }`
- [x] Add `ChartSeries(int seriesIndex)` brush: returns system high-contrast
      color when `IsForcedColors` (CanvasText, Highlight, LinkText, GrayText
      for series 0–3), normal palette color otherwise
- [x] Add `ChartSeriesDash(int seriesIndex)` returning dash pattern from cycle
- [x] Add `ChartSeriesMarker(int seriesIndex)` returning marker shape from cycle
- [x] When `IsForcedColors`, axes/labels → `CanvasText`, selection → `Highlight` /
      `HighlightText`, disabled → `GrayText`

### 3.2 — Host integration for forced-colors

- [x] In `ReactorHost` (or `RenderContext`), read
      `AccessibilitySettings.HighContrast` at startup
- [x] Listen for `AccessibilitySettings.HighContrastChanged` and
      `UISettings.ColorValuesChanged`; set `D3Charts.IsForcedColors` and trigger
      re-render
- [x] Propagate automatically — no author opt-in required

### 3.3 — Double-encoding defaults

- [x] Default palette: Okabe-Ito 8-color set — define as
      `ChartPalette.OkabeIto` static constant
- [x] Default marker shape cycle: circle, square, triangle, diamond, plus, cross,
      star, hexagon — always applied unless `.ColorOnly()`
- [x] Default dash cycle: solid, `4-2`, `2-2`, `6-2-2-2`, `8-4`, `1-1`
- [x] Implement `.ColorOnly()` modifier that disables shape/dash (triggers scanner
      warning `A11Y_CHART_004`)
- [x] Implement `.SeriesShapes(params MarkerShape[])` and
      `.SeriesDashes(params DashStyle[])` for explicit overrides

### 3.4 — `ChartPalette` sealed class

- [x] Create `src/Reactor/Charting/Accessibility/ChartPalette.cs`
- [x] Private constructor — only constructible via curated statics or `Harden()`
- [x] Static palettes: `OkabeIto`, `IBM`, `Viridis`, `Cividis`, `FluentDefault`
- [x] `.Palette(ChartPalette)` modifier (Tier 1 — curated)
- [x] `.SeriesColors(params Color[])` modifier (Tier 3 — scanner-validated)
- [x] `.RawColors(params Color[])` modifier (Tier 4 — escape hatch)

### 3.5 — `ChartPalette.Harden()` utility

- [x] Implement `Harden(Color[] input, HardenOptions?)` returning `HardenResult`
- [x] Algorithm operates in LCH color space
- [x] Check pairwise WCAG non-text contrast ≥ 3:1
- [x] Check pairwise ΔE ≥ 10 under deuteranopia/protanopia/tritanopia simulation
- [x] Check each color vs `ChartBackground` (light + dark) ≥ 3:1
- [x] For failing pairs, push lightness apart preserving hue/chroma; max 8 passes
- [x] Return `HardenResult` with `Palette`, `Diffs`, `PassedWithoutChanges`

### 3.6 — Reduced-motion integration

- [x] Implement `UseReducedMotion` hook (or integrate with
      `UISettings.AnimationsEnabled` / `SPI_GETCLIENTAREAANIMATION`)
- [x] `ChartAnimator`: on reduced-motion, skip entrance/exit animations (snap to
      final), disable pan inertia, terminate force-graph simulation immediately,
      keep only ≤ 150 ms opacity fades (WCAG 2.3.3)
- [x] Wire into existing chart transition/animation paths

### 3.7 — Focus indicator contrast

- [x] Double-ring focus indicator: 1px dark + 1px light stroke, guaranteeing
      3:1 contrast against any chart background
- [x] Ring geometry: 2 px perimeter + 2 px gap minimum (WCAG 2.4.13)
- [x] Wire into `ChartKeyboardNavigator` focus overlay (Layer 4)

### 3.8 — Hit target expansion

- [x] Point markers < 24×24 px get a transparent `D3Rect` hit shape sized 24×24,
      centered on the marker, when chart is `.Interactive()` (WCAG 2.5.8)
- [x] Implement `.TightHitTest()` escape hatch (triggers scanner warning
      `A11Y_CHART_005`)

### 3.9 — Selftest fixtures: forced-colors and encoding

- [x] Fixture `ChartA11y_ForcedColorsPalette`: set `D3Charts.IsForcedColors = true`,
      mount a 4-series `LineChart`, assert series brushes use system colors
      (CanvasText, Highlight, LinkText, GrayText)
- [x] Fixture `ChartA11y_DoubleEncoding`: mount a multi-series chart, assert each
      series has distinct marker shape AND dash pattern in addition to color
- [x] Fixture `ChartA11y_ColorOnlyWarning`: use `.ColorOnly()`, verify shape/dash
      are absent (visual regression anchor)
- [x] Fixture `ChartA11y_ReducedMotion`: set reduced-motion, mount chart with
      animation, assert animation skipped (final state reached in < 200ms)
- [x] Fixture `ChartA11y_HitTargetExpansion`: mount scatter with small markers,
      assert hit regions are ≥ 24×24
- [x] Register all in `SelfTestFixtureRegistry`

### 3.10 — Unit tests: palette and color math

- [x] Create `tests/Reactor.Tests/D3/ChartPaletteTests.cs`
- [x] Test `OkabeIto` palette has 8 colors with pairwise contrast ≥ 3:1
- [x] Test `Harden()` with a palette that fails pairwise contrast — verify output
      passes all checks
- [x] Test `Harden()` with colorblind-unsafe palette — verify ΔE ≥ 10 in output
- [x] Test `Harden()` with already-safe palette — verify `PassedWithoutChanges`
- [x] Test `Harden()` max iterations bound (does not infinite-loop on adversarial
      input)
- [x] Test forced-colors series color assignment (index → system color mapping)
- [x] Test dash cycle wraps correctly for > 6 series
- [x] Test marker shape cycle wraps correctly for > 8 series

---

## Phase 4: Layer 8 — AccessibilityScanner chart rules

### 4.1 — Scanner rule infrastructure

- [x] Extend `AccessibilityScanner.cs` with chart-specific scan methods
- [x] All new rules emit `A11yDiagnostic` in the same JSON shape as existing
      A11Y_001–008 (same records: `A11yDiagnostic`, `A11yFixSuggestion`,
      `A11yContext`)
- [x] Rules fire during the scan pass, not at mount time

### 4.2 — Implement chart scanner rules

- [x] `A11Y_CHART_001`: Chart has no `Title`/`AutomationName` and no derivable
      name → suggest `.Title("...")` or `.AutomationName("...")`
- [x] `A11Y_CHART_002`: Chart has no `Description` and `ChartSummarizer` produced
      empty → suggest `.Description("...")` or provide accessors with labels
- [x] `A11Y_CHART_003`: Chart is `.Interactive()` but `ChartKeyboardNavigator`
      disabled → suggest removing `.DisableKeyboard()`
- [x] `A11Y_CHART_004`: `.ColorOnly()` used — color is sole series encoding →
      suggest removing `.ColorOnly()` or providing `.SeriesShapes(...)`
- [x] `A11Y_CHART_005`: `.TightHitTest()` on marker < 24 px → suggest removal
- [x] `A11Y_CHART_006`: Focus indicator contrast < 3:1 → suggest default focus ring
- [x] `A11Y_CHART_007`: `.AnnounceEveryFrame()` floods live region → suggest removal
- [x] `A11Y_CHART_009`: Custom palette fails pairwise WCAG 3:1 → embed
      `Harden()` result in fix suggestion
- [x] `A11Y_CHART_010`: Custom palette fails colorblind ΔE < 10 → embed hardened
      alternative
- [x] `A11Y_CHART_011`: Custom palette fails background contrast → embed hardened
      alternative with adjusted lightness
- [x] `A11Y_CHART_012`: `.RawColors()` escape hatch used → informational, no
      blocking

### 4.3 — Selftest fixtures: scanner rules

- [x] Fixture `ChartA11y_Scanner_MissingTitle`: mount chart without title, run
      scanner, assert `A11Y_CHART_001` emitted with correct fix suggestion
- [x] Fixture `ChartA11y_Scanner_ColorOnly`: mount chart with `.ColorOnly()`, run
      scanner, assert `A11Y_CHART_004` emitted
- [x] Fixture `ChartA11y_Scanner_UnsafePalette`: mount chart with known-bad
      `.SeriesColors(...)`, run scanner, assert `A11Y_CHART_009` or `_010` emitted
      with hardened alternative in the fix JSON
- [x] Fixture `ChartA11y_Scanner_Clean`: mount chart with `.Title()` and defaults,
      run scanner, assert zero chart-rule violations
- [x] Register all in `SelfTestFixtureRegistry`

### 4.4 — Unit tests: scanner rule logic

- [x] Create `tests/Reactor.Tests/D3/ChartScannerRuleTests.cs`
- [x] Test each rule's detection logic in isolation (mock `IChartAccessibilityData`)
- [x] Test fix suggestion JSON structure matches expected schema
- [x] Test that `A11Y_CHART_012` is severity `"info"`, not `"warning"`
- [x] Test that scanner skips chart rules for non-chart elements

---

## Phase 5: Layer 3 — Alternate-view toggle convention

### 5.1 — `.AlternateView()` modifier

- [x] Add `.AlternateView(Element)` modifier to all chart element types
- [x] When set, enables T key / Alt+Shift+F11 toggle between chart and alternate
      view
- [x] Toggle announces state via live region: `"Showing data table"` /
      `"Showing chart"`
- [x] When alternate view is active, chart gets
      `AutomationProperties.AccessibilityView = Raw` to avoid double-announcement
- [x] When `.AlternateView()` not set, T key is a no-op (not an error)

### 5.2 — Focus save/restore on toggle

- [x] Save `{seriesIndex, pointIndex}` when toggling away from chart
- [x] Restore focus on return to chart view
- [x] Wire into `ChartFocusContext` (Layer 5)

### 5.3 — Selftest fixtures: alternate view

- [x] Fixture `ChartA11y_AlternateViewToggle`: mount chart with `.AlternateView()`,
      simulate T key, verify alternate view is mounted and chart is hidden from UIA
- [x] Fixture `ChartA11y_AlternateViewNoOp`: mount chart without `.AlternateView()`,
      simulate T key, verify no crash and chart remains visible
- [x] Register in `SelfTestFixtureRegistry`

---

## Phase 6: Layer 4 — `ChartKeyboardNavigator`

### 6.1 — Virtual focus infrastructure

- [x] Create `src/Reactor/Charting/Accessibility/ChartKeyboardNavigator.cs`
- [x] Chart root is a single focusable `Canvas` (`.IsTabStop(true)`)
- [x] Navigator holds `{seriesIndex, pointIndex}` state — no per-point XAML elements
- [x] Virtual focus cursor renders as `D3Rect` overlay (or ring for pie, circle
      for scatter) positioned over the current point
- [x] Focus indicator: double-ring (1px dark + 1px light) meeting WCAG 2.4.13

### 6.2 — Standard key bindings

- [x] ← / → : previous / next point in current series
- [x] ↑ / ↓ : switch to adjacent series (snap to nearest x-index)
- [x] Home / End : first / last point in current series
- [x] Ctrl+Home / Ctrl+End : first / last point across all series
- [x] Enter / Space : invoke (drill-down, tooltip)
- [x] Shift+← / → : extend brush selection
- [x] + / − or Ctrl+= / Ctrl+− : zoom in / out
- [x] Ctrl+0 : reset zoom
- [x] Alt+← / → / ↑ / ↓ : pan
- [x] L : focus legend
- [x] Space (on legend item) : toggle series visibility
- [x] T / Alt+Shift+F11 : toggle alternate view (Layer 3)
- [x] S : speak summary / replay announcement (Layer 6)
- [x] Shift+? / F1 : open keyboard help dialog
- [x] Esc : leave current mode; second Esc leaves chart

### 6.3 — `.Interactive()` API

- [x] Add `.Interactive()` modifier — turns on navigator (default off for static)
- [x] `.Interactive()` is implicit when `.Pan()`, `.Zoom()`, `.Brush()`,
      `.OnPointInvoke()`, or `.Selectable()` is used
- [x] Add `.OnPointInvoke(Action<T, int>)` for Enter/Space + click
- [x] Add `.OnBrushChanged(Action<ChartRange>)` for brush selection

### 6.4 — Selftest fixtures: keyboard navigation

- [x] Fixture `ChartA11y_KeyboardArrowNav`: mount interactive `LineChart`, simulate
      arrow keys, assert virtual focus moves to correct points (verify via peer
      `ISelectionItemProvider`)
- [x] Fixture `ChartA11y_KeyboardHomeEnd`: simulate Home/End, assert first/last
      point focused
- [x] Fixture `ChartA11y_KeyboardSeriesSwitch`: simulate ↑/↓, assert series index
      changes while snapping to nearest x-position
- [x] Fixture `ChartA11y_KeyboardInvoke`: simulate Enter on focused point, assert
      `OnPointInvoke` callback fired with correct data
- [x] Fixture `ChartA11y_KeyboardEsc`: simulate Esc, assert focus leaves chart
- [x] Register all in `SelfTestFixtureRegistry`

---

## Phase 7: Layers 5 + 6 — Viewport semantics + live region

### 7.1 — Viewport UIA (Layer 5)

- [x] Plot area gets `AutomationProperties.Name = "Plot area"` (localizable)
- [x] `AutomationLiveSetting` bound to announcer
- [x] `IScrollProvider` on plot area (if pan-enabled) + `IRangeValueProvider` on
      each axis
- [x] `AutomationProperties.ItemStatus` bound to current filter summary
- [x] Embedded-control tab order: Title/toolbar → Legend → Plot area → Overlays

### 7.2 — `ChartFocusContext` (Layer 5)

- [x] Create `src/Reactor/Charting/Accessibility/ChartFocusContext.cs`
- [x] Save `{seriesIndex, pointIndex}` when Tab leaves plot area
- [x] Esc returns focus to saved point and re-announces
- [x] On data/filter change, if saved point is filtered out, move to nearest
      surviving point in same series; emit polite announcement

### 7.3 — Decoration pruning (Layer 5)

- [x] All `D3Charts` decoration primitives (grid lines, tick marks, minor axes,
      background) auto-set `AccessibilityView = Raw` unless carrying meaningful
      data
- [x] Keeps UIA tree clean for screen-reader users

### 7.4 — `ChartLiveAnnouncer` (Layer 6)

- [x] Create `src/Reactor/Charting/Accessibility/ChartLiveAnnouncer.cs`
- [x] One polite live region per chart, trailing debounce 400 ms
- [x] Collapse bursts to one message
- [x] Message templates by event type: Zoom, Pan, Brush, Filter, Data update,
      Series toggle, Cross-chart
- [x] Assertive reserved for errors: `"No data in selected range."`

### 7.5 — On-demand announce (S key) (Layer 6)

- [x] S key re-speaks full current view summary regardless of debounce state
- [x] Does not interrupt in-progress announcement (queues instead)

### 7.6 — Announcement suppression during animation (Layer 6)

- [x] While animation is in flight, intermediate transitions don't announce
- [x] Only settled state announces
- [x] Reduced-motion users hear state immediately; full-motion users hear after
      tween completes (~200ms)

### 7.7 — Selftest fixtures: viewport + live region

- [x] Fixture `ChartA11y_ViewportUIA`: mount interactive chart with pan/zoom,
      assert `IScrollProvider` and `IRangeValueProvider` are exposed on plot area
- [x] Fixture `ChartA11y_FocusContextSaveRestore`: navigate to a point, Tab away,
      Esc back, assert focus returns to saved point
- [x] Fixture `ChartA11y_DecorationPruning`: mount chart, assert grid lines /
      tick marks have `AccessibilityView = Raw`
- [x] Fixture `ChartA11y_LiveRegionAnnounce`: trigger a zoom on an interactive
      chart, assert live region text updates with zoom summary after debounce
- [x] Fixture `ChartA11y_OnDemandAnnounce`: simulate S key, assert full summary
      spoken via live region
- [x] Register all in `SelfTestFixtureRegistry`

---

## Phase 8: E2E Appium/UIA Validation

Cross-process UIA tests validating that accessibility properties are visible to
real assistive technology through the Windows UIA pipeline. These tests live in
`tests/Reactor.AppTests/Tests/` and use the Appium/WinAppDriver infrastructure.

### 8.1 — Chart accessibility E2E test host fixture

- [x] Create a new Appium-navigable fixture in
      `tests/Reactor.AppTests.Host/` (alongside the existing accessibility
      showcase) that mounts several chart types with accessibility configured:
  - A `LineChart` with `.Title()`, `.SeriesNames()`, `.Units()`, 2 series, 5 points
  - A `BarChart` with `.Title()` and default labels
  - A `PieChart` with `.Title()` and slice labels
  - A chart with `.Interactive()` and keyboard nav enabled
- [x] Register the fixture in the fixture navigator so Appium can navigate to it

### 8.2 — E2E test class: `ChartAccessibilityTests`

- [x] Create `tests/Reactor.AppTests/Tests/ChartAccessibilityTests.cs`
- [x] Follow existing patterns from `AccessibilityTests.cs` (extend same base,
      use `NavigateToFixture(...)`, `FindById(...)`, `GetAttribute(...)`)
- [x] **Test: `ChartA11y_UIA_ChartHasAccessibleName`** — navigate to chart fixture,
      find chart element via UIA, assert `Name` attribute matches the `.Title()`
      value. Validates WCAG 1.1.1 end-to-end through the real UIA pipeline.
- [x] **Test: `ChartA11y_UIA_GridProviderExposed`** — find chart root, query for
      `IGridProvider` pattern availability, assert `RowCount` and `ColumnCount`
      match expected series/point counts. Validates WCAG 1.3.1 programmatic
      structure.
- [x] **Test: `ChartA11y_UIA_PointValueReadable`** — navigate into chart grid via
      UIA, read a specific point's `Value` property, assert it contains the
      expected human-readable string (series name, x-label, y-value). Validates
      screen-reader data access path.
- [x] **Test: `ChartA11y_UIA_ForcedColorsActive`** (conditional — runs only if
      high contrast is enabled or can be toggled in test setup) — assert chart
      uses system colors when `HighContrast` is active.

### 8.3 — E2E validation scope

- [x] Assert at minimum 1 E2E test verifying the full UIA → screen-reader path
      (chart name, grid structure, point values) is working end-to-end through
      the real Windows accessibility pipeline
- [x] This validates what selftests cannot: that the UIA properties survive the
      cross-process boundary and are visible to external assistive technology

---

## Phase 9: Integration + polish

### 9.1 — Wire all layers together

- [x] Verify all 8 layers compose correctly: a chart with `.Title()`,
      `.Interactive()`, `.AlternateView()`, and default palette passes all scanner
      rules, exposes full UIA tree, supports keyboard nav, announces via live
      region, and respects forced-colors / reduced-motion
- [x] Update all existing chart samples in `samples/` and `docs/` to include
      `.Title()` at minimum
- [x] Verify no regressions in existing charting unit tests and selftests

### 9.2 — Comprehensive integration selftest

- [x] Fixture `ChartA11y_FullIntegration`: mount a chart exercising all layers
      (title, series names, units, interactive, alternate view, default palette),
      run a sequence of assertions covering: peer exists, grid provider valid,
      point values correct, keyboard nav works, scanner returns zero violations

### 9.3 — Documentation

- [x] Add inline XML doc comments on all new public APIs
- [x] Update `SKILL.md` if charting accessibility modifiers should be included
- [x] Add sample in `docs/` or `samples/` showing the canonical accessible chart
      pattern (static + interactive)

### 9.4 — Final validation

- [x] Run full unit test suite: `dotnet test tests/Reactor.Tests`
- [ ] Run full selftest suite: `dotnet test tests/Reactor.SelfTests`
- [ ] Run E2E tests: `dotnet test tests/Reactor.AppTests --filter "ClassName~ChartAccessibilityTests"`
- [x] Verify zero `A11Y_CHART_*` scanner violations on all sample charts
