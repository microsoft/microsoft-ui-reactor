---
title: "SemanticPanelAutomationPeer"
description: "Custom AutomationPeer for SemanticPanel. Implements IRangeValueProvider and IValueProvider so screen readers can read the composite component's semantic role, value, and range. Analogous to SwiftUI's .accessibilityRepresentation {} and Compose's Modifier.semantics { role = Role.Slider }."
---

```csharp
public sealed class SemanticPanelAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider, IValueProvider
```

Namespace: `Microsoft.UI.Reactor.Accessibility`. Inherits from [`FrameworkElementAutomationPeer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.automation.peers.frameworkelementautomationpeer).

**Learn more:** [Accessibility](/microsoft-ui-reactor/main/guides/accessibility)

## Constructors [#constructors]

### `SemanticPanelAutomationPeer` [#semanticpanelautomationpeer_1]

```csharp
public SemanticPanelAutomationPeer(SemanticPanel owner)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `owner` | [`SemanticPanel`](/microsoft-ui-reactor/main/reference/accessibility/SemanticPanel) |  |

## Properties [#properties]

### `IsReadOnly` [#isreadonly]

```csharp
public bool IsReadOnly  // get
```

### `LargeChange` [#largechange]

```csharp
public double LargeChange  // get
```

### `Maximum` [#maximum]

```csharp
public double Maximum  // get
```

### `Minimum` [#minimum]

```csharp
public double Minimum  // get
```

### `SmallChange` [#smallchange]

```csharp
public double SmallChange  // get
```

### `Value` [#value]

```csharp
public double Value  // get
```

## Methods [#methods]

### `SetValue` [#setvalue]

```csharp
public void SetValue(double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |
