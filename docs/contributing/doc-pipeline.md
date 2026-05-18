# Doc Pipeline — Contributor Guide

This page covers the tooling that powers `mur docs compile` and the
authoring conventions you need to follow when changing docs. It is the
single source for spec 041's Phase-1 install / setup decisions.

> **Heads-up:** `docs/guide/*.md` is generated output. Never hand-edit
> it. Edit `docs/_pipeline/templates/<topic>.md.dt` (and supporting
> doc apps, diagrams, manifests) and run `mur docs compile`.

## 1. Prerequisites

The doc pipeline needs:

| Tool             | Purpose                                | Required for                |
|------------------|----------------------------------------|-----------------------------|
| .NET 9 SDK       | Building the `mur` CLI + doc apps      | Always                      |
| Windows App SDK  | Doc apps render WinUI controls         | Screenshot capture          |
| Node.js 20+      | Hosts `mermaid-cli`                    | `.mmd` → `.svg` diagrams    |
| `mermaid-cli`    | CLI front-end for Mermaid              | `.mmd` → `.svg` diagrams    |
| Chromium / Edge  | Pulled in by Puppeteer for `mmdc`      | `.mmd` → `.svg` diagrams    |

Doc apps and screenshots work without Mermaid. Mermaid only enters
the pipeline when a topic has at least one `*.mmd` file in
`docs/_pipeline/diagrams/<topic>/`.

## 2. Installing `mermaid-cli` on Windows

### 2.1 Local dev box

```powershell
npm install -g @mermaid-js/mermaid-cli
```

Verify:

```powershell
mmdc --version
```

The first invocation downloads a Puppeteer-managed Chromium build
(roughly 170 MB). Subsequent invocations are cached.

Render a sample diagram end-to-end:

```powershell
mmdc -i sample.mmd -o sample.svg
```

### 2.2 GitHub Actions (`windows-latest`)

Add this step before `mur docs compile`:

```yaml
- name: Install mermaid-cli
  run: npm install -g @mermaid-js/mermaid-cli
  shell: pwsh

- name: Cache Puppeteer Chromium
  uses: actions/cache@v4
  with:
    path: ~/.cache/puppeteer
    key: puppeteer-${{ runner.os }}-${{ hashFiles('**/package-lock.json') }}
```

### 2.3 Measured cost (spec §12.1 Q1)

These numbers are from a cold `windows-latest` runner without any
cache, captured during the Phase-1 spike (May 2026):

| Step                              | Cost           | Notes                                  |
|-----------------------------------|----------------|----------------------------------------|
| `npm install -g @mermaid-js/mermaid-cli` | 30–60 s | Cold install; ≤5 s when cached         |
| First `mmdc` invocation (Chromium download) | 15–30 s | Cached as Puppeteer artifact            |
| Per-diagram render               | 1–2 s          | Stable for diagrams under ~50 nodes    |

These are within the spec's targets (≤45 s install + ≤2 s per
diagram) and acceptable for CI. Cache the npm install and the
Puppeteer Chromium directory to keep PR builds fast.

### 2.4 Decision

**Mermaid is supported.** Diagram sources live in
`docs/_pipeline/diagrams/<topic>/*.mmd`; the compiler renders them
to `docs/guide/images/<topic>/<name>.svg`.

**Fallback:** if Mermaid ever proves flaky on CI (Puppeteer Chromium
changes, npm registry outages, etc.), authors may hand-author SVG
in the same directory. The pipeline copies any `*.svg` it finds
through unchanged, so a hand-authored SVG is interchangeable with a
generated one from the template's perspective.

## 3. Pipeline directives quick reference

| Directive                                              | Where             | Effect                                                                  |
|--------------------------------------------------------|-------------------|-------------------------------------------------------------------------|
| `tier: stub \| solid \| comprehensive`                 | Front-matter      | Drives the tier-lint check; default `solid`                             |
| `winui-ref: <url>`                                     | Front-matter      | Emits a "WinUI reference" callout at the top of the page                |
| `snippet="<topic>/<id>"`                               | Body              | Inlines a snippet captured from a doc app                               |
| `snippet="source:<path>#<region>"`                     | Body              | Inlines a region from `src/<path>` between `// <snippet:region>` markers |
| `screenshot://<topic>/<id>`                            | Body              | Inlines a captured screenshot                                           |
| `<!-- ai:lock --> ... <!-- /ai:lock -->`               | Body              | Author-locked block; AI passes must preserve verbatim                   |
| `<!-- ai:caveat --> ... <!-- /ai:caveat -->`           | Body              | Caveat callout; renders as a "**Caveat:**"-led blockquote               |
| `<!-- ref:Member -->`                                  | Body (templates)  | Expands to a link to the matching reference page                        |

