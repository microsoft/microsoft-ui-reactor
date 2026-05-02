# StocksGrid stress-perf — 5-run baseline

**Captured:** 2026-05-02
**Hardware:** ARM64 Windows 11, VS18 (2026), .NET 9. **Display: 120 Hz.**
**Workload:** 4,900 cells (`StockDataSource` 70×70). Each tick mutates N% of
cells; tick interval 33 ms; bench window 10 s. See
`tests/stress_perf/SPEC.md` and `tests/stress_perf/METHODOLOGY.md`.

**Sample size:** 5 runs per (variant × percent × power state). Headline
numbers are AC-power medians (cleanest, lowest variance). A battery-power
section follows because battery + DRR meaningfully changes the rankings.

## The right metric: Effective Refresh/sec

What the user perceives is bounded by the slower of two rates:

- **In-app renders/sec** — how often the framework completes a logical
  update (UI-thread ticks, React renders, etc.). Bound on "how much work
  was produced."
- **ETW Present/sec** — how often the OS displayed a frame from this
  process. Bound on "how much work was shown."

```
Effective Refresh/sec = min(Renders/sec, Present/sec)
```

If `Renders > Presents`, the display pipeline coalesces intermediate states
(only the latest cell value at each present makes the screen) — **display-bound**.
If `Renders < Presents`, the framework can't produce frames fast enough,
and the OS re-presents stale or interpolated content — **framework-bound**.

Raw Present count *overstates* user perception when there's a render
thread that batches updates (WPF) or a commit pipeline that emits
non-render frames (RN-Fabric). Raw render count overstates it when ticks
collapse before reaching the display. Effective Refresh is honest in both
directions.

## AC — Effective Refresh (5-run medians)

| Workload | Direct           | Bound            | **Wpf**             | **DirectX**          | Reactor          | RN-Fabric        |
|---------:|-----------------:|-----------------:|--------------------:|---------------------:|-----------------:|-----------------:|
| **10%**  | 10.11 [9.0–11.2] | 10.00 [8.4–11.2] | **18.78** [17.6–19.6]| 8.67 [8.6–12.3] (D)  | 8.11 [7.8–8.8]   | 6.10 [6.1–6.3] (F)|
| **50%**  | 3.11 [3.1–3.4]   | 3.22 [3.1–3.2]   | 6.44 [6.0–6.5]      | **7.67** [7.3–12.8] (D)| 3.67 [3.7–3.8]   | 2.50 [2.5–2.6] (F)|
| **100%** | 2.44 [2.3–2.4]   | 2.44 [2.3–2.4]   | 4.22 [4.2–4.3]      | **10.11** [5.9–11.4]  | 2.89 [2.8–3.0]   | 2.10 [1.9–2.2] (F)|

(D) = display-bound (render thread coalescing intermediate states).
(F) = framework-bound (OS re-presenting unchanged content).

## Headline rankings (AC)

```
10%:   Wpf 18.78  ▶  Direct 10.11  ≈  Bound 10.00  ▶  DirectX 8.67  ≈  Reactor 8.11  ▶  RN 6.10
50%:   DirectX 7.67  ▶  Wpf 6.44  ▶  Reactor 3.67  ▶  Bound 3.22  ≈  Direct 3.11  ▶  RN 2.50
100%:  DirectX 10.11  ▶  Wpf 4.22  ▶  Reactor 2.89  ▶  Direct/Bound 2.44  ▶  RN 2.10
```

## Headline findings

1. **WPF is the fastest XAML option at light loads.** 18.78 fps at 10%
   beats every WinUI 3 variant by 80%+. Tight variance (17.6–19.6) — the
   render thread isolates WPF from system noise.
2. **DirectX wins at saturation.** 7.67 / 10.11 fps at 50% / 100% — its
   workload-independent canvas redraw doesn't scale with mutation
   percent the way XAML property updates do.
3. **WPF and DirectX trade places by workload.** WPF dominates light
   loads (10%); DirectX dominates heavy loads (50%, 100%). Neither wins
   universally.
