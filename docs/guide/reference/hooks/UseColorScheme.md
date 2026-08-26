# UseColorScheme

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseColorScheme`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns the app-global `ColorScheme` — `ColorScheme.Light`
or `ColorScheme.Dark`, read from
`Application.Current.RequestedTheme`.


The value is re-evaluated on every render — when the app theme changes,
`ReactorHost` triggers a re-render so this hook
naturally picks up the new value.



This hook does **not** observe per-element `RequestedTheme` overrides: it
consults the application, not the calling component's position in the tree, so a
component inside a `.RequestedTheme(Dark)` subtree still reports the app theme.
It also never returns `ColorScheme.HighContrast` in a running app — use
[UseHighContrast](UseHighContrast.md) ([guide](../../hooks.md)) for forced-colors mode.




