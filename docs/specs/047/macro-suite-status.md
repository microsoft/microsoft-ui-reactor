# Macro Suite L1–L11 Status — Phase 0 §14 Deliverable 3.4

Per-scenario status going into the Phase 0 exit gate. The macro suite ships
in three implementations per spec §15.2 — `Direct` (raw WinUI), `ReactorToday`
(current `main`), `ReactorV2` (verbatim copy of `ReactorToday` at Phase-0
freeze).

| # | Scenario | Direct | ReactorToday | ReactorV2 | Notes |
|---|---|---|---|---|---|
| L1 | `TTFF_Blank` | ✅ `BlankWinUI3` | ✅ `BlankReactor` | ✅ `BlankReactorV2` (0.3.2) | All three present. `run_startup_bench.ps1` enumerates the ReactorV2 variant. |
| L2 | `TTFF_LoginForm` | ⏳ Phase 0 scaffold | ⏳ Phase 0 scaffold | ⏳ Phase 0 scaffold | Scenario shape documented below — six-control login form. Phase 1 ships implementations; Phase 0 freezes the contract. |
| L3 | `TTFF_SettingsPage` | ⏳ Phase 0 scaffold | ⏳ Phase 0 scaffold | ⏳ Phase 0 scaffold | 50-control mixed page. Same scaffold-and-defer treatment as L2. |
| L4 | `WorkingSet_AtStartup` | ⏳ snapshot tool | ⏳ snapshot tool | ⏳ snapshot tool | Captures private bytes + managed heap after L2 reaches its first frame; reuses L2 binaries. |
| L5 | `WorkingSet_Steady` | ⏳ Phase 1 | ⏳ Phase 1 | ⏳ Phase 1 | L3 + 5-minute idle. Long-running — needs the `WTSRegisterSessionNotification` plumbing in 0.3.6 wired through before it produces signal. |
| L6 | `FPS_VirtualizedList_Scroll` | ✅ `StressPerf.VirtualList.WinUI` | ✅ `StressPerf.VirtualList.Reactor` | ⏳ `StressPerf.VirtualList.ReactorV2` | Add the V2 variant in Phase 1 (mirrors the existing Reactor project). |
| L7 | `FPS_AnimatedTree` | ⏳ Phase 1 | ⏳ Phase 1 | ⏳ Phase 1 | 200-element tree, one continuously-animated prop. Builds on the StressPerf.Reactor / .ReactorV2 (0.3.1) shape. |
| L8 | `FPS_HotStateUpdate` | ⏳ Phase 1 | ⏳ Phase 1 | ⏳ Phase 1 | 1000-element form bound to `[NotifyPropertyChanged]` model. |
| L9 | `GC_PerFrame_AnimatedTree` | ⏳ Phase 1 | ⏳ Phase 1 | ⏳ Phase 1 | Variant of L7 — same binary, additional metrics. |
| L10 | `Mount_Storm` | ⏳ Phase 1 | ✅ (via `StressPerf.Reactor` grid burst-mount) | ✅ (via `StressPerf.ReactorV2` grid burst-mount) | Direct equivalent for the burst-mount path needs a new project. |
| L11 | `LongLived_HeapStability` | ⏳ Phase 1 | ⏳ Phase 1 | ⏳ Phase 1 | 30-minute synthetic session. Defers behind L5 since the harness plumbing is shared. |

Legend: ✅ shipped, ⏳ deferred to a phase with a written status, scaffold = contract frozen + project layout decided but no implementation.

## Phase 0 contract — what's locked vs deferred

**Locked at the Phase 0 exit gate:**
- The three-variant pattern (`Direct` / `ReactorToday` / `ReactorV2`) for every macro.
- The JSON-Lines result-row shape consumed by the §15.6 aggregator. Macro
  binaries write rows compatible with the schema `tools/spec047-aggregator`
  enumerates.
- L1's three-way coverage (the only macro that fully ships at Phase 0).
- Existing `StressPerf.Reactor.*` macros now have a `.ReactorV2` sibling
  for the FPS / mount-storm scenarios (L6, L10 partial).

