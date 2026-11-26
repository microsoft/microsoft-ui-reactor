---
title: "0.1.0-preview.17"
type: changelog
date: 2026-11-04
changelog:
  version: "0.1.0-preview.17"
description: "21 added, 9 changed, 2 deprecated, 2 removed and 54 fixed."
---

## Added

- `JumpList.GetRemovedArgumentsAsync()` reads user removals without updating the list.
  `UpdateAsync` and `ClearAsync` now return the removals observed in their own transaction,
  allowing apps to retain the feedback before Windows clears it. Await-only callers
  remain source-compatible; existing binaries must be rebuilt for the changed return types
  (spec 036 §11.3).

- Optional `ItemsHost.SetCollection` and property-owned teardown via the three-callback `ImperativeBridged` overload ([control reconciler guide](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)).

- **ReactorGallery catches up with recent framework features.** A new **System →
  File pickers** page demonstrates `PickFileAsync`, `PickMultipleFilesAsync`
  (with labelled `FileTypeChoices`), `PickFolderAsync` / `PickMultipleFoldersAsync`
  and `PickSaveFileAsync`; a **Patterns → ErrorBoundary** page covers catch-and-retry,
  static fallbacks, and the built-in fallback a `RenderErrorHandler` replaces; the
  Grid page adds `GridSize.MinSize` / `MaxSize`; and the TitleBar page shows icon
  inheritance and `.NoIcon()`. The gallery now declares its window icon once via
  `ReactorApp.Run(icon:)` and its shell title bar inherits it.

- **Component inspection for devtools: `ReactorDiagnostics.DescribeComponent`,
  `TrySetState`, `Rerender`, `GetAppliedProperties`, `GetReferenceEdges`.** An
  inspector can go from a realized control to the component behind it: its name,
  kind, props, hooks and consumed contexts as a text `ComponentSnapshot`; set a
  `UseState` / `UseReducer` / `UsePersisted` value from text (parsed to the hook's
  type, refused with a reason otherwise) and re-render it; list the WinUI
  properties a control's modifiers set (including the caption-derived default
  `AutomationProperties.Name`); and list the references it declares, including
  pending ones whose target has not mounted. Component snapshots and property
  lists are text — no live component, element or hook object; a resolved
  reference edge carries its mounted target control. Reads
  are on demand and UI-thread-affine. The render path gains only reference
  stores: per component, `UseContext` records the context it read and
  reference-list wiring keeps the authored list; per host render, the host
  records which root produced the displayed control (and, when it renders into its
  `ContentTarget`, that it owns it). With source mapping on, an open
  `ContentDialog` is tagged with its element so its chrome resolves to the
  `ContentDialog(...)` call site (spec 010, spec 057 §3.1).

