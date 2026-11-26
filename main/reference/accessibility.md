---
title: "Accessibility"
description: "The accessibility scanner, its diagnostics and semantic panels."
sidebar:
  label: "Overview"
---

**Learn more:** [Accessibility](/microsoft-ui-reactor/main/guides/accessibility)

## Types [#types]

| Type | Description |
| --- | --- |
| [`A11yContext`](/microsoft-ui-reactor/main/reference/accessibility/A11yContext) | Rich contextual information harvested from the element tree during the scan. |
| [`A11yDiagnostic`](/microsoft-ui-reactor/main/reference/accessibility/A11yDiagnostic) | A single accessibility diagnostic finding from the scanner. |
| [`A11yFixSuggestion`](/microsoft-ui-reactor/main/reference/accessibility/A11yFixSuggestion) | Tells an AI agent exactly which modifier to add and what value to use. |
| [`AccessibilityScanner`](/microsoft-ui-reactor/main/reference/accessibility/AccessibilityScanner) | Post-reconciliation accessibility scanner that walks the Reactor virtual element tree and produces structured diagnostics. |
| [`SemanticDescription`](/microsoft-ui-reactor/main/reference/accessibility/SemanticDescription) | Describes the semantic role, value, and range of a composite component for assistive technology. |
| [`SemanticPanel`](/microsoft-ui-reactor/main/reference/accessibility/SemanticPanel) | A lightweight WinUI Panel that provides custom automation semantics for composite Reactor components. |
| [`SemanticPanelAutomationPeer`](/microsoft-ui-reactor/main/reference/accessibility/SemanticPanelAutomationPeer) | Custom AutomationPeer for SemanticPanel. |
