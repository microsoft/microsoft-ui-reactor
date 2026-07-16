# Third-Party Control Registration and Package Boundaries — Design Proposal

## Status

**Proposed — design v0.3 (2026-07-16). No code changes.** This is a design
document responding to [issue #163](https://github.com/microsoft/microsoft-ui-reactor/issues/163)
("Simplify third-party control registration and package boundaries for custom
element types"). It builds on the *already-shipped* extensibility stack — the V1
handler protocol ([spec 047](047-extensible-control-model.md)), lazy trimmable
registration ([spec 048](048-control-registration-and-trimming.md)), and the
control wrapper source generator ([spec 058](058-control-wrapper-generator.md)) —
and on the packaging/distribution model ([spec 022](022-packaging-and-distribution.md)).
Its job is to settle the *delta* those specs did not cover: **package boundaries**
(carving a lightweight authoring package out of the monolithic `Reactor.dll`) and
a **standard library-registration convention** (`builder.UseMyLibrary()`).

**The decisions below are left open for the maintainer (@azchohfi).** Where this
doc lists options it states a recommendation, but the recommendation is not a
decision — [§12 Open questions](#12-open-questions) is the decision surface.

> **Review note (v0.2).** A cross-model design review (GPT-5.5 + Gemini 3.1 Pro,
> both cross-checked against source) found that the authoring types are far more
> entangled with the Reactor *runtime* (`Reconciler`, `RenderContext`, `Factories`,
> the internal `IV1HandlerEntry`/`ChangeEchoSuppressor`) than a clean "move the
> abstractions into their own assembly" framing implies. This revision folds those
> findings in: the package split is still the right direction, but **Phase 1 is a
> decoupling-design task (a runtime-seam abstraction), not a file move.** The
> affected sections (§4–§7) call out the specific coupling and the resolution.

> **Implementation-readiness note (v0.3, 2026-07-16).** Before committing to build,
> the four load-bearing assumptions were spiked directly against source. All the
> visibility/citation claims **hold**. The spike also *sharpened* three design
> points that would have cost implementation time:
> - **§5.2 correction:** the adapter cannot be materialized "after `TryResolve`" —
>   the registry is keyed by element type only, so `TControl` lives solely in the
>   `Register<E,C>` closure, and rebuilding the adapter later needs `MakeGenericType`,
>   which `ControlRegistry` forbids. The seam factory must run *inside* the generic
>   `Register` frame (new **Q13**).
> - **§5.1 breaking-change:** the `string → Element` implicit operator cannot follow
>   `Element` into Abstractions without a reference cycle; it must be dropped or moved
>   to the core DSL — a real (if small) source break (new **Q12**).
> - **§5.1/§5.3 scope:** the carve-out cluster includes `UnmountContext` **and**
>   `ReactorBinding<TElement>`, not just the two contexts.
>
> These are folded into §5 and §12. Net: the direction is sound and the seam is
> small and enumerable (§5.3 table), so the design is **ready to implement**, with
> Q9/Q12/Q13 as the Phase-1 spikes to settle first.

### North star

> A third-party control library should be able to add Reactor support by taking
> the **smallest possible dependency** — the abstractions needed to *define* a
> custom element type — without pulling in the reconciler, hosting, the built-in
> control catalog, or the optional feature libraries. App developers get **one
> obvious registration call per library**, and a missing registration fails
> **loudly and helpfully**, never as a blank screen.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Requirements](#2-requirements)
- [§3 Current state — what 047 / 048 / 058 already deliver](#3-current-state--what-047--048--058-already-deliver)
- [§4 The hard constraint — the abstractions are coupled to WinUI](#4-the-hard-constraint--the-abstractions-are-coupled-to-winui)
  - [§4.1 The deeper coupling — the authoring types entangle the runtime](#41-the-deeper-coupling--the-authoring-types-entangle-the-runtime-not-just-winui)
- [§5 Proposed package layout](#5-proposed-package-layout)
  - [§5.1 `Element` carve-out](#51-element-carve-out)
  - [§5.2 `ControlRegistry` — split the stored shape from the dispatch adapter](#52-controlregistry--split-the-stored-shape-from-the-dispatch-adapter)
  - [§5.3 Contexts, descriptors, `Reg` visibility, and the WinUI dependency](#53-contexts-descriptors-reg-visibility-and-the-winui-dependency)
- [§6 Source generators, authoring attributes, and the Abstractions boundary](#6-source-generators-authoring-attributes-and-the-abstractions-boundary)
- [§7 Library registration API — `UseLibrary`](#7-library-registration-api--uselibrary)
- [§8 Self-registration vs. explicit registration](#8-self-registration-vs-explicit-registration)
- [§9 Diagnostics](#9-diagnostics)
- [§10 Trimming and AOT](#10-trimming-and-aot)
- [§11 Migration — dogfooding with charting / data-grid / docking](#11-migration--dogfooding-with-charting--data-grid--docking)
- [§12 Open questions](#12-open-questions)
- [§13 Phasing](#13-phasing)
- [§14 Cross-version library interop — stable ABI](#14-cross-version-library-interop--stable-abi)

---

## §1 Motivation

Issue #163 lays out four coupled frustrations. Reactor is not missing
extensibility — spec 047 delivered the public `IElementHandler` /
`ControlDescriptor` surface, spec 048 made registration lazy and trim-safe, and
spec 058 added a `[GenerateReactorWrapper]` source generator that emits the
descriptor + registration + factory for a wrapped WinUI/WinRT control. What is
still awkward is the *shape of the dependency and the setup ceremony* around that
capability:

1. **The dependency is too heavy.** To *define* one custom element, a third-party
   library must reference the whole of `Reactor.dll` — the reconciler, hooks,
   hosting, every built-in control, charting, the data grid, docking. That is a
   large, opinionated dependency for a library whose own consumers may not even
   use Reactor. The alternative — shipping a *second* "MyControls.Reactor"
   integration package — doubles the maintenance surface and makes Reactor support
   look like an afterthought.

2. **Registration has no standard shape.** Registration today happens per control
   (a factory touch, or a Pattern-A static constructor — spec 048 §6). There is no
   single "wire up everything this library needs" entry point analogous to .NET
   MAUI's `builder.UseSkiaSharp()` / `ConfigureMauiHandlers(...)`. An app author
   bringing in a Reactor-enabled dependency has to *know* the library's per-control
   registration convention.

3. **Missing registration can still fail quietly.** Spec 048 already made the
   *reconciler* throw an actionable exception when it hits an unregistered element
   (see §9), but the failure only surfaces when that element is actually mounted;
   an element on a code path that renders late — or conditionally — can still
   present as missing UI until it hits. A library-scoped diagnostic ("you
   referenced *MyControls* but never called `UseMyControls()`") would catch the
   whole class up front.

4. **The package is monolithic.** Charting, the data grid, and docking all ship
   inside the one `Microsoft.UI.Reactor` package. The same split that would help
   third-party adoption also forces Reactor to prove out its own componentization
   story — a healthy pressure.

## §2 Requirements

**R1 — Minimal authoring dependency.** A library can define custom element types
by referencing one small package that carries only the authoring abstractions,
not the Reactor runtime.

**R2 — Standard registration convention.** One explicit, conventional call per
library (`builder.UseMyLibrary()` or equivalent) that centralizes all of that
library's element/handler registration and initialization.

**R3 — Loud, helpful failure.** A missing registration produces a clear
diagnostic that names the missing library and suggests the fix — never a blank
screen. (Extends 048's runtime throw with library-scoped, ideally build-time,
diagnostics.)

**R4 — Componentized layout.** Built-in features (charting, data grid, docking)
split into optional packages that mirror the third-party extensibility story.

**R5 — Do not regress the trim/AOT story.** Every proposal must preserve spec
048's load-bearing property: an app roots (and the trimmer keeps) only the
controls it actually reaches. A package split that re-roots the whole catalog is a
non-starter.

**R6 — Do not regress authoring ergonomics.** The hand-authored `IElementHandler`
recipe (spec 047, `tests/external_proof`) and the `[GenerateReactorWrapper]`
generator (spec 058) must keep working against the new, smaller dependency —
ideally with a *smaller* reference footprint, never a larger one.

**R7 — Cross-version library interop (stable ABI).** A control library built
against `Reactor.Abstractions` version *N* must load and run, **without
recompilation**, against a host that supplies a *different* core runtime version
*M* ≥ *N* — the roll-forward/unification model of `Microsoft.Extensions.*.Abstractions`.
Concretely: an app that transitively pulls `LibA` (built against Reactor *N*) and
`LibB` (built against Reactor *M*) unifies to a **single** runtime, and both
libraries' controls must mount, update, and echo correctly against it. This makes
the Abstractions surface a **versioned, additive-only contract**, not just a
smaller reference — see [§14](#14-cross-version-library-interop--stable-abi).

## §3 Current state — what 047 / 048 / 058 already deliver

This spec is a *delta*, so it is worth being precise about what already exists so
we do not re-propose it.

- **The registration runtime is already lazy and trim-safe (048).** The global
  `ControlRegistry` (`src/Reactor/Core/V1Protocol/ControlRegistry.cs`) holds
  `Type → Func<IV1HandlerEntry>` entries; every static reference to a handler /
  control type lives in the *caller* of `Register<TElement,TControl>` (a per-control
  factory cctor — Pattern A — or a `Reg<…>` static-field initializer — Pattern B),
  each on a per-control rooted path. Registration is idempotent first-wins. The
  opt-in `ReactorApp.RegisterAllBuiltIns()` roots the whole catalog for apps that
  want the direct-record idiom; apps that want a small binary simply do not call it.

- **Hand-authoring already works against the public surface only (047).**
  `tests/external_proof/Reactor.External.TestControl` ships an element + control +
  handler in a *separate assembly* with **no** `InternalsVisibleTo` — proving the
  public V1 surface is sufficient. Its `Marquee` holder (spec 048 §6, Pattern A)
  registers via a `static` cctor calling `ControlRegistry.Register<…>`.

- **Generated authoring already works (058).** `[GenerateReactorWrapper]` on a
  partial record emits the init-props, child/items slots, `On{Event}` callbacks,
  the `ControlDescriptor`, Pattern-A registration, and a factory — see
  `samples/apps/wct-controls`. The generator is a `netstandard2.0` Roslyn component
  that emits *strings* and binds attributes by metadata name, so it references
  neither `Reactor.dll` nor its own attributes assembly at build time.

- **The missing-handler failure already throws helpfully (048).**
  `Reconciler.ThrowNoHandlerRegistered` (`src/Reactor/Core/Reconciler.Mount.cs`)
  raises an `InvalidOperationException` naming the element type and listing four
  concrete fixes.

What is **not** yet present, and is the subject of this spec: a lightweight
authoring *package* (everything above still lives inside `Reactor.dll`), a
library-level `UseLibrary` convention, library-scoped diagnostics, and a
componentized feature-package layout.

## §4 The hard constraint — the abstractions are coupled to WinUI

The issue asks for a package that lets a library "add Reactor support … even when
the consumer of the library may not even be using Reactor." It is important to
state plainly what is and is not achievable here.

**A truly WinUI-free abstractions package is not feasible.** The core authoring
types are structurally coupled to WinUI:

- `Element` (`src/Reactor/Core/Element.cs`) and its subclasses reference
  `Microsoft.UI.Xaml`, `…Xaml.Controls`, `…Xaml.Media`, `Microsoft.UI.Text`, etc.
- `IElementHandler<TElement, TControl>` and `ControlDescriptor<TElement, TControl>`
  constrain `where TControl : UIElement`. The handler's whole purpose is to mount
  and patch a real WinUI control.

So "define a custom element type" *inherently* means naming WinUI types. A library
that takes the authoring dependency will transitively depend on WinUI regardless
of how we slice Reactor.

**What *is* achievable — and what the issue is really after — is decoupling from
the Reactor *runtime*, and shrinking the WinUI dependency to its smallest slice:**

- The minimal Reactor dependency becomes the authoring abstractions only — no
  reconciler, no hooks, no hosting, no built-in catalog, no feature libraries.
- The minimal **WinUI** dependency is the `Microsoft.WindowsAppSDK.WinUI`
  sub-package, **not** the full `Microsoft.WindowsAppSDK` metapackage. Windows App
  SDK 2.0 split the monolithic metapackage into independently-versioned
  sub-packages; this repo already references only `.WinUI` for framework-dependent
  library projects (see the injection rule in `Directory.Build.targets`; the WinUI
  sub-package currently tracks `2.1.0` vs the `2.1.3` aggregate). The abstractions
  package inherits that same floor — it pulls the WinUI slice, not the Runtime +
  DWriteCore redist.

This is the honest framing to carry into the design: **"minimal dependency" means
`Reactor.Abstractions` + `Microsoft.WindowsAppSDK.WinUI` — not zero Reactor and
not zero WinUI.**

### §4.1 The deeper coupling — the authoring types entangle the *runtime*, not just WinUI

The WinUI coupling above is the *easy* half. The design review surfaced a harder
one: the types we would put in `Reactor.Abstractions` currently reach into the
Reactor **runtime** (`Reconciler`, `RenderContext`, `Factories`), so a naïve file
move would drag the runtime down into the "abstractions" layer or create a
reference cycle. Concretely (all verified against source):

- **`Element` depends on the built-in factories.** `Element.cs:342` defines
  `public static implicit operator Element(string) => Factories.TextBlock(text)`,
  and `FuncElement`/`MemoElement` (`Element.cs:1510-1516`) are
  `record …(Func<RenderContext, Element> …)` — so the `Element` base file pulls
  `Factories` (built-in catalog) and `RenderContext` (core hooks runtime). Moving
  "the `Element` base" is therefore a *carve-out*, not a move (§5.1).
- **The dispatch entry is internal and names `Reconciler`.** `IV1HandlerEntry`
  (`V1HandlerRegistry.cs:30-40`) is `internal` and its `Mount`/`Update` take a
  `Reconciler`. `ControlRegistry` builds `V1HandlerAdapter` instances that
  implement it (`ControlRegistry.cs:112-113`). So `ControlRegistry` cannot move
  without either dragging `Reconciler` down or splitting its stored shape (§5.2).
- **The public author contexts expose `Reconciler`.** `MountContext` holds and
  exposes a `Reconciler` "escape hatch" (`MountContext.cs:50-65`);
  `ControlDescriptor.OneWayBridged` takes a delegate over `Reconciler`;
  `DescriptorHandler` calls `ctx.Reconciler`. Any of these in Abstractions drags
  the runtime (§5.3).
- **The generator emits direct runtime calls** (`Reconciler.SetElementTag`,
  `GetElementTag`, `DetachReactorState`, `ApplySetters`, plus
  `ReactorApp.TryRegisterControlAssembly` and `ElementExtensions.OnMount/UnmountAdd`,
  and the internal `ChangeEchoSuppressor.ShouldSuppress`) — the full closure and
  its resolution are §6.

**None of this kills the proposal** — it changes its shape. The unit of work in
Phase 1 is *designing a minimal runtime seam* (a small interface / delegate surface
in Abstractions that the core `Reconciler` implements) so the authoring types name
the seam, not `Reconciler`. §5 and §6 specify that seam's required members.

## §5 Proposed package layout

A layered set of packages, each depending only on the layer below:

| Package | Contains | Heavy deps |
|---|---|---|
| **`Microsoft.UI.Reactor.Abstractions`** | `Element` primitive base + the authoring contract (element records' modifier/attribute surface), `IElementHandler` / `IDecoratorElementHandler`, `ControlDescriptor` + descriptor kinds (`SingleContent` / `Panel` / `ItemsHost`), `PropEntry`, `ControlRegistry` (storing a **neutral** registration record — §5.2), `ReactorBinding` primitives, the author-facing `MountContext`/`UpdateContext`/`UnmountContext` re-shaped over a **runtime-seam interface** (§5.3), and that seam interface itself. **Not** here: `Reconciler`, `RenderContext`, `Factories`, `FuncElement`/`MemoElement`, `IV1HandlerEntry`/`V1HandlerAdapter`, the internal `Reg`/`ChangeEchoSuppressor` (§5.1–5.3). | `Microsoft.WindowsAppSDK.WinUI` |
| **`Microsoft.UI.Reactor`** (core) | Reconciler, hooks, `RenderContext`, hosting (`ReactorApp` / windows / render loop), the built-in control catalog + DSL factories (`Factories`, `Dsl.cs`), Flex/Yoga. | `Reactor.Abstractions` |
| **`Microsoft.UI.Reactor.Charting`** | The charting subsystem (`src/Reactor/Charting`). | `Reactor` |
| **`Microsoft.UI.Reactor.DataGrid`** | The data grid (`src/Reactor/Controls/DataGrid`). | `Reactor` |
| **`Microsoft.UI.Reactor.Docking`** | Docking windows (`src/Reactor/Docking`). | `Reactor` |
| **`Microsoft.UI.Reactor.Advanced`** *(exists)* | Win2D canvas (spec 053). | `Reactor` + Win2D |
| **`Microsoft.UI.Reactor.All`** *(metapackage)* | No code — references core + every feature package for a "batteries-included" experience. | all of the above |

Notes:

- **A third-party control library references `Reactor.Abstractions` only** (R1).
  The existing `tests/external_proof` project is the acceptance shape: it should
  compile and register against `Reactor.Abstractions` with no reference to the
  core runtime.
- **An app references `Microsoft.UI.Reactor`** (which brings the reconciler +
  built-ins), plus whichever feature packages it wants — or `…Reactor.All` for
  everything. Because feature packages self-register lazily (048), referencing a
  feature package does **not** root its whole surface unless the app actually uses
  it (R5).
- **The split is a boundary move, not a rewrite.** `Reactor.Advanced` already
  proves the "feature library as a separate package that references core" shape;
  charting / data-grid / docking follow the same pattern (§11).
- **`ControlRegistry` lives in Abstractions, not core.** Both the *definer* side
  (a library calling `Register`) and the *consumer* side (the reconciler calling
  `TryResolve`) need it, and the reconciler's dependency flows the correct
  direction (core → abstractions). `TryResolve` / the base-derived cache are
  `internal`; the reconciler in the core package reaches them via
  `InternalsVisibleTo` (already the pattern between test assemblies).

- **`ControlRegistry` lives in Abstractions, but its stored shape must be split
  from core's dispatch adapter** (§5.2). Both the *definer* side (a library calling
  `Register`) and the *consumer* side (the reconciler resolving) need the registry,
  but the internal `IV1HandlerEntry`/`V1HandlerAdapter` that it currently constructs
  name `Reconciler` and stay in core.

**Open design point:** the exact contents of the Abstractions package are *defined*
by §6's emitted-reference closure plus the hand-author surface **plus the §4.1
runtime seam**, not chosen freely. Anything the generator or a hand-written handler
*names* must resolve to Abstractions (directly, or via the seam interface that core
implements); anything else stays in core. That closure is the acceptance criterion,
and resolving it — §5.1 (`Element` carve-out), §5.2 (`ControlRegistry` split), §5.3
(contexts/descriptors/`Reg`) — is the main engineering risk of Phase 1.

### §5.1 `Element` carve-out

"Move the `Element` base to Abstractions" is a carve-out, not a file move (§4.1).
Grounding note: the whole element catalog lives in one 6865-line `Element.cs`, so
this is also a physical file split (the base record + contracts move; every
concrete built-in element record — `TextBlockElement` at `Element.cs:2616`, etc. —
stays in core).

- **The base `Element` record itself is nearly clean.** Verified: it carries only
  `Key`, `Modifiers` (`ElementModifiers`, `Element.cs:1768` — WinUI *value* types,
  fine under the §4 WinUI floor), and the outer-margin shim. The one true runtime
  coupling is the implicit `string → Factories.TextBlock` conversion (`Element.cs:342`).
- **The implicit-conversion carve-out is a breaking-change decision, not a free move.**
  A `string → Element` implicit operator **must be declared on `Element`** (the other
  operand, `string`, is a sealed BCL type). Its body needs a *concrete built-in*
  (`Factories.TextBlock` → `new TextBlockElement(...)`, `Element.cs:2616`) that stays
  in core — so if `Element` moves to Abstractions and keeps the operator, we get an
  **Abstractions → core reference cycle**. There is no in-place fix: the operator
  must either be **dropped** (source-breaking — container factories like `VStack("hi")`
  / `Grid("x")` rely on `string → Element` today) or the string-convenience must be
  **relocated into the core DSL** as `params`-overloads on the container factories.
  Recommendation: drop the implicit operator, add `string`-accepting convenience to
  the core containers; document as a one-time DSL migration. (New open question Q12.)
- `FuncElement`/`MemoElement` (`Element.cs:1510-1516`) depend on `RenderContext`
  (a core hooks-runtime type). **Resolution:** these are *composition primitives*,
  not authoring contracts — leave them (and the animation/resource/input
  conveniences) in **core**, and move only the pure `Element` record base + the
  modifier/attribute contract to Abstractions.
- **The author-facing type cluster that holds a `Reconciler` field is larger than
  just the contexts.** Verified: `MountContext` (`MountContext.cs:50`),
  `UpdateContext` (`:128`), `UnmountContext` (`:166`) **and** `ReactorBinding<TElement>`
  (`ReactorBindingT.cs`, constructed with `_reconciler` at `MountContext.cs:92`) all
  store a `Reconciler`. All four must be re-typed against the §5.3 seam, not just the
  two contexts the earlier draft named. `ReactorBinding<TElement>` is on the author
  hot path (`ctx.BindFor(ctrl, el).WriteSuppressed(...)`), so it is a required
  carve-out target.
- Net: Abstractions gets the pure `Element` primitive, the handler/descriptor
  contract, the three contexts, and `ReactorBinding<TElement>` (all seam-typed); core
  keeps `Factories`, `RenderContext`, `FuncElement`/`MemoElement`, the concrete
  element catalog, and the composition sugar. The carve-out line is "does a *definer*
  of a custom element need it?" — if not, it stays in core.

### §5.2 `ControlRegistry` — split the stored shape from the dispatch adapter

`ControlRegistry.Register<TElement,TControl>` currently wraps the handler factory
in a `V1HandlerAdapter` implementing the **internal** `IV1HandlerEntry`, whose
`Mount`/`Update`/`Unmount` take a `Reconciler` *in their signatures*
(`V1HandlerRegistry.cs:32,40,46`; adapter built at `ControlRegistry.cs:112-113`).
Moving the registry as-is drags `Reconciler` into Abstractions.

**Grounding correction (this changes the recommended mechanism).** The earlier
draft said "core materializes the adapter *after* `TryResolve`." That is **not
AOT-legal as written**: the registry dictionary is keyed by `typeof(TElement)`
*only* (`ControlRegistry.cs:118`), so the closed `TControl` survives **solely inside
the `Register<TElement,TControl>` generic-frame closure**. Rebuilding
`V1HandlerAdapter<TElement,TControl>` at dispatch time — outside that frame — would
require `Type.MakeGenericType`, which the registry **explicitly forbids**
(`ControlRegistry.cs:38-43`, the AOT contract). The adapter *must* be constructed
in the generic `Register` frame. So the split is:

- **Abstractions** holds the public `Register`/`RegisterDecorator`/
  `RegisterForDerivedTypes` surface and stores a **type-erased** `Func<object>`
  entry keyed by element type. The adapter is still produced *inside* the generic
  `Register<TElement,TControl>` frame (preserving static `TControl` visibility — no
  `MakeGenericType`), via one of:
  1. **Move `IV1HandlerEntry` + `V1HandlerAdapter` + `V1DecoratorHandlerAdapter`
     into Abstractions, re-typed from `Reconciler` to the §5.3 seam.** Cleanest for
     `Register` (its body is unchanged), but the adapters are *heavy* — they do child
     -strategy dispatch, element-tag anchoring, and component-teardown against the
     reconciler (`V1HandlerAdapter.cs:29-120`), so this pulls a lot of logic behind
     the seam.
  2. **Keep the adapters in core**, and have Abstractions' `Register<E,C>` call a
     core-supplied generic factory seam (`IReactorRuntime.CreateEntry<E,C>(handler)`)
     *from within* its own generic frame, storing the returned `Func<object>`. Keeps
     the heavy adapter logic in core; the only new cost is one generic interface
     method (statically instantiated per call site → AOT-legal, still no
     `MakeGenericType`).
- **Core** downcasts the stored `object` back to `IV1HandlerEntry` on the dispatch
  path (`TryResolve` consumer). `TryResolve` itself (`ControlRegistry.cs:267`) must
  then return the neutral `Func<object>`, not `Func<IV1HandlerEntry>`.

Recommendation: **option 2** — it keeps the reconciler-coupled adapter logic in core
and moves the minimum across the boundary. This is a Phase-1 spike (Q9/Q13). The
public `Register` surface and the first-wins/base-derived semantics of 048 §8 stay
intact either way.

### §5.3 Contexts, descriptors, `Reg` visibility, and the WinUI dependency

- **Author contexts must stop exposing `Reconciler`.** `MountContext` exposes a
  public `Reconciler` escape hatch (`MountContext.cs:64`; `UpdateContext.cs`-equiv
  `:138`, `UnmountContext` `:174`); `ControlDescriptor.OneWayBridged` and
  `DescriptorHandler` consume it. **Resolution:** replace the public `Reconciler`
  member with the minimal **runtime-seam** the context surface + generated code
  actually need. Grounded against source, the seam is small and enumerable — the
  union of what `MountContext`/`UpdateContext`/`UnmountContext` delegate to the
  reconciler plus what the generator emits:

  | Seam member | Backing today | Used by |
  |---|---|---|
  | `Mount(Element, Action)` | `_reconciler.Mount` (`MountContext.cs:69`) | `MountChild` |
  | `ReconcileV1Child(old,new,existing,rerender)` | `:79` | `ReconcileChild`, `[WrapElementSlot]` |
  | `RentControl<T>(policy,factory)` | `:97` | `MountContext.RentControl` |
  | `PushContextDisposable<T>` / `PushStaggerScopeDisposable` | `:102,:106` | context/stagger scopes |
  | `ReturnControl<T>` | `UnmountContext` `:179` | unmount pooling |
  | `SetElementTag` / `GetElementTag` / `DetachReactorState` | `Reconciler` `public static` `516/627/732` | generator (`1233,1243,1253,1313,1483,1498`) |
  | `ApplySetters<T>` | `Reconciler` `public static 2647` (also `ctx.ApplySetters` `:84`) | generator (`1314,1328,1332`) |
  | read-side echo check | `ChangeEchoSuppressor.ShouldSuppress` **internal** `68` | generator deferred path (`1497`) — the §6.2 gap |
  | assembly-register | `ReactorApp.TryRegisterControlAssembly` `public static`, **Hosting** `282` | generator (`1716`) |
  | lifecycle add | `ElementExtensions.OnMountAdd/OnUnmountAdd` `2470/2495` | generator (`1784,1786`) |

  Notes that fell out of the spike: (a) `ApplySetters` is a **static** method, so it
  is a static helper on the seam/Abstractions, not an instance member; (b) the
  assembly-register member currently lives in **Hosting** (`ReactorApp`) — the seam
  lets generated code name Abstractions instead of dragging hosting; (c) `BindFor`
  constructs `ReactorBinding<TElement>`, so that type is part of the carve-out (§5.1),
  not the seam. Abstractions declares the seam interface (`IReactorRuntime`, working
  name); core's `Reconciler` implements it (shape is **Q9**). Any descriptor entry
  that genuinely needs the full `Reconciler` (rare) stays a core-only API.
- **`Reg`/`RegDecorator`/`RegBase` are `internal`** (`Reg.cs:66`), and 048 §8
  deliberately steers 3P authors to the *public* `ControlRegistry.Register`. Feature
  packages (§11) that want Pattern-B scale registration would need these.
  **Decision required (Q7):** either (a) keep `Reg` internal and have feature
  packages self-register via generated Pattern-A cctors (the 058 path), or (b)
  promote a supported *public* bulk/factory registration primitive. Recommendation:
  (a) — do not widen `Reg` to public; feature packages use the same generated
  Pattern-A shape a 3P library uses, which is the dogfooding point.
- **The Abstractions NuGet declares its own `Microsoft.WindowsAppSDK.WinUI`
  dependency and TFM rules.** External consumers do **not** inherit this repo's
  `Directory.Build.targets` injection rule, so the package's `.nuspec`/csproj must
  carry the `.WinUI` sub-package reference and the `net10.0-windows…` TFM explicitly
  (mirroring how `Reactor.Advanced` is packaged).
- **The Abstractions NuGet declares its own `Microsoft.WindowsAppSDK.WinUI`
  dependency and TFM rules.** External consumers do **not** inherit this repo's
  `Directory.Build.targets` injection rule, so the package's `.nuspec`/csproj must
  carry the `.WinUI` sub-package reference and the `net10.0-windows…` TFM explicitly
  (mirroring how `Reactor.Advanced` is packaged).

## §6 Source generators, authoring attributes, and the Abstractions boundary

This section answers the review question: *where do the generator and its
attributes live, and should the wrappers be their own NuGet package?*

### §6.1 What the generator emits — the closure that defines the boundary

The wrapper generator (`src/Reactor.Wrappers.Generator/WrapperGenerator.cs`) emits
source that references a broad set of `Reactor.Core` / `V1Protocol` types. A scan
of the emit paths (line numbers verified against the current source) shows the
closure is **larger than a v0 draft assumed** — it includes direct static calls
into the runtime, not just type names:

*Authoring-contract types (map cleanly to Abstractions):*

- `Microsoft.UI.Reactor.Core.Element`
- `…Core.V1Protocol.ControlRegistry` (`Register` / `RegisterDecorator`) — `1716`, `1262`
- `…V1Protocol.Descriptor.ControlDescriptor<TElement,TControl>`, `DescriptorHandler<…>`
- `…V1Protocol.SingleContent<…>`, `Panel<…>`, `ItemsHost<…>`, `Descriptor.PropEntry`
- `…Core.ReactorBinding` / `ReactorBinding<TElement>`
- `…V1Protocol.MountContext` / `UpdateContext` / `UnmountContext`

*Runtime members (the real work — these must resolve through the §5.3 seam, not a move):*

- `Reconciler.SetElementTag` — `1233`, `1243`, `1313`, `1327`, `1331`
- `Reconciler.GetElementTag` — `1483`, `1498` (event trampolines)
- `Reconciler.DetachReactorState` — `1253`
- `Reconciler.ApplySetters` — `1314`, `1328`, `1332`
- `ChangeEchoSuppressor.ShouldSuppress` — `1497` (**internal**, read-side)
- `ReactorApp.TryRegisterControlAssembly` — `1716`
- `ElementExtensions.OnMountAdd` / `OnUnmountAdd` — `1784`, `1786`

**The acceptance criterion for the package split is: *this entire emitted-reference
closure must resolve against `Reactor.Abstractions`*** — either because the type
lives there (authoring-contract types) or because the call goes through the §5.3
runtime-seam interface that core's `Reconciler` implements (runtime members). If any
emitted symbol resolves only to `Reactor.dll`, generated wrapper code in a library
that references only `Reactor.Abstractions` fails to compile — or transitively drags
full Reactor — defeating R1.

The runtime members are the sharp edge, and two need explicit correction from the
v0 draft:

- **`Reconciler.*` are direct `public static` calls, not "namespace anchors."**
  `SetElementTag`/`GetElementTag`/`DetachReactorState`/`ApplySetters` are element-tag
  and setter primitives the generated mount/update/trampoline code calls directly.
  **Resolution:** put these on the Abstractions runtime seam (e.g. as methods on the
  re-shaped `MountContext`/`UpdateContext`, or a static `ReactorElementState` helper
  in Abstractions whose implementation core supplies). The generator then emits
  `ctx.SetElementTag(...)` / `ctx.GetElementTag(...)` instead of naming `Reconciler`.
- **`ChangeEchoSuppressor.ShouldSuppress` is a READ-side check** (`1497`), so the v0
  "emit `WriteSuppressed` instead" idea is wrong — `WriteSuppressed` only *arms* the
  token on the write; the generated event trampoline still needs a way to *consume*
  it and early-return on the echo. **Resolution options:** (a) expose a public
  read-side primitive in Abstractions (e.g. `ReactorBinding.IsEchoSuppressed(control)`)
  that core's suppressor backs; (b) route generated deferred-controlled props
  exclusively through the `.Controlled` descriptor entry (`1582-1611`), which already
  avoids the direct suppressor reference, and **forbid** the raw-trampoline
  deferred path in Abstractions-only wrappers; (c) a thin Abstractions shim the
  suppressor implements. **Recommendation: (a)+(b)** — prefer `.Controlled`, and
  provide the public read-side primitive for the cases that genuinely need the
  deferred trampoline. `ShouldSuppress` being `internal` today is exactly why this
  path does not yet compile for a true external assembly (§6.2).
- **`ReactorApp.TryRegisterControlAssembly`, `OnMountAdd`/`OnUnmountAdd`** likewise
  join the seam (assembly-register + lifecycle-add), so generated lifecycle/registration
  code names Abstractions surface, not `ReactorApp`/`ElementExtensions` in core.

Resolving this closure (i.e. designing the §5.3 seam and re-pointing the generator's
emit at it) is **Phase 1 work** and is the gating dependency for §6.3 Shape B.

### §6.2 What is already decoupled

- **The generator** references neither `Reactor.dll` nor its attributes assembly at
  build time (it emits strings, binds by metadata name). It ships today as an
  analyzer asset (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`).
- **The attributes** (`Reactor.Wrappers.Abstractions` — `[GenerateReactorWrapper]`,
  `[WrapControlled]`, `[WrapEvent]`, `[WrapElementSlot]`, `[WrapLifecycle]`, …)
  are a standalone assembly with **no** reference to `Reactor.dll`. Today it is
  `IsPackable=false` and bundled into the `Microsoft.UI.Reactor` package's `lib/`
  via the `_BundleWrappersAbstractions` target (`PrivateAssets="all"` so NuGet
  emits no phantom dependency).

Net effect today: an author gets the generator + attributes **only** by referencing
the full `Microsoft.UI.Reactor` package. Splitting them out is a real packaging
change, not a rename.

> **Latent gap worth noting.** Because the generator emits a call to the
> **internal** `ChangeEchoSuppressor.ShouldSuppress` (§6.1), the deferred-controlled
> trampoline path only compiles in assemblies granted `InternalsVisibleTo` from
> Reactor core (tests, devtools) — *not* a true external library. So one generated
> code path already fails the "public surface is sufficient" bar today; the §6.1
> read-side-primitive work fixes it as a side effect.
>
> **Verified against source (2026-07-16).** The emit is literal — `WrapperGenerator.cs:1497`
> writes `if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(__c)) return;`
> and `ChangeEchoSuppressor` is `internal static` (`ChangeEchoSuppressor.cs:49`). The
> IVT grants (`Reactor.csproj`) go only to `Reactor.Tests`, `Reactor.AppTests.Host`,
> `Reactor.Fuzz`, `Reactor.Markdown.TestRenderer`, `Microsoft.UI.Reactor.Devtools`,
> `PerfBench.*` — **not** `tests/external_proof`. So `tests/external_proof/Reactor.External.TestControl`
> is the exact Phase-1 gate: add a **deferred** `[WrapControlled]` prop there and it
> fails to compile *today*, proving the gap; the public read-side primitive (Q2/Q11)
> is what makes it pass. Note the **non-deferred** `.Controlled` path is already clean
> — echo suppression is encapsulated in the public descriptor entry
> (`WrapperGenerator.cs:1584`, no internal reference).

### §6.3 Three candidate shapes (evaluate, then decide)

**Shape A — fold into Abstractions.** The attributes fold into
`Reactor.Abstractions`; the generator ships as an analyzer asset *inside*
`Reactor.Abstractions`. A single `<PackageReference Include="Microsoft.UI.Reactor.Abstractions">`
gives an author the abstractions, the attributes, and the generator — covering both
hand-authoring and generated authoring.
- *Buys:* one reference for everything an author needs; nothing extra to publish.
- *Costs:* bundles a build-only analyzer into a runtime-carrying package (minor —
  analyzers are `PrivateAssets`/build-time and don't flow to the author's own
  consumers); a hand-author who never uses the generator still *has* it available
  (harmless, unused).

**Shape B — standalone `Reactor.Wrappers` (the review proposal).**
`Reactor.Abstractions` carries runtime types only; a separate `Reactor.Wrappers`
package carries the attributes + generator and takes a normal (transitive)
`PackageReference` on `Reactor.Abstractions`.
- *Buys:* a hand-author (external_proof/Marquee style) references only
  `Reactor.Abstractions` and never pulls the generator; a `[GenerateReactorWrapper]`
  user adds exactly one package (`Reactor.Wrappers`) that transitively brings
  Abstractions + attributes + generator; the generator/attributes can version
  independently of the runtime and of Abstractions.
- *Costs:* an extra package to build / sign / publish / version (spec 022 CI
  surface). Its entire value is **contingent** on §6.1 — the emitted closure must
  already be ⊆ Abstractions, else generated code forces a full-Reactor reference and
  the split buys nothing.

**Shape C — bundled in core (status-quo mechanics).** The attributes + generator
stay bundled inside `Microsoft.UI.Reactor`, unchanged.
- *Buys:* zero new packaging work; one obvious "batteries-included" reference for
  app authors; no new versioning coordination.
- *Costs:* an author who wants only to *define* a custom element still references
  the full runtime — the exact friction #163 raises (R1). It also does not compose
  with the Abstractions split unless the emitted closure is *also* reachable from
  Abstractions; if it is, then Shape C is effectively "Shape A's contents, shipped
  in the core package instead of the abstractions package," which re-imposes the
  heavy dependency on definers.

**Hybrid B+C.** Ship the attributes + generator standalone (`Reactor.Wrappers`,
Shape B) *and* have the core `Microsoft.UI.Reactor` package transitively include
them (so batteries-included app authors need not add a second reference). Costs the
Shape B publishing surface but gives both the minimal-definer path and the
one-reference app path.

### §6.4 Recommendation

The wrappers-packaging choice (A/B/C) is a **packaging decision that only becomes
real once the §6.1 runtime closure is clean** — until generated code resolves fully
against `Reactor.Abstractions`, *no* packaging arrangement lets a definer avoid the
full-Reactor reference. So the recommendation is sequenced:

1. **Phase 1 gate first:** design the §5.3 seam and re-point the generator's emit at
   it (§6.1). This is the load-bearing work; packaging is downstream of it.
2. **Then adopt Shape B, in the B+C hybrid form:** `Reactor.Abstractions` carries
   the authoring types; a standalone `Reactor.Wrappers` (attributes + generator)
   takes a transitive reference on it; the core `Microsoft.UI.Reactor` package also
   includes `Reactor.Wrappers` so app authors keep a single reference. This is the
   only shape that fully satisfies R1 for a *definer* (references `Reactor.Abstractions`
   alone; adds `Reactor.Wrappers` only to opt into generation) while keeping the
   app-author experience batteries-included.

**Fallbacks, corrected from v0.1:**

- If the §6.1 closure work is *not yet done*, **Shape A does not rescue it** — folding
  the generator into `Reactor.Abstractions` does nothing if the emitted code still
  names `Reactor.dll` types; the definer still transitively pulls full Reactor. So the
  genuine interim fallback is **Shape C (status quo: everything in
  `Microsoft.UI.Reactor`)** *or* a **reduced generator feature set** (emit only the
  subset whose closure is already ⊆ Abstractions, e.g. one-way/leaf wrappers, and
  defer controlled/deferred paths). Shape A is only meaningful *after* the closure is
  clean, at which point Shape B is strictly better anyway.
- Shape C long-term is not recommended (it does not move the definer off the full
  runtime — the core R1 friction), but it is the correct *no-op* state if the split
  is deferred entirely.

## §7 Library registration API — `UseLibrary`

Give Reactor a MAUI-style, one-call-per-library registration convention (R2).

### §7.0 What `UseLibrary` is *for* — and what it deliberately is not

The design review flagged a real tension: **per-control factory self-registration
(spec 048 / Pattern A) is already the trim-safe default**, and it stays that way.
Calling `Marquee.Of(...)` or a generated `SettingsCard(...)` factory registers *that
one control* on first use, rooting nothing else. `UseLibrary` must **not** be
positioned as the thing an app "has to call or the library won't work," because a
`Register` body that news up every control would root the whole library — exactly
the per-control trimming 048 fought to get, lost.

So `UseLibrary` is scoped to what per-control factory registration *cannot* do:

1. **Library-level initialization** that is not per control — XAML metadata provider
   registration, resource dictionaries, one-time service setup.
2. **Base-derived registrations** (`RegisterForDerivedTypes`) that intentionally
   cover a family in one entry.
3. **The direct-record idiom opt-in** — an app that builds element records directly
   (`new FooElement(...)`) instead of via factories needs *something* to register;
   `UseLibrary` is that opt-in, and — like `RegisterAllBuiltIns()` — it is
   **explicitly documented as a bulk trim opt-out**, not the default path.
4. **Discoverability + diagnostics** (§9) — a single call site the analyzer and the
   runtime throw can point at.

**The trim-safe default remains: reference the library, use its factories, pay only
for the controls you touch.** `UseLibrary` is the convenience/robustness layer on
top, with its rooting cost called out.

### §7.1 The contract

```csharp
namespace Microsoft.UI.Reactor;

/// <summary>One place a Reactor-enabled library wires up everything it needs:
/// element/handler registration, generated metadata, initialization.</summary>
public interface IReactorLibrary
{
    void Register(IReactorLibraryBuilder builder);
}
```

`IReactorLibraryBuilder` is a thin façade over `ControlRegistry` (+ any future
per-library metadata/init hooks) so a library never touches the registry directly
and Reactor keeps a seam to add cross-cutting setup later:

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

### §7.2 The app-facing call

Fold into the existing `ReactorApp` startup surface (`src/Reactor/Hosting/ReactorApp.cs`):

```csharp
ReactorApp.CreateBuilder()
    .UseLibrary<MyControls.MyControlsLibrary>()   // one call per library
    .UseLibrary(new AcmeCharts.AcmeChartsLibrary(options))
    .Run(App);
```

`UseLibrary<T>()` news up `T` (parameterless) and calls `Register`; the instance
overload supports libraries that need construction options. A library exposes a
friendly extension so app authors get the MAUI feel:

```csharp
namespace MyControls;
public static class MyControlsRegistration
{
    public static IReactorAppBuilder UseMyControls(this IReactorAppBuilder b)
        => b.UseLibrary<MyControlsLibrary>();
}
```

### §7.3 How built-ins and feature packages fold in

The existing `ReactorApp.RegisterAllBuiltIns()` (spec 048 §3.4 option A) *is* the
built-in library's `Register` body. Reframe it as the built-in `IReactorLibrary`
so the whole model is uniform: `RegisterAllBuiltIns()` becomes `UseLibrary<BuiltInControlsLibrary>()`
(the old method stays as a compatibility shim). Each feature package
(`…Charting`, `…DataGrid`, `…Docking`) ships its own `UseCharting()` /
`UseDataGrid()` / `UseDocking()` extension over the same interface. This directly
dogfoods the third-party model with Reactor's own components (R4, §11).

**Trim note (corrected).** `UseLibrary<T>()` names `T`, whose `Register` body names
whatever it registers — so calling it roots exactly that surface. If `Register`
bulk-registers every control, `UseLibrary` roots the whole library: that is the
**same trim opt-out shape as `RegisterAllBuiltIns()`**, not the per-control default.
Per §7.0, this is *intended* — `UseLibrary` is the convenience/direct-record path,
and libraries should keep their per-control factories self-registering so an app
that prefers the factory path (and never calls `UseLibrary`) still pays only for the
controls it touches (R5). Two consequences the design must honor:

- **The R3 "you forgot to register" diagnostic (§9) must NOT force an unconditional
  `UseLibrary` call**, or it would push every app into rooting whole libraries. It
  fires only when an element is *actually used via the direct-record idiom* with no
  registration in reach — not merely because a `[ReactorLibrary]` package is
  referenced.
- **Finer-grained modules are allowed.** A large library may expose
  `UseChartsCore()` / `UseChartsInteractive()` rather than one all-controls
  `UseCharts()`, so an app roots only the sub-surface it needs. Recommended for the
  feature packages in §11.

## §8 Self-registration vs. explicit registration

The issue asks whether custom element types can register themselves (static
initialization / assembly discovery) instead of requiring an explicit setup call.
Spec 048 already analyzed and **rejected the unconditional-eager forms** for the
built-in catalog; that analysis carries over.

- **`[ModuleInitializer]` / unconditional assembly-load registration is a trimmer
  root.** It runs on first type-load of the assembly, so the trimmer can never
  prove it dead, and its body names every handler + control → the whole library is
  kept. This defeats R5. Spec 048 §3.4 deleted exactly this shape (the eager
  `RegisterV1BuiltInHandlers` bootstrap) for that reason.

- **Reflection-based assembly scanning** (enumerate loaded assemblies for
  `[ReactorLibrary]` and invoke each) is AOT-hostile and startup-costly, and roots
  types the trimmer would otherwise drop.

**Recommendation:** keep *explicit* registration the norm over eager auto-discovery,
and offer self-registration only as **opt-in**. "Explicit" here means the two
trim-honest paths from §7: per-control factory self-registration (the trim-safe
**default** — pay only for controls you touch) and `UseLibrary` (the bulk /
direct-record opt-in, itself a documented trim opt-out per §7.0). Both are
AOT-friendly and statically discoverable by the trimmer; neither relies on
load-time or reflection-driven rooting. Layer *optional* discovery on top:

1. **`[assembly: ReactorLibrary(typeof(MyControlsLibrary))]`** — a *marker*, not an
   eager root. It does not register anything at load time. It exists so (a)
   diagnostics (§9) can point a developer at the exact `UseX()` they forgot, and
   (b) an *explicitly opted-in* discovery pass (for non-trimmed apps that value
   convenience over binary size) can find libraries to auto-`UseLibrary`.

2. **`ReactorApp…UseDiscoveredLibraries()`** — an explicit opt-out of the trim
   story (mirrors `RegisterAllBuiltIns()`): an ordinary method the trimmer removes
   when unreachable, that scans `[assembly: ReactorLibrary]` markers and registers
   each. Apps that want zero ceremony and don't care about trimming call it; apps
   that want a small binary do not. **It (and any reflection-based marker scan) is
   annotated `[RequiresUnreferencedCode]`** so the trim/AOT analyzers — which this
   repo treats as hard errors (`Directory.Build.targets`) — flag it at the call
   site rather than letting it silently defeat trimming.

This keeps the trim-safe default while giving the "I don't want to remember setup"
crowd a documented, opt-in escape hatch — the same philosophy as 048's
`RegisterAllBuiltIns()`.

## §9 Diagnostics

Layer library-scoped diagnostics on top of 048's existing runtime throw (R3).

- **Keep and enrich the runtime throw.** `ThrowNoHandlerRegistered` already lists
  four fixes. Enrich it: when the unregistered element's *assembly* carries
  `[assembly: ReactorLibrary(typeof(X))]`, name the specific missing call — e.g.
  *"`MarqueeElement` is defined in `MyControls`, which declares a Reactor library
  but was never registered. Call `builder.UseMyControls()` (or
  `UseLibrary<MyControls.MyControlsLibrary>()`) at startup."* This turns the most
  common third-party failure from a generic message into an exact instruction.
  **Keep this enrichment reflection-light and AOT-safe:** read the marker via the
  already-loaded `element.GetType().Assembly` custom-attribute (the element type is
  necessarily loaded at the throw site, so no extra assembly is force-loaded and no
  scan runs), and guard it so a trimmed-away attribute simply falls back to the
  generic message. Prefer the build-time analyzer below as the *primary* signal;
  the runtime enrichment is the backstop.

- **Add a build-time analyzer / `mur check` rule.** Flag the case where a project
  references a library that declares `[ReactorLibrary]` but the app never calls the
  corresponding `UseX()` / `UseLibrary<>()` — catching the whole class of "forgot
  to register" up front rather than at first mount. This lives alongside the
  existing analyzer suite (spec 060) and the `mur check` did-you-mean rules
  (spec 038). *Caveat:* detecting "never registered anywhere in the app" reliably is
  a whole-program question the analyzer can only approximate (registration can be
  indirect); scope it to a high-confidence heuristic (a direct `UseX()` call
  somewhere in the compilation) with a suppression, per the §4-core discipline in
  spec 060.

- **Direct-record construction guardrail.** Extend the analyzer to warn when an
  element record from a `[ReactorLibrary]` assembly is constructed directly
  (`new FooElement(...)`) rather than via its factory, mirroring the runtime
  message's "most common cause."

## §10 Trimming and AOT

Every proposal above is constructed to preserve spec 048's reachability model (R5):

- **Package split (§5) is trim-neutral.** Carving `Element` / `ControlRegistry`'s
  stored shape / descriptors into `Reactor.Abstractions` — and routing runtime calls
  through the §5.3 seam — does not change *what roots what*: the registration link
  (factory → `Register` → handler → control) is unchanged; only the assembly the
  link's endpoints live in changes, and the seam is an interface the trimmer follows
  like any other. The trimmer follows references across assemblies exactly as within
  one.

- **`UseLibrary<T>()` (§7) roots per-library.** It names `T`; `T.Register` names
  that library's controls. No `[ModuleInitializer]`, so an unreached `UseX()` is
  removed with everything it names — identical to `RegisterAllBuiltIns()` today.

- **Self-registration (§8) stays opt-in.** The `[ReactorLibrary]` marker roots
  nothing; only the explicit `UseDiscoveredLibraries()` opt-out re-roots, and it is
  an ordinary (removable) method.

- **Abstractions package sets `IsAotCompatible=true`** (as the core library does),
  so trim/AOT analyzers run on it; `ControlRegistry` already avoids reflection and
  `MakeGenericType` (048 §8), which the move preserves.

- **`Reactor.Abstractions` `IsTrimmable` / self-contained flags** follow the
  existing library conventions (`WindowsAppSDKSelfContained=false`; only app exes
  own self-contained packaging).

The AOT-proof harness (`tests/aot_trim_proof`) is the regression gate: after the
split it must still show the Hello-World app dropping every control it doesn't use,
and the external-proof AOT publish (spec 048 §11) must still succeed with the
library referencing only `Reactor.Abstractions`.

## §11 Migration — dogfooding with charting / data-grid / docking

Reactor's own optional features are the proving ground (R4). The `Reactor.Advanced`
package (Win2D, spec 053) already demonstrates the shape: a feature library in a
separate package/assembly that references core and self-registers lazily. The
migration extracts charting, data grid, and docking the same way:

1. Move `src/Reactor/Charting` → `src/Reactor.Charting` (new project, references
   `Reactor`), ship as `Microsoft.UI.Reactor.Charting`. Expose `UseCharting()`.
2. Repeat for `src/Reactor/Controls/DataGrid` → `Reactor.DataGrid` and
   `src/Reactor/Docking` → `Reactor.Docking`.
3. Provide `Microsoft.UI.Reactor.All` metapackage referencing core + all features
   so existing "everything" consumers get a one-line migration.
4. Update the ReactorGallery + samples to reference the specific feature packages
   they use — proving the split from the consumer side and exercising the
   `UseX()` convention end-to-end.

Each extraction is gated on: no `internal` cross-boundary reach that
`InternalsVisibleTo` can't cleanly express, no regression in the AOT-proof harness,
and the feature's selftests/E2E still green.

## §12 Open questions

These are the decision surface for @azchohfi. Each states a recommendation but is
`TBD`.

| # | Question | Recommendation |
|---|---|---|
| Q1 | Wrappers packaging: Shape A (fold) / B (standalone `Reactor.Wrappers`) / C (bundled) / B+C hybrid? | B+C hybrid, gated on the §6.1 closure; if the closure slips, fall back to **Shape C or a reduced generator feature set** (not Shape A — §6.4). |
| Q2 | Is the §6.1 emitted-reference closure cleanly hoistable into Abstractions, or does it force runtime types into the "abstractions" package? | Neither `Reconciler` nor `ChangeEchoSuppressor` move. Introduce the §5.3 runtime seam and re-point the generator's emit at it; expose a public **read-side** echo-suppress check (the current `ChangeEchoSuppressor.ShouldSuppress` is internal + read-side — the v0.1 "emit `WriteSuppressed`" idea was wrong, that primitive is write-side). Phase 1 spike. |
| Q3 | Registration: explicit `UseLibrary` only, or also opt-in discovery? | Explicit default + opt-in `[ReactorLibrary]` marker + `UseDiscoveredLibraries()` escape hatch. |
| Q4 | Package granularity: one `Reactor.Abstractions`, or split further (e.g. `…Abstractions` vs `…Authoring`)? | Single `Reactor.Abstractions`; revisit only if the closure proves it too heavy. |
| Q5 | Feature-package boundaries: charting / data-grid / docking each their own package, or a single `…Extras`? | Per-feature packages (mirrors the 3P story more honestly). |
| Q6 | Versioning across split packages — lockstep with the core version, or independent? Ties to spec 022 (versioning) and 057 (release channels). | Lockstep for core+features; the standalone `Reactor.Wrappers` may float. |
| Q7 | `Reg`/`RegDecorator`/`RegBase` are `internal` today (048 §8 steers 3P authors to public `ControlRegistry.Register`). Keep them internal, or promote a public bulk primitive? | Keep internal; feature packages register via generated Pattern-A cctors. Revisit only if a public bulk API proves necessary. |
| Q8 | Is the "define an element without any Reactor runtime" goal worth the packaging cost given the WinUI coupling (§4) is unavoidable anyway? | Yes for the runtime decoupling + smaller WinUI slice; but this is the strategic call. |
| Q9 | **Runtime-seam design (§5.3).** What is the minimal interface Abstractions declares and core's `Reconciler` implements (child mount/reconcile, `ApplySetters`, tag get/set, detach, echo-suppress read, lifecycle-add, assembly-register)? Should `MountContext`/`UpdateContext` expose *it* instead of the concrete `Reconciler`? | Design a single `IReactorRuntime` (working name) seam; contexts expose the seam, not `Reconciler`. The load-bearing Phase-1 deliverable. |
| Q10 | **Element carve-out (§5.1).** `Element` has an implicit `string → Factories.TextBlock` conversion and `FuncElement`/`MemoElement` depend on `RenderContext`. Which Element surface is the pure primitive that moves, and which convenience stays in core? | Move the pure `Element` primitive + handler/descriptor contract; keep `Factories`, `RenderContext`, `FuncElement`/`MemoElement`, and the implicit-conversion sugar in core. |
| Q11 | **Deferred-controlled in Abstractions-only.** Generated deferred-controlled wrappers read the internal `ShouldSuppress`, so today they only compile under `InternalsVisibleTo`. Expose a public read-side primitive, route through `.Controlled`, or forbid the pattern in Abstractions-only libraries? | Expose a public read-side echo-suppress check on the seam (ties to Q2); `.Controlled` remains the higher-level path. |
| Q12 | **`string → Element` implicit operator (§5.1).** It must be declared on `Element` but its body needs a core built-in (`TextBlockElement`) → Abstractions↔core cycle if `Element` moves. Drop it (source-breaks `VStack("hi")`-style DSL), or relocate the string convenience to core container factories? | Drop the operator; add `string`-accepting convenience to the core container factories; document as a one-time DSL migration. Confirm blast radius before Phase 1. |
| Q13 | **Adapter materialization mechanism (§5.2).** Given `TControl` lives only in the `Register<E,C>` closure and `MakeGenericType` is forbidden, do we (1) move `IV1HandlerEntry`+adapters to Abstractions re-typed against the seam, or (2) keep adapters in core behind a generic `IReactorRuntime.CreateEntry<E,C>` factory invoked from the `Register` frame? | Option 2 — keeps the heavy reconciler-coupled adapter logic in core; only a type-erased `Func<object>` + one generic interface method cross the boundary. |
| Q14 | **Element/record ABI evolution policy (§14).** What is the committed compatibility contract for the base `Element` record and the descriptor authoring shapes once they are the ABI — additive-only members, no positional-param changes, DIM-only seam growth? Do we gate it with a public-API baseline test (`reactor.api.txt`-style) scoped to the Abstractions assembly? | Yes: freeze the Abstractions surface behind an API-baseline gate; additive-only; new seam behavior via default-interface-methods. |
| Q15 | **WinUI-version compatibility bound (§14).** Since `Reactor.Abstractions` references the Windows App SDK, the R7 roll-forward guarantee is bounded by WinUI's own ABI across the versions in play. Do we pin a documented minimum Windows App SDK per Abstractions version, and test the interop harness across the supported WinUI range? | Document a per-Abstractions-version minimum WinUI; the interop-proof harness pins one WinUI; a broader matrix is CI follow-up. |

## §13 Phasing

- **Phase 1 — Abstractions carve-out + runtime seam (the load-bearing phase).**
  This is a *decoupling design task, not a file move.* Create
  `Microsoft.UI.Reactor.Abstractions` and:
  - Design the §5.3 runtime seam (Q9) — the small, enumerated interface (see the
    §5.3 table) core's `Reconciler` implements — and make `MountContext`/
    `UpdateContext`/`UnmountContext` **and `ReactorBinding<TElement>`** hold the seam
    instead of the concrete `Reconciler`.
  - Re-point the wrapper generator's emit (§6.1) at that seam + public primitives;
    expose the public read-side echo-suppress check (Q2/Q11) so the deferred-controlled
    path compiles without `InternalsVisibleTo`.
  - Carve out the pure `Element` primitive + handler/descriptor/`PropEntry` contract
    (Q10), leaving `Factories`/`RenderContext`/`FuncElement`/`MemoElement` in core;
    resolve the `string → Element` implicit-operator cycle (Q12).
  - Split `ControlRegistry` so the public `Register` surface + a type-erased
    `Func<object>` store live in Abstractions while the reconciler-coupled adapter is
    produced *inside* the generic `Register<E,C>` frame via the core factory seam
    (§5.2 / Q13) — **not** rebuilt post-`TryResolve` (that would need the forbidden
    `MakeGenericType`).
  - Gate: `tests/external_proof/Reactor.External.TestControl` compiles + registers
    against Abstractions only (it has **no** `InternalsVisibleTo` grant), **including
    a deferred `[WrapControlled]` prop** (the exact case that fails to compile today,
    per §6.2); the generator's full emitted closure resolves against Abstractions; the
    AOT-proof harness (`tests/aot_trim_proof`) stays green.
- **Phase 2 — `UseLibrary` + diagnostics.** Add `IReactorLibrary` /
  `IReactorLibraryBuilder` / `UseLibrary`; reframe `RegisterAllBuiltIns` as the
  built-in library; add the `[ReactorLibrary]` marker + enriched runtime throw +
  the analyzer/`mur check` rule (§9).
- **Phase 3 — feature-package extraction.** Split charting / data-grid / docking
  into packages with `UseX()` extensions; add the `…All` metapackage; migrate
  samples/gallery (§11).
- **Phase 4 — opt-in self-registration.** `UseDiscoveredLibraries()` + the
  discovery pass over `[ReactorLibrary]` markers, documented as the trim opt-out.

Phases 1–2 are the core of issue #163; Phase 3 is the dogfooding proof; Phase 4 is
the convenience layer and can slip without blocking the rest.

## §14 Cross-version library interop — stable ABI

**Driver (R7).** The strongest reason to carve out `Reactor.Abstractions` is not
trimming — it is letting a Reactor control library be *consumed by another Reactor
library* without version-skew runtime failures. Today a control library
`ProjectReference`s the whole of core `Reactor.dll` (see
`tests/external_proof/Reactor.External.TestControl.csproj` line 55). So `LibA`
binds to core *N*'s types and `LibB` to core *M*'s; when an app pulls both, NuGet
unifies to **one** runtime, and whichever library did not match the surviving
version can fail to bind at load time.

**Scope: roll-forward unification, not side-by-side.** We target the
`Microsoft.Extensions.*.Abstractions` model: one runtime per process, chosen by
NuGet's highest-wins, and every library binary-compatible with it. True
side-by-side (two core runtimes in separate `AssemblyLoadContext`s) is **out of
scope** — `Element` records from one context are a different `Type` than the
other's, so a shared reconciler cannot process both. Roll-forward is the tractable,
common case and is what the split below enables.

**Why this is feasible.** .NET assembly binding in the default load context is by
type/member signature, not by exact strong-name version, and NuGet unifies to a
single assembly. So *old generated code binds to a newer runtime* as long as every
symbol it references is unchanged. The guarantee therefore reduces to a single
discipline: **everything a library's compiled output references must live in
`Reactor.Abstractions`, and that surface must evolve additive-only.**

**The frozen ABI surface.** Grounded against the generator emit
(`WrapperGenerator.cs`) and the reconciler's reads of a foreign element, the exact
closure a control library binds to is enumerable:

| ABI member (emitted/referenced) | Source today | Stability action |
|---|---|---|
| `Element` base + `Key`, `Modifiers` (`ElementModifiers`) | core `Element.cs` (reconciler reads `Reconciler.cs:597,609`) | move to Abstractions; freeze base members |
| `Optional<T>` | core (`WrapperGenerator.cs:727,1140,1628`) | move to Abstractions |
| `ControlDescriptor<E,C>` / `DescriptorHandler<E,C>` | core V1Protocol (`:1382,1383`) | move authoring shapes to Abstractions |
| child strategies `SingleContent`/`Panel`/`ItemsHost`/element-slots | core V1Protocol (`:1384–1386,1426–1438`) | move to Abstractions |
| `ControlRegistry.Register<E,C>` + stored entry (`IV1HandlerEntry`) | core (`:1717`) | public `Register` in Abstractions; adapter built in-frame (Q13) |
| runtime seam: `GetElementTag`/`SetElementTag`/`DetachReactorState`/`ApplySetters` | core `Reconciler` **static** (`:1483,1498`, §5.3) | expose as Abstractions seam; **stop referencing concrete `Reconciler`** |
| read-side echo check `ShouldSuppress` | core `ChangeEchoSuppressor` **internal** (`:1497`) | **public read-side primitive** (Q2/Q11) — hardest blocker |
| `MountContext`/`UpdateContext`/`UnmountContext` + `ReactorBinding<T>` | core V1Protocol (§5.1/§5.3) | move to Abstractions over the seam |
| `ElementExtensions` lifecycle add | core (`:1779`) | move/relocate to Abstractions |
| `ReactorApp.TryRegisterControlAssembly` | core **Hosting** namespace (`:1716`) | relocate to an Abstractions/core seam — a control lib must not bind Hosting |

**Two current violations** make cross-version binding impossible *today*, and both
are already tracked: (1) the deferred-controlled trampoline emits the **internal**
`ChangeEchoSuppressor.ShouldSuppress` (only compiles under `InternalsVisibleTo`;
Q11); (2) generated code references the **concrete `Reconciler`** and
Hosting's `ReactorApp`, so a library binds core directly rather than a stable
subset. Closing both is the load-bearing Phase-1 work.

**WinUI-coupling caveat.** `Reactor.Abstractions` is **not** pure-managed. The
authoring shapes are generic over the control type (`ControlDescriptor<E, C> where
C : FrameworkElement`), contexts hand out `FrameworkElement`, and the generator
emits `Microsoft.UI.Xaml.FrameworkElement`/`UIElement` (`WrapperGenerator.cs:1376,
1483`). So Abstractions references the Windows App SDK, and the R7 guarantee is
bounded by **WinUI's own ABI stability** across the versions in play: it is "one
Reactor runtime + one compatible Windows App SDK," not "any WinUI." This is the
same constraint apps already accept (a process has one Windows App SDK), so it is a
statement of the boundary, not a new limitation. (Contrast the existing
`Reactor.Wrappers.Abstractions`, which holds only the compile-time authoring
*attributes* and is genuinely WinUI-free — a different assembly from the runtime
`Reactor.Abstractions` proposed here.)

**Evolution discipline.**

- *Additive-only.* Never remove or change the signature of an ABI member the
  generator or a hand-authored handler could bind to. New seam behavior arrives as
  new members (default-interface-methods on the seam interface; new static
  overloads), never as edits to existing ones.
- *Records stay evolvable.* The reconciler dispatches by element `Type` → registered
  entry and reads only base `Element` members — it never reconstructs a derived
  record positionally across the boundary. So a library's own element record shape
  is private; only additive changes to the *base* `Element` are ABI-visible.
- *Independent versioning.* `Reactor.Abstractions` gets its own slow SemVer; the
  `Reactor.Wrappers` generator package version tracks the Abstractions surface it
  emits against; core runtime churns freely as long as it keeps implementing the
  Abstractions contract backward-compatibly. This is exactly the
  `Microsoft.Extensions.*.Abstractions` playbook.

**Regression gate — a real two-version interop harness.** Analogous to the existing
AOT-proof harness, R7 needs an executable proof: build
`Reactor.External.TestControl` against `Reactor.Abstractions` *N*, then load the
**compiled DLL** (not a project reference) into a host running core *M* and assert
mount / update / echo all succeed through the public surface only. A binary-drop
test (reference the built assembly, not source) is the closest in-repo proxy for
the "no recompilation" clause; a full two-NuGet-version matrix can follow in CI.

New open questions **Q14** (Element/record ABI evolution policy) and **Q15**
(WinUI-version compatibility bound) are added to §12.
