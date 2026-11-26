# UseIntl

Returns an IntlAccessor for the current locale. Re-renders this component when the locale changes via a parent LocaleProvider. If no LocaleProvider is present, returns a default accessor using the OS locale. Uses Context internally — the context system handles re-renders automatically.

```csharp
public IntlAccessor UseIntl()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Localization](../../localization.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected IntlAccessor UseIntl()
```
