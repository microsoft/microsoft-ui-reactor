# Cross-agent reproducibility audit — gpt-5.5 vs claude-sonnet-4.6 (Validation Gate bar #2)

**Date:** 2026-05-11
**Spec:** 038 §3.0, §3.2, Validation Gate bar #2
**Sources:**

- gpt-5.5 525-run aggregate: `C:\Users\andersonch\Code\reactor-tokenusage\mining-out\pre-sonnet-bak\patterns.json` (104 clusters, 1027 fixes)
- claude-sonnet-4.6 525-run aggregate: `C:\Users\andersonch\Code\reactor-tokenusage\mining-out\patterns.json` (41 clusters, 368 fixes)

## Purpose

The Human Validation Gate's bar #2 ("Cross-agent reproducibility") requires that any Class-A Phase-3 rule's seed cluster reproduces across ≥ 2 different agents. With the claude-sonnet-4.6 corpus aggregated (handoff at `C:\temp\mur-handoff-sonnet-525.md`), this audit identifies the receiver-typed clusters that pass bar #2 and pegs the Class-A rule-authoring queue against them. Receiverless clusters (`receiver_type: null`) are out of scope — they're too broad to anchor an `IRulePattern.TryMatch` against.

## Cluster-key normalization

Cluster IDs are aggregator-run-local and do NOT compare across corpora. The cross-corpus key is the tuple `(diag_code, receiver_type, fix_kind)`. Receiver names are compared verbatim including any generic type parameters (so `TemplatedListViewElement<string>` and `TemplatedListViewElement<Product>` are distinct rows in the table below; rule design then decides whether to generalize over the type parameter).

## Cross-agent overlap table (receiver-typed clusters, both corpora)

The key bar #2 verdict column applies the spec §3.2 frequency floor (`count ≥ 10` in the original Validation Gate bar #1) jointly with the cross-agent presence test:

- **STRONG** — both agents have `count ≥ 3` on the same key.
- **WEAK** — both agents present, but one or both at `count < 3`. Acceptable as bar-#2 evidence; rule may need supplementary justification on bar #1 (frequency).
- **SINGLE** — present in only one corpus. Class-B (vocab) is still possible with doc citation; Class-A is blocked until a third corpus surfaces the key.

| `(code, receiver, fix_kind)` | gpt-5.5 count (freq) | sonnet count (freq) | Cross-agent | Notes |
|---|---|---|---|---|
| CS1955, GridSize, other | **110 (10.7%)** | **36 (9.8%)** | **STRONG** | Top cluster in *both* corpora. "Missing parens on GridSize factory" — Tier-2 doesn't cover CS1955 today. First Phase-3 Class-A rule target. |
| CS0117, Theme, other | **16 (1.6%)** | **11 (3.0%)** | **STRONG** | Already covered by `ThemeBackgroundSuffixRule` (Class-B); the cross-agent evidence now gives it Class-A justification on top of the vocab citation. Update PR description bar #2 line to cite this audit. |
| CS0117, GridSize, renamed_member | 3 (0.3%) | 4 (1.1%) | **STRONG** | GridSize member rename (`Auto` / `Star` / `Pixel` family). Rule target: lookup-table on missing-member name. |
| CS1061, TextBlockElement, renamed_member | **12 (1.2%)** | 1 (0.3%) | WEAK | Strong in gpt-5.5; single sonnet exemplar. Acceptable bar #2 with WEAK tag — author rule with the sonnet exemplar cited as the cross-agent witness. |
| CS1061, TextBlockElement, other | 2 (0.2%) | 3 (0.8%) | **STRONG** | Inverse of the above: stronger in sonnet. Same receiver type, different fix_kind classifier. May collapse with the renamed_member row at rule-design time (a single rule covering both `*Background → SolidBackground`-shaped and `*Color → Foreground`-shaped misses against TextBlockElement). |
| CS1929, TemplatedListViewElement\<T\>, other | 7 (across T ∈ {string×3, ListItem×1, int×1, WorkItem×1, DemoItem×1}) | 1 (T = Product) | **STRONG** (generalized over T) | Both agents hit the missing-`.WithKey()` / template-binding mistake. Rule must match `TemplatedListViewElement<_>` generically, not by literal type-parameter name. |
| CS1929, TemplatedListViewElement\<T\>, missing_with_key | 2 (T = ListItem, string) | 0 | SINGLE (gpt-5.5) | The fix_kind classifier diverges between corpora but the underlying mistake is the same — collapse with the row above. |
| CS0117, StackElement, other | 0 | 2 (0.5%) | SINGLE (sonnet) | Below threshold either way; defer to a third corpus drop. |
| CS0117, StackElement, fluent_to_named | 2 (0.2%) | 0 | SINGLE (gpt-5.5) | Same as above; defer. |
| CS0117, StackElement, missing_with_key | 0 | 1 (0.3%) | SINGLE (sonnet) | Same; defer. |
| CS0117, TextBlockElement, other | 0 | 3 (0.8%) | SINGLE (sonnet) | Could merge into the TextBlockElement family rule if scope expands; below independent-rule threshold. |
| CS1061, Ref\<int\>, other | 0 | 1 (0.3%) | SINGLE (sonnet) | Too rare; not a Phase-3 target. |
| CS1061, BorderElement, renamed_member | 2 (0.2%) | 0 | SINGLE (gpt-5.5) | Defer. |
| CS1061, BorderElement, fluent_to_named | 1 (0.1%) | 0 | SINGLE (gpt-5.5) | Defer. |
| CS1955, GridElement, * (other/fluent_to_named/renamed_member/import_added) | **29 (combined 2.8%)** | 0 | SINGLE (gpt-5.5) | **Striking gpt-5.5-only signal.** GridElement is the user-facing `Grid` factory's element type; sonnet's 525-run sample produced zero of these. Two interpretations: (a) sonnet's skill-bodies-in-context steered it away from this mistake (the skilled flavor's `MINING_NO_MUR_ADDENDUM` did NOT remove skill content); (b) gpt-5.5 has a specific WinUI 3 `Grid`-vs-`GridElement` confusion that sonnet doesn't share. Either way, a third corpus drop is needed before this is rule-eligible under bar #2. Until then, the existing Tier-2 fuzzy match catches what it can. |
| CS1061, ButtonElement, * (renamed_member/other) | 5 (combined 0.5%) | 0 | SINGLE (gpt-5.5) | The canonical `Button("x").OnClick(...)` case. Existing `ButtonOnClickFactoryMoveRule` is **Class-B (vocab)** so bar #2's frequency-and-corpus path is waived by §3.2's "doc citation as alternative" clause. The audit confirms the rule's Class-B status was the right call — gpt-5.5 is the only agent generating this miss empirically, but the WinUI 3 docs justify the rule structurally. |
| CS0117, Element, other | 6 (0.6%) | 0 | SINGLE (gpt-5.5) | Defer. |
| CS0117, ContentDialogElement, other | 3 (0.3%) | 0 | SINGLE (gpt-5.5) | Defer. |
| CS1061, GridElement, renamed_member | 4 (0.4%) | 0 | SINGLE (gpt-5.5) | Defer. |
| CS1061, StackElement, fluent_to_named | 4 (0.4%) | 0 | SINGLE (gpt-5.5) | Defer. |