**Deferred to Phase 1 with explicit rationale:**
- L2 / L3 (login form, settings page) need three new executables each.
  Choice: scaffold-and-defer rather than half-implement, so Phase 1 lands
  them with the v1 protocol promotion PRs.
- L5 / L11 (long-running scenarios) need the WTS session-notification
  plumbing from 0.3.6 to be wired into the harness. Defer until Phase 1
  promotes the v1 protocol so we're not measuring an unstable baseline.
- L7–L9 (FPS animated tree / hot state / GC per frame) share infrastructure
  with the existing stress_perf scenarios. Phase 1 will build a single
  `StressPerf.AnimatedTree.{Direct,Reactor,ReactorV2}` family covering
  L7+L9 with a flag, and a `StressPerf.HotState.*` family for L8.

## Baseline implications

The Phase 0 exit gate requires baseline numbers for what *is* shipped. From
the matrix above, the Phase 0 baseline run produces:

- **L1** — three-way TTFF data (BlankWinUI3 vs BlankReactor vs BlankReactorV2).
  At Phase-0 freeze, V2 ≈ Reactor.
- **L6** — two-way (Direct via `StressPerf.VirtualList.WinUI`; Reactor via
  `StressPerf.VirtualList.Reactor`). V2 column reads `not-yet-run` in
  `baseline-results/summary.md`.
- **L10** — two-way (Reactor via `StressPerf.Reactor`; V2 via
  `StressPerf.ReactorV2`). Direct column reads `not-yet-run`.

Spec §11.1 / §11.6 / §12 update against M1–M13 micro numbers (which are
fully shipped per 0.3.3) and **L1** TTFF. The macro-derived deltas that
back those updates are confined to the micro suite; macros add directional
confirmation in Phase 1.

## L2 / L3 contract (frozen at Phase 0)

So Phase 1 implementations don't drift the scenario.

### L2 `TTFF_LoginForm`

Six controls in the order they appear:
1. Heading: "Sign in"
2. TextBox (email), with `PlaceholderText = "Email"`
3. PasswordBox, with `PlaceholderText = "Password"`
4. CheckBox: "Remember me"
5. Button: "Sign in" (primary)
6. HyperlinkButton: "Forgot password?"

Layout: a single VStack with 12 px spacing inside a 320×320 dp Border with
8 px padding. No custom theming; default light theme.

Timing: process spawn (entry of `Main`) to first composited frame
(`CompositionTarget.Rendering` firing after the first commit). Recorded
identically to L1 via the existing `BenchmarkTracing.Log.*` ETW events.

### L3 `TTFF_SettingsPage`

50 controls — implemented as five sections, each 10 controls:

| Section | Controls |
|---|---|
| Appearance | Heading, 3× ToggleSwitch, 1× ComboBox, 1× Slider, 2× Divider, 1× SubHeading, 1× CheckBox |
| Notifications | Heading, 6× ToggleSwitch, 1× SubHeading, 2× Divider |
| Storage | Heading, 3× NumberBox, 1× Slider, 1× ProgressBar, 1× Button, 2× Divider, 1× SubHeading |
| Network | Heading, 2× TextBox, 1× ComboBox, 1× ToggleSwitch, 1× NumberBox, 2× Divider, 1× SubHeading, 1× Button |
| About | Heading, 4× TextBlock, 1× HyperlinkButton, 2× Divider, 1× SubHeading, 1× Button |

Layout: ScrollView containing a VStack of the five sections, each section
is its own VStack with 8 px gutter, sections separated by 16 px. Window
720×600 dp.

Timing: same as L2.

## Cross-reference

- 0.3.1 — `StressPerf.ReactorV2` already covers the in-process stress_perf
  shape that L7–L10 build on.
- 0.3.2 — `BlankReactorV2` covers L1.
- 0.3.5 — aggregator consumes whatever any macro emits, no per-scenario
  custom format.
- 0.3.6 — runbook captures the environment invariants every macro must
  honor. L5 / L11 specifically need WTS session-notification handling
  before they generate trustworthy long-running numbers.
