# Spec 064 — Framework concepts in the ReactorGallery search index

> Status: implemented. Originating issue: [#1275](https://github.com/microsoft/microsoft-ui-reactor/issues/1275).

## 1. Problem

`samples/ReactorGallery/reactor-search-index.json` is the corpus behind
`winapp find-ui --source reactor`. It answered "what is control X" well and
"how does mechanism Y work" not at all:

| Query | Before |
|---|---|
| `UseState hook` | no results |
| `UseEffect lifecycle` | no results |
| `element ref focus` | no results |
| `key down event handler` | no results |
| `state management` | `reactor-check-box-1`, `reactor-toggle-button-1` |
| `keyboard input` | `DatePicker`, `AutoSuggestBox`, `NumberBox` |

The gap was structural. The generator parsed `ControlRegistry.cs` and merged an
editorial sidecar keyed by control id, so a hook or a keyboard pattern had no id
to be filed under.

Mechanics are the expensive half for a coding agent: the issue measured 16
first-build errors across 4 build cycles, with 5 of 6 distinct messages tracing to
two missing topics — `UseElementFocus` (a real hook the agent did not know
existed) and `VirtualKey.OemPeriod` / `.Equal` (WPF member names that WinRT's
`VirtualKey` does not have).

## 2. The consumer contract (read from source, not assumed)

`winapp find-ui` lives in `microsoft/winappCli` under
`src/winapp-CLI/WinApp.Cli/Services/Controls/`. Five facts shaped this design:

1. **`SampleIndexParser.Parse` reads only the top-level `controls` array.** A
   sibling `concepts` array would be schema-legal and silently ignored. Every
   concept therefore had to land inside `controls`.
2. **`schemaVersion` must be exactly `1`.** `IsSupportedVersion` returns an empty
   scenario list — not an error — for any other value. **Bumping it would blank
   the entire Reactor corpus with no diagnostic anywhere.**
   `SearchIndexGeneratorTests.SchemaVersion_StaysAtOne` pins it with that reason.
3. **`category` is never read.** A new `Fundamentals` category costs nothing on
   the consumer side; it is purely gallery navigation.
4. **BM25 field weights** (`SearchEngine.SearchGrouped`): `curatedKeywords` 5.0,
   `keywords` 3.0, `name` 3.0, `id` 3.0, camel-split name 2.5, sample headers 0.8.
   These are the weights the engine applies; see §2.1 for which of them actually
   receive Reactor data today.
5. **`samples` is an array the CLI iterates**, and `curatedKeywords`, `details`
   and `docs` are control-level contract fields Reactor did not emit. All are
   additive — no version bump.

The fetch cap is 16 MB (`ControlsHttpHelper.MaxResponseBytes`) against an index
that was 89 KB, so plural samples were never a size concern.

### 2.1 `curatedKeywords` is emitted but currently inert

`SampleIndexParser` parses `curatedKeywords` into a third dictionary, but the
Reactor path drops it before it reaches the search engine:

- `ReactorFetcher.FetchAsync` and `.Parse` both destructure as
  `var (scenarios, tags, _)`.
- `ReactorProvider.FetchAsync` builds `new ProviderData(scenarios, tags, new())`
  — an empty curated dictionary.

Both are deliberate on the consumer side and say so in their own XML docs:
*"Reactor publishes no `curatedKeywords`, so that slot is empty here and the
tuple stays two-wide."* That assumption is exactly what this change invalidates,
and widening those two tuples is a one-line change in each — but it is theirs to
make, not ours.

**No retrieval claim in this spec rests on the 5.0 slot.** The field is emitted
because the published contract defines it, it costs nothing, and it activates the
moment the consumer wires it through. Measured differentially against the
regenerated index, the top-ranked result for every query in §1 is **identical
with and without** `curatedKeywords` contributing: the improvement comes from
`keywords` (3.0) plus the `id`, `name`, and camel-split-name fields.

## 3. Design

### 3.1 Concepts are gallery pages, not JSON

The nine topics are real `ControlPages/Fundamentals/*.cs` pages with registry
entries and router arms. They compile in CI, and the existing gallery lint suites
(`GallerySampleLintTests`, `GalleryCardIndependenceTests`,
`GallerySnippetAgreementTests`) apply to them unchanged.

That property is the point. A corpus authored as hand-written JSON strings is
exactly how an agent learns an API that does not exist — which is the defect the
issue reports. Writing them as compiled pages makes the snippets falsifiable.

| id | name | marked prose lives in |
|---|---|---|
| `use-state` | UseState | `reactor-getting-started` |
| `use-effect` | UseEffect | `reactor-getting-started` |
| `use-reducer` | UseReducer | `reactor-getting-started` |
| `use-memo` | UseMemo and UseCallback | `reactor-getting-started` |
| `use-ref` | UseRef | `reactor-getting-started` |
| `context` | Context | `reactor-getting-started` |
| `element-refs` | Element refs | `reactor-input` |
| `keyboard-input` | Keyboard input | `reactor-input` |
| `pointer-input` | Pointer and gestures | `reactor-input` |

### 3.2 Every clean card is emitted

`ParseSamples` now returns every qualifying `SampleCard` on a page rather than
the first, and `ResolveSamples` emits them all. A mechanics topic does not fit
one snippet, and the contract's `samples` array was always plural.

- An editorial `sampleOverride` still defines the **first** sample (replacing its
  header, its code, or both, or standing alone when no card qualifies); the
  page's remaining clean cards follow it.
- The per-card REAL-CODE-ONLY rule is unchanged: a card that abbreviates with a
  placeholder is passed over, and a page with no clean card at all still fails
  generation.
- Headers are deduplicated case-insensitively — two same-titled cards would ship
  two indistinguishable scenarios.

### 3.3 Prose is lifted from the skills by marker

`curatedKeywords` and `docs` are authored in `editorial.json`. `details` is not:
it is lifted verbatim out of the shipped agent kit.

```markdown
<!-- index:use-state -->
`UseState<T>(initial)` returns `(value, setValue)` …
<!-- /index:use-state -->
```

The marker id **is** the control id, so there is no second mapping to keep in
sync. The scanner walks the root `SKILL.md` plus the `plugins/` and `skills/`
trees in ordinal path order.

- **Repeated ids concatenate** in (file, offset) order. A concept legitimately
  spans sections — pointer events and gestures sit either side of the keyboard
  section in `reactor-input`.
- **Relative markdown links are rewritten** to absolute
  `https://github.com/microsoft/microsoft-ui-reactor/blob/main/…` URLs. The prose
  is read far from the file it was written in, so a relative link is dead weight
  there. A target that does not exist, or escapes the repo, fails generation.
- Extending the generator's no-silent-drops guarantee, generation fails on an
  unclosed marker, a nested or mismatched marker, a marker that closes nothing,
  and a marker id naming no control.

`--agent-kit=<dir>` overrides the scan root and `--no-agent-kit` disables it. By
default the root is the nearest ancestor of **`galleryDir`** containing
`Reactor.slnx`, so passing the real paths explicitly produces byte-identical
output to passing none. A synthetic gallery in a temp directory has no
`Reactor.slnx` above it and therefore contributes no `details` — the
marker ids would name none of its controls — so the tests opt out without
needing a flag.

### 3.4 The parity gate

Because `details` is lifted verbatim, asserting that the skill's prose appears in
`details` would be a tautology that passes however wrong either side is.

`SearchIndexSkillParityTests` measures the half that can drift instead: every
`Use*` / `.On*` API named in a marked block's **C# fences** must appear in at
least one of that entry's emitted **samples**. Delete a gallery card and it
reddens; add an API to a skill without demonstrating it and it reddens.
`TheGate_CanFail` is the instrument check, and the gate counts what it inspected
so a regex that matches nothing cannot report a clean pass.

Verified by mutation: deleting the tap card from `PointerInputPage` produced
four named findings (`.OnTapped`, `.OnDoubleTapped`, `.OnRightTapped`,
`.OnLongPress`); restoring it returned to green.

## 4. Defects found by building the gate

Writing pages that had to compile against the same APIs the skills describe
surfaced three shipped errors in `reactor-input/SKILL.md`, all of which had been
copied from WinUI's `ManipulationDelta` shape rather than Reactor's flat gesture
structs:

| Was | Is | Why |
|---|---|---|
| `e.Delta.Translation.X` | `e.Delta.X` | `PanGesture.Delta` is a `Point` |
| `e.Delta.Scale` | `e.ScaleDelta` | `PinchGesture` carries `Scale` / `ScaleDelta` |
| `e.Delta.Rotation` | `e.AngleDelta` | `RotateGesture` carries `Angle` / `AngleDelta` |

The 60Hz pan pattern also bound its cell with `UseRef<UIElement>()` and passed it
to `.Ref(...)`, which takes an `ElementRef`; `Ref<T>` is a different type and
would not compile. It now uses `UseElementRef<FrameworkElement>()`.

These slipped past `AgentKitDocGateTests` (dropped modifiers and wrapper
workarounds) and `GallerySourceStringPhantomTests` (API *names*, all of which are
real somewhere) because both scan names, not member-access chains. Requiring a
compiled gallery card per documented API is what caught them.

### 4.1 The tuple-dependency claim

Review of this change surfaced a fourth, subtler error — one this spec's own
first draft propagated. `reactor-getting-started/SKILL.md` stated that a **tuple**
dependency "compares unequal on every render", grouping it with freshly allocated
objects, arrays, and lambdas. That is false at runtime:

- `RenderContext.DepEquals<T>` compares value-type deps with
  `EqualityComparer<T>.Default`, so a `ValueTuple` of value types is **value-equal**
  when its fields are unchanged. The `object[]` params path compares elementwise
  with `Equals`, which is value-equal for a boxed `ValueTuple` too.
- The advice is nonetheless correct, for a different reason:
  `HookRulesAnalyzer` classifies `TupleExpressionSyntax` as unstable
  unconditionally (`TupleExpressionSyntax => (true, "tuple")`), so
  `REACTOR_HOOKS_004` rejects a tuple dep and fails the build.

The prose now states the analyzer reason rather than a false runtime one.
Reference types are still described as genuinely unequal, because they are. This
is exactly the defect class the change exists to prevent: prose that ships into a
retrieval corpus as authoritative is worse than no prose when it is wrong.

## 5. Result

104 entries (95 controls + 9 concepts), 235 samples, 166 KB — comfortably inside
the consumer's 16 MB cap. The nine concepts carry lifted prose, doc links,
curated intent terms (dormant until the consumer wires the 5.0 slot through, per
§2.1), and `usings` that name the right namespaces — `keyboard-input` ships
`Windows.System`, which is the fact the agent was missing about `VirtualKey`.

Every query in §1 now returns its topic as the top hit, and does so using only
the fields the consumer reads today.
