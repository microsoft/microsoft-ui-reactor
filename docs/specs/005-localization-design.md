# Reactor Localization System — Design Spec

## 1. Background & Motivation

Microsoft.UI.Reactor (Reactor) is a React-inspired declarative UI framework over WinUI 3, using pure C# with no XAML.
Today, text is passed as raw strings in `Render()`:

```csharp
Text("Hello, World!")
Button("Save", onSave)
```

There is no localization story. The one existing pattern (the regedit sample) uses static
`const string` fields in a `Strings.cs` class — a placeholder that doesn't scale.

This spec designs a production-grade localization system that:
- Aligns with the **Windows ecosystem** (MRT, `.resw`, OS language negotiation)
- Adds **ICU Message Format** on top for pluralization, gender, and formatting
- Supports **rich text, layout direction (BiDi/RTL)**, and adaptive properties
- Provides **CLI extraction tooling** for an author-in-English → extract → translate workflow
- Enables **AI-assisted translation** with human review

---

## 2. Prior Art

### 2.1 Windows / WinUI 3: MRT + .resw

The standard Windows localization mechanism:

```
Strings/
  en-US/Resources.resw          ← XML key/value pairs
  fr-FR/Resources.resw
  ar-SA/Resources.resw
```

```xml
<data name="Save" xml:space="preserve">
  <value>Save</value>
</data>
<data name="WelcomeMessage" xml:space="preserve">
  <value>Welcome, {0}!</value>
</data>
```

**XAML access**: `x:Uid="Save"` auto-binds `.Content`, `.Text`, etc. (not usable by Reactor)
**Code access**: `new ResourceLoader().GetString("Save")`

**Strengths**: OS language negotiation, MSIX per-language resource packs, Multilingual App
Toolkit (MAT) integration, Windows translator ecosystem familiarity.

**Limitations**: No pluralization, no ICU support, no interpolation beyond `string.Format`,
string-typed keys (typos are runtime errors), `x:Uid` requires XAML.

### 2.2 React: react-intl / FormatJS

Provider + hook pattern with ICU Message Format:

```jsx
const intl = useIntl();
intl.formatMessage({ id: 'itemCount' }, { count: 5 })
// ICU message: "{count, plural, one {# item} other {# items}}"
```

**Strengths**: ICU is the industry standard for professional translation. Handles plurals,
gender/select, number/date formatting in the message itself. Every major translation
platform (Crowdin, Phrase, Lokalise) supports it natively.

### 2.3 React: i18next

Namespace-based code splitting, robust fallback chains, plugin ecosystem:

```jsx
const { t } = useTranslation('settings');
t('itemCount', { count: 5 })
// JSON: "itemCount_one": "{{count}} item", "itemCount_other": "{{count}} items"
```

**Strengths**: Namespace splitting for large apps, `count`-based plural key suffixes
aligned with CLDR, selector-based TypeScript type safety.

### 2.4 LinguiJS

Compile-time macro extraction + ICU messages:

```jsx
<Trans>Read the <a href="/docs">documentation</a>.</Trans>
// Macro extracts message ID automatically, pre-compiles ICU at build time
```

**Key insight**: Messages can be **pre-compiled into functions at build time**, eliminating
runtime ICU parsing. This is the model we want for Reactor.

### 2.5 ICU on Windows — Is This Done?

ICU Message Format is not a Windows-native concept, but it's fully compatible:

- **.NET 6+ ships with ICU data** (`System.Globalization` uses ICU internally for
  `CultureInfo`, plural rules, date/number formatting via CLDR)
- **`MessageFormat`** is an actively-maintained .NET ICU Message Format library
  (NuGet, supports plural/select/number/date, ~50K downloads)
- **MessageFormat.NET** is an older alternative
- Storing ICU-formatted strings in `.resw` values is just strings — MRT doesn't care what
  the string content is; the ICU processing happens at runtime after retrieval

The approach of "ICU messages stored in .resw, processed at runtime by a .NET ICU engine"
is architecturally sound and gives us the best of both ecosystems.

---

## 3. Design Goals

1. **Windows-native storage** — `.resw` files as the resource backend; MRT handles language
   negotiation, MSIX packaging, and fallback chains
2. **ICU Message Format** — full pluralization, gender/select, number/date formatting in
   message strings, processed at runtime
3. **Hook-first API** — `UseLocalization()` hook consistent with Reactor's model
4. **Type-safe keys** — source generator produces typed accessors from `.resw` files;
   missing keys are compile-time errors
5. **Rich text support** — localized messages can contain inline formatting (bold, links)
   via tagged ICU syntax mapped to Reactor elements
6. **BiDi / RTL layout** — automatic `FlowDirection` propagation, logical layout properties
7. **CLI extraction** — idempotent tool scans C# AST, extracts localizable content,
   generates `.resw` skeleton for new locales
8. **AI-assisted translation** — CLI integrates with LLM APIs to pre-fill translations
   from English source, with human validation workflow
