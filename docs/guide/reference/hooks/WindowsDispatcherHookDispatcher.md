# WindowsDispatcherHookDispatcher

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.WindowsDispatcherHookDispatcher`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Default [IHookDispatcher](IHookDispatcher.md) ([guide](../../hooks.md)) backed by <c>DispatcherQueue.GetForCurrentThread()</c>.
Falls back to inline invocation when called outside a WinUI dispatcher (unit tests).


