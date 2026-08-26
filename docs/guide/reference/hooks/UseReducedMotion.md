# UseReducedMotion

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducedMotion`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns `true` when the user or system prefers reduced motion
(e.g., Windows "Show animations" is off, or `SPI_GETCLIENTAREAANIMATION`
returns false). Automatically re-renders the component when the preference changes.
<para>
Use this to skip entrance/exit animations, disable pan inertia, terminate
force-graph simulations immediately, and keep only ≤ 150 ms opacity fades
(WCAG 2.3.3).
</para><para>
The value is seeded during the first render and then tracked live through
`UISettings.AnimationsEnabledChanged`. That event needs Windows 10 2004 (19041);
on older builds the preference is re-read whenever a theme or palette change arrives
instead, so it updates on the next such notification rather than immediately.
</para>


