# Chunk 20 — Yoga port: threat model

**Status:** Phase 2 review, complete
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer date:** 2026-04-30
**Companion:** `000-chunking-and-threat-model.md` (Tier-6, section 9, Chunk 20)

---

## 1. Scope

`src/Reactor/Yoga/**` — a faithful C# port of Meta's Yoga flexbox layout engine
(C++ origin: `facebook/yoga`, headers `algorithm/CalculateLayout.cpp`,
`AbsoluteLayout.cpp`, `BoundAxis.h`, etc.). The port is `internal` to the
`Microsoft.UI.Reactor.Layout` assembly except for a small public surface
(enums and `FlexPanel`, the WinUI3 panel that hosts it).

| File | Lines | Notes |
|---|---:|---|
| `src/Reactor/Yoga/YogaAlgorithm.cs` | 2179 | The core algorithm. `CalculateLayout` → `CalculateLayoutInternal` (cache wrapper) → `CalculateLayoutImpl` (~600-line main loop) → flex-line + free-space distribution + absolute layout. **The single biggest review-relevant file.** |
| `src/Reactor/Yoga/FlexPanel.cs` | 552 | WinUI3 `Panel` subclass. Bridges `MeasureOverride`/`ArrangeOverride` to Yoga. Owns the per-child `YogaNode` cache and the `MeasureFunction` callback into WinUI for leaf measurement. Public type. |
| `src/Reactor/Yoga/YogaNode.cs` | 540 | Mutable tree node: style, layout, children, callbacks. Internal. |
| `src/Reactor/Yoga/YogaStyle.cs` | 335 | CSS-like style fields with edge-fallback resolution (Start/End/Horizontal/Vertical/All). |
| `src/Reactor/Yoga/AlgorithmUtils.cs` | 475 | `BoundAxisHelper`, `AlignHelper`, `BaselineHelper`, `PixelGridHelper`, `CacheHelper`, `FlexLineHelper`. |
| `src/Reactor/Yoga/LayoutResults.cs` | 173 | Computed layout output + `CachedMeasurement` ring (8 slots). |
| `src/Reactor/Yoga/YogaConfig.cs` | 127 | Config (point-scale, errata, experimental flags). Has a `Default` static and a `Freeze()` mechanism. |
| `src/Reactor/Yoga/YogaEnums.cs` | 126 | Internal enums. |
| `src/Reactor/Yoga/YogaValue.cs` | 116 | `YogaValue` (point/percent/auto), `YogaFloat` NaN helpers. |
| `src/Reactor/Yoga/FlexEnums.cs` | 63 | Public enums (FlexAlign, FlexDirection, etc.). |
| `src/Reactor/Yoga/FlexDirectionHelper.cs` | 81 | Axis ↔ physical-edge resolution. |
| **Total** | **4767** | |

The chunk is reviewed end-to-end. No CVEs are filed against this port; the
public Yoga CVE list is short (it has historically been noted in advisory
form for stack-overflow on adversarial trees and integer overflow in
`YGNodeStyleSet*` paths in old versions, but no CVE-ID was issued against
the C++ project — see Section 9 cross-reference).

---

## 2. Data-flow diagram

