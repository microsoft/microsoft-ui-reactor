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
monolithic package**, and confirming apps need **no per-library registration
ceremony** — controls self-register when first used.

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
> default reference. App developers need **no registration ceremony** — a control
> self-registers the first time its element is built — and the rare genuine miss fails
> **loudly and helpfully**, never as a blank screen.

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
- [§8 Explicit registration — the narrow escape hatch](#8-explicit-registration--the-narrow-escape-hatch)
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

2. **Registration is per control, with no library-level convention.** Registration
   happens per control (a factory touch, or a Pattern-A static constructor — spec 048
   §6). Some authors expect a single MAUI-style `builder.UseSkiaSharp()` to "wire up the
   library." Reactor's lazy self-registration means there is usually **nothing** to wire
   up (§8) — but the expectation, and the two cases lazy genuinely can't cover (handler
   overrides, eager global init), deserve an explicit answer.

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

**R2 — Registration is automatic; explicit only where lazy can't reach.** The common
case needs *no* per-library call: a control self-registers when its element is first
constructed (§3), and families register the same lazy way through a base-type entry.
Explicit registration — via the public `ControlRegistry.Register*` seam (for reusable
libraries) or the per-host `Reconciler.RegisterType`/`RegisterHandler` (for app-local
controls), both from app startup (§8) — is reserved for the two cases construction cannot
trigger: a handler for an element the library does not construct (e.g. an override), and
eager global init (e.g. a XAML resource dictionary). Reactor adds **no** bulk
"register-the-whole-library" API — it would re-introduce the trim opt-out spec 048 removed
(R5).

**R3 — Loud, helpful failure.** A missing registration produces a clear diagnostic
that names the missing element type and how to register it — never a blank screen.

**R4 — Leaner core.** The heavy optional subsystems (charting, data grid, docking,
markdown) move out of the default core reference so every app — and every wrapper
author — carries a smaller Reactor.

**R5 — Do not regress the trim/AOT story.** Every proposal must preserve spec 048's
property: an app roots (and the trimmer keeps) only the controls it actually reaches.

**R6 — Do not regress authoring ergonomics.** The hand-authored `IElementHandler`
recipe (spec 047, `tests/external_proof`) and the `[GenerateReactorWrapper]`
generator (spec 058) keep working, ideally with a smaller footprint, never larger.

**R7 — Cross-version library interop (stable ABI).** *A 1.0 commitment the seam is
evolved toward — not a property preview can rely on today.* The **target**: a control
library built against Reactor version *N* loads and runs, **without recompilation**,
against any host runtime *M* in the **same SemVer major** (*M* ≥ *N*) — the ABI
compatibility band. Additive surface ships in **minor** releases (patch = compatible fixes
only), so a compiled wrapper can roll forward: NuGet resolves a **single** Reactor version
into the graph, the default load context binds that one assembly by **identity** first, and
the wrapper's member references then resolve against it so long as every bound type/member
signature is unchanged. An app that transitively pulls `LibA` (built against *N*) and `LibB`
(built against *M*, same major) unifies to that one runtime, and both libraries' controls
must mount, update, and echo correctly against it — the *M* ≥ *N* direction holding only
when the resolved host version satisfies **every** wrapper's declared lower bound (via
bounded dependency ranges — §6.2), since NuGet's nearest-wins resolution or a direct/override
dependency can otherwise pick *M* < *N*. A deliberate break (§6.3) is confined to a
**major** bump.

