# Decision Criteria for §13 Open Questions — Phase 0 §14 Deliverable 6

Each entry: question summary, disambiguating §15 test(s), threshold that
picks each branch, and the spec edit that captures the decision once data
lands.

This document is the place Phase 1+ goes for the rule before re-litigation
starts. Anything not specified here either inherits today's behavior or gets
a separate design call.

---

## Q1 — Descriptor vs hand-coded handler as the primary first-party surface

**Summary.** §6 proposes declarative `ControlDescriptor<TElement, TControl>`
as a co-equal authoring surface alongside §4's hand-coded
`IElementHandler<TElement, TControl>`. Which is the primary first-party
shape? Source-gen is deferred — not a contender for Phase 2.

**Disambiguating tests.** Micro M1, M2, M5, M7, M10. Macro L4, L9. Spec §13
Q1 also calls out L12 (hot-reload), but L12 is deferred to Phase 2, so the
Phase-2 head-to-head treats hot-reload qualitatively until L12 lands.

**Decision matrix** (copied verbatim from spec §13 Q1 — ratified by this
doc so Phase 2 starts with it locked):

| Descriptor vs handler delta on M1/M2/M5/M7 | Outcome |
|---|---|
| Descriptor within 5% of handler | Ship descriptors as primary first-party surface. Handlers as escape hatch for irregular controls and runtime registration. |
| Descriptor 5–15% slower | Judgment call. Likely descriptors if L4/L7/L9 macros show no detectable regression (≤2%); handlers if any macro shows >2%. |
| Descriptor >15% slower on any of M1/M2/M5/M7 | Hand-coded handlers ship as primary surface. Descriptors stay available for *late-bound external controls only* (the §16-permanent-fallback path). Revisit when source-gen could collapse the cost. |

Phase 2 also produces qualitative inputs that feed the 5–15% branch:
- **LOC per control** count for the three test controls (ToggleSwitch,
  Slider, Border) in both shapes.
- **Cognitive load** rating from 2-3 engineers reading both versions cold.

**Spec edit when data lands.** §6 keeps the winning shape as
"primary first-party surface"; §13 Q1's matrix gets a "**Resolved:** …" line
citing the Phase-2 measurement. The loser stays documented in §16 (permanent
fallback path).

---

## Q3 — Echo suppression elimination (§8 / §8.1)

**Summary.** Can `ChangeEchoSuppressor` be deleted? §8 hypothesizes yes for
most sites; §8.1 proposes `mostRecentEventCount` as the cleaner replacement.

**Disambiguating tests.** Three correctness scenarios (each fails today
without suppression):
1. `Echo_Coercion_Slider` — write `Value = 1000` with `Maximum = 100`.
   Observe whether the user callback fires with `1000` (correct under
   suppression / counter; broken under naïve delete) or `100`.
2. `Echo_FloatPrecision_NumberBox` — write `Value = 0.3`. NumberBox stores
   `0.30000001`. Observe whether the callback re-fires the round-tripped
   value back into user state.
3. `Echo_UserStateRacesRender` — queue a `SetState` between render and
   event-dispatch (matches the ColorPicker / PropertyGrid cross-row bug
   captured in [`audits/begin-suppress-audit.md`](audits/begin-suppress-audit.md)).
   Observe whether the callback overwrites the just-set state.

Plus perf gate: **M2 (`Mount_Leaf_OneCallback`)** delta — the candidate
replacement must add ≤ 5% to M2.

**Decision criteria.**

| Candidate | Pass condition |
|---|---|
| `suppression-as-is` (status quo) | Reference. All three correctness tests pass; M2 is the baseline. |
| `delete + tight diff` (the §8 main bet) | Passes `Echo_Coercion_Slider` only if the test is rewritten to compare against `tag.Value` post-write (which requires per-control tolerance logic for `NumberBox`/`Slider`). Passes `Echo_UserStateRacesRender` only if the per-control handler holds a "last fired" value compared at fire time. Does **not** pass without per-control adjustments — and the audit shows that's 8 sites needing tolerance metadata and 1 site (ColorPicker) that fundamentally can't be solved without §8.1. |
| `mostRecentEventCount` round-trip (the §8.1 alternative) | Passes all three correctness scenarios by construction. Ships **iff** it adds ≤ 5% to M2. |

**Per the audit results** ([`begin-suppress-audit.md`](audits/begin-suppress-audit.md)):
14/24 sites can be eliminated by a tighter handler-side diff with **no** new
machinery; 8/24 sites need per-control tolerance metadata; **1/24 site**
(ColorPicker) genuinely requires §8.1. Building all of §8.1 just for
ColorPicker is over-engineering. Recommendation that flows from the audit:

