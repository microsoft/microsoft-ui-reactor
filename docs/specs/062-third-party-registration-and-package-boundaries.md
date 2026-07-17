# Third-Party Control Registration and Package Boundaries — Design Proposal

## Status

**Proposed.** Design document responding to
[issue #163](https://github.com/microsoft/microsoft-ui-reactor/issues/163)
("Simplify third-party control registration and package boundaries for custom
element types"). It builds on the already-shipped extensibility stack — the V1
handler protocol ([spec 047](047-extensible-control-model.md)), lazy trimmable
registration ([spec 048](048-control-registration-and-trimming.md)), and the control
wrapper source generator ([spec 058](058-control-wrapper-generator.md)) — and on the
packaging/distribution model ([spec 022](022-packaging-and-distribution.md)). It
settles the *delta* those specs did not cover: keeping a third-party control
library's **compiled output runnable across Reactor versions**, **slimming Reactor's
monolithic package**, and giving apps a **standard one-call-per-library registration
convention**.

One small enabling change ships alongside this spec:
`ReactorBinding.ShouldSuppressEcho` is now public (§3), closing the last internal
symbol the wrapper generator baked into external code. Everything else here is
design, phased in §13.

### North star

> A third-party control library adds Reactor support by wrapping its WinUI control,
> and that **compiled wrapper keeps running against newer Reactor runtimes without
> recompilation** — the binary roll-forward .NET already gives any additive,
> signature-compatible NuGet dependency. It gets that from a **stable, additive-only
> authoring seam inside the one Reactor runtime** — *not* a separate abstractions
> package.
> Reactor's own package stays lean by keeping heavy optional subsystems out of the
> default reference. App developers get **one obvious registration call per
> library**, and a missing registration fails **loudly and helpfully**, never as a
> blank screen.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Requirements](#2-requirements)
- [§3 Current state — what 047 / 048 / 058 already deliver](#3-current-state--what-047--048--058-already-deliver)
- [§4 Two hard constraints that shape the design](#4-two-hard-constraints-that-shape-the-design)
  - [§4.1 A wrapper inherently names WinUI](#41-a-wrapper-inherently-names-winui)
  - [§4.2 The authoring seam is welded to the runtime](#42-the-authoring-seam-is-welded-to-the-runtime)
- [§5 The decision — two independent tracks](#5-the-decision--two-independent-tracks)
  - [§5.1 Rejected alternatives](#51-rejected-alternatives)
- [§6 Track A — a stable authoring ABI, in place](#6-track-a--a-stable-authoring-abi-in-place)
  - [§6.1 The seam to freeze](#61-the-seam-to-freeze)
  - [§6.2 Governance — three pieces that do not exist yet](#62-governance--three-pieces-that-do-not-exist-yet)
  - [§6.3 Evolution discipline](#63-evolution-discipline)
- [§7 Track B — consolidate heavy subsystems into `Reactor.Advanced`](#7-track-b--consolidate-heavy-subsystems-into-reactoradvanced)
- [§8 Library registration API — `UseLibrary`](#8-library-registration-api--uselibrary)
- [§9 Self-registration vs. explicit registration](#9-self-registration-vs-explicit-registration)
- [§10 Diagnostics](#10-diagnostics)
- [§11 Trimming and AOT](#11-trimming-and-aot)
- [§12 Open questions](#12-open-questions)
- [§13 Phasing](#13-phasing)

---

## §1 Motivation

Reactor is not missing extensibility — spec 047 delivered the public
`IElementHandler` / `ControlDescriptor` surface, spec 048 made registration lazy and
trim-safe, and spec 058 added a `[GenerateReactorWrapper]` source generator that
emits the descriptor + registration + factory for a wrapped WinUI/WinRT control.
Issue #163 raises four coupled frustrations that remain:

1. **The dependency is heavy, and forward-compatibility isn't guaranteed.** To *define*
   one custom element a library references the whole of `Reactor.dll`. And because its
   compiled output binds concrete runtime types across a seam with no stability
   guarantee, nothing ensures that output keeps working against a newer runtime an app
   happens to pull in — .NET *can* roll the binding forward, but only as long as every
   bound symbol is still present and unchanged, which today nothing enforces. Two
   Reactor-enabled libraries built against different versions may therefore fail to
   compose.

2. **Registration has no standard shape.** Registration happens per control (a
   factory touch, or a Pattern-A static constructor — spec 048 §6). There is no
   single "wire up everything this library needs" entry point analogous to .NET
   MAUI's `builder.UseSkiaSharp()`.

3. **Missing registration can still fail quietly.** Spec 048 made the reconciler
   throw an actionable exception on an unregistered element, but only when that
   element is actually mounted — an element on a late or conditional path can present
   as missing UI until it hits.

4. **The package is monolithic.** Charting, the data grid, docking, and markdown all
   ship inside the one `Microsoft.UI.Reactor` package, so every app and every wrapper
   author carries them whether or not they are used.

## §2 Requirements

**R1 — Durable authoring ABI.** A library's *compiled* wrapper output binds a
**stable, additive-only authoring seam** and keeps running against a newer Reactor
runtime without recompilation (see R7 / §6). This is the load-bearing requirement;
it supersedes any notion of a separate "abstractions" reference (§4.2 / §5.1).

**R2 — Standard registration convention.** One explicit, conventional call per
library (`builder.UseMyLibrary()` or equivalent) that centralizes that library's
element/handler registration and initialization.

**R3 — Loud, helpful failure.** A missing registration produces a clear diagnostic
that names the missing library and suggests the fix — never a blank screen.

**R4 — Leaner core.** The heavy optional subsystems (charting, data grid, docking,
markdown) move out of the default core reference so every app — and every wrapper
author — carries a smaller Reactor.

**R5 — Do not regress the trim/AOT story.** Every proposal must preserve spec 048's
property: an app roots (and the trimmer keeps) only the controls it actually reaches.

**R6 — Do not regress authoring ergonomics.** The hand-authored `IElementHandler`
recipe (spec 047, `tests/external_proof`) and the `[GenerateReactorWrapper]`
generator (spec 058) keep working, ideally with a smaller footprint, never larger.

**R7 — Cross-version library interop (stable ABI).** A control library built against
Reactor version *N* must load and run, **without recompilation**, against a host that
supplies a *newer* runtime version *M* ≥ *N* — the NuGet single-version unification and
assembly roll-forward that .NET applies to any additive-compatible dependency. An app
that transitively pulls `LibA` (built against *N*)
and `LibB` (built against *M*) unifies to a **single** runtime, and both libraries'
controls must mount, update, and echo correctly against it.

## §3 Current state — what 047 / 048 / 058 already deliver

This spec is a *delta*, so it is worth being precise about what already exists.

- **The registration runtime is already lazy and trim-safe (048).** The global
  `ControlRegistry` (`src/Reactor/Core/V1Protocol/ControlRegistry.cs`) holds
  `Type → Func<…>` entries; every static reference to a handler / control type lives
  in the *caller* of `Register<TElement,TControl>` (a per-control factory cctor —
  Pattern A — or a `Reg<…>` static-field initializer — Pattern B), each on a
  per-control rooted path. Registration is idempotent first-wins. The opt-in
  `ReactorApp.RegisterAllBuiltIns()` roots the whole catalog for apps that want the
  direct-record idiom; apps that want a small binary simply do not call it.

- **Hand-authoring already works against the public surface only (047).**
  `tests/external_proof/Reactor.External.TestControl` ships an element + control +
  handler in a *separate assembly* with **no** `InternalsVisibleTo` — proving the
  public V1 surface is sufficient.

- **Generated authoring already works (058).** `[GenerateReactorWrapper]` on a
  partial record emits the init-props, child/items slots, `On{Event}` callbacks, the
  `ControlDescriptor`, Pattern-A registration, and a factory. The generator is a
  `netstandard2.0` Roslyn component that emits *strings* and binds attributes by
  metadata name, so it references neither `Reactor.dll` nor its own attributes
  assembly at build time.

- **The missing-handler failure already throws helpfully (048).**
  `Reconciler.ThrowNoHandlerRegistered` (`src/Reactor/Core/Reconciler.Mount.cs`)
  raises an `InvalidOperationException` naming the element type and listing concrete
  fixes.

- **The last internal-symbol leak is closed (this change).**
  `ReactorBinding.ShouldSuppressEcho(UIElement)` is now a **public** read-side echo
  primitive, and the wrapper generator's deferred-controlled trampoline emits it
  instead of the internal `ChangeEchoSuppressor.ShouldSuppress`. Every symbol the
  generator bakes into external generated code is now public — the precondition for
  Track A (§6).

What is **not** yet present, and is the subject of this spec: a *governed* stable
authoring ABI (§6), a leaner core with the heavy subsystems in `Reactor.Advanced`
(§7), a library-level `UseLibrary` convention (§8), and library-scoped diagnostics
(§10).

## §4 Two hard constraints that shape the design

### §4.1 A wrapper inherently names WinUI

The core authoring types are structurally coupled to WinUI: `Element` and its
subclasses reference `Microsoft.UI.Xaml`/`…Controls`/`…Media`, and
`IElementHandler<TElement,TControl>` / `ControlDescriptor<TElement,TControl>`
constrain `where TControl : UIElement` — a handler's whole purpose is to mount and
patch a real WinUI control. So "define a custom element type" *inherently* names
WinUI types; a wrapper transitively depends on WinUI no matter how Reactor is sliced.

The smallest WinUI slice is the `Microsoft.WindowsAppSDK.WinUI` sub-package (Windows
App SDK 2.0 split the monolithic metapackage into independently-versioned
sub-packages; this repo already injects only `.WinUI` for framework-dependent library
projects via `Directory.Build.targets`), **not** the full `Microsoft.WindowsAppSDK`
metapackage. This bounds R7: the roll-forward guarantee is "one Reactor runtime + one
compatible Windows App SDK," the same single-WinUI-per-process constraint apps
already accept — not "any WinUI."

### §4.2 The authoring seam is welded to the runtime

The authoring *execution* seam cannot be lifted into a separate assembly. Verified
against source:

- `MountContext` / `UpdateContext` / `UnmountContext`
  (`src/Reactor/Core/V1Protocol/MountContext.cs`) are `public readonly ref struct`s
  that each hold a `private readonly Reconciler _reconciler`, **expose it publicly**
  (`public Reconciler Reconciler => _reconciler;`, offered as an "escape hatch"), and
  proxy roughly a dozen reconciler operations (`MountChild`, `ReconcileV1Child`,
  `RentControl`, `PushContext`/stagger scopes, `ApplySetters`, …) on the per-mount,
  allocation-free hot path.
- `ControlDescriptor` references `MountContext` (its `AfterChildrenMount` callback),
  and `OneWayBridgedSetter` takes a `Reconciler` directly.
- The generator emits direct calls to `Reconciler.SetElementTag` / `GetElementTag` /
  `DetachReactorState` / `ApplySetters`, `ReactorApp.TryRegisterControlAssembly`, and
  `ElementExtensions.OnMountAdd` / `OnUnmountAdd`. (All of these are **public** as of
  this change — §3 — but they name the concrete `Reconciler` / `ReactorApp`.)

Splitting this seam into its own assembly would either drag `Reconciler` along (no
real split) or invert it into a broad `IRuntimeOps`-style interface that relocates
the same freeze onto a hot-path ref struct while adding dispatch cost — for a
single-implementation runtime that needs no polymorphism. That is why the ABI is
stabilized **in place** (§6), not carved out (§5.1).

## §5 The decision — two independent tracks

Issue #163's goals are met by two tracks that share no dependency and can land in
either order:

- **Track A — a stable authoring ABI, in place (§6).** Treat the (now fully public)
  authoring seam as a versioned, additive-only contract in the one runtime, enforced
  by a real API-compat gate and a binary-compat interop harness. Delivers R1 / R7.
- **Track B — consolidate heavy subsystems into `Reactor.Advanced` (§7).** Move
  charting, docking, markdown, and the data grid out of core into the existing
  advanced assembly, largely keeping their namespaces (§7). Delivers R4.

Registration ergonomics (`UseLibrary` + diagnostics, §8–§10) is a third, independent
workstream that serves R2 / R3 and can interleave with either track.

### §5.1 Rejected alternatives

- **A `Reactor.Abstractions` interfaces/DI package.** There is a single runtime
  implementation, loaded once, with no polymorphism or load-isolation requirement;
  Elements are immutable *data records*, not services. The three classic
  `.Abstractions` criteria (multiple implementations, independent consumers, an
  independently versioned contract) all fail. It would add a versioning unit and
  interface-dispatch overhead for no gain.
- **Splitting the authoring seam into its own assembly (no interfaces).** Rejected on
  contract-breadth, not perf: the seam is welded to `Reconciler` (§4.2). A split
  drags the runtime along or inverts to a broad runtime interface that merely
  relocates the freeze. One runtime → one versioning unit is simpler and honest.
- **A fan-out of per-feature packages** (`.Charting` / `.DataGrid` / `.Docking` /
  `.Markdown`). Each assembly carries fixed load/metadata overhead; a dozen small
  DLLs cost more than they save for the common app. Track B consolidates into the one
  existing `Reactor.Advanced` assembly instead.
- **Carving the built-in control catalog out of core behind a metapackage.** Large,
  invasive churn (the built-in element records are the bulk of `Element.cs`) that
  neither track needs; deferred indefinitely.

## §6 Track A — a stable authoring ABI, in place

The goal (R1/R7) is that a *compiled* wrapper binds a fixed subset of Reactor's
public surface and keeps working against a newer runtime. .NET default-context
assembly binding is by type/member signature and NuGet unifies to a single assembly,
so *old generated code binds to a newer runtime as long as every symbol it references
is unchanged*. The guarantee reduces to one discipline: **enumerate the surface a
wrapper binds, and evolve it additive-only.**

### §6.1 The seam to freeze

Grounded against the generator's emit and the reconciler's reads of a foreign
element, the bound surface is enumerable and, as of this change, entirely public:

| Frozen ABI surface | Where |
|---|---|
| `Element` base + `Key` / `Modifiers` (`ElementModifiers` value types) | `Core/Element.cs` (reconciler reads only base members) |
| `Optional<T>` | `Core` (emitted by generated init-props) |
| `ControlDescriptor<E,C>` + its builder methods, including the hand-coding arms (`Imperative`, `HandCodedControlled`, `HandCodedEvent`) and the `ChildrenStrategy` taxonomy (`SingleContent` / `Panel` / `ItemsHost` / element-slots) | `Core/V1Protocol` |
| `IElementHandler<E,C>` / `IDecoratorElementHandler<E>` / `DescriptorHandler<E,C>` | `Core/V1Protocol` |
| `MountContext` / `UpdateContext` / `UnmountContext` + `ReactorBinding<TElement>` | `Core/V1Protocol` |
| `ControlRegistry.Register*` | `Core/V1Protocol/ControlRegistry.cs` |
| `ReactorBinding.ShouldSuppressEcho` / `WriteSuppressed` | `Core` |
| `Reconciler.SetElementTag` / `GetElementTag` / `DetachReactorState` / `ApplySetters` (tag get/set) | `Core/Reconciler.*` |
| `ElementExtensions.OnMountAdd` / `OnUnmountAdd` | `Elements` |
| `ReactorApp.TryRegisterControlAssembly` | `Hosting` |

**What is *not* part of the binary freeze:** the app-facing DSL — the ~200 factory
methods, ~600 fluent modifiers, and the hooks — is bound by the **recompiling app**,
not by a shipped wrapper binary. It carries a softer, source-level add-only
compatibility expectation (don't gratuitously break `Text("x").Bold()`), but it is
not part of the versioned binary ABI a wrapper rolls forward against.

### §6.2 Governance — three pieces that do not exist yet

1. **A real API-compat gate.** Today `tests/Reactor.Tests/Tooling/ApiIndexGeneratorTests.Index_IsUpToDate`
   only byte-compares the generated `reactor.api.txt` for *staleness* — regenerating
   it makes the test pass, so it *blesses* breaking changes rather than blocking them.
   Add an additive-only gate — `Microsoft.CodeAnalysis.PublicApiAnalyzers`
   (`PublicAPI.Shipped.txt` / `.Unshipped.txt`) or `Microsoft.DotNet.ApiCompat`
   against the last shipped `Reactor.dll` — scoped to the §6.1 seam, so removing or
   changing a frozen member fails the build.

2. **A binary-compat interop harness.** Additive signatures are necessary but not
   sufficient — a wrapper depends on *behavior* (echo semantics, child reconcile,
   registration timing, descriptor ordering). Analogous to the existing AOT-proof
   harness, build `Reactor.External.TestControl` against an **older** Reactor, then
   load the **compiled DLL** (a binary drop, not a project reference) into a host
   running the HEAD runtime and assert mount / update / echo-suppress all succeed
   through the public surface only. A two-NuGet-version matrix can follow in CI.

3. **A loader / version policy.** Roll-forward works today partly by luck:
   `Reactor.dll` is **not strong-named** (no `SignAssembly`/key in `Reactor.csproj`)
   and its assembly version tracks the package version, so default-`AssemblyLoadContext`
   unification lets an old wrapper bind a newer runtime. Make this a *decision*: pin an
   explicit assembly-version / strong-name policy and document that the guarantee holds
   in the default load context with NuGet highest-wins unification. **Side-by-side**
   (two runtimes in separate `AssemblyLoadContext`s) is **out of scope** — `Element`
   records from different contexts are different `Type`s a shared reconciler cannot
   process; custom/plugin ALCs break the guarantee by design.

### §6.3 Evolution discipline

- **Additive by default; break only when nothing simpler works.** New seam behavior
  arrives as *new* members (new overloads; default-interface-methods on the
  handler/descriptor interfaces), never as edits to existing ones — a §6.1 signature is
  never removed or changed when an additive change would achieve the same result.
  Reactor still reserves the right to break the seam, but only as a genuine last resort
  when no compatible change exists; every such break is a deliberate, versioned,
  release-noted exception — not routine churn (and, in preview, expected to grow rarer
  as the API settles).
- **Records stay evolvable.** The reconciler dispatches by element `Type` → registered
  entry and reads only base `Element` members; it never reconstructs a derived record
  positionally across the boundary. A library's own element record shape is therefore
  private — only additive changes to the *base* `Element` are ABI-visible.
- **One versioning unit.** The core runtime churns freely as long as it keeps the
  §6.1 seam additive; there is no separate contract assembly to keep in lockstep.

## §7 Track B — consolidate heavy subsystems into `Reactor.Advanced`

Today only Win2D is extracted: `Reactor.Advanced` (`<Description>Advanced Reactor
components — first inhabitant: Win2D canvas.</Description>`, with `charts` already in
its `PackageTags`) is a one-way `ProjectReference` on core. Core still carries the
heavy leaves — charting/D3, docking, markdown, and the data grid — which dominate the
~3.5 MB `Reactor.dll`.

**Plan: move charting, docking, markdown, and the data grid into
`Reactor.Advanced`.** This follows the colleague-endorsed "one advanced assembly, not
a dozen DLLs" shape and reuses Advanced's existing self-registering
`Advanced.Factories` mirror pattern.

- **The moves are mechanical.** [PR #627](https://github.com/microsoft/microsoft-ui-reactor/pull/627)
  (merged, closed #498) already did the hard *dependency inversion* for the family:
  it introduced runtime seams (`IChartingHostBridge`, `IScanExtension`,
  `Element.OwnPropsEqualOverride`, `PathDataParser` relocated into core,
  `ChartingRuntime.Activate`) so a chart-free app trims charting/docking out, and
  added `CoreControlFamilyBoundaryTests` (an IL scan that *fails the build* if
  Core/Hosting statically reference charting/docking). Core holds **zero hard type
  references** to these subsystems, so relocating them across an assembly boundary is
  a project-file move, not a rewrite.
- **Charting and docking move source-clean.** Their public authoring surface lives under
  `Microsoft.UI.Reactor.Charting` / `Microsoft.UI.Reactor.Docking` — its own namespaces,
  *not* the shared core `Factories` partial. A namespace is not an assembly in C#, so
  moving the code to the Advanced *assembly* leaves every type's full name unchanged: a
  consumer adds only the `Reactor.Advanced` package reference, with **no source change**.
- **The data grid and markdown each take a one-line source break.** Both expose their DSL
  *entry points* through the shared core `Factories` partial, which cannot span assemblies:
    - the data grid — `DataGridFactories.cs` / `ColumnFactories.cs`
      (`public static partial class Factories` in namespace `Microsoft.UI.Reactor`);
    - markdown — the `Markdown(string)` / `Markdown(string, MarkdownOptions)` methods
      embedded in the core `Dsl.cs` `Factories` partial (`src/Reactor/Elements/Dsl.cs:1825`),
      which delegate to `MarkdownBuilder` in `Microsoft.UI.Reactor.Markdown`.

  On the move those entry points become `Microsoft.UI.Reactor.Advanced.Factories`, so a
  data-grid or markdown app author adds one line:
  `using static Microsoft.UI.Reactor.Advanced.Factories;`. Everything else keeps its
  namespace — the data-grid element/column records and the `MarkdownOptions` record (in
  `Microsoft.UI.Reactor.Markdown`) are untouched, and both factories return the base
  `Element`, so no derived record is named across the boundary and `with`/record usage is
  unaffected. A core-side compat shim is **not** an option: core is a one-way
  `ProjectReference` *target* of Advanced, so it cannot call back into the relocated
  implementation. These are minor, preview-window source breaks (§12 Q4).
- **Sequence:** charting + docking first (already decoupled, boundary-guarded, and
  source-clean), then markdown, then the data grid (the last two each carry the factory
  note above).

**Win:** smaller default JIT ship size, a smaller core NuGet package, and a smaller
core dependency for wrapper authors — with AOT already covered by #627's trimming
inversion. Each move is gated on: no new `internal` cross-boundary reach that
`InternalsVisibleTo` cannot cleanly express, `CoreControlFamilyBoundaryTests` and the
AOT-proof harness stay green, and the subsystem's selftests/E2E still pass.

## §8 Library registration API — `UseLibrary`

Give Reactor a MAUI-style, one-call-per-library registration convention (R2).

**What `UseLibrary` is for — and what it is not.** Per-control factory
self-registration (spec 048 / Pattern A) *remains the trim-safe default*: touching a
generated factory registers *that one control*, rooting nothing else. `UseLibrary` is
scoped to what per-control registration cannot do:

1. **Library-level initialization** that is not per control (metadata provider
   registration, resource dictionaries, one-time setup).
2. **Base-derived registrations** (`RegisterForDerivedTypes`) that intentionally cover
   a family in one entry.
3. **The direct-record idiom opt-in** — an app that builds element records directly
   (`new FooElement(...)`) instead of via factories needs *something* to register;
   `UseLibrary` is that opt-in, and — like `RegisterAllBuiltIns()` — it is explicitly
   a documented bulk trim opt-out, not the default path.
4. **A discoverability + diagnostics anchor** (§10) the analyzer and runtime throw can
   point at.

**The contract:**

```csharp
namespace Microsoft.UI.Reactor;

/// <summary>One place a Reactor-enabled library wires up everything it needs:
/// element/handler registration, generated metadata, initialization.</summary>
public interface IReactorLibrary
{
    void Register(IReactorLibraryBuilder builder);
}
```

`IReactorLibraryBuilder` is a thin façade over `ControlRegistry` (plus future
per-library init hooks) so a library never touches the registry directly:

```csharp
public interface IReactorLibraryBuilder
{
    IReactorLibraryBuilder Register<TElement, TControl>(Func<IElementHandler<TElement, TControl>> handler)
        where TElement : Element where TControl : UIElement;
    IReactorLibraryBuilder RegisterDecorator<TElement>(Func<IDecoratorElementHandler<TElement>> handler)
        where TElement : Element;
    // …RegisterForDerivedTypes, and future init hooks…
}
```

**The app-facing call** folds into the existing `ReactorApp` startup surface:

```csharp
ReactorApp.CreateBuilder()
    .UseLibrary<MyControls.MyControlsLibrary>()   // one call per library
    .UseLibrary(new AcmeCharts.AcmeChartsLibrary(options))
    .Run(App);
```

`UseLibrary<T>()` news up `T` and calls `Register`; the instance overload supports
libraries needing construction options. A library ships a friendly extension so app
authors get the MAUI feel:

```csharp
namespace MyControls;
public static class MyControlsRegistration
{
    public static IReactorAppBuilder UseMyControls(this IReactorAppBuilder b)
        => b.UseLibrary<MyControlsLibrary>();
}
```

**Built-ins and Advanced fold into the same shape.** `ReactorApp.RegisterAllBuiltIns()`
*is* the built-in library's `Register` body; reframe it as `UseLibrary<BuiltInControlsLibrary>()`
(the old method stays as a compatibility shim). Each consolidated Advanced subsystem
(§7) ships its own `UseCharting()` / `UseDataGrid()` / `UseDocking()` /
`UseMarkdown()` extension over the same interface — dogfooding the third-party model
with Reactor's own components.

**Trim note.** `UseLibrary<T>()` names `T`, whose `Register` body names whatever it
registers — so it roots exactly that surface, the *same trim opt-out shape* as
`RegisterAllBuiltIns()`. Libraries should keep their per-control factories
self-registering so an app that never calls `UseLibrary` still pays only for the
controls it touches (R5). A large subsystem may expose finer modules
(`UseChartsCore()` / `UseChartsInteractive()`) so an app roots only the sub-surface it
needs.

## §9 Self-registration vs. explicit registration

Spec 048 already analyzed and rejected the *unconditional-eager* forms for the
built-in catalog; that analysis carries over.

- **`[ModuleInitializer]` / assembly-load registration is a trimmer root.** It runs on
  first type-load and names every handler + control, so the trimmer can never prove it
  dead and keeps the whole library. This defeats R5. (Spec 048 §3.4 deleted exactly
  this shape.)
- **Reflection-based assembly scanning** is AOT-hostile, startup-costly, and roots
  types the trimmer would otherwise drop.

**Recommendation:** keep *explicit* registration the norm — the trim-safe default is
per-control factory self-registration; `UseLibrary` (§8) is the bulk / direct-record
opt-in. Layer *optional* discovery on top for teams that value zero ceremony over
binary size:

1. **`[assembly: ReactorLibrary(typeof(MyControlsLibrary))]`** — a *marker*, not an
   eager root. It registers nothing at load time; it exists so diagnostics (§10) can
   point at the exact `UseX()` a developer forgot, and so an explicitly opted-in
   discovery pass can find libraries.
2. **`ReactorApp…UseDiscoveredLibraries()`** — an explicit opt-out of the trim story
   (mirrors `RegisterAllBuiltIns()`): an ordinary, removable method that scans
   `[assembly: ReactorLibrary]` markers and registers each. It (and any marker scan)
   is annotated `[RequiresUnreferencedCode]` so the trim/AOT analyzers — hard errors in
   this repo — flag it at the call site.

## §10 Diagnostics

Layer library-scoped diagnostics on top of 048's runtime throw (R3).

- **Enrich the runtime throw.** When the unregistered element's *assembly* carries
  `[assembly: ReactorLibrary(typeof(X))]`, name the specific missing call — e.g.
  *"`MarqueeElement` is defined in `MyControls`, which declares a Reactor library but
  was never registered. Call `builder.UseMyControls()` at startup."* Keep this
  reflection-light and AOT-safe: read the marker off the already-loaded
  `element.GetType().Assembly` custom-attribute (no scan, no extra assembly load) and
  fall back to the generic message if the attribute was trimmed.
- **Add a build-time analyzer / `mur check` rule.** Flag a project that references a
  `[ReactorLibrary]` library but never calls the corresponding `UseX()` /
  `UseLibrary<>()`. Scope it to a high-confidence heuristic (a direct `UseX()` call
  somewhere in the compilation) with a suppression — "never registered anywhere" is a
  whole-program question the analyzer can only approximate. The R3 diagnostic must
  **not** force an unconditional `UseLibrary` call (that would push every app into
  rooting whole libraries, §8); it fires only when an element is actually used via the
  direct-record idiom with no registration in reach.
- **Direct-record construction guardrail.** Warn when an element record from a
  `[ReactorLibrary]` assembly is constructed directly (`new FooElement(...)`) rather
  than via its factory, mirroring the runtime message's most common cause.

## §11 Trimming and AOT

Both tracks preserve spec 048's reachability model (R5):

- **Track A is trim-neutral.** Stabilizing the seam in place adds no new roots — the
  seam is already public and reached through the existing registration link
  (factory → `Register` → handler → control). An API-compat gate is a build-time
  analyzer, not a runtime root.
- **Track B is trim-neutral.** #627 already inverted the core→feature dependencies so
  an app trims the subsystems it does not use; moving those subsystems into
  `Reactor.Advanced` changes the *assembly* they live in, not what roots them. The
  regression gates are `CoreControlFamilyBoundaryTests` (the IL boundary scan) and the
  AOT-proof harness (`tests/aot_trim_proof`), which must stay green through each move.
- **`UseLibrary` roots per-library**, a documented opt-out identical to
  `RegisterAllBuiltIns()`; self-registration discovery (§9) is opt-in only.

## §12 Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | API-compat gate tool: `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.*.txt`) or `Microsoft.DotNet.ApiCompat` against the last shipped DLL? | PublicApiAnalyzers for the authoring seam (in-repo, incremental, IDE-visible); ApiCompat as a CI backstop. |
| Q2 | Exact freeze boundary — is the *entire* §6.1 surface committed, or a named subset (e.g. is the full `MountContext` surface frozen, or only the members generated code + hand-authors actually bind)? | Freeze the members generated code and `external_proof` bind; mark the rest "public, not frozen." Settle with the interop harness. |
| Q3 | Loader policy — strong-name + pin `AssemblyVersion` now, or stay unsigned/default-ALC and only *document* the roll-forward requirement? | Document now; revisit strong-naming when the API stabilizes past preview. |
| Q4 | The data-grid **and markdown** `using static …Advanced.Factories` source break (§7) — acceptable in preview, or ship a type-forward / compat shim? | Accept in preview; call it out in release notes. A core-side compat shim isn't viable (core can't reference Advanced), so a `using static` is the honest fix. |
| Q5 | `UseLibrary` granularity — one `UseX()` per library, or sub-modules for large subsystems? | One per library by default; sub-modules where trimming a sub-surface matters (charts). |
| Q6 | Should the wrapper attributes + generator ship as a standalone `Reactor.Wrappers` package, or stay bundled in core (status quo)? | Independent of both tracks; keep bundled unless a concrete author asks — revisit later. |
| Q7 | Ship a `Microsoft.UI.Reactor.All` metapackage (core + Advanced) for a batteries-included reference? | Optional convenience; add if consumer feedback wants it. |

## §13 Phasing

- **Increment 1 — done (this change).** Public `ReactorBinding.ShouldSuppressEcho`;
  generator repointed off the last internal symbol. The wrapper-bound surface is now
  fully public (§3).
- **Track A — stable ABI in place.** A1: enumerate and lock the §6.1 seam behind the
  API-compat gate (Q1/Q2). A2: build the binary-compat interop harness. A3: settle and
  document the loader/version policy (Q3).
- **Track B — consolidate into Advanced.** B1: charting + docking → `Reactor.Advanced`.
  B2: markdown (with the §7 factory note, Q4). B3: data grid (§7 factory note, Q4).
- **Registration ergonomics — independent.** E1: `IReactorLibrary` / `UseLibrary` +
  built-ins/Advanced fold-in (§8). E2: `[ReactorLibrary]` marker + enriched throw +
  analyzer (§10). E3: opt-in `UseDiscoveredLibraries()` (§9).

Tracks A and B share no dependency and can land in either order; the ergonomics
workstream can interleave with either.