**Status — intended, not yet enforced.** The current architecture *can permit* roll-forward
today (the bound surface is public and the runtime loads in the default context), but that
is **unvalidated and not guaranteed**: nothing proves the bound signatures stay preserved or
that mount/update/echo behavior holds across releases. The API-compat gate, the interop
harness, and the loader/version policy (§6.2) do not exist yet, and the loader policy is
deliberately deferred (Q3). R7 therefore
becomes a **durable guarantee at 1.0, once that machinery lands** — 0.x previews carry no
ABI promise. Until then it is the requirement the seam is designed and evolved toward (§6),
not a contract a preview consumer should build on.

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
  `tests/external_proof/Reactor.External.TestControl` ships a hand-coded
  `IElementHandler` control **and** a source-generated wrapper in a *separate assembly*
  with **no** `InternalsVisibleTo`; it binds only the public V1 surface
  (`MountContext` / `UpdateContext`, `ReactorBinding`, `ControlDescriptor`, the registry),
  so the fact that it *compiles* is the empirical gate that the surface is
  public-sufficient. It is a representative control, not an exhaustive exercise of every
  frozen member — §6.1 enumerates the full public list.

- **Generated authoring already works (058).** `[GenerateReactorWrapper]` on a
  partial record emits the init-props, child/items slots, `On{Event}` callbacks, the
  `ControlDescriptor`, Pattern-A registration, and a factory. The generator *itself* is a
  lean `netstandard2.0` Roslyn component that emits *strings* and binds attributes by
  metadata name, so the *generator assembly* references neither `Reactor.dll` nor its own
  attributes assembly. That decoupling stops at the generator, though: the strings it
  emits are compiled **in the consumer** against Reactor's public runtime surface
  (`ReactorBinding.ShouldSuppressEcho`, `Reconciler.GetElementTag`, `ControlDescriptor`,
  …), so the *generated code* is tightly bound to that surface — which is exactly the ABI
  §6 must freeze.

- **The missing-handler failure already throws helpfully (048).**
  `Reconciler.ThrowNoHandlerRegistered` (`src/Reactor/Core/Reconciler.Mount.cs`)
  raises an `InvalidOperationException` naming the element type and listing concrete
  fixes.

- **The last internal symbol the generated wrapper needed is now public (this
  change).** `ReactorBinding.ShouldSuppressEcho(UIElement)` is now a **public** read-side
  echo primitive, and the wrapper generator's deferred-controlled trampoline emits it
  instead of the internal `ChangeEchoSuppressor.ShouldSuppress`. With that, every symbol
  the generator bakes into external generated code is public — and every member the §6.1
  seam enumerates is public (verified type-by-type), with `external_proof` compiling
  IVT-free as the empirical gate. That is the precondition for Track A (§6). It is *not* a
  claim that no Reactor internal could ever help a future hand-author — promoting a
  not-yet-enumerated member is an additive §6.3 change, not a regression.

What is **not** yet present, and is the subject of this spec: a *governed* stable
authoring ABI (§6), a leaner core with the heavy subsystems in `Reactor.Advanced`
(§7), and — where lazy self-registration cannot reach — a narrow explicit-registration
escape hatch (§8) plus reframed diagnostics (§10).

## §4 Two hard constraints that shape the design

### §4.1 A wrapper inherently names WinUI

The core authoring types are structurally coupled to WinUI: `Element` and its
subclasses reference `Microsoft.UI.Xaml`/`…Controls`/`…Media`, and
`IElementHandler<TElement,TControl>` / `ControlDescriptor<TElement,TControl>`
constrain `TControl` to a live WinUI control type (`IElementHandler` to `UIElement`,
`ControlDescriptor` to `FrameworkElement, new()`) — a handler's whole purpose is to
mount and patch a real WinUI control. So "define a custom element type" *inherently* names
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
  proxy several reconciler operations (`MountChild`, `ReconcileChild`,
  `RentControl`, `PushContext`/stagger scopes, `ApplySetters`, …) on the per-mount,
  allocation-free hot path (up to ~10 on `MountContext`, fewer on `UpdateContext` /
  `UnmountContext`).
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
  — *once built* — by a real API-compat gate and a binary-compat interop harness. Delivers
  R1 / R7 (which become a durable guarantee only when that machinery lands — §6.2 / R7).
