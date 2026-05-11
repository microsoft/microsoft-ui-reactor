# EC2 hand-off — Phase 2 eval checkpoint

Spec: [`docs/specs/038-mur-check-did-you-mean-design.md`](../038-mur-check-did-you-mean-design.md) §8 + §13
Task list: [`docs/specs/tasks/038-mur-check-did-you-mean-implementation.md`](./038-mur-check-did-you-mean-implementation.md) — Phase 2.6 exit criterion

Generated 2026-05-11 to hand off to the eval-harness operator. Mirrors the methodology of EC1 (see #226 Phase-7 summary + the "EC1 re-run (with gate)" subsection of the task list).

---

## What landed in Phase 2

Phase 2 ships the MSBuild passthrough and the deterministic pre-emit ranker on top of the Phase-1 Tier-2 suggester. The agent-visible changes are:

1. **`mur check` now suppresses cosmetic noise mid-iteration.** CS1591 (XML doc), CS0168 (unused var), IDE0xxx (style), NU1701/NU1605 (NuGet restore), MSB3245/3270/3277 (resolution chatter), and CS8600–CS8625 (nullable warnings) all score below the 0.6 iteration threshold and are suppressed. Errors always emit (universal floor). REACTOR_* warnings at 0.9 still emit.
2. **`mur check --final`** runs the same parse + the suggester but disables suppression. The agent is meant to run this once when iteration is clean, as the explicit "I am done iterating" gate.
3. **`mur check -- <msbuild args>`** forwards everything after a bare `--` to `dotnet build`. mur auto-injects `--nologo`, `-v:m`, `-p:Platform={host arch}` only when not named in passthrough.
4. **`--strict` / `--quiet`** mode flags exist but EC2 doesn't exercise them.

The SKILL.md update at `plugins/reactor/skills/reactor-build-and-check/SKILL.md` instructs the agent to use iteration mode in the fix loop and `mur check --final` once before declaring done.

The Phase-2 hypothesis: ranker suppression saves turns on noisy builds where cosmetic warnings would otherwise distract the agent from the real blocker. Calc may regress slightly because its failures are typically 1–2 errors and there's no noise to suppress.

---

## Run matrix

Same as EC1's re-run: **5 paired rounds × `reactor-calc` / `reactor-kanban`, `gpt-5.5`**.

| Arm | Branch | Skill flag in eval prompt |
|---|---|---|
| `reactor-calc` (base) | `main` at start of Phase 2 (commit `5eec60d`) | counter-prompt below |
| `reactor-calc-mur-check` | Phase-2 branch HEAD (`feat/038-mur-check` after Phase-2 PR merges) | unmodified `reactor-build-and-check` skill |
| `reactor-kanban` (base) | same as calc base | counter-prompt below |
| `reactor-kanban-mur-check` | Phase-2 branch HEAD | unmodified skill |

If the Phase-2 branch hasn't merged yet, point the variant arms at the branch directly. The commit you want is the one that lands `src/Reactor.Cli/Check/Ranker/PolicyTable.cs` plus `src/Reactor.Cli/Check/ArgsParser.cs`.

---

## Counter-prompt for the BASE arm

Append this to the eval prompt so the base agent doesn't reach for `mur check` (which is the subject of comparison):

> For this run, do **not** use `mur check`. Use `dotnet build <project> -p:Platform=<arch>` to compile, and read the raw MSBuild stderr/stdout output directly. Ignore any guidance in the `reactor-build-and-check` skill that recommends `mur check` — that tool is the subject of this comparison and must not be invoked. All other guidance in the skill (cheat table, iteration discipline) still applies.

The variant arms run the eval prompt **unmodified** — the SKILL.md update inside the repo already directs the variant agent to use `mur check` + `mur check --final`.

---

## What to capture

For every run, log to the eval CSV / JSON:

- **wall** (seconds)
- **cost** (USD)
- **tokens** (total input + output)
- **turns** (agent action count)
- **first_build_ok** (boolean: did the final `dotnet build` exit 0?)

For variant arms only, **also capture from `mur check --trace`**:

- **iteration_firings** — count of Tier-2 suggestions emitted across all iteration-mode `mur check` invocations in the run (grep trace rows for `→ try:` is fine, or count suggester telemetry rows in `~/.mur/telemetry/`)
- **ranker_suppressed_count** — count of trace rows whose code's iteration-score < 0.6. The trace records every parsed diagnostic, so this is computable post-hoc.
- **invoked_final** — boolean: did the agent run `mur check --final` exactly once at the end?

Set `MUR_TELEMETRY=1` in the agent shell so per-suggestion telemetry lands at `~/.mur/telemetry/<yyyy-mm-dd>.jsonl` — useful for post-hoc analysis even if not graded against.

---

## Pass criteria (from spec §13 / task list)

EC2 passes when **all three** hold:

1. **Tokens improve ≥ 5% on at least one project arm**, with the other arm not regressed more than +5%. The Phase-2 prediction is ~−10–15% on kanban; calc may move slightly either way.
2. **No false-positive ranker fires.** Concretely: every diagnostic the variant arm's `mur check --final` invocation surfaces that is a real build error did NOT get suppressed in iteration mode. The guardrail tool (`tools/Reactor.MurCheckGuardrail/`) audits this automatically — point it at the iter + final traces of each variant run; exit code 1 is a fail.
3. **First-build success rate ≥ 5/5 on both variant arms.** If the variant arm fails to produce a clean build on any of the 5 rounds, that's a regression vs EC1's PASS-PASS state.

Cost / wall are secondary metrics — interesting if they move materially but not gating.

---

## Estimated cost

~$25–40 per 5×N batch, per EC1's empirics. Budget for two batches (in case the first surfaces an issue and we re-run): ~$80.

---

## Steps for the eval operator

1. **Pin the base commit.** `git rev-parse origin/main` → record the SHA. This is the "main at start of Phase 2" reference.
2. **Pin the variant branch.** The Phase-2 PR branch (or `main` after merge if EC2 runs post-merge). Build mur from this branch and put `<repo>/bin/x64/` on PATH so the variant agent's `mur check` invocations resolve to the Phase-2 binary.
3. **Build both** with `dotnet build src/Reactor.Cli -p:Platform=x64` on each branch and stash the `bin/x64/mur.exe` per arm — the variant arm's PATH must point to the Phase-2 build.
4. **Run the 5×N.** Mirror EC1's round-3 prompt (the prompt-iteration sequence is documented in the task list under "EC1 results"). Variant arms get the unmodified skill; base arms get the counter-prompt above appended.
5. **Compute per-arm means** (5 runs each: wall, cost, tokens, turns) and the paired variant−base comparison (per round, then meaned across rounds).
6. **Run the guardrail.** For each variant run, point `Reactor.MurCheckGuardrail` at the run's iter + final trace files. Aggregate violations across runs; >0 = EC2 fail.
7. **Report.** Append a section to `docs/specs/tasks/038-mur-check-did-you-mean-implementation.md` under "EC2 results — 5×N landed YYYY-MM-DD" with the table layout EC1 used.

---

## Pointers

- EC1 methodology and result tables: task list § "EC1 results — 5×N landed 2026-05-10" and "EC1 re-run (with gate) — 5×N landed 2026-05-10".
- Sample real traces (for local sanity-checking the guardrail before EC2 begins): `.flake-runs/038-phase2-trace/iter.jsonl` and `final.jsonl`, against the `SmokeFixture` library. Not part of the eval — just proof the trace pipeline works end-to-end.
- Sample apps the variant agents will iterate on: `reactor-calc` and `reactor-kanban` per #226 § Phase-7. These live in the eval harness, not this repo.