```
┌────────────────────────────────────────────────────────────────────────┐
│                      DEVELOPER-AUTHORED COMPONENT TREE                  │
│  (trusted at build time — but values may be data-driven at runtime,     │
│   e.g. width bound to a server-supplied number, or text size bound to   │
│   user-input that flows into a measure func)                            │
└────────────────────────────────────────────────────────────────────────┘
                          │
                          ▼  Reactor reconciler creates UIElement tree
┌────────────────────────────────────────────────────────────────────────┐
│                          WinUI Panel layout pass                        │
│                                                                         │
│  FlexPanel.MeasureOverride(availableSize)                               │
│      │                                                                  │
│      ▼ SyncYogaTree()  ── builds/mutates YogaNode children ──           │
│      │   (ApplyAttachedProperties: Grow/Shrink/Basis/AlignSelf/Position │
│      │    /Insets/Margin from DependencyProperty values)                │
│      │                                                                  │
│      ▼ SetRootConstraints(availableSize)                                │
│      │                                                                  │
│      ▼ _rootNode.CalculateLayout(rootWidth, NaN, layoutDir)             │
│      │   │                                                              │
│      │   ▼ YogaAlgorithm.CalculateLayout                                │
│      │       │ Interlocked.Increment s_currentGenerationCount           │
│      │       ▼                                                          │
│      │       CalculateLayoutInternal (cache check, recursive)           │
│      │         │                                                        │
│      │         ▼ CalculateLayoutImpl                                    │
│      │             │                                                    │
│      │             ▼ MeasureFunc bridge ── calls back into WinUI ──     │
│      │                child.Measure() / DesiredSize                     │
│      │             │                                                    │
│      │             ▼ ResolveFlexibleLength → DistributeFreeSpace 1/2 → │
│      │             ▼ JustifyMainAxis → cross-axis align →              │
│      │             ▼ LayoutAbsoluteDescendants (recursive)              │
│      │             ▼ RoundLayoutResultsToPixelGrid (recursive)          │
│      │                                                                  │
│      ▼ for each child: child.Measure(yogaWidth + margin, yogaHeight…)   │
│                                                                         │
│  FlexPanel.ArrangeOverride(finalSize)                                   │
│      │                                                                  │
│      ▼ if size changed → re-run CalculateLayout (with _arranging=true,  │
│      │    measure func returns cached DesiredSize without re-measure)   │
│      │                                                                  │
│      ▼ for each child: child.Arrange(layoutRect)                        │
└────────────────────────────────────────────────────────────────────────┘
```

**Inputs:**
- Developer-set DependencyProperty values on the FlexPanel and its children
  (`FlexPanel.Grow`, `FlexPanel.Shrink`, `FlexPanel.Basis`, `FlexPanel.Left`,
  `FlexPanel.Top`, …; FrameworkElement `Width`/`Height`/`Margin`).
- `availableSize` from the WinUI parent (typically derived from the window
  or a ScrollViewer).
- The `MeasureFunction` return value — which is `child.DesiredSize` after
  WinUI measures, transitively driven by what TextBlock / RichTextBlock /
  third-party controls compute given the constraints.

**Outputs:**
- `Layout.GetPosition(...)`, `LayoutWidth`, `LayoutHeight` on each node.
- WinUI `Arrange(Rect)` calls on each child.
- No persistent state; no I/O; no FFI.

---

## 3. Trust boundaries crossed

This chunk crosses **zero external trust boundaries** in the sense the threat
model uses elsewhere — there is no transport, no parser-of-untrusted-bytes,
no FFI. The chunk's relevance to security is entirely **availability**:
will it terminate, will it allocate sanely, can it be wedged into a state
that the host process cannot recover from?

| Boundary | Assumption made | Validity |
|---|---|---|
| Developer source → YogaStyle field values | Trusted at build time. | OK in principle, but values can be DATA-DRIVEN at runtime (`<TextBlock Width={Binding ServerNumber}/>`). Adversarial server data therefore reaches `YogaValue.Value`. |
| Component tree shape | Bounded by what the developer wrote. | OK for static trees. **Not OK** for app code that builds trees recursively from untrusted data (e.g. a JSON document → component nesting), where the depth becomes attacker-controlled. |
| `MeasureFunction` callback | Returns finite, non-negative `YogaSize`. | YogaNode.Measure (line 285-289) clamps NaN/negative to 0 — defensive. But the function is allowed to **throw**, and exceptions propagate up the recursion, leaving partial layout state. |
| `BaselineFunction` callback | Returns finite, non-negative float. | `CalculateBaseline` (`AlgorithmUtils.cs:168-169`) explicitly **throws `InvalidOperationException`** on NaN — i.e. a misbehaving baseline function in app code crashes layout. |
| Threading | Layout is single-threaded per call. | The class comment on `YogaAlgorithm` says "Thread-unsafe (uses static generation counter)" but does NOT say "do not call concurrently from two threads on disjoint trees." Doing so corrupts cache state via the shared `s_currentGenerationCount`. WinUI dispatcher single-thread serializes UI calls in practice; nothing prevents a non-UI caller from breaking this. |

---

## 4. Asset inventory

What is worth attacking in this chunk:

