# Chunk 22 — Data system & controls

**Status:** Phase 2 — review complete
**Reviewed commit SHA:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer scope:** STRIDE + code review with focus on reflection-based EoP, validation bypass via async/partial-failure paths, formatter/regex DoS, and cross-trust ordering bugs in editing pipelines.

> The chunk-plan (`000-chunking-and-threat-model.md`) frames the primary threats as "instantiating attacker-controlled types via metadata" and "validation chains that can be bypassed degrade integrity invariants the dev believes are enforced." The principal finding of this review is that **no `Type.GetType(string)`, `TypeNameHandling`, `BinaryFormatter`, `XmlSerializer`, or `Assembly.Load` call exists in this surface**. All `Activator.CreateInstance` calls operate on `Type` references that originated from a developer-supplied generic parameter or `someObject.GetType()` — i.e., the type identity is *already* trusted before reflection runs. The realistic threat surface is therefore **(a) ReDoS/DoS on user-provided regex and mask strings**, **(b) silent validation bypass via the `ClearInternal`-then-add ordering used everywhere**, **(c) async-validator races where a row commits before async results land**, and **(d) reflection-based field-name dispatch (sort/filter) that ignores `[PropertyHidden]`/`[Browsable(false)]`**.

---

## 1. Scope

### `src/Reactor/Data/**`

| File | LOC | Role |
|---|---|---|
| `src/Reactor/Data/DataPage.cs` | 18 | `DataPage<T>` record + `IDataPage`. |
| `src/Reactor/Data/DataPageCache.cs` | 345 | LRU cache around `IDataSource<T>` with in-flight tracking. |
| `src/Reactor/Data/DataRequest.cs` | 25 | Request record (sort/filter/search/page). |
| `src/Reactor/Data/DataSourceResourceExtensions.cs` | 60 | `UseDataSource` hook glue. |
| `src/Reactor/Data/FieldDescriptor.cs` | 104 | Unified column / property descriptor. |
| `src/Reactor/Data/FilterDescriptor.cs` | 30 | Filter record + operator enum. |
| `src/Reactor/Data/GridAttributes.cs` | 33 | Reactor-specific column attributes. |
| `src/Reactor/Data/IDataSource.cs` | 60 | Source / mutable / observable / keyed interfaces. |
| `src/Reactor/Data/Providers/ListDataSource.cs` | 224 | In-memory provider with reflection sort/filter/search. |
| `src/Reactor/Data/Providers/ObservableListDataSource.cs` | 104 | INPC-aware provider. |
| `src/Reactor/Data/RowKey.cs` | 13 | Stable row key wrapper. |
| `src/Reactor/Data/SortDescriptor.cs` | 15 | Sort record. |
| **Subtotal** | **1031** | |

### `src/Reactor/Controls/**` (DataGrid, PropertyGrid, Editors, Validation, Virtualization, Formatting, MaskedTextBox, AutoSuggest)

