# UseSaveFilePickerAsync

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseSaveFilePickerAsync(Microsoft.UI.Reactor.Core.SaveFilePickerOptions)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Opens a save-file picker on the owning window's UI thread.

## Discussion

Call from an event handler, not during Render. Cancellation returns null. Writing to the returned file is the caller's responsibility.

## Featured in

- [Hooks](../../hooks.md)
- [Windows](../../windows.md)

