# Reactor Localization System — Implementation Tasks

Reference: [docs/specs/005-localization-design.md](../005-localization-design.md)

> Do NOT auto-localize any major apps/samples. The human will manually localize those after implementation is complete, as a validation exercise.

---

## Phase 1: Foundation (Runtime Core)

### 1.1 Core Types & Interfaces
- [x] Add `MessageFormat` NuGet package dependency to `Reactor.csproj`
- [x] Create `Reactor/Core/Localization/MessageKey.cs` — record struct with `Namespace` and `Key` properties
- [x] Create `Reactor/Core/Localization/NumberFormatOptions.cs` — options type with `NumberStyle` enum (Default, Currency, Percent)
- [x] Create `Reactor/Core/Localization/DateFormatOptions.cs` — options type with `DateStyle` enum (Short, Long, Full, Default)
- [x] Create `Reactor/Core/Localization/ListFormatType.cs` — enum (Conjunction, Disjunction)

### 1.2 IntlAccessor
- [x] Create `Reactor/Core/Localization/IntlAccessor.cs` implementing the full API:
  - `Locale` property (string)
  - `Direction` property (FlowDirection)
  - `IsRtl` convenience property
  - `Message(MessageKey key, object? args = null)` — loads string via ResourceLoader, formats via MessageFormat
  - `FormatNumber(double value, NumberFormatOptions? options = null)` — delegates to .NET CultureInfo
  - `FormatDate(DateTimeOffset value, DateFormatOptions? options = null)` — delegates to .NET CultureInfo
  - `FormatList(IEnumerable<string> values, ListFormatType type)` — locale-aware list join
- [x] Create `Reactor/Core/Localization/RtlHelper.cs` — static `IsRtlLocale(string locale)` with CLDR-sourced RTL language set

### 1.3 ICU Message Cache
- [x] Create `Reactor/Core/Localization/MessageCache.cs` — `ConcurrentDictionary<(string locale, string key), compiled>` caching parsed ICU patterns
- [x] Wire `MessageFormat` engine into cache: parse on first access, reuse thereafter
- [x] Add `Flush()` and `Flush(string locale)` methods for locale switching / hot reload

### 1.4 ResourceLoader Integration
- [x] Create `Reactor/Core/Localization/IStringResourceProvider.cs` — interface abstracting string loading (enables testing without MRT)
- [x] Create `Reactor/Core/Localization/ReswResourceProvider.cs` — implementation wrapping WinUI `ResourceLoader`
  - Handles namespace-to-ResourceLoader mapping (single `Resources.resw` vs. multiple named `.resw` files)
  - Falls back to default locale when key is missing in current locale
- [x] Add debug-mode logging for missing keys: `[Reactor.Intl] Missing key '{ns}.{key}' for locale '{locale}', falling back to en-US`

### 1.5 LocaleProvider Element
- [x] Create `Reactor/Elements/LocaleProviderElement.cs` — Reactor element that:
  - Stores locale string in context
  - Creates/caches `IntlAccessor` for the active locale
  - Sets `FlowDirection` on the root WinUI element when locale changes
  - Flushes message cache on locale change
- [x] Add DSL factory: `public static Element LocaleProvider(string locale, params Element[] children)`
- [x] Handle re-render propagation: components that called `UseIntl()` re-render on locale change

### 1.6 UseIntl Hook
- [x] Add `UseIntl()` method to `RenderContext` that:
  - Reads `IntlAccessor` from the nearest `LocaleProvider` context
  - Returns a safe default accessor (OS locale, direct ResourceLoader) when no provider exists
  - Subscribes the calling component for re-render on locale change

### 1.7 Fallback Behavior
- [x] Implement fallback chain: current locale -> default locale (en-US)
- [x] Debug mode: log warning on fallback
- [x] Release mode: silent fallback, no logging

### Phase 1 Testing
- [x] Unit test `MessageKey` — equality, hashing, string representation
- [x] Unit test `RtlHelper.IsRtlLocale` — known RTL locales (ar, he, fa, ur), known LTR (en, fr, de, ja), edge cases (region subtags like ar-SA)
- [x] Unit test `MessageCache` — cache hit, cache miss, flush, concurrent access
- [x] Unit test `IntlAccessor.Message()` — simple string, ICU plurals, ICU select, missing key fallback
- [x] Unit test `IntlAccessor.FormatNumber()` — currency, percent, default for en-US and a non-English locale
- [x] Unit test `IntlAccessor.FormatDate()` — short, long, full styles
- [x] Unit test `IntlAccessor.FormatList()` — conjunction ("A, B, and C"), disjunction ("A, B, or C")
- [x] Integration test: `IStringResourceProvider` mock with known .resw content -> IntlAccessor returns correct strings
- [x] Integration test: `LocaleProvider` wrapping a component -> `UseIntl()` returns correct locale/direction
- [x] Integration test: locale switch (en-US -> ar-SA) -> direction flips, strings change, cache is flushed

