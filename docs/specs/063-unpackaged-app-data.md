# Unpackaged App Data — Windows App SDK 2.2 Uptake

## Status

**Implemented — 2026-09-16.** Closes the `ApplicationData.GetForUnpackaged()` row deferred in
[spec 059 §5](059-titlebar-drag-regions.md#5-considered-but-deferred), and builds on
[spec 036 — Window Model §8](036-window-design.md) (the persistence-store abstraction).

Spec 059 surveyed Windows App SDK 2.x and adopted the one API that landed on an existing Reactor
surface (`TitleBar` drag regions), deferring the rest. Its §5 recorded:

> | `ApplicationData.GetForUnpackaged()` (2.2.0) | Could replace bespoke file-path persistence for
> dock-layout / window-placement in unpackaged apps with a first-class `ApplicationData`. Worth a
> focused look, but orthogonal to this spec and gated on a 2.2.0 bump. |

This spec is that focused look. It bumps the SDK to 2.2.0 and adopts the API — but adopts the
**path** surface rather than the settings surface, on the strength of a measurement rather than the
release notes (§3.1).

---

## Table of contents

- [§1 Motivation](#1-motivation)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 The SDK bump — 2.1.3 → 2.2.0](#3-the-sdk-bump--213--220)
- [§4 API surface](#4-api-surface)
- [§5 Considered but deferred](#5-considered-but-deferred)
- [§6 Decisions (resolved)](#6-decisions-resolved-2026-09-16)
- [§7 Implementation phases](#7-implementation-phases)
- [§8 Testing](#8-testing)

---

## §1 Motivation

`PackagedSettingsStore` routes window/dock persistence through
`Windows.Storage.ApplicationData.Current.LocalSettings`. That WinRT API requires package identity,
so in an unpackaged process it throws `InvalidOperationException` / `0x80073D54`. Reactor works
around this with a `PackagedSettingsStore.IsAvailable()` probe feeding auto-detection, and a
`JsonFileStore` fallback that writes to
`%LOCALAPPDATA%/<ProcessName>/reactor-windows.json`.

That fallback works, but its key — the **entry process's name** — is a weak identity:

- it changes when an app renames its executable, silently stranding saved layouts;
- two unrelated apps with the same exe name collide;
- it is Reactor-invented rather than platform-blessed, so nothing else on the system agrees with it.

Windows App SDK 2.2.0 added `Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged(publisher,
product)` — a first-class per-user app-data root for unpackaged apps, keyed on a stable
publisher/product pair. That is a genuinely better identity for this data, and it is the piece
spec 059 §5 flagged as worth adopting.

## §2 Goals / non-goals

**Goals**

1. Adopt Windows App SDK **2.2.0** repo-wide, with the version-pin and runtime implications
   documented (§3).
2. Adopt `ApplicationData.GetForUnpackaged()` in the persistence layer as a real, usable store
   (§4).
3. Establish empirically — not from release notes — which storage surface of that API is safe to
   build on at 2.2.0, and record the evidence (§3.1).
4. Leave existing apps' persisted layouts working, unchanged, with no migration step.

**Non-goals**

- Changing the auto-detected default store. §6 D2 explains why the default stays `JsonFileStore`.
- Migrating existing `JsonFileStore` data into the new root (§5).
- Adopting the `LocalSettings` key/value surface (§3.1), or `MachinePath` / `SharedLocalPath` (§5).
- Changing anything about the packaged path — `PackagedSettingsStore` is untouched.
- Going past 2.2.0. Later 2.x releases carry the fix discussed in §3.1, but "minimum, not latest"
  is the precedent set by spec 059 §3 and is deliberately preserved here.

## §3 The SDK bump — 2.1.3 → 2.2.0

**Decision:** bump `WindowsAppSDKVersion` to **2.2.0** — the *minimum* version exposing
`GetForUnpackaged()`, matching spec 059's "minimum version, not latest" rationale.

`WindowsAppSDKWinUIVersion` moves **2.1.0 → 2.2.1**.

> **The WinUI sub-package version is not derivable from the metapackage version.** Windows App SDK
> 2.0 split the metapackage into independently-versioned sub-packages. The pairing is neither
> equality nor a fixed offset: 2.1.3 pairs with WinUI 2.1.0, and 2.2.0 pairs with WinUI **2.2.1**.
> Nor is it "the newest WinUI on the feed" — 2.3.6 and 2.3.9 exist and belong to later trains. The
> only correct source is the metapackage's own nuspec `<dependency id="Microsoft.WindowsAppSDK.WinUI">`.
> Read it, don't infer it.

**Runtime / packaging implications:**

| Concern | Finding |
| --- | --- |
| Side-by-side runtime family | Unchanged. The 2.2.0 runtime package's own `WindowsAppSDK-VersionInfo.json` names the framework family `Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe`, and ships `Microsoft.WindowsAppRuntime.2.msix` — identical to 2.1.3. So 2.1.3 → 2.2.0 remains an **in-place servicing** bump within one SbS family, and `tools/WindowsAppRuntimeId.ps1`'s major-only rule is unchanged. |
| Sub-package movement | Relative to 2.1.3: `Foundation` 2.0.21 → **2.1.0**, `WinUI` 2.1.0 → **2.2.1**, `InteractiveExperiences` 2.0.13 → 2.0.15, `AI` 2.1.10 → 2.2.3, `ML` 2.1.1 → 2.1.70. `Base` (2.0.4), `DWrite` (2.1.0) and `Widgets` (2.0.5) are unchanged. |
| **Reachability of the new API** | `Microsoft.Windows.Storage` lives in the **Foundation** sub-package, not WinUI — worth checking, because `Directory.Build.targets` gives framework-dependent libraries (including the shipped `Microsoft.UI.Reactor`) only `Microsoft.WindowsAppSDK.WinUI`. Verified reachable: `WinUI 2.2.1` declares a dependency on `Foundation 2.1.0`, and post-restore `src/Reactor`'s `project.assets.json` resolves `Microsoft.WindowsAppSDK.Foundation/2.1.0`. **No new `PackageReference` is required.** |
| In-process test hosts | `Reactor.Tests`, `Reactor.AppTests.Host` etc. set `WindowsAppSDKSelfContained=true`, so they bundle the 2.2.0 runtime and validate against it directly. |
| Win2D | 1.4.0 unchanged; staying within major 2 keeps the pairing valid. |
| Navigation-motion parity | `WinUiNavigationMotionParityTests` deliberately reddens on any SDK bump, forcing re-verification of the WinUI motion constants Reactor copies. Re-verified — see below. |
| **Standalone scaffolding pins** | See the correction table below. |

**Navigation-motion re-verification.** The four WinUI sources backing every copied constant —
`src/dxaml/phone/lib/{ThemeTransitions.cpp,ThemeTransitions.h,NavigateTransitionHelper.h,NavigateTransitionHelper.cpp}`
— are **byte-identical** between `microsoft/microsoft-ui-xaml` tags `winui3/release/2.1.3`
(`fce9db16349395cd9617ed8dc08ab40df1f46415`) and `winui3/release/2.2.0`
(`fb389b88af6d358f09d0adb614f941b64a82515d`): blob SHAs `ade27cbf…`, `2c9bb435…`, `8761d909…`,
`bf82d5cd…`. Identical blob SHAs are content identity, so no line-level drift is possible. The
tag-to-tag diff is *non-empty* (22 files — ScrollView, RenderTargetBitmap, ContentPresenter,
`x:Bind`/`Setter` codegen) and touches no animation code; that non-empty diff is the positive
control proving the comparison can detect change, so the null result is a measurement rather than a
broken query. `VerifiedAgainstWindowsAppSdkVersion` advanced to 2.2.0 on that basis.

> **The discharge method is recorded in the guard's own failure message**, not only here — the next
> person to hit it is standing in the test output, not reading this spec. Three details make the
> difference between discharging it and concluding it is impossible:
>
> 1. **Tags are keyed to the SDK *metapackage* version** (`winui3/release/2.2.0`), not the
>    `Microsoft.WindowsAppSDK.WinUI` sub-package version. No `winui3/release/2.2.1` tag exists.
> 2. **The path prefix differs between layouts.** At a release tag the tree is rooted under `src/`;
>    on `winui3/main` it is not. Querying the main-branch path against a release tag 404s on *every*
>    file, which reads exactly like "this tree is not published at tags" — measured: all four files
>    return 404 at `dxaml/phone/lib/…` and resolve at `src/dxaml/phone/lib/…`, at 2.1.3, 2.2.0 and
>    2.3.1 alike.
> 3. **A README-resolves check is not a sufficient positive control** for (2), because `README.md`
>    exists under *both* layouts and therefore cannot distinguish a wrong prefix from an absent
>    tree. The control has to be path-shaped — another file known to live under `src/` at tags — or,
>    better, the non-empty tag-to-tag diff used above.
>
> Compare **blob SHAs**, not file contents: a SHA is content identity, and unlike a content diff it
> cannot be satisfied by two error responses comparing equal. Treat any non-200 as a miss, never as
> a match.

### §3.0 Correction to spec 059 §3

Spec 059's "Standalone scaffolding pins" row is the checklist the next bump gets read from, so two
errors in it are corrected here rather than left to propagate:

| Spec 059 §3 claim | Correction |
| --- | --- |
| `tools/Templates/.../Company.ReactorApp1.csproj` literally pins the SDK | **It does not.** The template sets `WindowsAppSDKSelfContained=true` and references only `Microsoft.UI.Reactor`; the SDK arrives transitively. There is no version literal to bump there. |
| (not listed) | **The `#:package Microsoft.WindowsAppSDK@<version>` file-based-app header is a second pin shape entirely.** It does not match the `Microsoft.WindowsAppSDK" Version=` grep that spec 059 §3 prescribes, so PR #723 missed **every instance of it** — not just one. 21 files were left at `2.0.1`, already a downgrade against 2.1.3, for a full release cycle: `SKILL.md`'s single-file-app snippet, 8 recipes under `skills/recipes/`, 10 under `plugins/reactor/skills/reactor-recipes/references/`, and 3 occurrences in `samples/apps/demo-script-tool/App/Resources/SystemPrompt.txt`. The recipe files are **packed into the shipped NuGet agent kit** (`src/Reactor/Reactor.csproj`), so this was a user-facing `NU1605` downgrade for anyone who ran a recipe, not an internal-only staleness. All now `2.2.0`. |

The root cause is structural: **the enumeration was a prose instruction, so it was only ever as good as
the last person's regex.** Fixing the instances without fixing the class would guarantee a third
occurrence at the next bump, so this spec also adds a guard —
`WinAppSDKReferenceGuardTests.No_literal_SDK_pin_sits_below_the_central_pinned_version` — which sweeps
the tracked tree for **both** pin shapes and fails on any literal below `$(WindowsAppSDKVersion)`.
Pins *above* it are allowed, because docs legitimately state per-feature minimums ("TitleBar drag
regions need ≥ 2.1.3") and a forward pin is not a downgrade.

The guard carries a positive control: it asserts **both** shapes are observed somewhere in the tree,
so a regex that silently decays reddens instead of reporting zero offenders and passing exactly as
green as a clean tree. Mutation-verified — reintroducing a single stale pin in
`skills/recipes/themed-card.cs` fails the test with the offending `file:line`.

For reference, the pins that must move with `WindowsAppSDKVersion` as of this bump:

- `src/Reactor.Cli/Program.cs` — CLI local-scaffold csproj string
- `SKILL.md` — ×2: a `PackageReference` snippet **and** a `#:package` header
- `samples/WinFormsInterop/README.md` — `PackageReference` snippet
- `skills/recipes/*.cs` (8) and `plugins/reactor/skills/reactor-recipes/references/*` (10) —
  `#:package` headers in **shipped** agent-kit recipes
- `samples/apps/demo-script-tool/App/Resources/SystemPrompt.txt` (×3) — the generator prompt that
  teaches an LLM which pin to emit, so a stale value propagates into generated apps
- `tests/stress_perf/ci/Run-PerfBenchmark.ps1` — pinned `Microsoft.WindowsAppSDK.Runtime`
- `bootstrap.ps1`, `tools/WindowsAppRuntimeId.ps1` — explanatory version comments
- `tests/Reactor.Tests/WinAppSDKReferenceGuardTests.cs`, `WinUiNavigationMotionParityTests.cs` —
  test-side pins, which fail as **test failures** rather than `NU1605`, so they do not look like
  the same class of problem when they go red

A stale pin below the central version is an `NU1605` downgrade against the framework's raised
floor, failing the `Integration Tests` (template smoke) and `bootstrap.ps1` CI jobs.

### §3.1 The `LocalSettings` roaming defect — measured, not assumed

Windows App SDK **2.5.1**'s release notes claim this fix (RuntimeCompatibilityChange
`ApplicationData_GetForUnpackaged_LocalSettings`, [WindowsAppSDK#6559](https://github.com/microsoft/WindowsAppSDK/issues/6559)):

> Fixed `ApplicationData.GetForUnpackaged().LocalSettings()` opening a registry key at the wrong
> path (`HKCU\SOFTWARE\publisher\product` rather than
> `HKCU\SOFTWARE\Classes\Local Settings\Software\publisher\product`), **which is a roaming location
> rather than one local to the machine**.

Targeting 2.2.0 means inheriting that defect. Because the decision in §6 turns on it, it was
**verified on 2.2.0** rather than taken on the release note's word. A sentinel was written through
`GetForUnpackaged(pub, prod).LocalSettings`, then both candidate hives were searched:

| Probe | Result |
| --- | --- |
| API round-trip (`Values["probe"]` read back) | sentinel returned — the write really happened |
| `HKCU\SOFTWARE\<pub>\<prod>` (**roaming**) | **sentinel found** at `@probe` |
| `HKCU\SOFTWARE\Classes\Local Settings\Software\<pub>\<prod>` (machine-local) | **key does not exist** |

The third arm matters: "found in neither" would have meant the probe was wrong, not that the
behaviour was correct. It did not occur, so the result is a measurement. **The defect is real on
2.2.0.**

(That release note's *version* attribution turns out to be wrong — the fix actually lands at
metapackage 2.3.1 / `Foundation` 2.3.5, established in §3.2. It does not change anything here:
2.2.0 was measured directly, and the correction only moves the follow-up trigger earlier.)

Two further properties were measured at the same time, and shape §4:

| Surface | Behaviour on 2.2.0 |
| --- | --- |
| `LocalPath` | Resolves to `%LOCALAPPDATA%\<publisher>\<product>` — **genuinely machine-local** (`%LOCALAPPDATA%` does not roam). Correct today, unaffected by #6559. |
| directory creation | `GetForUnpackaged()` resolves the path but does **not** create the directory. |
| `publisher` validation | The SDK rejects traversal (`..\..\x`), rooted (`C:\x`) and empty values with `ArgumentException`. Reactor does not need to sanitize. |
| `LocalCachePath` | Throws `NotImplementedException`. |

**The defect is confined to `LocalSettings`.** The path surface is correct. That is what makes a
useful adoption possible at 2.2.0 rather than forcing a wait for a later release — and it is why §4
builds on `LocalPath`.

### §3.2 Which runtime produced that result — and why the fix version is deliberately not named

Windows App SDK 2.x ships **one** side-by-side framework package per major
(`Microsoft.WindowsAppRuntime.2`), serviced **in place** (spec 059 §3). `GetForUnpackaged`'s
registry behaviour lives in that **runtime**, not in the compile-time reference. Two consequences,
both load-bearing:

**1. A framework-dependent measurement cannot attribute behaviour to a pinned SDK version.** An app
built against 2.2.0 on a machine carrying a newer 2.x runtime loads the *newer* runtime, so it would
observe the newer behaviour. "Version X fixed it" and "the installed runtime has the fix" are
indistinguishable from such a probe.

The measurement in §3.1 is not exposed to that confound, and this was verified rather than assumed.
`Reactor.Tests` sets `WindowsAppSDKSelfContained=true`, so it loads the runtime **bundled in its own
output directory**. Measured on a machine with `Microsoft.WindowsAppRuntime.2` registered at both
2.4.0 and 2.5.1:

```text
Microsoft.WindowsAppRuntime.dll
  path = <repo>\tests\Reactor.Tests\bin\x64\Debug\net10.0-windows10.0.22621.0\Microsoft.WindowsAppRuntime.dll
```

Not a `WindowsApps\` path — so the observed behaviour reflects the **pinned** `WindowsAppSDKVersion`.
The direction of the confound also matters: §3.1 observed the *roaming* hive, i.e. the **old**
behaviour, which accidentally loading a *newer, fixed* runtime cannot produce. Any future probe of
this API should record the loaded module path alongside the registry result; the check is one line
(`Process.GetCurrentProcess().Modules`), and without it a result is not attributable.

**2. For a consumer, correctness varies by *deployment mode and machine*, not by their SDK pin.**
This is the sharper form of the argument, and it is measured rather than hypothesised. The behaviour
is decided by the **loaded** runtime, so one unchanged source tree pinned to one SDK version splits
two ways:

| Deployment | Runtime that loads | `LocalSettings` lands in |
|---|---|---|
| `WindowsAppSDKSelfContained=true` | the **bundled** `Foundation` | roaming hive (for a 2.2.0 pin) — even on a machine carrying a fixed 2.x runtime |
| framework-dependent | the machine's installed 2.x runtime, serviced **in place** | machine-local hive wherever a fixed runtime is installed |

Same source, same pin, opposite storage locations. **A default whose correctness depends on how the
app was packaged and what happens to be installed on the user's machine is a bad default regardless
of which release fixed the bug** — and unlike the roaming defect itself, this does not go away when
the fix propagates, because the split persists for as long as any unfixed 2.x runtime is in the wild.
That is an independent and more durable reason to keep `JsonFileStore` as the unpackaged default
(§6 D2) than "2.2.0 is broken", and it survives the trigger in this section being corrected.
`LocalPath` has no such variance: it resolves under `%LOCALAPPDATA%` on every 2.x runtime, in either
deployment mode.

Accordingly **this spec does not key its follow-up trigger to a release number.** The published
release notes attribute the #6559 fix to 2.5.1. **That attribution is wrong** — a companion
two-arm experiment, run on one machine from this spec's own 2.2.0 base with the registry and
`%LOCALAPPDATA%` wiped between arms, places the fix at metapackage **2.3.1**:

| Arm | Metapackage | Foundation | Runtime loaded from | roaming hive | machine-local hive |
|---|---|---|---|---|---|
| A | 2.2.0 | 2.1.0 | own build output | **present** | absent |
| B | 2.3.1 | 2.3.5 | own build output | absent | **present** |

Arm A is Arm B's negative control: it shows the probe *does* report the roaming key when the defect
is live, so Arm B's "absent" is a measurement rather than a silent miss. Both arms loaded a bundled
runtime, so the machine's 2.5.1 SbS package is excluded in both directions. Arm A also independently
reproduces §3.1 from a separate worktree.

**The boundary is a `Foundation` version, not a metapackage version** — and the pairing between them
is neither stable nor even monotonic in appearance (verified against the metapackage nuspecs):

| Metapackage | 2.2.0 | 2.3.1 | 2.4.0 | 2.5.1 |
|---|---|---|---|---|
| `…WindowsAppSDK.Foundation` | 2.1.0 | **2.3.5** | 2.3.9 | 2.3.12 |

Foundation sits *below* the metapackage version at 2.2.0 and *above* it at 2.3.1. So a trigger naming
a metapackage version is two indirections from the thing that decides behaviour — the loaded
`Foundation` binaries — and a trigger naming a release note is simply wrong. The trigger is therefore
expressed as an **observable condition** and enforced by the positive control in
`UnpackagedAppDataStoreTests.Does_Not_Route_Window_Placement_Through_The_Roaming_Registry_Hive`: it
fails the moment the loaded runtime stops writing to the roaming hive, and its message walks the
reader through separating a broken probe from a real fix and through identifying which runtime
produced the result (§8).

For anyone re-running this: identify the runtime by **hashing the loaded binaries** and matching them
back to the package cache, not merely by checking the module path. A `bin\` path proves the runtime
is bundled rather than machine-wide — enough to exclude the SbS confound — but it does not say *which*
`Foundation` produced the behaviour, which is the number the boundary actually attaches to.

## §4 API surface

### §4.1 `UnpackagedAppDataStore`

A new public `IWindowPersistenceStore` in `Microsoft.UI.Reactor.Hosting.Persistence`:

```csharp
var store = new UnpackagedAppDataStore(publisher: "Contoso", product: "TimeTracker");
ReactorApp.WindowPersistenceStore = store;   // before the first OpenWindow
```

It resolves `ApplicationData.GetForUnpackaged(publisher, product).LocalPath` and persists to
`<LocalPath>/reactor-windows.json`.

It **deliberately does not use `LocalSettings`** (§3.1). Window placement is a `WINDOWPLACEMENT`
plus a monitor-layout fingerprint — screen coordinates, DPI and monitor topology. Roaming that
between a user's machines restores windows onto monitors that do not exist there, and the failure
is delayed and confusing: it only manifests for users who sign in on more than one machine. The
contract and the behaviour disagree on 2.2.0; the path surface has no such disagreement.

Implementation composes `JsonFileStore` over the SDK-provided root rather than reimplementing I/O,
so the hardened pieces — atomic write-then-rename, the 1 MB cap, base64 payloads, the AOT-safe
hand-rolled JSON, the write lock — are shared, not forked. `JsonFileStore` gained an `internal`
constructor overload taking a trace label so the two stores stay distinguishable on the spec 044
Persistence trace (`unpackaged-appdata` vs `json-file`); a new store that reported itself as
`json-file` would make those traces ambiguous.

Construction may throw `ArgumentException` for an empty or SDK-rejected publisher/product. That
matches `JsonFileStore(path)`: an invalid identity is an authoring error, whereas `TryRead`/`Write`
keep the `IWindowPersistenceStore` "never throw into the caller" contract.

The public surface is deliberately just the constructor plus the two interface methods. The
`Path` property is **`internal`** (visible to `Reactor.Tests`), unlike `JsonFileStore.Path`:
that type is *definitionally* file-backed, so a path is part of what it is, whereas this one is
"the SDK app-data store" and the file is an implementation detail of the `LocalPath`-vs-
`LocalSettings` choice in §6 D1 — a choice §5 leaves open. Keeping it internal means the public
surface is **identical under every outcome of that revisit**, so reversing D1 later would change
no published API. Widening `internal` → `public` is additive and non-breaking if an app author
ever needs the path for diagnostics; the reverse is not. Note `ReactorApp.WindowPersistenceStore`
is typed `IWindowPersistenceStore?`, which has no `Path` member, so the property was only ever
reachable through a caller's own concrete-typed reference.

### §4.2 Workarounds NOT removed

The bump makes it tempting to simplify the existing unpackaged workarounds. Each was checked and
**kept**, with the reason:

| Workaround | Kept because |
| --- | --- |
| `PackagedSettingsStore.IsAvailable()` | Auto-detection still has to choose between packaged and unpackaged stores. Unrelated to `GetForUnpackaged`. |
| The `InvalidOperationException` / `COMException` / `UnauthorizedAccessException` arms in `PackagedSettingsStore.TryRead`/`Write` | These guard `Windows.Storage.ApplicationData.Current` — a **different API** from `Microsoft.Windows.Storage.ApplicationData`. Nothing in the 2.2.0 bump changes its behaviour, and `PackagedSettingsStoreTests` demonstrates the arms are live by exercising them from the unpackaged xUnit host. Not dead code. |
| `JsonFileStore` and its process-name root | Still the default (§6 D2), and the only store with existing user data. |

## §5 Considered but deferred

| Candidate | Why deferred |
|---|---|
| Making `UnpackagedAppDataStore` the auto-detected unpackaged default | §6 D2. Revisit when the default-store guard's positive control flips (§8). |
| Using the `LocalSettings` key/value surface | §3.1 — roams on 2.2.0. Revisit when the guard's positive control fails, i.e. when the loaded runtime starts writing to the machine-local hive (§3.2 — the fix version is deliberately not named, and the runtime, not the SDK pin, decides). |
| Migrating existing `JsonFileStore` data into the app-data root | Requires a publisher/product pair the framework cannot invent on the app's behalf, and a one-way copy whose failure mode is silent data duplication. Only worth building alongside a default flip. |
| `ApplicationData.MachinePath` / `IsMachinePathSupported` | Per-machine (all-users) app data. No Reactor surface wants machine-wide window placement. |
| `ApplicationData.SharedLocalPath`, `TemporaryPath`, `LocalCacheFolder` | No consumer. `LocalCachePath` additionally throws `NotImplementedException` on 2.2.0 (§3.1). |
| `XamlBindingHelper.SetPropertyFrom{Thickness,CornerRadius,Color}` and `Setter.ValueProperty` (both 2.2.0) | Still deferred, unchanged, per spec 059 §5. This bump makes them *available* but adopts neither. |
| Entrance `outControlPoint1/2`, and the non-uniform DrillIn `BackNavigatingTo` scale curve | Surfaced while re-verifying §3's motion constants: `WinUiNavigationMotionParityTests` does not record the Entrance exit control points, and `BackNavigatingTo` uses a different scale easing (`{0.12, 0.0}` / `{0.0, 1.0}`) from the other three triggers. Both are **pre-existing** and independent of this bump. |
| Packaged-process behaviour of `GetForUnpackaged()` | Unmeasured, and therefore unclaimed. The store targets unpackaged apps; packaged apps should use `PackagedSettingsStore`. Establishing the packaged behaviour needs the `Reactor.PackagedTests` tier and is out of scope. |

## §6 Decisions (resolved 2026-09-16)

| # | Question | Resolution |
|---|---|---|
| D1 | Which storage surface — `LocalSettings` (the API's headline key/value store) or `LocalPath`? | **`LocalPath`.** Measured on 2.2.0: `LocalSettings` writes to a roaming hive (§3.1), `LocalPath` resolves under non-roaming `%LOCALAPPDATA%`. Window placement is machine-local data, so the settings surface is disqualified until the fix is in the loaded runtime — and even then §3.2 shows that is a per-machine property rather than something the SDK pin guarantees. |
| D2 | Does the new store become the unpackaged **default**? | **No — opt-in.** Not caution, but a correctness argument: the two stores key data differently (publisher/product vs. process name), so flipping the default would strand every existing unpackaged app's saved layout with no migration path (§5). Pinned by a test that reddens if the default changes (§8). |
| D3 | Should the framework synthesise a default publisher/product? | **No.** Any guess (assembly name, process name) reintroduces exactly the fragile identity the API exists to replace, and a wrong guess collides across apps. The pair is required and explicit. |
| D4 | Target version — **2.2.0** (minimum) vs. a later 2.x carrying the `LocalSettings` fix | **2.2.0.** Repo-owner call, consistent with spec 059 §3's "minimum, not latest". D1 means the defect is avoided by construction rather than by version, so the fix — and which release carries it (§3.2) — is not load-bearing for this spec. |
| D5 | Sanitize `publisher` / `product` before handing them to the SDK? | **No.** Measured: the SDK already rejects traversal, rooted and empty values with `ArgumentException` (§3.1). A second sanitizer would diverge from the platform's rule over time. Reactor validates only non-emptiness, for a clearer message, and pins the SDK's rejection with a test. |
| D6 | Spec home — standalone **063** vs. a section in **036** (window model) | **Standalone.** The SDK bump is repo-wide, and 059 is the precedent for an SDK-uptake spec. |
| D7 | Expose `Path` on the new store, as `JsonFileStore` does? | **No — `internal`.** §4.1. `JsonFileStore` is definitionally file-backed; this store's file is an artifact of D1, which §5 explicitly leaves open for revisit. Internal keeps the public surface identical under every outcome of that revisit, and `internal` → `public` is additive if anyone ever needs it. |

## §7 Implementation phases

1. ✅ **SDK bump** — `WindowsAppSDKVersion` 2.1.3 → 2.2.0, `WindowsAppSDKWinUIVersion` 2.1.0 → 2.2.1
   (read from the metapackage nuspec), plus every literal pin in §3.0.
2. ✅ **Measurement** — registry-hive probe establishing §3.1 before any design was committed.
3. ✅ **`UnpackagedAppDataStore`** — new public store over `LocalPath`; `JsonFileStore` gained an
   internal trace-label constructor.
4. ✅ **Motion-parity re-verification** — `VerifiedAgainstWindowsAppSdkVersion` → 2.2.0 with the
   blob-SHA citation recorded in the test.
5. ✅ **Tests** — §8.
6. ✅ **Docs** — regenerated public API index (both copies), CHANGELOG entry, this spec.

## §8 Testing

`UnpackagedAppDataStoreTests` (headless `Reactor.Tests`). The xUnit host has **no package
identity**, which is exactly the condition `GetForUnpackaged` targets, so this is the right tier —
no live XAML object is involved and no selftest is needed.

A bare write/read round-trip was deliberately **not** used as the oracle: both stores write the
same JSON document shape, so a round-trip passes identically whichever one ran and proves nothing
about the adoption. Every assertion pins the **storage location** instead.

| Test | Pins |
| --- | --- |
| `Persists_Under_The_Sdk_Provided_AppData_Root_Not_The_Process_Name_Root` | `store.Path` is under `GetForUnpackaged().LocalPath` and *not* under `JsonFileStore`'s process-name root. Guarded by a positive control that the SDK root actually contains the requested publisher/product, so the comparison cannot degenerate into a tautology. |
| `Written_Bytes_Land_In_A_File_Beneath_The_Sdk_AppData_Root` | Bytes reach the filesystem at an **independently recomputed** SDK path — not at `store.Path`, which a store that reported one location and wrote to another would satisfy self-consistently. |
| `A_Second_Store_Over_The_Same_Publisher_Product_Sees_The_Same_Data` | The root derives from publisher/product, not per-instance state — the property that makes this identity survive an executable rename. Paired with a different-`product` negative control, because the shared read on its own would also pass for any deterministic singleton path. |
| `Does_Not_Route_Window_Placement_Through_The_Roaming_Registry_Hive` | D1, as a regression guard. Carries its own positive control: it first drives `LocalSettings` directly and asserts the roaming key **does** appear, proving the probe can see it, before asserting the store did not create one. |
| `Rejects_Empty_Publisher_Or_Product`, `Rejects_A_Publisher_The_Sdk_Considers_Invalid`, `Rejects_A_Product_The_Sdk_Considers_Invalid` | D5 — Reactor's non-emptiness check and the SDK's own rejection, pinned for **both** segments. `product` reaches `Path.Combine` exactly as `publisher` does, so testing only the latter would leave half the documented validation claim unverified. |
| `Auto_Detection_Still_Picks_JsonFileStore_When_Unpackaged` | D2. Reddens if the default is flipped. |
| `PersistenceEtwBridgeTests.UnpackagedAppDataStore_Write_emits_its_own_storeKind_not_json_file` | The `unpackaged-appdata` trace label, plus the spec 044 §6.2.1 rule that no path reaches the payload. Distinguishability on the trace was the stated justification for the `JsonFileStore` `_storeKind` refactor, so it needs an assertion — otherwise the composed store could silently report as `json-file` and the refactor would be paying for nothing. |
| `WinAppSDKReferenceGuardTests.No_literal_SDK_pin_sits_below_the_central_pinned_version` | §3.0 — the structural fix for the literal-pin class. |

**Mutation-verified.** Green tests prove nothing until they are shown to redden, so three mutations
were applied to the product code and each was caught by exactly the intended test:

| Mutation | Caught by |
| --- | --- |
| Root the store at `JsonFileStore.DefaultPath()` instead of the SDK root | `Persists_Under_The_Sdk_Provided_AppData_Root_…` |
| Make the store touch `LocalSettings` | `Does_Not_Route_Window_Placement_Through_The_Roaming_Registry_Hive` |
| Flip `ResolvePersistenceStore()` to return the new store | `Auto_Detection_Still_Picks_JsonFileStore_When_Unpackaged` |

> A stale-build trap is worth recording, because it nearly produced a false result: restoring a
> mutated file with `Copy-Item` from a backup carries the backup's **original** timestamp, so
> MSBuild judged the source older than the built DLL and skipped the rebuild — the "reverted" run
> silently re-ran the mutant. It was caught by scanning the built DLL for the mutation marker, but
> only after the first scan (ASCII) was itself found to be broken: .NET user string literals live
> UTF-16-encoded in the `#US` heap, so an ASCII scan for a string literal can never match. The
> working instrument is a UTF-16 scan with a known-present literal as a positive control.

**The roaming guard is also the follow-up trigger.** Its positive control asserts the defect
is present in the loaded runtime. When that stops being true, the control fails — and its failure
message walks the reader through distinguishing a broken probe from a real fix, through confirming
*which* runtime produced the result (§3.2), and then points at §6 D1/D2. The revisit therefore
happens deliberately, announced by a red test keyed to an **observable condition** rather than to a
version number someone inferred from release notes.
