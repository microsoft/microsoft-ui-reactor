# Change-event echo — incident report and framework-level mitigations

**Date:** 2026-04-23
**Area:** `Reconciler.Update*` / `Reconciler.Mount*` for value-bearing WinUI controls.
**Severity:** Silent cross-component state corruption.

## What happened

The DataGrid's `Specialized Editors` demo exhibited an "exact value swap" bug: clicking Row A's Accent cell and then Row B's Accent cell caused Row A's `AccentColor` to be overwritten with Row B's color byte-for-byte. The bug reproduced every time.

Root cause was *not* in the DataGrid edit pipeline. It was in the **`PropertyGrid` bound to the selected row**, which renders an `Editors.Color()` (a full WinUI `ColorPicker`). When the selection shifted from Row A to Row B:

1. `UpdatePropertyGrid` → `UpdateColorPicker` ran with `n.Color = rowB.AccentColor`.
2. `UpdateColorPicker` wrote `cp.Color = n.Color` **before** calling `SetElementTag(cp, n)`. The ColorPicker's tag at that moment was still `rowAColorPickerElement` (the previous render).
3. `ColorPicker.ColorChanged` fired synchronously (in WinAppSDK 2.0-preview for this property). The handler read the stale tag, invoked *Row A's* `OnColorChanged` with `rowB.AccentColor`, and the PropertyGrid wrote `rowB.AccentColor` into `rowA.AccentColor`.

Because the echo fires on every programmatic write where the tag hasn't been swapped yet, the same issue silently corrupted `Quantity`, `UnitPrice`, `OrderDate`, and `Duration` on row A too — we only noticed the color because it was visually obvious.

## The class of bug

Every Microsoft.UI.Reactor (Reactor) updater for a WinUI control whose change event fires from a programmatic property write is vulnerable:

| Control | Change event fired by programmatic set |
|---|---|
| `ColorPicker.Color`     | `ColorChanged` — confirmed sync (or near-sync) |
| `NumberBox.Value`       | `ValueChanged` — confirmed |
| `ToggleSwitch.IsOn`     | `Toggled` — confirmed |
| `Slider.Value`          | `ValueChanged` — confirmed |
| `DatePicker.Date`       | `DateChanged` — confirmed |
| `CalendarDatePicker.Date` | `DateChanged` — confirmed |
| `TimePicker.Time`       | `TimeChanged` — confirmed |
| `CheckBox.IsChecked`    | `Checked` / `Unchecked` / `Indeterminate` — confirmed |
| `RadioButton.IsChecked` | `Checked` / `Unchecked` — confirmed |
| `ToggleSplitButton.IsChecked` | `IsCheckedChanged` — confirmed |
| `TextBox.Text`          | `TextChanged` — dispatched async, still observable |
| `PasswordBox.Password`  | `PasswordChanged` — confirmed |
| `AutoSuggestBox.Text`   | `TextChanged` — but filtered to `UserInput`, effectively safe |
| `RatingControl.Value`   | `ValueChanged` — does NOT fire from programmatic write in preview2 |
| `ComboBox.SelectedIndex` | `SelectionChanged` — ComboBox re-mounts on update, no echo path |

Anything that mounts a WinUI control and wires a `SomethingChanged` handler is a candidate — even adding a new control later without realizing the pattern reintroduces the bug.

## The fix that shipped

1. A small helper — `src/Reactor/Core/ChangeEchoSuppressor.cs` — backed by a `ConditionalWeakTable<UIElement, Counter>`. `BeginSuppress(control)` increments; the handler calls `ShouldSuppress(control)` as its first line and returns early if it decremented a token.
2. Every `Update<Control>` method now: (a) calls `SetElementTag` *first* (defense-in-depth even if the handler is delayed), (b) guards the value write with an equality check, and (c) calls `BeginSuppress` immediately before the assignment.
3. Every `Mount<Control>` handler wraps the user-callback dispatch behind `ShouldSuppress`. Pooled controls (`TextField`, `ToggleSwitch`) also get `BeginSuppress` + `SetElementTag`-first on re-rent so the retained handler sees the new element.
4. Regression coverage: `tests/Reactor.AppTests.Host/SelfTest/Fixtures/EchoSuppressionFixtures.cs` (12 fixtures, 48 assertions) exercises mount → update → simulated-user-edit for every affected control.