| Asset | Why it matters |
|---|---|
| **Process availability** (UI thread) | Layout runs on the WinUI dispatcher. If `CalculateLayout` enters infinite recursion, throws an unrecoverable native stack-overflow, or hangs, the entire app freezes. This is the only realistic threat. |
| **Memory** | Per-thread `FlexLineHelper.s_listPool` retains pooled `List<YogaNode>` capacity. `CachedMeasurements[8]` per node is fixed-size, but the per-node `YogaNode` graph's depth is what an adversarial tree controls. |
| **Layout correctness as a security property** | Not generally one. Edge cases: a measured-zero element where the developer expected a click target (security-relevant in a "consent UI" e.g. permission dialog) — a layout bug here that collapses a button to 0×0 would be a UI redress concern, but Yoga clamps measure outputs to non-negative and the WinUI hit-test layer is independent. Out of scope for STRIDE-DoS, but flagged. |

**Not assets in this chunk:** confidentiality (no secrets touched),
integrity of persisted state (none), authentication tokens (none).

---

## 5. STRIDE table

The trust model says this chunk is overwhelmingly a DoS surface; the
table reflects that. "App-author DoS" means a bug a developer writes that
an end user trips, not an external attacker.

| # | Cat | Threat | Attacker model | Impact | Likelihood | Mitigation today | Recommendation |
|---|---|---|---|---|---|---|---|
| T1 | **D**oS | Unbounded recursion in `CalculateLayoutInternal` on a deep tree (no `depth` cap) | Adversarial tree shape — either an app that builds nesting from untrusted data, or a buggy app | Native stack overflow → process crash | Medium for apps that build trees from data; low for static trees | None — `depth` is incremented but never compared to a limit (`YogaAlgorithm.cs:107,110`) | **F-1.** Add a hard depth cap (e.g. 256) and throw a recoverable `InvalidOperationException` instead of letting the CLR raise `StackOverflowException` (which is uncatchable). |
| T2 | **D**oS | Self-cycle / DAG cycle in node tree | An app developer who forgets to remove a node from its previous parent before re-inserting | Infinite recursion in `GetLayoutChildren`, `CollectLayoutChildren`, `GetLayoutChildCount`, `BaselineHelper.CalculateBaseline`, `RoundLayoutResultsToPixelGrid`, `ZeroOutLayoutRecursively`, `LayoutAbsoluteDescendants` | Process crash | Low for careful apps, but **easy to trip accidentally** | `InsertChild` does NOT check `child != this`, does NOT check if `child._owner != null` (which would indicate the child is already parented somewhere) (`YogaNode.cs:96-103`) | **F-2.** In `InsertChild`, reject `child == this` and reject `child._owner != null` with a clear exception. The C++ Yoga checks the latter via `YGNodeInsertChild`; this port has dropped that defensive check. |
| T3 | **D**oS | `Display.Contents` cycle (a node with `Display.Contents` whose grandchild references back into its own subtree) | Same as T2 | Infinite recursion in `GetLayoutChildren` (`YogaNode.cs:153-167`) before any depth-cap from F-1 would help | Low | None | **F-2** subsumes this if the cycle invariant is enforced at insertion; otherwise add a "visited set" to `CollectLayoutChildren`. |
| T4 | **D**oS | NaN propagation in measurement causing pathological cache-miss loop | Developer-bound `Width`/`MaxWidth` to a value that becomes NaN at runtime (e.g. `0.0/0.0` from a chart), then the cache key (which uses `InexactEquals` that treats NaN==NaN) appears to match yet `lastComputedSize` is also NaN, so `SizeIsExactAndMatchesOldMeasuredSize` etc. return false, and every layout pass takes the slow path | Low — slowdown, not crash | Low | `YogaFloat.InexactEquals` (`YogaValue.cs:55-60`) treats NaN as equal to NaN, which is the right choice for the cache key, but the rounding wrapper in `CanUseCachedMeasurement` (`AlgorithmUtils.cs:308-320`) calls `RoundValueToPixelGrid` which propagates NaN cleanly. Behaviour is correct, just slow | Note in code that NaN-bound widths bypass the cache; consider documenting on the public `FlexPanel` properties. |
| T5 | **D**oS | Float overflow → `Infinity` in `RoundValueToPixelGrid` | `MaxWidth` set to `1e38` (within float range), multiplied by `pointScaleFactor` ≥ 4 → `Infinity` → `(float)(scaledValue / pointScaleFactor)` is also `Infinity`, propagated to `LayoutWidth` | Garbage layout but no crash; downstream WinUI `Arrange(Rect)` with `Infinity` width throws `ArgumentException` → uncaught → app crash | Medium for apps that data-bind dimensions | None — `RoundValueToPixelGrid` (`AlgorithmUtils.cs:215-236`) checks for NaN but not Infinity (`AlgorithmUtils.cs:233`) | **F-3.** Clamp result against `float.MaxValue/2` or detect Infinity and treat as undefined. |
| T6 | **D**oS | `AspectRatio` with negative value | Developer sets `node.AspectRatio = -2` from data | Negative computed dimensions, fed back to `BoundAxis` which clamps to padding+border (still non-negative). Then `child.Measure(new Size(negativeWidth, …))` from FlexPanel.cs:304 throws on WinUI side → UI crash | Low | `YogaNode.AspectRatio` setter (`YogaNode.cs:239-248`) handles `0` and `±Infinity` (treats as auto) but not **negative** values, and not NaN explicitly (`float.NaN` would round-trip through unchanged) | **F-4.** Reject negative aspect ratios in the setter (treat as auto), as the C++ Yoga does via `YGAssertWithNode`. |
| T7 | **I**nfo / leak / **R**epudiation | Thrown exceptions surface internal layout state in stack traces (e.g. `BaselineHelper.CalculateBaseline` throws InvalidOperationException) | Adversarial component returns NaN baseline | Stack trace logged | Negligible — stack trace contains no secrets | OK — trace is bounded and informational | None. |
| T8 | **D**oS | Generation-counter wrap | A long-running app that has performed > 4×10^9 layouts (~1 layout/ms × 49 days continuous) | `s_currentGenerationCount` is `uint`, wraps. `LayoutResults.GenerationCount` and `ConfigVersion` could collide with old values, returning a stale cached layout | Very low — extreme uptime | None | Acknowledged. Practical risk is negligible; if it ever matters, switch to `ulong`. |
| T9 | **T**ampering / state-corruption | Unhandled exception inside a `MeasureFunction` callback (e.g. a 3rd-party text control throws on extreme input) | Adversarial input that flows into a child control's measure path | The tree is left half-laid-out: some nodes have `_isDirty=false`, others have stale `Layout`. Subsequent layouts may produce visually wrong results | Low–medium for apps that surface arbitrary content (markdown into RichTextBlock, etc.) | `YogaNode.Measure` (`YogaNode.cs:281-290`) does NOT wrap the callback in try/catch, and the algorithm itself does not | **F-5.** Decide: either wrap the callback and treat exceptions as "measure returned (0,0)" (option A — gracefully degrade), or document the contract and have FlexPanel mark its tree fully dirty on a measure-callback exception (option B — re-run cleanly next pass). The current behaviour (propagate, leave inconsistent state) is the worst of both. |
| T10 | **D**oS | Concurrent layout from non-UI thread | Developer who calls `CalculateLayout` directly from a background task on a tree that the UI thread is also laying out | Cache races, possible infinite loop, possible double-free of pooled lists | Very low | The class doc-comment on `YogaAlgorithm` (`YogaAlgorithm.cs:21`) says "Thread-unsafe", and `s_currentGenerationCount` uses `Interlocked.Increment`, but other state (`LayoutResults._dimensions[]`, `s_listPool`) is unsynchronized. `s_listPool` is `[ThreadStatic]` (`AlgorithmUtils.cs:373-374`) so list-pool corruption is avoided, but per-node Layout still races. | Document the contract on the public `FlexPanel` (it inherits from `Panel` which is dispatcher-affine, so the threat is only realized if a developer opts out). |
| T11 | **D**oS | `RoundValueToPixelGrid` divide by zero when `pointScaleFactor == 0` | Developer sets `Config.PointScaleFactor = 0` | Returns `(float)(scaledValue / 0)` = `Infinity`. Caller in `RoundLayoutResultsToPixelGrid` short-circuits via `if (pointScaleFactor != 0)` (line 252), but `CacheHelper.CanUseCachedMeasurement` also gates on `pointScaleFactor != 0` (line 307). | Practically guarded | Low | OK with the current callers. **F-6.** Defensive: add an early `if (pointScaleFactor == 0) return float.NaN;` to `RoundValueToPixelGrid` so a future caller cannot trip it. |
| T12 | **D**oS | Static `s_listPool` `[ThreadStatic]` Stack<List> grows unboundedly under deep wrap | A FlexPanel with thousands of wrapping flex lines | The pool retains every list ever returned to it for the lifetime of the thread. List capacity is preserved across rents, so a single very-large layout pinned a list that all subsequent (small) renters now pin too | Medium memory bloat, no crash | Low | None — `s_listPool` (`AlgorithmUtils.cs:373-387`) has no max-pool-size, no capacity-trim | **F-7.** Cap the pool to e.g. 16 lists, and trim oversized list capacities before re-pooling (`if (list.Capacity > 256) list.TrimExcess()`). |
| T13 | **E**oP via UI redress | Layout collapses a security-relevant control (consent dialog button) to 0×0 due to a bug | Hostile content in another control (sibling with extreme grow) | Low — Reactor's hit-testing is independent of Yoga, and zero-size controls are unhittable; an adversarial sibling could push a button outside `Arrange` rect | Low | `BoundAxis` clamping ensures non-negative dimensions; `YogaNode.Measure` clamps measure outputs ≥ 0 | Note. Out of Yoga's scope; pairs with WinUI Arrange. |

