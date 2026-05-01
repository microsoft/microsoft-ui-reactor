# Chunk 06 — Localization CLI: Threat Model

**Status:** Phase 2, deep STRIDE pass
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer scope:** `mur loc {extract|translate|validate|prune|status}` build-time tooling
**Trust frame inherited from `000-chunking-and-threat-model.md`:**
- Repo opened by the developer is **untrusted** (hostile-repo threat actor).
- Developer running the CLI is **trusted**.
- `.resw` files authored by translators are **semi-trusted** and an active threat surface.
- Network egress to translation providers is covered by Chunk 07 (out of scope here).

---

## 1. Scope

| File | LoC | Role |
|---|---:|---|
| `src/Reactor.Cli/Loc/LocCommand.cs` | 58 | Subcommand dispatch |
| `src/Reactor.Cli/Loc/ExtractCommand.cs` | 156 | Walk `*.cs`, extract literals, emit `.resw` |
| `src/Reactor.Cli/Loc/TranslateCommand.cs` | 298 | Read source `.resw`, drive translation provider, write target `.resw` |
| `src/Reactor.Cli/Loc/ValidateCommand.cs` | 178 | Read all locales, ICU syntax & param check |
| `src/Reactor.Cli/Loc/PruneCommand.cs` | 220 | Scan `*.cs` for `Loc.X.Y` refs, remove unused keys from `.resw` |
| `src/Reactor.Cli/Loc/StatusCommand.cs` | 147 | Read all locales, print coverage |
| `src/Reactor.Cli/Loc/LocalizableStringScanner.cs` | 371 | Roslyn DSL walker |
| `src/Reactor.Cli/Loc/KeyNamer.cs` | 182 | PascalCase key generation |
| `src/Reactor.Cli/Loc/KeyedLocString.cs` | 28 | Data class |
| `src/Reactor.Cli/Loc/LocalizableString.cs` | 37 | Data class |
| `src/Reactor.Cli/Loc/ReswReader.cs` | 243 | Parse `.resw` XML, ICU helpers |
| `src/Reactor.Cli/Loc/ReswWriter.cs` | 128 | Write `.resw` XML idempotently |
| `src/Reactor.Cli/Loc/SourceRewriter.cs` | 179 | Roslyn-driven in-place `.cs` rewrite |
| `src/Reactor.Cli/Loc/InterpolationConverter.cs` | 228 | `$"…"` → ICU |
| **Total** | **2453** | |

TFM: `net9.0-windows10.0.22621.0` (`src/Reactor.Cli/Reactor.Cli.csproj:5`). Roslyn is `Microsoft.CodeAnalysis.CSharp 4.8.0` (line 21).

Out-of-scope here but traversed for context: `AzureOpenAiProvider.cs`, `ITranslationProvider.cs`, `TranslationPrompt.cs` (covered by Chunk 07); `Reactor.Localization.Generator/LocSourceGenerator.cs` (Chunk 08) — referenced once because key sanitization in the generator is the build-time target of any `.resw`-key tampering attack.

---

## 2. Data-flow diagram

