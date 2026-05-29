# Echo-suppressor live call-site inventory — refreshed for Phase 4 §4.2

**Status:** the Phase-0 audit `begin-suppress-audit.csv` is **STALE**. Its 24 rows
cite legacy `Reconciler.Mount.cs` / `Reconciler.Update.cs` line numbers that no
longer exist — §4.5 deleted the legacy dispatch-switch *arms* and §4.2 (part A,
commit `8a67e34a`) deleted the ~28 orphaned legacy value-control handler *bodies*
they left behind. The categories in the CSV (eliminable-tight-diff / coercion /
float-precision / items-coercion / user-state-races) still describe the *semantic*
classes, but the *locations* are wrong. Use this file as the current source of
truth for the §4.2 "eliminate `ChangeEchoSuppressor`" surface.

This inventory was produced while scoping §4.2 part B and is the prerequisite the
spec author asked to refresh before the elimination is attempted (see the §4.2
handoff note in `047-extensible-control-model-phase4-implementation.md`).

## How the mechanism works today (counter token, causal)

- `ReactorBinding.WriteSuppressed(ctrl, mutate)` / `WriteSuppressed<T>` (PUBLIC API,
  `src/Reactor/Core/ReactorBinding.cs:29-46`) bumps `ReactorState.EchoSuppressCount`
  then runs `mutate`.
- Each control's change-event trampoline calls `ChangeEchoSuppressor.ShouldSuppress`
  as its first line; that consumes exactly **one** pending token (decrement) and
  returns early, so the synthesized echo from the programmatic write never reaches
  the user callback. This is a **causal** token — it suppresses the next event
  *caused by* the write, regardless of value.
- Separately, `EchoSuppressScopeDepth` (`ReactorState`, bumped around
  `ApplySetters`, `Reconciler.cs:2263/2270`) suppresses **any** echo while a
  user-authored `.Set(c => c.IsOn = true)` lambda runs — the engine cannot predict
  which value-bearing DPs the lambda writes, so there is **no expected value** here.
