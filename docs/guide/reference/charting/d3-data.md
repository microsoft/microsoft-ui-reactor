# D3: arrays and statistics

Binning, bisection, extents, grouping, ranges, ticks, statistics and random numbers.

**Learn more:** [Charting](../../charting.md)

## `Bin<T>`

A single bin containing items within the \[x0, x1) interval.

```csharp
public sealed class Bin<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `Bin`

```csharp
public Bin()
```

### Properties

#### `Count`

```csharp
public int Count  // get
```

#### `Items`

```csharp
public required List<T> Items  // get, init
```

#### `X0`

```csharp
public required double X0  // get, init
```

#### `X1`

```csharp
public required double X1  // get, init
```

## `BinGenerator`

```csharp
public static class BinGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create()`

Creates a bin generator for numeric data.

```csharp
public static BinGenerator<double> Create()
```

##### `Create<T>(Func<T, double>)`

Creates a bin generator with a custom value accessor.

```csharp
public static BinGenerator<T> Create<T>(Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, double>` |  |

## `BinGenerator<T>`

Bins continuous data into discrete intervals (histogram). Direct port of d3.bin().

```csharp
public sealed class BinGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `BinGenerator`

```csharp
public BinGenerator(Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, double>` |  |

### Methods

#### `Generate`

Generates bins for the given data.

```csharp
public Bin<T>[] Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetThresholdCount`

```csharp
public BinGenerator<T> SetThresholdCount(int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `count` | `int` |  |

#### `SetThresholds`

```csharp
public BinGenerator<T> SetThresholds(Func<double[], int, double[]> thresholds)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thresholds` | `Func<double[], int, double[]>` |  |

#### `SetValue`

```csharp
public BinGenerator<T> SetValue(Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, double>` |  |

## `D3Bisect`

Binary search utilities for sorted arrays. Direct port of d3-array's bisector.

```csharp
public static class D3Bisect
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `BisectCenter`

Returns the index of the value closest to `x` in a sorted `array`.

```csharp
public static int BisectCenter(double[] array, double x, int lo = 0, int hi = -1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `array` | `double[]` |  |  |
| `x` | `double` |  |  |
| `lo` | `int` | `0` |  |
| `hi` | `int` | `-1` |  |

#### `BisectLeft`

Returns the insertion point for `x` in `array` to maintain sorted order. If `x` is already present, inserts before (to the left of) any existing entries.

```csharp
public static int BisectLeft(double[] array, double x, int lo = 0, int hi = -1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `array` | `double[]` |  |  |
| `x` | `double` |  |  |
| `lo` | `int` | `0` |  |
| `hi` | `int` | `-1` |  |

#### `BisectRight`

Returns the insertion point for `x` in `array` to maintain sorted order. If `x` is already present, inserts after (to the right of) any existing entries.

```csharp
public static int BisectRight(double[] array, double x, int lo = 0, int hi = -1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `array` | `double[]` |  |  |
| `x` | `double` |  |  |
| `lo` | `int` | `0` |  |
| `hi` | `int` | `-1` |  |

## `D3Extent`

Computes the minimum and maximum values of an array.

```csharp
public static class D3Extent
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Extent`

##### `Extent(IEnumerable<double>)`

Returns the \[min, max\] of the given values.

```csharp
public static (double min, double max) Extent(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Extent<T>(IEnumerable<T>, Func<T, double>)`

Returns the \[min, max\] of the given values, applying a value accessor.

```csharp
public static (double min, double max) Extent<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

## `D3Group`

Groups and rolls up data by key. Direct port of d3.group() and d3.rollup().

```csharp
public static class D3Group
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Group`

Groups the given values by a key accessor, returning a dictionary of key → list.

```csharp
public static Dictionary<TKey, List<T>> Group<T, TKey>(IEnumerable<T> values, Func<T, TKey> key) where TKey : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `key` | `Func<T, TKey>` |  |

#### `Index`

Returns an index mapping each key to the first matching value.

```csharp
public static Dictionary<TKey, T> Index<T, TKey>(IEnumerable<T> values, Func<T, TKey> key) where TKey : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `key` | `Func<T, TKey>` |  |

#### `Rollup`

Groups the given values by a key accessor, then reduces each group.

```csharp
public static Dictionary<TKey, TResult> Rollup<T, TKey, TResult>(IEnumerable<T> values, Func<T, TKey> key, Func<IEnumerable<T>, TResult> reduce) where TKey : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `key` | `Func<T, TKey>` |  |
| `reduce` | `Func<IEnumerable<T>, TResult>` |  |

## `D3Random`

Random number generators for various probability distributions. Direct port of d3-random.

```csharp
public static class D3Random
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Bates`

Returns a function that generates Bates random numbers (mean of n uniform randoms). Port of d3.randomBates().

```csharp
public static Func<double> Bates(int n)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `n` | `int` |  |

#### `Bernoulli`

Returns a function that generates Bernoulli random numbers (0 or 1). Port of d3.randomBernoulli().

```csharp
public static Func<int> Bernoulli(double p = 0.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `p` | `double` | `0.5` |  |

#### `Binomial`

Returns a function that generates binomial random numbers. Port of d3.randomBinomial().

```csharp
public static Func<int> Binomial(int n, double p = 0.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `n` | `int` |  |  |
| `p` | `double` | `0.5` |  |

#### `Cauchy`

Returns a function that generates Cauchy random numbers. Port of d3.randomCauchy().

```csharp
public static Func<double> Cauchy(double location = 0, double scale = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `location` | `double` | `0` |  |
| `scale` | `double` | `1` |  |

#### `Exponential`

Returns a function that generates exponential random numbers. Port of d3.randomExponential().

```csharp
public static Func<double> Exponential(double lambda = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `lambda` | `double` | `1` |  |

#### `Geometric`

Returns a function that generates geometric random numbers. Port of d3.randomGeometric().

```csharp
public static Func<int> Geometric(double p = 0.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `p` | `double` | `0.5` |  |

#### `Int`

Returns a function that generates random integers in \[min, max). Port of d3.randomInt().

```csharp
public static Func<int> Int(int min, int max)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `min` | `int` |  |
| `max` | `int` |  |

#### `IrwinHall`

Returns a function that generates Irwin-Hall random numbers (sum of n uniform randoms). Port of d3.randomIrwinHall().

```csharp
public static Func<double> IrwinHall(int n)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `n` | `int` |  |

#### `LogNormal`

Returns a function that generates log-normal random numbers. Port of d3.randomLogNormal().

```csharp
public static Func<double> LogNormal(double mu = 0, double sigma = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mu` | `double` | `0` |  |
| `sigma` | `double` | `1` |  |

#### `Normal`

Returns a function that generates normal (Gaussian) random numbers. Port of d3.randomNormal().

```csharp
public static Func<double> Normal(double mu = 0, double sigma = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mu` | `double` | `0` |  |
| `sigma` | `double` | `1` |  |

#### `Pareto`

Returns a function that generates Pareto random numbers. Port of d3.randomPareto().

```csharp
public static Func<double> Pareto(double alpha = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `alpha` | `double` | `1` |  |

#### `Poisson`

Returns a function that generates Poisson random numbers. Port of d3.randomPoisson().

```csharp
public static Func<int> Poisson(double lambda = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `lambda` | `double` | `1` |  |

#### `Uniform`

Returns a function that generates uniform random numbers in \[min, max). Port of d3.randomUniform().

```csharp
public static Func<double> Uniform(double min = 0, double max = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `double` | `0` |  |
| `max` | `double` | `1` |  |

#### `Weibull`

Returns a function that generates Weibull random numbers. Port of d3.randomWeibull().

```csharp
public static Func<double> Weibull(double k = 1, double lambda = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `k` | `double` | `1` |  |
| `lambda` | `double` | `1` |  |

## `D3Range`

Generates a range of evenly-spaced numeric values. Direct port of d3.range().

```csharp
public static class D3Range
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Range`

##### `Range(double)`

Returns an array of integers from 0 (inclusive) to `stop` (exclusive).

```csharp
public static double[] Range(double stop)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `stop` | `double` |  |

##### `Range(double, double, double)`

Returns an array of evenly-spaced values from `start` (inclusive) to `stop` (exclusive) with the given `step`.

```csharp
public static double[] Range(double start, double stop, double step = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `start` | `double` |  |  |
| `stop` | `double` |  |  |
| `step` | `double` | `1` |  |

## `D3Statistics`

Statistical functions for numeric data arrays. Direct port of d3-array's min, max, sum, mean, median, quantile, variance, deviation.

```csharp
public static class D3Statistics
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `CumSum`

Returns the cumulative sum of the given values.

```csharp
public static double[] CumSum(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

#### `Deviation`

Returns the standard deviation (square root of variance).

```csharp
public static double Deviation(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

#### `Max`

##### `Max(IEnumerable<double>)`

Returns the maximum value in the given values, ignoring NaN.

```csharp
public static double Max(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Max<T>(IEnumerable<T>, Func<T, double>)`

Returns the maximum value using an accessor, ignoring NaN.

```csharp
public static double Max<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

#### `Mean`

##### `Mean(IEnumerable<double>)`

Returns the arithmetic mean of the given values, ignoring NaN.

```csharp
public static double Mean(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Mean<T>(IEnumerable<T>, Func<T, double>)`

Returns the arithmetic mean using an accessor, ignoring NaN.

```csharp
public static double Mean<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

#### `Median`

##### `Median(IEnumerable<double>)`

Returns the median of the given values (the 0.5-quantile).

```csharp
public static double Median(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Median<T>(IEnumerable<T>, Func<T, double>)`

Returns the median using an accessor.

```csharp
public static double Median<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

#### `Min`

##### `Min(IEnumerable<double>)`

Returns the minimum value in the given values, ignoring NaN.

```csharp
public static double Min(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Min<T>(IEnumerable<T>, Func<T, double>)`

Returns the minimum value using an accessor, ignoring NaN.

```csharp
public static double Min<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

#### `Quantile`

Returns the p-quantile of the given values using the R-7 method. The values are sorted internally. p must be in \[0, 1\].

```csharp
public static double Quantile(IEnumerable<double> values, double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |
| `p` | `double` |  |

#### `QuantileSorted`

Returns the p-quantile of pre-sorted values using the R-7 method. Port of d3-array's quantileSorted.

```csharp
public static double QuantileSorted(double[] sorted, double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sorted` | `double[]` |  |
| `p` | `double` |  |

#### `Sum`

##### `Sum(IEnumerable<double>)`

Returns the sum of the given values, ignoring NaN.

```csharp
public static double Sum(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

##### `Sum<T>(IEnumerable<T>, Func<T, double>)`

Returns the sum using an accessor, ignoring NaN.

```csharp
public static double Sum<T>(IEnumerable<T> values, Func<T, double> valueOf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<T>` |  |
| `valueOf` | `Func<T, double>` |  |

#### `Variance`

Returns the variance of the given values using Welford's algorithm.

```csharp
public static double Variance(IEnumerable<double> values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `IEnumerable<double>` |  |

## `D3Ticks`

Generates human-readable tick values for numeric scales. Direct port of d3-array's tick generation algorithm.

```csharp
public static class D3Ticks
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `TickIncrement`

Returns the tick increment for the given interval and count. A negative value indicates that ticks should be computed as division rather than multiplication.

```csharp
public static long TickIncrement(double start, double stop, int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `start` | `double` |  |
| `stop` | `double` |  |
| `count` | `int` |  |

#### `TickStep`

Returns the tick step size for the given interval and count.

```csharp
public static double TickStep(double start, double stop, int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `start` | `double` |  |
| `stop` | `double` |  |
| `count` | `int` |  |

#### `Ticks`

Returns an array of approximately `count` representative values from the given numeric interval \[`start`, `stop`\]. The returned tick values are uniformly-spaced, have human-readable values (such as multiples of powers of 10), and are guaranteed to be within the extent of the given interval.

```csharp
public static double[] Ticks(double start, double stop, int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `start` | `double` |  |
| `stop` | `double` |  |
| `count` | `int` |  |

### Fields

#### `MaxTicks`

Hard cap on emitted tick count. TASK-086.

```csharp
public const int MaxTicks = 10000
```
