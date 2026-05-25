# `BeginSuppress` Audit — Phase 0 §14 Deliverable 1

Drives the spec 047 §8 / §8.1 decision (can the change-echo suppressor be
eliminated entirely?) and the controlled/uncontrolled/initial classification
in §6.1. The raw data is in [`begin-suppress-audit.csv`](begin-suppress-audit.csv).

## Scope

Every call to `ChangeEchoSuppressor.BeginSuppress` reachable from production
Reactor.dll code, excluding the definition and doc-comment references inside
`ChangeEchoSuppressor.cs` itself. Total: **24 call sites** across
`Reconciler.Mount.cs` (3) and `Reconciler.Update.cs` (21).

## Tally by category

| Category | Count | Affected controls |
|---|---:|---|
| `eliminable-tight-diff` | 14 | TextBox ×3, PasswordBox, ToggleSwitch ×2, ToggleSplitButton, CheckBox, RadioButton, CalendarDatePicker, DatePicker, TimePicker, NumberBox-immediate-sync |
| `coercion` | 4 | NumberBox (Min, Max), Slider (Min, Max) |
| `float-precision` | 4 | NumberBox.Value, Slider.Value, RatingControl.Value, NumberBox-immediate-sync (also tight-diff) |
| `items-coercion` | 2 | CalendarView.SelectedDates (Add, Remove) |
| `user-state-races-render` | 1 | ColorPicker.Color |
| `defensive-redundant` | 1 | AutoSuggestBox.Text (added category — see below) |
| `focus-prop` | 0 | — |
| `reference-equality` | 0 | — |
| `animation-tick` | 0 | — |

Total: 24 (the immediate-sync NumberBox site at `Mount.cs:670` is double-counted
once as `eliminable-tight-diff` and once as `float-precision`; the table counts
it under `float-precision` since the precision concern is load-bearing).

### Schema extension: `defensive-redundant`

The original schema didn't anticipate a site whose own code comment declares it
unnecessary. `AutoSuggestBox.Text` (`Reconciler.Update.cs:951`) is documented
as "suppress anyway for consistency" — the underlying control already filters
`TextChanged` to `UserInput` only, so the programmatic `Text=` write cannot
echo to the user handler. Recommend deleting this site as part of any §8.x
follow-up rather than carrying it through a redesign.

## What this tells §8 / §8.1

The dominant category by a wide margin is **`eliminable-tight-diff` (14/24)**.
These sites are all simple programmatic writes already gated by
`if (control.X != element.X)`. The post-write state guarantees
`control.X == element.X`, so a handler-side check —
`lastFired != GetElementTag(control).X` — would suffice. Per §8 this means
**~58% of suppression sites can be eliminated by moving the de-duplication
into the per-control event handler**, without any new mostRecentEventCount
plumbing.

The remaining 10 sites fall into three groups, each requiring a different
treatment:

1. **`coercion` (4) — `float-precision` (4):** the change-event fires with a
   value the engine did not directly write. A naïve handler-side
   `lastFired != tag.X` would either miss the echo (coerced value happens to
   match) or fail the float comparison spuriously. These sites need either:
   - a per-control tolerance-aware comparison stored alongside the handler
     (matches today's `AreNumberBoxValuesEquivalent` discipline), or
   - the §8 escape hatch where the engine records "I wrote X expecting Y;
     suppress one echo for Y±tolerance."
   The numbers are small enough (8 sites, 4 controls) that descriptor-level
   tolerance metadata is plausible.

2. **`items-coercion` (2):** `CalendarView.SelectedDates` is mutated as a diff.
   The clean fix is "batch then assign" or "compute desired set, suppress one
   token per applied mutation" (today's choice). Not generalizable to a
   descriptor field — keep as a per-control imperative shim.

3. **`user-state-races-render` (1) — ColorPicker only:** the spec's §8.1 case.
   `ColorChanged` echoes through a re-rendered control whose tag has already
   moved to the next element. This is the only site that *requires* §8.1's
   `mostRecentEventCount` round-trip or its equivalent. One site is small
   enough that it can be addressed without protocol-level changes — e.g., the
   imperative shim can capture the expected color into the handler closure
   and reject mismatches.

### Implication for §8

- The §8 "eliminate `BeginSuppress` entirely" direction is **viable for the
  14 tight-diff sites with no spec changes**.
- The 8 coercion/float-precision sites are tractable with per-control
  tolerance metadata; not all-or-nothing.
- The 1 ColorPicker site is the only one that demands the heavier §8.1
  machinery. Building §8.1 just for one site is over-engineered; an imperative
  fix specific to ColorPicker (per-handler `expectedColor` plus tolerance) is
  likely the right shape.

## What this tells §6.1 (controlled / uncontrolled / initial classification)

Every site that suppresses is a value-bearing DP where Reactor exposes an
`OnXChanged` callback. The audit confirms the §6.1 split:
- **Controlled** props are exactly the ones in this audit (writes that need
  echo protection because they may otherwise re-trigger user callbacks).
- **Uncontrolled** props are written without `BeginSuppress` (e.g.,
  `Header`, `PlaceholderText`, `OnContent`/`OffContent`, `IsThreeState`,
  `SnapsTo`) — they are write-only from the engine's perspective and have no
  echo path back to user code.

No call site in the audit fits the `focus-prop`, `reference-equality`, or
`animation-tick` categories. Spec §6.1 can drop those rows or fold them into
"reserved for future controls."

## Open follow-ups carried to Phase 1

- Delete `Reconciler.Update.cs:951` (the `defensive-redundant`
  `AutoSuggestBox.Text` suppress) as a standalone correctness-no-op.
- Decide whether `coercion` sites move to descriptor-level metadata
  (`{ property: Value, coercedBy: [Minimum, Maximum] }`) or stay as imperative
  per-control logic. This is §13 Q3 territory.
- The single `user-state-races-render` ColorPicker site is the smallest
  evidence base in the codebase for §8.1. Consider whether the §8.1 design is
  load-bearing across the whole protocol or whether a one-control shim is the
  right scope. Captured in `decision-criteria.md` Q3.

## Cross-reference

Spec §8 should cite this audit. Suggested footnote at §8 first paragraph:

> See [`docs/specs/047/audits/begin-suppress-audit.csv`](audits/begin-suppress-audit.csv)
> for the per-call-site classification. 14 / 24 sites are
> `eliminable-tight-diff`, 8 / 24 fall into `coercion` + `float-precision`
> (tractable with per-control tolerance metadata), 1 / 24 (`ColorPicker.Color`)
> is the only site that requires §8.1's `mostRecentEventCount` round-trip,
> and 1 / 24 is documented as already redundant.