- Storage is on the attached `ReactorState` DP (not a CWT) to survive dual-RCW
  wrappers (issues #86 / #114).

## Why full elimination is a dedicated effort, not an autopilot pass

The spec proposes replacing the counter with per-control "expected Y ± tolerance,
suppress one echo" metadata. Independent review (rubber-duck, this session)
confirmed this is **causally weaker** and must not be rushed:

1. A value-predicate token cannot prove the matching event was *caused by* the
   write. A real user event that lands on the same value/tolerance before the
   pending metadata clears would be swallowed → silent missed callback / state
   corruption. (Even Slider/NumberBox bounds are user-reachable values.)
2. `EchoSuppressScopeDepth` / `ApplySetters` has no value to compare — a scoped
   suppression primitive must survive regardless.
3. `ReactorBinding.WriteSuppressed(UIElement, Action)` is **public** and carries no
   value / readback / property-identity / tolerance — external authors (e.g. the
   `MarqueeControl` external proof) rely on the current shape.
4. Existing fixtures (`EchoSuppressionFixtures`, `Spec047EchoStranding`) cover the
   no-echo and stranded-token classes but **not** the new "real event coincides
   with expected value" regression class. New regression coverage is required
   *before* any rewrite.

**Recommendation:** refresh-audit (this file) + new regression fixtures + spec
review first; then migrate per-class with the counter retained until every class
has a proven replacement. Do not delete the suppressor / `EchoSuppressCount` /
`EchoSuppressScopeDepth` until all classes below are covered.

## Live call sites (post-§4.2A; verified by ripgrep)

### Class A — general controlled value (causal counter required)
Trampoline `ShouldSuppress` gate + `WriteSuppressed` programmatic write. Value
collision possible (user can reproduce the engine-written value), so NOT safe to
drop to value-compare.

| Site | File:line |
|---|---|
| ControlledPropEntry trampoline / Update | `V1Protocol/Descriptor/PropEntry.cs:205, 253` |
| HandCodedControlledPropEntry Update | `V1Protocol/Descriptor/PropEntry.cs:388` |
| AutoSuggestBoxDescriptor | `…/Descriptors/AutoSuggestBoxDescriptor.cs:63` |
| ComboBoxDescriptor | `…/Descriptors/ComboBoxDescriptor.cs:44` |
| ExpanderDescriptor | `…/Descriptors/ExpanderDescriptor.cs:60, 68` |
| FlipViewDescriptor | `…/Descriptors/FlipViewDescriptor.cs:34` |
| GridViewDescriptor | `…/Descriptors/GridViewDescriptor.cs:20` |
| ListBoxDescriptor | `…/Descriptors/ListBoxDescriptor.cs:37` |
| NumberBoxDescriptor (ValueChanged) | `…/Descriptors/NumberBoxDescriptor.cs:51` |
| PasswordBoxDescriptor | `…/Descriptors/PasswordBoxDescriptor.cs:35` |
| PivotDescriptor | `…/Descriptors/PivotDescriptor.cs:32` |
| PipsPagerDescriptor | `…/Descriptors/PipsPagerDescriptor.cs:31` |
| RadioButtonsDescriptor | `…/Descriptors/RadioButtonsDescriptor.cs:44` |
| RichEditBoxDescriptor (gate + bare-mount BeginSuppress) | `…/Descriptors/RichEditBoxDescriptor.cs:51, 91` |
| SelectorBarDescriptor | `…/Descriptors/SelectorBarDescriptor.cs:36` |
| TabViewDescriptor | `…/Descriptors/TabViewDescriptor.cs:48` |
| TemplatedFlipViewDescriptor | `…/Descriptors/TemplatedFlipViewDescriptor.cs:53` |
| TextBoxDescriptor | `…/Descriptors/TextBoxDescriptor.cs:60` |
| SliderHandler | `V1Protocol/Handlers/SliderHandler.cs:47, 95` |
| TextBoxHandler | `V1Protocol/Handlers/TextBoxHandler.cs:94, 119, 126` |
| ToggleSwitchHandler | `V1Protocol/Handlers/ToggleSwitchHandler.cs:40, 66` |
| CheckBox legacy body (Path-B via CheckBoxHandler) | `Reconciler.Mount.cs:452,460,468`; `Reconciler.Update.cs:627,635,643,653` |

### Class B — coercion (Slider/NumberBox Min/Max)
`CoercingOneWayPropEntry.Update` wraps the write in `WriteSuppressed` when the new
Min/Max coerces the live Value. Bounds are user-reachable → also not collision-immune.

| Site | File:line |
|---|---|
| CoercingOneWayPropEntry | `V1Protocol/Descriptor/PropEntry.cs:502` |
| SliderHandler Min/Max | `V1Protocol/Handlers/SliderHandler.cs:83, 90` |
| NumberBoxDescriptor Min/Max (`.CoercingOneWay`) | `…/Descriptors/NumberBoxDescriptor.cs:71-78` |

### Class C — float precision (NumberBox immediate mode)
Already tolerance-based via `AreNumberBoxValuesEquivalent` (1e-12 relative), but
feeds the Class-A NumberBox callback path.

| Site | File:line |
|---|---|
| HandleNumberBoxImmediateTextChanged | `Reconciler.Mount.cs:427` (`BeginSuppress`), `:423-425` (`AreNumberBoxValuesEquivalent`) |

### Class D — collection batch (CalendarView)
Per-mutation `IObservableVector` echoes suppressed across a clear+add / diff batch.
A counter/scope is the natural fit; value-compare does not model "suppress N events".

| Site | File:line |
|---|---|
| CollectionDiffControlledPropEntry | `V1Protocol/Descriptor/PropEntry.cs:818` |
| CalendarViewDescriptor trampoline | `…/Descriptors/CalendarViewDescriptor.cs:63` |
| SyncSelectedDates shim | `Reconciler.Update.cs:2551, 2561` |

### Class E — setter scope (no expected value)
| Site | File:line |
|---|---|
| `EchoSuppressScopeDepth` + `ApplySetters` | `Reconciler.cs:606, 2263, 2270`; consumed in `ChangeEchoSuppressor.cs:64` |

### KD-1 drain + public API + source
| Site | File:line |
|---|---|
| `OnCustomEvent` `ShouldSuppress` drain | `V1Protocol/ReactorBindingT.cs:178` |
| `ReactorBinding<T>.WriteSuppressed` (instance) | `V1Protocol/ReactorBindingT.cs:192` |
| `ReactorBinding.WriteSuppressed` / `<T>` (PUBLIC) | `ReactorBinding.cs:29, 41` → `BeginSuppress` 33, 45 |
| `ChangeEchoSuppressor` (the class to delete) | `ChangeEchoSuppressor.cs:37-73` |
| `ReactorState.EchoSuppressCount` (the −4-byte field) + resets | `Reconciler.cs:602, 763, 788, 1097` |
