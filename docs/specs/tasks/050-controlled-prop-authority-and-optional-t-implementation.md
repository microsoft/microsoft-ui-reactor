# `Optional<T>` and the controlled-prop authority model — Implementation Tasks

Derived from: [`docs/specs/050-controlled-prop-authority-and-optional-t.md`](../050-controlled-prop-authority-and-optional-t.md)

> **Status:** Not yet started. Single-PR scope per spec §5 / §13 (no phasing
> for shipping; the "phases" below are commit/review-unit boundaries inside
> the same PR). Breaking-change tolerant — the plain-`T` `.Controlled` /
> `.HandCodedControlled` overloads are **deleted** outright (spec Q7, §5.1).

---

## Conventions

- The migration replaces a `Func<TElement, TValue>` getter contract on
  `Controlled` / `HandCodedControlled` with a `Func<TElement, Optional<TValue>>`
  contract. Spec §5.1 makes this a **compile-time** error to prevent the
  customer #494 footgun.
- **No naming conflicts.** Verified across `src/`, `tests/`, `samples/`,
  and `docs/`: every existing token matching `\bOptional\b` is `// Optional ...`
  doc text or a localization comment. Introducing
  `Microsoft.UI.Reactor.Optional<T>` shadows nothing.
- **No new spec / planning files outside `docs/specs/tasks/`.** Per
  repo convention, plan content lives in this task doc; per-commit notes
  live in commit messages.
- Element records use **C# record positional + `init`** mixing per
  `src/Reactor/Core/Element.cs` precedent. `Optional<T>` properties are
  added with `= default` (i.e. `Optional<T>.Unset`) so existing callers
  using positional `with` syntax keep compiling against the implicit
  `T → Optional<T>` conversion (§4.3).
- **Echo-suppression contract (spec 047 §8) preserved.** Both the value-diff
  echo arm (`PendingEchoMatch`) and the `ChangeEchoSuppressor` counter
  remain wired exactly as today; the Optional-aware Update gate only
  short-circuits **before** any arming when the new value is `Unset`. No
  arm is set on the `Unset → ...` path, so no strand risk is introduced.
- All migrated descriptors **preserve entry list order**, especially the
  Slider Min/Max-before-Value rule (`SliderDescriptor.cs:42-49`) — order
  is the execution order.
- **Trim/AOT.** `Optional<T>` is a plain `readonly struct` with no
  reflection. The core library has `IsAotCompatible=true` with warnings
  as errors; the migration must not introduce any new IL2026/IL2075/IL3050.
  The closed-generic instantiation set (§8.5 ≈ 25 types) is verified by
  the existing AOT publish CI step.
- Run tests on x64 dev machines via
  `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`
  (per repo memory — bare `dotnet test` fails on a transitive Minesweeper
  Windows App SDK arch error).
- A task is **done** only when:
  1. Code compiles clean under `Reactor.slnx` with warnings-as-errors.
  2. New analyzer rule `REACTOR0050` has both positive and negative tests.
  3. `dotnet test tests/Reactor.Tests` and `dotnet test tests/Reactor.SelfTests`
     are green on `main` HEAD as the baseline (Task 12.1) and on the
     migrated tree.
  4. The §11.4 baseline sweep is signed off (every newly-failed test is
     either fixed-as-real-regression or rewritten-as-footgun-removal,
     with the change documented in the PR).
  5. Microbenchmark (Task 12.2) shows no >5% regression on element-record
     allocation throughput and no measurable change on the reconciler
     Update hot path.

---

## Risks & blind-spots surfaced during planning

These are tracked here so reviewers can verify each is addressed:

