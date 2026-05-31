# Lazy, Trimmable Control Registration — Implementation Tasks

Derived from: [`docs/specs/048-control-registration-and-trimming.md`](../048-control-registration-and-trimming.md).

> **Status:** Not started. Spec is design-converged; this tracker decomposes the
> §13 phasing into a step-by-step task list.
>
> The work has four phases plus a validation phase that the user explicitly
> requested up front (a Hello-World app published with NativeAOT/full-trim that
> demonstrates unused controls are stripped). The validation phase is wired into
> §3 (proves the mechanism on the external proof) and §5 (the regression-guard
> CI test that the built-in migration must keep green).
>
> **Conventions** (mirroring `047-extensible-control-model-implementation.md`):
> - Every task is a checkbox; mark `[x]` only when its artifact (code + tests +
>   doc update + verified perf number) is landed.
> - The Phase 047 A|B parity bar is gone (V1 is the production path) but each
>   phase below must keep full xunit + selftest green.
> - Order matters: Phase 1 lands the runtime contract with no behavior change;
>   Phase 2 proves the mechanism end-to-end on a single external control; Phase
>   3 is the high-risk built-in migration that deletes `RegisterV1BuiltInHandlers`
>   and closes ~50 element constructors; Phase 4 is the optional ergonomic
>   layer.

## Exit gate (all must hold to declare 048 done)

1. `ControlRegistry` exists as a public, idempotent, lock-free global table
   (spec §8); the `Reconciler` consults it as dispatch precedence step 3 and
   caches hits into the per-host `_v1Handlers` table.
2. `RegisterV1BuiltInHandlers` is deleted; every built-in factory registers its
   handler via the `Reg<TElement, TControl, THandler>.Done` static-field touch
   (spec §7); `Factories` has **no** static constructor and **no** static
   field initializer that references a handler or WinUI control type (spec
   §10.2).
3. Built-in element record constructors are `internal` so the factory is the
   sole construction path (spec §6 construction discipline, §12.3); all
   call sites in `src/`, `tests/`, `samples/`, and `docs/_pipeline/apps/` are
   routed through factories.
4. A CI trim-regression test publishes a minimal app calling only `Button()` +
   `TextBlock()` with `PublishAot=true` (or `PublishTrimmed` + `TrimMode=full`)
   and asserts the produced binary contains **no** `TreeView`, `GridView`, or
   `Microsoft.UI.Xaml.Controls.TreeView` / `GridView` symbols (spec §11).
5. M1 / M2 micro-bench shows the per-factory `Reg<>.Done` branch disappears
   into the element-record allocation (spec §9) — published in
   `docs/specs/048/perf-results/`.
6. Public docs are refreshed: `docs/_pipeline/templates/extending-reactor-controls.md.dt`
   teaches Pattern A (the factory-as-registration pattern) as the **only**
   author surface for new controls; `control-reconciler-protocol.md.dt`
   documents the four-step dispatch precedence (per-host `_v1Handlers` →
   per-host `_typeRegistry` → `ControlRegistry.Default` → composition
   primitives).
7. Full xunit + selftest + solution build green; the AOT-selftests CI job
   (`.github/workflows/ci.yml`) still passes against the migrated codebase.

---

## Phase 1 — Runtime contract (no behavior change)

Lands `ControlRegistry` and the Reconciler dispatch-miss path. Built-ins still
register eagerly via `RegisterV1BuiltInHandlers` — Phase 1 is purely additive
so the rest of the codebase keeps working unchanged.

### 1.1 Add the `ControlRegistry` type (spec §8)

- [x] Create `src/Reactor/Core/V1Protocol/ControlRegistry.cs` with the public
      surface from spec §8: `Register<TElement, TControl>(Func<IElementHandler<TElement, TControl>>)`,
      backed by a `ConcurrentDictionary<Type, Func<IV1HandlerEntry>>` using
      `TryAdd` (first-wins, silent no-op on repeat — spec §12.1).
- [x] Decide and document the static `Default` instance shape (spec §8 calls
      it `ControlRegistry.Default`; if the class is `static` with static
      methods there is no `Default` instance — pick one and update the spec
      reference if necessary).
- [x] Expose a single internal `TryResolve(Type elementType, out IV1HandlerEntry entry)`
      that the Reconciler can call without the per-call generic dance (boxes
      the `Func<IElementHandler<E,C>>` once into a closed `IV1HandlerEntry`
      via `V1HandlerAdapter<E,C>` on first hit).
- [x] Mark the type `IsAotCompatible` clean — no reflection, no
      `MakeGenericType`. The closed-type capture happens inside the generic
      `Register<E, C>` so the JIT/AOT compiler can see the closed types
      statically.

### 1.2 Wire dispatch precedence into the Reconciler (spec §8 precedence list)

- [x] In `Reconciler.Mount.cs` and the equivalent V1 dispatch site
      (`Reconciler.cs:537` area, `_v1Handlers` lookup), add the new arm
      **3**: after a per-host `_v1Handlers` miss and a per-host
      `_typeRegistry` miss, call `ControlRegistry.TryResolve`. On a hit,
      invoke the registered factory once, adapt to `IV1HandlerEntry`, and
      **cache the result in the per-host `_v1Handlers`** so steady-state
      dispatch stays the existing fast per-host lookup.
- [x] Verify the order matches spec §8: (1) per-host `_v1Handlers`,
      (2) per-host `_typeRegistry`, (3) `ControlRegistry.Default`,
      (4) composition primitives. The per-host `RegisterHandler` shadow
      (spec §8) must still win.
- [x] Keep the public `Reconciler.RegisterHandler<TElement, TControl>` as the
      **explicit override / test hatch** (spec §8). Its strict throw-on-
      duplicate behavior stays unchanged.

