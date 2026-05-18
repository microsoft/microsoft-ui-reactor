# UseReducedMotion

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducedMotion`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns <c>true</c> when the user or system prefers reduced motion
(e.g., Windows "Show animations" is off, or <c>SPI_GETCLIENTAREAANIMATION</c>
returns false). Automatically re-renders the component when the preference changes.
<para>
Use this to skip entrance/exit animations, disable pan inertia, terminate
force-graph simulations immediately, and keep only ≤ 150 ms opacity fades
(WCAG 2.3.3).
</para>


