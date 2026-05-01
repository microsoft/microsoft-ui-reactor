# Chunk 21 — Charting / D3 port: threat model

**Status:** Phase 2 review, complete
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer date:** 2026-04-30
**Companion:** `000-chunking-and-threat-model.md` (Tier-6, section 9, Chunk 21)

---

## 1. Scope

`src/Reactor/Charting/**` — a port of subsets of D3 (d3-array, d3-scale,
d3-shape, d3-hierarchy, d3-force, d3-color, d3-format, d3-ease, d3-polygon,
d3-random, d3-delaunay, d3-contour, d3-path) plus Reactor-specific DSLs that
turn data into Reactor virtual-tree elements.

`PathDataParser.cs` is in scope for Chunk 12 (parsers) and is excluded here.

| File | Lines | Role |
|---|---:|---|
| `src/Reactor/Charting/ChartDsl.cs` | 580 | DSL entry: `LineChart`/`BarChart`/`AreaChart`/`PieChart`. Builds Reactor element trees. |
| `src/Reactor/Charting/D3Dsl.cs` | 477 | Low-level Reactor element factories (`D3Canvas`, `D3Rect`, `D3Path`, `D3Axes`, `D3Grid`, brushes). |
| `src/Reactor/Charting/TreeChartDsl.cs` | 454 | `TreeChart` and `ForceGraph` DSLs. |
| `src/Reactor/Charting/PathDataParser.cs` | 174 | **Out of scope — Chunk 12.** |
| `src/Reactor/Charting/Accessibility/ChartPalette.cs` | 506 | Palette generation, contrast checks. |
| `src/Reactor/Charting/Accessibility/ChartKeyboardNavigator.cs` | 410 | Keyboard / virtual focus. |
| `src/Reactor/Charting/Accessibility/ChartAutomationPeer.cs` | 276 | UIA peer; auto-generates accessible name/description. |
| `src/Reactor/Charting/Accessibility/ChartSummarizer.cs` | 193 | Mann-Kendall trend + outlier detection for screen-reader summaries. |
| `src/Reactor/Charting/Accessibility/ChartLiveAnnouncer.cs` | 189 | Live-region announcement throttling. |
| `src/Reactor/Charting/Accessibility/ChartPointProvider.cs` | 180 | UIA chart-point peer. |
| `src/Reactor/Charting/Accessibility/ChartFocusContext.cs` | 94 | Virtual focus context. |
| `src/Reactor/Charting/Accessibility/IChartAccessibilityData.cs` | 90 | Interface. |
| `src/Reactor/Charting/Accessibility/ChartAlternateViewWrapper.cs` | 84 | Toggle wrapper. |
| `src/Reactor/Charting/Accessibility/ChartAxisProvider.cs` | 83 | UIA axis peer. |
| `src/Reactor/Charting/D3/Array/Bin.cs` | 116 | Histogram. |
| `src/Reactor/Charting/D3/Array/Bisect.cs` | 57 | Binary search. |
| `src/Reactor/Charting/D3/Array/Extent.cs` | 64 | Min/max ignoring NaN. |
| `src/Reactor/Charting/D3/Array/Group.cs` | 64 | Dictionary group/rollup. |
| `src/Reactor/Charting/D3/Array/Range.cs` | 34 | Numeric range. |
| `src/Reactor/Charting/D3/Array/Statistics.cs` | 209 | Min/max/mean/quantile/variance. |
| `src/Reactor/Charting/D3/Array/Ticks.cs` | 103 | **Tick-spec generator — primary algorithm-correctness target.** |
| `src/Reactor/Charting/D3/Chord/Chord.cs` | 163 | Chord / matrix layout. |
| `src/Reactor/Charting/D3/Color/D3Color.cs` | 185 | CSS color parser, HSL convert, named colors. |
| `src/Reactor/Charting/D3/Contour/Contour.cs` | 255 | Marching squares + density KDE. |
| `src/Reactor/Charting/D3/Ease/Ease.cs` | 181 | Easing functions. |
| `src/Reactor/Charting/D3/Format/Format.cs` | 270 | d3-format spec parser, percentile/SI prefix. |
| `src/Reactor/Charting/D3/Interpolate/Interpolate.cs` | 26 | Number interpolation. |
| `src/Reactor/Charting/D3/Interpolate/InterpolateColor.cs` | 131 | RGB / HSL interpolation. |
| `src/Reactor/Charting/D3/Layout/Cluster.cs` | 147 | Dendrogram layout. |
| `src/Reactor/Charting/D3/Layout/ForceSimulation.cs` | 249 | N-body charge / link / center / collision force sim. |
| `src/Reactor/Charting/D3/Layout/Pack.cs` | 234 | Circle packing. |
| `src/Reactor/Charting/D3/Layout/Partition.cs` | 210 | Icicle / sunburst partition. |
| `src/Reactor/Charting/D3/Layout/Sankey.cs` | 327 | Sankey flow layout. |
| `src/Reactor/Charting/D3/Layout/Stratify.cs` | 125 | Tabular → tree. |
| `src/Reactor/Charting/D3/Layout/TreeLayout.cs` | 284 | Reingold–Tilford tree. |
| `src/Reactor/Charting/D3/Layout/Treemap.cs` | 259 | Treemap (Squarify/Slice/Dice). |
| `src/Reactor/Charting/D3/Path/PathBuilder.cs` | 192 | SVG path-string builder. |
| `src/Reactor/Charting/D3/Polygon/Polygon.cs` | 143 | Area, centroid, hull. |
| `src/Reactor/Charting/D3/Random/Random.cs` | 179 | Distributions (Uniform/Normal/Poisson/...). |
| `src/Reactor/Charting/D3/Scale/BandScale.cs` | 195 | Discrete band/point scales. |
| `src/Reactor/Charting/D3/Scale/LinearScale.cs` | 226 | **Continuous linear — primary scale target.** |
| `src/Reactor/Charting/D3/Scale/LogScale.cs` | 174 | **Logarithmic — log-of-non-positive risk.** |
| `src/Reactor/Charting/D3/Scale/OrdinalScale.cs` | 94 | Discrete with implicit growth. |
| `src/Reactor/Charting/D3/Scale/PowScale.cs` | 157 | Power / sqrt scales. |
| `src/Reactor/Charting/D3/Scale/QuantizeScale.cs` | 185 | Quantize / Quantile / Threshold. |
| `src/Reactor/Charting/D3/Shape/Arc.cs` | 150 | Arc generator (with `NotImplementedException` for corner radius). |
| `src/Reactor/Charting/D3/Shape/Area.cs` | 99 | Area generator. |
| `src/Reactor/Charting/D3/Shape/Curve.cs` | 497 | Linear/Step/Basis/Cardinal/CatmullRom/MonotoneX. |
| `src/Reactor/Charting/D3/Shape/Line.cs` | 93 | Line generator. |
| `src/Reactor/Charting/D3/Shape/Link.cs` | 74 | Link generator. |
| `src/Reactor/Charting/D3/Shape/Pie.cs` | 99 | Pie / arc-angle generator. |
| `src/Reactor/Charting/D3/Shape/Radial.cs` | 221 | Radial-line / radial-area. |
| `src/Reactor/Charting/D3/Shape/Stack.cs` | 61 | Stack generator. |
| `src/Reactor/Charting/D3/Shape/Symbol.cs` | 196 | Predefined symbols. |
| `src/Reactor/Charting/D3/Voronoi/Delaunay.cs` | 481 | **Delaunay (O(n³) Bowyer-Watson) + Voronoi.** |
| **Total (in scope)** | **~10 800** | |

