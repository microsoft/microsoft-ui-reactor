# UseIntl

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseIntl`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns an IntlAccessor for the current locale. Re-renders this component
when the locale changes via a parent LocaleProvider.
If no LocaleProvider is present, returns a default accessor using the OS locale.
Uses Context internally — the context system handles re-renders automatically.


