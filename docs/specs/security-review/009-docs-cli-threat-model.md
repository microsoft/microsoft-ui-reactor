# Chunk 09 — Docs CLI Threat Model

**Status:** Draft — Phase 2 deep review
**Reviewed commit:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3` (`feat(samples): add native chat sample (#95)`)
**Reviewer:** Security review pass
**Companion:** [`000-chunking-and-threat-model.md`](000-chunking-and-threat-model.md)

---

## 1. Scope

In-scope source files (1011 LOC total):

| File | LOC | Role |
|---|---:|---|
| `src/Reactor.Cli/Docs/CompileCommand.cs` | 302 | Pipeline orchestrator: validate → build → capture → assemble |
| `src/Reactor.Cli/Docs/ImageProcessor.cs` | 143 | Auto-crop + border/shadow on captured frames; uses `System.Drawing` |
| `src/Reactor.Cli/Docs/ScreenshotCapture.cs` | 143 | Spawns `dotnet run` doc app and HTTP-fetches frames from preview server |
| `src/Reactor.Cli/Docs/TemplateParser.cs` | 136 | Hand-rolled parser for `.md.dt` (YAML front-matter + Markdown body) |
| `src/Reactor.Cli/Docs/SnippetExtractor.cs` | 95 | Extracts `// <snippet:id>...// </snippet:id>` regions from `.cs` |
| `src/Reactor.Cli/Docs/DocAssembler.cs` | 75 | Replaces `snippet=` and `screenshot://` directives in template body |
| `src/Reactor.Cli/Docs/ManifestParser.cs` | 59 | Loads `doc-manifest.yaml` via YamlDotNet 16.3.0 |
| `src/Reactor.Cli/Docs/DocsCommand.cs` | 58 | Subcommand dispatcher |

Closely-coupled (out of chunk but referenced):
- `src/Reactor/Hosting/PreviewCaptureServer.cs` — the HTTP server the doc apps host (Chunk 03).
- `Reactor.Cli.csproj` — declares `<PackageReference Include="YamlDotNet" Version="16.3.0" />`.

Sample inputs reviewed for shape: `docs/_pipeline/templates/*.md.dt`, `docs/_pipeline/apps/*/doc-manifest.yaml`.

---

## 2. Data-flow diagram

```
                   ┌──────────────────────────────────────────────────────────┐
                   │                         REPO ON DISK                     │
                   │  (untrusted if repo is hostile; trusted if developer's)  │
                   └──────────────────────────────────────────────────────────┘
                              │                    │                   │
       docs/_pipeline/        │                    │                   │
         templates/*.md.dt    │  apps/*/...cs      │  apps/*/         │
         (front-matter +      │  (snippet markers) │   doc-manifest   │
          markdown body)      │                    │   .yaml           │
                              │                    │                   │
                              ▼                    ▼                   ▼
                    ┌──────────────────┐ ┌──────────────────┐ ┌────────────────┐
                    │  TemplateParser  │ │ SnippetExtractor │ │ ManifestParser │
                    │  (hand-rolled)   │ │  (line scanner)  │ │  (YamlDotNet)  │
                    └─────────┬────────┘ └────────┬─────────┘ └────────┬───────┘
                              │                   │                    │
                              ▼                   ▼                    ▼
                          DocTemplate       Snippet map           DocManifest
                              │                   │                    │
                              └────────────┬──────┴───────────┬────────┘
                                           ▼                  ▼
                                  ┌───────────────────────────────────┐
                                  │       CompileCommand              │
                                  │  Phase 1 validate → Phase 2 build │
                                  │  → Phase 3 capture → 4 extract    │
                                  │  → 5 ai (stub) → 6 assemble       │
                                  └─────┬──────────┬─────────┬────────┘
                                        │          │         │
                  Process.Start("dotnet build …")  │         │
                  Process.Start("dotnet run … --preview")    │
                                                   │         │
                                                   ▼         ▼
                  ┌────────────────────────────────────┐  ┌───────────────┐
                  │   ScreenshotCapture                │  │ DocAssembler  │
                  │   HTTP POST /preview {component}   │  │ regex replace │
                  │   HTTP GET  /frame  → bytes        │  │   directives  │
                  │   ImageProcessor.Process(bytes)    │  │               │
                  │     (System.Drawing.Bitmap)        │  │               │
                  └─────┬──────────────────────────────┘  └────────┬──────┘
                        │                                          │
                        ▼                                          ▼
              docs/guide/images/<topic>/<id>.<format>     docs/guide/<topic>.md
              (PNG bytes written to disk)                 (markdown written to disk)

                        ▲
                        │
                        │ HTTP loopback (untrusted in principle: any local process
                        │ can hit http://localhost:<port>/frame; chunk 03 owns
                        │ this trust boundary)
                        │
              ┌──────────┴──────────────┐
              │ doc-app process          │
              │ PreviewCaptureServer     │
              │ HttpListener on Loopback │
              │ src/Reactor/Hosting/     │
              │   PreviewCaptureServer.cs│
              └──────────────────────────┘
```

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Trust assumption made |
|---|---|---|---|
| B1 | Repo files (`*.md.dt`, `*.cs`, `*.yaml`) → CLI | inbound | "Source code, project files, and build inputs are trusted at build time," but per the chunking doc §2: a hostile repo opened by a developer is a real threat. CLI must not allow a hostile repo to escalate beyond "code that compiles." |
| B2 | CLI → child process (`dotnet build` / `dotnet run`) | outbound | Already in §2: building a hostile repo executes its `MSBuild` and source generators. The Docs CLI inherits this — invoking the doc app is morally equivalent to `dotnet run`. |
| B3 | CLI ↔ loopback HTTP (`http://localhost:<port>/preview`, `/frame`) | bidirectional | Loopback is in-TCB (§2). Chunk 03 owns the server side. The CLI is a *client* of an unauthenticated loopback service — the trust assumption is that the port came from the child process the CLI just spawned (it parses `CAPTURE_PORT=` from stdout). |
| B4 | CLI → filesystem (`docs/guide/`, `docs/guide/images/`) | outbound | Output directory is computed from the repo root. Anything that can influence the output path is a tampering primitive on the developer's filesystem. |

The Docs CLI **does not cross the network egress boundary** at this commit. There is no SSRF surface today: no manifest field is fetched from a URL, no HTTP client is pointed at a non-loopback host. (See Finding F-9 — the absence is not enforced; it's just that no field is wired up.)

---

## 4. Asset inventory

What is worth attacking on this chunk:

1. **Developer-machine code execution.** The CLI invokes `dotnet build` on csproj files inside `docs/_pipeline/apps/<topic>/` and `dotnet run` on the same. A hostile repo already gets MSBuild + analyzer + generator + `<Target>` execution by virtue of calling `dotnet`; the Docs CLI is an additional vector that auto-discovers and executes these for the user.
2. **Filesystem write outside `docs/guide/`.** Path-traversal on `screenshot.Id`, `screenshot.Format`, or `topicId` could write attacker-controlled bytes (PNG-shaped) outside the intended output directory.
3. **YAML / template parser DoS.** A pathological manifest or template stalls or crashes the build.
4. **Image-decode RCE / DoS.** `System.Drawing.Bitmap` is GDI+; CVE history (CVE-2007-3034, etc.) means it is not a hardened image surface. The CLI feeds bytes from `/frame` into it.
5. **JSON injection on the loopback POST `/preview` body.** `screenshot.Component` is interpolated into a JSON literal without escaping.
6. **Confused-deputy on the localhost port discovery.** The CLI trusts whatever is listening on the port the child process printed.

Not assets here (handled elsewhere):
- The PNG content itself is intentionally a screenshot of the developer's app — info disclosure of frame contents is a Chunk 03 concern.
- The compiled markdown is committed and reviewed by humans; it is not a runtime-loaded artifact in this chunk.

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| T1 | **Tampering** | Path traversal via `screenshot.Id` writes outside `docs/guide/images/<topic>/` | Hostile repo author writes `id: "../../../etc/passwd"` in `doc-manifest.yaml` | Arbitrary file write of PNG bytes inside dev machine | Med (anyone reviewing a hostile PR who runs `mur docs compile`) | `ScreenshotCapture.cs:87-89` does `Path.GetFullPath(...).StartsWith(topicDir + DirectorySeparatorChar)` check | **Mostly mitigated** — see F-1 for residual concerns |
| T2 | **Tampering** | Path traversal via `screenshot.Format` (e.g. `format: "../foo"`) | Same as T1 | Same as T1 | Med | Same `StartsWith` check on the joined path catches `..` segments | **Mitigated** — `Path.GetFullPath` normalizes |
| T3 | **Tampering** | Path traversal via `topicId` (the directory name under `apps/`) | Hostile repo author creates `apps/..something/` | Lets `topicDir` resolve outside `imagesDir` | Low (`Directory.GetDirectories` returns child dirs only; `Path.GetFileName` of a child can't be `..`) | `Path.Combine(imagesDir, topicId)` then directory creation | **Mitigated** in practice |
| T4 | **Tampering** | YAML deserialization gadget / type confusion in `ManifestParser` | Hostile manifest with `!!type` tags | RCE via deserializer-gadget if YamlDotNet supported it | Low | YamlDotNet's default `Deserializer` does **not** instantiate arbitrary CLR types from YAML tags (unlike Newtonsoft `TypeNameHandling.All`); only properties of the declared root type are populated | **Mitigated by YamlDotNet defaults** — see F-2 (verify) |
| T5 | **Tampering** | Template "expression" injection (SSTI) | Hostile `*.md.dt` body | Code execution at compile time, content rewrite | None | `DocAssembler` only does two regex substitutions: `snippet=` directives and `screenshot://` URLs. No expression evaluation, no scripted templating. Front-matter is parsed by a hand-rolled key/value reader (`TemplateParser.ParseFrontMatter`) into 5 string fields. | **No template-engine surface today** — see F-3 |
| T6 | **Tampering** | Snippet content injection — hostile `.cs` includes content that, when assembled into a fenced code block, breaks out of the fence | Hostile repo with a `.cs` file containing literal "```" inside a `<snippet:…>` region | Markdown-rendering downstream sees attacker-controlled markdown outside the code block | Med (literal "```" in C# source is rare but writable) | `DocAssembler.cs:48-50` emits ` ```csharp\n{snippet.Code}\n``` ` with no escaping of triple-backticks | **Finding F-4** |
| T7 | **Tampering** | JSON injection in loopback POST body via `screenshot.Component` | Hostile manifest sets `component: "x\",\"evil\":\"y"` | Sends an attacker-shaped JSON to `/preview`; behavior depends on Chunk 03 server. Not a code-injection primitive on its own, but the assumption that it's a single string is broken. | Low | None — `ScreenshotCapture.cs:72` uses `$"{{\"component\":\"{screenshot.Component}\"}}"` raw interpolation | **Finding F-5** |
| T8 | **Spoofing / Confused deputy** | An unrelated local process binds to the same loopback port the CLI is about to discover | Local non-admin malware on the dev box | The CLI POSTs `screenshot.Component` (repo-author-controlled) and reads "frames" from an attacker process | Low (race window: the CLI parses `CAPTURE_PORT=<n>` from its child's stdout, child claims a free port via `TcpListener(IPAddress.Loopback, 0)`) | None — no token, no PID/process check on the HTTP responder | **Finding F-6** |
| T9 | **DoS / RCE** | `System.Drawing.Bitmap` decoder on attacker-controlled `frameBytes` | Confused-deputy from T8, *or* a malicious PreviewCaptureServer if Chunk 03's `/frame` is reachable from non-loopback in some configuration | GDI+ has known image-decoder vulns; at minimum hangs/leaks; historically RCE-class | Low (loopback) → High-severity if T8 exploited | None — `ImageProcessor.cs:25` calls `new Bitmap(ms)` on bytes received over HTTP. No max-size, no pre-validation. `System.Drawing.Common` is "Windows-only" and Microsoft has explicitly deprioritized it for new processing of untrusted images. | **Finding F-7** |
| T10 | **DoS** | Pixel-by-pixel scan in `FindContentBounds` is O(W·H/4); no cap on input dimensions | Server returns a giant PNG | CLI hangs/OOMs | Low | None — no `MaxWidth`/`MaxHeight` check before `new Bitmap(ms)` | **Finding F-8 (low)** |
| T11 | **DoS** | Pathological YAML (anchor amplification, deeply nested) | Hostile manifest | Long compile or OOM | Low | YamlDotNet has no built-in anchor-bomb cap, but the schema deserializes into a fixed POCO with no recursive list/map fields → amplification has nowhere to expand into. The `ScreenshotConfig` list is bounded only by file size. | Info F-2 |
| T12 | **DoS** | Snippet-extractor: arbitrary nesting of `<snippet:id>` markers, never closing | Hostile `.cs` | Memory grows linearly with file size; warns and continues — not an unbounded DoS | Low | `SnippetExtractor.cs:67-70` warns on unclosed snippets; uses `File.ReadAllLines` (loads whole file into memory) | Info |
| T13 | **EoP** | Hostile csproj under `docs/_pipeline/apps/<topic>/` causes arbitrary build-time code execution via MSBuild `<Target Name="Build" …><Exec Command="…"/>` or analyzers | Hostile repo | Arbitrary code execution at the privilege of the developer running `mur docs compile` | High **if** the developer doesn't already understand that running the docs pipeline executes the doc apps | `CompileCommand.cs:235-264` `BuildApp` invokes `dotnet build`; `ScreenshotCapture.cs:28-37` invokes `dotnet run`. Neither is an additional vulnerability beyond "running the build" but it's an **attack-surface amplifier**: a repo can hide a malicious csproj under `docs/_pipeline/apps/` that the user never opened in the IDE. | **Finding F-9** |
| T14 | **EoP / argument injection** | `csproj` path interpolated into `dotnet build` / `dotnet run` arguments without escaping | Hostile repo names a csproj `foo" --some-evil-arg "x.csproj` | Argument injection into `dotnet` | Low (Windows `ProcessStartInfo.Arguments` *does* allow `\"`-quoting; csproj filenames with `"` are unusual but possible on NTFS) | `CompileCommand.cs:243` `Arguments = $"build \"{csproj}\" -v q --nologo -nowarn:MSB3277"`; `ScreenshotCapture.cs:31` similar. Both rely on naive `\"…\"` quoting around an attacker-controllable path. | **Finding F-10** |
| T15 | **EoP** | Template `LockedSection` content from a malicious template later interpreted in an AI-author phase | Hostile repo | At present, Phase 5 is "(not yet implemented)" — `CompileCommand.cs:169`. No risk today; future work risk. | None — Phase 5 unimplemented | Info / open question for future |
| T16 | **Repudiation** | No structured logging of which manifest produced which output | — | Hard to audit "did docs CI produce a tampered file?" | Med | All output goes to `Console.WriteLine` only | Info — out of scope |
| T17 | **Info disclosure** | Captured frames written to repo can contain whatever the doc app rendered (env vars, paths, secrets if a sample reads them) | Same as Chunk 03 | Disclosure into a committed image | Low | None in this chunk; doc apps are trusted authored content | Info — referred to Chunk 03 |
| T18 | **DoS** | Catastrophic regex backtracking in `DocAssembler.SnippetDirective` / `ScreenshotDirective` / `CompileCommand.SnippetRefPattern` | Hostile template body | Stalls compile | Low | Patterns are simple (`[^"]+`, `[^)]+`) — anchored to closing delimiters, no nested quantifiers; no obvious ReDoS | Mitigated |

---

## 6. Findings

Severity scale: **Critical** (RCE / unauth remote impact), **High** (build-time RCE on a hostile repo with realistic preconditions), **Medium** (tampering / DoS with a clear vector), **Low** (defense-in-depth gap, narrow precondition), **Info** (observation / future-work).

### F-1 — Path-traversal check on screenshot output is *almost* correct but case-fragile and misses one quirk

**Severity:** Low
**File:** `src/Reactor.Cli/Docs/ScreenshotCapture.cs:87-89`

```csharp
var outputPath = Path.GetFullPath(Path.Combine(topicDir, $"{screenshot.Id}.{screenshot.Format}"));
if (!outputPath.StartsWith(Path.GetFullPath(topicDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Screenshot id '{screenshot.Id}' would escape output directory");
```

This is a real defense and catches the obvious `id: "../../etc/foo"` case. Concerns:

1. **`OrdinalIgnoreCase` is the right choice on Windows but on a case-sensitive filesystem (e.g. shared dev volume mounted from WSL2 or a Linux CI runner) it is *too permissive*** — a directory `Topic` and `topic` would compare equal here but be different on disk. Not a bypass; it's a false-negative on hostile sibling directory names. Low severity.
2. **`topicDir` is not `Path.GetFullPath`-normalized when constructed (line 60)**. It is `Path.Combine(outputImagesDir, topicId)`. If `outputImagesDir` is relative, the comparison with `Path.GetFullPath(topicDir)` still works because `GetFullPath` is called on both sides — but reviewers should not trust this until verified.
3. **The check is per-file but not per-component** — `screenshot.Component` (used in the JSON body) is not validated for path-shape because it doesn't go to disk; that's correct.
4. **No check on `screenshot.Format` for invalid extensions** (e.g. `.exe`, `.lnk`, an alternate-data-stream `:foo`, or simply empty). Empty `Format` results in a file named `<id>.` which on Windows trims to `<id>`. The path-traversal check still holds, but the file may end up being written with a misleading name (e.g. matching an existing `.cs` filename in the same dir). Low.

**Recommendation:**
- Reject `screenshot.Id` matching `^[A-Za-z0-9._-]+$` (it's emitted as `${id}.${format}` in markdown anyway, and any other char can be misrendered).
- Reject `screenshot.Format` that isn't in an allowlist `{png, jpg, jpeg, webp}`.
- Consider keeping the `StartsWith` check but flipping to ordinal (case-sensitive) and resolving both sides through `Path.GetFullPath` *before* comparison.

### F-2 — YamlDotNet 16.3.0 default `Deserializer` is safe today, but this is not enforced

**Severity:** Info
**File:** `src/Reactor.Cli/Docs/ManifestParser.cs:49-58`

```csharp
private static readonly IDeserializer Deserializer = new DeserializerBuilder()
    .WithNamingConvention(HyphenatedNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();
```

The default YamlDotNet `Deserializer` does not honor `!System.Type, ...` tags or instantiate polymorphic CLR types — that capability is only enabled when you opt in via `WithTypeMapping`/`WithTagMapping`/`WithTypeDiscriminatingNodeDeserializer` and friends. No such opt-in is present here. The code is therefore not vulnerable to "BinaryFormatter-style" gadget chains.

But: a future maintainer adding `WithTagMapping` or `WithTypeConverter` to support polymorphism would silently re-introduce the deserialization-gadget surface, because the existing `Deserialize<DocManifest>` call assumes the schema is closed.

**Recommendation (defense in depth):**
- Add a unit test that asserts deserializing a YAML containing `!!python/object:os.system` (or any non-mapped tag) does *not* succeed in instantiating a non-`DocManifest` type. The `IgnoreUnmatchedProperties` setting is unrelated to type-tag handling.
- Consider a code comment at `ManifestParser.cs:49` documenting "do not enable type-tag mapping without a security review."

### F-3 — There is no template-engine — but the chunking doc asked us to look for SSTI; record explicitly

**Severity:** Info
**File:** `src/Reactor.Cli/Docs/DocAssembler.cs:14-69`, `TemplateParser.cs`

The `.md.dt` format is **not** a templating language. It is:
- a YAML-ish front-matter header, parsed by `ParseFrontMatter` into five fixed string fields;
- a Markdown body that goes through *exactly* two regex substitutions:
  - `` ```csharp snippet="topic/id" ... ``` `` → fenced code block of the snippet's source text;
  - `![alt](screenshot://topic/id)` → `![alt](images/topic/id.format)`.

There is no expression evaluation, no `{{ … }}` interpolation, no helper invocation, no method dispatch. SSTI is not a threat here.

The "AI Author" Phase 5 (`CompileCommand.cs:169`) is a `Console.WriteLine` placeholder. If/when it lands, treat it as a separate review.

### F-4 — Snippet content can break out of the fenced code block

**Severity:** Medium
**File:** `src/Reactor.Cli/Docs/DocAssembler.cs:45-51`

```csharp
sb.AppendLine("```csharp");
sb.AppendLine(snippet.Code);
sb.Append("```");
```

If a `.cs` source file under `docs/_pipeline/apps/<topic>/` contains the literal three-backtick sequence inside a `<snippet:…>` region (which is *legal* C# inside a verbatim string `@""`, a raw string literal `"""..."""`, or a multi-line comment), the fence terminates early and the rest of the snippet code is rendered as ordinary markdown. A hostile-repo author can use this to inject arbitrary markdown — including `<script>` tags if the markdown is later rendered in a webview, or links/images that fire off network requests — into the compiled doc.

Concretely, a snippet like:
```csharp
// <snippet:demo>
var s = """
```
[click](https://attacker.example/exfil?token=...)
""";
// </snippet:demo>
```
…would emit:
````
```csharp
var s = """
```
[click](https://attacker.example/exfil?token=...)
""";
```
````
The middle three lines are no longer inside the code fence in the output.

**Severity rationale:** Medium because (a) the output is a committed `.md` file under `docs/guide/` reviewed by humans, so the attacker has to slip the malicious template + snippet through code review, but (b) the attacker controls both halves of the docs build and can craft snippet content that *looks innocuous in a `.cs` diff* but escapes in the rendered output. The likelihood is low; the "defense in depth" cost is small.

**Recommendation:**
- Detect occurrences of triple-backticks inside `snippet.Code` and either (a) raise the fence to four (or five) backticks dynamically (CommonMark allows a longer fence to wrap content with shorter fences), or (b) reject the snippet with a clear error.

### F-5 — JSON injection in `/preview` POST body via `screenshot.Component`

**Severity:** Low
**File:** `src/Reactor.Cli/Docs/ScreenshotCapture.cs:72-74`

```csharp
var json = $"{{\"component\":\"{screenshot.Component}\"}}";
var content = new StringContent(json, global::System.Text.Encoding.UTF8, "application/json");
var switchResp = await http.PostAsync($"http://localhost:{port}/preview", content);
```

`screenshot.Component` is an unescaped repo-controlled string spliced into a JSON literal. A value like `x","other":"y` produces `{"component":"x","other":"y"}`. Any `"` or `\` in `Component` corrupts the JSON; control chars (`\n`, `\t`) make the JSON invalid; a NUL would stop most JSON parsers.

The downstream impact is bounded by what `PreviewCaptureServer.HandleSwitchComponent` accepts (Chunk 03). Worst-case it lets a hostile manifest pass *additional* fields to that endpoint that the manifest schema doesn't expose. Today the loopback server is in-TCB, so impact is low — but the chunking doc explicitly flags loopback-trust as the assumption to test, not assert.

**Recommendation:**
- Use `JsonSerializer.Serialize(new { component = screenshot.Component })` (or `System.Text.Json`'s source-generated equivalent for AOT-friendliness, since `Reactor.Cli` is otherwise AOT-aware).

### F-6 — Loopback port is trusted because it appeared on child-process stdout, with no further binding check

**Severity:** Low (today) / **Medium** (if loopback-trust assumption is challenged)
**File:** `src/Reactor.Cli/Docs/ScreenshotCapture.cs:117-142`

`WaitForCapturePort` reads `CAPTURE_PORT=<n>` from the child's stdout and treats whatever is bound on `127.0.0.1:<n>` as the doc app's preview server. There is:
- no shared secret / token in the URL;
- no PID check (anyone on the box could `bind()` first or after the child exits in `--preview` mode);
- no TLS / certificate;
- no `Origin`/`Host` validation.

If a local non-privileged process predicted the port and bound it before `Reactor.Hosting.PreviewCaptureServer` did (`PreviewCaptureServer.cs:317` uses `new TcpListener(IPAddress.Loopback, 0)` to grab a free port, and only *then* prints `CAPTURE_PORT=`), it is too late — the CLI reads the printed port and connects to whoever is listening, which on Linux/macOS for a 0-bound port is unambiguous, but on Windows pre-Win10 SO_REUSEADDR semantics could differ. In practice the print-after-bind ordering closes the race; document that.

A more realistic concern: a *separate* doc-app instance launched concurrently for an unrelated reason could collide, since the discovery key is "the next `CAPTURE_PORT=` line on stdout."

**Recommendation:**
- Pass an opaque token via env var to the child, have it echo `CAPTURE_PORT=<n> CAPTURE_TOKEN=<t>` and require the token in a header on `/preview` and `/frame` POSTs/GETs. (This is also a recommendation for Chunk 03.)
- Defer to Chunk 03's review — this finding is a CLI symptom of a server-side gap.

### F-7 — `System.Drawing.Bitmap` is fed bytes from HTTP without bounds or pre-validation

**Severity:** Medium
**File:** `src/Reactor.Cli/Docs/ImageProcessor.cs:23-26`, `ScreenshotCapture.cs:86-91`

```csharp
var frameBytes = await http.GetByteArrayAsync($"http://localhost:{port}/frame");
…
var processed = ImageProcessor.Process(frameBytes);
…
public static byte[] Process(byte[] frameBytes) {
    using var ms = new MemoryStream(frameBytes);
    using var source = new Bitmap(ms);   // GDI+ decode of attacker-controllable bytes
    …
}
```

Three properties of this code:
1. **`HttpClient.GetByteArrayAsync` has no size cap** — a malicious server can stream gigabytes.
2. **`new Bitmap(Stream)` calls into GDI+ (`gdiplus.dll`)**, which has a long history of image-decoder vulns (CVE-2007-3034, CVE-2020-0964, etc.). Microsoft documents `System.Drawing.Common` as Windows-only and explicitly recommends *not* using it on untrusted images for new code; `ImageSharp`/`SkiaSharp` are the recommended alternatives.
3. **The decoded image then drives `FindContentBounds`** which is `O(W*H/4)` with no cap on `bmp.Width`/`bmp.Height`.

In the **current threat model** (loopback-trusted, server is the doc app the CLI just spawned), the bytes are trusted by transitive trust. Combined with **F-6**, however, a local non-privileged attacker who wins the port race would feed attacker-controlled image bytes into GDI+ on the developer's machine.

**Recommendation:**
- Cap response size: use `HttpClient` with `MaxResponseContentBufferSize` or read the response with a `MaxBytes` check.
- Validate magic bytes (PNG `89 50 4E 47 0D 0A 1A 0A`, JPEG `FF D8 FF`) before constructing `Bitmap`.
- Cap `Width * Height` to a sane ceiling (e.g. `8192 * 8192`).
- Long-term: switch the docs pipeline to `SkiaSharp` or `ImageSharp` for hardened decode.

### F-8 — `FindContentBounds` is `GetPixel`-based and quadratic, can be slowed/hung by large attacker-controlled frames

**Severity:** Low
**File:** `src/Reactor.Cli/Docs/ImageProcessor.cs:44-94`

Each `Bitmap.GetPixel` call locks/unlocks bits internally — the routine is famously slow. Combined with no input-size cap (F-7), an attacker who controls the frame source can produce hangs that look like deadlocks. Independent of F-7, even in benign scenarios this is the wrong way to scan an image; reviewers should consider `LockBits` for performance separately from security.

**Recommendation:** Pair with F-7 — adding a max-dimensions check is sufficient mitigation for the security angle.

### F-9 — No SSRF surface today; the absence is implicit, not enforced

**Severity:** Info
**Files:** `ManifestParser.cs:6-45` (schema), `ScreenshotCapture.cs:74,86`

No manifest field today is a URL or a host. The only HTTP traffic the Docs CLI emits is to `http://localhost:<discovered-port>`. A future change adding e.g. `screenshots[].url:` for "fetch a remote screenshot" would introduce an SSRF surface that the CLI has no defenses for (no scheme allowlist, no IP-block check, no follow-redirects cap).

**Recommendation:** Document the property "the docs CLI does not perform non-loopback HTTP egress" as an invariant in `CompileCommand.cs`'s file header, so a maintainer adding a URL-fetching field has an explicit fence to consider.

### F-10 — `csproj` path interpolation into `dotnet` arguments is naively quoted

**Severity:** Low
**Files:** `src/Reactor.Cli/Docs/CompileCommand.cs:240-247`, `src/Reactor.Cli/Docs/ScreenshotCapture.cs:28-37`

```csharp
Arguments = $"build \"{csproj}\" -v q --nologo -nowarn:MSB3277",
```
and
```csharp
Arguments = $"run --project \"{csproj}\" -- --preview --vscode --fps 5",
```

`csproj` comes from `Directory.GetFiles(appDir, "*.csproj").FirstOrDefault()`. On NTFS, a filename can legally contain `"` (rare but possible — explorer typically forbids it but the API does not), in which case the surrounding `\"…\"` quoting closes early and any subsequent characters become additional CLI arguments to `dotnet build` / `dotnet run` / the doc app. A hostile repo can craft such a filename via `git config core.protectNTFS=false` or by checking in the file from a Linux machine.

Concrete example: a csproj named `App" --target Foo "App.csproj` produces `dotnet build "App" --target Foo "App.csproj" -v q --nologo …` — `--target Foo` gets injected.

**Recommendation:**
- Use the `ProcessStartInfo.ArgumentList` API instead of `Arguments` string interpolation. `ArgumentList` quotes per-argument correctly for Windows `CommandLineToArgvW` semantics.
- Independently: validate the discovered csproj filename matches `^[A-Za-z0-9._\-]+\.csproj$` before launching a child process on it.

### F-11 — Doc-app build/run is implicit RCE-on-hostile-repo, but it's the same RCE as opening the repo in the IDE

**Severity:** Info (acknowledged trust assumption)
**Files:** `CompileCommand.cs:235-264`, `ScreenshotCapture.cs:28-43`

Running `mur docs compile` against a hostile repo executes:
1. MSBuild + analyzers + source generators on every csproj under `docs/_pipeline/apps/`.
2. The compiled doc app itself, with `--preview --vscode --fps 5`.

This is consistent with §2 of `000-chunking-and-threat-model.md` — building a hostile repo is already RCE; the Docs CLI does not add new privilege. It does, however, **extend reach** to csprojes a developer may not have opened in the IDE. A reviewer who runs `mur docs compile` on an unfamiliar PR gets every `docs/_pipeline/apps/*/foo.csproj` executed.

**Recommendation:** Add a one-line note in the README / help output: "compile builds and runs all doc-app projects under docs/_pipeline/apps/."

---

## 7. Open questions

1. **Is loopback-trust valid for the doc-app port?** The CLI relies entirely on "the port came out of the child process's stdout" (F-6). Chunk 03 owns the server side — does `PreviewCaptureServer` do origin/header validation that would catch a port-squat attacker?
2. **YamlDotNet 16.3.0 supplied versions across the build matrix** — is the PackageReference floated/centralized? If yes, a downstream version bump to a tagged-type-friendly default would silently expand T4 (F-2). Where is the version pinned?
3. **Phase 5 (AI authoring) future shape** — `CompileCommand.cs:169` is a stub. When implemented, it will add an outbound trust boundary (presumably to Azure OpenAI) and ingestion of model output back into committed markdown. That's a separate threat model but worth scoping now (overlap with Chunk 07).
4. **Doc-app discovery is "any directory under `docs/_pipeline/apps/`"** (`CompileCommand.cs:200-215`). Should `mur docs compile` print the full list and require interactive confirmation, or take a `--allow-list` of expected topic IDs, before executing arbitrary csproj? This reduces F-11.
5. **Are docs/guide/*.md and docs/guide/images/** ever read at runtime by the framework or shipped to end users? If so, any tampering-via-snippet (F-4) becomes a runtime concern, not just a committed-artifact concern. Per the chunking doc the docs are author-time; confirm they are not re-shipped.

---

## 8. Out-of-scope referrals

| Concern | Owner |
|---|---|
| Authentication / origin-check on `PreviewCaptureServer` (F-6 root cause) | **Chunk 03** |
| `/preview` endpoint accepting unexpected fields (F-5 sink) | **Chunk 03** |
| Frame content disclosure of dev secrets in committed PNGs | **Chunk 03** (Reactor's preview surface owns what's in a frame) |
| `dotnet build`/`dotnet run` on hostile csproj as a generic risk | Out of Reactor's scope (MSBuild trust model); flagged as F-11 for visibility |
| AI authoring egress (future) | **Chunk 07** parallels |
| Markdown rendering of compiled `docs/guide/*.md` if it occurs at runtime | **Chunk 10** (markdown parser); tampering primitive sources from F-4 |

---

## 9. Severity summary

| Finding | Severity |
|---|---|
| F-4 Snippet fence break-out | **Medium** |
| F-7 Untrusted-image decode via GDI+ + no size cap | **Medium** |
| F-1 Path-traversal residuals (case + format whitelist) | Low |
| F-5 JSON injection in `/preview` body | Low |
| F-6 Loopback port discovery has no token | Low |
| F-8 `FindContentBounds` cost on large frames | Low |
| F-10 `csproj` arg-interpolation quoting | Low |
| F-2 YamlDotNet defaults safe but unenforced | Info |
| F-3 No template engine present | Info |
| F-9 No SSRF surface today, not enforced | Info |
| F-11 Hostile-repo build/run amplification | Info |

No Critical or High findings at this commit. The two Medium findings are the actionable items: switch the image pipeline off GDI+ + size-cap the frame fetch (F-7), and emit a longer fence when snippet code contains triple-backticks (F-4).