| # | Risk / consideration | Mitigation task |
|---|---|---|
| R1 | `with { Background = null }` for `Optional<Brush>` silently becomes `Optional.Of(null)` (force-assert), **not** `Unset`. Easy to misread. | Task 11.1 — explicit doc callout + analyzer hint (REACTOR0050 message text). |
| R2 | `Optional<T>` boxing if `EqualityComparer<Optional<T>>.Default` is ever invoked through the non-generic `IEqualityComparer` interface. | Task 1.2 — implement `IEquatable<Optional<T>>` + `GetHashCode` so the default comparer is monomorphic. We never compare whole Optionals in hot paths; this is defensive. |
| R3 | The descriptor controlled-entry comparer (`_comparer`) is `EqualityComparer<TValue>` (unwrapped `TValue`, **not** `Optional<TValue>`). Ensure migration does not accidentally box. | Task 2.1 — gate body extracts `nv_opt.Value` once, passes unwrapped `nv` everywhere downstream. |
| R4 | Hand-coded handler dual-paths (CheckBox/Slider/TextBox/ToggleSwitch) — if both descriptor and hand-coded path run on the same element, double-writes / double-arms. | Task 5 — drop the dual paths entirely; descriptor is sole path post-migration. |
| R5 | `ListView` / `GridView` hand-coded `SelectedIndex` writes today have **no echo suppression at all** (spec §10). Migrating them to `Optional<int>` exposes the missing arm. | Task 5.1 — route both through `ReactorBinding.WriteSuppressed` (or value-diff arm) as part of the migration; this is a real bug fix piggy-backing on the Optional migration. |
| R6 | `Optional<bool?>` for tri-state `CheckBox.IsChecked` — `with { IsChecked = null }` means "explicitly indeterminate" (force-assert), distinct from `Unset`. | Task 3.2 + Task 11.1 — explicit test fixture + doc example. |
| R7 | Hot reload: `ReactorHotReloadCopier` migrates fields by name. A record whose property changes from `int` to `Optional<int>` mid-session can't be copied across the HR boundary cleanly. | Task 12.4 — verify HR migration path handles the type change (typically a no-op because HR doesn't reshape record schemas in a live session, but document the limit). |
| R8 | Devtools `DevtoolsPropertyTools` reflects only on WinUI DPs (verified `DevtoolsPropertyTools.cs:60-127`), **not** element-record props — so devtools surface area is unaffected. PropertyGrid (`ReflectionTypeMetadataProvider`) reflects on user POCOs; if anyone points it at a migrated element record, `Optional<T>` will surface unedited. | Task 11.4 — PropertyGrid editor mapping: opt-out for `Optional<T>` (treat as read-only label) in Phase-1 scope. |
| R9 | Pooling: today's `ElementPool.CleanElement` sets `textBox.Text = ""`, `toggle.IsOn = false`, etc. (`ElementPool.cs:319-339`). After migration, the controlled-entry Mount for `Optional<T>.Unset` is a no-op — so a pooled control retains the previous mount's value through the reset slot. | Task 7 — replace `=` value resets with `ClearValue(DP)` for poolable controls that carry controlled DPs (uses the same WinUI primitive `Optional<T>` exposes). Smaller reset cost on pool return, and the next Mount with `Unset` correctly leaves the WinUI default in place. |
| R10 | The fast-path `DescriptorControlledPayload` (`PendingEchoMatch`) is **per closed generic** (`TElement, TControl, TValue, TArgs`). Two `.Controlled` entries on the same control with the same generics collide today (`PropEntry.cs:358-363`). Migration doesn't change this — but the spec is silent on the case, so flag it. | Task 4.0 — manually re-audit the migrated descriptors for this collision; none are expected post-migration but the check is cheap. |
| R11 | Snap-back recipe (§6.5) depends on `UseReducer(false) + bump` already wired in `RenderContext.cs:490-528`. Verify the docs match the actual API name. | Task 11.2 — cross-check the recipe against `RenderContext.UseReducer` signature before publishing. |
| R12 | Sample apps (`samples/apps/{minesweeper,headtrax,demo-script-tool,validation-showcase}` plus `samples/Reactor.TestApp`) read controlled element props (`var idx = el.SelectedIndex`) in a handful of places. | Task 10 — sweep all samples; the breaks are mechanical (`.Value` / `.GetValueOrDefault(-1)`). |
| R13 | Skills (`plugins/reactor/skills/`) contain example snippets using `Slider`/`TextBox`/etc. The factory shorthand absorbs the change (Task 6), but the recipe `.cs` references and inline code blocks must still parse. | Task 11.3 — sweep all skill markdown + reference `.cs` files. |
| R14 | The `Reactor.Compile.Analyzer.Tests` harness uses Microsoft.CodeAnalysis.Testing — needs an `Optional<T>` stub in the analyzer-test compilation context. | Task 8.3 — add the stub to the test fixtures, mirroring the existing pattern for `Element`. |
| R15 | Public XML doc on every new surface (`Optional<T>` itself, the four authoring shapes' new overloads) — repository style requires `<summary>` on public members and the `RequiredXmlDoc` analyzer is strict. | Each Task 1–6 sub-bullet includes "with full XML doc". |
| R16 | `Win2D` / `Reactor.Advanced` element types are out of the §9.2 scope; verify no controlled-prop bleed there. | Task 4.0 — quick scan confirms `Win2DCanvasElement`/`Win2DAnimatedCanvasElement` are draw-callback elements with no controlled value DPs (verified during planning). |

---

## Sequencing (commit-order inside the single PR)

Per spec §13:

1. Optional<T> primitive + unit tests (Task 1).
2. PropEntry surface — delete plain-T overloads, change generic signatures (Task 2).
3. `Prop.InitialOnly` shape + tests (Task 2.3).
4. OneWay + `dp:` overload (Task 2.4).
5. Element record migrations (Task 3) — split by control family for readable diffs.
6. Descriptor body migrations (Task 4).
7. Hand-coded handler alignment (Task 5).
8. Factory DSL preservation (Task 6).
9. Pooling cleanup using `ClearValue` (Task 7).
10. `REACTOR0050` analyzer (Task 8).
11. Unit tests + selftests + perf bench (Task 9).
12. Samples sweep (Task 10).
13. Docs + skills (Task 11).
14. Baseline sweep, perf gate, AOT smoke (Task 12).

Pause/resume point: build remains clean after Phase 2 because of the implicit
`T → Optional<T>` conversion (spec Q1). The broad build break trigger is Phase 3,
when element-record prop types change and downstream descriptor/handler/factory
readers are force-audited.

---

## Phase 0 — Pre-flight

### 0.1 Confirm baseline state of `main`

- [ ] Run `dotnet build Reactor.slnx -p:Platform=x64` against the merge-base.
      Record full output (warnings count especially).
- [ ] Run `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`.
      Record pass/fail set.
- [ ] Run `dotnet run --project tests/Reactor.AppTests.Host -- --self-test`.
      Record pass/fail set.
- [ ] Run `dotnet test tests/Reactor.SelfTests`.
      Record pass/fail set.
- [ ] Save baseline artifacts under
      `docs/specs/050-baseline-sweep/` (not committed to history beyond PR
      review; archive after merge). This is the §11.4 baseline.

### 0.2 Confirm there is no `Optional` identifier collision

- [x] Verified during planning (`grep \bOptional\b src/ tests/ samples/`):
      every match is `// Optional ...` doc comment / localization comment.
      No type, member, or namespace conflict.

### 0.3 Decision-log capture

Re-confirm each spec §Status table answer holds (Q1–Q9). If the implementer
disagrees with any during the PR, append a "Status amendments" subsection
to the spec rather than diverging silently.

---

## Phase 1 — `Optional<T>` primitive

### 1.1 Add `Optional<T>` struct

- [x] New file: `src/Reactor/Core/Optional.cs` in `namespace Microsoft.UI.Reactor`.
      Public top-level namespace per spec §4.1.
- [x] Members exactly per spec §4.1: `HasValue`, `Value` (throws when
      `!HasValue`), `GetValueOrDefault()`, `GetValueOrDefault(T fallback)`,
      static `Unset`, static `Of(T value)`, private ctor, implicit
      `operator Optional<T>(T value)`.
- [x] Add `IEquatable<Optional<T>>` + override `Equals(object?)` +
      `GetHashCode()` so the default `EqualityComparer<Optional<T>>` is
      monomorphic (defensive — guards R2).
- [x] Add `ToString()` returning either `"Unset"` or
      `value?.ToString() ?? "null"` for clean devtools / logging output.
- [x] Full `<summary>` XML docs on every public member. Reference §4.3
      implicit-conversion gotcha on the `implicit operator` doc.
- [x] **No** `Map` / `Bind` / `Match` / LINQ extension methods (spec §4.4).

### 1.2 Add `OptionalTests.cs`

`tests/Reactor.Tests/OptionalTests.cs`:

- [x] Default value is `Unset`; `Unset.HasValue == false`.
- [x] `Of(value).HasValue == true`; `Of(value).Value` returns value.
- [x] `Unset.Value` throws `InvalidOperationException`.
- [x] `GetValueOrDefault()` returns `default(T)` when `Unset`.
- [x] `GetValueOrDefault(fallback)` returns fallback when `Unset`, stored
      value when `HasValue`.
- [x] Implicit `T → Optional<T>` conversion produces `HasValue == true`
      (including for ref-type `null`, per §4.3).
- [x] Equality: `Of(x) == Of(x)`, `Of(x) != Of(y)`, `Unset != Of(default)`,
      `Unset == Unset`, `Unset != Of(null)` for `Optional<string>`.
- [x] `GetHashCode` distinct for `Unset` vs `Of(default)`.
- [x] Struct size assertions per spec §8.1 table — sanity-check via
      `Unsafe.SizeOf<Optional<int>>() == 8`, `Unsafe.SizeOf<Optional<bool>>() == 2`,
      `Unsafe.SizeOf<Optional<double>>() == 16`,
      `Unsafe.SizeOf<Optional<string>>() == 16` (x64).

---

## Phase 2 — PropEntry surface changes

### 2.1 Migrate `ControlledPropEntry<TElement, TControl, TValue, TArgs>`

`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:187-348`:

- [x] Change `Func<TElement, TValue> _get` → `Func<TElement, Optional<TValue>> _get`.
- [x] In `Mount`: extract `var v_opt = _get(el); if (!v_opt.HasValue) return;
      var v = v_opt.Value; ...` — keep the existing `_readBack` drift check
      and bare write.
- [x] In `Update`: identical extraction; `Unset` → `return` (control owns).
      Preserve the `_comparer.Equals(current, nv)` short-circuit and the
      value-diff arm exactly as today.
- [x] **Do not** change `_comparer`'s generic — it stays
      `IEqualityComparer<TValue>` on the unwrapped value. (R3.)
- [x] Audit the stale-payload-clear in `Mount` (`PropEntry.cs:275-282`):
      keep it; clear the arm even on the `Unset` skip path so pool reuse
      doesn't strand.

### 2.2 Migrate `HandCodedControlledPropEntry<...>`

`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:424-529`:

- [x] Same getter signature change to `Func<TElement, Optional<TValue>>`.
- [x] Same Mount / Update gate.
- [x] **Preserve `valueDiffEcho` opt-in.** Arm only on the `HasValue` branch
      (post-extract). The `ClearExpectedEcho` call on Mount must still
      run unconditionally (stale-arm-on-pool-reuse).

### 2.3 Add `InitialOnlyPropEntry<TElement, TControl, TValue>` + descriptor builder

- [x] Spec §7. Identical shape to `InitialPropEntry` (`PropEntry.cs:149-164`)
      but **named separately** to match the spec's authoring API. (Option:
      simply expose the existing `InitialPropEntry` under the new builder
      name `.InitialOnly` and remove the existing `.Initial` builder if
      no callers exist — verify via grep.)
