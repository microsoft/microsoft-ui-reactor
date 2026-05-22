# 043 — `mur find` and the Reactor sample catalogue

| | |
|---|---|
| **Status** | Draft |
| **Owner** | @andersonch |
| **Related** | [038](038-mur-check-did-you-mean-design.md) (`mur check` suggester architecture), [030](030-reactor-gallery-design.md) (Reactor Gallery sample app), [013](013-doc-system-design.md) (doc compile pipeline), `plugins/reactor/skills/reactor-recipes/` (deprecated by this spec) |
| **Reference** | `C:\Users\andersonch\Code\win-dev-skills\src\tools\winui-search` (the parity target) |

## 1. Summary

Add a **discovery-side** tool to the `mur` CLI — `mur find "<intent>"` — that returns a ranked list of canonical Microsoft.UI.Reactor (Reactor) scenarios for the agent to study **before** writing code, then `mur get <id>` to retrieve the full compilable example plus its pitfall notes. The corpus is a hand-curated library of ~200+ scenarios authored as real, mur-check-clean `.cs` files inside the Reactor repository, with sidecar JSON metadata, extracted into an embedded snapshot at build time.

This closes the workflow gap between Reactor and WinUI 3 agent tooling: `winui-search.exe` gives WinUI agents BM25-ranked Gallery + Toolkit samples and curated pitfall notes; `mur check` gives Reactor agents post-write fuzzy correction but no pre-write discovery. With `mur find`, the loop becomes **intent → find → get → write → check** instead of **guess → check → re-guess**.

`reactor-recipes` (6 hand-authored snippets, markdown-indexed) is superseded by this catalogue and deleted as part of the rollout.

## 2. Motivation

### 2.1 What `winui-search` does for WinUI agents

The reference implementation (`win-dev-skills/src/tools/winui-search`) indexes:

- ~100 WinUI 3 Gallery controls × multiple scenarios each (~400 entries)
- Every Community Toolkit scenario (~150 entries)
- ~30 curated platform integration patterns (JumpList, Share, file pickers, drag-drop)
- Per-control tag dictionaries layered onto the BM25 corpus

Plus a hand-curated `Notes.cs` side-channel: ~50 controls have known-pitfall warnings (`TreeView: never use custom .NET record/class types as TreeViewNode.Content`, `Border has NO .Cursor property in WinUI 3`) that surface *only* when the agent reaches for that control.

The query pipeline:

1. **Phrase preprocessing** — `"data grid"` → `datagrid`, `"context menu"` → `contextmenu` (~70 multi-word phrases collapsed into single tokens)
2. **Tokenize** — lowercase, strip non-alphanum/hyphen
3. **Synonym expansion** — ~150 cross-framework aliases (`modal` → `contentdialog`, `dropdown` → `combobox`, `sidebar` → `navigationview`, `pull to refresh` → `pulltorefresh`)
4. **Two-layer BM25** — score *controls* first using weighted multi-field docs (`controlName×3 + controlId×3 + enrichmentTags×3 + headerText×1.5`), then pick the best *scenario* inside the winning control with a second BM25 pass
5. **Original-query exact-name boost** — ×2 if the unexpanded query contains the exact control name, so explicit asks aren't drowned by synonym noise

The result: an agent asking "modal dialog with save/cancel" finds `ContentDialog` even though "modal" never appears in WinUI's vocabulary, sees the canonical XAML+C#, and inherits `Always set XamlRoot = Content.XamlRoot before ShowAsync()` for free.

### 2.2 Why Reactor needs the same tool

The current Reactor agent workflow has three discovery surfaces, none of which scale:

1. **`reactor.api.txt`** — 896-line flat signatures index. Agents grep it for factory names. No scenarios, no usage, no pitfalls. Best for "what's the signature of `DataGrid<T>`" — useless for "how do I build a sidebar nav."
2. **`reactor-recipes`** — 6 hand-authored single-file recipes (list-add-delete, sidebar-nav, form-with-validation, async-fetch-list, themed-card, calendar-multiselect, canvas-positioning, named-styles, use-custom-hook). Quality is high; corpus is too small to cover the API surface.
3. **`mur check`** suggester (Spec 038) — JaroWinkler over `FactoryIndex` for `CS0103`/`CS1061`/`CS0117`. This is *correction*, not *discovery*; the agent has to have already named (or mis-named) the factory.

The vocabulary problem is the same one WinUI faced: an agent translating a React component for a modal will reach for `Dialog`, `Modal`, or `Popup` — none of which exist as Reactor factories. Today they grep `reactor.api.txt` and miss; with `mur find`, the synonym map routes them to `ContentDialog` plus a working example.

### 2.3 Why a coded search beats a longer markdown skill

| | Markdown skill | `mur find` |
|---|---|---|
| Vocabulary translation (`modal` → `ContentDialog`) | No | Yes — synonym map |
| Corpus size before context bloat | ~30–50 entries before unreadable | ~200+ entries; only top-5 land in context |
| Lazy retrieval | All-or-nothing skill load | `find` → 5 lines; `get` → one scenario |
| Multi-field weighted ranking | Manual table ordering | BM25 with explicit field weights |
| Pitfalls bound to the right context | Generic "common mistakes" sections | Side-channel `Notes.GetNotes(factory)` fires only when that factory wins |
| Compilable code guarantee | No — code in markdown rots silently | Yes — every scenario is a real `.cs` file that passes `mur check --final` in CI |