The full subdirectory contains ~10 999 lines (including the 174 lines of
`PathDataParser.cs` deferred to Chunk 12).

---

## 2. Data-flow diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│  CHART DATA SOURCE                                                       │
│  • developer-authored constants (trusted)                                │
│  • app state from server / file / user input (semi-trusted to UNTRUSTED) │
│  • IReadOnlyList<T> + accessors Func<T, double>                          │
└──────────────────────────────────────────────────────────────────────────┘
              │
              ▼  ChartDsl.LineChart / BarChart / AreaChart / PieChart / TreeChart / ForceGraph
              │
┌──────────────────────────────────────────────────────────────────────────┐
│  ChartElement<T>.BuildElement(data)                                      │
│    │                                                                     │
│    ▼ D3Extent.Extent(data, accessor)        ── may return ±Inf/NaN ──    │
│    │                                                                     │
│    ▼ new LinearScale([xMin, xMax], [px0, px1]).Nice()                    │
│    │   │                                                                 │
│    │   ▼ Nice → D3Ticks.TickIncrement (Math.Floor/Ceiling/Pow/Round)     │
│    │           which can produce NaN/Inf/long-overflow on bad domains    │
│    ▼ D3Grid(yScale)   ┐                                                  │
│    ▼ D3Axes(xs, ys)   ├── ys.Ticks() / xs.Ticks() ── allocates           │
│    ▼ RenderData(...)  ┘    new double[i2 - i1 + 1]  ← unbounded allocation
│    │                                                                     │
│    ▼ Element[]  →  Reactor virtual tree  →  WinUI Canvas / Path / Text   │
└──────────────────────────────────────────────────────────────────────────┘
              │
              ▼  TextBlock(label).FontSize(...).Foreground(...)
              │  (label is rendered as plain text — NO markup interpretation)
              ▼
        Final WinUI render
```

A second flow exists for hierarchy / force layouts:

```
hierarchical data + childrenAccessor + valueAccessor
   │
   ▼ {Pack,Treemap,Partition,Cluster,Tree}Layout.Hierarchy(...)  ── recursion
   │   (BuildNode recurses with no depth cap → stack-overflow risk)
   ▼ SumValues / PackCircles / LayoutNode  ── recursion
   ▼ PackCircles is O(n⁴), Delaunay.From is O(n³), ChartSummarizer.DetectTrend is O(n²)
   ▼ Result nodes.X/Y → D3Circle/D3Link/D3PathTranslated → WinUI
