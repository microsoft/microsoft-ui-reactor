---
name: reactor-build-and-check
description: "Building Reactor apps and reading diagnostics — `mur check <path>` is the build (same exit code as `dotnet build`, never re-run to confirm) with one-line diagnostics, skill pointers, and `→ try:` did-you-mean suggestions to use verbatim. Covers iteration vs `--final` workflow, the common-build-errors cheat table mapping `REACTOR_HOOKS_*` / `REACTOR_DSL_*` / `REACTOR_THEME_*` / `REACTOR_A11Y_*` / `REACTOR_OPT_*` / `CS*` IDs to fixes, when single-file vs `.csproj` matters for analyzer coverage, build prerequisites. Use when a build fails, you see an analyzer warning, or you want a structured diagnostic stream instead of raw MSBuild output."
---

## Build & verify

Run after every non-trivial edit. **Read the output** — `dotnet run` exits with code 1 on build failure; silent ≠ success.

### Single-file `.cs` (default for new apps)

```powershell
dotnet run App.cs -p:Platform=ARM64        # or -p:Platform=x64
```

Single-file builds **do not load analyzers**. You'll catch CS errors but not the Reactor-specific `REACTOR_*` warnings.

### `.csproj` (multi-file, analyzer coverage)

```powershell
dotnet build MyApp.csproj -p:Platform=ARM64
```

Analyzers are bundled in the `Microsoft.UI.Reactor` package and load automatically.

### `mur check` — structured output with skill pointers

```powershell
mur check MyApp.csproj                       # iteration mode (default)
mur check --final MyApp.csproj               # once iteration is clean — pre-merge sweep
```

**`mur check` is the build, not a separate check step.** It runs `dotnet build` under the hood and returns the same exit code. When `mur check` exits 0, the build is green — **do not re-run `dotnet build` to confirm**. They're the same compilation; a redundant `dotnet build` after a green `mur check` is wasted work.

Two enrichments over raw `dotnet build`:

1. **Skill pointers** for known `REACTOR_*` IDs — one-line links into the relevant skill section.
2. **Did-you-mean suggestions** for unknown identifiers, surfaced as `→ try: <name>  // [<evidence>]`.

Emits one diagnostic per line:

```
C:\path\Program.cs:15:23  W  REACTOR_DSL_001  Element produced by Select(...)…   → SKILL.md gotcha #6 (.WithKey on dynamic list items)
C:\path\Program.cs:34:16  E  CS1061  'ButtonElement' does not contain a definition for 'OnClick'   → try: Button(label, onClick: ...)  // [factory has Action onClick parameter]
```

`<path>` defaults to `.` and accepts a `.csproj`, a directory, or a single `.cs` file. Skill pointers fire only for known `REACTOR_*` IDs — vanilla `CS` errors come through with severity + code + message, plus the `→ try:` suggestion when the suggester has a high-confidence candidate.

If `mur` isn't on PATH, fall back to `dotnet build` and read the output directly. Don't spelunk the package cache for it — `mur` is published with the framework but is a separate install.

#### `→ try:` suggestions — trust them

When `mur check` emits `→ try: <name>`, use that exact name in your next edit. The suggestion has already been computed against the live Reactor surface for this exact diagnostic — **do not search adjacent or sibling names in the codebase, the skill cache, or `reactor.api.txt` to second-guess it.** If the suggestion turns out to be wrong, the next `mur check` will tell you and emit a new suggestion. That self-correcting cycle is the cheap inner loop; manual verification breaks it.

Anti-pattern: agents who treated `→ try:` as a hint to verify (re-grepping the namespace, reading `reactor.api.txt`, calling into reflection) regressed in evals because the verification cost dwarfed the cost of just trying the suggestion and letting the next build correct it.

#### Iteration vs `--final`

`mur check` (no flag) is **iteration mode**: a ranker suppresses noise (CS1591 XML-doc, CS0168 unused-var, IDE0xxx style hints, NuGet restore chatter) so you only see what's actually blocking the build. Run this inside the fix loop.

