# UseElementRefExtensions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Hook that returns a strongly-typed `ElementRef` for binding to a
concrete WinUI control via the `.Ref(...)` modifier. The typed ref removes
the `(Button)ref.Current` cast at consumers (Composition, Ink, focus, …).

## Discussion

Spec 033 §3. The same `ElementRef` instance is returned across
re-renders (identity stable), so storing the ref in a deps array or comparing
with `Object.ReferenceEquals` is safe.

## Examples

<code>
var btn = ctx.UseElementRef&lt;Button&gt;();
ctx.UseEffect(() =&gt; btn.Current?.Focus(FocusState.Programmatic), Array.Empty&lt;object&gt;());
return Button("Press me", onPress).Ref(btn);
</code>


