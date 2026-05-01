# Chunk 12 — Other Parsers: Threat Model

**Status:** Phase 2 review (filled in)
**Reviewer:** automated security audit pass
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3` (HEAD as of review)
**Companion:** `000-chunking-and-threat-model.md`, Chunks 02 (devtools tools, where selector strings arrive) and 13 (navigation lifecycle, where `RouteArgs` are consumed).

This chunk covers three small, unrelated parsers that share two things: they each turn an external-or-semi-external string into structured runtime state, and they were originally built as "minimal subset" parsers without an explicit hostile-input threat model. The review focus from the chunking doc — DoS via parser complexity, parser confusion across two URI surfaces, and unbounded backtracking — drives the findings.

---

## 1. Scope

| File | LOC |
|---|---|
| `src/Reactor/Hosting/Devtools/SelectorParser.cs` | 92 |
| `src/Reactor/Hosting/Devtools/SelectorResolver.cs` | 264 |
| `src/Reactor/Charting/PathDataParser.cs` | 174 |
| `src/Reactor/Core/Navigation/DeepLinkMap.cs` | 259 |
| **Total** | **789** |

There is no other URL-parsing code under `src/Reactor/Core/Navigation/` — `NavigationStack.cs`, `NavigationDiagnostics.cs`, and friends do not touch `Uri`/`Uri.UnescapeDataString`. URL parsing in this chunk is exclusively `DeepLinkMap.cs`.

**Callers of in-scope code (used to ground trust assumptions):**

- `SelectorParser.Parse` is reached from `SelectorResolver.Resolve` (`SelectorResolver.cs:39`) which is invoked by every devtools tool in `DevtoolsTools.cs`/`DevtoolsFireTool.cs`/`DevtoolsPropertyTools.cs` (Chunk 02) for user-supplied `selector` arguments.
- `PathDataParser.Parse` is reached from `D3Dsl.D3Path` / `D3PathTranslated` (`Charting/D3Dsl.cs:232,243`) and `ChartDsl.ParsePathData` (`Charting/ChartDsl.cs:348`). String inputs at those sites originate in app-author code or D3 chart `PathBuilder` output. Data-driven charts can push attacker-influenced numeric data through the builder, but the builder format itself comes from trusted code.
- `DeepLinkMap` is consumed from app code; the canonical sample `samples/NavigationDemo/App.cs:31` calls `deepLinks.Resolve(args[dlIdx + 1])` against a raw `--deep-link` command-line string — i.e., a value supplied by whatever invoked the process (another app, a browser via protocol activation, `cmd /c`, `ShellExecute`, etc.).

---

## 2. Data-flow diagram

```
                                 ┌──────────────────────────┐
  devtools JSON-RPC client ──►   │ McpDispatcher (Chunk 01) │
  (loopback HTTP / stdio)        └────────────┬─────────────┘
                                              ▼
                                  DevtoolsTools.* handlers
                                              │   selector: string
                                              ▼
                                ┌──────────────────────────┐
                                │ SelectorParser.Parse     │  regex IR
                                │ SelectorResolver.Resolve │  recursive walk
                                └──────────────┬───────────┘
                                              ▼
                                       UIElement* + UIA reads


  data-driven D3 chart ──►   D3 PathBuilder ──►  SVG `d=` string
  (numeric inputs)                                     │
                                                       ▼
                                          PathDataParser.Parse
                                                       │
                                                       ▼
                                          PathGeometry (COM)


  external launcher / OS ──► protocol activation arg  ──┐
  (`myapp:///path?...`)                                 ▼
                                                DeepLinkMap.Resolve
                                                  ├─ Resolve(Uri)    ─► uri.AbsolutePath  (System.Uri-decoded)
                                                  └─ Resolve(string) ─► raw substring     (NOT System.Uri-decoded)
                                                              │
                                                  CompilePattern → compiled Regex (per .Map())
                                                              │
                                                  Regex.Match(path) → group captures
                                                              │
                                                  ParseQueryString → Uri.UnescapeDataString
                                                              │
                                                              ▼
                                                       RouteArgs → app `TRoute` factory