> **Resolved (driven by 0.1 audit, ratified here):** ship the
> "delete + tight diff" direction for the 14 trivial sites; ship per-control
> tolerance metadata for the 8 coercion / float-precision sites; ship a
> one-off imperative shim for ColorPicker. Do **not** build §8.1.

This recommendation flips if a *second* `user-state-races-render` site is
discovered after the audit (e.g., in a Phase-1 external control). Phase 1
re-runs the audit on any newly-registered control surface before promotion.

**Spec edit when data lands.** §8 is rewritten as the primary direction;
§8.1 is demoted to "considered, rejected — see decision-criteria.md Q3."
[`begin-suppress-audit.csv`](audits/begin-suppress-audit.csv) becomes the
canonical list of sites the §8 cleanup PR has to touch.

---

## Q6 — Setters re-run every update vs reference-equality skip

**Summary.** Today `ApplySetters` re-runs the entire `Action<T>[]` on every
update (idempotent but wasteful). A reference-equality check on the array
(skip when `oldEl.Setters == newEl.Setters`) would skip most updates.

**Disambiguating test.** M7 (`Update_NoChange`, 1000-element tree no-op
re-render). Variants: setters always re-run vs setters skip-on-array-equality.

**Decision criteria.**

| M7 delta from skip-on-ref-equality | Outcome |
|---|---|
| ≥ 5% faster on the 1000-element no-op re-render | Adopt skip-on-ref-equality. The contract becomes: "the same `Action<T>[]` reference is treated as identical setters; allocate a new array to force re-run." Document explicitly. |
| 0–5% | Adopt skip-on-ref-equality anyway. The change is one branch; no carrying cost. |
| Regression or noise | Keep status quo. Re-check after Phase 1 lands per-control payload structs (Q18) since pool reset semantics change the picture. |

**Risk.** Setters that close over mutable state (a counter incremented per
re-run) would observe behavior change. Recommend a `SetterRunPolicy` flag
on the element record (`Always` / `OnArrayChange`, default `OnArrayChange`)
so back-compat is opt-in per element if a real consumer trips on it.

**Spec edit when data lands.** §6.1 documents the chosen policy.

---

## Q7 — Pool integration with `ctx.AllocateControl`

**Summary.** Does the protocol-level `AllocateControl(factory)` actually
reuse pooled instances correctly across the v1 protocol boundary?

**Disambiguating test.** M12 (`Pool_Rent_HotPath`, ListView recycle 100
instances ↔ 20 pool slots). Variants: handler uses `ctx.AllocateControl`
vs direct `new T()` per-handler.

**Decision criteria.**

| M12 result | Outcome |
|---|---|
| `AllocateControl` matches or beats `new T()` on rent/return cycle (alloc bytes within 5% of zero, time within 5% of direct) | Promote `AllocateControl` as the documented mount path; deprecate direct construction in handlers. |
| `AllocateControl` regresses time or alloc | Investigate; if the cause is pool lookup overhead, consider TLS-cached `Type`-keyed slot. Do not ship the protocol primitive until the gap is closed. |

Plus correctness gate from Q18: rent → mount → mutate → unmount → return →
rent same control must observe zero residual state from the previous tenant.
M12 includes a correctness assertion phase before the timing phase.

**Spec edit when data lands.** §4 documents `AllocateControl` as the
required mount-time allocation primitive.

---

## Q11 — `handledEventsToo` routed-event subscription

**Summary.** Today's modifier API subscribes via `event +=`, equivalent to
`AddHandler(routedEvent, handler, handledEventsToo: false)`. Some scenarios
need `true` (observing a `KeyDown` an inner control marked Handled, etc.).

**Disambiguating test.** Scenario test: child Handled-marks `KeyDown`;
parent has `.OnKeyDownAny`; verify parent fires.

**Decision (Phase-1 surface, fixed in advance):**

| Option | Phase-1 plan |
|---|---|
| Add `.OnPointerPressedAny(...)` / `.OnKeyDownAny(...)` / etc. variants with their own trampolines | **Rejected for Phase 1.** Doubles routed-input slots in `ModifierEventHandlerState`, walking back the §9 savings. Revisit if Phase-2 macros (L4/L8) show the savings have headroom. |
| Imperative escape hatch `ctx.AddRawRoutedHandler(routedEvent, handler, handledEventsToo: true)` that bypasses the trampoline pattern | **Adopted for Phase 1.** Authors who need handled-too take on the same correctness burden today's `RegisterType` lambdas have (no pool survival, no trampoline reattach). Acceptable because the use case is rare. |
| Leave the surface unaddressed | Rejected; the question is a known gap. |

**Spec edit when data lands.** §9 ratifies the escape-hatch design;
§13 Q11 marked Resolved.

---