## 3. Goals / non-goals

### 3.1 Goals

- **G1.** Ship a `mur find "<intent>"` + `mur get <id>` command pair with parity to `winui-search`'s ranking quality (BM25 + synonym map + two-layer search + Notes side-channel).
- **G2.** Seed the catalogue with **~200+ hand-authored scenarios** organized into P0/P1/P2 priority bands (§5). Every scenario is a real `.cs` file that compiles clean against the current Reactor surface.
- **G3.** Build-time **extractor** that walks the scenario tree, validates each `.cs` file builds (CI gate), and emits the embedded JSON snapshot. Drift between catalogue and live API is a CI failure, not a silent error.
- **G4.** **Synonym map** specifically tuned for Reactor: React → Reactor (`useState` → `UseState`, `<div>` → `FlexColumn`/`VStack`, `key=` → `.WithKey`) plus the cross-framework aliases (`modal`, `dropdown`, `sidebar`, …) that WinUI users already wrote down.
- **G5.** **Pitfall notes** keyed by factory/component name (`UseState`: "with `List<T>` doesn't re-render on `.Add()` — use `UseReducer`"; `lists`: "`.Select(...).ToArray()` must include `.WithKey` or focus/animation breaks across reorders").
- **G6.** **Delete `reactor-recipes`** as part of the rollout. Port its 6 recipes into the catalogue (they become P0 scenarios). Single source of truth.
- **G7.** Ship as **`mur find` / `mur get` / `mur list` subcommands inside the existing `mur.exe`** — no separate binary. The embedded `scenarios.json` is a manifest resource on `Reactor.Cli`, alongside the existing `SKILL.md` and `reactor.api.txt` embeds. Consumers who installed `mur` already have it; the `Microsoft.UI.Reactor` NuGet `agentkit/` payload gains `scenarios.json` so users without `mur` can still read the catalogue, but there is no shipped sibling exe.

### 3.2 Non-goals

- **NG1.** Automated extraction from `samples/` (TodoApp, ReactorGallery, etc.). Those apps have application-level shape; the catalogue needs scenario-sized slices. Long-term we might index them too, but v1 is hand-curated only.
- **NG2.** Live GitHub re-fetch (`winui-search update` mode). Reactor's catalogue is part of the source tree; the build *is* the refresh.
- **NG3.** Learned/embeddings-based search. BM25 + synonyms is sufficient for a ~200-entry corpus and removes any inference-cost or non-determinism concerns. Revisit only if eval shows BM25 misses.
- **NG4.** Auto-generated scenarios from `reactor.api.txt`. The catalogue is curated; the long tail of obscure factories falls through to `mur --api` and `mur check`'s suggester. Coverage targets in §5 are explicit.
- **NG5.** A separate "anti-patterns" command. Pitfall notes ride alongside the positive scenario via `Notes`. No need for a parallel surface.

## 4. Architecture

### 4.1 Repository layout

```
src/Reactor.Cli/Find/                     # new subcommand
  FindCommand.cs                          # entry point: parses args, runs SearchEngine
  GetCommand.cs                           # `mur get <id>` — formats one scenario
  SearchEngine.cs                         # ported from winui-search (two-layer BM25)
  BM25.cs                                 # ported verbatim from winui-search
  Synonyms.cs                             # Reactor-tuned phrase+synonym maps (§4.7)
  StopWords.cs                            # ported, lightly extended
  Notes.cs                                # pitfall notes keyed by factory/component (§4.8)
  Models.cs                               # Scenario / Tag POCOs
  DataLoader.cs                           # reads embedded JSON snapshot

tools/Reactor.SampleCatalogue/            # build-time extractor
  Reactor.SampleCatalogue.csproj          # AOT-friendly; mirrors Reactor.SignaturesGen pattern
  Program.cs                              # walks samples/scenarios/, builds JSON
  ScenarioWalker.cs
  ScenarioCompiler.cs                     # invokes Roslyn to verify each .cs compiles

samples/scenarios/                        # the catalogue
  README.md                               # authoring contract
  _meta/
    tags.json                             # global tag dictionary (factory → keywords)
    aliases.json                          # global cross-framework aliases
  hooks/
    use-state-basic/
      Scenario.cs                         # compilable; `dotnet run` works
      scenario.json                       # id, intent, tags, pitfalls, notesKey
    use-state-list-pitfall/
      Scenario.cs                         # demonstrates broken UseState<List<T>>
      scenario.json                       # tag: "anti-pattern"; pairs with use-reducer-for-lists
    use-reducer-list/
      Scenario.cs
      scenario.json
    …
  layout/
  inputs/
  lists/
  forms/
  async/
  navigation/
  theming/
  charting/
  surfaces/
  commanding/
  accessibility/
  react-port/                             # one scenario per React→Reactor mapping
  cross-framework/                        # one per common alias (modal, sidebar, …)
```