```
                       (untrusted repo)              (semi-trusted translators)
                               │                              │
                               ▼                              ▼
             ┌──────────────────────────────┐    ┌────────────────────────┐
   --source  │ Directory.GetFiles(*.cs,     │    │ Strings/{locale}/*.resw│
   path ───▶ │   SearchOption.AllDirectories│    │ (XML)                  │
             │  → File.ReadAllText)         │    └────────────┬───────────┘
             └──────────────┬───────────────┘                 │ XDocument.Load
                            │                                 ▼
              ┌─────────────▼────────────┐         ┌─────────────────────┐
              │ LocalizableStringScanner │         │ ReswReader          │
              │ (Roslyn syntax walker)   │         │ ParseReswFile, ICU  │
              └─────────────┬────────────┘         │ syntax / param ext. │
                            │                      └──────────┬──────────┘
                            ▼                                 │
                ┌─────────────────────┐                       │
                │ InterpolationConv.  │                       │
                │ (Roslyn → ICU)      │                       │
                └─────────────┬───────┘                       │
                              ▼                                │
                     ┌────────────────┐                        │
                     │ KeyNamer       │                        │
                     │ (PascalCase)   │                        │
                     └────────┬───────┘                        │
                              ▼                                ▼
                  ┌─────────────────────────┐         ┌────────────────────┐
                  │ ReswWriter / Translate  │   ┌────▶│ Validate / Status  │
                  │   .Write (XDocument)    │   │     │  (read-only)       │
                  └────────────┬────────────┘   │     └────────────────────┘
                               │                │
                               ▼                │
              ┌────────────────────────────┐    │
   --output   │ Directory.CreateDirectory  │    │
   path  ───▶ │ Path.Combine(out, name)    │    │
              │ doc.Save(filePath)         │    │
              └────────────┬───────────────┘    │
                           │                    │
              ┌────────────▼─────────┐    ┌─────┴────────────────────────┐
              │ SourceRewriter (only │    │ PruneCommand → XDocument.Save│
              │ on --rewrite):       │    │ on every locale .resw        │
              │ File.WriteAllText    │    └──────────────────────────────┘
              │ over original .cs    │
              └──────────────────────┘
                       │
                       ▼
               (developer's working tree)
```

Two trust transitions matter:

