# SemanticPanelAutomationPeer

Custom AutomationPeer for SemanticPanel. Implements IRangeValueProvider and IValueProvider so screen readers can read the composite component's semantic role, value, and range. Analogous to SwiftUI's .accessibilityRepresentation \{\} and Compose's Modifier.semantics \{ role = Role.Slider \}.

```csharp
public sealed class SemanticPanelAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider, IValueProvider
```

Namespace: `Microsoft.UI.Reactor.Accessibility`. Inherits from [`FrameworkElementAutomationPeer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.automation.peers.frameworkelementautomationpeer).

**Learn more:** [Accessibility](../../accessibility.md)

## Constructors

### `SemanticPanelAutomationPeer`

```csharp
public SemanticPanelAutomationPeer(SemanticPanel owner)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `owner` | [`SemanticPanel`](SemanticPanel.md) |  |

## Properties

### `IsReadOnly`

```csharp
public bool IsReadOnly  // get
```

### `LargeChange`

```csharp
public double LargeChange  // get
```

### `Maximum`

```csharp
public double Maximum  // get
```

### `Minimum`

```csharp
public double Minimum  // get
```

### `SmallChange`

```csharp
public double SmallChange  // get
```

### `Value`

```csharp
public double Value  // get
```

## Methods

### `SetValue`

```csharp
public void SetValue(double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |
