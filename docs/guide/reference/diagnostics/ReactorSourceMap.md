# ReactorSourceMap

Spec 010 — runtime switch and read path for Reactor source mapping.

**Why a runtime flag at all.** Interceptors are baked in at compile time, so without a runtime gate every Debug build would pay the stamping cost whether or not anyone is inspecting. This flag is what the emitted interceptor checks before writing a location, so an un-inspected Debug session allocates nothing extra.

**It does not change control tagging for leaves.** The reconciler attaches a `ReactorState` (the control → element back-pointer) only to controls that something will read back — callbacks, a key, extras, or reference modifiers (see `Reconciler.NeedsTag`), which is the allocation win PR #468 landed. `NeedsTag` has no arm for this flag on ordinary elements: a stamped element carries its `CallSite` in the `Extensions` bucket and so already satisfies the existing `Extensions is not null` test. Adding one would only tag *unstamped* elements, which have no location to return, while re-introducing the per-leaf allocation. Elements the generator does not reach (wrapper factories, bare-string children) therefore stay untagged and report no location rather than a wrong one. The one exception is component boundaries: while this flag is on, every component's Border wrapper is tagged so an inspector can find where each component starts — one allocation per component, never per leaf.

**Who turns it on.** `winapp run --devtools on` launches the app with `REACTOR_SOURCEMAP=1` (see below) and `REACTOR_DIAGNOSTICS=1` (microsoft/winappCli#1197), and `ReactorApp` sets [`ReactorSourceMap.Enabled`](#enabled) when the process was launched under the in-app preview host (`--devtools run`), so a retail session never pays. It is public-settable so a host that embeds its own inspector (or a test) can opt in without going through the CLI.

```csharp
public static class ReactorSourceMap
```

Namespace: `Microsoft.UI.Reactor.Diagnostics`.

**Learn more:** [Diagnostics](../../diagnostics.md)

## Properties

### `Enabled`

True when source mapping is active for this process. Read by every generated factory interceptor before it stamps, so it is an `int` + `Volatile` rather than a lock.

```csharp
public static bool Enabled  // get, set
```

## Methods

### `GetSource`

Resolves the DSL call site that produced `control`.

This is the whole read chain, and it deliberately reuses the back-pointer that already exists rather than adding a second attached property: `UIElement` → `ReactorAttached.StateProperty` → `ReactorState.Element` → [`Element.CallSite`](../elements/Element.md#callsite).

Returns `null` when the control was not produced by Reactor, or when the element behind it carries no [`Element.CallSite`](../elements/Element.md#callsite) — because it was built while [`ReactorSourceMap.Enabled`](#enabled) was false, or because no provider reaches that factory (see the known limitations in the source-mapping guide). Note this is a property of the ELEMENT, not of tagging: a control can be tagged for unrelated reasons (callbacks, a key, other extras) and still report no location, and an element hand-stamped with a `CallSite` reports one even with the flag off.

Target-wrapping decorators are resolved through. `Flyout`, `MenuFlyout` and `CommandBarFlyout` mount their `Target`'s control and then deliberately replace that control's tag with themselves, because their Opened/Closed handlers read it back. The realized control is still the target's — a `Button` inside a `Flyout(...)` was created by the `Button(` line — so reporting the decorator's line would name the wrong creator, the same misattribution the generator avoids for pass-through factories. The walk is a loop so nested decorators resolve to the innermost target.

Native AOT in diagnostics mode does not tag a control whose element's only extra is its call site (no managed inspector can load to read the tag); there the location is resolved from the control's published `ReactorSource` value and is the same [`SourceLocation`](../components/SourceLocation.md) the tag would give.

Menu and command-bar items a flyout, menu bar or command bar builds from item records (`MenuItem(…)`, `AppBarButton(…)`) carry no element tag; in diagnostics mode their location is resolved the same way, from their published value.

```csharp
public static SourceLocation? GetSource(UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |
