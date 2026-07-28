# Analyzer Suite Expansion — implementation & coordination

Companion to [`docs/specs/060-analyzer-suite-expansion.md`](../060-analyzer-suite-expansion.md).
The spec is the **design + grounding** source of truth; this doc is the
**execution contract** that lets the work fan out across parallel sessions
("the flight") without divergence or merge thrash. Read the spec section named in
each packet for the full detect/fix/FP reasoning — this doc does not re-derive it.

> **Status:** This doc *is* the foundation contract for spec 060. Implementation
> waves A–E unstarted. Update the tracking table (bottom) as packets merge.

---

## 0. How to use this doc (per session)

1. Find your packet in §4. It names your rule(s), the spec section, the suggested
   analyzer class, and your **canonical `AnalyzerReleases.Unshipped.md` row**.
2. Follow the global conventions in §2 (skeleton, release row, tests, samples).
3. Implement analyzer (+ code-fix if the packet says ✔) under
   `src/Reactor.Analyzers/`, tests under `tests/Reactor.Tests/AnalyzerTests/`.
4. Add your descriptor **and** your release row in the same PR (§2.2 — they are
   coupled both ways; do not pre-seed rows).
5. Run your rule against `samples/` (§2.4) before opening the PR.
6. If your rule is app-author-facing, add its cheat-table row (§2.6) in the same PR.
7. Run the review-loop HARD gate (§2.7) — `pr-review` skill + Copilot review until clean —
   THEN reply to your creator with the clean-pass confirmation. Not done until this is clean.

---

## 1. Resolved open questions (spec §11)

| OQ | Decision |
|---|---|
| **#1 Final IDs** | Adopt the spec's IDs verbatim. Verified no collision with shipped/unshipped (highest existing: `HOOKS_009`, `THEME_003`, `A11Y_003`, `DSL_001`, `REACTOR0050`). `REACTOR_PERF_FUNCREF` keeps its legacy non-`NNN` form; the Grid string-track fixer stays **id-less** (CS0618 code-fix, no descriptor, no release row). |
| **#2 OPT_001 vs REACTOR0050** | Stay **two rules**. `OPT_001` = app-site selection-sentinel literal; `REACTOR0050` = descriptor `dp:` omission. No merge. |
| **#3 Shared helpers** | **Extract-first**, not duplicate-then-consolidate. The restricted allocation classifier is extracted by the **Wave C** owner; the `.Set`-lambda helper is extracted by the **Wave B** owner (see §3). |
| **#4 Category naming** | Short `Reactor.*` form for **all** new categories (matches `Reactor.Hooks`/`Reactor.Style`/`Reactor.Pool`/`Reactor.Dsl`). The one outlier is `A11Y_004`, which stays `Microsoft.UI.Reactor.Accessibility` so it groups with the shipped `A11Y_001–003`. |
| **#5 THREAD_001 marker** | `[UIThreadOnly]` **attribute** (not a generated allowlist). The **Wave D** session owns both the framework attribute + annotations and the analyzer (see §5). |

---

## 2. Global conventions

### 2.1 Analyzer skeleton
Follow the shipping pattern (`PoolResetSetAnalyzer.cs` is the compact reference):
`[DiagnosticAnalyzer(LanguageNames.CSharp)] sealed`, `public const string DiagnosticId`,
static `DiagnosticDescriptor Rule`, `SupportedDiagnostics => ImmutableArray.Create(Rule)`,
`Initialize` → `ConfigureGeneratedCodeAnalysis(None)` + `EnableConcurrentExecution()` +
a **syntactic gate** (`RegisterSyntaxNodeAction(..., SyntaxKind.InvocationExpression)` or
`RegisterSymbolAction`) before any `GetSymbolInfo`/`GetTypeInfo` (spec §3). Code-fix
handoff data travels in `Diagnostic.Properties`, never message text.

Project already sets `EnforceExtendedAnalyzerRules=true` and nullable enable;
netstandard2.0; analyzer + code-fix co-located in the one shipped DLL (RS1038 suppressed).