| File | LOC | Role |
|---|---|---|
| `src/Reactor/Controls/DataGrid/ColumnDsl.cs` | 231 | Reflection-driven column generation. |
| `src/Reactor/Controls/DataGrid/DataGridComponent.cs` | 1354 | Grid renderer. |
| `src/Reactor/Controls/DataGrid/DataGridDsl.cs` | 94 | DSL factory. |
| `src/Reactor/Controls/DataGrid/DataGridElement.cs` | 117 | Element record. |
| `src/Reactor/Controls/DataGrid/DataGridState.cs` | 1449 | Headless state machine: sort/filter/edit/validation/commit. |
| `src/Reactor/Controls/DataGrid/ResizeGrip.cs` | 90 | Column-resize affordance. |
| `src/Reactor/Controls/DataGrid/TypedColumns.cs` | 172 | Type-strict column factories. |
| `src/Reactor/Controls/PropertyGrid/ArrayOperations.cs` | 150 | Add/remove/reorder over `IList`/`Array`. |
| `src/Reactor/Controls/PropertyGrid/Attributes.cs` | 62 | `[PropertyOrder]` etc. |
| `src/Reactor/Controls/PropertyGrid/PropertyGridComponent.cs` | 402 | Renderer + `EditChain` propagation. |
| `src/Reactor/Controls/PropertyGrid/PropertyGridDefaults.cs` | 67 | Default templates. |
| `src/Reactor/Controls/PropertyGrid/PropertyGridDsl.cs` | 31 | DSL. |
| `src/Reactor/Controls/PropertyGrid/PropertyGridElement.cs` | 51 | Element record. |
| `src/Reactor/Controls/PropertyGrid/ReflectionTypeMetadataProvider.cs` | 381 | Reflection-based decompose/compose. |
| `src/Reactor/Controls/PropertyGrid/TypeMetadata.cs` | 61 | Metadata records. |
| `src/Reactor/Controls/PropertyGrid/TypeRegistry.cs` | 343 | CLR-type → `TypeMetadata` resolver. |
| `src/Reactor/Controls/Editors/CellRenderers.cs` | 131 | Display renderers. |
| `src/Reactor/Controls/Editors/Editors.cs` | 317 | Editor factories. |
| `src/Reactor/Controls/Validation/ErrorStyling.cs` | 158 | Error visual styling. |
| `src/Reactor/Controls/Validation/FormField.cs` | 157 | Form-field wrapper. |
| `src/Reactor/Controls/Validation/UseValidationContext.cs` | 67 | Hook bridge. |
| `src/Reactor/Controls/Validation/ValidateExtensions.cs` | 138 | Fluent `.Validate(…)` API. |
| `src/Reactor/Controls/Validation/ValidationContext.cs` | 455 | Synchronized message store. |
| `src/Reactor/Controls/Validation/ValidationMessage.cs` | 24 | Message record + severity. |
| `src/Reactor/Controls/Validation/ValidationReconciler.cs` | 85 | Drives sync/async validators. |
| `src/Reactor/Controls/Validation/ValidationRule.cs` | 90 | Cross-field rule element. |
| `src/Reactor/Controls/Validation/ValidationVisualizer.cs` | 167 | Bubble / catch errors. |
| `src/Reactor/Controls/Validation/Validators/BuiltInValidators.cs` | 217 | Required / Min / Max / Match / Url / … |
| `src/Reactor/Controls/Validation/Validators/IValidator.cs` | 25 | Validator interfaces. |
| `src/Reactor/Controls/Virtualization/VirtualListComponent.cs` | 107 | LazyVStack adapter. |
| `src/Reactor/Controls/Virtualization/VirtualListDsl.cs` | 40 | DSL. |
| `src/Reactor/Controls/Virtualization/VirtualListElement.cs` | 123 | Element record + ref. |
| `src/Reactor/Controls/Formatting/InputFormatter.cs` | 272 | Phone/Currency/Allow/Deny formatters. |
| `src/Reactor/Controls/MaskedTextBox/MaskedTextFieldElement.cs` | 57 | Element record. |
| `src/Reactor/Controls/MaskedTextBox/MaskEngine.cs` | 228 | Mask parser/applier. |
| `src/Reactor/Controls/AutoSuggest/AutoSuggestElement.cs` | 155 | Async-search component + `SearchManager<T>`. |
| **Subtotal** | **8068** | |
| **Grand total** | **9099** | |

---

## 2. Data-flow diagram