- **Track B — consolidate heavy subsystems into `Reactor.Advanced` (§7).** Move
  charting, docking, markdown, and the data grid out of core into the existing
  advanced assembly, largely keeping their namespaces (§7). Delivers R4.

Registration needs no track of its own: lazy self-registration already satisfies R2
(§8), leaving only a narrow explicit escape hatch and the reframed R3 diagnostic (§10)
as small, independent clean-ups that can interleave with either track.

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
  `.Markdown`). Each assembly carries fixed load/metadata overhead; several small
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
is unchanged*. Making that a *durable* guarantee (rather than the by-construction
behavior it is today — R7 status) reduces to one discipline plus the governance to
enforce it (§6.2): **enumerate the surface a wrapper binds, and evolve it additive-only.**

### §6.1 The seam to freeze

Grounded against the generator's emit and the reconciler's reads of a foreign
element, the bound surface is enumerable and, as of this change, entirely public:

| Frozen ABI surface | Where |
|---|---|
| `Element` base + `Key` / `Modifiers` (`ElementModifiers` records) | `Core/Element.cs` (reconciler reads only base members) |
| `Optional<T>` | `Core` (emitted by generated init-props) |
| `ControlDescriptor<E,C>` + its builder methods, including the hand-coding arms (`Imperative`, `HandCodedControlled`, `HandCodedEvent`) and the `ChildrenStrategy` taxonomy (`SingleContent` / `Panel` / `ItemsHost` / element-slots) | `Core/V1Protocol` |
| `IElementHandler<E,C>` / `IDecoratorElementHandler<E>` / `DescriptorHandler<E,C>` | `Core/V1Protocol` |
| `MountContext` / `UpdateContext` / `UnmountContext` + `ReactorBinding<TElement>` | `Core/V1Protocol` |
| `ControlRegistry.Register*` | `Core/V1Protocol/ControlRegistry.cs` |
| `ReactorBinding.ShouldSuppressEcho` / `WriteSuppressed` | `Core` |
| `Reconciler.SetElementTag` / `GetElementTag` / `DetachReactorState` / `ApplySetters` (tag get/set) | `Core/Reconciler.*` |
| `ElementExtensions.OnMountAdd` / `OnUnmountAdd` | `Elements` |
| `ReactorApp.TryRegisterControlAssembly` | `Hosting` |

The freeze is **transitive**: every type that appears in a frozen member's signature — its
generic arguments and constraints, base/interface contracts, and the `Element`-boundary
types a wrapper hands back or receives (`Element`, `Optional<T>`, the `ChildrenStrategy`
records) — is part of the ABI closure and is preserved along with the member. A frozen
signature is only as stable as the transitive types it names.

**Deliberately *outside* the frozen seam:** the per-host explicit-registration API
(`Reconciler.RegisterType` / `RegisterHandler`, §8) is public and useful for app-local
bespoke controls, but it is **not** part of the R7-frozen surface. It is per-host and aimed
at apps (which recompile against their target runtime) and framework-internal controls
(`ResizeGrip`, docking); a *compiled* control library that must roll forward registers
through the global `ControlRegistry.Register*` seam above, or via the generator (which
does). Keeping per-host registration unfrozen holds the seam to the minimum a rolled-forward
binary actually binds.

**Two compatibility tiers — *prove* vs *promise*.** Under the R7 policy (a 1.0 commitment,
not a preview guarantee — §2), every supported public API — the control-authoring seam
**and** the app-facing DSL (~200 factory methods, ~600 fluent modifiers, and the hooks) — is
treated as a **within-major ABI**: within a SemVer major it does not break, so a compiled
binary that binds any of it rolls forward across a same-major runtime. The tiers differ in
*how* that is assured, not in *whether* it holds:

