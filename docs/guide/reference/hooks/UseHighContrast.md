# UseHighContrast

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseHighContrast`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns `true` when the system is in a High Contrast (forced colors) theme.
Automatically re-renders the component when high contrast is toggled.


Use this to conditionally override custom styling (hardcoded backgrounds,
foregrounds, border colors) that would ignore forced-colors mode.
WinUI controls using ThemeResource brushes adapt automatically — this hook
is for Reactor components that use explicit color values.




