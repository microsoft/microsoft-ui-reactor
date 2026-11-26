# Observable\<T\>

A single-value cell that raises [`INotifyPropertyChanged.PropertyChanged`](https://learn.microsoft.com/dotnet/api/system.componentmodel.inotifypropertychanged.propertychanged) when [`Observable.Value`](#value) is assigned a different value. Exists to let developers declare small pieces of global app state (dev flags, light/dark mode, currently-signed-in user, etc.) in one line without hand-writing an INotifyPropertyChanged class.

Pairs with `RenderContext.UseObservable`, which subscribes the current component and re-renders on change:

```csharp
public static readonly Observable<bool> DebugUI = new(false);
// ...
var on = ctx.UseObservable(DebugUI).Value;
```

For richer state (multiple related properties, computed properties, XAML binding interop), authoring a plain INPC class is still the recommended path — [`Observable<T>`](Observable.md) is deliberately minimal and is not a replacement for that.

```csharp
public sealed class Observable<T> : INotifyPropertyChanged
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Reactivity Model](../../reactivity-model.md)

## Constructors

### `Observable`

```csharp
public Observable()
public Observable(T initial)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `initial` | `T` |  |

## Properties

### `Value`

```csharp
public T Value  // get, set
```

## Methods

### `ToString`

Returns a string that represents this object.

```csharp
public override string ToString()
```

## Events

### `PropertyChanged`

```csharp
public event PropertyChangedEventHandler? PropertyChanged
```

## Operators

### `op_Implicit`

```csharp
public static implicit operator T(Observable<T> cell)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cell` | [`Observable<T>`](Observable.md) |  |
