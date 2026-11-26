# Accessibility

The accessibility scanner, its diagnostics and semantic panels.

**Learn more:** [Accessibility](../../accessibility.md)

## Types

| Type | Description |
| --- | --- |
| [`A11yContext`](A11yContext.md) | Rich contextual information harvested from the element tree during the scan. |
| [`A11yDiagnostic`](A11yDiagnostic.md) | A single accessibility diagnostic finding from the scanner. |
| [`A11yFixSuggestion`](A11yFixSuggestion.md) | Tells an AI agent exactly which modifier to add and what value to use. |
| [`AccessibilityScanner`](AccessibilityScanner.md) | Post-reconciliation accessibility scanner that walks the Reactor virtual element tree and produces structured diagnostics. |
| [`SemanticDescription`](SemanticDescription.md) | Describes the semantic role, value, and range of a composite component for assistive technology. |
| [`SemanticPanel`](SemanticPanel.md) | A lightweight WinUI Panel that provides custom automation semantics for composite Reactor components. |
| [`SemanticPanelAutomationPeer`](SemanticPanelAutomationPeer.md) | Custom AutomationPeer for SemanticPanel. |
