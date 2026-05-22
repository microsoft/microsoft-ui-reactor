# 045 — Docking Windows

| | |
|---|---|
| **Status** | Draft — 2026-05-19 |
| **Owner** | @codemonkeychris |
| **Related** | [036](036-window-design.md) (Window primitive), [027](027-input-and-gestures-design.md) (input), [011](011-navigation-design.md) (routing), upstream [WinUI.Dock by qian-o](https://github.com/qian-o/WinUI.Dock) (MIT), [`ThirdPartyNoticeText.txt`](../../ThirdPartyNoticeText.txt) |
| **Phasing** | P1 vendor + wrap (XAML) → P2 Reactor-native rewrite → P3 fold into the [Window primitive](036-window-design.md) → P4 Windows 11 native-chrome polish |

## 1. Summary

Microsoft.UI.Reactor (Reactor) today has a single-window model evolving into a multi-window topology under [spec 036](036-window-design.md). What it does *not* have is a way to put **multiple, user-rearrangeable surfaces inside one shell** — the Visual Studio / VS Code / Photoshop / Blender / Figma layout idiom where users drag tabs around, split panes, pin tool windows to a side, and tear panes out into floating sub-windows. This is the missing-stair feature for "real desktop app" use cases: IDEs, editors, monitoring dashboards, technical artist tools, and the bulk of the WinForms/WPF apps Reactor is meant to court.

This spec lays out a three-phase plan to land a first-class docking system, ultimately fused with the Window primitive so that a `DockableWindow` is a kind of `Window`, a `DockHost` is a Reactor element that can adopt any Window into a tab/pane, and dragging a tab out of a host produces a real top-level Reactor `Window`. The phasing exists to **decouple the layout/interaction risk from the API risk**: phase 1 vendors a working, battle-tested implementation (WinUI.Dock) so we can ship a showcase, iterate on UX with real humans, and pin down the interaction model before we touch the reconciler.

The key non-negotiable: **interaction quality is the product**. A docking system that crashes on tear-out, drops the wrong tab order, snaps back unexpectedly, or has 100 ms preview lag is worse than no docking system at all. Every phase exit requires a human-in-the-loop review of interactivity against the showcase sample.

## 2. Goals / non-goals

### 2.1 Phase-aggregate goals

- **G1.** Reactor apps can express IDE-class docking layouts (documents, tool windows, splits, tab groups, auto-hide sides, floating tear-outs, layout persistence) in idiomatic Reactor C#.
- **G2.** Floating panes are first-class Reactor `Window`s once phase 3 lands — same persistence, same DPI story, same shell integration (taskbar, tray, jumplist) as any other Reactor Window.
- **G3.** Layouts are serializable (JSON), versionable, and restorable across sessions. Per-pane content state composes with [§8 Persistence in spec 036](036-window-design.md#8-persistence) and the existing `WindowPersistedScope`.
- **G4.** Interaction parity with Visual Studio docking on the showcase scenarios. Drop targets, preview overlays, drag thresholds, snap-back, and cross-window drop are reviewed by a human against the original WinUI.Dock sample at every phase boundary (review gates in §4.7, §5.7, §6.8, §7.4).
- **G5.** No daylight between the public API at phase 2 exit and phase 3 entry — phase 3 is a deepening of the model (`DockHost` becomes capable of adopting top-level Windows), not a rewrite.
- **G6.** Attribution and licensing for vendored WinUI.Dock code complies with its MIT license (notices preserved in `ThirdPartyNoticeText.txt`; per-file headers retained where present).

### 2.2 Non-goals

- **N1.** Touch-first docking gestures (pinch-to-tile, hold-and-drag tablet flow). Not in any phase. WinUI 3's drag/drop is mouse-or-pen-shaped; touch tear-out is a separate input spec.
- **N2.** Workspaces / layout presets UI (the "save this as 'Debug Layout'" menu in VS). The serialization primitives are in scope; the UI to manage *named* layouts is out.
- **N3.** Cross-process docking (drag a tab from app A into app B). Hard, security-sensitive, and outside the WinUI.Dock model.
- **N4.** Web/Uno parity. Reactor is WinUI 3 desktop-only (spec 036 N1). The upstream WinUI.Dock has Uno support; we will strip those code paths in vendoring (§4.4).
- **N5.** Re-implementing TabView. We use WinUI's `TabView` in phase 1 (transitively, through WinUI.Dock) and intend to keep using it through phase 2; phase 3 may replace it as part of native rendering, but that's a layout/perf call, not an architectural goal.
- **N6.** Inspectability of *drag state* via devtools/MCP. Static layout introspection is in scope (§8.2); intercepting an in-flight drag is not.

## 3. Prior art

WinUI.Dock is the starting point because (a) it's already WinUI 3 native, (b) it has the right shape (Visual Studio-style layout with floating tear-out), (c) it's MIT, (d) it's small enough (~30 source files) that a full vendoring + rewrite cycle is realistic, and (e) it credits AvalonDock + ImGui as inspirations — both of which represent the two dominant docking philosophies in the wider ecosystem.

This section catalogs every major docking system across desktop and web frameworks so we know what we're benchmarking against, what we're choosing *not* to do, and where the genuine gaps in the WinUI.Dock baseline are.

### 3.1 Feature matrix

Legend: ✅ first-class · 🟡 partial / via extension · ❌ absent · n/a · ? unverified.

| Feature | WinUI.Dock | AvalonDock (WPF) | VS XAML (closed) | DockPanelSuite (WinForms) | Telerik Docking (WPF/WinUI) | DevExpress DockLayout (WPF) | Syncfusion Docking | Qt ADS | Qt QDockWidget | GoldenLayout (web) | dockview (web React) | FlexLayout (web) | Mosaic (web React) | rc-dock | Flutter `docking` | Compose Desktop | SwiftUI NavigationSplitView |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Documents vs tool windows | ✅ (Document only — single role) | ✅ (LayoutDocument / LayoutAnchorable) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 (tool only) | 🟡 (componentName) | ✅ | 🟡 | ❌ (tiles only) | ✅ | ✅ | ❌ | ❌ |
| Tab groups | ✅ (DocumentGroup) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | 🟡 (TabRow) | ❌ |
| Recursive splits | ✅ (LayoutPanel) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 (manual) | 🟡 (3-col only) |
| Splitter resize w/ snap | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | 🟡 |
| Visual Studio-style drop targets (cross overlay) | ✅ | ✅ | ✅ | 🟡 (basic) | ✅ | ✅ | ✅ | ✅ | ❌ | 🟡 (highlight bands) | ✅ | 🟡 | ❌ | ✅ | 🟡 | ❌ | ❌ |
| Drop preview / phantom | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | 🟡 | ❌ | ✅ | 🟡 | ❌ | ❌ |
| Tear-out → floating window | ✅ (FloatingWindow) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 (popout window) | ✅ | ❌ | ❌ | ✅ | 🟡 (separate window) | ❌ | ❌ |
| Cross-window drop | 🟡 (known fragility, README note) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Cross-monitor + per-monitor DPI | 🟡 (inherits WinUI) | ✅ | ✅ | 🟡 (system DPI) | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | n/a | n/a | n/a | n/a | 🟡 | 🟡 | ✅ |
| Auto-hide / pin-to-side | ✅ (LeftSide/TopSide/RightSide/BottomSide + SidePopup) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ❌ | 🟡 | ❌ | ❌ | 🟡 | ❌ | ❌ | ❌ |
| Multiple floating windows per shell | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ |
| Per-tab close button | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Per-tab pin button | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | 🟡 | ❌ | ❌ | ❌ |
| Compact / icon-only tabs | ✅ (CompactTabs) | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | 🟡 | ❌ | ❌ | 🟡 | ❌ | ❌ | ❌ |
| Bottom-tab orientation | ✅ (TabPosition.Bottom) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | 🟡 | ❌ |
| Custom floating-window title bar | ✅ (IDockAdapter.GetFloatingWindowTitleBar) | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | 🟡 | ❌ | ✅ | 🟡 | ❌ | n/a |
| Layout serialization | ✅ (JSON, internal `JsonObject`) | ✅ (XML) | ✅ (XML) | ✅ (XML) | ✅ (XML/binary) | ✅ (XML/JSON) | ✅ | ✅ (XML/binary) | ✅ (QByteArray) | ✅ (JSON) | ✅ (JSON) | ✅ (JSON) | 🟡 | ✅ (JSON) | ✅ | ❌ | ❌ |
| Per-pane content-state hook for save/restore | ❌ (Adapter.OnCreated re-builds) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ (componentState) | ✅ | ✅ | 🟡 | ✅ | 🟡 | n/a | n/a |
| MVVM / data binding | 🟡 (README: "limited MVVM support") | ✅ | ✅ | ❌ (Forms) | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | n/a | n/a | n/a | n/a | ✅ | n/a | ✅ |
| Programmatic dock(target) API | ✅ (`Document.DockTo`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | n/a |
| Drag/drop accessibility (kbd/AT) | 🟡 (TabView a11y; drop targets not exposed) | 🟡 | ✅ | 🟡 | ✅ | ✅ | ✅ | 🟡 | 🟡 | ❌ | 🟡 | ❌ | ❌ | 🟡 | ❌ | ❌ | ✅ |
| Keyboard navigation between panes | 🟡 | ✅ (Ctrl+Tab `NavigatorWindow`) | ✅ (Ctrl+Tab) | 🟡 | ✅ | ✅ | ✅ | 🟡 | 🟡 | 🟡 | ✅ | 🟡 | 🟡 | 🟡 | ❌ | ❌ | ✅ |
| Theming (dark mode, accent) | ✅ (XAML themes) | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ |
| Layout versioning + migration | ❌ | 🟡 (manual) | ✅ | 🟡 | ✅ | ✅ | ✅ | 🟡 | 🟡 | 🟡 | 🟡 | ❌ | ❌ | 🟡 | ❌ | n/a | n/a |
| Modal floating panes | ❌ | ❌ | ❌ | ❌ | 🟡 | 🟡 | 🟡 | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | n/a | n/a |
| Native window chrome on float | ✅ (OverlappedPresenter, no caption) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | n/a | n/a | 🟡 | ✅ | n/a | n/a |
| Always-on-top floating | ❌ | 🟡 | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | 🟡 | n/a | n/a | n/a | 🟡 | n/a | n/a |
| Touch input (drag tab w/ finger) | ❌ | ❌ | 🟡 | ❌ | 🟡 | 🟡 | 🟡 | 🟡 | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | ✅ (iPad) |
| Animation on drop / split | 🟡 (visual state) | 🟡 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | 🟡 | ❌ | ✅ | 🟡 | 🟡 | ✅ |
| Multi-monitor floating restore | 🟡 | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | n/a | n/a | n/a | n/a | n/a | 🟡 | 🟡 | ✅ |
| AOT-clean (no reflection) | 🟡 (JsonSerializerContext, mostly) | ❌ | n/a | ❌ | ❌ | ❌ | ❌ | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | ✅ | ✅ |
| OSS license | MIT | Ms-PL | proprietary | MIT | proprietary | proprietary | proprietary | LGPL/Comm | LGPL | MIT | MIT | Apache 2 | MIT | MIT | MIT | Apache 2 | proprietary |

### 3.2 What we learn from the matrix

1. **No mainstream framework outside the .NET/Qt incumbents has this.** The web ecosystem caught up only recently (GoldenLayout, dockview). SwiftUI has nothing close — `NavigationSplitView` is a 3-column layout, not a layout *engine*. Compose Desktop has no docking package at all. This is a genuine differentiator for Reactor versus the cross-platform competitors.
2. **The "tool window vs document" distinction is universal.** WinUI.Dock collapses these into one `Document` type with a `CanPin` flag. This is a real gap; we'll restore the distinction in phase 2 (§5.3.1) — `Document` and `ToolWindow` (or `Pane`) as separate roles, sharing a common base.
3. **MVVM/data-bound layout is the biggest WinUI.Dock weakness — but the right fix for Reactor is *not* to add a binding API.** Their README admits the gap. AvalonDock fills it with `DocumentsSource` / `AnchorablesSource` collections paired with `LayoutItemTemplate` / `LayoutItemContainerStyle` — bind a `IEnumerable<TViewModel>`, get one tab per item, content rendered via the template. **We don't reproduce that pattern at all.** Reactor's functional composition *is* the data-to-tree mapping: the `DockNode` tree returned from a component's `Render(ctx)` is already a function of state. Building a separate `DocumentsSource` API would be a parallel data-binding pipeline that competes with the reconciler instead of using it. The fix for Reactor is to make the docking model a clean Reactor element tree (record types, keyed reconciliation, hooks for state) and let composition do the rest (§5.3.7, §5.3.11).
4. **Per-pane state restore (the "componentState" GoldenLayout idiom) is a feature we should add that WinUI.Dock lacks.** WinUI.Dock's `IDockAdapter.OnCreated` rebuilds content from scratch; per-pane content state isn't part of the serialized layout. AvalonDock has the `LayoutSerializationCallback` pattern (deserialization raises a callback per restored pane, app supplies content matched by `ContentId`). We extend serialization to include a per-pane state blob in phase 2 (§5.4) *and* provide the AvalonDock-style rehydration callback (§5.3.7).
5. **Touch is universally weak in desktop docking.** We accept that as non-goal N1 and don't pretend otherwise.
6. **Keyboard accessibility is mixed.** VS does it well; AvalonDock has a `NavigatorWindow` (Ctrl+Tab) modeled after VS. We replicate that in phase 2 (§5.3.3).
7. **Layout versioning is mostly absent.** We add a layout schema version from day one (§5.4.4) so we don't paint ourselves into a corner the way most of the field has.
8. **Layout-as-model, not layout-as-control-state.** AvalonDock's deepest architectural lesson: `LayoutRoot` / `LayoutPanel` / `LayoutDocumentPaneGroup` / `LayoutAnchorablePane` are `IXmlSerializable` POCOs that the controls render. The layout is queryable (`Descendents()`), mutable, and persistable independent of any UI. WinUI.Dock conflates the two by making everything a `Control` with a control template. **For Reactor this is even more natural** — our `DockNode` algebra (§4.3) is already a value-typed tree of records. Phase 2 makes this concrete by exposing the model as the public source of truth (§5.3.10).
9. **Cancellable lifecycle events at every transition.** AvalonDock has ~12 events covering `LayoutChanging` / `LayoutChanged`, `DocumentClosing` / `DocumentClosed`, `AnchorableHiding` / `AnchorableClosing` / `AnchorableClosed`, `ContentFloating` / `ContentFloated`, `ContentDocking` / `ContentDocked`, `ActiveContentChanged`, `LayoutFloatingWindowControlCreated` / `Closed` — each `*ing` variant is cancellable. WinUI.Dock has three fire-and-forget callbacks. We adopt the full AvalonDock event surface in phase 2 (§5.3.5).
10. **Memory of where panes came from.** AvalonDock tracks `PreviousContainer` / `PreviousContainerId` per content — when you `Hide()` an anchorable and later `Show()` it, it returns to the same container, not a default location. We adopt this in phase 2 (§5.3.9). It's the difference between "show the Output window" jumping it to a random spot vs. landing where the user left it.
11. **Insertion-policy injection.** AvalonDock's `ILayoutUpdateStrategy` lets apps intercept "where should a new anchorable or document be inserted" before insertion happens — the app can route specific content to specific containers (e.g., "anything matching `Error*` goes to the bottom group"). Phase 2 surfaces this as `IDockLayoutStrategy` (§5.3.6).
12. **Fine-grained per-pane permissions.** AvalonDock distinguishes `CanClose` from `CanHide` (anchorables hide rather than close by default — close removes from tree, hide collapses to side), plus `CanAutoHide`, `CanDockAsTabbedDocument`, `CanMove`, `CanFloat`. WinUI.Dock has just `CanClose` / `CanPin`. Phase 2 expands the permission set (§5.3.8).

### 3.3 Notes on individual prior art

- **AvalonDock** (Dirkster99 fork, Ms-PL, ~150 source files). The canonical WPF docking library, and the architectural reference we lean on most heavily for phase 2. Reviewed in-tree at `C:\Users\andersonch\Code\AvalonDock\source\Components\AvalonDock\`. **Architecture:** layout is a model — `LayoutRoot`, `LayoutPanel` (split), `LayoutDocumentPaneGroup` / `LayoutDocumentPane` (document tab area), `LayoutAnchorablePaneGroup` / `LayoutAnchorablePane` (tool window area), `LayoutAnchorGroup` (auto-hide side), `LayoutDocumentFloatingWindow` / `LayoutAnchorableFloatingWindow` — all `IXmlSerializable` POCOs that the `DockingManager` control renders against. `LayoutDocument` / `LayoutAnchorable` derive from `LayoutContent` and carry `Title`, `ContentId`, `Content` (the rehydrated view), plus per-content permission flags. **Key surfaces to steal/translate:**
  - `LayoutDocument` (closable, lives in `LayoutDocumentPane`) vs `LayoutAnchorable` (hideable, lives in `LayoutAnchorablePane`, can be auto-hidden to a side) — the role split we adopt as `Document` vs `ToolWindow` (§5.3.1).
  - `LayoutSerializationCallback` (`XmlLayoutSerializer.LayoutSerializationCallback`) — the deserialization-time event raised once per restored pane so the app can supply or refuse content keyed by `ContentId`. We adopt this (§5.3.7).
  - `ILayoutUpdateStrategy` — `BeforeInsertAnchorable` / `AfterInsertAnchorable` / `BeforeInsertDocument` / `AfterInsertDocument`. App-supplied policy hook the manager calls before deciding where new content lands. We adopt as `IDockLayoutStrategy` (§5.3.6).
  - `DocumentsSource` / `AnchorablesSource` / `LayoutItemTemplate` / `LayoutItemContainerStyle` — AvalonDock's MVVM binding surface. **We deliberately do not adopt this.** Reactor's functional composition replaces it: a parent component computes the `DockNode` tree from its own state every render; the reconciler diffs keyed children; that's the entire binding story. See §5.3.7 for the rationale.
  - `NavigatorWindow` (Ctrl+Tab) — VS-style pane navigator. We adopt (§5.3.3).
  - `PreviousContainer` / `PreviousContainerId` on `ILayoutPreviousContainer` — pane remembers its last container; reshowing it returns it home. We adopt (§5.3.9).
  - Cancellable event surface (`Closing` / `Hiding` / `Floating` / `Docking` with `CancelEventArgs`) — we adopt the full set (§5.3.5).
  - `DropTargetType` enum with explicit area kinds (`DockingManagerDockLeft` / `DocumentPaneDockInside` / `AnchorablePaneDockTop` / `DocumentPaneDockAsAnchorableRight` / …) — note the **last four**, which let a document area accept tool-window-style edge docks. WinUI.Dock fuses these into one 9-value `DockTarget` enum. We follow WinUI.Dock's simpler scheme but document the AvalonDock-style edge-of-document-area-becomes-anchorable behavior as a phase-2 enhancement.
  - Theme architecture (separate `AvalonDock.Themes.{Aero,Arc,Expression,Metro,VS2010,VS2013}` assemblies, each just a `ResourceDictionary`). We don't reproduce this surface area — Reactor theming (spec 001) covers it generically — but the precedent informs §8.4.
  - Test architecture: **FlaUI-driven UI automation** (`AvalonDockTest/FlaUI/`). They have automated tests for floating-window drag, drop targets, document lifecycle, keyboard navigation, layout serialization, smoke, stress, tool window visibility. We do *not* adopt FlaUI; the scenario list informs our selftest matrix (§8.3).
- **DockPanelSuite** (weifenluo, WinForms). The Visual-Studio-look-alike that ships with NuGet packages used by countless Forms apps. **What to steal:** nothing API-shaped (Forms idioms don't translate), but the *interaction model* — drop preview placement, drag thresholds, snap-back — is the gold standard humans have internalized for 20 years. **Phase boundaries reference this for interaction QA.**
- **WinUI.Dock** (qian-o). Our baseline. ImGui-inspired (`DockTarget.Center/SplitLeft/...`) overlay model, AvalonDock-inspired structural model. Strengths: clean XAML, JSON serialization with `JsonSerializerContext` (AOT-clean for primitives), tear-out + side-pin both work. Weaknesses: MVVM gap, no Documents/Tools distinction, no per-pane state, no layout versioning, no a11y on drop targets, cross-window DnD known fragile.
- **Qt Advanced Docking System (ADS)** (githubuser0xFFFF). Probably the most feature-complete docking system in any framework. Per-monitor DPI, cross-window drag, accessibility, AutoHide, central widget mode, central document area. **What to steal:** "central widget" pattern (a non-dockable always-present pane), the AutoHide gesture details, "perspective" save/load.
- **GoldenLayout** (deepstreamIO, then community). The dominant web docking library. JSON layout, componentState per pane (lesson #4 above). **What to steal:** componentState pattern, popout-window restoration on app reload.
- **dockview** (mathuo). Modern React/Vue/Vanilla docking. Best-in-class web a11y. **What to steal:** keyboard navigation patterns.
- **Telerik / DevExpress / Syncfusion**. Commercial, proprietary, full-featured. Used as feature-checklist references; we don't copy any code.
- **Visual Studio itself**. Closed-source, but its interaction model is what every other docking library is trying to clone. The phase-1 sample app is explicitly meant to be VS-feeling.
- **VS Code workbench**. Inspired by GoldenLayout-ish ideas but custom. Strong activity-bar / side-bar pattern but it's not a *general* docking system — it's bespoke for VS Code's UX. Not in matrix as a separate row because it's not a library.
- **JetBrains IDEA's docking** (private). Tool windows, document tabs, drag-to-rearrange, attach-to-monitor. **Aspirational interaction quality target alongside VS.**
- **Blender**, **Photoshop**. Different UX paradigm (workspace presets dominate) but excellent tear-out. Not directly in scope but referenced for review.

## 4. Phase 1 — vendor + wrap (XAML)

**Goal of this phase:** ship a working docking experience to a Reactor sample inside the next release cycle. No new code design risk: we vendor WinUI.Dock as-is and write the thinnest possible Reactor wrapper element. Humans review the sample and the wrapped surface for interaction parity against the upstream Example.WinUI sample.

**Exit criteria, in order:** (a) showcase sample builds and runs; (b) every WinUI.Dock feature in §3.1 is demonstrated; (c) a designated human reviewer signs off after side-by-side comparison with `WinUI.Dock\src\Examples\Example.WinUI` running on the same hardware; (d) public API in §4.3 is committed to *as the phase-2 target*, no breaking changes from here to phase 2 exit.

### 4.1 Vendoring

- Create `third_party/WinUI.Dock/` at the repo root. Copy the entire `src/WinUI.Dock/` tree from the upstream snapshot (commit pinned in a `VENDORED.md` adjacent file). Preserve the upstream `LICENSE` file at `third_party/WinUI.Dock/LICENSE`.
- Strip Uno-platform code paths during the initial copy. WinUI.Dock has conditional compilation for Uno; we are WinUI 3 only (§2.2 N4). Remove `Uno`-targeted projects, multitargeting in `.csproj`, and any `#if HAS_UNO` / `#if !WINDOWS` branches.
- Repackage as `Microsoft.UI.Reactor.Docking.Xaml` assembly under `src/Reactor.Docking.Xaml/`. The vendored namespace `WinUI.Dock` is *kept* (so internal call sites don't churn) but type-forwarded into `Microsoft.UI.Reactor.Docking.Xaml.Internal` via `[TypeForwardedTo]` so external API references the Reactor namespace.
- Update `Reactor.slnx` to include the new project.
- Append the WinUI.Dock notice block to `ThirdPartyNoticeText.txt` (done in this PR — see §12).

### 4.2 Light edits permitted at vendor time

The choice in clarifying questions was "vendor as-is + light wrapper", not "vendor + edits". To honor that, the only edits we apply at vendor time are:

1. Remove Uno code paths (per §4.1).
2. Apply Reactor's `.editorconfig` formatting (whitespace only — no semantic change).
3. Add `[assembly: InternalsVisibleTo("Reactor.Docking.Xaml.Tests")]` to `Properties/AssemblyInfo.cs`.
4. Fix the documented "cross-window DnD races against window close" bug (WinUI.Dock README "Known Issues") if and only if upstream has not fixed it by vendor time — see §4.6 contingency.

No other behavior changes. Bug fixes beyond #4 are deferred to phase 2.

### 4.3 Reactor wrapper API (phase-1 surface)

This is the API Reactor app authors call. It's also the **commitment surface for phase 2** — phase 2 implements this same shape with no XAML behind it.

```csharp
namespace Microsoft.UI.Reactor.Docking;

// Top-level host. Phase 3 will rename to `DockHost` and make it adoptable as
// content of an arbitrary Window. Phase-1/2 instances embed inside one window.
public sealed record DockManager(
    DockNode? Layout = null,
    IReadOnlyList<DockableContent>? LeftSide = null,
    IReadOnlyList<DockableContent>? TopSide = null,
    IReadOnlyList<DockableContent>? RightSide = null,
    IReadOnlyList<DockableContent>? BottomSide = null,
    DockableContent? ActiveDocument = null,
    IDockAdapter? Adapter = null,
    IDockBehavior? Behavior = null,
    string? PersistenceId = null,
    int LayoutSchemaVersion = 1) : Element;

// Tree algebra. One of: split (LayoutPanel), tab group, or leaf content.
// Sealed hierarchy mirrors WinUI.Dock DockModule/DockContainer/Document.
public abstract record DockNode;

public sealed record DockSplit(
    Orientation Orientation,
    IReadOnlyList<DockNode> Children,
    double? Width = null, double? Height = null,
    double? MinWidth = null, double? MinHeight = null,
    double? MaxWidth = null, double? MaxHeight = null) : DockNode;

public sealed record DockTabGroup(
    IReadOnlyList<DockableContent> Documents,
    TabPosition TabPosition = TabPosition.Top,
    bool CompactTabs = false,
    bool ShowWhenEmpty = false,
    int SelectedIndex = -1,
    double? Width = null, double? Height = null) : DockNode;

// A single pane. The "Document" name in WinUI.Dock conflates documents and
// tool windows; we keep one type for phase 1 fidelity and split it in phase 2.
public sealed record DockableContent(
    string Title,
    Element? Content = null,
    object? Key = null,
    bool CanClose = false,
    bool CanPin = false,
    double? Width = null, double? Height = null,
    string? PersistenceState = null) : DockNode;

public enum TabPosition { Top, Bottom }
public enum DockTarget { Center, SplitLeft, SplitTop, SplitRight, SplitBottom,
                         DockLeft, DockTop, DockRight, DockBottom }

public interface IDockAdapter
{
    Element OnContentCreated(DockableContent content);
    void OnGroupCreated(DockTabGroupContext group, DockableContent? draggedSource);
    Element? GetFloatingWindowTitleBar(DockableContent? draggedSource);
}

public interface IDockBehavior
{
    void OnDocked(DockableContent src, DockTarget target);
    void OnFloating(DockableContent content);
    // ActivateMainWindow — absorbed into Reactor's window topology;
    // not exposed at the IDockBehavior surface.
}
```

Notes:

- **All records.** Reactor's reconciler diffs by value; the docking subtree reconciles like any other Reactor element.
- **`Key` on `DockableContent`.** Per [spec 042 keyed reconciliation](042-keyed-list-reconciliation-design.md), required for tab identity to survive reorderings. WinUI.Dock uses `Title` as an implicit key (with the `##` namespace hack) — we replace that with an explicit `Key`.
- **`PersistenceId`** on `DockManager` ties into the per-window `WindowPersistedScope` from spec 036. Layout JSON is written under that scope.
- **`LayoutSchemaVersion`** is the migration knob lesson #7 from §3.2.
- **`PersistenceState` on `DockableContent`** is the GoldenLayout-style per-pane blob (lesson #4). In phase 1 it's a plain string the adapter is free to interpret; in phase 2 we may type it via generics.

### 4.4 What the wrapper actually does

A `DockManager` element renders to a single `WinUI.Dock.DockManager` XAML control via a Reactor reconciler "leaf wrapper" pattern (precedent: `PropertyGridComponent`, `DataGridFactories`). The wrapper:

1. **First mount:** instantiates the upstream `DockManager`, walks the `DockNode` tree, and constructs the corresponding `LayoutPanel` / `DocumentGroup` / `Document` instances. Wires `IDockAdapter` and `IDockBehavior` thunk implementations that forward to the Reactor-side interfaces.
2. **Update:** runs a structural diff between the previous `DockNode` tree and the new one keyed on `DockableContent.Key`. Maps tree-level edits onto upstream `DockManager` mutations (`Document.DockTo`, `Children.Add/Remove`, `ActiveDocument` setter). Pane content is reconciled normally by Reactor as a sub-tree underneath the `Document.Content` slot.
3. **Persistence:** on detach, calls `SaveLayout()` and stores the JSON under `WindowPersistedScope["docking:<PersistenceId>"]`. On mount, attempts `LoadLayout()` before applying the declarative tree as a fallback.
4. **Cross-window drag:** intercepts the upstream `Behavior.ActivateMainWindow()` callback and routes to `ReactorApp.PrimaryWindow.Activate()`. The known-fragile cross-window DnD is *not* exercised in phase 1 unless the showcase explicitly demonstrates a second Reactor `Window` with its own `DockManager` (we likely defer that to phase 3).

### 4.5 Showcase sample (the phase-1 deliverable that humans test)

Path: `samples/apps/dock-showcase/`.

The sample must exercise every feature in the §3.1 matrix that WinUI.Dock supports. Concretely, one scene each:

- **Scene A: IDE.** Top-level: solution explorer (left tool), code-editor tabs (center), properties panel (right tool), error list + terminal (bottom tool tabs). Equivalent of upstream Example.WinUI but driven from Reactor.
- **Scene B: Floating.** Tear a tab out and verify the FloatingWindow shows up, has the custom title bar, and accepts drop-back.
- **Scene C: Side pin.** Pin a panel to the right side, click to expand, verify SidePopup animation and re-dock.
- **Scene D: Compact tabs / bottom orientation.** Demonstrate `CompactTabs=true` and `TabPosition=Bottom`.
- **Scene E: Persistence.** Menu items "Save Layout to file" / "Load Layout from file" using `DockManager.SaveLayout()` / `.LoadLayout()` — mirrors upstream Example.WinUI Open/Save commands.
- **Scene F: Programmatic dock.** Button "Open Properties" that calls `DockTo(target, DockTarget.SplitRight)` programmatically.

The sample is wired into the [ReactorGallery](../../samples/ReactorGallery/) so the docking demos surface in the existing gallery navigation.

### 4.6 Risks and contingencies (phase 1)

- **Risk:** upstream WinUI.Dock fixes or breaks our targeted bug between snapshot and merge. **Contingency:** pin `VENDORED.md` to a specific commit and re-snapshot before merge; reapply our four light edits.
- **Risk:** cross-window DnD doesn't survive the wrapper layer because Reactor's reconciler tears down and re-mounts when the source window's tree mutates mid-drag. **Contingency:** in phase 1 we restrict drag-out to within a single `DockManager`. Cross-`DockManager` drag is a phase-3 deliverable, not a phase-1 regression risk.
- **Risk:** WinUI.Dock's `Document.Content` accepts `object?` and we pass a Reactor `Element` subtree. The reconciler needs a slot host. **Contingency:** wrap each `Document.Content` in a `ReactorContentControl` (already pattern-precedent in `DataTemplateDemo`).
- **Risk:** AOT. WinUI.Dock uses `JsonSerializerContext` for primitives; the layout structure is hand-rolled `JsonObject`, so we're mostly clean. **Verify with the `Reactor.AppTests.Host` AOT build pass at phase exit.**

### 4.7 Phase-1 human review gate

A human reviewer (designated at PR time) sits down with two builds running side by side: `WinUI.Dock\src\Examples\Example.WinUI` and `samples/apps/dock-showcase/`. They execute the following script against both, recording outcomes:

1. Drag a center tab to each of the five split targets (center, splitL/T/R/B). Visual preview matches? Drop lands correctly? Snap-back on Esc works?
2. Drag a center tab to each of the four edge dock targets. Same checklist.
3. Drag a tab out of the title bar, into open space. Floating window appears at pointer. Title bar matches `IDockAdapter.GetFloatingWindowTitleBar` content.
4. Drag a floating tab back into a tab group. Floating window auto-closes if last document.
5. Resize splits with the splitter. Minimums respected. Re-resize after a re-mount restores the same sizes.
6. Pin a tab to a side. Click the side icon. Popup shows. Drag-resize the popup. Re-pin from the popup. Close from the popup.
7. Save layout to JSON. Quit. Restart. Load. Layout matches.
8. (Negative test) Tear out a tab while resizing a different split. No crash.

**Sign-off recorded in PR description.** No phase-1 merge without this checklist green.

## 5. Phase 2 — Reactor-native rewrite (no XAML)

**Goal of this phase:** the showcase from phase 1 looks and behaves identically to a human user, but the implementation behind `Microsoft.UI.Reactor.Docking` no longer touches any vendored XAML control. All layout, interaction, drag, drop, preview, side-pin, and floating-window logic is Reactor-native C#.

**Why this phase exists:** XAML controls are an interop boundary — they have their own dependency-property system, their own VisualTree, their own template parts, their own lifetime. The phase-1 wrapper survives by carefully matching that boundary, but it leaks: AOT trimming is harder, layout perf is double-budgeted (Reactor → XAML → Yoga), devtools introspection stops at the wrapper. Phase 2 collapses the layer so docking is built on the same primitives as the rest of Reactor.

**Why this is *one phase* and not several:** because the public API committed at phase-1 exit doesn't change. We swap implementations underneath. The risk is interaction-quality regression, not API churn.

### 5.1 What's reimplemented

In dependency order (rough):

1. **Split + size constraint solver.** Replaces `LayoutPanel.UpdateLayoutStructure` (which uses `Grid` + `GridSplitter`). Built on Reactor's existing Yoga-backed `FlexPanel` (precedent: `FlexPanelDemo`). The `GridSplitter` becomes a Reactor splitter element with pointer drag + min/max clamping + persistent ratio storage.
2. **Tab group rendering.** WinUI.Dock uses `TabView` from WinUI. We keep using `TabView` here — it has the right accessibility shape and we don't gain enough by rewriting it. The wrapping `DockTabGroup` element renders a Reactor `TabView` modifier.
3. **Drop-target overlay.** Replaces `DockTargetButton` + `Preview`. A floating Reactor element absolutely-positioned over the manager, rendered via the existing overlay system (precedent: tooltip, highlight, dialog) — see `Controls/Tooltips/*` and the `ReconcileHighlightOverlayLifecycleTests`.
4. **Drag/drop pipeline.** Replaces `DragDropHelpers`. Built on the input-and-gestures spec ([027](027-input-and-gestures-design.md)) so docking drag is one of the gesture recognizers, not a parallel system.
5. **Side popup.** Replaces `SidePopup` / `Sidebar`. Implemented as a Reactor `Popup` (existing) anchored to the manager's edge.
6. **Floating window.** **This is where phase 2 meets phase 3 head-on.** A floating pane is *already* a Reactor `Window`. We don't build a new mini-window primitive; we open a top-level Reactor Window with a single docking pane mounted as its root content. See §6.2 for the exact API.
7. **Layout persistence.** Replaces WinUI.Dock's `SaveLayout`/`LoadLayout` JSON code. New format documented in §5.4. Wire-compatible with phase-1 JSON via a schema-version migration (§5.4.4).

### 5.2 What does *not* change

- The public API in §4.3. (That's the whole point.)
- The showcase sample at `samples/apps/dock-showcase/`. The same C# code runs against the new implementation.
- The third-party notice. The vendored code is *still in the repo* under `third_party/WinUI.Dock/` at phase-2 exit even though the runtime no longer references it — see §5.6 below.

### 5.3 What gets *added* (filling §3.2 gaps)

#### 5.3.1 Documents vs tool windows

```csharp
public abstract record DockableContent(...);
public sealed record Document(string Title, ...) : DockableContent(...);
public sealed record ToolWindow(string Title, ...) : DockableContent(...);
```

`Document` is the editor-tab-style center-area pane. `ToolWindow` is the can-be-pinned-to-side dockable utility pane. Differences:

- `ToolWindow.CanPin` defaults to `true`; `Document.CanPin` defaults to `false`.
- `ToolWindow` tabs at the bottom of a group default to compact mode; `Document` defaults to full tabs.
- Drag-pin gesture only offered for `ToolWindow`.
- Layout serialization distinguishes the two so a `LoadLayout` cross-version migration knows which side-pin defaults to apply.

This is a non-breaking addition: `DockableContent` becomes the abstract base; phase-1 instantiations using the closed-shape `DockableContent` constructor are obsoleted (warning, not error) in favor of `Document(...)` and `ToolWindow(...)`.

#### 5.3.2 Per-pane content state

`PersistenceState` on `DockableContent` is upgraded from a plain string to a typed slot:

```csharp
public sealed record Document<TState>(string Title, TState State, ...) : DockableContent;
```

`TState` is serialized via `WindowPersistedScope` (spec 033/036) — same system the rest of Reactor uses. The state is included in the layout JSON, so save/restore round-trips through one file.

#### 5.3.3 Keyboard navigation

- `Ctrl+Tab` opens a VS-style pane navigator overlay listing all open panes.
- `Ctrl+F4` closes the active pane (if `CanClose`).
- Arrow keys + space navigate drop targets during keyboard-initiated drag (initiated by `Ctrl+Shift+M` "move pane", VS parity).

#### 5.3.4 Layout versioning

Layout JSON gets a `"$schema": 2` field at the root. Migrations registered as `IDockLayoutMigration` services run in order on load.

#### 5.3.5 Cancellable lifecycle events (AvalonDock parity)

`DockHost` raises a full set of lifecycle events. Every `*ing` event carries a `Cancel` flag the handler can set to abort the transition; the matching `*ed` event fires only on success.

```csharp
public sealed record DockHost(...) : Element
{
    public Action<DockLayoutChangingEventArgs>? OnLayoutChanging { get; init; }
    public Action<DockLayoutChangedEventArgs>?  OnLayoutChanged  { get; init; }

    public Action<DockContentClosingEventArgs>? OnDocumentClosing  { get; init; }
    public Action<DockContentClosedEventArgs>?  OnDocumentClosed   { get; init; }

    public Action<DockContentHidingEventArgs>?  OnToolWindowHiding   { get; init; }
    public Action<DockContentClosingEventArgs>? OnToolWindowClosing  { get; init; }
    public Action<DockContentHiddenEventArgs>?  OnToolWindowHidden   { get; init; }
    public Action<DockContentClosedEventArgs>?  OnToolWindowClosed   { get; init; }

    public Action<DockContentFloatingEventArgs>? OnContentFloating { get; init; }
    public Action<DockContentFloatedEventArgs>?  OnContentFloated  { get; init; }
    public Action<DockContentDockingEventArgs>?  OnContentDocking  { get; init; }
    public Action<DockContentDockedEventArgs>?   OnContentDocked   { get; init; }

    public Action<DockableContent?>? OnActiveContentChanged { get; init; }

    public Action<FloatingWindowCreatedEventArgs>? OnFloatingWindowCreated { get; init; }
    public Action<FloatingWindowClosedEventArgs>?  OnFloatingWindowClosed  { get; init; }
}
```

Reactor-idiomatic delivery: events are `Action<TArgs>?` props on the record. Apps can compose them with their own state hooks. The `IDockBehavior` interface from phase 1 collapses into these (its three methods map to `OnContentDocked`, `OnContentFloating`, and the per-group docked variant).

#### 5.3.6 Insertion-policy hook (`IDockLayoutStrategy`)

Adopts AvalonDock's `ILayoutUpdateStrategy` model. An app-supplied strategy intercepts new-content insertion to override the default "go to active pane / right side / first available" heuristics.

```csharp
public interface IDockLayoutStrategy
{
    bool BeforeInsertDocument(DockHostModel layout, Document doc, DockNode? destination);
    void AfterInsertDocument(DockHostModel layout, Document doc);
    bool BeforeInsertToolWindow(DockHostModel layout, ToolWindow tool, DockNode? destination);
    void AfterInsertToolWindow(DockHostModel layout, ToolWindow tool);
}
```

Return `true` from `Before*` to indicate the strategy handled the insertion (manager skips its own placement); `false` to let the manager proceed. `After*` is the chance to set initial dimensions, pin to a side, etc.

Example: route any tool window with `Title.StartsWith("Error")` to the bottom side, narrow:
```csharp
public bool BeforeInsertToolWindow(DockHostModel layout, ToolWindow tool, DockNode? destination)
{
    if (tool.Title.StartsWith("Error"))
    {
        layout.BottomSide.Add(tool with { Height = 180 });
        return true;
    }
    return false;
}
```

`DockHostModel` (introduced in §5.3.10) is the mutable model handle. Strategies receive a *handle* rather than the immutable `DockNode` tree because they intend to mutate during insertion.

#### 5.3.7 Reactor-native composition (no binding API)

**No `DocumentsSource`. No `LayoutItemTemplate`. No `ContentResolver`.** Reactor's functional composition replaces all of them:

```csharp
Element MyShell(RenderContext ctx)
{
    var (documents, setDocuments) = ctx.UseState(ImmutableList<DocVM>.Empty);
    var activePaneKey = ctx.UseState<object?>(null);

    return new DockHost(
        Id: "main-shell",
        Layout: new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new ToolWindow(
                Key: "tool:solution",
                Title: "Solution Explorer",
                Content: SolutionExplorer()),

            new DockTabGroup(
                Documents: documents.Select(d => new Document(
                    Key: d.Id,                       // stable identity
                    Title: d.DisplayName,
                    Content: Editor(d.Id))).ToImmutableList(),
                SelectedIndex: documents.FindIndex(d => d.Id == activePaneKey.Value)),

            new ToolWindow(
                Key: "tool:properties",
                Title: "Properties",
                Content: Properties())
        }));
}
```

Why this is sufficient:
- **The collection-to-pane mapping** that `DocumentsSource` would do externally is just `.Select(...)` in the component. Adding/removing items is `setDocuments(documents.Add(...))` — the reconciler diffs and produces the same effect as collection-change events.
- **Per-item identity** is `Document.Key`. Spec 042 keyed reconciliation preserves child state across reorderings without a separate `LayoutItem` indirection.
- **Layout restore from JSON** is a side-effect of the same composition: the persisted layout is read into the component's state via `UsePersisted` (spec 033 / spec 036 §8), and then the render function produces the dock tree from that state. No external rehydration callback is needed — the component is the rehydrator.
- **Insert/remove policy** that `IDockLayoutStrategy` (§5.3.6) handles is the only piece that requires a sideways escape hatch, because it has to run *during* reconciliation (e.g., when a drag-drop adds a pane the component didn't ask for).

This is the pattern Reactor uses for everything else (data-tables, navigation, form fields) — docking is not special and doesn't get its own binding pipeline.

#### 5.3.8 Fine-grained per-pane permissions

`DockableContent` (and subclasses) gain AvalonDock-style permission flags. Defaults preserve phase-1 semantics:

```csharp
public abstract record DockableContent
{
    public bool CanClose    { get; init; } = false;
    public bool CanFloat    { get; init; } = true;
    public bool CanMove     { get; init; } = true;
    public object? Key      { get; init; }
    // ...title, content, sizes, etc.
}

public sealed record Document(...) : DockableContent
{
    // CanClose default flips to true for documents — they're closable by default.
    public new bool CanClose { get; init; } = true;
    public bool CanDockAsToolWindow { get; init; } = false;
}

public sealed record ToolWindow(...) : DockableContent
{
    public bool CanHide              { get; init; } = true;   // X button hides
    public bool CanAutoHide          { get; init; } = true;   // can pin to side
    public bool CanDockAsDocument    { get; init; } = true;   // can move into doc area
}
```

Key semantic distinction (from AvalonDock): for `ToolWindow`, **the X button hides; explicit close menu closes**. Hide = collapsed but in tree (re-shows via menu/command). Close = removed from tree (must be re-added programmatically). Documents close on X (no "hide" state).

#### 5.3.9 `PreviousContainer` — show-panel-where-you-left-it

Every `DockableContent` instance internally tracks the last `DockNode` container it was inside. When hidden then re-shown, or when added to a host that already remembers it from a prior session (matched by `Key`), it lands in the remembered container — not at the default insertion point.

This is invisible state from the app's perspective (no public field) and survives layout serialization (stored as `previousContainer` on the JSON content node). The `IDockLayoutStrategy` can override by returning `true` from `BeforeInsertToolWindow`.

Why this matters: it's the difference between Ctrl+Alt+O (Show Output) jumping the Output window to a random spot vs. landing where the user docked it last week. Every shipping IDE does this; WinUI.Dock does not.

#### 5.3.10 Layout-as-model: `DockHostModel`

Adopts AvalonDock's deepest architectural choice (lesson #8). The `DockNode` tree from §4.3 is the immutable view of the layout. `DockHostModel` is the mutable handle:

```csharp
public sealed class DockHostModel
{
    public DockNode? Root { get; }
    public IReadOnlyList<ToolWindow> LeftSide   { get; }
    public IReadOnlyList<ToolWindow> TopSide    { get; }
    public IReadOnlyList<ToolWindow> RightSide  { get; }
    public IReadOnlyList<ToolWindow> BottomSide { get; }
    public IReadOnlyList<FloatingDockWindow> Floating { get; }
    public DockableContent? ActiveContent { get; set; }

    public IEnumerable<DockableContent> AllContent();
    public IEnumerable<DockNode>        Descendants();

    public void Dock(DockableContent content, DockTarget target, DockNode? near = null);
    public void Float(DockableContent content, (double X, double Y)? at = null);
    public void Hide(ToolWindow tool);
    public void Show(ToolWindow tool);
    public void Close(DockableContent content);
    public void Activate(DockableContent content);

    public string SaveJson(int schemaVersion = 2);
    public void   LoadJson(string json);
}
```

The reconciler reads from the model to produce the rendered tree; `IDockLayoutStrategy` and event handlers receive the model directly; devtools/MCP introspect it. `DockHost` is the Reactor element that *owns* a `DockHostModel` instance.

**Controlled / uncontrolled — and how the round-trip works.** Reactor docking is fully controlled: the `Layout` prop on `DockHost` is read every render and is the canonical layout. User-driven drag-drop, side-pin, tear-out, and resize operations don't mutate the model behind the component's back — they fire `OnLayoutChanged` (and the more granular events from §5.3.5) carrying the proposed new layout. The component decides whether to accept (by updating its own state, typically via `UsePersisted` so it survives restarts) or reject (by ignoring the event; the next render restores the previous `Layout`). This is the same pattern Reactor uses for `TextBox.Text` + `OnTextChanged`, `DataGrid.Selection` + `OnSelectionChanged`, etc.

The `DockHostModel` exists as the *implementation* of this — it's how the reconciler tracks in-flight drag state, how `IDockLayoutStrategy` hooks intercept insertions, and how devtools introspect — but the model isn't a parallel source of truth users can write to. Apps interact with the layout through the controlled `Layout` prop + event round-trip, and observe details via the hooks in §5.3.11.

#### 5.3.11 Property hooks and context support

Replacing the binding API (§5.3.7) means components inside docked content need a *better* way to observe and act on dock state — without prop-drilling and without taking a top-down dependency on the host. We adopt the same context/hooks pattern Reactor uses for navigation (spec 011), data (spec 017), theme (spec 015), localization (spec 005).

**`DockHost` is a context provider.** When mounted, it registers a `DockContext` in `RenderContext` that every descendant can resolve. The context carries the `DockHostModel` (mutable handle) and a small fan-out of observable sub-states. Closes with unregister on unmount.

**Hook surface:**

```csharp
namespace Microsoft.UI.Reactor.Docking;

public static class DockHooks
{
    // The host this component is inside, if any. Walks up the context chain.
    // Returns null for components outside a DockHost (e.g., main shell chrome).
    public static DockHostModel? UseDockHost(this RenderContext ctx);

    // The active pane key in the current DockHost. Re-renders when active changes.
    public static object? UseActivePaneKey(this RenderContext ctx);

    // True iff the current consumer is inside the *active* pane of its host.
    // Convenience for "render expensive content only when visible" patterns.
    public static bool UseIsActivePane(this RenderContext ctx);

    // The pane this component is rendering inside. Throws if called outside
    // a Document/ToolWindow Content subtree. Re-renders on state transitions.
    public static DockPaneInfo UsePane(this RenderContext ctx);

    // Float/dock state of the enclosing pane. Re-renders on transitions.
    public static DockPaneState UseDockState(this RenderContext ctx);

    // Subscribe to layout changes. Returns a snapshot; re-renders when the
    // layout shape mutates. Used by devtools panels and any component that
    // wants to display the layout (e.g., a custom Window menu).
    public static DockLayoutSnapshot UseDockLayout(this RenderContext ctx);
}

public readonly record struct DockPaneInfo(
    object? Key,
    string Title,
    DockableContent Content);   // the record describing this pane

public enum DockPaneState
{
    Docked,        // pane is inside a DockHost in its main shell
    Floating,      // pane is in a floating window (phase 2+) or top-level Window (phase 3+)
    AutoHidden,    // pane is pinned to a side; not currently expanded
    AutoHiddenExpanded,  // pane is pinned to a side; popup currently shown
    Hidden,        // pane is in the tree but not visible
}
```

**Why this matters:**

- **Pane content can react to its own visibility/float state.** An editor pane that runs an expensive syntax-highlighting pass can `if (!ctx.UseIsActivePane()) return PlaceholderShell()`, then upgrade when the pane becomes active. A video preview pane can pause when it goes auto-hidden. Without hooks, the only way to know would be prop-drilling from the host through the layout tree to every pane content factory — exactly the kind of ceremony Reactor's hooks exist to avoid.
- **Commands from arbitrary depth.** A "Move to Floating Window" button buried deep in a settings panel calls `ctx.UseDockHost()?.Float(ctx.UsePane().Content)` and Just Works. No injection, no DI registration, no event-bus hop.
- **Devtools panels participate.** A devtools "Open Pane" view uses `UseDockLayout` to render the host tree; a "Pop Out" button uses `UseDockHost` to call `Float`. Devtools is just another component — same hooks as everyone else.
- **No global registry needed.** A process with two `DockHost`s in two top-level Windows resolves each consumer to *its* host via the context chain. No string ids needed in user code (the `Id` on `DockHost` is for cross-window `DefaultHostId` lookup in phase 3, not for hook resolution).

**Hook stability and re-render scope:**
- `UseActivePaneKey` re-renders only the consumer when active changes, not the whole host subtree. Implemented via the same selector-style subscription Reactor uses for `UseDataContext` (spec 017).
- `UseIsActivePane` is a boolean derivative — re-renders only on transitions in/out of active.
- `UseDockLayout` is the wide-net hook — re-renders on any structural change. Used sparingly (devtools, not pane content).
- `UseDockState` is per-pane — re-renders on state transitions for *that* pane only.

**Composition with phase 3 (`DockableWindow`):**
A `DockableWindow` opened via `OpenWindow(IsDockable: true, ...)` has its content rendered inside whichever surface currently hosts it (a `DockHost` tab slot when adopted; the Window's root when floating). The same hooks resolve correctly in both states: `UseDockHost()` returns the adopting host when adopted, returns `null` when floating; `UseDockState()` returns `Docked` or `Floating` accordingly. A component that re-renders on `UseDockState` transitions adapts naturally to tear-out — e.g., a status-strip footer that only appears when floating.

**Composition with phase 4 (`TitleBar` chrome):**
The tab-in-title-bar render path is a render-time consequence of `UseDockState() == Floating && DockTabGroup.Documents.Count == 1`. The phase-4 chrome adoption is checking the same hook surface, not a special case in the reconciler.

**This hook surface, together with §5.3.10 `DockHostModel`, is the entire data API for docking.** No `DocumentsSource`, no `ItemContainerStyle`, no `DataTemplate` machinery — just records and hooks, composing the same way every other Reactor primitive does.

### 5.4 New layout JSON format

```jsonc
{
  "$schema": 2,
  "active": "doc://editor/MainView.xaml",   // DockableContent.Key, URI-style
  "root": {
    "$": "split",
    "orientation": "vertical",
    "children": [
      {
        "$": "split", "orientation": "horizontal", "size": 1.0,
        "children": [
          { "$": "tabs", "selected": 0, "compact": false, "tabPosition": "top",
            "documents": [
              { "$": "doc", "key": "doc://editor/MainView.xaml",
                "title": "MainView.xaml", "canClose": true, "state": { ... } }
            ]
          }
        ]
      }
    ]
  },
  "sides": {
    "left":  [],
    "top":   [],
    "right": [],
    "bottom": [ { "$": "doc", "key": "tool://output", ... } ]
  },
  "floating": [
    { "key": "win://floating-1", "x": 320, "y": 100, "w": 600, "h": 400,
      "root": { "$": "tabs", ... } }
  ]
}
```

Notes:
1. Sizes are stored as **ratios** for splits (not absolute pixels) — robust to window resize and DPI.
2. Absolute pixel sizes are reserved for `floating[].x/y/w/h` and per-pane `width`/`height` overrides.
3. `key` is the stable identity used by phase-2 keyed reconciliation. Phase-1-format files (no `$schema`) are migrated by inferring keys from `title`.
4. Schema version 2 is phase-2 native; schema version 1 is the phase-1 (upstream WinUI.Dock) format reproduced bit-for-bit, kept readable for migration.

### 5.5 Phase-2 risks and contingencies

- **Risk:** drop preview overlay z-ordering wrong relative to other Reactor overlays (tooltips, dialogs). **Mitigation:** use the same overlay-priority enum spec'd in 036 §11 (Shell integration) — docking overlays sit below dialogs, above tooltips.
- **Risk:** floating window tear-out reuses Reactor's `OpenWindow` which has its own activation lifecycle. The "drag created a window that has no content yet" race needs care. **Mitigation:** phase-2 spec specifies that tear-out opens the new `Window` *synchronously* with the pane already attached as content, not on next tick.
- **Risk:** the drop-target hit-test surface is large (5 center targets + 4 edge targets) and pointer-event-heavy. Frame budget on a 4K display matters. **Mitigation:** target ≤2 ms hover-state latency, measured against [spec 031 frame-aligned sampling](031-frame-aligned-sampling-design.md). Add a benchmark to the perf suite.
- **Risk:** AOT trim warnings from the new code. **Mitigation:** all docking code goes under the same trim-warning-free guardrail the rest of `src/Reactor/` already passes. CI fails on new warnings.

### 5.6 Vendored code disposition at phase-2 exit

The `third_party/WinUI.Dock/` tree **stays in the repo** but the `Reactor.Docking.Xaml` assembly is removed from `Reactor.slnx` and from any published package. We keep the source for: (a) license compliance (attribution remains valid), (b) reference during phase 3, (c) regression-test fixture in case we need to A/B against the original behavior. A short note in `third_party/WinUI.Dock/VENDORED.md` documents this disposition.

### 5.7 Phase-2 human review gate

Same script as §4.7, executed against the new implementation and the *now-archived* phase-1 build. Acceptance criterion: **no human-discernible behavior difference** in the eight test cases, plus six new cases for the additions:

9. Documents vs tool windows visual distinction matches intent.
10. Per-pane content state survives save→quit→restart→load (state slot populated with editor scroll position; restored position matches).
11. Ctrl+Tab pane navigator opens, navigates, closes correctly.
12. Layout JSON schema version 1 file (saved from phase-1 build) loads correctly in phase-2 build.
13. Drop preview latency feels equivalent (timed where reasonable; subjective otherwise).
14. AOT-published binary runs the showcase end-to-end.
15. Run the showcase under `de-DE` and `ar-SA` (RTL); titles localize, drop targets / context-menu items localize, layout mirrors correctly under RTL, pointer hit-tests resolve in mirrored regions.
16. Screen reader pass: Narrator (or NVDA) announces pane roles, AutomationIds are stable across renders, focus is never lost across a close/tear-out/adopt cycle, drop-target navigation works keyboard-only with arrow+Enter.
17. Reduced-motion: enable `Settings → Ease of Access → Visual Effects → Animation effects off`; drop preview, side-popup, and tab-reorder transitions disappear; static positioning is correct.
18. Corrupt-layout recovery: hand-edit the persisted layout JSON to invalid syntax, restart the showcase; app starts with default layout; an error event is logged; no crash dialog.

## 6. Phase 3 — fold into the Window primitive

**Goal of this phase:** "DockableWindow is a Window variant; any Window can be re-parented into a DockHost." Docking is no longer a control you put in a Window — it is a property of the Window model itself.

**Why this is the last phase:** because by phase-2 exit we already have all the *layout* primitives we need. What changes in phase 3 is the *identity* of a docked pane. In phase 1/2, a "tab" is a `Document` record with a `Content` slot; the document doesn't exist as a Window. In phase 3, a tab *is* a `Window` with a flag saying "I am currently adopted by a `DockHost`." This is the integration the user described as the eventual destination.

### 6.1 Model

```
ReactorApp
  ├── Windows                  ReadOnlyList<ReactorWindow>      (spec 036)
  ├── DockableWindows          ReadOnlyList<ReactorWindow>      (subset: IsDockable)
  └── DockHosts                ReadOnlyList<DockHost>           (registry, by id)

ReactorWindow (existing, spec 036)
  ├── IsDockable               bool — declared at spec time, immutable
  ├── DockState                Floating | Adopted | Hidden
  ├── AdoptedBy                DockHost? — when DockState == Adopted
  └── AdoptionKey              object?  — stable identity inside the host

DockHost                       Reactor element placed inside a Window's tree.
  ├── Layout                   DockNode (same algebra as phase 1/2)
  └── adopts/expels ReactorWindow instances at runtime
```

A `DockableWindow` is a `ReactorWindow` for which `WindowSpec.IsDockable = true`. Its difference from a non-dockable window: at any moment its rendered content can be (a) the full HWND of a top-level OS window (the "Floating" state), or (b) hosted inside a `DockHost` somewhere in another Window's element tree (the "Adopted" state). The transition between states is what tab tear-out / tab drop-in becomes.

### 6.2 Tear-out is "convert Adopted to Floating"

```csharp
// User drags a tab out of its DockHost.
//
// Before: ReactorWindow { IsDockable=true, DockState=Adopted, AdoptedBy=hostA }
//   The window's HWND is *not present* — its element tree is rendered inside
//   hostA's tab area. The OS does not know this window exists.
//
// After:  ReactorWindow { IsDockable=true, DockState=Floating, AdoptedBy=null }
//   The window's HWND is created, its element tree migrates from hostA's
//   subtree into the new HWND's root, and the OS now sees a top-level window.

reactorApp.PromoteToFloating(window, position: pointerLocation);
```

Drop-into-host is the inverse:

```csharp
reactorApp.AdoptIntoHost(window, host: hostB, target: DockTarget.SplitRight);
// Closes the floating HWND, migrates element tree into hostB.
```

These operations are reconciler-driven — the user drags a tab, the docking gesture recognizer fires, the recognizer calls `PromoteToFloating` / `AdoptIntoHost`, the reconciler unmounts from one parent and mounts under the other. Per spec 036 §3.1, Reactor's reconciler already returns one `UIElement` per host; what's new here is that a single *logical* Window's element tree can be hosted by either an `HWND` (top-level) or a `DockHost`'s tab slot, and we can move between the two without destroying-and-recreating Reactor state.

### 6.3 Window-spec extensions for docking

```csharp
public sealed record WindowSpec(
    // ...existing fields from spec 036...
    bool IsDockable = false,            // opt in
    DockableWindowKind Kind = DockableWindowKind.Document,
    object? AdoptionKey = null,         // stable identity across adopt/promote
    string? DefaultHostId = null,       // host this window adopts into on open
    DockTarget DefaultAdoptionTarget = DockTarget.Center);

public enum DockableWindowKind { Document, ToolWindow }
```

A dockable window opens like any other window (`ReactorApp.OpenWindow(spec, ...)`), but:

- If `DefaultHostId` is set and a `DockHost` with that id is currently mounted, the window opens *adopted* (no HWND created until tear-out).
- Otherwise, it opens *floating* (top-level HWND, no host).

Tray-icon flyout windows (per spec 036 §11) can host a `DockHost` in their content — meaning tray flyouts can present dockable content. This is the "tray icon + dock = seamless" integration the user described.

### 6.4 DockHost as a Reactor element

```csharp
public sealed record DockHost(
    string Id,                          // global identity, used by DefaultHostId
    DockNode? Layout = null,
    IDockAdapter? Adapter = null,
    IDockBehavior? Behavior = null,
    string? PersistenceId = null,
    int LayoutSchemaVersion = 2) : Element;
```

This is the renamed `DockManager` from phase 1/2. The fields are the same except: `Id` is now globally required (it's how Windows find their host by `DefaultHostId`), and `LayoutSchemaVersion = 2` is the default (a phase-3 deployment loading a phase-2 layout file just works).

The `DockNode` algebra (§4.3) is extended:

```csharp
public sealed record DockableWindowRef(ReactorWindow Window) : DockNode;
```

Where a phase-1/2 layout had a `DockableContent` leaf, a phase-3 layout *can* have a `DockableWindowRef` leaf — i.e. the pane's identity is a Window reference, not an inline record. Migrating phase-2 layouts to phase-3 is seamless: a `DockableContent` becomes a `DockableWindowRef` to a synthetic Window opened at load time.

### 6.5 Shell integration

Because a `DockableWindow` *is* a `ReactorWindow`:

- It participates in the spec-036 `WindowOpened`/`WindowClosed` events when floating. Adopted-state windows don't emit those (no OS window exists), but they do fire `Adopted`/`Promoted` events on the host.
- Taskbar progress, overlay icons, jump lists (spec 036 §11) apply to floating dockable windows just like any other.
- The window persistence id (spec 036 §8) is honored for both floating *and* adopted lifetimes — the same window can be torn out, restarted, and re-attached without losing its persisted scope.
- Devtools / MCP can address dockable windows by their stable id whether they're currently HWND-backed or host-adopted.

### 6.6 Showcase sample, updated

The phase-1/2 showcase is rebuilt to demonstrate native Window integration:

- The "Solution Explorer" tool window is opened via `OpenWindow(new WindowSpec(IsDockable: true, Kind: ToolWindow, DefaultHostId: "main-shell", DefaultAdoptionTarget: DockLeft))`. Tearing it out floats it as a real top-level window; closing the float reuses the *same* `ReactorWindow` instance the next time it's opened, demonstrating identity preservation.
- A tray-icon flyout shows a small `DockHost` with two adopted tool windows — demonstrating that tray hosts dockable content.
- A second top-level `Window` (a secondary shell — "Settings") also hosts a `DockHost` and accepts cross-shell drag-and-drop of dockable windows from the main shell.

### 6.7 Phase-3 risks

- **Risk:** element-tree migration across `ReactorHost` boundaries. Reactor's reconciler today is single-host. Adopting a window means moving an element subtree from one host's reconciliation root into another's slot. **Mitigation:** treat the migration as an unmount-then-mount with **state preservation** — `RenderContext` and `useState` / `useEffect` snapshots carry across the boundary, identified by `AdoptionKey`. This is the largest single piece of reconciler work in the spec; budget accordingly.
- **Risk:** cross-window drag (spec 036 N2). **Mitigation:** we lift that non-goal in phase 3 — cross-Reactor-Window drag is now in scope because the windows share a process, share a dispatcher, and share a docking gesture recognizer.
- **Risk:** adopted-window HWND lifecycle. When adopted, no HWND should exist; on tear-out, one is created. A naïve implementation creates the HWND eagerly and just hides it — that breaks alt-tab, taskbar, etc. **Mitigation:** lazy HWND creation, fully deferred until first `PromoteToFloating`.
- **Risk:** modal dialog from inside an adopted DockableWindow. WinUI 3 `ContentDialog` finds its XamlRoot from the visual tree — when adopted, the tree is the host's tree, not a top-level. **Mitigation:** explicit XamlRoot routing in spec; resolved against the *host* window when adopted.

### 6.8 Phase-3 human review gate

The phase-2 script (§5.7), plus:

15. Open a dockable tool window into the main shell. Tear it out → free-floating top-level window with its own taskbar entry. Close. Reopen. Re-adopts at default position.
16. Open a tray-icon flyout containing a `DockHost`. Tear a tool window out of the flyout — it becomes a top-level window, the flyout closes, the tool window persists. Trigger the tray icon again — flyout re-opens, tool window re-adopts.
17. Open the secondary shell. Drag a dockable window from main shell to secondary shell. Tab lands. Reverse direction works.
18. Save layout from main shell. Reset. Reload. Adopted windows reappear adopted; previously-floating windows reappear floating at the same positions.
19. AT/AT-SPI: the screen reader announces an adopted dockable window with the correct role (document or tool) and the focus traversal includes both adopted panes and floating windows.

## 7. Phase 4 — Windows 11 native-chrome polish

**Goal of this phase:** the showcase floats and docks like a *modern Windows 11 app*, not like a competent generic XAML control. Tab strips live inside the title bar of floating windows (the way Edge, Files, Terminal, WinSCP-modern, Office, modern VS, modern VS Code, and the WinAppSDK Gallery all do it). System chrome — caption buttons, snap layouts, theme transitions, system backdrops — is supplied by the WinUI 3 `TitleBar` control rather than rolled by hand. Hit-testing, drag regions, and snap behavior come from the OS, not from our pointer pipeline.

**Why this is the last phase and not folded into phase 1/2:** the WinUI `TitleBar` control was introduced in WindowsAppSDK 1.6 and stabilized only in 1.7+. Adopting it as the chrome surface introduces a hard min-version on a moving target and would have constrained phase 1's "ship something working soon" goal. By the time phase 4 lands, we expect WindowsAppSDK 1.7+ to be our baseline (spec 036 §11 already anticipates the title-bar surface).

**Why polish belongs to its own phase, not woven through earlier phases:** the polish is *purely visual and interaction-feel*. None of it changes the data model, the reconciler, the layout algebra, or the public API surface. Mixing it into phase 1/2/3 would have diluted those phases with theme/asset/visual-tweak churn that's better landed in one focused pass after the foundation is correct. It also makes a clean human review gate: phase 4 exit is the visual sign-off.

### 7.1 What changes

#### 7.1.1 `TitleBar` control adoption

Floating dockable windows (phase 2+) currently use an `OverlappedPresenter` with `SetBorderAndTitleBar(true, false)` (the WinUI.Dock vendored approach — caption-less, drag region wired manually). Phase 4 replaces this with the WinUI `Microsoft.UI.Xaml.Controls.TitleBar` control as the window root chrome. Concretely, `ReactorWindow.NativeWindow.ExtendsContentIntoTitleBar = true`, the root content gets a `TitleBar` slot at the top, and the docking tab strip is placed *inside* that title bar's content area.

This is what gives us:
- **Native caption buttons** (minimize / maximize / close) at the OS-default position and size, with theme-aware brushes and proper hit-testing (snap-layouts flyout on hover, Aero shake, etc.).
- **Drag regions handled by the OS**, not by our pointer code. The non-tab area of the title bar is drag-region; the tabs are interactive — but unlike phase 2's manual `TitleBar_DragStarted` / `TitleBar_DragDelta` logic, hit-testing comes from `SetRegionRects` calls the OS respects.
- **Theme transitions** (dark/light, contrast) inherit from the system without us hand-restyling.
- **System backdrops** (Mica, MicaAlt, Acrylic) compose correctly under the title bar.

Reactor's existing `TitleBar(...)` element (`src/Reactor/Elements/Dsl.cs:610`, `TitleBarElement`) already wraps the WinUI control for non-docking windows. Phase 4 wires `DockHost` to use it for the floating-window path.

#### 7.1.2 Tabs in the title bar (the headline feature)

When a `DockableWindow` is floating with a single `DockTabGroup` as its root (the common tear-out case), the tab strip renders **inside** the `TitleBar`'s `Content` slot — flush with the caption buttons, with the active tab styled as the window's identity (Edge / Files / modern-VS pattern). When the floating window contains *multiple* groups (i.e. the user docked another tab into the float), the title bar reverts to showing the floating-window title (per `IDockAdapter.GetFloatingWindowTitleBar` from phase 1/2) and the tabs render in their normal pane position.

Visual contract:
```
┌─────────────────────────────────────────────────────────────────┐
│  [icon]  ◢ MainView.xaml ◣  ◢ Output ◣  +              ─ □ ✕   │  ← TitleBar
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│                    [active tab content here]                    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

Drag semantics:
- Drag the **tab itself** → tear it out (same as today). This is the case we have to test most carefully because the tab now lives inside the OS-managed title bar's hit-test surface.
- Drag the **title bar background** (non-tab area, non-caption-button area) → move the whole floating window. OS-handled.
- Drag the **active tab while it is the only tab** → moves the whole window (single-tab floats behave like normal windows; there's nothing to "tear out").

Hit-test regions are computed each frame from the tab strip's measured geometry and pushed to `AppWindow.TitleBar.SetDragRectangles` (the API on `Microsoft.UI.Windowing.AppWindowTitleBar`) so the OS knows which sub-rects are interactive vs. drag-region.

#### 7.1.3 Snap Layouts

Windows 11 Snap Layouts appear when the user hovers the maximize button. With phase-4 chrome, this works for free (the OS owns the caption button). For *docked content within a `DockHost`*, the equivalent feature is the existing drop-target overlay — already in scope from phase 2. We don't try to integrate the OS Snap Layouts into in-shell docking; that's a category error.

#### 7.1.4 System backdrop coordination

Floating dockable windows inherit `WindowSpec.Backdrop` (spec 036 §4.1, §5 in that spec) and render with Mica / MicaAlt / Acrylic as configured. The docking layout drawing respects the backdrop — splitter gutters become semi-transparent, tab-strip background is `TitleBarBackgroundFillBrush` not a hard color.

#### 7.1.5 Caption-button area awareness

The tab strip must not draw under the caption buttons. Their geometry comes from `AppWindow.TitleBar.RightInset` (RTL: `LeftInset`). The tab-strip layout reserves that inset as right-padding so tabs end before the buttons begin.

#### 7.1.6 Dark mode / accent color

WinUI 11 `TitleBar` honors the system theme without intervention. Reactor's docking theme overrides (spec 001 theming) compose with it — but in phase 4 we do an explicit pass to make sure the *combination* (system title bar + Reactor docking content + custom theme) doesn't produce contrast-mismatched results. This is largely a QA pass with the showcase sample.

#### 7.1.7 Floating-window persona

A floating window with a single tab gets the tab's `Title` and `Icon` as its `AppWindow.Title` / `AppWindow.SetIcon`, so alt-tab and taskbar show "MainView.xaml" rather than "Floating Window 3." When multiple tabs are present, the active tab's title is reflected. This composes with the Window persistence model (spec 036 §8) — the persisted Window identity is the dockable-window key, not the transient tab content.

### 7.2 What does *not* change

- Public API surface (§4.3 + phase-2 / phase-3 extensions). Phase 4 is purely visual / chrome. No new types on the docking model.
- Reconciler behavior.
- Layout JSON schema.
- Selftest contracts (the model-level tests don't care what chrome wraps the floating window).
- Anything in the main-shell (non-floating) view. The `DockHost` *inside* a regular Window keeps its existing tab-strip placement; only the *floating-window* persona changes.

### 7.3 Risks (phase 4)

- **Risk:** WindowsAppSDK `TitleBar` control API stability. **Mitigation:** target SDK version pinned in `Directory.Build.props`; spec'd minimum at phase-4 entry. If the API moves before we land, this phase slips — but phases 1–3 don't depend on it.
- **Risk:** `SetDragRectangles` perf when the tab strip resizes every frame during a hover. **Mitigation:** debounce drag-rectangle updates to layout-measure-change events, not every-frame. Benchmark before merge.
- **Risk:** caption-button inset changes mid-flight (e.g., theme switch flips RTL). **Mitigation:** subscribe to `AppWindowTitleBar.LayoutMetricsChanged` and re-measure on the next layout pass.
- **Risk:** the single-tab-takes-over-title-bar behavior collides with `IDockAdapter.GetFloatingWindowTitleBar` (the phase-1 adapter that lets apps customize the floating-window title). **Resolution:** `GetFloatingWindowTitleBar` continues to be the source of the *non-tab portion* of the title bar (text/branding shown when no tab fills it, or in the multi-group fallback). It does not contradict tab-in-titlebar; it complements it.
- **Risk:** non-Windows-11 hosts (Windows 10 LTSC, Server) fall back ungracefully. **Mitigation:** the WinUI 3 `TitleBar` control degrades to system-themed-titlebar-without-tabs on older Windows; behavior on Win10 is "looks like phase 2" with no tab-in-titlebar, which is acceptable.

### 7.4 Phase-4 human review gate

The phase-3 script (§6.8) re-run with the new chrome, plus:

20. Floating window with one tab shows the tab in the title bar; caption buttons are OS-default; theme matches system.
21. Drag the tab in the title bar → tear-out fires correctly. Drag the non-tab area → window moves; no tear-out.
22. Maximize via the caption button; hover the maximize button to see Snap Layouts; pick a quadrant; window snaps. Restore. Verify Reactor's reconciler does not interfere with snap geometry.
23. Switch system theme dark↔light while a floating window is open. Title bar updates; docking content updates; no flash, no contrast regression.
24. Verify alt-tab shows the floating window's *tab title*, not "Reactor App".
25. Verify Snap Layouts assist windows show the *tab title* and *tab icon*, not a generic.
26. RTL system locale: caption buttons render on the left; tab strip reserves left inset; behaviors mirror correctly.
27. High-contrast theme: chrome remains legible; drop targets remain distinguishable.

**Sign-off recorded in PR description.** Phase 4 is the final visual gate.

## 8. Cross-phase concerns

### 8.1 Performance budget

- **Drop-target hover-state update:** ≤ 2 ms (phase 2+, measured via the perf suite per spec 031).
- **Tear-out floating-window appearance:** ≤ 1 frame (16 ms) from drop-threshold gesture fire to HWND visible.
- **Layout JSON load:** ≤ 50 ms for a 200-pane layout (allowing for content lazy-mount).
- **Adoption / promotion (phase 3):** ≤ 1 frame for the element-tree migration itself, exclusive of content first-render.

### 8.2 Devtools / MCP

- A new `docking.snapshot` MCP tool returns the layout tree of a host (phase 1+).
- A new `docking.dock` MCP tool moves a pane programmatically — for headless test driving (phase 2+).
- `WindowRegistry` (spec 036 §10) includes `IsDockable` and `DockState` per window (phase 3).

### 8.3 Testing strategy

**Selftests first, UI automation second — by a wide margin.** The Reactor philosophy is that the reconciler is observable from in-process: the rendered element tree, focus state, layout sizes, and event delivery are all directly inspectable without spinning up an external driver. That's faster (no IPC, no automation latency), more reliable (no flakiness from window-activation races), more parallelizable, and AOT-clean. We exploit that here.

**Selftest coverage (the bulk of the matrix).** A `DockingFixtures` set under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (alongside `NavigationFixtures`, `DataGridEditFixtures`, `ReconcileHighlightOverlayLifecycleTests`, etc.) exercises:

- **Layout model.** Build a `DockHostModel`, perform a sequence of `Dock` / `Float` / `Hide` / `Show` / `Close` operations, assert the resulting `DockNode` tree shape and the `Descendants()` enumeration. Pure model — no rendering needed.
- **Reconciler.** Mount a `DockHost` with declarative layout, mutate inputs, assert the rendered visual-tree shape via Reactor's existing tree-inspection helpers.
- **Serialization round-trips.** SaveJson → LoadJson → assert structural and identity equivalence. Cover schema-version migration (v1 phase-1 fixtures load in v2).
- **Insertion-policy hooks.** Register an `IDockLayoutStrategy`, assert `BeforeInsert*` decisions land where expected.
- **Cancellable events.** Set `Cancel = true` in `OnDocumentClosing` / `OnToolWindowHiding`, assert the transition aborts and state is unchanged.
- **PreviousContainer memory.** Hide → Show → assert container identity is preserved across the cycle.
- **Composition-driven content updates.** Mutate component state that feeds the `DockNode` tree, assert pane creation/removal occurs and that *unchanged* panes preserve their per-pane state across the re-render (keyed reconciliation per spec 042).
- **Content rehydration via composition.** Save a layout, restart, assert the component-supplied content lands in the restored slots matched by `Key` (no separate `ContentResolver` callback — the component itself is the resolver).
- **Hook re-render scope.** Assert `UseActivePaneKey` re-renders only the consumer when active changes (not the whole subtree); assert `UseDockState` transitions on adopt/promote (phase 3).
- **State migration (phase 3).** Mount a `DockableWindow`, `PromoteToFloating`, `AdoptIntoHost(otherHost)`, assert per-pane `useState` / `useEffect` state survives.

**UI automation (a small, deliberate set).** Some interaction quality questions genuinely cannot be answered from in-process inspection — anything involving real OS-level pointer-down → drag → pointer-up timing, HWND creation latency on tear-out, or the cross-window HWND drag protocol. For these, a *small* number of automation fixtures use Reactor's existing AT-tree-based test harness in `Reactor.AppTests.Host` (the same harness used for `ImmediateAndDisabledFocusableTests`, navigation, datagrid edit). Target list — **strictly bounded to ~5–8 tests total across all phases**:

  1. Drag a tab from one group to another within the same host (phase 1).
  2. Tear out a tab → assert a new `ReactorWindow` exists (phase 2).
  3. Drag a floating window's title bar → assert position changes via `AppWindow.Position` (phase 2).
  4. Ctrl+Tab navigator opens and selects a pane (phase 2).
  5. Cross-shell drag from one Reactor `Window` to another (phase 3).
  6. Tab-in-title-bar hit-testing: drag from the title-bar tab → tear-out fires (phase 4).
  7. Title-bar drag-region: drag from the non-tab area of the title bar → window moves, no tear-out (phase 4).

We **do not adopt FlaUI** even though AvalonDock uses it as its primary automation surface. FlaUI is an external driver designed to drive *any* WPF/WinUI/Win32 app; it adds an IPC hop, fights for keyboard focus with the test process, and is a frequent source of flake in unattended CI. Reactor's in-process AT-tree harness avoids all of that. The AvalonDock FlaUI test *scenarios* remain a useful checklist (their `FloatingWindowTests`, `DocumentTabTests`, `KeyboardNavigationTests`, `LayoutSerializationTests`, `ToolWindowVisibilityTests` describe behaviors worth covering), but the implementation belongs to selftests.

A coverage gate on `Reactor.Docking` mirrors the policy applied to other components.

### 8.4 Documentation

Docs live in `docs/_pipeline/apps/docking/` and template into `docs/guide/docking.md` (the existing guide pipeline — `docs/guide/*` is generated output; never hand-edited per the project rule). Skill content lives in `skills/docking.md` once the API stabilizes (phase-1 exit at the earliest).

### 8.5 Performance — additional concerns

Expanding on §8.1's budgets with concerns that need to be designed *into* the implementation rather than measured against budgets:

- **Zero allocation during drag.** Pointer-move events fire at display refresh rate. The hover-state update path must not allocate (no LINQ on hot path, no string concatenation for AT names per event, drop-target hit-test results pooled). Verified via Reactor's allocation-counting test harness (precedent: spec 034 element-allocation reduction).
- **Reconciler diff cost on layout shape change.** A drag that mutates layout structure (split, merge, side-pin) re-renders the whole dock subtree. Cost scales with pane count; target ≤ 1 ms diff for 50-pane layouts. Mitigation: `DockNode` is a sealed record hierarchy with cheap equality; keyed reconciliation skips unchanged subtrees.
- **Floating-window HWND creation latency.** Cold-create of a top-level Win32 window through WinUI is in the 30–80 ms range and *cannot* run on the UI thread without a frame stall. Mitigation: in phase 2 tear-out, the HWND is created on the dispatcher with the pane subtree pre-rendered into a `Border` host, then handed off — the user sees the floating window in the same frame the drag releases. In phase 3, `DockableWindow` already keeps an HWND warm only when floating; adoption→promotion creates the HWND lazily but pre-warms the element tree.
- **DPI change cost.** When a floating window crosses a monitor boundary, the entire docked content re-measures. Target ≤ 16 ms for the re-layout; longer is acceptable for content rendering. Reuses spec 036 §5 DPI machinery.
- **Cold start of a persisted layout.** A 50-pane layout restored at app launch should render first frame in ≤ 200 ms (matching Reactor's startup-perf budget). Mitigation: panes outside the current viewport defer content `useEffect` registration until first visible; the `DockNode` tree itself is structurally cheap.
- **Memory profile.** Pane records carry the `Content` `Element` tree. Closing a pane drops the reference; the next reconciler tick collects. There must be no static dictionary of all-time pane keys, no `DragDropHelpers`-style GUID→object table that outlives the drag, no captured-closure leaks on `OnLayoutChanged` event subscriptions.
- **SetDragRectangles throttling (phase 4).** Already in §7.3 risks; reiterated here as a perf budget item — debounce to layout-measure changes, not pointer-move.

### 8.6 Localization

**Already covered:**
- Phase 4 §7.1.5 / §7.4 item 26 covers RTL caption-button inset awareness (chrome direction).
- §5.3.3 Ctrl+Tab pane navigator is a UI surface that lives in the docking layer (subject to l10n).

**Adding now.** Every user-facing string the docking subsystem renders must route through Reactor's localization system (spec 005, `IntlAccessor`). The docking subsystem owns these string surfaces:

- Drop-target tooltips ("Dock left", "Split right", "Add as tab"). Currently in WinUI.Dock these are visual-only icons — when we add AT names (§8.7), the AT names must be localized.
- `NavigatorWindow` headings ("Documents", "Tool Windows", "Active"; pane-state labels).
- Per-pane context-menu items ("Close", "Hide", "Float", "Pin to side", "Auto-hide", "Move to next group"). Phase 2 introduces these; all routed through `IntlAccessor` from day one.
- Side-pin sidebar tooltip ("Solution Explorer").
- Floating-window default title fallback ("Floating Window" when no `Title` set).
- Error / fallback strings for layout-restore failures (§8.8 reliability).

What apps own (not l10n responsibility of the docking subsystem):
- `Document.Title` / `ToolWindow.Title` — app-supplied. Docs (§8.4) note these are user-facing strings the app must localize.
- Pane content — by definition.

Localization resource keys land in `src/Reactor/Resources/Reactor.resw` under the `Docking.*` prefix. Existing pipeline (per spec 005) generates `.xlf` files for downstream loc.

### 8.7 Accessibility

**Already covered:**
- §5.3.3 keyboard navigation: Ctrl+Tab pane navigator, Ctrl+F4 close pane, keyboard-initiated drag with arrow-key drop-target navigation.
- §6.8 item 19 (phase 3) screen reader pass on adopted vs floating distinction.
- §7.4 item 27 (phase 4) high-contrast theme legibility.
- §3.1 matrix tracks accessibility per framework — Reactor docking aims for the column-leader cells.

**Adding now.**

*AT roles and identity:*
- Each `Document` exposes UIA role `TabItem` inside its containing `DocumentGroup` (which exposes `Tab`). Each `ToolWindow` exposes UIA role `Pane` with a localized `AccessibleName` derived from `Title`. Auto-hidden tool windows on a side strip expose role `Button` (the sidebar entry) until expanded, then `Pane`.
- Each pane carries a stable `AutomationId` derived from `Key.ToString()` so AT and selftest automation can address panes deterministically across renders.
- `DockHost` itself exposes a `LandmarkRegion` with a localized name (the showcase will use "Editor area" / "Tool area" / etc., set by the app).

*Keyboard accessibility (expanding §5.3.3):*
- All drop targets are focusable. During a keyboard-initiated move (`Ctrl+Shift+M` per §5.3.3), focus enters the drop-target overlay; arrow keys move between targets; Enter confirms; Esc cancels.
- Splitter handles are focusable and resizable via arrow keys (precedent: WPF `GridSplitter`).
- Tab strip is fully arrow-key navigable; `Ctrl+PageUp`/`Ctrl+PageDown` for next/previous (VS parity); `Ctrl+W` / `Ctrl+F4` to close active.
- `Alt+F7` opens a hidden-pane picker (re-show a closed-but-remembered tool window — pairs with §5.3.9 `PreviousContainer`).
- All keyboard chords are configurable via spec 027 input binding.

*Screen reader announcements (live regions):*
- Layout state transitions announce via a docking-owned ARIA-equivalent live region (UIA `LiveSetting=Polite`): "MainView.xaml moved to right pane", "Output pinned to bottom", "Properties window torn out".
- The live region is one shared resource per `DockHost`, registered via `Reactor.Hosting`'s AT bridge.

*Focus management:*
- After a pane is closed, focus moves to the next-active pane in the same group, or the host if the group becomes empty. Never lost to no-element.
- After tear-out, focus moves to the newly-created floating window's active pane.
- After re-adoption (phase 3), focus moves to the adopted pane in its new home.

*Touch targets and pointer hit-zones:*
- Drop-target buttons are minimum 44 × 44 DIPs (WCAG 2.5.5 / Microsoft Fluent guidance). Splitter handles minimum 8 DIPs visual / 16 DIPs hit-test.
- Tab-strip hit-test extends 4 DIPs past the visual border for forgiveness on close-button targeting.

*High-contrast and reduced-motion:*
- High-contrast mode: covered by spec 001 theming for all docking chrome. Phase-4 review (§7.4 item 27) explicitly verifies.
- Reduced-motion (`Windows.UI.ViewManagement.UISettings.AnimationsEnabled`): drop-preview animations, side-popup slide, tab-reorder slide all disabled. Static positioning, no transitions. Verified in selftests.

*A11y-specific selftests* (added to the §8.3 matrix):
- AT-tree assertion: mount a host, walk the AT tree, assert role/name/AutomationId for every pane.
- Keyboard-only docking: drive the whole "open / move / pin / close" cycle through keyboard input in a selftest; assert state transitions and live-region announcements.
- Focus invariant: after every transition, assert the focused element is a valid focus target inside the host (not null, not disposed).

### 8.8 Globalization (RTL and bidi)

**Already covered:**
- Phase 4 §7.1.5 / §7.4 item 26: caption-button inset RTL handling.

**Adding now.**

*Flow direction propagation:*
- `DockHost` honors `FlowDirection` from its `RenderContext` (Reactor's existing FlowDirection propagation — spec 005). When RTL:
  - Sidebar order flips: left side becomes right, right becomes left (visually); semantically `LeftSide` / `RightSide` retain their *logical* meaning (left of reading order). This matches Office / VS RTL conventions.
  - Tab order in `DocumentGroup` flips (first tab on right).
  - Drop-target overlay mirrors (DockLeft icon appears at the right edge).
  - Splitter drag direction inverts to match.
- Floating-window position math (`x` increases rightward in screen coords) is RTL-invariant — screen coordinates don't flip.

*Bidi text in titles:*
- `Document.Title` / `ToolWindow.Title` route through the WinUI `TextBlock` bidi pipeline. Mixed LTR-in-RTL titles (e.g. "ملف.cs") render correctly without docking-specific handling.

*Persisted layouts and locale:*
- Layout JSON uses invariant culture for all numeric fields (positions, sizes, ratios). A layout saved in `de-DE` (comma decimal separator) must load in `en-US` and vice versa. `JsonSerializerContext` with default settings already produces invariant output; covered by a selftest assertion.

*Selftest:* mount the showcase under RTL `RenderContext`, assert visual-tree shape mirrors and that pointer hit-tests resolve to the mirrored regions correctly.

### 8.9 Security

**Already covered:**
- N3 in §2.2: cross-process docking is out of scope, eliminating a class of attack surface.
- §4.6 risk: vendored code provenance is pinned in `VENDORED.md`.

**Adding now.**

*Layout JSON deserialization safety.* Layout JSON is persisted user data that crosses a trust boundary (file system → process memory). Defenses:

- **Size limit:** parser refuses inputs > 1 MB. A 50-pane layout produces ~30 KB; 1 MB is a generous ceiling.
- **Depth limit:** nesting depth capped at 32 (configured via `JsonReaderOptions.MaxDepth`). Deeper inputs are rejected as malformed.
- **Schema validation:** every node is validated against the v2 schema before being applied to the model. Unknown fields are tolerated (forward-compat). Required fields missing → reject the whole layout, fall back to default.
- **No code paths from JSON.** Layout JSON describes structure and identity (`Key`, `Title`, `width`, etc.) only. It carries no expression, no command, no type name to instantiate via reflection. `ContentResolver` was removed (§5.3.7); the component is the resolver and runs only app-trusted code.
- **AOT-clean parsing.** `JsonSerializerContext` for all docking types — no reflection at runtime, eliminating an entire class of trim/AOT exploit surface.
- **No external schema URLs.** `$schema: 2` is a version integer, not a URL. No network fetch.
- **Failure mode:** corrupt or unparseable layout → log via `ReactorEventSource` (per spec 044), fall back to default layout, app continues. **Never throws on the load path.**

*Persisted state isolation.* Per-pane state (§5.3.2) lives in `WindowPersistedScope` keyed by `(window-id, dockable-key)`. Two windows with the same `PersistenceId` accessing the same pane key see the same state — which is the desired UX semantic but means apps must not put cross-user secrets in pane state without a higher layer (e.g., DPAPI). Documented in §8.4 docs.

*Cross-window drag-drop payload.* In-process drag uses object refs (per §5.1 item 4, rebuilt on input-and-gestures), not the WinUI.Dock string-keyed GUID table (which is replaced; see §8.10 reliability). No untrusted serialization across the drag.

*Tray-icon flyout adoption (phase 3).* A tray-icon flyout can host a `DockHost`. The flyout window's HWND has the same trust boundary as any other Reactor window — no privileged-region exposure. The flyout closing while a pane is being dragged out is handled by the same lazy-HWND mechanism as ordinary tear-out (§6.7).

*Vendored code maintenance.* `third_party/WinUI.Dock/` ships at phase 1 and goes runtime-unused at phase 2 exit (§5.6). After phase 2 exit, security advisories against upstream WinUI.Dock are *not* an active concern — the code path doesn't execute. We still subscribe to their issue tracker; if a CVE-class issue affects vendored code we're still distributing, we patch or remove.

### 8.10 Reliability

**Already covered:**
- §5.5 phase-2 risks: overlay z-order, tear-out race, hit-test perf.
- §6.7 phase-3 risks: element-tree migration, lazy HWND, modal `XamlRoot`.
- §7.3 phase-4 risks: WindowsAppSDK API stability, `SetDragRectangles` perf, RTL inset changes, non-Windows-11 graceful degradation.
- §8.3 testing strategy: cancellable-event selftests; serialization round-trip selftests.

**Adding now.**

*Failure modes and recovery:*

- **Corrupt persisted layout** → §8.9 fallback path. Layout-load failures are surfaced via a `ReactorEventSource` event (per spec 044 logging policy) so devtools can show them, but never throw.
- **Multi-display restore where a saved floating-window position is now off-screen** (monitor disconnected). On `LoadLayout`, every floating window's restored bounds are intersected against `DisplayArea.FindAll()` (per spec 036 DPI machinery). If the bounds don't intersect any display, the window is repositioned to the primary display's center, preserving size.
- **Orphaned floating windows when the parent shell closes.** Per `WindowSpec.Owner` (spec 036 §9), a floating dockable window with an owner closes when the owner closes. A floating dockable window *without* an owner (i.e., a `DockableWindow` that's been promoted and lost its host context) is treated as a top-level shell in its own right and persists until explicitly closed.
- **Process crash mid-drag.** Drag state is in-memory only; on restart, the previously-persisted layout is restored (the partial drag is lost, which is correct).
- **`useEffect` cleanup on pane close.** Closing a pane unmounts its `Content` element tree, which runs all `useEffect` cleanup hooks in dependency order. This is a Reactor invariant, not docking-specific — but the docking selftests verify it (effect-counter pattern from existing fixtures).
- **Floating window outliving its `DockHost`.** Phase-2 floating windows are top-level Reactor `Window`s opened by the host. If the host unmounts (e.g., its containing window closes) while floats are open, the floats are closed via `OnLayoutChanging` → cleanup chain. In phase 3, dockable windows decouple from their adopting host on close — they survive and become orphan top-levels.
- **Concurrent layout mutation.** All `DockHostModel` mutations occur on the UI dispatcher. Off-thread access throws — no silent racing. Documented as a contract; not enforced with locks (UI thread affinity is the model).
- **Memory leaks in event subscriptions.** `OnLayoutChanged` and friends are `Action<TArgs>?` props on the `DockHost` record (§5.3.5). Each render passes a fresh delegate; the reconciler holds only the current one. No long-lived subscriber lists, no `+=` accumulation.

*Reliability-specific selftests* (added to §8.3 matrix):
- Inject a malformed JSON layout, assert fallback to default and that the load-error event fires.
- Save a layout with a floating window positioned at (10000, 10000), assert it's repositioned on load (simulated single-display).
- Open 100 panes, close them in random order, assert allocation count returns to baseline (no leak).

### 8.11 Versioning

**Already covered:**
- §5.3.4 layout JSON schema versioning (`$schema: 2`, `IDockLayoutMigration` services).
- §5.4 explicit v1→v2 migration spelled out.
- §9 Migration: phase-to-phase API stability commitments.
- §4.3: phase-1 API is the commitment surface for phase 2.

**Adding now.**

*Layout JSON schema policy:*
- **Backward read-compat:** every shipped major Reactor version reads every previously-shipped schema version. v1 (phase-1 / WinUI.Dock format) is readable through all future versions; v2 (phase-2 native) likewise; future v3+ will be additive.
- **Forward-tolerance:** loading a *newer*-than-known schema version logs a warning and best-effort-parses the parts it understands, falling back to default for unknown structural nodes. This lets a Reactor 1.0 app read a layout file saved by a Reactor 1.1 user without exploding.
- **No deprecation sunset.** Layout files persist for years; the migration ladder stays complete. Cost is small (each migration is ~30 lines).
- **Migration registry:** `IDockLayoutMigration` services are ordered by `(fromVersion, toVersion)` pairs. Loading a v1 file in a Reactor that knows about v3 runs v1→v2 and v2→v3 in order.

*Public API versioning across phases:*
- The §4.3 API surface is the **phase-1 commitment**. Phase 2 is additive only — every phase-1 type and member remains, with the same semantics. Phase 3 renames `DockManager` → `DockHost` with a `[Obsolete]` type-forwarder retained through the next release.
- Phase-3 changes documented in §9 "Migration".
- Semver: docking lives inside the Reactor versioning umbrella. Breaking changes to docking APIs are major-version events for Reactor as a whole.

*WindowsAppSDK version coupling (phase 4):*
- Phase-4 entry bumps the minimum `WindowsAppSDK` to whatever version stabilizes the `TitleBar` control. The bump is announced one release cycle in advance. Apps still on the older SDK fall back to the phase-2/3 chrome (the `TitleBar` adoption is feature-detected — see §7.3 risks).

*Vendored upstream tracking:*
- `third_party/WinUI.Dock/VENDORED.md` records the upstream commit hash. Re-vendoring before phase 1 merge is a routine commit; after phase 2 exit, re-vendoring is at our discretion (we own the code path).

*Persisted-state schema versioning (per-pane state, §5.3.2):*
- Per-pane `TState` is app-defined; its schema versioning is the app's responsibility, not the docking subsystem's. The docking subsystem stores it opaquely as a JSON blob with a `version` field reserved for the app. We document this in §8.4.

## 9. Migration

There is no existing docking code in Reactor today, so "migration" only applies *between phases*:

- Phase 1 → phase 2: API surface guaranteed stable (§4.3 commitment). Layout JSON files written by phase 1 load in phase 2 (schema-version migration in §5.4.4). Apps don't recompile.
- Phase 2 → phase 3: API additive. `DockManager` is renamed to `DockHost`; the old name remains as a type forwarder. `DockableContent` records remain valid (they're treated as inline-defined `DockableWindow`s with no `WindowSpec`). New `IsDockable` opt-in is required to use the Window-integrated mode.

## 10. Implementation plan

Each phase ships in a single feature branch with multiple PRs.

### 10.1 Phase 1 (target: next release after merge)

1. Add `third_party/WinUI.Dock/` with vendored source, `LICENSE`, `VENDORED.md` (commit pin + edits log).
2. Append WinUI.Dock notice block to `ThirdPartyNoticeText.txt`.
3. Add `src/Reactor.Docking.Xaml/` wrapper assembly; add to `Reactor.slnx`.
4. Implement Reactor wrapper element + `DockNode` algebra (§4.3).
5. Implement `IDockAdapter` / `IDockBehavior` thunks.
6. Add `samples/apps/dock-showcase/` with all six scenes (§4.5).
7. Wire into `ReactorGallery`.
8. Phase-1 human review gate (§4.7); record sign-off in PR description.
9. Author docs draft into `docs/_pipeline/apps/docking/` (no hand-edits of `docs/guide/*`).
10. Add `DockingFixtures` minimal smoke fixture to `Reactor.AppTests`.

### 10.2 Phase 2 (target: release N+1)

1. Native split solver on Yoga / `FlexPanel`.
2. Drop-target overlay on Reactor overlay system.
3. Drag/drop on input-and-gestures recognizers (depends on spec 027).
4. Native side popup on Reactor `Popup`.
5. Native floating-window: open as full Reactor `Window`.
6. New layout JSON (`$schema: 2`) + phase-1 migration.
7. Document/ToolWindow split (§5.3.1).
8. Per-pane state (§5.3.2).
9. Keyboard navigation incl. Ctrl+Tab pane navigator overlay (§5.3.3).
10. Cancellable lifecycle events (§5.3.5).
11. `IDockLayoutStrategy` insertion-policy hook (§5.3.6).
12. Fine-grained per-pane permissions: `CanHide` vs `CanClose`, `CanFloat`, `CanMove`, `CanAutoHide`, `CanDockAsDocument` / `CanDockAsToolWindow` (§5.3.8).
13. `PreviousContainer` show-panel-where-you-left-it (§5.3.9).
14. `DockHostModel` layout-as-model surface (§5.3.10).
15. `DockContext` + hook surface (`UseDockHost`, `UseActivePaneKey`, `UseIsActivePane`, `UsePane`, `UseDockState`, `UseDockLayout`) (§5.3.11).
16. Remove `Reactor.Docking.Xaml` assembly from `Reactor.slnx`; keep `third_party/` source.
17. Phase-2 human review gate (§5.7).
18. Perf benchmarks per §8.1 + zero-allocation drag verification (§8.5).
19. Selftest scenarios informed by AvalonDock FlaUI scenarios but implemented as Reactor selftests (§8.3).
20. Localization: `Docking.*` resource keys wired through `IntlAccessor` (§8.6).
21. Accessibility: AT roles / `AutomationId` per pane, focusable drop targets + splitters, live-region announcements, focus invariants, reduced-motion handling, 44-DIP touch targets (§8.7).
22. Globalization: RTL `FlowDirection` propagation; invariant-culture JSON; bidi title handling (§8.8).
23. Security: JSON size/depth limits, schema validation, AOT-clean parsing, safe-fallback on corrupt layout (§8.9).
24. Reliability: off-screen-restore repositioning, malformed-layout selftest, leak-baseline selftest (§8.10).

### 10.3 Phase 3 (target: release N+2)

1. `WindowSpec.IsDockable` and friends (depends on spec 036 having shipped).
2. `DockHost` rename + global registry.
3. Reconciler element-tree migration (the big one) — state preservation across host boundaries.
4. `PromoteToFloating` / `AdoptIntoHost` reconciler-driven primitives.
5. Cross-shell drag (lifts spec 036 N2 for dockable windows).
6. Shell integration (taskbar / tray / jumplist for floating dockable windows).
7. Devtools / MCP additions (§8.2).
8. Showcase sample rebuilt (§6.6).
9. Phase-3 human review gate (§6.8).
10. AT/screen-reader pass.

### 10.4 Phase 4 (target: release N+3)

1. WindowsAppSDK min-version bump (whatever ships `TitleBar` stably).
2. Floating-window chrome switches to WinUI `TitleBar` control.
3. Tab-in-title-bar render path for single-group floats.
4. `SetDragRectangles` integration; per-frame debounce.
5. Caption-button inset awareness in tab-strip layout (incl. RTL).
6. Active-tab → window-title/icon propagation.
7. Backdrop coordination pass (Mica / Acrylic with new chrome).
8. Theme + dark-mode + high-contrast QA pass against showcase.
9. Phase-4 human review gate (§7.4).

## 11. Resolved questions

- **Where does the spec live?** `docs/specs/045-docking-windows-design.md` (this file).
- **Phase 3 integration model?** Window-variant (DockableWindow is a Window; any Window can be adopted). Confirmed at spec-time per clarifying question.
- **Phase 1 implementation strategy?** Vendor WinUI.Dock as-is plus minimal light edits; wrap in Reactor element. Confirmed at spec-time.
- **Competitive scope?** Full matrix across all named frameworks plus AvalonDock/DockPanelSuite/Qt/GoldenLayout (§3.1). Confirmed at spec-time.
- **License compliance?** WinUI.Dock is MIT (`Copyright (c) 2025 qian-o`). Notice appended to `ThirdPartyNoticeText.txt` in this PR (§12).
- **Phasing gate mechanism?** Human-in-the-loop review checklist at each phase exit (§4.7, §5.7, §6.8, §7.4). Mandatory.
- **MVVM binding API (`DocumentsSource` / `LayoutItemTemplate`)?** Rejected. Reactor's functional composition produces the dock tree from component state every render; that *is* the binding. A parallel binding pipeline would compete with the reconciler. See §3.2 lesson #3 and §5.3.7. The deepest piece of evidence: AvalonDock added this surface because WPF had no other way to map data to UI; Reactor does, so we don't.
- **How do nested components observe and act on dock state?** Property hooks and context — `DockHost` provides a `DockContext`, descendants resolve via `UseDockHost` / `UseActivePaneKey` / `UseIsActivePane` / `UsePane` / `UseDockState` / `UseDockLayout`. See §5.3.11. This is the canonical Reactor pattern (mirrors navigation, theme, data-context).
- **`TabView` retained or rewritten in phase 2?** Retained. WinUI's `TabView` carries the right accessibility shape; we don't gain enough by rewriting it. Revisit only if AT or theme issues surface.
- **Cross-shell drag scope (phase 3)?** Same-process only — shells share a `ReactorApp` instance. Out-of-process is N3.
- **`DockHost` for non-dockable static splits?** No. Static splitter layouts are `FlexPanel`; conflating is a category error.
- **Layout JSON: own schema or adopt GoldenLayout's?** Own. GoldenLayout's web-shaped `componentName` string registry doesn't match Reactor identity (keys are runtime objects, not registered names).
- **`Adapter` / `Behavior` interfaces from WinUI.Dock — survive or replace?** Replace. Phase-1 has them verbatim for fidelity; phase 2 collapses them into `Action<TArgs>?` props and `On*` events on the `DockHost` record. Breaking changes here are acceptable.
- **Touch input revisited in phase 3?** No. Touch tear-out remains its own future spec (N1).

## 12. Notice update

Adding the following block to `ThirdPartyNoticeText.txt` as part of this spec PR:

```
------------------- WinUI.Dock ----------------------
MIT License

Copyright (c) 2025 qian-o

Source: https://github.com/qian-o/WinUI.Dock

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
------------------------------------------------------
```

## 13. Out of scope (across all phases)

- Touch tear-out (N1).
- Named workspace presets UI (N2).
- Cross-process docking (N3).
- Uno / web parity (N4).
- TabView replacement (N5).
- Drag-state mid-flight devtools introspection (N6).
- Any backend other than WinUI 3 on Windows desktop.