---

## Phase 2: Source Generator + Pseudolocalization

### 2.1 Source Generator Project Setup
- [x] Create new project `Reactor.Localization.Generator` (C# source generator, targets `netstandard2.0`)
- [x] Add to `Reactor.sln`
- [x] Reference from `Reactor.csproj` as analyzer/source generator

### 2.2 .resw Parser
- [x] Implement `.resw` XML parser in the generator — reads `<data name="..."><value>...</value></data>` entries
- [x] Support single-file layout (`Resources.resw` -> flat keys) and multi-file layout (`Common.resw`, `Settings.resw` -> namespaced keys)
- [x] Parse `<comment>` elements for metadata (ai-translated markers, etc.)

### 2.3 Loc.g.cs Code Emission
- [x] Emit `Loc` static class with nested classes per namespace (one per `.resw` file name)
- [x] Each key becomes `public static readonly MessageKey {Name} = new("{Namespace}", "{Key}");`
- [x] Add `/// <summary>` XML doc with the default-locale (en-US) value for IntelliSense
- [x] Add `[GeneratedCode]` attribute on the class
- [x] Auto-detect flat vs. namespaced from number of `.resw` files in default locale folder

### 2.4 Missing Key Diagnostics
- [x] Cross-reference all locale folders at build time
- [x] Emit `REACTOR_LOC001` warning for keys in default locale missing in other locales
- [x] Support `ReactorLocMissingKeySeverity` MSBuild property to promote warnings to errors

### 2.5 MSBuild Integration
- [x] Add `ReactorLocDefaultLocale` property (default: `en-US`)
- [x] Add `ReactorLocStringsPath` property (default: `Strings/`)
- [x] Wire `.resw` files as `AdditionalFiles` for the source generator to consume
- [x] Add `ReactorPseudoLocalize` MSBuild property

### 2.6 Pseudolocalization
- [x] Create `Reactor/Core/Localization/PseudoLocalizer.cs` — transforms strings:
  - ASCII -> accented equivalents (a->å, e->é, etc.)
  - Wrap in `[!! ... !!]` markers
  - Pad ~30% longer to simulate expansion
  - Preserve ICU syntax (`{variables}`, `{count, plural, ...}`) untouched
- [x] Missing key pseudo: render as `[?? Namespace.Key ??]` (distinct from localized pseudo)
- [x] Wire into `IntlAccessor.Message()` when pseudolocalization is enabled
- [x] Support runtime toggle: `LocaleProvider(locale, pseudoLocalize: true, children)`

### Phase 2 Testing
- [x] Unit test source generator: given a sample `.resw` file, verify emitted `Loc.g.cs` content
- [x] Unit test source generator: flat layout (single .resw) -> no nested classes
- [x] Unit test source generator: multi-file layout -> correct nested class hierarchy
- [x] Unit test source generator: missing key diagnostic fires for incomplete locale
- [x] Unit test `PseudoLocalizer` — basic string transformation, ICU preservation, expansion padding
- [x] Integration test: build a test project with `.resw` files -> `Loc.X.Y` compiles and resolves at runtime
- [x] Integration test: pseudolocalization mode -> all `t.Message()` output wrapped in `[!! ... !!]`

---

## Phase 3: CLI — Extract

### 3.1 CLI Project Setup
- [x] Create new project `Reactor.Cli` (console app, `dotnet tool` packaging)
- [x] Add Roslyn (`Microsoft.CodeAnalysis.CSharp`) dependency for AST parsing
- [x] Set up `duct-loc` command structure with subcommands: `extract`, `translate`, `validate`, `status`, `prune`

### 3.2 AST Scanner
- [x] Implement Roslyn-based scanner that parses `.cs` files and walks the syntax tree
- [x] Detect Reactor DSL patterns — localizable call sites:
  - `Text(literal)`, `Heading(literal)`, `Button(literal, ...)`
  - `.Placeholder(literal)`, `.Header(literal)`, `.ToolTip(literal)`, `.Title(literal)`
  - `.AutomationName(literal)` (localizable), `.HelpText(literal)` (localizable)
  - Skip `.AutomationId(literal)` (not localizable)
- [x] Detect string interpolation (`$"..."`) and convert to ICU message format:
  - Simple variable: `{name}` -> `{name}`
  - Dotted expression: `{user.Name}` -> `{name}` (last segment, camelCase)
  - Format specifiers: `:C` -> currency, `:P` -> percent, `:d` -> date short, etc.
  - Skip method calls and complex expressions with warning
- [x] Detect ternary expressions: `x ? "A" : "B"` -> extract both branches as separate keys
- [x] Detect null-coalescing: `val ?? "Default"` -> extract literal side only
- [x] Skip strings already wrapped in `t.Message()`
- [x] Skip non-DSL string arguments, log messages, exception messages

### 3.3 Key Naming
- [x] Generate hierarchical keys from code context: `{ClassName}.{ContextHint}`
  - Strip common suffixes (Page, Component, View) from class name
  - Generate hint from string content (PascalCase, truncated)
- [x] Ensure uniqueness: same English string in different components -> different keys
- [x] Add plural hint comment when variable name suggests quantity (count, total, num*)

### 3.4 .resw Generation
- [x] Create/update `.resw` XML files in the output directory
- [x] Idempotent: re-running on already-extracted code produces no changes
- [x] Preserve existing keys/values/comments — only add new entries
- [x] Alphabetical key ordering within each `.resw` file

### 3.5 Source Rewrite (`--rewrite`)
- [x] Replace bare string literals with `t.Message(Loc.X.Y)` calls
- [x] Replace interpolated strings with `t.Message(Loc.X.Y, new { ... })` calls
- [x] Insert `var t = UseIntl();` declaration if not already present in the method
- [x] Handle ternary rewriting: both branches get separate `t.Message()` calls
- [x] Handle null-coalescing rewriting: literal side replaced with `t.Message()`
- [x] Preserve formatting/whitespace as much as possible

### 3.6 Dry Run & CI Mode
- [x] `--dry-run` flag: report what would change without writing files
- [x] Non-zero exit code when unextracted strings are found (for CI gating)
- [x] Clear output format: `[NEW]`, `[SKIP]`, `[REWRITE]`, `[WARN]` prefixes

### Phase 3 Testing
- [x] Unit test AST scanner: simple `Text("Hello")` -> detected with correct context
- [x] Unit test AST scanner: `Button("Save", handler)` -> first arg detected as localizable
- [x] Unit test AST scanner: interpolation `$"Hello, {name}"` -> ICU conversion `Hello, {name}`
- [x] Unit test AST scanner: format specifiers (`:C`, `:P`, `:d`) -> correct ICU types
- [x] Unit test AST scanner: ternary extraction -> two separate keys
- [x] Unit test AST scanner: null-coalescing -> literal side only
- [x] Unit test AST scanner: `t.Message()` already present -> skipped
- [x] Unit test AST scanner: `.AutomationId("x")` -> not extracted
- [x] Unit test key naming: class name + string content -> correct PascalCase key
- [x] Unit test .resw generation: new keys added, existing keys preserved
- [x] Unit test source rewrite: bare string -> `t.Message(Loc.X.Y)` with correct formatting
- [x] Unit test source rewrite: interpolation -> `t.Message(Loc.X.Y, new { ... })`
- [x] Unit test source rewrite: `UseIntl()` declaration inserted when missing
- [x] Integration test: extract on a sample component file -> correct .resw output + rewritten source
- [x] Integration test: idempotency — extract twice, second run produces no changes
- [x] Integration test: `--dry-run` returns non-zero when unextracted strings exist

---

## Phase 4: Rich Text & Layout

### 4.1 RichMessage API
- [x] Implement `IntlAccessor.RichMessage(MessageKey key, object? args, Dictionary<string, Func<string, Element>>? tags)`:
  - Parse ICU message to get formatted string with `<tag>content</tag>` markers
  - Map tags to element factories provided by the caller
  - Return a `GroupElement` containing the resulting child elements (text spans + wrapped elements)
- [x] Handle nested tags and plain text segments between tags

### 4.2 Logical Layout Modifiers
- [x] Add `MarginInlineStart(double)` modifier — resolves to left in LTR, right in RTL
- [x] Add `MarginInlineEnd(double)` modifier
- [x] Add `PaddingInlineStart(double)` modifier
- [x] Add `PaddingInlineEnd(double)` modifier
- [x] Add `BorderInlineStart(...)` modifier
- [x] Resolve based on current `FlowDirection` at mount/update time

### 4.3 UseLocalizedAsset Hook
- [x] Implement `IntlAccessor.Asset(string path)` — resolves to locale-qualified asset path via MRT
- [x] Fall back to unqualified path if no locale-specific asset exists

### Phase 4 Testing
- [x] Unit test `RichMessage`: simple tag `<bold>text</bold>` -> correct element mapping
- [x] Unit test `RichMessage`: multiple tags in one message -> correct ordering
- [x] Unit test `RichMessage`: tags with ICU arguments -> arguments resolved, tags mapped
- [x] Unit test `RichMessage`: no tags -> falls back to plain text element
- [x] Unit test logical modifiers: LTR -> `MarginInlineStart` = left margin
- [x] Unit test logical modifiers: RTL -> `MarginInlineStart` = right margin
- [x] Integration test: `RichMessage` in a rendered component -> correct visual tree

---

## Phase 5: CLI — Translate & Validate

### 5.1 AI Translation
- [x] Create AI provider abstraction: `ITranslationProvider` interface
- [x] Implement GitHub Copilot SDK provider (replaces Azure OpenAI / OpenAI / Anthropic — single provider via `gh` CLI)
- [x] Build ICU-aware system prompt:
  - Explain ICU syntax to preserve `{variables}`, `{count, plural, ...}`
  - Include locale-specific instructions (formal/informal register, script)
  - Include existing translations for consistency context
- [x] Batch translation: group keys, send in batches to reduce API calls
- [x] Mark AI translations with `<comment>ai-translated: pending-review</comment>`

### 5.2 `duct-loc translate` Command
- [x] `--source` flag for source locale directory
- [x] `--target` flag for comma-separated target locales
- [x] `--missing-only` flag to only translate missing/draft keys
- [x] `--model` flag to select model (via Copilot SDK)
- [x] Progress output showing keys translated per locale

### 5.3 `duct-loc validate` Command
- [x] Parse all `.resw` files and validate ICU syntax (catch unmatched braces, bad syntax)
- [x] Check parameter consistency: all locales should use the same `{param}` names as the source
- [x] Report missing keys per locale
- [x] Report encoding issues
- [x] Exit code: non-zero if any errors found

### 5.4 `duct-loc status` Command
- [x] Read all locale folders and count: total keys, translated, AI-draft (pending-review), missing
- [x] Output table: Locale / Keys / Translated / AI-Draft / Missing / Coverage %

### 5.5 `duct-loc prune` Command
- [x] Scan all `.cs` files for `Loc.{Namespace}.{Key}` references
- [x] Compare against all keys in `.resw` files
- [x] Report (or remove with `--dry-run` / default) keys with zero references
- [x] Idempotent, safe for CI

### Phase 5 Testing
- [x] Unit test ICU-aware system prompt generation — correct syntax instructions per locale
- [x] Unit test AI provider abstraction — mock provider returns translations, correctly written to .resw
- [x] Unit test `validate`: valid ICU -> pass, broken ICU -> error reported
- [x] Unit test `validate`: parameter mismatch -> warning reported
- [x] Unit test `status`: correct counts from sample .resw files
- [x] Unit test `prune`: unused key detected, referenced key preserved
- [x] Integration test: `translate --missing-only` fills only missing keys, leaves existing
- [x] Integration test: `validate` on a fully correct set -> zero errors
- [x] Integration test: `prune --dry-run` reports but does not delete

---

## Phase 6: Polish & Dev Experience

### 6.1 Hot Reload
> **Deferred.** Separate file watcher for .resw is unnecessary complexity — the existing code hot reload path handles rebuild-required changes, and value-only edits are too niche to justify a separate infrastructure.

### 6.2 CI Integration Examples
- [x] Add example Azure Pipelines YAML snippet for localization checks (extract --dry-run, prune --dry-run, validate)
- [x] Document in `docs/reference/localization-ci.md`

### Phase 6 Testing
- [ ] Manual test: run full CI gate commands on a sample project -> clean pass

---

## Final Validation (Manual)

After all phases are complete, the human will manually:
- [ ] Localize a sample app end-to-end using the implemented system
- [ ] Run `duct-loc extract --rewrite` on a real app
- [ ] Add ICU plural/select messages to .resw files
- [ ] Run `duct-loc translate` to generate translations
- [ ] Test runtime locale switching (LTR and RTL)
- [ ] Test pseudolocalization mode to find missed strings
- [ ] Run `duct-loc validate` and `duct-loc status`
