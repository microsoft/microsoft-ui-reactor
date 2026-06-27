# TitleBar Drag Regions — Windows App SDK 2.1 Uptake

## Status

**Implemented — 2026-06-26.** Builds on [spec 036 — Window Model](036-window-design.md)
and [spec 054 — Windowing Evolution](054-windowing-evolution.md) (modern title bar), and consumes
the auto-mapping path from [spec 058 — Control Wrapper Generator](058-control-wrapper-generator.md).

The triggering question was open-ended: *"survey the Windows App SDK 2.x release notes and find new
APIs Reactor could leverage."* This spec captures that survey, adopts the one high-value uptake it
surfaced (the `Microsoft.UI.Xaml.Controls.TitleBar` drag-region APIs added in 2.1.3), and records the
rest of the survey as considered-but-deferred so the decision trail is durable.

**Status of work:** the 2.0.1 → 2.1.3 bump, `AutoRefreshDragRegions`, and the `.IsDragRegion()` modifier
are implemented and verified — the full `Reactor.Tests` suite is green (9711 passed / 0 failed), plus a
real-WinUI selftest (`TitleBar_DragRegions`) confirming `AutoRefreshDragRegions` round-trips onto the
control and `.IsDragRegion(false)` writes the attached property on a child. The §6 decisions were
resolved per the recommendations.

---

## Table of contents