When `mur check` exits 0, you are done — the build is green. `mur check --final` is an optional pre-merge sweep that re-runs the build and emits the cosmetic/transient diagnostics the iteration ranker suppressed (XML doc gaps, unused locals, style hints, nullable warnings, NuGet restore chatter). It's the right tool for human code review or a CI ship-readiness gate. **It does not gate task completion** — running it is not required to declare done; if you choose to run it, treat any new diagnostics it surfaces as polish work, not blockers.

Additional flags:

- `mur check --strict` — promotes warnings to errors. Use for one-shot CI gates; not the inner loop.
- `mur check --quiet` — errors only. For sub-iteration loops where you want the smallest possible signal.
- `mur check -- <msbuild args>` — anything after `--` is forwarded verbatim to `dotnet build`. Override platform, config, restore, verbosity:
  ```powershell
  mur check -- -p:Platform=x64
  mur check --final -- -c Release --no-restore
  ```
  `mur` auto-injects `--nologo`, `-v:m`, and `-p:Platform={host arch}` only if you didn't already name the same flag in the passthrough section.

## Common build errors — cheat table

| ID | Severity | What it means | Fix |
|---|---|---|---|
| `REACTOR_HOOKS_001` | warning | Hook called inside `if` / `for` / `while` / `switch` / `try` | Move the hook to the top of `Render()`. Use the result conditionally, not the call. |
| `REACTOR_HOOKS_004` | warning | Hook `deps` contains a freshly-allocated object/array/lambda | Memoize with `UseMemo`, hoist to a field, or project to a scalar key. |
| `REACTOR_HOOKS_005` | warning | Hook called outside `Render()` or a custom-hook method | Move the call into `Render()` or a `Use*` helper. Hooks read slot state that only exists during render. |
| `REACTOR_HOOKS_006` | info | `UseResource` fetcher looks non-idempotent (`Post*`/`Create*`/`Delete*`/`Save*`) | Use `UseMutation` for writes — `UseResource` re-runs on deps change, retry, focus revalidation. |
| `REACTOR_HOOKS_007` | warning | `UseMemoCells` builder closure missing dependencies | Add the captured variable to the deps array. |
| `REACTOR_HOOKS_009` | warning | `Command.DebounceMs` set on a command bound without `UseCommand` | Route it through `UseCommand`: `var cmd = UseCommand(new Command { …, DebounceMs = 1500 });`. The debounce window lives in the hook store, so a raw bound `Command` never debounces. |
| `REACTOR_STATE_001` | warning | A `Component` subclass implements `INotifyPropertyChanged` (MVVM habit) | The render loop never subscribes to a component's INPC, so `PropertyChanged` is invisible and does nothing. Hold reactive state with `UseState`, or wrap an external observable source with `UseObservable`. |
| `REACTOR_HOOKS_011` | warning | Controlled input (e.g. `TextBox(name, _ => { })`) has a state-derived value but an empty/parameter-ignoring change callback — user edits are dropped (fake `Mode=OneWay`) | Feed the new value back into state: `TextBox(name, v => setName(v))`. For a genuinely read-only display, make it explicit — `TextBox(name, _ => { }).IsReadOnly(true)` — never `.IsEnabled(false)`. |
| `REACTOR_HOOKS_003` | warning | `UseEffect(async () => …)` compiles as **async void** — exceptions escape the flush pipeline, cleanup decouples from the await, the setter can fire after unmount | Move the awaited work into a local `async Task RunAsync(CancellationToken ct)` and start it from a sync effect: `UseEffect(() => { var cts = new CancellationTokenSource(); _ = RunAsync(cts.Token); return () => { cts.Cancel(); cts.Dispose(); }; }, deps);`. |
| `REACTOR_HOOKS_010` | warning | Reference state (`List`/array/class) mutated in place, then the **same instance** re-passed to its setter — the setter compares via `EqualityComparer<T>.Default`, the same instance compares equal, so no re-render is scheduled | Pass a new value: `setItems([.. items, item])`. Never `setItems(prev => …)` — the setter is `Action<T>`, not a functional updater. |
| `REACTOR_HOOKS_012` | warning | `Memo(builder, dep)` given a freshly-allocated array/`List`/plain-class dep (reference equality) — the memo never hits its stable path | Hoist the dep to a stable `UseMemo`/field or project it to a scalar key. Records/tuples compare by value and are fine. |
| `REACTOR_HOOKS_013` | warning | `UseState(new List<…>())` / `UsePersisted(key, new …())` re-allocates the initial value every render; the hook only reads it once | Wrap it in `UseMemo(() => new …(), [])` so it allocates once. Not `UseRef` — it eager-allocates too. |
| `REACTOR_CTX_001` | info | `.Provide(ctx, new …())` of a reference-equality type (plain class/array/collection) re-allocates each render and re-renders every `UseContext` consumer | Memoize it: `.Provide(ctx, UseMemo(() => new …(), deps))`, or provide a `record` (context diffs by `Equals`). |
| `REACTOR_PERF_FUNCREF` | info | `new Command { … }` built inline in `Render()`/a `Use*` hook (re-allocated every render) | Wrap it: `var save = UseMemo(() => new Command { … }, deps);` to keep a stable instance across renders. Pure allocation hygiene — deps are the render values the command captures. |
| `REACTOR_DSL_001` | warning | `Select(...)` projecting into a layout container without `.WithKey(...)` | `items.Select(i => Row(i).WithKey(i.Id)).ToArray<Element?>()`. Keys keep focus + animation state across reorders. |
| `REACTOR_DSL_002` | info | `.WithKey(...)` keyed off the list index or a per-render value (`Guid.NewGuid()`, `DateTime.Now`/`UtcNow`, `Random`, `Environment.TickCount`) | Key off the item's stable id: `items.Select((i, idx) => Row(i).WithKey(i.Id))`. An index / per-render key re-mounts rows on insert/reorder, exactly like no key. |
| `REACTOR_GRID_001` | warning | A declared `Grid` column/row that no child is placed in (unused track) | Remove the leftover `GridSize` track, or place a child there with `.Grid(row:, column:)`. Only fires when every child's placement is statically visible. |
| `REACTOR_MOD_001` | info | Same atomic-placement modifier twice in one chain (`.Grid(row: 1).Grid(column: 2)`) — atomic-replace, so `row` resets to 0 | Merge into one call: `.Grid(row: 1, column: 2)`. Applies to `.Grid`/`.Canvas`/`.RelativePanel`/`.Flex`. |
| `REACTOR_DSL_003` | warning | Typed collection (`ListView<T>`/`GridView<T>`/`LazyVStack<T>`/…) `keySelector` returns a constant/null or ignores its item | Key by a stable, unique item property: `ListView(items, i => i.Id, (i, _) => Row(i))`. A constant key collides every row → keyed-diff bailout → full list re-realization. |
| `REACTOR_THEME_001` | warning | Hardcoded color on a themed surface | Use `Theme.*` tokens (e.g. `Theme.PrimaryText`, `Theme.CardBackground`). See `reactor-design`. |
| `REACTOR_THEME_002` | info | Lightweight styling opportunity | Optional. Use `.Resources(r => r.Set("ButtonBackground", …))` for visual-state overrides. |
| `REACTOR_THEME_003` | info | `RequestedTheme` modifier available | Use `.RequestedTheme(ElementTheme.Dark)` for subtree theme overrides. |
| `REACTOR_THEME_004` | warning | Inline `new SolidColorBrush(...)` passed to `.Background`/`.Foreground`/`.WithBorder` | Use a `Theme.*` token (e.g. `Theme.SolidBackground`, `Theme.PrimaryText`) — a raw brush is a fixed color that ignores Light/Dark. |
| `REACTOR_OPT_001` | info | XAML-habit sentinel on an `Optional<T>` selection prop in `new …{ }`/`with { }` — `SelectedIndex`/`SelectedPageIndex = -1`, or a nullable `Date = null` — implicitly becomes `Optional.Of(sentinel)`, a force-assert re-applied every render | Use `Optional<T>.Unset` to let the control own the selection, or `Optional<T>.Of(value)` to keep the explicit force-assert (e.g. `Optional<int>.Of(-1)`, `Optional<DateTimeOffset?>.Of(null)`). |
| `REACTOR_A11Y_001` | warning | Icon-only button missing accessible name | Add `.AutomationName("Delete")` (or similar). |
| `REACTOR_A11Y_002` | warning | Image missing alt text | Add `.AutomationName(...)` or `.AccessibilityHidden(true)` for decorative images. |
| `REACTOR_A11Y_003` | warning | Form field missing label | Wrap in `FormField(input, label: "Email", required: true)`. |
| `REACTOR_PERSIST_001` | warning | 2-arg `UsePersisted(key, initial)` defaults to process-wide `PersistedScope.Application` | Pass an explicit scope: `PersistedScope.Window` (host lifetime) or `PersistedScope.Application` (make current behavior explicit). |
| `REACTOR_THREAD_002` | warning | Blocking a `Task` (`.Result` / `.Wait()` / `.GetAwaiter().GetResult()`) inside `Render()` or a `UseEffect` effect | Never block on the UI thread. Fetch with `UseResource(ct => FetchAsync(ct), System.Array.Empty<object>())`, or `await` inside an async effect and set state. |
| `REACTOR_CMD_001` | info | Raw-init element sets **both** `Command` and its own `OnClick` / toggle callback | The callback wins (`EffectiveCallback = userCallback ?? Invokable(cmd)`), so the command never runs. Delete the redundant callback, or bind via the `.Command(...)` modifier / `Button(cmd)` factory (which never set a callback). |
| `REACTOR_THREAD_001` | warning | UI-thread-only member (window / tray / taskbar mutator) called inside a `Task.Run` / `Task.Factory.StartNew` / `ThreadPool.QueueUserWorkItem` lambda | Marshal it back: `var d = ReactorApp.UIDispatcher; if (d is null) window.Close(); else d.TryEnqueue(() => window.Close());`. Null-safe because the dispatcher is null until the first window bootstraps. |
| `REACTOR_ITEMS_001` | warning | `.Set(x => x.ItemsSource = ...)` on a Reactor-owned collection (ListView/GridView/TreeView/TabView/Pivot/FlipView/SelectorBar) | Pass the data through the element's `items` factory argument — Reactor owns the items via keyed reconciliation. (AutoSuggestBox is exempt.) |
| `REACTOR_ITEMS_002` | warning | An `ItemsView(...)` `viewBuilder` returns something whose static type can never be an `ItemContainerElement` — a lambda return (`(p, i) => Border(...)`) or a method group whose declared return type is a concrete non-container | Wrap the returned element: `(p, i) => ItemContainer(Border(...))`. ItemsView's inner `ItemsRepeater` requires an `ItemContainer` root — without it the framework throws at mount time. The lambda form ships a code fix. Silent when the return type is only known as `Element`, is a type parameter, or is a conditional with mixed branch types. |
| `REACTOR_CTRL_001` | warning | `.Set(x => x.SelectedItem/SelectedValue = ...)` on a selector that also sets `SelectedIndex` | Delete the `.Set(...)` — controlled `SelectedIndex` is the authority. Don't drive selection from two places. |
| `REACTOR_VIS_001` | warning | Imperative `.Set(c => c.Visibility = Visibility.Collapsed)` | Use `.IsVisible(false)` / `.IsVisible(true)` (or conditional inclusion `cond ? el : null`). `.Set` writes aren't reconciled. |
| `REACTOR_EVENT_001` | warning | Event subscription via `.Set(c => c.Event += h)` (re-subscribes every render) | Use the declarative `.On<Event>(h)` modifier where one exists, else `.OnMountAdd(c => ((TControl)c).Event += h).OnUnmountAdd(c => ((TControl)c).Event -= h)` with a stable `h` (static method or field). |
| `REACTOR_MOD_002` | info | `.Set(c => c.Prop = v)` where a first-class `.Prop(v)` modifier exists | Use the modifier. `.Set` setters re-run every render, are never unwound when a later render drops them, and `Element.SettersEqual` pins the element to the reconciler's update path. The fix rewrites the whole `.Set` — including multi-statement bodies — into a modifier chain. |
| `REACTOR_MOD_003` | warning | A common modifier that `ApplyModifiers` never writes to this element's control (`.Background(...)` on a `Rectangle`, `.Padding(...)` on a `Canvas`, `.CornerRadius(...)` on an `Image`) | The call compiles and is silently discarded. On a shape use the paint modifier (`.Fill(...)` / `.Stroke(...)` / `.StrokeThickness(...)` — the fix does the rewrite); otherwise host the element in a control that supports it. Gates: `Background` → Panel/Control/Border; `Foreground` and fonts → Control/TextBlock; `Padding` → Control/Border/Grid/StackPanel/RelativePanel/TextBlock; `CornerRadius` → Control/Border/Grid/StackPanel/RelativePanel; `BorderBrush`/`BorderThickness` → Control/Border. |
| `REACTOR_POOL_001` | warning | `.Set` writes a property `ElementPool.CleanElement` clears on pool return — either an instance write `c.Prop = v` (layout/appearance: `Margin`, `Width`/`Height`, `Min`/`Max` sizes, `HorizontalAlignment`/`VerticalAlignment`, `Opacity`, `AccessKey`, `IsTabStop`, plus `Padding`, `CornerRadius`, `BorderThickness`, `BorderBrush`, `Background` and `IsEnabled` on their supported receivers) or an attached setter `Owner.SetProp(c, v)` (`AutomationProperties.*`, `ToolTipService.*`, `TitleBar.IsDragRegion`, `FlexPanel.*`). **Receiver-aware:** fires only when the `.Set` lambda parameter's *exact* type is one `ElementPool` recycles — `TextBlock`, `RichTextBlock`, `StackPanel`, `Grid`, `Border`, `ScrollViewer`, `Canvas`, `Viewbox`, `ProgressBar`, `ProgressRing`, `Image`, `InfoBadge`, `Button`, `TextBox`, `ToggleSwitch`. The identical write on a receiver the pool never holds (`CheckBox`, `RelativePanel`, a user subclass of a pooled type) reports `REACTOR_MOD_002` instead — the write is still dropped by the next render, it just isn't the pool that drops it. | Use the surviving modifier — the value is stored on `Element.Modifiers` and re-applied every render, whereas the `.Set` write is silently discarded when the control is reused. Most map 1:1 and ship a fix (`.Padding(...)`, `.Background(...)`, `AutomationProperties.SetName` → `.AutomationName(...)`, `ToolTipService.SetToolTip` → `.ToolTip(...)`); the rest are diagnostic-only because the modifier's shape differs — `.PositionInSet(position, size)`, `.Required()`, `.Flex(grow: ...)`. |
| `REACTOR_A11Y_004` | warning | Clickable container (`Border`/`Grid`/`Canvas`/`Rectangle`/`Ellipse`/`VStack`/`HStack`) has `.OnTapped` but is not keyboard-reachable | Add `.IsTabStop(true)` and pair with `.OnKeyDown` for Enter/Space activation. |
| `REACTOR_INPUT_001` | warning | Ctrl/Alt chord tested inside a `.OnKeyDown` lambda (focus-scoped, fires only while the element has focus) | Register it app-wide as a `Command` accelerator: `new Command { …, Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control) }`, then drop the `.OnKeyDown` chord. |
| `REACTOR_ANIM_002` | info | `.Keyframes(name, trigger, …)` trigger changes every render (`DateTime.Now`, `Guid.NewGuid()`, a fresh allocation) | Pass a stable trigger — a `UseState`/`UseReducer` counter you increment only when you mean to retrigger. Recomputed values restart the animation each reconcile (flicker). |
| `REACTOR_INPUT_002` | warning | `TryGetFiles` in an `.OnDrop(...)` handler — accepts UNC / DOS-device / reparse-point / shell-virtual files the drag source chose | Swap to `.TryGetSafeLocalFiles(out var files)` — same `bool(out IReadOnlyList<IStorageItem>)` signature, filters to safe local paths (UNC triggers SMB/NTLM auth, reparse points escape the shared dir, MOTW is lost). |
| `REACTOR_NAV_001` | warning | `UseNavigation` handle stashed in a `static` field or property | Don't stash the handle statically — it outlives the page and pins its dispatcher. Get the shared handle from a descendant with child-mode `UseNavigation<TRoute>()` (no initial value), or pass it through `Context`. |
| `REACTOR_DIALOG_001` | warning | WinUI `ContentDialog.ShowAsync()` opened imperatively from a handler | Model it declaratively: `ContentDialog(title, content) with { IsOpen = open, OnClosed = _ => setOpen(false) }`. The dialog stays in the tree and `IsOpen` controls visibility — the imperative dialog has no parent theme and can't be tested. |
| `REACTOR_MEDIA_001` | info | `WebView2` is a direct child of an auto-layout stack (`HStack`/`VStack`/`FlexRow`/`FlexColumn`) with no explicit size | Pin `.Width(...)` and `.Height(...)` (or host it in a fixed-size `Grid` cell). Unsized, WebView2 measures to its web content and oscillates as the page reflows. |
| `REACTOR_ANIM_003` | warning | `async` lambda passed to `AnimationScope.WithAnimation` / `WithAnimationAsync` | The lambda is `async void`, so mutations after `await` run with an empty `[ThreadStatic]` scope and don't animate. Split into a `WithAnimation` call per phase around each `await`. Passing an async lambda to `WithAnimationAsync` won't help either — it also takes an `Action`. |
| `REACTOR_LIFECYCLE_002` | warning | `UseEffect(() => …)` allocates a timer / subscription / event with no cleanup | Return a cleanup from the effect (picks the `Func<Action>` overload): `UseEffect(() => { var t = new PeriodicTimer(…); …; return () => t.Dispose(); }, …);`. The `Action` overload can't tear down, so the producer outlives the component and can keep firing after unmount. |
| `REACTOR_MEMO_001` | info | A fluent modifier is applied to a keyed `Memo(key, factory)` wrapper, so the row opts out of the virtualized cross-recycle cache | Move the modifier(s) inside the factory: `Memo(id, () => Row(item).Padding(8))` instead of `Memo(id, () => Row(item)).Padding(8)`. Only a bare keyed-Memo wrapper is cached — fold any state the moved modifiers read into the key (e.g. `Memo((id, isSelected), …)`) or a cache hit can serve stale content. |
| `REACTOR_DYM_001` | warning | A Reactor property/field is invoked like a method (e.g. `GridSize.Auto()`) — pairs with compiler `CS1955` | Drop the parentheses: `GridSize.Auto`. `Auto` is a property; `Star(…)`/`Px(…)` are the method factories. The IDE offers a one-click "Remove parentheses" fix. |
| `REACTOR_DYM_002` | warning | An invented `Theme.*Background` token (e.g. `Theme.AppBackground`) — pairs with compiler `CS0117` | Use the real surface-background token `Theme.SolidBackground`; `Theme.LayerBackground` → `Theme.LayerFill`. The IDE offers a one-click rename fix. |
| `REACTOR_DYM_003` | warning | An unresolved bare call closely matches a Reactor factory name (e.g. `Buton(...)`) — pairs with compiler `CS0103` | Rename to the suggested factory: `Button(...)`. Fires only on a close, unambiguous factory match (the typo flavour of `CS0103`); the IDE offers a one-click rename. |
| `REACTOR_DYM_004` | warning | A Reactor factory is called with too few arguments (e.g. `ScrollViewer()`) — pairs with compiler `CS7036` | Supply the missing argument(s); the message lists the factory's full parameter shape as named arguments. Fires only when a single overload uniquely matches, so multi-overload factories (e.g. `Button()`) are deliberately left to the raw compiler error. Message only — no code fix (the `<Element>` placeholder wouldn't compile). |
| `REACTOR_DYM_005` | warning | A `string` is passed where a Reactor `Element` is expected (e.g. `ScrollViewer("hi")`) — pairs with compiler `CS1503` | Wrap the string in a text factory: `ScrollViewer(TextBlock("hi"))` (or `Heading`/`Caption`). Narrow, high-confidence special case only; general type mismatches degrade to the raw `CS1503`. Message only — no code fix. |
| `CS0103` | error | "The name 'X' does not exist in the current context" | Missing `using` — most often `Microsoft.UI.Reactor.Layout` (FlexAlign), `Microsoft.UI.Xaml.Controls` (InfoBarSeverity, Orientation), or `static Microsoft.UI.Reactor.Factories`. (A close-but-wrong factory name instead raises `REACTOR_DYM_003` above.) |
| `CS1061` | error | "'X' does not contain a definition for 'Y'" | A type-specific modifier called on an element type that does not declare it — `.Bold()` / `.FontSize()` / `.TextWrapping()` are `TextBlockElement`-only, so `Button("Go").Bold()` fails. This is **not** an ordering problem: modifiers are generic (`<T> where T : Element`) and preserve the concrete type, so `.Margin(16).Bold()` and `.Bold().Margin(16)` compile alike. Check the receiver's element type against `reactor-dsl/references/reactor.api.txt`. |
| `CS0117` | error | "'Element' does not contain a definition for X" | You're calling a factory or static member that doesn't exist — confirm the name against `reactor-dsl/references/reactor.api.txt`. (For a missing *instance* modifier see `CS1061` above.) |
| `MSB4025` | error | "The project file could not be loaded" | Single-file `.cs` build attempted without `-p:Platform=...` on a WinUI project. Add `-p:Platform=ARM64` (or x64). |
| `NETSDK1136` | error | "platform required" | Same fix — pass `-p:Platform=ARM64` or `x64`. |

