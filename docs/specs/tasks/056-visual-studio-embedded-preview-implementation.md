# Visual Studio Embedded Preview — Implementation Tasks

Derived from: [`docs/specs/056-visual-studio-embedded-preview.md`](../056-visual-studio-embedded-preview.md).

> **Status:** Not started. Spec is in Draft. Phase 0 spike (Appendix C) has
> already been performed in a throwaway prototype outside the repo and
> validated cross-process WinUI 3 HWND reparenting end-to-end on
> Windows 11 ARM64 (x64 emulation). This tracker covers Phase 0b
> (residual matrix vs. a real VSIX), Phase 1 (ship the MVP), and Phase 2
> (polish). Phase 3 stretch items are listed at the bottom but not
> decomposed.
>
> **How to use this tracker:** Every actionable item is a checkbox. Mark
> `[x]` only when the artifact (code + tests + doc update, where
> applicable) is landed and green. The work pauses/resumes cleanly at
> any checkbox boundary — always finish a phase's **exit gate** before
> calling that phase done. Phase 0b validates the load-bearing
> assumption (real VSIX hosting, not a WinForms placeholder) and must
> precede Phase 1 coding of the HwndHost subclass.
>
> **Conventions** (mirroring `054-windowing-evolution-implementation.md`,
> `051-devtools-trimmability-and-isolation-implementation.md`):
> - Order matters. Phase 0b de-risks the residual matrix. Phase 1 lands
>   Reactor `--embed` plumbing **before** the VSIX so the VSIX can be
>   developed against a working child. Phase 2 polishes; Phase 3 is
>   stretch.
> - Each phase keeps the Reactor triple gate green:
>   `dotnet build Reactor.slnx -p:Platform=x64` →
>   `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build` →
>   `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`.
> - **The VSIX is .NET Framework 4.7.2.** It cannot reference Reactor
>   core projects. The only contract is the HTTP API on
>   `PreviewCaptureServer` + the `CAPTURE_PORT=`/`CAPTURE_TOKEN=` stdout
>   lines + the new `/hwnd` + `/embed/*` endpoints + the
>   `{ "protocol": "embed-v1" }` field on `/status`. Treat that as a
>   versioned wire protocol — see Task 1.0.2.
> - **New `PreviewCaptureServer` endpoints inherit the existing security
>   envelope.** Bearer auth, loopback-only listener, Host header
>   allowlist, `Content-Type: application/json`, 4 KB body cap. Tests
>   under `tests/Reactor.Tests/Devtools/` already cover the envelope;
>   add new endpoint-specific tests there.
> - **No XAML editing for the VSIX UI logic.** WPF `UserControl` XAML
>   is unavoidable for chrome layout, but every line of state, command
>   handling, and IPC lives in code-behind/ViewModel/helper classes
>   that are unit-testable headlessly (Tier A in §13 of the spec).
> - **VS extension stability rule.** The VSIX runs in-proc inside
>   `devenv.exe`. A null-ref, unhandled async exception, or P/Invoke
>   misuse crashes VS. Every async entry point wraps in
>   `JoinableTaskFactory.RunAsync`, catches `Exception` at the
>   boundary, and logs to the "Reactor Preview" output channel.
> - **Two-place selftest registration** is mandatory for any new
>   selftest fixture (rule from
>   `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`):
>   add the fixture name to the string list **and** to the
>   `name → ctor` switch arm. Most of this spec's tests are not
>   selftest-shaped, but `Reactor.Tests` integration tests for the new
>   endpoints are.
> - **Docs are generated.** Read `docs/_pipeline/ai-author-skill.md`
>   first. Never hand-edit `docs/guide/*`. The user-facing doc for this
>   feature goes under `docs/_pipeline/templates/vs-extension.md.dt`
>   (new) plus an optional doc app under
>   `docs/_pipeline/apps/vs-extension/` if we want screenshots.
> - **Build the VSIX in CI but do NOT run E2E (Apex/WinAppDriver) in
>   CI by default.** Tier C.2 is brittle and optional; Tier A and Tier B
>   are the CI gate.

---

## Spec citations cross-checked against current tree

- `src/Reactor/Hosting/Devtools/DevtoolsCliParser.cs` exists with
  `DevtoolsCliOptions` record. ✅ We extend it with `EmbedMode`,
  `EmbedStyle`, `EmbedHostPid` (spec §5.1).
- `src/Reactor.Devtools/PreviewCaptureServer.cs` exists; `GetComponents`,
  `GetCurrentComponent`, `SwitchComponent` callback hooks at lines
  ~52–58. ✅ We add `GetHwnd`, `AckEmbed`, `ResizeEmbed`, `MoveEmbed`,
  `ReleaseEmbed` next to those.
- `src/Reactor.Devtools/DevtoolsHost.cs` is where the server is wired
  (`RunWithDevtools`). ✅ We register the new callbacks alongside the
  existing `server.SwitchComponent` registration.
- `src/Reactor/Hosting/ReactorWindow.cs`,
  `src/Reactor/Hosting/WindowSpec.cs` — embed mode adds an
  `EmbedRequest` knob and gates `AppWindow.Show()`/presenter/titlebar.
  ✅ Spec §5.2.
- `src/Reactor/Hosting/HotReloadService.cs` — untouched. ✅
- `src/vscode-reactor/src/extension.ts` — source of truth for the
  port/token sniffer, `findAllComponentClasses` regex, dotnet
  resolver. We port these 1:1 into C# in the VSIX.
- Line numbers in the spec are approximate against current `main`;
  re-grep before editing.

---

## Overall exit gate (all must hold to declare 056 Phase 1 done)

1. `--devtools run --embed --embed-host-pid <pid>` launches a
   Reactor app whose top-level window is hidden, `WS_CHILD`-styled,
   and exposes its HWND via `GET /hwnd` (spec §5.1, §5.2).
2. `POST /embed/ack { parent, w, h, generation }` reparents the
   window cross-process and shows it; `POST /embed/resize` resizes;
   `POST /embed/release` detaches and exits cleanly within 1s (spec §5.3).
3. `/status` returns `{ "protocol": "embed-v1", "generation": int, ... }`;
   the existing fields are unchanged (spec §5.3, §16 Q4).
4. New endpoints inherit the existing security envelope: bearer auth,
   loopback only, `Host: 127.0.0.1:<port>` enforced, `Content-Type:
   application/json` required for writes, 4 KB body cap, 409 on
   generation mismatch (spec §5.3, §10).
5. `--embed` without `--embed-host-pid` is a parser error; `--embed`
   without `--devtools run` is rejected (spec §5.1).
6. **Embed mode disables the screenshot timer.** `PreviewCaptureServer`
   does not capture or serve `/frame` when `EmbedMode = true` (spec §5.1,
   §2 non-goals).
7. `src/vs-reactor/` builds a `Reactor.VsExtension.vsix` as part of
   `Reactor.slnx`. The VSIX loads in the Experimental Hive without
   ActivityLog errors (spec §6.1, §12).
8. The VSIX tool window opens, the embed handshake succeeds against a
   live Reactor target on a sample project, mouse/keyboard input
   routes correctly, the dropdown auto-tracks the active editor, and
   force-reload restarts the child within ~5s while preserving the
   placeholder HWND (spec §6.7, §7, §8.1).
9. L1 hot reload (Render-body edit → save → in-place refresh) works
   in the embedded surface without the extension doing anything
   (spec §5.5, §8.2). L2 (rude edit → `dotnet watch` respawn) is
   detected via a new `CAPTURE_TOKEN=` and re-handshakes against the
   same placeholder HWND (spec §5.5, §8.3).
10. VS crash / force-kill cleans up the entire `dotnet watch` + child
    tree via the Windows Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`)
    within ≤1s; the `--embed-host-pid` watchdog is a verified secondary
    (spec §4.2 Risk 5, §6.5).
11. DPI mismatch refuses embed (HTTP 412 on `/embed/ack`) and the VSIX
    falls back to `--embed-mode owner` with a user-visible message
    (spec §4.2 Risk 2).
12. Elevation mismatch (VS elevated vs child non-elevated, or vice
    versa) is detected pre-launch and surfaced as an actionable error
    (spec §16 Q12).
13. Tier A unit tests (≥40 tests, ~80% coverage of pure logic) +
    Tier B SDK tests (≥10 tests) green in CI on a `windows-latest`
    runner (spec §13.1, §13.2).
14. `src/vs-reactor/TESTING.md` contains the Tier C.1 manual smoke
    checklist; release pipeline references it (spec §13.3).
15. `src/vs-reactor/README.md` documents install, debug
    (`/RootSuffix Exp`), the `Tools → Options → Reactor Preview`
    settings (if any), and troubleshooting (DPI / elevation / WinAppSDK
    version) (spec §6, §11, §12).
16. `.github/workflows/release.yml` ships `Reactor.VsExtension.vsix`
    as a release asset; signing handled by the existing pipeline
    (spec §11.3, §14.3).
17. Full xunit + selftest + solution build green on Phase 1 exit.

---

## Phase 0 — Spike (status: COMPLETE, no remaining tasks)

See spec §15 Phase 0 (Appendix C). Initial spike done 2026-06 outside
the repo (`C:\Users\andersonch\Code\ReactorDemo\embed-spike\`); proved
Mitigation A is sufficient for the baseline interactive embed.
**Nothing more to do here — this section is for context only.**

The spike is **not** merged. The eventual `src/vs-reactor/` and the
Reactor `--embed` flag are implemented from scratch per the spec; the
spike artifact is a documentation reference for the Win32 dance.

---

## Phase 0b — Residual matrix against a real VSIX (spec §15 Phase 0b, ~2–3 days)

The 2026-06 spike used a WinForms placeholder. WPF's airspace and
HwndHost dock-state machine differ from WinForms; the residual matrix
must be re-run inside a real `ToolWindowPane` before Phase 1 commits.

This phase produces **throwaway artifacts**: a minimal "spike VSIX"
with only the `HwndHostPlaceholder` + the Reactor child wired up, no
chrome, no component dropdown. Its sole job is to retire the residual
risks; once green, the throwaway is discarded and Phase 1 builds the
real extension from a clean slate.

### 0b.1 Bootstrap the spike VSIX

- [ ] Create `src/vs-reactor-spike/` (gitignored, **not** part of
      `Reactor.slnx`). Hand-roll a minimal VSIX project on .NET
      Framework 4.7.2 with `Microsoft.VisualStudio.SDK` 17.8+ and
      `Microsoft.VSSDK.BuildTools` 17.8+. Single `ToolWindowPane`
      whose content is a `Grid` containing only a `HwndHostPlaceholder`.
- [ ] Hand-roll a minimal Reactor target app under `samples/apps/embed-spike/`
      (also gitignored or removed before Phase 1 PR) whose `Program.cs`
      reads `--embed-host-hwnd <hex>` from argv, applies Mitigation A
      via P/Invoke, and exits when `--embed-host-pid` dies. Mirror the
      throwaway from Appendix C but in-repo.
- [ ] Wire the spike VSIX to launch the spike target via
      `Process.Start("dotnet", "run --project samples/apps/embed-spike -- --embed-host-hwnd …")`,
      parse `CHILD_HWND=` from stdout, and pass back via stdin or argv.
      No HTTP yet — argv/stdout only, matching Appendix C.

### 0b.2 Run the residual matrix (spec §15 Phase 0b table)

Each row must be **manually verified** by a human on a developer rig
and recorded with a screenshot + pass/fail in the PR description
(this is the "human validation" gate for Phase 0b).

- [ ] **#3** Hover → tooltip on a Button appears on the embedded surface.
- [ ] **#6** Tab traversal between two `TextBox`es. **Expected to need
      A′'s `TabIntoCore`; if it fails before A′ is wired, record that
      result and queue the A′ implementation as a Phase 1 must-do.**
- [ ] **#7** Ctrl+A / Ctrl+C / Ctrl+V in a `TextBox` standard
      shortcuts. **Expected to need A′'s `TranslateAcceleratorCore`.**
- [ ] **#8** Escape closes a `ContentDialog`.
- [ ] **#9** IME (Pinyin or Japanese): composition window at caret,
      commits cleanly. (Requires IME installed on the test machine; if
      unavailable in the test rig, **note as deferred to Phase 1
      manual smoke**, do not block Phase 0b on it.)
- [ ] **#11** `MenuFlyout` from right-click on a Button: appears,
      dismisses.
- [ ] **#12** DPI mixed-monitor: float the tool window, drag from a
      100% monitor to a 200% monitor; content rescales without blur or
      coordinate drift.
- [ ] **#13** Full VS dock cycle: dock → float → re-dock → tab with
      Output window → auto-hide → restore. Placeholder must remain
      stable across all transitions.
- [ ] **#16** Force reload × 20 in rapid succession: Spy++ shows zero
      orphan placeholder HWNDs and zero leaked top-level Reactor
      windows after the 20th cycle.
- [ ] **#17** UIA tree: `inspect.exe` sees the WinUI tree under the
      VS tool window root; focus follows pointer/keyboard moves.
- [ ] **#18** Force-kill `devenv.exe`: `dotnet watch` + child
      terminate within 1s. (Job Object is wired here even though the
      rest of the spike is HTTP-less, because this risk is
      cleanup-shaped, not protocol-shaped.)
- [ ] **#19** Elevation mismatch: run VS elevated and child
      non-elevated (and vice versa). Document the result — likely
      "input silently dropped" — and decide whether to block (refuse)
      or warn in Phase 1.
- [ ] **#20** ARM64-native: requires Reactor to ship an arm64 nupkg.
      If the local feed is x64-only, **defer #20 to Phase 1 release
      checklist** and document the gap.

### 0b.3 A′ scope decision (spec §4.2 Mitigation A′)

- [ ] After running the matrix, write a one-page A′ scope memo into
      the Phase 0b PR description listing which A′ items are
      **required** (i.e., something failed without them) vs.
      **belt-and-braces** (i.e., not strictly required but standard
      Win32 hygiene). This decides the size of the HwndHost subclass
      in Task 1.4.

### 0b.4 Q11 — remote ContentIsland viability (spec §16 Q11)

- [ ] One engineer-day spike: can `DesktopChildSiteBridge.AcceptRemoteEndpoint`
      / `ContentIsland.ConnectRemoteEndpoint` be reached from a .NET
      Framework 4.7.2 in-proc VSIX talking to a .NET 10 WinUI child?
      **Pass criterion:** either a working prototype that survives
      mouse+keyboard, OR a documented "not viable in current
      WinAppSDK" with the specific API/runtime mismatch named. If a
      working prototype exists, **STOP** — the rest of this tracker
      collapses to "wire ContentIsland into the VSIX and ship", and
      the §4.1/§4.2 Win32 cascade becomes a fallback.
- [ ] If not viable, note the WinAppSDK version checked and revisit
      annually (Phase 3 task placeholder).

### 0b.5 Phase 0b exit gate

- [ ] Residual matrix has a pass/fail row for every entry in the
      spec §15 Phase 0b table, with screenshots in the PR description.
- [ ] A′ scope memo committed to the Phase 1 implementation PR
      description.
- [ ] Q11 (remote ContentIsland) resolved: either we pivot or we
      proceed with §4.1/§4.2.
- [ ] Spike VSIX + spike target deleted (or kept under
      `src/vs-reactor-spike/` but firmly gitignored so a fresh clone
      doesn't see them). **No spike code lands on `main`.**

---

## Phase 1 — MVP (spec §15 Phase 1)

Ships the spec end to end. Split into a Reactor-side sub-phase (1.0–1.2)
landed in one PR, then the VSIX sub-phase (1.3–1.8) landed in a follow-up
PR that can take a dependency on the published Reactor wire protocol.
Tier A/B test work happens alongside each sub-phase.

### 1.0 Reactor-side: protocol design and tests-first

Before any production code in Reactor, write the wire-protocol contract
as integration tests under `tests/Reactor.Tests/Devtools/`. These tests
boot a `PreviewCaptureServer` headlessly, then exercise the new
endpoints with `HttpClient`. They must fail until 1.1/1.2 land.

#### 1.0.1 Wire-protocol versioning (spec §16 Q4)

- [ ] Add a `protocol` string field to the `/status` response.
      Constant value: `"embed-v1"`. Also add `generation` (int,
      starts at 1). Round-trip the field through
      `PreviewJsonContext`. Existing `/status` fields unchanged.
- [ ] Integration test: `Status_ReportsEmbedV1Protocol`.
- [ ] Document in `src/Reactor.Devtools/README.md` (and the
      `PreviewCaptureServer.cs` class comment) that **any breaking
      change to the embed endpoints bumps the suffix** (`embed-v2`)
      and the extension is expected to reject unknown protocols.

#### 1.0.2 Failing-first integration tests for new endpoints

- [ ] `Hwnd_RequiresBearerToken`
- [ ] `Hwnd_RejectsExternalHostHeader`
- [ ] `Hwnd_ReturnsZeroBeforeWindowReady` (HWND is `0` until the
      Devtools host publishes one)
- [ ] `Hwnd_ReturnsHexEncodedHandle` (post-publish)
- [ ] `Hwnd_IncludesGeneration`
- [ ] `EmbedAck_RequiresPost`
- [ ] `EmbedAck_RequiresJsonContentType`
- [ ] `EmbedAck_RejectsBodyOver4Kb`
- [ ] `EmbedAck_RejectsMissingParent`
- [ ] `EmbedAck_RejectsGenerationMismatch_409`
- [ ] `EmbedAck_InvokesCallbackOnce`
- [ ] `EmbedResize_CoalescesRapidCalls` (last-write-wins per
      dispatcher pass)
- [ ] `EmbedMove_OwnerModeOnly_Returns400InChildMode`
- [ ] `EmbedRelease_InvokesCallbackAndAcks`
- [ ] `EmbedEndpoints_RejectInNonEmbedSession` (when no `--embed`
      was passed, the endpoints return 404 — they don't exist
      outside embed mode)

All tests live in `tests/Reactor.Tests/Devtools/PreviewCaptureServer_EmbedTests.cs`.
Run via `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build
--filter "FullyQualifiedName~PreviewCaptureServer_EmbedTests"`.

### 1.1 Reactor-side: `--embed` CLI plumbing (spec §5.1)

- [ ] In `src/Reactor/Hosting/Devtools/DevtoolsCliParser.cs`, extend
      `DevtoolsCliOptions` with `EmbedMode bool`, `EmbedStyle
      EmbedMode` (new enum: `Child`, `Owner`), and `EmbedHostPid int?`.
- [ ] Add an `EmbedMode` enum next to the record. Default value
      `EmbedMode.Child`.
- [ ] Update `DevtoolsCliParser.Parse` to recognize `--embed`,
      `--embed-mode {child|owner}`, `--embed-host-pid <pid>`.
- [ ] Validation rules (must be enforced at parse time, fail with
      a clear stderr message):
  - `--embed` requires `--devtools run` (the only subverb the
    embed shape makes sense under).
  - `--embed` requires `--embed-host-pid`. Without it, refuse to
    embed (we won't strand the process if VS dies).
  - `--embed` implies `--vscode` (the `PreviewCaptureServer` must
    be up). If `--vscode` is not also passed, **set it
    automatically** and emit a one-line stderr informational
    line; do not error.
- [ ] Add `DevtoolsCliParserTests.Embed_*` xunit cases in
      `tests/Reactor.Tests/`: required pairing, --embed-mode parse,
      pid parse, error messages.

### 1.2 Reactor-side: embed runtime (spec §5.2–§5.5)

#### 1.2.1 `WindowSpec.EmbedRequest`

- [ ] Add a nullable `EmbedRequest? Embed` knob to `WindowSpec`
      (in `src/Reactor/Hosting/WindowSpec.cs`). Record:
      `EmbedRequest(EmbedMode Style, int HostPid, bool InitialVisibility)`.
- [ ] `EmbedRequest.Style == Child` → window is `WS_CHILD`, hidden
      until ack. `Owner` → window stays top-level with
      `GWLP_HWNDPARENT` set on ack.

#### 1.2.2 Style flip on window creation (spec §5.2)

- [ ] In `src/Reactor/Hosting/ReactorWindow.cs`, gate
      `AppWindow.Show()` on `Spec.Embed is null || Spec.Embed.InitialVisibility`.
- [ ] Apply the style flip from spec §5.2 (`GetWindowLong` /
      `SetWindowLong` for GWL_STYLE and GWL_EXSTYLE) immediately
      after the window is constructed but **before** `AppWindow.Show()`
      would have been called.
- [ ] Gate presenter, backdrop, titlebar configuration on
      `!Spec.Embed.HasValue || Spec.Embed.Value.Style == Owner`.
      Some of those paths throw or no-op for child windows; the gate
      keeps the existing top-level path untouched.
- [ ] DPI awareness self-check: verify
      `GetProcessDpiAwarenessContext() == DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`.
      If not, print an actionable stderr message ("add
      `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`
      to your csproj") and `Environment.Exit(2)`. **No partial-success
      path here** — the DPI prereq is hard.
- [ ] Spawn the host-PID watchdog: open a process handle for
      `Spec.Embed.HostPid` and `WaitForSingleObject` on a background
      thread. On signal, call `SetParent(myHwnd, IntPtr.Zero)` then
      `Environment.Exit(0)`. (Belt-and-braces; the VSIX's Job Object
      is the primary mechanism — spec §4.2 Risk 5.)

#### 1.2.3 New `PreviewCaptureServer` endpoints (spec §5.3)

- [ ] In `src/Reactor.Devtools/PreviewCaptureServer.cs`, add
      callback hooks next to `SwitchComponent`:
  - `public Func<(IntPtr Hwnd, int Generation)>? GetHwnd { get; set; }`
  - `public Func<IntPtr, int, int, int, bool>? AckEmbed { get; set; }` — args `(parent, w, h, generation)`, returns `true` on success / `false` on generation mismatch (server translates to 409).
  - `public Action<int, int>? ResizeEmbed { get; set; }`
  - `public Action<int, int>? MoveEmbed { get; set; }` (owner mode only)
  - `public Action? ReleaseEmbed { get; set; }`
- [ ] Implement the route handlers next to `ServeFrame` /
      `HandleSwitchComponent`. Each write endpoint:
  - requires `POST`
  - requires `Content-Type: application/json`
  - rejects bodies > 4 KB
  - on success → 200 `{ "ok": true }`
  - on failure → 4xx `{ "ok": false, "error": "..." }`
  - on generation mismatch → 409 `{ "ok": false, "error": "generation-mismatch", "expected": <int>, "got": <int> }`
- [ ] Suppress the screenshot capture timer / `/frame` endpoint
      when `EmbedMode = true` (the extension never asks for frames).
      Save it as a configuration flag on `PreviewCaptureServer` that
      `DevtoolsHost` flips when starting in embed mode.
- [ ] All five endpoints + `protocol` field generated through
      `PreviewJsonContext` (no reflection-mode JSON; AOT-safe).
- [ ] Wire requests through the existing security envelope: bearer
      token, Host header allowlist, loopback check, body cap, no
      Origin allowance changes.

#### 1.2.4 `DevtoolsHost` wiring (spec §5.4)

- [ ] In `DevtoolsHost.RunWithDevtools`, when
      `options.EmbedMode == true`, additionally register the new
      callbacks against the same `PreviewCaptureServer` instance.
- [ ] Construct `WindowSpec` with `Embed = new EmbedRequest(
      options.EmbedStyle, options.EmbedHostPid.Value,
      InitialVisibility: false)`.
- [ ] `_embedGeneration` field on `DevtoolsHost`, initialized to 1.
      For Phase 1 it never changes within a process; respawn changes
      it implicitly (new process = new generation from the extension's
      perspective).
- [ ] `ApplyEmbedAck(parent, w, h, gen)`: verify gen matches; verify
      DPI awareness of `parent` via
      `GetWindowDpiAwarenessContext(parent)` compared against the
      child's window via `AreDpiAwarenessContextsEqual`; on mismatch
      return false (translates to 412 — see spec §4.2 Risk 2.2,
      preserving Phase 1 exit gate item 11). On success: dispatch to
      the UI thread, `SetParent`, `SetWindowPos`, `ShowWindow(SW_SHOW)`,
      `SetFocus(reactorHwnd)`.
- [ ] `ApplyEmbedResize(w, h)`: dispatch-coalesce so multiple resize
      calls within a single render pass collapse to the last value.
- [ ] `ApplyEmbedMove(x, y)`: owner-mode only; ignored / 400 in
      child mode.
- [ ] `ApplyEmbedRelease()`: `SetParent(myHwnd, IntPtr.Zero)`,
      `Window.Close()` (so existing shutdown handlers — telemetry,
      persistence — run), then `Environment.Exit(0)` within 1s via a
      timeout task to guarantee the contract.

#### 1.2.5 Integration tests (spec §13.1, mirror of 1.0.2 now made
green)

- [ ] All Task 1.0.2 tests pass.
- [ ] Additional `DevtoolsHost_EmbedTests`:
  - `EmbedHost_HidesWindowUntilAck`
  - `EmbedHost_AppliesStyleFlip`
  - `EmbedHost_DpiMismatch_Refuses412`
  - `EmbedHost_GenerationMonotonicWithinProcess` (sanity)
  - `EmbedHost_HostPidWatchdog_ExitsOnSignal` (use a dummy
    process; assert the child exits within 2s of its termination)

#### 1.2.6 Owner-mode (Mitigation C, spec §4.2)

- [ ] When `EmbedStyle == Owner`, skip the WS_CHILD flip; instead
      set `GWLP_HWNDPARENT` to the parent HWND on ack. Window stays
      top-level (`WS_OVERLAPPEDWINDOW`); minimizes/follows VS.
- [ ] `ApplyEmbedMove` becomes the position-tracking path —
      extension pushes screen rect changes here.
- [ ] Selftest: `Embed_OwnerMode_Reparent_*` (visible only locally;
      headless selftest can't verify "follows VS" — instead verify
      `GetAncestor(hwnd, GA_PARENT)` returns the expected handle and
      the window stays top-level).

### 1.3 VS extension: project scaffolding (spec §6.1)

- [ ] Create `src/vs-reactor/Reactor.VsExtension/` with:
  - `Reactor.VsExtension.csproj` (.NET Framework 4.7.2, SDK style,
    `<UseWPF>true</UseWPF>`, `Microsoft.VisualStudio.SDK` 17.8+
    PackageReference, `Microsoft.VSSDK.BuildTools` 17.8+,
    `<GeneratePkgDefFile>true</GeneratePkgDefFile>`, VSIX
    properties).
  - `source.extension.vsixmanifest` declaring the Reactor publisher,
    name, version, dependencies (VS 2022 17.8+), and the
    `extension.vsixmanifest` Product/Asset entries.
  - Standard VS SDK template files: `Properties/AssemblyInfo.cs`,
    `Resources.resx`, `VSPackage.resx`, `index.html` if needed.
- [ ] Add `src/vs-reactor/Reactor.VsExtension/Reactor.VsExtension.csproj`
      to `Reactor.slnx`. The other Reactor projects don't reference
      it and vice versa.
- [ ] Set `<TargetFramework>net472</TargetFramework>`. **Suppress
      AOT-related warnings**: the VSIX is not AOT-publishable and its
      `IsAotCompatible` is irrelevant.
- [ ] Sanity build: `dotnet build src/vs-reactor/Reactor.VsExtension/`
      produces a `.vsix` in `bin/Debug/`. F5 launches Exp hive.

### 1.4 VS extension: `HwndHostPlaceholder` + WPF chrome (spec §6.3, §6.4)

#### 1.4.1 Placeholder window class

- [ ] `UI/HwndHostPlaceholder.cs` — `HwndHost` subclass per spec §6.3:
      `BuildWindowCore` creates a Win32 placeholder child window of a
      pre-registered class (`ReactorEmbedPlaceholder`),
      `DestroyWindowCore` destroys it, `OnWindowPositionChanged`
      raises `PlaceholderResized`.
- [ ] `UI/PlaceholderClass.cs` — process-global `WNDCLASSEX`
      registered once on package load. WndProc is `DefWindowProcW`
      except for `WM_ERASEBKGND` (return 1 to suppress flicker).
- [ ] **A′ from Phase 0b — only the items the residual matrix
      proved necessary** (spec §4.2 Mitigation A′):
  - `TranslateAcceleratorCore`, `TranslateCharCore`, `TabIntoCore`
    overrides (if Phase 0b matrix #6/#7 required them — almost
    certainly yes).
  - `WM_MOUSEACTIVATE` → `SetFocus(child)` forward.
  - `WM_CHANGEUISTATE` / `WM_UPDATEUISTATE` forwarder if matrix
    showed focus-rectangle issues.
  - `WM_MOUSEWHEEL` forward to focused descendant.
  - `WM_DPICHANGED` forward to the embedded child (settles spec §16 Q2).
- [ ] Unit tests (Tier A): `HwndHostPlaceholder_RegistersClassOnce`,
      `HwndHostPlaceholder_RaisesResized_OnPositionChange`. (The
      window-creation paths require real WPF dispatcher → these may
      need to be xunit-WPF-style with `[STAThread]`.)

#### 1.4.2 WPF UserControl chrome

- [ ] `UI/ReactorEmbedControl.xaml` per spec §6.4: 3-row grid
      (chrome / preview / footer), ComboBox + reload button +
      status TextBlock, error overlay over the preview row,
      building-status footer.
- [ ] `UI/ReactorEmbedControl.xaml.cs` — code-behind wires
      `Placeholder.PlaceholderResized` → `EmbedClient.PostResize`.
      On `Loaded`: if no preview is running, fire
      `PreviewActiveFileCommand`. On `Unloaded`: `EmbedClient.Release()`
      then `Launcher.Dispose()` (Job Object close).
- [ ] `UI/ReactorEmbedControlViewModel.cs` — bindable VM with
      `Components`, `SelectedComponent`, `StatusText`, `StatusBrush`,
      `ErrorOverlayVisible`, `ErrorTitle`, `ErrorDetail`,
      `BuildingVisible`, `ForceReloadCommand`. **All of these are
      headlessly unit-testable** (Tier A).
- [ ] Tier A unit tests for the VM:
  - `VM_SetComponents_PopulatesDropdown`
  - `VM_SelectComponent_PinsManually`
  - `VM_StatusTransitions_Idle_Launching_Embedded_Building_Respawning`
  - `VM_ForceReloadCommand_DisabledWhenIdle`
  - `VM_AutoTrack_DoesNotOverridePinUntilCleared`

### 1.5 VS extension: `EmbedClient` + `ReactorChildLauncher` (spec §6.5, §6.6)

#### 1.5.1 `EmbedClient`

- [x] `Embed/EmbedClient.cs` — thin `HttpClient` wrapper with bearer
      injection, `Host: 127.0.0.1:<port>` header, JSON post helpers
      (`GetHwndAsync`, `AckEmbedAsync`, `ResizeAsync`, `MoveAsync`,
      `ReleaseAsync`, `GetComponentsAsync`, `PreviewAsync`,
      `StatusAsync`).
- [x] One `EmbedClient` per session; `Dispose` cancels in-flight
      requests via `HttpClient.CancelPendingRequests`.
- [x] `protocol` field check on `StatusAsync`: if absent or not
      `"embed-v1"`, throw `EmbedProtocolMismatchException` for the
      VM to surface as "Reactor version mismatch — please update
      the Reactor package".
- [x] Round-trip `generation` on `AckEmbedAsync`; if server returns
      409, raise the `EmbedGenerationMismatch` event for the launcher
      to ignore (it means a new respawn is already in flight).
- [x] Tier A tests with a stubbed `HttpMessageHandler`:
  - [x] `Ack_SendsCorrectJson`
  - [x] `Ack_AddsBearerHeader`
  - [x] `Ack_SetsHostHeader`
  - [x] `Ack_GenerationMismatch_RaisesEvent`
  - [x] `Status_MissingProtocol_ThrowsMismatch`
  - [x] `Resize_PostsCorrectBody`
  - [x] `Release_PostsCorrectBody`
  - [x] `Components_DeserializesPayload`

#### 1.5.2 Job Object helper

- [x] `Embed/JobObject.cs` — wraps `CreateJobObject`,
      `SetInformationJobObject(JobObjectExtendedLimitInformation,
      JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)`, `AssignProcessToJobObject`,
      `CloseHandle`. **Load-bearing for cleanup on VS crash.**
- [x] Tier A test: spawn a `cmd.exe /c ping -t 127.0.0.1`, assign to
      a job, close the handle, assert `cmd.exe` exits within 1s.

#### 1.5.3 `ReactorChildLauncher`

- [x] `Embed/ReactorChildLauncher.cs` per spec §6.5. Owns the Job
      Object, the `dotnet watch` process, and the
      `sessionCounter`/`NewSession` event pipeline. Parses
      `CAPTURE_PORT=` / `CAPTURE_TOKEN=` from stdout. Bumps
      `sessionId` on every fresh `(port, token)` pair.
- [x] `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1`,
      `DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1`,
      `NoDefaultCurrentDirectoryInExePath=1` env vars set on the child.
- [x] Rude-edit prompt auto-answer: when stdout contains "Do you
      want to restart your app", write "y\n" to stdin.
- [x] `SupervisorExited` event for the case where `dotnet watch`
      itself dies (rare; raises "Preview supervisor exited" overlay).
- [x] Tier A tests with a stub stdout source (not a real process):
  - [x] `Launcher_PortAndTokenSequence_ParsesPair_BumpsSession`
  - [x] `Launcher_PortBeforeToken_Pairs`
  - [x] `Launcher_TokenBeforePort_Pairs`
  - [x] `Launcher_NewPairAfterPrior_RaisesSecondSession`
  - [x] `Launcher_RudeEditPrompt_AutoAnswersYes`
  - [x] `Launcher_StderrLines_PropagateThroughEvent`

#### 1.5.4 `DotnetResolver` (security-hardened, spec §10)

- [x] `Embed/DotnetResolver.cs` — port the
      `findDotnetExecutable` logic from
      `src/vscode-reactor/src/extension.ts`. Refuse
      `dotnet[.exe|.cmd|.bat|.com]` resolved from inside the
      workspace; `realpath` check to defeat symlink escapes.
- [x] Tier A tests (corresponding to spec §13.1 enumerated cases):
  - [x] `Resolver_RejectsWorkspaceLocal`
  - [x] `Resolver_RejectsSymlinkEscape`
  - [x] `Resolver_ReturnsSystemPath_OnHappyPath`

### 1.6 VS extension: package, tool window, commands (spec §6.2)

#### 1.6.1 `ReactorPackage`

- [x] `ReactorPackage.cs` — `AsyncPackage` with
      `[PackageRegistration(UseManagedResourcesOnly = true,
      AllowsBackgroundLoading = true)]`,
      `[ProvideToolWindow(typeof(ReactorEmbedToolWindow), Style =
      VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]`,
      `[Guid(PackageGuidString)]`.
- [x] Initialize commands on background load. **Every async entry
      point in `try/catch(Exception)` with a single log path.**
- [x] Optional `Debugger.Launch()` if env var
      `Reactor_VsExtension_DebugBreakOnAttach=1` (spec §12.2).

#### 1.6.2 `ReactorEmbedToolWindow`

- [x] `ReactorEmbedToolWindow.cs` — `ToolWindowPane` with
      `Caption = "Reactor Preview"`, `Content = new ReactorEmbedControl()`.
- [x] Tool window registered with a toolbar carrying three
      commands: `PreviewActiveFile`, `Stop`, `ForceReload`.

#### 1.6.3 Commands

- [x] `Commands/PreviewActiveFileCommand.cs` — resolves the active
      document's csproj via `ProjectContextResolver`, starts the
      launcher, performs the embed handshake.
- [x] `Commands/StopPreviewCommand.cs` — calls `Release()` then
      `Launcher.Dispose()` (Job Object close kills the whole tree).
- [x] `Commands/ForceReloadCommand.cs` — release + dispose +
      relaunch with the same csproj/component (spec §8.2 code block).
      ~200 ms quiet period between dispose and relaunch.

### 1.7 VS extension: editor tracking + component dropdown (spec §7)

#### 1.7.1 `ProjectContextResolver`

- [x] `Embed/ProjectContextResolver.cs` — given an active doc path,
      walk parents to find a `.csproj`. For `.cs` files inside one,
      parse it (regex from `findAllComponentClasses` in
      `extension.ts`) for `Component` subclasses.
- [x] Tier A tests with synthetic file contents (no IO):
  - `ComponentRegex_FindsBasicComponent`
  - `ComponentRegex_FindsGenericComponent`
  - `ComponentRegex_IgnoresUnrelatedClasses`
  - `ComponentRegex_HandlesNestedNamespaces`
  - `ProjectResolver_WalksToParentCsproj`
  - `ProjectResolver_ReturnsNull_ForOrphanFile`

#### 1.7.2 Editor tracking

- [x] `UI/EditorTracker.cs` — subscribes to
      `IVsRunningDocumentTable` (`OnAfterFirstDocumentLock`,
      `OnAfterSave`, `OnAfterAttributeChange`) and DTE
      `WindowEvents.WindowActivated`. Raises an
      `ActiveDocumentChanged(string path)` event.
- [x] Tier B test (`Microsoft.VisualStudio.SDK.TestFramework`):
      `EditorTracker_RaisesOnActiveDocChange` (project created; test skipped pending in-proc harness).

#### 1.7.3 Auto-select policy (spec §7.2, §7.3)

- [x] VM consumes `ActiveDocumentChanged`. Skip if file is not
      `.cs` or doesn't parse to a known component. Otherwise:
      if not manually pinned, set `SelectedComponent` and POST
      `/preview`.
- [x] If the active doc's csproj differs from the running preview's
      csproj, **stop** the preview and relaunch against the new
      csproj. Manual pin is cleared on project switch.
- [x] Manual pin sticks until cleared via the dropdown's
      "Auto-track active file" item.
- [x] Tier A VM tests cover all three branches.

#### 1.7.4 Refresh on hot reload (spec §7.4)

- [x] After every L1/L2 reload event observed by the launcher,
      re-fetch `/components` and update the dropdown. Cheap.

### 1.8 VS extension: lifecycle + error UX (spec §9)

- [x] State machine matching spec §9 table: Idle, Launching, Waiting
      for handshake, Embedded, Building, Respawning, Build failed,
      Crashed-no-respawn, Project switch.
- [x] Each state → VM transition with status text + brush + error
      overlay visibility.
- [x] **30s respawn timeout** (spec §8.3): if no new `CAPTURE_TOKEN=`
      arrives within 30s of the previous child exiting, show
      "Build failed, click ↻ to retry" with the last few stderr lines
      pulled from the launcher's buffered output.
- [x] **Auto-restart once on unexpected exit**, then give up and
      require manual ↻ (spec §9 "Crashed (no respawn)").
- [x] Tier A tests: `Lifecycle_*` for each transition.
- [ ] Tier B test: `Package_Dispose_KillsLauncher_DropsPlaceholder`.

### 1.9 Elevation mismatch detection (spec §16 Q12)

- [x] `Embed/ElevationCheck.cs` — `IsCurrentProcessElevated` via
      `OpenProcessToken` + `TokenElevation`. (We can't pre-check the
      child's elevation because it hasn't started; both VS and the
      child inherit from the launch context, so the rule is "if VS
      is elevated, refuse to embed and explain why".)
- [x] Surface in the tool window chrome before launch: "Visual
      Studio is elevated; embedded preview will silently drop input
      due to UIPI. Restart VS non-elevated to use embedded preview."
- [x] Tier A test: `ElevationCheck_DetectsAdminToken` (skipped on
      runners that can't elevate).

### 1.10 Tier B — VS SDK in-process tests (spec §13.2)

- [ ] `src/vs-reactor/Tests/Reactor.VsExtension.SdkTests/` xunit project
      with `Microsoft.VisualStudio.SDK.TestFramework.Xunit` PackageReference.
- [ ] Tests:
  - `Package_InitializeAsync_Succeeds`
  - `Package_RegistersToolWindow`
  - `Package_FindsToolWindowAsync_ReturnsInstance`
  - `Commands_DispatchedThroughOleCommandTarget`
  - `EditorTracker_RaisesOnRDTEvent`
  - `Package_OnSolutionClose_DisposesLauncher`
- [ ] Add to `Reactor.slnx` and to the CI workflow.

### 1.11 Tier A — pure unit tests (spec §13.1)

- [ ] `src/vs-reactor/Tests/Reactor.VsExtension.Tests/` xunit project
      (.NET Framework 4.7.2, no VS SDK dependency). Aggregate of all
      Tier A tests called out in 1.4 / 1.5 / 1.7 / 1.9.
- [ ] Target ≥40 tests, ~80% line coverage of pure-logic surface
      (per spec §13.1 budget).
- [ ] Add to `Reactor.slnx` and to the CI workflow.

### 1.12 CI integration (spec §13, §14)

- [ ] `.github/workflows/ci.yml`: add a step that runs Tier A and
      Tier B tests on `windows-latest` (the runner already has the
      VS SDK available via the workload).
- [ ] Confirm the VSIX builds as part of the solution build (it is
      in `Reactor.slnx`). The build artifact (`*.vsix`) is uploaded
      as a CI artifact for manual download/test.
- [ ] **Do not run Tier C E2E in CI.** Phase 1 stops at Tier B.

### 1.13 Manual smoke checklist — Tier C.1 (spec §13.3, human validation)

- [ ] Create `src/vs-reactor/TESTING.md` with the spec §13.3 C.1
      checklist verbatim, plus an explicit "this must pass before
      every release" header. Items to cover at minimum:
  1. Launch Exp hive. Tool window opens. Placeholder visible.
  2. Open a Reactor sample project (`samples/apps/minesweeper` or
     similar). Tool window auto-discovers it.
  3. Status reaches "Live" within 15s. Mouse click registers.
  4. Type into a `TextBox`. Backspace/arrow keys work.
  5. Tab traversal between focusable controls works.
  6. ComboBox opens a popup; the popup may extend outside the
     placeholder rect.
  7. Switch component via the dropdown. Auto-track active file
     follows.
  8. Edit a Render body, save → embedded UI refreshes in place
     (L1 hot reload).
  9. Edit a record/type shape, save → child respawns within ~10s,
     placeholder HWND survives (L2 respawn).
  10. Click ↻ → status returns to "Live" within ~10s.
  11. Float the tool window. Drag across monitors at different
      DPIs. No blur, no input drop.
  12. Force-close VS (Task Manager). Within 1s, `dotnet watch` +
      child are gone (verify in Task Manager).
  13. UIA: open `inspect.exe`, walk to the tool window → the WinUI
      tree is visible. Narrator can hop between focusable elements.
- [ ] Each release candidate runs through the checklist; results
      attached to the release PR description.

### 1.14 Documentation (spec §6, §11, §12)

- [ ] `src/vs-reactor/README.md` — install, debug, settings,
      troubleshooting (DPI, elevation, WinAppSDK version).
- [ ] Add a new user-facing doc: create
      `docs/_pipeline/templates/vs-extension.md.dt` (compiled to
      `docs/guide/vs-extension.md`). Cover: when to use the VS
      extension vs the VS Code one, install steps, dropdown
      auto-track, force reload, known limitations.
- [ ] If we want screenshots: a doc app under
      `docs/_pipeline/apps/vs-extension/` is **not** appropriate
      (the screenshot pipeline assumes Reactor, not VS). Use
      hand-captured screenshots committed under
      `docs/_pipeline/templates/img/vs-extension/`.
- [ ] Run `mur docs compile` and confirm `docs/guide/vs-extension.md`
      generates cleanly. Never hand-edit the generated file.
- [ ] Update `docs/guide/index.md` template to add the new page to
      the side nav.

### 1.15 Release packaging (spec §11.3, §14.3)

- [ ] `.github/workflows/release.yml`: add a step that picks up
      `bin/Release/Reactor.VsExtension.vsix` and uploads it as a
      fourth release asset (alongside the two NuGets + skill kit
      zip).
- [ ] Sign the VSIX using the existing release pipeline's signing
      cert (the same one used for the NuGet packages). Per spec
      §11.3, unsigned is acceptable for Phase 1 dev distribution if
      signing is a blocker — note in the release notes.
- [ ] Update the release notes template to mention the VSIX, link
      to `src/vs-reactor/README.md`, and call out the WinAppSDK 1.6+ /
      Windows 11 23H2+ requirement.

### 1.16 Phase 1 exit gate

- [ ] All 17 items in **Overall exit gate** above hold.
- [ ] Tier A + Tier B green in CI.
- [ ] Tier C.1 manual smoke green on a Phase 1 release candidate
      (human validation, documented in the PR).
- [ ] Reactor triple gate green (build / unit / selftest).
- [ ] At least one Reactor sample project (`samples/apps/minesweeper`
      recommended) works end-to-end through the embed flow.
- [ ] Release candidate VSIX installed on a fresh `windows-latest`-class
      machine without admin rights, F5'd against a real csproj,
      reached "Live" status (independent reproduction of the manual
      smoke).

---

## Phase 2 — Polish (spec §15 Phase 2)

Smaller, additive PRs. Each item is independent and can ship on its
own cadence after Phase 1.

### 2.1 DTE-driven auto-preview on solution load

- [ ] On `IVsSolutionEvents.OnAfterOpenSolution`, if the
      tool window is visible AND the user has the setting enabled,
      auto-launch against the solution's startup project.
- [ ] Tools → Options page: `Reactor Preview → Auto-launch on
      solution load` checkbox (default off).
- [ ] Tier B test: `OnSolutionOpen_AutoLaunches_WhenSettingEnabled`.

### 2.2 Persisted last-component-per-project

- [ ] Per-user state at `%LOCALAPPDATA%\Reactor\vs\<projectHash>.json`
      storing `{ "lastComponent": "MyComponent", "csprojPath": "..." }`.
- [ ] On launch, if a stored value exists for the active csproj,
      pre-select it (still subject to the user's auto-track pin).
- [ ] Tier A test: `PersistedState_RoundTrip`.

### 2.3 Owner-mode polish

- [ ] "Pin to top" toggle for owner-mode windows (sets
      `HWND_TOPMOST` via `SetWindowPos`).
- [ ] Tools → Options page: `Reactor Preview → Embedded mode → Force
      floating` (overrides auto-detection).

### 2.4 Tool window toolbar affordances

- [ ] `Refresh components` button (manual re-fetch of `/components`).
- [ ] `Open MCP port` button (copies `http://127.0.0.1:<port>` to
      clipboard for `mur devtools` / Copilot use).
