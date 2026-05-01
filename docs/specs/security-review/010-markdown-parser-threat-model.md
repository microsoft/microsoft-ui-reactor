# Chunk 10 — Markdown Parser (md4c port) — Threat Model

**Status:** Phase 2 review (drafted).
**Reviewer:** Claude (Opus 4.7).
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`.
**Companion doc:** [`000-chunking-and-threat-model.md`](000-chunking-and-threat-model.md).

---

## 1. Scope

| File | LoC | Notes |
|---|---:|---|
| `src/Reactor/Markdown/Md4cParser.cs` | 701 | Orchestration, state, marks, mark-char map, ProcessDoc entry. |
| `src/Reactor/Markdown/Md4cParser.Block.cs` | 2 035 | Block-level parsing (lists, headings, code blocks, HTML blocks, tables). |
| `src/Reactor/Markdown/Md4cParser.Inline.cs` | 3 084 | Inline parsing (emphasis, autolinks, links, code spans, raw HTML, entities). |
| `src/Reactor/Markdown/Md4cTypes.cs` | 129 | Detail structs + delegates for SAX callbacks. |
| `src/Reactor/Markdown/Md4cEnums.cs` | 154 | `MdParserFlags`, block/span/text type enums. |
| `src/Reactor/Markdown/Md4cEntity.cs` | 2 180 | Generated named-entity table (~2 100 entries) + `EntityLookup`. |
| `src/Reactor/Markdown/Md4cUnicode.cs` | 716 | Unicode case folding + classification tables (skipped per scope). |
| `src/Reactor/Markdown/Md4cHtml.cs` | 481 | **Public** Markdown → HTML renderer. |
| `src/Reactor/Markdown/MarkdownBuilder.cs` | 808 | SAX visitor that builds Reactor `Element` tree. |

`Md4cBuilder.cs` listed in the chunking doc does **not exist** in this commit; the builder is `MarkdownBuilder.cs`. Total ≈ 10 288 LoC, of which ~2 180 lines (entity table) and ~716 lines (Unicode tables) are out of review scope per the chunking note. Effective review surface ≈ **~7 400 LoC**.

Review focus per the chunking spec:
- Parser entry points (`Md4cParser.Parse`, `MarkdownBuilder.Build`, `Md4cHtml.Render` / `ToHtml`).
- The HTML renderer (`Md4cHtml`).
- Link / image URL handling (`MarkdownBuilder.OnEnterSpan`, `IsSafeUri`).
- Recursion / depth bounds and known md4c CVE classes.

---

## 2. Data-flow diagram

```
                 ┌─────────────────────────────────────────────────────────┐
                 │  UNTRUSTED MARKDOWN STRING                              │
                 │  (chat message, README in opened repo, downloaded doc,  │
                 │   LLM output, embedded help content)                    │
                 └──────────────────────┬──────────────────────────────────┘
                                        │
                                        ▼
                       Factories.Markdown(string)              Md4cHtml.ToHtml(string)
                          [Dsl.cs:759-766]                     [Md4cHtml.cs:50-57]
                                        │                                │
                                        ▼                                ▼
                       MarkdownBuilder.Build(md, opts)            HtmlRenderer (closure)
                          [MarkdownBuilder.cs:121]                       │
                                        │                                │
                                        └────────┐    ┌──────────────────┘
                                                 ▼    ▼
                                        Md4cParser.Parse(text, flags, callbacks…)
                                                  [Md4cParser.cs:279]
                                                        │
                                          (lines → blocks → inlines → SAX callbacks)
                                                        │
                                ┌───────────────────────┴───────────────────────┐
                                ▼                                                ▼
                  MarkdownBuilder visitor                          Md4cHtml.HtmlRenderer
                  [MarkdownBuilder.cs]                             [Md4cHtml.cs]
                                │                                                │
                                ▼                                                ▼
              Element tree (RichText, RichTextHyperlink,            HTML string (StringBuilder)
              Image, TextBlock, …)                                          │
                                │                                           ▼
                                ▼                                 (Caller injects into WebView2,
                 Reactor reconciler.Mount/Update                   HtmlContent control, file write,
              [Reconciler.Mount.cs:287-293,                        clipboard, network response …)
               Reconciler.Update.cs:578-610]
                                │
                                ▼
              WinUI Hyperlink.NavigateUri set
              [Reconciler.Mount.cs:289]
                                │
                                ▼ user click
              WinUI built-in Launcher.LaunchUriAsync
              (no Reactor-side gate at click time)
                                │
                                ▼
              Shell URI handler (browser, mail client,
              custom protocol handler …)
```

### Known runtime callers

| Site | Trust of input | Reach to user click |
|---|---|---|
| `samples/apps/chat/Chat.UI/ChatTimeline.cs:217` — `Markdown(entry.Text ?? "", _markdownOptions)` | **Untrusted** — chat history of arbitrary peers / LLM output. | Yes — links land in a tap-able Hyperlink. |
| `samples/apps/monaco-editor/App.cs:251` — `Markdown(text)` | Editor buffer (currently developer-typed, but illustrative). | Yes. |
| `tests/Reactor.AppTests.Host/SelfTest/Fixtures/MarkdownHtmlFixtures.cs:91` — `Md4cHtml.ToHtml(...)` injected into a **WebView2**. | Test fixture, but pattern is exposed as a sample. | **WebView2 executes JS** if attacker MD reaches it. |
| `Md4cHtml` itself is `public` (`Md4cHtml.cs:12`) — any consumer of the package can use it as a Markdown→HTML library. | Untrusted if used on user input. | Whatever the embedder does with the HTML. |

---

## 3. Trust boundaries crossed

| # | Boundary | Assumption | Held? |
|---|---|---|---|
| 1 | Untrusted markdown string → in-process parser. | `string` is well-formed UTF-16; no allocation/copy outside managed heap; parser is panic-safe and bounded. | Mostly — nesting cap exists, but no input-size cap and no exception barrier in the Build entry point. |
| 2 | Parser SAX events → builder. | Builder treats every callback string as untrusted text. | Mostly — but URL filtering only applied to absolute URIs (relative URIs and Md4cHtml paths are unfiltered). |
| 3 | Builder → reconciler → WinUI. | Reactor `Element`s are inert; `RichTextHyperlink.NavigateUri` is the only user-clickable shell-out vector. | The only filter is `MarkdownBuilder.IsSafeUri` (http/https/mailto). WinUI's Hyperlink launches the URI through the Launcher API on click — there is no second-line check at click time. |
| 4 | Md4cHtml → embedder. | Embedder is responsible for sanitizing before injecting into a WebView2. | **Not held in practice** — the bundled fixture (`MarkdownHtmlFixtures.cs:91`) demonstrates injecting Md4cHtml output verbatim into a WebView2 with no sanitization. |

---

## 4. Asset inventory

1. **Process integrity / availability.** Hostile markdown must not crash, hang, or OOM the host process.
2. **The user's "click is consent" trust.** A markdown link's visible text must not be able to trick the user into launching attacker-chosen URIs of attacker-chosen schemes (browser-stealing protocols, `file:`, custom IPC schemes registered on the box).
3. **WebView2 isolation.** When `Md4cHtml` output is rendered in a WebView2, attacker JS execution must not be possible.
4. **No side-channel on local state.** Parser timing/memory must not depend on secrets — it shouldn't, because no secret is in scope, but DoS-by-timing is real.
5. **Markdown-as-corpus integrity.** The same input on different platforms shouldn't surface to different parsing results that confuse consumers (e.g. the renderer disagrees with a downstream sanitizer).

---

## 5. STRIDE table

| # | STRIDE | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| T1 | Tampering / EoP | Hostile markdown supplies a link with attacker-chosen URI scheme; user clicks; OS launches attacker-controlled handler. | Author of any markdown the app renders (chat peer, README author, file dropped on disk). | URL launch via `Launcher.LaunchUriAsync` — `file:`, `ms-appx:`, `ms-settings:`, `intent:`, registered custom protocols (`vscode:`, `slack:`, `git:`) can drive arbitrary local actions, escape browser sandbox, or expose local files in some shells. | **High** — markdown links are the marquee feature attackers test. | `MarkdownBuilder.IsSafeUri` (`MarkdownBuilder.cs:649-655`) only allows absolute `http`/`https`/`mailto`. **All relative URIs pass** (`!uri.IsAbsoluteUri ⇒ true`). | F-1, F-2. |
| T2 | Tampering | Attacker supplies a link whose visible text spoofs a URL different from `NavigateUri` (homograph / RTL-override / display vs href mismatch). | Same. | User believes they are clicking `https://corp/login` but lands on attacker site. | **High** — easy to construct. | None. `RichTextHyperlink.Text` is rendered verbatim; no URL display, no title-attribute hover. | F-3. |
| T3 | Tampering | `Md4cHtml` is invoked on attacker markdown and the HTML is injected into a WebView2 (or a `mshtml` host, clipboard, …). `[click](javascript:alert(1))` becomes a working `<a href="javascript:alert(1)">`. | Author of markdown rendered through Md4cHtml → WebView2. | Arbitrary JS execution in the WebView's origin → token theft / file read / IPC into the host depending on host config. | **High** when this path is used (the bundled fixture demonstrates the unsafe pattern). | None — `Md4cHtml.RenderAttribute(... urlEsc=true)` percent-encodes special chars but performs **no scheme filtering**. Raw HTML blocks/spans are written verbatim (`Md4cHtml.cs:469`). | F-4, F-5. |
| T4 | DoS | Pathological CommonMark — emphasis storms, deeply-nested brackets, link reference cycles. | Anyone supplying markdown. | CPU pegged / parse hang. | **Medium**. | Rule-of-three emphasis stacks (`Md4cParser.Inline.cs:2164`+); container nesting cap `MaxNestingDepth = 100` (`Md4cParser.Block.cs:1147`); `maxRefDefOutput = min(16·N, 1 MB)` (`Md4cParser.cs:259`); `TABLE_MAXCOLCOUNT = 128` (`Md4cParser.cs:22`); `CODESPAN_MARK_MAXLEN = 32`; HTML scan horizon (`ScanForHtmlCloser` `Md4cParser.Inline.cs:171-204`). These are the standard md4c hardenings. | F-6 (no input-size or wall-clock cap at the entry point). |
| T5 | DoS | Memory amplification — `marks[]`, `blockBytes[]`, `containers[]` all `Array.Resize` with no upper limit. | Anyone supplying markdown. | OOM / process termination. | **Medium**. | Growth is `O(input size)`, but with input size unbounded, total can be many × the input. | F-7. |
| T6 | DoS | Recursion-by-callback — `Md4cParser.Parse` invokes user-supplied callbacks; in `MarkdownBuilder` they are non-recursive, but a *third-party* embedder could observe deep nesting (e.g., 100 nested blockquotes) and recurse. | Markdown author + careless embedder. | Stack overflow inside the embedder. | **Low–medium**. | Block nesting capped at 100 — manageable for most hosts but enough to overflow a callback that recurses 1× per level on a slim stack. | F-8 (info). |
| T7 | Info disclosure / Tampering | Reference-link label normalization (Unicode case fold + whitespace collapse) doesn't match link-target normalization elsewhere → consumer-confused-deputy. | Markdown author. | Low — no secret in scope; mainly correctness. | Low. | Standard md4c label normalization. | F-9 (open question). |
| T8 | EoP via builder bug | `RichTextHyperlink` constructed with non-absolute or `about:error` Uri assigned to WinUI Hyperlink. WinUI throws on assignment → caught → `NavigateUri = "about:error"` (`Reconciler.Mount.cs:290`). | Markdown author. | Click does nothing (good fail-safe). | — | Catch barrier exists. | OK. |
| T9 | DoS via entity table | Numeric entity `&#xFFFFFF;` in builder path — `MarkdownBuilder.AddEntityText` calls `Md4cEntity.EntityLookup` (returns null for numeric forms, falls through to `AddInlineRun(entityStr)`). | Markdown author. | None — numeric entities pass through as raw text. | Low. | Md4c parser caps entity numerics at 8 hex / 7 decimal digits (`Md4cParser.Inline.cs:298, 314`) — can't overflow. | OK; but **MarkdownBuilder never decodes numeric entities** at all — they render as `&#65;` literal text. See F-10 (correctness, not security). |
| T10 | Repudiation | No logging of which markdown was parsed, success/error, parse time. | n/a — repudiation isn't really in scope. | — | None. | Out of scope. |