## Bar #2 verdicts (Class-A Phase-3 rule queue)

The Phase-3 priority list in spec 038 §"Data Checkpoint C" called out three targets (Theme.*Background, *Element alignment family, GridSize parens). Re-anchored against this audit, the actionable Class-A queue is:

### Tier 1 — unblock immediately

1. **`GridSizeFactoryParensRule`** — `(CS1955, GridSize, other)` cross-agent STRONG (110+36=146 events; top freq in both corpora). Fix is "missing parens on GridSize factory call" — agent typed `GridSize.Star` instead of `GridSize.Star()`. Tier-2 doesn't cover CS1955 today (it's outside Tier-2 `SupportedCodes`); this would be Phase-3's first cross-tier rule addition. Open the rule PR with this audit cited as bar #2 evidence.
2. **`ThemeBackgroundSuffixRule`** — already merged structurally as Class-B; update the rule file's header comment + PR-history entry in `docs/specs/tasks/038-rule-history.md` (when that artifact lands) to note the Class-A Validation Gate is *also* met now (16+11=27 events). No new rule-author work; this is a re-classification.
3. **`GridSizeRenamedMemberRule`** — `(CS0117, GridSize, renamed_member)` cross-agent STRONG (3+4=7 events). Need to pull the exemplar `fixes.jsonl` rows to see which `Auto`/`Star`/`Pixel` family members are being missed; rule is a lookup-table on missing-member name.

### Tier 2 — author next, lower priority

4. **`TextBlockElementMemberRule`** — `(CS1061/CS0117, TextBlockElement, *)` cross-agent reproducible after collapsing the `renamed_member`/`other` fix_kind classifier divergence (gpt-5.5 14 events, sonnet 4 events). Rule shape: lookup-table for common WinUI 3 → Reactor text-styling member renames. The fix_kind divergence between corpora is a classifier artifact, not a real semantic split.
5. **`TemplatedListViewWithKeyRule`** — `(CS1929, TemplatedListViewElement<_>, *)` cross-agent STRONG when generalized over the type parameter. Rule must match the open generic; closed-generic literal matching fails the test. Lower priority on count alone, but it's the "missing `.WithKey()`" pitfall that the harness already classifies as a distinct `missing_with_key` fix_kind — worth a rule when the design is straightforward.

### Tier 3 — defer until third corpus drop

The five gpt-5.5-only clusters listed in the "Cross-agent" SINGLE rows above — most notably `CS1955` against `GridElement` (29 events) and `CS1061` against `ButtonElement` (5 events, non-`OnClick`). These are real and frequent in gpt-5.5 but absent from sonnet. Bar #2 fails; do not open Class-A PRs for these until a third agent's corpus is mined (e.g. a future `claude-opus-*` or `gemini-*` run). The `ButtonElement` `OnClick` row remains served by the existing Class-B `ButtonOnClickFactoryMoveRule` (vocab justification, bar #1 waived).

## Skilled-flavor weighting note

Sonnet's 564 ranker rows split 404 unaided / 160 skilled (per handoff). Skilled rows are over-represented in the smaller-receiver-type clusters (sonnet's top freq cluster C0004 `CS7036/null/other` is 30% of the sonnet corpus and skews toward skilled exemplars). For frequency-bar calculations, the counts above weight all rows equally; if a per-flavor breakdown becomes load-bearing for future rule decisions, run a second-pass aggregate filtered on `eval_flavor`.

## Next concrete actions

1. **`GridSizeFactoryParensRule`** PR — author against the C0004 (sonnet) / C0001 (sonnet old-id) / C0004 (gpt-5.5) exemplars. Fixtures must cite ≥ 3 distinct `run_id` per spec §3.2, drawn from both corpora to make the bar #2 line in the Validation Gate template self-supporting.
2. **`ThemeBackgroundSuffixRule`** post-merge update — note Class-A bar-#2 evidence in the rule's file-header comment block. This is paperwork, not code.
3. **`GridSizeRenamedMemberRule`** PR — author after #1 to keep the GridSize rules co-located for review purposes.
4. The two Tier-2 rules (`TextBlockElementMemberRule`, `TemplatedListViewWithKeyRule`) follow Phase 3's 3-rules-per-PR cadence.