## 4. Running the pipeline

```powershell
# Full compile (build doc apps, capture screenshots, extract, assemble)
mur docs compile

# Lint only — fast, no doc-app build or screenshot capture
mur docs compile --validate-only

# Lint a single tier (e.g. while authoring Comprehensive pages)
mur docs compile --validate-only --tier=comprehensive

# Tier-lint only — narrower than --validate-only (no cross-link
# analyzer, no reference discovery). Best inner loop while iterating
# on a tier upgrade.
mur docs check-tier
mur docs check-tier --topic hooks
mur docs check-tier --tier solid --ci

# Skip costly phases for inner-loop iteration
mur docs compile --skip-screenshots --skip-diagrams

# Render diagrams only (fast Mermaid loop)
mur docs render-diagrams --topic architecture-overview

# Scaffold a new Mermaid diagram
mur docs new-diagram architecture-overview overview
```

## 5. Tier-lint diagnostic codes

The validator emits diagnostics from `mur docs compile --validate-only`
(and the narrower `mur docs check-tier`) to stderr. Each is
`<file>:<line> <CODE>: <message>` so editors can parse them as build
errors. `check-tier` runs only the §11 codes in the table below; it
does not run the cross-link analyzer (`REACTOR_DOC_XLINK_001`) or
reference-generation codes.

| Code                  | Meaning                                                | Severity |
|-----------------------|--------------------------------------------------------|----------|
| `REACTOR_DOC_TIER_001`| Missing title                                          | error    |
| `REACTOR_DOC_TIER_002`| Missing body paragraph                                 | error    |
| `REACTOR_DOC_TIER_003`| Fewer than 3 resolved `snippet=` references            | error    |
| `REACTOR_DOC_TIER_004`| No resolved `screenshot://` reference                  | error    |
| `REACTOR_DOC_TIER_005`| No reference table in first half                       | error    |
| `REACTOR_DOC_TIER_006`| Missing `## Tips` heading                              | error    |
| `REACTOR_DOC_TIER_007`| Missing `## Next Steps` heading or fewer than 3 links  | error    |
| `REACTOR_DOC_TIER_008`| No mental-model lead paragraph (≥80 words)             | error    |
| `REACTOR_DOC_TIER_009`| No `<!-- ai:caveat -->` block                          | error    |
| `REACTOR_DOC_TIER_010`| Missing `## Patterns` heading                          | error    |
| `REACTOR_DOC_TIER_011`| Missing `## Common Mistakes` heading                   | error    |
| `REACTOR_DOC_TIER_012`| Fewer than 5 inline cross-links                        | error    |
| `REACTOR_DOC_TIER_W001`| `winui-ref:` not declared on a transparent wrapper    | warning  |

## 6. Snippet / image / diagram error codes

| Code                       | Meaning                                                    |
|----------------------------|------------------------------------------------------------|
| `REACTOR_DOC_SNIPPET_001`  | Source file not found for `snippet="source:..."`           |
| `REACTOR_DOC_SNIPPET_002`  | Region marker not found in source file                     |
| `REACTOR_DOC_SNIPPET_003`  | Region opened without a matching close marker              |
| `REACTOR_DOC_SNIPPET_004`  | Nested region with same name as outer region               |
| `REACTOR_DOC_DIAGRAM_001`  | `mermaid-cli` not on PATH but the topic has `.mmd` files   |
| `REACTOR_DOC_IMAGE_001`    | `![..](images/<topic>/...)` reference resolves to nothing  |
| `REACTOR_DOC_REGISTRY_W001`| Registry rule maps to a category with no `guide-pages`     |
| `REACTOR_DOC_REGISTRY_W002`| Registry-declared guide page has no inbound `<!-- ref:Member -->` marker (doc-coverage gate, spec [041 §5.3](../specs/041-docs-comprehensive-uplift.md)) |

## 7. Quarterly tier audit (spec 041 §5.4)

The CI tier-drift gate (§8 below) catches *per-PR* drift — i.e. a PR
that touches a template body in a way that violates its declared tier.
It does not catch *silent* drift: a Comprehensive page whose
surrounding API changed under it, leaving its mental-model lead
correct in structure but stale in content; a Solid page whose
companion doc app stopped working months ago; a reference table that
no longer matches the current public surface.