## Q17 — Registry precedence and subtype behavior

**Summary.** Four sub-questions about how `_typeRegistry` lookup behaves
after the split-library plan. Spec §13 Q17 proposed answers; this doc
ratifies them, with one revision: **no override mechanism in v1.**

**Ratified recommendations** (revised from spec §13 Q17):

| Sub-question | Resolved answer |
|---|---|
| Element-type lookup exactness | **Exact runtime type only.** No assignable / base-match. Subtype dispatch is a footgun under the split-library plan. |
| Downstream package registers for a built-in element type | **Throw at registration time.** Built-ins are not overridable in v1. Reactor's built-in dispatcher always wins for built-in element types. |
| Open generic element registrations (e.g., `RegisterType<DataGrid<>, _>`) | **Not supported in v1.** Open generics interact badly with trim. |
| Duplicate registration (any element type already registered) | **Throw at registration time.** No `RegisterOverride` verb in v1. |

**Rationale for the v1 "no override" stance.** Override-of-handler is a
foot-gun whose primary use cases (test fakes, A/B handler swaps,
shadowing a built-in) can be served by:
- Composing the Reconciler with the desired registry contents from
  scratch in test setup, or
- Adding an explicit `RegisterOverride` verb in a later release. That
  addition is purely additive — existing `RegisterType` callers keep
  working unchanged — so deferring it is **non-breaking**.

Shipping v1 without override means: every registered type's handler is
known to be unique at startup. Reduces debug surface significantly.

**Validation test.** One scenario covers all four: register a handler
for an element type whose base also has one (assert exact-type lookup);
register for a type that already has a built-in handler (assert throw);
register the same type twice (assert throw).

**Spec edit when data lands.** §2 / §6 incorporate the registry rules;
the public `RegisterType` signature is locked in Phase 1.
`RegisterOverride` is not exposed. §13 Q17 is updated with the
"no override" revision and a footnote linking here.

---

## Q18 — Pool policy as a public API

**Summary.** `AllocateControl` is necessary but not sufficient. External
authors need the full pool contract.

**Ratified recommendations** (from spec §13 Q18):