1. **Repo → CLI (process memory):** `*.cs` content is parsed by Roslyn (safe — Roslyn is hardened for hostile syntax), and `.resw` XML is parsed by `XDocument`. Both are *interpreted*, but the interpretation is followed by *writes back to the working tree*.
2. **CLI → working tree:** Any path computed from a developer-supplied `--source`, `--output`, `--resources`, or any path discovered by directory enumeration is eligible for `File.WriteAllText` / `XDocument.Save`. There is **no canonicalization or containment check** anywhere in the chunk.

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Assumption made |
|---|---|---|---|
| B1 | Hostile-repo `*.cs` content → CLI parser | in | Roslyn is robust on adversarial input. (Held — Roslyn is the canonical C# compiler frontend.) |
| B2 | Hostile-repo `*.resw` content → CLI XML parser | in | `XDocument.Load(string)` defaults are safe (DTD prohibited, no resolver). **In .NET 9 this holds**; see Finding F1. |
| B3 | Hostile-repo directory layout (symlinks, junctions, reparse points) → recursive enumeration | in | `Directory.GetFiles(..., AllDirectories)` will not escape the supplied root. **This does not hold on Windows** — see Finding F2. |
| B4 | Translator-authored `name=` attribute in `.resw` → generator-emitted C# (Chunk 08) | in→build | Either keys are filtered, or the generator escapes them when interpolating into emitted source. See Finding F3 (cross-chunk). |
| B5 | CLI → developer working tree | out | The CLI may overwrite any path under `--source` (`--rewrite`) or under `--output`. The developer is trusted to point these at the right place; but no rails prevent point-and-shoot footgun. See Finding F4. |

---

## 4. Asset inventory

- **A1 — Integrity of the developer's working tree.** `mur loc extract --rewrite` and `mur loc prune` both modify files in-place; if a hostile repo can steer those writes outside the repo, that is a code-execution-adjacent primitive (e.g. clobber `~/.gitconfig`, drop a `.cs` into a sibling project that is later built).
- **A2 — Integrity of generated identifiers.** A `.resw` `name=` attribute flows into a C# identifier and into a string literal in `Loc.g.cs` (Chunk 08, `LocSourceGenerator.cs:164`). Any path that fails to escape `"` / `\` in that string-literal context is a build-time RCE primitive. See F3.
- **A3 — Confidentiality of files outside the repo.** If symlink-following or `..` in `--source` lets the CLI read files the developer did not intend, those file contents end up in CLI output, in `.resw` `<value>` content, or printed to stderr in `[WARN] Failed to parse {file}: …` (`ExtractCommand.cs:73`). Because the CLI prints absolute paths in errors, info disclosure of the host filesystem layout is real.
- **A4 — CLI availability.** `XDocument.Load` on a billion-laughs `.resw` is a DoS concern; in current .NET 9 default settings DTDs are prohibited so this is mitigated by default.
- **A5 — Roslyn parse-cost.** A pathological `.cs` (deeply nested generics, etc.) is at most a per-file slowdown; not a meaningful threat for build-time CLI use.

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / Recommendation |
|---|---|---|---|---|---|---|---|
| T1 | Tampering | `--source` is `..\..\..\..` (or absolute path outside repo). `extract --rewrite` then writes back to source files outside the repo. | Hostile repo's CONTRIBUTING.md tells the developer to run `mur loc extract --source ../../shared --rewrite`. | Arbitrary file overwrite under user privileges. | Low (requires social engineering) | None — `sourcePath` is used directly with no canonicalization. | **F4.** Don't fix by sandboxing; document the trust contract: `--source` and `--output` are trusted to be under the repo by the developer. Add a `--repo-root` containment check or refuse paths containing `..`. |
| T2 | Tampering | A symlink/junction inside the repo points at `C:\Users\<dev>\Documents\` (or any sibling project). `Directory.GetFiles(..., AllDirectories)` traverses through it, and `--rewrite` then writes back to files OUTSIDE the supposed repo root. | Malicious git repo. Trivial to author: `mklink /J subdir C:\Users\Public` is just a directory entry on Windows; `git` does ship junctions in some cases. | Arbitrary file overwrite under user privileges, scoped to ".cs files outside repo." Combined with a sibling project that gets built later, RCE. | Medium (junctions are a known git supply-chain primitive) | None. | **F2.** High. Use `EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }` on every recursive enumeration. Also reject any discovered path whose `Path.GetFullPath` does not have the source root as a prefix. |
| T3 | Tampering | Hostile `.resw` containing a `name=` whose value is `Foo", " + System.IO.File.ReadAllText("/secret") + "` — designed to break out of the C# string literal at `LocSourceGenerator.cs:164`. | Hostile repo or compromised translator. | Build-time RCE in CI. | Medium-high (the generator code path is the canonical attack surface for this trust class). | `KeyNamer` only ever produces PascalCase keys for the *extract* path; but the generator does NOT re-validate keys it reads from `.resw`. | **F3.** Cross-chunk to **Chunk 08**. The CLI side is fine; the fix lives in the generator. Recorded here for traceability of the threat model. |
| T4 | Tampering | A `.cs` file in the repo is a junction to `C:\Windows\notepad.exe`. The CLI `File.ReadAllText` returns binary; Roslyn produces a parse error; the CLI prints the absolute target path in the warning. | Hostile repo. | Information disclosure (absolute file paths revealed); CLI does not crash. | Low | The catch in `ExtractCommand.cs:71-75` handles parse exceptions but logs `{file}` (which is the path *inside* the repo, not the symlink target). | Acceptable risk; document. |
| T5 | Information disclosure / DoS | XXE / billion-laughs / external-DTD attack on `XDocument.Load` against a hostile `.resw`. | Hostile repo. | XXE: read arbitrary file via DTD entity expansion → leaked into `<value>` content. Billion laughs: CLI hang / OOM. | Low (mitigated by .NET 9 defaults). | `XDocument.Load(string)` in modern .NET defaults to `DtdProcessing.Prohibit` and `XmlResolver = null`. The five call sites (`ReswReader.cs:99`, `ReswWriter.cs:28,75`, `TranslateCommand.cs:230`, `PruneCommand.cs:183`) all use the default-settings overload. | **F1.** Defense-in-depth: pin the parser by switching to `XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 })` so a future maintainer cannot accidentally reintroduce the issue with `XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }`. |
| T6 | DoS | A `.resw` with a 10MB single `<value>` (no DTD, just bulk) causes `XDocument.Load` to allocate equivalently. | Hostile repo. | Per-process memory pressure during `extract` / `validate` / `status`. | Low — build-time CLI, single user, recoverable. | None. | Document; no action. |
| T7 | DoS | Pathologically deep ICU braces in `.resw` `<value>` against `ReswReader.ExtractIcuParameters` / `ValidateIcuSyntax`. | Hostile/translator. | The brace counter is depth-tracked but the loop is O(n) on string length; no recursion. Bounded. | Low | Iterative parser, not recursive. | No action. |
| T8 | DoS / catastrophic-backtracking | `KeyNamer.GenerateHintFromValue` runs `Regex.Replace(value, @"\{[^}]+\}", " ")` and `[^\w\s]` on attacker-controlled `.resw` values. | Hostile repo. | None significant — the regexes are linear, no nested quantifiers. | Negligible | — | No action. |
| T9 | DoS / catastrophic-backtracking | `PruneCommand.LocRefPattern = new Regex(@"Loc\.(\w+)\.(\w+)\|Loc\.(\w+)", RegexOptions.Compiled)` against attacker-controlled `*.cs` content. | Hostile repo. | Pattern is bounded (no nested quantifiers). | Negligible | — | No action. |
| T10 | Tampering | `SourceRewriter.cs:55` does `File.WriteAllText(filePath, …)` where `filePath` came from `Directory.GetFiles(..., AllDirectories)`. If that enumeration followed a symlink, the write goes through the symlink to the real target. | Hostile repo. | Same as T2; same mitigation. | Medium | None. | **F2 (continued).** |
| T11 | Tampering | `Path.Combine(outputDir, $"{reswFileName}.resw")` in `ReswWriter.cs:67` and `TranslateCommand.cs:223`. `reswFileName` derives from a class name and `KeyNamer.StripClassSuffix` does not constrain it to be path-safe. A class named `..\..\evil` would produce `..\..\evil.resw`. | Hostile repo with a class named `[…/…]…`. | Class names cannot validly contain path separators in C# — Roslyn would not parse them as identifiers. So `ClassDeclarationSyntax.Identifier.Text` is constrained to legal identifier chars. | Negligible (Roslyn enforces identifier well-formedness). | Roslyn's parser. | No action. Document the dependency on Roslyn. |
| T12 | Tampering | `TranslateCommand.cs:103`: `var targetDir = Path.Combine(stringsDir, targetLocale)` where `targetLocale` is split from CLI `--target` arg. The developer is trusted, but if a script piped a tainted locale string in (e.g. `../etc/passwd`), the CLI would `CreateDirectory` and write there. | Indirect (build automation feeding strings). | Arbitrary directory creation + `.resw` write. | Very low (CLI-arg trust). | None. | Reject locale strings that aren't `xx`/`xx-YY`-shaped. (Same shape check the generator already does — `LocSourceGenerator.cs:236`.) |
| T13 | Tampering / EoP via build | `KeyNamer` always produces PascalCase keys via `GenerateHintFromValue` which strips non-word characters. So **the extract path itself cannot inject** through the key. | — | — | — | `KeyNamer.cs:139` `Regex.Replace(cleaned, @"[^\w\s]", "")` and `KeyNamer.cs:155` `if (!char.IsLetter(result[0])) result = "Key" + result`. | Mitigated. The injection threat lives at hand-edited `.resw` step (T3 → Chunk 08). |
| T14 | Repudiation | None of the writes log. `[NEW]` / `[SKIP]` lines on stdout are best-effort. | n/a | — | — | — | No action. CLI is interactive; developer sees output. |
| T15 | Spoofing | None — no auth surface in this chunk. | n/a | — | — | — | — |
| T16 | EoP (host) | `SourceRewriter.cs` reads, modifies, and writes `.cs` files. The Roslyn output it produces (`t.Message(Loc.X.Y)`) is template-built but `Loc.X.Y` is concatenated from `entry.ReswFileName` and `entry.Key`, both of which originate from `KeyNamer` (sanitized). So the rewrite cannot inject arbitrary C# into the developer's source via the rewriter itself. | — | — | — | KeyNamer sanitization. | Acceptable. **However**: if a future change makes the rewriter pull `Key`/`ReswFileName` from a `.resw` instead of from the scanner output, the same injection issue as F3 reappears in source-tree form. Add a comment-anchored invariant in `SourceRewriter.BuildReplacement`. |
| T17 | Tampering | `XDocument.Save(filePath)` followed by another invocation that opens it: write-write race / concurrent invocation. Not adversary-driven; just correctness. | n/a | — | — | None. | Out of scope for security. |

