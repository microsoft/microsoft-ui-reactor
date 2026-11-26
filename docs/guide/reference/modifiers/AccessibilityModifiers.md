# AccessibilityModifiers

Advanced accessibility properties (WCAG Tier 2/3). Stored as a lazy sub-record on ElementModifiers to avoid allocating storage for elements that don't need advanced accessibility annotations. All fluent extension methods create/merge this record automatically — developers never need to construct it directly.

```csharp
public record AccessibilityModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](../../modifier-system.md), [Accessibility](../../accessibility.md)

## Constructors

### `AccessibilityModifiers`

```csharp
public AccessibilityModifiers()
```

## Properties

### `AccessibilityView`

AutomationProperties.AccessibilityView — UIA tree visibility (Content, Control, Raw).

```csharp
public AccessibilityView? AccessibilityView  // get, init
```

### `DescribedByRefs`

AutomationProperties.DescribedBy targets resolved from ElementRefs.

```csharp
public IReadOnlyList<ElementRef>? DescribedByRefs  // get, init
```

### `FlowsFromRefs`

AutomationProperties.FlowsFrom targets resolved from ElementRefs.

```csharp
public IReadOnlyList<ElementRef>? FlowsFromRefs  // get, init
```

### `FlowsToRefs`

AutomationProperties.FlowsTo targets resolved from ElementRefs.

```csharp
public IReadOnlyList<ElementRef>? FlowsToRefs  // get, init
```

### `FullDescription`

AutomationProperties.FullDescription — extended description for complex elements.

```csharp
public string? FullDescription  // get, init
```

### `HelpText`

AutomationProperties.HelpText — supplemental description read after the Name.

```csharp
public string? HelpText  // get, init
```

### `IsRequiredForForm`

AutomationProperties.IsRequiredForForm — screen readers announce "required".

```csharp
public bool? IsRequiredForForm  // get, init
```

### `ItemStatus`

AutomationProperties.ItemStatus — status string (e.g., "3 unread").

```csharp
public string? ItemStatus  // get, init
```

### `LabeledBy`

AutomationProperties.LabeledBy target AutomationId — resolved by the reconciler.

```csharp
public string? LabeledBy  // get, init
```

### `LabeledByRef`

AutomationProperties.LabeledBy target resolved from an ElementRef.

```csharp
public ElementRef? LabeledByRef  // get, init
```

### `LandmarkType`

AutomationProperties.LandmarkType — landmark region (Main, Navigation, Search, Form).

```csharp
public AutomationLandmarkType? LandmarkType  // get, init
```

### `Level`

AutomationProperties.Level — hierarchical depth (e.g., tree node level).

```csharp
public int? Level  // get, init
```

### `LiveSetting`

AutomationProperties.LiveSetting — live region announcement mode (Polite, Assertive).

```csharp
public AutomationLiveSetting? LiveSetting  // get, init
```

### `PositionInSet`

AutomationProperties.PositionInSet — ordinal position (1-based) in a group.

```csharp
public int? PositionInSet  // get, init
```

### `SizeOfSet`

AutomationProperties.SizeOfSet — total count in the group.

```csharp
public int? SizeOfSet  // get, init
```

### `TabFocusNavigation`

UIElement.TabFocusNavigation — Tab behavior within a container (Local, Once, Cycle).

```csharp
public KeyboardNavigationMode? TabFocusNavigation  // get, init
```

## Methods

### `Merge`

```csharp
public AccessibilityModifiers Merge(AccessibilityModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`AccessibilityModifiers`](AccessibilityModifiers.md) |  |
