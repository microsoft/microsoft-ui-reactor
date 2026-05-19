# PendingComponent

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.PendingComponent`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Component backing the <c>Pending</c> element. Hosts a [PendingScope](PendingScope.md) ([guide](../../hooks.md)),
subscribes to its [Changed](Changed.md) ([guide](../../hooks.md)) event, and chooses between
`Fallback` and `Child`.

## Discussion

The child subtree is always rendered. When the scope reports any loading resource,
the child's <c>Visibility</c> is flipped to <c>Collapsed</c> and the fallback is
shown instead. Because the subtree stays mounted, its hooks keep running, their
fetches complete in the background, and the UI swaps to the real content on the
next render frame after the scope reports clean.


