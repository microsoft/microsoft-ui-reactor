# 525-pair mining corpus — Data Checkpoint C source artifacts

Mirror of `C:\Users\andersonch\Code\reactor-tokenusage\mining-out\` as of
2026-05-11. Captured here so the analysis in
`../2026-05-11-525run.md` is reproducible against the exact bytes even if
the upstream `reactor-tokenusage` repo rotates or is cleaned up.

| File | Source schema | Used by |
|---|---|---|
| `fixes.jsonl` | spec 037 §6 — `(run_id, turn_before, turn_after, file, diag, before, after, delta, fix_kind, ...)` | Tier-2 threshold tuning harness (`ThresholdTuningTests.EndToEnd_corpus_run`); Phase-3 rule authoring exemplars |
| `ranker-labels.jsonl` | spec 037 §3 — one row per (build, diagnostic) with `addressed_by_next_fix`, etc. | Phase-4 learned ranker training (Data Checkpoint D — pending); gate-threshold distribution analysis |
| `patterns.json` | spec 037 §7 — clusters keyed on `(diag_code, receiver_type, fix_kind)` with `frequency`, `count`, `exemplar_run_ids`, `proposed_rule` | Phase-3 human-review queue for rule authoring |
| `unresolved.jsonl` | rows the mining pipeline couldn't classify | Cosmetic — track noise floor |

**Not included:** the 563 raw `*.events.jsonl` files under
`C:\Users\andersonch\Code\reactor-tokenusage\results\`. Each `fixes.jsonl`
row carries a `source_event_log` field pointing to its origin trace; if a
reviewer needs the raw turn-by-turn agent transcript for a specific
exemplar, retrieve it from the sibling repo using that path.

**Provenance:** all 1,027 fix pairs / 1,233 ranker rows / 104 clusters come
from a single agent (`gpt-5.5`). The spec 037 §11 cross-agent
reproducibility bar (Phase-3 Validation Gate #2) is **not met** by this
drop alone — a second-agent corpus drop is required before opening any
Phase-3 rule PR.
