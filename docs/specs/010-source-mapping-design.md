# Reactor Source Mapping — Design Spec

Map every running UI element back to the C# source that created it, enabling
"go to definition" from the `--preview` dev tools to the user's IDE.

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

---

## Research: Approaches Investigated

Six approaches were evaluated. The first two form the recommended plan; the
others are documented for context and as future options.

### Approach 1: CallerInfo Attributes ✅ Recommended

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
| Coverage | Every factory method call site |
| Limitation | Reports the *immediate* call site — helper/wrapper methods report their own location, not the caller's |

**Used by:** NUnit/xUnit assertions, `ArgumentNullException.ThrowIfNull()`, INotifyPropertyChanged.

### Approach 2: WinUI Attached Properties ✅ Recommended

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

### Approach 3: React-Style Compile-Time Source Transform

React's Babel plugin injected `__source: { fileName, lineNumber }` into every
JSX element. The C# equivalent would be a Roslyn Source Generator or Fody IL
weaver that post-processes factory calls.

**Verdict:** Unnecessary — CallerInfo attributes achieve the same result with no
custom tooling. React 19.2 actually *dropped* this approach in favor of native
stack traces. Source generators cannot rewrite existing code (only add new
files), so users would need to call generated wrappers.

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

Combine Approaches 1 + 2: **CallerInfo on the DSL, attached properties on the
controls.** This is the C# equivalent of Flutter's source tracking, using
existing language features instead of a custom compiler.

### Phase 1: Element Source Tracking

#### 1.1 Add `SourceLocation` to the Element base record

```csharp
// Reactor/Core/Element.cs

#if DEBUG
/// <summary>
/// Source file and line where this element was created via the DSL.
/// Populated automatically by CallerInfo attributes. Debug-only.
/// </summary>
public readonly record struct SourceLocation(string FilePath, int LineNumber)
{
    public override string ToString() => $"{FilePath}:{LineNumber}";

    /// <summary>Short display form: filename + line only.</summary>
    public string ToShortString()
    {
        var fileName = System.IO.Path.GetFileName(FilePath);
        return $"{fileName}:{LineNumber}";
    }
}
#endif

public abstract record Element
{
    // ... existing properties (Key, Modifiers, Attached, etc.)

#if DEBUG
    public SourceLocation? Source { get; init; }
#endif
}
```

#### 1.2 Add CallerInfo parameters to all DSL factory methods

Every factory method in `Dsl.cs` gets two optional trailing parameters:

```csharp
// Before:
public static TextElement Text(string content)
    => new(content);

// After:
public static TextElement Text(string content,
#if DEBUG
    [CallerFilePath] string __sourceFile = "",
    [CallerLineNumber] int __sourceLine = 0
#endif
    )
    => new(content)
#if DEBUG
    { Source = new(__sourceFile, __sourceLine) }
#endif
    ;
```

This pattern applies to all ~40 factory methods: `Text()`, `Button()`,
`VStack()`, `HStack()`, `Grid()`, `Image()`, `TextBox()`, `CheckBox()`,
`Component<T>()`, `Func()`, `ForEach()`, `Memo()`, `When()`, etc.

**Extension methods** (`ElementExtensions.cs`) that return new elements (e.g.,
`.Key()`, `.Grid()`) should propagate the existing `Source` via `with`:

```csharp
public static T Key<T>(this T element, string key) where T : Element
    => element with { Key = key };
// Source is automatically preserved by record 'with' expressions.
```

No changes needed for pure modifier extensions — `with` preserves all
properties including `Source`.

#### 1.3 Add `SourceInfo` attached property for WinUI controls

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

#### 1.4 Write source info during reconciliation

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

This is a single insertion point — all mount paths flow through a common method
that can apply the attached property.

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

| Item | Files affected | Effort |
|------|---------------|--------|
| `SourceLocation` struct + `Element.Source` property | `Element.cs` | Small |
| CallerInfo on DSL factory methods | `Dsl.cs` (~40 methods) | Medium (mechanical) |
| CallerInfo on element constructors | Element record types | Medium (mechanical) |
| `SourceInfo` attached property | New file: `Diagnostics/SourceInfo.cs` | Small |
| Reconciler mount hook | `Reconciler.Mount.cs` | Small |
| Preview server `/api/elements` endpoint | `PreviewCaptureServer.cs` | Medium |
| Preview server `/api/open-source` endpoint | `PreviewCaptureServer.cs` | Small |
| Dev tools inspect overlay | Preview UI code | Medium |

### What does NOT change

- **Release builds** — all source tracking is `#if DEBUG`, zero impact
- **Element semantics** — `Source` is not included in record equality (see note below)
- **Existing tests** — `Source` is optional and debug-only
- **User API** — CallerInfo params are optional with defaults, fully backward-compatible

### Record equality note

`SourceLocation` must not affect element diffing in the reconciler. Since
`Source` will differ between renders even when the element is logically the
same, the reconciler's `CanUpdate()` / equality checks should either:
- Exclude `Source` from comparison (custom `Equals`), or
- Use a separate side-table (`ConditionalWeakTable<Element, SourceLocation>`)
  instead of a record property

The record property approach is simpler and the reconciler already compares
specific fields rather than using full record equality, so this is likely a
non-issue — but it must be verified during implementation.

---

## Future Extensions

These are not in scope for the initial implementation but the design
accommodates them:

### Stack-based resolution for helper methods

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

### Visual Studio integration

The `SourceInfo.LocationProperty` attached property is already visible in VS's
XAML Live Visual Tree. A VS extension could add a "Go to Reactor Source" context
menu item that reads this property and navigates.

### Source map file (out-of-band)

For scenarios where embedded metadata is undesirable, emit a
`{assembly}.ductsourcemap.json` file at build time (via a Source Generator)
mapping element type + key → source location. Similar to JavaScript source maps
or PDB files.
