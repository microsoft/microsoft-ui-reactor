# Analyzer Suite Expansion — XAML-Developer Guardrails

## Status

**Proposed — 2026-07-01.** Design only; no analyzers implemented yet. This spec
extends the shipping Roslyn suite documented in
[Analyzer Architecture](../guide/analyzer-architecture.md) and
[Rules of Reactor](../guide/rules-of-reactor.md). It was produced by a research
pass over `docs/guide` + `docs/specs` cross-checked against framework and
analyzer source, focused on one question: **what mistakes does a developer
arriving from XAML/WinUI make in Reactor that the type system can't catch, and
which of those are cheap for an analyzer to catch reliably?**

Three of the **§4 core** rules below are already *called for by name* in the
codebase (a source comment, an `[Obsolete]` overload, and a tracked issue) but
have never been built — those are the lowest-risk to land first. The rest were
selected for high XAML-habit frequency, a silent/loud failure that's hard to
diagnose at runtime, and a low false-positive detection shape. (The later
blind-spot pass surfaces four *more* doc-named-but-unbuilt rules in
[§12](#12-batch-2--cross-model-blind-spot-additions) — so "three" scopes to the
core catalog, not the whole document.)

> **Revision — 2026-07-01 (post-review).** This draft was reviewed by five models
> (Claude Opus 4.8 / Opus 4.7, GPT‑5.5, GPT‑5.3‑Codex, Gemini 3.1 Pro), each at
> its maximum reasoning effort, each verifying the load-bearing claims against
> source. Their findings drove concrete changes, recorded inline and summarized
> in [§10](#10-review-changelog). The headline corrections: the earlier
> `CTX_001` "records compare unequal" premise was **wrong** (context diffs use
> `Equals`), so it is reframed and downgraded; a helper the spec named
> `IsFreshlyAllocated` does not exist (it is `ClassifyDepExpression`); several
> proposed code-fixes were invalid C# or unsafe and were corrected or dropped;
> `ANIM_001` was **cut** (no reliable detection) and `REF_002` **deferred**;
> two safer rules (`VIS_001`, `THREAD_002`) took their place; and the misplaced
> fourth ⭐ was removed.
>
> A **second review round** (same five models, max effort) then verified those
> fixes against source — all held — and drove a final polish pass on two
> detection/fix mechanics (`THREAD_001` cross-assembly detection, `LIFECYCLE_001`
> receiver cast) plus code-fix and consistency corrections, recorded in
> [§10](#10-review-changelog).
>
> A **third round** shifted from correctness to coverage: the five models
> brainstormed missing analyzers and cross-vetted the pooled set, yielding the
> **Batch 2** catalogue ([§12](#12-batch-2--cross-model-blind-spot-additions)) —
> 15 endorsed additions (4 already doc-named), with premise-wrong and duplicate
> ideas filtered out.

---

## Table of contents

- [§1 Motivation](#1-motivation)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 Design constraints (inherited)](#3-design-constraints-inherited)
- [§4 The catalog — 21 analyzers](#4-the-catalog--21-analyzers)
  - [§4.1 Hooks, state & reactivity](#41-hooks-state--reactivity)
  - [§4.2 Controlled properties & controls](#42-controlled-properties--controls)
  - [§4.3 Commanding](#43-commanding)
  - [§4.4 Theming & visibility](#44-theming--visibility)
  - [§4.5 DSL & list keys](#45-dsl--list-keys)
  - [§4.6 Threading, effects & lifecycle](#46-threading-effects--lifecycle)
  - [§4.7 Descriptor authoring](#47-descriptor-authoring)
  - [§4.8 Persistence](#48-persistence)
- [§5 Cross-cutting notes](#5-cross-cutting-notes)
- [§6 Release tracking & categories](#6-release-tracking--categories)
- [§7 Testing strategy](#7-testing-strategy)
- [§8 Rollout & severity defaults](#8-rollout--severity-defaults)
- [§9 Considered but not included](#9-considered-but-not-included)
- [§10 Review changelog](#10-review-changelog)
- [§11 Open questions](#11-open-questions)
- [§12 Batch 2 — cross-model blind-spot additions](#12-batch-2--cross-model-blind-spot-additions)

---

## §1 Motivation

Reactor renders the same WinUI controls a XAML page does, but relocates the
source of truth from dependency-property bindings to hook state and a
reconciler (see [Reactor vs XAML](../guide/reactor-vs-xaml.md)). Most XAML→Reactor
bugs are therefore *habit* bugs: the code compiles, looks right to a reviewer
who knows WinUI, and fails silently at runtime because the mental model shifted.
The shipping suite already covers the loudest of these (conditional hooks,
missing keys, hard-coded colors). This spec fills the next tier: the silent
"why won't my slider move / why did the command not run / why did selection snap
back" class that today only surfaces in manual testing.

Every candidate is grounded in a documented "Common Mistake," a source-level
caveat, or an explicitly deferred analyzer. The full research trail (44 raw
candidates, including a set deferred in [§9](#9-considered-but-not-included))
informed the selection.

## §2 Goals / non-goals

**Goals**

- Catch high-frequency XAML-habit mistakes at build time with a red squiggle.
- Ship a paired `CodeFixProvider` wherever the fix is mechanical and unambiguous.
- Keep every rule's false-positive rate near the suite's current ~zero bar, so
  teams can safely promote the category to error in CI.

**Non-goals**

- Whole-program dataflow or CFG analysis. Rules that need it are scoped to a
  single method body / single fluent chain, or dropped. (This constraint is why
  `THREAD_001`'s speculative "after `ConfigureAwait(false)`" arm was removed in
  review — see [§10](#10-review-changelog).)
- Heuristic "smell" rules (deep nesting, "expensive" LINQ, over-memoization).
  Those were evaluated and deferred as too false-positive-prone for a default-on
  analyzer.
- Style/formatting opinions. The suite catches correctness/perf/a11y, not taste.

## §3 Design constraints (inherited)

From [Analyzer Architecture](../guide/analyzer-architecture.md):

- **Syntactic gate first.** Match `SyntaxKind.InvocationExpression` (factories are
  `IdentifierNameSyntax`, modifiers are `MemberAccessExpressionSyntax`) or a
  `SymbolAction` for per-symbol questions. Only call
  `SemanticModel.GetSymbolInfo` after a cheap name/shape check has already
  filtered the node down.
- **Code-fix handoff via `Diagnostic.Properties`.** Anything the fix needs beyond
  the diagnostic `Location` goes in the property bag, never the message text.
- **Release tracking.** Every **diagnostic** rule gets a descriptor id on the
  `REACTOR_<CATEGORY>_<NNN>` convention and an entry in
  `AnalyzerReleases.Unshipped.md`. Two documented exceptions: the string-track
  `Grid` fixer (§4.5) is a `CodeFixProvider` registered on `CS0618` with **no**
  descriptor and no release row; and one id carried over verbatim from existing
  docs (`REACTOR_PERF_FUNCREF`, §12) predates the `<NNN>` numbering.
- **Validate against `samples/`.** A new rule that finds nothing across the sample
  apps is over-fit or gated too narrowly.

> **ID/category convention is loose.** The `REACTOR_<CATEGORY>_<NNN>` id does not
> strictly track the analyzer *category* string. This is pre-existing:
> `REACTOR_THEME_001` lives in category `Reactor.Style`, and the shipped a11y
> rules use `Microsoft.UI.Reactor.Accessibility`, not `Reactor.A11y`. The §4
> table therefore surfaces the category column explicitly rather than implying
> it from the id. See [§6](#6-release-tracking--categories).

Each entry below notes: **XAML habit → failure**, **Detect** (gate → confirm),
**Fix**, **FP risk**, and **Grounding** (source/doc anchor, verified in review).

---

## §4 The catalog — 21 analyzers

> A later multi-model blind-spot pass added a **Batch 2** of 15 further endorsed
> analyzers (4 of them doc-named) — see [§12](#12-batch-2--cross-model-blind-spot-additions).
> This section remains the source-hardened core.

Summary (⭐ = already called for by name in the codebase — exactly three;
✔ = ships a mechanical code fix):

| Id | Category | Catches | Sev | Fix |
|---|---|---|---|---|
| `REACTOR_STATE_001` | Reactor.State | `INotifyPropertyChanged` implemented on a `Component` | Warning | — |
| `REACTOR_HOOKS_010` | Reactor.Hooks | Reference state mutated in place, same ref re-passed to setter | Warning | ✔ |
| `REACTOR_HOOKS_011` | Reactor.Hooks | Controlled input with `onChanged` present but empty (fake `Mode=OneWay`) | Warning | ✔ |
| `REACTOR_HOOKS_002` | Reactor.Hooks | Hook after an early-`return` guard (comment-reserved slot) | Info | — |
| `REACTOR_HOOKS_003` | Reactor.Hooks | `async`-void `UseEffect` body (comment-reserved slot) | Warning | ✔ |
| `REACTOR_HOOKS_012` | Reactor.Hooks | `Memo(builder, params deps)` given deps that lack value equality | Warning | — |
| `REACTOR_HOOKS_013` | Reactor.Hooks | `UseState`/`UsePersisted` initial value allocated every render | Warning | ✔ |
| `REACTOR_CTX_001` | Reactor.Context | Context value provided as a fresh **reference-equality** value each render | Info | ✔ |
| `REACTOR_OPT_001` | Reactor.Controlled | `Optional<T>` selection sentinel literal force-asserts instead of "unset" | Info | ✔ |
| `REACTOR_ITEMS_001` | Reactor.Collections | `.Set(ItemsSource = …)` on a framework-owned collection | Warning | — |
| `REACTOR_CTRL_001` | Reactor.Controls | `.Set(SelectedItem/SelectedValue = …)` fighting `SelectedIndex` | Warning | ✔ |
| `REACTOR_CMD_001` | Reactor.Commanding | Raw-init `Command` + `OnClick` both set (command silently dropped) | Info | ✔ |
| `REACTOR_THEME_004` | Reactor.Style | Hard-coded `Brush`/`Color` object escapes `THEME_001` | Warning | ✔ |
| `REACTOR_VIS_001` | Reactor.Layout | Imperative `.Set(c => c.Visibility = …)` instead of `.IsVisible(...)` | Warning | ✔ |
| _(id-less — CS0618 code-fix)_ ⭐ | — | String-typed `Grid` track literals → typed `GridSize[]` | — (CS0618) | ✔ |
| `REACTOR_DSL_002` | Reactor.Dsl | Non-stable `.WithKey(...)` (index / `Guid.NewGuid()` / `DateTime.Now`) | Info | — |
| `REACTOR_THREAD_001` | Reactor.Threading | UI-thread-only mutator called inside a background-thread lambda | Warning | ✔ |
| `REACTOR_THREAD_002` | Reactor.Threading | Blocking a `Task` (`.Result`/`.Wait()`) in `Render()`/effect | Warning | — |
| `REACTOR_LIFECYCLE_001` | Reactor.Lifecycle | Event subscription via `.Set(fe => fe.Event += h)` | Warning | ✔ |
| `REACTOR_DESC_001` ⭐ | Reactor.Descriptor | Non-`static` lambda passed to a `ControlRegistry.Register*` | Warning | ✔ |
| `REACTOR_PERSIST_001` ⭐ | Reactor.Persistence | `UsePersisted(key, initial)` silently defaults to `Application` scope | Warning | ✔ |

### §4.1 Hooks, state & reactivity

#### `REACTOR_STATE_001` — INPC on a Component *(Reactor.State · Warning · no fix)*

**XAML habit → failure.** A `Component` subclass implements
`INotifyPropertyChanged` and raises `PropertyChanged` for local state, exactly
as an MVVM view-model would. The framework never subscribes to a component's
INPC, so the field is invisible to the render loop and updates do nothing.

**Detect.** `SymbolAction` on named types: the type derives from Reactor's
`Component` base **and** implements `System.ComponentModel.INotifyPropertyChanged`.

**Fix.** None auto (structural) — message points to `UseState` / `UseObservable`.

**FP risk.** Near zero (symbol-level, two-condition match).

**Grounding.** [reactor-vs-xaml.md §Common Mistakes](../guide/reactor-vs-xaml.md)
("Implementing INPC on local state out of habit"), §MVVM.

#### `REACTOR_HOOKS_010` — Mutate-then-set reference state *(Reactor.Hooks · Warning · fix)*

**XAML habit → failure.** `ObservableCollection` reflexes: `items.Add(x);
setItems(items);`. The `UseState` setter compares the new value to the old with
`EqualityComparer<T>.Default` and returns early on equality
(`RenderContext.cs:175,187`); the mutated-in-place list is the *same reference*,
so no re-render is scheduled. No exception.

> **Accuracy note (review):** `EqualityComparer<T>.Default` is **value** equality
> when `T : IEquatable<T>` (records, tuples, primitives). The silent-miss bug
> therefore hits reference types *without* value equality — `List<T>`,
> `T[]`, `Dictionary<,>`, a plain mutable class — not records. The rule must
> restrict to those types (see FP risk).

**Detect.** Syntactic: a void-returning mutator (`.Add/.Remove/.Insert/.RemoveAt/
.Clear/.Sort/.AddRange`, or indexer set) on identifier `x`, followed in the same
block by `setX(x)` with the *same* symbol and no intervening reassignment.
Confirm `x`/`setX` came from a `UseState`/`UsePersisted` deconstruction (reuse
the setter-symbol tracking from `HOOKS_008`), and that `x`'s type is a mutable
collection / a reference type not implementing `IEquatable<T>`.

**Fix.** For the common `.Add(v)` shape on a `UseState`: rewrite to
`setItems([.. items, v])` (a **new** value). Do **not** emit a functional updater
`setItems(prev => …)` — the `UseState` setter is `Action<T>`
(`RenderContext.cs:122`) and takes a value, not a transform. The functional form
belongs only to `UseReducer<T>` (`Action<Func<T,T>>`, `RenderContext.cs:208`),
which this rule does not target.

**FP risk.** Medium — single-block only; skip if the list was defensively copied
earlier in the block; the value-equality type filter removes the record case.

**Grounding.** [reactivity-model.md §Common Mistakes](../guide/reactivity-model.md);
[collections.md](../guide/collections.md); `RenderContext.cs:122,175,187`.

#### `REACTOR_HOOKS_011` — Controlled input not wired *(Reactor.Hooks · Warning · fix)*

**XAML habit → failure.** `TextBox(name, _ => { })` — the `Mode=OneWay` / "I'll
read it back via `x:Name`" mental model. The control renders the value but
silently drops edits. The docs note explicitly that no analyzer emits this today.

**Detect.** Syntactic: a controlled-input factory (`TextBox`, `PasswordBox`,
`NumberBox`, `Slider`, `ComboBox`, `CheckBox`, `ToggleSwitch`, `RatingControl`,
`CalendarDatePicker`, …) where **(a)** the first (value) argument is a non-trivial
expression that reads state (an identifier / member access — signalling the
author intends a live value), **and (b)** the change-callback argument is present
but is an empty-block lambda or a lambda that never references its parameter.
Confirm the callee resolves to the Reactor DSL factory.

> **Gating note (review):** do **not** fire merely because the callback is
> *omitted* — several factories make both the value and the callback optional
> (`Dsl.cs:277-281`), and a bare `TextBox("static label")` is a legitimate
> read-only display. Requiring an explicit state-derived value argument (a) keeps
> the rule to the "I wired a value but forgot the setter" case.

**Fix.** Control-specific, offered via `Diagnostic.Properties`: for `TextBox` /
`RatingControl`, insert `.IsReadOnly(true)` (both have that modifier —
`ElementExtensions.cs:876,1420`) to make read-only intent explicit; for controls
with no `IsReadOnly`, emit **no** auto-fix (nudge only). Never emit
`.IsEnabled(false)` — that disables and de-focuses the control, a different,
destructive behavior than the intended read-only display.

**FP risk.** Low with gate (a) in place.

**Grounding.** [xaml-developers.md](../guide/xaml-developers.md) (caveat: no analyzer
today); [forms.md §Controlled-Input Pattern](../guide/forms.md); `Dsl.cs:277-281`;
`ElementExtensions.cs:876,1420`.

#### `REACTOR_HOOKS_002` — Hook after an early-return guard *(Reactor.Hooks · Info · no fix)*

**XAML habit → failure.** Guard-clause reflex:
`var (a,_) = UseState(0); if (a < 0) return Invalid(); var (b,_) = UseState("");`.
Hook `b` runs only on renders that pass the guard, shifting the slot table —
the same corruption `HOOKS_001` guards, but the current ancestor-only walk never
sees it because the hook isn't lexically inside an `if`.

**Detect.** Syntactic: inside a `Render()`/`Use*` body, an `if` with no `else`
whose body is only a `return`, followed later in the *same block* by another
`Use*` invocation. Confirm via the existing `IsLikelyReactorHook`.

**Fix.** None (auto-hoisting risks reordering data dependencies).

**FP risk.** None for the matched shape; the value concern is *false negatives*.

> **Coverage bound (review):** this fires only on the single-guard, single-block
> shape. `if (…) throw`, multiple stacked guards, `switch`-arm returns, and
> nested blocks are **not** covered and are not promised as a follow-up. Because
> coverage is narrow and a miss here is a latent crash the author would want
> flagged loudly if caught, the rule ships at **Info** to avoid implying
> completeness — not because a true positive is low-stakes.

**Grounding.** This is the comment-reserved `REACTOR_HOOKS_002` slot —
`HookRulesAnalyzer.cs:42-43` header comment (*"REACTOR_HOOKS_002 and 003 require
control-flow / data-flow analyses"*). No `DiagnosticDescriptor` is reserved yet;
the slot is a comment, not a declared descriptor. [rules-of-reactor.md §1](../guide/rules-of-reactor.md).

#### `REACTOR_HOOKS_003` — async-void UseEffect body *(Reactor.Hooks · Warning · fix)*

**XAML habit → failure.** `UseEffect(async () => { await Fetch(); setData(…); })`.
`UseEffect` only accepts `Action` / `Func<Action>` (`RenderContext.cs:363,379`) —
an `async` lambda compiles as `async void`. Exceptions escape the flush
pipeline, cleanup ordering decouples from the `await`, and the setter can fire
after unmount.

**Detect.** Syntactic: `UseEffect` whose first-argument lambda carries the
`async` modifier. Confirm the target resolves to `RenderContext.UseEffect`.
(There is **no** `UseLayoutEffect` API — do not reference one.)

**Fix.** A **template/refactoring** (not a silent one-click): extract the original
`async` body into a local `async Task RunAsync(CancellationToken)` and rewrite to
`UseEffect(() => { var cts = new CancellationTokenSource(); _ = RunAsync(cts.Token);
return () => cts.Cancel(); }, deps)`. Because it hoists the lambda body, it is
offered as a preview.

**FP risk.** Near zero — there is no valid `async` `UseEffect` body (no
`Func<Task>` overload exists; `RenderContext.cs:363-482`).

**Grounding.** Comment-reserved `REACTOR_HOOKS_003` slot;
[effects-scheduling.md](../guide/effects-scheduling.md) ("Don't await directly in the
lambda"); `RenderContext.cs:363-482`.

#### `REACTOR_HOOKS_012` — Memo deps that lack value equality *(Reactor.Hooks · Warning · no fix)*

**XAML habit → failure.** `Memo(ctx => ExpensiveChart(data), someArray)` where
`someArray` is a freshly-allocated array / mutable collection / non-record class.
`Memo` is invisible to `HOOKS_004` today: the hook fast-path
`LooksLikeHook` requires the name to `StartsWith("Use")`
(`HookRulesAnalyzer.cs:213-214`), and `Memo` doesn't. So the subtree re-renders
every frame, plus memo bookkeeping overhead.

> **Accuracy note (review):** `MemoElement` deps are compared with `Equals`, not
> reference identity (`Reconciler.cs:2630`, `DepsEqual`), so `new[] { filter, sort }` passed to the
> `params object?[]` slot compares element-wise **by value** and is *fine* when
> the elements have value equality. The real miss is a single dep whose type
> lacks value equality (an array, a `List<T>`, a plain class). Reword accordingly
> — this is not "fresh deps never match."

**Detect.** Register a dedicated path for `Memo`. Two overloads share the name and
**must be disambiguated**: `Memo(Func<RenderContext,Element>, params object?[]
dependencies)` (`Dsl.cs:1239`) is the target; `Memo<TKey>(TKey key, Func<Element>
factory)` (`Dsl.cs:1276`) is a keyed cache **designed** to take freshly-allocated
tuple/record keys and must be **excluded**. Gate by resolving the symbol and
requiring the `params object?[] dependencies` parameter. Then reuse the existing
classifier (below) on each dep, restricted to reference types without value
equality.

**Fix.** None automatic. `HOOKS_004` itself ships **without** a `CodeFixProvider`
(there is no `HOOKS_004` fixer under `src/Reactor.Analyzers/`), and the correct
rewrite — hoisting the unstable dep into a stable `UseMemo`/local — depends on
where the value should live. Message-only nudge, mirroring `HOOKS_004`.

**FP risk.** Low once the keyed overload is excluded and value-equality types are
skipped.

**Grounding.** `HookRulesAnalyzer.cs:213-214`; `Dsl.cs:1239,1276`;
[collections.md](../guide/collections.md) (warns not to confuse the two `Memo`s);
[advanced.md](../guide/advanced.md).

#### `REACTOR_HOOKS_013` — Eager-allocated initial value *(Reactor.Hooks · Warning · fix)*

**XAML habit → failure.** `UseState(new List<string>())`. Developers expect "pass
the initial value" to run once (like a field initializer); C# re-evaluates the
argument on *every* render, but `UseState` only consults it on the first. Steady
allocation that scales with render frequency, not state changes. The framework's
own internal docs flag this.

**Detect.** Bind by **parameter**, not position: the allocating expression must be
the initial-value argument, which is arg 0 for `UseState<T>(T initial, …)`
(`RenderContext.cs:122`) but arg 1 for `UsePersisted<T>(string key, T initial,
…)` (`RenderContext.cs:824`). Target these two overloads only; reducer overloads
have a different shape and are out of scope. Use a **restricted** classifier —
object/array/anonymous/collection creation **only**.

> **Accuracy note (review):** the shared `ClassifyDepExpression` helper
> (`HookRulesAnalyzer.cs:440`) also returns `Unstable` for `TupleExpression`,
> lambdas, and anonymous methods. Reusing it verbatim here would false-fire on
> `UseState((0, ""))` (a stack `ValueTuple`, not a heap allocation) and on a
> deliberately-stored `Func<>` initial value. HOOKS_013 needs the restricted
> subset, not the raw helper.

**Fix.** Wrap in `UseMemo(() => new …(), [])`. Do **not** offer `UseRef<T>(new())`
as an alternative — `UseRef<T>(T initialValue = default!)` (`RenderContext.cs:791`)
stores an already-evaluated argument, so it re-allocates every render exactly like
the bug being fixed; a genuine lazy form must be spelled `var r =
UseRef<List<T>?>(null); r.Current ??= new();`.

**FP risk.** Low with the restricted classifier and correct arg position.

**Grounding.** [hooks-internals.md](../guide/hooks-internals.md) (explicit
"would defeat the point" caveat); `RenderContext.cs:122,824`;
`HookRulesAnalyzer.cs:440`.

#### `REACTOR_CTX_001` — Context value re-allocated each render *(Reactor.Context · Info · fix)*

**XAML habit → failure.** `.Provide(Ctx.Theme, new ThemeConfig(...))` inline in
`Render()`. If the provided value is a **reference type without value equality**,
a new instance every render compares unequal, so every `UseContext` consumer in
the subtree re-renders. The docs call this the "classic failure mode" of a theme
provider.

> **Accuracy correction (review — this was a blocking error in the first draft):**
> context values are diffed with `ContextValuesEqual`, which compares each value
> via `Equals` (`Element.cs:1358`), **not** reference identity. A freshly
> allocated `record`/`struct` (the shape the [context.md](../guide/context.md)
> example itself uses) with unchanged fields compares **equal** and does **not**
> thrash consumers. The rule must therefore fire only for reference types that
> use reference equality (plain classes without an `Equals` override, arrays,
> mutable collections) — and it is an allocation/perf *nudge*, hence **Info**, not
> a Warning.

**Detect.** Syntactic: a `.Provide(ctx, value)` call where `value` is an
object/array/collection creation. `Provide` is the extension method
`ContextExtensions.Provide<T,TValue>(this T element, Context<TValue> context,
TValue value)` (`ContextExtensions.cs:11`), not a method on `Context<T>` — resolve
accordingly. Then a semantic check that `TValue` is a reference type **without**
value equality — not a `record`, not a struct, no `IEquatable<TValue>`, **and** no
user override of `Equals(object)` (a plain class overriding `Equals` has value
semantics and must not fire). Note: the
shared classifier does not yet recognize `WithExpressionSyntax`, so `value with
{ … }` is a known false negative unless the classifier is extended.

**Fix.** Wrap in `UseMemo(() => new …(), deps)` (deps left as a `// TODO` when not
inferable).

**FP risk.** Low **only** with the value-equality type check; without it the rule
is wrong (see correction). That check is mandatory, not optional.

**Grounding.** `Element.cs:1358` (`ContextValuesEqual` → `Equals`);
[context.md](../guide/context.md); `ContextExtensions.cs:11`.

### §4.2 Controlled properties & controls

#### `REACTOR_OPT_001` — Optional<T> selection sentinel force-asserts *(Reactor.Controlled · Info · fix)*

**XAML habit → failure.** WinUI's "`-1` means nothing selected." Since spec 050,
selection props are `Optional<T>`, which has an implicit `T → Optional<T>`
operator (`Optional.cs:61`) — so `element with { SelectedIndex = -1 }` becomes
`Optional.Of(-1)` (force-assert "clear it", re-applied every render) rather than
`Optional<T>.Unset` (let the control own it). Compiles clean, opposite runtime
meaning.

> **Scope + severity correction (review):** the first draft over-reached. `Text =
> ""`, `Password = ""`, and `IsChecked = false` are ordinary values with
> legitimate intent, not sentinels — flagging them is a false positive. Restrict
> the member allowlist to true *selection* sentinels: `SelectedIndex`,
> `SelectedPageIndex`, `Date`,
> `Time`. And because `Optional.Of(-1)` is a **documented-valid** force-assert
> (migration table, "force-assert" column), the rule is a *nudge to be explicit*,
> not a correctness error — ship at **Info**. It is **not** ⭐: nothing in the
> codebase pre-announces it; spec 050 §6.2 explicitly argued the `Optional<T>`
> API design means "no analyzer" is needed for the compile-time half, and names
> only `REACTOR0050` (the descriptor-side rule).

**Detect.** Syntactic: an object-initializer / `with` member assignment where the
member is in `{ SelectedIndex, SelectedPageIndex, Date, Time }` and the RHS is the sentinel literal
(`-1` / `null`). Then one `GetTypeInfo` confirming the declared type is
`Optional<T>`. (`SelectedItem`/`SelectedValue` are handled by `CTRL_001`.)

**Fix.** Two `CodeAction`s via `Diagnostic.Properties`: `Optional<T>.Unset`
(control-owned) or `Optional.Of(x)` (keep the force-assert, silences the nudge).

**FP risk.** Low with the narrowed allowlist; Info severity absorbs the residual
"the author really did want a force-assert" case.

**Grounding.** `Optional.cs:61`; `Element.cs:6355` (`PipsPagerElement.SelectedPageIndex`);
[migration/050-optional-t.md](../guide/migration/050-optional-t.md);
[spec 050 §6.2](050-controlled-prop-authority-and-optional-t.md). Distinct from
`REACTOR0050` (descriptor-author `dp:` omission).

#### `REACTOR_ITEMS_001` — .Set(ItemsSource=…) on an owned collection *(Reactor.Collections · Warning · no fix)*

**XAML habit → failure.** `ListView<T>(items, …).Set(lv => lv.ItemsSource =
other)`. Reactor's `ListView<T>`/`GridView<T>` own the collection via keyed
reconciliation (populating `.Items`); a manual `ItemsSource` throws or fights the
diff, corrupting selection/virtualization.

**Detect.** Reuse `PoolResetSetAnalyzer`'s "`.Set` lambda assigns member X" shape
(`PoolResetSetAnalyzer.cs:124`), then add a step it doesn't have: confirm the
receiver's **element type** is in a curated allowlist of collection elements
(`ListViewElement`, `GridViewElement`, `TreeViewElement`, `TabViewElement`,
`PivotElement`, `FlipViewElement`, `SelectorBarElement`) **and** the assigned
member is `ItemsSource`. Exclude `AutoSuggestBoxElement`, where `.Set(ItemsSource
= …)` is the documented escape hatch.

> **Feasibility note (review):** POOL_001's shape checks the *property name only*,
> never the receiver type; a runtime `[registered]` descriptor is not visible to
> a syntactic analyzer. So this is a **hand-curated element-type table** (like
> `PoolResetSetAnalyzer.TrappedProperties`), with the maintenance burden that
> implies — not a drop-in reuse.

**Fix.** None (data belongs in the `items` factory arg) — message links the recipe.

**FP risk.** Low — narrow property + curated type table.

**Grounding.** [control-reconciler-protocol.md](../guide/control-reconciler-protocol.md);
[forms.md](../guide/forms.md); `PoolResetSetAnalyzer.cs:124`.

#### `REACTOR_CTRL_001` — .Set(SelectedItem=…) fights SelectedIndex *(Reactor.Controls · Warning · fix)*

**XAML habit → failure.** "Set `SelectedItem` *and* `SelectedIndex`." Reactor
selectors expose `Optional<int> SelectedIndex`; a `.Set(cb => cb.SelectedItem =
x)` on the underlying control creates a second authority that races/clobbers the
controlled index write.

**Detect.** A `.Set` lambda assigning the *WinUI control's* `.SelectedItem` /
`.SelectedValue` on an element from a curated table of `SelectedIndex`-controlled
elements — `ComboBoxElement`, `RadioButtonsElement`, `ListViewElement`,
`GridViewElement` — **and** the same element also sets `SelectedIndex`. Same
curated-table shape as `ITEMS_001`.

> **Correction (review):** the first draft listed `NavigationView` and
> `.SelectedTag`. Both are wrong. `NavigationViewElement` selects by
> **`SelectedTag`** (a `string?` mapped through `SelectionChanged`,
> `Element.NavigationView.cs:12,125`), not `SelectedIndex`; and `SelectedTag` is
> a *Reactor element* property, not a WinUI control member, so `.Set(nv =>
> nv.SelectedTag = …)` won't compile and can never be the detected target.
> NavigationView is dropped from this rule.

**Fix.** Delete the `.Set(SelectedItem = …)` call (the controlled `SelectedIndex`
is the authority). Do **not** auto-convert `SelectedItem` → `SelectedIndex` — that
is not a mechanical rewrite. Fire only when a competing `SelectedIndex` is
actually set on the element.

**FP risk.** Low — curated table + requires the competing prop present.

**Grounding.** `Element.cs` (ComboBox/RadioButtons `SelectedIndex`);
`Element.NavigationView.cs:12,125`.

### §4.3 Commanding

#### `REACTOR_CMD_001` — Raw-init Command + OnClick both set *(Reactor.Commanding · Info · fix)*

**XAML habit → failure.** In WPF/UWP, `Button.Click` fires *alongside* `Command`.
In Reactor `EffectiveCallback(userCallback, cmd) => userCallback ?? Invokable(cmd)`
(`CommandBindings.cs:169`) — the explicit `OnClick` wins and `cmd.Execute` never
runs.

> **Value + fix correction (review):** the fluent path already guards this. The
> `.Command(...)` modifier sets `el with { Command = command, OnClick = null }`
> (`ElementExtensions.cs:1053`), and there is a dedicated `Button(Command)`
> factory (`Dsl.cs:176`); there is **no** `.OnClick()` modifier. So the bug is
> reachable **only** by hand-writing a raw `new ButtonElement(...) { Command = …,
> OnClick = … }` or an equivalent `with`. That is rare — hence **Info**, not
> Warning. And "move the OnClick body into `cmd.Execute`" is **unsafe** (it
> bypasses `CanExecute`, breaks `UseCommand`/`DebounceMs` arming, and can swallow
> an async `ExecuteAsync`), so the auto-fix is limited to **deleting the
> redundant `OnClick`** (the property to remove travels in `Diagnostic.Properties`).

**Detect.** Syntactic: a raw `new …Element(…) { … }` / `with` that assigns *both*
`Command` and the element's **own** click/toggle callback, resolved through a
per-element callback map: `OnClick` for `ButtonElement` / `HyperlinkButtonElement`
/ `RepeatButtonElement` / `SplitButtonElement` (`Element.cs:2849,3112`);
`OnIsCheckedChanged` (or `OnCheckedStateChanged`) for `ToggleButtonElement` /
`ToggleSplitButtonElement` (`Element.cs:3033,3045,3155`). Also flag the
constructor-positional callback shape (`new ButtonElement("Save", DoThing) {
Command = cmd }`), since the positional `Action?` *is* `OnClick`. Do **not**
cross-reference `CommandDebounceAnalyzer`'s list: it matches DSL *factory* names
and includes `MenuItem`/`AppBarButton`, which are plain data records
(`MenuFlyoutItemData`/`AppBarButtonData`), not command-capable `Element` records —
the two rules can't share a table.

**Fix.** Delete the redundant mapped callback (the `OnClick` /
`OnIsCheckedChanged` / `OnCheckedStateChanged` for that element).

**FP risk.** Low — unconditional precedence, raw-init shape only.

**Grounding.** `CommandBindings.cs:169`; `ElementExtensions.cs:1053`; `Dsl.cs:176`;
[commanding.md](../guide/commanding.md).

### §4.4 Theming & visibility

#### `REACTOR_THEME_004` — Hard-coded Brush/Color object *(Reactor.Style · Warning · fix)*

**XAML habit → failure.** `Background="Red"` becomes
`.Background(new SolidColorBrush(Colors.Red))`. `THEME_001` only inspects *string
literals* (`UseThemeRefAnalyzer.cs:79-82`), so any `Brush`-typed argument sails
past it — a silent dark-mode regression identical to the string case it catches.

**Detect.** Reuse `THEME_001`'s target-modifier gate (`.Background`/`.Foreground`/
`.WithBorder`); flag when the argument is an inline `new SolidColorBrush(...)` /
`new SolidColorBrush(Colors.X)` (restrict to inline creation to avoid flagging a
field that legitimately holds a resolved token brush).

**Fix.** When the color is in `THEME_001`'s `ColorToThemeToken` map, rewrite `new
SolidColorBrush(Colors.X)` to the matching `Theme.*` token. For an **unmapped**
color there is no mechanical fix — `Theme.Ref(key)` needs a resource-key string the
analyzer can't invent — so the diagnostic stands without an auto-fix in that case.

**FP risk.** Low — inline-creation-only keeps token-brush fields safe.

**Grounding.** `UseThemeRefAnalyzer.cs:79-82`; `ElementExtensions.cs` (`Brush`
overloads); [styling.md](../guide/styling.md).

#### `REACTOR_VIS_001` — Imperative Visibility toggling *(Reactor.Layout · Warning · fix)*

**XAML habit → failure.** `element.Set(c => c.Visibility = Visibility.Collapsed)`
— the code-behind `myControl.Visibility = …` reflex. It bypasses the declarative
`.IsVisible(bool)` modifier (`ElementExtensions.cs:159`) / conditional inclusion
(`cond ? el : null`), and — like the rest of the `.Set`-family — an imperative
write is not reconciled: on a later render that doesn't re-run the `.Set`, or when
the pooled control is reused by a different element, the value is stale. This is
exactly `POOL_001`'s failure mode; `Visibility` simply isn't in that analyzer's
`TrappedProperties` table today (verified — `PoolResetSetAnalyzer.cs:36`), so this
rule is best understood as **extending `POOL_001` to cover `Visibility`** with a
targeted fix.

**Detect.** The `.Set`-lambda-assignment shape (`PoolResetSetAnalyzer.cs:124`)
where the assigned member is `Visibility` and the receiver derives from
`UIElement`.

**Fix.** Offer the auto-fix **only** when the RHS is a literal `Visibility.Collapsed`
→ `.IsVisible(false)` or `Visibility.Visible` → `.IsVisible(true)`; `.IsVisible`
round-trips only those two values (`Reconciler.cs:3691`), so any other value gets a
diagnostic but no rewrite. For a **conditional** RHS (`cond ? Collapsed : Visible`)
the fix must read both branches and emit `.IsVisible(!cond)` / `.IsVisible(cond)`
by polarity; if the polarity can't be determined syntactically, nudge only (never
emit a possibly-inverted `.IsVisible(cond)`).

**FP risk.** Low — specific member + a first-class modifier exists as the fix
target. Best implemented as part of the shared `.Set`-family analyzer — ideally by
adding `Visibility` to `POOL_001`'s table with a `.IsVisible` fix
([§5](#5-cross-cutting-notes)).

**Grounding.** `ElementExtensions.cs:159` (`.IsVisible`); `Reconciler.cs:3691`
(Visible/Collapsed round-trip); `PoolResetSetAnalyzer.cs:36,124`;
[layout.md](../guide/layout.md).

### §4.5 DSL & list keys

#### Grid string-track migration ⭐ *(CS0618 code-fix — no diagnostic id · fix)*

**XAML habit → failure.** `Grid(["*","Auto","200"], ["*"], …)` — bringing XAML's
most string-typed corner along. `"*"` vs `"1*"` vs `"Star"` parse to different
runtime results with zero compile-time check; typos fail silently at layout time.

> **Restructure (review):** the string overload is already `[Obsolete(error:
> false)]` (`Dsl.cs:746-749`), so **`CS0618` already fires** on every call site. A
> new `REACTOR_GRID_001` *diagnostic* would double-report the same squiggle. The
> value spec 033 §1 wanted is the **fix**, not another warning. So this ships as a
> `CodeFixProvider` **registered against `CS0618`** for the obsolete `Grid`
> overload — no new diagnostic id. (The ⭐ still stands: spec 033 §1 calls for
> exactly this fixer.) **Round-3 note:** the `REACTOR_GRID_001` *diagnostic* id is
> deliberately **not** used here — `docs/guide/layout.md:555` already reserves it
> for a different, doc-named "unused-column" analyzer, now catalogued in
> [§12](#12-batch-2--cross-model-blind-spot-additions). This fixer stays id-less.

**Detect.** The code fix registers on `CS0618` occurrences whose target symbol is
the `Grid(string[], string[], …)` overload (`Dsl.cs:750`).

**Fix.** Parse each string element to `GridSize.Star/.Auto/.Px` and swap to the
typed `Grid(GridSize[], GridSize[], …)` overload (`Dsl.cs:733`). Fully mechanical
for inline literal arrays; for a variable / non-literal `string[]`, offer no
automatic fix (the values aren't visible) and leave the `CS0618` warning to stand.

**FP risk.** None — keyed off the compiler's own obsolete diagnostic.

**Grounding.** `Dsl.cs:733,746-750`; [spec 033 §1](033-winui-xaml-reviewer-feedback.md).

#### `REACTOR_DSL_002` — Non-stable .WithKey *(Reactor.Dsl · Info · no fix)*

**XAML habit → failure.** `items.Select((item, i) => Row(item).WithKey(i.ToString()))`,
or `.WithKey(Guid.NewGuid().ToString())` / `.WithKey($"{DateTime.Now.Ticks}")`. A
key exists, so `DSL_001` stays silent — but a positional or per-render-random key
is identical to (or worse than) no key on reorder/insert: it loses
focus/animation/`ElementRef` identity and forces the LIS keyed-diff into its
worst case.

**Detect.** Syntactic, two shapes: **(1)** `.WithKey(arg)` where `arg`'s only
identifiers resolve by name to the *second* (index) parameter of the enclosing
`Select`/`ForEach` lambda and never reference the item parameter; **(2)**
`.WithKey(arg)` where `arg` contains `Guid.NewGuid()`, `DateTime.Now`,
`Random`, or `Environment.TickCount`. Must not flag composite keys like
`$"{item.Id}-{i}"` (shape 1 references the item too).

> **Feasibility + severity note (review):** extending `MissingWithKeyAnalyzer` is
> real work. It already admits the single-lambda `Select` form and any lambda
> shape, but it only does a **textual `.WithKey(` probe** and never parses the key
> *expression* (`MissingWithKeyAnalyzer.cs:68`), so both shapes here need new
> key-expression analysis (index-parameter symbol resolution / interpolation
> unwrapping / the `Guid.NewGuid()`|`DateTime.Now` name check). And a positional
> key is only *wrong* for lists that reorder/insert (harmless for static lists),
> with no safe auto-fix. Hence **Info**, no fix.

**Fix.** None (can't synthesize real identity) — message suggests the item's id.

**FP risk.** Low with the narrow shapes; Info severity absorbs the static-list case.

**Grounding.** [reconciliation.md](../guide/reconciliation.md) ("Using array index
as a key"); [collections.md](../guide/collections.md); `MissingWithKeyAnalyzer.cs:68`.

### §4.6 Threading, effects & lifecycle

#### `REACTOR_THREAD_001` — UI-thread mutator on a background thread *(Reactor.Threading · Warning · fix)*

**XAML habit → failure.** Touching a window/control from a background thread
without the dispatcher: `_ = Task.Run(() => window.Close());`. Reactor's
`ThreadAffinity.ThrowIfNotOnUIThread` throws at runtime.

**Detect.** Syntactic gate: an invocation lexically inside a
`Task.Run`/`Task.Factory.StartNew`/`ThreadPool.QueueUserWorkItem` lambda that is
**not** already wrapped in a `TryEnqueue`. Then confirm the invoked method is
UI-thread-guarded via a **`[UIThreadOnly]` marker attribute** on the framework
member.

> **Detection correction (round 2 — the first-draft mechanism was infeasible):**
> resolving the callee and inspecting its body for a
> `ThreadAffinity.ThrowIfNotOnUIThread(...)` call **cannot work** for the members
> this rule targets. In a consumer project the Reactor framework is a *metadata*
> reference — `IMethodSymbol.DeclaringSyntaxReferences` is empty and analyzers
> can't read IL — so a body-probe only sees callees defined in the *same*
> compilation, never `ReactorWindow.Close()` et al. The workable mechanism is a
> **`[UIThreadOnly]` attribute** on the ~20+ guarded members (metadata-visible),
> which the analyzer keys off; a generated symbol/FQN allowlist (emitted from the
> members carrying the runtime guard) is the fallback. A hard-coded member-name
> list is **not** acceptable (stale on day one). The speculative "after
> `ConfigureAwait(false)`" arm is dropped — it needs CFG, which §2 rules out.
>
> **Cost:** `[UIThreadOnly]` must be added to the framework surface, so this rule
> carries a small framework API change and can't ship purely from
> `Reactor.Analyzers` — sequence it after that annotation lands (see OQ#5).

**Fix.** Wrap: `ReactorApp.UIDispatcher!.TryEnqueue(() => window.Close());`.

**FP risk.** Low for the background-lambda shape once the callee is confirmed
UI-thread-guarded semantically.

**Grounding.** [threading-and-dispatch.md](../guide/threading-and-dispatch.md)
(`ThrowIfNotOnUIThread`); [windows.md](../guide/windows.md); `ReactorWindow.cs:1777-1817`
(guarded call sites, e.g. `Activate`/`Hide`/`Show`/`Close`).

#### `REACTOR_THREAD_002` — Blocking a Task on the UI thread *(Reactor.Threading · Warning · no fix)*

**XAML habit → failure.** `var data = FetchAsync().Result;` / `.Wait()` /
`.GetAwaiter().GetResult()` inside `Render()` or a `UseEffect` body — the
WinForms/WPF "just block for the result" reflex. On the UI thread this deadlocks
or freezes the reconciler and the dispatcher together.

**Detect.** Syntactic: a `.Result` member access, or a `.Wait()` /
`.GetAwaiter().GetResult()` invocation, whose receiver is `Task`/`Task<T>` (or
`ValueTask`/`ValueTask<T>`) (semantic confirm), lexically inside a `Render()`
override or a `UseEffect` lambda and **not** inside a nested `Task.Run`.

**Fix.** None mechanical (the correct rewrite is `UseResource`/async effect, which
restructures the method) — message points to the async-data recipe.

**FP risk.** Near zero once the receiver is confirmed `Task`-typed and the
enclosing context is render/effect.

**Grounding.** [threading-and-dispatch.md](../guide/threading-and-dispatch.md)
("Don't block the UI thread"); [async-resources.md](../guide/async-resources.md).

#### `REACTOR_LIFECYCLE_001` — Event subscription via .Set(+=) *(Reactor.Lifecycle · Warning · fix)*

**XAML habit → failure.** `control.Loaded += Handler` from code-behind, translated
into `.Set(c => c.Loaded += OnLoad)`. `.Set` replays on *every* reconcile, so each
render adds a subscription — the handler fires once per past render and old
closures are never removed (leak + multiplying invocation).

**Detect.** Syntactic: a `.Set(lambda)` whose body is a compound assignment
(`AddAssignmentExpression`/`SubtractAssignmentExpression`) — reuse
`PoolResetSetAnalyzer.TryGetLambdaAssignment` (`:124`). Then a **required**
semantic check that the LHS member is an `event` symbol.

> **FP correction (review):** the event-symbol check must be **mandatory**, not
> optional. `TryGetLambdaAssignment` returns any assignment, so branching on
> `+=`/`-=` alone would false-fire on numeric compound assignment
> (`.Set(c => c.Opacity += 0.1)`, `c.Width -= 4`) and on `+=` to a non-event
> delegate field or an `ObservableCollection.CollectionChanged` — all producing
> uncompilable fixes. Restrict further to receivers deriving from
> `FrameworkElement`.

**Fix.** Rewrite to `.OnMount(c => ((TControl)c).Event += h).OnUnmount(c =>
((TControl)c).Event -= h)`. Two constraints, both from round-2 review: **(1)**
`.OnMount`/`.OnUnmount` receive `Action<FrameworkElement>`
(`ElementExtensions.cs:2433,2461`) and can't return a cleanup, so teardown is the
separate `.OnUnmount`; **(2)** control-specific events (`Click`, `Toggled`,
`TextChanged`, …) aren't on `FrameworkElement`, so the lambda must **cast to the
concrete control type** — recoverable because each `.Set` overload is concrete-typed
(`Set(this ButtonElement, Action<WinUI.Button>)`, `ElementExtensions.cs:1913`), so
the fixer reads `TControl` from the original `.Set` lambda's parameter type. Offer
the fix **only** when the handler `h` is a stable delegate — a `static` method
group or a field/property on the enclosing component — since `.OnMount` runs once
at mount and `.OnUnmount` at unmount, a per-render captured local would make `-=`
unsubscribe a *different* delegate and leak. Otherwise nudge-only.

**FP risk.** Near zero **with** the mandatory event-symbol check.

**Grounding.** [advanced.md](../guide/advanced.md) ("don't accumulate event
handlers"); `ElementExtensions.cs:2433,2461`; `PoolResetSetAnalyzer.cs:124`.

### §4.7 Descriptor authoring

#### `REACTOR_DESC_001` — Non-static Register lambda ⭐ *(Reactor.Descriptor · Warning · fix)*

**Library-author rule (not a XAML habit).**
`ControlRegistry.Register<E,C>(() => new MyHandler())` without `static` defeats the
trimmer's ability to drop the holder→handler→control chain from a NativeAOT
publish (and, when the lambda captures, allocates a closure per call — a
*non-capturing* `() => new MyHandler()` is cached in a static field by Roslyn, so
the primary cost is trim hygiene, not per-call allocation). The docs mark the
`static` keyword mandatory and say the analyzer is the only guard.

**Detect.** Purely syntactic: an invocation of any of the four `ControlRegistry`
static-lambda entry points — `Register` (`ControlRegistry.cs:99`),
`RegisterForDerivedTypes` (`:214`), `RegisterDecorator` (`:180`),
`RegisterDecoratorForDerivedTypes` (`:237`) — whose lambda argument's `Modifiers`
lack `StaticKeyword`.

**Fix.** Insert the `static` modifier — but only when the lambda captures nothing
(a capturing lambda won't compile with `static`; there the fix is a nudge to
refactor the capture out).

**FP risk.** None — the framing is unconditional.

> **Framing note (review):** the guide says "mandatory"
> (`extending-reactor-controls.md:591`) while the API XML doc says "strongly
> recommended" (`ControlRegistry.cs:96`). Present this as a perf/trim-hygiene rule
> for control authors, not a correctness bug — and cover all four entry points,
> not just `Register`/`RegisterForDerivedTypes`.

**Grounding.** `extending-reactor-controls.md:599` — *"(Issue #486 tracks adding
the analyzer.)"*; `ControlRegistry.cs:99,180,214,237`.

### §4.8 Persistence

#### `REACTOR_PERSIST_001` — UsePersisted 2-arg scope ⭐ *(Reactor.Persistence · Warning · fix)*

**XAML habit → failure.** `UsePersisted("filter", "")` reads as "just remember
this," but the 2-arg overload is `=> UsePersisted(key, initial,
PersistedScope.Application)` (`RenderContext.cs:824-825`) — process-wide, so state
bleeds across windows/tabs sharing the key. Invisible until two windows are open
at once.

**Detect.** Syntactic: `UsePersisted` invoked with exactly two arguments (no
`scope:`); confirm the symbol is `RenderContext.UsePersisted<T>(string, T)`.

**Fix.** Insert `, PersistedScope.Window` (recommended) or `, PersistedScope.
Application` (make the current behavior explicit) as two actions.

**FP risk.** None — arity + symbol check.

**Grounding.** `RenderContext.cs:820` — *"The two-arg form will trigger an analyzer
warning in a follow-up"*; `RenderContext.cs:824-825`;
[persistence.md](../guide/persistence.md).

---

## §5 Cross-cutting notes

- **A `.Set(...)`-family analyzer.** `ITEMS_001`, `CTRL_001`, `VIS_001`, and
  `LIFECYCLE_001` all match "a `.Set` lambda assigns/subscribes something the
  framework owns," the same shape `POOL_001` already implements. They should
  share one internal helper (lambda → assignment/compound-assignment → member
  lookup) with a per-rule member/type table. Note the extra step POOL_001 lacks:
  it checks the *property name only*, never the receiver **type**, so the
  collection/selector rules need a curated element-type table on top (a runtime
  descriptor registration is invisible to a syntactic analyzer).
- **The `HOOKS_004` allocation family.** `HOOKS_012` (Memo), `HOOKS_013` (UseState
  initializer), and `CTX_001` (`.Provide`) all classify "a freshly-allocated value
  in a position where identity/equality matters." They reuse the existing
  classifier **`ClassifyDepExpression`** (`HookRulesAnalyzer.cs:440`, returning
  `(bool Unstable, string Kind)`) — **not** an `IsFreshlyAllocated` helper, which
  does not exist. Two caveats the reuse must respect: the classifier also flags
  `TupleExpression`, lambdas, and anonymous methods (so HOOKS_013 needs a
  *restricted* object/array/collection-only variant), and it does **not** yet
  handle `WithExpressionSyntax` (so the `with { … }` cases in CTX_001/OPT_001 are
  false negatives until it is extended). Open question [#3](#11-open-questions)
  proposes extracting the shared, restricted helper as part of this work.
- **Comment-reserved slots.** `HOOKS_002` and `HOOKS_003` exist only as a header
  comment in `HookRulesAnalyzer.cs:42-43`; no `DiagnosticDescriptor` is reserved,
  and they are absent from `AnalyzerReleases.Unshipped.md`. "Filling the slot"
  means adding the descriptor, not un-commenting one.
- **False-positive discipline.** Every rule is scoped to a single method body or a
  single fluent chain. Anything needing cross-method or whole-program flow was
  deferred ([§9](#9-considered-but-not-included)) rather than shipped as a noisy
  heuristic — including, after review, `THREAD_001`'s `ConfigureAwait(false)` arm.

## §6 Release tracking & categories

New categories introduced: `Reactor.State`, `Reactor.Context`, `Reactor.Controlled`,
`Reactor.Collections`, `Reactor.Controls`, `Reactor.Commanding`, `Reactor.Layout`,
`Reactor.Threading`, `Reactor.Lifecycle`, `Reactor.Persistence`. Existing categories
extended: `Reactor.Hooks`, `Reactor.Style`, `Reactor.Dsl`, `Reactor.Descriptor`.
`GRID_001` adds no category — it is a `CodeFixProvider` on `CS0618`.

The category strings do not track the id prefixes (a pre-existing quirk — see
[§3](#3-design-constraints-inherited)). Before implementation, decide whether to
align the new a11y-adjacent categories with the existing
`Microsoft.UI.Reactor.Accessibility` style or keep the shorter `Reactor.*` form
used by `Reactor.Hooks`/`Reactor.Style` (open question [#4](#11-open-questions)).

Each **diagnostic** rule adds a row to
`src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md` and, on GA, moves to
`AnalyzerReleases.Shipped.md`. `GRID_001` is exempt — a `CodeFixProvider` on
`CS0618` has no `DiagnosticDescriptor` and therefore no release row. Consumers
promote/suppress per category via `.editorconfig`.

## §7 Testing strategy

Per rule, add `tests/Reactor.Tests/AnalyzerTests/` cases (the existing analyzer
test home — e.g. `HookRulesAnalyzerTests.cs`, `PoolResetSetAnalyzerTests.cs`) for:
the positive case, the negative case, and the near-miss that almost trips the
syntactic fast path (the regressions that bite later). Every code-fix rule
additionally gets a fix round-trip test.

Before merge, run the assembled analyzer against `samples/` — a rule that finds
nothing there is over-fit or gated too narrowly. For fast CI signal, gate on the
three highest-coverage samples first (`ReactorGallery`, `StylingGallery`,
`TodoApp`) and run the full sample sweep nightly rather than per-PR.

## §8 Rollout & severity defaults

- **Land the three ⭐ items first** (`GRID_001`, `DESC_001`, `PERSIST_001`): each is
  already sanctioned in-tree, near-zero FP, and mechanically fixable. On GA the
  diagnostic rows for `DESC_001`/`PERSIST_001` move from
  `AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md`; `GRID_001` ships
  as a `CodeFixProvider` on `CS0618` with no release row. (Note `THREAD_001` is
  *not* in the first wave — it depends on the `[UIThreadOnly]` framework
  annotation; see OQ#5.) Everything else stays unshipped until it clears the
  `samples/` sweep.
- **Ship most rules at `Warning`.** Ship at **Info** the five nudge-class rules
  whose "violation" is sometimes a legitimate choice or whose coverage is
  intentionally narrow: `HOOKS_002`, `CTX_001`, `OPT_001`, `DSL_002`, `CMD_001`.
- No **§4 core** rule is `Error` by default. The single exception is the Batch-2
  `WIN2D_001` (§12) — a fatal cross-device crash — which defaults to `Error`;
  every other rule ships `Warning` or `Info`. Teams opt into further promotion via
  `.editorconfig`, matching current guidance ("treat analyzer warnings as build
  errors in CI").

## §9 Considered but not included

Weighed during design or in review and deliberately left out of this batch:

- **`ANIM_001` — `.Animate()` on `Width`/`Height`.** *Cut.* The premise (a XAML dev
  expects `.Width(x).Animate()` to animate the width) is real, but there is **no
  reliable syntactic signal**: `.Animate()` configures compositor properties
  globally (`AnimateProperty` has only `Opacity/Offset/Scale/Rotation/CenterPoint`),
  so `.Width(300).Animate(Curve.Spring())` is perfectly valid when the author is
  animating opacity/scale on a statically-sized element. Two reviewers flagged the
  chain-adjacency heuristic as unavoidably false-positive; no gating separates
  intent from valid use. Dropped.
- **`REF_002` — imperative `SetBinding` on an owned control.** *Deferred.*
  `SetBinding`/`BindingOperations.SetBinding` in Reactor app code is rare, so the
  narrow rule finds little; the *valuable* generalization — "any imperative write
  to a reactive DP inside `.Set`/`.OnMount`" (e.g. `.Set(c => c.Background = brush)`)
  — is a larger design that overlaps `POOL_001`/`THEME_004` and needs its own
  scoping pass. Revisit as a v2 `.Set`-family member.
- **Mutable instance field on a `Component`.** A field written in a handler and
  read in `Render()` drives nothing (the STATE_001 story for the non-INPC case).
  Real, but the honest detection needs single-method write/read correlation and
  has a legitimate-mutable-cache false-positive surface; deferred pending a tight
  scoping.
- **Non-deterministic values in `Render()`** (`DateTime.Now`, `Guid.NewGuid()`,
  `new Random()` inline). Churn + unstable keys; overlaps `HOOKS_013` and
  `DSL_002`. A candidate, deferred to keep this batch tight.
- **`.Set(c => c.DataContext = …)`** — a XAML habit, but establishing a
  `DataContext` on a Reactor-owned control is nearly always dead code; low enough
  frequency to defer, and partially covered by the deferred imperative-DP-write
  rule above.
- **Reducer same-ref return** (`prev.Add(x); return prev;` from a `UseReducer<T>`
  updater) and **captured-lambda-in-`.Click` without `UseCallback`** — both real,
  both need light dataflow; queued behind the shared setter-tracking helper.

## §10 Review changelog

Two review rounds, five models each (Claude Opus 4.8 / Opus 4.7, GPT‑5.5,
GPT‑5.3‑Codex, Gemini 3.1 Pro), each at max effort and each verifying claims
against source.

### Round 1 (first draft)

Consolidated outcome: **2 Needs-work, 3 Approve-with-changes**,
with strong cross-model consensus on a concrete defect set. All corrections below
were re-verified against source before applying.

| Change | Driver (agreement) |
|---|---|
| `CTX_001` premise corrected (context diffs use `Equals`, not reference id; `Element.cs:1358`), reframed to reference-equality-only types, **downgraded to Info** | Gemini + GPT‑5.5 (blocking), Codex, Opus 4.8 |
| Removed the misplaced 4th ⭐ from `OPT_001`; reconciled "three" across intro/table/§8; **downgraded OPT_001 to Info** and narrowed its member allowlist to `SelectedIndex/Date/Time` | all five |
| **Cut `ANIM_001`** (no reliable detection); **deferred `REF_002`** (low value); added `VIS_001` + `THREAD_002` in their place | GPT‑5.5, Gemini, Opus 4.7/4.8 |
| Renamed the fictional `IsFreshlyAllocated` → `ClassifyDepExpression`; specified a restricted variant and noted the `with`-expression gap | Opus 4.7, Opus 4.8, Codex |
| `HOOKS_012`: reworded (deps compare by `Equals`), required the `params`-deps overload, **excluded the keyed `Memo<TKey>`** | GPT‑5.5, Opus 4.7, Codex, Opus 4.8 |
| `HOOKS_013`: bound to the correct per-overload initial-value argument index; restricted classifier to exclude tuples/lambdas | GPT‑5.5, Codex, Opus 4.8 |
| `HOOKS_010`: fixed the invalid functional-updater fix (UseState setter is `Action<T>`); corrected the equality characterization | GPT‑5.5, Opus 4.7/4.8 |
| `LIFECYCLE_001`: fix corrected to `.OnMount(...).OnUnmount(...)`; event-symbol check made **mandatory** | GPT‑5.5, Opus 4.7/4.8 |
| `CTRL_001`: removed NavigationView (it's `SelectedTag`, not `SelectedIndex`) and the un-compilable `.SelectedTag` target; fix reduced to deleting the `.Set` | GPT‑5.5, Codex, Opus 4.7/4.8 |
| `CMD_001`: **downgraded to Info**, scoped to raw-init only (`.Command()` already nulls `OnClick`; `Button(Command)` factory exists), dropped the unsafe "move into Execute" fix | Codex (blocking), Opus 4.7/4.8 |
| `THREAD_001`: replaced the hard-coded member allowlist with a `ThrowIfNotOnUIThread`/attribute semantic check; **dropped the `ConfigureAwait(false)` arm** | Opus 4.7, Codex, Gemini |
| `HOOKS_011`: gated on an explicit state-derived value; replaced the destructive `.IsEnabled(false)` fix with `.IsReadOnly(true)` (where available) or nudge-only | Gemini, Codex, Opus 4.8 |
| `HOOKS_003`: removed the non-existent `UseLayoutEffect` reference | GPT‑5.5, Codex, Opus 4.8 |
| `GRID_001`: restructured as a `CodeFixProvider` on `CS0618` (no duplicate diagnostic) | Codex, Opus 4.8 |
| `DESC_001`: cover all four `ControlRegistry.Register*` entry points; reframed as author perf/trim hygiene | Opus 4.8 |
| `DSL_002`: **downgraded to Info**, broadened to `Guid.NewGuid()`/`DateTime.Now` keys, noted the `MissingWithKeyAnalyzer` extension cost | Opus 4.8, Opus 4.7 |
| `HOOKS_002`: **downgraded to Info**, documented the single-guard coverage bound | Opus 4.7 |
| `ITEMS_001`/`CTRL_001`: reworded to a curated element-type table (not descriptor introspection) | Opus 4.8, Opus 4.7 |
| §5/§6/§8 consistency: "comment-reserved" slots, surfaced category column, fixed "two soft arms"→zero, sample-triage note | Opus 4.8, Opus 4.7 |

**Verified accurate and unchanged** (spot-checks by ≥3 reviewers): `CMD_001`
`EffectiveCallback` (`CommandBindings.cs:169`), `PERSIST_001`
(`RenderContext.cs:820,824`), `GRID_001` `[Obsolete]` (`Dsl.cs:746`), `DESC_001`
`#486` (`extending-reactor-controls.md:599`), `HOOKS_012` name-gate
(`HookRulesAnalyzer.cs:214`), `OPT_001` implicit operator (`Optional.cs:61`),
`THEME_004` string-only gate (`UseThemeRefAnalyzer.cs:79`).

### Round 2 (post-revision verification)

The revised draft went back to the same five models. Outcome: **3
Approve-with-changes, 2 Needs-work**, with **every round-1 fix independently
verified against source** (both Opus reviewers confirmed ~20–28 citations each).
No round-1-severity premise errors remained; the round-2 changes are localized
detection/fix mechanics and polish.

| Change | Driver |
|---|---|
| `THREAD_001` detection reworked — the body-probe is **infeasible cross-assembly** (metadata symbols have no syntax/IL); committed to a `[UIThreadOnly]` attribute (framework change) with a generated symbol-map fallback, and flagged the framework-change cost | GPT‑5.5 + Codex (blocking), Opus 4.8 |
| `LIFECYCLE_001` fix corrected to **cast** the `FrameworkElement` receiver to the concrete control type (recoverable from the typed `.Set` overload) and to require a stable handler delegate | Gemini, GPT‑5.5, Opus 4.8/4.7 |
| `CMD_001` uses a **per-element callback map** (`OnClick` vs `OnIsCheckedChanged`/`OnCheckedStateChanged`), adds the ctor-positional-callback shape, and drops the bogus `CommandDebounceAnalyzer` sync note (those are data records) | GPT‑5.5, Codex, Opus 4.7 |
| `VIS_001` reframed as a `POOL_001` extension (`Visibility` is absent from `TrappedProperties`); fix gated to literal Visible/Collapsed and to branch-polarity-aware conditionals | GPT‑5.5, Codex, Opus 4.8 |
| `HOOKS_012` code fix **dropped** (no `HOOKS_004` fixer exists); now message-only, ✔ removed | GPT‑5.5 |
| `HOOKS_013` `UseRef` alternative fix removed (eager-allocates; `RenderContext.cs:791`) | Gemini, GPT‑5.5 |
| `HOOKS_003` fix relabeled a preview/template (extracts the async body) | Gemini, Opus 4.8 |
| `CTX_001` value-equality check extended to exclude classes overriding `Equals(object)` | Codex, Opus 4.7 |
| `OPT_001` allowlist gains `SelectedPageIndex` (`Element.cs:6355`) | Opus 4.7 |
| `DESC_001` reframed as trim-hygiene (a non-capturing lambda is cached); fix skips capturing lambdas | Opus 4.8 |
| `THEME_004` fix limited to mapped colors (no `Theme.Ref` stub) | GPT‑5.5 |
| `DSL_002` grounding corrected (barrier is the textual `.WithKey(` probe, not arg-count) | Opus 4.7/4.8 |
| Consistency: HOOKS_011 table wording, `GRID_001` category `—` + no release row, §7 test path `tests/Reactor.Tests/AnalyzerTests/`, `THREAD_002` `ValueTask` gap | Opus 4.7/4.8, GPT‑5.5, Codex |

Both Opus reviewers verified `Visibility` is **not** in `POOL_001`'s
`TrappedProperties` (so `VIS_001` is not a duplicate), and that the two new rules
(`VIS_001`, `THREAD_002`) are free of the accuracy/FP defects round 1 caught.

### Round 3 (blind-spot expansion)

A third pass changed scope rather than correctness: five models brainstormed
analyzers the spec *missed*, then cross-vetted the pooled 38 candidates
(everyone judged everything). That produced the **Batch 2** catalogue in
[§12](#12-batch-2--cross-model-blind-spot-additions) — 15 endorsed additions
(4 doc-named), with premise-wrong / duplicate / docs-conflicting ideas filtered
out ([§12.1](#121-rejected--deferred-in-round-3)). The cross-vet notably caught a
*reviewer's own* mistake — a "fictional API" REJECT of `WIN2D_001` that source
verification disproved — reinforcing why the everybody-reviews-everything step
earns its cost.

## §11 Open questions

1. **Final ID assignment.** The `NNN` numbers avoid collisions with shipped rules,
   but confirm the `HOOKS_010`/`012`/`013` sequence against any in-flight analyzer
   work before descriptors are cut.
2. **`OPT_001` vs `REACTOR0050`.** Both live in the `Optional<T>` story. Confirm
   they stay two rules (app-site selection literal vs descriptor `dp:` omission).
3. **Shared classifier extraction.** Extract a restricted, `with`-aware variant of
   `ClassifyDepExpression` as the shared dependency for `HOOKS_012`/`HOOKS_013`/
   `CTX_001`, and refactor `POOL_001` into the shared `.Set`-lambda helper as part
   of landing `ITEMS_001`/`CTRL_001`/`VIS_001`/`LIFECYCLE_001` — or duplicate
   first and consolidate later.
4. **Category naming.** Align the new categories with the existing
   `Microsoft.UI.Reactor.Accessibility` convention, or standardize on the shorter
   `Reactor.*` form and treat the a11y category as the outlier.
5. **`THREAD_001` marker (decided in round 2).** The `ThrowIfNotOnUIThread`
   body-probe is infeasible cross-assembly (framework methods are metadata-only),
   so a `[UIThreadOnly]` attribute on the guarded members is the committed
   mechanism (a framework API change), with a generated symbol/FQN allowlist as the
   fallback. Remaining sub-question: attribute vs. generated allowlist, and where to
   sequence the annotation work relative to the analyzer.

---

## §12 Batch 2 — cross-model blind-spot additions

**Process.** After the §10 correctness rounds converged the core 21, five models
(Opus 4.8/4.7, GPT‑5.5, GPT‑5.3‑Codex, Gemini 3.1 Pro) each independently
brainstormed "blind spot" analyzers the spec missed; the pooled 38 candidates
were then cross-vetted by all five (every model judged every candidate
ADD/DUP/DEFER/REJECT). The 15 below cleared a ≥3/5 ADD bar — with one documented
exception, `WIN2D_001` at 2/5† (retained after verification disproved the lone
objection; see the table's † note) — and carry no unresolved premise error. Batch 2
is intentionally lighter-weight than §4 — these are
*endorsed candidates*, not yet through the same two-round source-hardening — so
each carries its vote tally and needs an implementation-time verification pass.

**Four are DOC-NAMED but unbuilt** — the shipped docs already reference them as if
they exist, so building them just makes the docs true (like the §4 ⭐ set):
`REACTOR_INPUT_001` (input-and-gestures.md:631), `REACTOR_PERF_FUNCREF`
(commanding.md:645), `REACTOR_GRID_001` (layout.md:555 — the "unused-column"
rule, reclaiming the id freed in §4.5), and `REACTOR_A11Y_004` (runtime scanner
`A11Y_KEYBOARD_001`, input-and-gestures.md:643).

| Id | Category | Catches | Sev | Fix | Vote |
|---|---|---|---|---|---|
| `REACTOR_INPUT_001` ⭐ | Reactor.Input | Ctrl/Alt chord on `.OnKeyDown` instead of a `Command` accelerator | Warning | ✔ template | 5/5 |
| `REACTOR_PERF_FUNCREF` ⭐ | Reactor.Performance | Inline `new Command{…}` in `Render()` without `UseMemo` (accelerator rewire churn) | Info | ✔ `UseMemo` | 5/5 |
| `REACTOR_GRID_001` ⭐ | Reactor.Layout | A declared `Grid` track that no child occupies ("unused column") | Warning | — | 4/5 |
| `REACTOR_A11Y_004` ⭐ | Microsoft.UI.Reactor.Accessibility | Clickable `Border`/`Grid`/`Rectangle` (`.OnTapped`) with no `.IsTabStop`/`.TabIndex` | Warning | ✔ `.IsTabStop(true)` | 5/5 |
| `REACTOR_DIALOG_001` | Reactor.Lifecycle | Imperative `new ContentDialog().ShowAsync()` instead of controlled `IsOpen` | Warning | — | 5/5 |
| `REACTOR_NAV_001` | Reactor.Navigation | `UseNavigation` handle captured into a `static` field | Warning | — | 5/5 |
| `REACTOR_ANIM_002` | Reactor.Animation | `.Keyframes(name, <unstable trigger>)` (e.g. `DateTime.Now`) re-fires every render | Info | — | 5/5 |
| `REACTOR_LIFECYCLE_002` | Reactor.Lifecycle | `UseEffect(Action)` allocates a timer/subscription/`IDisposable` with no returned cleanup | Warning | ~ template | 4/5 |
| `REACTOR_INPUT_002` | Reactor.Input | Unsafe `TryGetFiles` in `.OnDrop` instead of `TryGetSafeLocalFiles` | Warning | ✔ swap | 4/5 |
| `REACTOR_MOD_001` | Reactor.Modifier | Duplicate atomic modifier in one chain (`.Grid().Grid()`) — last-wins overwrite | Info | ✔ merge | 4/5 |
| `REACTOR_ANIM_003` | Reactor.Animation | `async` lambda to `AnimationScope.WithAnimation` (ThreadStatic scope lost post-await) | Warning | — (split phases) | 4/5 |
| `REACTOR_DSL_003` | Reactor.Dsl | Typed `keySelector` that never keys by item (returns null/const/ignores the param) | Warning | ~ | 4/5 |
| `REACTOR_MEDIA_001` | Reactor.Layout | `WebView2` in an auto-layout stack with no explicit `Width`/`Height` | Info | — | 4/5 |
| `REACTOR_MEMO_001` | Reactor.Performance | Modifiers on a keyed `Memo(key,factory)` wrapper silently opt out of the recycle cache | Info | ✔ move inside factory | 3/5 |
| `REACTOR_WIN2D_001` | Reactor.Win2D | `UseCanvasResources` without `.UseSharedDevice()` on the canvas → cross-device crash | Error | ✔ append `.UseSharedDevice()` | 2/5† |

Terse entries (pitfall → detect → grounding):

- **`REACTOR_INPUT_001`** ⭐ — `TextBox(…).OnKeyDown((s,e) => { if (e.Key==VirtualKey.S && ctrl) Save(); })` is focus-scoped, so the intended app-wide `Ctrl+S` fires nowhere else and never reaches `AccessKeyManager`. Detect: `.OnKeyDown` lambda testing `VirtualKeyModifiers.Control`/`.Menu`. Fix: rewrite to a `Command` whose `Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control)` — `Command` exposes `Accelerator`/`AccessKey` (`Command.cs:76`), *not* an `AccessKeyModifiers` member; the chord helper is `Accelerator(VirtualKey, VirtualKeyModifiers)` (`Dsl.cs:1935`). (input-and-gestures.md:631.)
- **`REACTOR_PERF_FUNCREF`** ⭐ — `var save = new Command{…}` built in `Render()` gives a fresh identity each render → the command's `KeyboardAccelerator` is torn down and rewired every render (avoidable churn — the host clears and rebuilds accelerators each render, `CommandBindings.cs:73`, `CompositeLifecycle.cs:48`; it does *not* grow unbounded). Detect: `new Command{…}` in a `Render`/`Use*` body not wrapped in `UseMemo`/`UseCommand`. (commanding.md:645.)
- **`REACTOR_GRID_001`** ⭐ — a `GridSize` track with no child assigned to that row/column. Detect: literal track array vs. the `.Grid(row:/column:)` placements in the same call. Intent-heavy ("when enabled" per the doc) — ship at Warning, no auto-fix. (layout.md:555.)
- **`REACTOR_A11Y_004`** ⭐ — `Border(content).OnTapped(Open)` is mouse/touch-hittable but not in the tab order. Detect: A11Y_001-style chain walk — a non-focusable container factory with a tap handler and no `.IsTabStop`/`.TabIndex`/`.OnKeyDown`. Fix: append `.IsTabStop(true)`. (input-and-gestures.md:639-643; `.IsTabStop` is in POOL_001's `TrappedProperties`.)
- **`REACTOR_DIALOG_001`** — `Button("Save", async () => await new ContentDialog{…}.ShowAsync())` escapes the render tree (no parent theme, untestable, can't be driven by `IsOpen`). Detect: `new ContentDialog`/`.ShowAsync()` on the WinUI type (an `IdentifierNameSyntax` `ContentDialog(...)` factory is the correct, distinguishable path). (dialogs-and-flyouts.md:554-594.)
- **`REACTOR_NAV_001`** — `static NavigationHandle<Route>? Nav; … Nav = UseNavigation(…)` outlives the page and pins its dispatcher. Detect: `SymbolAction` on `static` fields typed `NavigationHandle<>` assigned from `UseNavigation`. (navigation.md:745-775.)
- **`REACTOR_ANIM_002`** — `.Keyframes("pulse", DateTime.Now, …)` restarts the animation every reconcile (visible flicker). Detect: the trigger arg is `DateTime.Now`/`Guid.NewGuid()`/an allocation — reuse the restricted classifier from §5. (animation.md:726-738.)
- **`REACTOR_LIFECYCLE_002`** — `UseEffect(() => { var t = new PeriodicTimer(…); … }, [])` with no cleanup: the timer fires post-unmount and the setter hits a dead context. Detect: an `Action`-overload `UseEffect` whose body creates a known lifetime type (`new PeriodicTimer`/`Timer`/`.Subscribe(`/`event +=`) with no `using`/`Dispose` in-body. Distinct from §4 `HOOKS_003` (async-void body) and `THREAD_002`. (effects.md:340-376; RenderContext.cs:363,379.)
- **`REACTOR_INPUT_002`** — `.OnDrop(args => args.Data.TryGetFiles(out var f))` accepts UNC/reparse/virtual files. Detect: `TryGetFiles` on a `DragData` receiver inside `.OnDrop`. Fix: swap to `TryGetSafeLocalFiles`. (DragData.cs:193; input-and-gestures.md:480-486.)
- **`REACTOR_MOD_001`** — `.Grid(row:1).Grid(column:2)` resets `row` to 0 (attached modifiers are atomic-replace, not merge). Detect: the same atomic-replace modifier name ≥2× in one linear chain. Fix: merge into one call. (modifier-system.md:290-341.)
- **`REACTOR_ANIM_003`** — `AnimationScope.WithAnimation(curve, async () => { …; await X; setStage(…); })`: `AnimationScope` is `[ThreadStatic]`, so post-await mutations run with an empty scope and animate nothing. Detect: `async` lambda arg to `WithAnimation` (no `Func<Task>` overload). Fix: **no clean mechanical rewrite** — `WithAnimationAsync(Curve?, Action)` (AnimationScope.cs:63) also takes an `Action`, not a `Func<Task>`, so it wouldn't remove the `async void`; the diagnostic should advise splitting the animated mutations into per-phase `WithAnimation` calls around the `await` (a real `Func<Task>` overload would be needed for a one-click fix). (animation.md:708-724; AnimationScope.cs:28,63.)
- **`REACTOR_DSL_003`** — `ListView(items, _ => "row", …)` / a `keySelector` returning null/const/ignoring the item → duplicate keys force a keyed-diff bailout (full re-realization). Detect: the typed `keySelector` lambda body is null/literal or never references its parameter. Distinct from `DSL_001/002` (`.WithKey`). (Dsl.cs:1464; KeyedListDiff.cs:230; collections.md:74-92.)
- **`REACTOR_MEDIA_001`** — `HStack(WebView2(uri))` with no size oscillates in an indeterminate container. Detect: `WebView2(...)` child of `HStack`/`VStack`/`FlexRow`/`FlexColumn` lacking `.Width`/`.Height`. (text-and-media.md:423-428.)
- **`REACTOR_MEMO_001`** — `Memo(item.Id, () => Row(item)).Padding(8)` — modifiers on the keyed-`Memo` wrapper bypass the cross-recycle cache. Detect: post-call modifiers on a `Memo(key, factory)` receiver (semantic-confirm the keyed overload). Fix: move modifiers into the factory body. (Dsl.cs:1256; ElementFactory.cs:151.)
- **`REACTOR_WIN2D_001`** † *(Reactor.Advanced; niche)* — `UseCanvasResources(...)` on a canvas that never sets `.UseSharedDevice()` makes the shared resources device-affine to the wrong device → a fatal cross-device draw crash. Detect: `UseCanvasResources` in a `Render` returning a Win2D canvas element without `.UseSharedDevice()`. Fix: append `.UseSharedDevice()`. †Vote was 2/5, but one REJECT rested on a *mistaken* "no such API" claim — round-3 verification confirmed the `.UseSharedDevice()` modifier and its `Win2DSharedDeviceGuard` crash-guard are real (`Win2DCanvasModifiers.cs:47`, `Win2DSharedDeviceGuard.cs`), so it is retained as a valid niche fatal-crash rule.

### §12.1 Rejected / deferred in round 3

The cross-vet also *filtered* candidates — the value of "everybody reviews
everything" was catching these:

- **Premise-wrong (rejected):** `ITEMS_VLKEY` — `VirtualList` is int-indexed **by
  design**; index keying is the intended cache path (Dsl.cs:1272). `NAV_OFFTHREADBOOL`
  — the off-thread `GoBack()`/`GoForward()` `bool` means "scheduled", not
  "succeeded", **by design** (NavigationHandle.cs). `DLG_ONCLOSEDSTALE` — the stale
  `OnClosed` closure is a **framework** mount-time-capture bug
  (`OverlayLifecycle.cs:97-104`), to fix in the reconciler, not a user-code
  analyzer. `HOOKS_014` (`UseObservable` for local state) — already caught by
  shipped `HOOKS_004` (unstable source arg).
- **Duplicate:** `CMD_DEBOUNCE` (DebounceMs without UseCommand) = shipped
  `HOOKS_009`.
- **Docs-conflict (deferred):** `LAYOUT_ATTACHEDSET` (`.Set(Canvas.SetLeft…)` →
  `.Canvas(...)`) is sound for the `Grid.SetRow` case, but `docs/guide/layout.md:244-266`
  currently *teaches* `.Set(Canvas.SetLeft/Top)`; reconcile the docs before shipping
  the rule.
- **Deferred (real, but too FP / needs heavier analysis / niche for this batch):**
  `WIN2D_002` (stale `RedrawKey`), `FLEX_001` (`.Width` vs `.Flex` — no syntactic
  intent signal, like the cut `ANIM_001`), `DATA_001` (inline `ListDataSource`),
  `DIALOG_002` (missing `OnClosed` — parent may own dismiss), `FORM_DISABLEDFOCUSABLE`
  ("submit-like" intent heuristic), `LAYOUT_STACKAUTO` (runtime `LayoutFootgunDetector`
  already covers), `THEME_REFTYPED` (low-value hygiene), `CMD_ASYNCHANDLER`
  (async-void click handlers are a widely-accepted pattern), `EFFECT_SETSTATECLEANUP`
  (legit for reset-on-dep-change), `PERF_PROPS`, `PROPS_MUTATE`, `MEMO_STALE`,
  `DND_MOVEONEND`, `GEST_SETTER`, `A11Y_ANNOUNCE`/`A11Y_FOCUSTRAP` (whole-tree
  handle-mount correlation — batch together later).