---

## 6. Findings

### F1 — `XDocument.Load` not pinned to safe `XmlReaderSettings` *(Defense-in-depth, Low)*

**Locations:**
- `src/Reactor.Cli/Loc/ReswReader.cs:99`
- `src/Reactor.Cli/Loc/ReswWriter.cs:28`
- `src/Reactor.Cli/Loc/ReswWriter.cs:75`
- `src/Reactor.Cli/Loc/TranslateCommand.cs:230`
- `src/Reactor.Cli/Loc/PruneCommand.cs:183`

All five call sites pass a path string directly to `XDocument.Load`. In .NET 9, the `Load(string)` overload internally constructs an `XmlReader` whose default settings prohibit DTDs and have a null `XmlResolver`, so XXE / external DTD / billion-laughs are **not currently exploitable**. However:

1. The setting is implicit and depends on the BCL version. Future Reactor work might switch to `XDocument.Load(stream)` or `XDocument.Load(reader)` with custom settings; if a maintainer does that without re-applying hardening, we silently regress.
2. `XmlException` is the only exception caught (`ReswReader.cs:117`); other XML-related exceptions (`InvalidOperationException`, `IOException`) escape and crash the CLI.

**Recommendation:** Centralize a single helper:
```csharp
internal static XDocument LoadReswSafely(string path) {
    using var reader = XmlReader.Create(path, new XmlReaderSettings {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 16 * 1024 * 1024,
        IgnoreComments = false, // we read comments
    });
    return XDocument.Load(reader);
}
```
Wire all five call sites through it. This makes the policy explicit and reviewable.