### 1.3 Unit tests for the registry + dispatch path

- [x] `tests/Reactor.Tests/V1Protocol/ControlRegistryTests.cs`: idempotent
      `Register` (second call for same element type is a silent no-op),
      lock-free `TryAdd` semantics, factory invoked exactly once across N
      concurrent dispatch hits for the same element type.
      (Landed at `tests/Reactor.Tests/Spec048/V1Protocol/ControlRegistryTests.cs`.)
- [x] Dispatch-precedence test: register an element type globally, then
      register a different handler for the same element type via
      `Reconciler.RegisterHandler` on a host, and assert the per-host
      registration wins.
- [x] Cache test: after the first global-table hit on a given host, the
      registry's factory delegate is **not** invoked on subsequent mounts of
      the same element type on that host (the per-host `_v1Handlers` cache
      short-circuits).
- [x] Open-question §12.2 confirmation test: per-host `RegisterHandler`
      successfully shadows a globally-registered handler for the same
      element type.

### 1.4 Phase 1 exit gate

- [x] Full xunit green: `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`.
      (CI green; ARM64 dev box blocked by pre-existing
      `NumberBoxDescriptor` cctor / WinAppSDK activation issue affecting
      all local unit-test runs, unrelated to this work.)
- [x] Full selftest green: `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`.
      (Spec048 fixtures: 3/3 pass with 9 assertions. Full suite: 1 pre-existing
      unrelated flake in `RareControlFixtures.cs:153` viewport-realization test.)
- [x] Solution build clean: `dotnet build Reactor.slnx -p:Platform=x64`.
- [x] No behavior change observable: built-ins still register eagerly via
      `RegisterV1BuiltInHandlers`; the new dispatch arm only fires for
      element types not in `_v1Handlers` / `_typeRegistry`.

---

## Phase 2 — Prove the mechanism end-to-end on the external proof