- **Tier 1 — promised (the DSL and the rest of the public surface).** Kept within-major
  stable by additive-only **discipline** + review + release notes. SemVer is a release
  policy, not a CLR verifier, so the promise is only as real as the discipline behind it
  (§6.3): add overloads; never mutate an existing signature — no changed or added optional
  parameters or defaults (an added optional param looks source-compatible but is a **binary**
  break), no `ref`/`in`/`out` or return-type edits, no reshaped record primary constructors,
  no new abstract interface/base members. Pinning the DSL as a *gated* surface is
  deliberately declined: it is large and evolving, and add-overload discipline already keeps
  it within-major-safe without freezing ~800 members to a checked-in txt file.
- **Tier 2 — proven (the §6.1 seam).** On top of the same within-major promise, the ~35
  seam members are additionally *mechanically* guaranteed (the exact gated boundary — the
  whole §6.1 table vs the subset generated code + `external_proof` actually bind — is settled
  in Q2) — the API-compat gate fails the
  build on any change, and the interop harness proves an old wrapper binary still
  mounts/updates/echo-suppresses on the HEAD runtime — and held to a stricter
  last-resort-only posture even across majors. Tier 2 is stronger **evidence**; it does
  **not** relax Tier 1's obligations. It proves the subset the whole third-party-control
  ecosystem binds.

The only honest residual is at a **major** bump: a deliberate break (§6.3) may require
DSL-binding binaries (a component/library binary per §8, or an app) to recompile — that is
what a major signals — while the seam is held even harder. Seam-first gating is a
*prioritization*, not an exclusion: the API-compat gate can grow to cover more of Tier 1
over time.

### §6.2 Governance — three pieces that do not exist yet

Three complementary mechanisms, each a *different kind* of check: a build-time gate (1),
a runtime test (2), and a load-time policy (3).

1. **A real API-compat gate — a *build-time* check, not a test.** It runs during
   compilation and fails the build before any test executes. Today
   `tests/Reactor.Tests/Tooling/ApiIndexGeneratorTests.Index_IsUpToDate` *is* a test, and a
   toothless one: it only byte-compares the generated `reactor.api.txt` for *staleness*, so
   regenerating the file makes it green — it *blesses* breaking changes rather than blocking
   them. Replace it with an additive-only gate scoped to the §6.1 seam:
   - **`Microsoft.CodeAnalysis.PublicApiAnalyzers`** — a Roslyn analyzer over checked-in
     `PublicAPI.Shipped.txt` (the frozen surface) + `PublicAPI.Unshipped.txt` (pending
     adds). Removing or changing a shipped member raises `RS0017` at compile; adding public
     API not listed raises `RS0016`, forcing a reviewable txt-file diff. Additive by
     construction.
   - **`Microsoft.DotNet.ApiCompat`** — diffs the just-built `Reactor.dll` against the
     last-shipped baseline and fails on any binary-incompatible delta (the mechanism the
     .NET runtime uses for its own ref assemblies).

   Either way, removing or changing a frozen member is a red build, not a snapshot to bless.

2. **A binary-compat interop harness — a *runtime* test the gate can't replace.**
   Additive signatures are necessary but not sufficient: a wrapper also depends on
   *behavior* (echo semantics, child reconcile, registration timing, descriptor ordering),
   which no API diff can see. This is an integration test with one non-negotiable shape — it
   loads a **pre-compiled DLL** (a binary drop, **not** a project reference; a project
   reference recompiles against HEAD and hides the very break we are hunting). In the same
   spirit as the existing AOT-proof harness (which inspects a shipped artifact rather than
   recompiling from source), build `Reactor.External.TestControl` against an
   **older** Reactor, then load that unchanged DLL into a host running the HEAD runtime and
   assert mount / update / echo-suppress all succeed through the public surface only. A
   two-NuGet-version matrix can follow in CI.