---

## 6. Findings

### F-1 — `MarkdownBuilder.IsSafeUri` allows **all** relative URIs (Tampering, **Medium**)

**Location:** `src/Reactor/Markdown/MarkdownBuilder.cs:649-655`

```csharp
private static bool IsSafeUri(Uri? uri)
{
    if (uri is null) return false;
    if (!uri.IsAbsoluteUri) return true;          // ⟵ unconditional bypass
    var scheme = uri.Scheme;
    return scheme is "http" or "https" or "mailto";
}
```

`Uri.TryCreate("foo:bar", UriKind.RelativeOrAbsolute, …)` parses `foo:bar` as **relative** when `foo` is not a recognized scheme — and `Uri` recognizes only a small whitelist (`http`, `https`, `ftp`, `file`, `mailto`, etc.). Schemes like `slack:`, `vscode:`, `intent:`, `wallet:`, `mycorp:` typically parse as **relative** URIs in .NET, hit the `!uri.IsAbsoluteUri` branch, and survive the filter.

Once the relative `Uri` is assigned to `Microsoft.UI.Xaml.Documents.Hyperlink.NavigateUri` (`Reconciler.Mount.cs:289`), WinUI's click handler invokes `Launcher.LaunchUriAsync`. With a relative URI Launcher generally fails — but there is no contract guarantee, and the attacker-controlled string is also visible inside the Reactor element tree where embedders may consume it (e.g. for "open in browser" right-click affordances).

