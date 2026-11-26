---
title: "GridSize"
description: "Strongly-typed grid track size, used by the typed Microsoft.UI.Reactor.Factories.Grid(GridSize[], GridSize[], Element?[]) factory in place of the legacy string-form (\"Auto\", \"*\", \"200\", \"1.5*\") tracks. Optional Min and Max properties are available to constrain the track size."
---

```csharp
public readonly record struct GridSize
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Layout](/microsoft-ui-reactor/main/guides/layout)

## Remarks [#remarks]

Spec 033 §1. The smart constructors [`GridSize.Auto`](#auto), [`GridSize.Star`](#star), and [`GridSize.Px`](#px) validate their inputs at the boundary; the implicit conversion to [`GridLength`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.gridlength) lets the typed form compose with anything that takes a WinUI [`GridLength`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.gridlength).

The string parser ([`GridSize.Parse`](#parse)) is invariant-culture only — track strings are never localized. Whitespace is trimmed; `"Auto"` matches case-insensitively; numeric forms use [`CultureInfo.InvariantCulture`](https://learn.microsoft.com/dotnet/api/system.globalization.cultureinfo.invariantculture).

When Min or Max are specified, the track size is clamped to that range. For example, `GridSize.Px(100) with { Min=50 and Max=200 }` will be set to 100 initially and depending on changes clamped to 50 and 200. Min can be set higher as Max in which case the track size will be clamped to the Max value.

## Constructors [#constructors]

### `GridSize` [#gridsize_1]

```csharp
public GridSize(double value, GridUnitType type, double? min = null, double? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `double` |  |  |
| `type` | `GridUnitType` |  |  |
| `min` | `double?` | `null` |  |
| `max` | `double?` | `null` |  |

## Properties [#properties]

### `Auto` [#auto]

The auto-sized track. Equivalent to the WinUI `Auto` length.

```csharp
public static GridSize Auto  // get
```

### `Max` [#max]

The maximum size of the track. If specified, the track size will not be greater than this value.

```csharp
public double? Max  // get
```

### `Min` [#min]

The minimum size of the track. If specified, the track size will not be less than this value.

```csharp
public double? Min  // get
```

### `Type` [#type]

The type of the track size: `Auto`, `Star`, or `Pixel`.

```csharp
public GridUnitType Type  // get
```

### `Value` [#value]

The value of the track size. For `Pixel` types, this is the pixel size; for `Star` types, this is the star weight; for `Auto`, this is always 1.

```csharp
public double Value  // get
```

## Methods [#methods]

### `Parse` [#parse]

Parses a track string into a [`GridSize`](/microsoft-ui-reactor/main/reference/layout/GridSize). Accepts: `"Auto"` / `"auto"`, `"*"`, `"<n>*"`, `"<n>.<m>*"`, `"<n>"`, `"<n>.<m>"`. Whitespace is trimmed.

```csharp
public static GridSize Parse(string s)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `s` | `string` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`FormatException`](https://learn.microsoft.com/dotnet/api/system.formatexception) | Thrown for any other input. |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | Thrown when `s` is null. |

### `Px` [#px]

A pixel-sized track. `pixels` must be \>= 0.

```csharp
public static GridSize Px(double pixels)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `pixels` | `double` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) | Thrown when `pixels` is \< 0. |

### `Star` [#star]

A star-weighted track. `weight` defaults to 1 (i.e. `"*"`); 1.5 produces `"1.5*"`, 0.33 produces `"0.33*"`.

```csharp
public static GridSize Star(double weight = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `weight` | `double` | `1` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) | Thrown when `weight` is \<= 0. |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

Implicit conversion to [`GridLength`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.gridlength) so the typed form composes with any WinUI surface that takes a [`GridLength`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.gridlength).

```csharp
public static implicit operator GridLength(GridSize s)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `s` | [`GridSize`](/microsoft-ui-reactor/main/reference/layout/GridSize) |  |
