---
title: "Modifiers"
description: "Fluent extension methods that change layout, appearance, input and behavior."
sidebar:
  label: "Overview"
---

Modifiers are extension methods on element records. Each one returns a changed copy of the element, so you can chain them: `TextBlock("Hi").Margin(8).Bold()`.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system), [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## Common modifiers [#common-modifiers]

Modifiers that most elements have, grouped by purpose.

| Area | Description |
| --- | --- |
| [Layout and spacing](/microsoft-ui-reactor/main/reference/modifiers/layout-and-spacing) | Size, margin, padding, alignment and the attached layout properties. |
| [Appearance](/microsoft-ui-reactor/main/reference/modifiers/appearance) | Background, foreground, borders, corners, opacity, transforms, styles and theme. |
| [Typography](/microsoft-ui-reactor/main/reference/modifiers/typography) | Fonts, text alignment, wrapping, trimming and other text formatting. |
| [Accessibility](/microsoft-ui-reactor/main/reference/modifiers/accessibility) | Automation names, landmarks, live regions and other accessibility properties. |
| [Focus and keyboard](/microsoft-ui-reactor/main/reference/modifiers/focus-and-keyboard) | Tab order, access keys, directional focus and focus traps. |
| [Input and events](/microsoft-ui-reactor/main/reference/modifiers/input-and-events) | Pointer, keyboard, gesture and drag and drop events, and whether an element takes input. |
| [Lifecycle and identity](/microsoft-ui-reactor/main/reference/modifiers/lifecycle) | Keys, refs, mount and unmount callbacks, size changes and context values. |
| [Animation](/microsoft-ui-reactor/main/reference/modifiers/animation) | Transitions, keyframes, layout animations, interaction states and scroll-linked animation. |
| [Tooltips and flyouts](/microsoft-ui-reactor/main/reference/modifiers/tooltips-and-flyouts) | Tooltips, flyouts and context flyouts attached to any element. |
| [Validation](/microsoft-ui-reactor/main/reference/modifiers/validation) | Validators, required fields and error styling. |
| [Native properties](/microsoft-ui-reactor/main/reference/modifiers/native-properties) | The Set modifier, which changes the real WinUI control directly. |

## Control modifiers [#control-modifiers]

Modifiers that only some controls have, grouped by the area of those controls.

| Area | Description |
| --- | --- |
| [Shared controls](/microsoft-ui-reactor/main/reference/modifiers/shared-controls) | Modifiers that several kinds of controls share, such as headers, placeholders and selection events. |
| [Layout](/microsoft-ui-reactor/main/reference/modifiers/layout) | Modifiers that only the controls in the Layout area have. |
| [Buttons](/microsoft-ui-reactor/main/reference/modifiers/buttons) | Modifiers that only the controls in the Buttons area have. |
| [Input](/microsoft-ui-reactor/main/reference/modifiers/input) | Modifiers that only the controls in the Input area have. |
| [Collections](/microsoft-ui-reactor/main/reference/modifiers/collections) | Modifiers that only the controls in the Collections area have. |
| [Navigation](/microsoft-ui-reactor/main/reference/modifiers/navigation) | Modifiers that only the controls in the Navigation area have. |
| [Dialogs and flyouts](/microsoft-ui-reactor/main/reference/modifiers/dialogs-and-flyouts) | Modifiers that only the controls in the Dialogs and flyouts area have. |
| [Media and icons](/microsoft-ui-reactor/main/reference/modifiers/media) | Modifiers that only the controls in the Media and icons area have. |
| [Shapes](/microsoft-ui-reactor/main/reference/modifiers/shapes) | Modifiers that only the controls in the Shapes area have. |
| [Status and info](/microsoft-ui-reactor/main/reference/modifiers/status-and-info) | Modifiers that only the controls in the Status and info area have. |
| [Forms](/microsoft-ui-reactor/main/reference/modifiers/forms) | Modifiers that only the controls in the Forms area have. |
| [Interop](/microsoft-ui-reactor/main/reference/modifiers/interop) | Modifiers that only the controls in the Interop area have. |
| [Docking](/microsoft-ui-reactor/main/reference/modifiers/docking) | Modifiers that only the controls in the Docking area have. |
| [Win2D canvas](/microsoft-ui-reactor/main/reference/modifiers/win2d) | Modifiers that only the controls in the Win2D canvas area have. |

## Types [#types]

| Type | Description |
| --- | --- |
| [`AccessibilityModifiers`](/microsoft-ui-reactor/main/reference/modifiers/AccessibilityModifiers) | Advanced accessibility properties (WCAG Tier 2/3). |
| [`ElementExtensions`](/microsoft-ui-reactor/main/reference/modifiers/ElementExtensions) | Fluent modifier extension methods for elements. |
| [`ElementExtras`](/microsoft-ui-reactor/main/reference/modifiers/ElementExtras) | Cross-cutting "extras" bucket for `Element` (spec 047 §4.4). |
| [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) |  |
| [`LayoutModifiers`](/microsoft-ui-reactor/main/reference/modifiers/LayoutModifiers) | Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). |
| [`VisualModifiers`](/microsoft-ui-reactor/main/reference/modifiers/VisualModifiers) | Visual-related modifiers (brushes, borders, transforms, opacity). |