**Recommendation:** require `uri.IsAbsoluteUri && scheme ∈ {http, https, mailto}` and drop the `!IsAbsoluteUri ⇒ true` branch. If relative links are intentionally supported (intra-document anchors), filter to schemes that are explicitly `null`/empty *and* whose original string starts with `#` or `/`.

### F-2 — `IsSafeUri` doesn't normalize before scheme-checking (Tampering, **Low**)

**Location:** `src/Reactor/Markdown/MarkdownBuilder.cs:603-606`

```csharp
if (detail is MdSpanADetail a && a.Href.Text is not null)
{
    Uri.TryCreate(a.Href.Text, UriKind.RelativeOrAbsolute, out var uri);
    _linkUri = IsSafeUri(uri) ? uri : null;
}
```

`Uri.Scheme` in .NET is lower-cased and trimmed, so `JaVaScRiPt:alert(1)` and `\tjavascript:alert(1)` are correctly rejected when parsed as absolute. But a leading control character or BOM can in some cases force the `Uri` ctor down the relative-URI path (which then bypasses per F-1). Combined with F-1 this is the same bug; if F-1 is fixed by requiring `IsAbsoluteUri`, this issue dissolves.

### F-3 — Hyperlink display text vs `NavigateUri` mismatch is unconstrained (Tampering, **Medium**)

**Location:** `MarkdownBuilder.cs:678` (constructs `RichTextHyperlink` with `Text = sb.ToString()` flattened from arbitrary inline runs).

The visible text of a markdown link `[https://corp.example.com/login](https://attacker.example/)` is `https://corp.example.com/login`; the `NavigateUri` is the attacker's. Reactor exposes no hover/title or per-link confirmation prompt; the user clicks what they read. This is a long-standing markdown pattern but worth recording as a known acceptable risk *only* if the calling app is presented with the means to mitigate (e.g., a `MarkdownOptions.LinkRewriter` that can show the target on hover, or a "links you visit will go through `https://…` — proceed?" interstitial).

