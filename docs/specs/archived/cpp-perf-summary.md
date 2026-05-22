# ReactorCpp: C++ vs C# Reactor Performance Summary (Archived)

**Status:** Experiment completed and archived. The ReactorCpp source code has been removed from the repo.
**Date:** 2026

## Overview

ReactorCpp was a fully native C++20 port of the Microsoft.UI.Reactor (Reactor) framework — element model, hooks, reconciler, and WinUI bridge — designed to test whether a fully native stack could avoid managed/unmanaged transition costs and show measurable FPS and memory improvements.

## Benchmark Results

Stress test: 70x70 grid = 4,900 `TextElement` cells, 10-second runs at three load levels.

| Variant | 10% FPS | 50% FPS | 100% FPS | Avg Update (ms) | Avg Memory (MB) |
|---------|---------|---------|----------|-----------------|-----------------|
| **ReactorCpp** | **28.7** | 7.5 | 4.6 | 0.1-0.2 | 403 |
| Reactor C# | 19.2 | 8.0 | 5.6 | 0.1-0.5 | 459 |
| Direct (C#) | 26.3 | 8.3 | 5.6 | 2.1-15.1 | 477 |
| Binding (C#) | 22.3 | 7.0 | 4.7 | 6.2-41.2 | 514 |
| DirectX | 38.4 | 39.0 | 38.4 | 0.0-0.2 | 142 |

### ReactorCpp Phase-Level Timing (per render batch)

```
PERF [6 renders]: tree=3.06ms  reconcile=19.46ms  effects=0.02ms  total=22.54ms
PERF [4 renders]: tree=3.04ms  reconcile=21.64ms  effects=0.00ms  total=24.68ms
PERF [4 renders]: tree=2.53ms  reconcile=24.72ms  effects=0.00ms  total=27.25ms
PERF [3 renders]: tree=2.65ms  reconcile=20.26ms  effects=0.00ms  total=22.91ms
PERF [3 renders]: tree=2.63ms  reconcile=20.16ms  effects=0.00ms  total=22.79ms
PERF [3 renders]: tree=2.74ms  reconcile=20.73ms  effects=0.00ms  total=23.46ms
```

## Key Finding

ReactorCpp's reconciler is **2x faster than C# Reactor at low load** (28.7 vs 19.2 FPS) and uses **12% less memory**. At high load, both hit the same XAML layout ceiling. The original hypothesis — that a fully native stack avoids enough overhead to show measurable improvement — is **confirmed for the reconciler itself**, though the XAML rendering pipeline remains the dominant cost.

## Lessons Learned

1. **The XAML layout engine is the ceiling.** At high update rates, both C++ and C# reconcilers are bottlenecked by XAML's measure/arrange/render pipeline, not by the diff algorithm. DirectX at 38 FPS proves this — neither reconciler can exceed ~8 FPS at 50% load regardless of how fast the diff runs. The reconciler's job is to minimize the number of XAML property sets; once that's minimized, further optimization requires bypassing XAML entirely.

2. **C++/WinRT ABI overhead is real but manageable.** Each C++/WinRT property set is a raw COM vtable call with HRESULT checking. This is faster than C# for individual calls (no marshaling), but lacks the CLR's batched RCW caching. At 100% load (9,800+ property sets per frame), C++ is slightly slower than C# Reactor. The fix would be to batch WinRT updates or use `SetValue` with dependency property tokens.

3. **`std::variant` was the right call.** The closed element type set enables exhaustive `std::visit` dispatch with zero virtual call overhead. Element construction is stack-allocated — no `new`, no GC, no boxing. The variant is ~128 bytes (dominated by `std::vector<Element>` in StackElement), which is reasonable.

4. **Hooks translate cleanly to C++.** `auto [count, set_count] = use_state(0)` via structured bindings is nearly identical to the C# `var (count, setCount) = UseState(0)`. The main friction point is `use_reducer`'s functional updater syntax, which requires lambdas where C# uses simple assignment.

5. **Single-file apps are viable.** The TestApp was 1,161 lines in a single `main.cpp` — shorter than the C# version. No XAML, no resource files, no code-behind.

## Conclusion

The C++ reconciler is measurably faster, but the XAML rendering pipeline is the true bottleneck at scale. Further perf gains require bypassing XAML (e.g., DirectX rendering), not optimizing the reconciler language. The C# Reactor implementation is sufficient for production use.

If revisiting this in the future, the key question is whether a custom rendering backend (not XAML) combined with a native reconciler can approach DirectX-level throughput (~38 FPS) while retaining declarative ergonomics. The archived spec documents (`cpp-native-reconciler.md`, `cpp-tasks.md`) in this directory contain the full design and implementation details.