## How we should have caught this sooner

### 1. A selftest fixture per new control is non-negotiable — and it must cover the echo path

The existing `SpecializedEditorsTests` only asserted that the control *mounted*. That's a shallow test: it never toggled state, never re-rendered, never watched whether onChange fired spuriously. The mitigation is to make the echo invariant part of the per-control fixture template:

> Every fixture for a control that exposes an `onChange`-style parameter must assert, at minimum:
>   - mount does not invoke `onChange`,
>   - an internal state change that re-renders the control with a new value does not invoke `onChange`,
>   - direct property write simulating user input *does* invoke `onChange`.

Add this to the "How to add a new element type" checklist in `CONTRIBUTING.md` (§6) so the requirement is visible when new controls land.

### 2. Make the safe pattern the default: a `SetSuppressed<T>` helper the updaters opt into by calling

Right now every updater hand-rolls the `SetElementTag` + equality-check + `BeginSuppress` + assignment pattern. It's easy to add a new updater and forget one of the four steps. Extracting a helper like:

```csharp
// In Reconciler.cs
private static void SetSuppressed<TVal>(UIElement control, TVal current, TVal target,
    Action<TVal> assign)
    where TVal : IEquatable<TVal>
{
    if (current.Equals(target)) return;
    ChangeEchoSuppressor.BeginSuppress(control);
    assign(target);
}

// Usage
SetSuppressed(cp, cp.Color, n.Color, v => cp.Color = v);
```

makes the updater code one line per property and eliminates the class of mistake where someone adds a new property and forgets to guard it. The three-line body today *is* the pattern — but by not having a name it's also invisible to the next contributor.

### 3. A unit-level "echo-check" scaffold

The selftests live in-process with real WinUI, which is the right home for event semantics. But there's a unit-level check we can run in `Reactor.Tests` that catches the *static* mistake: for every `Update<X>` method, verify it calls `SetElementTag` **before** any non-const property assignment on the control. That's a Roslyn-based lint we could run in CI:

- Parse `Reconciler.Update.cs` into a syntax tree.
- For every method named `Update*`, find the first statement that's either `SetElementTag(...)` or a member assignment on the control parameter.
- Fail if the member assignment comes first.

That catches the specific shape of the bug (tag-set-after-value-write) without any runtime instrumentation, costs essentially nothing in CI time, and scales as new updaters are added. It's weaker than the selftest (won't catch cases where suppression is needed but absent), but it's faster feedback and complementary.

### 4. Documentation: name the pattern in the reconciler comment header

`Reconciler.cs` opens with a summary of how mount / update / children are split across partial files. That's the place to add a short "every value-bearing updater obeys this contract" paragraph pointing at `ChangeEchoSuppressor`. Contributors reaching for the pattern should find it the moment they're looking for where to put a new updater.

### 5. Consider: sentinel mode for the DEBUG build

`ChangeEchoSuppressor.BeginSuppress` could, in `#if DEBUG`, also record the call site (`[CallerMemberName]`). If the handler never consumes the token within one dispatcher tick, log a warning — "suspicious: `UpdateX` suppressed a change event that never fired." That would surface the opposite failure mode (over-suppression from a write that doesn't actually trigger an event) early rather than as a mysterious lost-user-input bug.

## Secondary findings from the investigation

Two unrelated issues surfaced in the trace logs and deserve tracking:

- **Stacked `OnPointerPressed` / `OnTapped` handlers on DataGrid rows.** A single click on a row fired the enqueued handler 4–10 times (once per previous render). The handler is idempotent so it's not visibly broken today, but it's wasted work and masks real ordering issues. Investigate how `.OnPointerPressed` binds on re-render — it should replace the handler, not accumulate.
- **`ObservableListDataSource.OnItemPropertyChanged` triggers `DataChanged` on every INPC event.** Combined with the echo bug this was cascading: one real edit fired 5+ `DataChanged` events, each causing a full grid re-render. With echo-suppression in place the cascade quiets down, but coalescing INPC bursts (e.g. within a single dispatcher tick) is still worth doing.

Both are separate from this fix; filing as follow-up work.
