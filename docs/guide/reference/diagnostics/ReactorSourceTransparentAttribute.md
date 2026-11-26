# ReactorSourceTransparentAttribute

Spec 010 — marks a helper method as *source-transparent*: elements it returns are attributed to the line that CALLED it, not to the line inside it that ran the DSL factory.

Without this, a thin forwarding helper collapses every one of its call sites onto a single line, because an interceptor replaces the *call site* and the call site of `TextBlock(...)` in `static Element MyHeader() => TextBlock("header");` is inside `MyHeader`. Annotating `MyHeader` flips the attribution outward: the source-map generator emits no interceptor for the factory calls in its body, and instead intercepts calls *to* `MyHeader` and stamps the caller's line.

**Deliberately opt-in.** It is not a blanket rule for helpers, because for most element-returning methods the body line is the RIGHT answer — a `Component.Render()` body is where the author actually wrote the UI, and deferring it to the reconciler's call site would be a regression. Only a thin forwarder, whose own line carries no information a reader wants, benefits. This attribute is how that intent is expressed.

**Nesting.** Annotated methods compose: a transparent helper calling another transparent helper keeps deferring outward until it reaches a caller that is not annotated, and that caller's line is what gets stamped.

``[ReactorSourceTransparent] internal static Element Field(string label, string value) => HStack(TextBlock(label), TextBlock(value)); // Both rows report THEIR OWN line, instead of both reporting the // `HStack(` line inside Field. var form = VStack( Field("Name", name), Field("Email", email));``

```csharp
public sealed class ReactorSourceTransparentAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Diagnostics`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

**Learn more:** [Diagnostics](../../diagnostics.md)

## Remarks

**Requirements.** The generator has to be able to emit an interceptor that forwards to the annotated method, so it must be `static`, return an `Element`, and be reachable by name from a generated file in the same compilation — i.e. `public` or `internal` (never `private`), not declared in a `file`-local or generic type, and not a local function (C# interceptors cannot intercept those). An annotation the generator cannot honour is reported as `REACTOR_SOURCEMAP_001` rather than silently doing nothing, and attribution falls back to today's behaviour — the helper's own line — so a bad annotation never makes attribution worse than no annotation.

**Where it takes effect.** Rule 1 (suppressing stamps inside the body) applies in the compilation that *declares* the method; rule 2 (stamping at the call site) applies in the compilation that *calls* it. Both need the source-map generator loaded, so annotating a method in a library whose consumer has not enabled `ReactorSourceMap` simply leaves those elements unstamped.

A plain marker attribute with no members and no reflection surface, so it is trim- and AOT-safe. It is read by the generator from metadata, which is why it must be `public`.

## Constructors

### `ReactorSourceTransparentAttribute`

```csharp
public ReactorSourceTransparentAttribute()
```