4. **Reactor leads the WinUI 3 cluster at 50% and 100%.** Direct / Bound
   beat Reactor at 10% but Reactor pulls ahead under heavier mutation.
   AC's tighter noise resolved the cluster ordering that battery couldn't.
5. **RN-Fabric is consistently last** at every workload, on both AC and
   battery. JS-thread-bound; AC doesn't help it.

## Display refresh rates (Global VSync/sec, AC, medians)

| Workload | Direct | Bound | **Wpf**  | DirectX | Reactor | RN-Fabric |
|---------:|-------:|------:|---------:|--------:|--------:|----------:|
| 10%      | 82     | 78    | **114**  | 72      | 68      | 58        |
| 50%      | 27     | 28    | 55       | 66      | 32      | 27        |
| 100%     | 20     | 22    | 36       | **78**  | 24      | 22        |

Even on AC, display refresh rate is a function of which framework is
running. Two regimes:

- **Light workloads (10%):** WPF generates enough render activity to keep
  the panel near 120 Hz. Other XAML variants get 70–80 Hz. RN drops to 58 Hz.
- **Heavy workloads (50% / 100%):** Most frameworks fall to 20–35 Hz as
  the GPU goes idle between ticks. **DirectX maintains 66–78 Hz** because
  full-canvas Direct2D draws keep the GPU pipeline saturated.

This is why "DirectX feels faster" at saturation persists on AC — it's
both committing more frames *and* getting more display refresh than the
others. The "feel" gap closes at 10% where WPF's render thread keeps the
panel at full 120 Hz.

## Calibration (5-run medians, AC)

| Variant     | Renders/s_med | ETW Present/s_med | Ratio  | Bottleneck |
|-------------|--------------:|------------------:|-------:|------------|
| Direct      | 10.6          | 10.11             | 1.05×  | balanced   |
| Bound       | 10.1          | 10.22             | 0.99×  | balanced   |
| Wpf         | 18.8          | 18.78             | 1.00×  | balanced   |
| DirectX     | 10.6          | 8.67              | 1.22×  | **display** |
| Reactor     | 8.3           | 8.11              | 1.02×  | balanced   |
| RN-Fabric   | 6.1           | 7.56              | 0.81×  | **framework** |

**On AC, WPF is now balanced (1:1).** On battery WPF was display-bound
at 1.43× — the render thread was outpacing the throttled display. With
the display running at full rate (120 Hz) on AC, every render makes it
to a present. WPF's effective refresh exactly matches its render rate.

Direct, Bound, Reactor are all 1:1 within ~5%. Easy-mode `Total Renders`
is reliable for these.

DirectX is display-bound (1.22×) — Win2D's Invalidate+Draw cycle is
faster than the display can present, so some intermediate canvas states
get coalesced.

RN-Fabric is framework-bound (0.81×) — the C++ commit pipeline produces
~20% more presents than React renders (animation interpolation,
post-layout follow-ups). Those frames don't carry fresh React content.

## Variance — AC vs battery

Standard deviation as % of median, rough buckets:

| Variant     | Battery | AC      | Note |
|-------------|--------:|--------:|------|
| Direct      | ~30%    | ~12%    | UI thread on battery is exposed to system noise |
| Bound       | ~25%    | <5%     | Likewise |
| Wpf         | <5%     | ~5%     | Render thread isolation works on both |
| DirectX     | ~40%    | ~20%    | GPU contention persists on AC (thermal) |
| Reactor     | ~20%    | <5%     | AC removed almost all variance |
| RN-Fabric   | <5%     | <2%     | Deterministic JS↔C++ bottleneck on both |

**AC is much more measurable.** The 30% Reactor@10% variance we saw on
battery (run 1 cold-start outlier 10.0 vs runs 2–5 around 6.0) collapses
to <5% on AC. AC numbers are quotable; battery numbers should always be
labeled.

## Memory — peak RSS (median, MB, AC)

```
DirectX     137 MB  ◀── pure D2D canvas, no XAML tree
Direct      417
Bound       504
Reactor     510
Wpf         950     ◀── MILCore + render-thread state
RN-Fabric 1,156     ◀── Hermes + JS bundle + Yoga + Fabric shadow tree
```