If a `REACTOR_*` ID isn't in this table, the bundled analyzer DLL has more docs. The descriptions ship in the warnings themselves.

## Iteration discipline

- **`mur check` is the build.** Same exit code as `dotnet build`. Don't re-run `dotnet build` to confirm a green `mur check` — it's redundant work on the same compilation.
- **Trust `→ try:` suggestions directly.** They're precomputed against the actual Reactor surface for the exact diagnostic. Use the suggested name verbatim; don't grep adjacent or sibling names. If it's wrong, the next `mur check` will say so — that's the self-correcting loop.
- **Batch fixes.** Read every error/warning in one pass, fix them all, then re-build. Don't re-build after each single fix.
- **`mur check` in the loop. When it exits 0, you are done.** Iteration mode suppresses cosmetic noise so the real blocker doesn't scroll off attention. `mur check --final` is an optional pre-merge sweep for human review / CI gates — not a task-completion requirement; skipping it is fine.
- **Don't introspect via `[System.Reflection]`.** Enumerating Reactor types or members at runtime to "discover" the API is unnecessary and slow. This cheat table plus `mur check`'s did-you-mean suggestions plus `reactor-dsl/references/reactor.api.txt` cover the surface.
- **Trust the analyzer over your memory.** If `REACTOR_DSL_001` says "missing `.WithKey`", add `.WithKey(...)` — the analyzer is right.
- **Don't bypass.** Avoid `#pragma warning disable REACTOR_*` unless you have a specific known reason. The analyzers exist because the runtime symptoms are subtle (focus loss, identity drift, refetch storms).

## Prerequisites

| Requirement | Minimum | Install |
|---|---|---|
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| `mur` (optional) | latest | Build from source: `dotnet build src/Reactor.Cli`. Selfhost only today. |
| Microsoft.UI.Reactor | 0.0.0-local (selfhost) or a published version | Selfhost: `mur pack-local`. Consumer: `<PackageReference>` in `.csproj`. |
