# Chunk 11 — ICU + Locale Formatting — Threat Model

**Status:** Phase 2 — review complete
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer focus:** Runtime ICU MessageFormat parsing and locale-aware formatting. The threat surface is the combination of (a) **semi-trusted `.resw` ICU patterns** authored by translators and (b) **untrusted argument values** flowed in from app code (which may itself be carrying user input). Primary STRIDE concerns are availability (parser exceptions / unhandled exceptions reaching render), information disclosure (argument-name lookup scope), and tampering (BiDi / RTL override codepoints surviving to the rendered UI).

> **Headline findings:**
> 1. **`MessageFormatter.FormatMessage` exceptions are unhandled** in `IntlAccessor.Message` / `RichMessage` (`IntlAccessor.cs:65-69` and `:98-103`). A malformed ICU pattern in a `.resw` (e.g., `MalformedLiteralException`) or a translator-introduced placeholder name not present in `args` (`VariableNotFoundException`) propagates out of the localization layer and into the WinUI render path. A single bad translation row crashes the page that renders that string. Severity **High** (DoS).
> 2. **Argument lookup pulls from the entire reflected object surface** (`IntlAccessor.cs:289-305`). When a developer passes a non-anonymous DTO (e.g., `t.Message(key, currentUser)`), every public instance property of that object becomes addressable from the `.resw` ICU pattern. A malicious or compromised translation can read fields the developer never intended to expose — e.g., `{Email}`, `{TokenHash}`, `{InternalId}`. Severity **High** (information disclosure / privilege boundary on the translator).
> 3. **No BiDi / RTL-override sanitization** of either `.resw` patterns or argument values. Codepoints U+202A-U+202E and U+2066-U+2069 in either source flow unchanged into the rendered text (`IntlAccessor.cs:65-72`, `:97-106`). Combined with rich-text mapping (`RichMessage` factories that may render hyperlinks, buttons, etc.), this enables homograph / display-spoof attacks in trust-relevant UI. Severity **Medium**.
> 4. **`_assetCache` is a non-thread-safe `Dictionary<string, string>`** (`IntlAccessor.cs:23`) on an instance shared via Context across the entire subtree. A background-thread caller of `Asset()` can race and corrupt the dictionary. Severity **Low** (DoS / latent crash).

---

## 1. Scope

| File | Lines | Role |
|---|---:|---|
| `src/Reactor/Core/Localization/IntlAccessor.cs` | 358 | Core accessor: `Message`, `RichMessage`, `FormatNumber`, `FormatDate`, `FormatList`, `Asset`, args reflection, rich-text regex |
| `src/Reactor/Core/Localization/MessageCache.cs` | 52 | Per-locale `Jeffijoe.MessageFormat.MessageFormatter` cache |
| `src/Reactor/Core/Localization/MessageKey.cs` | 11 | `(Namespace, Key)` value type |
| `src/Reactor/Core/Localization/LocaleContext.cs` | 60 | Legacy ambient + `IntlContexts.Locale` Context handle |
| `src/Reactor/Core/Localization/LocaleProviderElement.cs` | 69 | DSL element + `LocaleProviderComponent` lifecycle |
| `src/Reactor/Core/Localization/IStringResourceProvider.cs` | 14 | Resource-loader abstraction |
| `src/Reactor/Core/Localization/ReswResourceProvider.cs` | 132 | Disk-backed `.resw` XML reader (`XDocument.Load`) |
| `src/Reactor/Core/Localization/PseudoLocalizer.cs` | 89 | Accent-map / wrap-pad transformer for testing |
| `src/Reactor/Core/Localization/RtlHelper.cs` | 22 | Hard-coded RTL language list |
| `src/Reactor/Core/Localization/DateFormatOptions.cs` | 17 | `DateStyle` enum + options |
| `src/Reactor/Core/Localization/NumberFormatOptions.cs` | 18 | `NumberStyle` enum + options |
| `src/Reactor/Core/Localization/ListFormatType.cs` | 10 | `Conjunction`/`Disjunction` enum |
| **Total** | **852** | |

The actual ICU MessageFormat parser is the third-party `MessageFormat` NuGet package (Jeffijoe, v8.0.0; commit `dae6f0b…` per `messageformat.nuspec`). Reactor's surface in this chunk is the *adapter*; the parser internals are out-of-scope but its **exception contract** is in-scope (we must handle what it throws).

---

## 2. Data-flow diagram

```
.resw on disk (semi-trusted, translator-authored)
        │
        │ XDocument.Load            ReswResourceProvider.cs:112
        ▼
  Dictionary<locale, Dictionary<ns, Dictionary<key, value>>>      ReswResourceProvider.cs:38
        │
        │ GetString(locale, ns, key)
        ▼
  IntlAccessor.ResolvePattern  ── falls back to defaultLocale ── IntlAccessor.cs:267-285
        │
        │ pattern (string)
        │
   App code calls ┐
   t.Message(key, args)              IntlAccessor.cs:57-73
   args = anonymous obj │ DTO │ IDictionary
        │
        ▼
  IntlAccessor.ToArgsDictionary    IntlAccessor.cs:289-305
   ── reflects ALL public instance properties of args.GetType() ──
        │
        │ IDictionary<string, object>
        ▼
  MessageCache.Format(locale, pattern, dict)   MessageCache.cs:28-35
        │
        ▼
  Jeffijoe.MessageFormat.MessageFormatter.FormatMessage(pattern, dict)
        │  (third-party — parses ICU, applies plural/select, substitutes vars)
        │  ↳ throws MalformedLiteralException on bad ICU
        │  ↳ throws VariableNotFoundException on undefined placeholder
        │  ↳ throws on bad type-coercion etc.
        ▼
  formatted string ─────► (optional) PseudoLocalizer.Transform   PseudoLocalizer.cs:38-71
        │
        ▼
   Returned to render code as TextBlockElement.Content / etc.

  RichMessage path: also runs Regex `<(\w+)>(.*?)</\1>`        IntlAccessor.cs:226-265
                    against the formatted result, dispatches
                    each match to Func<string, Element> tag factories.
```

`Asset(path)` is a parallel data flow — locale-qualified asset-path probing via `File.Exists` (`IntlAccessor.cs:121-158`).

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption |
|---|---|---|
| Disk → process: `Strings/{locale}/{ns}.resw` | inbound | **Semi-trusted.** Authored by app team and translators. A malicious translation PR is the realistic threat actor. |
| App code → IntlAccessor (`args` object) | inbound | App code is "trusted code", but the **values** in `args` are often user-controlled (display name, file name, error message). Treat values as untrusted. |
| IntlAccessor → Jeffijoe.MessageFormat | outbound to dependency | Trusted dependency for parsing logic, but its exception contract is the boundary we own. |
| IntlAccessor → render path (returned string / Element) | outbound | The string is rendered into XAML `TextBlock` (or, for `RichMessage`, into developer-supplied factories that may produce hyperlinks/buttons). The renderer treats Reactor strings as already-safe. |
| `Asset()` → filesystem (`File.Exists`) | outbound | Probes disk based on locale + path. If `Locale` itself is untrusted, this is a path-traversal probe. |
| Locale → `new CultureInfo(locale)` | outbound | Throws on invalid input; .NET CLR is the trusted dependency. |

The **single most important boundary** in this chunk is between a translator's `.resw` and the developer's expectation about which argument names are reachable. The current implementation makes that boundary *much* more permeable than the developer expects.

---

## 4. Asset inventory