```
 App developer code
   │
   │  defines T (data row), columns, validators, registry overrides
   ▼
 ┌──────────────────────────────────────────────────────────────────┐
 │  DataGridElement / PropertyGridElement / VirtualListElement     │
 │  (element records with developer-provided callbacks)             │
 └─────────────┬────────────────────────────────────────────────────┘
               │
               ▼
 ┌──────────────────────────┐         ┌──────────────────────────┐
 │ DataGridState<T>         │ ◀──────▶│ TypeRegistry / Reflection│
 │  ├─ sort/filter list     │         │   ├─ Resolve(Type)       │
 │  ├─ edit state           │         │   ├─ Decompose(owner)    │
 │  ├─ ValidationContext    │         │   └─ Compose(updates)    │
 │  └─ pending commits      │         └──────────────────────────┘
 └─────────────┬────────────┘
               │
   ┌───────────┴────────────┐
   │                        │
   ▼                        ▼
 ┌────────────────────┐   ┌────────────────────────────────────┐
 │  IDataSource<T>    │   │  Validators / Formatters / Mask    │
 │  ├─ ListDataSource │   │   ├─ Validate.Match(regex)         │
 │  │   (reflection-  │   │   ├─ InputFormatter.AllowOnly(rgx) │
 │  │    based sort/  │   │   ├─ MaskEngine(maskString)        │
 │  │    filter on    │   │   └─ ValidationContext (sync')     │
 │  │    field name)  │   └────────────────────────────────────┘
 │  └─ user-supplied  │
 │     async source   │
 └────────────────────┘

 Inputs that flow into this surface:
 ───────────────────────────────────
   1.  User row data (T instances)             — developer-trusted (in-proc)
   2.  Cell edit text from keyboard/paste      — UNTRUSTED (runtime)
   3.  Filter / sort field-name strings        — semi-trusted: developer
                                                  builds them, but UI
                                                  affordances let the user
                                                  type / pick them
   4.  Regex strings to Validate.Match,
       AllowOnly, DenyOnly, Email              — developer-authored,
                                                  but a translation
                                                  string or config could
                                                  feed them
   5.  Mask strings to MaskEngine              — developer-authored
   6.  Async-validator results                  — UNTRUSTED if validator
                                                  hits a network
   7.  Async-data-source pages                  — same
```

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption made by code |
|---|---|---|
| App developer's CLR types ↔ reflection on `T`/property names | both | Property names supplied to `ListDataSource.ApplyFilter` / `ApplySort` and to `DataGridState.ApplyClientSort/Filter` are trusted enough to read any public instance property of `T`. `[Browsable(false)]` / `[PropertyHidden]` are display attributes only — they do **not** restrict reflection access. |
| Untrusted UI text → validator/formatter regex compile | inbound | The *pattern* passed to `Validate.Match`, `InputFormatter.AllowOnly`, `DenyOnly`, and `Validate.Email` is assumed to be developer-authored at code-write time. There is no `Regex` timeout. |
| Async validator / async data source ↔ render loop | outbound (await) | `DataGridState.CommitEdit()` and `CommitRowEdit()` block on `_editValidation.IsValid()`, which only contains messages from validators that have *already added* to the context. Async validators that have not yet completed are ignored. |
| `Activator.CreateInstance` for array element / record copy | inbound | Type is always derived from a `T` generic parameter or a `Type` already obtained via `someObject.GetType()` — the attacker model "type-name supplied as data" is **not present** in this codebase. |
| Cross-thread `StateChanged?.Invoke()` from `Timer` callback in `SearchManager` | outbound | The callback runs on the threadpool but the event is consumed by Reactor's render loop on the UI thread. No marshalling. |
| `ObservableListDataSource` ↔ `INotifyPropertyChanged` events | inbound | The collection's items can fire INPC from arbitrary threads; subscriber holds no synchronization between event handler and subsequent `DataChanged?.Invoke()`. |

---

## 4. Asset inventory

