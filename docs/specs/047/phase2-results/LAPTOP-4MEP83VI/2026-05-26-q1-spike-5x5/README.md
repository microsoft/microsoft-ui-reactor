# Spec 047 §14 Phase 2 — Q1 head-to-head (descriptor vs hand-coded handler)

**Spike status:** measurement complete. Q1 matrix verdict below.

**Capture environment.** LAPTOP-4MEP83VI, ARM64-native, Release, .NET 10.0.8,
Windows 11 26200, AC power, other apps closed before capture, 3 separate
process launches × 5 reps per launch = **15 measurements per (bench, variant)
cell**. (Launches 4–5 from the planned 5-launch run died on long-pole
benches at process minute ~80 — sufficient data captured at n=15.)

**Variants compared.**

- `ReactorToday` — legacy `MountXxx` switch (V1 OFF).
- `ReactorV2` — Phase 1 hand-coded handlers
  (`ToggleSwitchHandler` / `SliderHandler` / `BorderHandler` /
  `TextBoxHandler` / `ListViewHandler`) on the V1 dispatch shell.
- `ReactorDescriptors` — Phase 2 descriptor interpreter
  (`DescriptorHandler<TElement,TControl>` driving
  `ToggleSwitchDescriptor` / `SliderDescriptor` / `BorderDescriptor`),
  same V1 dispatch shell, same `MountContext` surface. TextBox and
  ListView keep their hand-coded handlers so every bench has a working
  dispatch.

**Source under test.** `src/Reactor/Core/V1Protocol/Descriptor/` — fluent
`ControlDescriptor<TElement,TControl>` + `PropEntry` types + interpreter.
Behavior parity verified by 23 AppTests.Host self-test assertions
(`Desc_ToggleSwitch_*`, `Desc_Slider_*`, `Desc_Border_*`) — every
descriptor variant produces identical DP values and identical
callback-fire patterns to the hand-coded handlers across mount, update,
programmatic-write, and coercion paths.

## Headline result

**Q1 matrix verdict: ship hand-coded handlers as the primary author
surface.** Two of the four required Q1 benches (M2, M10) land in the
">15% slower" band, both on the same control × event shape (ToggleSwitch +
controlled callback).

| Bench | Desc vs V2 ns | Desc vs V2 alloc | Q1 band |
|---|---:|---:|---|
| M1 Mount_Leaf_NoCallback (TextBlock — not contested) | +1.3% | -0.0% | <=5% |
| M2 Mount_Leaf_OneCallback (ToggleSwitch + callback) | **+19.1%** | +15.9% | **>15%** |
| M5 Dispatch_Switch_Warm (8-element mix) | +13.4% | +4.1% | 5-15% |
| M7 Update_NoChange (1000 TextBlocks) | -8.2% | +0.0% | 5-15% |
| M10 EventHandlerState_Alloc (ToggleSwitch + callback) | **+31.5%** | +21.7% | **>15%** |

M2 alone is sufficient to trigger the §13 Q1 ">15% on any of M1/M2/M5/M7"
clause.

## Where the cost lives

The +19% / +31% gap is concentrated in **one** code path: the descriptor's
`ControlledPropEntry.EnsureSubscribed` route through public
`ReactorBinding<T>.OnCustomEvent`. Specifically:

| Step | Hand-coded handler | Descriptor model |
|---|---|---|
| Per-control trampoline storage | Typed payload (`ToggleSwitchEventPayload`) stashed in `ControlEventStateBox`, slot is a strongly-typed `RoutedEventHandler` field | Closure appended to `CustomEventAnchorPayload.Trampolines` list (no dedup), gated by per-entry `ConditionalWeakTable<TControl,object>` |
| First-mount trampoline allocation | Static delegate — captures nothing; one allocation total per process | Closure capturing `fe + readBack + getCallback` per first-mount per control; ~4 captures |
| Subscription idempotence | Null-slot check on the typed payload field (one ref load) | CWT lookup (hash + bucket walk) |
| Dispatcher path | Static method invocation | Closure invocation through `Action<TElement, TArgs>` indirection |

The hand-coded handlers use `Reconciler.GetOrCreateControlEventPayload<T>`,
which is **internal**. Descriptors authored by external assemblies cannot
call it; the only public alternative is `OnCustomEvent`, which has the
shape above.

This is the gap KD-4 already identifies in the Phase 1 known-defects list:
> "Phase 2 followup. Make `OnCustomEvent` idempotent against re-mounts of
> the same control: dedup by the user's subscribe/unsubscribe MethodInfo
> or similar stable key… Pair with a public typed-event surface for the
> audited eight (`OnToggled`, `OnClick`, `OnTextChanged`, `OnValueChanged`,
> `OnImageOpened`, `OnImageFailed`, `OnViewChanged`,
> `OnNumberBoxValueChanged`) so external authors get the same fast path
> the built-ins use."

The Q1 matrix outcome turns on this exact gap. If KD-4 lands first,
descriptors would be re-measured against the typed-event surface both
shapes can use; the +19/+31% likely shrinks dramatically because the
remaining interpreter overhead (M1: +1.3%, M9: -0.1%) is in the noise.

## Where the descriptor model is competitive

Stripping away the controlled-event path leaves the interpreter's own tax.
On benches where the contested controls *don't* involve callback wiring,
the descriptor model is within noise of (or faster than) the hand-coded
handler:

| Bench | Desc vs V2 ns | What it measures |
|---|---:|---|
| M1 (TextBlock, no callback) | +1.3% | Baseline mount path — interpreter does not engage |
| M7 (no-change update, 1000 TextBlocks) | -8.2% | Diff path — interpreter not engaged |
| M9 (all-changed update, 1000 TextBlocks) | -0.1% | Pure interpreter property-diff loop |
| M5 (8-element warm mix) | +13.4% | Mix; high CI, judgment-call band |

M9 is a particularly clean signal: a 1000-element tree where every prop
differs every iteration exercises the interpreter's property-entry
iteration directly. **0.1% gap** — the iteration overhead is in the
noise.

## Q1 matrix application

Per §13 Q1's pre-committed decision matrix:

> **Descriptor >15% slower on any of M1/M2/M5/M7:** ship hand-coded
> handlers as the primary surface. Descriptors stay available for
> *late-bound external controls* only (the §16-permanent-fallback path),
> not as the recommended first-party shape. Revisit when source-gen could
> collapse the cost.

M2 is +19.1%. The matrix triggers. **Primary surface: hand-coded
`IElementHandler<TElement,TControl>`.** Descriptors remain available as a
secondary author shape — useful for runtime-registered third-party
controls where the author can't ship a custom handler class (§16
permanent-fallback path) — but they are not the recommended first-party
shape for built-in or split-library controls.

## Reopen condition

Re-measure Q1 after either of:

1. **KD-4 ships** (public typed-event surface + `OnCustomEvent` dedup).
   The descriptor's controlled-event path now uses the same trampoline
   storage as the hand-coded handlers. Expected delta: M2 / M10 drop
   from +19/+31% to near-noise; matrix verdict flips.

2. **Source generation lands** (§7, currently deferred). The generator
   emits handlers from descriptor-style attributes; per-control event
   tables are statically known. The §11 / §12 memory wins were always
   gated on source-gen anyway.

Until either, the §13 Q1 verdict stands.

## Files

- `launch-1.jsonl` / `launch-2.jsonl` / `launch-3.jsonl` — raw bench output,
  JSON-Lines, one row per (bench × variant × rep).
- `aggregate.py` — reads `launch-*.jsonl`, emits the means + 95% CI table
  and the Q1 deltas. Run with no arguments from this directory.