Each scenario folder is **self-contained and compilable**. `cd samples/scenarios/hooks/use-state-basic && dotnet run Scenario.cs -p:Platform=ARM64` produces a working app. The folder name is the scenario `id` (kebab-case).

### 4.2 Authoring contract for a scenario

Every scenario folder has exactly two files:

**`Scenario.cs`** — a complete single-file Reactor app:

```csharp
// id: use-state-basic
// intent: count clicks; demonstrate UseState with primitive value
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("UseStateBasic", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            Heading($"Count: {count}"),
            Button("+1", () => setCount(count + 1)));
    }
}
```

**`scenario.json`** — sidecar metadata the extractor reads:

```json
{
  "id": "use-state-basic",
  "category": "hooks",
  "title": "Counter with UseState",
  "intent": "increment a primitive value on click",
  "tags": ["state", "counter", "hook", "useState", "primitive"],
  "factoryAnchors": ["UseState", "Button", "VStack"],
  "notesKey": "UseState",
  "relatedIds": ["use-state-list-pitfall", "use-reducer-list"],
  "priority": "P0"
}
```

The extractor strips the leading metadata comments and `#:package` lines from `Scenario.cs` to produce the *displayed* code snippet (`get` output keeps the headers; UI consumers can opt out). `relatedIds` powers the cross-reference field shown at the bottom of `mur get`.

### 4.3 Build-time extractor (`tools/Reactor.SampleCatalogue`)

Mirrors the `tools/Reactor.SignaturesGen` pattern that already populates `reactor.api.txt`:

1. Walk `samples/scenarios/**/scenario.json`.
2. For each scenario:
   - Validate the sidecar JSON against the schema.
   - Compile `Scenario.cs` with Roslyn (in-memory `CSharpCompilation` against the current `Reactor.dll`). Hard-fail on any error; warn on `REACTOR_*` analyzer hits and embed them as suggested fixes.
   - Strip metadata comments and `#:package` headers from the displayed snippet; keep the raw text alongside for `mur get --raw`.
3. Emit `samples/scenarios/_generated/scenarios.json` (committed for auditability) and embed it into `Reactor.Cli` and the `Microsoft.UI.Reactor` NuGet payload (`agentkit/scenarios.json`).
4. CI gate: if extraction fails, the build fails. Equivalent to the `mur --regen-api` AfterBuild target — extractor wiring described in §6.

This guarantees the catalogue and the live Reactor API never drift. A renamed factory takes down the build until the catalogue is updated.

### 4.4 Search engine port

Files ported from `winui-search` essentially verbatim:

- `BM25.cs` — K1=1.2, B=0.75, weighted TF, unchanged.
- `StopWords.cs` — copy from winui-search; add a small Reactor-specific list (`reactor`, `element`, `factory`, `hook` — common but uninformative within this corpus).
- `SearchEngine.cs` — two-layer search:
  - **Layer 1** ranks *factories* (a "factory" is the union of all scenarios that share a `factoryAnchors[0]`). Weighted doc:
    - `factoryAnchors[0]` × 3
    - `tags` × 3
    - `title` × 2
    - `intent` × 1.5
  - **Layer 2** picks the best *scenario* within the winning factory using BM25 over `(title, intent, tags)`.
  - Original-query exact-anchor boost: ×2 if the unexpanded query contains the exact factory name (preserves explicit asks).
- A new pseudo-category: **react-port** and **cross-framework** scenarios don't bind to a single factory anchor — they bind to a *concept*. These are flattened into Layer 1 as their own pseudo-factories (anchor = the synonym key, e.g., `modal-dialog`).

### 4.5 CLI surface

```text
mur find <query> [--max N] [--category <name>] [--include-anti-patterns]
mur get <id> [--raw]
mur list [--category <name>]
```

- **`find`** — Default `--max 5`. Output: one result per line, `<id>  <title>  → SKILL: <category>`. Example:
  ```
  $ mur find "sidebar navigation"
  Found 5 matches for "sidebar navigation":
    sidebar-nav-basic        Sidebar nav with NavigationView          → SKILL: navigation
    sidebar-nav-with-detail  Sidebar nav with master-detail content   → SKILL: navigation
    drawer-overlay           Hamburger / overlay drawer (SplitView)   → SKILL: navigation
    nested-routes            Nested navigation routes                 → SKILL: navigation
    deep-link                Deep-link into a sidebar item            → SKILL: navigation
  To get full code: mur get <id>
  ```
- **`get`** — Returns formatted markdown:
  ```markdown
  ## Sidebar nav with NavigationView
  *Category: navigation · Intent: sidebar pane with two items navigating between pages*

  **C#:**
  ```csharp
  // …Scenario.cs minus the metadata header…
  ```

  **Important (Notes for `UseNavigation`):**
  - Routes are registered once at the top of the render tree; navigating mutates the active key, not the registration.
  - `WithNavigation` must wrap the root or `UseNavigation` returns null.

  **See also:** `sidebar-nav-with-detail`, `drawer-overlay`
  ```