| Asset | What an attacker would want to do to it |
|---|---|
| **Validation invariants** ("required", "max length", "pattern matched", server-side async check returned OK) | Bypass them — commit a row whose validator would have rejected it, by exploiting async/clear ordering. |
| **Reflection-only data** (properties declared `[Browsable(false)]`, `[PropertyHidden]`, or marked init-only with secret material in records) | Extract the values via grid sort/filter side-channels; mutate them via PropertyGrid's reflection-based `BuildCompose` fallback that copies *all* writable properties, including ones the developer believes are "hidden." |
| **App availability** (UI thread responsiveness) | DoS via ReDoS in `Validate.Match` / `Validate.Email` / `AllowOnly`; DoS via huge mask strings; DoS via `PageSize = int.MaxValue` client-side fallback. |
| **App integrity** (the row item shown in the grid is the row item the user authored) | Race optimistic-commit error revert against further user edits; mutate inflight cache state via observable INPC from a background thread. |
| **Information disclosure** | A grid that lets the user pick filter/sort field names from a dropdown can be made to read any public property of `T` — including fields the developer marked `[PropertyHidden]` or `[Browsable(false)]`, e.g. an `Account.PasswordHash`. The hash isn't displayed, but ordering / filtering reveals it. |

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| T1 | DoS | ReDoS on `Validate.Match` / `Email` / `AllowOnly` / `DenyOnly` patterns. | Developer ships a pattern with catastrophic backtracking, OR a translation/config supplies it; user types into the field. | UI thread hangs (`Regex.IsMatch` is run synchronously inside the render path via `ValidateField`). | Medium — depends on whether the pattern is fixed at compile time. | None. `RegexOptions.Compiled` is set, but no `MatchTimeout` on `Regex` and no length cap. The default email regex (`BuiltInValidators.cs:45`) is itself bounded ([0,61] each label) and not catastrophic, but `Validate.Match(<arbitrary>)` is. | F-1 (Medium). |
| T2 | Tampering / Bypass | "Last writer wins" on `ClearInternal` — multiple validators or rules targeting the same field cause earlier failure messages to be erased before the form is submitted. | Developer composes two `ValidationRule`s on the same field, OR composes one `Validate.Required` plus one `ValidationRule`. The latter's evaluator wipes the prior. | Form submits with invariant violation that the dev believed they enforced. | High — natural composition pattern. | None. | F-2 (High). |
| T3 | Tampering / Bypass | Async-validator race: `CommitEdit` checks `IsValid()` synchronously while async validators are still in flight. | User edits a cell that has only async validators (e.g., uniqueness check), presses commit before the async task completes. | Row commits with no validation having run. | High in single-edit path; medium in row-edit path. | `DataGridState.CommitEdit` and `CommitRowEdit` block on `_editValidation.IsValid()` only. The async result, if added later, lands in a `ValidationContext` that has already been zeroed out by the commit's path (line 1000 `_editValidation = null`). | F-3 (High). |
| T4 | Tampering | Async validator results accumulate without dedup: `ValidateFieldAsync` (`DataGridState.cs:1254`, `ValidationReconciler.cs:64`) does **not** call `ClearInternal` first, so re-running it appends a new copy of every error every time the value changes. | Field value changes rapidly (typing); each keystroke fires a fresh async run; old results are not removed. | Eventually displays "duplicate" errors and persists stale results from earlier values. Cosmetic at first, but `IsValid()` returns wrong answer if the most-recent value is valid but a stale invalid is still present. | High when async validators are wired. | None. The sync path *does* clear (`ValidationReconciler.cs:25`); the async path does not. | F-4 (Medium). |
| T5 | Information disclosure / EoP | Reflection by **string field name** in `ListDataSource.ApplyFilter` (`:131`), `ApplySort` (`:205`), `DataGridState.ApplyClientFilters` (`:1420`), `ApplyClientSort` (`:1392`) ignores `[PropertyHidden]` / `[Browsable(false)]` — these are decompose-time filters in `ReflectionTypeMetadataProvider.IsHidden` only. | An app exposes a filter-by-field-name UI (CSS-like or a dropdown that includes "advanced" fields) and accepts user input. User filters by `Equals` on a sensitive property until row count → 0 to recover its value. | Side-channel disclosure of fields the developer thought were hidden. | Low–medium — depends on whether the app exposes such a UI. | None — `BindingFlags.Public | Instance` plus the `Field` string literally drives reflection. | F-5 (Medium). |
| T6 | EoP / Tampering | `ReflectionTypeMetadataProvider.BuildCompose` parameterless-ctor fallback (`:362`) writes **every** writable property of the new instance, copying values from the original object. Combined with PropertyGrid's `EditChain.PropagateImmutableEdit`, an edit to property `A` rebuilds the parent and silently re-writes `B`, `C`, … — including ones the dev marked `[PropertyHidden]`. | Developer treats `[PropertyHidden]` as "users can't edit this." A compose-driven edit on a sibling property invokes `Activator.CreateInstance` and replays `_currentValues` for every property. | Hidden-only-in-UI invariants aren't structurally enforced; the round-trip can drop invariants enforced by the constructor that the fallback never calls. | Medium. | The primary path tries to find a constructor matching property names first; the parameterless fallback only runs when no such ctor exists. | F-6 (Medium). |
| T7 | DoS | `ListDataSource.ApplySearch` reflects over **all** public string properties of `T` and runs `Contains(query, OrdinalIgnoreCase)` per row per property (`:185–193`). With a wide row and a million rows this is O(rows × stringProps × len). The grid's "client fallback" (`DataGridState.cs:1309`) sets `PageSize = int.MaxValue` and pulls the entire dataset into memory if the source advertises only client capabilities. | Developer wires a non-trivial source as `ListDataSource` and relies on the server-capability flags. The user types in the search box. | UI freeze / OOM. | Medium — `ListDataSource` is documented as in-memory, but the `int.MaxValue` `PageSize` in the grid's fallback is a footgun for any custom source that *honors* the request. | None — no row cap, no length cap on `query`, no compiled regex (`Contains` is bounded though). | F-7 (Medium). |
| T8 | Tampering / Concurrency | `SearchManager<T>.Search` (`AutoSuggestElement.cs:89`) disposes the prior `CancellationTokenSource` and `Timer` outside any lock; the threadpool `Timer` callback writes `State`, `Results`, `ErrorText` and reads `_cts.Token` without synchronization. Concurrent `Search`/`Cancel`/`Dispose` calls race. | Rapid keypress; `Dispose` while a search is in flight. | `ObjectDisposedException`, lost cancellations, stale results overwrite fresh results, `StateChanged` invoked cross-thread without dispatcher marshalling. | Medium. | Comment on line 122 says "cancelled — don't update state", but the cancellation check happens inside the await callback, after `State = SearchState.Loading` was already written. | F-8 (Medium). |
| T9 | DoS | `MaskEngine` parses every char of the mask string to a `MaskToken` (`MaskEngine.cs:172`), and `Apply` walks the token array per `Apply` call (`:64`). `MaskedTextFieldElement.RawValue` and `IsComplete` (`:23`,`:36`) construct a **fresh** `MaskEngine` per access — and they're invoked from `get` properties used in render. A multi-megabyte mask string passed to `MaskedTextField(mask: ...)` reparses on every render. | Developer interpolates user input or a translation into the `mask:` argument. | Render-thread O(n) re-allocation per frame; eventually CPU-bound. | Low — masks are usually constants — but the per-render allocation is wasteful. | None. | F-9 (Low). |
| T10 | Repudiation / Integrity | `DataPageCache.FetchBlockAsync` swallows **all** non-`OperationCanceledException` exceptions (`:306`) and stores the exception's `.Message` in a `Failed` block. The `BlockLoaded` event still fires; the block is permanent until invalidated. | Data source raises a `SecurityException` (e.g., access denied) on a specific page. | The block is replaced with an empty `Failed` placeholder; the row count seen by callers may already be set from an earlier success and *stays*, so totals don't reflect the failed range. | Low. | The error is exposed via `block.Error`, but no logging hook fires. | F-10 (Low). |

