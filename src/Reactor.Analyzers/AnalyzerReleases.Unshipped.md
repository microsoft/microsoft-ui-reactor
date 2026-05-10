### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
REACTOR_THEME_001 | Reactor.Style | Warning | UseThemeRefAnalyzer - Use ThemeRef instead of hard-coded color
REACTOR_THEME_002 | Reactor.Style | Info | UseLightweightStylingAnalyzer - Consider lightweight styling for visual-state overrides
REACTOR_THEME_003 | Reactor.Style | Info | RequestedThemeSetAnalyzer - RequestedTheme modifier available
REACTOR_HOOKS_001 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook called conditionally
REACTOR_HOOKS_004 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook deps contains freshly allocated value
REACTOR_HOOKS_005 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook called outside Render or custom-hook method
REACTOR_HOOKS_006 | Reactor.Hooks | Info | HookRulesAnalyzer - UseResource fetcher looks non-idempotent (use UseMutation for writes)
REACTOR_HOOKS_007 | Reactor.Hooks | Warning | UseMemoCellsAnalyzer - Builder closure capture missing from dependencies
REACTOR_A11Y_001 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Icon-only button needs an accessible name
REACTOR_A11Y_002 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Image needs alt text or AccessibilityHidden
REACTOR_A11Y_003 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Form field needs a label
REACTOR_DSL_001 | Reactor.Dsl | Warning | MissingWithKeyAnalyzer - Dynamic list item missing .WithKey