**Battle-tested rules from the ⭐ validation wave (each caught a real HIGH bug — do not skip):**
- **Anchor hooks on `Component` OR `RenderContext`, not just `RenderContext`.** Hook /
  persistence / context APIs are re-exposed as `protected` wrappers on `Component` (e.g.
  `Component.UsePersisted` → `Context.UsePersisted`), so a component author's unqualified
  `UsePersisted(...)` binds to `Component`, not `RenderContext`. An analyzer that matches
  only `RenderContext.Xxx` misses the idiomatic call and fires nowhere. Accept both
  containing types — mirror the shipped `HookRulesAnalyzer.IsLikelyReactorHook` (anchors
  on Component *or* RenderContext). Applies to HOOKS_*, PERSIST_001, CTX_001.
- **Code-fixes that insert a type reference must emit a `global::`-qualified name** (e.g.
  `global::Microsoft.UI.Reactor.Core.PersistedScope.Window`), optionally + `Simplifier.Annotation`
  to shorten where a `using` exists, or resolve via `ToMinimalDisplayString`. A bare
  `IdentifierName("PersistedScope")` yields non-compiling code at any call site lacking the
  `using` (or using a fully-qualified receiver). Never emit a bare type identifier.
- **When a fix/analyzer reproduces runtime behavior, mirror the EXACT runtime code path.**
  The Grid fixer mirrored a *parallel* API (`GridSize.Parse`) that only "mostly agrees"
  with the obsolete overload's real parser (`PanelAttachedHooks.ParseColumnDef`), and
  silently changed layout on `"AUTO"`/`" 2* "`. Find the actual runtime path, match it
  exactly, and **withhold** wherever you can't guarantee identical semantics.

### 2.2 Release tracking (coupled both ways — verified)
- A descriptor with no row → **RS2000**. A row with no descriptor → **RS2002**.
  So **add your row when you add your descriptor, in the same PR. Never pre-seed.**
- Append your canonical row (from your packet) to
  `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md` under `### New Rules`.
  Keep the table's existing shape (`Rule ID | Category | Severity | Notes`).
- On merge conflicts (multiple sessions appending): resolution is always **keep
  both rows**. The integrator will keep the file sorted by category then ID.

### 2.3 Tests (`tests/Reactor.Tests/AnalyzerTests/`)
- Harness is `Microsoft.CodeAnalysis.CSharp.Testing` used directly:
  `new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> { TestCode = src }.RunAsync(...)`,
  `CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>` for fixes. No shared base.
- Each test file **stubs its own** minimal Reactor-shaped types inline (see
  `PoolResetSetAnalyzerTests.Stubs`) — do not reference the real framework.
- Required per rule (spec §7): **positive**, **negative**, and the **near-miss** that
  almost trips the syntactic fast path. Every ✔ (code-fix) rule adds a **fix
  round-trip** test.
- Run tests with **`dotnet test tests/Reactor.Tests -p:Platform=x64 --filter FullyQualifiedName~<YourClass>`**.
  The project is `Platforms=x64;ARM64` + `WindowsAppSDKSelfContained=true`; if a plain
  `dotnet test` errors with a WindowsAppSDK architecture mismatch (the flight sessions
  consistently hit this in worktrees), the explicit `-p:Platform=x64` reliably avoids it.

### 2.4 Samples sweep (spec §7)
Before merge, run your assembled rule against `samples/`. A rule that fires
**nowhere** is over-fit/over-gated — widen or add a sample. As a manual pre-PR
check, sweep the three high-coverage samples (`ReactorGallery`, `StylingGallery`,
`TodoApp`); the full `samples/` sweep is the fuller net. CI does **not** gate on
these today (it only AOT-publishes the hello-world samples) — wiring a per-PR
sample gate + a nightly full sweep is a recommended follow-up (spec §7), not
current CI.
- **Analyzers are packed-only, not wired into local framework/sample builds**
  (`Reactor.csproj`) — a plain local sample build won't surface your diagnostic. The
  realistic sweep is **grep for the trigger pattern across `samples/` + your unit tests**
  (or `mur check` / a packed nupkg).