- [ ] `Show devtools log` button (opens the child's log file in
      Notepad / the default text editor).

### 2.5 Hot-reload visual feedback

- [ ] Add a new `/lastReload` endpoint on `PreviewCaptureServer`
      returning the timestamp of the most recent reload event.
- [ ] VSIX polls it on a low-frequency timer (~500ms) and on a new
      value, flashes a 1-pixel border on the placeholder for ~200ms.
- [ ] Setting to disable.

### 2.6 Phase 2 exit gate

- [ ] Each Phase 2 item ships with Tier A coverage + one-line
      addition to `src/vs-reactor/TESTING.md` C.1 checklist.

---

## Phase 3 — Stretch (spec §15 Phase 3)

Placeholders only — not decomposed. Each is its own future spec /
tracker. Listed here so the Phase 1 architecture can be sanity-
checked against them.

- **3.1 Component property inspector.** Replays
  `DevtoolsPropertyTools` MCP surface as a WPF tree in the tool
  window or a side panel.
- **3.2 Live element tree view.** Replays
  `DevtoolsTools.GetComponentTree` as a WPF TreeView, sync-selected
  with the embedded surface (click-to-inspect).
- **3.3 "Open in external window" pop-out.** Detach the embedded
  child and make it standalone; re-embed on demand.