- **`list`** — Enumerate all scenarios, grouped by category. Same UX as `winui-search list`.
- **`--include-anti-patterns`** — anti-pattern scenarios are excluded from `find` by default to keep the top-5 positive. Opt-in flag retrieves them; they're tagged with `priority: anti-pattern` in the sidecar.

`mur get` is a top-level verb (not nested under `find`) to match `mur check`, `mur loc`, etc., and to mirror `winui-search`'s `get`.

### 4.6 Distribution

- **In-source builds** — `Reactor.Cli` already embeds `SKILL.md` and `reactor.api.txt` as manifest resources. Add `scenarios.json` (extractor output) the same way. `mur find` reads the embedded JSON; no separate sibling binary.
- **NuGet payload** — `Microsoft.UI.Reactor.nuspec` already ships `agentkit/` content. Add `agentkit/scenarios.json` (and a small `agentkit/scenarios.md` index for human browsing). Users without `mur` installed can still cat the JSON; users with `mur` get the ranked CLI.
- **Skill integration** — `plugins/reactor/skills/reactor-getting-started/SKILL.md` adds a callout matching the `winui-design` SKILL.md callout (cite the canonical winui pattern verbatim):
  > **Before picking factories, search the catalogue.** `mur find "<feature description>"` returns a ranked shortlist; `mur get <id>` returns the full compilable scenario plus its pitfall notes. Batch all your searches for the page or feature at the top of the task — do *not* interleave searching with coding.

### 4.7 Synonym & phrase maps (`Synonyms.cs`)

Three maps composed in this order:

**Phrases (multi-word → single token), ~80 entries** — superset of winui-search's list, biased toward Reactor/React vocabulary:

```text
"data grid"          → datagrid
"sidebar nav"        → sidebar
"sidebar navigation" → sidebar
"master detail"      → masterdetail
"content dialog"     → contentdialog
"pull to refresh"    → pulltorefresh
"infinite scroll"    → infinite
"context menu"       → contextmenu
"global state"       → context
"shared state"       → context
"server state"       → useresource
"client state"       → usestate
"derived state"      → usememo
"effect cleanup"     → useeffect
"use state"          → usestate                  (so React vocabulary collapses)
"use effect"         → useeffect
"use memo"           → usememo
"use callback"       → usecallback
"use reducer"        → usereducer
"use context"        → usecontext
"use ref"            → useref
"use resource"       → useresource
"use mutation"       → usemutation
"theme switch"       → theme
"dark mode"          → theme
"high contrast"      → highcontrast
… (see appendix A)
```

**Synonym map (cross-framework + React → Reactor), ~200 entries** — keys lowercase; lookup case-insensitive. Examples (full table in appendix B):

```text
modal              → contentdialog, dialog
popup              → flyout, contentdialog, teachingtip
sidebar            → navigationview, splitview
drawer             → navigationview, splitview
breadcrumbs        → breadcrumbbar
tabs               → tabview, pivot, selectorbar
dropdown           → combobox, dropdownbutton
select             → combobox
typeahead          → autosuggestbox
spinner            → progressring
loader             → progressring, progressbar
toast              → infobar, appnotification
snackbar           → infobar, teachingtip
card               → card, border
divider            → divider, rectangle
useState           → usestate
useEffect          → useeffect
useReducer         → usereducer
useMemo            → usememo
useCallback        → usecallback
useRef             → useref
useContext         → usecontext
useNavigate        → usenavigation
useQuery           → useresource
useMutation        → usemutation
useInfiniteQuery   → useinfiniteresource
hook               → use
state              → usestate, usereducer
effect             → useeffect
ref                → useref, elementref
memo               → usememo
callback           → usecallback
context            → usecontext
key                → withkey
className          → style, modifier
className=…        → margin, padding, background
style={…}          → margin, padding
flex               → flexrow, flexcolumn, flex
flexbox            → flexrow, flexcolumn
gap                → vstack, hstack, rowgap, columngap
div                → flexrow, flexcolumn, vstack, hstack
span               → textblock
button             → button
input              → textfield, textbox, numberbox
input[type=text]   → textfield
input[type=number] → numberbox
input[type=checkbox] → checkbox
input[type=radio]  → radiobuttons
input[type=date]   → calendardatepicker, datepicker
textarea           → textbox
table              → datagrid, listview
list               → listview, foreach
grid               → grid, gridview
map                → foreach
.map(              → foreach, select
form               → formfield, usevalidationcontext
label              → caption, formfield
fragment           → group
suspense           → useresource
ErrorBoundary      → errorboundary
…
```

**Reactor-specific abbreviations / common typos:**

```text
vstack       → vstack
hstack       → hstack
flexcol      → flexcolumn
flexrow      → flexrow
elem         → element
btn          → button
txt          → textblock, textfield
img          → image
nav          → navigationview, usenavigation
```

Notable design decision: **React-to-Reactor mappings live in the synonym map, not in a separate React-port category**. The catalogue still has dedicated react-port scenarios (so an agent can `mur find react useState`), but the synonyms ensure that an agent who types `useState` finds the Reactor equivalent without knowing to ask for "react-port."

### 4.8 Pitfall notes (`Notes.cs`)