| Pool concept | Resolved answer |
|---|---|
| Poolability | Descriptors / handlers declare `IsPoolable` explicitly. Controls with persistent native resources, custom DirectX surfaces, or non-resettable state opt out. |
| Pool key | `typeof(TControl)` only for v1. Finer keys (e.g., `(typeof(TextBlock), styleKey)`) revisited later. |
| Reset contract | The contract enumerates exactly what is cleared on return: `ControlEventState` (per §9.2), pending event subscriptions, `ModifierEventHandlerState`, attached-DP `Tag`, `DataContext` if Reactor sets one. Anything not in the list is a reuse hazard and must not survive return. |
| What survives | Layout caches, template state, internal control-of-control state (`ListView` realized container reuse). Enumerated separately from the reset list. |
| Dual-RCW | Pool return is idempotent and does not double-clear. (Mirrors today's `ReactorAttached.StateProperty` discipline.) |
| Diagnostic | A non-resettable property found dirty on rent emits a structured log entry. Surfaces external-control reset bugs early. |

**Validation tests.**
- M12 (perf) — see Q7.
- Correctness: rent → mount → mutate → unmount/return → rent same control
  → assert no residual state from previous tenant. Run against
  pool-policy-aware (uses `IsPoolable`) and pool-policy-naive (no flag,
  defaults to non-poolable) handlers.

**Spec edit when data lands.** §6 (descriptor metadata) and §4 (`MountContext`
surface) document the contract. The `ElementPool` public API is extended to
match.

---

## Q19 — `WriteSuppressed` as a public primitive

**Summary.** Regardless of which §8 direction wins, the *public surface* for
"this write should not produce an apparent user event" stays stable.

**Ratified decision.** Phase 1 ships `ReactorBinding<T>.WriteSuppressed(...)`
as a public method backed by today's `ChangeEchoSuppressor.BeginSuppress`.

**Validation tests.**
- M2 (`Mount_Leaf_OneCallback`) and M13 (`Setters_Suppression_Scope`)
  against Phase 1's `WriteSuppressed`.
- The Phase 4 swap of the underlying mechanism (whichever §8 direction Q3
  picks) must not change M2 / M13 outcomes — `WriteSuppressed` is a stable
  API across the swap.

**Spec edit when data lands.** §4 and §8 both reference `WriteSuppressed`;
§8's eventual implementation choice changes the body, not the signature.

---

## Q9 — Override semantics for handler swap-out

**Summary.** §13 Q9 asks how an external author swaps in a fake handler
for testing (e.g., a stub `ButtonHandler`). The original proposal was
`RegisterOverride<TElement, TControl>(handler)` with a structured log
entry.

**Ratified decision.** **No override mechanism in v1.** Direct
consequence of [[Q17]]'s "duplicate registration throws" stance —
overriding an already-registered handler is the same operation as
duplicate registration, just with intent to replace.

**Testing strategies that work without an override verb:**
- **Compose the Reconciler from scratch** in test setup. Tests
  instantiate a `Reconciler` with the registry contents they need —
  no shared global state, no override pattern needed.
- **Inject the handler at construction time** via the test's own DI
  container. Most existing in-tree tests already do this.
- **Use a test-only subclass** that wraps the production handler and
  intercepts the method under test.

**Adding override later is non-breaking.** A future `RegisterOverride<T,C>`
verb is purely additive — existing `RegisterType` callers keep working,
and the override case becomes the explicit opt-in. So Phase 1 ships
without it; if a real consumer scenario surfaces post-Phase-1 that
truly can't be served by the alternatives above, add the verb in a
point release.

**Spec edit when data lands.** §13 Q9 marked Resolved with the v1
"no override" stance + the alternative testing strategies. §4 documents
that `RegisterType` is the only registry mutation in v1.

---

## Q12 — `Update` return type and substitution semantics

**Summary.** §4's straw-man has `UIElement? Update(ctx, oldEl, newEl, ctrl)`,
which would let the handler return a *different* control mid-update.
Substitution requires parent-collection fixup, modifier reapply, and
`ItemsControl` re-realization — a deep invariants surface to maintain.

**Ratified decision.** **Forbid substitution.** Phase 1 ships
`void Update(MountContext ctx, TElement oldEl, TElement newEl, TControl control)`.
Type changes flow through the existing unmount-and-remount path; the
handler must mutate `control` in place or accept that the engine
remounts.

**Rationale.** Substitution-mid-update is a bug farm — every consumer
of the parent collection has to be invalidated, every modifier that
holds a control reference becomes stale, and the `ItemsControl`
realized-container caches need to be re-keyed. The existing remount
path already handles all of this correctly. Adopting the substitution
shape would re-introduce these failure modes at every `Update` call
site instead of confining them to type-change boundaries.

**Matches industry shape.** React Native Fabric's `updateProps(oldProps,
newProps) → void` makes the same call for the same reason.

**Substitution is non-breaking to add later** — `void Update(...)` is a
strict subset of `UIElement? Update(...)`'s contract. If a real need
emerges in Phase 3+ the signature can be widened without breaking
existing handlers.

**Spec edit when data lands.** §4's straw-man is updated to
`void Update(...)`. §13 Q12 marked Resolved with a pointer here.

---

## Q14 — Concurrency model

**Summary.** §13 Q14 calls out that Reactor is UI-thread-only today,
but the protocol surface doesn't say so explicitly. WinUI controls
backed by `DispatcherQueue`-bound resources require UI-thread
allocation; off-thread mount would surface as a `RPC_E_WRONG_THREAD`
the first time a property is set.

**Ratified decision.** **UI-thread-only.** All `Mount` / `Update` /
`Unmount` calls run on the thread that owns the `DispatcherQueue` the
Reactor instance was created on. Handlers may freely access
control-state and DPs without synchronization. Handlers that allocate
WinUI controls do not need to dispatch to the UI thread — the engine
guarantees they are already on it.

**No thread-affinity flag in v1.** The §13 Q14 hypothetical of
`ThreadAffinity = Any / UIThread` for off-thread mount (background
list virtualization) is **deferred** until a real consumer surfaces.
Adding the flag later is non-breaking (default stays `UIThread`).

**Diagnostic.** Reactor's existing `DispatcherQueue.HasThreadAccess`
check at `Mount` entry continues to fire in Debug builds. Tightening
to an unconditional throw in Release is **deferred to Phase 1** —
needs a measurement pass to confirm no in-tree caller hits it
unintentionally.

**Spec edit when data lands.** §4 documents the UI-thread guarantee on
the `MountContext` surface. §13 Q14 marked Resolved.

---

## Cross-references

- Audit data: [`audits/begin-suppress-audit.md`](audits/begin-suppress-audit.md)
  (Q3), [`audits/event-handler-state-audit.md`](audits/event-handler-state-audit.md)
  (Q11 ergonomics), [`audits/existing-api-surface.md`](audits/existing-api-surface.md)
  (Q17, Q19 in-tree consumer compatibility).
- Perf suite tests M1–M13 and L1–L11 are defined in spec §15.3 / §15.4
  and instantiated under `tests/perf_bench/PerfBench.ControlModel` (Phase 0
  deliverable 0.3.3).
- Each resolved Q updates the corresponding §13 entry in the spec with a
  "**Resolved:** …" line.