---

## 6. Findings

Severity scale: **Crit** (process crash from low-effort input), **High** (DoS or
crash from app-data-driven values), **Med** (robustness / latent), **Low** (note).

### F-1 — Recursion has no depth cap (High)

`YogaAlgorithm.cs:107,110` — `CalculateLayoutInternal` takes `uint depth`,
increments it on entry, and passes it through to recursive calls. **It is
never compared to a limit.** The recursion paths go:

- `CalculateLayoutInternal` → `CalculateLayoutImpl` → either branches into
  per-child `CalculateLayoutInternal` (lines 571, 766, 1078, 1602, 2061,
  2073) for measure-passes, layout-passes, stretch passes, and absolute-child
  passes, OR recurses into `LayoutAbsoluteDescendants` (line 882, 2167).
- `BaselineHelper.CalculateBaseline` (`AlgorithmUtils.cs:190`) is recursive
  on layout children with no depth cap.
- `PixelGridHelper.RoundLayoutResultsToPixelGrid` (`AlgorithmUtils.cs:280`)
  is recursive over all children.
- `YogaAlgorithm.ZeroOutLayoutRecursively` (line 1255), `CleanupContentsNodesRecursively`
  (line 1279), `LayoutAbsoluteDescendants` (line 2167) all recurse without a cap.

