# Modifiers: Accessibility

Automation names, landmarks, live regions and other accessibility properties.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Accessibility](../../accessibility.md)

## `AccessibilityHidden`

Hides element from screen readers entirely. Shorthand for .AccessibilityView(AccessibilityView.Raw).

```csharp
T AccessibilityHidden<T>(this T el) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Example**

```csharp
Icon(decorativeGlyph).AccessibilityHidden()
```

## `AccessibilityView`

Sets AutomationProperties.AccessibilityView (Content, Control, Raw). Use Raw to hide decorative elements from screen readers.

```csharp
T AccessibilityView<T>(this T el, AccessibilityView view) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | `AccessibilityView` |  |

**Example**

```csharp
Image(decorativeUri).AccessibilityView(AccessibilityView.Raw)
```

## `AutomationId`

Sets AutomationProperties.AutomationId on the element's control. Provides a stable identifier for UI Automation / test tools (FlaUI, WinAppDriver). Usage: Button("Go", onClick).AutomationId("GoButton")

```csharp
T AutomationId<T>(this T el, string id) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |

## `AutomationName`

Sets AutomationProperties.Name on the element's control. Usage: Button("Go", onClick).AutomationName("Navigate forward")

```csharp
T AutomationName<T>(this T el, string name) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

## `DescribedBy`

```csharp
T DescribedBy<T>(this T el, params ElementRef[] targets) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `targets` | [`ElementRef[]`](../input/element-refs.md#elementref) |  |

## `FlowsFrom`

```csharp
T FlowsFrom<T>(this T el, params ElementRef[] targets) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `targets` | [`ElementRef[]`](../input/element-refs.md#elementref) |  |

## `FlowsTo`

```csharp
T FlowsTo<T>(this T el, params ElementRef[] targets) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `targets` | [`ElementRef[]`](../input/element-refs.md#elementref) |  |

## `FullDescription`

Sets AutomationProperties.FullDescription — extended description for complex elements.

```csharp
T FullDescription<T>(this T el, string desc) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `desc` | `string` |  |

**Example**

```csharp
Chart(...).FullDescription("Bar chart showing Q1 revenue by region")
```

## `HeadingLevel`

Sets AutomationProperties.HeadingLevel (Level1–Level9). Screen reader users navigate by headings, like HTML h1–h6.

```csharp
T HeadingLevel<T>(this T el, AutomationHeadingLevel level) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `level` | `AutomationHeadingLevel` |  |

**Example**

```csharp
TextBlock("Settings").HeadingLevel(AutomationHeadingLevel.Level1)
```

## `HelpText`

Sets AutomationProperties.HelpText — supplemental text read by screen readers after the Name. Analogous to SwiftUI's .accessibilityHint().

```csharp
T HelpText<T>(this T el, string text) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

**Example**

```csharp
TextBox(email, setEmail).HelpText("Enter your work email address")
```

## `HierarchyLevel`

Sets AutomationProperties.Level — hierarchical depth (e.g., tree node depth).

```csharp
T HierarchyLevel<T>(this T el, int level) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `level` | `int` |  |

**Example**

```csharp
TreeItem(text).HierarchyLevel(2)
```

## `ItemStatus`

Sets AutomationProperties.ItemStatus — status string announced by screen readers.

```csharp
T ItemStatus<T>(this T el, string status) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `status` | `string` |  |

**Example**

```csharp
MailFolder("Inbox").ItemStatus("3 unread")
```

## `LabeledBy`

Associates this element with a labelling element through a reactive ElementRef.

```csharp
T LabeledBy<T>(this T el, ElementRef target) where T : Element
T LabeledBy<T>(this T el, string labelAutomationId) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `LabeledBy<T>(T, ElementRef)` | Associates this element with a labelling element through a reactive ElementRef. |
| `LabeledBy<T>(T, string)` | Associates this element with a labelling element via its AutomationId. The reconciler resolves the reference at mount time. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |
| `labelAutomationId` | `string` |  |

**Example**

```csharp
TextBox(email, setEmail).LabeledBy("EmailLabel")
```

## `Landmark`

Sets AutomationProperties.LandmarkType (Main, Navigation, Search, Form, Custom). Screen readers announce landmarks and let users jump between them.

```csharp
T Landmark<T>(this T el, AutomationLandmarkType type) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `AutomationLandmarkType` |  |

**Example**

```csharp
VStack(children).Landmark(AutomationLandmarkType.Main)
```

## `LiveRegion`

Sets AutomationProperties.LiveSetting. Screen readers announce content changes. Polite = queued after current speech. Assertive = interrupts immediately.

```csharp
T LiveRegion<T>(this T el, AutomationLiveSetting mode = (AutomationLiveSetting)1) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mode` | `AutomationLiveSetting` | `(AutomationLiveSetting)1` |  |

**Example**

```csharp
TextBlock(statusMessage).LiveRegion(AutomationLiveSetting.Polite)
```

## `PositionInSet`

Sets AutomationProperties.PositionInSet and SizeOfSet (e.g., "item 3 of 10").

```csharp
T PositionInSet<T>(this T el, int position, int size) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |
| `size` | `int` |  |

**Example**

```csharp
ListItem(text).PositionInSet(3, 10)
```

## `Semantics`

Wraps this element in a SemanticPanel that provides custom automation semantics to screen readers. Use this for composite components that need to describe their role and value (e.g., a star rating widget built from Image elements that should announce as "slider, 3 of 5 stars").

Analogous to SwiftUI's .accessibilityRepresentation \{\} and Compose's Modifier.semantics \{ role = Role.Slider \}.

```csharp
SemanticElement Semantics<T>(this T el, string? role = null, string? value = null, double? rangeMin = null, double? rangeMax = null, double? rangeValue = null, bool isReadOnly = true) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `role` | `string?` | `null` |  |
| `value` | `string?` | `null` |  |
| `rangeMin` | `double?` | `null` |  |
| `rangeMax` | `double?` | `null` |  |
| `rangeValue` | `double?` | `null` |  |
| `isReadOnly` | `bool` | `true` |  |

**Example**

```csharp
StarRating(value: 3, max: 5)
    .Semantics(role: "slider", value: "3 of 5 stars",
               rangeValue: 3, rangeMin: 0, rangeMax: 5)
```
