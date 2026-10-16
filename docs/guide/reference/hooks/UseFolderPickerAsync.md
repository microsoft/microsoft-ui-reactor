# UseFolderPickerAsync

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseFolderPickerAsync(Microsoft.UI.Reactor.Core.FolderPickerOptions)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Opens a folder picker initialized with the owning window HWND. Must be called on the UI thread.

## Discussion

Call from an event handler, not during Render. Cancellation returns null.

## Featured in

- [Hooks](../../hooks.md)
- [Windows](../../windows.md)

