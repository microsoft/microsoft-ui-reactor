# Reactor.SearchIndex

Generates `samples/ReactorGallery/reactor-search-index.json` — a deterministic, schema-versioned
index of the ReactorGallery's **controls and framework concepts**, consumed by the external
**winui-search** CLI (which fetches it from
`raw.githubusercontent.com/.../main/samples/ReactorGallery/reactor-search-index.json`).

It is a build-time, headless Roslyn tool (never shipped in the NuGet). It parses the gallery
**source** — `ControlRegistry.cs` (id/name/category/description), `PageRouter.cs` (tag → page),
`ControlPages/**` (every *complete* real-code `SampleCard`) — merges the hand-curated
`editorial.json` sidecar (`keywords`, `curatedKeywords`, `relatedControls`, `usings`, `docs`,
`sampleOverride`, `exclude`, keyed by control id), and lifts each entry's `details` prose out of
the shipped agent kit by marker (see below). Output is a pure function of those inputs: controls
sorted by id, fixed key order, LF newlines, no volatile values.

## Usage

```pwsh
# Regenerate the committed index (run after editing gallery controls, editorial.json,
# or an `<!-- index:… -->` block in a SKILL.md):
dotnet run --project tools/Reactor.SearchIndex

# Fail (exit 1) if the committed index is stale, without rewriting it:
dotnet run --project tools/Reactor.SearchIndex -- --check
```

Exit codes: `0` success / up-to-date, `1` stale (`--check`), `2` usage or generation error.

## Authoring a framework concept

A non-control topic (a hook, element refs, keyboard input) is an ordinary gallery page in the
`Fundamentals` category — it needs a `ControlRegistry` entry, a `PageRouter` arm, a page under
`ControlPages/Fundamentals/`, and an `editorial.json` entry. Nothing about the tooling is
special-cased for it; see [spec 064](../../docs/specs/064-search-index-framework-concepts.md).

Its **prose** is not authored here. Wrap the owning section of the SKILL.md in a marker whose id
is the control id, and the generator lifts it verbatim into `details`:

```markdown
<!-- index:use-state -->
`UseState<T>(initial)` returns `(value, setValue)` …
<!-- /index:use-state -->
```

That is what keeps the index and the shipped skills saying the same thing — they are the same
bytes, not two copies kept in step by hand. Repeating an id is allowed (the blocks concatenate in
document order) for a concept that spans sections. Repo-relative links inside a marked block are
rewritten to absolute GitHub URLs, because the prose is read far from the file it lives in.

## Guarantees & gate

- **Keywords are required** and canonicalized (trim / lowercase / collapse / dedupe) — generation
  fails if an included control has none. `curatedKeywords` (the consumer's higher-weighted 5.0
  BM25 slot) is optional and canonicalized the same way.
- **Real code only** — sample cards with placeholder tokens (`...`, `<your-key>`, abbreviated
  URLs) are rejected. Every *other* clean card on the page is emitted, in source order, with
  case-insensitively duplicate headers dropped.
- **No silent drops** — an orphan/typo'd editorial key, a misspelled editorial field, an
  unparseable `ControlInfo` entry, a `docs` entry missing its title or uri, an unclosed / nested /
  mismatched `<!-- index:… -->` marker, a marker id naming no control, a repo-relative link in a
  marked block that resolves to nothing, or any non-`exclude` skip all fail generation.
- **`schemaVersion` stays `1`.** The consumer pins it and treats any other value as "nothing I
  understand" — returning zero scenarios with no error — so a bump would silently blank the whole
  corpus. Everything added since has been additive and schema-legal.

`tests/Reactor.Tests/Tooling/SearchIndexGeneratorTests.cs` regenerates in-process and asserts
byte-equality with the committed file (the staleness gate that runs in CI `dotnet test`);
`SearchIndexToolTests.cs` covers the editorial/skip/override/marker/CLI edge cases; and
`SearchIndexSkillParityTests.cs` requires every API a marked skill block demonstrates to appear in
one of that entry's emitted samples.

Curate `editorial.json` and the SKILL.md markers, never the generated JSON.

## Trimming / NativeAOT

The generator is trim- and NativeAOT-clean: JSON goes through the `System.Text.Json` source
generator (`SearchIndexJsonContext` / `EditorialJsonContext`) and regexes through
`[GeneratedRegex]`, so the IL2*/IL3* analyzer (`IsAotCompatible=true`) runs clean over our code.

A native single-file build works:

```pwsh
dotnet publish tools/Reactor.SearchIndex -r win-x64 -c Release -p:PublishAot=true -p:IlcTreatWarningsAsErrors=false
```

…produces a ~12 MB native `Reactor.SearchIndex.exe` that emits a byte-identical index. The
`IlcTreatWarningsAsErrors=false` downgrade is required only because the **Roslyn** dependency has
trim-unsafe spots (e.g. `CommonCompiler.GetAssemblyLocation` → `Assembly.Location`, `IL3000`)
that are unreachable at runtime for our syntax-only parsing — the native binary runs correctly.
(Native linking also needs the VS C++ tools, i.e. `vswhere.exe`/`link.exe` on `PATH`.)

