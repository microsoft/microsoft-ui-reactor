# Transition comparison — Reactor vs. WinUI XAML

Two apps that play the same navigation transitions, one through Reactor and one through a real
WinUI `Frame`. Run both, put the windows side by side, and click the same row in each.

```
dotnet run --project samples/TransitionComparison.Reactor -c Debug -p:Platform=x64
dotnet run --project samples/TransitionComparison.Xaml    -c Debug -p:Platform=x64
```

They open at the same size with the same two-column layout, the same button order, and the same
two full-bleed pages (teal `PAGE A`, violet `PAGE B`), so the only thing that should differ is
the motion.

## Why this exists

Reactor has no `Frame` to hand a `NavigationTransitionInfo` to — it needs both pages alive at
once and a completion callback to sequence caching and the `onNavigatedTo` / `onNavigatedFrom`
lifecycle, so it replays WinUI's motions on the Composition layer instead
(`docs/specs/011-navigation-design.md`, Appendix C).

That makes the timings, offsets, scales, and easing curves *copies* of WinUI's rather than the
platform's own. `WinUiNavigationMotionParityTests` pins each value to the WinUI source it came
from, but a test comparing numbers cannot tell you whether the result *looks* right. These two
apps are how you check that by eye.

## What maps to what

| Row | Reactor | WinUI XAML |
|---|---|---|
| Default | `NavigationTransition.Default` | `Frame.Navigate` with no transition info |
| Entrance | `NavigationTransition.Entrance()` | `EntranceNavigationTransitionInfo` |
| Slide — FromRight | `NavigationTransition.Slide(SlideDirection.FromRight)` | `SlideNavigationTransitionInfo { FromRight }` |
| Slide — FromLeft | `NavigationTransition.Slide(SlideDirection.FromLeft)` | `SlideNavigationTransitionInfo { FromLeft }` |
| Slide — FromBottom | `NavigationTransition.Slide()` | `SlideNavigationTransitionInfo { FromBottom }` |
| DrillIn | `NavigationTransition.DrillIn()` | `DrillInNavigationTransitionInfo` |
| None | `NavigationTransition.None` | `SuppressNavigationTransitionInfo` |
| Fade | `NavigationTransition.Fade()` | — |
| Spring | `NavigationTransition.Spring()` | — |

The last two are Reactor extensions with no WinUI counterpart. They appear as disabled rows in
the XAML app rather than being omitted, so the two lists stay aligned and the gap is visible.

**Go Back** plays the reverse of whichever transition you last used, on both sides.

## Reduced motion

Both apps stop animating when **Settings → Accessibility → Visual effects → Animation effects**
is off, and swap pages instantly instead. That is not automatic for Reactor: WinUI's theme
transitions honour the setting themselves, but a Composition replay honours nothing unless it
asks, so `TransitionEngine` reads `UISettings.AnimationsEnabled` (spec 006 §4.3).

If you want to see the animations, turn that setting on first — otherwise both apps will look
correctly, and identically, motionless.