9. **Incremental adoption** — apps can localize progressively; bare `Text("Hello")` still
   compiles and works (just isn't localizable until extracted)

---

## 4. Architecture

### 4.1 Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        BUILD TIME                                │
│                                                                  │
│  ┌──────────────┐    Source Gen    ┌─────────────────────────┐   │
│  │ en-US/       │ ──────────────►  │ Messages.g.cs           │   │
│  │ Resources.resw│                 │  static class Messages  │   │
│  └──────────────┘                  │  {                      │   │
│                                    │    MessageKey Greeting;  │   │
│                                    │    MessageKey ItemCount; │   │
│                                    │    ...                   │   │
│                                    │  }                      │   │
│                                    └─────────────────────────┘   │
│                                                                  │
│  ┌──────────────┐   duct-loc CLI   ┌────────────────────────┐   │
│  │ C# source    │ ──────────────►  │ en-US/Resources.resw   │   │
│  │ (AST scan)   │   (extract)      │ (new keys added)       │   │
│  └──────────────┘                  └────────────────────────┘   │
│                                                                  │
│  ┌──────────────┐   duct-loc CLI   ┌────────────────────────┐   │
│  │ en-US .resw  │ ──────────────►  │ fr-FR/Resources.resw   │   │
│  │ (source)     │   (translate)    │ ar-SA/Resources.resw   │   │
│  └──────────────┘                  │ (AI pre-filled)        │   │
│                                    └────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                        RUNTIME                                   │
│                                                                  │
│  ┌──────────────┐                  ┌─────────────────────────┐  │
│  │ MRT / OS     │ ── negotiates ──►│ ResourceLoader          │  │
│  │ (user prefs, │    language      │ (loads winning locale)  │  │
│  │  fallbacks)  │                  └───────────┬─────────────┘  │
│  └──────────────┘                              │                 │
│                                                ▼                 │
│                                    ┌─────────────────────────┐  │
│                                    │ ICU MessageFormat Engine │  │
│                                    │ (MessageFormat │  │
│                                    │  or equivalent)          │  │
│                                    └───────────┬─────────────┘  │
│                                                │                 │
│                                                ▼                 │
│  ┌──────────────┐                  ┌─────────────────────────┐  │
│  │  Component   │ ◄── UseIntl() ── │ IntlContext             │  │
│  │  Render()    │     hook         │ (locale, direction,     │  │
│  │              │                  │  format helpers)        │  │
│  └──────────────┘                  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 Layer Responsibilities

| Layer | Responsibility | Technology |
|-------|---------------|------------|
| **Storage** | Persist translations per locale | `.resw` files (MRT standard) |
| **Negotiation** | Pick best locale from user prefs | MRT / `Windows.Globalization.ApplicationLanguages` |
| **Loading** | Read strings for active locale | `ResourceLoader` (WinUI 3) |
| **Formatting** | ICU parse + interpolate + plural/select | `MessageFormat` (NuGet) |
| **Type safety** | Compile-time key validation | C# Source Generator |
| **Hook API** | Expose to components, trigger re-render | `UseIntl()` hook on `RenderContext` |
| **Layout** | BiDi/RTL flow direction | `FlowDirection` on root + logical properties |
| **Extraction** | Scan source → update .resw | CLI tool (`duct-loc extract`) |
| **Translation** | AI pre-fill + human review | CLI tool (`duct-loc translate`) |

---

## 5. Developer API

### 5.1 The UseIntl Hook

```csharp
public sealed class IntlAccessor
{
    /// <summary>Current locale (e.g., "en-US", "ar-SA")</summary>
    public string Locale { get; }

    /// <summary>Current text direction</summary>
    public FlowDirection Direction { get; }

    /// <summary>Format a message with optional ICU arguments</summary>
    public string Message(MessageKey key, object? args = null);

    /// <summary>Format a message that contains rich text tags, returning Element</summary>
    public Element RichMessage(MessageKey key, object? args = null,
        Dictionary<string, Func<string, Element>>? tags = null);

    /// <summary>Format a number for the current locale</summary>
    public string FormatNumber(double value, NumberFormatOptions? options = null);

    /// <summary>Format a date for the current locale</summary>
    public string FormatDate(DateTimeOffset value, DateFormatOptions? options = null);

    /// <summary>Format a list for the current locale (e.g., "A, B, and C")</summary>
    public string FormatList(IEnumerable<string> values, ListFormatType type = ListFormatType.Conjunction);
}
```

Hook on `RenderContext`:

```csharp
public IntlAccessor UseIntl()
{
    // Reads from IntlContext (set by LocaleProvider at app root)
    // Subscribes: re-renders this component when locale changes
}
```

### 5.2 Simple Text

**Developer authors in English directly:**

```csharp
class SettingsPage : Component
{
    public override Element Render()
    {
        var t = UseIntl();

        return VStack(
            // Simple string — key is Loc.Settings.Title
            Heading(t.Message(Loc.Settings.Title)),

            // Interpolation — .resw value: "Logged in as {name}"
            Text(t.Message(Loc.Settings.LoggedInAs, new { name = user.Name })),

            // Plurals — .resw value: "{count, plural, one {# item} other {# items}} in cart"
            Text(t.Message(Loc.Cart.ItemCount, new { count = cart.Count })),

            Button(t.Message(Loc.Common.Save), onSave)
        );
    }
}
```

### 5.3 Rich Text

For messages containing inline formatting (bold, links, etc.), the ICU message uses
XML-like tags that map to Reactor element factories:

**.resw value:**
```
Click <link>here</link> to read the <bold>documentation</bold>.
```

**Component:**
```csharp
t.RichMessage(Loc.Help.ClickHere, tags: new()
{
    ["link"]  = text => Hyperlink(text, "https://docs.example.com"),
    ["bold"]  = text => Text(text).Bold(),
})
```

`RichMessage` returns a `GroupElement` containing the parsed segments as child elements.
This is analogous to react-intl's rich text values and LinguiJS's `<Trans>` component map.

### 5.4 Adaptive Layout Properties

Some localized properties aren't strings — they're layout values that change per locale.
For example, a text field might need more width in German (longer words) or the entire
layout might mirror for Arabic.

**.resw supports property-qualified keys** (the `x:Uid` dotted convention):

```xml
<!-- en-US -->
<data name="SearchBox.PlaceholderText" xml:space="preserve">
  <value>Search...</value>
</data>
<data name="SearchBox.MinWidth" xml:space="preserve">
  <value>200</value>
</data>

<!-- de-DE -->
<data name="SearchBox.PlaceholderText" xml:space="preserve">
  <value>Suchen...</value>
</data>
<data name="SearchBox.MinWidth" xml:space="preserve">
  <value>260</value>
</data>
```

Reactor exposes this via modifier overloads:

```csharp
TextField(searchText, setSearchText)
    .Placeholder(t.Message(Loc.SearchBox.PlaceholderText))
    .MinWidth(t.Number(Loc.SearchBox.MinWidth))  // locale-adaptive width
```

---

## 6. BiDi / RTL Support

### 6.1 FlowDirection

WinUI 3's `FlowDirection` property cascades from parent to child. When set on the root
`FrameworkElement`, all descendants inherit it — text alignment, margin/padding direction,
scroll bars, and control layout all flip automatically.

Reactor will manage this at the `LocaleProvider` level:

```csharp
// Inside LocaleProvider implementation:
// When locale changes, set FlowDirection on the host's root element
var direction = IsRtlLocale(locale)
    ? FlowDirection.RightToLeft
    : FlowDirection.LeftToRight;
rootElement.FlowDirection = direction;
```

The `IntlAccessor.Direction` property exposes this to components that need explicit
direction-aware logic:

```csharp
var t = UseIntl();

// Most of the time, FlowDirection cascading handles everything.
// But for custom drawing or absolute positioning:
var startMargin = t.Direction == FlowDirection.RightToLeft
    ? new Thickness(0, 0, 16, 0)   // padding on the right
    : new Thickness(16, 0, 0, 0);  // padding on the left
```

### 6.2 Logical Layout Properties

Reactor should add **logical property modifiers** that resolve based on flow direction,
analogous to CSS logical properties:

```csharp
// Physical (breaks in RTL):
Text("Hello").Margin(left: 16)

// Logical (adapts automatically):
Text("Hello").MarginInlineStart(16)   // left in LTR, right in RTL
Text("Hello").PaddingInlineEnd(8)     // right in LTR, left in RTL
```

These resolve at mount/update time based on the current `FlowDirection`.

### 6.3 RTL-Aware Elements

| Concern | How WinUI Handles It | Reactor Action Needed |
|---------|--------------------|--------------------|
| Text alignment | Inherits FlowDirection | None — automatic |
| Margin/Padding | Respects FlowDirection | Add logical modifiers (InlineStart/End) |
| HStack child order | Respects FlowDirection | None — automatic |
| Navigation/back icons | Must be manually mirrored | Provide `UseIntl().IsRtl` for conditional icon selection |
| ScrollBar position | Respects FlowDirection | None — automatic |
| Grid column order | Respects FlowDirection | None — automatic |

### 6.4 Locale-to-Direction Mapping

Built-in mapping for RTL locales (sourced from CLDR):

```csharp
private static readonly HashSet<string> RtlLanguages = new()
{
    "ar", "he", "fa", "ur", "ps", "sd", "ug", "yi", "ku", "dv", "ha", "ks", "syr"
};

public static bool IsRtlLocale(string locale)
{
    var lang = locale.Split('-')[0].ToLowerInvariant();
    return RtlLanguages.Contains(lang);
}
```

---

## 7. Resource File Structure

### 7.1 Folder Layout

Standard MRT folder structure, optionally organized by namespace:

```
Strings/
  en-US/
    Resources.resw              ← flat: all keys in one file
  fr-FR/
    Resources.resw
  ar-SA/
    Resources.resw
```

Or for large apps with namespace splitting:

```
Strings/
  en-US/
    Common.resw                 ← shared strings (Save, Cancel, OK)
    Settings.resw               ← settings page strings
    Cart.resw                   ← cart/checkout strings
  fr-FR/
    Common.resw
    Settings.resw
    Cart.resw
```

Each namespace maps to a nested class in the generated `Loc` type:

```csharp
// Generated:
static class Loc
{
    static class Common
    {
        public static readonly MessageKey Save = new("Common", "Save");
        public static readonly MessageKey Cancel = new("Common", "Cancel");
    }
    static class Settings
    {
        public static readonly MessageKey Title = new("Settings", "Title");
        public static readonly MessageKey LoggedInAs = new("Settings", "LoggedInAs");
    }
    static class Cart
    {
        public static readonly MessageKey ItemCount = new("Cart", "ItemCount");
    }
}
```

### 7.2 .resw Content with ICU

The `.resw` values contain ICU Message Format strings. MRT treats them as opaque strings;
Reactor's ICU engine processes them at runtime:

```xml
<!-- en-US/Cart.resw -->
<data name="ItemCount" xml:space="preserve">
  <value>{count, plural, =0 {Your cart is empty} one {# item in cart} other {# items in cart}}</value>
</data>
<data name="OrderTotal" xml:space="preserve">
  <value>Total: {total, number, ::currency/USD}</value>
</data>
<data name="LastUpdated" xml:space="preserve">
  <value>Last updated {date, date, medium}</value>
</data>
<data name="ClickToLearnMore" xml:space="preserve">
  <value>Click &lt;link&gt;here&lt;/link&gt; to learn more.</value>
  <comment>Tags: link = hyperlink element</comment>
</data>
```

```xml
<!-- ar-SA/Cart.resw -->
<data name="ItemCount" xml:space="preserve">
  <value>{count, plural, =0 {سلة التسوق فارغة} one {عنصر واحد في السلة} two {عنصران في السلة} few {# عناصر في السلة} many {# عنصرًا في السلة} other {# عنصر في السلة}}</value>
</data>
```

Note: Arabic has 6 plural forms (zero, one, two, few, many, other). ICU handles this
correctly via CLDR plural rules — the translator fills in all forms, and the ICU engine
selects the right one at runtime.

---

## 8. Source Generator

### 8.1 What It Generates

The source generator runs at build time, reads all `.resw` files in the default locale
folder, and emits:

1. **`Loc.g.cs`** — typed key constants (one `MessageKey` per `.resw` entry)
2. **Diagnostics** — warnings for keys that exist in the default locale but are missing
   in other locales

```csharp
// Loc.g.cs (generated)
namespace MyApp.Localization;

/// <summary>Auto-generated from Strings/en-US/*.resw</summary>
[global::System.CodeDom.Compiler.GeneratedCode("Reactor.Localization.Generator", "1.0")]
public static class Loc
{
    public static class Common
    {
        /// <summary>Save</summary>
        public static readonly MessageKey Save = new("Common", "Save");
        /// <summary>Cancel</summary>
        public static readonly MessageKey Cancel = new("Common", "Cancel");
        /// <summary>OK</summary>
        public static readonly MessageKey OK = new("Common", "OK");
    }

    public static class Cart
    {
        /// <summary>{count, plural, =0 {Your cart is empty} one {# item in cart} other {# items in cart}}</summary>
        public static readonly MessageKey ItemCount = new("Cart", "ItemCount");
    }
}
```

The `/// <summary>` contains the default-locale value, so developers get IntelliSense
showing the actual English string when hovering over `Loc.Cart.ItemCount`.

### 8.2 Missing Key Diagnostics

At build time, the generator cross-references all locale folders and emits:

```
warning REACTOR_LOC001: Key 'Cart.ItemCount' exists in en-US but is missing in fr-FR
warning REACTOR_LOC001: Key 'Cart.ItemCount' exists in en-US but is missing in ar-SA
```

These are warnings by default, errors in release builds (configurable via MSBuild property):

```xml
<PropertyGroup>
  <ReactorLocMissingKeySeverity>Error</ReactorLocMissingKeySeverity>
</PropertyGroup>
```

---

## 9. LocaleProvider & Runtime Switching

### 9.1 App Root Setup

```csharp
class App : Component
{
    public override Element Render()
    {
        // Initialize from OS preference
        var osLocale = Windows.Globalization.ApplicationLanguages.Languages[0];
        var (locale, setLocale) = UseState(osLocale);

        return LocaleProvider(locale,
            VStack(
                // Language picker for runtime switching
                ComboBox(
                    items: SupportedLocales,
                    selected: locale,
                    onChanged: setLocale,
                    itemTemplate: loc => Text(new CultureInfo(loc).NativeName)
                ),
                Router(/* app content */)
            )
        );
    }
}
```

### 9.2 LocaleProvider Implementation

`LocaleProvider` is a Reactor element that:

1. Stores locale + direction in a shared context (singleton for v1, context for v2)
2. Loads the `ResourceLoader` for the active locale
3. Creates the `IntlAccessor` instance
4. Sets `FlowDirection` on the root WinUI element
5. When locale changes: flushes caches, re-creates accessor, triggers re-render of all
   components that called `UseIntl()`

```csharp
// DSL factory:
public static Element LocaleProvider(string locale, params Element[] children)
{
    return new LocaleProviderElement(locale, children);
}
```

### 9.3 Runtime Language Switching

When `setLocale("ar-SA")` is called:

1. `LocaleProvider` re-renders with new locale
2. `ResourceLoader` is re-created for the new locale (MRT resolves the correct `.resw`)
3. `FlowDirection` is updated on the root element (LTR → RTL)
4. All components calling `UseIntl()` re-render with the new `IntlAccessor`
5. ICU message cache is flushed and re-populated lazily

This happens in a single render cycle — no app restart needed.

---

## 10. CLI Extraction Tool: `duct-loc`

### 10.1 Overview

A command-line tool that scans Reactor component source code and manages `.resw` files.
Designed to be run after each feature checkpoint. **Idempotent** — running it twice
produces the same output.

### 10.2 Commands

#### `duct-loc extract`

Scans C# source files using Roslyn's syntax/semantic analysis to find localizable content.

```bash
# Scan all .cs files under src/, update en-US .resw files
duct-loc extract --source src/ --output Strings/en-US/

# Dry run — show what would change without writing
duct-loc extract --source src/ --output Strings/en-US/ --dry-run
```

**What it finds:**

1. **Existing `t.Message(Loc.X.Y)` calls** — validates the key exists in `.resw`
2. **Bare string literals in DSL calls** — `Text("Hello")`, `Button("Save", ...)`,
   `Heading("Settings")`, `.Placeholder("Search...")`, `.Header("Column Name")`
3. **String interpolations** — `Text($"Welcome, {name}")` → suggests ICU message

**What it produces:**

For each bare string found, the CLI:
- Generates a key name from context (e.g., `Text("Save")` inside `SettingsPage.Render()`
  → key `Settings.Save`)
- Adds the key + English value to the default locale `.resw`
- Optionally rewrites the source to use `t.Message(Loc.Settings.Save)` (with `--rewrite` flag)
- Skips strings already wrapped in `t.Message()`

```bash
# Full extract + rewrite pass
duct-loc extract --source src/ --output Strings/en-US/ --rewrite

# Before:
#   Text("Welcome back!")
# After:
#   Text(t.Message(Loc.Main.WelcomeBack))
# And en-US/Main.resw now contains key "WelcomeBack" = "Welcome back!"
```

**Idempotency**: Running extract again finds no new bare strings (they've been rewritten)
and the `.resw` file is unchanged.

#### `duct-loc translate`

Pre-fills translations for target locales using AI, with the English `.resw` as source.

```bash
# Translate en-US to fr-FR and ar-SA using AI
duct-loc translate --source Strings/en-US/ --target fr-FR,ar-SA

# Only translate keys that are missing or marked as draft
duct-loc translate --source Strings/en-US/ --target fr-FR --missing-only

# Use a specific AI provider
duct-loc translate --source Strings/en-US/ --target ja-JP --provider azure-openai
```

**How it works:**

1. Reads all keys + values from the source locale `.resw` files
2. For each target locale, identifies keys that are missing or marked `[draft]`
3. Sends batches to the configured LLM API with a system prompt that:
   - Explains ICU Message Format syntax (preserve `{variables}`, `{count, plural, ...}`)
   - Provides locale-specific instructions (formal/informal register, script)
   - Includes any existing translations as context for consistency
4. Writes translated values to target `.resw` files
5. Marks each AI-translated entry with a comment: `<!-- ai-translated: pending-review -->`

**Human review workflow:**

```xml
<!-- fr-FR/Cart.resw after AI translation -->
<data name="ItemCount" xml:space="preserve">
  <value>{count, plural, =0 {Votre panier est vide} one {# article dans le panier} other {# articles dans le panier}}</value>
  <comment>ai-translated: pending-review</comment>
</data>
```

A human translator reviews and removes the `pending-review` marker:

```xml
<data name="ItemCount" xml:space="preserve">
  <value>{count, plural, =0 {Votre panier est vide} one {# article dans le panier} other {# articles dans le panier}}</value>
  <comment>reviewed: 2026-04-10 by translator@example.com</comment>
</data>
```

#### `duct-loc status`

Reports translation coverage across locales:

```bash
$ duct-loc status

Locale    Keys   Translated   AI-Draft   Missing   Coverage
en-US     142    142          0          0         100.0%
fr-FR     142    128          12         2          90.1%
ar-SA     142    95           40         7          66.9%
ja-JP     142    0            0          142         0.0%
```

#### `duct-loc validate`

Checks ICU syntax validity and parameter consistency:

```bash
$ duct-loc validate

ERROR: fr-FR/Cart.resw:ItemCount — ICU parse error: unmatched brace at position 42
WARN:  ar-SA/Settings.resw:LoggedInAs — parameter {name} in en-US but {nom} in ar-SA
WARN:  fr-FR/Common.resw — missing key: NewFeatureBadge
```

### 10.3 Extraction Heuristics

The CLI uses Roslyn to parse the C# AST and finds localizable content by recognizing
Reactor DSL patterns:

| Pattern | Example | Extracted As |
|---------|---------|-------------|
| `Text(literal)` | `Text("Hello")` | Simple string |
| `Heading(literal)` | `Heading("Settings")` | Simple string |
| `Button(literal, ...)` | `Button("Save", onSave)` | Simple string |
| `Text($"...{var}...")` | `Text($"Hello, {name}")` | ICU message: `"Hello, {name}"` |
| `.Placeholder(literal)` | `.Placeholder("Search...")` | Property-qualified key |
| `.Header(literal)` | `.Header("Name")` | Property-qualified key |
| `.ToolTip(literal)` | `.ToolTip("Copy to clipboard")` | Simple string |
| `.Title(literal)` | `.Title("Confirm Delete")` | Simple string |
| Ternary branches | `Text(x ? "Show" : "Hide")` | Two keys (one per branch) |
| Null-coalescing literal | `Text(val ?? "Default")` | Literal side only |

**What it ignores:**
- String arguments to non-DSL methods
- `AutomationId`, `Name` (accessibility — separate concern)
- Strings that are clearly not user-visible (log messages, exception messages)
- Strings already wrapped in `t.Message()`

### 10.4 String Interpolation → ICU Conversion (Key Design)

C# string interpolation (`$"..."`) and ICU Message Format (`{variable}`) are structurally
the same thing: named holes in a string. The extraction CLI exploits this to provide a
seamless authoring experience.

**Recommendation: developers should use C# string interpolation freely.** It is the
natural way to write parameterized text in C#, and the CLI converts it mechanically to
ICU messages. This means developers write natural C# code and get localizable ICU strings
for free — no need to think about localization syntax while authoring.

#### How the Conversion Works

The CLI parses C# `InterpolatedStringExpression` nodes from the Roslyn AST. Each
interpolation hole (`{expression}`) is mapped to a named ICU placeholder:

| C# Interpolation | ICU Message (auto-generated) | Argument Map |
|---|---|---|
| `$"Hello, {name}"` | `Hello, {name}` | `new { name }` |
| `$"Hello, {user.Name}"` | `Hello, {name}` | `new { name = user.Name }` |
| `$"{count} items"` | `{count} items` | `new { count }` |
| `$"Total: {price:C}"` | `Total: {price, number, currency}` | `new { price }` |
| `$"Due: {date:d}"` | `Due: {date, date, short}` | `new { date }` |
| `$"Score: {pct:P0}"` | `Score: {pct, number, percent}` | `new { pct }` |

**Rules for expression → placeholder name:**
- Simple variable: `{count}` → `{count}` (keep as-is)
- Dotted expression: `{user.Name}` → `{name}` (last segment, camelCase)
- Method call: `{GetTotal()}` → skip with warning (not extractable)
- Complex expression: `{items.Count + 1}` → skip with warning

**Rules for C# format specifier → ICU formatter:**
- `:C` / `:C2` → `{var, number, currency}`
- `:N` / `:N2` → `{var, number}`
- `:P` / `:P0` → `{var, number, percent}`
- `:d` → `{var, date, short}`
- `:D` → `{var, date, long}`
- `:f` / `:F` → `{var, date, full}`
- No specifier → `{var}` (plain interpolation)

#### Full Example: Before, After Extract, After Localization

**Step 1 — Developer writes natural C#:**

```csharp
class InboxPage : Component
{
    public override Element Render()
    {
        var t = UseIntl();

        return VStack(
            Heading("Inbox"),
            Text($"You have {messages.Count} messages"),
            Text($"Last login: {lastLogin:D}"),
            Text($"Storage: {usedPct:P0} used"),
            Button("Compose", onCompose)
        );
    }
}
```

**Step 2 — `duct-loc extract --rewrite` transforms to:**

```csharp
class InboxPage : Component
{
    public override Element Render()
    {
        var t = UseIntl();

        return VStack(
            Heading(t.Message(Loc.Inbox.Title)),
            Text(t.Message(Loc.Inbox.YouHaveMessages, new { count = messages.Count })),
            Text(t.Message(Loc.Inbox.LastLogin, new { lastLogin })),
            Text(t.Message(Loc.Inbox.StorageUsed, new { usedPct })),
            Button(t.Message(Loc.Inbox.Compose), onCompose)
        );
    }
}
```

**Auto-generated `en-US/Inbox.resw`:**

```xml
<data name="Title" xml:space="preserve">
  <value>Inbox</value>
</data>
<data name="YouHaveMessages" xml:space="preserve">
  <value>You have {count} messages</value>
  <comment>auto-extracted from interpolation; consider adding plural support</comment>
</data>
<data name="LastLogin" xml:space="preserve">
  <value>Last login: {lastLogin, date, long}</value>
</data>
<data name="StorageUsed" xml:space="preserve">
  <value>Storage: {usedPct, number, percent} used</value>
</data>
<data name="Compose" xml:space="preserve">
  <value>Compose</value>
</data>
```

Note: the CLI adds a comment hint (`consider adding plural support`) when it detects a
variable named `count`, `total`, `num*`, or similar quantity-suggesting names adjacent to
a noun. This nudges the developer or localizer to add ICU plural forms.

**Step 3 — Developer or localizer upgrades ICU in en-US .resw:**

```xml
<data name="YouHaveMessages" xml:space="preserve">
  <value>{count, plural,
    =0 {You have no messages}
    one {You have # message}
    other {You have # messages}
  }</value>
</data>
```

**Step 4 — `duct-loc translate` produces ar-SA .resw (AI-assisted):**

```xml
<data name="YouHaveMessages" xml:space="preserve">
  <value>{count, plural,
    =0 {ليس لديك رسائل}
    one {لديك رسالة واحدة}
    two {لديك رسالتان}
    few {لديك # رسائل}
    many {لديك # رسالة}
    other {لديك # رسالة}
  }</value>
  <comment>ai-translated: pending-review</comment>
</data>
```

**The C# code never changes after Step 2.** Only the `.resw` values evolve — from simple
auto-extracted interpolations, to developer-enhanced ICU with plurals, to fully localized
translations. The application code is completely decoupled from translation complexity.

#### Why This Matters

In most localization systems, the developer must think about localization while authoring:
they write `t("inbox.messageCount", { count })` from the start, mentally mapping between
code and resource keys. In Reactor's model:

1. **Author naturally** — write `Text($"You have {count} messages")` like any C# code
2. **Extract mechanically** — the CLI does the tedious conversion
3. **Localize incrementally** — the `.resw` evolves from simple to rich ICU over time

This inverts the usual workflow: instead of "think about i18n while coding", it's "code
first, localize later". The extraction is a checkpoint, not a constraint.

### 10.5 Key Naming Strategy

The CLI generates hierarchical key names from code context:

```
{Namespace}.{ClassName}.{ContextHint}
```

Examples:
- `Text("Save")` in `SettingsPage.Render()` → `Settings.Save`
- `Button("Add to Cart", ...)` in `ProductCard.Render()` → `Product.AddToCart`
- `.Placeholder("Search products...")` in `CatalogPage.Render()` → `Catalog.SearchProductsPlaceholder`

Duplicate values across components get distinct keys (same English string might need
different translations in different contexts).

---

## 11. ICU Engine Integration

### 11.1 ICU Engine: `MessageFormat` by jeffijoe

**NuGet package**: `MessageFormat` (install: `dotnet add package MessageFormat`)
**GitHub**: https://github.com/jeffijoe/messageformat.net
**Version**: 8.0.0 (released 2026-02-23)
**Downloads**: ~3.1M total
**License**: MIT
**Targets**: net8.0, net10.0, netstandard2.0, netstandard2.1
**Dependencies**: Zero native dependencies, trimmable, strong-named

**Feature coverage:**

| ICU Feature | Supported | Example |
|-------------|-----------|---------|
| Simple interpolation | Yes | `{name}` |
| Plural | Yes | `{count, plural, one {# item} other {# items}}` |
| Select | Yes | `{gender, select, male {He} female {She} other {They}}` |
| Ordinal | Yes | `{rank, selectordinal, one {#st} two {#nd} few {#rd} other {#th}}` |
| Nested messages | Yes | Arbitrary depth |
| Number formatting | Yes | `{age, number}`, styles: integer, currency, percent |
| Date formatting | Yes | `{birthday, date}`, styles: short, full |
| Time formatting | Yes | `{now, time}`, styles: short, medium |
| Custom formatters | Yes | Lambda or derived class |
| CLDR plural rules | Yes | Built-in since v5.0 |

**Alternatives evaluated and rejected:**

| Library | Why Not |
|---------|---------|
| `SmartFormat.NET` (16M DL) | Own syntax, not ICU-compatible — can't share strings with web/mobile |
| `icu.net` (1.2M DL) | Native ICU4C interop — heavy deployment burden, not trimmable |
| `FormatWith` (1.3M DL) | Simple named params only, no ICU features, unmaintained since 2021 |
| `MoonBuggy` (381 DL) | Interesting source-gen approach but pre-1.0, not production-ready |
| .NET `System.Globalization` | Uses ICU data for cultures but does NOT parse ICU message syntax |

**Decision**: `MessageFormat` is the clear choice. Actively maintained, full ICU feature
set, zero native dependencies, .NET 8/10 ready, 3M+ downloads. No need to defer ICU support.

```csharp
using MessageFormat;

var mf = new MessageFormatter();

// Simple interpolation
mf.Format("Hello, {name}!", new { name = "World" });
// → "Hello, World!"

// Plurals
mf.Format("{count, plural, =0 {No items} one {# item} other {# items}}", new { count = 5 });
// → "5 items"

// Select (gender)
mf.Format("{gender, select, male {He} female {She} other {They}} left.", new { gender = "female" });
// → "She left."

// Nested
mf.Format("{count, plural, one {# new message from {name}} other {# new messages from {name}}}",
    new { count = 3, name = "Alice" });
// → "3 new messages from Alice"
```

### 11.2 Caching Strategy

ICU message parsing is not free. We cache parsed message patterns per locale:

```csharp
internal class MessageCache
{
    // Key: (locale, resourceKey) → Value: pre-parsed message pattern
    private readonly ConcurrentDictionary<(string, string), MessagePattern> _cache = new();

    public string Format(string locale, string key, string rawMessage, object? args)
    {
        var pattern = _cache.GetOrAdd((locale, key), _ => Parse(rawMessage));
        return pattern.Format(locale, args);
    }
}
```

For hot paths, consider pre-compiling messages via source generator (future optimization).

### 11.3 Fallback to .NET Formatting

For number, date, and list formatting, we delegate to .NET's built-in `Intl`-equivalent
APIs (which themselves use ICU/CLDR data since .NET 6):

```csharp
// Number formatting
public string FormatNumber(double value, NumberFormatOptions? options = null)
{
    var culture = new CultureInfo(Locale);
    return options?.Style switch
    {
        NumberStyle.Currency c => value.ToString("C", new NumberFormatInfo
        {
            CurrencySymbol = c.Currency,
            // ... configure from culture
        }),
        NumberStyle.Percent => value.ToString("P", culture),
        _ => value.ToString("N", culture)
    };
}

// Date formatting
public string FormatDate(DateTimeOffset value, DateFormatOptions? options = null)
{
    var culture = new CultureInfo(Locale);
    return options?.Style switch
    {
        DateStyle.Long => value.ToString("D", culture),
        DateStyle.Short => value.ToString("d", culture),
        DateStyle.Full => value.ToString("F", culture),
        _ => value.ToString("G", culture)
    };
}
```

---

## 12. End-to-End Workflow

### 12.1 Developer Authors in English

Developers are encouraged to use **C# string interpolation** (`$"..."`) as the natural
way to write parameterized text. The CLI handles the conversion to ICU automatically.

```csharp
// Developer writes UI naturally, in English, using string interpolation:
class ProductCard : Component<ProductProps>
{
    public override Element Render()
    {
        var product = Props;

        return VStack(
            Heading(product.Name),
            Text($"{product.ReviewCount} reviews"),
            Text($"{product.Price:C}"),
            Button("Add to Cart", () => AddToCart(product.Id))
        );
    }
}
```

### 12.2 Run Extract

```bash
$ duct-loc extract --source src/ --output Strings/en-US/ --rewrite

  [NEW] Product.ReviewCount = "{count} reviews" (from interpolation)
  [NEW] Product.Price = "${price}" (from interpolation)
  [NEW] Product.AddToCart = "Add to Cart" (from Button label)
  [SKIP] product.Name — dynamic expression, not extractable
  [REWRITE] src/Components/ProductCard.cs — 3 strings replaced

$ cat src/Components/ProductCard.cs  # after rewrite
```

```csharp
class ProductCard : Component<ProductProps>
{
    public override Element Render()
    {
        var product = Props;
        var t = UseIntl();

        return VStack(
            Heading(product.Name),  // dynamic — not localized
            Text(t.Message(Loc.Product.ReviewCount, new { count = product.ReviewCount })),
            Text(t.FormatNumber(product.Price, NumberStyle.Currency("USD"))),
            Button(t.Message(Loc.Product.AddToCart), () => AddToCart(product.Id))
        );
    }
}
```

### 12.3 Enhance ICU Messages

Developer reviews generated `.resw` and upgrades simple messages to use ICU features:

```xml
<!-- Before (auto-generated): -->
<data name="ReviewCount"><value>{count} reviews</value></data>

<!-- After (developer adds plural support): -->
<data name="ReviewCount"><value>{count, plural, =0 {No reviews yet} one {# review} other {# reviews}}</value></data>
```

### 12.4 Run Translate

```bash
$ duct-loc translate --source Strings/en-US/ --target fr-FR,ar-SA,ja-JP

  Translating 24 keys to fr-FR... done (24/24)
  Translating 24 keys to ar-SA... done (24/24)
  Translating 24 keys to ja-JP... done (24/24)
  All translations marked as ai-translated: pending-review
```

### 12.5 Human Review

Translator opens `Strings/fr-FR/Product.resw` in any XML editor or MAT:

```xml
<data name="ReviewCount" xml:space="preserve">
  <value>{count, plural, =0 {Aucun avis} one {# avis} other {# avis}}</value>
  <comment>ai-translated: pending-review</comment>
</data>
```

Translator approves or corrects, removes the `pending-review` comment.

### 12.6 Validate & Ship

```bash
$ duct-loc validate
  All 72 keys valid across 4 locales. 0 errors, 0 warnings.

$ duct-loc status
  Locale  Keys  Translated  AI-Draft  Missing  Coverage
  en-US   24    24          0         0        100.0%
  fr-FR   24    24          0         0        100.0%
  ar-SA   24    20          4         0        100.0% (4 pending review)
  ja-JP   24    24          0         0        100.0%
```

---

## 13. Comparison with Alternatives (Summary)

| Dimension | This Design (ICU + MRT) | Pure MRT (Option A) | Pure ICU/JSON (Option B-old) | Static Classes |
|-----------|:-:|:-:|:-:|:-:|
| **Windows-native storage** | .resw (MRT) | .resw (MRT) | JSON (custom) | C# code |
| **OS language negotiation** | Yes | Yes | No | No |
| **MSIX resource packs** | Yes | Yes | No | No |
| **MAT integration** | Yes | Yes | No | No |
| **Pluralization** | ICU auto (CLDR) | Manual | ICU auto | Manual |
| **Gender/select** | ICU select | No | ICU select | Manual |
| **Rich text** | ICU tags + element map | No | ICU tags | No |
| **BiDi/RTL** | Auto FlowDirection | Manual | Manual | Manual |
| **Type-safe keys** | Source generator | Source generator | Source generator | Native C# |
| **CLI extraction** | Roslyn AST scan | No | No | No |
| **AI translation** | Built-in CLI | No | No | No |
| **Translator workflow** | .resw + MAT + Crowdin | .resw + MAT | JSON + Crowdin | Needs custom tool |
| **Scales to 500+ strings** | Yes | Yes | Yes | Poorly |
| **Implementation effort** | Medium-Large | Small | Medium | Small |

---

## 14. Hot Reload & Intermediate State

### 14.1 Hot Reload

The localization system has **no negative impact on hot reload**:

- `.resw` files are loaded by `ResourceLoader` at runtime — they are not compiled into
  the assembly. Changing a `.resw` key's **value** (e.g., editing the English text or
  ICU message) does not require rebuilding.
- In dev mode, Reactor watches `.resw` files for changes. When a file changes:
  1. The ICU message cache for that namespace is invalidated
  2. `ResourceLoader` is refreshed (re-created for the active locale)
  3. All components that called `UseIntl()` are scheduled for re-render
  4. The updated strings appear on screen without restarting
- **Structural changes** (adding/removing keys) do require the source generator to re-run
  (next build), since `Loc.g.cs` needs new/removed `MessageKey` fields. But value edits
  to existing keys are instant.
- Hot reload of C# component code works as normal — `UseIntl()` is just another hook call.

### 14.2 Intermediate State: Partial Localization

A core design principle is that **bare strings are never broken by the localization system**.
The developer's workflow naturally produces intermediate states that must all be valid:

```
State 1: No localization at all
  Text("Hello")                    ← works, renders "Hello"
  Button("Save", onSave)           ← works, renders "Save"

State 2: After first extract --rewrite (some strings converted)
  Text(t.Message(Loc.Main.Hello))  ← works, resolved from .resw
  Text("New feature!")             ← works, still a bare string
  Button(t.Message(Loc.Main.Save)) ← works, resolved from .resw

State 3: Developer adds more bare strings, keeps working
  Text(t.Message(Loc.Main.Hello))  ← works
  Text("New feature!")             ← works (not yet extracted)
  Text("Another thing")            ← works (not yet extracted)
  Button(t.Message(Loc.Main.Save)) ← works

State 4: Run extract again — picks up new strings, leaves existing ones alone
  Text(t.Message(Loc.Main.Hello))        ← unchanged
  Text(t.Message(Loc.Main.NewFeature))   ← newly extracted
  Text(t.Message(Loc.Main.AnotherThing)) ← newly extracted
  Button(t.Message(Loc.Main.Save))       ← unchanged
```

**Key guarantees:**

1. `Text(string)` always works — it's just a string. No `LocaleProvider` required.
2. `t.Message(key)` works as long as a `LocaleProvider` is in the tree. If no provider
   exists, `UseIntl()` returns a **default accessor** that uses the OS locale and
   `ResourceLoader` directly — it does not throw.
3. Mixed bare + localized strings in the same component are fine.
4. The extract CLI is idempotent — running it on already-extracted code is a no-op.
5. Missing translations fall back gracefully (see Section 15.4).

**What the developer never has to worry about:**
- "Will my app break if I haven't extracted all strings yet?" → No.
- "Do I need to set up localization before I can use `Text()`?" → No.
- "What if I extract, then add more strings, then extract again?" → Works perfectly.

### 14.3 Pseudolocalization Mode

To help developers find hardcoded strings that escaped extraction, Reactor supports a
**pseudolocalization mode** enabled by a dev flag:

```xml
<!-- In .csproj or launchSettings.json -->
<PropertyGroup>
  <ReactorPseudoLocalize>true</ReactorPseudoLocalize>
</PropertyGroup>
```

Or at runtime:
```csharp
LocaleProvider("en-US", pseudoLocalize: true, children)
```

When enabled, all strings returned by `t.Message()` are transformed:

| Original | Pseudolocalized |
|----------|----------------|
| `Save` | `[!! Ŝåvé !!]` |
| `Add to Cart` | `[!! Åðð ťö Çåŕť !!]` |
| `{count} items` | `[!! {count} ïťéɱš !!]` |

**What this reveals:**
- **Hardcoded strings** — they appear as normal English, while localized strings have
  the `[!! ... !!]` wrapper. Anything without the wrapper was missed by extraction.
- **Truncation** — pseudo strings are ~30% longer, simulating German/French expansion.
- **Character encoding** — accented characters reveal rendering issues with fonts.
- **Layout breaks** — longer strings expose overflow, ellipsis, and wrapping problems.

The pseudo transformation preserves ICU syntax (`{variables}`, `{count, plural, ...}`)
so the message still formats correctly.

---

## 15. Design Decisions

### 15.1 Context-Based IntlAccessor (not Singleton)

`UseIntl()` reads from a **component-tree context** set by `LocaleProvider`, matching
React's `IntlProvider` pattern. This enables:

- **Testing**: Wrap a component in `LocaleProvider("fr-FR", ...)` in tests to verify
  French rendering without affecting other tests
- **Nested overrides**: A documentation panel could render in English while the rest
  of the app is in Japanese
- **Multiple windows**: Each window can have its own locale if needed

### 15.2 Namespace Granularity: Support Both

- Small apps use a single `Resources.resw` per locale → flat `Loc.KeyName`
- Large apps split by feature into `Common.resw`, `Settings.resw`, etc. → `Loc.Common.KeyName`
- The source generator auto-detects: multiple `.resw` files = namespaced, single file = flat

### 15.3 Accessibility Strings

**Decision**: Localize user-facing accessibility properties (those read aloud by screen
readers); do not localize programmatic identifiers.

| Property | Localize? | Rationale |
|----------|-----------|-----------|
| `AutomationProperties.Name` | **Yes** | Screen readers speak this to the user |
| `AutomationProperties.HelpText` | **Yes** | Read aloud as supplementary description |
| `AutomationProperties.LiveSetting` | **Yes** | Announced on change |
| `AutomationProperties.AutomationId` | **No** | Programmatic identifier for test automation |
| `AutomationProperties.ClassName` | **No** | Programmatic, not user-visible |

This matches the industry standard (WCAG, Microsoft's own UIA guidance, Android/iOS
accessibility localization). The extraction CLI should recognize `.AutomationName()` and
`.HelpText()` modifiers as localizable, but not `.AutomationId()`.

The extraction heuristic table (Section 10.3) is extended:

| Pattern | Example | Extracted? |
|---------|---------|-----------|
| `.AutomationName(literal)` | `.AutomationName("Save button")` | Yes |
| `.HelpText(literal)` | `.HelpText("Saves your changes")` | Yes |
| `.AutomationId(literal)` | `.AutomationId("BtnSave")` | No |

### 15.4 Fallback Behavior

**Debug mode**: When a key is missing in the current locale:
1. Fall back to the default locale (en-US)
2. Log a warning: `[Reactor.Intl] Missing key 'Cart.ItemCount' for locale 'fr-FR', falling back to en-US`
3. If pseudolocalization is on, missing keys render as `[?? Cart.ItemCount ??]` (distinct
   from the `[!! ... !!]` pseudo pattern) making them immediately visible

**Release mode**: Silent fallback to default locale. No logging, no visual indicators.

### 15.5 Image/Asset Localization

MRT supports locale-qualified assets (images, audio, etc.) via the same folder convention:

```
Assets/
  en-US/
    hero-banner.png
  ja-JP/
    hero-banner.png       ← Japanese version of the banner
```

**Decision**: Reactor provides a `UseLocalizedAsset()` hook for developers who want this,
but does **not** proactively extract or manage image assets. The CLI focuses on strings.

```csharp
var t = UseIntl();
var bannerPath = t.Asset("Assets/hero-banner.png");  // resolves to locale-qualified path
Image(bannerPath)
```

Under the hood, this uses `StorageFile.GetFileFromApplicationUriAsync` with MRT
qualification, or falls back to the unqualified path if no locale-specific asset exists.

### 15.6 Extraction in CLI Only

**Decision**: The source generator reads `.resw` files and emits typed keys. It does
**not** scan source code or rewrite files. Extraction and rewriting are CLI-only operations
(`duct-loc extract --rewrite`).

Rationale:
- Source generators should not have side effects (writing non-generated files)
- Extraction is an intentional developer action at a checkpoint, not an automatic build step
- Rewriting source code during compilation would be confusing and hard to debug
- The CLI gives the developer explicit control and a clear diff to review

---

## 16. Implementation Plan

### Phase 1: Foundation
- `MessageKey` type + `IntlAccessor` with `Message()`, `FormatNumber()`, `FormatDate()`
- `UseIntl()` hook on `RenderContext` (context-based, with safe default when no provider)
- `LocaleProvider` element + `FlowDirection` management
- `ResourceLoader` integration for loading `.resw` at runtime
- `MessageFormat` NuGet dependency for ICU processing
- Basic caching of parsed ICU messages
- Fallback behavior: warn + fallback in debug, silent in release

### Phase 2: Source Generator + Pseudolocalization
- Read default-locale `.resw` files, emit `Loc.g.cs` with nested typed keys
- IntelliSense: `/// <summary>` with English value on each key
- Build warnings for keys missing in non-default locales
- MSBuild integration (`ReactorLocMissingKeySeverity`, `ReactorPseudoLocalize` properties)
- Pseudolocalization transform: `[!! ŜåvÉ !!]` for localized, `[?? Key ??]` for missing

### Phase 3: CLI — Extract
- Roslyn-based C# AST scanner for Reactor DSL patterns (Text, Button, Heading, Placeholder,
  ToolTip, Header, AutomationName, HelpText — not AutomationId)
