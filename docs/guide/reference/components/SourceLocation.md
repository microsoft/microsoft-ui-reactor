# SourceLocation

Spec 010 — the C# source location that produced an [`Element`](../elements/Element.md).

[`SourceLocation.ToString`](SourceLocation.md) / [`SourceLocation.ToShortString`](#toshortstring) keep their `file:line` forms; read [`SourceLocation.ColumnNumber`](#columnnumber) directly.

```csharp
public readonly record struct SourceLocation(string FilePath, int LineNumber, int ColumnNumber)
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Source Mapping](../../source-mapping.md)

## Constructors

### `SourceLocation`

Spec 010 — the C# source location that produced an [`Element`](../elements/Element.md).

[`SourceLocation.ToString`](SourceLocation.md) / [`SourceLocation.ToShortString`](#toshortstring) keep their `file:line` forms; read [`SourceLocation.ColumnNumber`](#columnnumber) directly.

```csharp
public SourceLocation(string FilePath, int LineNumber, int ColumnNumber)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `FilePath` | `string` | Absolute path of the source file at compile time. Under a deterministic build (`DeterministicSourcePaths`, which this repo enables when `CI=true`) this is the *mapped* path — e.g. `/_/src/Reactor/Elements/Dsl.cs` — not a local disk path. |
| `LineNumber` | `int` | 1-based line number of the DSL call site. |
| `ColumnNumber` | `int` | 1-based column of the DSL call site on `LineNumber`, or `0` when the provider did not record one. The source-map generator reports the first character of the invoked method's name — the `B` of `Button` in `Row(Button("a"), Button("b"))`, the `T` of `TextBlock` in `Factories.TextBlock("x")` — which is what tells several factory calls on one line apart. In the rare layout where the name and its `(` sit on different lines, [`SourceLocation.LineNumber`](#linenumber) follows the paren (matching `[CallerLineNumber]`), so the column is the paren's too and the pair always names a position on the reported line. For an element stamped in argument position (a `string` converted to an element) it is the first character of the argument expression. Counted in UTF-16 code units with a tab counting as one, the convention Roslyn diagnostics and VS Code's `file:line:column` links use. |

## Properties

### `ColumnNumber`

1-based column of the DSL call site on `LineNumber`, or `0` when the provider did not record one.

The source-map generator reports the first character of the invoked method's name — the `B` of `Button` in `Row(Button("a"), Button("b"))`, the `T` of `TextBlock` in `Factories.TextBlock("x")` — which is what tells several factory calls on one line apart. In the rare layout where the name and its `(` sit on different lines, [`SourceLocation.LineNumber`](#linenumber) follows the paren (matching `[CallerLineNumber]`), so the column is the paren's too and the pair always names a position on the reported line. For an element stamped in argument position (a `string` converted to an element) it is the first character of the argument expression.

Counted in UTF-16 code units with a tab counting as one, the convention Roslyn diagnostics and VS Code's `file:line:column` links use.

```csharp
public int ColumnNumber  // get, init
```

### `DeclaredName`

The identifier the element created at this call site was assigned to, as the source-map generator saw it — `title` for `var title = TextBlock("x").Bold()`, a field or property name for an initializer, the member name for an expression-bodied member or local function. `null` for an element written inline (an argument, a collection element), for a build without source mapping, or after a hot-reload edit moved the call.

Not stored on the location: it is looked up in the generator's static table, so it costs nothing per element and does not take part in equality.

```csharp
public string? DeclaredName  // get
```

### `FilePath`

Absolute path of the source file at compile time. Under a deterministic build (`DeterministicSourcePaths`, which this repo enables when `CI=true`) this is the *mapped* path — e.g. `/_/src/Reactor/Elements/Dsl.cs` — not a local disk path.

```csharp
public string FilePath  // get, init
```

### `LineNumber`

1-based line number of the DSL call site.

```csharp
public int LineNumber  // get, init
```

## Methods

### `ToShortString`

Short display form: filename + line only (`MainPage.cs:34`).

```csharp
public string ToShortString()
```