- **"Fires nowhere in samples" is acceptable for migration/hygiene rules** (e.g.
  PERSIST_001) whose samples already use the good form — do **not** add a
  deliberately-bad sample just to make the rule fire.

### 2.5 Severity (spec §8)
Ship at the severity in your packet. The five **Info** nudge-class rules
(`HOOKS_002`, `CTX_001`, `OPT_001`, `DSL_002`, `CMD_001`) are deliberate — do not
promote. No core rule ships `Error` (the only `Error` is Batch-2 `WIN2D_001`).

### 2.6 Agent-kit cheat table
The shipped `reactor-build-and-check` skill carries a curated **common-build-errors
cheat table** (`plugins/reactor/skills/reactor-build-and-check/SKILL.md`, "Common
build errors — cheat table") that authors rely on to fix a `REACTOR_*` warning. It is
**curated, not exhaustive** (niche / library-author rules like `POOL_001` are already
omitted). When your rule is an **app-author-facing** diagnostic (the XAML-habit
Warning/Info rules — `HOOKS_010/011/013`, `THEME_004`, `VIS_001`, `OPT_001`,
`CMD_001`, `ITEMS_001`, `CTRL_001`, `EVENT_001`, `THREAD_001/002`, `DSL_002`,
`PERSIST_001`, `CTX_001`, `STATE_001`, plus the Batch-2 author rules), add a one-row
entry (`ID | severity | trigger | fix`) in the same PR. Skip it for
control-author / niche rules (`DESC_001`, `WIN2D_001`) — the analyzer DLL's own
message carries those.

Also append a one-line row to the **analyzer-architecture rules table** — edit the
TEMPLATE `docs/_pipeline/templates/analyzer-architecture.md.dt` (NOT the generated
`docs/guide/analyzer-architecture.md`), so the guide stays complete. Same convention as
the release file and cheat table: each session appends its own row, keep-all-rows on
conflict, the integrator resolves. **Do not touch the already-stale generated
`docs/guide/*.md`** — CI recompiles them from templates; editing them just creates churn.

### 2.7 Review loop — a HARD gate before you report done
Every PR goes through BOTH review tools before you report the packet complete. **Do not
message your creator "done" until this loop is clean** — in the ⭐ wave, 2 of 3 code PRs
shipped real HIGH bugs the review caught, and the two tools are complementary (the
`pr-review` skill caught a bug Copilot missed; they converged on another).
1. Run the `pr-review` skill (skill tool, name `pr-review`) on your branch. Apply every
   Critical/High and every valid Medium/Low finding (verify each against source first).
2. Request a GitHub Copilot review on your PR:
   `gh api --method POST /repos/microsoft/microsoft-ui-reactor/pulls/<PR>/requested_reviewers -f "reviewers[]=copilot-pull-request-reviewer[bot]"`
3. Wait ~90s, read its comments
   (`gh api "/repos/microsoft/microsoft-ui-reactor/pulls/<PR>/comments?per_page=100"`);
   verify each against source — reply on the thread if a comment is wrong, fix + commit +
   push if right.
4. Re-request and repeat until a Copilot review on your **latest HEAD** adds no new
   comments (the repo auto-reviews on push and re-anchors old comments — treat only NEW
   issues on your latest commit as actionable).
5. THEN reply to your creator with the PR link, final HEAD sha, test count, and a one-line
   summary of what each review raised + how you resolved it. **This closing reply is the
   signal the orchestrator waits on — always send it.**

---

## 3. Shared helpers (extract-first)

### 3.1 `.Set`-lambda helper — owned by Wave B
Promote `PoolResetSetAnalyzer.TryGetLambdaAssignment` into a shared
`internal static` helper (suggested `SetLambdaHelpers` in a new file) that returns
the assignment/compound-assignment inside a `.Set(x => x.M = v)` /
`.Set(x => x.E += h)` lambda. `POOL_001` and the four Wave-B rules consume it.
Note the gap the spec calls out (§4.1 ITEMS_001 feasibility note): POOL_001 checks
the **property name only**, never the **receiver element type** — the
collection/selector rules add a curated element-type table on top (a runtime
descriptor registration is invisible to a syntactic analyzer). `VIS_001` is best
done as a **POOL_001 extension** (add `Visibility` to its flow with a `.IsVisible`
fix), per spec §4.4 / §5.

### 3.2 Restricted allocation classifier — owned by Wave C
Extract a restricted, `with`-aware variant of
`HookRulesAnalyzer.ClassifyDepExpression` (`HookRulesAnalyzer.cs:440`) as shared
`internal static`. The raw helper also returns `Unstable` for `TupleExpression`,
lambdas, and anonymous methods, and does **not** handle `WithExpressionSyntax`:
- `HOOKS_013` / `CTX_001` need the **restricted** subset:
  object/array/anonymous-object/collection-creation **only** (exclude tuples &
  lambdas — spec §4.1 HOOKS_013 accuracy note).
- Extend it to recognize `WithExpressionSyntax` so the `value with { … }` cases in
  `CTX_001`/`OPT_001` stop being false negatives (spec §4.1 / §5).
Consumers: `HOOKS_012`, `HOOKS_013`, `CTX_001` (and Batch-2 `ANIM_002`).

---

## 4. Work packets

Fix column: ✔ mechanical code-fix required · ~ template/preview fix · — nudge only.
"Spec §" points at the full write-up. Suggested class names reduce divergence; the
owner may adjust.

### Wave A — independent flight (fan out; each = new file or sole editor of one file)

| ID | Cat | Sev | Fix | Suggested class | Spec § | Notes / grounding |
|---|---|---|---|---|---|---|
| _(id-less)_ | — (CS0618) | — | ✔ | `GridStringTrackCodeFix` | §4.5 | CodeFixProvider on **CS0618** for the obsolete `Grid(string[],string[],…)` (`Dsl.cs:746-750`) → typed `GridSize[]` (`Dsl.cs:733`). No descriptor, no row. Inline literal arrays only. |
| `REACTOR_DESC_001` ⭐ | Reactor.Descriptor | Warning | ✔ | `StaticRegisterLambdaAnalyzer` | §4.7 | All four `ControlRegistry` entry points (`Register`/`RegisterForDerivedTypes`/`RegisterDecorator`/`RegisterDecoratorForDerivedTypes`, `:99,180,214,237`). Fix inserts `static` only when lambda captures nothing. Issue #486. |
| `REACTOR_PERSIST_001` ⭐ | Reactor.Persistence | Warning | ✔ | `UsePersistedScopeAnalyzer` | §4.8 | 2-arg `UsePersisted(key, initial)` → defaults to `Application` (`RenderContext.cs:824`). Fix offers `, PersistedScope.Window` / `.Application`. Arity + symbol check. |
| `REACTOR_STATE_001` | Reactor.State | Warning | — | `ComponentInpcAnalyzer` | §4.1 | `SymbolAction`: type derives `Component` **and** implements `INotifyPropertyChanged`. No fix. |
| `REACTOR_HOOKS_011` | Reactor.Hooks | Warning | ✔ | `ControlledInputAnalyzer` | §4.1 | Standalone (does **not** touch HookRulesAnalyzer.cs). Gate (a): value arg is state-derived (identifier/member); (b): change callback present but empty / never reads its param. Fix `.IsReadOnly(true)` where available (`ElementExtensions.cs:876,1420`), else nudge. Never `.IsEnabled(false)`. |
| `REACTOR_THEME_004` | Reactor.Style | Warning | ✔ | extend `UseThemeRefAnalyzer` | §4.4 | Reuse THEME_001 modifier gate (`.Background`/`.Foreground`/`.WithBorder`); flag inline `new SolidColorBrush(...)`. Fix only for colors in `ColorToThemeToken`; unmapped → diagnostic, no fix. Sole editor of `UseThemeRefAnalyzer.cs`. |
| `REACTOR_OPT_001` | Reactor.Controlled | Info | ✔ | `OptionalSentinelAnalyzer` | §4.2 | Member ∈ `{SelectedIndex, SelectedPageIndex, Date}` assigned sentinel `-1`/`null` in initializer/`with`; one `GetTypeInfo` confirms `Optional<T>`. Two fixes: `Optional<T>.Unset` / `Optional.Of(x)`. **Correction (OPT_001/#788):** spec §4.2 also lists `Time`, but it is **un-triggerable** and was dropped — `TimePickerElement.Time` is a non-nullable `Optional<TimeSpan>` (Element.cs:3844), so the `-1`/`null` sentinels aren't type-compatible. `Date` stays (`CalendarDatePickerElement.Date` is nullable `Optional<DateTimeOffset?>`, Element.cs:3796). |
| `REACTOR_CMD_001` | Reactor.Commanding | Info | ✔ | `RawCommandCallbackAnalyzer` | §4.3 | Raw `new …Element{…}`/`with` setting **both** `Command` and the element's own callback (per-element map: `OnClick` vs `OnIsCheckedChanged`/`OnCheckedStateChanged`) + ctor-positional callback shape. Fix deletes the redundant callback. Do **not** share `CommandDebounceAnalyzer`'s list. |
| `REACTOR_THREAD_002` | Reactor.Threading | Warning | — | `BlockingTaskAnalyzer` | §4.6 | `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on `Task`/`ValueTask` (semantic confirm) lexically inside `Render()` or a `UseEffect` lambda, not inside nested `Task.Run`. No fix. |
| `REACTOR_DSL_002` | Reactor.Dsl | Info | — | extend `MissingWithKeyAnalyzer` | §4.5 | Adds key-**expression** analysis (the analyzer only does a textual `.WithKey(` probe today, `:68`): shape 1 = key resolves only to the `Select`/`ForEach` index param; shape 2 = `Guid.NewGuid()`/`DateTime.Now`/`Random`/`Environment.TickCount`. Must not flag `$"{item.Id}-{i}"`. Sole editor of `MissingWithKeyAnalyzer.cs`. |

Canonical rows (Wave A):
```
REACTOR_DESC_001 | Reactor.Descriptor | Warning | StaticRegisterLambdaAnalyzer - ControlRegistry.Register* lambda should be static (trim hygiene)
REACTOR_PERSIST_001 | Reactor.Persistence | Warning | UsePersistedScopeAnalyzer - 2-arg UsePersisted defaults to Application scope; specify scope
REACTOR_STATE_001 | Reactor.State | Warning | ComponentInpcAnalyzer - INotifyPropertyChanged on a Component is invisible to the render loop
REACTOR_HOOKS_011 | Reactor.Hooks | Warning | ControlledInputAnalyzer - Controlled input has a state-derived value but an empty change callback
REACTOR_THEME_004 | Reactor.Style | Warning | UseThemeRefAnalyzer - Hard-coded Brush/Color object bypasses theme tokens
REACTOR_OPT_001 | Reactor.Controlled | Info | OptionalSentinelAnalyzer - Selection sentinel literal force-asserts instead of Optional<T>.Unset
REACTOR_CMD_001 | Reactor.Commanding | Info | RawCommandCallbackAnalyzer - Raw-init Command + own click callback both set (callback wins; command never runs)
REACTOR_THREAD_002 | Reactor.Threading | Warning | BlockingTaskAnalyzer - Blocking a Task (.Result/.Wait) in Render/effect
REACTOR_DSL_002 | Reactor.Dsl | Info | MissingWithKeyAnalyzer - Non-stable .WithKey (index / Guid.NewGuid / DateTime.Now)
```

### Wave B — `.Set`-family cluster (ONE owner; extract §3.1 first)

| ID | Cat | Sev | Fix | Suggested class | Spec § |
|---|---|---|---|---|---|
| `REACTOR_ITEMS_001` | Reactor.Collections | Warning | — | `SetOwnedItemsSourceAnalyzer` | §4.2 |
| `REACTOR_CTRL_001` | Reactor.Controls | Warning | ✔ | `SetSelectedItemAnalyzer` | §4.2 |
| `REACTOR_VIS_001` | Reactor.Layout | Warning | ✔ | POOL_001 extension | §4.4 |
| `REACTOR_EVENT_001` (reconciled from `LIFECYCLE_001`) | Reactor.Events | Warning | ✔ | `SetEventSubscriptionAnalyzer` | §4.6 |

> **Integration reconciliation (2026-07-06):** `main` independently shipped PR #763 as
> `REACTOR_EVENT_001` (`Reactor.Events`) — the same `SetEventSubscriptionAnalyzer`. Wave B's
> `REACTOR_LIFECYCLE_001` was folded into it: the shipped `EVENT_001` id/category is retained,
> and LIFECYCLE_001's broad semantic detection (any event on a `FrameworkElement` via Reactor
> `.Set`, `+=`/`-=`) + its `.OnMountAdd`/`.OnUnmountAdd` fix were merged in alongside EVENT_001's
> declarative-modifier fix (offered when the event has a declarative `.On*` modifier).

Curated element-type tables (spec): ITEMS_001 = `{ListView,GridView,TreeView,TabView,Pivot,FlipView,SelectorBar}Element`, member `ItemsSource`, **exclude** `AutoSuggestBoxElement`. CTRL_001 = `{ComboBox,RadioButtons,ListView,GridView}Element`, WinUI member `SelectedItem`/`SelectedValue`, **and** the element also sets `SelectedIndex` (NavigationView dropped — it's `SelectedTag`). EVENT_001 (reconciled from LIFECYCLE_001) = compound-assignment whose LHS is an **event** symbol (mandatory check) on a `FrameworkElement`-derived receiver; fix `.OnMount(c => ((TControl)c).E += h).OnUnmount(… -= h)` only when `h` is a stable delegate, casting via the `.Set` overload's concrete `TControl`.

Canonical rows (Wave B):
```
REACTOR_ITEMS_001 | Reactor.Collections | Warning | SetOwnedItemsSourceAnalyzer - .Set(ItemsSource=...) on a framework-owned collection
REACTOR_CTRL_001 | Reactor.Controls | Warning | SetSelectedItemAnalyzer - .Set(SelectedItem/SelectedValue) fights controlled SelectedIndex
REACTOR_VIS_001 | Reactor.Layout | Warning | PoolResetSetAnalyzer - Imperative .Set(Visibility=...) instead of .IsVisible(...)
REACTOR_EVENT_001 | Reactor.Events | Warning | SetEventSubscriptionAnalyzer - Event wired via .Set(+=/-=) re-subscribes every render; use a declarative On* modifier or .OnMountAdd/.OnUnmountAdd  (reconciled from LIFECYCLE_001)
```

### Wave C — Hooks/allocation cluster (ONE owner; extract §3.2 first)

All of HOOKS_002/003/010/012/013 reuse `HookRulesAnalyzer` internals
(`IsLikelyReactorHook`, HOOKS_008 setter-symbol tracking, `LooksLikeHook`
name-gate, the classifier) — one owner to serialize edits to that 906-line file.
CTX_001 is a separate analyzer file consuming the extracted classifier.

| ID | Cat | Sev | Fix | Where | Spec § |
|---|---|---|---|---|---|
| `REACTOR_HOOKS_002` | Reactor.Hooks | Info | — | `HookRulesAnalyzer.cs` (fill comment-reserved slot `:42-43`) | §4.1 |
| `REACTOR_HOOKS_003` | Reactor.Hooks | Warning | ~ | `HookRulesAnalyzer.cs` (fill slot) | §4.1 |
| `REACTOR_HOOKS_010` | Reactor.Hooks | Warning | ✔ | `HookRulesAnalyzer.cs` (reuse HOOKS_008 tracking) | §4.1 |
| `REACTOR_HOOKS_012` | Reactor.Hooks | Warning | — | `HookRulesAnalyzer.cs` (dedicated `Memo` path; exclude keyed `Memo<TKey>`) | §4.1 |
| `REACTOR_HOOKS_013` | Reactor.Hooks | Warning | ✔ | `HookRulesAnalyzer.cs` (per-overload initial-value arg) | §4.1 |
| `REACTOR_CTX_001` | Reactor.Context | Info | ✔ | new `ContextProvideAnalyzer` | §4.1 |

> `HOOKS_003` fix is `~`, not `✔`: the spec §4 **summary** table marks it ✔, but
> §4.1 (and the §10 round-2 relabel) specify a **template/preview** fix that hoists
> the async body into a `Task`-returning local — not a silent one-click rewrite.

Key gotchas (from spec): HOOKS_010 fix is `setItems([.. items, v])` (setter is
`Action<T>`, **not** a functional updater); restrict to mutable/non-`IEquatable`
types. HOOKS_012 message-only (no HOOKS_004 fixer exists); disambiguate the two
`Memo` overloads. HOOKS_013 restricted classifier + correct arg index
(`UseState` arg0 / `UsePersisted` arg1); fix `UseMemo(() => new…(), [])`, **not**
`UseRef`. CTX_001 value-equality check is mandatory (record/struct/`IEquatable`/
`Equals`-override → do not fire); `Provide` is `ContextExtensions.Provide` (`:11`).

Canonical rows (Wave C):
```
REACTOR_HOOKS_002 | Reactor.Hooks | Info | HookRulesAnalyzer - Hook after an early-return guard
REACTOR_HOOKS_003 | Reactor.Hooks | Warning | HookRulesAnalyzer - async-void UseEffect body
REACTOR_HOOKS_010 | Reactor.Hooks | Warning | HookRulesAnalyzer - Mutate-then-set reference state (same ref re-passed to setter)
REACTOR_HOOKS_012 | Reactor.Hooks | Warning | HookRulesAnalyzer - Memo dependency lacks value equality
REACTOR_HOOKS_013 | Reactor.Hooks | Warning | HookRulesAnalyzer - UseState/UsePersisted initial value allocated every render
REACTOR_CTX_001 | Reactor.Context | Info | ContextProvideAnalyzer - Context value re-allocated each render (reference-equality type)
```

### Wave D — THREAD_001 + framework attribute (ONE session)

`REACTOR_THREAD_001` · Reactor.Threading · Warning · ✔ · `UIThreadAffinityAnalyzer` · spec §4.6.
Depends on the framework change in §5 (same PR). Canonical row:
```
REACTOR_THREAD_001 | Reactor.Threading | Warning | UIThreadAffinityAnalyzer - UI-thread-only mutator called on a background thread
```

### Wave E — Batch 2 (15; second flight, **verify against source first**)

Batch 2 is endorsed but not source-hardened (spec §12) — **each packet starts with
a verification pass** against the cited anchors before implementing. IDs are
reserved here; categories per spec §12 (short `Reactor.*`, except `A11Y_004`).
Reuse the Wave C classifier for `REACTOR_ANIM_002`. Doc-named (build to make docs
true): `REACTOR_INPUT_001`, `REACTOR_PERF_FUNCREF`, `REACTOR_GRID_001`
(unused-column), `REACTOR_A11Y_004`.

`REACTOR_INPUT_001`⭐, `REACTOR_PERF_FUNCREF`⭐, `REACTOR_GRID_001`⭐ (Reactor.Layout,
unused-column — distinct from the id-less §4.5 fixer), `REACTOR_A11Y_004`⭐
(Microsoft.UI.Reactor.Accessibility), `REACTOR_DIALOG_001`, `REACTOR_NAV_001`,
`REACTOR_ANIM_002`, `REACTOR_LIFECYCLE_002`, `REACTOR_INPUT_002`, `REACTOR_MOD_001`,
`REACTOR_ANIM_003`, `REACTOR_DSL_003`, `REACTOR_MEDIA_001`, `REACTOR_MEMO_001`,
`REACTOR_WIN2D_001` (Error). See spec §12 table + terse entries for
detect/grounding/vote.

---

## 5. `[UIThreadOnly]` attribute spec (Wave D)

THREAD_001 cannot body-probe framework methods (they are metadata-only in a
consumer compilation — `DeclaringSyntaxReferences` empty, no IL access; spec §4.6
round-2 note). Mechanism = a marker attribute the analyzer keys off.

1. **Define** `Microsoft.UI.Reactor.Hosting.UIThreadOnlyAttribute` (internal
   marker is insufficient — the analyzer reads it from **metadata**, so it must be
   `public`). `[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]`,
   sealed, no members. AOT/trim safe (plain attribute). No `PublicAPI.txt` tracking
   exists in this repo, so no surface-tracking file to update.
2. **Annotate** every member that calls `ThreadAffinity.ThrowIfNotOnUIThread(...)`.
   Current call sites (grep `ThreadAffinity.ThrowIfNotOnUIThread` scoped to
   `src/Reactor/Hosting/` — a bare `ThrowIfNotOnUIThread` also matches the VS
   extension's unrelated `ThreadHelper.ThrowIfNotOnUIThread`): `ReactorWindow.cs` (~20:
   `Activate/Hide/Show/Close/SavePlacement/Update/SetSize/SetPosition/SetAspectRatio/
   BeginDragMove/RegisterAspectRatioOverride/SetOpacity/SetNoActivate/
   SetIgnorePointerInput/CenterOnScreen/SetThumbnailToolbar/ClearThumbnailToolbar/
   Mount…`), plus `ReactorApp.cs`, and `Hosting/Shell/{JumpList,ReactorTrayIcon,
   TaskbarProgress,TaskbarOverlay,TaskbarItem}.cs`. Enumerate from the live
   `ThreadAffinity.ThrowIfNotOnUIThread` grep (scoped to `src/Reactor/`) at
   implementation time — do not hard-code this list into the analyzer.
3. **Analyzer** gate: invocation lexically inside a `Task.Run`/`Task.Factory.StartNew`/
   `ThreadPool.QueueUserWorkItem` lambda, not already inside `TryEnqueue`; confirm the
   callee carries `[UIThreadOnly]`. Fallback if the attribute slips: a generated
   symbol/FQN allowlist (not a hand-typed list — stale on day one).
4. **Fix** (null-safe): `var d = ReactorApp.UIDispatcher; if (d is null) <call>; else d.TryEnqueue(() => <call>);`
   — `UIDispatcher` is `DispatcherQueue?`, null pre-bootstrap (`ReactorApp.cs:89`), so
   no null-forgiving `!`.

---

## 6. Orchestration / branching

- Foundation (this doc) lands first. The flight branches so it can **see this doc**:
  either (a) merge foundation to `main`, then spawn Wave A/B/C/D from `main`; or
  (b) stack the flight on the foundation branch. Decide with the human at spawn time.
- Wave A = one session per packet (independent PRs, any merge order). Waves B/C/D =
  one session each. Wave E = a second flight after Batch-1 patterns settle.
- Integrator (this session) resolves the trivial `Unshipped.md` append conflicts and
  keeps the tracking table below current.

---

## 7. Tracking table

Status: ☐ not started · ◐ in progress · ☑ merged.

| Wave | ID | Owner/session | Status |
|---|---|---|---|
| A | Grid CS0618 fixer | — | ☐ |
| A | REACTOR_DESC_001 | — | ☐ |
| A | REACTOR_PERSIST_001 | — | ☐ |
| A | REACTOR_STATE_001 | — | ☐ |
| A | REACTOR_HOOKS_011 | — | ☐ |
| A | REACTOR_THEME_004 | — | ☐ |
| A | REACTOR_OPT_001 | — | ☐ |
| A | REACTOR_CMD_001 | — | ☐ |
| A | REACTOR_THREAD_002 | — | ☐ |
| A | REACTOR_DSL_002 | — | ☐ |
| B | REACTOR_ITEMS_001 | — | ☐ |
| B | REACTOR_CTRL_001 | — | ☐ |
| B | REACTOR_VIS_001 | — | ☐ |
| B | REACTOR_LIFECYCLE_001 → EVENT_001 (reconciled) | — | ☑ |
| C | REACTOR_HOOKS_002 | — | ☐ |
| C | REACTOR_HOOKS_003 | — | ☐ |
| C | REACTOR_HOOKS_010 | — | ☐ |
| C | REACTOR_HOOKS_012 | — | ☐ |
| C | REACTOR_HOOKS_013 | — | ☐ |
| C | REACTOR_CTX_001 | — | ☐ |
| D | REACTOR_THREAD_001 (+ `[UIThreadOnly]`) | — | ☐ |
| E | Batch 2 ×15 | — | ☐ |