- Key naming heuristics from component class + method context
- `--rewrite` mode to inject `UseIntl()` + `t.Message()` into source
- `--dry-run` mode to preview without writing
- Idempotent: re-running on extracted code is a no-op
- `.resw` file creation and incremental update

### Phase 4: Rich Text & Layout
- `RichMessage()` API with tag-to-element mapping
- Logical layout modifiers (`MarginInlineStart`, `PaddingInlineEnd`, `BorderInlineStart`)
- RTL-aware default behaviors in reconciler
- `UseLocalizedAsset()` hook for locale-qualified images (MRT-backed)

### Phase 5: CLI — Translate & Validate
- AI provider abstraction (Azure OpenAI, OpenAI, Anthropic)
- Batch translation with ICU-aware system prompts
- `pending-review` comment markers in `.resw`
- `duct-loc translate --missing-only` for incremental translation
- `duct-loc validate` — ICU syntax, parameter consistency, encoding
- `duct-loc status` — coverage report across locales
- `duct-loc prune` — find and remove dead keys not referenced in source

### Phase 6: Polish
- Hot reload: file watcher on `.resw` files, cache invalidation, auto re-render
- Pre-compiled ICU messages via source generator (zero runtime parse, future optimization)
- XLIFF export/import for professional translation agencies
- CI integration examples (validate in PR checks, status in release gates)

---

## 17. Additional Design Decisions (Resolved)

### 17.1 ICU Engine: Confirmed

`MessageFormat` (NuGet) by jeffijoe is confirmed as the ICU engine. v8.0.0 (Feb 2026)
supports all required features: plural, select, ordinal, nested messages, number/date/time.
3.1M downloads, MIT, net8.0/net10.0, zero native deps. See Section 11.1 for full evaluation.

### 17.2 Ternary & Complex Expression Extraction

The `--rewrite` flag handles common expression patterns beyond simple string literals:

```csharp
// Ternary — SUPPORTED: both branches extracted as separate keys
Text(isExpanded ? "Collapse" : "Expand")
// → Text(isExpanded ? t.Message(Loc.Panel.Collapse) : t.Message(Loc.Panel.Expand))

// Null-coalescing — SUPPORTED: literal side extracted
Text(customLabel ?? "Default Label")
// → Text(customLabel ?? t.Message(Loc.Settings.DefaultLabel))

// Conditional with string literal — SUPPORTED
Button(isNew ? "Create" : "Update", onSubmit)
// → Button(isNew ? t.Message(Loc.Form.Create) : t.Message(Loc.Form.Update), onSubmit)
```

**Not supported (best-effort skip with warning):**
- Strings built across multiple statements (`var s = "Hello"; s += " World"; Text(s);`)
- Strings returned from helper methods (`Text(GetLabel())`)
- String concatenation (`Text("Hello " + name)` — suggest converting to ICU interpolation)
- Deeply nested expressions

The CLI logs a warning for patterns it can't handle:

```
WARN: src/Components/Panel.cs:42 — cannot extract: string from method call GetLabel()
WARN: src/Components/Panel.cs:58 — cannot extract: string concatenation, convert to interpolation
```

The goal is **best-effort coverage of common patterns**, not full data-flow analysis.
Developers handle the remaining cases manually.

### 17.3 Key Uniqueness & Dead Key Cleanup

**Key generation is always unique** — every extraction site gets its own key, even if the
English string is identical across components:

```
SettingsPage: Text("Save")  → Loc.Settings.Save
ProfilePage:  Text("Save")  → Loc.Profile.Save
```

Developers can manually merge keys after extraction if they want shared translations
(e.g., create `Loc.Common.Save` and update both call sites).

**Dead key detection** is provided via a new CLI command:

```bash
# Find keys in .resw that are not referenced anywhere in source
$ duct-loc prune --source src/ --resources Strings/en-US/ --dry-run

  UNUSED: Common.OldFeature (not referenced in any .cs file)
  UNUSED: Settings.DeprecatedOption (not referenced in any .cs file)
  2 unused keys found. Run without --dry-run to remove from all locale .resw files.

# Actually remove them
$ duct-loc prune --source src/ --resources Strings/en-US/
  Removed Common.OldFeature from 4 locale files
  Removed Settings.DeprecatedOption from 4 locale files
```

The prune command:
1. Scans all `.cs` files for `Loc.{Namespace}.{Key}` references
2. Compares against all keys in `.resw` files
3. Reports (or removes) keys that have zero references
4. Is idempotent — safe to run in CI

### 17.4 CI Enforcement via --dry-run

`duct-loc extract --dry-run` exits with a non-zero exit code if unextracted strings are
found, making it usable as a CI gate:

```yaml
# Azure Pipelines / GitHub Actions example
- script: duct-loc extract --source src/ --output Strings/en-US/ --dry-run
  displayName: 'Check for unextracted strings'
  # Fails the build if any bare strings exist in DSL calls
```

```bash
$ duct-loc extract --source src/ --output Strings/en-US/ --dry-run

  UNEXTRACTED: src/Components/NewFeature.cs:15 — Text("Beta Feature")
  UNEXTRACTED: src/Components/NewFeature.cs:22 — Button("Try It", ...)
  2 unextracted strings found. Run without --dry-run to extract.
  Exit code: 1
```

Similarly, `duct-loc prune --dry-run` can enforce no dead keys, and `duct-loc validate`
can enforce ICU syntax correctness and translation completeness in CI:

```yaml
# Full localization CI gate
- script: |
    duct-loc extract --source src/ --output Strings/en-US/ --dry-run
    duct-loc prune --source src/ --resources Strings/en-US/ --dry-run
    duct-loc validate --resources Strings/
  displayName: 'Localization checks'
```
