# UseHighContrastScheme

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseHighContrastScheme`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns the high contrast scheme name (e.g., "High Contrast Black",
"High Contrast White") or <c>null</c> if not in high contrast mode.
Automatically re-renders the component when the scheme changes.
<para>
Must be called instead of (not in addition to) [UseHighContrast](UseHighContrast.md) ([guide](../../hooks.md))
because each consumes the same hook slots. Use one or the other.
</para>