That gap is closed by a **quarterly tier audit**.

**Cadence.** Once per quarter, on the first business day of the
quarter (i.e. early January, April, July, October).

**Owner.** The Reactor doc-pipeline owner — see `CODEOWNERS` for
`docs/_pipeline/` for the current name. The spec 041 §0 owner field
also tracks this role.

**Workflow.**

1. Run `mur docs compile --validate-only --ci` against a clean clone.
   Capture the full output. Errors block other audit work — fix them
   first.
2. Run `mur docs check-tier` once with `--tier comprehensive` and once
   with `--tier solid`. Read every finding — including W-level
   warnings the CI gate currently ignores (e.g. W001 winui-ref noise).
   Treat each as a small "should this still be at this tier?"
   question rather than a strict fail.
3. Pick a sample of 5–8 Comprehensive pages from the
   highest-traffic and the most-recently-changed surfaces and read
   them end-to-end. Look for stale references, missing newer hooks /
   controls / behaviors, drift in mental-model framing.
4. Re-rank any page where the audit changes your mind: drop a
   Comprehensive that no longer earns its keep down to Solid, promote
   a Solid that has organically grown to Comprehensive.
5. Record the audit pass — even a one-line entry — in a new
   `docs/specs/041/audits/<YYYY-Qn>-tier-audit.md` file with: pages
   inspected, findings, re-rankings applied, follow-ups deferred. The
   pattern matches the existing Phase 4 retro file shape.
6. Land any re-rankings or content fixes as their own PR(s) — do not
   bundle them with the audit-record commit.

**Findings disposition.** If an audit pass surfaces ≥5 stale pages
the owner should also schedule a focused doc-rev sprint within the
quarter. The expectation is that most quarters produce 0–2 findings;
runs producing more than that indicate the per-PR gate is too lax and
should be tightened (e.g. flip W001 to error after the lint-quality
cleanup, or expand the path filter in `.github/workflows/ci.yml`).

For inner-loop iteration during the audit, the local commands in §8
(below) are the same surface CI runs.

## 8. Tier-drift CI gate (spec 041 §5.2)

The `docs-check-tier` job in `.github/workflows/ci.yml` runs `mur docs
check-tier` on every PR that changes a file under any of:

- `docs/_pipeline/templates/` — page templates
- `docs/_pipeline/apps/` — doc apps backing snippets / screenshots
- `src/Reactor.Cli/Docs/` — the doc-pipeline CLI itself

It is intentionally narrower than the `docs-compile` job: no doc-app
build, no screenshot capture, no diagram rendering, no reference
generation, no cross-link analyzer. The job runs in seconds and exists
to fail PRs that knock a template's declared tier out of compliance
with its §11 structural checklist.

### Failure modes

- **`REACTOR_DOC_TIER_001..012` errors** fail the job. The fix is
  almost always to bring the template's body back into shape (add the
  missing heading, mental-model lead, snippet count, reference table,
  caveat block, etc.). See §5 above for the per-code meanings.
- **Tier-inflation attempt.** The lint blocks a `tier: comprehensive`
  declaration on a page that does not meet the Comprehensive bar. If
  the page is genuinely at Solid quality, lower the declared tier
  rather than disabling the lint.
- **Discovery error** (`REACTOR_DOC_TEMPLATE_001` or similar from
  `TemplateParser.Parse`). The front-matter is malformed; look at the
  file path in the error message and validate the YAML block at the
  top of the template.
- **`REACTOR_DOC_TIER_W001` warnings** (winui-ref not declared) do
  **not** fail the job today. They are intentional informational noise
  on internals / meta pages. The `--ci` flag would elevate them; that
  flag is held off pending the Phase 5 lint-quality cleanup that
  filters W001 to transparent-wrapper-page surfaces.
- **Job did not run** when expected. Confirm the PR actually changed
  a file under one of the watched paths above; the `changes` job emits
  `docs-templates=false` for branches that only touched unrelated files
  and the tier-drift job skips in that case.

### Running the same check locally

```powershell
# Same flags as CI:
mur docs check-tier

# Author iteration loop while fixing a finding:
mur docs check-tier --topic <name>

# Tier-targeted lint pass (e.g. while shepherding several Solid pages
# toward Comprehensive):
mur docs check-tier --tier solid
```
