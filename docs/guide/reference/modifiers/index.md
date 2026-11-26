# Modifiers

Fluent extension methods that change layout, appearance, input and behavior.

Modifiers are extension methods on element records. Each one returns a changed copy of the element, so you can chain them: `TextBlock("Hi").Margin(8).Bold()`.

**Learn more:** [Modifier System](../../modifier-system.md), [Styling and Theming](../../styling.md)

## Common modifiers

Modifiers that most elements have, grouped by purpose.

| Area | Description |
| --- | --- |
| [Layout and spacing](layout-and-spacing.md) | Size, margin, padding, alignment and the attached layout properties. |
| [Appearance](appearance.md) | Background, foreground, borders, corners, opacity, transforms, styles and theme. |
| [Typography](typography.md) | Fonts, text alignment, wrapping, trimming and other text formatting. |
| [Accessibility](accessibility.md) | Automation names, landmarks, live regions and other accessibility properties. |
| [Focus and keyboard](focus-and-keyboard.md) | Tab order, access keys, directional focus and focus traps. |
| [Input and events](input-and-events.md) | Pointer, keyboard, gesture and drag and drop events, and whether an element takes input. |
| [Lifecycle and identity](lifecycle.md) | Keys, refs, mount and unmount callbacks, size changes and context values. |
| [Animation](animation.md) | Transitions, keyframes, layout animations, interaction states and scroll-linked animation. |
| [Tooltips and flyouts](tooltips-and-flyouts.md) | Tooltips, flyouts and context flyouts attached to any element. |
| [Validation](validation.md) | Validators, required fields and error styling. |
| [Native properties](native-properties.md) | The Set modifier, which changes the real WinUI control directly. |

## Control modifiers

Modifiers that only some controls have, grouped by the area of those controls.

| Area | Description |
| --- | --- |
| [Shared controls](shared-controls.md) | Modifiers that several kinds of controls share, such as headers, placeholders and selection events. |
| [Layout](layout.md) | Modifiers that only the controls in the Layout area have. |
| [Buttons](buttons.md) | Modifiers that only the controls in the Buttons area have. |
| [Input](input.md) | Modifiers that only the controls in the Input area have. |
| [Collections](collections.md) | Modifiers that only the controls in the Collections area have. |
| [Navigation](navigation.md) | Modifiers that only the controls in the Navigation area have. |
| [Dialogs and flyouts](dialogs-and-flyouts.md) | Modifiers that only the controls in the Dialogs and flyouts area have. |
| [Media and icons](media.md) | Modifiers that only the controls in the Media and icons area have. |
| [Shapes](shapes.md) | Modifiers that only the controls in the Shapes area have. |
| [Status and info](status-and-info.md) | Modifiers that only the controls in the Status and info area have. |
| [Forms](forms.md) | Modifiers that only the controls in the Forms area have. |
| [Interop](interop.md) | Modifiers that only the controls in the Interop area have. |

## Types

| Type | Description |
| --- | --- |
| [`AccessibilityModifiers`](AccessibilityModifiers.md) | Advanced accessibility properties (WCAG Tier 2/3). |
| [`ElementExtensions`](ElementExtensions.md) | Fluent modifier extension methods for elements. |
| [`ElementExtras`](ElementExtras.md) | Cross-cutting "extras" bucket for `Element` (spec 047 §4.4). |
| [`ElementModifiers`](ElementModifiers.md) |  |
| [`LayoutModifiers`](LayoutModifiers.md) | Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). |
| [`VisualModifiers`](VisualModifiers.md) | Visual-related modifiers (brushes, borders, transforms, opacity). |