A tree with 5000–10000 levels of nesting (which an app-driven tree-from-JSON
can produce by accident) raises `StackOverflowException`, which the CLR
treats as **uncatchable** since .NET 2.0. The process dies.

The C++ Yoga has the same shape and is theoretically vulnerable to the same
issue; it is acknowledged in the upstream code that a developer pathological
tree is a developer bug. But the C# port runs on the UI thread of a desktop
app and the consequence is "the user's app crashes," which is worse than a
unit test failing.

**Recommend:** Add a `const uint MaxDepth = 256;` constant; in
`CalculateLayoutInternal` after `depth++`, check `if (depth > MaxDepth)
throw new InvalidOperationException("Yoga layout depth exceeded.");`.
Mirror in baseline / round / zero / cleanup recursions, or convert those
to explicit-stack iteration. `InvalidOperationException` is catchable by
the WinUI dispatcher; `StackOverflowException` is not.

### F-2 — `InsertChild` permits cycles and aliasing (High)

`YogaNode.cs:96-103`:

```csharp
public void InsertChild(YogaNode child, int index)
{
    if (_measureFunc != null)
        throw new InvalidOperationException(...);
    _children.Insert(index, child);
    child._owner = this;
    MarkDirtyAndPropagate();
}
```

There is no check that `child != this`, and no check that `child._owner ==
null` (i.e. that the child is not already parented somewhere — the C++
Yoga `YGNodeInsertChild` requires `child->getOwner() == nullptr`). Missing
both means:

- `node.InsertChild(node, 0)` produces self-cycle. `GetLayoutChildren`
  (line 153-167) recurses indefinitely; before F-1's cap can fire, the
  iterator state machine pins infinite memory.
- `parentA.InsertChild(child, 0); parentB.InsertChild(child, 0);` corrupts
  parent pointers — `child._owner` ends up pointing to `parentB` while
  `parentA._children` still contains `child`. Subsequent layouts produce
  inconsistent dirty propagation (`MarkDirtyAndPropagate` walks via
  `_owner` only, so `parentA` is never marked dirty even when the shared
  child is mutated).

