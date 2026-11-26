---
title: "Diagnostic snapshots"
description: "Snapshots of the component tree that the diagnostics API returns."
---

**Learn more:** [Diagnostics](/microsoft-ui-reactor/main/tools/diagnostics)

## `AppliedProperty` [#appliedproperty]

A WinUI property Reactor writes on a control because the element that produced it sets a common modifier, or because Reactor derived it (the default automation name).

```csharp
public sealed record AppliedProperty(string Modifier, string Property, string Value)
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Constructors [#constructors]

#### `AppliedProperty` [#appliedproperty_1]

A WinUI property Reactor writes on a control because the element that produced it sets a common modifier, or because Reactor derived it (the default automation name).

```csharp
public AppliedProperty(string Modifier, string Property, string Value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Modifier` | `string` | The modifier that carries the value: `"Width"`, `"IsVisible"`, `"Accessibility.HelpText"`, or `"DefaultAutomationName"` for the name Reactor derives from a control's caption. |
| `Property` | `string` | The WinUI property it is written to, as `OwnerType.PropertyName`: `"FrameworkElement.Width"`, `"UIElement.Visibility"`, `"AutomationProperties.Name"`. |
| `Value` | `string` | The element-side value as text. It can differ from the property value when Reactor converts it (`IsVisible` → `Visibility`) or folds several modifiers into one property (inline margins into `Margin`). |

### Properties [#properties]

#### `Modifier` [#modifier]

The modifier that carries the value: `"Width"`, `"IsVisible"`, `"Accessibility.HelpText"`, or `"DefaultAutomationName"` for the name Reactor derives from a control's caption.

```csharp
public string Modifier  // get, init
```

#### `Property` [#property]

The WinUI property it is written to, as `OwnerType.PropertyName`: `"FrameworkElement.Width"`, `"UIElement.Visibility"`, `"AutomationProperties.Name"`.

```csharp
public string Property  // get, init
```

#### `Value` [#value]

The element-side value as text. It can differ from the property value when Reactor converts it (`IsVisible` → `Visibility`) or folds several modifiers into one property (inline margins into `Margin`).

```csharp
public string Value  // get, init
```

## `ComponentSnapshot` [#componentsnapshot]

A point-in-time description of one mounted component, for inspectors. Every value is text: the snapshot holds no reference into the component, so it can be kept, serialized or sent across a process boundary.

```csharp
public sealed record ComponentSnapshot(string Name, string Kind, bool IsRoot, IReadOnlyList<DiagnosticValue> Props, IReadOnlyList<DiagnosticValue> State, IReadOnlyList<DiagnosticValue> Contexts)
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Constructors [#constructors_1]

#### `ComponentSnapshot` [#componentsnapshot_1]

A point-in-time description of one mounted component, for inspectors. Every value is text: the snapshot holds no reference into the component, so it can be kept, serialized or sent across a process boundary.

```csharp
public ComponentSnapshot(string Name, string Kind, bool IsRoot, IReadOnlyList<DiagnosticValue> Props, IReadOnlyList<DiagnosticValue> State, IReadOnlyList<DiagnosticValue> Contexts)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` | The component's name: its class (`Counter`) or, for a render function, where the function is defined (`RenderEachTime in MainPage.Render`, `Memo in MainPage.Render`, `render in App.Main` for a host root). |
| `Kind` | `string` | `"class"`, `"function"` or `"memo"`. |
| `IsRoot` | `bool` | True for a host's root component (the one passed to `Mount`). |
| `Props` | `IReadOnlyList<DiagnosticValue>` | The props of a `Component<TProps>` (or the props carried by its element), one row per public property, or one row named `Props` for a scalar — or for a props type whose members were trimmed away (NativeAOT), described as `<Type> (members unavailable)`. Empty for function components and propless classes. |
| `State` | `IReadOnlyList<DiagnosticValue>` | The component's hooks in call order. [`DiagnosticValue.Index`](#index) is the index [`ReactorDiagnostics.TrySetState`](#trysetstate) takes. |
| `Contexts` | `IReadOnlyList<DiagnosticValue>` | The contexts the component reads through `UseContext`. |

### Properties [#properties_1]

#### `Contexts` [#contexts]

The contexts the component reads through `UseContext`.

```csharp
public IReadOnlyList<DiagnosticValue> Contexts  // get, init
```

#### `IsRoot` [#isroot]

True for a host's root component (the one passed to `Mount`).

```csharp
public bool IsRoot  // get, init
```

#### `Kind` [#kind]

`"class"`, `"function"` or `"memo"`.

```csharp
public string Kind  // get, init
```

#### `Name` [#name]

The component's name: its class (`Counter`) or, for a render function, where the function is defined (`RenderEachTime in MainPage.Render`, `Memo in MainPage.Render`, `render in App.Main` for a host root).

```csharp
public string Name  // get, init
```

#### `Props` [#props]

The props of a `Component<TProps>` (or the props carried by its element), one row per public property, or one row named `Props` for a scalar — or for a props type whose members were trimmed away (NativeAOT), described as `<Type> (members unavailable)`. Empty for function components and propless classes.

```csharp
public IReadOnlyList<DiagnosticValue> Props  // get, init
```

#### `State` [#state]

The component's hooks in call order. [`DiagnosticValue.Index`](#index) is the index [`ReactorDiagnostics.TrySetState`](#trysetstate) takes.

```csharp
public IReadOnlyList<DiagnosticValue> State  // get, init
```

## `DiagnosticValue` [#diagnosticvalue]

One prop, hook or context value of a [`ComponentSnapshot`](#componentsnapshot), as text.

```csharp
public sealed record DiagnosticValue(int Index, string Name, string Kind, string Type, string Value, bool Editable, bool MigratedByHotReload)
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Constructors [#constructors_2]

#### `DiagnosticValue` [#diagnosticvalue_1]

One prop, hook or context value of a [`ComponentSnapshot`](#componentsnapshot), as text.

```csharp
public DiagnosticValue(int Index, string Name, string Kind, string Type, string Value, bool Editable, bool MigratedByHotReload)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Index` | `int` | Position in its list. For [`ComponentSnapshot.State`](#state) and [`ComponentSnapshot.Contexts`](#contexts) it is the hook's call-order index. |
| `Name` | `string` | The prop's name, or the context's declaration name (`Context<T>` when it has none). Empty for hooks: hook names are not recorded. |
| `Kind` | `string` | For a prop, `"prop"`. For a hook, `"state"`, `"reducer"`, `"ref"`, `"memo"`, `"effect"`, `"context"`, `"persisted"`, `"navigationLifecycle"` or `"unknown"`. For a context, `"default"` when the value the component read equals the context's default value (no provider, or a provider supplying that same value), otherwise `"provided"`. |
| `Type` | `string` | The value's type as C# spells it (`int`, `int?`, `List<TaskItem>`). Nullable reference annotations are not visible at runtime, so a `string?` reads `string`. Empty when the hook has no value. |
| `Value` | `string` | The value as text: strings quoted, collections summarised as `List<T> (n items)` (`(count unknown)` for a sequence that does not advertise its count — it is never enumerated), long values cut at 200 characters. Values are not redacted: an inspector that shows them where a secret must not appear hides them itself. Empty when the hook has no value (effects). |
| `Editable` | `bool` | True when [`ReactorDiagnostics.TrySetState`](#trysetstate) can set it from text: a state, reducer or persisted hook of a text-typed value. |
| `MigratedByHotReload` | `bool` | For hooks: true when the most recent hot-reload pass migrated this value to an edited type. Always false for props and contexts. |

### Properties [#properties_2]

#### `Editable` [#editable]

True when [`ReactorDiagnostics.TrySetState`](#trysetstate) can set it from text: a state, reducer or persisted hook of a text-typed value.

```csharp
public bool Editable  // get, init
```

#### `Index` [#index]

Position in its list. For [`ComponentSnapshot.State`](#state) and [`ComponentSnapshot.Contexts`](#contexts) it is the hook's call-order index.

```csharp
public int Index  // get, init
```

#### `Kind` [#kind_1]

For a prop, `"prop"`. For a hook, `"state"`, `"reducer"`, `"ref"`, `"memo"`, `"effect"`, `"context"`, `"persisted"`, `"navigationLifecycle"` or `"unknown"`. For a context, `"default"` when the value the component read equals the context's default value (no provider, or a provider supplying that same value), otherwise `"provided"`.

```csharp
public string Kind  // get, init
```

#### `MigratedByHotReload` [#migratedbyhotreload]

For hooks: true when the most recent hot-reload pass migrated this value to an edited type. Always false for props and contexts.

```csharp
public bool MigratedByHotReload  // get, init
```

#### `Name` [#name_1]

The prop's name, or the context's declaration name (`Context<T>` when it has none). Empty for hooks: hook names are not recorded.

```csharp
public string Name  // get, init
```

#### `Type` [#type]

The value's type as C# spells it (`int`, `int?`, `List<TaskItem>`). Nullable reference annotations are not visible at runtime, so a `string?` reads `string`. Empty when the hook has no value.

```csharp
public string Type  // get, init
```

#### `Value` [#value_1]

The value as text: strings quoted, collections summarised as `List<T> (n items)` (`(count unknown)` for a sequence that does not advertise its count — it is never enumerated), long values cut at 200 characters. Values are not redacted: an inspector that shows them where a secret must not appear hides them itself. Empty when the hook has no value (effects).

```csharp
public string Value  // get, init
```

## `KeyedListDiagnostic` [#keyedlistdiagnostic]

One captured keyed-list key diagnostic: a null key, or duplicate keys in a keyed list or among a panel's `.WithKey` siblings. A list with duplicate keys is rebuilt in full when it changes (a bailout); a null-keyed item is matched by position and the list keeps diffing incrementally. Reported at mount as well as on update. Immutable record so the dev tools overlay can display an arbitrary subset without locking the producer.

```csharp
public sealed record KeyedListDiagnostic(DateTime TimestampUtc, string? ControlContext, KeyedListDiagnosticKind Kind, IReadOnlyList<string> SampleKeys, int Count)
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Constructors [#constructors_3]

#### `KeyedListDiagnostic` [#keyedlistdiagnostic_1]

One captured keyed-list key diagnostic: a null key, or duplicate keys in a keyed list or among a panel's `.WithKey` siblings. A list with duplicate keys is rebuilt in full when it changes (a bailout); a null-keyed item is matched by position and the list keeps diffing incrementally. Reported at mount as well as on update. Immutable record so the dev tools overlay can display an arbitrary subset without locking the producer.

```csharp
public KeyedListDiagnostic(DateTime TimestampUtc, string? ControlContext, KeyedListDiagnosticKind Kind, IReadOnlyList<string> SampleKeys, int Count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `TimestampUtc` | `DateTime` | When the diagnostic was first observed. |
| `ControlContext` | `string?` | Human-readable label — the host control's type name. May be `null` when the diff ran outside a control context (e.g. direct unit-test invocation of `KeyedListDiff.Apply`). |
| `Kind` | [`KeyedListDiagnosticKind`](#keyedlistdiagnostickind) | Which class of key problem was found (null key vs. duplicate keys). |
| `SampleKeys` | `IReadOnlyList<string>` | A small, bounded set of representative offending keys (duplicates that collided, or the literal token `"<null>"` for the null-key case). Capped to keep large lists from blowing the record's footprint. |
| `Count` | `int` | Number of times this same (control, kind, sample-set) has fired since process start, including the first. Lets the dev overlay show "fired 1× / 12× / 134×" without spamming a fresh entry per render. |

### Properties [#properties_3]

#### `CallSite` [#callsite]

The C# call site of the list factory, or of the first duplicated child for sibling keys, when the source map recorded one (Debug builds by default); otherwise `null`.

```csharp
public SourceLocation? CallSite  // get, init
```

#### `ComponentName` [#componentname]

The component whose render produced the list or panel, when known. Reactor knows it while an inspector is attached (`REACTOR_DIAGNOSTICS=1`); otherwise `null`.

```csharp
public string? ComponentName  // get, init
```

#### `ControlContext` [#controlcontext]

Human-readable label — the host control's type name. May be `null` when the diff ran outside a control context (e.g. direct unit-test invocation of `KeyedListDiff.Apply`).

```csharp
public string? ControlContext  // get, init
```

#### `Count` [#count]

Number of times this same (control, kind, sample-set) has fired since process start, including the first. Lets the dev overlay show "fired 1× / 12× / 134×" without spamming a fresh entry per render.

```csharp
public int Count  // get, init
```

#### `Kind` [#kind_2]

Which class of key problem was found (null key vs. duplicate keys).

```csharp
public KeyedListDiagnosticKind Kind  // get, init
```

#### `SampleKeys` [#samplekeys]

A small, bounded set of representative offending keys (duplicates that collided, or the literal token `"<null>"` for the null-key case). Capped to keep large lists from blowing the record's footprint.

```csharp
public IReadOnlyList<string> SampleKeys  // get, init
```

#### `TimestampUtc` [#timestamputc]

When the diagnostic was first observed.

```csharp
public DateTime TimestampUtc  // get, init
```

## `KeyedListDiagnosticKind` [#keyedlistdiagnostickind]

Kind of structural list diagnostic surfaced through [`ReactorDiagnostics`](#reactordiagnostics). Tracked separately so the dev tools overlay can filter and color by severity-of-cause without parsing the message string.

```csharp
public enum KeyedListDiagnosticKind
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `NullKey` | `0` | A `KeySelector` / `IReactorKeyed.Key` returned `null`. |
| `DuplicateKey` | `1` | Two or more items in the new list returned the same key. |

## `ReactorDiagnostics` [#reactordiagnostics]

Process-wide diagnostic collector for structural problems the reconciler can detect at runtime (today: keyed-list bailouts). Read-only surface for agents, tests, and the in-app dev overlay; the producer side is `internal`.

```csharp
public static class ReactorDiagnostics
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Remarks [#remarks]

The collector deduplicates by (control instance, kind, hashed sample keys). The first occurrence is logged in full; subsequent occurrences of the same triple bump a counter rather than appending a fresh entry. This matches the spec 042 §9 Q1 resolution ("warn-and-bailout via a one-shot diagnostic") and avoids drowning the dev overlay during a run-away render loop.

The buffer is bounded to [`ReactorDiagnostics.MaxRecentEntries`](#maxrecententries); once full, the oldest entry is dropped. This is intentionally generous — keyed-list bailouts are a per-app rarity, not a per-frame event.

Reads are lock-free against a snapshot; writes take a short lock.

### Properties [#properties_4]

#### `IsSourcePublishingEnabled` [#issourcepublishingenabled]

True when Reactor is publishing [`ReactorDiagnostics.SourceProperty`](#sourceproperty) in this process: the `Reactor.DevtoolsSupport` build switch is on and the process was started with `REACTOR_DIAGNOSTICS=1`.

```csharp
public static bool IsSourcePublishingEnabled  // get
```

#### `RecentKeyedListWarnings` [#recentkeyedlistwarnings]

Recent keyed-list key diagnostics (see [`KeyedListDiagnostic`](#keyedlistdiagnostic)), newest first. Returns an independent snapshot — safe to enumerate while producers continue running. Empty when no key problem has ever been found (the common case).

```csharp
public static IReadOnlyList<KeyedListDiagnostic> RecentKeyedListWarnings  // get
```

#### `SourceProperty` [#sourceproperty]

Attached string property, registered as `"ReactorSource"`, that Reactor sets on every control it realizes while diagnostics publishing is on. Its value describes where the control came from — call site, owning component, element kind, key, declared name, and for component boundaries the component's hooks — in the v1 grammar:

```csharp
v=1|at=path:line[:col][|rel=<root|0>]|owner=Comp|element=Kind[|mounts=Comp][|root=Comp][|key=k][|name=n][|hooks=i:name@line;...]
```

with `%` escaped as `%25` and `|` as `%7C` in values. `at` is never an absolute developer path: it is relative to the project directory, or to the solution/repo root (`rel=root`), or just the file name (`rel=0`) — the same posture as XAML's runtime source info, which reports `ms-appx:///` paths.

**Why a string on the control.** An out-of-process inspector reads it through XamlDiagnostics like any other property (it is reported as `.ReactorSource`), with no managed code in the app — so it works for Native AOT apps, where no managed inspector agent can be loaded.

**When it is set.** Only when the app was built with the `Reactor.DevtoolsSupport` switch on AND launched with `REACTOR_DIAGNOSTICS=1`; see [`ReactorDiagnostics.IsSourcePublishingEnabled`](#issourcepublishingenabled). Otherwise no control carries it and the publishing code is trimmed from AOT/trimmed builds. Reading this property registers the attached property (once) but publishes nothing.

```csharp
public static DependencyProperty SourceProperty  // get
```

### Methods [#methods]

#### `ClearRecentKeyedListWarnings` [#clearrecentkeyedlistwarnings]

Drop every captured entry and contextual dedup state, retaining per-control counts. Tests use this between scenarios; production code never needs to call it.

```csharp
public static void ClearRecentKeyedListWarnings()
```

#### `DescribeComponent` [#describecomponent]

Describes the component hosted at `element`: a component's wrapper `Border` (every class, function and memo component is mounted into one), or a host's root content (its rendered root control, the window content or `ContentTarget` it was installed into, or a `ReactorHostControl` itself). Returns `null` for any other element. Works with or without source mapping.

```csharp
public static ComponentSnapshot? DescribeComponent(UIElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `UIElement` |  |

**Remarks**

Must be called on the UI thread.

One element can anchor two components: when a root renders a component directly (`Render() => Component<Shell>()`), the root's rendered control *is* `Shell`'s wrapper. The wrapper wins — the element describes `Shell`, the innermost component it hosts — and [`ReactorDiagnostics.TrySetState`](#trysetstate) / [`ReactorDiagnostics.Rerender`](#rerender) address the same component. The root stays reachable through its other anchors: a `ContentTarget`, a `ReactorHostControl`, or the dev-overlay wrapper. A host that installs straight into `Window.Content` has none of those, so in that one shape its root is not reachable.

#### `GetAppliedProperties` [#getappliedproperties]

The WinUI properties Reactor writes on `control` from the common modifiers of the element that produced it, so an inspector can mark a property as set in C# (and overwritten when the element's value changes). Empty when the control is not tagged with its element — Reactor tags only controls something reads back (callbacks, a key, or a stamped source location), so turn on [`ReactorSourceMap.Enabled`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceMap#enabled) for full coverage.

```csharp
public static IReadOnlyList<AppliedProperty> GetAppliedProperties(UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |

**Remarks**

Covers [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) and [`AccessibilityModifiers`](/microsoft-ui-reactor/main/reference/modifiers/AccessibilityModifiers), plus the `AutomationProperties.Name` Reactor derives from a captioned control (`Button("Save")`, `CheckBox("Agree")`, …) when the author sets none. That one is reported as `"DefaultAutomationName"`, and only while the live name still equals it; a different live name was written by the app. Properties a control's own element record sets (`TextBlock("hi")` → `Text`) are not listed. Must be called on the UI thread.

#### `GetHosts` [#gethosts]

Snapshot of every live Reactor host in the process, in creation order: windows opened through `ReactorApp` (including ones not yet mounted), hosts created with `new ReactorHost(window)`, and [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) islands. Disposed hosts are excluded. Safe to call from any thread; see [`ReactorHostInfo`](#reactorhostinfo) for consistency rules.

```csharp
public static IReadOnlyList<ReactorHostInfo> GetHosts()
```

**Remarks**

Provisional diagnostics API for external inspectors. A [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) that was removed from the tree but never disposed stays listed until it is collected — Reactor cannot tell a transient reparent from a removal (issue #344); check its `XamlRoot` to skip detached islands.

#### `GetReferenceEdges` [#getreferenceedges]

The references `control` declares to other controls — including pending ones whose target has not mounted yet, for which the WinUI property itself (e.g. `XYFocusRight`, `AutomationProperties.LabeledBy`) still reads null. Grouped by property in a fixed order (control-descriptor references, then bindings, then the modifier references), then list position; both `LabeledBy` forms are adjacent, the `ElementRef` one first.

```csharp
public static IReadOnlyList<ReferenceEdgeSnapshot> GetReferenceEdges(UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |

**Remarks**

`ElementRef` references (modifiers such as `.LabeledBy(ref)` or `.XYFocusRight(ref)`, control descriptors and bindings) are always reported. An AutomationId `.LabeledBy("id")` is reported while pending; once resolved it is reported only while the control is loaded and tagged with its element (turn source mapping on). Reads existing reconciler state on demand. Must be called on the UI thread.

#### `GetSource` [#getsource]

Reads [`ReactorDiagnostics.SourceProperty`](#sourceproperty) from `element`; `null` when unset.

```csharp
public static string? GetSource(DependencyObject element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `DependencyObject` |  |

#### `Rerender` [#rerender]

Schedules a re-render of the component hosted at `element`, bypassing its memoization (the path a state setter takes).

```csharp
public static bool Rerender(UIElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `UIElement` |  |

**Returns:** False when no component is hosted at `element` or it has never rendered.

**Remarks**

A re-render diffs the new element tree against the previous one, so it re-applies a property only when the element's value for it changed: it does **not** restore a property that was edited directly on the control while the element's value stayed the same. Must be called on the UI thread that renders the component.

#### `TrySetState` [#trysetstate]

Development-time state edit: parses `text` to the value type of hook `hookIndex` of the component hosted at `element`, writes it, and — when the value changed — schedules a re-render exactly as the hook's own setter would.

```csharp
public static bool TrySetState(UIElement element, int hookIndex, string text, out string? error)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `UIElement` | A component wrapper or host root, as for [`ReactorDiagnostics.DescribeComponent`](#describecomponent). |
| `hookIndex` | `int` | The hook's [`DiagnosticValue.Index`](#index). |
| `text` | `string` | Invariant-culture text: a number, a single character, `true`/`false`, an enum member name, a date, time span or GUID, or the string itself (unquoted). `null` sets a string or nullable to null. |
| `error` | `string?` | Why the edit was refused, when this returns false. |

**Returns:** True when the value was written (including when it was already equal).

**Remarks**

Only state, reducer and persisted hooks of a text-typed value can be set. Must be called on the UI thread that renders the component.

### Fields [#fields]

#### `MaxRecentEntries` [#maxrecententries]

Maximum number of distinct (control, kind, sample-set) entries retained. New entries beyond this drop the oldest.

```csharp
public const int MaxRecentEntries = 64
```

#### `MaxSampleKeys` [#maxsamplekeys]

Maximum number of representative keys captured per entry. Larger duplicate sets are truncated and an ellipsis is appended.

```csharp
public const int MaxSampleKeys = 8
```

## `ReactorHostInfo` [#reactorhostinfo]

Point-in-time description of one live Reactor host: where it renders, what its root is, and where that root was mounted. Returned by [`ReactorDiagnostics.GetHosts`](#gethosts).

```csharp
public sealed class ReactorHostInfo
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Remarks [#remarks_1]

Provisional diagnostics API for external inspectors (for example `winapp devtools`); shape may change while Reactor is in preview.

Deliberately exposes no reconciler internals: the root is described by name and call site, and reached on screen through [`ReactorHostInfo.RootControl`](#rootcontrol) — the same WinUI control an inspector already walks. Everything else is an existing public WinUI or Reactor hosting type.

A snapshot, not a live view: re-query after a render, window open, or close. The snapshot itself can be taken from any thread, and every value in it is read atomically (never half of one mount and half of another). A snapshot taken while the UI thread is remounting may still pair the outgoing root's name with the incoming root's site; take it on the UI thread when the values must agree with each other or with the visual tree you are about to walk. Touching the returned WinUI objects is subject to their usual UI-thread rules.

Holding a snapshot keeps its host, window and root control alive; the registry behind [`ReactorDiagnostics.GetHosts`](#gethosts) does not.

### Properties [#properties_5]

#### `Host` [#host]

The window host, when [`ReactorHostInfo.Kind`](#kind_3) is [`ReactorHostKind.WindowHost`](#values_1).

```csharp
public ReactorHost? Host  // get
```

#### `HostControl` [#hostcontrol]

The embedded host control, when [`ReactorHostInfo.Kind`](#kind_3) is [`ReactorHostKind.HostControl`](#values_1).

```csharp
public ReactorHostControl? HostControl  // get
```

#### `HostElement` [#hostelement]

The element whose content is the Reactor root: the [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) itself, a window host's [`ReactorHost.ContentTarget`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#contenttarget), or null when a window host renders straight into `Window.Content`.

```csharp
public FrameworkElement? HostElement  // get
```

#### `Kind` [#kind_3]

Which host shape this is.

```csharp
public ReactorHostKind Kind  // get
```

#### `MountSite` [#mountsite]

Where the root was mounted — the `ReactorApp.Run`, `ReactorApp.OpenWindow`, `ReactorWindow.Mount`, `ReactorHost.Mount` or `ReactorHostControl.Mount` call in app code. Null unless the calling project was built with source mapping (`ReactorSourceMap=true`, the Debug default) and [`ReactorSourceMap.Enabled`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceMap#enabled) was true at mount time; also null for a root created from [`ReactorHostControl.ComponentFactory`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl#componentfactory) or [`ReactorHostControl.ComponentType`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl#componenttype), or mounted by framework code, none of which has a call site in app code.

```csharp
public SourceLocation? MountSite  // get
```

#### `ReactorWindow` [#reactorwindow]

The `ReactorApp` window that owns the host (its `Id`, `Key` and `NativeWindow` identify it), or null for a host constructed directly on a window and for every [`ReactorHostKind.HostControl`](#values_1).

```csharp
public ReactorWindow? ReactorWindow  // get
```

#### `RootComponentName` [#rootcomponentname]

The root component's type name, namespace-qualified, with nested types joined by `.` and generic arguments spelled out (`MyApp.Pages.Dashboard`, `MyApp.ListPage<MyApp.Order>`). Null for a render-function root and before the first mount.

```csharp
public string? RootComponentName  // get
```

#### `RootControl` [#rootcontrol]

The control the root currently renders as, or null before the first render. This is the control the host places in `Window.Content` / its content slot; when a dev overlay is on, that slot holds an overlay wrapper and this control is its child. After a render error it is the error fallback panel.

```csharp
public UIElement? RootControl  // get
```

#### `RootRenderFunctionName` [#rootrenderfunctionname]

A readable name for a render-function root: the method that declares it, with compiler-generated names undone — `MyApp.Program.Main (lambda)`, `MyApp.Program.Main.Render` for a local function, `MyApp.Shell.Render` for a method group. Null for a component root, before the first mount, and when the runtime cannot describe the delegate (for example NativeAOT with stack-trace data disabled). For a lambda, [`ReactorHostInfo.MountSite`](#mountsite) is usually the better identity.

```csharp
public string? RootRenderFunctionName  // get
```

#### `Window` [#window]

The WinUI window a [`ReactorHostKind.WindowHost`](#values_1) renders into. Null for [`ReactorHostKind.HostControl`](#values_1), which does not own a window; use [`ReactorHostInfo.HostElement`](#hostelement)'s `XamlRoot` to place it.

```csharp
public Window? Window  // get
```

## `ReactorHostKind` [#reactorhostkind]

Which kind of Reactor host a [`ReactorHostInfo`](#reactorhostinfo) describes.

```csharp
public enum ReactorHostKind
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `WindowHost` | `0` | A [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) rendering into a WinUI [`Window`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window) — either a window opened through `ReactorApp.Run` / `ReactorApp.OpenWindow` (see [`ReactorHostInfo.ReactorWindow`](#reactorwindow)) or a `new ReactorHost(window)` on any window, optionally redirected into a [`ReactorHost.ContentTarget`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#contenttarget). |
| `HostControl` | `1` | A [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) embedded in a XAML tree (a hybrid "island"). |

## `ReferenceEdgeSnapshot` [#referenceedgesnapshot]

One reference a control declares to another control — resolved, or still pending because the target has not mounted.

```csharp
public sealed record ReferenceEdgeSnapshot(string Property, bool IsList, int Index, string? TargetAutomationId, string? ExpectedTargetTypeName, bool IsResolved, UIElement? Target)
```

Namespace: `Microsoft.UI.Reactor.Core.Diagnostics`.

### Constructors [#constructors_4]

#### `ReferenceEdgeSnapshot` [#referenceedgesnapshot_1]

One reference a control declares to another control — resolved, or still pending because the target has not mounted.

```csharp
public ReferenceEdgeSnapshot(string Property, bool IsList, int Index, string? TargetAutomationId, string? ExpectedTargetTypeName, bool IsResolved, UIElement? Target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Property` | `string` | The referencing property: `"LabeledBy"`, `"DescribedBy"`, `"FlowsTo"`, `"FlowsFrom"`, `"XYFocusUp"`, `"XYFocusDown"`, `"XYFocusLeft"`, `"XYFocusRight"` or `"ToolTipPlacementTarget"`. A control descriptor's or binding's reference carries no author-visible name and reports `"reference#N"` / `"binding#N"`. |
| `IsList` | `bool` | True for a list-valued reference (`DescribedBy`, `FlowsTo`, `FlowsFrom`, a descriptor's `ReferenceList`); each target is its own entry. |
| `Index` | `int` | Position of this target within a list-valued reference (0 for a scalar one), counting references only: a null entry in the authored list is not a reference and is skipped. |
| `TargetAutomationId` | `string?` | The AutomationId the author passed to `.LabeledBy("id")`; `null` for an `ElementRef` reference. |
| `ExpectedTargetTypeName` | `string?` | For a typed `ElementRef<T>`, `T` as C# spells it (`Button`); otherwise `null`. |
| `IsResolved` | `bool` | True when the target is mounted. |
| `Target` | `UIElement?` | The mounted control the reference resolves to; `null` while pending. Unlike the rest of the diagnostics surface this is a live WinUI control, not text — use it in-process, on the UI thread, to locate the target (e.g. to inspect or highlight it). |

### Properties [#properties_6]

#### `ExpectedTargetTypeName` [#expectedtargettypename]

For a typed `ElementRef<T>`, `T` as C# spells it (`Button`); otherwise `null`.

```csharp
public string? ExpectedTargetTypeName  // get, init
```

#### `Index` [#index_1]

Position of this target within a list-valued reference (0 for a scalar one), counting references only: a null entry in the authored list is not a reference and is skipped.

```csharp
public int Index  // get, init
```

#### `IsList` [#islist]

True for a list-valued reference (`DescribedBy`, `FlowsTo`, `FlowsFrom`, a descriptor's `ReferenceList`); each target is its own entry.

```csharp
public bool IsList  // get, init
```

#### `IsResolved` [#isresolved]

True when the target is mounted.

```csharp
public bool IsResolved  // get, init
```

#### `Property` [#property_1]

The referencing property: `"LabeledBy"`, `"DescribedBy"`, `"FlowsTo"`, `"FlowsFrom"`, `"XYFocusUp"`, `"XYFocusDown"`, `"XYFocusLeft"`, `"XYFocusRight"` or `"ToolTipPlacementTarget"`. A control descriptor's or binding's reference carries no author-visible name and reports `"reference#N"` / `"binding#N"`.

```csharp
public string Property  // get, init
```

#### `Target` [#target]

The mounted control the reference resolves to; `null` while pending. Unlike the rest of the diagnostics surface this is a live WinUI control, not text — use it in-process, on the UI thread, to locate the target (e.g. to inspect or highlight it).

```csharp
public UIElement? Target  // get, init
```

#### `TargetAutomationId` [#targetautomationid]

The AutomationId the author passed to `.LabeledBy("id")`; `null` for an `ElementRef` reference.

```csharp
public string? TargetAutomationId  // get, init
```
