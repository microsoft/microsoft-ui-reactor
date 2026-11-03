---
name: reactor-wrapping
description: "Turn ANY WinUI control — a vendor/third-party control, your own custom Control, or a Windows Community Toolkit one — into a first-class declarative Reactor element with the [GenerateReactorWrapper] source generator, zero hand-written wrapper code. Covers auto-inference (props, content, two-way pairs, panel/items children, events), the [Wrap*] annotation cheat-sheet, imperative-lifecycle controls, diagnostics, and when to fall back to a hand-written ControlDescriptor."
---

# Reactor — Wrapping Arbitrary Controls (source generator)

Use this skill when the app needs a control Reactor has **no built-in factory
for** — a Windows Community Toolkit control, a third-party vendor control, or the
app's own custom `Control` subclass. The `Reactor.Wrappers.Generator` source
generator turns the control into a first-class declarative element (init-props +
`ControlDescriptor` + Pattern-A registration + a parameterized factory named after
the control) from a single annotated partial record. **No hand-written wrapper,
handler, or registration code.**

> Canonical worked example: `samples/apps/wct-controls/` (declarations in
> `WctControls.cs`, usage in `Program.cs`). Design spec: `docs/specs/058-control-wrapper-generator.md`.
> For controls the generator still can't express, drop to the hand-written
> `ControlDescriptor` / `IElementHandler` path in `docs/guide/extending-reactor-controls.md`.

## The whole feature in one example

```csharp
using Microsoft.UI.Reactor.Wrappers;   // the [Wrap*] authoring attributes

namespace MyApp;

// 1. Declare an empty partial record, annotated with the control to wrap.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard))]
public partial record SettingsCardElement;
```

```csharp
// 2. Consume the generated factory via `using static` (named after the control).
using static MyApp.SettingsCardElement;

SettingsCard(
    header: "Wi-Fi",
    description: wifiOn ? "Connected" : "Disconnected",
    content: ToggleSwitch(isOn: wifiOn, onIsOnChanged: setWifiOn))  // a Reactor child INSIDE a WCT control
```

The generator fills the rest of that partial: one init-property per surfaced
control property, the single-content/children slot, `On{Event}` callbacks, the
`ControlDescriptor`, the trim-rooting `static` cctor registration, and the
`SettingsCard(...)` factory. `Setters` is always emitted as an escape hatch
(`.Set(c => c.Foo = …)`) for anything not surfaced.

The generator emits `ReactorApp.TryRegisterControlAssembly(typeof(X).Assembly)`
in the element's static constructor to discover a control library's metadata
before first realization (spec 058 §9a). The `Try` call quietly returns `false`
if the provider has been trimmed; a `0xC000027B` crash when the wrapped control
is first realized is a symptom that explicit registration may be needed.

For code-only Native AOT apps, implicit discovery of the toolkit provider can be
trimmed. Explicitly construct and register its generated `XamlMetaDataProvider`
before realization when needed; see `docs/aot-support.md`. The observable Items
collection's generated WinRT exposure does not root toolkit metadata.

## Requirements & setup

- Reference `Microsoft.UI.Reactor` (carries the generator in `analyzers/dotnet/cs/`
  and the authoring attributes in `Reactor.Wrappers.Abstractions.dll`).
- Reference the control's own package (e.g. the WCT
  `CommunityToolkit.WinUI.Controls.*` packages).
- The target must be a **non-static `FrameworkElement`** with a **public
  parameterless constructor** (else `REACTORGEN001`).
- Keep the record `partial` (the generator fills the other half) and in a
  namespace you control.

## What is auto-inferred (zero config)

The generator walks members from the most-derived type **up to but excluding**
`Control`/`FrameworkElement` (Reactor already models that layout plumbing via
generic modifiers like `.Margin()`, `.Width()`):

| Control shape | Becomes |
|---|---|
| Settable value prop (`string`/`object`→text, `bool`/`int`/`double`/enum, structs like `Thickness`/`Color`, `Brush`/`FontFamily`, `bool?`/`DateTimeOffset?` tri-state) | a nullable / `Optional<T>` **one-way** init-prop (skipped when unset) |
| Value prop `P` **paired with** a `{P}Changed` event | a **two-way controlled** `Optional<T>` prop + `On{P}Changed` callback, with echo suppression |
| The control's `[ContentProperty]` (a `UIElement` child, or `object` named exactly `Content`) | the single **`content:`** child slot |
| A public `Children` of `UIElementCollection` (a `Panel`) | a `params Element[]` children slot |
| A public `Items` whose type is/implements `IList<object>` (an `ItemsControl`) | a `params object[] items` slot; incremental reconciliation, `.WithKey(...)` preserves state across reorders, and writable `IList<object>` sinks get an observable list on mount |
| A fire-and-forget `RoutedEventHandler` / `TypedEventHandler<,>` / `EventHandler<A>` event | an `On{Event}` callback (typed events auto-surface the whole args as `Action<A>`) |

Two-way auto-pairing keys **strictly on the `{P}Changed` name**. Controls whose
change event breaks that convention (`ToggleSwitch.IsOn`↔`Toggled`,
`Segmented.SelectedIndex`↔`SelectionChanged`) stay one-way until you add
`[WrapControlled]`.

## Annotation cheat-sheet (opt-in, when inference isn't enough)

All live in `Microsoft.UI.Reactor.Wrappers`. All are repeatable unless noted.

