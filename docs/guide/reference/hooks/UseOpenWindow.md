# UseOpenWindow

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseOpenWindow(Microsoft.UI.Reactor.WindowKey,Microsoft.UI.Reactor.WindowSpec,System.Func{Microsoft.UI.Reactor.Core.Component})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Open or reuse a secondary window keyed by <paramref name="key" />. Renders
that pass the same <paramref name="key" /> share the same
`ReactorWindow`; if the spec changes
across renders the live window is updated via
`Update`. The returned
handle is identity-stable across renders so long as the key is stable.

## Discussion

<para>Unmount semantics: when the calling component unmounts, the opened
window stays open. Components that want the inverse behavior must close
the window explicitly — e.g. by registering a <c>UseEffect</c> cleanup
that calls `Close` on the
returned handle. (spec 036 §4.3 / §15.6)</para><para><b>Note — asymmetry with [UseTrayIcon](UseTrayIcon.md) ([guide](../../hooks.md)):</b> tray icons
are component-scoped and close on unmount; opened windows are app-scoped
and survive unmount. The asymmetry is deliberate (a window is normally
expected to outlive the menu item that opened it, while a tray icon
belongs to the component that declared it), so the two hooks behave
inversely despite the matching naming.</para><para>Returns <c>null</c> when no UI dispatcher has been captured —
happens in unit-test contexts where no <c>ReactorApp.Run</c> is in
flight. In production this is unreachable.</para>


