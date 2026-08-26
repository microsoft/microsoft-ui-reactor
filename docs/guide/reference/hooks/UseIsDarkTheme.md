# UseIsDarkTheme

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseIsDarkTheme`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Convenience wrapper — returns `true` when the app-global color
scheme is `ColorScheme.Dark`. Exactly
`UseColorScheme() == ColorScheme.Dark`, so it inherits that hook's
semantics: it does not observe per-element `RequestedTheme`.