3. **A loader / version policy.** Roll-forward works today partly by luck:
   `Reactor.dll` is **not strong-named** (no `SignAssembly`/key in `Reactor.csproj`)
   and its assembly version tracks the package version, so default-`AssemblyLoadContext`
   unification lets an old wrapper bind a newer runtime. Make this a *decision*: pin an
   explicit assembly-version / strong-name policy and document that the guarantee holds
   in the default load context, where NuGet resolves a **single** Reactor version into the
   graph. The same-major band is enforced at *restore*, not left to luck at load: a
   generated library package declares a **bounded** Reactor dependency range — lower bound
   its build version, upper bound the next major (e.g. `[1.2.0, 2.0.0)`) — so a graph that
   mixes majors fails or warns during restore (NU1107 conflict / NU1608 out-of-range /
   NU1605 downgrade) instead of silently binding a
   mismatched runtime and throwing `MissingMethodException` at mount time. **Side-by-side**
   (two runtimes in separate `AssemblyLoadContext`s) is **out of scope** — `Element`
   records from different contexts are different `Type`s a shared reconciler cannot
   process; custom/plugin ALCs break the guarantee by design.

### §6.3 Evolution discipline

- **Additive by default; break only when nothing simpler works.** This governs the whole
  supported public API within a major (Tier 1), and the §6.1 seam most strictly (Tier 2):
  new behavior arrives as *new* members (new overloads; default-interface-methods on the
  handler/descriptor interfaces), never as edits to existing ones — an existing signature is
  never removed or changed when an additive change would achieve the same result.
  Reactor still reserves the right to break the seam, but only as a genuine last resort
  when no compatible change exists; every such break is a deliberate, **major-versioned**,
  release-noted exception — not routine churn (and, in preview, expected to grow rarer
  as the API settles).
- **Records stay evolvable.** The reconciler dispatches by element `Type` → registered
  entry and reads only base `Element` members; it never reconstructs a derived record
  positionally across the boundary. A library's own element record shape is therefore
  private — only additive changes to the *base* `Element` are ABI-visible.
- **One versioning unit — internals churn, public signatures do not.** There is no separate
  contract assembly to keep in lockstep, and the runtime's *internal* implementation and
  *behavioral* details may churn freely between releases — but that freedom stops at the
  **public signature**. Within a major, public signatures do not change (Tier 1 by
  discipline, Tier 2 by the gate); "churns freely" never licenses breaking a public API
  within a major.

## §7 Track B — consolidate heavy subsystems into `Reactor.Advanced`

Today only Win2D is extracted. `Reactor.Advanced` is its **own** NuGet package
(`PackageId` `Microsoft.UI.Reactor.Advanced`; `<Description>… first inhabitant: Win2D
canvas.</Description>`; `charts` already in its `PackageTags`) that **depends on** core —
the in-repo one-way `ProjectReference` to `Reactor.csproj` becomes a package dependency at
pack, so the direction is **Advanced → core**, never the reverse. An app opts in with a
single `PackageReference` to `Microsoft.UI.Reactor.Advanced`; NuGet then transitively
resolves core. Core still carries the heavy leaves — charting/D3, docking, markdown, and the
data grid — which dominate the ~3.5 MB `Reactor.dll`.

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
- **Namespaces stay put — the assemblies move, the namespaces do not.** As above, the
  relocated types keep their existing names
  (`Microsoft.UI.Reactor.Charting` / `.Docking` / `.Markdown`; the data grid's
  `Microsoft.UI.Reactor.Controls` records) even though they now ship in `Reactor.Advanced`.
  Rebranding them to `Microsoft.UI.Reactor.Advanced.*` would break every consumer's `using`
  directives and type references for **no** compatibility benefit — the avoidable break §6.3
  tells us not to make. (Win2D is `…Advanced.Win2D` only because it was *greenfield* in
  Advanced and never had a core namespace to preserve.) One honest caveat: for an
  already-*compiled* consumer the assembly move is a binary break regardless of namespace —
  its metadata still names `…, Reactor` for a type now in `…, Reactor.Advanced` — and the
  usual `[TypeForwardedTo]` softener is **unavailable**, because core is Advanced's one-way
  dependency *target* and cannot reference it back without a cycle. Compiled consumers
  therefore recompile (picking up the new assembly with zero code edits beyond the two
  `using static` lines above), consistent with this being a deliberate, major-versioned move.