`FlexPanel.SyncYogaTree` (lines 481-493) does some defensive
`RemoveChild`/`InsertChild` reordering, but for a node not parented by
`_rootNode` it just calls `InsertChild` regardless of the child's existing
owner. If two FlexPanels somehow share a `YogaNode` (e.g. via developer
error), state corrupts silently.

**Recommend:** In `InsertChild`, throw on `child == this` and on
`child._owner != null && child._owner != this`. Mirror the C++ assertion.

### F-3 — `RoundValueToPixelGrid` does not clamp Infinity (Med)

`AlgorithmUtils.cs:215-236`. The function explicitly tests for NaN inputs
(line 231, 233) and returns `float.NaN` when either input is NaN. It does
not test for `Infinity`. With `MaxWidth = 1e38` and `pointScaleFactor =
4`, `value * pointScaleFactor` overflows to `+Infinity`, and the final
`(float)(scaledValue / pointScaleFactor)` returns `Infinity` rather than
`NaN`. `Infinity` then flows to `LayoutResults.SetDimension`, then to
`FlexPanel.MeasureOverride` where it is returned as the panel's
`DesiredSize`. WinUI happily accepts that, but on Arrange,
`Rect(0, 0, +Infinity, …)` throws `ArgumentException` from
`Windows.Foundation.Rect`. Result: the app crashes on a layout pass
triggered by a data-bound `MaxWidth`.

The likelihood is low — most code paths get `MaxWidth` from a
DependencyProperty whose default is undefined — but a chart binding feeding
"max value of dataset" to a width is plausible.

**Recommend:** At the end of `RoundValueToPixelGrid`, also test
`float.IsInfinity` and return `float.NaN` (or clamp to a sentinel finite
maximum). Optionally clamp at `BoundAxis` instead, where it would catch
the broader class of cases.

### F-4 — `AspectRatio` setter does not reject negative values (Med)

`YogaNode.cs:239-248`:

```csharp
public float AspectRatio
{
    get => _style.AspectRatio;
    set
    {
        // Degenerate aspect ratios act as auto
        _style.AspectRatio = (value == 0 || float.IsInfinity(value)) ? float.NaN : value;
        MarkDirtyAndPropagate();
    }
}
```

A negative aspect ratio passes the gate. It is then used in
`YogaAlgorithm.cs:1010, 1015, 1031, 1046, 1545, 2041, 2043` as a divisor
or multiplier on `(childWidth - marginRow)`. A negative aspect ratio
produces a negative computed dimension, which `BoundAxis` clamps via
`MaxOrDefined(value, paddingAndBorder)`. The result is a child with
clamped-to-padding size — non-crashing — but the FlexPanel-side code at
`FlexPanel.cs:304` does `child.Measure(new Size(layout.Width + m.Left +
m.Right, ...))`, and if margins are zero and Yoga's clamped dimension is
the padding+border value, there's no negative leak. So today this is a
correctness bug masquerading as a security one.

The C++ Yoga's `YGNodeStyleSetAspectRatio` does not check sign either,
but it asserts in debug-only paths. The C# port silently accepts it.

**Recommend:** Treat negative as `float.NaN` (auto), same as the existing
`0` and `Infinity` cases. Cheap, prevents one class of "weird layout from
data-bound props."

### F-5 — Exceptions from `MeasureFunction` corrupt half-laid-out tree state (Med)

`YogaNode.cs:281-290`:

```csharp
internal YogaSize Measure(float availableWidth, YogaMeasureMode widthMode, …)
{
    var size = _measureFunc!(this, availableWidth, widthMode, availableHeight, heightMode);
    if (YogaFloat.IsUndefined(size.Width) || size.Width < 0)
        size.Width = YogaFloat.MaxOrDefined(0, size.Width);
    …
}
```

If `_measureFunc` throws (e.g. a TextBlock with a malformed font fallback,
a third-party control with a bug), the exception unwinds straight through
`MeasureNodeWithMeasureFunc` → `CalculateLayoutImpl` → `CalculateLayoutInternal`
→ `CalculateLayout`. The tree is left in an indeterminate state:

- Some nodes have `Layout.GenerationCount = generationCount` set, others
  not.