```

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption challenged here |
|---|---|---|
| Devtools client → SelectorParser/Resolver | inbound | "Loopback caller is trusted" (Chunk 01 baseline). This chunk reviews what happens if it is **not** — the parser is the post-dispatch surface that runs on caller-controlled bytes. |
| App author / chart input → PathDataParser | inbound | App-author strings are trusted; numeric values funneled into a builder are semi-trusted (chart data may be telemetry/PII/external). |
| External process / OS launcher → DeepLinkMap | inbound | **Untrusted.** Protocol activation is the canonical "another app invoked us" entry, and that other app is not bounded by our threat model. |
| DeepLinkMap → app `TRoute` factory | outbound | The factory consumes `RouteArgs` and may use them as file paths, IDs into stores, or page identifiers. Confidentiality/integrity downstream depends on what the parser hands over. |

---

## 4. Asset inventory

| Asset | Worth attacking because… |
|---|---|
| Devtools-tool dispatch CPU and stack | A selector that hangs the parser or recurses the visual tree blocks every other devtools call (single dispatcher in Chunk 01). |
| WinUI tree integrity | Selector resolution returns a `UIElement` that "fire" tools then click, set text on, etc. Resolving the wrong element is a confused-deputy. |
| App routing decisions | A deep link that resolves to a different route than the OS / launcher believed it was sending is a **confused-deputy**: the launcher thinks it sent `/public/X`, the app navigates to `/admin/X`. |
| App process liveness | Any uncaught exception from a parser brought up by attacker-controlled input is a DoS. `int.Parse(\d+)` with a 100-digit string is the cheapest crash vector here. |
| App process memory | An SVG path string whose only job is `M0,0 L1,1 L2,2 …` × N forces N COM `LineSegment` allocations with no cap. Same for query strings of millions of `&`-separated pairs. |

---

## 5. STRIDE table

| # | STRIDE | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|---|
| T1 | DoS | ReDoS in selector regexes | Devtools-loopback caller | Hangs MCP dispatcher | Low | All four selector regexes are anchored (`^…$`) with non-overlapping/disjoint alternations and bounded quantifiers per character class; tested via inspection, no catastrophic backtracking found | None (selector regexes verified safe). Add a `Regex.MatchTimeout` defense-in-depth — see F1. |
| T2 | DoS | Unbounded recursion in `SelectorResolver.Collect` | Devtools-loopback caller | StackOverflow → process crash | Medium | None — `Collect` recurses with no depth cap (`SelectorResolver.cs:185-209`) | F2 |
| T3 | DoS | Uncaught `OverflowException` from `int.Parse` on `\d+` | Devtools caller (selector with `{component:'x',line:99…9}`) and external deep-link caller (`/detail/99…9`) | Process crash | High | None — `SelectorParser.cs:73` and `DeepLinkMap.cs:80-81` only catch `FormatException` | F3 |
| T4 | DoS | Uncaught `FormatException` from `PathDataParser.ReadNumber` on malformed numbers | Data-driven chart with attacker-influenced numeric format | Process crash on render | Low (path strings come from internal builder) | None — `double.Parse` at `PathDataParser.cs:172` is unguarded | F4 |
| T5 | DoS | Unbounded segment/figure list in PathDataParser | Whoever supplies the path string | Memory growth, COM-object pressure | Low | None — no segment-count cap | F5 |
| T6 | DoS | Unbounded query-string pair count in `ParseQueryString` | External deep-link caller | Memory blowup, dictionary insertion | Low | None — `Split('&')` then loop with no cap (`DeepLinkMap.cs:196`) | F6 |
| T7 | EoP / parser confusion | Same URI string routed differently by `Resolve(Uri)` vs `Resolve(string)` | External caller controlling activation argument shape | Bypass intended route → reach a sensitive route the launcher did not authorize | **Medium-High** — this is the textbook re-routing bug | None — two distinct entry points with different decode semantics (`DeepLinkMap.cs:134-150`). Path traversal segments (`..`) are not normalized either way. | F7 |
| T8 | EoP / parser confusion | Path traversal in wildcard `**` route | External caller | Wildcard captures `../../etc/passwd`-style values that downstream handlers may use as file paths | Medium | None — wildcard pattern is `(.+)` with no `..` filter; `String.TrimEnd('/')` is the only normalization | F8 |
| T9 | EoP | Pattern injection if a developer interpolates user data into `Map(pattern, …)` | App author misuse (template) | Caller-controlled regex compiles into the route table | Low (requires the app to do something dumb) | None | F9 (advisory) |
| T10 | Tampering | Selector parser silently truncates / accepts unexpected shapes (e.g. `r:` selector with empty payload, type path with empty steps via `RemoveEmptyEntries`) | Devtools caller | Resolution targets the wrong element | Low | `SelectorParser.cs:77` uses `RemoveEmptyEntries|TrimEntries`, which means `Button >> Button` is silently treated as `Button > Button`. NodeId selectors are not validated beyond `StartsWith("r:")` (`SelectorParser.cs:49-50`); a malformed `r:foo` is handed to the registry which rejects it with a clear error | Acceptable but document. |
| T11 | Info disclosure | Selector ambiguity payload includes node IDs / type names of up to 10 elements | Devtools caller | Caller learns names of nearby elements | Low | This is by design (`SelectorResolver.cs:130, 178`) — devtools is in-TCB | Not a finding; flag as "loopback-trust-dependent" — Chunk 01 owns. |
| T12 | DoS | `figure?.Segments.Add` with no preceding `M` is a no-op; attacker can pad the path with non-`M`-prefixed segments to inflate parse time without producing geometry | Anyone supplying path data | CPU spend | Low | None | Acceptable (work proportional to input length). |
| T13 | Tampering | Lower-case relative-path commands (`m`, `l`, `a`, `q`, `c`, `z`) hit the `default:` case (`PathDataParser.cs:138-141`) and are silently skipped — but their numeric arguments are then read as orphan tokens in the next iteration of the outer loop, which falls through to `default:` and skips them too | Path-string supplier | Silent semantic corruption (wrong geometry) — *not* a security issue but a robustness/correctness one | Existing comment line 4 says only `M L A Q C Z h v` are supported | Acceptable; document or reject unsupported commands explicitly. |

---

## 6. Findings

Each finding cites file:line, severity, attacker model, and a specific recommendation.

### F1 — Devtools selector regexes have no `MatchTimeout` (Low)

**Where:** `SelectorParser.cs:34-40`.

```csharp
private static readonly Regex NameRegex     = new(@"^\[name=…\]$",                  RegexOptions.Compiled);
private static readonly Regex ReactorRegex  = new(@"^\{\s*component\s*:\s*'…line:(\d+)\s*\}$", RegexOptions.Compiled);
private static readonly Regex TypeStepRegex = new(@"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d+)\])?$", RegexOptions.Compiled);
```

I read each of these for catastrophic backtracking. They are anchored, the alternations are token-disjoint, and the only quantifiers are `[^']*`, `[^"]*`, `\d+`, and `[A-Za-z0-9_]*` against single-byte char classes. None of them have nested quantifiers over an overlapping class, so they are linear-time on any input.

**Defense-in-depth recommendation:** still pass `RegexOptions.Compiled | RegexOptions.ExplicitCapture` and a `TimeSpan` `matchTimeout` (e.g. 200 ms) when constructing the `Regex`. The cost of doing so is zero at parse time and it caps the blast radius of any future grammar expansion (e.g. supporting nested attribute selectors) that introduces backtracking risk. Same recommendation applies to `DeepLinkMap.cs:225`'s compiled regex.

### F2 — `SelectorResolver.Collect` recurses with no depth cap (Medium)

**Where:** `SelectorResolver.cs:185-209`.

```csharp
private static void Collect(UIElement element, …)
{
    …
    int childCount = VisualTreeHelper.GetChildrenCount(element);
    for (int i = 0; i < childCount; i++)
    {
        if (VisualTreeHelper.GetChild(element, i) is UIElement child)
            Collect(child, predicate, sink, …);
    }
}
```

A devtools caller cannot directly construct a deep visual tree, but the predicate runs on whatever tree the running app currently has. The DoS path is:

1. caller picks a selector that does not prune (`#someid`, `Button[5]`) and points it at a window whose root is a deep XAML tree (legitimately deep nested charts, recursive component trees, infinite-scroll virtualized panels with off-screen realized children),
2. `Collect` walks every node of the tree on the *managed* stack — for each `UIElement` we get one `Collect` frame, so a 5,000-deep tree blows the stack.

`StackOverflowException` cannot be caught, so it terminates the host process, killing the user's app.

**Recommendation:** convert `Collect` to an explicit-stack iterative walk (a `Stack<UIElement>` is enough — order is not relied upon for ambiguity reporting, only for picking "highest ancestor" with `pruneSubtreeOnMatch`, and pre-order iteration over an explicit stack preserves that). Alternatively, gate recursion with a `depth` counter and throw `McpToolException("selector-too-deep")` past a fixed limit (4096 or so).

### F3 — `int.Parse(@"\d+")` is unguarded against `OverflowException` in two parsers (High)

**Where:**
- `SelectorParser.cs:73`: `ReactorLine: int.Parse(reactorMatch.Groups[2].Value)`
- `DeepLinkMap.cs:80-81`: `_ when type == typeof(int)  => int.Parse(raw, …)`, `_ when type == typeof(long) => long.Parse(raw, …)`

Both regexes that feed these are `\d+` with no length bound. `int.Parse("99999999999999999")` (17 digits) throws `OverflowException`, **not** `FormatException`. The wrapping `try/catch` at `DeepLinkMap.cs:87` only catches `FormatException`, so the overflow propagates uncaught into the caller.

For the deep-link path, the caller is whatever invoked the app via protocol activation. An external app that fires `myapp:///detail/9999999999999999999` crashes the target on every launch — a permanent denial of service against any app that maps an `int` route parameter. This is reachable today by `samples/NavigationDemo/App.cs:31`'s `--deep-link` handling and would be reachable by any production app following the sample.

For the selector path, devtools is loopback-trusted; impact is just "an authorized caller can crash the dispatcher." Lower bar but same fix.

**Recommendation:** in `DeepLinkMap.ConvertValue`, change

```csharp
catch (FormatException ex)
```

to

```csharp
catch (Exception ex) when (ex is FormatException or OverflowException)
```

and throw a `FormatException` wrapping it. In `SelectorParser`, use `int.TryParse` and throw `FormatException` on failure (consistent with the rest of the file).

### F4 — `PathDataParser.ReadNumber` does not handle malformed numeric tokens (Low)

**Where:** `PathDataParser.cs:153-173`.

```csharp
while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
…
if (start == i) return 0;
return double.Parse(s[start..i], CultureInfo.InvariantCulture);
```

The accept-loop allows any number of `.` characters and any sequence of `[+-]` followed by `[+-]…` (the sign is only checked once but a path like `M-+5,0` will read `-` then exit the sign branch, then the `+` is left in the buffer and re-enters via outer-loop `default:` skip). The actually pathological input is `"."` alone (start advances past one dot, `s[start..i]` is `"."`, `double.Parse(".")` throws), or `"1.2.3"` (the entire token is consumed, `double.Parse` throws on the second `.`).

`FormatException` propagates uncaught into `PathDataParser.Parse`, which is called from `D3Path` / `D3PathTranslated` during element construction during render. An exception during render is caught by the reconciler at most as a hot-reload failure, but commonly bubbles up and kills the frame.

This is **lower likelihood** than F3 because path data today comes from D3's `PathBuilder` and is well-formed. It becomes higher if the framework is later used to render any external SVG path — e.g., from a downloaded chart definition or a clipboard paste.

**Recommendation:** wrap `double.Parse` in `TryParse`; on failure, skip the malformed token and continue (matching the parser's existing "skip unknown chars" philosophy at line 138-141). Add a unit test for `M0,0 L., L1.2.3,0 L0,0 Z` — currently it crashes.

### F5 — `PathDataParser` produces unbounded segment count (Low, defense-in-depth)

**Where:** `PathDataParser.cs:33-142` (the entire switch).

Every command branch appends to `figure.Segments`. There is no cap on the number of segments per figure or figures per geometry. A 1 MiB path string of `"L1,1L1,1…"` produces ~125k `LineSegment` COM objects — a non-trivial allocation footprint on the UI thread.

Trust says path strings are produced by the chart builder, which is bounded by the chart's data length, so an attacker would need to push a huge data set. But the same parser is `public static` and could be reached from app code that pastes user-supplied SVG.

**Recommendation:** add a configurable cap (e.g. 100k segments) and short-circuit with a logged warning when exceeded.

### F6 — `DeepLinkMap.ParseQueryString` has no pair-count cap (Low)

**Where:** `DeepLinkMap.cs:189-205`.

External caller sends `myapp:///?a&a&a&a&…` with a query of N pairs. We allocate a dictionary with at most one entry (because `Dictionary` overwrites on duplicate key), but `String.Split('&')` allocates an array of N strings first, plus we call `Uri.UnescapeDataString` N times. This is O(N) work and O(N) transient memory before the dictionary collapses.

`new Uri()` itself caps query length at `~64 KiB` for absolute URIs (Windows behavior is platform-dependent) but the `Resolve(string)` overload at line 140 has no length check, so a 10 MiB raw string flows in as-is.

**Recommendation:** cap the input length (e.g. 8 KiB for the whole URI) at the top of `Resolve`, or cap pair count at 64 in `ParseQueryString`. Whichever is louder.

### F7 — Parser confusion: `Resolve(Uri)` vs `Resolve(string)` route the same input differently (High, security-relevant)

**Where:** `DeepLinkMap.cs:134-150`.

```csharp
public DeepLinkResult<TRoute> Resolve(Uri uri)
{
    var queryParams = ParseQueryString(uri.Query);
    return Resolve(uri.AbsolutePath, queryParams);              // ← System.Uri-canonicalized path
}

public DeepLinkResult<TRoute> Resolve(string path)
{
    Dictionary<string, string>? queryParams = null;
    var qi = path.IndexOf('?');
    if (qi >= 0) {
        queryParams = ParseQueryString(path.Substring(qi));
        path = path.Substring(0, qi);
    }
    return Resolve(path, queryParams);                          // ← raw bytes, NO canonicalization
}
```

These two overloads have **different** decoding/canonicalization semantics for the same logical input:

| Behavior | `Resolve(Uri)` | `Resolve(string)` |
|---|---|---|
| Percent-decoding of path | `uri.AbsolutePath` decodes "unreserved" characters (per `System.Uri.UnescapeDataString` rules) | none |
| `..` segment collapse | `System.Uri` collapses dot segments according to RFC 3986 *for hierarchical schemes only* — and is famously inconsistent across .NET versions | none |
| Backslash → forward-slash | `System.Uri` normalizes `\` to `/` for some schemes | none |
| Trailing `/` | preserved | trimmed at line 154 |
| Case normalization of host/scheme | normalized by `System.Uri` | irrelevant — host not parsed |
| Query | `uri.Query` is the raw escaped form (with leading `?`) — same single-decode as the string overload | same |

The launcher path on Windows usually arrives via `AppInstance.GetActivatedEventArgs()` → `ProtocolActivatedEventArgs.Uri` (a `System.Uri`), so a route registered via `Map("/admin", …)` is matched against the System.Uri-decoded form. But the same app may also expose `--deep-link <string>` (the sample does, `samples/NavigationDemo/App.cs:31`), which routes the raw command-line argument through the *string* overload. Two paths into the same router with different normalization is the textbook **confused-deputy** setup.

Concrete attack scenarios:

1. **Path-traversal-into-different-route via percent-decoded slash.** Attacker sends `myapp:///public%2F..%2Fadmin`. Under `Resolve(Uri)`, `uri.AbsolutePath` is what `System.Uri` decides to do with `%2F` — historically .NET has variously decoded this to `/`, left it as `%2F`, or raised. If the runtime decodes, the path becomes `/public/../admin` (or possibly `/admin` after dot-segment collapse), which matches `Map("/admin", …)`. Under `Resolve(string)`, the bytes are matched literally against `^/admin$` — no match. **The same URI authorizes `/admin` only when the OS-canonicalized path is used** — but a launcher that constructs the link as a string and hands us the string sees no match, while the OS-routed path does. Whichever the app trusts, the other one is the bypass.

2. **Trailing slash semantics.** The string overload calls `path.TrimEnd('/')` (line 154); the `Uri` overload does not (it goes straight through to `Resolve(path, queryParams)` which then does call `TrimEnd`). Net effect: both paths *do* trim today (line 154 is in the inner `Resolve` both call). So this specific axis is consistent — but the consistency is incidental, not designed; future edits to either overload could re-open the gap.

3. **Dot-segment collapse asymmetry.** Neither overload removes `..` — the inner `Resolve` does only `TrimEnd('/')`. So even the `Uri` overload depends entirely on whether `System.Uri` collapsed dot segments before producing `AbsolutePath`. That collapse behavior **changes between .NET 6, 7, 8, 9** and across the `IriParsing`/`UnicodeBidi` switches in `app.config`. The same code on the same input gives different routes on different runtimes.

**Recommendation (pick one of, in order of preference):**

a. **Eliminate the string overload.** Make `Resolve(string)` internal/test-only and force callers to construct a `Uri` explicitly via `Uri.TryCreate(s, UriKind.Relative, out var u)`. The sample app's `--deep-link <string>` becomes `if (Uri.TryCreate(args[dlIdx+1], UriKind.RelativeOrAbsolute, out var u)) deepLinks.Resolve(u);`.

b. **Canonicalize once, in one place.** Normalize the input to a canonical form *before* either overload reaches the regex match: percent-decode reserved characters per a fixed list, reject any input whose decoded form contains `..` or `%2F` or `%5C`, lowercase scheme+host (n/a), then dispatch.

c. **Reject ambiguous inputs loudly.** If the path contains `%`, run both decoded and raw forms through `Regex.Match` and require the *same* route match; if they diverge, throw "ambiguous-deep-link" and refuse to navigate. This is the most conservative; it preserves backward compatibility but catches the divergence.

This is the single most consequential finding in this chunk.

### F8 — Wildcard `**` route does not reject `..` segments (Medium)

**Where:** `DeepLinkMap.cs:213-219, 251-258`.

```csharp
if (pattern.EndsWith("/**"))
{
    var prefix = pattern.Substring(0, pattern.Length - 3);
    prefix = CompileSegments(prefix, paramNames);
    paramNames.Add("**");
    regexPattern = $"{prefix}/(.+)";
}
```

The wildcard captures any non-empty suffix into `RouteArgs["**"]`. The sample `samples/NavigationDemo/App.cs:24` uses `Map("/docs/**", args => new DocsPage(args.GetWildcard() ?? "index"))`. `DocsPage` then likely uses that string to look up content — a real app would use it as a relative file path or doc ID.

External callers can send `/docs/../config.json` or `/docs/../../etc/passwd` and the wildcard captures `..` / `../../etc/passwd`. The framework hands the string to the app; whether that's exploitable depends entirely on how the app uses it. This is a **framework-level safe-default question**: today's contract is "we hand you whatever the user typed, you sanitize it." Most apps will not.

**Recommendation:** in `RouteArgs.GetWildcard`, by default reject values whose normalized path contains `..` segments (i.e. `value.Split('/').Any(seg => seg == "..")`). Provide an opt-out for apps that explicitly want raw bytes (`GetWildcardRaw()`). Document in `DeepLinkMap.cs` that wildcard captures are untrusted.

### F9 — Pattern compilation does not validate developer-supplied pattern shape (Advisory)

**Where:** `DeepLinkMap.cs:207-249`.

`CompilePattern` accepts any string. If a developer ever interpolates user data into the pattern (`map.Map($"/profile/{userKey}", …)`), the user data lands inside a regex template and can craft a pattern that matches more routes than intended. This is an app-author footgun, not a framework bug, but the API does not warn against it.

**Recommendation:** document that `Map` patterns must be literals; consider a Roslyn analyzer that flags non-literal `string` arguments to `DeepLinkMap.Map`.

---

## 7. Open questions

1. **Can the devtools dispatcher actually receive a hostile selector?** Chunk 01 currently asserts loopback = trusted. F2 (stack-blow recursion) only matters if that assumption fails. This finding is therefore conditional on the open Chunk 01 question; it should be re-rated once that's resolved.
2. **What protocol-activation surface does Reactor expect apps to use?** The sample wires it as a CLI arg, not as `AppInstance.GetActivatedEventArgs`. If the *intended* path is the latter, the `Resolve(string)` overload should arguably be deleted. If both are intended (e.g. test harness uses string), F7's "canonicalize once" recommendation applies.
3. **Does `System.Uri` in WindowsAppSDK / .NET 9 (the Reactor target) decode `%2F` in `AbsolutePath`?** This is platform-dependent and changes the F7 analysis. Recommend a unit test that pins the behavior on the current runtime and breaks the build if it changes.
4. **Where do path-data strings actually originate at runtime?** The header comment on `PathDataParser.cs:1-3` claims "produced by our `PathBuilder`," but `D3Path` is `public static` and accepts arbitrary `string?`. Is there a code path where externally-sourced SVG (clipboard, downloaded asset, user-pasted) reaches it? If yes, F4/F5 jump from Low to Medium.
5. **Selector ambiguity-payload contents** — the candidate list includes element type names and Automation IDs. If a future cross-context attacker reaches the dispatcher (Chunk 01 fails open), this is metadata leak that helps them target subsequent calls. Owner: Chunk 02.

---

## 8. Out-of-scope referrals

- **Chunk 01** owns whether selector strings are reachable by hostile callers at all. F2 / F3 (selector half) escalate sharply if Chunk 01's loopback-trust assumption is challenged.
- **Chunk 02** owns the "what does the candidate list disclose" question (T11 above).
- **Chunk 13** owns persisted nav-state security; the `RouteArgs` produced here are consumed by code in that chunk and persisted by `PersistedStateCache`. Type-confusion / overflow concerns in deserialization belong there.
- **Chunk 21 (Charting)** owns end-to-end chart-data correctness — F4/F5 are the *parser* slice. The chart builders themselves (D3 port) are reviewed in 21.
- **Chunk 23 (Hooks)** does not interact with this chunk.

---

## Summary of actionable findings

| ID | Severity | One-line fix |
|---|---|---|
| F3 | **High** | Catch `OverflowException` in `DeepLinkMap.ConvertValue` and `SelectorParser` reactor-source line parse; both currently crash on long digit strings. |
| F7 | **High** | Eliminate or re-canonicalize `DeepLinkMap.Resolve(string)` so it cannot route the same logical URI to a different route than `Resolve(Uri)`. |
| F2 | Medium | Convert `SelectorResolver.Collect` to an iterative walk to remove `StackOverflowException` on deep visual trees. |
| F8 | Medium | Default to rejecting `..` segments in wildcard captures (`RouteArgs.GetWildcard`). |
| F1 | Low | Add `Regex.MatchTimeout` to compiled regexes as defense-in-depth. |
| F4 | Low | Use `double.TryParse` in `PathDataParser.ReadNumber` and skip malformed tokens. |
| F5 | Low | Cap segment count in `PathDataParser.Parse`. |
| F6 | Low | Cap query-string pair count and overall input length in `DeepLinkMap.Resolve`. |
| F9 | Advisory | Document / analyze that `DeepLinkMap.Map` patterns must be literals. |