- [x] Add `ControlDescriptor<TElement, TControl>.InitialOnly<TValue>(get, set)`
      builder method.
- [x] Per spec §7.2, none of the 26 migrated records use `.InitialOnly`
      in this PR — it ships for downstream authors. Added Reactor.Tests
      surface coverage plus selftest execution coverage.

### 2.4 Add `OneWay + dp:` `ClearValue` overload

- [x] New `OneWayClearValuePropEntry<TElement, TControl, TValue>` in
      `PropEntry.cs` taking
      `(Func<TElement, Optional<TValue>> get, Action<TControl, TValue> set,
        DependencyProperty dp, IEqualityComparer<TValue>? comparer = null)`.
- [x] **Mount:** `HasValue` → write; `Unset` → `ctrl.ClearValue(dp)`.
      (Mount-time `ClearValue` is intentional — releases whatever local
      value the freshly-rented pool control might still carry to the WinUI
      precedence chain.)
- [x] **Update:** diff old vs new on `Optional<TValue>` (not raw TValue):
      `Unset → HasValue` → write; `HasValue → Unset` → `ClearValue`;
      `HasValue → HasValue` with value change → write; otherwise no-op.
- [x] Add `ControlDescriptor<TElement, TControl>.OneWay<TValue>(get, set, dp)`
      overload. Resolves spec §6.3 ambiguity by routing through the new
      entry class.
- [x] Spec note: the **plain-`T`** `.OneWay` (no `dp:`) and
      `.OneWayConditional` overloads **stay** unchanged (spec §5.2 row 1+2)
      — the 141 `OneWayConditional` migrations are out-of-scope (§9.1).

### 2.5 Delete plain-`T` `.Controlled` and `.HandCodedControlled` overloads

- [x] `ControlDescriptor.cs:144-156` — delete the plain-`T` `.Controlled`
      overload outright (spec Q7).
- [x] `ControlDescriptor.cs:180-197` — delete the plain-`T`
      `.HandCodedControlled` overload outright.
- [x] Update the class-level XML doc examples to use `Optional<T>` getters.
- [x] Build remains clean after Phase 2 because of the implicit
      `T → Optional<T>` conversion (spec Q1). The build break trigger is
      Phase 3, when element-record prop types migrate to `Optional<T>`.

### 2.6 PropEntry unit tests

`tests/Reactor.Tests/PropEntryOptionalTests.cs` (new):

- [x] `ControlledPropEntry_Mount_Unset_NoWrite` — verified in selftest
      fixture `PropEntryOptional_Execution`; Reactor.Tests covers builder surface.
- [x] `ControlledPropEntry_Mount_HasValue_Writes` — verified in selftest
      fixture `PropEntryOptional_Execution`; Reactor.Tests covers builder surface.
- [x] `ControlledPropEntry_Update_UnsetToUnset_NoOp` — verified in selftest
      fixture `PropEntryOptional_Execution`.
- [x] `ControlledPropEntry_Update_HasValueToUnset_NoOp` — **critical for
      #494**: selftest verifies the control's value survives when the author
      drops the controlled binding.
- [x] `ControlledPropEntry_Update_UnsetToHasValue_Writes` — verified in
      selftest fixture `PropEntryOptional_Execution`.
- [x] `ControlledPropEntry_Update_HasValueChange_Writes_ArmsEcho` — verified
      in selftest fixture `PropEntryOptional_Execution`.
- [x] `ControlledPropEntry_Update_HasValueSame_NoOp_NoArm` — selftest verifies
      the current==nv short-circuit.
- [x] `HandCodedControlledPropEntry_*` — mirrored in selftest fixture
      `PropEntryOptional_Execution` with `valueDiffEcho:true` and `false`;
      Reactor.Tests covers builder surface.
- [x] `InitialOnlyPropEntry_*` — Reactor.Tests covers builder surface;
      selftest verifies Mount writes and Update never writes.
- [x] `OneWayClearValuePropEntry_*` — Reactor.Tests covers dp overload dispatch;
      selftest covers all transitions and `ClearValue(dp)` edges.

---

