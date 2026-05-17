# SetLoading

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.PendingScope.SetLoading(System.Object,System.Boolean)`

## Summary

Update <paramref name="token" />'s loading state. Silently ignored if the token
was never registered (defensive — avoids forcing the caller to track whether they
registered).

