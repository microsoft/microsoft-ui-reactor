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

# Skip costly phases for inner-loop iteration
mur docs compile --skip-screenshots --skip-diagrams

# Render diagrams only (fast Mermaid loop)
mur docs render-diagrams --topic architecture-overview

# Scaffold a new Mermaid diagram
mur docs new-diagram architecture-overview overview
```

## 5. Tier-lint diagnostic codes

The validator emits diagnostics from `mur docs compile --validate-only`
to stderr. Each is `<file>:<line> <CODE>: <message>` so editors can
parse them as build errors.

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

## 7. Tier audit cadence

Run a manual `mur docs compile --validate-only` audit every quarter
to catch silent tier drift. Owners: see CODEOWNERS for
`docs/_pipeline/`.
