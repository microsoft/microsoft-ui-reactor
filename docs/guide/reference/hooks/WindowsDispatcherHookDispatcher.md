# WindowsDispatcherHookDispatcher

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.WindowsDispatcherHookDispatcher`

## Summary

Default [IHookDispatcher](IHookDispatcher.md) backed by <c>DispatcherQueue.GetForCurrentThread()</c>.
Falls back to inline invocation when called outside a WinUI dispatcher (unit tests).

