---
title: "Elements: Forms"
description: "Form fields, validation rules, validation visualizers and the property grid."
sidebar:
  label: "Forms"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## `FormFieldElement` [#formfieldelement]

A wrapper element that provides label, required indicator, description, and error display for a child form control. Acts as an inline visualizer: when the child has errors, the description text is replaced with error messages.

```csharp
public sealed record FormFieldElement(Element Content, string? Label = null, bool Required = false, string? Description = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`FormField`](/microsoft-ui-reactor/main/reference/factories/forms#formfield)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `Description` | `string?` | `null` |  |
| `FieldName` | `string?` |  | The field name to query from the ValidationContext. When null, auto-detected from any ValidationAttached on the Content element. |
| `Label` | `string?` | `null` |  |
| `Required` | `bool` | `false` |  |
| `ShowWhen` | [`ShowWhen`](/microsoft-ui-reactor/main/reference/validation/enums#showwhen) |  | Controls when error messages are shown (default: WhenTouched). |

## `PropertyGridElement` [#propertygridelement]

Element record for the PropertyGrid component. Serves as both the virtual DOM element and the typed props for PropertyGridComponent.

```csharp
public record PropertyGridElement : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Modifiers:** [`RootChanged`](/microsoft-ui-reactor/main/reference/modifiers/forms#rootchanged)

| Property | Type | Description |
| --- | --- | --- |
| `ArrayItemTemplate` | [`ArrayItemTemplate?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#arrayitemtemplate) |  |
| `ArrayToolbarTemplate` | [`ArrayToolbarTemplate?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#arraytoolbartemplate) |  |
| `CategoryTemplate` | [`CategoryTemplate?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#categorytemplate) |  |
| `Filter` | `Func<FieldDescriptor, bool>?` | Optional filter predicate — return false to hide a property. |
| `OnRootChanged` | `Action<object>?` | Callback when the root object is replaced (for immutable roots). |
| `PropertyLabelTemplate` | [`PropertyLabelTemplate?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#propertylabeltemplate) |  |
| `PropertyRowTemplate` | [`PropertyRowTemplate?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#propertyrowtemplate) |  |
| `Registry` | [`TypeRegistry`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#typeregistry) | The type registry for resolving editors. |
| `ShowSearch` | `bool` | Whether to show the search/filter box. |
| `Target` | `object` | The target object whose properties are edited. |

## `ValidationRuleElement` [#validationruleelement]

A virtual element that represents a cross-field validation rule. Place anywhere in the element tree; errors bubble to the nearest visualizer. Does not render any UI — it only produces validation messages.

```csharp
public sealed record ValidationRuleElement(string Field, Func<bool> Predicate, string Message, Severity Severity = Severity.Error) : Element
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ValidationRule`](/microsoft-ui-reactor/main/reference/factories/forms#validationrule), [`ValidationRuleAsync`](/microsoft-ui-reactor/main/reference/factories/forms#validationruleasync)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AsyncPredicate` | `Func<Task<bool>>?` |  | Optional async predicate for rules that require I/O. When set, takes precedence over the sync Predicate. |
| `Field` | `string` |  |  |
| `Message` | `string` |  |  |
| `Predicate` | `Func<bool>` |  |  |
| `Severity` | [`Severity`](/microsoft-ui-reactor/main/reference/validation/enums#severity) | `Severity.Error` |  |

## `ValidationVisualizerElement` [#validationvisualizerelement]

An element that catches validation errors from its content subtree and displays them. Errors caught by a visualizer are removed from further bubbling.

```csharp
public sealed record ValidationVisualizerElement(VisualizerStyle Style, Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ValidationVisualizer`](/microsoft-ui-reactor/main/reference/factories/forms#validationvisualizer)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `CustomRender` | `Func<IReadOnlyList<ValidationMessage>, Element>?` | Custom render function for Custom-style visualizers. Receives all caught messages and returns an Element to render. |
| `SeverityFilter` | `Severity?` | Optional severity filter. When set, only catches messages of this severity. Messages with other severities continue to bubble. |
| `ShowWhen` | [`ShowWhen`](/microsoft-ui-reactor/main/reference/validation/enums#showwhen) | Controls when the visualizer shows its content. |
| `Style` | [`VisualizerStyle`](/microsoft-ui-reactor/main/reference/validation/enums#visualizerstyle) |  |
| `Title` | `string?` | Optional heading text for the visualizer. |