### F2 — Recursive directory enumeration follows symlinks / junctions / reparse points *(High)*

**Locations:**
- `src/Reactor.Cli/Loc/ExtractCommand.cs:52` — `Directory.GetFiles(sourcePath, "*.cs", SearchOption.AllDirectories)`
- `src/Reactor.Cli/Loc/PruneCommand.cs:133` — same call against `sourceDir`
- `src/Reactor.Cli/Loc/SourceRewriter.cs:28` — `File.ReadAllText(filePath)` and `:55` `File.WriteAllText(filePath, …)` where `filePath` came from F2's enumeration

`Directory.GetFiles` with `SearchOption.AllDirectories` recurses into reparse points by default on Windows. A hostile repo can ship a junction (`mklink /J`) or a symlink (`mklink /D`) that points outside the repo. Consequences:

- On `extract` / `prune` (read-only): the scanner reads `.cs` files outside the supposed scan root. Their content gets surfaced in stdout (`[NEW] Foo.Bar = "<contents>"`) and ends up in `.resw` `<value>` elements written to the developer's working tree. Information disclosure of arbitrary readable files.
- On `extract --rewrite`: `SourceRewriter.cs:55` writes through that junction, modifying files outside the repo under the developer's privileges. This is the realistic primitive: a hostile repo's README says "run `mur loc extract --rewrite` to test localization," and the rewrite clobbers files in `~/Documents` or in a sibling repo.