---

## 6. Findings

### F-1 — Unbounded user-supplied regex on UI thread (DoS / ReDoS)

**Files:** `src/Reactor/Controls/Validation/Validators/BuiltInValidators.cs:142–162`, `src/Reactor/Controls/Formatting/InputFormatter.cs:235–261`

**Severity:** Medium

`MatchValidator` constructs a `Regex` with `RegexOptions.Compiled` and **no `MatchTimeout`**. The validator is run synchronously inside `ValidateField` from `DataGridState.UpdateRowEditValue` (which fires on every keystroke) and from `ValidationReconciler.ValidateField`. `AllowOnlyFormatter` and `DenyOnlyFormatter` do the same — pattern arrives as a `string`, no length cap, no timeout.

The default `Validate.Email` pattern (`BuiltInValidators.cs:45`) is itself non-catastrophic (each label segment is bounded `{0,61}`), but **any** developer call like

```csharp
Validate.Match(@"^(a+)+$", ...)
```

freezes the UI thread on a 30-character `aaaa…X`. The same applies to `InputFormatter.AllowOnly(<user-supplied pattern>)` — and the formatter is invoked on every keystroke during typing.

**Recommendation:**
- Set `Regex.MatchTimeout` to something like `TimeSpan.FromMilliseconds(50)` for the compiled regex, and treat timeout as a validation failure (or non-match in the formatter case).
- Consider rejecting patterns above a length cap (e.g. 1024 chars) at construction, since long patterns are almost always developer mistakes.
- Document that `Validate.Match` patterns must be developer-authored constants, not interpolated from translations / configs / network.

### F-2 — `ClearInternal`-then-add wipes co-located validation messages (silent bypass)

**Files:** `src/Reactor/Controls/Validation/ValidationReconciler.cs:24,46`, `src/Reactor/Controls/Validation/ValidationRule.cs:61`, `src/Reactor/Controls/DataGrid/DataGridState.cs:1237`

**Severity:** High

Every code path that runs validators on a field starts with:

```csharp
ctx.ClearInternal(fieldName);     // wipes ALL internal messages for this field
// then add new ones
```

`ClearInternal` removes the entire bucket for the field — it is *not* scoped to "messages I added." So:

1. `ValidationRuleElement.Evaluate` (line 61) and a sibling `ValidationRuleElement` on the same field — the second wipes the first's failure message.
2. Two consecutive `ValidationReconciler.ValidateField` calls in the same render — same problem.
3. `DataGridState.ValidateField` (line 1237) wipes any messages written by an async validator into the same context.