| Attribute | Use it to… |
|---|---|
| `[GenerateReactorWrapper(typeof(T), Exclude/Include/AutoDiscover)]` | wrap `T`; prune noisy inherited props (`Exclude = new[]{ "CommandParameter" }`) or switch to explicit opt-in (`AutoDiscover = false, Include = new[]{ … }`). |
| `[WrapControlled("P", ChangedEvent = "…")]` | make `P` two-way when its event isn't `{P}Changed`. Use `Events = new[]{ "Checked","Unchecked" }` for multi-event values (read back after any fire). `Deferred = true` for deferred/coercing string boxes (`PasswordBox.Password`, `AutoSuggestBox.Text`). A control has **one** controlled slot — two `[WrapControlled]` on one control warns `REACTORGEN012`. |
| `[WrapOneWay("P")]` | opt a prop **out** of two-way auto-pairing (display-only despite a `{P}Changed`, e.g. `ProgressBar.Value`). |
| `[WrapAlias("FriendlyName", "ControlProperty")]` | surface a control prop under a nicer name (`"Min"`→`"Minimum"`). Aliasing the content property turns it into a string value prop (no child slot). |
| `[WrapContent("Prop")]` | override which property is the `content:` child slot. |
| `[WrapConvert("Prop")]` | surface a struct prop (`CornerRadius`, `Thickness`) as an ergonomic scalar via its single-arg constructor (`double?` → `new CornerRadius(v)`). |
| `[WrapEvent("Foo", Arg = "ErrorMessage")]` | project event args into a typed `On{Event}(args.Arg)` callback; `Args = new[]{ … }` for multi-param. |
| `[WrapLifecycle(nameof(Start), OnUnmounted = nameof(Stop))]` | run a `static void M(TControl)` on mount/unmount for imperative controls (`CameraPreview.StartAsync`) — **no call-site boilerplate**. |
| `[WrapPanelChildren(PerChild = …)]` | wire per-child attached props (`Grid.SetRow`, `Canvas.SetLeft`) onto a panel; `AfterAll` for two-pass (RelativePanel). |
| `[WrapManual("P")]` + `static partial Customize(d)` | hand-chain one entry the generator can't infer while it still generates the rest. |
| `[WrapElementSlot("Prop", ControlProperty = "…")]` | auto-wire a **secondary single-element slot** that writes a dedicated control property (`TabView.TabStripHeader`, `SettingsCard.HeaderIcon`) — mounts via `ctx.MountChild`, updates via state-preserving `ctx.ReconcileChild`, and terminally unmounts the child before detaching it, independently of the descriptor author's `OnUnmount`. When no slot element is declared, the control's constructor default is left untouched. Use `ControlProperty=` when the element-facing name differs from the control property. The control property must be public-settable and `object`/`UIElement`-typed. For the **primary** child use `[WrapContent]`; for a slot that shares a control prop with a string fallback (`Expander.Header`) stay manual. |
| `[WrapPolymorphic(nameof(Resolve))]` / `[WrapDecorator(nameof(Create))]` | controls whose concrete type is chosen at runtime, or that need a fully custom mount/update/unmount lifecycle (emit an `IDecoratorElementHandler`, not a descriptor). |

### Imperative control (lifecycle) — paste-ready

```csharp
// CameraPreview must be StartAsync'd after mount and Stop'd on unmount. Declared
// ONCE here, so every call site is a plain declarative `CameraPreview(...)`.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.CameraPreview))]
[WrapEvent("PreviewFailed", Arg = "Error")]
[WrapLifecycle(nameof(StartPreview), OnUnmounted = nameof(StopPreview))]
public partial record CameraPreviewElement
{
    private static async void StartPreview(CommunityToolkit.WinUI.Controls.CameraPreview cp)
    {
        try { await cp.StartAsync(cp.CameraHelper); } catch { /* surfaced via PreviewFailed */ }
    }
    private static void StopPreview(CommunityToolkit.WinUI.Controls.CameraPreview cp)
    {
        try { cp.Stop(); } catch { /* already torn down */ }
    }
}
```

### Non-conventional two-way event

```csharp
// SelectedIndex changes through SelectionChanged, not SelectedIndexChanged.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.Segmented))]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;
// → Segmented(selectedIndex: idx, onSelectedIndexChanged: setIdx, items: …)
```

## Hand-tuning a single generated member

Write the member yourself in your half of the partial; the generator skips any
member you declare. Example — treat `Header` as templated Reactor content instead
of plain text:

```csharp
public partial record SettingsCardElement
{
    public Element? Header { get; init; }
}
```

## Diagnostics

`REACTORGEN001` (target isn't a non-static `FrameworkElement` with a public
parameterless ctor) · `…002` (bad Include/Exclude name) · `…003`/`…004`
(WrapControlled property / change-event) · `…005` (WrapAlias control property) ·
`…006` (WrapOneWay) · `…007` (WrapContent) · `…008` (WrapConvert) ·
`…009`/`…010` (WrapEvent target / arg) · `…011` (WrapLifecycle method) · `…012` (two
controlled props share one control) · `…013` (WrapElementSlot control property
missing/not-settable — set `ControlProperty=`) · `…014` (WrapElementSlot control
property not `UIElement`-assignable) · `…015` (WrapElementSlot slot name not a valid
identifier). Run `mur check <path>` — it surfaces these
with skill pointers.

## When to drop to a hand-written descriptor

The generator targets the common case. Reach for the manual
`ControlDescriptor` / `IElementHandler` path (`docs/guide/extending-reactor-controls.md`,
registered via `ControlRegistry.RegisterHandler`) only when a control's contract
is something the attributes above can't yet express — and prefer adding a reusable
`[Wrap*]` capability over a one-off handler when the shape is general.