- **3.4 Roslyn-based component discovery** (spec §16 Q13). Replace
  the regex with `Microsoft.VisualStudio.LanguageServices` for
  precise discovery including partials, generics, record syntax.
- **3.5 Marketplace publishing.** Publish the VSIX to the public VS
  Marketplace under the Reactor team's publisher account.
- **3.6 Re-evaluate remote ContentIsland.** Annual revisit per spec
  §16 Q11 — if WinAppSDK has matured to support cross-process
  ContentIsland, the entire `WS_CHILD` cascade becomes a fallback.

---

## Open questions deferred to implementation (spec §16)

These are not standalone Phase 0/1/2 tasks — they're flagged here so
the implementer remembers to **resolve in the PR description** as
they come up.

- **Q1** Message-only window survival on respawn. **Resolve in 1.5.3**
  by confirming the placeholder HWND survives across respawns under
  load (covered by the manual smoke + the 20× force-reload Phase 0b
  test #16).
- **Q5** Project type discovery (`.fsproj`, `.sln`-only). **Resolve
  in 1.7.1** by scoping Phase 1 to `.csproj` and surfacing "no
  Reactor project found" in the chrome with a settings link.
- **Q6** Multi-targeted projects. **Resolve in 1.5.3** by adding a
  `Reactor.Preview.Framework` setting (default `net10.0-windows`)
  that becomes a `--framework` pass-through to `dotnet watch run`.
- **Q8** Tool-window state preservation across VS restart. **Resolve
  in 2.1** (auto-launch on solution load) and document expected
  behavior in `src/vs-reactor/README.md`.
- **Q9** Embed endpoints in `Reactor.Advanced`. **Resolve in 1.16**
  release-candidate smoke by including at least one
  `Reactor.Advanced`-using sample in the test matrix.
- **Q10** `/embed/release` semantics with WS_CHILD. **Resolve in
  1.2.4** by following the spec §16 Q10 closeout: explicit
  `SetParent(NULL)` before `Window.Close()` before `Environment.Exit(0)`.
- **Q14** Keyboard accelerator collisions. **Resolve in 1.4.1** by
  shipping the spec §16 Q14 default ("embedded surface wins when
  focused; VS wins when chrome focused") and documenting in
  README.
- **Q15** Supported WinAppSDK + Windows versions. **Resolve in 1.14**
  by surfacing a "requires Windows 11 23H2 + WinAppSDK 1.6+" check
  on first launch, falling back to floating preview with a clear
  message on older builds.

---

## Notes for future contributors

- **The Reactor side is small.** ~5 endpoints, one CLI flag triple,
  one `WindowSpec` knob, a watchdog thread, a style-flip block.
  Roughly 500–800 LoC of additive C#. Most of the work is the VSIX.
- **The VSIX is medium-sized.** ~3,000–5,000 LoC of C# + ~200 LoC
  of XAML. The dominant complexity is the state machine + the
  HwndHost subclass + the launcher's session-generation race
  handling.
- **The hardest debugging happens cross-process.** Spec §12.2
  describes attaching to both processes; use it. Set
  `Reactor_VsExtension_DebugBreakOnAttach=1` to get a
  `Debugger.Launch()` prompt on package load.
- **If something gets wedged, kill `devenv.exe` from Task Manager.**
  The Job Object guarantees the rest of the tree dies with it.
  This is the failure-mode recovery story for the developer of
  the extension as much as for the user.
- **`dotnet watch` is the supervisor — not `mur devtools`.** Spec
  §8.1 explains why. Exit-42 reload is not a thing here.
- **Rude-edit recovery is detected by stdout, not `Process.Exited`.**
  Spec §5.5 is the load-bearing detail. Get it wrong and the
  extension hangs in "Respawning…" forever after every type-shape
  edit.
