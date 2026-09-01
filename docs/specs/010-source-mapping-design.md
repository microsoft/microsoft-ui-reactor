# Reactor Source Mapping — Design Spec

Map every running UI element back to the C# source that created it, enabling
"go to definition" from the `--preview` dev tools to the user's IDE.

---

## Status

**Shipped (2026-08-31)** via **Approach 3 (Roslyn interceptors)** — not Approach 1,
which this spec originally recommended. This document is retained as the design
record and has been **rewritten as-built**; the sections below carry inline
amendments where the original text is wrong.

| PR | Commit | Scope |
|---|---|---|
| [#1147](https://github.com/microsoft/microsoft-ui-reactor/pull/1147) | `33a35999` | Per-element source mapping via Roslyn interceptors |
| [#1161](https://github.com/microsoft/microsoft-ui-reactor/pull/1161) | `27ed6662` | Helper attribution + converted-argument stamping |

> **What shipped, in one place.** For usage documentation see
> [`docs/guide/source-mapping.md`](../guide/source-mapping.md), which is the
> living reference; this spec records *why* the design ended up here.
>
> | Surface | Location |
> |---|---|
> | `SourceLocation(string FilePath, int LineNumber)` | `src/Reactor/Core/SourceLocation.cs` |
> | `Element.CallSite` | `src/Reactor/Core/Element.cs` |
> | `ReactorSourceMap.Enabled` / `.GetSource(UIElement)` | `src/Reactor/Diagnostics/ReactorSourceMap.cs` |
> | `[ReactorSourceTransparent]` | `src/Reactor/Diagnostics/ReactorSourceTransparentAttribute.cs` |
> | Interceptor generator (1,169 lines) | `src/Reactor.SourceMap.Generator/` |
> | `REACTOR_SOURCEMAP_001` | emitted when a `[ReactorSourceTransparent]` helper is not forwardable |
>
> **On citations.** This document cites code by **file and symbol name**, never by
> line number. `Element.cs` alone is >7,600 lines and moved every previously-cited
> line by hundreds when source mapping landed; a line number in a long-lived design
> record is stale the moment the next PR merges. Every symbol named here is
> greppable.
>
> **Two independent gates**, which is a refinement on this spec's single
> `#if DEBUG` idea:
>
> 1. **Build gate** — the `<ReactorSourceMap>` MSBuild property decides whether
>    interceptors are generated at all. On by default in Debug, off in Release,
>    mirroring how WPF gates XAML source info behind `XamlDebuggingInformation`.
>    Setting it `true` in Release does work, and embeds source paths.
> 2. **Runtime gate** — `ReactorSourceMap.Enabled` decides whether those
>    interceptors stamp anything. Defaults to **false even in Debug**; seeded from
>    `REACTOR_SOURCEMAP` (exactly `"1"`), settable by a host, and turned on by the
>    devtools verb.
>
> So a plain Debug build generates interceptors but stamps nothing until something
> enables it.

### How the original design fared

The *problem statement*, *design goals*, and *Approaches 2, 4, 5, 6* stand.
Several concrete recommendations did not survive implementation — following this
document as originally written produces a feature covering 79% of the DSL that is
silently wrong after any reformat and source-breaking for consumers.

Two throwaway spikes were built and measured head-to-head before implementation,
one per candidate mechanism (Approach 1 and Approach 3). The spike branches were
not retained — the measurements they produced are recorded inline below and in
[Measured results](#measured-results-2026-08-26), which is the durable record:

| Section | Original claim | Outcome |
|---|---|---|
| **Approach 1** (recommended) | CallerInfo on every factory | **Rejected.** Cannot reach 40 of 189 factories — `params` forbids trailing optional parameters, and no workaround exists |
| **Approach 3** (dismissed) | "Source generators cannot rewrite existing code" | **Outdated, and now shipped.** C# interceptors do exactly that and are stable in C# 14 / .NET 10 |
| **§1.1** | `public SourceLocation? Source { get; init; }` | **Does not compile** — six collision sites. Shipped as `CallSite` |
| **§1.3 / §1.4** | New attached DP + mount-path write | **Redundant, not implemented** — `Reconciler.ReactorState.Element` already carries the back-pointer |
| **Record equality note** | "must be verified during implementation" | **Real — needed three mitigations.** An earlier amendment called it a non-issue; that was wrong. See the note in Implementation Scope |
| **Goal 1** ("zero production cost") | `#if DEBUG` strips everything | **Unachievable as written** — replaced by the two-gate model above plus bucketed storage |
| **Future Extensions** — source map file | keyed on "element type + key" | **Under-determined** — keys cover ~3% of call sites and are scoped to dynamic list items |
| **Future Extensions** — helper attribution | "not in scope for the initial implementation" | **Solved in #1161** via `[ReactorSourceTransparent]` |

Two limitations that *both* spikes measured as unfixable were subsequently closed
by #1161 — see [Measured results](#measured-results-2026-08-26). Treat the spike
numbers there as a snapshot of the decision, not as the current limitation set.

---

## Problem

When inspecting a live Microsoft.UI.Reactor (Reactor) app (via `--preview` mode or a future element
inspector), there is no way to know *which line of user code* caused a given
`TextBlock`, `Button`, or `Grid` to appear. The reconciler creates WinUI
controls, but the connection to the DSL call site is lost. Developers need to
click an element and jump straight to the `Text("Hello")` or
`Button("Save").OnClick(...)` call that produced it.

---

## Design Goals

1. **Zero production cost** — source tracking is stripped or inert in Release builds
2. **No custom build tooling** — works with stock `dotnet build`, no Fody/Metalama/IL weaving
3. **Per-element granularity** — every element in the tree carries its creation location
4. **IDE integration** — the `--preview` server can open the source file at the correct line
5. **Extensible** — the design leaves room for future enhancements (stack-based resolution, out-of-process inspection) without breaking changes

> **Goals 1 and 2 restated (2026-08-26).**
>
> **Goal 1 cannot be met via `#if DEBUG`.** Reactor ships as the
> `Microsoft.UI.Reactor` NuGet package built in Release, so anything compiled out
> of Release does not exist for consumers at all — a consumer building Debug against
> the Release package would get nothing. The capture surface must ship
> unconditionally; only the *cost* can be conditioned. **As shipped**, the goal is
> met by three things: (a) the **build gate** (`<ReactorSourceMap>`, Debug-on /
> Release-off), so a retail build contains no interceptors at all; (b) the
> **runtime gate** (`ReactorSourceMap.Enabled`, default false even in Debug); and
> (c) **bucketed storage** in `ElementExtras`, measured at **+0.00 B/op** when
> unstamped — byte-identical to baseline.
>
> **Goal 2 is not met, and that is an accepted trade.** Interceptors require
> shipping a Roslyn generator as an analyzer and a consumer-side
> `<InterceptorsNamespaces>` opt-in. This was judged worth it because the
> alternative leaves 21% of the DSL unreachable and silently lies after a
> reformat. Note the constraint's original intent — "no Fody/Metalama/IL weaving" —
> is still honoured: interceptors are a first-party C# language feature, not a
> forked compiler or a post-build IL rewriter.

---

## Research: Approaches Investigated

Six approaches were evaluated. **Amended 2026-08-26:** the original recommendation
was Approaches 1 + 2; spike results moved it to **Approaches 3 + 2** — see the
Status block above. Approach 1 is retained in full, since its analysis is sound
and its blocker is the reason the recommendation moved.

### Approach 1: CallerInfo Attributes ⚠️ Superseded (was: Recommended)

C# provides `[CallerFilePath]` and `[CallerLineNumber]` attributes that the
compiler fills in at every call site as compile-time constants:

```csharp
public static Element Text(string content,
    [CallerFilePath] string sourceFile = "",
    [CallerLineNumber] int sourceLine = 0)
{
    return new TextElement(content) { SourceFile = sourceFile, SourceLine = sourceLine };
}
```

`Text("Hello")` is compiled to `Text("Hello", @"C:\src\MyPage.cs", 47)` — the
values are baked into IL as constants. Zero runtime cost, zero allocations.

| Aspect | Detail |
|--------|--------|
| Runtime cost | None — compiler injects constants |
| Build tooling | None — standard C# feature since 4.5 |
| Coverage | **149 of 189 factories.** The 40 `params`-bearing factories are unreachable — see below |
| Limitation | Reports the *immediate* call site — helper/wrapper methods report their own location, not the caller's |
| Limitation | **Silently stale under Hot Reload** — see [Measured results](#measured-results-2026-08-26) |
| Limitation | **Source-breaking for consumers** — optional parameters do not participate in method-group conversion, so `items.Select(TextBlock)` stops compiling |

**Used by:** NUnit/xUnit assertions, `ArgumentNullException.ThrowIfNull()`, INotifyPropertyChanged.

> **Blocker found by spike (2026-08-26): `params` factories cannot be reached.**
> C# requires `params` to be the last parameter, so the pattern below does not
> compile for `VStack`, `HStack`, `Grid`, `Flex`, `Canvas`, `WrapGrid` and the
> rest of the container family — **40 of 189 factories (21%)**, and precisely the
> ones that answer "which layout put this here". All three workarounds were built
> and measured:
>
> | Workaround | Result |
> |---|---|
> | Trailing CallerInfo after `params` | `CS0231: A params parameter must be the last parameter in a parameter list` |
> | Leading source parameters before `params` | **Declaration compiles** — then breaks every call site with `CS1503`. ~4,275 call sites across 584 files |
> | Non-`params` overload taking `Element?[]` | Legal, but overload resolution prefers the expanded `params` form, so it never binds at an existing call site |
>
> There is no acceptable workaround. This is the finding that disqualifies
> Approach 1 as the primary mechanism.

### Approach 2: WinUI Attached Properties ✅ Recommended (already satisfied — amended 2026-08-26)

> **The principle is right and the work is already done.** Storing the mapping on
> the realized control via an attached `DependencyProperty` is exactly correct —
> but Reactor **already does this**, so no new property is needed.
> `Reconciler.ReactorState` carries an `Element` back-pointer, is stored on the
> control via `ReactorAttached.StateProperty`, and is refreshed on every update by
> `SetElementTagIfNeeded`. Reading `CallSite` is:
>
> ```
> UIElement → ReactorAttached.StateProperty → ReactorState.Element → Element.CallSite
> ```
>
> Adding the `SourceInfo` DP sketched below would duplicate that and pay a
> per-control string. See §1.3 for the full amendment, including the one real
> caveat (`NeedsTag` sparsity).
>
> The "one `SetValue` per mount" cost in the table below is therefore **zero** — the
> write already happens for other reasons.

<details>
<summary>Original proposal (historical — superseded by the existing ReactorState)</summary>

During reconciliation, write the source info from the `Element` record onto the
real WinUI3 control via a custom attached `DependencyProperty`:

```csharp
public static class SourceInfo
{
    public static readonly DependencyProperty LocationProperty =
        DependencyProperty.RegisterAttached(
            "Location", typeof(string), typeof(SourceInfo),
            new PropertyMetadata(null));

    public static void SetLocation(DependencyObject obj, string value)
        => obj.SetValue(LocationProperty, value);

    public static string GetLocation(DependencyObject obj)
        => (string)obj.GetValue(LocationProperty);
}
```

This makes source locations visible to:
- The XAML Live Visual Tree in Visual Studio
- The `--preview` dev tools / element inspector
- Any future diagnostic overlay

| Aspect | Detail |
|--------|--------|
| Runtime cost | One `SetValue` per mount, negligible |
| Visibility | Readable by VS diagnostics, custom inspectors |
| Limitation | Small per-control memory overhead in debug builds |

**Used by:** WPF `VisualDiagnostics.GetXamlSourceInfo`, Snoop WPF.

</details>

> **Correction to the "Used by" line above (2026-08-26):** WPF exposes
> `System.Windows.Diagnostics.VisualDiagnostics.GetXamlSourceInfo`, but **WinUI 3
> has no public managed equivalent** — source info reaches Visual Studio over the
> XAML Diagnostics COM channel and is not callable from app code. The cited prior
> art does not transfer, which is precisely why Reactor must carry `CallSite`
> itself. (It is also the wrong shape regardless: `GetXamlSourceInfo` maps a control
> to a `.xaml` file, and Reactor has no XAML.)

### Approach 3: React-Style Compile-Time Source Transform ✅ SHIPPED (PR #1147)

React's Babel plugin injected `__source: { fileName, lineNumber }` into every
JSX element. The C# equivalent would be a Roslyn Source Generator or Fody IL
weaver that post-processes factory calls.

**Original verdict (2024, superseded):** *"Unnecessary — CallerInfo attributes
achieve the same result with no custom tooling. Source generators cannot rewrite
existing code (only add new files), so users would need to call generated
wrappers."*

> **Superseded (2026-08-26), then shipped (2026-08-31).** The premise is no longer
> true. **C# interceptors** let a source generator replace a call site without
> touching the original code — exactly the Babel-plugin model — and they are
> **stable in C# 14 / .NET 10**, which is this repo's target framework
> (`net10.0-windows10.0.22621.0`). They were preview-gated when this spec was written.
>
> A spike proved this out end to end, and PR #1147 shipped it as
> `src/Reactor.SourceMap.Generator/`:
>
> - **Full coverage — 189 of 189 factories**, including all 40 `params` and all
>   22 generic ones. Validated at scale: 5,247 interception sites in
>   `Reactor.AppTests.Host` and 1,660 in `ReactorGallery`, both compiling clean.
> - **Zero public signature churn.** `reactor.api.txt` grows by 16 lines (the new
>   type and slot); not one of the 189 factory signatures changes. Approach 1
>   changes ~170 lines and adds two pseudo-parameters to every factory in the
>   agent-facing DSL reference.
> - **Hot-reload accurate.** The location constant lives in the *generated
>   interceptor body*, so an edit re-runs the generator and EnC emits a delta.
>   Approach 1's constant is baked into the caller's IL, which EnC does not
>   re-emit when lines merely shift above it.
> - **Generic factories bind**, including `Component<T, TProps> where T : Component<TProps>, new()`.
>   Roslyn does **not** require exact constraint equality — a too-weak constraint
>   fails loudly with `CS0310` at the forwarding call, so there is no
>   silent-non-application failure mode.
>
> Costs, all gated off by default: a shipped analyzer, a **+16–22% incremental
> build** tax (wall; +20–27% on the `Csc` task), and **+312 B per stamped call site**
> while active.
>
> Two implementation notes, both of which cost the spike real time and are worth
> keeping if this generator is ever rewritten:
>
> - Take the interceptor *signature* from `IMethodSymbol.OriginalDefinition` and only
>   the *call site* from the constructed symbol. Rendering the constructed symbol's
>   substituted parameter types alongside an open type-parameter list produces a
>   non-binding interceptor whose symptom is a misleading `CS0122` accessibility error.
> - The generator must apply the compiler's `PathMap` itself, including separator
>   normalization. `[CallerFilePath]` is rewritten under `DeterministicSourcePaths`
>   (which this repo enables when `CI=true`, see `Directory.Build.props`); a
>   generator-emitted string literal is not. Without this the two mechanisms disagree
>   on the path in every CI-built binary. `SourceLocation.ToShortString` scans for
>   both separators for the same reason.
>
> **Metalama / Fody remain unnecessary** — see Approach 4. Interceptors are a
> first-party language feature and need no forked compiler.

### Approach 4: Flutter's Kernel Transformer Pattern

Flutter uses a Dart compiler transformer that modifies every `Widget`
constructor to accept a `const _Location(file, line, column)` parameter. For
C#, the equivalent would be **Metalama** (a Roslyn fork with
`ISourceTransformer`) that rewrites Element constructors transparently.

**Verdict:** Architecturally interesting but overkill. Metalama is a heavy
dependency (forked Roslyn), adds build complexity, and must track .NET version
updates. Reserve for later if CallerInfo's immediate-call-site limitation
becomes a real pain point.

### Approach 5: PDB + Runtime Stack Walking

Capture a `StackTrace` at element creation time, resolve source locations from
PDB files at runtime. Mark Reactor framework methods with
`[DebuggerNonUserCode]` so the walker skips to user code.

```csharp
var frame = new StackFrame(skipFrames: 1, fNeedFileInfo: true);
// frame.GetFileName() => "C:\src\MyPage.cs"
// frame.GetFileLineNumber() => 42
```

**Verdict:** `new StackTrace(fNeedFileInfo: true)` costs ~1ms per call —
catastrophic when creating thousands of elements per render. JIT inlining can
also elide frames. Not viable as a primary approach, but could be used as an
on-demand fallback triggered by the inspector (see Future Extensions).

**Used by:** Visual Studio debugger (ICorDebug), Sentry, dotnet-dump (ClrMD).

### Approach 6: Lazy PDB Resolution via Out-of-Process Inspector

Store only the method token + IL offset at creation time (~microseconds), defer
expensive PDB-to-source resolution to an out-of-process inspector using ClrMD
(`Microsoft.Diagnostics.Runtime`).

**Verdict:** Promising as a future enhancement for the helper-method problem.
The IL offset is cheap to capture, and the `--preview` server could resolve
locations on demand. However, it requires shipping PDBs and adds significant
implementation complexity. Better as a Phase 2 enhancement.

---

## Recommended Plan

> **Amended (2026-08-26): the mechanism changed from Approach 1 to Approach 3;
> shipped 2026-08-31.** The *shape* of the plan below survives — a
> compile-time-populated `CallSite` on the element record, surfaced to an
> inspector — but the capture mechanism is **interceptors**, and §1.3 / §1.4 were
> not implemented. Read the amendments inline in each subsection.

Combine Approaches 3 + 2: **interceptors populate the element record, and the
existing `ReactorState` back-pointer surfaces it on the control.** This is the C#
equivalent of React's Babel source transform, using a first-party language feature
instead of a custom compiler.

### Phase 1: Element Source Tracking

#### 1.1 Add `CallSite` to the Element base record

> **Amended (2026-08-26): the original name does not compile, and the storage
> location matters more than the slot.** Both spikes hit this independently.
>
> **`Element.Source` collides with six existing declarations.** Adding
> `public SourceLocation? Source { get; init; }` to the base record yields
> 3× `CS8866` + 2× `CS0108`:
>
> | Site | Declaration | Error |
> |---|---|---|
> | `Element.cs` | `ImageElement(string Source)` | CS8866 |
> | `Element.cs` | `WebView2Element(Uri? Source = null)` | CS8866 |
> | `Element.cs` | `MediaPlayerElementElement(string? Source = null)` | CS8866 |
> | `Element.cs` | `AnimatedIconElement` → `object? Source` | CS0108 |
> | `Element.cs` | `ParallaxViewElement` → `UIElement? Source` | CS0108 |
> | `ElementExtensions.cs` | `Source(this ParallaxViewElement, UIElement)` — a fluent **extension method** | An instance property shadows it during member lookup, risking `CS1955` at `el.Source(...)` call sites |
>
> The sixth site is why a rename is mandatory rather than cosmetic — working around
> the five records individually leaves it to surface later. **Use `CallSite`**
> (verified unused across `src/Reactor`, as are `SourceInfo`, `SourceSpan`, `DebugSource`).
>
> **Do not gate the slot on `#if DEBUG`.** The library ships as the
> `Microsoft.UI.Reactor` NuGet package built in Release, so a Debug-only property
> does not exist for consumers at all. Gate the *cost*, not the API.
>
> **Store it in the `ElementExtras` bucket (spec 047 §4.4), not inline.** An inline
> `Nullable<SourceLocation>` costs **+24.0 B on every `Element` in every build,
> unconditionally** — measured at 1,127,848 B/op on M12 and reproduced independently
> by both spikes, on different architectures, to the byte. Bucketing it alongside
> `Attached` / `ThemeBindings` brings the unstamped path to **+0.00 B/op**,
> byte-identical to baseline rep-for-rep. The public API is unchanged either way.
>
> Two consequences of bucketing, both benign: a stamped element has non-null
> `Extensions`, so it already satisfies `NeedsTag` (making the runtime flag relevant
> only to *unstamped* elements); and it declines the `Extensions is null` fast paths
> in `ElementFactory.cs` (the keyed-memo and component-compare arms) while the flag
> is on.

**As shipped** (`src/Reactor/Core/SourceLocation.cs`, abridged — see the file for
the full doc comments):

```csharp
// namespace Microsoft.UI.Reactor.Core

/// <summary>
/// Spec 010 — the C# source location that produced an Element.
/// Deliberately (FilePath, LineNumber) and nothing more: the greatest common
/// denominator of the two candidate providers, since CallerInfo cannot supply a
/// column. An interceptor provider knows the column and may add it additively.
/// </summary>
public readonly record struct SourceLocation(string FilePath, int LineNumber)
{
    public override string ToString() => $"{FilePath}:{LineNumber}";

    public string ToShortString()
    {
        if (string.IsNullOrEmpty(FilePath))
            return LineNumber.ToString(CultureInfo.InvariantCulture);

        // Deliberately NOT Path.GetFileName: a deterministic-build path uses '/'
        // even on Windows, and a Windows-authored path uses '\'. Scan for either.
        int slash = FilePath.LastIndexOfAny(new[] { '/', '\\' });
        string name = slash >= 0 ? FilePath.Substring(slash + 1) : FilePath;
        return $"{name}:{LineNumber}";
    }
}

public abstract record Element
{
    // ... existing properties (Key, Modifiers, Attached, etc.)

    /// <summary>Routes to the ElementExtras bucket — see amendment above.</summary>
    public SourceLocation? CallSite { get; init; }
}
```

> **Note `FilePath` semantics.** Under a deterministic build
> (`DeterministicSourcePaths`, enabled here when `CI=true`) this is the *mapped*
> path — e.g. `/_/src/Reactor/Elements/Dsl.cs` — not a local disk path. That is
> why `ToShortString` cannot use `Path.GetFileName`: it would fail to split a
> forward-slash path on Windows.

#### 1.2 Populate `CallSite` at every factory call site

> **Amended (2026-08-26): superseded by Approach 3.** The CallerInfo variant below
> is retained as historical context. It reaches only **149 of 189** factories —
> `params` blocks the other 40 (see Approach 1), and it carries the Hot Reload
> staleness and method-group breaking change documented in
> [Measured results](#measured-results-2026-08-26).
>
> **Use interceptors instead.** They require *no* signature change, so this section's
> mechanics do not apply: call sites stay exactly as written, and the generator
> stamps `CallSite` on the returned element.
>
> Note the factory count in the original text below (~40) predates significant DSL
> growth. The measured surface is **189 factories in `Dsl.cs`** — 40 `params`-bearing,
> 22 generic, the rest plain.

<details>
<summary>Original CallerInfo mechanics (historical)</summary>

Every factory method in `Dsl.cs` gets two optional trailing parameters:

```csharp
// Before:
public static TextElement Text(string content)
    => new(content);

// After:
public static TextElement Text(string content,
    [CallerFilePath] string __sourceFile = "",
    [CallerLineNumber] int __sourceLine = 0)
    => new(content) { CallSite = new(__sourceFile, __sourceLine) };
```

**Extension methods** (`ElementExtensions.cs`) that return new elements (e.g.,
`.Key()`, `.Grid()`) propagate the existing `CallSite` via `with` automatically —
no changes needed for pure modifier extensions.

</details>

Under either mechanism, `with` preserves `CallSite` through modifier chains — the
spike confirmed the stamp survives fluent chaining.

#### 1.3 ~~Add `SourceInfo` attached property for WinUI controls~~ — REDUNDANT

> **Amended (2026-08-26): do not implement this.** A control-to-Element back-pointer
> already exists and is maintained by the reconciler:
>
> ```
> UIElement → Reconciler.ReactorAttached.StateProperty → ReactorState.Element → Element.CallSite
> ```
>
> `ReactorState.Element` is a field on `Reconciler.ReactorState`, stored via the
> attached DP registered as `ReactorAttached.StateProperty`. It is refreshed on
> every update by `SetElementTagIfNeeded`, so it tracks the current render rather
> than a mount-time snapshot. `Reactor.csproj` already grants
> `<InternalsVisibleTo Include="Microsoft.UI.Reactor.Devtools" />`, so devtools can
> read it with no new public API.
>
> A second attached DP would duplicate this and add a per-control string. Reading
> `CallSite` is one DP read.
>
> **One caveat that is real:** the back-pointer is deliberately sparse. `NeedsTag`
> tags only elements with callbacks, a `Key`, `Extensions`, or
> reference modifiers — TextBlock/Border/StackPanel/Image leaves stay untagged, which
> PR #468 (`2f4f0c50`) introduced to save ~301 B/op on M12. Bucketing `CallSite` into
> `ElementExtras` (§1.1) satisfies `NeedsTag` automatically for stamped elements, so
> no gate change is needed for them; the runtime flag remains only for *unstamped*
> elements.

<details>
<summary>Original proposal (historical — do not implement)</summary>

```csharp
// Reactor/Diagnostics/SourceInfo.cs

#if DEBUG
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Diagnostics;

public static class SourceInfo
{
    public static readonly DependencyProperty LocationProperty =
        DependencyProperty.RegisterAttached(
            "Location", typeof(string), typeof(SourceInfo),
            new PropertyMetadata(null));

    public static void SetLocation(DependencyObject obj, string value)
        => obj.SetValue(LocationProperty, value);

    public static string? GetLocation(DependencyObject obj)
        => (string?)obj.GetValue(LocationProperty);
}
#endif
```

</details>

#### 1.4 ~~Write source info during reconciliation~~ — REDUNDANT

> **Amended (2026-08-26): do not implement this either.** It exists only to feed
> the §1.3 attached property, which is itself redundant. `SetElementTagIfNeeded`
> already refreshes `ReactorState.Element` on every mount and update, so
> `CallSite` is reachable from any tagged control with no mount-path change at
> all.
>
> The original text's claim that `Mount` is "a single insertion point" is
> **correct** and still worth knowing — `Reconciler.Mount` is a single public
> choke point where all four dispatch arms converge, with a common post-dispatch
> `ApplyModifiers` tail. It simply is not needed for this feature.

<details>
<summary>Original proposal (historical — do not implement)</summary>

In `Reconciler.Mount.cs`, after creating a WinUI control, set the attached
property:

```csharp
private FrameworkElement MountElement(Element element, ...)
{
    var control = /* existing mount logic */;

#if DEBUG
    if (element.Source is { } src)
        Diagnostics.SourceInfo.SetLocation(control, src.ToString());
#endif

    return control;
}
```

</details>

### Phase 2: Preview Server Integration

#### 2.1 Add source location to the element inspector endpoint

The `--preview` mode already runs a `PreviewCaptureServer` over HTTP. Add an
endpoint that returns the element tree with source locations:

```
GET /api/elements
```

Returns a JSON tree:

```json
{
  "type": "StackPanel",
  "source": "MainPage.cs:34",
  "children": [
    { "type": "TextBlock", "source": "MainPage.cs:35", "text": "Hello" },
    { "type": "Button", "source": "MainPage.cs:36", "content": "Click me" }
  ]
}
```

#### 2.2 Add "go to source" endpoint

```
POST /api/open-source
{ "file": "C:\\src\\MainPage.cs", "line": 34 }
```

This endpoint launches the IDE at the specified location. Implementation
options (in priority order):

1. **VS Code:** `code --goto {file}:{line}`
2. **Visual Studio:** `devenv /edit {file} /command "Edit.GoTo {line}"`
3. **Generic:** Use the `REACTOR_EDITOR` environment variable (same pattern as
   React's `REACT_EDITOR` / `launch-editor`)

#### 2.3 Click-to-source in the dev tools overlay

When `--preview` is running with dev tools enabled, add an inspect mode:

1. User activates inspect mode (keyboard shortcut or button in overlay)
2. Hovering an element highlights it and shows `source: MainPage.cs:34`
3. Clicking opens the source in the IDE via the `/api/open-source` endpoint

---

## Implementation Scope

> **Amended (2026-08-26)** to reflect the recommended Approach 3 path and the
> measured surface. Two rows are deleted as redundant (§1.3, §1.4); the factory
> count was ~4.7× understated.

| Item | Files affected | Effort |
|------|---------------|--------|
| `SourceLocation` struct + `Element.CallSite`, bucketed into `ElementExtras` | `SourceLocation.cs`, `Element.cs` | Small |
| Interceptor generator (non-generic + `params`) | New generator project; model on `Reactor.Wrappers.Generator` | **Large** |
| Interceptor generator — generic factories | same | Medium |
| Packaging: pack as analyzer + `InterceptorsNamespaces` opt-in in `build/Reactor.targets` | `.csproj` / `.targets` | Medium — **see hazard below** |
| `InterceptsLocationAttribute` polyfill | generator | Small |
| Runtime flag (`ReactorSourceMap`) + `NeedsTag` arm for unstamped elements | `Reconciler.cs` | Small |
| ~~`SourceInfo` attached property~~ | — | **Deleted — redundant (§1.3)** |
| ~~Reconciler mount hook~~ | — | **Deleted — redundant (§1.4)** |
| Devtools §3.2 wiring: `NodeIdBuilder` rule 2, `SelectorResolver` ReactorSource arm, `includeReactorSource` | `Reactor.Devtools` | Medium |
| Preview server inspect + open-source endpoints | `PreviewCaptureServer.cs` | Medium |
| Dev tools inspect overlay | Preview UI code | Medium |

> **Packaging hazard.** A missing opt-in is a **hard build error, not a no-op**: if
> the generator emits and the consumer has not listed the namespace in
> `<InterceptorsNamespaces>`, they get `CS9137` and their build breaks. The generator
> must gate on the same property that the targets file uses to set
> `InterceptorsNamespaces` — keep both in one file so they cannot drift.
> Also note `InterceptsLocationAttribute` is **not** in the .NET 10 BCL (`CS0234`);
> probe `GetTypeByMetadataName` before emitting a polyfill so a future BCL addition
> does not collide.

### What does NOT change

- **Public API signatures** — under Approach 3, zero of the 189 factory signatures
  change. `reactor.api.txt` grows by 16 lines (the new type and slot).
- **Element semantics** — `CallSite` does not participate in diffing; see the
  resolved note below.
- **Unstamped allocation** — bucketed storage keeps the flag-off path byte-identical
  to baseline.
- **Release builds by default** — `ReactorSourceMap` defaults off, so nobody pays
  the build-time or stamping cost who has not opted in.

### Record equality note — REAL, and it needed three mitigations

> **Correction (2026-09-01).** An earlier amendment recorded this as "verified —
> a non-issue, no mitigation required." **That was wrong**, and it is worth
> recording why, because the spec's own instinct was better than the spike's
> conclusion.
>
> The spike checked `Element.ShallowEquals` — the reconciler's
> explicit allow-list — confirmed it never reads `CallSite`, and stopped there.
> That single check was sound but incomplete: it verified the path the spec
> *named*, not the paths bucketing subsequently created. Shipping needed three
> distinct mitigations:
>
> 1. **`CallSite` is excluded from `ElementExtras`' synthesized equality.**
>    Necessary, but on its own not sufficient.
> 2. **A `CallSite`-only bucket compares equal to no bucket at all.** This is the
>    case that actually bites. Stamping a bare element *materializes* the bucket, so
>    a bare element holds `null` while a stamped one holds a `CallSite`-only
>    `ElementExtras` — they differ on the bucket's **presence**, before the ignored
>    field is ever compared. With source mapping on, factory-built elements are
>    stamped while an expected value built with `new SomeElement(...)` is not, so
>    without this every such comparison fails.
> 3. **`Reconciler.CallSiteChangedOnSkip`** decides whether a *shallow-skipped*
>    element still needs its source tag refreshed — equality being ignored means the
>    reconciler can skip an element whose stamp has moved.
>
> The general lesson: an equality-ignored field is not a free change once the field
> can materialize a container. Pinned by
> `tests/Reactor.Tests/CallSiteChangedOnSkipTests.cs`, which deliberately calls the
> predicate directly — the selftest that nominally covered the decorator arm takes a
> full update and stays green with the unwrap removed, so it characterizes rather
> than guards.

The original text below correctly anticipated that this needed verifying; it was
only wrong about which mechanism would carry the fix.

---

## Future Extensions

These are not in scope for the initial implementation but the design
accommodates them:

### Stack-based resolution for helper methods

> **SOLVED in PR #1161 (2026-08-31)** — `[ReactorSourceTransparent]`.
>
> Both spikes measured this as unimproved: an interceptor replaces the *call site*,
> and the call site is inside the helper, so attribution landed on the helper's own
> line exactly as CallerInfo did. The shipped fix inverts the rule rather than
> resolving a stack.
>
> `[ReactorSourceTransparent]` marks a static, `Element`-returning helper as a pure
> forwarder. The generator then emits **no** interceptor for DSL calls *inside* it,
> and instead intercepts calls **to** it — so each call site gets its own line
> instead of collapsing onto the helper body. Annotated helpers compose, deferring
> outward until they reach a caller that is not annotated.
>
> Three properties worth preserving if this is ever revisited:
>
> - **Opt-in on purpose.** For a `Component.Render()` body the body line *is* the
>   right answer, so transparency must be requested rather than inferred.
> - **Conditioned on being forwardable, not merely annotated.** An annotation the
>   generator cannot honour reports `REACTOR_SOURCEMAP_001` rather than silently
>   producing a worse answer than no annotation at all.
> - **Read from metadata**, so the attribute works across assembly boundaries —
>   `PendingFactory.Pending` is annotated, and its callers now get a location where
>   they previously got `null`.
>
> The Lazy-PDB and `[DebuggerNonUserCode]` options below were **not** needed.

<details>
<summary>Original proposal (historical — superseded by <code>[ReactorSourceTransparent]</code>)</summary>

If a user writes a helper `MyHeader()` that calls `Text(...)`, CallerInfo
reports `MyHeader` as the source, not the page that called `MyHeader()`. To
solve this:

- Add an optional `[CallerFilePath]`/`[CallerLineNumber]` to component
  `Render()` methods
- Or use the Lazy PDB approach (Approach 6): capture IL offsets cheaply,
  resolve to the nearest user-code frame on demand when the inspector requests
  it
- `[DebuggerNonUserCode]` on all Reactor framework code enables "Just My Code"
  style filtering

</details>

### Visual Studio integration

> **Amended (2026-08-26).** The original text below is wrong on a factual point:
> **WinUI 3 has no public managed `GetXamlSourceInfo`.** WPF exposes
> `System.Windows.Diagnostics.VisualDiagnostics.GetXamlSourceInfo`, but in WinUI 3
> source info flows to Visual Studio over the XAML Diagnostics COM channel and is
> not reachable from app code. There is no first-party API to piggyback on, which
> is why Reactor must carry `CallSite` itself.
>
> The practical surfaces, in ascending cost:
>
> 1. **MCP tool** — no UI. `SelectorResolver.cs` and `DevtoolsUiaTools.cs`
>    are already stubbed and hard-erroring on this; wiring them is devtools §3.2.
> 2. **VS Code** — pick mode in the embedded preview, then
>    `vscode.window.showTextDocument(uri, { selection })`. No CLI shell-out needed.
> 3. **Visual Studio** — `vs-reactor` already embeds the live app HWND in a tool
>    window and holds a bidirectional channel to `PreviewCaptureServer` (the
>    `embed-v1` handshake carries the port, and `/embed/resize|move|release` already
>    flow over it). Add an inspect toggle and navigate via `IVsUIShellOpenDocument`.
>    `EditorTracker.cs` already does editor→preview; this is the reverse arrow.
>
> `ReconcileHighlightOverlay` is the existing visual primitive for the hover
> highlight — it draws Composition-layer rectangles rather than XAML elements
> specifically so the overlay does not show up as reconcile churn.
>
> **Leaf-level click-to-source requires the runtime flag.** Hit-testing a
> `TextBlock` finds no `ReactorState` unless the element was stamped, so an
> unstamped tree yields container-level attribution only.

<details>
<summary>Original text (historical — factually incorrect for WinUI 3)</summary>

The `SourceInfo.LocationProperty` attached property is already visible in VS's
XAML Live Visual Tree. A VS extension could add a "Go to Reactor Source" context
menu item that reads this property and navigates.

</details>

### Source map file (out-of-band)

For scenarios where embedded metadata is undesirable, emit a
`{assembly}.reactorsourcemap.json` file at build time (via a Source Generator)
mapping element type + key → source location. Similar to JavaScript source maps
or PDB files.

> **Amended (2026-08-26): the sketched schema does not work, but the idea has a
> real use.**
>
> **Keying on "element type + key" is under-determined.** Keys cover roughly 3% of
> element call sites (169 `.WithKey(...)` / `.Key(...)` uses against 4,227
> `TextBlock(` and 1,680 `Button(` in `samples/` + `tests/` alone), and
> `MissingWithKeyAnalyzer` scopes them to *dynamic list items* — they are a list
> reconciliation mechanism, not a general identity. For the other ~97%,
> `TextBlockElement → ???` has thousands of candidate lines. Any workable format
> needs call-site identity, which is exactly what the in-band work produces — so
> **this file cannot be built without doing Phase 1 first.** It is a delivery
> format, not an alternative.
>
> **Where it does earn its keep**, and why it may be worth doing:
>
> 1. **Keeping paths out of shipped binaries.** Because the capture parameters must
>    exist in the Release NuGet package for consumer call sites to populate, source
>    paths would otherwise be baked into user assemblies as string literals. That
>    runs against existing repo policy — `Directory.Build.props` enables
>    `DeterministicSourcePaths` in CI expressly "so PDBs don't leak local paths",
>    and TASK-064 redacts `ex.Message` from ETW for the same reason. A sidecar map
>    shipped like a PDB avoids it.
> 2. **Shrinking the per-element slot.** Replacing `SourceLocation?` with an `int`
>    call-site id into a generated per-assembly table drops the stamped cost and
>    removes the string reference. **Only a generator can do this** — CallerInfo
>    hands the call site a string and would need runtime interning. This is an
>    additional argument for Approach 3.

---

## Measured results (2026-08-26)

Two throwaway spikes, built in parallel and measured head-to-head, one per
candidate mechanism. **The spike branches were not retained**, so this section is
the durable record — it is deliberately specific enough (error codes, byte counts,
reproduction method) to be re-derived rather than merely believed. Approach 3's
half is additionally verifiable against the shipped generator; Approach 1 never
shipped, so its rows below are the only surviving evidence for why it was
rejected.

| Axis | Approach 1 (CallerInfo) | Approach 3 (interceptors) |
|---|---|---|
| Plain factories | ✅ | ✅ |
| **`params` factories (40)** | ❌ **impossible** | ✅ |
| **Generic factories (22)** | ✅ trivial | ✅ verified incl. `Component<T,TProps>` |
| **Hot reload** | ❌ **silently stale, unbounded** | ✅ correct |
| Public API churn | ~170 lines | **16 lines, 0 signatures** |
| **Consumer source-break** | ❌ method-group conversions | ✅ none |
| Unstamped alloc (bucketed) | +0.00 B/op | +0.00 B/op |
| Stamped alloc | **+152 B/site** | +312 B/site |
| Build-time cost | none | **+16–22% wall, +20–27% `Csc`** (incremental) |
| `string → Element` operator | wrong (`Element.cs`) | null ("unknown") |

Method: M12 `Pool_Rent_HotPath`, Release, 1000 iterations × 5 reps. Allocation
bytes are the comparable axis — the timing axis is environment-contaminated (spec
047 §15.5 isolation is not enforced from an automated run). The +24.0 B inline-slot
figure (1,127,848 B/op) was reproduced independently by both spikes on different
architectures, to the byte.

### Hot Reload staleness — the deciding measurement

Both mechanisms in one `dotnet watch` process, same file, same run. Inserting 10
comment-only lines *above* an untouched `Render()`:

```
dotnet watch 🔥 C# and Razor changes applied in 544ms.
  interceptors (non-params) = Program.cs(36,57)   ← CORRECT (was 26)
  interceptors (params)     = Program.cs(37,57)   ← CORRECT (was 27)
  CallerInfo                = Program.cs:28       ← STALE by exactly 10
```

**Mechanism:** `[CallerLineNumber]` is baked into the *caller's* IL, and EnC does
not re-emit a method whose IL is unchanged when lines merely shift above it. The
interceptor's constant lives in the *generated* body, whose content hash changes,
so a delta is emitted. CallerInfo self-corrects only when the enclosing method's
own IL changes — so it is wrong after any reformat, comment, or using-statement
insertion, with **no signal to the user**. Staleness per method equals net line
drift since that method was last edited, and is unbounded.

### Known limitations at decision time — both since closed

Both of the following were measured by *both* spikes and recorded here as
unfixable. **PR #1161 closed both.** They are kept because the reasoning that
declared them unfixable was wrong in an instructive way: each was treated as a
property of the *capture mechanism*, when both were actually properties of *where
the generator chose to intercept*.

- **`implicit operator Element(string)`** (declared on `Element` in `Element.cs`)
  calls `TextBlock` from inside the Reactor assembly, so bare-string children
  (`VStack("hi")`) were not attributable to user code — CallerInfo reported
  `Element.cs` itself, interceptors reported `null`, and an operator body inside
  Reactor is structurally unreachable by an interceptor. **Fixed by
  argument-position stamping:** the *enclosing* interceptor stamps each converted
  argument at its own line, respecting first-stamp-wins and never writing into a
  `params` array the caller owns.
- **Helper-method attribution** — see
  [Stack-based resolution for helper methods](#stack-based-resolution-for-helper-methods)
  above. **Fixed by `[ReactorSourceTransparent]`.**

### Still open

- **Non-`Factories` entry points.** Constructing an element record directly
  (`new TextBlockElement(...)`) is not a factory invocation and is not
  intercepted.
- **Decorator targets** resolve through `IDecoratorElementHandler<T>.GetSourceTarget`,
  and the walk is deliberately cycle- and depth-bounded — a pathological
  decorator chain resolves to no source rather than hanging.