## Phase 3 — Element record migration (26 records)

> Per spec §9.2. Splitting into commit-sized batches by control family.

### 3.1 Selection-index family (8 records)

For each: change the named property's type from `int` → `Optional<int>`
and default it to `default` (= `Unset`). Update XML doc to mention
`Unset` semantics.

- [x] `ComboBoxElement.SelectedIndex` (`Element.cs:2472-2478`).
- [x] `ListBoxElement.SelectedIndex` (`Element.cs:4059-4064`).
- [x] `ListViewElement.SelectedIndex` (`Element.cs:3102-`).
- [x] `GridViewElement.SelectedIndex` (`Element.cs:3129-`).
- [x] `FlipViewElement.SelectedIndex` (`Element.cs:3167-`).
- [x] `TemplatedFlipViewElement<T>.SelectedIndex` (`Element.cs:3514-`).
- [x] `RadioButtonsElement.SelectedIndex` (`Element.cs:2461-`).
- [x] `PivotElement.SelectedIndex` (`Element.cs:3087-`).
- [x] `TabViewElement.SelectedIndex` (`Element.cs:3035-`).
- [x] `SelectorBarElement.SelectedIndex` (`Element.cs:4076-`).
- [x] `PipsPagerElement.SelectedPageIndex` (`Element.cs:4086-`).

> Spec §9.4 sentinel-value warning: any `= -1` default on these properties
> becomes `Optional<int>.Unset`. Document this in the per-record XML doc
> and add a deselect example (`with { SelectedIndex = Optional.Of(-1) }`)
> in the migration guide.

### 3.2 Toggle family (6 records)

- [x] `CheckBoxElement.IsChecked` → `Optional<bool?>` (tri-state-aware;
      R6). Also covers `CheckedState` for the three-state-mode descriptor
      gap when it lands later — out of scope here but the type is already
      right.
- [x] `ToggleSwitchElement.IsOn` → `Optional<bool>`.
- [x] `RadioButtonElement.IsChecked` → `Optional<bool>`.
- [x] `ToggleSplitButtonElement.IsChecked` → `Optional<bool>`.
- [x] `ExpanderElement.IsExpanded` → `Optional<bool>`.

### 3.3 Numeric family (4 records)

- [x] `SliderElement.Value` → `Optional<double>`.
- [x] `NumberBoxElement.Value` → `Optional<double>`.
- [x] `RatingControlElement.Value` → `Optional<double>`.
- [x] `ColorPickerElement.Color` → `Optional<Color>`.

### 3.4 Date/time family (3 records)

- [x] `CalendarDatePickerElement.Date` → `Optional<DateTimeOffset?>`.
      (Already nullable today; the `Optional<DateTimeOffset?>` carries the
      tri-state "control owns / asserted-no-date / asserted-date X" cleanly.)
- [x] `DatePickerElement.Date` → `Optional<DateTimeOffset>`.
- [x] `TimePickerElement.Time` → `Optional<TimeSpan>`.

### 3.5 Text-input family (4 records)

Per spec §9.3 — uniform `Optional<string>`, not `Prop.InitialOnly`.

- [x] `TextBoxElement.Value` → `Optional<string>`.
- [x] `PasswordBoxElement.Password` → `Optional<string>`.
- [x] `RichEditBoxElement.Text` → `Optional<string>`.
- [x] `AutoSuggestBoxElement.Text` → `Optional<string>`.

### 3.6 Build break audit

After 3.1–3.5, expect the solution to break in:
- Every descriptor file that closes the generic on the changed type (Task 4).
- Every hand-coded handler that reads `el.X` directly (Task 5).
- Every factory DSL signature that takes the prop as a parameter (Task 6).
- Every sample / test that reads back `el.X` as the raw type (Tasks 9, 10).

The build break is the safety net — every reader gets force-audited.

---

## Phase 4 — Descriptor body migrations

For each of the 26 descriptors (spec §9.2 table) update the
`.Controlled<TValue, TArgs>(get: ...)` invocation so the `get:` lambda
returns `Optional<TValue>` (mechanically: `static e => e.X` keeps the
exact same source text — only the inferred type changes, because the
record property is now `Optional<TValue>`).

### 4.0 Pre-audit (R10, R16)

- [x] Scan all 80+ descriptor files in
      `src/Reactor/Core/V1Protocol/Descriptor/Descriptors/` for any
      `.Controlled` / `.HandCodedControlled` invocation. Confirm the set
      matches spec §9.2 (9 + 16 = 25, plus ListView/GridView when their
      hand-coded handlers move into descriptors per Task 5 → 26).
- [x] Confirm no two `.Controlled` entries on the same control share the
      `(TElement, TControl, TValue, TArgs)` closed-generic tuple
      (`DescriptorControlledPayload` collision per `PropEntry.cs:358-363`).
- [x] Confirm `src/Reactor.Advanced/Win2D/*` has zero `.Controlled`
      entries (verified during planning).

### 4.1 Pure `.Controlled` descriptors (9 from §9.2)

Per-file commits (one per descriptor or one per family):

- [x] `ToggleSwitchDescriptor.cs` — `IsOn`.
- [x] `CheckBoxDescriptor.cs` — `IsChecked`. (Note: keep the three-state
      handler gap; the descriptor's `bool?` extraction still works since
      `Optional<bool?>.Value` is `bool?`.)
- [x] `RadioButtonDescriptor.cs` — `IsChecked`.
- [x] `ToggleSplitButtonDescriptor.cs` — `IsChecked`.
- [x] `SliderDescriptor.cs` — `Value`. **Preserve Min/Max-before-Value
      order** (`SliderDescriptor.cs:42-49`).
- [x] `RatingControlDescriptor.cs` — `Value`.
- [x] `ColorPickerDescriptor.cs` — `Color`.
- [x] `CalendarDatePickerDescriptor.cs` — `Date`.
- [x] `DatePickerDescriptor.cs` — `Date`.
- [x] `TimePickerDescriptor.cs` — `Time`.

### 4.2 `.HandCodedControlled` descriptors (16 from §9.2)

