# UseFilePickerAsync

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseFilePickerAsync(Microsoft.UI.Reactor.Core.FilePickerOptions)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Opens a single-file picker initialized with the owning window HWND. Must be called on the UI thread.

## Discussion

Call from an event handler, not during Render. Cancellation returns null.

## Featured in

- [Hooks](../../hooks.md)
- [Windows](../../windows.md)

