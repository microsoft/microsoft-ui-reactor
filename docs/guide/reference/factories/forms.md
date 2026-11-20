# Factories: Forms

Form fields, validation rules, validation visualizers and the property grid.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Forms and Input](../../forms.md)

## `FormField`

Creates an auto-wired FormField from a FieldDescriptor. Resolves the editor from TypeRegistry, sets label/description from the descriptor, detects required from validators, and wires validation.

```csharp
FormFieldElement FormField(FieldDescriptor field, object value, Action<object> onChange, TypeRegistry? registry = null)
FormFieldElement FormField(Element content, string? label = null, bool required = false, string? description = null, string? fieldName = null, ShowWhen showWhen = ShowWhen.WhenTouched)
```

Declared in `FormFieldDsl`.

| Overload | Description |
| --- | --- |
| `FormField(FieldDescriptor, object, Action<object>, TypeRegistry?)` | Creates an auto-wired FormField from a FieldDescriptor. Resolves the editor from TypeRegistry, sets label/description from the descriptor, detects required from validators, and wires validation. |
| `FormField(Element, string?, bool, string?, string?, ShowWhen)` | Wraps a form control with a label, required indicator, and description/error area. |

**Returns:** [`FormFieldElement`](../elements/forms.md#formfieldelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `field` | [`FieldDescriptor`](../data/descriptors.md#fielddescriptor) |  |  |
| `value` | `object` |  |  |
| `onChange` | `Action<object>` |  |  |
| `registry` | [`TypeRegistry?`](../controls/property-grid-templates.md#typeregistry) | `null` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `label` | `string?` | `null` |  |
| `required` | `bool` | `false` |  |
| `description` | `string?` | `null` |  |
| `fieldName` | `string?` | `null` |  |
| `showWhen` | [`ShowWhen`](../validation/enums.md#showwhen) | `ShowWhen.WhenTouched` |  |

## `ValidationRule`

Creates a cross-field validation rule. When the predicate returns false, the message is added to the ValidationContext under the given field name. When the predicate returns true, any previous message for this rule is cleared.

```csharp
ValidationRuleElement ValidationRule(Func<bool> predicate, string message, string field, Severity severity = Severity.Error)
```

Declared in `ValidationRuleDsl`.

**Returns:** [`ValidationRuleElement`](../elements/forms.md#validationruleelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `predicate` | `Func<bool>` |  | Returns true when the rule passes (valid). |
| `message` | `string` |  | Error message when the rule fails. |
| `field` | `string` |  | Field name to associate the error with. |
| `severity` | [`Severity`](../validation/enums.md#severity) | `Severity.Error` | Severity level (default: Error). |

## `ValidationRuleAsync`

Creates an async cross-field validation rule.

```csharp
ValidationRuleElement ValidationRuleAsync(Func<Task<bool>> asyncPredicate, string message, string field, Severity severity = Severity.Error)
```

Declared in `ValidationRuleDsl`.

**Returns:** [`ValidationRuleElement`](../elements/forms.md#validationruleelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `asyncPredicate` | `Func<Task<bool>>` |  |  |
| `message` | `string` |  |  |
| `field` | `string` |  |  |
| `severity` | [`Severity`](../validation/enums.md#severity) | `Severity.Error` |  |

## `ValidationVisualizer`

Creates a custom validation visualizer with a render function.

```csharp
ValidationVisualizerElement ValidationVisualizer(Func<IReadOnlyList<ValidationMessage>, Element> render, Element content, Severity? severityFilter = null, ShowWhen showWhen = ShowWhen.Always)
ValidationVisualizerElement ValidationVisualizer(VisualizerStyle style, Element content, string? title = null, Severity? severityFilter = null, ShowWhen showWhen = ShowWhen.Always)
```

Declared in `ValidationVisualizerDsl`.

| Overload | Description |
| --- | --- |
| `ValidationVisualizer(Func<IReadOnlyList<ValidationMessage>, Element>, Element, Severity?, ShowWhen)` | Creates a custom validation visualizer with a render function. |
| `ValidationVisualizer(VisualizerStyle, Element, string?, Severity?, ShowWhen)` | Creates a validation visualizer that catches errors from its content subtree. |

**Returns:** [`ValidationVisualizerElement`](../elements/forms.md#validationvisualizerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<IReadOnlyList<ValidationMessage>, Element>` |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `severityFilter` | `Severity?` | `null` |  |
| `showWhen` | [`ShowWhen`](../validation/enums.md#showwhen) | `ShowWhen.Always` |  |
| `style` | [`VisualizerStyle`](../validation/enums.md#visualizerstyle) |  |  |
| `title` | `string?` | `null` |  |