- **Sequence:** charting + docking first (already decoupled, boundary-guarded, and
  source-clean), then markdown, then the data grid (the last two each carry the factory
  note above).

**Who benefits — and who does not.** Because Advanced sits *on top of* core, moving the
heavy leaves shrinks **core**, so the win is asymmetric:

- **Core-only consumers win.** The common app that never touches
  charting/docking/markdown/data-grid — and, per #163, the 3P wrapper author who references
  only core to define a custom element — gets a ~1.6 MB-smaller download and a **narrower**
  core surface in IntelliSense, the API index, and the frozen §6.1 ABI. That last point is a
  direct Track A synergy: fewer controls in core is a smaller surface to keep stable.
- **Advanced consumers see no reduction.** They opted into the heavy stuff and get core +
  the leaves either way; the data grid's AOT/reflection caveats simply now sit behind an
  explicit opt-in package instead of in everyone's core.

**What this is _not_ — the spec should not oversell it.**

- **AOT/trimmed apps already get it.** #627's trimming inversion drops unreferenced
  charting/docking to ~0 in a NativeAOT / `PublishTrimmed` publish, so this is really a
  **default-JIT** ship-size win, where the whole `Reactor.dll` ships regardless.
- **The boundary is deliberately coarse.** Advanced is one grab-bag ("no dozen DLLs"), so an
  app that wants only the data grid still pulls charting + docking + markdown. Fine-grained
  "pay only for what you use" is what AOT trimming already delivers; the split does not try
  to reproduce it. Not doing Track B at all — and relying solely on #627 — stays a
  legitimate choice for AOT-first shops.

Each move is gated on: no new `internal` cross-boundary reach that `InternalsVisibleTo`
cannot cleanly express, `CoreControlFamilyBoundaryTests` and the AOT-proof harness stay
green, and the subsystem's selftests/E2E still pass.

## §8 Explicit registration — the narrow escape hatch

**The common case needs no registration call.** A control self-registers the first time
its element is constructed: a generated wrapper's Pattern-A cctor sits on the element
record itself; the built-in catalog self-registers from its factories (a `Reg<>` touch, or
a generated element cctor for descriptor-backed built-ins), and a base-derived family
through `RegBase<>` or an **explicit** base-type static constructor — explicit (not a
`static`-field initializer) so the base type is not `beforefieldinit` and constructing a
derived record is guaranteed to run it (§3). Because a
Reactor app builds its element tree *before* it reconciles, construction always precedes
dispatch — so there is nothing to "wire up" for the controls a library owns, and no
MAUI-style `UseMyLibrary()` step.

An earlier draft proposed exactly such a bulk `UseLibrary<T>()` / `IReactorLibrary`
convention (R2). It is **dropped**: rooting a library's whole `Register` body is the same
trim opt-out spec 048 deliberately removed (R5), and it merely duplicates the registration
that constructing the element already performs.

**Two cases genuinely need an explicit call**, because no element construction triggers
them:

1. **A handler for an element the library does not construct** — e.g. a library that
   registers an *override* handler for the built-in `ButtonElement`. Constructing a
   `ButtonElement` runs the *owner's* cctor, never the override's, so the override must
   register at startup, before that element is first used (registration is first-wins).
2. **Eager global initialization** — e.g. merging a XAML `ResourceDictionary` into
   `Application.Current.Resources`. This is ordinary WinUI app-startup work, not Reactor
   control registration, and is often too late if deferred to a render pass.

**Two explicit-registration paths already exist — no new abstraction.** Both run from the
app's existing `ReactorApp.Run(...)` startup delegate; pick by scope:

