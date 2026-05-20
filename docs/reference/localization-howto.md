# How to Localize a Reactor App

A step-by-step guide for taking an existing Reactor application with hardcoded
English strings and making it fully localizable.

## Prerequisites

### Understanding the pieces

The localization system has two compile-time parts:

- **Runtime (`Reactor.Core.Localization`)** — `IntlAccessor`, `MessageCache`,
  `LocaleProviderElement`, `ReswResourceProvider`, etc. These live inside
  `Reactor.dll` itself, under `Reactor/Core/Localization/`. If you already
  reference `Reactor`, you have the runtime — there is no separate
  `Reactor.Localization` NuGet package.

- **Source generator (`Reactor.Localization.Generator`)** — A Roslyn source
  generator that reads your `.resw` files at build time and emits a typed
  `Loc` class (`Loc.g.cs`) with compile-time-checked `MessageKey` constants.
  This is a separate project/DLL.

### Setting up within this repo (Reactor.TestApp)

The source generator is **not transitive** — even though `Reactor.csproj`
references it, your app project needs its own reference. Add all of the
following to your `.csproj`:

```xml
<!-- Source generator: emits Loc.g.cs from .resw files -->
<ItemGroup>
  <ProjectReference Include="..\..\Reactor.Localization.Generator\Reactor.Localization.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- Localization properties and resource files -->
<PropertyGroup>
  <ReactorLocDefaultLocale>en-US</ReactorLocDefaultLocale>
  <ReactorLocStringsPath>Strings\</ReactorLocStringsPath>
</PropertyGroup>

<ItemGroup>
  <!-- Feed .resw files to the source generator at build time -->
  <AdditionalFiles Include="Strings\**\*.resw" />
  <!-- Expose MSBuild properties to the analyzer -->
  <CompilerVisibleProperty Include="ReactorLocDefaultLocale" />
  <CompilerVisibleProperty Include="ReactorLocMissingKeySeverity" />
</ItemGroup>

<!-- Copy .resw files to output so ReswResourceProvider can read them at runtime.
     The runtime reads .resw XML directly (not via MRT/PRI) for compatibility
     with unpackaged apps. -->
<ItemGroup>
  <Content Include="Strings\**\*.resw"
           CopyToOutputDirectory="PreserveNewest"
           Link="Strings\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

Three things to note:
- **`AdditionalFiles`** is what the source generator reads at build time.
- **`CompilerVisibleProperty`** exposes MSBuild properties to the analyzer.
  Without these, the generator can't read `ReactorLocDefaultLocale`.
- **`Content` with `CopyToOutputDirectory`** copies the raw `.resw` XML
  files to the output folder. The runtime reads these directly rather than
  going through MRT/PRI, which avoids `ResourceLoader` issues in
  unpackaged apps.

> **For consumers outside this repo** (i.e., people using a published Reactor
> NuGet package): the package would include the generator as an analyzer
> dependency and ship a `.targets` file that sets up `ReactorLocDefaultLocale`,
> `ReactorLocStringsPath`, and the `AdditionalFiles` glob automatically. That
> packaging work hasn't been done yet — for now, add the items above
> manually.

### CLI

The CLI project is `src/Reactor.Cli/Reactor.Cli.csproj`. Run it via `dotnet run`,
which auto-builds before executing:

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc extract --help
```

Everything after `--` is passed to the CLI as arguments.

---

## Phase 1: Prepare your code

Before running the extractor, audit your source for patterns that need
manual attention. The scanner is good at finding bare strings inside DSL
calls (`Text(...)`, `Button(...)`, etc.) but it cannot understand your
application's semantics.

### 1a. Decouple display strings from programmatic keys

If a string is used as **both a label and a match/state key**, the
extractor cannot safely rewrite it. Refactor first.

**Before** (broken — label doubles as switch key):

```csharp
var (currentTab, setTab) = UseState("Counter");

TabButton("Counter", currentTab, setTab);   // display label
// ...
currentTab switch { "Counter" => ..., }      // match key
```

**After** (safe — enum for state, separate string for display):

```csharp
enum Tab { Counter, TodoList, Form }

var (currentTab, setTab) = UseState(Tab.Counter);

// label comes from localization; state is an enum
TabButton(t.Message(Loc.Demo.CounterTab), Tab.Counter, currentTab, setTab);
// ...
currentTab switch { Tab.Counter => ..., }
```

Look for this pattern anywhere strings are used in `switch`, dictionary
keys, `==` comparisons, or passed to APIs that expect a fixed value.

### 1b. Identify strings that should NOT be localized

Not every string in a `Text()` call is user-facing prose. Skip or mark
these so you can exclude them during review:

| Category | Examples | Why skip |
|---|---|---|
| Numeric labels | `Button("10", ...)`, `Button("500", ...)` | Numbers are universal |
| Color hex values | `"#e57373"`, `"#4A90D9"` | Not displayed to users |
| Technical units | `"px"`, `"ms"` | Debatable — may localize later |
| Seed/test data | `new("Build Reactor library", true)` | Not UI chrome |
| Emoji-only content | `Text("×")` (delete button) | Symbol, not text |

### 1c. Wrap your app root in a `LocaleProvider`

The extractor adds `var t = UseIntl();` to each component, but someone
needs to set the locale at the root. Add this manually in your root
component:

```csharp
class MyApp : Component
{
    public override Element Render()
    {
        var (locale, setLocale) = UseState("en-US");

        return LocaleProvider(locale,
            // ... your app tree
        );
    }
}
```

This also sets `FlowDirection` automatically for RTL locales.

### 1d. Plan for static helper methods

`UseIntl()` is a hook — it only works inside a component's `Render()`
method. If you have static helpers that produce elements with strings:

```csharp
static Element LegendItem(string color, string label) =>
    HStack(4,
        Border(Empty()).Background(color).CornerRadius(2).Size(12, 12),
        Text(label).FontSize(12)
    );
```

You have two options:

1. **Pass the `IntlAccessor`** as a parameter (preferred for leaf helpers):
   ```csharp
   static Element LegendItem(IntlAccessor t, MessageKey labelKey) =>
       HStack(4,
           Border(Empty()).Background(color).CornerRadius(2).Size(12, 12),
           Text(t.Message(labelKey)).FontSize(12)
       );
   ```

2. **Convert to a component** (preferred if the helper is complex enough
   to justify it):
   ```csharp
   class LegendItem : Component { ... }
   ```

The extractor cannot rewrite static methods safely. Handle these manually.

---

## Phase 2: Extract

### 2a. Dry run

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc extract --source samples/Reactor.TestApp --output samples/Reactor.TestApp/Strings/en-US --dry-run
```

This reports every string it would extract and every source rewrite it
would make, without modifying any files. Review the output:

- Are the generated keys sensible? (`Counter.CurrentCount`, `Todo.Add`, etc.)
- Did it pick up strings you want to skip?
- Did it miss anything?

### 2b. Extract and rewrite

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc extract --source samples/Reactor.TestApp --output samples/Reactor.TestApp/Strings/en-US --rewrite
```

This does three things:

1. **Creates `.resw` files** in `Strings/en-US/` with one file per
   component class (e.g., `Counter.resw`, `Todo.resw`).
2. **Rewrites your `.cs` files** — bare strings become `t.Message(Loc.X.Y)`
   calls and `var t = UseIntl();` is inserted where missing.
3. **Converts interpolations** — `$"Count: {n}"` becomes an ICU pattern
   like `Count: {n}` in the `.resw`, and the call site gets argument passing.

The operation is idempotent: running it twice produces no additional changes.

### 2c. Fix up what the extractor couldn't handle

After extraction, manually address:

- Static helper methods (see Phase 1d)
- Strings that were extracted but shouldn't have been (delete from `.resw`,
  revert the source rewrite)
- Strings that were missed (wrap manually with `t.Message(...)`)

---

## Phase 3: Enhance ICU messages

The extractor produces simple `{variable}` placeholders. For production
quality, edit the `.resw` values to add ICU features:

### Plurals

```xml
<!-- Before (extracted) -->
<data name="ItemCount"><value>{count} items</value></data>

<!-- After (hand-edited) -->
<data name="ItemCount">
  <value>{count, plural, one {# item} other {# items}}</value>
</data>
```

### Select (gender, category)

```xml
<data name="WelcomeUser">
  <value>{gender, select, male {Mr.} female {Ms.} other {Mx.}} {name}, welcome!</value>
</data>
```

### Number/date formatting

```xml
<data name="Price"><value>{amount, number, currency}</value></data>
<data name="JoinDate"><value>Joined {date, date, long}</value></data>
```

The extractor automatically converts C# format specifiers (`:C` → currency,
`:D` → long date, etc.) but review these for correctness.

---

## Phase 4: Build and verify

```bash
dotnet build
```

The `Reactor.Localization.Generator` source generator reads your `.resw`
files and emits a typed `Loc` class:

```csharp
// Loc.g.cs (auto-generated)
internal static class Loc
{
    internal static class Counter
    {
        public static readonly MessageKey CurrentCount = new("Counter", "CurrentCount");
        public static readonly MessageKey Reset = new("Counter", "Reset");
    }
}
```

If a key is referenced in code but missing from `.resw`, you get a
compile error. If a key exists in `en-US` but is missing in another
locale, you get `REACTOR_LOC001` warning (or error, depending on
`ReactorLocMissingKeySeverity`).

---

## Phase 5: Translate