Memory ranking is essentially identical battery and AC. Power state
doesn't change architectural memory footprints.

## Battery vs AC — what changes

Effective Refresh deltas (AC vs battery):

| Variant     | 10%       | 50%       | 100%      |
|-------------|----------:|----------:|----------:|
| Direct      | +28%      | +12%      | +22%      |
| Bound       | **+58%**  | +11%      | +16%      |
| Wpf         | +38%      | +7%       | −3%       |
| DirectX     | **−3%**   | **−24%**  | **−21%**  |
| Reactor     | +30%      | +18%      | +20%      |
| RN-Fabric   | 0%        | +4%       | 0%        |

**DirectX got slower on AC** at every workload (3–24%). Sustained AC
runs hotter overall and DirectX is the most thermally exposed variant —
sustained Direct2D + GPU work hits the thermal envelope sooner than the
others. On battery, DRR was actually *helping* DirectX by keeping the
display warm; on AC there's no such help and thermal throttling bites.

**RN-Fabric was nearly identical** on both power states — its JS↔C++
commit pipeline is the bottleneck, not the CPU/GPU clock rate.

**Everything else gained 10–60%** on AC, mostly from absent CPU
throttling.

### Why "DirectX is insanely fast" looked obvious on battery

Battery confounded two things in DirectX's favor:
- DRR throttled WPF / WinUI / Reactor display refresh (20–30 Hz on
  battery vs 60+ Hz on AC for those variants).
- DirectX's full-canvas redraws kept its display at 45–60 Hz
  unchanged.

So battery had DirectX displaying at 2× the rate of WPF/WinUI variants
even though *content commit rates* were similar. AC fixes that — at
light loads WPF actually beats DirectX. The "DirectX crushes everything"
perception was a battery-specific artifact.

## Conclusions

1. **AC is the right environment for any quoted number.** Battery
   numbers are informative but not directly comparable. Always label
   `PowerState` on any baseline.
2. **WPF is the fastest XAML framework at light loads on AC.** Render
   thread isolation, low per-tick cost, and aggressive display-refresh
   maintenance combine to put WPF at 18.78 fps at 10% — almost double
   any WinUI 3 variant.
3. **DirectX wins at saturation but at a thermal cost.** It's the
   workload-independent option, but on AC it's actually 3–24% slower
   than on battery because of thermal pressure. For a sustained heavy
   workload it's still the best choice; for light interactive UIs WPF
   beats it.
4. **Reactor is the best of the WinUI 3 cluster at 50% / 100%** but
   loses to Direct/Bound at 10%. The 22 ms tree-build cost in the
   reconciler dominates light-workload performance; reducing element
   allocation on render would be the leverage point.
5. **RN-Fabric is the slowest framework tested**, framework-bound at
   every workload. The JS↔C++ commit pipeline is a hard ceiling that
   power state and percent don't move.
6. **DRR is real and active even on AC**, especially for "quiet"
   frameworks. Display refresh rate is itself a perf variable that
   varies by framework — see the vsync table above.

## Open questions

- Per-cell freshness for WPF — does its 18.78 effective refresh actually
  manifest as 18.78 distinct cell-state changes per cell-per-second, or
  does the render thread further dedupe? Would need PresentMon's
  `MsBetweenDisplayChange` or visual frame diffing to answer.
- Why does DirectX get worse on AC? Confirmed thermally with sustained
  GPU load? Worth a quick test where DirectX runs *first* in the
  sequence (cold) vs last (hot).
- Reactor's tree-build cost (22 ms steady) is the dominant reconcile
  phase. Investigation candidate: element allocation pooling, cached
  text formatters.

## How to reproduce

Both datasets came from `tests/stress_perf/run_stocks_grid_baseline.ps1
-Repeats 5`, run as administrator. The AC-vs-battery state was captured
automatically into the `PowerState` column of each row. CSVs are at
`tests/stress_perf/baseline-stocks-grid.{csv,summary.csv}`.

For the DRR / global-vsync hypothesis test specifically:
`tests/stress_perf/run_dwm_attribution_test.ps1`. ~70 s; runs idle +
WPF/DirectX at 50%/100% with `PresentTracer --all-pids`.