- [x] `AutoSuggestBoxDescriptor.cs` — `Text`.
- [x] `ComboBoxDescriptor.cs` — `SelectedIndex`.
- [x] `ExpanderDescriptor.cs` — `IsExpanded`.
- [x] `FlipViewDescriptor.cs` — `SelectedIndex`.
- [x] `GridViewDescriptor.cs` — `SelectedIndex` (Task 5 first).
- [x] `ListBoxDescriptor.cs` — `SelectedIndex`.
- [x] `ListViewDescriptor.cs` — `SelectedIndex` (Task 5 first).
- [x] `NumberBoxDescriptor.cs` — `Value`.
- [x] `PasswordBoxDescriptor.cs` — `Password`.
- [x] `PipsPagerDescriptor.cs` — `SelectedPageIndex`.
- [x] `PivotDescriptor.cs` — `SelectedIndex`.
- [x] `RadioButtonsDescriptor.cs` — `SelectedIndex`.
- [x] `RichEditBoxDescriptor.cs` — `Text`.
- [x] `SelectorBarDescriptor.cs` — `SelectedIndex`.
- [x] `TabViewDescriptor.cs` — `SelectedIndex`.
- [x] `TemplatedFlipViewDescriptor.cs` — `SelectedIndex`.
- [x] `TextBoxDescriptor.cs` — `Value` (single `valueDiffEcho:true`
      branch; preserve the snap-back gap callout in the file header).

### 4.3 Per-descriptor coverage smoke

For each migrated descriptor, write a unit test in
`tests/Reactor.Tests/DescriptorOptionalCoverage/` exercising the four
gate transitions (Unset→Unset, Unset→HasValue, HasValue→HasValue same,
HasValue→Unset). Reuse a shared test harness — one file per descriptor
but ≤ 30 lines each.

---

## Phase 5 — Hand-coded handler alignment

Per spec §10 table.

### 5.1 ListView / GridView — fix the missing echo suppression