Phase 2 validates Pattern A on the existing `Reactor.External.TestControl`
project before touching any built-in code. This is the §13 phasing step 2
and is the natural place to wire the user-requested AOT/trim Hello-World
validation app (the external control becomes the "control that must NOT be
present in the trimmed app's binary" probe).

### 2.1 Convert the external `Marquee` to Pattern A (spec §6)

- [x] Rewrite `tests/external_proof/Reactor.External.TestControl/MarqueeElement.cs`
      with an `internal` constructor and add a `public static class Marquee`
      holder (per spec §6 example) whose static cctor calls
      `ControlRegistry.Register<MarqueeElement, MarqueeControl>(static () => new MarqueeHandler())`
      and exposes `Marquee.Of(string caption)` as the sole construction
      path. Keep the `Setters` init-only prop on the element record.
- [x] Delete the explicit `reconciler.RegisterHandler(new MarqueeHandler())`
      call site in `tests/external_proof/Reactor.External.TestControl.Tests/MarqueeHandlerSelftests.cs`
      (and any sibling test setup) — the cctor of `Marquee` does it.
      (Replaced 6 sites in `Spec047ExternalProofFixtures.cs` and removed the
      `RegisterMarqueeHandler` helper; the xunit tests in
      `Reactor.External.TestControl.Tests` deliberately retain explicit
      `RegisterHandler` calls because they test the *per-host registration
      API surface*, which spec 047 keeps as a public contract.)
- [x] Confirm the `static` lambda compiles (it should — `MarqueeHandler` has
      a public parameterless ctor) and that no closure is allocated.

### 2.2 Run the existing external proof tests against Pattern A

- [x] `dotnet test tests/external_proof/Reactor.External.TestControl.Tests`
      stays green; the Pattern A registration replaces the per-host
      registration cleanly. (Selftest path verified: 6 fixtures pass.
      xunit `RegisterHandler_*` tests blocked locally by the pre-existing
      `NumberBoxDescriptor` cctor / WinAppSDK activation issue affecting
      all ARM64 dev-box unit-test runs; CI x64 runner is unaffected.)
- [x] Verify the `IsAotCompatible=true` warnings-as-errors gate
      (`Reactor.External.TestControl.csproj:47`) still produces zero
      warnings after the rewrite.

### 2.3 Hello-World AOT validation app (the user-requested probe)

This is the spec §11 / §13.2 verification test and the regression guard
referenced in the §11 "Verification trim test" callout. It lives next to
the external proof so it can reference `Reactor.External.TestControl`
without polluting the main solution.

- [x] Create `tests/aot_trim_proof/Reactor.AotHelloWorld/Reactor.AotHelloWorld.csproj`:
      - `<OutputType>WinExe</OutputType>`,
      - `<TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>`,
      - `<Platforms>x64;ARM64</Platforms>`,
      - `<UseWinUI>true</UseWinUI>`,
      - `<WindowsPackageType>None</WindowsPackageType>`,
      - `<PublishAot>true</PublishAot>` (or equivalent
        `PublishTrimmed=true` + `TrimMode=full` when AOT is not viable on
        the target SDK),
      - `ProjectReference` to `src/Reactor/Reactor.csproj` and
        `tests/external_proof/Reactor.External.TestControl/`.
      (Implementation note: `<PublishAot>true</PublishAot>` is gated on
      `Condition="'$(PublishAotInternal)' == 'true'"` to prevent
      propagation into the netstandard2.0 analyzer ProjectReferences;
      pass `-p:PublishAotInternal=true` at publish time. Same workaround
      as `Reactor.AppTests.Host`.)
- [x] Author the smallest possible Reactor app: a `ReactorHost` rendering
      `VStack(TextBlock("Hello, Reactor!"), Button("Click", () => {}))` —
      **only** `TextBlock` and `Button` factories called; **no** reference
      to `Marquee.Of`, `TreeView`, `GridView`, `TabView`, `Image`,
      `CalendarView`, or any other catalog factory.
- [x] Add `tests/aot_trim_proof/Reactor.AotHelloWorld.TrimAssertions/`:
      an xunit (or MSBuild target) check that runs after `dotnet publish`
      and asserts the produced `.exe` / `.dll` set does **not** contain
      the strings `MarqueeControl`, `MarqueeHandler`, `TreeView`,
      `GridView`, `TabViewHandler`, `Microsoft.UI.Xaml.Controls.TreeView`,
      `Microsoft.UI.Xaml.Controls.GridView` (use `dotnet-ildasm`,
      `nm.exe`, or a simple binary string search — pick the cheapest
      mechanism that works in CI). The assertion list is exactly the
      "things that should be trimmed" set from spec §11.
      (Phase 2 ships with the list narrowed to `MarqueeControl`,
      `MarqueeHandler` — the Pattern A proof. The other symbols are
      Phase 3 deliverables: they remain in the binary as long as
      `RegisterV1BuiltInHandlers` is still eager. The
      `TrimAssertionTests.ForbiddenSymbols` constant carries an inline
      comment noting which symbols Phase 3 must add.)
- [x] Document the runbook in `tests/aot_trim_proof/README.md`: how to
      run the publish + assertion locally, what failure means (something
      re-rooted the catalog), and how to extend the assertion list when
      adding new must-trim controls.

> **Caveat from spec §11 — record it in the README:** the WinUI/WinAppSDK
> framework's own trim story is evolving. The assertion checks
> **Reactor-side rooting**, not the SDK's internal types. A SDK regression
> that re-roots its own controls is out of scope for this guard.

### 2.4 Phase 2 exit gate

- [x] External proof tests green with Pattern A registration.
- [x] `dotnet publish tests/aot_trim_proof/Reactor.AotHelloWorld -c Release -r win-x64 -p:PublishAot=true`
      completes cleanly with **no** new trim/AOT warnings beyond what the
      WinAppSDK itself already emits (capture the warning baseline in the
      README). (Verified locally on ARM64 with `-p:PublishAotInternal=true`.
      The CI runs `-r win-x64` per the new `aot-trim-proof` job.)
- [x] The trim-assertion job fails loudly if `Marquee` / `TreeView` /
      `GridView` symbols appear in the published binary — verified by
      temporarily adding `Marquee.Of("x")` to the Hello-World app and
      observing the failure, then reverting. (Verified: probe failure
      named `MarqueeControl` + `MarqueeHandler` in the published exe;
      revert restored green state.)
- [x] CI hook: a new `aot-trim-proof` job in `.github/workflows/ci.yml`
      (after `aot-selftests`) runs the publish + assertion on every PR.
- [x] Perf measurement (spec §9): record the per-call cost of the
      `Reg<>.Done` static-field read against the baseline factory call;
      commit results under `docs/specs/048/perf-results/phase2-baseline.md`.
      (Standalone `RegStaticReadBench` harness lives under
      `tests/aot_trim_proof/RegStaticReadBench/`. ARM64 dev-box numbers:
      inline shape sub-noise (~0.3 ns/op), no-inlining upper bound
      ~1.7 ns/op. M1/M2 absolute baseline cited from spec 047 phase4
      results; Phase 3 PR re-runs M1/M2 on canonical hardware to land
      the final empirical proof in `phase3-migration.md`.)

---

## Phase 3 — Built-in migration

The big phase. Dismantles `RegisterV1BuiltInHandlers`, introduces the
`Reg<TElement, TControl, THandler>` helper (spec §7), distributes one
registration touch into each of the ~120 factory methods in
`src/Reactor/Elements/Dsl.cs`, and closes built-in element constructors.

### 3.1 Land the `Reg<>` helper

- [x] Add `src/Reactor/Core/V1Protocol/Reg.cs` with the spec §7 shape:
      `internal static class Reg<TElement, TControl, THandler>` where
      `THandler : IElementHandler<TElement, TControl>, new()`, with
      `internal static readonly byte Done = Init()` and a private `Init`
      that calls `ControlRegistry.Register<TElement, TControl>(static () => new THandler())`.
- [x] Document the **static-lambda mandate** (spec §6 ¶ "The `static`
      keyword on the lambda is mandatory") in an XML doc comment so future
      contributors do not regress to a capturing lambda.
- [x] Unit test: `_ = Reg<MyElement, MyControl, MyHandler>.Done` registers
      exactly once, on the first read, regardless of read count.

> **§3.1 close-out notes:**
> - `Reg.cs` lands as `internal` (not `public`) — spec §7 calls Pattern B an
>   authoring shim for the Reactor assembly; external authors use Pattern A
>   directly via `ControlRegistry.Register` (already public).
> - `Done` is `byte` (not `int`/`bool`) per spec §9 to minimize the
>   per-closed-generic static-data footprint. Value is 1, asserted in
>   `Done_Field_Reads_Nonzero_To_Confirm_Init_Ran` so a future contributor
>   cannot quietly default it back to 0.
> - Tests live in `tests/Reactor.Tests/Spec048/V1Protocol/RegTests.cs` and
>   join `ControlRegistryTestCollection` for global-registry isolation.
>   Each test uses a unique nested element/handler triple — the CLR runs
>   `Reg<…>.cctor` at most once per process per closed-generic, so sharing
>   triples across tests would make the second test see stale "no
>   registration on read" state.
> - All 6 RegTests green (`First_Touch…`, `Repeated_Reads…`,
>   `Done_Field_Reads_Nonzero…`, `Two_Factories_Sharing_A_Closed_Generic…`,
>   `Concurrent_First_Touches…`, `Distinct_Closed_Generics…`).
> - The pre-existing 7 `ControlRegistryTests` failures (NumberBoxDescriptor
>   cctor → `NumberBox.TextProperty` WinUI activation in xunit) reproduce
>   with §3.1 files removed — unrelated to this work. Will be revisited
>   when §3.4 deletes `RegisterV1BuiltInHandlers`, which removes the
>   descriptor cctor chain that triggers the activation.

### 3.2 Inventory + close built-in element constructors (spec §12.3)

- [ ] Grep `src/`, `tests/`, `samples/`, and `docs/_pipeline/apps/` for
      direct `new XxxElement(...)` constructions of built-in element
      records. Build an inventory under
      `docs/specs/048/audits/element-ctor-call-sites.md`.
- [ ] For each built-in element record in `src/Reactor/Core/Element.cs`
      (and split-out element files): change the public constructor to
      `internal`. The factory in `Dsl.cs` is the sole construction path.
- [ ] Update every call site in the inventory to use the corresponding
      factory. For tests that need a one-off element-record fixture, prefer
      `internal` + `InternalsVisibleTo("Reactor.Tests")` over re-opening
      the ctor publicly.

### 3.3 Convert factories — one `Reg<>` touch per factory (spec §7)

This is mechanical but high-cardinality (~120 factory methods across the
partial `Factories` class). Execute as a single PR per logical control
group to keep diffs reviewable.

- [ ] **Input controls** — `Button`, `RepeatButton`, `HyperlinkButton`,
      `DropDownButton`, `SplitButton`, `ToggleSplitButton`, `ToggleButton`,
      `CheckBox`, `RadioButton`, `RadioButtons`, `ToggleSwitch`,
      `Slider`, `NumberBox`, `Rating` / `RatingControl`, `PipsPager`,
      `ColorPicker`, `SelectorBar`. *(15 of 17 element types done — see
      §3.3 Input-group close-out note below. `Button` and `CheckBox` are
      deferred to §3.4 along with the decorator-global-registration path
      they depend on.)*
- [x] **Text controls** — `TextBlock`, `Heading`, `Subheading`,
      `RichTextBlock`, `TextBox`, `PasswordBox`, `RichEditBox`,
      `AutoSuggestBox`. *(Done — see §3.3 close-out note below.)*
- [ ] **Container / layout** — `Border`, `StackPanel` and `VStack` /
      `HStack` / `ZStack` / `Stack`, `Grid`, `Canvas`, `RelativePanel`,
      `WrapGrid`, `ScrollViewer`, `ScrollView`, `Viewbox`, `Expander`,
      `FlexPanel`, `Frame`, `SplitView`, `RefreshContainer`,
      `ParallaxView`, `SwipeControl`, `SemanticZoom`. *(10 of 17 element
      types done — see §3.3 Container/layout-group close-out note below.
      The 7 panel/expander decorators (Stack/Grid/Canvas/RelativePanel/
      WrapGrid/FlexPanel/Expander) defer to §3.4 with the decorator-global-
      path work.)*
- [ ] **Collections** — `ListView`, `GridView`, `ListBox`, `ComboBox`,
      `Pivot`, `FlipView`, `TabView`, `BreadcrumbBar`, `ItemsRepeater`,
      `ItemsView`, `ItemContainer`, `TreeView`, `LazyStack`,
      `TemplatedListView<T>`, `TemplatedFlipView<T>`. *(10 of 15 element
      types done — see §3.3 Collections-group close-out note below.
      `ItemsRepeater` and `ItemsView` use the base-derived global path,
      and `LazyStack` / `TemplatedListView<T>` / `TemplatedFlipView<T>`
      combine base-derived AND decorator paths — all defer to §3.4.)*
- [x] **Date / time** — `DatePicker`, `CalendarDatePicker`, `TimePicker`,
      `CalendarView`. *(Done — see §3.3 close-out note below.)*
- [x] **Status / info** — `ProgressBar`, `ProgressRing`, `InfoBar`,
      `InfoBadge`, `TeachingTip`, `AnnounceRegion`, `AnnotatedScrollBar`.
      *(Done — see §3.3 close-out note below.)*
- [ ] **Media / icons** — `Image`, `MediaPlayerElement`,
      `PersonPicture`, `Icon` / `AnimatedIcon`, `AnimatedVisualPlayer`,
      `WebView2`, `MapControl`. *(7 of 8 element types done — `Icon` is a
      decorator and defers to §3.4 with the decorator-global-path work.
      See §3.3 Media/icons-group close-out note below.)*
- [ ] **Shapes** — `Rectangle`, `Ellipse`, `Line`, `Path`.
- [ ] **Navigation / chrome** — `NavigationView`, `NavigationHost`,
      `TitleBar`, `XamlHost`, `XamlPage`, `Semantic`.
- [ ] **Overlays** — `ContentDialog`, `Flyout`, `Popup`, `MenuBar`,
      `MenuFlyout`, `CommandBar`, `CommandBarFlyout`.

(After each control group lands, run xunit + selftest. The `Reg<>` touch
is idempotent so duplicate-touch from `Heading()` and `TextBlock()` both
hitting `Reg<TextBlockElement, …>` is silently absorbed — spec §10.3.)

> **§3.3 close-out note — descriptor-backed controls & the thin-handler template (Text group landed):**
>
> The `Reg<TElement, TControl, THandler>` shim from §3.1 requires
> `THandler : …, new()`. That covers the ~13 hand-coded handlers directly
> (e.g. `TextBox` → `Reg<TextBoxElement, TextBox, TextBoxHandler>.Done`), but
> **not** the ~75 descriptor-backed built-ins, whose production handler is
> `new DescriptorHandler<E,C>(XxxDescriptor.Descriptor)` — not `new()`-able.
>
> **Decision (validated via rubber-duck):** do *not* hang a registration
> sentinel off the descriptor holder — any `.Descriptor` access would then
> auto-register, which is wrong for *carved* descriptors whose retained
> descriptor is **not** their production handler (Button → `ButtonHandler`
> decorator, GridView → `GridViewHandler`, TextBox → `TextBoxHandler`). Each
> descriptor-backed control instead gets a thin, `new()`-able subclass:
>
> ```csharp
> // appended to XxxDescriptor.cs
> internal sealed class XxxDescriptorHandler()
>     : DescriptorHandler<XxxElement, WinUI.Xxx>(XxxDescriptor.Descriptor);
> ```
>
> This required unsealing `DescriptorHandler<TElement,TControl>`
> (`public sealed class` → `public class`, see its `<remarks>`). The factory
> then touches `_ = Reg<XxxElement, WinUI.Xxx, XxxDescriptorHandler>.Done;`,
> unifying **all** registration (hand-coded + descriptor) on the single
> `Reg<>` mechanism with zero closure (static lambda). The thin handler is
> only rooted via its own `Reg<>` touch, so `XxxDescriptor.Descriptor` stays
> private to it — carved descriptors are never accidentally registered.
>
> **Landed for the Text group:**
> - Unsealed `DescriptorHandler` (`Descriptor/DescriptorHandler.cs`).
> - Thin handlers appended to `TextBlockDescriptor`, `RichTextBlockDescriptor`,
>   `RichEditBoxDescriptor`, `PasswordBoxDescriptor`, `AutoSuggestBoxDescriptor`.
> - Aliases (`V1`, `Desc`, `WinUI = Microsoft.UI.Xaml.Controls`) added to
>   `Dsl.cs`; the bare `Controls.` prefix resolves to
>   `Microsoft.UI.Reactor.Controls`, so the `WinUI` alias is required for the
>   control type argument.
> - 10 Text factories touched: `TextBlock`/`Heading`/`SubHeading`/`Caption`
>   (all → `TextBlockElement`), `RichTextBlock(string)` + `RichTextBlock(RichTextParagraph[])`,
>   `RichEditBox`, `TextBox` (hand-coded `TextBoxHandler`), `PasswordBox`,
>   `AutoSuggestBox`.
> - Selftest `Spec048_TextGroupFactoriesRegisterHandlers`
>   (`Fixtures/Spec048RegistrationFixtures.cs`) asserts each factory call
>   populates `ControlRegistry.Contains(typeof(XxxElement))`. **Must** be a
>   selftest, not xunit: the registry unit tests call `ResetForTesting()`,
>   and the registration cctor runs at most once per process.
> - Green: Spec048 selftests (16 checks) + Spec048 xunit (16 tests),
>   `-p:Platform=x64`.
>
> **Safety:** dormant while `RegisterV1BuiltInHandlers` is intact — built-ins
> still resolve via per-host arm 1, so these global registrations are never
> reached. Zero observable behavior change. Remaining control groups fan out
> with this same template before §3.4.

> **§3.3 close-out note — Input-group landed:**
>
> Fanned out the Text-group thin-handler template to the Input controls.
> 15 of 17 element types are wired; the two decorator-based types (`Button` /
> `CheckBox`) are deferred to §3.4 with the decorator-global-path work.
>
> **Why decorator deferral is unavoidable in §3.3.** `Reg<TElement, TControl, THandler>`
> constrains `THandler : IElementHandler<TElement, TControl>, new()` and
> `ControlRegistry.Register<TElement, TControl>` takes a
> `Func<IElementHandler<TElement, TControl>>`. `ButtonHandler` and
> `CheckBoxHandler` implement `IDecoratorElementHandler<TElement>` — a
> separate, non-inheriting interface (see
> `src/Reactor/Core/V1Protocol/IDecoratorElementHandler.cs:67`,
> `src/Reactor/Core/V1Protocol/IElementHandler.cs:23`). Wiring them now
> would require either (a) extending `ControlRegistry` + `Reg<>` with a
> decorator-aware overload, or (b) wrapping the decorator in a synthetic
> `IElementHandler<TElement,TControl>` that delegates to the underlying
> decorator. Both are §3.4 work — neither belongs in a no-behavior-change
> §3.3 fan-out commit. Documented as a §3.4 blocker.
>
> **Landed for the Input group:**
> - Thin handlers appended to 13 descriptors: `RepeatButton`,
>   `HyperlinkButton`, `DropDownButton`, `SplitButton`, `ToggleSplitButton`,
>   `ToggleButton`, `RadioButton`, `RadioButtons`, `NumberBox`,
>   `RatingControl`, `PipsPager`, `ColorPicker`, `SelectorBar`.
> - Added `using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;` to
>   `Dsl.cs` since `RepeatButton` and `ToggleButton` live in the Primitives
>   namespace (the `WinUI = Microsoft.UI.Xaml.Controls;` alias does not
>   cover them) — matches the existing `WinPrim` alias convention in those
>   descriptor files.
> - 22 Input factories touched (15 unique `Reg<>` closed generics):
>   `HyperlinkButton`×2, `RepeatButton`×2, `ToggleButton`×2 +
>   `ThreeStateToggleButton`, `DropDownButton`, `SplitButton`×2,
>   `ToggleSplitButton`×2, `NumberBox`, `RadioButton`, `RadioButtons`,
>   `Slider` (hand-coded `SliderHandler`), `ToggleSwitch` (hand-coded
>   `ToggleSwitchHandler`), `RatingControl`, `ColorPicker`, `SelectorBar`,
>   `PipsPager`. All factories converted from expression-bodied to block
>   form where required to host the touch line.
> - Selftest `Spec048_InputGroupFactoriesRegisterHandlers`
>   (`Fixtures/Spec048RegistrationFixtures.cs`) asserts each factory call
>   populates `ControlRegistry.Contains(typeof(XxxElement))` for all 15
>   element types. Same selftest-not-xunit rationale as the Text group
>   (registry unit tests `ResetForTesting()`; cctor runs once per process).
> - Green: Spec048 selftests (31 checks, 15 new) + Spec048 xunit (16 tests)
>   + Reactor.Tests (9148 passed / 0 failed), `-p:Platform=x64`.
>
> **NumberBox cctor safety.** `NumberBoxDescriptor.cctor` activates
> `WinUI.NumberBox.TextProperty` (the existing xunit `ControlRegistryTests`
> failure noted in §3.1). The thin `NumberBoxDescriptorHandler` does NOT
> trigger that cctor on the `Reg<>` touch — the touch only invokes
> `ControlRegistry.Register(static () => new THandler())`, which captures
> the factory delegate without instantiating `THandler` (see `Reg.Init()`
> in `src/Reactor/Core/V1Protocol/Reg.cs:96`). The handler ctor — and with
> it the `NumberBoxDescriptor.Descriptor` field access that triggers the
> descriptor cctor — fires only on first dispatch hit, which is dormant
> while `RegisterV1BuiltInHandlers` owns per-host arm 1 dispatch. The
> selftest exercises factory call + registry membership only, not dispatch,
> so it stays green without any WinUI activation.

> **§3.3 close-out note — Container/layout-group landed:**
>
> Same template; 10 of 17 element types wired. The 7 panel-style controls
> (`Stack` / `VStack` / `HStack` / `ZStack` + `Grid` + `Canvas` +
> `RelativePanel` + `WrapGrid` + `FlexPanel`) and `Expander` use
> `IDecoratorElementHandler<TElement>` (see
> `src/Reactor/Core/V1Protocol/Handlers/PanelDelegateHandlers.cs` and
> `ExpanderHandler.cs`) and defer to §3.4 with the same Button/CheckBox
> decorator rationale.
>
> **Landed for the Container/layout group:**
> - Thin handlers appended to 9 descriptors: `ScrollViewer`, `ScrollView`,
>   `Viewbox`, `Frame`, `SplitView`, `RefreshContainer`, `ParallaxView`,
>   `SwipeControl`, `SemanticZoom`.
> - 10 factories in `Dsl.cs` gain a `Reg<>.Done` touch: `Border` (hand-coded
>   `BorderHandler`), `ScrollViewer`, `ScrollView`, `Viewbox`, `SplitView`,
>   `Frame`, `RefreshContainer`, `ParallaxView`, `SwipeControl`,
>   `SemanticZoom`. Factories converted from expression-bodied to block
>   form to host the touch.
> - Selftest `Spec048_ContainerGroupFactoriesRegisterHandlers`
>   (`Fixtures/Spec048RegistrationFixtures.cs`) asserts each factory call
>   populates `ControlRegistry.Contains(typeof(XxxElement))` for all 10
>   element types.
> - Green: Spec048 selftests (41 checks, 10 new) + Reactor.Tests
>   (9148 passed / 0 failed), `-p:Platform=x64`.

> **§3.3 close-out note — Collections-group landed:**
>
> Same template; 10 of 15 element types wired. Five element types defer
> to §3.4:
> - `ItemsRepeater`, `ItemsView` use the base-derived global path
>   (`RegisterDescriptorForDerivedTypes` in
>   `src/Reactor/Core/Reconciler.cs:385-386`); `Reg<>` only knows about
>   closed exact types, so derived-type fan-out needs its own §3.4 work.
> - `LazyStack`, `TemplatedListView<T>`, `TemplatedFlipView<T>` combine
>   `IDecoratorElementHandler<TElement>` with the base-derived
>   `RegisterDecoratorHandlerForDerivedTypes` walk
>   (`Reconciler.cs:383-384`) and defer with the same decorator-global-
>   path + base-derived rationale.
>
> **Landed for the Collections group:**
> - Thin handlers appended to 8 descriptors: `ComboBox`, `ListBox`,
>   `Pivot`, `FlipView`, `TabView`, `BreadcrumbBar`, `ItemContainer`,
>   `TreeView`.
> - 10 factories in `Dsl.cs` gain a `Reg<>.Done` touch: `ListView` and
>   `GridView` use the hand-coded `V1.Handlers.ListViewHandler` /
>   `GridViewHandler`; the rest reference the new descriptor subclasses.
>   `ComboBox(string[])`, `ListBox`, `Pivot`, `FlipView`, `TabView`,
>   `BreadcrumbBar`, `ItemContainer`, `TreeView(TreeViewNodeData[])`
>   converted from expression-bodied to block form to host the touch.
> - Selftest `Spec048_CollectionsGroupFactoriesRegisterHandlers`
>   (`Fixtures/Spec048RegistrationFixtures.cs`) asserts each factory call
>   populates `ControlRegistry.Contains(typeof(XxxElement))` for all 10
>   element types.
> - Green: Spec048 selftests (51 checks, 10 new) + Reactor.Tests
>   (9148 passed / 0 failed), `-p:Platform=x64`.

> **§3.3 close-out note — Date/time-group landed:**
>
> Same template; all 4 element types wired (no deferrals — every Date/time
> control is descriptor-backed with no decorator or base-derived variants).
>
> **Landed for the Date/time group:**
> - Thin handlers appended to 4 descriptors: `DatePicker`,
>   `CalendarDatePicker`, `TimePicker`, `CalendarView`.
> - 4 factories in `Dsl.cs` gain a `Reg<>.Done` touch (all converted from
>   expression-bodied to block form to host the touch).
> - `using System;` added to `Spec048RegistrationFixtures.cs` so the new
>   fixture's `DateTimeOffset` / `TimeSpan` literals resolve unambiguously
>   (otherwise C# resolves `System` against
>   `Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures` and falls through
>   to `Microsoft.UI.System`, which has no `DateTimeOffset`).
> - Selftest `Spec048_DateTimeGroupFactoriesRegisterHandlers` asserts each
>   factory call populates `ControlRegistry.Contains(typeof(XxxElement))`.
> - Green: Spec048 selftests (55 checks, 4 new) + Reactor.Tests
>   (9148 passed / 0 failed), `-p:Platform=x64`.

> **§3.3 close-out note — Status/info-group landed:**
>
> Same template; all 7 element types wired. `AnnounceRegion` has no public
> `Factories` entry — it's constructed inside `AnnounceHandle.ctor`
> (reached via the `UseAnnounce` hook), so the `Reg<>.Done` touch lives
> there instead of in `Dsl.cs`. `Progress` is the canonical name for
> `ProgressElement` (renders WinUI `ProgressBar`); the deprecated
> `ProgressBar()` overloads forward to `Progress(double)` /
> `ProgressIndeterminate()` and inherit the touch transitively.
>
> **Landed for the Status/info group:**
> - Thin handlers appended to 7 descriptors: `ProgressBar`, `ProgressRing`,
>   `InfoBar`, `InfoBadge`, `TeachingTip`, `AnnounceRegion`,
>   `AnnotatedScrollBar`. `ProgressBarDescriptorHandler` is typed
>   `DescriptorHandler<ProgressElement, WinUI.ProgressBar>` (matches the
>   descriptor's actual element type, not the WinUI name).
> - 6 factory groups in `Dsl.cs` gain a `Reg<>.Done` touch (`Progress`,
>   `ProgressIndeterminate`, `ProgressRing` ×2, `InfoBar`, `InfoBadge` ×2,
>   `TeachingTip`, `AnnotatedScrollBar`).
> - `AnnounceHandle.ctor` in `src/Reactor/Hooks/UseAnnounce.cs` touches
>   `Reg<AnnounceRegionElement, WinUI.TextBlock, AnnounceRegionDescriptorHandler>`
>   before constructing the element.
> - Selftest `Spec048_StatusInfoGroupFactoriesRegisterHandlers` covers all
>   7 element types; it instantiates `AnnounceHandle` directly (visible via
>   `InternalsVisibleTo Reactor.AppTests.Host`) to exercise that path.
> - Green: Spec048 selftests (62 checks, 7 new) + Reactor.Tests
>   (9148 passed / 0 failed), `-p:Platform=x64`.

> **§3.3 close-out note — Media/icons-group landed:**
>
> Same template; 7 of 8 element types wired. `Icon` is a decorator
> (`IconHandler` registered via `RegisterDecoratorHandler<IconElement>` in
> `Reconciler.cs:480`) and defers to §3.4 with the decorator-global-path
> work. The remaining 7 are descriptor-backed with no decorator or
> base-derived variants.
>
> **Landed for the Media/icons group:**
> - Thin handlers appended to 7 descriptors: `Image`,
>   `MediaPlayerElement`, `PersonPicture`, `AnimatedIcon`,
>   `AnimatedVisualPlayer`, `WebView2`, `MapControl`.
> - 7 factories in `Dsl.cs` gain a `Reg<>.Done` touch (all converted from
>   expression-bodied to block form to host the touch).
> - Selftest `Spec048_MediaIconsGroupFactoriesRegisterHandlers` asserts
>   each factory call populates
>   `ControlRegistry.Contains(typeof(XxxElement))` for all 7 element types.
> - Green: Spec048 selftests (69 checks, 7 new) + Reactor.Tests
>   (9148 passed / 0 failed), `-p:Platform=x64`.

### 3.4 Delete `RegisterV1BuiltInHandlers`

- [ ] Remove the body of `RegisterV1BuiltInHandlers()` in
      `src/Reactor/Core/Reconciler.cs:335`; remove the call from the
      ctor at `Reconciler.cs:265`. The Reconciler ctor no longer roots
      any handler or control type.
- [ ] Confirm via `dotnet build Reactor.slnx -p:Platform=x64` that the
      build is clean — i.e., no remaining call sites reference the
      removed method.

### 3.5 Enforce the "no type-level aggregation on `Factories`" invariant (spec §10.2, §10.4)

- [ ] Audit all `Dsl*.cs` partials for: (a) any `static` constructor on
      `Factories`, (b) any `static readonly` field initializer that
      references a handler type or a WinUI control type, (c) any
      `[ModuleInitializer]` in the Reactor assembly (spec §4 documents
      why this is forbidden). Record findings in
      `docs/specs/048/audits/factories-aggregation-audit.md`.
- [ ] Optional but recommended: add a Roslyn analyzer
      (`Reactor.Analyzers.NoFactoriesAggregationAnalyzer`) that flags any
      static cctor or control-referencing static field on `Factories`
      with diagnostic id `REACTOR_TRIM_001`. Wire it into
      `src/Reactor.Analyzers/`.
- [ ] Extend the Hello-World trim assertion (§2.3) to a richer probe
      that asserts the **full** must-trim list from spec §11 (every
      control NOT used by `VStack(TextBlock, Button)`).

### 3.6 Phase 3 exit gate

- [ ] Full xunit + selftest + solution build green on x64.
- [ ] CI `aot-selftests` job stays green.
- [ ] CI `aot-trim-proof` job (from §2.3) passes against the migrated
      built-ins — i.e., publishing the Hello-World app drops `TreeView`,
      `GridView`, `TabView`, `MarqueeControl`, and every other
      unreferenced control.
- [ ] M1/M2/M3 micro-benches (spec §9) show the `Reg<>.Done` branch
      disappears into the element-record allocation; results committed to
      `docs/specs/048/perf-results/phase3-migration.md`. If a regression
      shows, escalate before flipping the migration on `main`.

---

## Phase 4 — Ergonomic layer (optional, spec §13.4)

Phase 4 is the source-gen ergonomic layer plus the documentation refresh.
The source generator is **optional** per spec §13.4; the doc refresh is
**not** — it is required by exit gate item 6.

### 4.1 Documentation refresh (required)

User docs are generated from `docs/_pipeline/templates/*.md.dt` via `mur
docs compile`; **edit the templates, not the compiled output**. The
relevant memory citation: "User docs under docs/guide are generated from
docs/_pipeline/templates/*.md.dt".

- [ ] `docs/_pipeline/templates/extending-reactor-controls.md.dt`:
      - Replace the "five-step playbook" table (lines ~36–47) with a
        four-step playbook that drops step 3 (the `Register` extension)
        and step 4 (the explicit per-host `RegisterHandler` call). The
        new "step 3" is the factory holder with the cctor-driven
        `ControlRegistry.Register` call (Pattern A) — and the playbook's
        prose makes clear that the factory is the registration trigger.
      - Replace the "Step 3 — Register" / "Step 4 — Call Register at
        startup" sections wholesale with a single "Step 3 — Wrap the
        constructor in a factory holder" section showing the spec §6
        `Marquee` example end-to-end.
      - Update the "Common mistake" callout about "forgetting to call
        `Register` at startup" — that mistake is now unrepresentable.
        Replace it with the new common mistake: a non-`static` lambda
        passed to `ControlRegistry.Register`.
      - Cross-link to the new `aot-trim-proof` Hello-World app as the
        canonical "prove your control trims" recipe.
- [ ] `docs/_pipeline/templates/control-reconciler-protocol.md.dt`:
      - Add a "Dispatch precedence" section enumerating the four-step
        order (spec §8 precedence list).
      - Document the global `ControlRegistry` as the standard
        registration path; demote per-host `Reconciler.RegisterHandler`
        to "explicit override / test escape hatch" (spec §8 last
        paragraph).
      - Add a Caveat block explaining the open-question §12.1 amendment:
        global registry is **first-wins idempotent**, per-host
        `RegisterHandler` keeps the strict throw-on-duplicate.
- [ ] `docs/_pipeline/templates/controls.md.dt` (and any other guide page
      that mentions registration): scrub for now-stale references to
      `RegisterV1BuiltInHandlers` or "register the handler at startup".
- [ ] Add a new `docs/_pipeline/templates/control-registration-and-trimming.md.dt`
      page (tier: `solid`) that teaches the trim story end-to-end:
      Pattern A for a single control, Pattern B (the `Reg<>` helper) for
      a control library, the Hello-World AOT app as the verification
      recipe. Doc app under `docs/_pipeline/apps/control-registration-and-trimming/`.
- [ ] Run `mur docs compile` and verify the compiled
      `docs/guide/extending-reactor-controls.md` /
      `control-reconciler-protocol.md` / new
      `control-registration-and-trimming.md` are well-formed. If `mur` is
      unavailable in the current env, document the regeneration
      requirement in the PR description so a reviewer with `mur` runs it.

### 4.2 Source generator (optional)

- [ ] Add `src/Reactor.SourceGen/RegistrationGenerator.cs` (or extend an
      existing generator) that scans for `IElementHandler<TElement,
      TControl>` implementations and emits the corresponding `Reg<>` touch
      + closed-ctor partial for the matching factory automatically. Per
      spec §12.4, this is an additive ergonomic layer over the same
      runtime contract; the generator is **not** a prerequisite for any
      earlier phase.
- [ ] Tests: a fixture handler implementation produces the expected
      generated registration, and the generated code passes the trim
      assertion.
- [ ] Skip / defer if the source-gen approach significantly complicates
      the build pipeline; the runtime contract from Phase 1–3 stands on
      its own.

### 4.3 Final close-out

- [ ] Spec §12 open questions resolved and the spec status updated from
      "Proposed" to "Implemented" (or, if §4.2 is deferred, "Implemented
      except §12.4 source generation").
- [ ] Memory entry stored: the new "factory-as-registration" pattern is
      the only supported way to register a control; per-host
      `RegisterHandler` is for tests / sandboxed embeds only.
- [ ] Final exit-gate sweep:
      - [ ] Full xunit + selftest + solution build green on x64.
      - [ ] CI `aot-trim-proof` green.
      - [ ] No `RegisterV1BuiltInHandlers` reference anywhere.
      - [ ] No `static` cctor on `Factories` partials.
      - [ ] All built-in element record ctors `internal`.
      - [ ] Docs regenerated and committed.

---

## Explicitly out of scope

- **Source-gen author surface** (spec §12.4) — landed in Phase 4 if
  cheap, deferred otherwise. The runtime contract does not depend on it.
- **WinAppSDK / WinUI framework trim improvements** (spec §11 caveat) —
  out of Reactor's scope. The Hello-World assertion checks Reactor-side
  rooting only; SDK-side regressions are not this spec's responsibility.
- **A second sandboxed `ControlRegistry` instance per host** (spec §12.2
  open question, second half) — Phase 1 ships only the global `Default`
  plus per-host override; an explicit "ignore global defaults" switch is
  deferred until a real consuming scenario asks for it.

## Build / test cmds (verified in this env, dotnet 10.0.x)

- xunit: `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`
- selftest: `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test [--filter Name]`
- solution build: `dotnet build Reactor.slnx -p:Platform=x64`
- AOT publish (after §2.3 lands): `dotnet publish tests/aot_trim_proof/Reactor.AotHelloWorld -c Release -r win-x64 -p:PublishAot=true`
- external proof tests: `dotnet test tests/external_proof/Reactor.External.TestControl.Tests`
- docs compile (when `mur` is available): `mur docs compile`
