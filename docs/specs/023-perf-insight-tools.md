# 023 — Performance Insight Tools

**Status:** Draft
**Date:** 2026-04-17
**Author:** Chris Anderson

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Personas](#3-personas)
4. [State of the World](#4-state-of-the-world)
5. [Competitive Landscape](#5-competitive-landscape)
6. [Conceptual Model: Intent → Cost](#6-conceptual-model-intent--cost)
7. [Convention Over Infrastructure](#7-convention-over-infrastructure)
8. [Responsibility Split: WinUI vs Reactor](#8-responsibility-split-winui-vs-reactor)
9. [WinUI Capabilities](#9-winui-capabilities)
10. [Reactor Extensions](#10-reactor-extensions)
11. [Attribution Mechanism — Options](#11-attribution-mechanism--options)
12. [Synthetic Workload Strategy](#12-synthetic-workload-strategy)
13. [AI-Agent Interface](#13-ai-agent-interface)
14. [Extensibility Down the Stack](#14-extensibility-down-the-stack)
15. [Success Metrics](#15-success-metrics)
16. [Open Questions](#16-open-questions)
17. [Implementation Phases](#17-implementation-phases)

---

## 1. Problem Statement

A WinUI/XAML developer who wants to answer "why is my app slow?" — or, more usefully, "what did I just do that made it slow?" — faces a stack of powerful but disjoint tools: VS Diagnostic Tools, PerfView, WPR + WPA, the new (24H2 ADK) WPA XAML Frame Analysis plugin, GPUView, `DebugSettings.EnableFrameRateCounter`, and Live Visual Tree's descendant count. Each is useful in isolation. None of them speak the developer's language.

The tools report on threads, stacks, GC generations, DirectX queues, composition surfaces, and DWM frames. The developer thinks in XAML elements, bindings, data templates, styles, and components. Bridging those two worlds is left entirely to the human. The friction is high enough that most devs don't try — they ship something that feels slow, a PM complains, and the fix cycle depends on one person on the team who knows how to read an ETL file.

Meanwhile, the *design* that caused the cost — a third drop shadow on a card, a nested `ItemsRepeater` with no recycling, a `DataTemplate` that binds to a non-`INotifyPropertyChanged` property — is made by a different person using different tools, with no feedback loop at all. Design-time cost is invisible until runtime, and runtime cost is unattributable back to design.

**This spec is primarily a WinUI/XAML spec.** The overwhelming majority of the value — element-level attribution, compile-time perf reports, default-on framework signposts, a public dependency-graph query API, an agent-consumable trace format — benefits every WinUI app and should ship in `microsoft/microsoft-ui-xaml`, the XAML compiler, and (where applicable) the .NET BCL. Microsoft.UI.Reactor (Reactor) is a consumer of these capabilities and contributes a narrow set of component-layer extensions (§10) that the underlying framework cannot reasonably supply.

The ambition is *not* a better Instruments clone. It is a new contract between the code a developer writes and the cost that code produces — surfaced in the IDE at design time, in an overlay at runtime, in a trace file post-hoc, and in a structured feed an AI agent can query. The competitive landscape (section 5) shows that Apple and Google have solved roughly half of this each. Nobody has shipped all of it.

## 2. Goals and Non-Goals

### Goals

- **Attribute runtime cost to developer intent.** A frame that took 19 ms must resolve to the element, binding, template, or component that caused it — not to `ntdll!RtlUserThreadStart` or `Microsoft.UI.Xaml!XamlCore::Tick`.
- **Ship as much as possible in WinUI itself.** A capability belongs in `microsoft/microsoft-ui-xaml`, the XAML compiler, or the .NET BCL unless it provably cannot live there. Reactor-only surface is the last resort, not the first.
- **Give designers design-time estimates.** Before a single line of code ships, surface a prediction: "three drop shadows on a 200-row list = ~12 ms/frame on a 25th-percentile device, +38 MB working set."
- **Lower the activation energy to near zero.** Default-on overlay in debug builds. No `perfcore.ini` edits. No admin-elevated `wpr` commands. No separate downloads.
- **Make the perf system AI-agent-native.** Every signal a human can see must be queryable by an agent through a structured, stable, documented contract — not scraped from a `.etl` file.
- **Stay honest about costs.** Instrumentation must be cheap enough to leave on in Release (sub-1% overhead at steady state) or cleanly disabled with a single flag.
- **Extend gracefully down the stack.** The runtime contract must allow — without rework — the same intent to be attached to lower-level ETW events (composition, DX, DWM) when we control those layers.

### Non-Goals

- **Not** building a competing general-purpose profiler. PerfView and WPA remain better at system-wide analysis and we will interop with them, not replace them.
- **Not** shipping a new kernel provider. We emit into existing ETW infrastructure and light up new keywords.
- **Not** solving crash/hang telemetry from production. That's a different spec (and mostly solved by Watson/App Center for the Windows crash path); we may converge schemas later, but dev-loop tools come first.
- **Not** a marketplace of plugins, a DSL for rules, or a generic rule engine. A small, opinionated set of rules and views maintained by the WinUI team.
- **Not** GPU shader-level profiling. PIX already does that well; we integrate rather than duplicate.
- **Not** Reactor-only. Everything that can be WinUI-native is WinUI-native. Spec 007 remains separate — that's about making Reactor's reconciler faster, this is about making cost visible to every WinUI developer.

## 3. Personas

### P1 — App developer (primary)

Writes WinUI/XAML in C#, knows their way around Visual Studio, has heard of WPA but has never opened it. Runs into perf problems at unpredictable times — usually when a PM or tester says "this list is janky." Wants a green-yellow-red signal, an element or component name, and a next action. Will install a NuGet package; will not install the ADK.

### P2 — UX designer (secondary)

Works in Figma and sometimes XAML. Knows what a drop shadow costs conceptually but not numerically. Wants to iterate on a card design with live feedback on "is this going to hurt the app?" Will not open Visual Studio on their own; will look at an overlay a dev runs for them.

### P3 — AI agent (tertiary, but the force multiplier)

A coding agent (Copilot, Claude, an internal tool) that can read structured perf data and propose code changes. Has no access to a GUI. Cannot navigate WPA. Can reliably call a CLI, read JSON, and write a patch. Wants: "run the app, report which element or component is slow and why, and which knob to turn."

### Non-Personas (explicit)

- OS kernel engineer doing context-switch analysis.
- Graphics engineer debugging a shader.
- SRE looking at production RUM.

These users already have better tools (WPA, PIX, AppCenter) and are not who this spec optimizes for.

## 4. State of the World

The honest summary of what a WinUI 3 dev has access to today, and why they don't use it:

| Tool | What it's good at | Why devs don't reach for it |
|---|---|---|
| VS Diagnostic Tools | CPU sampling, .NET allocation | No XAML attribution. The old Application Timeline (the one with the XAML-aware layout/render lanes) is WPF/UWP only, not WinUI 3. |
| PerfView | .NET GC and allocation attribution | 50 MB download, 2000s UI. The data model is right; the UX hasn't been redesigned in a decade. |
| WPR + WPA | System-wide ETW analysis | Requires ADK install, knowing which profiles to `-start`, knowing which providers to add, learning WPA's table/graph model. |
| WPA + XAML Frame Analysis plugin (24H2) | Frame-level XAML activity | Ships in the ADK, disabled by default, requires a manual `perfcore.ini` edit to enable. Frame granularity only — sub-frame attribution is still on the developer. |
| GPUView | DX queue + DWM composition | Separate tool, high expertise bar. |
| `DebugSettings.EnableFrameRateCounter` | In-window FPS overlay | The only zero-setup signal — and it has an open AV bug on WinUI 3 Desktop (`microsoft-ui-xaml#2835`) that has been unfixed for years. Many devs hit it on first try and give up. |
| Live Visual Tree descendant count | "Which container is huge" | Hint, not an answer. No time or memory attribution. |
| Hot Reload | (Not a perf tool) | Microsoft Learn *explicitly* tells devs to turn it off before measuring, because VisualDiagnostics injection distorts timings. The IDE's default mode is the one you can't measure. |

Two load-bearing facts hide in this table:

1. **The ETW substrate is already very good.** Providers exist for XAML, composition, DWM, DirectX, .NET runtime, and kernel events, and they share a clock. What's missing is a coherent *schema* across them and a coherent *presentation* on top.
2. **`Microsoft.UI.Xaml.Controls.Perf` already exists.** It's a TraceLogging provider inside microsoft-ui-xaml that individual controls emit into (ItemsRepeater virtualization, NavigationView). It is not publicly documented, has no stable schema, and emits whatever each control author chose. Formalizing even a subset of this into a documented, stable contract would unblock this spec without a framework rewrite.

## 5. Competitive Landscape

### 5.1 Apple

**The moat is not any single tool. It's also not just "one clock" — ETW gives us that. It's the combination of one clock, one schema, universal correlation, default-on framework emission, and a queryable dependency graph.** ktrace is Apple's substrate; `os_signpost` (2018) and its stricter refactor `OSSignposter` (2021) are the canonical way app and framework code emit intervals and events into it. Apple's frameworks emit signposts everywhere by default — App Launch phases, Core Animation commit/render split, SwiftUI body evaluations. Instruments is a GUI over this unified stream.

It's worth being precise about where Microsoft is at parity and where it isn't, because "Apple has a shared clock and we don't" is a tempting but wrong framing:

| Dimension | Apple | Windows / ETW |
|---|---|---|
| **Shared clock** | `mach_absolute_time` flows through ktrace + user events | QPC flows through kernel + user providers. **Parity.** |
| **Schema coherence** | One `os_signpost` primitive: category, name, signpost ID, interval begin/end, metadata — every framework uses it the same way | Every provider (XAML, DWM, D3D, .NET runtime) defines its own event shapes independently. A GC interval is two unrelated events; a XAML frame has its own schema; a DWM commit has yet another. **Gap.** |
| **Correlation primitive** | Signpost ID is first-class on *every* signpost | `ActivityId` / `RelatedActivityId` exists but is inconsistently plumbed across providers. Correlation within a provider works; cross-provider is rare. **Gap.** |
| **Causality / flow** | Signpost ID chains; Perfetto has explicit flow events | No first-class "this event caused that event" primitive in ETW. **Gap.** |
| **Default-on framework emission** | UIKit/Core Animation signposts always on, near-zero-cost when not tracing | `Microsoft.UI.Xaml.Controls.Perf` is disabled unless captured, ad hoc per control author, not systematically emitting Measure/Arrange. **Gap.** |
| **Queryable framework dependency graph** | SwiftUI AttributeGraph — the instrument queries it at capture time to join "which views depend on this state" to "which bodies ran" | WinUI's DependencyProperty system has the binding graph inside it. Not exposed as a query surface. **Gap.** |
| **Capture unification** | Instruments is one pipeline; merging is default | WPA can merge multi-session traces, but you have to know to. **Small gap.** |

Practical consequence: the Apple developer clicks a slow frame in **Animation Hitches**, sees it broke down into "commit phase: 7 ms (CPU encode)" vs "render phase: 9 ms (GPU)," drops the same trace into Time Profiler, and the same timestamp range shows the C function that ran. No cross-tool correlation is needed because they share a clock *and a schema* — the signpost IDs are the same primitive everywhere.

The right reading is: **Microsoft has the substrate; we lack the conventions on top of it.** §7 makes this explicit.

**SwiftUI Instrument (WWDC25).** The closest production example of what section 6 calls Intent Attribution. The instrument shows a **Cause-Effect graph**: gesture → state change → observable property → list of views whose `body` re-ran, with dimmed nodes for bodies that ran but produced no render-visible change. This is possible because SwiftUI's **AttributeGraph** exposes a queryable runtime dependency graph — each `body` is a node, each `@State`/`@Observable` edge is stored, and the instrument just pulls it.

The implication: **to ship SwiftUI-Instrument-quality attribution, the framework must expose a dependency graph at runtime, not just emit events.** WinUI has this information inside the DependencyProperty system — it just isn't exposed. A public query API (§9.6) is the lift.

**MetricKit + Xcode Organizer.** This is the dev→prod unification story. A signpost emitted during profiling (`OSSignposter`) aggregates through `MXSignpostMetric` into a histogram per named interval, delivered in a daily `MXMetricPayload` from every consenting device and aggregated into Xcode Organizer by OS version / device class / release. Same primitive, dev and prod. Windows has no analog — WPR is dev-only, Watson/App Center use a different schema.

**Apple Silicon PMCs** are exposed in Instruments as time-aligned lanes overlaying CPU samples, with bottleneck preset groupings (front-end bound / back-end bound / memory bound). Instruments 26 added a guided workflow that rotates counter groups across runs.

**Culture vs tooling — honest read.** Ranked by actual impact: (1) vertical integration (one clock, one schema through kernel/framework/compositor/GPU); (2) framework design choices (AttributeGraph is queryable; UIKit run-loop makes "frame" first-class everywhere); (3) WWDC rhythm — 3–6 performance sessions per year with named framework engineers, defining best practice; (4) tooling UX, which is actually *not great* — Instruments has a steep curve and no guided workflow before Xcode 26. The moat is the data model, not the UI.

Also non-obvious: **Apple has no design-time perf overlay.** SwiftUI Previews do not warn about expensive modifiers or recomputation counts. The whole Apple story is measure-on-device, not estimate-at-design-time.

### 5.2 Google

**Perfetto is architecturally the most sophisticated app-perf infrastructure either vendor has shipped.** Three layers: producers (ATrace, ftrace, `TRACE_EVENT` SDK with flow events that link causal work across threads), a daemon that multiplexes into a single protobuf trace on a unified clock, and a **Trace Processor** that loads the trace into an in-process columnar database queryable by SQL (PerfettoSQL). Pre-baked "trace-based metrics" emit protobuf summaries designed for CI.

**Capture is fully decoupled from analysis.** A trace can be viewed in the browser UI, queried from Python, batch-analyzed in CI, or queried by an AI agent with SQL. This is the closest any vendor has come to agent-native perf tooling, though Google has not marketed it that way.

**Compose Compiler Metrics.** The Compose compiler emits three files at build time — class stability, per-composable skippable/restartable, and a CSV of composables. This is **design-time static analysis of performance**, rare in the industry. A developer can see before running that a composable is non-skippable because it takes an unstable `List<Foo>`. Exists but is hidden behind a gradle flag and not surfaced in the IDE. **The XAML compiler can do the same thing for XAML and do it better** (§9.3).

**Baseline Profiles.** A list of hot methods AOT-compiled at install time by ART, generated from a Macrobenchmark "golden path" run and shipped as an app asset. Typical gain: ~30% faster startup and first-use. The workflow — capture the intent once, let the runtime optimize — is a pattern WinUI/.NET could adopt for XAML parse, binding, and reflection hot paths.

**Compose in Android Studio.** Layout Inspector shows per-composable recomposition + skip counts as columns in the tree. Direct equivalent of the SwiftUI instrument but at design inspection time rather than trace time.

### 5.3 Synthesis — ranked gaps

**Things Apple/Google have that Windows lacks:**

1. Unified event *schema* and correlation conventions across kernel/framework/compositor/GPU. (ETW gives us the clock; we lack the schema and correlation layer on top — see §5.1 table.)
2. Framework-aware attribution at the widget level, backed by a queryable runtime dependency graph.
3. Dev→prod telemetry on the *same* schema (MetricKit).
4. SQL-queryable trace store (Perfetto).
5. Compile-time perf reports (Compose Compiler Metrics).
6. "Capture golden path, runtime optimizes" (Baseline Profiles).
7. Guided bottleneck analysis with preset PMC groupings (Instruments 26).

**Unclaimed territory neither has shipped:**

- **Design-time cost estimates driven by aggregated real-device telemetry.** Apple has the parts (MetricKit histograms, signpost intervals) but has not fused them into an IDE overlay that says "this `ListView` template will cost ~14 ms/frame at the 25th percentile of your telemetry cohort."
- **AI-agent-first tooling surface.** Perfetto's SQL is the closest architectural primitive. Nobody has designed a perf tool from the ground up around "an agent will do 90% of the queries."
- **Intent attribution across framework → composition → DX → DWM.** Apple's stitchedness stops at Core Animation; Metal System Trace is a separate workflow. ETW already crosses these boundaries on Windows — this is a schema + UX opportunity, not a capture problem.
- **Compile-time perf reports for XAML.** The XAML compiler could emit "this `DataTemplate` is non-recyclable because it binds to a non-`INotifyPropertyChanged` property" today. Nobody ships this for any XAML-family framework.
- **Zero-config framework signposts for every `Measure`/`Arrange`/`Render`/`Binding.Update`.** Apple does this for App Launch; nobody does it for the general render loop.

Full research notes live at `docs/research/perf-competitive-teardown.md` (to be created in phase 1).

## 6. Conceptual Model: Intent → Cost

The central abstraction this spec introduces is **Intent**: a stable, developer-meaningful identifier attached to the logical source of UI work. An Intent can be:

- A XAML element (`<Border x:Name="CardBorder"/>`)
- A `DataTemplate` instantiation (`ProductCardTemplate#17`)
- A `Binding` (`Product.Title → TextBlock.Text`)
- A named region the developer explicitly scoped (`using (Perf.Scope("CartCheckout"))`)
- A Reactor component instance (`ProductCard[id=42]`) — a Reactor-only kind (§10)

Most Intent kinds are WinUI-native. The framework knows about elements, templates, and bindings; it just doesn't surface stable IDs for them today.

Every Intent has:

- **An identity** — stable across the lifetime the framework can reasonably track. For elements, this is a `PerfTag` synthesized by the XAML compiler from file + line + column (§9.2), persisted through template instantiation. For Reactor components, it extends through remounts via the component key.
- **A parent** — forming a tree that mirrors the element tree (and, in Reactor, the component tree), so costs roll up.
- **Source coordinates** — file, line, optionally column, to navigate the developer back to the code.
- **A kind** — Element, Template, Binding, UserScope, Component (Reactor-only).

Cost is any measurable quantity associated with an Intent during a time window:

- CPU wall time (Measure, Arrange, Render, Binding.Update, component Render)
- Managed allocations (bytes + count, optionally generation)
- Composition commits (surfaces created, visual tree mutations)
- Frames attributed (this Intent was on the critical path of N of the last M frames)

The system's job, top to bottom, is: **for every cost, produce the Intent that caused it.** Everything else — the overlay, the design-time estimator, the agent interface — is a view on that mapping.

This is the same conceptual trick Apple pulled with `os_signpost` plus AttributeGraph, and Google pulled with Perfetto flow events plus Compose's composition tree. We are not inventing the idea; we are adapting it to XAML.

## 7. Convention Over Infrastructure

The load-bearing insight of this spec: **Microsoft has the substrate. We lack the conventions on top of it.**

ETW is a shared clock across kernel/user/native/managed. Providers exist for every layer we care about. What is missing is not capture infrastructure — it is a small, boring set of conventions every provider agrees to follow:

1. **A universal interval primitive.** An `os_signpost`-equivalent in .NET base: `System.Diagnostics.Signpost.Begin(category, name, id, metadata...)` / `.End(id)`. Built over `EventSource` ActivityId. Near-zero-cost when nobody is listening. **WinUI frameworks adopt it for Measure/Arrange/Render/Binding.Update; apps adopt it for their own work; tools consume one schema instead of N.** This is the single highest-leverage API in the entire design.

2. **A universal correlation ID on framework events.** Every cost-emitting XAML event carries an `IntentId` field (§11 option B). Composition, DX, and DWM providers adopt the same field opportunistically. Tools filter/group by it with stock ETW operators.

3. **A compile-time source of stable identity.** The XAML compiler synthesizes a `PerfTag` for every element and persists it through template instantiation (§9.2). Runtime pointer identity is no longer the finest grain the tools can reach.

4. **A compile-time perf report.** The XAML compiler emits `PerfReport.json` per Page/UserControl (§9.3). Compose Compiler Metrics is the proof this is valuable; XAML can do it better because XAML is more statically analyzable than Kotlin DSL.

Each of these is small. Each is a convention, not a rewrite. Together they give WinUI what `os_signpost` + Signpost IDs + AttributeGraph give Apple: one clock, one schema, one correlation, default-on emission, and a queryable framework model.

This is the right lens on the spec. Everything in §9 is a concrete instance of one of these four conventions.

## 8. Responsibility Split: WinUI vs Reactor

| Capability | Owner | Rationale |
|---|---|---|
| `System.Diagnostics.Signpost` API | **.NET BCL** | Cross-framework primitive; belongs with `EventSource`, not XAML. |
| Formalized `Microsoft.UI.Xaml.Perf` provider with documented, stable schema | **WinUI** | Already exists internally; documenting it is zero runtime. |
| `IntentId` field convention on framework events | **WinUI** | Convention layer on ETW. Universal wire-format addition, zero per-event cost when absent. |
| Default-on signposts for Measure / Arrange / Render / Binding.Update | **WinUI** | Framework instruments itself. |
| `PerfTag` from XAML compiler (stable per-element ID from source coords) | **XAML compiler** | Only the compiler knows the authoring-time identity; element pointer identity is insufficient. |
| Fixed + upgraded `DebugSettings` overlay (per-frame breakdown, top-N elements, working-set delta) | **WinUI** | `EnableFrameRateCounter` already lives here. Fixing the AV and extending is strictly in-scope for WinUI. |
| XAML compile-time `PerfReport.json` per Page/UserControl | **XAML compiler** | Direct Compose Compiler Metrics analog; no runtime cost. |
| `x:Bind` compile-time complexity annotations | **XAML compiler** | `x:Bind` is compile-time already; enrichment is incremental. |
| `DataTemplate` recyclability verdict + reasons at compile time | **XAML compiler** | Static analysis. Drives analyzer rule PERF001 with zero runtime work. |
| `Microsoft.UI.Xaml.Analyzers` NuGet (PERF001–PERF004, PERF006, PERF007) | **WinUI** | Roslyn + XAML analyzers. Rules target XAML constructs, not Reactor. |
| Public dependency-graph query API (`GetBindingsReferencing(dp)`, binding-edge introspection) | **WinUI** | The AttributeGraph analog. Internal data VisualDiagnostics already uses; promote to public. |
| `xamlperf` CLI + canonical JSON trace export | **WinUI / WindowsAppSDK** | Framework-neutral at the XAML layer. |
| MCP server over the JSON trace | **WinUI / WindowsAppSDK** | Agent surface over the same schema. |
| Synthetic workload catalog (`shell`/`feed`/`dashboard`/`editor`) | **WinUI** | Reference measurements serve every WinUI app. StressPerf (Reactor) is the first contribution, not the only one. |
| Stable-across-remount Component Intent kind | **Reactor** | WinUI elements lose identity on re-templating; only the Reactor reconciler persists identity through the component key. |
| Component-tree rollups (distinct from visual-tree rollups) | **Reactor** | The logical component parent chain is a Reactor concept. |
| Reconciler-aware metrics (memoization effectiveness, re-render without output change) | **Reactor** | Requires control of the render loop. |
| `PERF005` — component with `>N SetState` calls per render | **Reactor** | `SetState` is a Reactor API. |
| Single-function render cost boundary | **Reactor** | Components have a `Render()`; WinUI has no single-function equivalent. |

The table is the rule. Anything not in the Reactor rows belongs in WinUI, the XAML compiler, or the BCL. §9 elaborates each WinUI row; §10 is the full list of Reactor-only surface.

## 9. WinUI Capabilities

### 9.1 `System.Diagnostics.Signpost` (proposed .NET BCL addition)

A clean `os_signpost` equivalent for managed code. API sketch:

```csharp
namespace System.Diagnostics;

public static class Signpost
{
    public static SignpostId NewId();
    public static void Event(string category, string name, SignpostId id = default, string? message = null);
    public static IntervalToken BeginInterval(string category, string name, SignpostId id = default, string? message = null);
    // struct IntervalToken : IDisposable — End called on Dispose
}
```

Built over `EventSource` with ActivityId. Category + name are TraceLogging metadata. Near-zero-cost when no `EventListener` / ETW session is attached (one interlocked read + branch). WinUI adopts it for framework events in §9.4; any app adopts it for its own work; tools consume one schema.

This is the single most leveraged API in the spec. It is not strictly required — we could get most of the way with TraceLogging directly — but without a named convention every dev writes their own and we recreate the ETW provider-sprawl problem one layer up.

### 9.2 Stable element identity from the XAML compiler (`PerfTag`)

Today, element identity at runtime is pointer identity, which doesn't survive template instantiation and has no connection to source code. We propose the XAML compiler synthesize a `PerfTag` string per XAML element derived from:

- File path (relative to the project)
- Line + column of the element's opening tag
- For elements inside a `DataTemplate`: template identity + instance index

Emitted into the generated `InitializeComponent`, attached to the element as a (debug-only) attached property or entry in a weak-ref side table. Readable by the overlay, the trace export, and the design-time estimator. Zero runtime cost in Release if we strip the side table; debug-only is fine for v1.

Rough shape: `Views/Cart.xaml:82:5#Border` for a top-level element, `ProductCardTemplate#17/Border@8:12` for an element inside the 17th instance of a template.

### 9.3 XAML compile-time `PerfReport.json`

Per Page / UserControl / `DataTemplate`, the XAML compiler emits a JSON report during build containing:

- **Element census** — counts by element type, total element count, max depth.
- **Binding census** — `x:Bind` expressions with their complexity class (one-time / one-way / two-way, function call depth, fallback usage).
- **Template recyclability** — boolean + reasons ("non-recyclable: binds to `Product.Title` where `Product` does not implement `INotifyPropertyChanged`").
- **Effect inventory** — shadows, blurs, opacity masks applied to template roots.
- **Resource-dictionary census** — merged-dictionary graph with duplicate detection. Each `ResourceDictionary.MergedDictionaries` edge is recorded; any dictionary reached by more than one path is flagged with both paths and the estimated redundant-lookup multiplier (XAML searches in reverse order, so a dictionary included twice is walked twice per lookup). Drives `XAML_PERF007` in §9.7. Caveat: the compiler only sees its own compilation unit; inclusions through `package.appxfragment` or dynamically merged dictionaries are out of reach here and are caught instead by the runtime signposts in §9.4.
- **Static cost estimate** — a number and confidence band from the lookup table in §12, keyed on the census.

Location: `obj/$(Configuration)/$(TFM)/PerfReport.json`. Consumed by: the IDE (hover tooltip, Problems window), CI (regression gates), AI agents (structured cost signal before runtime).

This is the Compose Compiler Metrics analog and the single biggest unclaimed design-time perf win in the industry. XAML is *more* statically analyzable than Compose's Kotlin DSL, so this should be strictly better than what Android ships.

### 9.4 Default-on framework signposts (Measure / Arrange / Render / Binding.Update)

WinUI emits, using §9.1's Signpost API, intervals for:

- Every `Measure` and `Arrange` on a `UIElement` over a threshold (e.g., > 0.2 ms — below that, aggregate per frame).
- Every `Binding.UpdateTarget` / `UpdateSource`.
- Every `DataTemplate` instantiation.
- `Layout` / `Render` / `Commit` phase boundaries.

Each interval carries the element's `PerfTag` as the `IntentId`. Always on. Zero listener = zero cost. This is the single most important framework change in the spec — without it, the overlay, the trace, and the agent surface have nothing to attribute.

### 9.5 Upgraded `DebugSettings` overlay

Replace `DebugSettings.EnableFrameRateCounter` (which has an open AV on WinUI 3 Desktop) with `DebugSettings.PerfOverlay`. A translucent overlay rendered via a separate HWND child so it cannot crash the app. Shows:

- FPS + frame time (p50, p95, p99 over a 2s rolling window)
- Top-N Intents by wall time, with their `PerfTag`
- Working-set delta since app start and since last 10s
- Spike marker — click a spiked frame to open a tree view rooted at the Intent that caused it

Default-on in debug builds. One line to enable in Release for field debugging. Supersedes the existing FPS counter and its bug.

### 9.6 Public dependency-graph query API

Promote internals used by Live Visual Tree / VisualDiagnostics to a public programmatic surface:

```csharp
namespace Microsoft.UI.Xaml.Diagnostics;

public static class DependencyGraph
{
    public static IReadOnlyList<BindingEdge> GetBindingsReferencing(DependencyObject source, DependencyProperty property);
    public static IReadOnlyList<BindingEdge> GetBindingsTargeting(DependencyObject target);
    public static IReadOnlyList<DependencyObject> GetDescendants(DependencyObject root);
    // etc.
}
```

This is the WinUI equivalent of SwiftUI's AttributeGraph exposure. The data already exists inside the DP system; we make it queryable. Enables SwiftUI-Instrument-quality "why did this re-render" views for any WinUI tool, not just ours.

### 9.7 `Microsoft.UI.Xaml.Analyzers` NuGet

Roslyn + XAML analyzers emitting warnings at compile time:

- **`XAML_PERF001`** — `DataTemplate` binds to a property whose containing type does not implement `INotifyPropertyChanged`. (Driven by §9.3's recyclability verdict.)
- **`XAML_PERF002`** — Non-virtualizing panel (`StackPanel`, `Grid`) used inside an `ItemsControl` with an unbounded source.
- **`XAML_PERF003`** — Nested `ItemsRepeater` without explicit key function.
- **`XAML_PERF004`** — Effect or shadow applied to a template root (cost scales with list length).
- **`XAML_PERF006`** — Use of a `Windows.UI.*` type (notably `Windows.UI.Colors`, `Windows.UI.Text.FontWeights`) that transitively loads `Windows.UI.Xaml.dll` into a WinUI 3 app. The rule flags references from C# and XAML, names the loaded assembly, and offers a code fix to the `Microsoft.UI.*` equivalent. Pure Roslyn + XAML static analysis — the symbol's defining assembly tells us everything we need. Highest-confidence, lowest-cost rule in the set; observed in production 1P apps on the hot path.
- **`XAML_PERF007`** — Duplicate `ResourceDictionary` in a `MergedDictionaries` graph. Driven by §9.3's resource-dictionary census. Reports each dictionary reached by more than one path with all paths listed, because the offending include is often in a different file than the dictionary itself (e.g., one path via `package.appxfragment`, another via an explicitly merged `*Resources.xaml`). Best-effort when inclusions cross compilation units; the runtime signposts in §9.4 cover the residual case.

All six are XAML-general. Shipped under the WinUI org.

### 9.8 `xamlperf` CLI + canonical JSON export

Framework-neutral CLI for capture / analyze / compare / query over the WinUI perf ETW session (§13). Ships under Windows App SDK.

### 9.9 MCP server

Model Context Protocol server over §9.8's schema. Ships under Windows App SDK.

## 10. Reactor Extensions

The narrow set of capabilities that genuinely require Reactor's render loop and cannot be WinUI-native:

1. **Component Intent kind.** A fifth kind added to §6's model (`Component`), identified by the reconciler's stable component key, surviving remounts that would break element pointer identity.
2. **Component-tree rollups.** A logical parent chain distinct from the visual tree; costs roll up the component tree so "ProductCard spent 3 ms this frame" means the component, not its root `Border`.
3. **Reconciler-aware metrics.** Re-render count per component, memoization hit rate, re-renders that produced byte-identical output. Spec 007's `PerfTracker` becomes the first emitter.
4. **`REACTOR_PERF005`** — component with `>N SetState` calls per render. The only rule in §9.7 that doesn't generalize.
5. **Single-function render cost boundary.** Wall-time attribution crisper than WinUI can do because Reactor has one `Render()` per component; the Reactor-specific tooling view shows "Render()" as its own row.

Everything else — overlay, CLI, MCP, analyzer, compile-time report, IntentId, signposts, dependency-graph API, synthetic workload catalog — is consumed from WinUI unchanged. The Reactor extensions are an additional `EventSource` that emits Component-kind intents into the same schema. Tools read them through the same JSON.

This is deliberately small. If the full-framework story lands, Reactor's perf story is "plus five bullets," not a parallel universe.

## 11. Attribution Mechanism — Options

The IntentId carrying convention (§7.2) needs a mechanism to flow the Intent from element/binding/component creation down to cost-emitting events. Three options. Pros and cons below; no final pick in this spec — that's phase 0.

### Option A — AsyncLocal<IntentId>

Store the active Intent in an `AsyncLocal<IntentId>` (`ThreadLocal`-equivalent that follows async flow). Framework code reads the current value; signposts include it.

**Pros**
- Trivial to implement. No framework changes beyond the reader at each signpost site.
- Works across `await` boundaries.
- Zero wire format changes — ID is just a field in existing events.

**Cons**
- Breaks whenever work is dispatched to a thread pool or native thread without AsyncLocal propagation (composition, DX, DWM — i.e., the layers we most want to attribute to).
- Native code cannot participate; we lose attribution as soon as we cross the COM boundary into Xaml.dll.
- Easy to "lose" the Intent in subtle ways (custom `TaskScheduler`, fire-and-forget tasks).
- Not extensible down-stack without a second mechanism.

### Option B — Correlation Vector (first-class ID field through events)

Every ETW/TraceLogging event gains an `IntentId` field. The framework explicitly sets the active Intent before calling into lower layers; when the lower layer emits (composition commit, DX frame present), it reads back the current Intent and tags its event. Think of it as the MS-internal "Correlation Vector" pattern used for distributed tracing, applied to the render stack.

**Pros**
- Works across thread, process, and managed/native boundaries — the ID is data, not ambient state.
- Lines up directly with how `os_signpost` attributes in Apple's stack and how Perfetto flow events work in Google's.
- Extensible: future ETW providers (DWM, DX) can opt in by adding the field, without changing the capture path.
- Tooling (WPA, PerfView) can filter/group by the field with standard operators.

**Cons**
- Requires framework cooperation at every layer we care about. Promoting `Microsoft.UI.Xaml.Controls.Perf` to a documented schema is phase 1 and is inside our reach; lighting up DWM is a multi-release ask.
- Needs a cheap "current Intent" store at each layer; for native layers, that's a TLS slot.
- Event schema becomes part of the contract — breaking changes cost.

### Option C — Out-of-band sampling + post-hoc attribution

No ID propagation. Sample stacks at high rate, emit Intent lifecycle events (Intent N started at T1, ended at T2), and post-process: a sample at time T belongs to whichever Intent was active at T on that thread.

**Pros**
- Zero framework changes. We only need to emit lifecycle events.
- Works identically for managed and native code — sampling is profiler-level.
- Easy to bolt onto existing ETW captures (the Time Profiler pattern).

**Cons**
- Lossy. Stack sampling at 1 kHz misses short work; short Intents (a 0.3 ms `Measure`) won't get a single sample.
- Multithread attribution is ambiguous — if two Intents are active on different threads at the same moment, a cost on a third thread is unattributable without heuristics.
- Does not solve design-time estimates or live overlay (both need per-Intent accounting, not aggregate statistics).

### Recommendation (non-binding for this spec)

Ship **A for v1** because it's cheap and unblocks the overlay + design-time analyzer. Plan to migrate to **B** as WinUI formalizes `Microsoft.UI.Xaml.Perf`. Use **C** opportunistically, always — it's complementary: sampled stacks are a second data source that doesn't cost propagation.

## 12. Synthetic Workload Strategy

The "three drop shadows cost 12 ms" claim requires a reference. We have no synthetic workload infrastructure today; StressPerf (spec 007) is a single grid scenario.

### 12.1 Workload catalog

A small, declarative catalog of WinUI archetypes. v1 candidates:

- `shell` — NavigationView + 5 pages, basic content.
- `feed` — 500-item virtualized list, image + title + subtitle rows.
- `dashboard` — 10 charts + 20 live-updating cards.
- `editor` — rich text surface + side panel + command bar.
- `grid` — 4,800-cell stress grid (StressPerf from Reactor, contributed upstream).

Each archetype is a WinUI app in `samples/perf-workloads/` under WinUI, parameterized (`--items N`, `--effects low|med|high`), driven by `--headless --percent P --duration D`. Reference frame cost measured on reference machines (ARM64 Surface laptop, x64 desktop, x64 low-spec laptop) lives in a `PerfReferenceNumbers.json` alongside each sample.

### 12.2 Design-time estimator, v1 — lookup table

Lookup table keyed on element-kind × effect-count × repeater-size, built from the workload catalog's measurements. Hovering a XAML element in VS shows the nearest-neighbor estimate with explicit uncertainty ("±30%, based on 12 measurements on 3 devices"). The lookup table lives in the `Microsoft.UI.Xaml.Analyzers` NuGet so the IDE has it without extra setup.

Deliberately dumb. The goal of v1 is to *prove the loop* — design-time feedback exists, devs respond to it — not to be accurate.

### 12.3 Real-device telemetry, v2 — the MetricKit-style loop

Consumer apps opt into anonymized perf telemetry. Frame costs aggregated by IntentId / element PerfTag, device class, OS version flow back into the estimator's lookup table. Privacy design lifts from MetricKit: local aggregation, differential privacy fuzzing, minimum-N thresholds, no device IDs, opt-in per-consumer-app.

This is the ambitious piece. **Nobody has shipped an IDE overlay driven by real-device telemetry from your own consumer app.** Apple has the parts.

### 12.4 Seeding

Phase 1: WinUI team writes and measures the archetypes. Reference numbers in-repo.
Phase 2: consumers contribute via PR, gated on a CI measurement harness on a known device.
Phase 3: real-device telemetry supersedes synthetic numbers where it has signal; synthetic remains the floor for archetypes with no telemetry.

## 13. AI-Agent Interface

Treating agents as a first-class consumer is the biggest leverage point in this spec. **If an agent has to parse a `.etl` file or screen-scrape WPA to answer a perf question, we have failed.**

### 13.1 Surface — `xamlperf` CLI

Ships under Windows App SDK. Subcommands:

- `xamlperf capture --duration 30s --out trace.json` — runs the active app, captures an Intent-tagged trace from the WinUI perf ETW session, writes canonical JSON.
- `xamlperf analyze trace.json` — ranked hot Intents with suggested next actions (e.g., "element `Views/Cart.xaml:82#Border` re-measured 600× in 10s; consider `CacheMode`").
- `xamlperf compare before.json after.json` — diffs two traces, flags regressions and improvements by Intent.
- `xamlperf query trace.json '<expr>'` — small expression language (`intent.kind == 'Element' && wall_ms > 2`).

Reactor apps invoke the same CLI; the Reactor event source is just another producer feeding the same schema.

### 13.2 Canonical JSON schema (sketch)

```json
{
  "trace_version": "1.0",
  "captured_at": "2026-04-17T15:02:00Z",
  "duration_ms": 30000,
  "device": { "arch": "arm64", "os": "10.0.26100", "model": "Surface Laptop 7" },
  "intents": [
    {
      "id": "intent/0042",
      "kind": "Element",
      "name": "Border",
      "perf_tag": "Views/Cart.xaml:82:5#Border",
      "source": { "file": "Views/Cart.xaml", "line": 82 },
      "parent_id": "intent/0011"
    },
    {
      "id": "intent/0099",
      "kind": "Component",
      "name": "ProductCard",
      "source": { "file": "Views/Cart.cs", "line": 82 },
      "parent_id": "intent/0042"
    }
  ],
  "costs": [
    { "intent_id": "intent/0042", "metric": "wall_ms", "window": "frame/1203", "value": 3.4 }
  ],
  "frames": [
    { "id": "frame/1203", "start_ms": 1204.2, "duration_ms": 19.1, "hitched": true, "top_intents": ["intent/0042", "intent/0017"] }
  ]
}
```

Schema versioned in `docs/reference/perf-schema.md` under Windows App SDK. Additive within a major version.

### 13.3 MCP server (stretch)

Model Context Protocol server exposing capture/analyze/compare as structured tools.

### 13.4 Why this is a differentiator

Perfetto's SQL is the closest architectural analog and was not designed for agents — Google is retrofitting Gemini onto it now. Designing the surface around agent consumption from day one (stable schemas, named metrics, explanatory text built into analysis output, not just numbers) is a credible first-in-industry claim for a major UI stack.

## 14. Extensibility Down the Stack

The spec's scope is the WinUI/XAML layer. Every architectural choice above is made assuming we will later attribute composition, DX, and DWM events to the same Intents:

- **Option 11.B (Correlation Vector)** is the only mechanism that survives the boundary into native layers. When we control those layers (we do, organizationally), we add `IntentId` to their existing providers' events.
- **JSON schema (§13.2)** has open-ended `kind` and `metric` fields, so composition and DX events (`commit_ms`, `gpu_encode_us`) slot in without churn.
- **§9 capabilities** have no layer affinity — the overlay grows a "composition" panel; the analyzer grows composition rules.

Sequencing: WinUI/XAML phase 1 establishes the contract and proves the value; composition + DWM participation is a separate spec once the contract is stable. Apple's advantage accrued over many years of incremental framework additions against a stable substrate. Our stable substrate is the IntentId + JSON schema.

## 15. Success Metrics

The metric the spec optimizes for is **time to first insight** — how long from "my app feels slow" to "the developer knows which element or component to fix." Current baseline: hours to days. Target:

- **P1 (overlay shipped):** < 30 seconds. Open app in debug, see overlay, see offender.
- **P2 (design-time analyzer):** < 0 seconds — the warning is in the IDE before the app runs.
- **P3 (agent loop):** < 2 minutes, no human intervention — agent captures, analyzes, proposes patch.

Secondary metrics:

- Overlay steady-state CPU overhead < 1% on reference device.
- Trace JSON size for 30s capture < 5 MB uncompressed.
- Analyzer false-positive rate < 10% (measured on `samples/perf-workloads/`).
- Fraction of shipped WinUI apps (and, separately, Reactor apps) where the default overlay would have surfaced a known perf issue the team already knows about.

## 16. Open Questions

1. **How much of §9 lands in `microsoft/microsoft-ui-xaml` vs. user-space?** The spec's whole thesis is "WinUI first." A phase-0 prototype spike must answer: which §9 items can we prototype in user-space to prove the design, vs. which are architecturally WinUI-only (compiler, internal provider)?

2. **Do we promote `Microsoft.UI.Xaml.Controls.Perf` to a documented provider?** It exists, it's on, it has ad-hoc schema per control author. Documenting a subset and stabilizing is the cheapest win. Coordinate with WinUI team.

3. **Does `System.Diagnostics.Signpost` belong in this spec or its own?** It's a .NET BCL proposal that outlives XAML. Probably separate spec + dotnet/runtime issue, with this spec as the first consumer and justification.

4. **Custom controls in the design-time estimator.** Lookup covers known elements; a custom `ProductCard` has no entry. Options: estimate from template, estimate from first-run telemetry, say "unknown" honestly. v1: honest-unknown.

5. **Does the overlay ship in Release builds?** Surface-area-vs-debuggability tradeoff. Default debug-only for v1.

6. **Privacy for §12.3 telemetry.** MetricKit is the reference; needs legal review before code.

7. **Where does spec 007's StressPerf / PerfTracker fit?** StressPerf becomes the first workload in §12.1, contributed upstream. PerfTracker becomes one emitter into the Reactor-extension EventSource (§10).

8. **`xamlperf watch` — live stream?** Probably deferred; the §9.5 overlay covers the same need cheaper.

9. **Who curates "suggested next action" rules in §13.1?** A small table owned by the WinUI team for v1. Not a general rule engine.

10. **Non-render-loop perf issues that this spec deliberately does not catch.** Some known-wins are invisible to an Intent→cost model because they are not render work — e.g., unpackaged C++ apps failing to opt into the Segment Heap (manifest/linker switch), binplaced CRT version, or app-manifest startup-task misconfiguration. §9.5's working-set-delta makes the *symptom* visible but not the *fix*. The right home is a separate `Microsoft.WindowsAppSDK.Analyzers` / project-template-default effort. Calling it out here so reviewers don't expect this spec to absorb it.

## 17. Implementation Phases

### Phase 0 — Spike (2 weeks)

- Decide attribution mechanism A/B/C via prototype.
- Prototype §9.1 `System.Diagnostics.Signpost` in user-space to prove the API shape.
- Prototype §9.2 `PerfTag` synthesis in a fork of the XAML compiler; measure build-time cost.
- Fix `DebugSettings.EnableFrameRateCounter` AV on WinUI 3 Desktop, or prove we must ship our own.
- Inventory `Microsoft.UI.Xaml.Controls.Perf` events; coordinate with WinUI team on formalizing a subset.
- Working minimal overlay against the StressPerf grid.

### Phase 1 — WinUI-native Intent runtime + overlay (4–6 weeks)

- `System.Diagnostics.Signpost` in .NET BCL (separate spec + dotnet/runtime PR).
- Formalized `Microsoft.UI.Xaml.Perf` schema (documented, stable).
- Default-on Measure/Arrange/Render/Binding.Update signposts (§9.4).
- `PerfTag` in XAML compiler (§9.2).
- `DebugSettings.PerfOverlay` (§9.5).
- JSON trace export and `xamlperf capture` / `xamlperf analyze` CLI (§13).
- Workload catalog under WinUI (§12.1), with StressPerf contributed from Reactor.

### Phase 2 — Compile-time, design-time, agent surfaces (6–8 weeks)

- `PerfReport.json` from the XAML compiler (§9.3).
- `x:Bind` complexity annotations and `DataTemplate` recyclability verdict.
- `Microsoft.UI.Xaml.Analyzers` NuGet with PERF001–PERF004, PERF006, PERF007 (§9.7).
- Lookup-table design-time estimator in VS + VS Code (§12.2).
- Public dependency-graph query API (§9.6).
- `xamlperf compare` for regression diffs.
- MCP server (stretch).
- **Reactor extensions shipped in parallel** (§10): Component Intent kind, reconciler EventSource, PERF005, component-tree rollups.

### Phase 3 — Down-stack + real telemetry (open-ended)

- IntentId correlation added to composition + DWM event schemas.
- Real-device telemetry opt-in and aggregation (§12.3). Legal review gate.
- Perfetto protobuf export (§13 stretch).
- Overlay "composition" and "GPU" panels.

Each phase independently shippable. No long-dark phase.