- **`REACTOR_DYM_006`: color strings that render gray** (issue [#1375](https://github.com/microsoft/microsoft-ui-reactor/issues/1375), spec 061 §7.3). A new
  default-on **Warning** flags compile-time-constant color strings that `BrushHelper` can't
  parse and therefore silently fall back to gray: `.Background("orange")`, `.Foreground("#888")`,
  or a font name passed to `ResourceBuilder.Set(key, string)` under a `*FontFamily` key. It covers
  `BrushHelper.Parse`, `ResourceBuilder.Set(string, string)` and the `string` overloads of
  `Background` / `Foreground` / `BorderBrush` / `WithBorder`, suggests the exact hex (or
  `new FontFamily(...)` for a `*FontFamily` key), and ships a code fix; `mur check`'s new
  `ColorStringLiteralRule` repeats the rewrite as a `→ try:` hint. Unlike `REACTOR_DYM_001`–`005`
  it fires on code that compiles, so a project with `TreatWarningsAsErrors` can stop building on
  an existing gray color. Fix the value, or adjust the severity with
  `dotnet_diagnostic.REACTOR_DYM_006.severity` in `.editorconfig`.

- **Typed `ResourceBuilder` overloads for non-brush styling values** (issue [#1369](https://github.com/microsoft/microsoft-ui-reactor/issues/1369), spec 015
  Proposal 2). `.Resources(r => r.Set(key, value))` previously accepted only color strings,
  `Brush`, `ThemeRef`, `double` and `CornerRadius`, so lightweight-styling keys of any other
  type — 362 `Thickness` keys alone in WinUI's `generic.xaml` — needed a
  `.Set(control => control.Resources[...] = ...)` callback on the native control instead.
  `Set` now also takes `Thickness`, `FontWeight`, `FontFamily`, `bool` and `GridLength`, and
  the new `SetColor(key, Color)` writes a literal `Color` resource. It is deliberately not a
  `Set` overload: `Set("ButtonBackground", Colors.Red)` stays a compile error, because a raw
  `Color` stored under a brush-typed key silently changes nothing. Existing calls bind
  as before: integer arguments still select `double`, and strings still create brushes.

- **Inspector diagnostics: call-site column and a per-component render event**
  (issue [#1326](https://github.com/microsoft/microsoft-ui-reactor/issues/1326), spec 010 §1.1):
  - `SourceLocation.ColumnNumber` (1-based, `0` = unknown), a third positional
    member: `new SourceLocation(path, line)` now needs a column. The source-map generator
    stamps the column of the invoked method's name, so several calls on one line
    resolve to distinct positions. `ToString()` stays `file:line`.
  - `ComponentRendered` on the `Microsoft-UI-Reactor` provider (EventId 40,
    Verbose, keywords `Render | RenderDetail`; `RenderDetail` = `0x4000` is new):
    one event per component render, including mount, the host root and a render
    that threw. The payload carries the component name, a stable `componentId`,
    the `reason` (`mount` / `state` / `props` / `context` / `parent` /
    `hotReload` / `forced`) and the elapsed time.
  - `ReactorTrace.GetComponentControl(long)` and
    `ReactorTrace.TryGetComponentId(UIElement, out long)` map a `componentId` to
    the component's on-screen control and back, so an inspector can flash what
    re-rendered. A host root's id only maps forward, to the root content: that
    control is often also a child component's wrapper, whose id it reports.

- **Inspector diagnostics for hosts, roots and component boundaries** (spec 010).
  `ReactorDiagnostics.GetHosts()` (`Microsoft.UI.Reactor.Core.Diagnostics`) returns
  a snapshot of every live `ReactorHost` and `ReactorHostControl` — window,
  host element, root control, root component or render-function name, and
  `MountSite`, the `ReactorApp.Run` / `OpenWindow` / `Mount` call that mounted the
  root (recorded by the source-map generator when source mapping is on). While
  `ReactorSourceMap.Enabled` is true, every component's wrapper is also tagged, so
  `Reconciler.GetElementTag` finds each component boundary. Provisional API for
  external inspectors such as `winapp devtools`.

- **Inspect Reactor apps with no managed agent, Native AOT included: `ReactorSource`
  on every control** (issue [#1341](https://github.com/microsoft/microsoft-ui-reactor/issues/1341), spec 010):
  - `ReactorDiagnostics.SourceProperty` (`"ReactorSource"` in XamlDiagnostics) is an
    attached string published on every realized control in diagnostics mode. It is
    readable out of process with no managed code: call site, owner component,
    element kind, key, mounted component, host root, declared name and hook names,
    in a versioned `|`-separated grammar (`v=1`). Paths are relative to the
    project (XAML parity); never absolute.
  - `SourceLocation.DeclaredName`: the identifier an element was assigned to, like
    `x:Name`. The source-map generator also records each component render's hook
    variables (`hooks=0:count@36;…`).
  - Gating: new MSBuild property `ReactorDiagnostics` (Debug default, `true` for a
    Release / Native AOT devtools build; turns on the trimmable
    `Reactor.DevtoolsSupport` switch and implies `ReactorSourceMap`) plus
    `REACTOR_DIAGNOSTICS=1` at launch. Without the switch, trimmed and AOT builds
    contain none of it; with it but without the variable, nothing is published.
  - Source-mapped builds share one call-site bucket per call site instead of
    one per element. Under Native AOT diagnostics mode, elements whose only extra
    is their call site are not tagged (no managed agent can read the tag);
    `ReactorSourceMap.GetSource` resolves them from the published value.
  - **Changed:** Debug builds now turn `Reactor.DevtoolsSupport` on by default
    (through `ReactorDiagnostics`); opt out with `-p:ReactorDiagnostics=false`.

- **`ReactorHostControl` can be declared in XAML.** It gains a public parameterless
  constructor (the XAML compiler rejected the old optional-parameter one with
  `WMC0100`) and a `ComponentType` property, so a hybrid page can write
  `<reactor:ReactorHostControl ComponentType="local:StatsCard" />`. The root is
  created through the app's generated XAML type information, so it stays trim- and
  AOT-safe. A `ComponentType` that is only assigned from code has no such entry; the
  host shows an error naming the fix (`ComponentFactory` / `Mount`) instead of mounting,
  and a throwing or null-returning `ComponentFactory` is reported the same way rather
  than escaping the `Loaded` handler and terminating the app. `samples/ReactorHostControlDemo`
  now declares its counter host in markup, and `samples/InteropFirst` declares its host
  with `x:Name` (issue [#1324](https://github.com/microsoft/microsoft-ui-reactor/issues/1324)).

- **`ReactorHostControl.IsIdle` / `WaitForIdleAsync(int maxYields = 50)`**, matching
  `ReactorHost`. Await it after a `setState` or `Mount` to read a XAML island's realized
  tree back once its render loop settles, instead of waiting on wall-clock time. Both
  hosts now share one idle-wait loop (issue [#1327](https://github.com/microsoft/microsoft-ui-reactor/issues/1327)).

- **`Theme.NotifyResourcesChanged()`** for runtime resource edits (a brand dictionary
  swap, a replaced brush in an app-level dictionary, an inspector's live
  resource edit). Reactor caches each resolved `(key, theme)` brush, and until now cleared
  that cache only on a theme or palette change, so such an edit was invisible to `ThemeRef`
  modifiers, overrides and `ThemeRef.Resolve`. The call clears the cache and re-renders
  every live host past memoization and the skip of unchanged elements. Callable from
  any thread (issue [#1327](https://github.com/microsoft/microsoft-ui-reactor/issues/1327)). A brush resolved eagerly and kept (`ThemeRef.Resolve` inside a
  memo factory, or stored in state) is a fixed value and is not refreshed; bind it with
  `Theme.Ref` instead.

- **Getting Started documents the single-file path.** A Reactor app does not need
  a `.csproj`: .NET 10 runs a lone `.cs` file whose `#:package` / `#:property`
  header supplies what a project file otherwise would. The header also references
  `Microsoft.Windows.SDK.BuildTools.WinApp`, so `dotnet run counter.cs` launches
  the app packaged, just like `winapp run counter.cs`; `WindowsPackageType=None`
  runs it unpackaged (winappCli#794 / [#874](https://github.com/microsoft/microsoft-ui-reactor/issues/874), shipped in winapp 0.7.0). The
  agent-kit skills use the same header.

- **Validation now works on any control, not only inside `FormField`** — the
  Forms guide's "Validation Context" example needed new surface to work as
  written (issue [#1262](https://github.com/microsoft/microsoft-ui-reactor/issues/1262)):
  - `ValidationContext.Changed`, raised when the context's observable state
    changes (a message appearing or disappearing, a field becoming touched, a
    reset). `UseValidationContext()` subscribes to it, so mutating the context
    from an event handler repaints the form.
  - `ValidationReconciler.EvaluateRulesAsync(…)`, the asynchronous counterpart to
    `EvaluateRules` for rules built with `ValidationRuleAsync`. Rules run in
    order, so the resulting message order matches the order given.
  - `EvaluateRules(ctx, setId, …)` and `EvaluateRulesAsync(ctx, setId, …)`, named
    rule sets: the call owns that set, so a rule that disappears from it has its
    message withdrawn. The identity is explicit because ownership is destructive
    — two unrelated callers sharing one context must not silently retract each
    other's rules.
  - `FormField` marks its field touched when the editor loses focus, so the
    default `ShowWhen.WhenTouched` reveals errors on blur as the guide describes.
    Nothing in the framework called `MarkTouched` before, leaving that default
    unreachable unless the app marked fields by hand (forms-validation tasks §1E.1,
    `docs/specs/tasks/forms-validation-implementation.md`).
  - `ValidationContext.SubmitAttempted`, recorded by `MarkAllTouched()` and
    cleared by `ResetAll()`. This is what `ShowWhen.AfterFirstSubmit` waits for;
    the framework had no notion of a submit before, so that policy could never
    display anything.

- **Apps can replace the built-in render-error fallback (issue [#1291](https://github.com/microsoft/microsoft-ui-reactor/issues/1291)).** Outside an
  `ErrorBoundary`, a render failure showed a fallback with the full exception text
  (type, message, stack trace), which a shipped app could not hide. A
  `RenderErrorHandler` now replaces it, set app-wide
  (`ReactorApp.DefaultRenderErrorHandler`), per window (`WindowSpec.RenderErrorHandler`)
  or per host (`ReactorHost` / `ReactorHostControl.RenderErrorHandler`). It sees the
  root render, child renders, the reconcile pass, effects and dispose-time cleanups,
  through a `RenderError` that carries the exception and its `Source`. Return an element
  to show it, `null` to keep the built-in fallback, or call `Propagate()` to route the
  exception to `ReactorApplication.OnUnhandledException`. A handler that throws (or whose
  fallback throws) fails closed to a neutral message without exception details. With no
  handler, behavior is unchanged.

- **Framework mechanics are searchable in the ReactorGallery index (spec 064,
  issue [#1275](https://github.com/microsoft/microsoft-ui-reactor/issues/1275)).** `find-ui --source reactor` answered "what is control X" but not
  "how does mechanism Y work": `UseState hook` and `key down event handler`
  returned nothing, and `state management` returned CheckBox. Nine
  **Fundamentals** gallery pages — hooks, element refs, keyboard and pointer
  input — are now indexed alongside the controls, and the index emits the
  contract's `curatedKeywords`, `docs` and `details` fields. `details` is lifted
  verbatim from the shipped agent-kit skills, so the two cannot drift.

- `Publish docs` now verifies the live site after deploying. The `publish` job stamps the
  Pages artifact with the run that built it, and a new `verify` job polls
  <https://microsoft.github.io/microsoft-ui-reactor/> until it is serving *that* run —
  failing the workflow otherwise. Every probe carries a unique query key so a pass cannot
  come from cached content, and an already-published version fetched with the same request
  shape acts as a positive control, so a broken probe is reported as unverified rather than
  blamed on the deployment. (issue [#1268](https://github.com/microsoft/microsoft-ui-reactor/issues/1268))

- **`mur templates status`** reports whether `dotnet new reactor` actually resolves, rather than
  merely whether the template pack id is registered — an installed-but-too-old pack (`0.0.6-alpha`
  shipped before the Reactor templates existed) is a PASS on the package check and a failure on the
  next scaffold. Exit codes are load-bearing: `0` available, `1` probe failed, `2` pack installed
  but too old, `3` pack not installed. It backs `mur doctor` and `bootstrap.ps1`'s verification
  step, which give different remediation for each.

- **Declarative caption-button theme (issue [#1297](https://github.com/microsoft/microsoft-ui-reactor/issues/1297)).** `WindowSpec.TitleBarTheme` and
  `TitleBar(...).PreferredTheme(WindowTitleBarTheme)` set the theme of the window's
  system caption buttons (`AppWindow.TitleBar.PreferredTheme`). WinUI does not carry
  an element's `RequestedTheme` through to the caption, so an app that themes its
  content opposite to the system declares the matching caption theme. Opt-in: with
  nothing declared Reactor never writes the property, so an imperatively set value is
  left alone; removing a declaration restores the value the caption had before
  Reactor first applied one. The spec
  wins over the element, and — unlike the caption height — no content extension is
  required. ReactorGallery uses it so its caption buttons follow the gallery theme.

- **`REACTOR_LIFECYCLE_003` — a custom control updates the child it hosts with
  `Reconciler.UpdateChild` ([#1307](https://github.com/microsoft/microsoft-ui-reactor/issues/1307)).** `UpdateChild` is only correct after a check
  that the new element can update the old control (same element type and key).
  That check is internal, so code outside the framework can't make it, and
  `UpdateChild` also leaves a control it replaced mounted for the caller to
  unmount. A `RegisterType` registration or `IElementHandler` that hosts a child
  this way throws `InvalidCastException` when the child changes element type, and
  a replaced child's effect cleanups and unmount callbacks never run and its refs
  are never cleared. The analyzer points at `Reconciler.Reconcile`, which takes the
  same arguments, does both, and returns the control the slot should hold
  (`UpdateContext.ReconcileChild` in a handler). Where the existing control was
  read from its slot just before the call, the code fix rewrites
  `var x = r.UpdateChild(…); if (x is not null) slot = x;` into
  `var x = r.Reconcile(…); if (!ReferenceEquals(x, existing)) slot = x;`, which
  also empties the slot when the child becomes `Empty()`, and drops a manual
  `UnmountChild(existing)` made through the same reconciler. It is offered only
  where that keeps the code's meaning: for instance, not when the body does more
  than install the result. The rule is silent in the assembly that declares
  `Reconciler`, whose slot owners make the check first. The repository's two call
  sites, the data grid's internal `ResizeGrip` and the regedit sample, now use
  `Reconcile` (neither reached the bad path), and the Extending Reactor Controls
  guide shows a `RegisterType` host reconciling its child.

- **Connected animations between navigation pages (issue [#1414](https://github.com/microsoft/microsoft-ui-reactor/issues/1414)).**
  `NavigationTransition.Connected(key)` was a stub that played the Entrance page
  transition; it now animates the element marked `.ConnectedAnimation(key)` on the
  outgoing page into the matching element on the incoming page. The new
  `NavigateOptions.ConnectedAnimationKey` pairs that shared element with any page
  transition, such as DrillIn or Fade. `NavigationHost` waits briefly for the
  destination's layout (including cold `ScrollView` content and elements an effect
  creates), keeps both pages until both animations finish, and animates back on
  `GoBack()` / `GoForward()`. Cached pages and the active pages of nested hosts take
  part. A missing, duplicate or unmeasurable target falls back to the selected page
  transition, and with Windows animations off the navigation completes immediately.
  Each `NavigationHost` now owns its pages' lifecycle callbacks and guards, so a page
  that hosts an inner navigation stack no longer runs that stack's callbacks and guards
  as its own, and a page whose navigation is interrupted still receives
  `onNavigatedFrom`. See the [navigation guide](/microsoft-ui-reactor/main/guides/navigation).

## Changed

- Consolidate hook and memo dependency comparisons in `RenderContext.DepsEqual`,
  preserving existing equality semantics and typed fast paths
  (spec 020 §6.4; follow-up to [#1431](https://github.com/microsoft/microsoft-ui-reactor/issues/1431)).

- **Markdown `LinkBuilder` now receives a `MarkdownLink`** ([#1436](https://github.com/microsoft/microsoft-ui-reactor/issues/1436)). Breaking: the
  callback's type changes from `Func<Element[], Uri, Element>` to
  `Func<MarkdownLink, Element>`, so existing `(label, uri) => ...` callbacks no longer
  compile; rewrite them as `link => ...`. `MarkdownLink` carries the label as plain `Text`
  (image alt text in place of images, entities decoded, line breaks as spaces), the
  formatted `Label` (`RichTextBlockElement`, `null` when empty) and the validated `Uri`,
  so setting an accessible name or tooltip no longer means walking the label's inlines.
  In 0.1.0-preview.16 the callback was declared but never called — see Fixed.

- **Picker helpers moved to `Microsoft.Windows.Storage.Pickers`, renamed as non-hooks, and
  completed** (issues [#1371](https://github.com/microsoft/microsoft-ui-reactor/issues/1371), [#1374](https://github.com/microsoft/microsoft-ui-reactor/issues/1374), spec 054 §8.3). Breaking: `UseFilePickerAsync` /
  `UseFolderPickerAsync` are now `PickFileAsync` / `PickFolderAsync` on `RenderContext` and
  `Component`, and return path-based `PickFileResult` / `PickFolderResult` instead of
  `StorageFile` / `StorageFolder`. New beside them: `PickMultipleFilesAsync`,
  `PickMultipleFoldersAsync`, and `PickSaveFileAsync` with `SaveFilePickerOptions`, whose
  `FileTypeChoices` is an ordered `IReadOnlyList<FileTypeChoice>` (order sets the default
  entry). Call them directly from event handlers; the hook-rules analyzer no longer flags
  them. Option enums such as `PickerLocationId` now come from
  `Microsoft.Windows.Storage.Pickers`; `ViewMode`, `SettingsIdentifier`, `Title`,
  `SuggestedFolder`, `SuggestedStartFolder`, open-picker `FileTypeChoices` and
  `InitialFileTypeIndex` are new init-only options. The dialog is owned by the window whose
  host rendered the component (previously
  whichever window was most recently active, which with several windows picked the wrong one
  and threw after any window closed), and the pickers now work in elevated apps. They also
  work in content hosted by `ReactorHostControl` (owned by the window the control is loaded
  in; issue [#1395](https://github.com/microsoft/microsoft-ui-reactor/issues/1395)) and by a `ReactorHost` constructed over a raw `Window`.
- **The doc pipeline moved out of `mur`, and doc screenshots are captured with winapp's
  Windows Graphics Capture.** `mur docs` is now `dotnet run --project tools/Reactor.DocPipeline --`
  (`compile`, `check-tier`, `render-diagrams`, `new-diagram`; same options). It is a contributor
  tool for this repository, so it no longer ships in the `mur` dotnet tool; `mur docs` prints the
  new command and exits 1. Out of the tool it can target Windows, which `PackAsTool` forbids
  (NETSDK1146), and capture screenshots in-process with the winapp UI Automation library
  (`Microsoft.Windows.SDK.BuildTools.WinApp.UIAutomation`). The pipeline still launches each doc
  app through the in-app preview host and switches components with it, but the pixels now come
  from Windows Graphics Capture instead of the preview host's `PrintWindow` + JPEG frame stream.
  Images keep the same framing (client area only, physical pixels, the 150% convention), are
  lossless, and content-crop is typically 1–4 px tighter. Capture no longer waits on the frame
  stream's warm-up, so a topic is about 8–10 s faster, and it never activates a window: without
  Graphics Capture a screenshot fails rather than falling back to `PrintWindow`. `mur` also drops
  its YamlDotNet and System.Drawing.Common dependencies ([#1320](https://github.com/microsoft/microsoft-ui-reactor/issues/1320); spec 013 §4).
- **`ReactorHostControl.Stats` returns `RenderStats` by value** instead of
  `ref readonly`. The XAML compiler emits type metadata for every public property of
  a control used in markup, and the by-ref property generated
  `typeof(RenderStats&)`, which does not compile. Reads such as `host.Stats.Fps`
  are unchanged; only `ref` bindings to it and already-compiled binaries are
  affected. `ReactorHost.Stats` is unchanged (issue [#1324](https://github.com/microsoft/microsoft-ui-reactor/issues/1324)).

- **`.Validate(fieldName, value, validators…)` now runs its validators during the
  render that calls it**, instead of only when a `FormField` mounts the element —
  the only consumer that ever ran them. Results are therefore readable by the
  same `Render()` that produced them, which is what the documented
  `When(ctx.HasError(…), …)` pattern requires. `UseValidationContext()` also
  publishes a component-local context to the rendered subtree automatically, so
  `FormField`, the visualizers and nested components no longer need an explicit
  `.Provide(ValidationContexts.Current, ctx)`; an explicit provide still takes
  precedence. The validator-only `.Validate(fieldName, validators…)` overload is
  unchanged and still attach-only. (forms-validation tasks §1A.5,
  `docs/specs/tasks/forms-validation-implementation.md`; issue [#1262](https://github.com/microsoft/microsoft-ui-reactor/issues/1262))

- **The search index emits every clean `SampleCard` on a page, not just the first
  (spec 064 §3.2, issue [#1275](https://github.com/microsoft/microsoft-ui-reactor/issues/1275)).** Regenerate after changing *any* card on a
  gallery page, not only the opening one.

- The `Publish docs` concurrency group now sets `queue: max`. The Actions default keeps at
  most one *pending* run per group and cancels the previous one when a new run queues, so a
  docs push landing while a release tag's run waited behind `main` would evict the tag run
  before it started — leaving the release unpublished. Runs now wait in FIFO order.
  (issue [#1268](https://github.com/microsoft/microsoft-ui-reactor/issues/1268))

- **`bootstrap.ps1` and `mur upgrade` set up the Windows App SDK template pack through
  `winapp`** (PR [#1277](https://github.com/microsoft/microsoft-ui-reactor/issues/1277)). With Reactor's own `reactorapp` template removed (see Removed),
  bootstrap no longer `dotnet new install`s a Reactor templates nupkg; it runs
  `winapp new --list`, which installs `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` (the
  pack behind `dotnet new reactor`) on demand, and `mur upgrade` reports on that pack instead
  of installing templates. `winapp` rather than `dotnet new install`, because the pack is
  prerelease-only and `dotnet new install` has no `--prerelease` switch, so it resolves
  stable versions and fails outright. The new `-WinAppSdkTemplatesVersion` bootstrap
  parameter pins the pack and maps to `winapp new --template-version`; `mur templates status`
  (see Added) backs `mur doctor` and bootstrap's verification.

## Deprecated

- **`Microsoft.UI.Reactor.ProjectTemplates` is deprecated on NuGet.org.** Published versions
  remain restorable but are marked deprecated with a pointer to `dotnet new reactor`. Scaffold the
  replacement with `winapp new -t reactor -n MyApp`, which installs the Windows App SDK template
  pack on demand. To install the pack without scaffolding, pin it explicitly —
  `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates::<version>` — since it is
  prerelease-only and a bare `dotnet new install` resolves stable versions.

- **`bootstrap.ps1 -NpmRegistry` is deprecated and ignored.** `GitHub.Copilot.SDK` 1.0.14 no
  longer downloads its native CLI from npm, so the parameter has nothing to redirect; bootstrap
  now prints a warning and continues. To redirect the CLI download, set the SDK's
  `CopilotCliReleaseBaseUrl` MSBuild property or the `COPILOT_CLI_DOWNLOAD_BASE_URL` environment
  variable, or point `CopilotCliBinaryPath` at a pre-downloaded binary (issue [#1292](https://github.com/microsoft/microsoft-ui-reactor/issues/1292)).

## Removed

- **Removed automatic npm-mirror detection for the Copilot CLI download**
  (`tools/CopilotNpmRegistry.props` and bootstrap's `~/.npmrc` / `NPM_CONFIG_REGISTRY` probe).
  `GitHub.Copilot.SDK` 1.0.14 dropped the `CopilotNpmRegistryUrl` property it set and now fetches
  a SHA-256-verified CLI bundle from the github/copilot-cli GitHub releases, so a configured npm
  mirror no longer affects `dotnet build`. Builds on networks that block GitHub release downloads
  should use `CopilotCliReleaseBaseUrl`, `COPILOT_CLI_DOWNLOAD_BASE_URL`, or
  `CopilotCliBinaryPath` instead (issue [#1292](https://github.com/microsoft/microsoft-ui-reactor/issues/1292)).

- **Removed the in-repo `Microsoft.UI.Reactor.ProjectTemplates` package and its
  `dotnet new reactorapp` template** (`tools/Templates/`). Reactor's project templates now ship in
  the official Windows App SDK `dotnet new` pack
  ([`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI.CSharp.Templates)
  `0.0.7-alpha` and later), which provides `reactor`, `reactor-mvu`, `reactor-navview`, and
  `reactor-tabview` (microsoft/WindowsAppSDK#6620, microsoft/WindowsAppSDK#6786).

  **Behaviour change:** scaffolded apps are now **packaged** (single-project MSIX) rather than
  unpackaged, so `dotnet run` launches with full package identity and requires Developer Mode.
  No template produces the unpackaged shape any more — scaffold with `dotnet new reactor` and set
  `<WindowsPackageType>None</WindowsPackageType>` yourself (see the packaging guide).

  `mur pack-local` no longer produces a templates nupkg and its `--framework-version` flag is
  gone; the release workflow no longer packs or publishes the package.

## Fixed

- **Virtualized list rows now see their ancestors' `Context`** (issue [#1386](https://github.com/microsoft/microsoft-ui-reactor/issues/1386)). Rows of
  `LazyVStack` / `LazyHStack`, `ItemsRepeater<T>`, `ItemsView<T>`, `ListView` / `GridView`
  (typed and untyped) and `TreeView<T>` are built by WinUI during layout, after the
  reconciler has finished walking the tree, so `UseContext` in a row returned the
  context's default value, and hooks built on it misbehaved: child-mode
  `UseNavigation<T>()` threw "found no ancestor NavigationHost", `UseIntl()` ignored a
  `LocaleProvider`, and `UseResource` ignored an enclosing `Pending`. A re-render of the
  parent temporarily fixed the visible rows, but rows that scrolled in afterwards were
  wrong again. Each list now saves its ancestors' provided values when it is reconciled
  and restores them while building a row, so rows see the nearest `.Provide(...)` on first
  display and while scrolling. Rows that previously read a default will now read the
  provided value. Lists built by a custom handler on `ElementFactory<T>` are covered too;
  a custom handler that builds rows itself from `ContainerContentChanging` is not.
  Removing the only `.Provide(...)` above a reference-stable consumer now also re-renders
  that consumer with the default, which it previously missed.
- **Building a list row in the middle of another update no longer corrupts that update**
  (issue [#1386](https://github.com/microsoft/microsoft-ui-reactor/issues/1386)). WinUI can ask a virtualized list for rows synchronously while Reactor is
  still applying an unrelated update, for example when an effect or a control's own
  property setter calls `UpdateLayout()`. Such rows used to join that update's
  bookkeeping, so a row that threw was reported to that unrelated update's
  `ErrorBoundary`, which then showed its fallback. Row building now sets the surrounding
  update's state aside and restores the list's own context and theme, so a failing row
  shows its own error. (Window hooks in such rows are covered by the window-owner fix
  below.)
- On initial use, `UseDataSource` uses one page size for data-source requests, offset cursors,
  and resource row positions, preventing missing or misplaced rows when options
  are omitted or specify a different size ([#1453](https://github.com/microsoft/microsoft-ui-reactor/issues/1453); part of [#1452](https://github.com/microsoft/microsoft-ui-reactor/issues/1452)).

- **Border theme-background precedence.** An unset background now releases local
  values on pooled controls and direct-to-themed updates so a `ThemeRef` style
  can supply the brush. Removing a record background preserves a still-present
  concrete `.Background(brush)` modifier, and removing that modifier restores a
  still-set record background. Explicit brushes and native setters retain
  precedence (spec 050 §6.3).

- **Disposing a host retires its ContentDialogs.** `ReactorHostControl.Dispose()`,
  `ReactorHost.Dispose()`, and standalone reconciler disposal cancel deferred opens
  and detach/hide owned dialogs before user cleanup, without raising the element's
  `OnClosed`. Previously effect cleanup ran but the native modal could remain on
  screen after its host was disposed. Ordinary tree-disposal cleanup semantics
  are unchanged (PR [#1444](https://github.com/microsoft/microsoft-ui-reactor/issues/1444)). Reentrant disposal during dialog-content mount
  or update also leaves the dialog detached, and closed dialogs release their
  `Opened` handler after `ShowAsync` completes.

- Async resource hooks detect dependency changes even when hashes collide and
  treat mismatched cached dependencies as a miss, preventing wrong-query data
  in `UseResource` and `UseInfiniteResource`. Cursor-paged resources also use
  the requested predecessor's cursor after page eviction and cache invalidation
  ([issue #1430](https://github.com/microsoft/microsoft-ui-reactor/issues/1430),
  spec 020 §6.2, §7, §9.2).

- `UsePersisted` restores saved null values for reference and nullable value
  types instead of falling back to `initialValue` after remount
  ([issue #1442](https://github.com/microsoft/microsoft-ui-reactor/issues/1442),
  spec 033 §2 in `033-winui-xaml-reviewer-feedback.md`).

- **Unmount callbacks survive managed-wrapper collection** (spec 047 §3).
  `.OnUnmount(...)` now lives in native attached state rather than a weak table
  keyed by a managed wrapper, so a still-mounted control keeps its teardown callback
  when WinRT projects a new wrapper before removal. Callbacks stay scoped to the host
  that registered them. A wrapper that shares its control with an inner target, as
  `Flyout` does, no longer erases the target's callback when the wrapper has no callback
  of its own, or drops its own while the target re-registers.

- **Keyed-list diagnostics retain their native control identity** (spec 047 §3).
  Duplicate/null-key warning counters now share one ledger across managed wrappers
  for the same control instead of emitting a second entry after wrapper collection.

- **`AutoSuggestBox` clears stale choices when `Suggestions` becomes empty**
  (spec 047 §6.1). The suggestions binding now clears the native `ItemsSource`
  instead of skipping empty updates, preserving the existing control and allowing
  later suggestions to repopulate it. Empty suggestions leave `ItemsSource` null,
  so direct `Items.Add(...)` setters continue to work on mount.

- **Keyed children keep their own controls while siblings exit** (spec 014, feature 2).
  Fading controls retained from an earlier render no longer shift keyed prefix, suffix,
  removal or reorder updates onto another child's control. Live controls keep their
  identity and the final child order remains correct when the exits complete.

- **Wrapped third-party controls now get source-map call sites** (spec 010). A factory
  emitted by `[GenerateReactorWrapper]` (`SettingsCard(...)`, `RadialGauge(...)`) is
  invisible to the source-map generator, because no source generator sees another's
  output. Its calls were never stamped, and every DSL call taking one as an argument —
  `VStack(8, SettingsCard(...))`, `HStack(8, gauge.Width(140))` — failed to bind there
  and was dropped too, so devtools saw a bare `StackPanel` with no Reactor element.
  `Reactor.Wrappers.Generator` now intercepts calls to the factories it emits (same line,
  column and `ReactorSourceMap.Enabled` rules; only when `ReactorSourceMap` is true and
  the build lists `Microsoft.UI.Reactor.Generated` in `InterceptorsNamespaces`), and the
  source-map generator re-binds such containing calls as if each wrapper value were
  already an `Element`, so they keep their location and declared name — or leaves a call
  unstamped where that could change which overload the real compilation picks.

- **ThemeRef resource overrides follow a `RequestedTheme` set outside the Reactor tree**
  (follow-up to [#771](https://github.com/microsoft/microsoft-ui-reactor/issues/771)). When an app themes a native ancestor of a `ReactorHostControl`
  or a `ReactorHost.ContentTarget`
  (for example `RequestedTheme = Dark` on the window root under a Light app),
  `.Resources(r => r.Set(key, Theme.Ref(...)))` overrides resolved against the app
  theme: at mount the control is not yet parented, and no theme change follows to
  re-resolve it. ListView, GridView, TreeView and ItemsView/ItemsRepeater rows realized
  during layout had the same problem, because they mount outside any root pass.
  Both hosts now supply a native theme anchor to the reconciler, including for custom
  error fallbacks. Layout-time realization inherits its container's theme, pooled rows
  refresh unchanged descendants when reused under a different theme, and a change to the host's inherited theme
  forces a resource refresh even through unchanged subtrees (including when a
  theme-event render runs before the host's Loaded event).

- **A declarative `ContentDialog` follows the theme where it is declared** (issue [#1388](https://github.com/microsoft/microsoft-ui-reactor/issues/1388),
  PR [#1389](https://github.com/microsoft/microsoft-ui-reactor/issues/1389)). The dialog is side-mounted on the window's `XamlRoot`, so it rendered in the
  window/app theme. It now follows the effective theme (`ActualTheme`) at its declaration
  site, including local ancestor overrides and secondary windows, and updates while open.
  `.RequestedTheme(...)` on the dialog is now forwarded to the visible dialog (it only
  reached the hidden placeholder); an explicit `ElementTheme.Default` follows the owner, as
  elsewhere in Reactor. `.Set(dialog => ...)` still runs last and wins, even when it writes
  the theme the dialog would follow. Dialog content `ThemeRef` resources resolve against the
  dialog's theme on first open. Apps relying on the old look can pin it with
  `.RequestedTheme(...)` or `.Set(d => d.RequestedTheme = ...)`.

- **Diagnostics an inspector relies on (winapp DevTools) are no longer missing or anonymous**
  (issue [#1421](https://github.com/microsoft/microsoft-ui-reactor/issues/1421), spec 044 / spec 010 / spec 042).
  - A commit-phase failure that replaces a window's content with the error screen now raises a
    new `ReactorEventSource.CommitError` event (EventId 41: phase, host root component,
    exception type). Before, nothing was raised while the window showed the error.
  - `ReactorEventSource.WindowOpened` / `WindowClosed` were declared but never raised; windows
    opened through `ReactorApp.OpenWindow` / `Run` now report both (root component type, HWND).
  - Templated `ListView<T>` / `GridView<T>` / `FlipView<T>` rows carry their `KeySelector` key
    on the realized element (as its element tag), as `LazyVStack` /
    `VirtualList` rows already did. FlipView pages are still matched by position.
  - A list born with a null or duplicated key (including `LazyVStack`, which used to swap a
    null key for `__null_N` silently) is reported at mount, and a null key met during an update
    is reported on every keyed list (the item is still matched by position, and the rest of the
    list now always diffs incrementally instead of resetting on some paths). Duplicate
    `.WithKey` siblings in a panel (`VStack(… .WithKey("same"), … .WithKey("same"))`) are
    reported too. Every `KeyedListDiagnostic` now carries `ComponentName` (while an inspector is
    attached) and `CallSite`, and the logged warning names both.
  - `MenuItem(…)` / `AppBarButton(…)` and the other menu and command-bar item factories are
    source-mapped (new `CallSite` on `MenuFlyoutItemBase` / `AppBarItemBase`, excluded from
    equality), so inspectors map the items a `MenuFlyout`, `CommandBarFlyout`, `MenuBar` or
    `CommandBar` builds back to C# and their owning component.
  - `TabView`, `Pivot`, `ListView`, `GridView` and `FlipView` accept a plain `int` selected
    index (it was ambiguous between `int?` and `Optional<int>`, CS0121), and new `XamlHost(…)` /
    `XamlPage(…)` factories record a call site for `XamlHostElement` / `XamlPageElement`.

- **Markdown `LinkBuilder` is now invoked** ([#1401](https://github.com/microsoft/microsoft-ui-reactor/issues/1401), [#1436](https://github.com/microsoft/microsoft-ui-reactor/issues/1436)). Previously the callback was
  ignored and every link rendered as a default hyperlink. Each link whose target
  passes the URI policy now calls it with a `MarkdownLink` (see Changed), and
  the returned element renders inline; returning `null` throws. Links nested in a label
  stay part of the outer link. If the outer target is rejected, the whole label renders
  as plain text, and a URL inside it does not become a separate link. Links render as
  before when no callback is set.
- **Markdown numeric character references are decoded** ([#1436](https://github.com/microsoft/microsoft-ui-reactor/issues/1436)). `&#65;` and
  `&#x42;` used to render as literal text; they now render as `A` and `B`, like
  named entities. Following CommonMark, `&#0;`, surrogate code points and values
  above U+10FFFF render as U+FFFD.
- **Opening a `Flyout` from state no longer replaces the window with a render error**
  (issue [#1419](https://github.com/microsoft/microsoft-ui-reactor/issues/1419)). `Flyout(Button(...), ...) with { IsOpen = open }` opened through
  `FlyoutBase.ShowAttachedFlyout`, but a Button/SplitButton target holds its flyout in the
  control's own `Flyout` property, so WinUI threw `ArgumentException` ("ShowAttached was
  called on an element without an attached FlyoutBase") on mount with `IsOpen = true` and on
  every `false` → `true` edge. It now opens through the installed flyout's `ShowAt` (deferred
  to the target's `Loaded` at mount), the path `CommandBarFlyout` already used, so both slots
  work. `IsOpen` stays a one-shot open trigger; the user dismisses the flyout.

- **Unpackaged Jump Lists now use source-generated COM interop under Native AOT.**
  Shell link activation, property stores and removal queries no longer depend on
  built-in COM marshalling. Worker calls use a balanced explicit COM apartment scope and
  release transaction-owned interfaces deterministically. See the
  [Jump list guide](/microsoft-ui-reactor/main/guides/desktop-integration#jump-list) (spec 036 §11.3).

- **`.InteractionStates()` and `.Keyframes()` translations no longer move an element to its
  parent's origin** (issue [#1432](https://github.com/microsoft/microsoft-ui-reactor/issues/1432)). Both animated the handoff visual's `Offset`, and on a
  XAML element that `Offset` is its layout position, so a `translation:` replaced the
  element's position instead of nudging it, and hover-out sent it to the top-left of its
  parent. Both now animate `Translation`.

- **A conditional child no longer shifts its unkeyed siblings** (issue [#1423](https://github.com/microsoft/microsoft-ui-reactor/issues/1423)). The
  container factories drop `null` / `Empty()` arguments and flatten `Group(...)`, so
  toggling `cond ? X : Empty()` moved every later sibling down one index and the
  positional reconcile compared it with the child that used to sit there: a sibling of
  another type remounted (its state reset) and one of the same type took over its
  neighbour's state. The factories now record each child's argument slot and unkeyed
  children are matched by slot, as React treats `null`. A child whose argument slot
  changes is now a different child (`a ? X() : Empty(), a ? Empty() : X()` remounts
  `X`). Containers with a keyed child keep keyed matching. Re-rendering while a child's
  exit transition is still playing also no longer pairs its siblings with the wrong control.

- **`ListView<T>` / `GridView<T>` rows are unmounted when WinUI drops their container
  without recycling it** (issues [#1423](https://github.com/microsoft/microsoft-ui-reactor/issues/1423), [#1440](https://github.com/microsoft/microsoft-ui-reactor/issues/1440)). Under fast insert / remove / move churn WinUI
  could park a container offscreen, unlinked from any item, without raising the recycle
  `ContainerContentChanging`, so the row's components stayed mounted and their effects never
  cleaned up.
  The update-time sweep also handles an unlinked container whose item was already cleared while
  its template still holds a live view; its visuals remain available for the native delete
  transition. WinUI parks it in the layout pass after the update (a row moved twice, or moved
  out of the realized range, is enough), so after a structural change the reconciler now checks
  the containers once that layout pass completes. Unmounting also no longer stops at a
  throwing cleanup (with issue [#1291](https://github.com/microsoft/microsoft-ui-reactor/issues/1291)): every parked row is torn down, and a throwing control
  handler, descriptor `OnUnmount` hook, property unmount callback, or decorator or
  `RegisterType` unmount callback is collected rather than ending the teardown. Each failure
  is reported once, in order (to the `RenderErrorHandler` as `Cleanup`), the children it did
  not reach are torn down (once each), and the half-torn-down control is never pooled.

- **`REACTOR_A11Y_001` no longer flags text buttons with a non-literal string label**
  (spec 006 §6.3). `Button(S("Usage_Refresh"), onClick)` — a localized resource lookup,
  a `string` variable, or an interpolated string — was reported as an icon-only button
  needing `.AutomationName(...)` because only string *literals* were recognised as text
  content. The analyzer now also skips any first argument whose semantic type is (or
  implicitly converts to) `string`; element/icon content still reports. Icon-glyph or
  emoji *strings* remain the runtime `AccessibilityScanner` A11Y_001 check's job, as
  string literals already were.

- **`REACTOR_DSL_001` no longer asks for a key on a `Select` that projects values, not
  elements** (spec 042). A `Select` passed into a layout factory was flagged whatever it
  produced, so `Grid(days.Select(_ => GridSize.Star()).ToArray(), rows, …)`, which builds
  column definitions, raised a missing-`.WithKey()` warning with nothing to key. After the
  cheap syntactic gates the rule now checks the projection's type and skips anything that
  is not a `Microsoft.UI.Reactor.Core.Element`. It counts the type a body converts to
  (`ForEach`'s `Func<T, Element>`) and `where T : Element` constraints, so those
  projections still report. An unresolved type keeps the old verdict, so broken code
  still reports.

- Flat `ItemsHost` children reconcile incrementally, same-instance Element item sources honor dirty-state/context/theme/forced-render invalidation instead of skipping unconditionally, and generated Element slots receive terminal teardown while still parented ([control reconciler guide](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)).

- **Window hooks resolve the window that renders the component, not the most recently
  active host** (issue [#1396](https://github.com/microsoft/microsoft-ui-reactor/issues/1396), spec 036 §7.1). `UseWindow`, the parameterless
  `UseWindowSize`, `UseWindowPosition`, `UseIsCovered`, `UseWindowAspectRatio`,
  `UseWindowDragMove`, `UseWindowState`, `UseIsActive`, `UseClosingGuard`, `UseDpi`, the
  window-inferring `UseBreakpoint(minWidth)`,
  `UsePersisted(PersistedScope.Window)` and the `TitleBar` element now read the owner the
  host's reconciler records on each component, the same owner the picker methods use. Before,
  they read the process-wide active host, so a sibling rendered after a window opened
  mid-render (`UseOpenWindow`) and a virtualized row realized by `LazyVStack` /
  `ElementFactory` got another window: its size, DPI and persisted scope, and a closing guard,
  aspect lock or `TitleBar` registered on it. Content with no owning `ReactorWindow` (tray
  flyouts, `ReactorHostControl`, a `ReactorHost` over a raw `Window`) gets the documented
  "outside a window" values. Behaviour change: `ReactorHostControl` content previously saw an
  unrelated host's window and now sees none. A `TitleBar` inside it registers, once loaded,
  with the `ReactorWindow` that actually contains the control (found from its `XamlRoot`)
  instead of with whichever host was active, so that window is content-extended and prepared
  for close as issue [#537](https://github.com/microsoft/microsoft-ui-reactor/issues/537) requires. Embedded content that needs its window can pass it explicitly
  (`UseWindowSize(window)`, `UseBreakpoint(window, minWidth)`) or read
  `XamlRoot.RasterizationScale` for DPI.
- **Replacing a host's root no longer leaves the old root running** (issue [#1326](https://github.com/microsoft/microsoft-ui-reactor/issues/1326)).
  `ReactorHost.Mount(...)` and `ReactorHostControl.Mount(...)` now retire the
  outgoing root before installing the new one: its effect cleanups run, and a
  component root's hook state is reset, so mounting that instance again later starts
  fresh. Before, the outgoing root's `UseEffect` subscriptions and timers stayed live
  and could no longer be reached by `Dispose()`. `ReactorHost.Mount(Func<...>)` also
  clears a previous component root, which the render loop checks first and so kept
  rendering instead of the new function root. Mounting the instance that is already
  mounted just re-renders it. A replacement whose first render produces nothing now
  releases the previous root's content (and its components' effects) instead of leaving
  it on screen, and a cleanup that throws during the swap is routed like a disposal-time
  cleanup failure without blocking the new root.
- **`ReactorWindow`, the tray host and Devtools embedding no longer throw
  `EntryPointNotFoundException` in x86 processes** ([#1365](https://github.com/microsoft/microsoft-ui-reactor/issues/1365)). They imported
  `GetWindowLongPtrW` / `SetWindowLongPtrW` directly, which 32-bit `user32.dll`
  does not export. A shared `NativeWindowLong` helper now dispatches to
  `GetWindowLongW` / `SetWindowLongW` when `IntPtr.Size == 4`; x64 and ARM64
  behaviour is unchanged.
- **`RenderError` names the component that threw, and now reports every render
  error** (issue [#1321](https://github.com/microsoft/microsoft-ui-reactor/issues/1321), spec 044 §6.2.1). For class components the event reported
  the element record (``ComponentElement`1`` / `ComponentElement`) instead of the
  component unless the `Render` keyword was also on, so an Errors-only listener
  such as `winapp devtools logs` could not tell which component failed. It now
  reports the component's type name (`Counter`; a generic one as `Foo<Int32>`),
  and the other per-component events agree on it. **Listeners now also receive
  errors that were previously silent:** a component (class, function or memo)
  throwing on its first render, a `ReactorHost` / `ReactorHostControl` root
  component or render function throwing (in `Render()` or in its effects), and any
  render error an `ErrorBoundary`
  catches — reported once, at the throw site, even though the app shows the
  fallback. Same event, same payload; the message stays redacted. Generic
  component names change from ``Foo`1`` to `Foo<Int32>` in every component event.
  For these component failures the event is emitted before any `RenderErrorHandler`
  runs, so it fires whether the handler replaces the fallback or calls `Propagate()` —
  once per throw either way. `Reconcile` and `Cleanup` failures are not render errors
  and are not reported on it.

- **DataGrid inline editors no longer shift the cell's text when editing starts**
  (issue [#1340](https://github.com/microsoft/microsoft-ui-reactor/issues/1340)). The built-in TextBox and NumberBox editors used `.Padding(2)` inside
  WinUI's 32px minimum height, which draws text at the top, so the text moved about 4px
  up and 5px left as soon as a cell entered edit mode. Text editors now use the cell's
  horizontal padding and symmetric vertical padding, with `TextControlThemeMinHeight`
  overridden to 0, so their text lands exactly where the display cell drew it. A custom
  `col.Editor` keeps any padding, `MinHeight` or `TextControlThemeMinHeight` it sets.

- **A `SwipeControl` side in `SwipeMode.Execute` with more than one item no longer
  replaces the whole UI with the render-error panel** (issue [#1344](https://github.com/microsoft/microsoft-ui-reactor/issues/1344)). WinUI accepts
  only one item in an Execute-mode `SwipeItems` collection and rejects a second with
  `E_INVALIDARG`, which escaped the reconciler. Reactor now keeps the first item,
  ignores the rest, and emits a `SwipeControl.ExecuteItems` diagnostic warning naming
  the side and how many items were dropped, once each time a side becomes truncated.

- **An `ErrorBoundary` whose child fails part-way through mounting no longer leaves
  that child's effects running** (issue [#1291](https://github.com/microsoft/microsoft-ui-reactor/issues/1291)). A component registers before its
  effects run, so a child whose second effect threw had already opened whatever its
  first effect opened (a subscription, a timer), and a control's `.OnMount(...)` had
  already run. The boundary discarded the half-built subtree without it ever being
  attached, so nothing could unmount it, and every re-render retried the child and
  leaked another set. The boundary now rolls the failed child back — the controls
  that did finish are unmounted (so their interaction states, animations and
  `.OnUnmount(...)` actions go too), and the components that did not still have their
  effect cleanups run — both when it first mounts and on each retry.

- **Dropping an `AutoSuggestBox` right after its text changed can no longer crash
  the app** (PR [#1302](https://github.com/microsoft/microsoft-ui-reactor/issues/1302), supersedes [#559](https://github.com/microsoft/microsoft-ui-reactor/issues/559)). WinUI raises the box's `TextChanged` from
  an internal timer 150 ms after its text last changed, keeps that timer running
  after the box leaves the tree, and does not keep the box alive for it. When the
  box was unmounted (or its host disposed) inside that window and full GCs ran,
  the tick called into managed state the GC had already collected, and WinUI
  fail-fasted the process with `STATUS_STOWED_EXCEPTION` (`0xC000027B`). Reactor
  now holds the box from the moment it leaves the tree until well after any such
  tick. This was the intermittent selftest-host crash in the Optional fixtures
  (`ControlledOptionalTextInputFamily`, `OptionalEchoStrandRegression`, and
  whichever fixture ran next).

- **ReactorGallery category and search-result pages scroll vertically**
  (issue [#1298](https://github.com/microsoft/microsoft-ui-reactor/issues/1298)). Both views put a bare header and card grid into the
  `NavigationView` content. The card grid turns off its own scrolling so it can
  size to its content, and nothing else supplied a scroller, so in a short
  window the cards below the fold couldn't be reached. Both views now share a
  page helper that wraps the content in `ScrollView`, matching the control
  pages and Home.

- **Controls registered with `Reconciler.RegisterType` now carry their element tag, so
  keyed lists of them keep their place and their `unmount` callback runs** (spec 047
  §14, PR [#1301](https://github.com/microsoft/microsoft-ui-reactor/issues/1301)). The reconciler records each control's element on the control and reads
  it back in several places, but it left a registered type's control to its own
  callbacks, which only `XamlInterop` tagged. A keyed registered-type child that survived
  a re-render was therefore neither moved nor patched: a `Grid` of them that grew or
  reordered showed stale content at another child's row and column. The `unmount`
  callback never ran, keyed or not, so the docking host's cleanup (closing its floating
  windows, unregistering from `DockHostRegistry`) never ran either, and the guide's
  `editor.Dispose()` example never disposed anything. The docking cleanup now runs, but
  it covers only the host's latest `DockManager` element, so a window floated under an
  earlier element still outlives the host; that is a separate docking bug. A
  `.Ref(...)` on a registered type kept pointing at the removed control, which the pool
  could already have handed to another element. The reconciler now tags these controls
  wherever it reads the tag itself: when the element is keyed or carries callbacks,
  extras or a reference modifier, and always when the registration has an `unmount`
  callback. A control already tagged for an element of another type, such as one the
  callback got from `Reconciler.Mount`, keeps that tag. An `update` callback that
  returns the control it was handed now counts as patching it, where the child
  reconcilers used to unmount it. Unmounting a replaced control also no longer clears a
  `.Ref(...)` that has already moved to its replacement. Because `unmount` now runs, it
  replaces the reconciler's walk over the control's children, as it always has for
  `XamlInterop`: a registration that mounts children through the reconciler and
  supplies `unmount` has to unmount them there. Calling `UnmountChild` on the control
  itself from `unmount` walks its children once rather than calling `unmount` again.

- **A docking host's floating windows close when the host unmounts, whichever
  `DockManager` they were opened under** (spec 045 §8.10, PR [#1305](https://github.com/microsoft/microsoft-ui-reactor/issues/1305)). Apps build a new
  `DockManager` in their render, and every state change renders again, so a host
  sees a new element instance on almost every render. The per-host tables
  (floating windows, `DockHostRegistry`, and the chord, live-announcer, model and
  drag-gate bridges) were keyed by that instance, so a pane floated under an earlier
  instance kept its window open after the host was gone: in the Reactor IDE sample,
  View ▸ Reset Layout left the floated pane open next to its re-docked copy, and
  devtools still listed the old host. The tables are now keyed by the host, so unmount
  closes every window the host opened and clears every table. While the host is
  mounted, `DockHostModel.Floating`
  no longer drops a floating pane on the next render, and `docking.list` shows the
  host once, under an id that stays stable, instead of once per render.

- **Apps that reference only `Microsoft.UI.Reactor` are framework-dependent
  again** (regression from [#822](https://github.com/microsoft/microsoft-ui-reactor/issues/822)). The package now depends on
  `Microsoft.WindowsAppSDK.Runtime`; without it the Windows App SDK silently
  built such apps self-contained, so a plain `dotnet run` failed with
  *"WindowsAppSDKSelfContained requires a supported Windows architecture"*. To
  keep bundling the Windows App Runtime, set `WindowsAppSDKSelfContained=true`.
  To move to a newer WinUI, bump the full `Microsoft.WindowsAppSDK` package;
  bumping `Microsoft.WindowsAppSDK.WinUI` on its own now fails the Windows App
  SDK's version check.

- **Devtools servers no longer lose their port to another process between
  choosing and binding it** (spec 024 §7). `DevtoolsMcpServer` and
  `PreviewCaptureServer` probed a free loopback port and bound `HttpListener` to
  it later, so another process could take it in between and `Start` failed with
  `HttpListenerException` (32). With several selftest hosts on one machine, the
  `Devtools_*` fixtures failed intermittently. Both servers now bind and, if
  another process holds the probed port, probe and bind a new one. A port pinned
  with `--mcp-port` is never moved: the app exits with code 43, and
  `mur devtools` picks a new port if it chose the port itself, or reports the
  conflict if the user pinned it.

- **A component's own state change is no longer lost under a reused or memoized
  element** (extends the dirty-ancestor path from [#377](https://github.com/microsoft/microsoft-ui-reactor/issues/377)). When a panel child
  was the same element instance on every render — one element reused across
  renders, such as a single `DockManager`, an explicitly memoized wrapper such as
  `UseMemo(() => Border(Counter()), [])`, or a reused children array — or was a
  `Memo(key, …)` used as a plain child, a component inside it that set its own
  state was marked for re-render but never re-rendered: the reconciler skipped
  the unchanged child without descending to it. A reused `DockManager`, for
  example, never applied a queued `model.PinToSide` or `Float`. The child skips
  now descend into exactly the children that lead to such a component; their
  siblings still skip without reading a control. A same-key `Memo(key, …)` is
  still a no-op, except that its factory runs again when a component inside it
  updates itself.

- **A child that its update replaces is unmounted in a single-child slot too**
  (spec 047 §14, [#1307](https://github.com/microsoft/microsoft-ui-reactor/issues/1307)). When an update handed back a new control for the one
  child of a `Border`, a named slot such as `SplitView.Pane`, a tab's content, a
  generated element slot such as `TabView.TabStripHeader`, or a `RichTextBlock`
  `InlineUI(...)` child, Reactor swapped the new control in but, unlike a panel,
  never unmounted the old one. The old subtree's effect cleanups and `.OnUnmount`
  actions never ran, and a ref to a control in it kept pointing at the
  detached control. A `ValidationVisualizer` in such a slot did this on every
  re-render, because its update remounts it, and so did a `RegisterType`
  `update` that returns a new control. Slots that a control fills by hand still
  swap a replaced control out without unmounting it: `CommandBar` content,
  `Expander` content and header template, `ContentDialog`, `Flyout` and `Popup`
  content, `.WithFlyout`, `.WithContextFlyout` and `.WithToolTip(element)`
  content, and realized item content in `ListView`, `GridView`, `TreeView<T>`
  and templated `FlipView`.

- **A flyout's old Target is unmounted once, not twice, when its Target changes
  element type at a component's root or through `Reconciler.Reconcile`** ([#1307](https://github.com/microsoft/microsoft-ui-reactor/issues/1307)).
  A `Flyout`, `MenuFlyout` or `CommandBarFlyout` that wraps a Target unmounts
  the old Target itself when the Target's element type changes. The reconcile
  path behind component roots, `ErrorBoundary`, the app root and the public
  `Reconciler.Reconcile` then unmounted it again, so the old subtree's handler
  unmounts ran twice. That path
  now notices the update already unmounted it, as single-child slots do. A
  panel still unmounts such a Target twice.

- **The Forms guide's "Validation Context" example now works as written**
  (issue [#1262](https://github.com/microsoft/microsoft-ui-reactor/issues/1262)). Clicking **Register** on an empty form submitted successfully
  with no errors, because five defects compounded: `.Validate()` was inert
  outside `FormField`, the context was never provided to the subtree, reading it
  in `Render()` was always a pass stale, mutating it scheduled no re-render, and
  `ShowWhen.WhenTouched` was unreachable because nothing ever called
  `MarkTouched`. Making that path work surfaced further validation bugs, all fixed
  here:
  - **Verdicts outliving their control.** A validated control behind a condition,
    a whole `FormField`, or a child removed from a parent left its message in the
    context, so a form stayed invalid over a field with no control — permanently,
    with no way to clear it short of `ClearAll()`. The same happened when a field
    name changed and when a rule moved to another context.
  - **Async rules that silently passed.** A rule built with `ValidationRuleAsync`
    never ran its predicate when placed in the element tree, and recorded a
    passing verdict when evaluated synchronously — reporting an invalid field as
    valid. Mounted rules now dispatch asynchronously; the synchronous paths throw
    instead.
  - **Async ordering.** Overlapping checks applied in completion order, so a slow
    failing check could reinstate an error a newer run had cleared, and a verdict
    about a replaced value survived the change. Passes are now ordered per producer
    and cancellable.
  - **Fields that were never registered.** A field validated only through
    `ValidateFieldAsync`, the rule batches, or `.ValidateAsync(…)` on an element
    built outside a render pass was skipped by `MarkAllTouched()` and the
    validity summary.
  - **Wrong verdicts.** A validator-only attachment inside a `FormField` was
    validated against `null`, reporting a required-field error for a control that
    plainly had text, and `NotifyValueChanged` discarded `AddExternal` messages on
    every call rather than only when the value actually moved.
  - **Display policies that could never fire.** `FormField`'s default
    `ShowWhen.WhenTouched` showed nothing because nothing called `MarkTouched`,
    and `ShowWhen.AfterFirstSubmit` showed nothing because no caller supplied the
    submit flag — both behaved exactly like `ShowWhen.Never` while silently
    accepting the setting. Blur now marks touched, and `MarkAllTouched()` records
    the submit attempt that `AfterFirstSubmit` waits for.
  - **Documentation.** The guide documented `ValidationContext.IsValidating`,
    which does not exist, and said `Validate.MustAsync` runs automatically. The
    async section now states the real contract — attach-only, driven from an
    effect through `ValidationReconciler.ValidateFieldAsync`. The `ShowWhen`
    reference now also names the signal each policy waits for, since two of them
    need one the app has to send: `WhenDirty` measures against the baseline only
    `SetInitialValue` records, and `AfterFirstSubmit` waits for
    `MarkAllTouched()`.

- **Opening a DataGrid editor from inside the grid could commit it the moment it opened
  (issue [#1288](https://github.com/microsoft/microsoft-ui-reactor/issues/1288)).** Starting a row edit from the row's own "Edit" button, or tapping a cell
  while another cell was being edited, removed the element holding keyboard focus in the
  re-render that opened the new editor. XAML then moved focus to the next element in tab
  order, which is outside the grid, and when the grid's blur-commit safety net saw that
  before the new editor had taken focus, it committed the edit the user had just opened. The
  editor closed as it appeared, and `onRowChanged` fired with unchanged values. It showed on
  CI runners, not on fast machines. The grid now moves focus onto its own root before any
  such re-render, so a removed element never holds focus. Focus is moved only when it is
  already inside the grid. The root also keeps that focus through the mouse or touch release
  that follows: WinUI's root scroller used to take focus on the release, which reopened the
  same race when the button came up after the move. For the same reason, clicking a cell
  while the grid itself holds keyboard focus, after tabbing onto it for example, no longer
  moves that focus out of the grid.

- **Compact docking tool tabs are no longer blank, and every docking tab has a UI
  Automation name (spec 045 §5.1, §5.3.1, PR [#1295](https://github.com/microsoft/microsoft-ui-reactor/issues/1295)).** A `DockTabGroup` with
  `CompactTabs: true`, or one made up only of `ToolWindow`s (compact by default), used
  WinUI's `TabViewWidthMode.Compact`, which shows only the icon of an unselected tab.
  Docking panes have no icon, so every unselected tab rendered as an empty stub with no
  title. Compact groups now size each tab to its title, and use `Compact` only when every
  tab has an icon. Tool-window tabs with a pin button also had no UI Automation name,
  because WinUI names a tab only from a plain-text header; they are now named after their
  title.

- **The Visual Studio preview failed to start every session.** The extension was built
  against a newer `System.Text.Json` than Visual Studio binds extensions to, so it failed
  to load at runtime. It now tracks the `Microsoft.VisualStudio.SDK` baseline, with a test
  to keep it there. The VSIX's advertised minimum host moves to Visual Studio 17.14 to
  match that baseline. (PR [#1282](https://github.com/microsoft/microsoft-ui-reactor/issues/1282))

- **The Visual Studio preview now supports packaged (MSIX) apps.** Previewing a packaged
  app failed, because the preview never launched it through its execution alias. It now
  works with no project changes, which matters more now that `dotnet new reactor` scaffolds a
  packaged app (see Removed). (PR [#1282](https://github.com/microsoft/microsoft-ui-reactor/issues/1282))

- **Wrong code and guidance in the agent skills (spec 064 §4, issue [#1275](https://github.com/microsoft/microsoft-ui-reactor/issues/1275)).** Three
  gesture snippets — in the skill kit's `skills/input.md` and the plugin's `reactor-input`
  skill — used WinUI's nested `ManipulationDelta` shape rather than Reactor's flat gesture
  structs, and the 60Hz pan pattern bound a `UseRef` box into `.Ref(...)`, which takes an
  `ElementRef` — all four shipped as copy-ready code that would not compile. The plugin's
  `reactor-getting-started` hook-dependency gotcha also misattributed the tuple and array
  rules to the runtime rather than to `REACTOR_HOOKS_004`, which is what actually rejects
  them. Found by requiring a compiled gallery card for every API the skills demonstrate.
- **The framework package attached to GitHub Releases bundled an ARM64-only
  `Reactor.Wrappers.Abstractions.dll`** (spec 022 §5, spec 058 §15.7). The Package workflow packed
  `Microsoft.UI.Reactor` after a whole-solution build, and the nupkg it attaches to each GitHub
  Release carried an ARM64 copy of that `lib/` DLL, which an x64 process refuses to load. The
  `0.1.0-preview.15` and `0.1.0-preview.16` attachments are affected. The packages on NuGet feeds
  come from a separate build and are not. The workflow now packs from per-project builds, which
  produce a platform-neutral copy, and fails if any packed assembly is not AnyCPU (PR [#1293](https://github.com/microsoft/microsoft-ui-reactor/issues/1293)).
- **Keyed `Memo(key, …)` children keep their place when the hand-built list around them grows
  or reorders** (issue [#327](https://github.com/microsoft/microsoft-ui-reactor/issues/327)). Outside a virtualized list, `Memo(key, factory)` mounts no control
  of its own; the factory output's control stands in for it. The keyed reconcile could therefore
  not recognize a `Memo(key, …).WithKey(…)` child that survived a re-render. The child stayed
  unpatched at its old index while its siblings were placed around it, so a `Grid` gave it
  another child's row and column. The factory output now carries the wrapper's key.
- **A resource override dropped from an element could stay applied.** Reactor records which
  `Resources` keys an element's `.Resources(...)` overrides wrote, so it can remove them once
  they are dropped, and kept that record in a table keyed by the control's managed wrapper. When
  the wrapper was garbage-collected, WinRT projected a new one over the same native control and
  the record came back empty: dropping the override, or changing its keys, after a GC left the
  old value in the control's `Resources`. The record now lives in the control's `ReactorState`,
  which is stored on the native object and so survives a wrapper collection (spec 047 §3).
  Pooled controls leaked the keys too: `ElementPool` recycled a control without them, so the
  next element to rent it inherited the previous renter's overrides. That happened after a GC
  for a renter with overrides of its own, and always for one without, because mount only
  applies overrides an element declares. Returning a control to the pool now strips the keys
  Reactor wrote. Found through an intermittent `Issue675_TransitionAway_StaleOverrideRemoved`
  selftest failure; the new `Issue675_TransitionAwayAfterWrapperCollected`,
  `Issue675_PooledOverrideRenterDoesNotInheritKeys` and
  `Issue675_PooledPlainRenterDoesNotInheritKeys` fixtures force the GC and fail without the fix
  (PR [#1294](https://github.com/microsoft/microsoft-ui-reactor/issues/1294)).
- **Two transitions with the same `.ConnectedAnimation(key)` within one frame no longer crash
  the app** (issue [#1152](https://github.com/microsoft/microsoft-ui-reactor/issues/1152)). WinUI takes a connected animation's snapshot in its next frame. A
  source that has already left the tree stays in its parent's unloading storage until that
  frame's commit, and only the preparation gives it a composition node. Preparing the same key
  again before that frame cancels the first animation, so the commit dereferences the node the
  cancel took away: `0xC0000005` at `CConnectedAnimationService::PreCommit` in
  `Microsoft.UI.Xaml.dll`. Reactor prepares every keyed element it unmounts, so any two passes
  that both unmounted the key before WinUI rendered a frame could crash. In CI the selftest host
  died this way in `ConnectedAnimation_OrphanOnlyPassDoesNotCrash`, in about 5% of runs and in
  every selftest lane. Reactor now does not prepare a key again until WinUI has rendered a frame
  since its last preparation, so the second element does not animate. The new
  `ConnectedAnimation_RepreparedBeforeFrameDoesNotCrash` fixture runs both passes with no frame
  in between and crashed on every run before the fix. The earlier explanation for the withdrawn
  `Cancel()` cleanup (PR [#1124](https://github.com/microsoft/microsoft-ui-reactor/issues/1124)), that the source had been pooled and reset, was wrong: that crash
  was this same fault.

- **Apps that reference Reactor no longer fail to build in deeply nested folders, and their
  publish output now includes the app's `.pri` (issue [#1271](https://github.com/microsoft/microsoft-ui-reactor/issues/1271),
  [WindowsAppSDK#6394](https://github.com/microsoft/WindowsAppSDK/issues/6394)).**
  `Microsoft.UI.Reactor` now depends on `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.260930100,
  which fixes both. If you copied the `_CopyWinUIResourcesForAot` target from
  `docs/aot-support.md`, you can delete it.

- **The `Microsoft.UI.Reactor` package has a single `lib/` folder and always contains
  `ReactorApplication.xbf`.** A second `lib/` folder used to hold only that file, and packing
  from a clean tree left it out.

- **`mur check` prints error messages in full** (issue [#1273](https://github.com/microsoft/microsoft-ui-reactor/issues/1273), spec 038 §9). Every
  diagnostic's message was cut to 97 characters plus `...`, which on long-path build errors
  hid the very file names needed to fix them. Errors now keep their complete message in all
  four modes; warnings and info diagnostics keep the compact 100-character form, and every
  diagnostic is still a single line.
