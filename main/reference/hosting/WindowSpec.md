---
title: "WindowSpec"
seo:
  description: "Immutable (init-only) declarative description of a top-level Reactor window. Hand to ReactorApp.OpenWindow to open a window; hand to ReactorWindow.Update to diff against the current spec and apply only changed fields."
---

Immutable (init-only) declarative description of a top-level Reactor window. Hand to [`ReactorApp.OpenWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#openwindowwindowspec-funccomponent-actionreactorhost) to open a window; hand to [`ReactorWindow.Update`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#update) to diff against the current spec and apply only changed fields.

```csharp
public sealed record WindowSpec
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)

## Remarks [#remarks]

All sizes and positions are **DIPs** (device-independent pixels) — no Win32 / no `SizeInt32` in user code. (spec 036 §4.1)

[`WindowSpec.Validate`](#validate) enforces the cross-field invariants and is called by the hosting layer (the [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) constructor and [`ReactorWindow.Update`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#update)) before the spec is used; tests may call it directly. It requires `Width`/`Height` to be positive *when set* (`null` defers the initial extent to the OS), max ≥ min when both are set, and [`WindowSpec.ManualPosition`](#manualposition) iff [`WindowSpec.StartPosition`](#startposition) is [`WindowStartPosition.Manual`](/microsoft-ui-reactor/main/reference/hosting/window-options#values_9).

## Constructors [#constructors]

### `WindowSpec` [#windowspec_1]

Construct with explicit field values. Validation runs after the record auto-init.

```csharp
public WindowSpec()
```

## Properties [#properties]

### `ActivateOnOpen` [#activateonopen]

Whether the window auto-activates after content mounts. Default: true.

```csharp
public bool ActivateOnOpen  // get, init
```

### `AspectRatio` [#aspectratio]

Optional width / height aspect lock applied during interactive resize.

```csharp
public double? AspectRatio  // get, init
```

### `AspectRatioBasis` [#aspectratiobasis]

Whether [`WindowSpec.AspectRatio`](#aspectratio) constrains the outer window rect or the client (content) area. Default is [`AspectRatioBasis.Window`](/microsoft-ui-reactor/main/reference/hosting/window-options#values); switch to [`AspectRatioBasis.Client`](/microsoft-ui-reactor/main/reference/hosting/window-options#values) when the ratio describes the content (e.g. 1:1 for a square video, 16:9 for a game viewport) and the framework should auto-account for chrome.

```csharp
public AspectRatioBasis AspectRatioBasis  // get, init
```

### `Backdrop` [#backdrop]

Optional declarative backdrop. Seeds the per-host modifier.

```csharp
public BackdropChoice? Backdrop  // get, init
```

### `CornerStyle` [#cornerstyle]

DWM corner preference. On Windows 10 the platform ignores this setting.

```csharp
public WindowCornerStyle CornerStyle  // get, init
```

### `Embed` [#embed]

Optional request to embed this window into an external HWND host.

```csharp
public EmbedRequest? Embed  // get, init
```

### `ExtendsContentIntoTitleBar` [#extendscontentintotitlebar]

Whether app content extends into the title-bar region. When `null`, a mounted `TitleBar(...)` element infers `true`; otherwise the explicit value wins. Hot reload cannot migrate the previous bool field shape across this type change; restart after editing this field during HR.

```csharp
public bool? ExtendsContentIntoTitleBar  // get, init
```

### `Height` [#height]

Initial DIP height. Must be positive when set. `null` (the default) leaves the initial height to the OS. See [`WindowSpec.Width`](#width).

```csharp
public double? Height  // get, init
```

### `Icon` [#icon]

Optional window icon — the Win32 `HICON` Windows shows in the window's caption and the Alt-Tab switcher. Build it with [`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath) for a file beside the app or [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource) for a packaged `ms-appx:///` asset.

```csharp
public WindowIcon? Icon  // get, init
```

**Remarks**

Distinct from `TitleBar(...).Icon(...)`, which draws an app mark inside the window's client area. A window may set both, and they may legitimately differ. When this is `null`, Reactor falls back to `Assets\AppIcon.ico` beside the app and then to the executable's embedded icon.

In an **unpackaged** app this also drives the taskbar button and Task Manager, because there is no package identity to take precedence. In a **packaged** app it does not: the shell resolves those two surfaces through package identity and the manifest's `Square44x44Logo`, never looking at the window handle. A packaged app that wants a consistent icon everywhere must ship a matching manifest logo in addition to setting this.

A binary source ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)) is **not** accepted here — this surface needs a filesystem path, because that is what `AppWindow.SetIcon` takes. Declaring one falls through to the same convention/PE fallback as declaring no icon at all, so the window is never left barer than it would otherwise have been. Binary sources are for the tray icon, the taskbar overlay, and thumbnail-toolbar buttons.

### `IgnorePointerInput` [#ignorepointerinput]

Apply the Win32 `WS_EX_TRANSPARENT` extended style so mouse events pass THROUGH the window to whatever's underneath. Default false. Used by spec 045 §2.6 tear-off so the drag preview doesn't block clicks on drop-target overlays below it.

```csharp
public bool IgnorePointerInput  // get, init
```

**Remarks**

Requires [`WindowSpec.Opacity`](#opacity) \< 1.0 — the OS only honors `WS_EX_TRANSPARENT` on layered windows. [`WindowSpec.Validate`](#validate) throws when this field is true with `Opacity == 1.0`; the runtime mutator `ReactorWindow.SetIgnorePointerInput(true)` also throws if the live window is not layered.

### `IsMaximizable` [#ismaximizable]

Whether the maximize caption button is enabled (Overlapped only).

```csharp
public bool IsMaximizable  // get, init
```

### `IsMinimizable` [#isminimizable]

Whether the minimize caption button is enabled (Overlapped only).

```csharp
public bool IsMinimizable  // get, init
```

### `IsMovableByBackground` [#ismovablebybackground]

Whether pressing non-interactive background starts an OS window drag.

```csharp
public bool IsMovableByBackground  // get, init
```

### `Key` [#key]

Optional stable identity for `UseOpenWindow` reuse and shell lookups.

```csharp
public WindowKey? Key  // get, init
```

### `Level` [#level]

Z-order tier for this window.

```csharp
public WindowLevel Level  // get, init
```

### `ManualPosition` [#manualposition]

DIP top-left position. Set together with [`WindowStartPosition.Manual`](/microsoft-ui-reactor/main/reference/hosting/window-options#values_9); otherwise `null`.

```csharp
public (double X, double Y)? ManualPosition  // get, init
```

**Remarks**

The DIP→physical conversion uses the window's **initial** DPI (the monitor it opens on). Coordinates that target a different monitor on a mixed-DPI desktop may land at a slightly different physical position because Windows virtual-screen coordinates are physical pixels with no global DIP coordinate space. For absolute placement on a specific non-primary monitor, prefer [`WindowStartPosition.CenterOnOwner`](/microsoft-ui-reactor/main/reference/hosting/window-options#values_9) (with an owner already on that monitor) or call [`ReactorWindow.SetPosition`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#setposition) after the window opens.

### `MaxHeight` [#maxheight]

Optional maximum DIP height.

```csharp
public double? MaxHeight  // get, init
```

### `MaxWidth` [#maxwidth]

Optional maximum DIP width.

```csharp
public double? MaxWidth  // get, init
```

### `MinHeight` [#minheight]

Optional minimum DIP height.

```csharp
public double? MinHeight  // get, init
```

### `MinWidth` [#minwidth]

Optional minimum DIP width.

```csharp
public double? MinWidth  // get, init
```

### `NoActivate` [#noactivate]

Apply the Win32 `WS_EX_NOACTIVATE` extended style so the window appears without stealing foreground activation. Matches VS tool-window / drag-preview behavior. Default false. The flag is applied during chrome setup (before Activate) so the first show observes it.

```csharp
public bool NoActivate  // get, init
```

### `Opacity` [#opacity]

Window-wide alpha in \[0..1\]. 1.0 = fully opaque (default, no layering overhead). Values below 1.0 are applied via Win32 `SetLayeredWindowAttributes` on the underlying HWND. Used by the docking subsystem to render an in-flight tear-off floating window at reduced opacity (spec 045 §2.6 tear-off follow-up); apps may also set it for HUD overlays etc.

```csharp
public double Opacity  // get, init
```

### `Owner` [#owner]

Optional parent window for owned-window semantics.

```csharp
public ReactorWindow? Owner  // get, init
```

### `PersistPlacement` [#persistplacement]

Restore window placement from the registered store on open and save on close. Ignored when [`WindowSpec.PersistenceId`](#persistenceid) is null.

```csharp
public bool PersistPlacement  // get, init
```

### `PersistenceFallback` [#persistencefallback]

Initial placement used when persistence is enabled but no placement exists yet.

```csharp
public WindowStartPosition PersistenceFallback  // get, init
```

### `PersistenceId` [#persistenceid]

Optional persistence id for placement and future per-window persistence subsystems.

```csharp
public string? PersistenceId  // get, init
```

### `Presenter` [#presenter]

Coarse presenter selection.

```csharp
public PresenterKind Presenter  // get, init
```

### `RenderErrorHandler` [#rendererrorhandler]

Optional replacement for the built-in render-error fallback in this window. Applied to the window's [`ReactorHost.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#rendererrorhandler) before the first render (and again by [`ReactorWindow.Update`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#update)). `null` (the default) falls through to [`ReactorApp.DefaultRenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#defaultrendererrorhandler). See [`WindowSpec.RenderErrorHandler`](#rendererrorhandler). (issue #1291)

```csharp
public RenderErrorHandler? RenderErrorHandler  // get, init
```

### `ResizeMode` [#resizemode]

Resize/chrome policy for overlapped windows.

```csharp
public WindowResizeMode ResizeMode  // get, init
```

### `ShowInSwitcher` [#showinswitcher]

Whether the window appears in Alt-Tab / shell switchers.

```csharp
public bool ShowInSwitcher  // get, init
```

### `ShowInTaskbar` [#showintaskbar]

Whether the window appears in the taskbar.

```csharp
public bool ShowInTaskbar  // get, init
```

### `SizeToContent` [#sizetocontent]

Content-driven sizing mode. Size changes are applied after the first layout pass, so apps may see one frame at the initial Width/Height (or the OS-chosen size when those are null) before content settles.

```csharp
public WindowSizeToContent SizeToContent  // get, init
```

### `StartPosition` [#startposition]

Initial placement strategy.

```csharp
public WindowStartPosition StartPosition  // get, init
```

### `Style` [#style]

Window chrome style for overlapped windows.

```csharp
public WindowStyle Style  // get, init
```

### `Title` [#title]

Window caption text. Defaults to `"Reactor App"`.

```csharp
public string Title  // get, init
```

### `TitleBarHeight` [#titlebarheight]

System caption height. `null` (the default) leaves the platform default alone; a mounted `TitleBar(...)` element's `HeightOption` then applies instead, if it declares one.

```csharp
public WindowTitleBarHeight? TitleBarHeight  // get, init
```

**Remarks**

Requires a content-extended window — the native `AppWindow.TitleBar.PreferredHeightOption` setter throws `ERROR_INVALID_STATE` otherwise. Reactor applies the value only once the window is content-extended (including the deferred flip a `TitleBar(...)` element performs at mount) and warns instead of throwing when the window never extends.

This sizes the **system caption** only. A mounted WinUI `TitleBar` control does not follow it, which is why `TitleBar(...).Tall()` — which sizes both — is the preferred spelling when the window renders a `TitleBar(...)` element. (issue #917)

### `TitleBarTheme` [#titlebartheme]

Theme of the system caption buttons (`AppWindow.TitleBar.PreferredTheme`). `null` (the default) leaves the platform default alone; a mounted `TitleBar(...)` element's `PreferredTheme` then applies instead, if it declares one. (issue #1297)

```csharp
public WindowTitleBarTheme? TitleBarTheme  // get, init
```

**Remarks**

WinUI does not derive the caption theme from the content's `RequestedTheme`; an app that themes its content opposite to the system sets this to match. Removing a declaration restores the value the caption had before Reactor first applied one; a value set imperatively and never declared is left alone.

### `Width` [#width]

Initial DIP width. Must be positive when set. `null` (the default) leaves the initial width to the OS — Reactor does not override that axis, so the window keeps the width Windows picks for a new top-level window (the same behavior a plain XAML `Window` gets). When both axes are `null` no `AppWindow.Resize` call is made at all.

```csharp
public double? Width  // get, init
```

## Methods [#methods]

### `Validate` [#validate]

Throws [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) when any cross-field invariant is violated. Called by [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) (constructor and [`ReactorWindow.Update`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#update)) before the spec is applied; tests may also call this directly to verify a spec.

```csharp
public void Validate()
```

### `WithPersistence` [#withpersistence]

Configure placement persistence in one call.

```csharp
public WindowSpec WithPersistence(string id, WindowStartPosition fallback = WindowStartPosition.Default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `id` | `string` |  |  |
| `fallback` | [`WindowStartPosition`](/microsoft-ui-reactor/main/reference/hosting/window-options#windowstartposition) | `WindowStartPosition.Default` |  |
