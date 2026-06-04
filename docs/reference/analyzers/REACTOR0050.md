# REACTOR0050 — Optional OneWay requires `dp:` for `ClearValue`

**Category:** `Reactor.Descriptor`  
**Default severity:** Warning

`REACTOR0050` fires when a `ControlDescriptor.OneWay(...)` entry reads an
`Optional<T>` value but does not provide the `dp:` argument. Without a
`DependencyProperty`, `Unset` can only skip the write; it cannot call
`ClearValue` to release the local value back to WinUI styling/template
fallback.

```csharp
// Warns: Unset skips; no ClearValue channel.
descriptor.OneWay(
    get: e => e.Background,
    set: (c, v) => c.Background = v);

// Preferred for DP-backed properties.
descriptor.OneWay(
    get: e => e.Background,
    set: (c, v) => c.Background = v,
    dp: Control.BackgroundProperty);
```

If skip-write is the intended semantic, use `OneWayConditional` or mark
the source property with `[NoClearValue]`. A nearby comment containing
`REACTOR0050: intentional skip` also suppresses the diagnostic for
non-DP-backed setters.