Same shape as `winui-search/Notes.cs` — `Dictionary<string, string[]>` keyed by the `notesKey` field on each scenario (typically the factory or hook name). Examples:

```csharp
["UseState"] = [
    "UseState with a List<T> does NOT re-render on `.Add()` / `.Remove()` — same reference. Use UseReducer for collections.",
    "UseState returns (value, setter). The setter is stable across renders — safe to omit from dependency arrays.",
    "Call UseState unconditionally at the top of Render. Hooks track slot identity by call order."
],
["UseEffect"] = [
    "Effects run AFTER render commits. Don't read state set inside the same render unless via UseEffect's cleanup or a deps change.",
    "Return a cleanup lambda when the effect subscribes to anything. The cleanup runs before the next effect AND on unmount.",
    "Empty deps `[]` means 'run once on mount' — but the effect still re-runs if the component remounts due to key change."
],
["lists"] = [
    "Lists produced by `items.Select(...).ToArray()` MUST include `.WithKey(item.Id)` on every element. Without keys, focus, animation, and child state drift across reorders.",
    "`UseState<List<T>>` mutating in place does not re-render. Use `UseReducer<TState, TAction>` or `UseCollection`."
],
["UseResource"] = [
    "UseResource re-runs the fetcher when deps change, on retry, and on focus revalidation. Use UseMutation for writes (POST/PUT/DELETE).",
    "Match on `AsyncValue<T>` to render loading / error / data — don't unwrap by checking null."
],
["Theme"] = [
    "Use `Theme.*` tokens (`Theme.PrimaryText`, `Theme.CardBackground`). Hardcoded colors trip `REACTOR_THEME_001`.",
    "`.Resources(r => r.Set(\"ButtonBackground\", …))` applies lightweight styling without a global Theme override."
],
["WithKey"] = [
    "Required on every element produced from `.Select(...)` inside a layout container. Without it the analyzer emits `REACTOR_DSL_001` and reordering breaks focus/animation.",
    "Key must be stable across renders. Don't key by index for reorderable lists — that defeats the purpose."
],
["DataGrid"] = [
    "DataGrid<T> takes an IDataSource<T>, not a raw list. Wrap with `IDataSource.From(items)` for in-memory data; use a custom IDataSource for virtualized fetch.",
    "Column<T>(...) is the column builder. The `accessor` returns the cell value; format with the `format` parameter, don't synthesize strings.",
    "For sortable/filterable in-memory data, pass `IDataSource.From(items)` and the source handles sort/filter internally."
],
["ContentDialog"] = [
    "ContentDialog is non-routed — show it via `UseDialog().Show(...)` from a hook, not by mounting it as a child element.",
    "Primary/secondary/close buttons map to the three result branches. For yes/no/cancel, provide all three texts."
],
…
```

Notes attach when `mur get` returns a scenario; the search index does **not** see them (keeping the index small and the notes readable). The `notesKey` field on each scenario decouples display from search.

## 5. Catalogue enumeration

Three priority bands. **P0 must ship in v1**; **P1 follows in the immediately-subsequent milestone**; **P2 is the long-tail that closes the parity gap with winui-search.**

Counts below are scenarios, not folders. Anti-pattern scenarios are listed but tagged separately and excluded from default `find` results.

### 5.1 P0 — Foundation (≈60 scenarios, v1 ship gate)

**Hooks (16)**
- `use-state-basic` — primitive counter
- `use-state-record` — record-shaped state with structural equality
- `use-state-list-pitfall` *(anti-pattern)* — pairs with `use-reducer-list`
- `use-reducer-list` — list add/delete/toggle via reducer
- `use-reducer-typed` — strongly-typed actions
- `use-effect-mount` — fire-once effect
- `use-effect-cleanup` — subscription with cleanup
- `use-effect-deps` — re-run on dep change
- `use-ref-dom` — element ref handoff to native APIs
- `use-ref-mutable` — non-render mutable storage
- `use-memo` — derived state
- `use-callback` — stable function identity for child props
- `use-context-basic` — provide/consume
- `use-context-multi` — composing contexts
- `use-reducer-with-context` — global state pattern
- `custom-hook-pattern` — naming, return shape, composition

**Layout (10)**
- `vstack-basic` — vertical shrink-wrap stack with spacing
- `hstack-basic` — horizontal shrink-wrap stack
- `flexrow-with-grow` — CSS-flexbox row, one child grows
- `flexcolumn-with-justify` — alignment + justification
- `grid-basic` — column/row sizes, child placement
- `grid-spans` — row/column spans, GridSize types
- `border-with-corner` — padded border with corner radius
- `card-surface` — themed card with border + background + padding
- `scrollviewer-vertical` — scrollable content region
- `canvas-positioning` — absolute positioning with `.Canvas(left, top)`

**Text (6)**
- `textblock-basic`
- `heading-subhead-caption` — type ramp
- `body-bodystrong` — emphasis styles
- `rich-text-inlines` — bold/italic/hyperlink runs
- `text-wrap-truncate` — wrapping and trimming
- `localized-text` — pairs with `reactor-localization`

**Buttons (6)**
- `button-label-onclick`
- `button-with-icon`
- `button-with-command` — `Command` + `.CanExecute`
- `hyperlink-button`
- `togglebutton-basic`
- `appbarbutton-in-commandbar`

**Inputs (10)**
- `textfield-twoway`
- `numberbox-validated`
- `checkbox-bool`
- `toggleswitch`
- `radiobuttons-group`
- `combobox-from-list`
- `combobox-of-elements`
- `autosuggestbox-typeahead`
- `calendardatepicker`
- `slider-range`

**Lists (6)**
- `list-basic-foreach` — `ForEach<T>` for small static lists
- `list-add-delete-toggle` — keyed list with reducer
- `list-with-empty-state` — empty fallback
- `list-with-loading` — loading skeleton
- `master-detail` — list + detail pane
- `virtualized-large-list` — `ItemsRepeater` + virtualizing layout

**Forms (6)**
- `form-text-fields` — TextField + label
- `form-validation-context` — `UseValidationContext`
- `form-field-wrapper` — `FormField(input, label, required)`
- `form-submit-gating` — disable submit until valid
- `form-async-submit` — submit through `UseMutation`
- `form-with-server-errors` — surface server-side validation failures

### 5.2 P1 — High-value (≈90 scenarios, immediately-subsequent milestone)

**Navigation (10)**
- `sidebar-nav-basic`
- `sidebar-nav-with-detail`
- `top-tabs`
- `breadcrumb-trail`
- `drawer-overlay`
- `nested-routes`
- `conditional-route-guard`
- `deep-link-to-item`
- `back-button-system`
- `navigation-lifecycle`

**Async (12)**
- `fetch-with-loading-error`
- `fetch-with-retry`
- `fetch-with-cache`
- `paginated-list`
- `infinite-scroll`
- `optimistic-update`
- `mutation-with-rollback`
- `debounced-search`
- `parallel-fetches`
- `dependent-fetches`
- `async-with-cancellation`
- `useresource-vs-usemutation-pitfall` *(anti-pattern)*

**Theming (10)**
- `themed-card`
- `brush-tokens`
- `runtime-theme-switch`
- `named-style-basic`
- `named-style-based-on`
- `dark-light-override`
- `high-contrast-dict`
- `lightweight-style-override`
- `theme-resource-vs-static`
- `acrylic-surface`

**Animation (4)**
- `visual-state-transition`
- `connected-animation`
- `fade-in-out`
- `list-reorder-animation`

**Accessibility (8)**
- `automation-name-icon-only-button`
- `automation-id-all-controls`
- `focus-trap-dialog`
- `focus-management-on-mount`
- `keyboard-shortcut-accelerator`
- `live-region-announce`
- `reduced-motion-respect`
- `high-contrast-survival`

**Commanding (6)**
- `command-basic`
- `command-with-canexecute`
- `command-shared-across-surfaces`
- `commandhost-wrapping`
- `keyboard-shortcut-binding`
- `context-menu-flyout`

**Charting (12)**
- `linechart-basic`
- `barchart-basic`
- `areachart-basic`
- `piechart-when-appropriate`
- `donut-with-center-label`
- `multi-series-line`
- `line-with-axes-labels`
- `bar-grouped`
- `line-with-tooltip`
- `accessible-chart-alternate-view`
- `treechart-hierarchy`
- `forcegraph-network`

**Data display (10)**
- `datagrid-basic`
- `datagrid-sortable`
- `datagrid-editable`
- `datagrid-virtualized`
- `datagrid-row-detail`
- `treeview-hierarchical`
- `flipview-paged-content`
- `gridview-card-grid`
- `itemsrepeater-custom-layout`
- `annotated-scrollbar-overview`

**Surfaces (8)**
- `contentdialog-confirm`
- `contentdialog-async-result`
- `flyout-attached`
- `menuflyout-context`
- `teachingtip-onboarding`
- `infobar-status`
- `appnotification-toast`
- `commandbarflyout-rich`

**Input / gestures (4)**
- `drag-drop-files`
- `drag-reorder-list`
- `pointer-gestures-basic`
- `focus-rings-keyboard-only`

**Window / shell (6)**
- `mica-backdrop`
- `title-bar-customization`
- `window-state-persistence`
- `tray-icon`
- `open-second-window`
- `system-back-button`

### 5.3 P2 — Long-tail and translation layer (≈80 scenarios)

**React-port pairs (30)** — one scenario per row in the React-to-Reactor mapping table. Each shows the React snippet in a comment, then the Reactor translation. Indexed under `category: react-port` so `mur find "react useState"` finds them but they don't clutter the canonical `hooks/` results.

Examples:
- `react-useState`, `react-useEffect`, `react-useReducer`, `react-useMemo`, `react-useCallback`, `react-useRef`, `react-useContext`, `react-useQuery` (→ UseResource), `react-useMutation`, `react-useInfiniteQuery`
- `react-jsx-children`, `react-fragment`, `react-conditional-render`, `react-list-map-key`, `react-spread-props`
- `react-component-state`, `react-lift-state-up`, `react-context-provider`, `react-error-boundary`, `react-suspense`
- `react-flex-layout`, `react-grid-layout`, `react-className-to-modifier`, `react-style-prop`, `react-input-onChange`
- `react-controlled-input`, `react-uncontrolled-input`, `react-form-submit`, `react-router-link`, `react-router-route`

**Cross-framework intent (20)** — one scenario keyed off each of the cross-framework synonyms (`modal`, `popup`, `sidebar`, `drawer`, `dropdown`, `select`, `picker`, `tabs`, `breadcrumbs`, `divider`, `card`, `accordion`, `stepper`, `chip`, `toast`, `snackbar`, `banner`, `tooltip`, `spinner`, `loader`). Each demonstrates the Reactor factory that the synonym resolves to, with the foreign vocabulary explicitly mentioned in the intent string so BM25 picks it up.

**Specialized controls (20)**
- `mapcontrol-pin`
- `webview2-navigate-and-script`
- `mediaplayer-video`
- `personpicture-avatar`
- `ratingcontrol`
- `pipspager-paginated`
- `animatedicon-state-transition`
- `colorpicker-with-accents`
- `progressring`
- `progressbar-determinate`
- `expander-settings`
- `breadcrumbbar`
- `passwordbox`
- `splitbutton-default-action`
- `dropdownbutton-with-flyout`
- `selectorbar-modern-pivot`
- `pivot-static-sections`
- `annotatedscrollbar`
- `flipview-template`
- `richeditbox-formatted-text`

**Anti-patterns (10)** — excluded from default `find`, surface via `--include-anti-patterns`. Each pairs with a positive scenario via `relatedIds`.
- `usestate-list-mutation` ↔ `use-reducer-list`
- `hook-inside-if` ↔ `custom-hook-pattern`
- `hook-outside-render` ↔ `use-state-basic`
- `unkeyed-list` ↔ `list-add-delete-toggle`
- `hardcoded-color` ↔ `themed-card`
- `single-file-without-platform` ↔ `use-state-basic`
- `useresource-for-mutation` ↔ `form-async-submit`
- `iValueConverter-attempt` ↔ `react-conditional-render`
- `scrollviewer-around-listview` ↔ `virtualized-large-list`
- `border-cursor-attempt` ↔ `pointer-gestures-basic`

### 5.4 Total

| Band | Count | Bound to ship |
|---|---|---|
| P0 | 60 | v1 — required before merge |
| P1 | 90 | v1+1 milestone |
| P2 | 80 | v1+2 milestone |
| **Total** | **230** | |

230 hand-authored scenarios is parity-comparable to winui-search (gallery has ~150 unique controls × multi-scenario; effective searchable count is ~400 but the agent-relevant subset is ~200).

## 6. Migration

### 6.1 `reactor-recipes` deletion

- Port the 6 existing recipes (`list-add-delete.cs`, `sidebar-nav.cs`, `form-with-validation.cs`, `async-fetch-list.cs`, `themed-card.cs`, `calendar-multiselect.cs`, `canvas-positioning.cs`, `named-styles.cs`, `use-custom-hook.cs`) into `samples/scenarios/` at their respective categories. They become P0 scenarios — see §5.1 for placement.
- Delete `plugins/reactor/skills/reactor-recipes/` entirely.
- Remove the `reactor-recipes` row from `plugins/reactor/agents/reactor-dev.agent.md`'s "When to load each skill" table.
- Replace any other doc references with a pointer to `mur find` (one pass with Grep; expect <10 hits).
- Bump `plugin.json` minor version.

### 6.2 `samples/` is untouched

`samples/apps/`, `samples/NavigationDemo/`, `samples/TodoApp/`, `samples/ReactorGallery/`, etc., remain as application-level showcase apps. The new `samples/scenarios/` tree is sibling, not a replacement. Long-term we could index `samples/apps/` too (NG1), but v1 doesn't.

### 6.3 `reactor.api.txt` stays

`mur --api` and the flat signatures index remain the canonical reference for "what's the exact signature?" `mur find` complements it; doesn't replace it. The agent docs (reactor-dev.agent.md) get a new line:

> Discovery: `mur find "<intent>"` → ranked scenario list → `mur get <id>` for full code + pitfalls.
> Reference: `mur --api` for flat signatures when you need a name you can't find by intent.

### 6.4 Skill updates

- `reactor-getting-started/SKILL.md` — add the `mur find` callout (§4.6) above the existing "Use a `.csproj` …" section.
- `reactor-design/SKILL.md` — add a parallel callout for design-time exploration (mirrors `winui-design/SKILL.md`'s pattern).
- `reactor-charts/SKILL.md` — point at the 12 charting scenarios in P1.
- `reactor-async/SKILL.md`, `reactor-forms/SKILL.md`, `reactor-navigation/SKILL.md`, `reactor-commanding/SKILL.md`, `reactor-input/SKILL.md` — add a one-line "find canonical examples with `mur find` filtered to this category" pointer.

## 7. Open questions

### Resolved

- **OQ-1 (resolved).** `Scenario.cs` is **single-file** using `#:package`/`#:property` headers — the compact authoring shape wins. Analyzers don't load on single-file builds; authors run `mur check` against a temporary `.csproj` by hand when authoring an analyzer-specific scenario (rare). Catalogue extraction validates Roslyn compilation only; analyzer coverage is not part of the gate.
- **OQ-2 (resolved).** Top-level CLI: `mur find <query>` and `mur get <id>`, mirroring `mur check`. No nested `mur find get`.
- **OQ-3 (resolved).** **No separate binary.** `mur find` is a subcommand of the existing `mur.exe`; `scenarios.json` ships as a manifest resource on `Reactor.Cli` and as a sibling file under the NuGet `agentkit/` payload. The skill bundle does not include a redistributable exe.
- **OQ-6 (resolved).** 30 person-hours for the P0 catalogue is accepted as the v1 authoring budget. No catalogue-jam needed; single author across Phase 2.

### Still open

- **OQ-4.** Synonym map authority post-ship — it's the highest-leverage file in the tool; a missing alias degrades agent hit rate silently. *Suggest: file lives in `src/Reactor.Cli/Find/Synonyms.cs` (versioned with code); changes go through normal PR review with an "is the synonym well-motivated" rubric. Eval harness (OQ-5) is the regression backstop.*
- **OQ-5.** Eval harness — a `tests/Reactor.Cli.Find.Tests/` Spec-038-style eval that measures find-quality on ~30 canned queries (precision@5, MRR). Without it, synonym-map regressions ship silently. *Lean: yes, authored alongside Phase 3 (P1 catalogue), not gating Phase 2.*

## 8. Phased rollout

**Phase 0 — Spec & alignment (this doc)**
- Review with @andersonch; resolve open questions OQ-1, OQ-2, OQ-3.

**Phase 1 — Tool skeleton (no catalogue content)**
- Port `winui-search`'s `BM25.cs`, `SearchEngine.cs`, `StopWords.cs`, `DataLoader.cs`, `Models.cs` into `src/Reactor.Cli/Find/`.
- Wire `mur find` + `mur get` + `mur list` into `Program.cs` (mirror existing `check` wiring).
- Stub catalogue: one scenario (`use-state-basic`) so the smoke test passes.
- Stub extractor: `tools/Reactor.SampleCatalogue/` walks the (one-entry) tree, validates Roslyn compilation, emits JSON.
- CI wiring: extractor runs in the build, scenarios.json is regenerated and diffed; PRs that touch scenarios but don't regen the JSON fail.

**Phase 2 — P0 catalogue (60 scenarios)**
- Author all P0 scenarios (§5.1) using the contract in §4.2.
- Author the Reactor-tuned synonym map (§4.7).
- Author the `Notes.cs` for the P0 factory anchors (§4.8).
- Delete `reactor-recipes` after the 6 ports land (§6.1).
- Skill callouts in `reactor-getting-started` and `reactor-design`.
- Ship behind `mur find` as the public surface; declare v1.

**Phase 3 — P1 catalogue (90 scenarios) + eval harness**
- Expand to §5.2 catalogue.
- Author `tests/Reactor.Cli.Find.Tests/` (OQ-5): canned-query eval, precision@5, MRR.
- Synonym map expansion driven by eval-failure analysis.

**Phase 4 — P2 catalogue (80 scenarios)**
- React-port + cross-framework + specialized + anti-pattern scenarios (§5.3).
- Skill callouts in `reactor-charts`, `reactor-async`, `reactor-forms`, `reactor-navigation`, `reactor-commanding`, `reactor-input`.

**Phase 5 — Operational**
- `winui-search`-style `update`? Not for v1 (NG2); reconsider if downstream consumers ship behind a frozen NuGet and need a way to re-fetch.

## 9. Success metrics

- **Coverage:** every public factory in `reactor.api.txt` is reachable from at least one scenario by Phase 4 (verified by extractor cross-check).
- **Search quality (post-Phase 3 eval):** precision@5 ≥ 0.8 on the canned eval set; MRR ≥ 0.7. Compare against winui-search's published-internally baseline.
- **Agent-loop impact:** measure mean `mur check` round-trips per task before/after `mur find` lands. Target: ≥25% reduction in fix-up iterations on tasks that match a P0/P1 scenario.
- **Catalogue freshness:** zero CI failures from drift — the extractor's compile gate guarantees the catalogue compiles against every Reactor build.

## Appendix A — Full phrase table (deferred)

To be populated in Phase 2 against winui-search's `Synonyms.Phrases` as the starting set, augmented with the React/Reactor phrases listed inline in §4.7.

## Appendix B — Full synonym table (deferred)

To be populated in Phase 2. Pre-seed from winui-search's `Synonyms.Map` + the React-to-Reactor table from `reactor-getting-started/SKILL.md` + the cross-framework intents in §5.3.

## Appendix C — Pitfall notes catalogue (deferred)

Populate alongside Phase 2 catalogue authoring. Each P0 factory anchor in §5.1 gets ≥2 notes; reactor's existing `reactor-build-and-check/SKILL.md` cheat table (the `REACTOR_*` ID rows) is the source-of-truth for analyzer-anchored pitfalls.