- Some nodes have `_isDirty = false` set (line 242), others still dirty.
- The cache slot at `NextCachedMeasurementsIndex` may have been advanced
  past the partial write.

Subsequent `CalculateLayout` calls will see partially-stale cache entries
and may return wrong results without ever re-running the offending
subtree.

**Recommend:** Document the contract on `MeasureFunction` (and, in the
public FlexPanel callback at `FlexPanel.cs:445-465`, wrap the measure
delegate in a try/catch that returns `YogaSize(0,0)` on exception and
calls `node.MarkDirty()` to force re-measure next pass).

### F-6 — `RoundValueToPixelGrid` divide-by-zero is currently guarded only by callers (Low)

See T11. `AlgorithmUtils.cs:215`. Both call sites guard, but adding a
defensive `if (pointScaleFactor == 0) return float.NaN;` at the function
entry costs one comparison and prevents future regressions.

### F-7 — `s_listPool` has no size or capacity bound (Low)

`AlgorithmUtils.cs:371-387`. `[ThreadStatic]` `Stack<List<YogaNode>>` with
no upper bound on the stack size and no `TrimExcess` on returned lists.
A spike of 10,000 children on one layout pass leaves the thread holding
a 10k-capacity `List<YogaNode>` reference for the rest of the process
lifetime. Memory bloat, not a correctness bug.

**Recommend:** Cap pool depth at 16; trim list capacity above 256.

### F-8 — `ResolvedMin/MaxDimension` comments contradict the code (Low / docs)

`YogaStyle.cs:317-321`:

```csharp
// Match C++ FloatOptional addition: always add padding+border in content-box mode,
// even when value is undefined — the padding+border itself forms a minimum.
float paddingAndBorder = …;
float pb = YogaFloat.IsDefined(paddingAndBorder) ? paddingAndBorder : 0;
return value + pb;
```

`value` is `NaN` when undefined; `NaN + pb = NaN` regardless of `pb`. The
comment claims "even when value is undefined — the padding+border itself
forms a minimum," which would imply returning `pb` when `value` is NaN.
The actual behavior matches the C++ `FloatOptional + float = FloatOptional`
semantics (undefined+anything=undefined), which is correct for the
algorithm. The comment is misleading.

**Recommend:** Either fix the comment, or change behaviour if the
comment was the design intent (it was not — verified by reading
`yoga/numeric/FloatOptional.h` and the C++ `yoga/style/Style.h` resolution
paths, which propagate undefined exactly as this port does).

### F-9 — Generation counter wraparound (Low)

See T8. Cosmetic for any realistic uptime, but trivially fixable by
making `s_currentGenerationCount` and the matching `LayoutResults`
fields `ulong`. Not worth a code change today; flag as a known limit.

### F-10 — `BaselineHelper.CalculateBaseline` throws on NaN with no recovery path (Low)

`AlgorithmUtils.cs:168-169`. Throws `InvalidOperationException` if the
user's baseline function returns NaN. Unlike `Measure` (which clamps
NaN/negative to 0), the baseline path crashes hard. This is consistent
with C++ Yoga, which `YGAssert`s on the same condition. For Reactor's
"app developer experience" this is harsher than Measure's silent clamp;
consider matching the Measure policy (clamp NaN baseline to 0 and log).

### F-11 — `YogaConfig.Default.Freeze` enforced only by `Debug.Assert` (Low)

`YogaConfig.cs:36, 46, 59, 75, 85, 95`. All mutating setters guard with
`Debug.Assert(!_frozen, …)`, which is a no-op in Release builds. A
release build can mutate `YogaConfig.Default`, affecting every node that
holds a reference to it. The blast radius is the per-process default;
"silent drift" of point-scale or web-defaults across a long session is
the worst outcome.

**Recommend:** Promote to runtime check (throw `InvalidOperationException`)
in Release as well. The cost is one branch per setter, run rarely.

---

## 7. Open questions

1. **Is layout ever called from a background thread today?** The
   `YogaAlgorithm` doc-comment says thread-unsafe, but I did not audit
   every call site in the 303-file Reactor core to verify. Spec
   confirmation from the team would resolve T10's likelihood.

