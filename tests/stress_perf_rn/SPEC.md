# StressPerf — React Native ports

Two React Native apps that match existing C# stress demos byte-for-byte in
behavior so we can compare frameworks on the same machine.

Both target **react-native-windows** so the rendering target (XAML/WinUI) is
the same as Reactor and Direct/Bound. Comparing Reactor → react-native-web
would be unfair (DOM vs XAML).

## Demo 1 — StocksGrid

Mirror of `tests/stress_perf/StressPerf.Reactor/Program.cs`.

Identical behavior:

- 70 cols × 70 rows = **4,900 cells** (`StockDataSource.Columns/Rows`)
- Each cell: `SYMBOL PRICE` (e.g. `AAB 142.37`) — green if up, red if down
- Cell size 64×18, FontSize 8, Padding(2,1,2,1)
- `setInterval` every **33 ms** (~30 Hz)
- Each tick: mutate **N%** of cells (slider 0–100), price ±2% biased upward
- Symbols: deterministic, derived from `(row, col)` indices — same algorithm
  as `StressPerf.Shared.StockDataSource`
- Initial prices: deterministic with seeded RNG (seed = 42)

Headless mode (`--headless --percent N --duration S`):

- Auto-start at the given percent, run for S seconds, write report file, exit
- Report file alongside the executable, same format as the C# version

## Demo 2 — VirtualList (new — has a Reactor sibling too)

Brand-new scenario. The Reactor companion lives at
`tests/stress_perf/StressPerf.VirtualList.Reactor/`. The same spec drives both.

### List shape

- **5,000 rows** by default. Buttons in the top bar switch to 1,000 / 5,000 / 10,000.
- Each row is exactly **76 px tall**, full-width.
- Row layout (left to right):
  - Avatar: 48×48 colored square, single white initial letter, FontSize 18
  - Center column (flex 1):
    - Line 1 (bold, 14): `{name} • {category}`
    - Line 2 (14):       `{message}` (single line, truncate w/ ellipsis)
    - Line 3 (12, dim):  `{timestamp} • #{tag}`
  - Right edge: pill `♥ {likes}` (FontSize 12)
- Alternating row background (`#FFFFFF` / `#F5F5F5`) so virtualization is
  visible during scroll.

### Data — deterministic from index `i`

```
name      = NAMES[i % NAMES.length]                  // 16 names
category  = CATEGORIES[(i / 7) % CATEGORIES.length]  // 8 categories
message   = "{adjective} {noun} #{i}"                // adj/noun pools
timestamp = format(BASE_DATE + i minutes)
tag       = TAGS[(i * 31) % TAGS.length]             // 12 tags
likes     = ((i * 1664525 + 1013904223) >>> 0) % 999 // LCG
avatarHue = (i * 137) % 360                          // golden-angle palette
```

The pools (`NAMES`, `CATEGORIES`, …) are duplicated identically in
`ListItemSource.cs` and `ListItemSource.ts`.

### App chrome

Top toolbar:

- Buttons: `1k` / `5k` / `10k` to set item count
- Button: `Run benchmark`
- Readouts: `FPS: …`, `P50: … ms`, `P95: … ms`, `P99: … ms`, `Mem: … MB`

### Benchmark action

Pressing **Run benchmark**:

1. Snap scroll offset to 0
2. Tween scroll offset from 0 → `(rowHeight * count - viewportHeight)` over
   **5 seconds** by writing `scrollViewer.ChangeView(...)` /
   `flatListRef.scrollToOffset(...)` on every animation frame
   (`CompositionTarget.Rendering` / `requestAnimationFrame`). Use
   `disableAnimation: true` / `animated: false` so we control the curve, not
   the host.
3. Record per-frame delta (ms) for every frame the tween was active.
4. After the tween, compute P50 / P95 / P99 across the deltas, alongside
   average FPS and peak memory, and print/write a report.

### Headless mode

`--headless --count 5000 --duration 5`:

- Auto-runs the benchmark immediately on startup
- Writes a report file `StressPerf.VirtualList.{Reactor|RN}.report.txt` next
  to the executable, plus a `…samples.csv` with one row per second
  (Second, FPS, Memory_MB) and a `…frames.csv` with one row per frame
  (FrameIndex, DeltaMs).
- Exits when done.