- [§1 Motivation — the 2.x survey](#1-motivation--the-2x-survey)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 The SDK bump — 2.0.1 → 2.1.3](#3-the-sdk-bump--201--213)
- [§4 API surface](#4-api-surface)
- [§5 Considered but deferred](#5-considered-but-deferred)
- [§6 Decisions (resolved)](#6-decisions-resolved-2026-06-26)
- [§7 Implementation phases](#7-implementation-phases)
- [§8 Testing](#8-testing)

---

## §1 Motivation — the 2.x survey

Reactor pins **Windows App SDK 2.0.1** (`Directory.Build.props`, centralized via CPM; Win2D 1.4.0).
The 2.x line has since shipped 2.1.3 (2026-05-21) and 2.2.0 (2026-06-09). Filtering the release notes
to *XAML / UI* surface (the AI / ML / Video / Storage additions are out of scope for a UI framework),
the new APIs vs. 2.0.1 are:

| API (version) | Reactor relevance | Verdict |
|---|---|---|
| **`TitleBar` drag regions** — `SetIsDragRegion` / `GetIsDragRegion`, `AutoRefreshDragRegions`, `RecomputeDragRegions()` (2.1.3) | `TitleBarElement` maps **directly** to `Microsoft.UI.Xaml.Controls.TitleBar` and exposes a user `Content` slot. The gallery sample already places an `AutoSuggestBox` + `Button` there — the exact mixed interactive / non-interactive case these APIs fix. | **Adopt** (this spec) |
| `XamlBindingHelper.SetPropertyFromThickness` / `…CornerRadius` / `…Color` (2.2.0) | Boxing-free value-type DP sets. Reactor sets value props through source-generated strongly-typed CLR setters; no boxing-pool infra exists today. | Defer (§5) |
| `Setter.ValueProperty` (2.2.0) | Exposes the `Setter.Value` DP; relevant only to programmatic `Style`/`Setter` construction. | Defer (§5) |
| `ApplicationData.GetForUnpackaged()` (2.2.0) | First-class per-user app data for unpackaged apps — could simplify dock-layout / window-placement persistence. | Defer (§5) |

The **only** addition that lands squarely on an existing Reactor surface is the title-bar drag-region
family. It is also self-justifying: 2.1.3 changed the *default* drag-region behavior so the framework
now recursively walks `TitleBar.Content` and excludes only **interactive** controls from the drag
region (RuntimeCompatibilityChange `TitleBar_ContentDragRegions`). Today, Reactor hands the whole
`TitleBar` to `Window.SetTitleBar`, so custom `Content` controls fight the drag region or need manual
hole-punching. Just bumping the SDK inherits the better default; surfacing the two override APIs
completes the story declaratively.

## §2 Goals / non-goals

**Goals**

1. Adopt Windows App SDK **2.1.3** repo-wide, with the runtime / bootstrap / CI implications documented.
2. Surface `TitleBar.AutoRefreshDragRegions` as a declarative `TitleBarElement` property + fluent modifier.
3. Surface the `TitleBar.IsDragRegion` attached property as a cross-cutting `.IsDragRegion()` modifier
   usable on any element placed in a title bar's content.
4. Keep the API DIP-only / declarative (spec 036 / 054 conventions) — no imperative escape hatch unless
   a declarative form can't express the scenario.

**Non-goals**

- Adopting 2.2.0 or any of its APIs (§5).
- Reworking the `Window.SetTitleBar` registration path (`RegisterWindowTitleBar` in `Element.cs`) — the
  new drag-region behavior layers on top of it unchanged.
- A boxing-elimination pass over the reconciler's value-prop setters (§5).

## §3 The SDK bump — 2.0.1 → 2.1.3

**Decision:** bump `WindowsAppSDKVersion` to **2.1.3** — the *minimum* version that exposes the
title-bar drag-region APIs. Rationale for "minimum, not latest":

- 2.1.3 unlocks every API this spec adopts; 2.2.0 adds only the deferred surface (§5), so going further
  widens the behavioral-change blast radius for no in-scope benefit.
- 2.1.3 is **already the pinned runtime** in `tests/stress_perf/ci/Run-PerfBenchmark.ps1`
  (`Microsoft.WindowsAppSDK.Runtime` `2.1.3`), so the perf leg and the framework converge on one version.

**Runtime / packaging implications** (`WindowsAppSDKSelfContained=false` by default — the framework and
its samples consume the *installed* runtime):

| Concern | Finding |
|---|---|
| Side-by-side runtime family | All 2.x releases share **one** SbS framework keyed on the API-contract major (`Microsoft.WindowsAppRuntime.2.msix`), per the comment in `Run-PerfBenchmark.ps1`. So 2.0.1 → 2.1.3 is an **in-place servicing** bump, not a new SbS runtime. |
| `bootstrap.ps1` | Installs `Microsoft.WindowsAppRuntime.2.0` (the major-2 winget id), which services forward to ≥ 2.1.3. The "matching `WindowsAppSDKVersion=2.0.1`" comment should be refreshed to 2.1.3; the winget id itself does not change. |
| In-process test hosts | `Reactor.Tests`, `Reactor.AppTests.Host`, etc. set `WindowsAppSDKSelfContained=true`, so they **bundle** the matching 2.1.3 runtime and don't depend on a machine install — unit tests + selftests validate against the bumped version directly. |
| Win2D | 1.4.0 is already paired with 2.x in-repo; staying within major 2 keeps that pairing valid. |
| **Standalone scaffolding pins** | The `dotnet new` template (`tools/Templates/.../Company.ReactorApp1.csproj`), the CLI's local-scaffold csproj string (`Reactor.Cli/Program.cs`), and the `Microsoft.WindowsAppSDK` install snippets in `docs/guide/index.md`, `SKILL.md`, and `samples/WinFormsInterop/README.md` each **directly** pin the SDK and do **not** inherit the central `WindowsAppSDKVersion`. A stale `2.0.1` / `2.0.*` pin is an **NU1605 downgrade** against the framework's new `>= 2.1.3` dependency — the `Integration Tests` (template smoke) and `bootstrap.ps1` CI jobs scaffold + restore and fail on it. These must be bumped in lockstep (done: `2.1.3` / `2.1.*`). Future SDK bumps should grep `Microsoft.WindowsAppSDK" Version=` and update every literal pin. |
| Inherited default-behavior change | `TitleBar_ContentDragRegions` (auto-exclude interactive controls from the drag region) applies to every `TitleBarElement.Content` for free. This is the headline win even before the override APIs. |

**Validated:** with `WindowsAppSDKVersion=2.1.3`, `dotnet restore` + `dotnet build src/Reactor/Reactor.csproj`
both succeed clean. The descriptor generator regenerates without new diagnostics.

## §4 API surface

### §4.1 `AutoRefreshDragRegions` (per-title-bar, declarative)

Add a non-nullable `bool AutoRefreshDragRegions` to the `TitleBarElement` record. Because the title-bar
wrapper runs in **descriptor-only auto-discovery** mode (spec 058 §15), the generator discovers the new
`TitleBar.AutoRefreshDragRegions` dependency property and emits the `.OneWay(e => e.AutoRefreshDragRegions,
(c, v) => c.AutoRefreshDragRegions = v)` mapping automatically; a non-nullable value-typed record prop is
promoted to an unconditional write, so no `Customize` hand-wiring is needed. Default `false` matches the
WinUI control default.

```csharp
// fluent modifier (ElementExtensions)
public static TitleBarElement AutoRefreshDragRegions(this TitleBarElement el, bool value = true) =>
    el with { AutoRefreshDragRegions = value };
```

When `true`, the control re-derives drag regions on every layout pass — the right fit for a reactive
framework whose `Content` changes across renders.

### §4.2 `.IsDragRegion(bool?)` (cross-cutting child modifier)

`TitleBar.IsDragRegion` is a **static attached property** (`bool?` — `true` = draggable, `false` =
clickable, unset = framework-decided). It can sit on any descendant of the title-bar content, so it is
modeled as a cross-cutting modifier on `ElementModifiers`, exactly like `AutomationName` / `IsTabStop`:

1. `ElementModifiers` gains `public bool? IsDragRegion { get; init; }`.
2. A generic modifier:
   ```csharp
   public static T IsDragRegion<T>(this T el, bool isDragRegion = true) where T : Element =>
       Modify(el, new ElementModifiers { IsDragRegion = isDragRegion });
   ```
3. `Reconciler.ApplyModifiers` applies it on the realized `FrameworkElement`, mirroring the
   `AutomationName` set/clear arms:
   ```csharp
   if (m.IsDragRegion.HasValue && m.IsDragRegion != oldM?.IsDragRegion)
       WinUI.TitleBar.SetIsDragRegion(fe, m.IsDragRegion.Value);
   else if (!m.IsDragRegion.HasValue && oldM?.IsDragRegion.HasValue == true)
       fe.ClearValue(WinUI.TitleBar.IsDragRegionProperty);
   ```

Setting the attached DP on an element that is *not* inside a `TitleBar` is inert (an unread attached
value), so the modifier is safe on any element and needs no title-bar-subtree gating. The unset state
intentionally falls through to the new 2.1.3 default (auto-exclusion), so authors only reach for
`.IsDragRegion(false)` to make a non-interactive visual clickable, or `.IsDragRegion(true)` to force a
gap draggable.

```csharp
TitleBar("Gallery") with
{
    Content = HStack(8,
        AutoSuggestBox("", _ => {}).Width(200),          // interactive → excluded by default
        Button("\uE713", () => {}).IsDragRegion(false))   // explicit: keep clickable
}.AutoRefreshDragRegions();
```

### §4.3 `RecomputeDragRegions()` — intentionally not surfaced

The imperative `RecomputeDragRegions()` method is **not** wrapped. `AutoRefreshDragRegions` provides the
same effect declaratively and continuously, which is the idiomatic Reactor fit; exposing an imperative
"recompute now" method would invite call sites that fight the render loop. Revisit only if a concrete
scenario needs a one-shot recompute that `AutoRefreshDragRegions` can't cover (§6, D3).

## §5 Considered but deferred

| Candidate | Why deferred |
|---|---|
| `XamlBindingHelper.SetPropertyFrom{Thickness,CornerRadius,Color}` (2.2.0) | Boxing-free value-type DP sets. Reactor writes value props via source-generated strongly-typed CLR setters (`c.Margin = …`), which box internally; capturing this win means threading explicit `DependencyProperty` references through the generator, fighting its strongly-typed model for a marginal GC improvement. No boxing-pool infra exists today to justify the inconsistency. Revisit only with reconcile-allocation evidence (the `perf-compare` harness). Requires 2.2.0. |
| `Setter.ValueProperty` (2.2.0) | Useful only for boxing-free programmatic `Style`/`Setter` construction; tangential to Reactor's theming-token path. Requires 2.2.0. |
| `ApplicationData.GetForUnpackaged()` (2.2.0) | Could replace bespoke file-path persistence for dock-layout / window-placement in unpackaged apps with a first-class `ApplicationData`. Worth a focused look, but orthogonal to this spec and gated on a 2.2.0 bump. |

## §6 Decisions (resolved 2026-06-26)

| # | Question | Resolution |
|---|---|---|
| D1 | Target version — **2.1.3** (minimal) vs. **2.2.0** (latest stable, picks up §5 candidates) | **2.1.3.** Minimal blast radius; aligns with the pinned perf runtime. |
| D2 | `AutoRefreshDragRegions` default | **`false`** (match WinUI). Opt-in via `.AutoRefreshDragRegions()`. Avoids a per-layout recompute cost no author asked for. |
| D3 | Expose `RecomputeDragRegions()`? | **No** (§4.3). |
| D4 | Spec home — standalone **059** vs. a new section in **054** (windowing) | **Standalone**, because the SDK bump is repo-wide, not windowing-local. |

## §7 Implementation phases

1. ✅ **SDK bump** — `Directory.Build.props` 2.0.1 → 2.1.3; refreshed the `bootstrap.ps1` version comment.
2. ✅ **`AutoRefreshDragRegions`** — added the record property to `TitleBarElement`; generator auto-maps it
   (`.OneWay<bool>(e => e.AutoRefreshDragRegions, …)`, confirmed in the generated descriptor); added the
   `.AutoRefreshDragRegions()` fluent modifier.
3. ✅ **`.IsDragRegion()`** — added `bool? IsDragRegion` to `ElementModifiers`, the generic modifier, and the
   `ApplyModifiers` set/clear arm.
4. ✅ **Sample** — extended `samples/ReactorGallery/.../TitleBarPage.cs` "TitleBar with Content".
5. ✅ **Docs** — windowing skill + `windows.md.dt` guide template (recompiled `docs/guide/windows.md`).

## §8 Testing

| Tier | Coverage |
|---|---|
| Unit (`Reactor.Tests`) | `.IsDragRegion()` / `.AutoRefreshDragRegions()` produce the expected `ElementModifiers` / `TitleBarElement` records (modifier plumbing, default + override values). |
| Selftest (`Reactor.AppTests.Host/SelfTest/Fixtures`) | Mount a real `TitleBar` with content; assert `TitleBar.GetIsDragRegion(child)` reflects `.IsDragRegion(false/true)` and clears on unset; assert `AutoRefreshDragRegions` round-trips on the control. |
| Build / CI | Full `Reactor.slnx` build + unit tests + selftests on the bumped SDK (the standard PR gate). |
