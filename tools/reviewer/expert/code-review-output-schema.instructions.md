# Expert C# Code Review Output Schema

Defines the JSON structure produced by the expert C# code review orchestrator. Version 2.0.

**Rules**: Every review output validates against this schema. All required fields present. Enum fields use only defined values. All findings cite a real pattern from a loaded skill. No fabricated pattern references.

## Schema Version

- **Current**: `2.0`
- **Format**: `{pr_id}-{model_short}-cs-review-v{n}.json`

## Top-Level Structure

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schema_version` | string | yes | Schema version. Current: `"2.0"` |
| `model` | string | yes | LLM model identifier (e.g., `"claude-opus-4.6"`) |
| `pr_id` | string | yes | ADO pull request ID |
| `pr_url` | string | yes | Full ADO pull request URL |
| `repository` | string | yes | Repository name |
| `organization` | string | yes | ADO organization/project path |
| `pr_metadata` | object | yes | Pull request metadata |
| `reviewed_files` | string[] | yes | List of file paths reviewed |
| `executive_summary` | string | yes | Human-readable summary (see Executive Summary below) |
| `classification` | object | yes | Change classification output |
| `routing` | object | yes | Skill routing decisions |
| `findings` | array | yes | Review findings ordered by severity then priority (may be empty) |
| `risk_assessment` | object | yes | Composite risk assessment |
| `tool_escalation` | object | yes | Recommended external tool checks |
| `skill_performance` | object | yes | Per-skill metrics |
| `timing` | object | yes | Pipeline stage timing |
| `self_eval` | object | yes | Self-evaluation gate results |

## Object Definitions

### `pr_metadata`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `title` | string | yes | PR title |
| `author` | string | yes | PR author display name |
| `source_branch` | string | yes | Source branch name |
| `target_branch` | string | yes | Target branch name |

### `executive_summary`

A single string field in the JSON. This is the first thing a developer reads. It must convey exactly what happened in the review -- nothing more, nothing less.

**Writing rules:**
1. **First sentence**: State what was reviewed -- number of files, primary language (C#), change type
2. **Second sentence**: State whether issues were found -- count by severity (e.g., "Found 1 critical, 2 high, and 1 medium severity issue")
3. **Third sentence**: If issues were found, state the most important one in plain language
4. **Fourth sentence**: State the action required -- whether human review is needed, whether the PR is safe to merge, or what must be fixed first
5. **No adjectives**. No "comprehensive", "thorough", "robust". No marketing. No bragging about pattern counts or expert reviewers consulted.
6. **No hedging**. Don't say "may" or "might" when you mean "does" or "will". If uncertain, say "uncertain" and explain why.
7. **Plain language**. An average developer who has never seen this system should understand every word.

**Example:**
> Reviewed 6 C# files (ConnectionManager.cs, QueryBuilder.cs, UserService.cs, Startup.cs, Program.cs, appsettings.json) covering a new database access layer. Found 1 critical, 1 high, and 2 medium severity issues. The critical issue is SQL injection: QueryBuilder.cs constructs SQL via string interpolation with user input at line 47. Human review required before merge -- security boundary violation and missing IDisposable on connection wrapper.

### `classification`

Produced by Phase 1 (Classify).

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `language` | string | yes | Enum: `csharp`, `cpp`, `c`, `rust`, `python`, `typescript`, `go`, `mixed` | Primary language detected |
| `domains` | string[] | yes | Enum values (see Domain Enum below) | Detected review domains |
| `change_type` | string | yes | Enum: `new-feature`, `bugfix`, `refactor`, `perf-optimization`, `security-fix`, `dependency-update`, `test-only`, `docs-only`, `deletion-only` | Classified change type |
| `risk_score` | number | yes | Range: `0.0` - `1.0` | Composite risk score from classifier |

#### Domain Enum

The following domains are valid for C# code reviews:

| Domain | Description |
|--------|-------------|
| `memory-lifecycle` | `IDisposable` patterns, finalizers, `GCHandle`, `using` blocks, resource leaks, `WeakReference`, managed/unmanaged memory boundaries |
| `error-handling` | Exception handling patterns, `try/catch/finally`, null checks, `Result`-style patterns, error propagation, exception types |
| `concurrency` | `async`/`await`, `Task`, `lock`, `SemaphoreSlim`, `CancellationToken`, thread safety, `Parallel`, `SynchronizationContext`, deadlocks |
| `performance` | Hot path allocations, LINQ in tight loops, `StringBuilder` usage, `Span<T>`/`Memory<T>`, boxing, caching, `ArrayPool`, `stackalloc` |
| `security` | SQL injection, deserialization, input validation, cryptography, authentication, authorization, secrets management, CORS, CSRF |
| `api-design` | Public API surface, nullability annotations, XML docs, breaking changes, versioning, `[Obsolete]`, generic constraints, interface design |
| `ui-framework` | WPF/WinForms/MAUI threading, dispatcher access, data binding, dependency properties, visual tree, control lifecycle |
| `build-packaging` | `.csproj` configuration, NuGet packaging, `Directory.Build.props`, target framework, assembly attributes, source generators, analyzers |
| `test-infrastructure` | Test patterns, mocking, test fixtures, assertion quality, test isolation, code coverage, integration test setup |

### `routing`

Produced by Phase 2 (Route).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `path` | string | yes | Routing path. Enum: `deep`, `standard`, `fast` |
| `skills_applied` | string[] | yes | Skills that produced findings or ran analysis (cs-* prefixed) |
| `skills_not_applied` | array | yes | Array of `{ skill: string, reason: string }` -- skipped skills with justification |
| `total_skills_available` | integer | yes | Total skills detected |
| `total_skills_applied` | integer | yes | Count of skills that ran |
| `total_skills_skipped` | integer | yes | Count of skills skipped |
| `routing_rationale` | string | yes | Free-text explanation of routing decisions |

**Invariant**: `total_skills_applied + total_skills_skipped = total_skills_available`

**Skill naming convention**: All C# expert review skills use the `cs-` prefix (e.g., `cs-memory-lifecycle`, `cs-error-handling`, `cs-concurrency`, `cs-performance`, `cs-security`, `cs-api-design`, `cs-ui-framework`, `cs-build-packaging`, `cs-test-infrastructure`).

### `skill_performance`

A map of skill name -> performance metrics. Each entry:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `patterns_checked` | integer | yes | Total patterns evaluated by this skill |
| `patterns_matched` | integer | yes | Patterns that matched code in the diff |
| `findings_produced` | integer | yes | Findings emitted after match filtering |
| `findings_suppressed` | integer | yes | Findings matched but suppressed (dedup, low-confidence) |
| `confidence_avg` | number \| null | yes | Average confidence of produced findings. `null` when `findings_produced = 0` |

**Invariant**: `patterns_matched >= findings_produced + findings_suppressed`
**Invariant**: `confidence_avg` is `null` if and only if `findings_produced = 0`

### `findings[]`

Each finding in the array. Ordered by severity (S1 first), then priority (P0 first), then by file and line number.

#### Severity -- aligned with ADO `Microsoft.VSTS.Common.Severity`

Severity categorizes the **impact** of the issue.

| Value | ADO Label | Definition |
|-------|-----------|------------|
| `1` | **S1 -- Critical** | Process crash, data loss, resource corruption, or exploitable security vulnerability |
| `2` | **S2 -- High** | Major functionality broken or severe correctness bug with no workaround |
| `3` | **S3 -- Medium** | Functionality impaired but workaround exists, or design issue that will compound |
| `4` | **S4 -- Low** | Minor issue, cosmetic, or edge case with negligible user impact |

#### Priority -- aligned with ADO `Microsoft.VSTS.Common.Priority`

Priority categorizes the **urgency** -- when must this be fixed.

| Value | ADO Label | Definition |
|-------|-----------|------------|
| `0` | **P0 -- Must fix immediately** | Blocking with no workaround. PR must not merge until resolved. |
| `1` | **P1 -- Must fix before merge** | Blocking some scenarios. Should be resolved in this PR. |
| `2` | **P2 -- Should fix before release** | Not blocking merge but will cause problems. Fix in a follow-up. |
| `3` | **P3 -- Fix if time** | Low impact. At author's discretion. |

#### Finding Fields

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `id` | string | yes | Format: `F{n}` (sequential) | Unique finding identifier |
| `severity` | integer | yes | Enum: `1`, `2`, `3`, `4` | Impact level (S1-S4, see table above) |
| `priority` | integer | yes | Enum: `0`, `1`, `2`, `3` | Fix urgency (P0-P3, see table above) |
| `introduced_by_pr` | boolean | yes | -- | `true` = this PR introduced the issue. `false` = preexisting issue in code the PR touches. |
| `confidence_level` | string | yes | Enum: `high`, `medium`, `low` | Evidence grade -- how completely the cited pattern materialized |
| `confidence_justification` | string | yes | Non-empty | Explains the confidence level -- must cite visible code evidence |
| `domain` | string | yes | Must be a value from `classification.domains` | Domain this finding belongs to |
| `skill` | string | yes | Must be a value from `routing.skills_applied` | Skill that produced this finding |
| `pattern_cited` | string | yes | Non-empty | Pattern name or ID from the skill's catalog |
| `file` | string | yes | Must be in `reviewed_files` | File path where finding applies |
| `line_range` | integer[2] | yes | `[start, end]`, 1-indexed, `start <= end` | Line range in the file |
| `symbol` | string | no | -- | Class, method, property, or field name where the issue occurs (for quick navigation) |
| `problem` | string | yes | Non-empty | What is wrong -- plain statement of the defect |
| `location_detail` | string | yes | Non-empty | Where exactly -- describe the code path, the specific lines, what the code is doing at that point |
| `solution` | string | yes | Non-empty | How to fix it -- concrete, actionable fix guidance a dev can implement |

**Cross-references**:
- `finding.domain` must appear in `classification.domains`
- `finding.skill` must appear in `routing.skills_applied`
- `finding.file` must appear in `reviewed_files`
- `skill_performance[finding.skill].findings_produced` must equal the count of findings with that skill

### `risk_assessment`

Produced by Phase 4 (Synthesize). The composite risk scoring methodology and HITL decision matrix are defined in the risk assessor [3].

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `overall_risk` | string | yes | Enum: `critical`, `high`, `medium`, `low` | Risk classification label [3] |
| `risk_score` | number | yes | Range: `0.0` - `1.0` | Numeric risk score [3] |
| `human_review_recommended` | boolean | yes | -- | Whether HITL review is recommended [3] |
| `confidence` | number | yes | Range: `0.0` - `1.0` | Confidence in the risk assessment itself |
| `hitl_reasons` | string[] | yes | Non-empty when `human_review_recommended = true` | Reasons HITL is recommended [3] |
| `rationale` | string | yes | Non-empty | Free-text risk assessment rationale |

**Risk label mapping** [3]:
- `low`: risk_score < 0.3
- `medium`: risk_score >= 0.3 and < 0.6
- `high`: risk_score >= 0.6 and < 0.85
- `critical`: risk_score >= 0.85

**Invariant**: When `human_review_recommended = true`, `hitl_reasons` must contain at least one entry

### `tool_escalation`

Captures recommendations for external verification tools beyond LLM-based review. Common escalation targets for C# include Roslyn analyzers, .NET security scanners, and runtime verification tools.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `recommended_checks` | string[] | yes | External tools recommended (e.g., `"Roslyn security analyzers"`, `"BinSkim for assembly hardening"`, `"dotnet-counters for resource leak verification"`) |
| `tier` | string | yes | Enum: `none`, `advisory`, `escalation` |

### `timing`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `total_seconds` | number | yes | Total pipeline wall-clock time |
| `stages` | object | yes | Per-stage timing breakdown |
| `diff_fetch_seconds` | number | yes | Time to fetch diff content |
| `file_context_fetch_seconds` | number | yes | Time to fetch surrounding file context |

**`stages` fields** (all required, in seconds):

| Field | Description |
|-------|-------------|
| `classify` | Phase 1: language + domain classification [3] |
| `route` | Phase 2: skill routing decisions [1] |
| `analyze` | Phase 3: expert skill analysis (dominant stage) [1] |
| `synthesize` | Phase 4: findings aggregation + risk assessment [3] |

### `self_eval`

The self-evaluation gate runs 10 checks to validate internal consistency before emitting the review. All checks must pass.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `passed` | boolean | yes | Whether all self-evaluation checks passed |
| `checks_run` | integer | yes | Total self-eval checks executed |
| `checks_passed` | integer | yes | Checks that passed |
| `checks_failed` | integer | yes | Checks that failed |
| `errors` | string[] | yes | Error descriptions for failed checks (empty when all pass) |

**Invariant**: `checks_passed + checks_failed = checks_run`
**Invariant**: `passed = true` if and only if `checks_failed = 0`

#### Self-Evaluation Checks (10-Check Gate)

1. **All required fields present** -- no `undefined` or missing keys in the output JSON
2. **Enum compliance** -- categorical fields (`language`, `domains`, `change_type`, `severity`, `priority`, `confidence_level`, `overall_risk`, `path`, `tier`) use only defined enum values
3. **Range compliance** -- numeric scores within `[0.0, 1.0]`; line numbers are positive integers; severity in `{1,2,3,4}`; priority in `{0,1,2,3}`
4. **Cross-reference integrity** -- every `finding.domain` appears in `classification.domains`; every `finding.skill` appears in `routing.skills_applied`; every `finding.file` appears in `reviewed_files`
5. **Skill performance invariants** -- `patterns_matched >= findings_produced + findings_suppressed`; `confidence_avg` is `null` iff `findings_produced = 0`; `skill_performance[skill].findings_produced` equals the count of findings citing that skill
6. **Routing invariant** -- `total_skills_applied + total_skills_skipped = total_skills_available`
7. **Risk-label consistency** -- `overall_risk` matches `risk_score` per the label mapping (`low` < 0.3, `medium` 0.3-0.6, `high` 0.6-0.85, `critical` >= 0.85)
8. **HITL reason presence** -- when `human_review_recommended = true`, `hitl_reasons` contains at least one entry
9. **Finding sequential IDs** -- findings are numbered `F1`, `F2`, ... with no gaps; ordered by severity (S1 first), then priority (P0 first)
10. **Self-eval consistency** -- `checks_passed + checks_failed = checks_run`; `passed = true` iff `checks_failed = 0`

## Validation Rules

When producing or consuming a review output file, validate all 10 self-evaluation checks above. Additionally:

- **Confidence-level validity** -- `confidence_level` uses only defined enum values (`high`, `medium`, `low`)
- **Pattern citation** -- every finding's `pattern_cited` references a real pattern from the skill's catalog; no fabricated references
- **Executive summary compliance** -- follows the 7 writing rules defined above
- **Language field** -- for C# reviews, `classification.language` must be `csharp` (or `mixed` if the PR contains non-C# files that were also reviewed)

## References

[1] *Expert C# Code Review Orchestrator* (`@expert-cs-code-review-orchestrator`, local artifact). Path: [`agents/expert-cs/expert-cs-code-review-orchestrator.agent.md`](./expert-cs-code-review-orchestrator.agent.md). Defines the orchestrator pipeline, skill routing, and review depth selection.

[2] *Change Classification Instructions* (`@change-classifier`, local artifact). Path: [`agents/expert-cs/classifier/change-classifier.instructions.md`](./classifier/change-classifier.instructions.md). Defines language detection, domain signal weights, change type classification, and risk score computation.

[3] *Risk Assessment Instructions* (`@risk-assessor`, local artifact). Path: [`agents/expert-cs/risk-assessor/risk-assessor.instructions.md`](./risk-assessor/risk-assessor.instructions.md). Defines five risk dimensions, composite scoring formula, HITL decision matrix, and risk label thresholds.

[4] OASIS, "Static Analysis Results Interchange Format (SARIF) Version 2.1.0," March 2020. Available: [https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html](https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html)

[5] MITRE, "Common Weakness Enumeration (CWE)." Available: [https://cwe.mitre.org/](https://cwe.mitre.org/)

## TODO

- **JSON Schema formal definition**: Generate a machine-readable JSON Schema (`$schema: draft-2020-12`) from this document for automated validation in CI
- **SARIF export**: Add optional SARIF 2.1.0 [4] export from findings for integration with Azure DevOps Advanced Security
- **Vocabulary tracking**: Register `noun:finding`, `noun:skill-performance`, `noun:tool-escalation` in dictionary
