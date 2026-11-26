---
title: "0.1.0-preview.15"
type: changelog
date: 2026-09-11
changelog:
  version: "0.1.0-preview.15"
description: "3 added, 3 changed and 4 fixed."
---

## Added

- **Binary icon sources on `WindowIcon` (spec 036 §4.1, issue [#1185](https://github.com/microsoft/microsoft-ui-reactor/issues/1185)).**
  `WindowIcon.FromBytes(ReadOnlySpan<byte>)` takes encoded `.ico` or PNG data and
  `WindowIcon.FromRgba(ReadOnlySpan<byte>, int, int)` takes a raw straight-alpha RGBA8
  buffer, so an icon that lives in an embedded resource, a download, or a
  procedurally-drawn badge no longer has to be written to a temporary file before the
  shell can show it. Consumed by the three surfaces that need a raw `HICON`: the tray
  icon (spec 036 §11.4), the taskbar overlay (§11.2), and thumbnail-toolbar buttons
  (§11.5). Both factories copy the caller's buffer, and a multi-frame `.ico` held in
  memory has its closest frame selected the same way `LoadImageW` would from a file.
  The new `WindowIcon.Kind` (`WindowIconKind.Path` / `Resource` / `Binary`) reports which
  factory produced an icon; `IsResource` is unchanged.

  `WindowSpec.Icon` does **not** accept a binary source — `AppWindow.SetIcon` needs a
  filesystem path — and reports it as not applied, so the window falls through to the
  `Assets\AppIcon.ico` convention or its PE icon rather than showing nothing. Jump lists
  and the `TitleBar` icon default skip it for the reason they already skip a PE icon:
  they need a `Uri` or a path, and binary data is neither.

- **`REACTOR_ICON_001` — a `WindowIcon` source kind the target surface silently skips
  (spec 061, issue [#1185](https://github.com/microsoft/microsoft-ui-reactor/issues/1185)).** Every `WindowIcon` factory type-checks against every
  icon-taking surface, but the surfaces need different primitives and quietly drop what
  they cannot use — a `Debug.WriteLine` and a missing glyph, invisible in a Release build.
  The analyzer reports `FromResource` handed to a tray icon, taskbar overlay or
  thumbnail-toolbar button (all need a raw `HICON`, which cannot come from an `ms-appx:`
  URI), and `FromBytes` / `FromRgba` handed to `WindowSpec.Icon`, `ReactorApp.Run(icon:)`
  or a jump-list entry (which need a filesystem path or a `Uri`).

  It fires only when the icon's kind is provably known at the use site — a direct factory
  call, or a write-once local whose initializer is one (including through the documented
  `UseMemo(() => WindowIcon.FromPath(...))` idiom). A conditional, field, parameter or
  method result stays silent, as does either non-binary kind on a jump-list entry, since
  packaged-versus-unpackaged is a runtime property. Reactor's own spec 036 carried the
  `FromResource`-for-a-tray-icon mistake in its worked example, which is the case for
  surfacing this in the editor.

- **Struct-typed overloads for `.Margin(...)`, `.Padding(...)` and `.CornerRadius(...)`
  (issue [#1192](https://github.com/microsoft/microsoft-ui-reactor/issues/1192)).** All four common layout modifiers now accept their WinUI struct
  directly, matching the `.BorderThickness(Thickness)` overload that has shipped since
  [#775](https://github.com/microsoft/microsoft-ui-reactor/issues/775): `.Margin(Thickness)`, `.Padding(Thickness)` and
  `.CornerRadius(CornerRadius)`. This makes the `REACTOR_POOL_001` migration a
  lift-and-shift for struct-typed writes — `.Set(fe => fe.Margin = someThickness)`
  becomes `.Margin(someThickness)` instead of forcing the value to be decomposed into
  four doubles and any struct-typed local to be re-typed. Purely additive; the `double`
  overloads keep their existing binding and stay the ergonomic default.

  Two consequences for hand-written code, both from the bare literal converting to
  `double` and to the struct alike. `.Margin(default)`, `.Padding(default)` and
  `.CornerRadius(default)` are now ambiguous (`CS0121`); `.BorderThickness(default)`
  has always behaved this way for the same reason. The parameterless target-typed
  `.Margin(new())`, `.Padding(new())` and `.CornerRadius(new())` are newly ambiguous
  too — they previously bound the `double` overload as `new double()`, i.e. zero.
  Name the type in either shape — `default(Thickness)` / `new Thickness()` for
  `.Margin` / `.Padding` / `.BorderThickness`, `default(CornerRadius)` /
  `new CornerRadius()` for `.CornerRadius` — or pass a value.

## Changed

- **Markdown list-item defaults now use `GridElement` rather than `StackElement`
  ([openclaw/openclaw-windows-node#1362](https://github.com/openclaw/openclaw-windows-node/issues/1362)).**
  `MarkdownOptions.ListItem` still receives an `Element` after default construction
  and may wrap or replace it. Callbacks that cast the default to `StackElement`
  must adapt to the Auto/Star Grid.

- **The documentation site is published per release, with a version selector (no
  product code changed).** <https://microsoft.github.io/microsoft-ui-reactor/> was
  rebuilt from the tip of `main` on every docs push, so it only ever showed unreleased
  documentation and there was no way to read the docs matching the release you were
  running. The site is now versioned with [mike](https://github.com/jimporter/mike):
  each version renders once into a `gh-pages` branch and stays byte-identical
  afterwards, a release tag publishes its version and takes the `latest` alias, and
  `main` is published separately as `main (development)`. The site root redirects to
  `latest`, and every version that is not `latest` carries a banner saying so.

  Versioning moves every page under a version directory, which would turn already
  published links into 404s, so the published site root also carries a `404.html` that
  forwards legacy unversioned paths to the same page under `latest`, preserving the
  query string and anchor — existing links such as `.../getting-started/#manual-setup`
  keep working. See
  [the release runbook](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/contributing/release-runbook.md#versioned-documentation-site).

- **`PoolResetSetCodeFix` now fixes struct-typed `.Set(...)` writes it previously left
  alone (issue [#1192](https://github.com/microsoft/microsoft-ui-reactor/issues/1192)).** It could only rewrite a literal `new Thickness(uniform)` or
  `new Thickness(l, t, r, b)`, so every other right-hand side — an opaque local, a
  field, a call, a ternary — was reported and left for a human. With the struct
  overloads above those values now pass straight through to the modifier. Both
  spellings of a constructor literal still decompose, including the target-typed
  `new(8)`, so `.Margin(8)` remains the output rather than `.Margin(new Thickness(8))`.
  A literal carrying an object initializer or a named argument is no longer
  decomposed: `new Thickness(8) { Left = 5 }` is `Thickness(5,8,8,8)`, so emitting
  `.Margin(8)` would have silently dropped the initializer, and the struct's parameter
  names differ from the modifier's (`Thickness(uniformLength)` against
  `Margin(uniform)`), so copying a named argument across produced `CS1739`. Both now
  ride the struct overload whole. A target-typed `new(...)` that cannot be decomposed
  is still left unfixed, because it carries no type of its own and the rewrite would
  be ambiguous.

## Fixed

- **Long Markdown list content wraps within finite available width
  ([openclaw/openclaw-windows-node#1362](https://github.com/openclaw/openclaw-windows-node/issues/1362)).**
  Default list rows now measure content in a Star column beside an Auto-sized marker
  instead of a horizontal StackPanel's infinite-width measure. Plain, formatted,
  ordered, nested, task-list and multi-block content keep their markers, spacing
  and selection, including in unified rich-text mode.

- `REACTOR_MOD_002` now covers four properties `ElementPool.CleanElement` resets that no
  diagnostic mentioned at all: `IsHitTestVisible`, `Stretch`, `StretchDirection` and `IsActive`.
  `IsHitTestVisible` was excluded on the stated grounds that no modifier existed, while
  `.IsHitTestVisible(bool)` had been in `ElementExtensions.cs` all along, and `Stretch` was
  excluded as a "Viewbox-only modifier" — a description of what the element-type gate is for
  rather than a reason to skip the property. Because an exclusion counts as a classification,
  nothing ever rechecked either claim (issue [#1193](https://github.com/microsoft/microsoft-ui-reactor/issues/1193)).
- `ElementPool.CleanElement` now clears `IsTabStop` on every pooled element rather than only on
  `Control` receivers. WinUI 3 declares the property on `UIElement` and `ApplyModifiers` writes it
  ungated, so `.IsTabStop(false)` reaches poolable non-`Control`s — `TextBlock`, `RichTextBlock`,
  `Grid`, `StackPanel`, `Border`, `Canvas`, `Viewbox`, `Image` — and each could carry a stale tab
  stop into its next renter, making a control unexpectedly unreachable by keyboard. This is the
  same missing-reset shape as issue [#985](https://github.com/microsoft/microsoft-ui-reactor/issues/985), found by the widened consistency scan (issue [#1193](https://github.com/microsoft/microsoft-ui-reactor/issues/1193)).
- The pool ⇄ analyzer consistency invariants now scan the whole of `CleanElement` instead of
  stopping at its `switch (fe)` dispatch, so resets in the type-specific arms — the `TextBlock`
  font/text family, the `TextBox`, `Viewbox` and `ProgressRing` arms — are checked against
  `ModifierTable` for the first time. Previously a clear placed after the dispatch was invisible
  to every invariant, and [#985](https://github.com/microsoft/microsoft-ui-reactor/issues/985)/#950 had to relocate clears into the FE-common block to get them
  covered (issue [#1193](https://github.com/microsoft/microsoft-ui-reactor/issues/1193)).
