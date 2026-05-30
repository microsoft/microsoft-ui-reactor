# Lazy, Trimmable Control Registration — Design Proposal

## Status

**Proposed — design converged, not yet implemented.** This spec is the follow-on
to [spec 047](047-extensible-control-model.md), which delivered the V1 handler
protocol (`IElementHandler<TElement, TControl>`, descriptors, the
`Reconciler.RegisterHandler` author surface) and proved an external assembly can
author + register a control against the public surface only — see
`tests/external_proof/Reactor.External.TestControl/`. 047 made external authoring
*possible*. This spec settles how registration should *happen* so that it is
automatic, lazy, and — the load-bearing requirement — **does not defeat AOT
trimming**.

The design below was reached through a multi-step discussion; §3 and §4 preserve
the dead-ends and *why* each was rejected, because the rejections are what pin the
final shape.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Requirements](#2-requirements)
- [§3 The core tension — pick two of three](#3-the-core-tension--pick-two-of-three)
- [§4 Rejected approaches](#4-rejected-approaches)
- [§5 Resolution — the factory is the registration link](#5-resolution--the-factory-is-the-registration-link)
- [§6 Pattern A — the 3P / hand-authored control](#6-pattern-a--the-3p--hand-authored-control)
- [§7 Pattern B — the scale pattern for the built-in catalog](#7-pattern-b--the-scale-pattern-for-the-built-in-catalog)
- [§8 The `ControlRegistry` contract](#8-the-controlregistry-contract)
- [§9 Cost model — the hot path](#9-cost-model--the-hot-path)
- [§10 Built-in migration — dismantling the central registrar](#10-built-in-migration--dismantling-the-central-registrar)
- [§11 Trimming model and caveats](#11-trimming-model-and-caveats)
- [§12 Open questions](#12-open-questions)
- [§13 Phasing](#13-phasing)

---

## §1 Motivation

Two goals, one of which dominates.

**Goal 1 — external (third-party) controls should "just work."** A 3P author who
wraps a WinUI/WinRT control (the spec 047 `RatingControl` / `MarqueeControl`
walkthroughs) should be able to ship an element + handler such that a consuming app
uses the control with no registration ceremony — and certainly without the
"register on every host, before first render" footgun that the current
per-`Reconciler` `RegisterHandler` model carries (documented as a Common Mistake in
`docs/guide/extending-reactor-controls.md`).

**Goal 2 (the north star) — AOT trimming must be able to remove unused elements and
their WinUI control types, to shrink the app binary.** This is the requirement that
disqualifies the otherwise-obvious automatic-registration designs, and it forces a
change to how **built-in** controls register, not just external ones.

That second point is the crux and deserves stating plainly: **today every Reactor
app roots the entire built-in control surface.** `RegisterV1BuiltInHandlers()` in
the `Reconciler` constructor (`src/Reactor/Core/Reconciler.cs:335`) news up ~50
handlers and names ~50 WinUI control types, from a constructor every app reaches.
The trimmer therefore marks all of them reachable and keeps every one — an app that
uses only `Button` and `TextBlock` still carries `TabView`, `TreeView`, `GridView`,
every overlay, and their WinUI backing types. No built-in control is removable as
the architecture stands. Goal 2 is impossible without changing this.

---

## §2 Requirements

In priority order:

1. **Elements stay pure data objects.** An `Element` record must not reference its
   handler or its WinUI control. The coupling direction is strictly handler →
   element (the handler already names the element via `IElementHandler<E, C>`).
2. **Automatic / unforgettable.** No app-developer registration step. Using a
   control must be sufficient to register it; there is nothing to forget.
3. **Lazy.** Referencing or even constructing an element must not force its handler
   or WinUI control type to load. Heavy types load on first *use*.
4. **AOT-trimmable.** An element never used in an app — and its WinUI control type —
   must be removable by the trimmer. This is the binary-size win.

---

## §3 The core tension — pick two of three

Requirements 1, 2, and 4 cannot all hold at once. The reason is a fact about how
the trimmer works: **the ILLink / NativeAOT trimmer keeps a type only if there is a
*static* reference path to it from a root.** It follows the static call graph; it
cannot follow a runtime dictionary lookup.

To remove an unused control, the trimmer needs a static path of the form *"the app
uses this element ⇒ therefore this handler + WinUI control are needed."* But:

- A **pure-data element** (req 1) does not reference its handler, so that path does
  not exist statically — it exists only at runtime, as a `Dictionary<Type, …>`
  lookup the trimmer cannot see.
- **Automatic** registration (req 2), done eagerly, means *something* references all
  handlers from a root (a registrar, a module initializer, a ctor). That roots
  everything ⇒ nothing trims.

So:

| Keep | Give up | Mechanism |
|---|---|---|
| pure data + automatic | trimmable | runtime registry, eagerly populated (roots all) |
| pure data + trimmable | automatic | link made explicitly at each use site |
| automatic + trimmable | pure data | element statically references its handler |

This is the same reason dependency-injection containers and reflection-based
registries are trim-hostile. The resolution (§5) does not break the tension — it
**relocates the static link** to a place that is neither the element nor a global
root: the factory function the app already calls to use the control.

---

## §4 Rejected approaches

Each was rejected against a specific requirement.

**Explicit per-host `RegisterHandler(reconciler)`** (the 047 status quo). Fails
req 2: the app must register on every `ReactorHost`, before first render; a miss is
a runtime `Mount` throw (exact-type dispatch has no fallback, by 047 §13 Q17).
Kept, but demoted to an override / test escape hatch (§8).

**Element implements a self-describing interface** (`element.CreateHandler()`).
Fails req 1 — the element carries behavior and references its handler.

**Element static constructor self-registers.** Fails req 1 (same coupling) and adds
a precise-cctor check to the element-allocation path (the M1 hot path 047 is
already nursing).

**`[ModuleInitializer]` self-registration.** This is the seductive one, and it fails
req 4 decisively. At *runtime* a module initializer that registers factory
*delegates* stays lazy (the delegates defer handler construction). But a
`[ModuleInitializer]` is a trimmer **root** that can never be removed, and its body
references every handler and WinUI control it registers — even behind a
`static () => new FooHandler()` lambda, the lambda's IL names `FooHandler` →
`FooControl`. So the trimmer keeps all of them. Module init does not merely fail to
help trimming; it actively prevents it. It also has the assembly-granularity
problem (loading one type in an assembly runs the initializer, which touches every
registered type's metadata).

**Reflection / naming-convention discovery** (`Type.GetType("…Handler")`, assembly
attribute scans). Most automatic of all, but fails the project's AOT-clean
constraint (spec 047 §4.7) — reflection-resolved handlers are exactly what the
trimmer strips and what NativeAOT forbids.

---

## §5 Resolution — the factory is the registration link

Reactor controls are already used through **factory functions**: `Button(...)`,
`TextBlock(...)`, and for externals an author-provided `Marquee(...)`. Factories are
the single chokepoint through which an element is constructed. Make the factory the
carrier of the static element→handler link *and* the lazy, idempotent registration
trigger:

- The **element** stays a pure-data `record` referencing nothing (req 1).
- The **factory** references the handler + control and registers it on first call
  (req 2 — using the control *is* registering it; req 3 — registration/loading
  happens on first use).
- Because the only static reference to the handler/control flows *through the
  factory*, the trimmer keeps them **iff the factory is reachable** (req 4). An app
  that never calls `Marquee(...)` lets the factory, handler, and WinUI control all
  be trimmed.

The global registry still exists (handlers must be found by `element.GetType()` at
dispatch), but it is **populated lazily by factory calls**, never by an eager root —
so the registry itself roots nothing (§8).

Two ergonomic expressions of this one mechanism follow: Pattern A for hand-authored
3P controls, Pattern B for the ~50-control built-in catalog. They compile to the
same registry contract and the same hot-path cost.

---

## §6 Pattern A — the 3P / hand-authored control

For an author shipping one or a few controls, co-locate registration in the factory
holder's static constructor:

```csharp
// Pure data — references nothing about its handler or its WinUI control.
public sealed record MarqueeElement(string Caption) : Element;

// The factory the app calls. The static cctor registers the handler exactly once —
// CLR-guaranteed to run before the first Of() returns — and the `static` lambda is
// cached in a static field, so steady-state calls allocate only the element record.
public static class Marquee
{
    static Marquee() =>
        ControlRegistry.Register<MarqueeElement, MarqueeControl>(static () => new MarqueeHandler());

    public static MarqueeElement Of(string caption) => new(caption);
}
```

Why this satisfies every requirement:

- **Pure data:** the element is untouched; the *factory holder* references the
  handler (which references the control).
- **Automatic:** the app cannot obtain a `MarqueeElement` without calling
  `Marquee.Of`, and that call's class-init registers the handler. Nothing to forget.
- **Lazy:** `MarqueeHandler` / `MarqueeControl` load on the first `Marquee.Of`
  call, not when the assembly loads and not when an unrelated control is used.
- **Trimmable:** the trimmer follows `Marquee` → cctor → `MarqueeHandler` →
  `MarqueeControl`. If `Marquee` is never referenced, the whole chain is unreachable
  and removed.

The `static` keyword on the lambda is **mandatory**, not stylistic: it guarantees
the delegate is cached in a static field (one allocation, ever) and captures
nothing (a capture is a compile error).

**Construction discipline.** For the trim story to hold there must be no catch-all
fallback resolver (one would re-root every handler — see §8). So the factory must be
the *only* construction path: give `MarqueeElement` an `internal` constructor and
expose only `Marquee.Of`. A raw `new MarqueeElement(...)` from outside would bypass
registration and dispatch-miss; closing the ctor makes that unrepresentable.

---

## §7 Pattern B — the scale pattern for the built-in catalog

The built-in catalog has ~50 factories on the giant `public static partial class
Factories` (`src/Reactor/Elements/Dsl.cs:31`). Pattern A would mean ~50 holder
types and would not fit the existing facade. Instead, a single generic helper does
the guarded registration, because **statics on a closed generic type are
per-closed-type** — `Reg<ButtonElement, …>` and `Reg<TreeViewElement, …>` are
distinct types with distinct cctors and distinct trim fates:

```csharp
// Written once. Each closed Reg<E, C, H> is its own type with its own cctor +
// static field: it registers exactly one control, once, and is trimmed unless a
// kept factory references that exact instantiation.
internal static class Reg<TElement, TControl, THandler>
    where TElement : Element
    where TControl : UIElement
    where THandler : V1Protocol.IElementHandler<TElement, TControl>, new()
{
    internal static readonly byte Done = Init();
    static byte Init()
    {
        ControlRegistry.Register<TElement, TControl>(static () => new THandler());
        return 0;
    }
}
```

Each factory method gains one line:

```csharp
public static ButtonElement Button(string label, Action? onClick = null)
{
    _ = Reg<ButtonElement, WinUI.Button, ButtonHandler>.Done;   // register once; static read after warmup
    return new ButtonElement(label, onClick);
}
```

`Reg<ButtonElement, WinUI.Button, ButtonHandler>` roots only Button's handler +
control; `Reg<TreeViewElement, …>` is a different type rooted only by `TreeView()`.
The facade stays intact — **`Factories` is not split** — because ILLink trims unused
static methods member-by-member: an uncalled `Factories.TreeView()` (and the
`Reg<>` touch in its body) is removed even though `Factories.Button()` is kept.

This pattern is also available to 3P authors with large control libraries; Pattern A
is just the lower-ceremony choice for one or two controls.

---

## §8 The `ControlRegistry` contract

```csharp
public static class ControlRegistry
{
    // Idempotent, lock-free. Keyed by typeof(TElement). First registration wins;
    // a repeat for the same element type is a silent no-op (it must NOT throw —
    // see §12). Backed by ConcurrentDictionary<Type, Func<IV1HandlerEntry>>.TryAdd.
    public static void Register<TElement, TControl>(Func<IElementHandler<TElement, TControl>> handlerFactory)
        where TElement : Element where TControl : UIElement;
}
```

**It roots nothing.** The registry holds `Type → Func<…>` entries. The only static
references to handler/control types live in the *callers* of `Register` — the
per-control factory cctors (Pattern A) and `Reg<>` instantiations (Pattern B), each
on a per-control rooted path. `ControlRegistry.Default` itself is trim-neutral.

**Dispatch / precedence.** The `Reconciler` resolves a handler for `element` in
this order, caching the resolved entry into its own per-host `_v1Handlers`
(`Reconciler.cs:537`) on first hit so steady-state dispatch is the existing fast
per-host lookup:

1. per-host `_v1Handlers` — built-ins resolved on this host so far, plus explicit
   per-host registrations;
2. per-host `_typeRegistry` (`Reconciler.cs:35`) — legacy `RegisterType` callbacks;
3. **`ControlRegistry.Default`** — the global lazy table; on a hit, invoke the
   factory once, adapt to `IV1HandlerEntry`, cache into `_v1Handlers`;
4. composition primitives (the carved-out arms above the protocol).

**The explicit per-host API survives as an override + test hatch.** `Reconciler.
RegisterHandler<TElement, TControl>` (`Reconciler.cs:972`) stays public and is
consulted first (step 1), so a host can shadow the global default (sandboxed XAML
islands, tests that want a clean table). It keeps the strict throw-on-duplicate of
047 §13 Q17, because there the registration is explicit and the throw is
deterministic and greppable — unlike the global table (§12).

---

## §9 Cost model — the hot path

After warmup, a `Button()` / `Marquee.Of()` call costs:

| Step | Cost |
|---|---|
| class-init / `Reg<>.Done` static-field read | one static read + one predicted branch (~sub-ns) |
| delegate allocation | **none** — the `static` lambda is cached once |
| registry lookup / `TryAdd` | **none** — short-circuited once initialized |
| lock | **none** — the CLR type-init lock fires only on the first call |
| element record `new` | the allocation paid today, unchanged |

Net new per-call cost: **one predicted branch, zero allocations, zero locks** — and
that branch is noise next to the element-record allocation the factory already does.
Registration work (the `Register` call, the one delegate allocation) happens exactly
once per control type per process, on the cold first-use path.

Because the built-in migration (§10) adds this branch to the M1/M2 micro-bench path,
it must be measured there before landing — the expectation is that it disappears
into the allocation, but 047's perf gates make confirmation mandatory.

---

## §10 Built-in migration — dismantling the central registrar

The work is to delete the eager central registrar and distribute it:

1. **Remove `RegisterV1BuiltInHandlers()` from the `Reconciler` ctor**
   (`Reconciler.cs:335`). Move each control's registration into its factory via the
   `Reg<>` touch (§7).
2. **Preserve the "no type-level aggregation on `Factories`" invariant.** `Factories`
   today is pure methods returning element records — no static constructor, no
   `static readonly` fields referencing controls. That must stay true: a static
   cctor or a static field initializer on the partial class is kept whenever *any*
   factory is used, and if it references controls it re-roots the whole catalog,
   reintroducing the exact problem this spec removes. Because `Factories` is partial
   across files, the audit covers **every** partial.
3. **Multiple factories → one element is fine.** `TextBlock()`, `Heading()`,
   `Subheading()` all produce `TextBlockElement`; each touches
   `Reg<TextBlockElement, WinUI.TextBlock, TextBlockHandler>`. Idempotent
   registration absorbs the repeat, and the control is kept iff *any* of them is
   used.
4. **Enforce the invariant mechanically.** Two complementary guards:
   - a **trim test** in CI (§11) that publishes a minimal app and asserts unused
     controls are absent from the output;
   - optionally a Roslyn analyzer that flags a static constructor or
     control-referencing static field initializer added to `Factories`.

Carved composition primitives (Component/Func/Memo/ErrorBoundary, validation,
interop bridges — see 047 §14) are unaffected; they sit above the handler protocol
and were never in the V1 registry.

---

## §11 Trimming model and caveats

**The win is a full-trim / NativeAOT story.** Unused *public* factory methods are
removed when the app is trimmed from its entry point (`PublishTrimmed` with
`TrimMode=full`, or NativeAOT). The conservative library-preserving trim mode keeps
the public API surface and will not drop them — so the binary-size reduction is
specifically tied to aggressive whole-app trimming, which matches the goal.

**WinUI/WinAppSDK framework trimmability is a separate, evolving factor.** This
design removes *Reactor-side* rooting — the part we control — so the trimmer is
*allowed* to drop an unused control. Whether a given WinUI control type then trims
cleanly depends on the SDK's own NativeAOT/trim readiness, which is improving but
historically limited. The spec's claim is bounded accordingly: we stop pinning the
catalog; we do not promise the SDK trims everything.

**Verification trim test (the regression guard).** Stand up a throwaway app that
calls only `Button()` and `TextBlock()`; publish NativeAOT / full-trim; assert the
output contains no `TreeView` / `GridView` / `WinUI.TreeView` symbols. A failure
means something re-rooted the catalog (a stray `Factories` cctor, a surviving
central registrar, an accidental fallback resolver). This single test guards the
entire "no type-level aggregation" rule and should land with the migration.

---

## §12 Open questions

1. **Duplicate policy for the global table vs. 047 §13 Q17.** 047 mandates
   throw-on-duplicate. Under the lazy global table a hard throw is the wrong
   behavior: multiple factories legitimately register the same element type (§10.3),
   and a throw from a cctor surfaces as a module-poisoning `TypeInitialization
   Exception` at a nondeterministic first-use point. **Proposed resolution:** the
   global `ControlRegistry.Register` is idempotent *first-wins, no throw*; the
   explicit per-host `RegisterHandler` keeps the strict throw (deterministic,
   greppable). This needs ratification as a §13 Q17 amendment.

2. **Can a different host override a globally-registered control?** Step 1 of the
   precedence order (§8) says yes — a per-host `RegisterHandler` shadows the global
   default. Confirm this is the desired isolation primitive for XAML-island /
   sandboxed embeds, and whether a host needs an explicit "ignore global defaults"
   switch.

3. **Closing element constructors.** Pattern A/B both rely on the factory being the
   sole construction path (no fallback resolver). That implies built-in element
   records move to `internal` constructors. Audit for current call sites that
   `new` an element record directly (tests, samples) and route them through
   factories.

4. **Source-generated `Reg<>` touches.** The `Reg<>` line is mechanical; a source
   generator could emit it (and the closed-ctor) from each `IElementHandler<E, C>`
   implementation, eliminating the one-line-per-factory edit and making "a handler
   exists ⇒ its factory registers it" a compile-time guarantee. This is the 047 §7
   source-gen surface; it is an additive ergonomic layer over the same runtime
   contract, not a prerequisite.

---

## §13 Phasing

1. **Runtime contract.** Add `ControlRegistry` (idempotent, lock-free `TryAdd`) and
   the `Reconciler` dispatch-miss resolution + per-host caching (§8). No behavior
   change yet — built-ins still register eagerly.
2. **Prove the patterns on the external proof.** Convert
   `Reactor.External.TestControl` to Pattern A; add the §11 trim test asserting an
   unused external control is dropped. This validates the mechanism end-to-end
   before touching built-ins.
3. **Built-in migration.** Introduce `Reg<>`, remove `RegisterV1BuiltInHandlers`,
   convert the ~50 factories, close element constructors, add the `Factories`
   no-aggregation analyzer/test (§10). Gate on the M1/M2 perf bench (§9) and the
   trim test (§11).
4. **Ergonomic layer (optional).** Source-generate the `Reg<>` touches (§12.4) and
   refresh `docs/guide/extending-reactor-controls.md` to teach the factory-as-
   registration pattern instead of per-host `RegisterHandler`.
