# UIThreadOnlyAttribute

Marks a method or property setter that must be invoked on the UI thread — the annotated method (or the property's setter) calls `ThreadAffinity.ThrowIfNotOnUIThread`, which throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) when reached from a background thread once the UI dispatcher has been captured. (The guard is a no-op before the first window bootstraps, while [`ReactorApp.UIDispatcher`](ReactorApp.md#uidispatcher) is still null.) Property getters are not guarded, so the REACTOR_THREAD_001 analyzer flags writes to an annotated property, not reads.

```csharp
public sealed class UIThreadOnlyAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Hosting`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

**Learn more:** [Reactor for XAML Developers](../../xaml-developers.md)

## Remarks

The marker exists for the `REACTOR_THREAD_001` analyzer (`UIThreadAffinityAnalyzer`). In a consumer compilation the Reactor framework is a metadata-only reference, so the analyzer cannot inspect a callee's body for the runtime guard — `DeclaringSyntaxReferences` is empty and IL is not available. A metadata-visible attribute is the committed mechanism the analyzer keys off, so this type must be `public`.

This is a plain marker attribute with no members and no reflection surface, so it is trim- and AOT-safe. Keep it in sync with the members that call `ThreadAffinity.ThrowIfNotOnUIThread`; the analyzer reads the attribute rather than a hard-coded member list.

## Constructors

### `UIThreadOnlyAttribute`

```csharp
public UIThreadOnlyAttribute()
```