2. **Are app-author trees ever generated from external data?** If yes —
   e.g. a JSON-driven UI, a markdown renderer, a charting DSL — F-1 (depth
   cap) and F-2 (cycle check) become "External-data DoS" instead of
   "developer footgun." The chunking doc lists Markdown (Chunk 10),
   Charting (Chunk 21), and Data system (Chunk 22) as places where this
   coupling could be introduced. Each of those chunks should ask "does
   this code path produce a YogaNode tree whose depth is attacker-bounded?"

3. **What is the policy on `MeasureFunction` exceptions?** Today: silently
   tear the tree apart. The team should pick an explicit policy (F-5).

4. **Is `YogaConfig.Default` mutable in Release builds intentional?** F-11.
   If yes, the design decision should be documented; if no, fix is one
   line per setter.

5. **CVE cross-reference (Section 9 below):** I did not find a published
   CVE for the Meta-Yoga C++ engine in the major databases (NVD, GitHub
   Security Advisories) at the time of this review. The closest public
   issues are unbounded recursion bugs filed against `facebook/yoga` that
   were closed as "expected on adversarial input" and a 2022 fix in
   `yoga/algorithm/CalculateLayout.cpp` for an aspect-ratio + percent
   interaction that crashed in debug. **Open question:** does the team
   want the port to harden against these classes proactively (yes,
   IMO — see F-1, F-2, F-4) or stay 1:1 with upstream?

---

## 8. Out-of-scope referrals

| Surfaced concern | Belongs to |
|---|---|
| Whether app code that builds Yoga trees from external data feeds it adversarial depth | Chunk 10 (Markdown), Chunk 21 (Charting), Chunk 22 (Data system / DataGrid). Add a check to those: "does this subsystem produce YogaNodes whose depth or count is controlled by untrusted data?" |
| WinUI `Rect` rejecting Infinity → uncaught `ArgumentException` | Chunk 14 (Reconciler / hosting) — the unhandled-exception policy on the dispatcher is a framework concern. F-3 prevents Yoga from being the trigger; the broader policy is elsewhere. |
| Threading discipline (no background-thread layout) | Chunk 14 — same dispatcher-affinity question as the rest of the core. |
| `FlexPanel`'s `MeasureFunction` callback into WinUI (re-entrant `Measure`/`Arrange` semantics) | Chunk 15 (Hosting) — WinUI's own LayoutCycleException is the contract Reactor must honour. The defensive `_arranging` flag in `FlexPanel.cs:330,363,451` exists to avoid re-entry; that pattern should be reviewed alongside other panels in the framework. |
| Public `FlexPanel` API attack surface (DependencyProperty values from XAML loaded at runtime from a *.xaml-marked-untrusted source) | Out of scope — Reactor does not load untrusted XAML. The framework treats XAML as code. |

---

## 9. CVE / advisory cross-reference

- **`facebook/yoga` CVEs:** none filed in NVD or GHSA at review date
  (2026-04-30). The C++ engine has had bug reports for stack-overflow
  on adversarial trees (closed as "developer error") and for assertion
  failures on degenerate aspect ratios in debug builds (fixed silently).
- **React Native (which embeds Yoga) CVEs:** several CVEs touch RN's
  bridge or native modules, none specific to Yoga's flexbox math.
- **Comparable C# layout-engine vulnerabilities:** Microsoft
  WindowsAppSDK has had `LayoutCycleException` correctness bugs but
  not security ones. The C# port's introduction of recursion without
  a depth cap (F-1) is **a regression vs. defensive practice in
  comparable managed-side ports** (e.g. WPF panels generally have
  iterative layout).

The recommendation is therefore not driven by a public CVE; it is
driven by the structural absence of guards that the C++ original
relies on language-level (stack-probing on Linux, watchdogs on iOS)
or developer-discipline (debug asserts) and that do not translate to
.NET's uncatchable `StackOverflowException` model.

---

## Summary

This is a **faithful** port of Yoga, and the algorithm itself is correct.
The findings are not bugs in the math; they are the gaps a port leaves
behind when the host runtime has different failure modes from the
original (uncatchable stack overflow, no debug-only `YGAssert`, no
language-level signed-overflow trapping).

The four findings worth fixing now: **F-1** (depth cap), **F-2** (cycle
check on `InsertChild`), **F-3** (Infinity clamp on rounded values),
**F-4** (negative aspect-ratio handling). Each is a few lines. F-5 is
a design call for the team. The rest are notes.
