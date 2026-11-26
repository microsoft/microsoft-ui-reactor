# D3: color, format and interpolation

Colors, number formatting, easing and interpolators.

**Learn more:** [Charting](../../charting.md)

## `D3Color`

CSS color utilities. Parses hex, rgb(), hsl(), and named colors. Provides brighter/darker manipulation for chart theming.

```csharp
public readonly struct D3Color
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `D3Color`

```csharp
public D3Color(byte r, byte g, byte b, double opacity = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `r` | `byte` |  |  |
| `g` | `byte` |  |  |
| `b` | `byte` |  |  |
| `opacity` | `double` | `1` |  |

### Properties

#### `B`

```csharp
public byte B  // get
```

#### `G`

```csharp
public byte G  // get
```

#### `Opacity`

```csharp
public double Opacity  // get
```

#### `R`

```csharp
public byte R  // get
```

### Methods

#### `Brighter`

```csharp
public D3Color Brighter(double k = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `k` | `double` | `1` |  |

#### `Darker`

```csharp
public D3Color Darker(double k = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `k` | `double` | `1` |  |

#### `Parse`

```csharp
public static D3Color Parse(string format)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

#### `ToHex`

```csharp
public string ToHex()
```

#### `ToRgb`

```csharp
public string ToRgb()
```

#### `ToString`

Returns a string that represents this object.

```csharp
public override string ToString()
```

### Fields

#### `Category10`

D3's category10 color scheme — 10 distinct colors for categorical data.

```csharp
public static readonly IReadOnlyList<D3Color> Category10
```

#### `Tableau10`

Tableau10 color scheme — another popular 10-color categorical palette.

```csharp
public static readonly IReadOnlyList<D3Color> Tableau10
```

## `D3Ease`

Easing functions for smooth animation transitions. Each method takes t in \[0, 1\] and returns an eased value. Direct port of d3-ease.

```csharp
public static class D3Ease
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Back`

Creates back easing in-out. Port of d3.easeBack.

```csharp
public static Func<double, double> Back(double s = 1.70158)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `s` | `double` | `1.70158` |  |

#### `BackIn`

Creates back easing in (overshooting). Port of d3.easeBackIn.

```csharp
public static Func<double, double> BackIn(double s = 1.70158)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `s` | `double` | `1.70158` |  |

#### `BackOut`

Creates back easing out. Port of d3.easeBackOut.

```csharp
public static Func<double, double> BackOut(double s = 1.70158)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `s` | `double` | `1.70158` |  |

#### `Bounce`

Bounce easing in-out. Port of d3.easeBounce.

```csharp
public static double Bounce(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `BounceIn`

Bounce easing in. Port of d3.easeBounceIn.

```csharp
public static double BounceIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `BounceOut`

Bounce easing out. Port of d3.easeBounceOut.

```csharp
public static double BounceOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Circle`

Circular easing in-out. Port of d3.easeCircle.

```csharp
public static double Circle(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `CircleIn`

Circular easing in. Port of d3.easeCircleIn.

```csharp
public static double CircleIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `CircleOut`

Circular easing out. Port of d3.easeCircleOut.

```csharp
public static double CircleOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Cubic`

Cubic easing in-out. Port of d3.easeCubic.

```csharp
public static double Cubic(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `CubicIn`

Cubic easing in. Port of d3.easeCubicIn.

```csharp
public static double CubicIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `CubicOut`

Cubic easing out. Port of d3.easeCubicOut.

```csharp
public static double CubicOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Elastic`

Creates elastic easing in-out. Port of d3.easeElastic.

```csharp
public static Func<double, double> Elastic(double amplitude = 1, double period = 0.3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `amplitude` | `double` | `1` |  |
| `period` | `double` | `0.3` |  |

#### `ElasticIn`

Creates elastic easing in. Port of d3.easeElasticIn.

```csharp
public static Func<double, double> ElasticIn(double amplitude = 1, double period = 0.3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `amplitude` | `double` | `1` |  |
| `period` | `double` | `0.3` |  |

#### `ElasticOut`

Creates elastic easing out. Port of d3.easeElasticOut.

```csharp
public static Func<double, double> ElasticOut(double amplitude = 1, double period = 0.3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `amplitude` | `double` | `1` |  |
| `period` | `double` | `0.3` |  |

#### `Exp`

Exponential easing in-out. Port of d3.easeExp.

```csharp
public static double Exp(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `ExpIn`

Exponential easing in. Port of d3.easeExpIn.

```csharp
public static double ExpIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `ExpOut`

Exponential easing out. Port of d3.easeExpOut.

```csharp
public static double ExpOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Linear`

Linear easing (identity). Port of d3.easeLinear.

```csharp
public static double Linear(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Poly`

Creates polynomial easing in-out with the given exponent.

```csharp
public static Func<double, double> Poly(double e = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `e` | `double` | `3` |  |

#### `PolyIn`

Creates polynomial easing in with the given exponent.

```csharp
public static Func<double, double> PolyIn(double e = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `e` | `double` | `3` |  |

#### `PolyOut`

Creates polynomial easing out with the given exponent.

```csharp
public static Func<double, double> PolyOut(double e = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `e` | `double` | `3` |  |

#### `Quad`

Quadratic easing in-out. Port of d3.easeQuad.

```csharp
public static double Quad(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `QuadIn`

Quadratic easing in. Port of d3.easeQuadIn.

```csharp
public static double QuadIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `QuadOut`

Quadratic easing out. Port of d3.easeQuadOut.

```csharp
public static double QuadOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `Sin`

Sinusoidal easing in-out. Port of d3.easeSin.

```csharp
public static double Sin(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `SinIn`

Sinusoidal easing in. Port of d3.easeSinIn.

```csharp
public static double SinIn(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

#### `SinOut`

Sinusoidal easing out. Port of d3.easeSinOut.

```csharp
public static double SinOut(double t)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `t` | `double` |  |

## `D3Format`

Locale-independent number formatting matching D3's format specifier syntax. Direct port of d3.format() — supports specifiers like ".2f", ".0%", ",.0f", ".2s", etc.

```csharp
public static class D3Format
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Format`

Returns a formatting function for the given specifier string. Supported types: f (fixed), e (exponent), g (general), r (rounded), s (SI-prefix), % (percent), d (integer), b (binary), o (octal), x/X (hex), c (character).

```csharp
public static Func<double, string> Format(string specifier)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `specifier` | `string` |  |

#### `FormatPrefix`

Returns a SI-prefix format function that auto-selects the prefix.

```csharp
public static Func<double, string> FormatPrefix(string specifier, double reference)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `specifier` | `string` |  |
| `reference` | `double` |  |

#### `FormatValue`

Formats a value using the given specifier string.

```csharp
public static string FormatValue(double value, string specifier)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |
| `specifier` | `string` |  |

#### `PrecisionFixed`

Returns the decimal precision needed for fixed-point notation of the given step.

```csharp
public static int PrecisionFixed(double step)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `step` | `double` |  |

#### `PrecisionPrefix`

Returns the decimal precision needed for the given step and value.

```csharp
public static int PrecisionPrefix(double step, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `step` | `double` |  |
| `value` | `double` |  |

#### `PrecisionRound`

Returns the decimal precision needed for significant-digit notation of the given step relative to the given max value.

```csharp
public static int PrecisionRound(double step, double max)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `step` | `double` |  |
| `max` | `double` |  |

## `D3Interpolate`

Interpolation functions for smooth transitions between values.

```csharp
public static class D3Interpolate
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Number`

Returns a function that interpolates between `a` and `b` using a parameter t in \[0, 1\].

```csharp
public static Func<double, double> Number(double a, double b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `double` |  |
| `b` | `double` |  |

#### `Round`

Like [`D3Interpolate.Number`](#number), but rounds the result to the nearest integer.

```csharp
public static Func<double, double> Round(double a, double b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `double` |  |
| `b` | `double` |  |

## `D3InterpolateColor`

Interpolation functions for colors, dates, and strings. Extends the base D3Interpolate with additional types.

```csharp
public static class D3InterpolateColor
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Array`

Returns a function that interpolates between two arrays of numbers. Port of d3.interpolateArray().

```csharp
public static Func<double, double[]> Array(double[] a, double[] b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `double[]` |  |
| `b` | `double[]` |  |

#### `Date`

Returns a function that interpolates between two Date values. Port of d3.interpolateDate().

```csharp
public static Func<double, DateTime> Date(DateTime a, DateTime b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `DateTime` |  |
| `b` | `DateTime` |  |

#### `Hsl`

Returns a function that interpolates between two colors in HSL space. Port of d3.interpolateHsl().

```csharp
public static Func<double, D3Color> Hsl(D3Color a, D3Color b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`D3Color`](#d3color) |  |
| `b` | [`D3Color`](#d3color) |  |

#### `Rgb`

##### `Rgb(D3Color, D3Color)`

Returns a function that interpolates between two colors in RGB space. Port of d3.interpolateRgb().

```csharp
public static Func<double, D3Color> Rgb(D3Color a, D3Color b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`D3Color`](#d3color) |  |
| `b` | [`D3Color`](#d3color) |  |

##### `Rgb(string, string)`

Returns a function that interpolates between two colors in RGB space, specified as CSS color strings.

```csharp
public static Func<double, D3Color> Rgb(string a, string b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `string` |  |
| `b` | `string` |  |
