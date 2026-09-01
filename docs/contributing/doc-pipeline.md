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
| .NET 10 SDK      | Building the `mur` CLI + doc apps      | Always                      |
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
| `{{reactorVersion}}`                                   | Body              | Substituted with the pinned public package version — single source `<ReactorPublicVersion>` in root `Directory.Build.props` |

> **Version substitution.** Never hardcode the public package version
> (e.g. `0.1.0-preview.11`) in a guide template. Write the `{{reactorVersion}}`
> token instead; `mur docs compile` replaces it with `<ReactorPublicVersion>`
> read from the root `Directory.Build.props` (via `VersionSource`, a committed
> file read — never a live NuGet lookup, so the output stays deterministic and
> the CI freshness gate can't false-fail when a new version publishes). That
> property is the one place a release bumps the version; the templates csproj
> derives its `MicrosoftUIReactorVersion` fallback from the same property, so
> the docs and the scaffolded template share a single literal. `README.md` is
> deliberately version-agnostic (it names no version and links to NuGet /
> Releases) and is **not** touched by `mur docs compile`.

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

### Which `Reactor.xml` the reference phase reads

Phase 5.7 generates `docs/guide/reference/**` from the XML doc comments the C#
compiler emits, so it needs a *built* `src/Reactor`. It reads the **most recently
written** `Reactor.xml` anywhere under `src/Reactor/bin` — configuration plays no
part in the choice, and neither does the directory layout, so flat
`bin/<config>/<tfm>/`, platform-stamped `bin/<arch>/<config>/<tfm>/` and
RID-nested publish outputs are all candidates. The one thing it won't descend
into is a junction or symlink *nested* inside `bin`: what's behind one isn't this
build's output, and a link pointing back at an ancestor would loop. Redirecting
`bin` itself is fine — the exclusion applies to what the walk finds, not to where
it starts.

It prints what it picked:

```
═══ Phase 5.7: Reference ═══
  XML: src/Reactor/bin/x64/Release/net10.0-windows10.0.22621.0/Reactor.xml (2026-08-01T19:45:14Z, newest of 3 candidate(s))
```

Read that line if a regenerated reference page disagrees with the source you just
edited. Until [issue #1068][i1068] this phase took the first hit of a fixed
`Debug`-then-`Release` sweep, so a months-old Debug build quietly beat a
just-built Release one and the generator rewrote pages from it — reintroducing a
sentence the commit had deleted — while exiting 0.

Selecting the newest build does not make the newest build *fresh*. If you edit a
`<summary>` and don't rebuild, every candidate predates your source and the
regenerated page is wrong in exactly the same way. That case is caught
separately: the phase compares the chosen XML against the newest `.cs` under
`src/Reactor` (ignoring `bin`/`obj`, which the build writes itself) and warns
with [`REACTOR_DOC_REFGEN_W002`](#6-snippet--image--diagram-error-codes) when the
source is newer. Run `dotnet build src/Reactor` and compile again.

If no `Reactor.xml` exists at all, **a local compile** skips reference generation
rather than failing, printing:

```
  (Reactor.xml not found — run `dotnet build src/Reactor` first)
```

That degradation is deliberate: on a first compile you should still get your
guide pages. Under `--ci` the same condition **exits 1** instead. CI always
builds, so a missing input there means the run is not the run that was asked
for, and skipping the phase would leave the ~117 pages under
`docs/guide/reference/` silently at whatever was committed. The same applies to
a missing `reference-map.yaml`. `ReferenceStalenessWiringTests` pins both
directions.

CI does build it. The `docs-build` job runs `docs compile --no-screenshots --ci`
*without* `--no-build`, so Phase 2 builds every doc app, each of which
`ProjectReference`s `src/Reactor/Reactor.csproj` — reference generation therefore
runs for real on every PR. `REACTOR_DOC_REFGEN_W002` still stays quiet there, for
an ordering reason rather than a missing-build one: `actions/checkout` writes the
sources before that build runs, so the emitted XML always postdates every `.cs`.

That is no longer left as a property of how the job happens to be spelled. The
freshness gate ([§10](#10-compiled-output-freshness-gate)) reads a clean
`git status -- docs/guide` as proof the committed output matches a fresh
compile, and that reading is only sound if every page was actually written — so
the non-zero exit above is what the gate stands on.

[i1068]: https://github.com/microsoft/microsoft-ui-reactor/issues/1068

### Screenshots and committed images

Phase 3 (capture) is the **only** phase that writes a screenshot — the only
binary writer in the pipeline. `--skip-screenshots` (alias `--no-screenshots`)
skips it outright, so a compile with that flag leaves every committed screenshot
byte-identical — the CI `docs-build` job proves this on every PR with
`git status --porcelain -- docs/guide/images` immediately after the compile.

#### Capture at 150% display scaling

Screenshots are captured **only on a contributor's machine** — every workflow
runs `--no-screenshots`, and the `docs-build` gate asserts capture never writes
in CI. So the image dimensions committed to this repo are a property of whoever
last ran capture, not of a build server.

Capture at **150%** display scaling. A doc app's `doc-manifest.yaml` declares a
window size in *logical* pixels, so a captured PNG scales roughly with the
display scale factor — but not by an exact multiple. Most manifests use
`region: client`, which captures the client area only and so excludes the
window frame, and the window manager may adjust the requested extent. Treat the
scale as the thing to match and the pixel dimensions as an observed
consequence, not a formula to validate against: `v1-protocol` declares
`width: 520`, and its committed `led-indicator.png` measures 640px wide when
captured at 125% and 766px at 150%.

The practical check is comparative, not arithmetic — if your regenerated image
is close in size to the one you replaced, you captured at the same scale as the
last contributor; if it jumped by ~20%, you did not.

That number is a convention, not a law of the pipeline — it was chosen because
it is what most of the corpus already used and it renders sharply on modern
displays. What matters is that it is *written down*: before this section existed
the corpus silently mixed scales (`docking` and `v1-protocol` at 125%,
`win2d-canvas` at 150%), because nothing told anyone what to use. If you
regenerate a page's images at a different scale, that page becomes inconsistent
with the rest of the docset for no reason a reader can see.

Check your scale before capturing (Settings → System → Display → Scale), and
regenerate a topic with:

```powershell
# one topic
dotnet run --project src/Reactor.Cli -- docs compile --topic layout

# one image (the ref must belong to --topic, or omit --topic entirely)
dotnet run --project src/Reactor.Cli -- docs compile --screenshots layout/card
```

Capture needs an **interactive desktop** — it launches each doc app and
screenshots its window. Over RDP with no console session, or on a locked
machine, Phase 3 reports `N failed screenshot capture(s)` and leaves the
existing images untouched rather than writing blanks.

It is *not* the only phase that writes under `docs/guide/images/`, and the
distinction matters if you are reasoning about that directory rather than about
screenshots. Phase 5.5 (diagrams) writes there **three** ways: it copies
`docs/_pipeline/diagrams/<topic>/*.svg` into `docs/guide/images/<topic>/`, it
renders each `<name>.mmd` to `<name>.svg` in that same directory via `mmdc`, and
it writes a `.<name>.mmd.sha256` cache sidecar beside it. All three are text, all
three have filenames disjoint from any captured `.png`, and none is skipped by
`--no-screenshots` (use `--skip-diagrams` for that). So the guarantee is about
screenshots, not about the directory. **The CI gate above is deliberately broader
than the guarantee** — it watches the whole directory, so a diagram write on the
skip path fails the build rather than being assumed away. Do not narrow the gate
to `*.png` to "match" this paragraph.

The mermaid render is the one to remember, because it is the one a source-level
audit misses: `mmdc` is a separate process, so that write appears in **no**
`File.Write*`/`File.Copy` search of this repo. The first version of this
paragraph enumerated only the two managed writes for exactly that reason. What
keeps the rendered file off the `.png` namespace is that its `.svg` destination
is hard-coded at the call site in `DiagramProcessor` — it is not a property of
`mmdc`, which will happily render PNG when asked. `DiagramTests` pins the full
written set for that reason, so adding a fourth writer fails a test that names
this paragraph rather than silently outdating it.

`git status`, not `git diff`: `git diff` reports tracked modifications only, so
it is blind to a *new* PNG appearing under `docs/guide/images/`. That is the
shape the near-miss in [issue #989][i989] actually took, because `git add -A`
stages precisely the untracked files `git diff` never reports.

One check, not two — `git status --porcelain` **subsumes** `git diff --exit-code`
rather than complementing it. Against a tracked modification it reports
` M docs/guide/images/…`; against a new file it reports `?? docs/guide/images/…`;
`git diff` catches the first and exits 0 on the second. So pairing them adds a
second failure path and no coverage. The first version of this gate ran only
`git diff` while the comment above it claimed *nothing may write a PNG* — the
narrower half of a two-part claim, which is why the wording here is deliberately
specific about which writes are caught.

Capture itself needs an **interactive desktop**. It launches each doc app,
waits for the preview capture server, and reads real frames over HTTP. In a
headless, locked, or RDP-disconnected session the app window never paints and
the capture server returns a solid-white surface. Historically that surface was
written straight over the committed screenshot as a ~3 KB white rectangle, and
the compile still exited 0 ([issue #989][i989]). Several guards now prevent that:

- `ImageProcessor` raises `REACTOR_DOC_SHOT_001` for a contentless frame
  **before** anything opens the output file, so the existing image is left
  untouched and the failure is counted. "Contentless" is judged after
  compositing against white, because an unrendered composition surface arrives
  as transparent black — visually identical to the white stub once written, but
  invisible to a naive RGB threshold.
- A non-zero capture-failure count fails the compile **regardless of `--ci`**.
  It describes an action the run just took: it was asked to refresh *N*
  screenshots and refreshed fewer. Validation findings stay `--ci`-gated,
  because they report pre-existing tree state an author may be part-way through
  fixing.
- Every compile re-checks the committed corpus and raises
  `REACTOR_DOC_IMAGE_002` for any referenced screenshot whose interior is
  blank, so a stub that reaches the tree from any source is caught on the next
  compile rather than at review time. Full-size captures are scored with the
  border/shadow chrome excluded; catalog thumbs (`<id>-thumb.<ext>`) carry no
  chrome and are scored whole.
- "Blank" means either *no pixel darker than near-white* or *one flat fill of
  any colour*. The second clause matters because the first only recognises a
  white stub: a themed window that painted its background but never its content
  comes back uniformly dark, every pixel counts as content, and without the
  uniformity test it would overwrite a committed screenshot and exit 0.
  Uniformity rather than a minimum content-coverage ratio — the sparsest
  interior in the committed corpus is 0.6084 % content pixels, so any coverage
  floor able to catch a stub sits close enough to real assets to condemn them,
  while no genuine screenshot is a single colour.
- **Both clauses judge the composited pixel, through one shared function.**
  They previously did not: the darkness test composited against white while the
  uniformity test compared stored BGRA bytes. A frame reaching one flat colour
  through mixed alpha — opaque white beside half-transparent white, which is
  what a partially-composed surface produces — then read as *varied*, scored as
  content, and would have been written over a committed screenshot. The two
  predicates ask different questions but both ask them about the *visible*
  pixel, so a second copy of that arithmetic is a second definition of "visible"
  that can drift, and the drift fails open.
- The capture poller waits for a frame using **that same definition**, not a
  looser one. It has to: the poller decides when to stop waiting, so if it
  accepted a frame the processor then rejects, capture would fail on the first
  poll rather than waiting out a deadline that would have produced the real
  frame. The uniformly-dark case is the only one that separates the two
  predicates — a white or transparent frame fails the content scan on its own —
  which is why it is pinned by a test rather than left to the reader.
- Every guard above asks whether the frame was **painted**, never whether it is
  the frame that was **requested**. Nothing downstream can tell a correct
  screenshot of the wrong control from a correct one — it has real content, so
  it passes cleanly. That gap is closed at the only point it can be: the
  `POST /preview` component-switch body is JSON-*encoded* rather than
  interpolated, so a manifest-authored component name is a value in that
  request and cannot become part of its structure. It used to be able to: a
  name shaped like `A", "component": "B` produced valid JSON with a duplicate
  key, and `System.Text.Json` takes the last one — so the capture switched to
  `B` while the manifest read as naming neither.
- **Both** requests the capture client makes carry a token cancelled by their own
  deadline, not just the frame poll. An unbounded `HttpClient` call falls back to
  a 100-second default, and the loop condition around it is only tested between
  iterations — so a server that accepts the connection and never answers is not
  bounded by the deadline the code declares. The component switch matters more
  than the poll here rather than less, because it runs *once per screenshot*:
  removing its token and stalling the server holds a single switch for **100.03 s
  against a 0.5 s timeout**, with the connection proven accepted rather than
  refused. The failure direction was always safe — a cancelled request is counted
  in `Failed`, never written — but the symptom of the unbounded form is a capture
  pass that looks like it is working for hours instead of failing in seconds,
  which is the same "no output is not evidence of no problem" shape as the rest of
  this list.
- A topic or screenshot id containing `:` is refused before it is joined to any
  output root, and so is a `:` in a markdown image reference. Containment alone
  does not cover it: on Windows `:` is a stream
  and drive separator, so `<images>/topic:hidden` resolves to a path that
  genuinely *is* under the images root — the containment check passes — while
  the write lands in the alternate data stream `hidden` on a file named
  `topic`. The directory then lists one `topic` of length 0 and the real bytes
  are invisible to a listing, to `git`, and to any size check. Same shape as
  everything else on this list: a guard that runs, returns a correct answer,
  and answers the wrong question. None of the 194 committed `screenshot://`
  references contain a `:`, so this rejects nothing that exists today.
  The read side needs the same rule for the mirrored reason: `File.Exists`
  answers **true** for an existing stream, so the blank gate would decode the
  stream's bytes and pass a page whose rendered image is blank. Both sides call
  one predicate — `DocPaths.HasStreamOrDriveSeparator` — because for a while
  only the write side implemented the rule while the read side asserted in a
  comment that it was handled there, at a call site that never called it.
- A referenced image that clears the pre-decode caps but that the decoder
  cannot read is reported as `REACTOR_DOC_IMAGE_003` rather than being scored.
  The distinction is load-bearing: "could not decode" is not "not blank", and
  spelling them the same way is what let a corrupt file pass the gate silently.
  Note that a *truncated* PNG — the shape an interrupted capture leaves behind —
  decodes rather than faulting, so it surfaces as `REACTOR_DOC_IMAGE_002`; the
  `_003` path covers corruption that defeats the decoder outright. It also
  covers a file that is intact but *unreadable at that moment* — locked by
  another process, or permission-denied — because those raise `IOException` /
  `UnauthorizedAccessException` from the same call and letting them escape would
  kill the whole compile over one file handle. The gate cannot distinguish the
  two, so the finding names both remedies rather than asserting corruption.
- `_003` also covers a file that never reaches the decoder because it is not an
  image at all: zero bytes, or bytes carrying no PNG/JPEG signature under a
  `.png`/`.jpg` name. Both were once *skipped*, which meant an HTML error page,
  a mislabelled SVG, or a Git-LFS pointer saved as `.png` produced **zero
  findings**. The likely way to hit it is a checkout of an LFS-tracked repo made
  without LFS: every image is a short text pointer, and the whole corpus passed
  clean. The two guards that remain skips are the *caps* — an over-cap or
  missing file — because those are decisions the gate makes about how much work
  to do, not statements about the file. A missing file is already
  `REACTOR_DOC_IMAGE_001`'s business.
  This is also why the magic-bytes pre-check does *not* swallow those two
  exceptions: it answers "is this a raster?" from the file's content, and a read
  it could not perform is not an answer. It used to return `false` for them,
  which the caller reads as "not a raster" and skips — so a locked file produced
  a clean compile while this paragraph claimed otherwise. A gate that skips
  analysis is a gate that passes.
- The `-thumb` suffix is therefore **reserved**. A manifest entry whose `id`
  ends in it without `kind: catalog-thumb` is rejected with
  `REACTOR_DOC_SHOT_002` — otherwise a full-size screenshot could claim the
  chrome-free scoring rule and hide a blank capture behind its own border.
  The reservation deliberately **exempts** `kind: catalog-thumb`, since there the
  suffix is correct rather than a collision — which is exactly why appending it
  is done once, by `ImageProcessor.ThumbAwareFileBase`, and is idempotent. An id
  of `widget-thumb` with `kind: catalog-thumb` otherwise produced
  `widget-thumb-thumb.png`, and because the filename and the generated URL were
  derived by two separate copies of the append they *agreed*, so links resolved
  and no gate fired. One function, because the two callers must never diverge:
  `ScreenshotCapture` chooses the file that is written and `DocAssembler` chooses
  the URL that points at it, and a divergence surfaces as a broken image rather
  than a compile error.
- The reservation reads the **whole id**, via `ImageProcessor.IdHasThumbSuffix`.
  This is not incidental: the sibling `HasThumbSuffix` is a *path* predicate that
  strips from the last dot, and calling it with an id — which has no extension —
  silently changed the subject. A dotted id such as `widget.v2-thumb` was read as
  `widget`, passed the reservation, and still produced `widget.v2-thumb.png`,
  which the gate then *did* score as a thumb. The two ends disagreed about one
  screenshot, which is precisely the collision this rule claims to make
  unrepresentable. One suffix rule, plus one adapter that adds filename
  extraction for the case where the subject really is a path.
- Image-reference validation runs on the *assembled* page, where `DocAssembler`
  prefixes one `../` per level of topic nesting. References resolve **relative
  to the page**, so nested topics (`recipes/*`) are checked like flat ones — and
  a `../` run that doesn't match the page's own depth now lands outside
  `docs/guide/images/` and is reported as `REACTOR_DOC_IMAGE_001` instead of
  being silently accepted.

If you are staging a docs change by hand, stage the specific `.md` / `.md.dt`
paths rather than `git add -A`, and check `git status` for unexpected entries
under `docs/guide/images/` before committing.

[i989]: https://github.com/microsoft/microsoft-ui-reactor/issues/989

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
| `REACTOR_DOC_IMAGE_001`    | `![..](images/<topic>/...)` doesn't resolve to a file inside `docs/guide/images/` — missing file, a `../` run that doesn't match the page's depth, a `:` in the reference (on Windows that names a drive or an alternate data stream, so the bytes read would not be the file the page appears to reference), or reference text that isn't a usable path at all |
| `REACTOR_DOC_IMAGE_002`    | Referenced screenshot exists but its interior is blank — a failed capture overwrote it. Restore from git and re-capture on an interactive desktop |
| `REACTOR_DOC_IMAGE_003`    | Referenced image cannot be scored as an image. Either it is not one — zero bytes, or no PNG/JPEG signature under a `.png`/`.jpg` name (a Git-LFS pointer, an HTML error page, a mislabelled SVG) — or it is corrupt and will not render (restore from git and re-capture), or it is intact but locked / permission-denied (clear that and re-run) |
| `REACTOR_DOC_IMAGE_004`    | *(warning — does **not** fail `--ci`)* The blank-image gate could not run: `System.Drawing.Common` is Windows-only, so on any other platform there is no decoder. Emitted **once per page** — the condition is a property of the machine, not of any image, so one finding per screenshot would bury it. Nothing else is suppressed: reference validation (`_001`) and the decoder-free checks behind `_003` (zero-byte file, no PNG/JPEG signature) still run everywhere. Never fires on the supported configuration. It is the one non-fatal finding this gate emits, and deliberately so: it says the scan *could not run*, not that an image is bad, and it fires precisely on the platform that cannot decode — so breaking the build on it would fail a docs compile over a missing codec while nothing is wrong with the docs |
| `REACTOR_DOC_SHOT_001`     | Captured frame was contentless; nothing was written and the existing screenshot was left untouched. The message names which clause fired — *no pixel below the threshold* or *one flat colour* — because for a dark fill the first wording states the opposite of what happened |
| `REACTOR_DOC_SHOT_002`     | Manifest screenshot id ends in the reserved `-thumb` suffix without `kind: catalog-thumb`. Matched against the whole id, so a dotted id (`widget.v2-thumb`) is caught too |
| `REACTOR_DOC_REGISTRY_W001`| Registry rule maps to a category with no `guide-pages`     |
| `REACTOR_DOC_REGISTRY_W002`| Registry-declared guide page has no inbound `<!-- ref:Member -->` marker (doc-coverage gate, spec [041 §5.3](../specs/041-docs-comprehensive-uplift.md)) |
| `REACTOR_DOC_REFGEN_W002`  | *(warning — does **not** fail `--ci`)* The `Reactor.xml` the reference phase selected is older than the newest `.cs` under `src/Reactor`, so `docs/guide/reference/**` is being generated from a build that predates the source it documents — an edited `<summary>` will not appear and a deleted one comes back. Run `dotnet build src/Reactor` and compile again. Warning, not error, because it reports that the *input* may be stale rather than a defect in the tree. It does not fire in CI: the docs job builds `src/Reactor` (via Phase 2's doc-app builds) *after* checkout has written the sources, so the XML always postdates them. See [§4](#which-reactorxml-the-reference-phase-reads) |

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

## 9. Doc-snippet analyzer gate

Every `snippet=` block in `docs/guide/` is extracted verbatim from a doc app
under `docs/_pipeline/apps/`. A doc app is therefore not a scratch project —
it is the code the guides tell readers to write, and it is held to the same
analyzer rules those readers get from the NuGet package.

Two gaps used to hide that:

1. Only `win2d-canvas` was listed in `Reactor.slnx`, so CI never built the
   other 52 doc apps at all.
2. A `ProjectReference` to `src/Reactor` does **not** flow `Reactor.Analyzers`
   — it ships as a packed `<None Pack="true">` item — so even that one app
   compiled without the rules its own readers are subject to.

`docs/_pipeline/apps/Directory.Build.props` now wires the consumer analyzer
bundle into every doc app, and the `docs-snippet-gate` job in
`.github/workflows/ci.yml` builds them all through
`docs/_pipeline/apps/DocApps.proj`. Any `REACTOR_*` diagnostic fails the job
and is echoed as a GitHub annotation with its `file:line`.

Rules that fire most often here, and what they mean for a snippet:

| Rule | Why it matters in a doc app |
|------|-----------------------------|
| `REACTOR_THEME_001/004` | A hard-coded colour ignores the reader's theme. Use a `Theme` token. |
| `REACTOR_MOD_003` | The receiver silently drops the modifier — the snippet does not do what the prose says. |
| `REACTOR_A11Y_001/002/003/004` | The sample teaches an inaccessible control. |
| `REACTOR_HOOKS_001/005` | The sample violates the rules of hooks. |
| `REACTOR_DSL_001/002` | The sample teaches unstable list keys. |

### Fix at the source

Fix the code in `docs/_pipeline/apps/<topic>/App.cs`. Do **not** add
`<NoWarn>`, `#pragma warning disable`, or an `.editorconfig` severity
downgrade — `DocAppGateWiringTests` rejects those, because a suppression ships
the anti-pattern to every reader who copies the snippet.

The one exception is a page whose subject *is* the thing the rule flags: the
provisional-API pages acknowledge `REACTOR_V1_PREVIEW`, and
`rules-of-reactor` deliberately shows hook and key violations under "Wrong:"
labels. Those pairs live in the `AllowedSuppressions` ledger in
`tests/Reactor.DocPipeline.Tests/DocAppGateWiringTests.cs`, each with the
justification that earned it. Adding a suppression without a ledger entry
fails the test; leaving a ledger entry whose suppression is gone also fails it.

### Running the same check locally

```powershell
# All doc apps, exactly as CI runs them.
dotnet build docs/_pipeline/apps/DocApps.proj -t:Rebuild -c Debug -p:Platform=x64 `
  -p:TreatWarningsAsErrors=true -p:WarningsNotAsErrors=NU1900

# A single app while iterating.
dotnet build docs/_pipeline/apps/<topic>/<topic>.csproj -c Debug -p:Platform=x64 `
  --no-restore -p:BuildProjectReferences=false -t:Rebuild
```

`-t:Rebuild` is load-bearing: without it an up-to-date build never re-runs
`csc`, so the analyzers do not run and a dirty app reports zero warnings.
`-p:BuildProjectReferences=false` keeps several single-app builds from racing
on `src/Reactor`'s `obj/bin`.

## 10. Compiled-output freshness gate

`docs/guide/**` is generated. The `docs-build` job recompiles all of it on every
PR — 72 topic pages, `README.md`, and ~117 pages under `reference/` — and then
asserts that recompiling changed nothing:

```pwsh
git status --porcelain --untracked-files=all -- docs/guide
```

Non-empty output fails the PR. The fix is never to edit the reported file:

```powershell
dotnet run --project src/Reactor.Cli -- docs compile --no-screenshots
```

then commit the result.

Until [issue #1052][i1052] this check covered **two** of those files. CI ran the
full compile, produced a complete answer about all of them, and threw it away —
so a `src/` edit that moved a `snippet="source:..."` region left the published
page stale with the job green. It bit twice in a month: `architecture-overview.md`
published a `GetElement` body that no longer existed, and [PR #1157][p1157]
would have shipped an analyzer alongside ten guide pages that the analyzer
itself rejects. The doc-app snippet gate (§9) does not catch that second one —
it checks the *source* a page is generated from, not the generated page.

### Why the gate reads the compile log first

A clean tree only means *fresh* if the compile that was supposed to rewrite the
tree actually rewrote it. An empty diff produced by a compile that never wrote
anything looks exactly like an empty diff produced by an up-to-date corpus, so
the gate refuses to return a verdict unless the log shows the run completed:

- `Documentation compiled successfully.` must be present. A compile that dies in
  Phase 2 prints `✗ build failed` and returns 1 — and locally that is easy to
  miss, because the tree it leaves behind is clean. Offline this is the common
  case: `--ci` builds Release, `TreatWarningsAsErrors` promotes `NU1900`, and the
  run dies before assembling anything. Pass `-p:WarningsNotAsErrors=NU1900` when
  measuring from a machine that cannot reach the NuGet vulnerability API.
- No phase other than 2 (build), 3 (capture) and 5 (AI author) may report
  `(skipped)`. Those three write no page; anything else does, so a `--skip-*`
  added to the invocation would silently narrow the gate instead of failing it.
  Only the workflow can see this one — the CLI cannot know that a flag it was
  handed was a mistake.

Each of those is a way for the gate to become a check that cannot fail — which
is the defect it was added to fix, one level up.

There is a third way a compile can exit 0 without regenerating, and it is
**not** checked here on purpose. Phase 5.7 prints its header and then bails when
`Reactor.xml` or `reference-map.yaml` is missing (see *Which `Reactor.xml` the
reference phase reads*), leaving ~117 reference pages unwritten. `mur docs
compile --ci` now **returns non-zero** for that, so the compile step catches it
and the gate never sees it. The first version of this gate grepped stdout for
`Reactor.xml not found` instead, which was both the wrong owner and the wrong
direction of failure: reword the message in `CompileCommand.cs` and the grep
silently stops matching — it fails *open*. `ReferenceStalenessWiringTests` pins
the exit code, in both the `--ci` and the local direction, so the contract is a
test rather than a string.

Note the asymmetry that makes this correct rather than merely stricter: locally,
a missing `Reactor.xml` is the first-compile case and still just skips with a
message, because an author who hasn't built yet should still get their guide
pages. Under `--ci` there is no such case — CI always builds.

### Why `git status` and not `git diff`

The same reason given for the images gate above, and it bites harder here:
`git diff` reports tracked modifications only. A new topic template or a newly
generated reference page lands as an *untracked* file, which `git diff` reports
as nothing at all. Measured against a planted
`docs/guide/reference/hooks/PlantedProbe.md`: `git status` reports
`?? docs/guide/reference/hooks/PlantedProbe.md`, `git diff --name-only` reports
an empty string.

### It does not replace the images gate

The freshness gate watches a superset of `docs/guide/images`, but the two say
opposite things. The images gate says *nothing may be written here* and you fix
it by removing the write; the freshness gate says *what was written must be
committed* and you fix it by committing. Merged, a reintroduced screenshot write
would be answered with "commit the regenerated output" — the exact wrong
instruction, and the one [issue #989][i989] exists to prevent. The images gate
therefore runs **first**, so the specific diagnosis lands before the general one.

### Consequence for ordinary PRs

Any `src/` change that alters a region a guide page snippets from now turns
`docs-build` red until the page is recompiled. That is the point, but it means a
red docs job is a routine outcome of framework work rather than a sign something
is broken — recompile and commit, and check the diff belongs to your change.

### The job has to be armed for a docs-only edit

`docs-build` runs on the `non-md` change filter, which is false when every
changed file ends in `.md`. Hand-editing a generated page under `docs/guide` is
exactly that shape — and it is the likeliest way to introduce the drift this
gate catches, so the gate would have skipped the change it exists for. A
separate `compiled-docs` filter (`^docs/guide/`) re-arms the job, ORed into its
`if:`. Unrelated pure-Markdown edits still skip it, so this costs nothing
elsewhere. Same fix as the `audit-ledger` filter beside it
([issue #959][i959]). `VersionSingleSourceTests` pins both halves.

[i959]: https://github.com/microsoft/microsoft-ui-reactor/issues/959

[i1052]: https://github.com/microsoft/microsoft-ui-reactor/issues/1052
[p1157]: https://github.com/microsoft/microsoft-ui-reactor/pull/1157

## 11. Inline C# in templates

A ` ```csharp snippet="topic/id" ` block is extracted from a real doc app, so CI compiles it and
the `docs-snippet-gate` job holds it to the same analyzer rules a reader's own project uses.

A plain ` ```csharp ` block is just text. Nothing compiles it, nothing analyses it, and it renders
identically on the published page — so a reader cannot tell the verified one from the unverified
one. That gap shipped real defects: `testing.md` taught a `ProfileCard`/`Mount` API that does not
exist, `hooks-internals.md` read `Ref<T>.Value` when the property is `.Current`, and `layout.md`
referenced an undeclared `window`.

`InlineSnippetLedgerTests` now fails the build on any new hand-typed C# example. You have three
ways to satisfy it.

### A — move it into the topic's doc app

The default for ordinary app-level Reactor code. Wrap the code in `// <snippet:id>` /
`// </snippet:id>` inside `docs/_pipeline/apps/<topic>/App.cs`, then reference it:

````markdown
```csharp snippet="<topic>/<id>"
```
````

Supporting types the example needs in order to compile go *outside* the markers, so they do not
appear on the page.

### B — point at real repo source

For code that *is* framework, analyzer, or test code. Add the same markers to the real file and
reference it by path:

````markdown
```csharp snippet="source:src/Reactor/Core/RenderContext.cs#use-state-slot"
```
````

This is the right choice on the under-the-hood pages, and it is strictly better than copying:
the page can no longer drift from the implementation it documents. It also works against
`tests/` — a page that shows a test which is itself a passing test in this repo is the strongest
guarantee available.

Only ever add **comment markers** to files under `src/` or `tests/`; never change their behaviour.

### C — leave it inline, and say why

Legitimate when the block genuinely cannot compile: a migration guide's "before" half, a two-line
syntax fragment, or a deliberately-wrong example the prose (rather than a `// Don't` comment)
introduces as the trap. Add it to `AllowedInlineExamples` in
`tests/Reactor.DocPipeline.Tests/InlineSnippetLedgerTests.cs`, keyed by template and then by the
block's **full text**, with the reason.

Use a raw string literal for the key and paste the block verbatim — the gate normalizes both sides
(LF endings, no trailing whitespace, no leading or trailing blank lines), so indentation inside the
block is preserved and must match. The failure message prints each offending block in exactly the
form the key needs. Entries are keyed on the whole block rather than its first line because openers
are shared: `hooks-internals` has two different examples that both begin
`var (count, setCount) = UseState(0);`, and a first-line key silently excused both while only one
had been reviewed.

Two shapes need no ledger entry because they are self-evidently not code to copy: a signature
listing (no `;` or `{` anywhere, and every parameter reads as a declaration — `Markdown(string
markdown)`, `Border(Element)` — rather than a passed value like `TextBlock(message)`), and a block
whose first line labels it a counterexample (`// Don't`, `// Wrong`, `// Avoid`, …).

C is for code that *should not* be compiled — never for code that *will not* compile. If a block
fails to build, that is the bug this system exists to surface: fix the code, don't ledger it.