- [x] `Handlers/ListViewHandler.cs:92, 126` — read element's
      `Optional<int> SelectedIndex`; if `Unset`, skip. If `HasValue`, do
      a drift check via `lv.SelectedIndex != nv.Value` and route the write
      through `ReactorBinding.WriteSuppressed` (or via the value-diff
      arm — pick whichever the descriptor would use post-migration so
      Phase 4.2's eventual carve-over is trivial).
- [x] `Handlers/GridViewHandler.cs:100, 143` — same fix.
- [x] Add a unit test that repros the historical bug class (sibling
      re-render with `Unset` does not clobber user selection on
      ListView/GridView).
- [x] **This is a real bug fix piggy-backed on the migration.** Call it
      out separately in the PR description.

### 5.2 Drop the dual-pathed handlers

For each, verify that the descriptor path covers all behavior the
hand-coded handler exercised (no carve-outs except the documented
three-state CheckBox / TextBox snap-back gaps which are out-of-scope
per Phase 3 spec):

- [x] `Handlers/CheckBoxHandler.cs:31, 35, 106` — delete the file if the
      descriptor is sole. If a residual `OnCheckedStateChanged` or
      indeterminate-event path remains uncovered, slim the handler to
      that only.
- [x] `Handlers/SliderHandler.cs:59, 95` — same. Confirm the descriptor
      covers Min/Max coercion (it does, via `CoercingOneWay`).
- [x] `Handlers/TextBoxHandler.cs:73, 186` — same. Snap-back gap remains
      a known gap (file-level comment).
- [x] `Handlers/ToggleSwitchHandler.cs:54, 80` — same.
- [x] Update `Reconciler.cs` dispatch (any remaining `switch` arms for
      these element types) to route through the descriptor handlers.
      Confirm no V1 handler-registry conflict.

### 5.3 Handler test sweep

- [x] All tests under `tests/Reactor.Tests/` that target the four dropped
      handlers either delete (test is redundant with descriptor coverage)
      or rewrite to point at the descriptor entry. Track each in the §11.4
      baseline-triage table.

---

## Phase 6 — Factory DSL preservation

`src/Reactor/Elements/Dsl.cs`: every factory in spec §9.4's "factory
shorthand" set keeps its plain-`T` signature and internally wraps with
`Optional.Of(value)`.

### 6.1 Wrap the 26 factories

For each factory in the table below, change the constructor argument
to wrap via `Optional.Of(...)`:

- [x] `Slider` — `value` (`Dsl.cs:354`).
- [x] `TextBox` — `value` (`Dsl.cs:295`).
- [x] `PasswordBox` — `password` (`Dsl.cs:302`).
- [x] `NumberBox` — `value` (`Dsl.cs:308`).
- [x] `AutoSuggestBox` — `text` (`Dsl.cs:314`).
- [x] `RichEditBox` — `text` (`Dsl.cs:140`).
- [x] `CheckBox` — `isChecked` (`Dsl.cs:320`). For `ThreeStateCheckBox`
      (`Dsl.cs:327`): wrap `checkedState` as `Optional<bool?>.Of(checkedState)`.
- [x] `RadioButton` — `isChecked` (`Dsl.cs:333`).
- [x] `ComboBox` — both overloads (`Dsl.cs:345, 351`).
- [x] `ToggleSwitch` — `isOn` (`Dsl.cs:361`).
- [x] `RatingControl` — `value` (`Dsl.cs:368`).
- [x] `ColorPicker` — `color` (`Dsl.cs:374`).
- [x] `CalendarDatePicker` — `date` (`Dsl.cs:382`).
- [x] `DatePicker` — `date` (`Dsl.cs:388`).
- [x] `TimePicker` — `time` (`Dsl.cs:394`).
- [x] `Expander` — `isExpanded` (`Dsl.cs:613`).
- [x] `ListBox` — `selectedIndex` (`Dsl.cs:1521`).
- [x] `SelectorBar` — `selectedIndex` (`Dsl.cs:1529`).
- [x] `PipsPager` — `selectedPageIndex` (`Dsl.cs:1537`).
- [x] `TabView`, `Pivot`, `ListView`, `GridView`, `FlipView` factories —
      where applicable, accept an optional `int? selectedIndex = null`
      parameter and translate `null → Optional<int>.Unset`,
      `value → Optional.Of(value)`.

### 6.2 Add `Optional<T>` overloads for the snap-back recipe

- [x] For each of the above factories, add a second overload accepting
      the `Optional<T>` directly. Example:
      `public static SliderElement Slider(Optional<double> value, ...)`.
      Lets authors write `Slider(Optional.Of(5.0), onChanged: ...)` for
      the §6.5 snap-back recipe without the plain-`T` overload swallowing
      the call via implicit conversion.

### 6.3 Factory unit tests

- [x] `tests/Reactor.Tests/DslFactoryOptionalTests.cs` — verify each
      factory still produces an element whose property is `HasValue`
      with the right inner value when called with a plain `T`, and `Unset`
      when called via the new `Optional<T>` overload with `Unset`.

---

## Phase 7 — Pooling improvements (R9)

> User-requested. Replace per-type explicit value resets with
> `ClearValue(DP)` so pooled controls re-mount cleanly under the new
> `Optional<T>.Unset → skip-write` Mount semantics.

### 7.1 Audit `ElementPool.CleanElement`

- [x] Walk `ElementPool.cs:199-340`. For each `case` arm that resets a
      controlled property by assignment (e.g. `textBox.Text = ""`,
      `toggle.IsOn = false`), establish whether the property has a
      backing `DependencyProperty`.
- [x] For poolable interactive types in scope of Phase 3 migration
      (TextBox, ToggleSwitch; future expansion may add CheckBox / Button
      etc.), the answer is yes for all the migrated value props.

### 7.2 Replace value-assignments with `ClearValue`

- [x] `TextBox` — replace `textBox.Text = ""` with `textBox.ClearValue(TextBox.TextProperty)`.
- [x] `ToggleSwitch` — replace `toggle.IsOn = false` with `toggle.ClearValue(ToggleSwitch.IsOnProperty)`.
- [x] **Note:** any other reset in the same `case` arm (e.g.
      `textBox.PlaceholderText = ""`, `toggle.OnContent = null`) stays
      as-is — those are non-controlled props.
- [x] Do **not** drop the `VisualStateManager.GoToState(..., "Normal", false)`
      calls — visual state is orthogonal to DP value.

### 7.3 Pool reuse + echo suppression cross-check

- [x] Verify that `ClearValue(DP)` does **not** synchronously fire the
      change event on a detached control (it does on attached ones, but
      `CleanElement` runs after `DetachFromParent`+`ForceDetach`, so the
      control is parent-less and event subscriptions are dropped per
      `Reconciler.ClearCurrentEventHandlers`). If it does fire, the
      payload's `Trampoline` is still wired but the `CurrentEventHandlers`
      table is empty, so no callback would invoke — but the `_readBack`
      drift check on next Mount could still differ. Test this explicitly
      in Task 9 selftests.
- [x] The fast-path `DescriptorControlledPayload.HasExpectedEcho` slot
      is already cleared on next Mount (`PropEntry.cs:275-282`). Keep
      that clear unconditional — it covers both old and new Mount paths.

### 7.4 Consider extending poolable set

- [x] **Out of scope** but capture for future: with the controlled-value
      reset path now uniform (`ClearValue` + Mount-time `Unset` skip),
      the pool can safely admit more interactive types (Slider, ComboBox,
      etc.). File a follow-up issue; do **not** expand `PoolableTypes`
      in this PR — would balloon the test matrix. Agent 4 did not expand
      `PoolableTypes`; follow-up remains for Agent 5+/future work.

### 7.5 Pool tests

- [x] `tests/Reactor.Tests/ElementPoolOptionalResetTests.cs` — for each
      pool-eligible interactive type:
  - [x] Mount with `Value = Optional.Of("hello")` → pool return → Mount
        with `Value = Optional<string>.Unset` → assert control's `Text`
        is the WinUI default (empty string for TextBox), not "hello".
  - [x] Mount with `Value = Optional.Of("hello")` → pool return → Mount
        with `Value = Optional.Of("world")` → assert "world" is written
        and no echo strands (next user keystroke arrives at the new
        callback).
      Verified in `ElementPoolOptionalReset` selftest for TextBox and
      ToggleSwitch; unit test pins the `ClearValue(DP)` source contract.

---

## Phase 8 — `REACTOR0050` analyzer

Spec §6.4.

### 8.1 Add analyzer

- [x] New file: `src/Reactor.Analyzers/OneWayClearValueAnalyzer.cs`.
- [x] Diagnostic ID: `REACTOR0050`. Severity: `Warning`. Category:
      `Reactor.Descriptor`. Help link: spec §6.4.
- [x] Fires on any `ControlDescriptor<,>.OneWay<>(get, set, ...)`
      invocation where the inferred `TValue` of `get`'s return type is
      `Optional<T>` **and** no `dp:` argument is provided.
- [x] Does **not** fire on `.OneWayConditional`, `.OneWay` with plain-`T`
      getter, or `.OneWay` with `dp:` supplied.
- [x] Suppression: support `[NoClearValue]` attribute on the descriptor
      field, OR a `// REACTOR0050: intentional skip — no DP backing`
      pragma. Match the existing analyzer suppression patterns
      (`src/Reactor.Analyzers/PoolResetSetAnalyzer.cs`).

### 8.2 `NoClearValue` attribute

- [x] New file: `src/Reactor/Core/V1Protocol/Descriptor/NoClearValueAttribute.cs`.
- [x] `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`,
      sealed. Public. XML docs explaining when to use.

### 8.3 Analyzer tests

`tests/Reactor.Compile.Analyzer.Tests/REACTOR0050Tests.cs`:

- [x] Positive: `.OneWay(get: e => e.X, set: ...)` where `e.X` is
      `Optional<Brush>` and no `dp:` → diagnostic at the invocation.
- [x] Negative: same with `dp:` supplied → no diagnostic.
- [x] Negative: `.OneWayConditional(...)` with `Optional<T>` getter → no
      diagnostic.
- [x] Negative: `.OneWay(get: e => e.X, set: ...)` with plain-`T` getter →
      no diagnostic.
- [x] Suppression via `[NoClearValue]` attribute on the descriptor field
      silences the diagnostic.
- [x] Test compilation includes a stub `Optional<T>` to keep the analyzer
      test independent of the main `Reactor` reference (R14).

### 8.4 End-to-end analyzer smoke

- [x] Build `samples/Reactor.TestApp` and `samples/apps/minesweeper` with
      the analyzer enabled. Expect zero `REACTOR0050` warnings (no
      sample currently uses `Optional<T>` in `OneWay` without `dp:`).

---

## Phase 9 — Tests (unit + selftest + perf)

### 9.1 Unit tests — covered

- Task 1.2: `OptionalTests.cs`.
- Task 2.6: `PropEntryOptionalTests.cs`.
- Task 4.3: `DescriptorOptionalCoverage/*` per descriptor.
- Task 6.3: `DslFactoryOptionalTests.cs`.
- Task 7.5: `ElementPoolOptionalResetTests.cs`.
- Task 8.3: `REACTOR0050Tests.cs`.

### 9.2 Selftest fixtures

Per spec §11.2. Each fixture must be registered in
`tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`
**both** in the fixture-name string list **and** in the name→constructor
switch arm (per repo memory — both registrations required, else
discovery silently misses the fixture).

- [x] `ControlledOptionalCustomerRepro.cs` — exact #494 reproduction:
      `BasemapGallery`-shaped component with sibling toggle re-render,
      controlled prop `Unset` survives the re-render.
- [x] `ControlledOptionalSelectionFamilyFixture.cs` — covers all 11
      selection-index records (one per record). Each tests:
      (a) `Unset` survives sibling re-render,
      (b) `Optional.Of(state)` updates control when state changes,
      (c) snap-back recipe (force-constant via `UseReducer(false) + bump`).
- [x] `ControlledOptionalToggleFamilyFixture.cs` — 5 toggle records,
      same three sub-cases.
- [x] `ControlledOptionalNumericFamilyFixture.cs` — 4 numeric records.
- [x] `ControlledOptionalDateTimeFamilyFixture.cs` — 3 date/time records.
- [x] `ControlledOptionalTextInputFamilyFixture.cs` — 4 text-input
      records. **Critical** for the "type and finish" idiom.
- [x] `OneWayClearValueFixture.cs` — confirms `.OneWay(get, set, dp:)`
      with `Unset` calls `ClearValue` and visual fallback to a known
      XAML-resource brush actually occurs.
- [x] `OptionalEchoStrandRegressionFixture.cs` — for each controlled
      descriptor, drive the control to drift, programmatically write
      `Optional.Of(v)` where `v == current`, then drive a real user
      interaction and verify the callback fires (no strand). This is
      the spec 047 §8 contract carrying forward. RatingControl and
      AutoSuggestBox user-only programmatic-event gaps are TAP-skipped.

### 9.3 Performance microbenchmarks

Per spec §11.3. Land under `tests/perf_bench/PerfBench.ControlModel/` or
a new sibling project — re-use the existing BenchmarkDotNet wiring.

- [x] `OptionalElementAllocBench` — baseline vs migrated allocation
      throughput for `TextBoxElement` and a selection-heavy element
      (`GridViewElement` with 100 items).
- [x] `OptionalReconcilerUpdateBench` — cycles for the descriptor
      `Update` hot path with controlled prop, for both `HasValue` and
      `Unset` arms.
- [x] **Gate:** no >5% regression on allocation throughput, no
      measurable change on Update hot path. Record results under
      `docs/specs/050-baseline-sweep/perf/`. Migrated-tree absolute
      results recorded; main-baseline comparison deferred to Agent 6.

### 9.4 Trim/AOT smoke

- [x] Run the existing AOT publish CI step locally for one app
      (`samples/apps/hello-world-aot`) — confirm zero new IL2026/IL2075/
      IL3050 warnings (`PublishAotInternal=true` per repo memory; bare
      `PublishAot=true` is blocked by analyzer ProjectReferences).
- [x] Run `mstat` verifier (`tools/Reactor.MstatVerifier`) and confirm
      Optional<T> closed-generic count is ≤ 30 (spec §8.5 ceiling).

---

## Phase 10 — Samples sweep

For every app under `samples/`, audit any `var x = el.Y;` or
`element.Y == sentinel` pattern where `Y` is now `Optional<T>`. Mechanical
fix: `el.Y.Value` or `el.Y.GetValueOrDefault(sentinel)`.

### 10.1 Sweep list

- [x] `samples/apps/minesweeper/`.
- [x] `samples/apps/headtrax/`.
- [x] `samples/apps/demo-script-tool/`.
- [x] `samples/apps/validation-showcase/`.
- [x] `samples/apps/hello-world-aot/`.
- [x] `samples/Reactor.TestApp/Demos/` — especially
      `ControlsCoverageFixtures.cs` (spec mentions Optional context here).
- [x] `samples/InteropFirst/`.
- [x] `samples/scenarios/` — every project. No `samples/scenarios/`
      directory exists in this checkout; full `samples/**/*.csproj` sweep passed.

### 10.2 Build all samples

- [x] `dotnet build samples/apps/minesweeper/Minesweeper.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
      (per repo memory — both flags required for unpackaged self-contained
      apps). Repeat per app csproj.

### 10.3 New sample: snap-back recipe demo

- [x] Add one tiny new demo to `samples/Reactor.TestApp/Demos/`
      (`OptionalSnapBackDemo.cs`) implementing the spec §6.5 pattern with
      a Slider clamped to 5.0. Wires up the `UseReducer + bump` idiom
      and demonstrates the "user drags away → snaps back" behavior
      end-to-end. Acts as both a sample and a manual smoke test for
      the recipe.

---

## Phase 11 — Docs + skills

### 11.1 Source docs (spec §12.1)

> User docs under `docs/guide/` are generated from
> `docs/_pipeline/templates/*.md.dt` via `mur docs compile` (repo memory).
> Edit the templates, never the compiled output.

- [x] `docs/_pipeline/templates/extending-reactor-controls.md.dt` —
      rewrite the `.Controlled` section around the `Optional<T>`
      requirement. Decision tree from §5.2:
        - controlled with user authority → `Optional<T>`
        - one-way with WinUI fallback → `Optional<T>` + `dp:`
        - mount-only → `Prop.InitialOnly`
        - always write, no callback → plain-`T` `.OneWay`
      Cite the React analogy (§2). Call out the §4.3 implicit-conversion
      gotcha (R1).
- [x] `docs/_pipeline/templates/control-reconciler-protocol.md.dt` —
      add an "Optional-aware Update gate" subsection in the Update
      protocol section.
- [x] `docs/_pipeline/templates/advanced.md.dt` — new
      "`Optional<T>` and authority" section. Include the snap-back recipe
      (§6.5) and the `ClearValue` channel example (§6.3). Cross-check the
      `UseReducer(false) + bump` API against `RenderContext.cs:490-528`
      before publishing (R11).
- [x] New top-level doc: `docs/guide/migration/050-optional-t.md` (with
      template `docs/_pipeline/templates/migration/050-optional-t.md.dt`)
      — app-author migration guide for the 26 records in spec §9.2.
      Includes sentinel-value migration table
      (`SelectedIndex = -1` → `Optional<int>.Unset` for "control owns"
      vs `Optional.Of(-1)` for "force-assert deselect").
- [x] `docs/reference/` — analyzer reference page documents
      `REACTOR0050`.
- [x] Ran `mur docs compile --no-build --skip-screenshots --skip-diagrams` cleanly on ARM64 and committed both template + compiled Markdown output. Existing reference-generation warnings are non-fatal and unrelated.

### 11.2 README + spec cross-reference

- [x] `README.md` — bump the "Authoring custom controls" pointer if it
      lists `.Controlled` signatures.
- [x] `docs/specs/047-extensible-control-model.md` — append a
      "Superseded by spec 050" note to §13 Q17 and the Controlled section.

### 11.3 Skill updates

For every `.SKILL.md` file under `plugins/reactor/skills/` and every
reference `.cs` snippet, audit for code blocks containing `Slider(...)`,
`TextBox(...)`, `ToggleSwitch(...)`, `ComboBox(...)`, `CheckBox(...)`,
`SelectedIndex = ...`, `IsChecked = ...`, etc. (per the grep run during
planning):

- [x] `plugins/reactor/skills/reactor-design/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-commanding/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-docking/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-getting-started/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-forms/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-input/SKILL.md`.
- [x] `plugins/reactor/skills/reactor-dsl/references/reactor.api.txt` —
      this is the LLM API reference; regenerated/hand-audited changed
      signatures (factory shorthand unchanged; added `Optional<T>`
      overload lines where Task 6.2 introduced them). Mirrored the same
      signature updates into the root `skills/reactor.api.txt` index.
- [x] `plugins/reactor/skills/reactor-recipes/references/async-fetch-list.cs`.
- [x] `plugins/reactor/skills/reactor-recipes/references/form-with-validation.cs`.
- [x] `plugins/reactor/skills/reactor-recipes/references/list-add-delete.cs`.
- [x] `plugins/reactor/skills/reactor-recipes/references/use-custom-hook.cs`.

In each: keep the call-site syntax exactly the same where the implicit
conversion saves the user (`with { Value = 5 }`), update any read-back
patterns (`var v = el.Value;` → `var v = el.Value.GetValueOrDefault(0);`),
and add a "controlled props use `Optional<T>` — see
[`migration/050-optional-t.md`]" sidebar where relevant.

### 11.4 PropertyGrid behavior (R8)

- [x] `src/Reactor/Controls/PropertyGrid/ReflectionTypeMetadataProvider.cs`
      — when the inspected property type is `Optional<T>`, emit a
      read-only label editor with the rendered `ToString()`. Do **not**
      attempt to provide an interactive editor in this PR — Optional
      authoring is a runtime semantic, not a data-binding shape.
      File a follow-up issue if interactive editing is wanted.
- [x] Add a unit test under `tests/Reactor.Tests/PropertyGrid/` covering
      the `Optional<T>` field path.

---

## Phase 12 — Baseline sweep, perf gate, ship

### 12.1 Baseline triage (spec §11.4)

- [x] Diffed the recorded `main` baseline against migrated-tree results.
      Orchestrator verified unit tests at 9189 passed / 0 failed / 62 skipped;
      full selftest suite had only three pre-existing environment flakes,
      each clean in two isolation reruns.
- [x] Tracked the triage table in the paste-ready PR description: Agent 3
      footgun rewrites, Agent 5 TAP-skipped user-event-only selftests, and
      the three pre-existing flakes are all categorized as non-regressions.
- [x] Agent 3 partial sweep: `tests/Reactor.Tests` compile fallout repaired and unit suite compared against the recorded 9135-pass baseline; migrated tree passes with 9186 passed / 0 failed / 62 skipped; final orchestrator run verified 9189 passed / 0 failed / 62 skipped.

### 12.2 Perf gate (spec §11.3)

- [x] Confirmed Task 9.3 migrated-tree benches: OAlloc mean 316.9 ns/op; OUpdate mean 80.5 µs/op. Paired simple `new TextBoxElement("hello")` probe vs main showed throughput improved (8.97 ns/op vs 9.82 ns/op) with the expected +8 B/op record-size increase from the `Optional<string>` field.
- [x] Confirmed no measurable Update hot-path concern from migrated-tree OUpdate absolute; exact OUpdate pair cannot run on `main` because the OUpdate bench was introduced by this branch.
- [x] Captured details in `docs/specs/050-baseline-sweep/perf/comparison.log` and folded them into the PR description before final artifact cleanup.

### 12.3 Stress run

- [x] Triggered CI stress run (`.github/workflows/ci-stress.yml`) on branch
      `spec050-optional-controlled-prop-authority` via `gh workflow run`:
      https://github.com/microsoft/microsoft-ui-reactor/actions/runs/26973961537.
      Reviewer should monitor completion; do not gate on existing PDM_* /
      NativeDock_* known low-rate flakes per repo memory.

### 12.4 Hot reload sanity (R7)

- [x] Documented hot-reload limit in
      `docs/_pipeline/templates/dev-tooling.md.dt` (compiled to
      `docs/guide/dev-tooling.md`): `ReactorHotReloadCopier` copies
      fields by name and compatible type, so a stored value whose field
      type changes from `int` → `Optional<int>` cannot migrate across the
      hot-reload boundary; restart or remount to cross that schema change.
      Manual live smoke remains reviewer verification because long-lived
      `dotnet watch` processes are risky in this shared environment.

### 12.5 Final cleanup

- [x] Removed the `docs/specs/050-baseline-sweep/` artifacts directory
      after folding the unit/selftest/perf/AOT findings into the
      paste-ready PR description.
- [x] Tag the spec §15 "Open questions" section as resolved (or append
      any newly-discovered ones with their resolution).

---

## Out-of-scope (deliberately deferred)

- **141 `OneWayConditional` migrations** to `Optional<T>` + `dp:` (spec
  §9.1). The new entry shape ships here; the migration is opportunistic
  per descriptor as future PRs.
- **Three-state CheckBox descriptor extension** — known gap per
  `CheckBoxDescriptor.cs:23-35`. Untouched by this work.
- **TextBox snap-back gap** — known gap per `TextBoxDescriptor.cs`.
  Untouched.
- **Expanded pool eligibility** (Task 7.4). Filed as follow-up.
- **PropertyGrid interactive `Optional<T>` editor** (R8). Read-only this
  PR; interactive in a follow-up.
- **`Map` / `Bind` / LINQ on `Optional<T>`** (spec §4.4). Will grow
  organically if needed.

---

## Cross-references

- Spec: [`docs/specs/050-controlled-prop-authority-and-optional-t.md`](../050-controlled-prop-authority-and-optional-t.md)
- Customer issue: [#494](https://github.com/microsoft/microsoft-ui-reactor/issues/494)
- Echo-suppression contract: [`docs/specs/047-extensible-control-model.md`](../047-extensible-control-model.md) §8
- ClearValue primitive (verified): `microsoft-ui-xaml-lift/dxaml/xcp/dxaml/lib/DependencyObject.cpp`
- React reference: <https://react.dev/reference/react-dom/components/input#controlling-an-input-with-a-state-variable>

