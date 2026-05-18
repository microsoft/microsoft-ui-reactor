# UseColorScheme

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseColorScheme`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns the effective `ColorScheme` at this component's
position in the tree. Automatically reflects the current system theme,
per-element <c>RequestedTheme</c> overrides, and High Contrast mode.
<para>
The value is re-evaluated on every render — when the theme changes,
`ReactorHost` triggers a re-render so this hook
naturally picks up the new value.
</para>