```

The key trust observation: **none of this code crosses a network or
deserializes opaque bytes**, but **every numeric path consumes
attacker-controlled `double`s without validation**. The chart-data parameter
is a `IReadOnlyList<T>` plus accessors; the framework does not own or
sanitize the values the accessor returns.

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption |
|---|---|---|
| App state / data-binding source → `IReadOnlyList<T>` argument | inbound | **Semi-trusted to untrusted.** May contain `double.NaN`, `double.PositiveInfinity`, `double.MaxValue`, negatives where the algorithm expects positives. |
| Accessor delegate (`Func<T, double>`) | inbound | Trusted (developer-supplied) but may *return* untrusted values from `T`. Exceptions thrown by the delegate propagate to the render thread. |
| Format specifier strings (`D3Format.Format("...")`) | inbound | Usually developer-authored (trusted). Input width / precision are user-controlled if the specifier itself comes from data (rare but legal). |
| Chart configuration (color strings, dimensions, paddings) | inbound | Developer-authored. Some setters (e.g. `BandScale.PaddingOuter`) accept any double without clamping. |
| Output: Reactor virtual-tree `Element[]` → WinUI `TextBlock` / `Path` / `Canvas` | outbound | Plain text — no markup interpretation. Path strings flow to `PathDataParser` (Chunk 12). |
| Output: Accessibility surface (`AutomationName`, `FullDescription`, `ItemStatus`) | outbound | Plain text consumed by UIA / screen reader. No script execution surface. |

**The data-flow trust edge** is "data values arrive at scale / layout
algorithms without validation." Reactor's stated trust model treats chart
data as semi-trusted; the algorithms must therefore tolerate adversarial
doubles (NaN/±Inf/MaxValue) without crashing the render thread, blocking it,
or allocating arbitrarily large buffers.

---

## 4. Asset inventory

The "asset" being protected by this chunk is **availability of the render
thread** (and, secondarily, **memory of the host process**). There is
essentially no confidentiality or integrity asset here that an attacker who
controls the chart data does not already have via the data path. Specifically:

1. **Render-thread liveness.** A multi-second algorithm or an exception
   thrown during measure/arrange will freeze the UI or terminate the app.
2. **Process memory.** Unbounded array allocation from chart data is a
   classic resource-amplification path.
3. **Algorithmic correctness for a11y.** `ChartSummarizer` flows summary
   strings into screen-reader output; an injected NaN that produces
   `"min NaN, max NaN, increasing"` is misleading but not exploitable
   beyond confusion.
4. **Color-palette contrast properties.** Out of scope here (handled by
   the accessibility scanner / `ChartPalette.cs`).

Charts do **not** touch the file system, network, secrets, or user identity.
Label text is rendered through `TextBlock` with no markup parsing
(`D3Dsl.Text`, `D3Dsl.cs:254`), so attacker-controlled label strings get the
same trust treatment as any other `string` content — they are not a markup
injection surface.

---

## 5. STRIDE table

| Cat. | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|
| **D** | Adversarial domain `[start, stop]` causes `D3Ticks.Ticks` to allocate `i2 - i1 + 1` doubles where the difference is huge | Attacker controls `xMin`/`xMax` via data | OOM / multi-second alloc on render thread | M | None. `count <= 0` returns `[]` but extreme finite domains are not capped. | **F-1** below. Cap `n` (e.g. `n > 10_000`) and return early. |
| **D** | `LogScale.Ticks` with a domain crossing zero or extending to `Inf` produces an int range `i..j` cast from `(int)Math.Floor(Log(d0))` where `Log(0)=-Inf` saturates to `int.MinValue` | Attacker passes domain `[0, X]` or `[NaN, X]` | Loop over ~2³² iterations on render thread | H | None. No `d0 > 0` precondition is enforced. | **F-2** below. Validate `d0 > 0`, `d1 > 0`, return early on infinite/NaN. |
| **D** | `Pack.PackCircles` is O(n⁴) (line 98–138) on flat hierarchies with one parent and many children | Attacker controls the children list | Multi-second freeze on render thread for n≈1000 | M | None. | **F-3** below. Bound child count or replace with d3-pack's chain-based algorithm. |
| **D** | `Delaunay.From` is O(n³) (Bowyer-Watson without spatial index) | Attacker controls the point list | Render-thread freeze for n≈10 000 | M | None. d3-delaunay's own JS port is O(n log n) via `delaunator`. | **F-4** below. Bound point count or warn at API edge. |
| **D** | Recursion bombs in `BuildNode` / `SumValues` / `LayoutNode` / `Visit` / `Descendants` for tree, treemap, partition, cluster, pack | Deeply-nested hierarchical data | StackOverflow → process termination | M | None. No depth cap or iterative rewrite. | **F-5** below. Guard with a depth cap or convert to iterative traversals. |
| **D** | `D3Color.Parse("#xxxxxx")` and other 6-hex inputs containing non-hex characters throw `FormatException` from `Convert.ToByte` | Attacker provides a "color" string read from data and used in `.Stroke(...)` | Render-thread exception kills frame | L–M | `format.Trim().ToLowerInvariant()` only normalises case. Non-hex characters in a 3- or 6-digit hex still reach `Convert.ToByte`. | **F-6** below. Use `byte.TryParse(... NumberStyles.HexNumber ...)` and fall back to a sentinel color. |
| **D** | `D3Format.Format(specifier)` produces an integer `Width` from the spec string with no overflow / size cap (`Width = Width * 10 + (s[i] - '0')`) | Attacker controls a format-spec string | `new string(spec.Zero ? '0' : ' ', Width)` allocates Width bytes — up to ~`int.MaxValue`/2 | L (specifier rarely data-driven) | None. | **F-7** below. Cap `Width` at e.g. 256 and similarly for precision. |
| **D** | `D3Format.Format(".999f")` calls `value.ToString("F999", ...)` which throws `ArgumentOutOfRangeException` (max precision is 99) | Attacker controls precision in spec string | Render-thread exception | L | None. | **F-7** below. Cap precision at 99 (or 17 = `double.DigitsRoundTrip`). |
| **D** | `OrdinalScale<TDomain>.Map` adds unknown keys to the domain dictionary unbounded when `Unknown` is `NaN` (the default) | Attacker passes a stream of distinct keys | Memory growth → OOM | L–M | "Set Unknown to a finite value to disable" is documented in the XML comment but the **default is implicit growth**. | **F-8** below. Default to non-growing or expose a max-size guard. |
| **D** | `BandScale.PaddingOuter` accepts negative values (no clamp at line 76) → `_step = (r1 - r0) / (n − pi + 2 * pOuter)` can divide by zero or invert sign | Attacker indirectly via dynamic chart settings | Renders bands with `NaN`/negative width | L | `PaddingInner` is clamped (line 69) but `PaddingOuter` is not. | **F-9** below. Clamp `PaddingOuter` to `[0, 1]` to match `PaddingInner`. |
| **D** | `QuantizeScale.Map` throws `ArgumentException` from `Math.Clamp(i, 0, _range.Length - 1)` when `Range` is empty (line 22) | Trivially: caller forgets to set a range | Render-thread exception | L | `Map` has a NaN guard but no empty-range guard. | **F-10** below. Return `double.NaN` (or the unknown sentinel) when `_range.Length == 0`, matching D3 behavior. |
| **D** | `ThresholdScale.Map` throws `IndexOutOfRangeException` from `_range[^1]` when `Range` is empty | As above | Render-thread exception | L | None. | **F-10** below. Same fix. |
| **D** | `ArcGenerator.Generate` throws `NotImplementedException` if `_cornerRadius > 0` (line 94–95) | Caller / DSL sets a corner radius | Render-thread exception, potentially during a measure pass | L–M | Throw is intentional. | **F-11** below. Either implement, ignore (round to 0), or surface via a settable feature flag. |
| **D** | `D3Ticks.TickSpec` integer overflow on adversarial domains (`(long)Math.Round(...)` near `long.MaxValue`, then `++i1`) | Attacker passes huge finite or near-infinite domain | Wrap-around → `n = i2 - i1 + 1` becomes a giant or negative value, then `new double[n]` fails or OOMs | M | None. | **F-1** (combined with cap on `n`). |
| **T** | A label string injected via `_dataLabel` / `LabelAccessor` / `_xAxisLabel` is rendered to UIA `AutomationName`/`FullDescription` and to a `TextBlock` | Attacker controls a data column used as label | Misleading screen-reader output, visual UI tampering (within the chart bounds) | M | Labels are rendered as plain text (`D3Dsl.Text` → `TextBlock`); no markup parsing. The accessibility surface receives raw strings without further interpretation. | **No code change required** for markup safety. Treat label strings as `string` everywhere. The intentional semantics of a chart label are "show this string"; an attacker who supplies the string already has full UI control over that label. |
| **T** | `Math.Round(value)` then `(long)` cast for `'d'`/`'b'`/`'o'`/`'x'` formats (`D3Format.cs:90,94,95,96,97`) silently saturates on NaN/Inf | Attacker pushes NaN through a `'d'`-formatted number axis | Tick label reads `0` instead of NaN, or `-9223372036854775808` for `+Inf` | L | None. | Document or detect and emit `"NaN"`/`"∞"` like D3.js. |
| **I** | `ForcedColorsTheme.FromSystem` swallows all exceptions (`catch { return Default; }`, line 472–476) | None | Mask of UIA color-query failures | L | Swallow. | OK; the host needs a non-throwing path. Note for documentation. |
| **D** | `ForceSimulation.Run(int iterations)` accepts unbounded iterations and the inner `ApplyChargeForce`/`ApplyCollisionForce` are O(n²); attacker can pass huge `iterations` via `ForceGraphElement.Iterations(n)` | Developer (trusted) — but the parameter could be data-bound | Multi-second freeze | L | None; comment in `ApplyChargeForce` (line 156) says "fine for <1000 nodes". | **F-12** below. Cap iterations and node count, or document hard limit. |

---

## 6. Findings

Severity legend: **High** = render-thread crash on plausible data, **Medium**
= multi-second freeze or memory amplification on plausible data, **Low** =
correctness / cleanup nit.

### F-1. `D3Ticks.Ticks` allocates an unbounded array on extreme domains — Medium

**Location:** `src/Reactor/Charting/D3/Array/Ticks.cs:54–83`, called from
`LinearScale.Ticks` (`LinearScale.cs:133–137`), `LogScale.Ticks`, `PowScale.Ticks`,
`Bin.cs:76`, `Contour.cs:49`, and `D3Dsl.D3Axes` / `D3Grid` (`D3Dsl.cs:364–382`).

```csharp
// Ticks.cs:62
if (i2 < i1) return [];
int n = (int)(i2 - i1 + 1);
var ticks = new double[n];
```

`i1` and `i2` are `long` derived from `(long)Math.Round(start * inc)` /
`(long)Math.Round(start / inc)`, with `inc` produced by
`(long)(Math.Pow(10, power) * factor)`. For an attacker-controlled domain
that produces a tiny `inc`, `i2 - i1` can be enormous (up to
`long.MaxValue`); the `(int)` cast then either silently truncates to a
huge int or, when summed with the `+ 1` after `++i1` overflow at lines 28–29
or 37–38, can become negative and trigger an `OverflowException` from
`new double[n]`.

Realistic hostile domain: `xMin = 0, xMax = 1e15, count = 6` is fine
(produces ~10 ticks). But `xMin = -1e308, xMax = 1e308, count = 10` produces
`step = NaN`, `power = NaN`, `error = NaN`, `factor = 1`, and
`inc = (long)(Math.Pow(10, NaN) * 1) = (long)NaN = 0` in .NET. With `inc = 0`,
the `else` branch at line 32–39 then computes `i1 = (long)Math.Round(start / 0) = (long)NaN = 0`,
which dodges the catastrophe — but only by coincidence. A near-edge domain
like `xMin = 0, xMax = double.MaxValue / 2` with `count = 1` (which becomes
`count = 2` via the recursive call at line 41–42) has `step ≈ 9e307`,
`power = 307`, `factor = 1`, `inc = 1e307`-cast-to-long = `long.MaxValue`,
`i1 = (long)Math.Round(0 / 1e307) = 0`, `i2 = (long)Math.Round(MaxValue/2 / 1e307) ≈ 9e0` —
also fine. So the practical attack surface is narrower than at first glance
**but** a NaN/Inf-laden domain is a footgun that can produce non-finite tick
values which then poison every downstream coordinate.

**Recommendation:**

1. After computing `n`, check `if (n < 0 || n > 10_000) return [];` (or
   surface a `tooManyTicks` callback).
2. Reject `start`/`stop` where either is non-finite.
3. The `Bin.cs:92` companion (`n = Math.Max(0, (int)(Math.Round((stop - start) / step)) + 1)`)
   has the same shape and benefits from the same guard.

This is a **Medium** finding because: (a) reaching it requires data-driven
domains with values near `±double.MaxValue` (rare but not impossible — a
buggy data source emitting `Number.MAX_VALUE` from a JS feed is a real
class), and (b) the consequence is a several-hundred-MB allocation, not RCE.

### F-2. `LogScale.Ticks` enters a `for(p = int.MinValue; p <= int.MaxValue; p++)` loop on non-positive domain — High (likelihood Low; impact High)

**Location:** `src/Reactor/Charting/D3/Scale/LogScale.cs:81–116`

```csharp
public double[] Ticks(int count = 10)
{
    double d0 = _domain[0], d1 = _domain[^1];
    bool reverse = d1 < d0;
    if (reverse) (d0, d1) = (d1, d0);

    int i = (int)Math.Floor(Log(d0));   // Log(d0) = ln(d0)/ln(_base)
    int j = (int)Math.Ceiling(Log(d1));
    var ticks = new List<double>();

    if (_base % 1 == 0)
    {
        int b = (int)_base;
        for (int p = i; p <= j; p++)
        {
            for (int k = 1; k < b; k++) { ... }
        }
        ...
    }
    ...
}
```

If `d0 = 0`, `Log(0) = -Infinity`, and per .NET ECMA-335 §III.3.27 a
`(int)` cast of `±Infinity` is *implementation-defined but reliably
saturating* in CoreCLR — `(int)double.NegativeInfinity` returns
`int.MinValue`. Likewise `(int)double.PositiveInfinity` returns `int.MaxValue`.
The outer `for` then runs ~`2³²` iterations, with the inner loop running
`b - 1` times each (default 9 for `_base = 10`) → ~3.8 × 10¹⁰ iterations.

`LogScale.Map` does guard `x <= 0` at line 30 and returns `NaN`, but
`Ticks()` has **no such guard on the domain itself**. The `Ticks()` method
is called by `D3Dsl.D3Axes` on every render of a chart that uses a log
scale.

A second variant: if `_base = 1`, `Log(x) = ln(x)/ln(1) = ln(x)/0 = NaN`,
`(int)NaN = 0`, and the loop is bounded — but `Math.Pow(1, p) = 1` for all
`p`, so all ticks collapse to 1 and the algorithm produces wrong data.

A third: if `_base = 0` or negative (the setter `Base { set { _base = value; ... } }`
at line 65 has no validation), behavior is undefined.

**Recommendation:**
- Validate `d0 > 0 && d1 > 0 && double.IsFinite(d0) && double.IsFinite(d1)` at
  the top of `Ticks()`. Return `[]` otherwise.
- Validate `_base > 1 && double.IsFinite(_base)` in the `Base` setter.
- Cross-reference with `LogScale.Nice` (line 119–126) which has the same
  exposure: `Math.Pow(_base, Math.Floor(Log(0)))` = `Math.Pow(_base, -Inf)` = 0,
  fine; `Math.Pow(_base, Math.Ceiling(Log(d1)))` for `d1 = 0` is `0` again.
  Less catastrophic but produces a `[0, 0]` domain that then breaks `Map`.

This is the worst single finding in the chunk: a chart with a log y-axis and
a data column that includes a `0` (a row count, a zero-balance dollar
amount, etc.) freezes the render thread for as long as it takes to do
~4 × 10¹⁰ trivial iterations — minutes to tens of minutes on modern CPUs.

### F-3. `Pack.PackCircles` is O(n⁴) — Medium

**Location:** `src/Reactor/Charting/D3/Layout/Pack.cs:98–138`

```csharp
for (int i = 2; i < circles.Count; i++)
{
    ...
    for (int j = 0; j < i; j++)
        for (int k = j + 1; k < i; k++)
        {
            var positions = PlaceCircle(circles[j], circles[k], ri);
            foreach (var (px, py) in positions)
            {
                for (int m = 0; m < i; m++) { ... }   // overlap test
                ...
            }
        }
}
```

For a flat hierarchy with one root and `n` children, this is roughly
`Σ_i (i² · 2 · i)` ≈ `n⁴ / 2`. At `n = 1000` that's 5 × 10¹¹ ops —
practically a render-thread freeze of many seconds on top of the OS layer.

D3's `d3-hierarchy/pack` uses a doubly-linked **front-chain** which is
O(n²) amortized; the port replaced it with a brute-force best-fit pass.

**Recommendation:** port d3-hierarchy's `packEnclose` / front-chain algorithm
**or** cap children per node (warn or truncate when > ~500). The chunking
plan flags this as "lower attacker reach" but the surface is "any data-bound
treemap/circle-pack consuming a flat-array source", which is realistic.

### F-4. `Delaunay.From` is O(n³) — Medium

**Location:** `src/Reactor/Charting/D3/Voronoi/Delaunay.cs:115–156`

The Bowyer-Watson loop tests each new point against every existing
triangle (line 119–124) and rebuilds the triangle list each iteration
(line 150–151). With `n` input points and ~2n triangles, this is
O(n²) per iteration × n iterations = O(n³). At `n = 10_000` that's 10¹²
operations.

`d3-delaunay` and `delaunator` use sweep-line / hashed grid construction
in O(n log n).

**Recommendation:** Either (a) cap point count before calling `From` (e.g.
`Voronoi` overlay rejects `n > 5000` and renders a warning), or
(b) port `delaunator`'s algorithm. The current note in the file
("Uses a sweep-line Delaunay triangulation algorithm") at line 2 is
**factually wrong** — the implementation is incremental, not sweep-line.

Also: line 86 — `if (... double.IsInfinity(minRadius))` returns degenerate
output, but the prior loop (line 79–84) calls `Circumradius` with
adversarial points where `2 * (dx * ey - dy * ex)` can be `0` and
`Circumradius` returns `+Infinity`. That branch is correctly handled; flag
only that the early-exit is silent (no telemetry).

### F-5. Stack-overflow recursion bombs across all hierarchy layouts — Medium

**Location:**
- `Pack.cs:30–40` (`BuildNode`), `42–59` (`SumValues`), `61–149` (`PackCircles`), `181–188` (`ScaleNode`), `204–210` (`Descendants`)
- `Treemap.cs:47–57` (`BuildNode`), `59–75` (`SumValues`), `77–110` (`LayoutNode`), `221–227` (`Descendants`)
- `Partition.cs:88–98` (`BuildNode`), `100–116` (`SumValues`), `127–131` (`Visit`), `118–125` (`RoundAll`), `154–160` (`Descendants`)
- `Cluster.cs:93–104` (`BuildHierarchy`), `130–141` (`Visit`/`VisitAfter`)
- `TreeLayout.cs:74–85` (`BuildHierarchy`), `105–139` (`FirstWalk`/`Apportion`), `141–150` (`SecondWalk`)
- `Stratify.cs:86–119` (`ComputeDepths`, `ConvertToTreemap`, `ConvertToPartition`)

None of these traversals is depth-capped or converted to an explicit stack.
On Windows the default thread stack is 1 MiB; deep .NET recursion hits the
limit at about 10 000 frames for a function this size. A treemap built from
attacker-controlled JSON with depth ~30 000 is enough to crash the process.

**Recommendation:** introduce a single `MaxHierarchyDepth` constant (say
1024) and enforce it at the top of `BuildNode`-style entries; or rewrite
`BuildNode`/`Visit`/`Descendants` iteratively using a `Stack<T>`.

`TreeLayout.Apportion` (line 152–203) walks `Thread` pointers in a `while`
loop. The threads are set internally by the algorithm itself, so an
externally-supplied tree cannot inject a `Thread` cycle directly — but if a
caller manually constructs `TreeNode<T>`s and sets `Thread`, an infinite
loop is possible. `TreeNode<T>.Thread` is `internal`, so the risk is
contained to the assembly and is informational only.

### F-6. `D3Color.Parse` throws `FormatException` on non-hex characters in a 6-digit "hex" color — Low–Medium

**Location:** `src/Reactor/Charting/D3/Color/D3Color.cs:55–67`

```csharp
if (format.StartsWith('#'))
{
    string hex = format[1..];
    if (hex.Length == 3)
        return new D3Color(
            (byte)(Convert.ToByte(hex[0..1], 16) * 17),
            (byte)(Convert.ToByte(hex[1..2], 16) * 17),
            (byte)(Convert.ToByte(hex[2..3], 16) * 17));
    if (hex.Length == 6)
        return new D3Color(
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
}
```

`Convert.ToByte("zz", 16)` throws `FormatException`. The DSL surface
(`ChartElement<T>.Stroke(string)`, `Fill(string)`, `LinkColor`, `NodeColor`)
takes raw user strings. If any of these is data-bound, a bad value crashes
the render.

**Recommendation:** swap to `byte.TryParse(span, NumberStyles.HexNumber,
CultureInfo.InvariantCulture, out var b)` and fall through to
`return new D3Color(0, 0, 0)` (matching what the function does at line 114
for unrecognized inputs). Same fix for the 3-digit branch.

Note also: `format.Trim().ToLowerInvariant()` (line 52) hides hex from
case-sensitive callers — fine here because hex-decoding is case-insensitive,
but `ToLowerInvariant` allocates a new string on every parse and `Parse`
is called in static-field initializers (`Category10` line 149–153,
`Tableau10` line 156–160). Cold-path nit.

### F-7. `D3Format` integer-overflow / max-precision footguns — Low

**Location:** `src/Reactor/Charting/D3/Format/Format.cs:226–230, 240–245, 87–91, 120`

```csharp
// Width parser
while (i < s.Length && char.IsDigit(s[i]))
{
    spec.Width = spec.Width * 10 + (s[i] - '0');   // ← unbounded
    i++;
}

// Precision parser
while (i < s.Length && char.IsDigit(s[i]))
{
    p = p * 10 + (s[i] - '0');                     // ← unbounded
    i++;
}
spec.Precision = p;
```

A specifier `"00000000000000000000.0f"` causes `spec.Width` to overflow into
arbitrary positive or negative ints. If the resulting `spec.Width - len` is
positive and large, `new string(' ', spec.Width - len)` (line 120) allocates
that many `char`s — up to ~`int.MaxValue / 2`, i.e. ~1 GiB.

A specifier `".999f"` produces `spec.Precision = 999`, which then becomes
`value.ToString("F999", ...)` (line 87) — .NET rejects precision > 99 with
`ArgumentOutOfRangeException`.

**Recommendation:** in `ParseSpecifier`, cap `spec.Width` at e.g. 256 and
`spec.Precision` at 17 (matches `double` round-trip precision). This is a
**Low** severity finding because format specifiers are typically authored,
not data-driven, but the `D3Format.Format(...)` API takes an arbitrary
string and is reachable from any caller.

### F-8. `OrdinalScale.Map` grows the domain unbounded by default — Low–Medium

**Location:** `src/Reactor/Charting/D3/Scale/OrdinalScale.cs:30–39`

```csharp
public double Map(TDomain x)
{
    if (!_index.TryGetValue(x, out int i))
    {
        if (!double.IsNaN(_unknown)) return _unknown;
        i = _domain.Count;
        AddToDomain(x);                           // ← grows on every miss
    }
    return _range.Length > 0 ? _range[i % _range.Length] : double.NaN;
}
```

The default value of `_unknown` is `double.NaN` (line 14), and `_unknown is NaN`
takes the **growing** branch. A chart fed from a streaming data source
where the categorical key changes per row will accumulate one dictionary
entry per call.

The XML doc on lines 26–28 says: "If x is not in the domain and Unknown is
NaN, x is implicitly added to the domain (matching D3 behavior). Set Unknown
to a finite value to disable implicit domain growth." — so the behavior is
documented and intentional. But the *default* is the unsafe one, which is
a footgun.

**Recommendation:** flip the default — initialize `_unknown` to a sentinel
like `0` (or expose `OrdinalScale.AllowImplicitGrowth = false` via a
constructor arg), and update the doc to make the streaming-data hazard
explicit.

### F-9. `BandScale.PaddingOuter` / `PointScale.Padding` accept negative / unbounded values — Low

**Location:** `src/Reactor/Charting/D3/Scale/BandScale.cs:73–77`

```csharp
public double PaddingOuter
{
    get => _paddingOuter;
    set { _paddingOuter = value; Rescale(); }       // no clamp
}
```

Compare `PaddingInner` at line 67–70: `value = Math.Clamp(value, 0, 1);`.
With `_paddingOuter = -1000`, `Rescale()` computes
`_step = (r1 - r0) / (n - _paddingInner + _paddingOuter * 2)` — divisor can
go negative or zero. `BandScale.Range` setter (line 60–63) also does
`_r0 = value[0]; _r1 = value[1];` with no length check; passing a
zero-length or single-element array throws `IndexOutOfRangeException`.

**Recommendation:** clamp `PaddingOuter` to `[0, 1]` (matching D3's
`paddingOuter`); guard the `Range` setter with a length check and ignore
or throw a clear `ArgumentException`.

### F-10. `QuantizeScale.Map` and `ThresholdScale.Map` throw on empty `Range` — Low

**Location:**
- `QuantizeScale.cs:17–23` — `Math.Clamp(i, 0, _range.Length - 1)` throws when `_range` is empty (Clamp requires `min ≤ max`).
- `ThresholdScale.cs:149–154` — `_range[^1]` throws when `_range` is empty.

These are scales that haven't been configured yet (default `_range` for
`QuantizeScale` is `[0, 1]` so the scenario is "user explicitly cleared the
range"). The right behavior matches `QuantileScale.Map` (line 84:
`return _range.Length > 0 ? _range[Math.Min(i, _range.Length - 1)] : double.NaN;`).

**Recommendation:** mirror `QuantileScale.Map` — return `double.NaN` (or
the ordinal `Unknown` sentinel) on empty range.

### F-11. `ArcGenerator` throws `NotImplementedException` on `cornerRadius > 0` — Low

**Location:** `src/Reactor/Charting/D3/Shape/Arc.cs:91–96`

```csharp
else if (rc > Epsilon)
{
    throw new NotImplementedException(
        "Corner radius on arcs is not yet implemented. Set corner radius to 0 or omit it.");
}
```

`SetCornerRadius` (line 133) accepts the value silently, but `Generate`
throws when the value is later applied. Either implement the corner-tangent
path (the d3-shape `arc.js` reference is ~80 lines), explicitly clamp
`cornerRadius` to 0 in `SetCornerRadius` with a documented warning, or move
the throw to `SetCornerRadius` so it surfaces at configuration time, not
during a measure pass.

### F-12. `ForceSimulation.Run(int iterations)` accepts unbounded iterations — Low

**Location:** `src/Reactor/Charting/D3/Layout/ForceSimulation.cs:111–119`

```csharp
public ForceSimulation Run(int iterations = 300)
{
    for (int i = 0; i < iterations; i++)
    {
        Tick();
        if (_alpha < _alphaMin) break;
    }
    return this;
}
```

Combined with the O(n²) inner loops in `ApplyChargeForce` (line 158–175)
and `ApplyCollisionForce` (line 227–245), and the documented "fine for
< 1000 nodes" comment at line 156, an `Iterations(int.MaxValue)` call (or
even `Iterations(50_000)` with `n = 5000`) freezes the render thread.

**Recommendation:** clamp `iterations` to e.g. ≤ 10 000 inside `Run`, or
expose a `MaxRunMilliseconds` budget that breaks the loop.

`AlphaDecay` setter (line 88) likewise accepts any double — `AlphaDecay(0)`
or a negative value disables convergence. Less critical because `Run` is
already iteration-bounded.

### F-13. `D3Bisect.BisectRight` mid-point uses unsigned shift `(int)((uint)(lo + hi) >> 1)` — Note (no finding)

**Location:** `src/Reactor/Charting/D3/Array/Bisect.cs:22, 40`

This is the canonical Java/JDK overflow-safe midpoint. For `int`, `lo + hi`
can overflow into a negative `int`, but the `(uint)` cast preserves the
bit pattern and `>> 1` recovers the correct unsigned midpoint as long as
`lo + hi` fits in `uint` — which it does for `array.Length ≤ int.MaxValue`.
**No defect**, but worth noting because reviewers familiar with the
`((lo + hi) >> 1)` C# bug will pause here.

### F-14. `Stratify` does not detect orphan nodes outside the chosen root — Low

**Location:** `src/Reactor/Charting/D3/Layout/Stratify.cs:21–66`

If two nodes A and B both name each other as parent (`A.parent = B,
B.parent = A`), neither becomes the root and the function throws "No root
found" (line 60) — safe.

But if a single node has `parentId = ""` and *also* a separate component
has a cycle (`B↔C`), the root is found, `Build` succeeds, and the cycle
nodes (`B`, `C`) are silently included in `nodes` but not visited from the
root. The returned `TreeNode<T>` looks correct — but anyone subsequently
walking the `Children` collections of the orphan B or C from elsewhere
hits an infinite loop.

**Recommendation:** after `Build`, do a single pass to confirm every node
is reachable from the root (count BFS visits and compare to `nodes.Count`),
and throw `"Disconnected component"` otherwise.

### F-15. `D3Color.FromHsl` does not validate inputs; NaN flows silently — Note (no defect)

`FromHsl` (line 125–144) calls `((h % 360) + 360) % 360` — for `h = NaN`
this is `NaN`, and the angle bucket falls through to the `else` branch.
`Math.Clamp(NaN, ...)` returns NaN. The arithmetic ultimately produces a
byte `0` from `(byte)NaN` via the unchecked conversion in `ClampByte`. The
resulting `D3Color(0, 0, 0)` is wrong but does not crash.

This is the typical "NaN in, garbage out" pattern; consistent with the rest
of the chunk. Documenting it here so the pattern is acknowledged.

### F-16. `Voronoi.EdgeIntersect` divides by zero on horizontal/vertical clip edges — Note (no defect)

**Location:** `src/Reactor/Charting/D3/Voronoi/Delaunay.cs:449–459`

`(_xmin - a.x) / (b.x - a.x)` with `b.x == a.x` returns `±Infinity`, and the
returned point becomes `(NaN, NaN)`. `EdgeInside` at line 441–447 then sees
NaN and excludes the point (NaN comparisons return false). The polygon
silently drops a vertex; no exception. Acceptable.

### F-17. Recommendation: input-sanitization at the DSL boundary

The cleanest mitigation for the bulk of F-1, F-2, F-3, F-4, F-5, F-9, and
F-12 is a single guard on the public DSL entry points (`ChartDsl.LineChart`,
`BarChart`, `PieChart`, `TreeChart`, `ForceGraph`):

1. Reject `data.Count > MAX_POINTS` (suggested 100 000).
2. Reject hierarchies with `MaxDepth > MAX_DEPTH` (suggested 1024).
3. Replace `NaN`/`±Inf` accessor results with `0` (or skip and warn) before
   they reach `D3Extent` / scale construction.
4. Cap `_iterations` on `ForceGraphElement` (line 207) to ≤ 10 000.

These are policy decisions, not algorithm fixes — but they convert every
"render-thread freeze on extreme data" finding into "rendering renders
nothing visibly wrong on extreme data," which is the right default for a
library being marketed as production-safe.

---

## 7. Open questions

1. **Is chart data ever sourced from an unauthenticated network feed
   (vs. always from app code that has already validated)?** If the answer
   is "always validated", several of the findings drop a severity tier.
   The chunking-plan trust model (`000-…md` §2) classifies network data as
   untrusted; if charts in production routinely render server JSON, the
   findings stand at the listed severities.

2. **What's the policy on label strings that look like RTL/control
   characters?** Labels go through `TextBlock` which respects bidi /
   formatting characters. A malicious label `"AdminUser‮"` in a chart
   axis can cause the label to render right-to-left, potentially confusing
   the user about which axis is which. This is the same Unicode-spoofing
   issue raised in Chunk 11 (ICU); the fix (filter `RtlHelper`) is owned
   there. **Open question:** do chart labels go through `RtlHelper`, or
   are they passed raw to `TextBlock`? Code path inspection suggests raw —
   defer to Chunk 11 reviewer.

3. **`ForceSimulation._rng = new Random(42)` (line 247)** — the deterministic
   seed is intentional for reproducible jitter, but it means jitter values
   are predictable. Is there any place where chart layout is used to convey
   information that would be compromised by predictability? (Probably no;
   noting for completeness.)

4. **Are charts ever rendered on a non-UI thread?** `D3Dsl.IsDarkTheme` /
   `IsForcedColors` are `[ThreadStatic]` (line 52, 71, 84, 99). If chart
   construction runs on a background thread, those flags are wrong. The
   docs at line 64–69 acknowledge the constraint. Verify the Reactor host
   contract documents this clearly enough that a developer doesn't quietly
   misuse `Task.Run(() => BuildChart(...))`.

5. **`PathBuilder.F(double)` does not reject `NaN`/`±Infinity`** (line 30–38);
   it serializes `NaN` and `∞` into the SVG path string, which then flows
   to `PathDataParser` (Chunk 12). Is the parser tolerant of those tokens?
   Defer to Chunk 12.

---

## 8. Out-of-scope referrals

| Surfaced concern | Belongs to chunk |
|---|---|
| `PathDataParser.Parse` reachable from `D3Path` / `D3PathTranslated` (`D3Dsl.cs:229–249`) — parser-internal threats (recursion, unbounded backtracking, NaN tokens emitted by `PathBuilder.F`) | **Chunk 12** (Other parsers) |
| Bidi / spoofing characters in chart labels (`_xAxisLabel`, `_dataLabel`, `LabelAccessor`) reaching `TextBlock` | **Chunk 11** (ICU + locale formatting / `RtlHelper`) |
| The reconciler's behavior when chart construction throws inside `BuildElement` (e.g. `D3Color.Parse` on bad hex, `ArcGenerator` `NotImplementedException`) — does the framework catch render-time exceptions? | **Chunk 14** (Reconciler & component model) |
| Forced-colors theme query via `Windows.UI.ViewManagement.UISettings` (`ForcedColorsTheme.FromSystem`, `D3Dsl.cs:445–476`) — system-call surface | Trusted dependency (per `000-…md` §2) — no chunk owns it for security review. |
| `IDataGrid` / property-grid reflection that may instantiate chart accessors at runtime | **Chunk 22** (Data system & controls) |
| `XamlHostElement` integration in `ForceGraphElement.ToElement` (line 241) — direct WinUI element insertion bypassing the reconciler | **Chunk 14** (Reconciler) |