| Asset | Where | Why an attacker cares |
|---|---|---|
| App availability | render path | A single bad ICU pattern crashes any page that touches the affected message. |
| User-visible text fidelity | rendered TextBlocks / RichMessage factories | RTL/BiDi overrides, ZWJ, control characters, look-alike domains in formatted output → phishing / consent-spoof. |
| In-process object graph | DTOs passed as `args` | Reflection over the object exposes any public instance property. A translator who can change a `.resw` row to `{InternalToken}` exfiltrates the field via UI text. |
| Filesystem map | `Asset()` `File.Exists` probes | Probing a locale-qualified path lets an attacker (who controls `path` or `Locale`) enumerate which files exist under `AppContext.BaseDirectory`. |
| Cached `MessageFormatter` instances | `MessageCache._formatters` | `Flush()` / `Flush(locale)` can purge them; concurrent flush + format can race (low impact — `ConcurrentDictionary` is thread-safe). |
| Process integrity (memory) | none in managed code beyond what BCL gives us | Not a memory-safety surface; pure managed. |

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Recommendation |
|---|---|---|---|---|---|---|---|
| T-01 | DoS | Malformed ICU in `.resw` (`{count, plural, one {# item} other {# items} ` — missing close brace) throws `MalformedLiteralException` from `Jeffijoe.MessageFormat`; `IntlAccessor.Message` does not catch it. | Translator commits a typo; or a translation tool emits malformed ICU. | The page rendering that key throws into the reconciler and likely crashes the visual subtree on every render. | High (typos happen). | None. `IntlAccessor.cs` has zero `try/catch`. | **F-01.** Wrap `_messageCache.Format(...)` in try/catch and degrade to a marker string (e.g., the raw pattern, or `[!! malformed ICU: <key> !!]`). Log once per (locale,key) to avoid log flooding. |
| T-02 | DoS | Translator references an argument name that the developer does not pass (e.g., `.resw` has `{user_name}` but app passes `{userName}`). `Jeffijoe` throws `VariableNotFoundException`. | Renaming bug, or translator inserting placeholder names hoping developer will provide. | Same as T-01 — render-time crash. | High (rename mismatches are common). | None. | Same handler as T-01; specifically catch `VariableNotFoundException` and emit `{name}` literally so the UI shows `Hello {name}` instead of crashing. |
| T-03 | Information disclosure | A translator changes a `.resw` value to reference a property name on the args object that the developer didn't intend to expose (e.g., the developer passes `currentUser` and expects only `{Name}`, but the row reads `Welcome {Email} ({InternalUserId})`). `ToArgsDictionary` reflects ALL public properties. | Hostile or compromised translator; or an LLM-translated `.resw` (Chunk 07) hallucinates an extra placeholder; or a leaked translation account. | Tokens, emails, internal IDs, anything in a public property of the passed DTO ends up rendered to UI / logs / screenshots. | Medium-High — depends on whether apps pass DTOs vs anonymous objects. The codebase's own tests pass anonymous objects, but the design doc gives no warning. | None. The `[UnconditionalSuppressMessage]` on `ToArgsDictionary` (`IntlAccessor.cs:287-288`) shows the team is aware of the reflection but hasn't scoped it. | **F-02.** Either (a) restrict reflection to anonymous types only (`type.IsDefined(CompilerGeneratedAttribute) && type.Name.Contains("AnonymousType")`), or (b) require args to be `IDictionary<string, object>`, or (c) add an opt-in attribute (`[LocArgs]`) to whitelist properties. Option (a) is the smallest behavioral change. Document the constraint in the localization design doc. |
| T-04 | Tampering / spoofing | RTL override codepoints (U+202A LRE, U+202B RLE, U+202D LRO, U+202E RLO, U+2066-U+2069 isolate family) survive end-to-end. A malicious translation can include `‮` to flip subsequent text right-to-left, making `gpj.exe` appear as `exe.jpg`, etc. | Hostile translator; or attacker-controlled argument value. | UI-level spoof — phishing, file-extension trick, fake button labels in trust-relevant prompts (delete confirmations, navigation). Severity rises in `RichMessage` because the rendered output may include hyperlinks whose visible text disagrees with the underlying URL. | Medium. | None. The post-format string is returned as-is. | **F-03.** At the IntlAccessor boundary, reject or strip the BiDi override codepoints from formatted output, OR wrap formatted text in U+2068 (FSI — first-strong isolate) + U+2069 (PDI) so embedded direction overrides cannot escape the message. Keep an opt-in for legitimate use (some scripts genuinely need RLE). |
| T-05 | Tampering | Argument values are interpolated into `RichMessage`'s formatted output and then matched against the regex `<(\w+)>(.*?)</\1>`. A user-controlled argument value containing `<bold>evil</bold>` will be parsed as a tag and dispatched to `tags["bold"]` factory. | Untrusted user input flowed into a `RichMessage` arg. | A hostile arg can synthesize tags the developer didn't expect (e.g., `<link>`, dispatching the user-controlled string to a hyperlink factory that builds a navigation), turning text injection into element injection. | Medium (only matters when `RichMessage` has tags AND args). | The factory is developer-supplied so the consequences depend on what the factory does. | **F-04.** Document this clearly: arg values rendered through `RichMessage` are *not* HTML/tag-escaped. Either (a) escape `<` in arg values before formatting, (b) tag-resolve only inside the *pattern* before arg substitution (requires a different parse order), or (c) require tag-name allowlist enforcement explicitly in the API. |
| T-06 | DoS | Recursion / nested `select`/`plural` in ICU pattern → deep parser recursion in Jeffijoe. Reactor imposes no nesting cap. | Malicious `.resw`. | Stack overflow on UI thread → process termination. | Low. We don't measure depth, but Jeffijoe's parser is iterative for the most part; the actual recursion is in the Patterns object tree. | None. | **F-05.** Add a sanity-check size cap on `.resw` value length AND on max nesting depth (count `{` minus `}` running max). Either at `ReswResourceProvider.ParseReswFile` (cheap), or at first-format time. A 4 KB / depth-10 cap covers all real-world ICU. |
| T-07 | DoS | Regex `<(\w+)>(.*?)</\1>` with `Singleline` and a backreference — pathological input could provoke catastrophic backtracking. | Malicious `.resw` (since regex is run on formatted result, which is mostly translator-controlled). | UI thread hang. | Low. The regex is reasonably simple; backreference + non-greedy is not worst-case exponential by itself, but `.NET` regex is backtracking by default. | None. The regex has no timeout. | **F-06.** Pass a `TimeSpan` timeout to `Regex.Matches` (e.g., 50ms) or recompile with `RegexOptions.NonBacktracking`. The `Compiled` option does not constrain backtracking. |
| T-08 | EoP / Tampering | Path traversal via `Asset(path)` when `Locale` is user-controlled (settings UI sets the locale). `Path.Combine(dir, Locale, fileName)` does not normalize `..`. `File.Exists` only probes — but the *returned* path is then used by the caller (likely `Image` source), which may follow it. | A user-supplied locale string like `../../../../Users/Public/secret`. | Read-arbitrary-file-existence (probe) + serve attacker-chosen file as an asset. | Low. Most apps don't surface a free-form locale text box. | None. | **F-07.** Validate `locale` against `^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$` in `IntlAccessor` ctor and `LocaleProviderComponent.Render`. Reject anything else. |
| T-09 | DoS / Concurrency | `_assetCache` is a non-thread-safe `Dictionary<string, string>` shared across the subtree via Context. Any call to `Asset()` from background work races with the UI thread. | Background-thread asset lookup. | Dictionary corruption → `InvalidOperationException` ("Operations that change non-concurrent collections must have exclusive access") on a future read; in rare cases, infinite loop on concurrent `Add`. | Low (UI thread mostly). | None — relies on UI-thread-only convention. | **F-08.** Use `ConcurrentDictionary<string, string>` (cheap; matches `_propertyCache`'s pattern), or document that `Asset()` is UI-thread-only and assert it. |
| T-10 | DoS / Concurrency | `LocaleContext._subscribers` is a `List<Action>` with no synchronization (`LocaleContext.cs:29-58`). `Subscribe` / `Unsubscribe` / `NotifySubscribers` race; `Add` during enumerate would crash without the snapshot — they snapshot, so it's better, but `Add`/`Remove` together still race. | Locale change while components are subscribing. | Lost subscription / duplicate notification / `IndexOutOfRangeException`. | Low — only matters if Subscribe/Unsubscribe happen off-UI-thread. | Snapshot-on-notify (line 57) avoids the most common crash. | **F-09.** Either lock around `Subscribe/Unsubscribe`, or use `ImmutableList<Action>` with CAS. |
| T-11 | Information disclosure | `Debug.WriteLine` of missing keys (`IntlAccessor.cs:277, 283`, `ReswResourceProvider.cs:82, 128`) and exceptions includes locale + key + path. Debug listeners on customer machines can ingest these. | Adversary with a debug listener (DebugView) running. | Reveals app key namespace structure, `Strings/...` paths. | Very low. Debug-only; not in Release listeners by default. | Compiler removes `Debug.WriteLine` in non-Debug builds *only if* `[Conditional("DEBUG")]` is honored — `Debug.*` already does this. | None required; document. |
| T-12 | DoS / memory | `_propertyCache` (`IntlAccessor.cs:24`) is a `static` `ConcurrentDictionary<Type, PropertyInfo[]>` — never flushed. | Pathological app that creates thousands of short-lived anonymous arg types. | Memory growth. | Very low — anonymous types are bounded by emit sites in compiled code. | The cache key is `Type`; bounded by the emit count. | None required. |
| T-13 | DoS | `MessageCache` and `ReswResourceProvider` cache by `locale` key with no cap. An app that programmatically creates thousands of unique (e.g., randomized) locale strings would unboundedly grow both caches. | Hostile app code (out-of-scope) or buggy app. | Memory growth. | Very low — locales are bounded set in any real app. | None. | None required. Note in design doc that `Locale` should come from a fixed allowlist. |
| T-14 | Repudiation | None applicable — this chunk does not log security-relevant events. | n/a | n/a | n/a | n/a | n/a |
| T-15 | Spoofing | None applicable at this layer (no auth here). | n/a | n/a | n/a | n/a | n/a |

---

## 6. Findings

### F-01 — Unhandled `MessageFormatter` exceptions crash the render path  *(High)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:65-69`, `:97-103`; `src/Reactor/Core/Localization/MessageCache.cs:28-35`.

`Message` calls `_messageCache.Format(...)`, which calls `MessageFormatter.FormatMessage`. The Jeffijoe parser throws (per its public XML doc, `Jeffijoe.MessageFormat.xml`):

- `MalformedLiteralException` on bad ICU syntax (mismatched braces, bad type-name, malformed plural keyword);
- `VariableNotFoundException` when a placeholder name is not in the args dictionary;
- `FormatException` / `ArgumentException` on type mismatches inside `plural` (e.g., `count = "x"`).

`IntlAccessor.cs` contains zero `try`/`catch` (verified via grep). The exception bubbles to the render path. In WinUI/Reactor a thrown exception during render is at best a swallowed component-tree fault (per `Reconciler.Update.cs` error-boundary semantics), at worst a visual-tree crash.

This means **a single typo in a translator-submitted `.resw` row breaks the page that displays that string** — every time it renders. There is no fallback to the default locale on parse failure (the fallback is for *missing* keys only — see `ResolvePattern` lines 267-285).

**Fix:** wrap the `Format` calls in try/catch. On exception, log once and degrade to the original pattern (so the developer sees `Hello {name}` rather than a crash).

```csharp
string formatted;
try
{
    formatted = _messageCache.Format(Locale, pattern, dict);
}
catch (Exception ex) when (ex is Jeffijoe.MessageFormat.Parsing.MalformedLiteralException
                            or Jeffijoe.MessageFormat.Formatting.VariableNotFoundException
                            or FormatException)
{
    Debug.WriteLine($"[Reactor.Intl] Format failed for '{key}' in '{Locale}': {ex.Message}");
    formatted = pattern; // fall through with raw pattern
}
```

This also makes pseudolocalization mode resilient to malformed source strings.

### F-02 — Argument lookup leaks every public property of the args object  *(High)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:289-305`.

```csharp
private static IDictionary<string, object> ToArgsDictionary(object args)
{
    if (args is IDictionary<string, object> dict)
        return dict;

    var result = new Dictionary<string, object>();
    var props = _propertyCache.GetOrAdd(args.GetType(),
        t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
    foreach (var prop in props)
    {
        var value = prop.GetValue(args);
        if (value is not null)
            result[prop.Name] = value;
    }
    return result;
}
```

`GetProperties(BindingFlags.Public | BindingFlags.Instance)` enumerates **every** public instance property of `args.GetType()`. The intent is anonymous types (`new { name = "Alice" }`) but nothing prevents passing a real DTO:

```csharp
var user = await GetCurrentUserAsync();   // has Email, AccessToken, InternalId, …
intl.Message(Loc.Welcome.Title, user);
```

A `.resw` row authored as `Welcome {Email} (token: {AccessToken})` will resolve and render those values. The translator does not need code-execution rights — only the ability to commit a string change.

The trust-model assumption stated in the threat-model intro is that `.resw` is *semi-trusted*. The current implementation upgrades a translator's text-edit privilege into a "read any public field of any object the developer hands me" privilege. That is a lateral-movement primitive when the developer does the natural thing.

**Fix:** restrict reflection to anonymous types, OR require `IDictionary<string, object>`. The minimal fix is one type-check:

```csharp
private static bool IsAnonymousType(Type t) =>
    t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
    && t.IsGenericType
    && t.Name.Contains("AnonymousType")
    && (t.Name.StartsWith("<>") || t.Name.StartsWith("VB$"));
```

If not anonymous and not a dictionary, throw `ArgumentException` at the call site so the developer notices in dev. (Document the constraint in the localization design doc and the `Message` xmldoc.)

A weaker mitigation is to add an opt-in attribute (`[LocArg]`) on properties, but that's a larger surface change.

### F-03 — No BiDi / RTL-override sanitization on output  *(Medium)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:65-72`, `:97-106`. Also `RtlHelper.cs` only decides flow direction at the locale level — there is no codepoint filtering.

The post-format string returned to the renderer can contain U+202A LRE, U+202B RLE, U+202C PDF, U+202D LRO, U+202E RLO, U+2066-U+2069 isolate family. These codepoints alter how subsequent characters are rendered without changing the underlying logical order. They are the standard primitive behind:

- file-extension spoofing (`evil_‮gpj.exe` rendered as `evil_exe.jpg`);
- visual swap of button labels around a fragment of a confirmation prompt;
- making a hyperlink's *visible* text disagree with the URL it points to (especially in `RichMessage`, where a `<link>{url}</link>` factory is plausible).

Either the `.resw` (translator) or the args (potentially user-controlled — file names, comment text) can carry these codepoints.

**Fix (preferred):** wrap each formatted message with FSI/PDI:

```csharp
formatted = "⁨" + formatted + "⁩";
```

This isolates any embedded BiDi from leaking direction into adjacent UI text. Apply at the `Message` and `RichMessage` boundary. For arg values specifically, a stronger fix is to strip U+202A-U+202E from arg string values before passing to `MessageFormatter`.

**Caveat:** some legitimate translations use these codepoints. Provide a per-call escape hatch (e.g., `IntlAccessor.MessageRaw`) for the exceptional case.

### F-04 — `RichMessage` tag regex runs after arg substitution; arg values can synthesize tags  *(Medium)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:97-111`, `:226-265`.

`RichMessage` formats the ICU pattern *first*, which substitutes args into the result, *then* runs the tag regex over the result. So an arg value that contains `<link>http://attacker</link>` will be parsed as a tag and dispatched to the developer-provided `tags["link"]` factory — the same factory the developer assumed only fires for tags in the trusted `.resw`.

If the factory builds a `HyperlinkButton` whose `NavigateUri` comes from the matched content, this is a navigation-injection primitive driven by user-controlled arg values.

**Fix:** either
- (a) HTML-escape `<`, `>`, `&` in arg values before formatting (and unescape inside text-only spans);
- (b) parse tags in the *pattern* before substituting args (requires a small mini-parser since ICU's substitution and Reactor's tags are now interleaved); or
- (c) document loudly that tag factories receive untrusted text and the factory must validate (this is what the current implementation effectively assumes).

Option (a) is least disruptive. The escape only affects literal `<` characters in arg values, which is rarely intentional in localized strings.

### F-05 — No depth or size cap on ICU patterns  *(Low)*

**Location:** `src/Reactor/Core/Localization/ReswResourceProvider.cs:108-131`, `MessageCache.cs:28-35`.

`ParseReswFile` reads any `<value>` content of any size. `MessageFormatter.FormatMessage` is then asked to parse it. Jeffijoe builds a tree of pattern nodes; deep nesting → deep tree → recursion in formatters. Without measurement we can't claim a stack overflow exists, but there is no defense-in-depth cap on either the value length or the brace-nesting depth.

**Fix:** add a cheap pre-check in `ParseReswFile` after reading `<value>.Value` — reject (and log) values longer than e.g. 8 KB or with running brace-depth > 16. Real-world ICU never exceeds either.

### F-06 — Rich-text regex has no timeout  *(Low)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:226`.

```csharp
private static readonly Regex TagPattern = new(@"<(\w+)>(.*?)</\1>", RegexOptions.Compiled | RegexOptions.Singleline);
```

`Compiled` does not bound backtracking. The non-greedy quantifier with a backreference is not classically catastrophic but is not provably linear either; a long input with many `<x>` openers and no matching close has well-known degenerate cases. Combined with `Singleline`, an entire long `.resw` value flows through it.

**Fix:** add a `TimeSpan` timeout (50ms) to the constructor, or use `RegexOptions.NonBacktracking` (.NET 7+).

### F-07 — `Asset()` path-traversal probe via untrusted locale  *(Low)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:121-158`.

`Path.Combine(dir, Locale, fileName)` on a malicious `Locale` like `../../../../Windows/System32` produces a path that escapes `Strings/`. `File.Exists(localePath)` probes it; the resulting boolean tells the caller whether the file exists. Even without serving the file, this is an OS-level enumeration primitive.

If `Locale` is *also* the input to `LocaleProviderComponent`, and the app exposes locale selection to end users, an end user can probe `File.Exists` for any path on the machine.

**Fix:** validate `locale` once in `IntlAccessor` ctor:

```csharp
private static readonly Regex LocaleRx = new(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);
if (!LocaleRx.IsMatch(locale))
    throw new ArgumentException($"Invalid BCP-47 locale: '{locale}'", nameof(locale));
```

This also protects `new CultureInfo(locale)` from the ICU "neutral culture from any string" surprises.

### F-08 — Non-thread-safe `_assetCache`  *(Low)*

**Location:** `src/Reactor/Core/Localization/IntlAccessor.cs:23`.

```csharp
private readonly Dictionary<string, string> _assetCache = new();
```

`IntlAccessor` is constructed once per (locale, defaultLocale, pseudo) triple via `Context.UseMemo` (`LocaleProviderElement.cs:33-35`) and shared via Context. Any background-thread call to `Asset()` races with the UI thread. The convention "UI thread only" is not asserted.

**Fix:** change to `ConcurrentDictionary<string, string>` to mirror `_propertyCache`. One-line change.

### F-09 — `LocaleContext._subscribers` is unsynchronized  *(Low)*

**Location:** `src/Reactor/Core/Localization/LocaleContext.cs:29-58`.

`Subscribe`/`Unsubscribe` mutate a `List<Action>` without locking. `NotifySubscribers` snapshots the list (line 57), avoiding enumeration crashes, but `Subscribe` and `Unsubscribe` themselves can race against each other.

The class is documented as "Legacy ambient context… kept for backward compatibility" (line 17-18). New code uses `IntlContexts.Locale` Context. If the legacy path is genuinely deprecated, mark it `[Obsolete]` and remove the subscriber API; otherwise lock around the list.

### F-10 — `PseudoLocalizer` regex over partially-formatted output is benign but confusingly named  *(Informational)*

**Location:** `src/Reactor/Core/Localization/PseudoLocalizer.cs:33`, `:38-71`.

`PseudoLocalizer.Transform` is applied to the *post-format* string (`IntlAccessor.cs:72, :106`), so any `{...}` left in the result is from arg values, not from ICU placeholders. The regex `\{[^}]*\}` will preserve them un-accented, which is the *opposite* of what the comment ("Preserves ICU syntax") implies in this context. Behaviour is harmless (just slightly inconsistent pseudolocalized output if an arg legitimately contains `{}`), but the comment should be corrected so future maintainers don't move the call earlier in the pipeline thinking it would help.

---

## 7. Open questions

1. **Is `IntlAccessor` documented as UI-thread-only?** The spec doc (`docs/specs/005-localization-design.md`) does not say. If yes, several findings (F-08, F-09) downgrade. If no, fix them as written.
2. **What is the policy on translator trust?** If the translation pipeline goes through Chunk 07 (`TranslateCommand` calling Azure OpenAI), the translation *is* model output and the model is explicitly untrusted. F-02 and F-03 then escalate from Medium to High. The team should confirm whether ML-translated `.resw` files are auto-merged or human-reviewed.
3. **Is there a render-time error boundary** that catches an exception thrown out of a `TextBlockElement.Content` getter? If yes, F-01 stays "DoS of one component"; if no, it's "DoS of the page". The reconciler review (Chunk 14) should clarify.
4. **Should `Asset()` be in this chunk at all?** It is locale-keyed but is filesystem-touching code. Consider moving to a separate "ResourceResolver" review surface that includes `ResourceLoader` parity questions.
5. **Is rich-text tag dispatch deliberately HTML-shaped?** If apps copy/paste from HTML they will hit the `<bold>foo & bar</bold>` problem with `&`. We should decide whether `RichMessage` is "HTML-ish but Reactor-only" (no entity decoding, no `&amp;` conversion) and document.
6. **`new CultureInfo(locale)` on Linux/Globalization Invariant Mode** — does Reactor ever run with `InvariantGlobalization=true`? If yes, all `new CultureInfo("xx-YY")` throws and the entire localization layer collapses. Worth a build-time check.

---

## 8. Out-of-scope referrals

- **Jeffijoe.MessageFormat parser internals** — recursion bounds, integer-overflow in plural-rule evaluation, allocator behavior on attacker patterns. Tracked as a third-party dependency review; if the team wants a deeper guarantee, fork or fuzz the parser separately. Reactor's only obligation here is to handle its declared exceptions (F-01).
- **`.resw` XML parsing (XXE / billion-laughs)** — `XDocument.Load(path)` is used in `ReswResourceProvider.cs:112`. Default `XmlReader` in modern .NET prohibits DTD processing, so XXE is not exposed, but a defense-in-depth `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersFromEntities = 0 }` should be set explicitly. This overlaps with **Chunk 06** (Localization CLI) and **Chunk 08** (Source Generators) which also parse `.resw` — recommend a shared parser with hardened settings.
- **Generated `Loc.g.cs` keys** — covered by Chunk 08. Findings there about `entry.Key` injection apply to the generated keys consumed here.
- **Translator trust pipeline (Azure OpenAI translate)** — Chunk 07. F-02 / F-03 severity depends on Chunk 07's review verdict.
- **Persisted locale state** — none today; locale lives in props. If app-state persistence ever stores it, see Chunk 13.
- **WinUI flow-direction propagation** — `LocaleProviderComponent` sets `FlowDirection` on a wrapping `BorderElement` (`LocaleProviderElement.cs:64-67`). The trust assumption is that WinUI inherits FlowDirection correctly to all descendants. This is a WinUI/WindowsAppSDK behavior — out of scope per §10 of the chunking doc.