In practice the developer composes `Validate.Required("field", value)` and a separate `ValidationRule(field: "x", predicate: …)` and assumes both errors are visible. They aren't — only the last evaluation survives. `IsValid()` then returns "valid" once the last validator passes, even though an earlier one failed.

**Recommendation:** Track messages by *source* (validator instance, rule element, or a token returned at registration). `ClearInternal` should clear only that source's prior messages. Alternatively, keep an "accumulating" mode in which `ValidateField`/`Evaluate` only clear messages that share the same `Code`.

### F-3 — `CommitEdit` / `CommitRowEdit` ignore in-flight async validators

**Files:** `src/Reactor/Controls/DataGrid/DataGridState.cs:957, 1091, 1254`

**Severity:** High

`CommitEdit` (line 962) and `CommitRowEdit` (line 1096) gate on `_editValidation.IsValid()` synchronously. There is no `await` for in-flight async validators, no "pending validation" flag, and no observation of whether `ValidateFieldAsync` (line 1254) has been called at all for the current edit.

`UpdateRowEditValue` (line 1077) calls only the sync `ValidateField`. The caller (DataGrid render) must remember to *separately* invoke `ValidateFieldAsync` and wait for it before allowing commit. Nothing in `DataGridState` enforces this; the public API encourages "type, then press Tab/Enter to commit."

Consequence: an async uniqueness validator (network round-trip, "is this email already taken?") is silently skipped if the user commits faster than the network. A developer who configured `Validate.MustAsync(...)` believes the row cannot commit with a duplicate email; it can.

**Recommendation:**
- Track "async validation in flight" inside `DataGridState`; have `CommitEdit`/`CommitRowEdit` either return null or queue the commit until the in-flight validation drains.
- Add a `HasPendingAsyncValidation` predicate visible to UI consumers so they can disable the commit button.
- Document the contract on `IAsyncValidator` clearly: "async validators run *to completion* before commit succeeds."

### F-4 — Async validator path does not clear stale messages

**Files:** `src/Reactor/Controls/Validation/ValidationReconciler.cs:64–71`, `src/Reactor/Controls/DataGrid/DataGridState.cs:1254–1267`

**Severity:** Medium

The sync path (`ValidationReconciler.ValidateField`, line 25) calls `ctx.ClearInternal(fieldName)` before running validators. The async path (`ValidateFieldAsync`, line 64) does **not**. Each invocation appends results without removing prior ones. If the value changes from `"a"` (invalid) → `"b"` (valid) → `"c"` (invalid), then re-runs of the async validator leave stale messages from `"a"` lingering, and `IsValid()` returns false even though the current value is valid. Conversely, if the value changes back from invalid → valid and only async validators apply, the prior invalid message persists and `CommitEdit` is wrongly blocked.

`DataGridState.ValidateFieldAsync` has the same bug.

**Recommendation:** Async path should clear at start (with the F-2 caveat — clear by *source*, not bucket-wide), and should associate each result with the value-revision it was computed for so stale results from earlier values are dropped on arrival.

### F-5 — Reflection sort/filter by string field name has no allowlist

**Files:** `src/Reactor/Data/Providers/ListDataSource.cs:131, 185, 205`; `src/Reactor/Controls/DataGrid/DataGridState.cs:1392, 1420`

**Severity:** Medium

```csharp
var prop = typeof(T).GetProperty(filter.Field, BindingFlags.Public | BindingFlags.Instance);
if (prop is null) return items;
return ... items.Where(x => Equals(prop.GetValue(x), filter.Value)) ...
```

Any `FilterDescriptor` or `SortDescriptor` whose `Field` matches **any** public instance property of `T` succeeds — including ones the developer marked `[PropertyHidden]` or `[Browsable(false)]`. Those attributes are honored only in `ReflectionTypeMetadataProvider.IsHidden` (line 224), which gates the *PropertyGrid display* of the property, not reflection access.

If the application exposes a filter-builder UI that takes a column name from the user (CSS-like selectors, advanced-filter dropdown including "all properties of T"), the user can choose `PasswordHash` (or any sensitive property) and binary-search its value via `Contains` / `Equals` filters by observing row counts. Same for `OrderBy` revealing ordering against a hidden field.

