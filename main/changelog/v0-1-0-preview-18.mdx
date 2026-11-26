---
title: "0.1.0-preview.18"
type: changelog
date: 2026-11-12
changelog:
  version: "0.1.0-preview.18"
description: "7 added, 9 changed and 15 fixed."
---

## Added

- Element-valued content overloads for `HyperlinkButton`, `RepeatButton`,
  `ToggleButton` / `ThreeStateToggleButton`, `DropDownButton`, `SplitButton`,
  `ToggleSplitButton`, `CheckBox` / `ThreeStateCheckBox`, and `RadioButton`
  (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).
- `Imperative<TElement, TControl>.Unmount`, an optional teardown callback the engine
  runs when a tagged control using that children strategy unmounts, so a handler can
  unmount the children its `Reconcile` mounted (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).
- Reactor's render timing is recordable by a non-elevated **private** ETW session (the kind
  `winapp perf` attaches with), which never enables the `Microsoft-UI-Reactor` EventSource.
  A new raw TraceLogging provider, `Microsoft-UI-Reactor-Perf`
  (`0902a96c-4e92-5b91-4d2b-5a8058055c78`), writes a `RenderPass` span per host render pass,
  an inclusive `ComponentRender` span per component render (mount, update, the host root, and
  rows a virtualized list realizes during layout), `Reconcile`, `Effects` and `ChildReconcile`
  spans (with inserted/removed/moved/replaced counts), `StateChange` naming the component whose
  setter ran, and `KeyedListDiagnostic`. `RenderPass` Start names the component whose
  re-render request scheduled the pass (`requesterId`, `requesterName`, `requestCount`), with
  no need for the verbose `State` keyword. `ComponentRender` Start carries the C# call site that
  created the component from the source map (`callSiteFile` (the file name only),
  `callSiteLine`, `callSiteColumn`). `Reconcile` Stop counts the panel child edits made during
  the entry at the default level (`inserted`, `removed`, `moved`, `replaced`). Function and memo
  components are named by their render function, as DevTools names them
  (`RenderEachTime in MainPage.Render`). With no session listening each emit site is one
  enabled check. The provider ships at schema revision 2; schema:
  `docs/reference/perf-trace-events.md` (issues [#1484](https://github.com/microsoft/microsoft-ui-reactor/issues/1484), [#1489](https://github.com/microsoft/microsoft-ui-reactor/issues/1489); call-site fields follow the
  spec 044 §6.2.1 payload PII policy).

- `TitleBarElement.LeftHeader` and `.LeftHeader(Element)` support declarative content
  before the title bar's title, using the same child lifecycle as `RightHeader`
  (issue [#1478](https://github.com/microsoft/microsoft-ui-reactor/issues/1478)).

- Opt-in message-wide Markdown text selection in Reactor Advanced:
  `MarkdownOptions.TextSelection`, `MarkdownTextSelection.Scope`, semantic `Surface`
  and composable `Visibility` gates coordinate separately rendered Markdown leaves,
  lists, code and tables. Includes grapheme-aware caret movement, modern ScrollView /
  ItemsView scrolling, UIA TextPattern ranges and feedback-only `OnCopyResult` after
  Reactor's single clipboard write (openclaw/openclaw-windows-node#1844).

- `PersonPictureElement.ProfilePicture(string? uri)` fluent modifier, matching the existing
  `DisplayName` / `Initials` sugar. ReactorGallery's PersonPicture page uses it to show an
  avatar loaded over HTTPS (issue [#1460](https://github.com/microsoft/microsoft-ui-reactor/issues/1460)).

- `ControlDescriptor.HandCodedControlled` takes `armOnMount`. With `valueDiffEcho`, it arms
  the Mount write's echo as well as Update's, for a control whose change event arrives
  after the trampoline is wired, as `RichEditBox`'s `Document.SetText` does
  (spec 047 §8.3).

## Changed

- **`AsyncValue<T>` and `LoadState` are now C# 15 `closed` records (breaking for
  subclasses).** They were abstract records whose `private protected` constructor was meant
  to keep the case set closed, but a subclass in another assembly could still chain to the
  compiler-generated `protected` copy constructor (`: base(other)`); such subclasses now fail
  with CS9382. The built-in cases and pattern matching are unchanged. Consumers compiling
  with C# 15 can `switch` over the cases exhaustively without a `_` arm, and a missing case
  is warning CS8509. Reactor still targets `net10.0` and stays consumable from the .NET 10
  SDK; building Reactor from source now requires the .NET 11 SDK (spec 020 §5).
- Public `SingleContent` strategies leave native setter-supplied content intact
  when no Element child is declared (on mount or on both sides of an update).
  Declare `Empty()` to clear the slot explicitly; this also applies to
  third-party control handlers (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).
- `Button` and the button family share content reconciliation that preserves
  descendant state. For `Button`, replaced declared content is now cleaned up when
  it gives way to text or native setter-supplied content. A declared `ContentElement`
  now also supplies `Button`'s caption-derived accessible name instead of `Label`;
  icon-only content should supply `.AutomationName(...)` (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).
- `CheckBox(value, callback, null)` and `ThreeStateCheckBox(value, callback, null)`
  are now ambiguous between string and Element content overloads. Use `label: null`
  or cast to `(string?)null` to select the existing text overload (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).
- **`ExpanderElement.HeaderTemplate` and `.HeaderTemplate(Element)` are renamed to
  `HeaderContent` and `.HeaderContent(Element)` (breaking).** This Element-valued
  child is mounted into WinUI's `Expander.Header`, not its `HeaderTemplate`
  DataTemplate property. Rename fluent calls, object/`with` initializers, and
  property reads, then recompile consumers; string-header fallback and child
  lifecycle behavior are unchanged (issue [#1480](https://github.com/microsoft/microsoft-ui-reactor/issues/1480)). See the [migration notes](/microsoft-ui-reactor/main/guides/layout#migration-expanderheadertemplate-renamed-to-headercontent).

- `.Set` writes to `BorderThickness` or `BorderBrush` on `Grid` and `StackPanel`
  now report `REACTOR_POOL_001` (Warning). Builds using `TreatWarningsAsErrors`
  may need to use the corresponding fluent modifiers instead. The same writes on
  `RelativePanel` now report `REACTOR_MOD_002` (Info), since it is not pooled
  (issue [#1039](https://github.com/microsoft/microsoft-ui-reactor/issues/1039)).

- The generated API index now includes Reactor Advanced factories, modifiers, hooks,
  enums and public types alongside the core Reactor surface, keeping `mur --api` and
  both shipped agent-reference copies complete (PR [#1475](https://github.com/microsoft/microsoft-ui-reactor/issues/1475)).

- The internal Yoga layout engine behind `FlexPanel` is synced to upstream
  facebook/yoga@41118bd (pinned in `cgmanifest.json`), with a few documented local
  divergences. `FlexPanel` layouts are unchanged apart from the fixes listed under Fixed.
- **The guide, README, agent kit and `bootstrap.ps1` now describe Windows App SDK template pack
  `0.0.8-alpha`** (microsoft/WindowsAppSDK#6845; spec 022). That pack adds `reactor-singlefile`, a
  one-file app with no `.csproj`, and a `--packaging Packaged|Unpackaged` option on the four project
  templates, so the unpackaged shape no longer needs a hand edit after scaffolding. Its scaffolds no
  longer reference the `Microsoft.WindowsAppSDK` metapackage and the `--wasdk-version` option is
  gone: the Windows App SDK arrives through `Microsoft.UI.Reactor` at the version Reactor depends on.
  Add the metapackage yourself for a self-contained app or a newer WinUI. An older installed pack
  rejects `--packaging` and lacks `reactor-singlefile`; update it with
  `winapp new --list --template-version latest`.

## Fixed

- In diagnostics mode (`REACTOR_DIAGNOSTICS=1`), mounting a different root over content
  that keeps a closed `CommandBarFlyout` no longer leaves that flyout's commands holding
  a collected `ReactorSource` value. CsWinRT boxes a string written to a
  DependencyProperty as a managed CCW, and XAML stops keeping it alive on those commands
  after the swap. The next garbage collection freed the renamed value, and any later read
  of the property threw `NullReferenceException`, including Reactor's own walk on the next
  root swap. The renamed values are now held for the process, like the values written
  when the commands mounted (spec 010).

- Removing a subtree through the pooled teardown path now unregisters the
  `ErrorBoundary` nodes inside it, as the non-pooled path already did, so a
  recycled wrapper control no longer keeps a stale boundary record (issue [#1482](https://github.com/microsoft/microsoft-ui-reactor/issues/1482)).

- `ContentDialog` resolves the application implicit style, falling back to
  `DefaultContentDialogStyle`, before its first template realization. This restores
  the native entrance animation for programmatically created dialogs while
  preserving app-level implicit styles. Caller `.Set(...)` styles keep priority
  (issue [#1487](https://github.com/microsoft/microsoft-ui-reactor/issues/1487);
  [WinUI #8476](https://github.com/microsoft/microsoft-ui-xaml/issues/8476)).

- Flyout, MenuFlyout and CommandBarFlyout targets retain their own event callbacks
  after mount, update and pool reuse. Decorator identity is stored separately so
  flyout callbacks and teardown still use the current decorator. Exit transitions
  and connected-animation keys use the first configured value from the outermost
  decorator inward. Animated modifiers select the current render's `.Animate()`
  curve when a flyout target or decorator updates, with ambient curves taking
  precedence. Unmount also releases refs and animation state on intermediate
  decorators. Nested decorators attach only
  the outermost native flyout, including through transparent Memo or RegisterType
  targets. They clean up displaced content and keep inner updates
  from changing the outer flyout or duplicating its callbacks. For these
  decorators, the provisional `Reconciler.GetElementTag` now returns the target's
  tag instead of the decorator element (spec 047 §14 Phase 1; [#942](https://github.com/microsoft/microsoft-ui-reactor/issues/942)).

- A controlled `RichEditBox` or `AutoSuggestBox` no longer drops the user's first edit after
  a state write ([#1455](https://github.com/microsoft/microsoft-ui-reactor/issues/1455)). Both now suppress their asynchronous echo with the value-diff arm
  instead of the suppress counter, which kept a token after every write. A `RichEditBox`
  whose state value contains line breaks no longer calls `SetText` on every render, and
  `onTextChanged` keeps a trailing break the user types (spec 047 §8.3).

- `BorderBrush`, `BorderThickness`, and `BorderInlineStart` now apply to `Grid`,
  `StackPanel`, and `RelativePanel`, including themed border brushes. Pooled grids
  and stack panels release their border values before reuse. The Regedit example
  uses the supported fluent modifiers (issue [#1039](https://github.com/microsoft/microsoft-ui-reactor/issues/1039)).

- Pooled controls release `HorizontalContentAlignment` and `VerticalContentAlignment`
  on return, preventing content alignment from leaking into the next element that
  reuses the control ([issue #1013](https://github.com/microsoft/microsoft-ui-reactor/issues/1013)).

- `UseInfiniteResource` and `UseDataSource` hooks that share a `CacheKeyPrefix` isolate
  cached pages by the hook's initial page size. Hooks with different sizes no longer reuse
  incompatible pages, while hooks with matching sizes and query dependencies can still
  share them (spec 020 §14 D6; [#1452](https://github.com/microsoft/microsoft-ui-reactor/issues/1452), case 3).
- `UseDataSource` retains its initial normalized page size across rerenders and
  query restarts, keeping requests and cursor offsets aligned with resource row
  positions. A later render that asks for a different size logs one
  `UseDataSource.PageSize` diagnostic warning per hook instead. To use a different size,
  remount the calling component by changing its key from the parent (part of [#1452](https://github.com/microsoft/microsoft-ui-reactor/issues/1452), case 2).
- `DataGrid` under `ReactorFeatureFlags.UseHookBasedPaging` now keeps its mount-time page
  size, matching the legacy paging path. Changing `RowHeight` or `EstimatedRowHeight` at
  runtime no longer restarts pagination or discards loaded rows when a sort or filter is
  active (part of [#1452](https://github.com/microsoft/microsoft-ui-reactor/issues/1452)).

- `PersonPicture`'s `ProfilePicture` no longer throws during reconciliation for empty,
  relative, or malformed URIs; it shows no picture instead. Removing `ProfilePicture` on
  a later render now clears the previous image rather than leaving it on screen (issue [#1460](https://github.com/microsoft/microsoft-ui-reactor/issues/1460)).
- **`FlexPanel` no longer gives shrinking children astronomically large sizes.** When every
  shrinkable item in a line froze at its minimum size, the remaining shrink total could be a
  float residual instead of exactly 0, and dividing by it produced sizes around 10⁹–10¹¹ px
  (or, once clamped, children squeezed to their minimum and text wrapped into very tall
  panels). Ported from upstream Yoga (facebook/yoga#1665, facebook/yoga#2038).
- **`FlexPanel` sizes now land exactly on the device-pixel grid.** Rounded widths and heights
  are computed in double precision, so far from the origin a child no longer comes out one
  float step short (e.g. 43.9998 instead of 44) of its measured size, and a measured child
  whose size is a whole number of device pixels keeps exactly that size instead of gaining
  or losing a pixel (which could wrap or truncate its text). Ported from upstream Yoga
  (facebook/yoga#2037, facebook/yoga#2040).
- **Wrapping `FlexPanel` rows no longer break a line on float rounding noise** when arranged
  at exactly their content-sized width (facebook/yoga#2022).
- **`Collapsed` children of a `FlexPanel` with `Wrap = WrapReverse` are no longer arranged
  with a NaN rect.** The wrap-reverse pass mirrored hidden children too, using their
  undefined measured size; they now keep the zero layout Yoga gives `display: none` nodes.
  This bug is also present in upstream Yoga.
