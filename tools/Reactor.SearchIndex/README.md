# Reactor.SearchIndex

Generates `samples/ReactorGallery/reactor-search-index.json` — a deterministic, schema-versioned
index of the ReactorGallery's controls consumed by the external **winui-search** CLI (which
fetches it from `raw.githubusercontent.com/.../main/samples/ReactorGallery/reactor-search-index.json`).

It is a build-time, headless Roslyn tool (never shipped in the NuGet). It parses the gallery
**source** — `ControlRegistry.cs` (id/name/category/description), `PageRouter.cs` (tag → page),
`ControlPages/**` (the first *complete* real-code `SampleCard`) — and merges the hand-curated
`editorial.json` sidecar (`keywords`, `relatedControls`, `usings`, `sampleOverride`, `exclude`,
keyed by control id). Output is a pure function of those inputs: controls sorted by id, fixed key
order, LF newlines, no volatile values.

## Usage

```pwsh
# Regenerate the committed index (run after editing gallery controls or editorial.json):
dotnet run --project tools/Reactor.SearchIndex

# Fail (exit 1) if the committed index is stale, without rewriting it:
dotnet run --project tools/Reactor.SearchIndex -- --check
```

Exit codes: `0` success / up-to-date, `1` stale (`--check`), `2` usage or generation error.

## Guarantees & gate

- **Keywords are required** and canonicalized (trim / lowercase / collapse / dedupe) — generation
  fails if an included control has none.
- **Real code only** — sample cards with placeholder tokens (`...`, `<your-key>`, abbreviated
  URLs) are rejected in favour of the first complete card, or an editorial `sampleOverride`.
- **No silent drops** — an orphan/typo'd editorial key, a misspelled editorial field, an
  unparseable `ControlInfo` entry, or any non-`exclude` skip all fail generation.

`tests/Reactor.Tests/Tooling/SearchIndexGeneratorTests.cs` regenerates in-process and asserts
byte-equality with the committed file (the staleness gate that runs in CI `dotnet test`);
`SearchIndexToolTests.cs` covers the editorial/skip/override/CLI edge cases.

Curate `editorial.json`, never the generated JSON.