**Recommendation:**
```csharp
var enumOpts = new EnumerationOptions {
    RecurseSubdirectories = true,
    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
};
var csFiles = Directory.GetFiles(sourcePath, "*.cs", enumOpts);
```
And in `SourceRewriter`, before writing, verify:
```csharp
var canonical = Path.GetFullPath(filePath);
var rootCanonical = Path.GetFullPath(sourceRoot);
if (!canonical.StartsWith(rootCanonical + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new SecurityException($"Refusing to write outside source root: {canonical}");
```
The scanner doesn't currently know the source root; thread it through `LocalizableStringScanner.Scan` → `KeyedLocString.Source.FilePath` → `SourceRewriter.Rewrite`.

### F3 — Cross-chunk: `.resw` key tampering → C# string-literal injection *(High; lives in Chunk 08)*

`src/Reactor.Localization.Generator/LocSourceGenerator.cs:164`:
```csharp
sb.AppendLine($"{indent}public static readonly MessageKey {SanitizeIdentifier(entry.Key)} = new(\"{ns}\", \"{entry.Key}\");");
```
`SanitizeIdentifier` is applied to the *identifier* slot, but the *string-literal* slot uses the raw `entry.Key`. A `.resw` with `name="abc\"); System.Diagnostics.Process.Start(\"calc\"); //"` produces emitted C# that breaks out of the literal context. This is the EoP-via-build threat the chunking doc flagged.