**Recommendation:**
- Add a `FilterAllowed` / `SortAllowed` predicate to `ListDataSource`/`DataGridState` so the developer must opt in.
- Default behavior should be: only fields exposed via `FieldDescriptor` (i.e., visible columns) are allowed targets for sort/filter.
- Document explicitly that `[PropertyHidden]` is not a security boundary.

### F-6 — `BuildCompose` parameterless-ctor fallback re-writes hidden properties

**File:** `src/Reactor/Controls/PropertyGrid/ReflectionTypeMetadataProvider.cs:361–377`

**Severity:** Medium

When no constructor matches the property names, the fallback path is:

```csharp
var newObj = Activator.CreateInstance(type)!;
foreach (var prop in properties)
{
    if (!prop.CanWrite) continue;
    var value = updates.TryGetValue(prop.Name, out var updated)
        ? updated
        : prop.GetValue(currentValue);
    prop.SetValue(newObj, value);
}
return newObj;
```

This iterates the same `properties` array passed in by the caller (`BuildMetadata`, line 128), which is filtered by `IsHidden` *but* `IsHidden` is not consulted here. More importantly: **the constructor that the type defines is bypassed.** Any invariant the developer enforced in the ctor (validation, derived-field computation, defensive copy) is silently dropped.

For PropertyGrid usage this is mostly cosmetic — but `BuildInitOnlySetter` (line 301) is also reachable from `DataGrid` column auto-generation (`ColumnDsl.cs:138`), and the same fallback is in play.

**Recommendation:**
- If no name-matching ctor exists, *fail* `BuildCompose` and surface the type as immutable/read-only rather than silently round-tripping every writable property.
- Consider adding a `[ComposeIgnore]` attribute (or honoring `[PropertyHidden]` in the fallback) so the dev can keep some properties stable across edits.

### F-7 — Client-fallback path requests `PageSize = int.MaxValue`

**File:** `src/Reactor/Controls/DataGrid/DataGridState.cs:1308–1309`

**Severity:** Medium

```csharp
var request = new DataRequest
{
    PageSize = int.MaxValue,
    ...
};
var page = await _source.GetPageAsync(request, cancellationToken);
```

When the data source declares neither `ServerSort` nor `ServerFilter` (or `ServerSearch`), the grid falls back to "load everything, sort/filter locally." A custom `IDataSource<T>` that honors `PageSize` literally — for example, a SQL-backed source that emits `LIMIT @n` — will issue an unbounded query. Even sources that cap to a sensible default still receive a request that *says* "give me all rows, please."

**Recommendation:**
- Cap `PageSize` to a configurable maximum (e.g., 100 000) and document that the grid does not support client-side fallback for unbounded data sources.
- Better: detect client fallback at registration time and refuse to mount the grid against a source that lacks the necessary server capabilities.

### F-8 — `SearchManager<T>` is not thread-safe; can race `Cancel`/`Dispose`/`Search`

**File:** `src/Reactor/Controls/AutoSuggest/AutoSuggestElement.cs:89–155`

**Severity:** Medium

`SearchManager<T>.Search` mutates `_cts`, `_debounceTimer`, `State`, `Results`, `ErrorText` without any lock. The `Timer` callback runs on the threadpool. In the worst case:

- `Dispose` cancels and disposes `_cts`; an in-flight `Timer` callback then awaits `_search(query, token)`; on resume it dereferences a disposed token and races `StateChanged?.Invoke()`.
- Two `Search` calls back-to-back: the second `Cancel()`/`Dispose()`s `_cts` while the first's Timer callback is reading it — TOCTOU `NullReferenceException` or `ObjectDisposedException`.
- `StateChanged` is invoked cross-thread; subscribers that touch WinUI controls without `DispatcherQueue.TryEnqueue` will throw `RPC_E_WRONG_THREAD`.

The comment at line 121 ("Cancelled — don't update state") only handles the *normal* cancel path inside the awaited search, not the race against `Dispose`.

**Recommendation:**
- Wrap all mutations under a single lock.
- After `await _search`, re-check `_cts == oldCts && !token.IsCancellationRequested` before publishing results.
- Marshal `StateChanged` invocations onto the UI thread explicitly (or document that the consumer must marshal).

### F-9 — `MaskEngine` re-parsed on every render via element `get` accessors

**File:** `src/Reactor/Controls/MaskedTextBox/MaskedTextFieldElement.cs:18–39`

**Severity:** Low