**Recommendation:** add a `MarkdownOptions.LinkBuilder : (string text, Uri target) → Element` hook so apps that render untrusted markdown (chat) can implement their own confirmation / unfurl / origin-preview UX. Today they have to override `MarkdownOptions.Heading/Paragraph/…` but **no link callback exists** — the link is built inside `MarkdownBuilder.LeaveLink` with no extension point.

### F-4 — `Md4cHtml` performs zero URL scheme filtering (Tampering / EoP, **High** when output reaches a WebView2)

**Location:** `src/Reactor/Markdown/Md4cHtml.cs:390-406, 115-161`

```csharp
case MdSpanType.A:
    var a = (MdSpanADetail)detail!;
    Verbatim("<a href=\"");
    RenderAttribute(a.Href, true);   // urlEsc = true: percent-encodes, no scheme check
    …
    Verbatim("\">");
```

`UrlEscaped` percent-encodes characters with `NEED_URL_ESC`, but `:` and `/` are explicitly preserved as URL-safe (`Md4cHtml.cs:80`), so `javascript:alert(1)` round-trips intact. Result: `[click](javascript:alert(1))` becomes `<a href="javascript:alert(1)">click</a>`. If this output is loaded into a WebView2 (the pattern is demonstrated in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/MarkdownHtmlFixtures.cs:91`), a click runs JS in the WebView's origin.

**Recommendation:** apply the same scheme allowlist used in the runtime path. Reject (drop `href`) or replace with `about:blank` for any scheme not in `{http, https, mailto}`. If retaining schemes is desired, add an `Md4cHtml.SafeMode` flag (default-on) that performs the filtering, and document the unsafe mode as for trusted input only.

### F-5 — `Md4cHtml` writes raw HTML blocks/spans verbatim (Tampering / XSS, **High** when used unsafely)

**Location:** `src/Reactor/Markdown/Md4cHtml.cs:468-470`

```csharp
case MdTextType.Html:
    Verbatim(text);
    break;
```

The CommonMark spec mandates that recognized inline HTML (`<script>…</script>`, `<img onerror=…>`, etc.) be passed through verbatim, and md4c's HTML renderer does so. Reactor's choice to expose `Md4cHtml` as **public** and to ship a sample that pipes its output into a WebView2 means a Reactor consumer who reasonably believes "this is the framework's HTML renderer" gets an XSS sink.

**Recommendation:** at minimum, make the *default* of `Md4cHtml.ToHtml` set `MdParserFlags.NoHtml` (disables both raw HTML blocks **and** spans — already supported by the parser) and require the caller to opt in to `AllowRawHtml = true` to get verbatim HTML. Document the security invariant on the `Md4cHtml` type.

### F-6 — No input-size cap at the markdown entry point (DoS, **Medium**)

**Location:** `src/Reactor/Elements/Dsl.cs:759-766`, `src/Reactor/Markdown/MarkdownBuilder.cs:121-136`, `src/Reactor/Markdown/Md4cHtml.cs:50-57`

`Markdown(string)` accepts any-length string. Internal limits are reasonable (`maxRefDefOutput ≤ 1 MB`, container depth ≤ 100, table cols ≤ 128, codespan marks ≤ 32) but the parser still allocates `O(N)` mark records, block records, and verbatim-line records. A 100 MB markdown blob containing 50 M `*` characters builds tens of millions of mark structs.

**Recommendation:** add a `MarkdownOptions.MaxInputBytes` (default 1 MB or 4 MB) and reject early in `MarkdownBuilder.Build`. This is a single-line backstop and converts DoS into a graceful error.

### F-7 — Mark / block / container array growth is unbounded (DoS, **Low**, depends on F-6)

**Location:**
- `Md4cParser.cs:405-406`: `marks[]` doubles indefinitely on `AddMark`.
- `Md4cParser.cs:684-685`: `blockBytes[]` grows 1.5× indefinitely.
- `Md4cParser.Block.cs:1156-1159`: `containers[]` grows 1.5× until `MaxNestingDepth=100`.
- `Md4cParser.cs:259`: `buffer` grows in 128-aligned 1.5× steps.

If F-6 is fixed by a sane input-size cap these growth strategies are fine. If not, an attacker-controlled long input lets a `\` + `*` + `_` + `~` repeating pattern push `nMarks` to roughly N/4 entries × ~24 bytes ≈ 6× the input size in heap. With a 1 GB input that's ~6 GB.

### F-8 — Container nesting limit is the **only** depth backstop and is per-parser (DoS, info)

**Location:** `Md4cParser.Block.cs:1147` — `MaxNestingDepth = 100`.

The parser itself is iterative (no recursive C-style descent), so a deep MD doesn't blow the .NET stack inside the parser. But callbacks are invoked at depth-N, and a SAX consumer that recursively descends in `OnEnterBlock` will see 100 nested calls. `MarkdownBuilder` is non-recursive (uses an explicit `Stack<BlockFrame>`) so this is fine for the bundled builder. Document the 100-deep contract for third-party callback writers; consider lowering to ~32 for the builder by default since CommonMark never *needs* depth >50 in practice.

### F-9 — Behavior with surrogate / control / private-use codepoints in URLs is unspecified (Tampering, **Low**)

`UrlEscaped` (`Md4cHtml.cs:115-161`) UTF-8 percent-encodes `≥128` codepoints. There is no normalization (NFC), no IDNA, and no rejection of surrogate halves or U+FFFD. Combined with F-3 this enables Unicode-display tricks (RLO override, fullwidth letters that look like ASCII). Out-of-scope for an MVP fix; record as an open question.

### F-10 — Numeric/hex character references are NOT decoded by `MarkdownBuilder` (correctness, info)

**Location:** `MarkdownBuilder.cs:773-789`

`AddEntityText` only checks `Md4cEntity.EntityLookup` (named entities). For `&#65;` or `&#x1F600;` it falls through to `AddInlineRun(entityStr)` and renders the literal `&#65;` text. Md4cHtml decodes them correctly (`Md4cHtml.cs:210-231`), so the two render paths disagree. Not a security bug, but worth fixing for consistency.

### F-11 — `MarkdownBuilder.Build` swallows parser errors and returns an empty `VStack` (correctness / repudiation, info)

**Location:** `MarkdownBuilder.cs:133-135`

```csharp
if (ret != 0)
    global::System.Diagnostics.Debug.WriteLine($"MarkdownBuilder: md4c parse failed with code {ret}");
return builder._result ?? VStack();
```

There is no exception barrier around the parser; an unexpected `IndexOutOfRangeException` (e.g., a parser invariant bug under fuzzed input) would propagate to the reconciler. There is also no signal back to the caller that input was rejected. A `try/catch` returning a placeholder error element would harden the boundary.

### F-12 — `MarkdownOptions.Image` callback receives a **`Uri` already filtered by `IsSafeUri`**, but the default path uses `Image(_imageSrc.ToString())` (info)

**Location:** `MarkdownBuilder.cs:688-700`, `Reconciler.Mount.cs:809-822`

The default-Image branch round-trips through a `string` and `new Uri(.., RelativeOrAbsolute)` again inside `MountImage`. This is redundant but not exploitable given the filter held. Mention only because a future change to `MountImage`'s URI handling could reintroduce the scheme bypass for images specifically — keep the audit anchor here.

---

## 7. Open questions

1. **Is `Md4cHtml` actually intended to be a public API of Reactor, or is it test-only infrastructure that escaped into the public surface?** The class is `public`, lives outside the `Internal` folder, and the self-test fixture treats it as a customer-facing renderer. The threat model changes drastically depending on intent. *Recommendation: mark `Md4cHtml` `internal` if it is for tests only, OR commit to it being safe-by-default per F-4 / F-5.*
2. **What is the WinUI `Hyperlink.NavigateUri` runtime contract on relative URIs?** Confirmed empirically that the catch in `Reconciler.Mount.cs:290` exists, suggesting the team has seen it throw. The behavior across WindowsAppSDK versions should be pinned by a unit test that constructs a `Uri("mycustomscheme:foo", RelativeOrAbsolute)` and asserts `Hyperlink.NavigateUri = uri` either throws or filters.
3. **Is there a planned `LinkBuilder` extension point?** Today no markdown link click handler exists in `MarkdownOptions`. Apps can't intercept clicks for confirmation. This forces *every* consumer to either accept the current scheme allowlist or fork the builder.
4. **Reference-label normalization parity.** `IsLinkReference`/`LookupRefDef` (`Md4cParser.Inline.cs:1048-1106`, `:664-…`) collapse Unicode whitespace and lowercase ASCII. If a downstream system (search index, link rewriter) parses the same markdown with different rules, two definitions could collide ambiguously. Worth a property-based test.
5. **Fuzzing.** This port has been ported by hand from C; the C md4c has its own corpus of regression inputs and OSS-Fuzz history (e.g., GHSA-fffx-7qj8-3vh3 emphasis-quadratic, OSS-Fuzz issue 60918 reference-link expansion). **Has the C# port been run against md4c's own test corpus and the CommonMark spec tests?** The repo contains `tests/Reactor.Tests/MarkdownTests.cs` with spec tests, but a fuzz harness over `Md4cHtml.ToHtml` would shake out parser-divergence bugs faster than re-discovering them in production.

---

## 8. Out-of-scope referrals

- **Click → Launcher dispatch path** is delegated to WinUI's built-in `Hyperlink` behavior. If the WinUI team's `Hyperlink` ever begins calling `Launcher.LaunchUriAsync` on a `mailto:` URI without the user-default-mail-client confirmation, that is a WinUI/Windows.AppSDK issue, not Reactor's. Track in **Chunk 17 (Commanding & accessibility)** if Reactor decides to wrap clicks.
- **Image source resolution and bitmap decoding** (`BitmapImage(uri)` → WinRT image codec) is delegated to WinUI; risks of decoder bugs on malformed images are out of scope for this chunk and live in WinUI/WindowsAppSDK.
- **WebView2 navigation policy** — when an app *does* feed `Md4cHtml` output into a WebView2, the responsibility for `IsScriptEnabled = false` / CSP / `NavigationStarting` filters is the embedder's. Reactor's contribution is to not hand the embedder a footgun by default (F-4, F-5).
- **Unicode tables (`Md4cUnicode.cs`)** — explicitly excluded from review per the chunking note. The `UnicodeBsearch` lookup itself is tiny and was reviewed for index safety.
- **`Md4cEntity.cs` table** — auto-generated; out of scope. Spot-checked: all codepoints in the table are ≤ 0x10FFFF, so `char.ConvertFromUtf32` (in `MarkdownBuilder.AddEntityText`) cannot throw on a successful lookup.
- **DoS via reconciler tree size** (an MD with 1 M list items mounting 1 M Reactor elements) is a property of **Chunk 14 (Reconciler & component model)**, not the markdown parser.
- **Localization / RTL homograph in link text** also surfaces in **Chunk 11 (ICU + locale formatting)** for translator-supplied strings.

---

## Summary of severities

| Severity | Findings |
|---|---|
| **High** (XSS / scheme escape via known callers) | F-4, F-5 *(only when `Md4cHtml` is fed into a WebView)* |
| **Medium** | F-1, F-3, F-6 |
| **Low** | F-2, F-7, F-8, F-9 |
| Info / correctness | F-10, F-11, F-12 |

**Top three concrete fixes (highest leverage):**
1. Default `Md4cHtml` to `NoHtml | safe-scheme-allowlist`; require an opt-in flag for unsafe rendering. (F-4, F-5)
2. Tighten `MarkdownBuilder.IsSafeUri` to require `IsAbsoluteUri` (or to explicitly allowlist relative-URI shapes). (F-1)
3. Add `MarkdownOptions.MaxInputBytes` and a `MarkdownOptions.LinkBuilder` extension point. (F-3, F-6)