The CLI side is **not** the source of this — `KeyNamer` only ever generates PascalCase keys. The exposure is "translator hand-edits `.resw` and adds a malicious key." Fix lives in `LocSourceGenerator.cs`: escape `"` and `\` in the `entry.Key` interpolation, and reject any key that doesn't match `^[A-Za-z_][A-Za-z0-9_.-]*$` so the only way to put pathological characters into `entry.Key` is to bypass extraction entirely.

**This finding is filed here for traceability; the fix belongs in Chunk 08's threat model.**

### F4 — No containment on `--source` / `--output` / `--resources` paths *(Medium)*

**Locations:**
- `ExtractCommand.cs:43-45` — `sourcePath ??= "."; outputPath ??= Path.Combine("Strings", "en-US");`
- `TranslateCommand.cs:48` — `sourcePath ??= Path.Combine("Strings", "en-US");`
- `TranslateCommand.cs:103` — `targetDir = Path.Combine(stringsDir, targetLocale)` (no locale shape check; see T12)
- `ValidateCommand.cs:35`, `StatusCommand.cs:34`, `PruneCommand.cs:46-47` — defaults

Every CLI accepts an absolute path or a relative path containing `..`. Combined with F2 (symlink following), a developer running `mur loc extract --source SuspiciousRepo --rewrite` against a freshly cloned repo opens the symlink-traversal hole. There is no `--repo-root` rail and no validation that the supplied path is under the developer's intended workspace.

**Recommendation:** Add lightweight validation:
- Reject paths containing `..` segments after `Path.GetFullPath` normalization, *or*
- Add a `--repo-root` flag (default `.`), and reject any `--source` / `--output` whose canonical form is not a child of `--repo-root`. This is the cheap, explicit version of F2's containment check.

### F5 — Locale string from `--target` is not validated before use as a directory name *(Low)*

`src/Reactor.Cli/Loc/TranslateCommand.cs:62,103`:
```csharp
var targets = targetLocales.Split(',', …);
…
var targetDir = Path.Combine(stringsDir, targetLocale);
Directory.CreateDirectory(targetDir);
```
`targetLocale` is interpolated directly into a path. A caller passing `--target ../../etc` would create `etc/` two levels above `stringsDir`. Developer-trust mitigates the immediate threat, but the generator already enforces a shape check (`LocSourceGenerator.cs:236`: `locale.Length < 2 || locale.Length > 10`) and the CLI should mirror that.

**Recommendation:** Validate each target against `^[a-z]{2,3}(-[A-Z][A-Za-z0-9]+)*$` (or at minimum disallow `/`, `\`, `..`, and absolute paths) before `Path.Combine`.

### F6 — `XmlException` is caught silently in `ReswWriter.LoadExisting`; `IOException` is not caught at all *(Low)*

`src/Reactor.Cli/Loc/ReswWriter.cs:42-46`: `LoadExisting` swallows `XmlException` with no logging. A malformed pre-existing `.resw` therefore appears empty to the extractor, which then re-extracts every key, producing duplicate `<data>` entries in the existing file (because the writer adds, doesn't replace).

**Recommendation:** Log the path and the exception message to stderr at minimum; or refuse to write if any pre-existing `.resw` failed to parse, so the developer can fix it manually before extraction silently double-writes.

### F7 — `Console.Error.WriteLine($"[WARN] Failed to parse {file}: {ex.Message}")` may leak host paths *(Informational)*

`src/Reactor.Cli/Loc/ExtractCommand.cs:73`: prints whatever path `Directory.GetFiles` returned in the warning message. If F2 is exploited, this is the channel by which the absolute path of a symlink-resolved file outside the repo is revealed to whoever sees CLI output (e.g. CI logs uploaded to a hostile build artifact).

**Recommendation:** Print only the path *as supplied by the developer's source root*, never the absolute resolved path.

### F8 — `SourceRewriter` reparses each modified file twice with two slightly different parse modes *(Correctness, not security)*

`SourceRewriter.cs:29` parses the original source; `:103` reparses the post-replacement string to find Render() methods. If string replacements introduce a Roslyn-illegal token (they shouldn't, given KeyNamer sanitization), the second parse fails and the file is then `WriteAllText`-ed in a partially-rewritten state. Recommend: verify `tree.GetDiagnostics()` post-rewrite and refuse to write if any new errors appeared. (Not a security finding; flagged for code-review handoff.)

---

## 7. Open questions

- **OQ-1 — Symlink behavior in real-world repos.** Does Reactor's CI build matrix encounter junctions in any sample / submodule? If yes, F2's `AttributesToSkip = FileAttributes.ReparsePoint` could break legitimate use (e.g. workspace-wide symlinks). Validate with the build team before landing F2's fix.
- **OQ-2 — Does the developer's typical workflow ever invoke `mur loc extract --rewrite` on a freshly-cloned repo before reading code?** If yes, social-engineering the rewrite is realistic and F2 / F4 are urgent. If extract is always run after the developer has accepted the repo's code, the threat is dampened.
- **OQ-3 — Is `mur loc` ever invoked from CI?** If yes, the .resw → C# injection (F3 / Chunk 08) becomes a CI-RCE primitive on every PR. Confirm with release-pipeline review.
- **OQ-4 — Translator trust.** Is the `.resw` review process actually a human-in-the-loop step, or are translation-provider outputs auto-merged? `TranslateCommand.cs` writes `comment="ai-translated: pending-review"`, suggesting human review is expected, but nothing enforces that the comment is consumed before the file is built.

---

## 8. Out-of-scope referrals

- **F3 (key injection into emitted source)** — fix in **Chunk 08**, `LocSourceGenerator.cs:164`. Recommendation: escape `"` / `\` and validate `entry.Key` against `^[A-Za-z_][A-Za-z0-9_.-]*$` *in the generator*, even though `KeyNamer` already does so on the extract path. Belt-and-suspenders because hand-edited `.resw` bypasses extraction entirely.
- **Translation provider response handling** (untrusted LLM output written into `<value>` elements at `TranslateCommand.cs:264`) — covered by **Chunk 07**. The `.resw` writer in this chunk does not validate that translated text is well-formed XML-text-content (no NUL bytes, no control chars, no embedded `]]>` that might break XML serialization on round-trip). XDocument's serializer will refuse some of these, but failure mode is "exception during `doc.Save`" rather than "validate-then-reject," so the developer sees a stack trace instead of a clear "translator returned XML-illegal text" message. Defer.
- **Loopback / devtools trust assumption** — irrelevant to this chunk; no network surface here.
- **`Reactor.Cli/Devtools` / `Reactor.Cli/Docs`** — separate chunks.
