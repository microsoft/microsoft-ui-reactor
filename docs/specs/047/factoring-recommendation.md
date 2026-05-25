# Spec 047 Factoring Recommendation — Phase 0 §14 Deliverable 7

Outcome: **Keep spec 047 unified** for now. Two carve-outs ship as standalone
follow-ups (one explicitly ahead of Phase 1, one alongside).

## Inputs

- [`audits/begin-suppress-audit.csv`](audits/begin-suppress-audit.csv) +
  [`audits/begin-suppress-audit.md`](audits/begin-suppress-audit.md) — 24
  call sites, 14 trivially eliminable, 8 tolerance-shaped, 1 ColorPicker
  edge case, 1 redundant.
- [`audits/event-handler-state-audit.md`](audits/event-handler-state-audit.md)
  — 7 distinct controls with `control-intrinsic` events, 51 fields total
  on `EventHandlerState`, 42 of them universal routed-input.
- 0.4 baseline numbers (in
  [`baseline-results/`](baseline-results/) — partial at Phase 0 freeze;
  M1–M13 captured on the local LAPTOP-4MEP83VI x64-emulated ARM machine;
  ARM64-native and a separate x64 workstation deferred behind the gate).
- The reviewer's proposed split: **047-core + memory + echo + setters-fix
  + source-gen**.

## Recommendation by reviewer-proposed bucket

| Reviewer bucket | Recommendation |
|---|---|
| **047-core** — the v1 protocol surface (handlers, MountContext, `BindFor`, public-API promotion) | **Keep in 047.** The v1 protocol is the primary product of the spec and shares invariants with everything else in this list. Carving it out leaves the rest of 047 dependent on an external spec, inverting the dependency order. |
| **memory** — `EventHandlerState` split per §9 / 0.2 audit | **Keep in 047, plan to land independently within the Phase 1 PR train.** The 0.2 audit confirms the split is structurally well-defined: 7 per-control structs, no `hybrid-or-ambiguous` fields, a clean cut between `ModifierEventHandlerState` and `ControlEventStateBox`. But the change is small enough (one struct rename + 7 new structs + one switch in `GetOrCreateEventState`) that a dedicated spec adds more process overhead than the design clarity it gains. The work lives under spec 047 §9. If the Phase 1 PR train grows to >5 PRs, **revisit** carving this out as `047-memory` then. |
| **echo** — `BeginSuppress` evolution per §8 / §8.1 / 0.1 audit | **Keep in 047 as the §8 direction.** The 0.1 audit shrunk the design space significantly: §8.1 (mostRecentEventCount) is no longer load-bearing — only 1 site (ColorPicker) needed it, and that site can be handled with a one-off shim. The remaining work (delete 14 trivial suppressions, add per-control tolerance metadata for 8 sites, ColorPicker shim, delete the 1 redundant defensive call) is small enough to live in §8 + a short follow-up PR. **Split rejected.** |
| **setters-fix** — §8.2 `Set(...)` runs outside echo scope, fires unmasked write | **Carve out as a standalone fix and ship ahead of Phase 1.** This is a current correctness bug (callback fires once today on `Set(ts => ts.IsOn = true)`; should fire zero times). The fix is narrow and doesn't require the v1 protocol — just plumb setters through the existing suppression scope. M13's Phase-0 baseline records the failing behavior; the standalone fix flips M13 to pass. **Recommendation:** open a separate small spec `047a-setters-suppression-scope` (or land directly as a typed bug fix with PR notes citing §8.2). Either way, do not couple to the rest of 047 — the bug is two-paragraph-explainable and worth not blocking on the bigger phasing. |
| **source-gen** — §7 source generator direction | **Already deferred per §7 status and §14 Phase 2.** Source-gen is not in 047's Phase 1 / Phase 2 scope; it stays a "considered, deferred" direction in §7. Carving it into its own spec would only matter if Phase 2 reactivates it; defer that decision. **No action at this gate.** |

## Three signals spec §14 calls out

### Signal 1 — is the §8 evolution small enough to leave inline?

**Yes.** The 0.1 audit reduced the §8 work to:
- 14 trivial deletions (delete the `BeginSuppress` call site; rely on the
  existing `if (control.X != element.X)` gate + a handler-side comparison
  against `tag.X`).
- 1 defensive-redundant deletion (AutoSuggestBox).
- 8 sites get a per-control tolerance metadata addition (NumberBox, Slider,
  RatingControl have float-precision needs; coercion-prone sites declare
  the coerced-by relationship).
- 1 ColorPicker imperative shim.

That's about a day of work and one PR. Inline as a §8 follow-up; **no
dedicated spec.**

### Signal 2 — does the `EventHandlerState` split land in v1 protocol PRs or as a precursor?

**Inside the Phase 1 PR train.** The 0.2 audit shows the structural cut is
clean (0 ambiguous cases). The §9.4 frequency hypothesis (most controls
allocate no `ModifierEventHandlerState`) will be **measured** by M11 in
Phase 1 — that measurement drives whether the savings make it worth a
dedicated PR or whether it rides along with the §9 cleanup. Either way, no
separate spec; ride spec 047 §9.

### Signal 3 — is the §8.2 setter-echo fix small enough to ship ahead?

**Yes, and it should.** This is the carve-out worth doing. The fix is
narrow, the bug is current and reproducible (M13 baseline confirms the
1-callback fire today), and the fix doesn't depend on any of the rest of
047's protocol work. Shipping ahead lets Phase 0 close with a measurable
correctness improvement in main rather than waiting for the larger v1
protocol PRs. Open a 1–2-paragraph spec note or land as `Fix #issue` with
the §8.2 description in the PR.

## Carve-outs to execute before Phase 1 starts

- **047a-setters-suppression-scope** (or equivalent PR) — fix the §8.2
  bug. M13's Phase-0 baseline flips from `fires=1` to `fires=0`. ~1
  PR, ~50 lines of change in `Reconciler.cs` to thread `BeginSuppress`
  around `ApplySetters` for value-bearing controls.

That's the only one. Everything else stays in spec 047.

## What stays in 047 (final scope)

Unchanged. Spec 047 still covers:
- The v1 protocol surface (§4) and the public API promotion (§14 Phase 1).
- The descriptor-vs-handler decision matrix (§13 Q1, ratified in
  [`decision-criteria.md`](decision-criteria.md)).
- The echo-suppression evolution (§8) — reshaped by the 0.1 audit but
  still inline.
- The per-control `EventHandlerState` split (§9) — direction confirmed
  by the 0.2 audit, ships inside the Phase 1 PR train.
- The §13 open questions, with criteria already pre-committed for the
  data-driven ones.

## Implications for the task file

The implementation task file
[`docs/specs/tasks/047-extensible-control-model-implementation.md`](../tasks/047-extensible-control-model-implementation.md)
stays under one number (047). No rename, no re-scoping. The
Phase 0 exit checklist item 0.7 closes with this recommendation. The
setters-fix carve-out is tracked as a separate work item (not in the 047
task file) — it's its own PR / issue.

## Review log

This recommendation is intended to land at the same gate as the other Phase 0
deliverables. Reviewers should explicitly confirm the carve-out: shipping
the §8.2 fix ahead of the v1 protocol work is the only structural change
relative to the spec as drafted.