`MaskedTextFieldElement.RawValue` and `IsComplete` build a fresh `MaskEngine` each invocation. These properties are reasonable to call from `Render()`, where they will run once per render. The engine's `Parse` is O(mask length); for normal masks (≤30 chars) this is fine, but a dev who supplies a translated mask (`MaskPreset.IPv4` is fine; a localized phone mask is fine) is one interpolation away from a per-frame O(n) allocation. The `Parse` method has no length cap.

**Recommendation:** Construct `MaskEngine` once at element construction, not per `get` call. Cache the engine on the element record. Add a sanity cap in `MaskEngine.Parse` (e.g., reject masks > 1024 chars with `ArgumentException`).

### F-10 — `DataPageCache` swallows source exceptions; `BlockLoaded` does not signal failure to logs

**File:** `src/Reactor/Data/DataPageCache.cs:306–318`

**Severity:** Low

```csharp
catch (Exception ex)
{
    var block = new CacheBlock<T>(blockIndex, Array.Empty<T>(), BlockStatus.Failed, ex.Message);
    ...
}
```

Any exception (`SecurityException`, `IOException`, hostile-source-specific) is reduced to a string `Message`. The cache stores the failed block forever (until `Invalidate`), and the `BlockLoaded` event fires identically for success and failure — consumers must inspect `block.Status`. There is no `ILogger` hook, no `EventSource` event, and the original exception is gone.

This is a repudiation/diagnostics weakness more than an exploit. A failing data source that *eventually* recovers won't be retried because the failed block is now cached — the consumer must call `Invalidate()` to retry, but `Invalidate` blows away **all** blocks including loaded ones (line 121).

**Recommendation:**
- Expose a per-block retry path that re-fetches just one block.
- Surface the original `Exception` (not just `.Message`) on `CacheBlock<T>` for diagnostic consumers.
- Consider firing an `EventSource` event for `BlockFetchFailed`.

---

## 7. Open questions for the team

1. **`[PropertyHidden]` semantics.** Is `[PropertyHidden]` documented anywhere as "display-only, not a security/access boundary"? F-5 and F-6 both depend on this contract. If the framework intends it to be a security boundary, both findings escalate.
2. **Validator composition.** Is the "last writer wins" behavior of `ClearInternal` (F-2) deliberate? If yes, this should be loudly documented at the `Validate.*` factory site so devs don't compose two `ValidationRule`s on the same field.
3. **Async-validator contract.** What is the intended commit semantics when async validators are present (F-3)? Should `CommitEdit` await pending async validation, or is the dev expected to disable the commit affordance until `ValidateFieldAsync` completes?
4. **Regex source trust.** Are validator regex patterns ever sourced from `.resw` translations (`Loc.g.cs`) or runtime config? If yes, F-1 escalates from Medium to High because a hostile localization PR becomes a UI-thread DoS.
5. **`PageSize = int.MaxValue` contract.** Is the client-fallback path (F-7) actually exercised in production sample apps, or is it dead code in practice? If exercised, the cap is needed; if not, the path should be removed and the grid should *fail loudly* when the source can't sort/filter.
6. **Cross-thread `StateChanged`.** Are AutoSuggest consumers expected to marshal `StateChanged` onto the dispatcher themselves, or does Reactor's render loop handle the cross-thread invocation? F-8 depends on this.

---

## 8. Out-of-scope referrals

- **Markdown / md4c** rendering of validation messages or grid cells (e.g., a dev pipes user content through `MarkdownBuilder` into a cell renderer) — defer to **Chunk 10**.
- **ICU formatting** in `CellRenderers.Date` / `Time` / numeric — locale-string parsing is **Chunk 11**'s problem.
- **Deep-link / selector parsing** is **Chunk 12** — but the *grid* exposes a CSS-like filter syntax in `DataGridState` that should be cross-checked against `SelectorParser` for grammar drift if the project later wires them together.
- **Reconciler concurrency** behind `StateChanged?.Invoke()` (cross-thread, reentrancy from validator effects) — defer to **Chunk 14**.
- **Hosting ETW** — `DataPageCache` (F-10) lacks ETW; if ETW becomes the standard event channel, this is **Chunk 15**.
- **Hooks library** — `UseDataSource`, `UseValidationContext`, `UseInfiniteResource` plumbing for paging — defer to **Chunk 23**.
- **WinForms interop** — none of this surface uses WinForms; not applicable.