- **Global, ABI-stable — `ControlRegistry.Register` / `RegisterDecorator` /
  `RegisterForDerivedTypes`.** Process-wide, part of the frozen §6.1 seam, so a *reusable or
  shipped* control library that registers this way rolls forward under R7. The handler
  factory should be `static` (the `StaticRegisterLambdaAnalyzer` warns otherwise) so it
  captures nothing and is cached once; per-instance state lives in the `IElementHandler`
  class. This is the path for library-level overrides and eager global init; a library that
  needs eager setup ships its own ordinary `public static void UseAcme()` over it.
- **Per-host, ergonomic — `host.Reconciler.RegisterType(...)` / `RegisterHandler(...)`.**
  These register on one host's reconciler: `RegisterType` takes **inline** mount/update/unmount
  delegates (no `IElementHandler` class needed) and `RegisterHandler` takes an `IElementHandler`
  instance; either may **capture** host-local state, and different hosts may register
  different implementations for the same element type. This is the right tool for a
  *bespoke, app-local* custom control — the shape Reactor's own `ResizeGrip` / docking
  natives and the Monaco / regedit samples use. It is deliberately **outside** the §6.1
  frozen seam: apps recompile against their target runtime, so they need no roll-forward
  promise — a control that must roll forward as a *compiled* dependency uses the global seam
  (or the generator) instead.

Reactor deliberately does **not** add a per-library `IReactorLibrary` interface on top of
either path: that would add frozen ABI surface Track A works to *minimize*, in exchange for
sugar over a one-line call an author can already write.

## §9 Self-registration vs. explicit registration

Spec 048 already analyzed and rejected the *unconditional-eager* forms for the
built-in catalog; that analysis carries over and is why lazy self-registration — not a
discovery scan — is the default.

- **`[ModuleInitializer]` / assembly-load registration is a trimmer root.** It runs on
  first type-load and names every handler + control, so the trimmer can never prove it
  dead and keeps the whole library. This defeats R5. (Spec 048 §4 rejected exactly
  this shape.)
- **Reflection-based assembly scanning** is AOT-hostile, startup-costly, and roots
  types the trimmer would otherwise drop.

**Recommendation:** lazy self-registration is the norm — a control's code is rooted only
when its element is constructed (§3). Explicit registration is the narrow §8 escape hatch,
along one of two paths: the global `ControlRegistry.Register*` seam (library-level overrides
/ eager init, R7-stable) or the per-host `Reconciler.RegisterType` / `RegisterHandler`
(app-local bespoke controls, not frozen). Reactor ships **no** marker-driven discovery pass
(`[assembly: ReactorLibrary]` / `UseDiscoveredLibraries()`): it would be either a trimmer
root or a `[RequiresUnreferencedCode]` opt-out, and lazy registration already delivers
zero-ceremony wiring without it.

## §10 Diagnostics

Keep 048's runtime throw on an unregistered mount (R3), but — with lazy self-registration
the norm — reframe *what it blames*. A "mounted but unregistered" element is now almost
unreachable for generated wrappers (constructing the element self-registers it); it survives
only for a factory-carried control constructed by raw `new` that bypassed the factory
trigger, or a third-party override that was never registered.

- **Reword the runtime throw** to point at those causes rather than at a nonexistent
  registration call:
  *"No handler registered for `FooElement`. Create it through its factory (built-in /
  Pattern-B controls self-register on the factory call), ensure its generated wrapper is
  referenced so its self-registration runs, register a third-party override at startup via
  `ControlRegistry.Register…`, or — for a bespoke app-local control — register it on the
  host's reconciler via `Reconciler.RegisterType`/`RegisterHandler` before first mount
  (§8)."* Reflection-light and AOT-safe: it names only `element.GetType()`.
