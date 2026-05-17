# Phase 2 — Retro

Captured 2026-05-17. Phase 2 took the Phase 1 stub set for the seven Reactor-unique pages and promoted them to their target tiers (1 Comprehensive + 6 Solid + 5 recipes), plus the recipes index. Eleven commits, ~3,500 lines of template + doc-app code, no rework.

## What surprised us

1. **Catalog-thumb harness wiring was less work than feared.** Spec §2.0 framed this as a "don't block on it" risk; the actual implementation was one new field (`Kind`) on `ScreenshotConfig`, a `ProcessThumb` method on `ImageProcessor` (letterbox + bicubic downscale), and a kind-dispatch in `ScreenshotCapture`. The `DocAssembler` emits `<id>-thumb.png` URLs for catalog-thumb entries. Seven unit tests pass. The pipeline change is in production; the gallery still uses placeholder PNGs because the harness needs a CI runner to capture against the controls doc app.

2. **The "≥3 snippets per Solid page" lint rule forced denser snippet markers.** First-pass templates ran into `REACTOR_DOC_TIER_003` because each recipe had only one snippet for the whole component. The fix was structural: split each recipe's component into 3 snippet markers (state / shape / render) so the template can pull each section as a separate code block with prose between them. Side effect — the recipe pages read better because the prose explanation is interleaved with the code instead of being one big code-then-paragraph block.

3. **The "reference table in first half" heuristic flagged every recipe.** All five Solid recipes had a `## Reference` table near the bottom of the page that the lint heuristic couldn't find (it counts lines, and the snippet expansion pushed the table past the midpoint). Fix was to move the Reference table to a "Primitives" section right after the lead paragraph. The bottom `## Reference` section was redundant after this and got removed.

4. **`Component` is not directly an `Element`.** A trap several times — `new Counter()` doesn't compile as a return value, but `Component<Counter>()` does. The discrepancy is consistent with React (a `Counter()` call vs. `<Counter />`), but the C# error message (`Cannot implicitly convert type 'Counter' to 'Element'`) isn't suggestive. Worth noting in the components page deep-dive in Phase 4.

5. **`UsePersisted` is in-memory only.** The original task framing ("cover migration story, JSON shape, conflict resolution") implied disk persistence; the framework's hook is an LRU cache. The persistence page reframes this — `UsePersisted` survives re-mount, not re-launch; for cross-process persistence the user combines it with a `UseEffect` disk bridge. The "JSON shape" language now refers to the disk-bridge pattern, not the cache.

## Deferred to Phase 2.5

1. **Four recipes** — paginated-list, multi-step-form, command-palette, drag-reorder. Spec instruction was "aim to ship all 9 but don't sacrifice quality"; the five shipped recipes are the highest-traffic patterns. The four deferred recipes remain at stub and are marked "Phase 2.5" in the gallery so the surface area stays discoverable.

2. **Catalog-thumb capture against the live controls doc app.** The harness wiring is in production; the actual screenshot capture against `docs/_pipeline/apps/controls/` belongs to the doc-pipeline CI runner pass. Gallery uses 320×240 gray placeholders today.

3. **Auto-generated theming-tokens swatch table.** Spec §14 risk-mitigation language: "the hand-curated table that lints clean is more valuable than a stalled auto-gen." The page carries a `<!-- TODO Phase 4 -->` marker so the follow-up is discoverable. 35 typed tokens + the `Theme.Ref(string)` escape hatch = ≥36 surface entries, validated against `src/Reactor/Core/Theme.cs`.

4. **Inbound cross-link injection** from existing pages (hooks, effects, components, styling) back to the new Phase 2 pages. Happens automatically during the Phase 4 cross-link sweep.

## Process notes

- **Doc-app build time** ran to ~1 minute per fresh app (Windows App SDK reference assemblies). Phase 2 added 9 new doc apps; serial builds added ~10 minutes of friction during snippet-marker iteration. Phase 3+ should look at incremental output paths to avoid re-resolving the SDK every build.
- **Background-task interleaving was fragile.** Two `dotnet build` invocations running concurrently against `Reactor.csproj` raced on `input.json` (per memory note). Phase 2 builds were serial throughout.
- **Compile-then-validate loop was fast** — a single `mur docs compile --validate-only` after a template change is sub-2-seconds; the lint-iteration loop was the right granularity for catching tier-bar issues before they reached commit.