### 5a. AI-assisted translation

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc translate --source samples/Reactor.TestApp/Strings/en-US --target fr-FR,ja-JP,ar-SA --missing-only
```

This uses GitHub Copilot (via the `gh` CLI) to pre-fill translations.
Each translated entry is tagged with `ai-translated: pending-review` in
the `.resw` comment field.

> **Note:** The Copilot integration requires `gh copilot` to be
> configured with permission handling. If you see an error about
> `OnPermissionRequest handler is required`, you can create translations
> manually or with another AI tool. The `.resw` format is straightforward
> XML — copy the `en-US` files to each target locale folder and translate
> the `<value>` elements, keeping `{placeholder}` tokens intact.

### 5b. Human review

Open the generated `.resw` files and review. Remove or update the
`ai-translated` comment once approved.

### 5c. Validate

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc validate --resources samples/Reactor.TestApp/Strings
```

Checks:
- ICU syntax is valid (balanced braces, etc.)
- All locales use the same `{variable}` names as the source locale
- No missing keys

### 5d. Coverage report

```bash
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc status --resources samples/Reactor.TestApp/Strings
```

Prints a table like:

```
Locale  Keys  Translated  AI-Draft  Missing  Coverage
en-US     50          50         0        0    100.0%
fr-FR     50          45         5        0     90.0%
ar-SA     50          30        20        0     60.0%
```

---

## Phase 6: Prune unused keys

As you refactor code and remove UI, keys can become orphaned. Clean up:

```bash
# Preview what would be removed
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc prune --source samples/Reactor.TestApp --resources samples/Reactor.TestApp/Strings/en-US --dry-run

# Actually remove them
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj -- loc prune --source samples/Reactor.TestApp --resources samples/Reactor.TestApp/Strings/en-US --rewrite
```

---

## Runtime API quick reference

### In a component

```csharp
public override Element Render()
{
    var t = UseIntl();  // re-renders when locale changes

    return VStack(
        Text(t.Message(Loc.MyComponent.Title)),
        Text(t.Message(Loc.MyComponent.ItemCount, ("count", items.Count))),
        Text(t.FormatDate(DateTimeOffset.Now)),
        Text(t.FormatNumber(price)),
        Text(t.FormatList(names, ListFormatType.Conjunction))
    );
}
```

### Rich text (inline formatting)

`.resw` value:
```xml
<value>Read the <bold>documentation</bold> or <link>contact us</link>.</value>
```

Component:
```csharp
t.RichMessage(Loc.Help.ReadDocs, tags: new()
{
    ["bold"] = text => Text(text).Bold(),
    ["link"] = text => Hyperlink(text, "https://example.com"),
})
```

### RTL / layout direction

`LocaleProvider` automatically sets `FlowDirection` on its subtree. When
the locale changes to an RTL language (Arabic, Hebrew, etc.), WinUI
inherits `FlowDirection.RightToLeft` down the visual tree, flipping
layout for all descendants.

Access the direction manually for custom layout adjustments:

```csharp
var t = UseIntl();
if (t.IsRtl) { /* adjust layout */ }
FlowDirection dir = t.Direction;
```

### Pseudolocalization (QA)

```csharp
LocaleProvider("en-US", pseudoLocalize: true, MyApp())
```

Transforms strings to accented equivalents wrapped in `[!! ... !!]`
markers with ~30% padding. Missing keys render as `[?? Namespace.Key ??]`.

---

## Known limitations

- **Static methods cannot call `UseIntl()`** — pass `IntlAccessor` as a
  parameter or convert to a component.
- **Expression-bodied `Render()` methods** — the extractor rewrites
  string literals to `t.Message(Loc.X.Y)` but cannot inject the
  `var t = UseIntl();` declaration into an expression body. This produces
  a compile error (`'t' does not exist`). Convert expression-bodied
  `Render()` methods to block bodies before or after extraction:
  ```csharp
  // Before (breaks):  public override Element Render() => Text(t.Message(Loc.X.Y));
  // After (works):
  public override Element Render()
  {
      var t = UseIntl();
      return Text(t.Message(Loc.X.Y));
  }
  ```
- **Strings used as both labels and state keys** must be refactored before
  extraction (see Phase 1a).
- **The extractor may over-extract** numeric labels, emoji, or technical
  abbreviations. Review the dry-run output.
- **MRT/ResourceLoader not used** — the runtime reads `.resw` XML files
  directly from disk rather than going through the Windows PRI resource
  system. This avoids `FileNotFoundException` issues with `ResourceLoader`
  in unpackaged apps, but means the `.resw` files must be copied to the
  output directory (see Prerequisites). This may be revisited if Reactor
  moves to packaged (MSIX) deployment.
- **Hot reload** of `.resw` changes is not yet supported. Restart the app
  to pick up resource changes during development.