- **Add a direct-record construction guardrail.** Warn when a *factory-registered* element
  record (a built-in / Pattern-B-style control, where the registration link lives on the
  factory) is constructed directly (`new FooElement(...)`) rather than via its factory — the
  most common cause of the throw above. It must **not** fire on generated wrappers (058),
  whose element-rooted Pattern-A cctor makes direct construction self-registering and safe.

## §11 Trimming and AOT

Both tracks preserve spec 048's reachability model (R5):

- **Track A is trim-neutral.** Stabilizing the seam in place adds no new roots — the
  seam is already public and reached through the existing registration links
  (a factory or generated element cctor → `Register` → handler → control). An API-compat gate is a build-time
  analyzer, not a runtime root.
- **Track B is trim-neutral.** #627 already inverted the core→feature dependencies so
  an app trims the subsystems it does not use; moving those subsystems into
  `Reactor.Advanced` changes the *assembly* they live in, not what roots them. The
  regression gates are `CoreControlFamilyBoundaryTests` (the IL boundary scan) and the
  AOT-proof harness (`tests/aot_trim_proof`), which must stay green through each move.
- **Explicit registration stays lazy-friendly.** The §8 escape hatch — global
  `ControlRegistry.Register*` for overrides / eager init, or per-host `Reconciler.RegisterType`
  for app-local controls — registers only what the app names; there is no bulk per-library
  root and no discovery scan (§9), so an app still pays only for the controls it constructs.

## §12 Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | API-compat gate tool: `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.*.txt`) or `Microsoft.DotNet.ApiCompat` against the last shipped DLL? | PublicApiAnalyzers for the authoring seam (in-repo, incremental, IDE-visible); ApiCompat as a CI backstop. |
| Q2 | Exact freeze boundary — is the *entire* §6.1 surface committed, or a named subset (e.g. is the full `MountContext` surface frozen, or only the members generated code + hand-authors actually bind)? | Freeze the members generated code and `external_proof` bind; mark the rest "public, Tier 1 — within-major by discipline, not in the Tier-2 gate." Settle with the interop harness. |
| Q3 | Loader policy — strong-name + pin `AssemblyVersion` now, or stay unsigned/default-ALC and only *document* the roll-forward requirement? | Document now; revisit strong-naming when the API stabilizes past preview. |
| Q4 | The data-grid **and markdown** `using static …Advanced.Factories` source break (§7) — acceptable in preview, or ship a type-forward / compat shim? | Accept in preview; call it out in release notes. A core-side compat shim isn't viable (core can't reference Advanced), so a `using static` is the honest fix. |
| Q5 | Should the wrapper attributes + generator ship as a standalone `Reactor.Wrappers` package, or stay bundled in core (status quo)? | Independent of both tracks; keep bundled unless a concrete author asks — revisit later. |
| Q6 | Ship a `Microsoft.UI.Reactor.All` metapackage (core + Advanced) for a batteries-included reference? | Optional convenience; add if consumer feedback wants it. |

## §13 Phasing

- **Increment 1 — done (this change).** Public `ReactorBinding.ShouldSuppressEcho`;
  generator repointed off the last internal symbol. The wrapper-bound surface is now
  fully public (§3).
- **Track A — stabilize the ABI in place.** A1: enumerate and lock the §6.1 seam behind the
  API-compat gate (Q1/Q2). A2: build the binary-compat interop harness. A3: settle and
  document the loader/version policy (Q3).
- **Track B — consolidate into Advanced.** B1: charting + docking → `Reactor.Advanced`.
  B2: markdown (with the §7 factory note, Q4). B3: data grid (§7 factory note, Q4).
- **Registration clean-ups — independent, small.** E1: reword the R3 runtime throw and
  add the direct-record guardrail (§10). E2: document the §8 explicit-registration escape
  hatch (overrides / eager init) against the existing `ControlRegistry.Register*`. No
  `IReactorLibrary`, marker, or discovery pass ships.

Tracks A and B share no dependency and can land in either order; the registration
clean-ups can interleave with either.
